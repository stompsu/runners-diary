using System.Net;
using System.Net.Http.Json;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Races;

namespace RunnersDiary.Api.Tests.Races;

/// <summary>
/// AC4: for every ordered pair of callers (two guests, two Entra users, guest vs user in both directions) the interests, marks and
/// signups of one never change what the other sees, and a signup creates the run under the caller only.
/// </summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class RaceIsolationTests(ContainersApiFactory factory)
{
    private static readonly string[] Kinds = ["guestA", "guestB", "userA", "userB"];

    public static TheoryData<string, string> Pairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var owner in Kinds)
        {
            foreach (var intruder in Kinds.Where(k => k != owner))
            {
                data.Add(owner, intruder);
            }
        }

        return data;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    private async Task<TestActor> ActorAsync(string kind) => kind.StartsWith("guest", StringComparison.Ordinal)
        ? await factory.GuestAsync(kind + "-" + Guid.NewGuid().ToString("N")[..6])
        : factory.EntraUser(kind + "-" + Guid.NewGuid().ToString("N")[..6]);

    private static async Task<string> SnapshotAsync(TestActor actor, params Race[] races)
    {
        var parts = new List<string>
        {
            await (await actor.Client.GetAsync(U("/api/races/interests"), Ct)).Content.ReadAsStringAsync(Ct),
            (await actor.ProposalsAsync(includeDismissed: true)).ToString(),
        };
        foreach (var race in races)
        {
            parts.Add((await (await actor.GetRaceAsync(race.Id)).ReadAsync()).ToString());
        }

        return string.Join('\n', parts);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task Interests_marks_and_signups_of_the_owner_never_change_the_intruders_view(string ownerKind, string intruderKind)
    {
        factory.SkipIfDockerUnavailable();
        var owner = await ActorAsync(ownerKind);
        var intruder = await ActorAsync(intruderKind);
        var token = RaceTestSupport.NewToken();
        var dismissed = await factory.SeedAsync(RaceTestSupport.Facts(token, 20, "Dismissed"));
        var signedUp = await factory.SeedAsync(RaceTestSupport.Facts(token, 21, "SignedUp"));
        var interested = await factory.SeedAsync(RaceTestSupport.Facts(token, 22, "Interested"));
        await intruder.ConfigureAsync(token);
        var before = await SnapshotAsync(intruder, dismissed, signedUp, interested);

        // The owner configures, marks and signs up.
        await owner.ConfigureAsync(token, b => { b["horizonDays"] = 60; b["showInCalendar"] = false; b["minDistanceMeters"] = 5_000; });
        Assert.Equal(HttpStatusCode.OK, (await owner.SetStateAsync(dismissed.Id, "Dismissed")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.SetStateAsync(interested.Id, "Interested")).StatusCode);
        var ownerRun = await (await owner.SignUpAsync(signedUp.Id)).ReadAsync();
        var ownerRunId = ownerRun.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PostAsync(U("/api/races/proposals/seen"), content: null, Ct)).StatusCode);

        // The owner sees its own data (so the comparison below could fail).
        var ownerList = await owner.ProposalsAsync(includeDismissed: true);
        Assert.Equal("SignedUp", ownerList.ItemFor(signedUp.Id).GetProperty("state").GetString());
        Assert.Equal("Dismissed", ownerList.ItemFor(dismissed.Id).GetProperty("state").GetString());

        // The intruder's responses are byte-for-byte unchanged and show no trace of the owner's marks or run.
        var after = await SnapshotAsync(intruder, dismissed, signedUp, interested);
        Assert.Equal(before, after);
        var intruderList = await intruder.ProposalsAsync();
        Assert.All(intruderList.GetProperty("items").EnumerateArray(), i =>
        {
            Assert.Equal("None", i.GetProperty("state").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, i.GetProperty("linkedRunId").ValueKind);
        });
        Assert.Equal(3, intruderList.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.GetAsync(U($"/api/runs/{ownerRunId}"), Ct)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task The_intruders_writes_never_touch_the_owners_rows_and_a_signup_creates_the_run_under_the_intruder(string ownerKind, string intruderKind)
    {
        factory.SkipIfDockerUnavailable();
        var owner = await ActorAsync(ownerKind);
        var intruder = await ActorAsync(intruderKind);
        var token = RaceTestSupport.NewToken();
        var dismissed = await factory.SeedAsync(RaceTestSupport.Facts(token, 20, "Dismissed"));
        var signedUp = await factory.SeedAsync(RaceTestSupport.Facts(token, 21, "SignedUp"));
        await owner.ConfigureAsync(token);
        await intruder.ConfigureAsync(token, b => b["horizonDays"] = 30);
        var ownerInterests = await (await owner.Client.GetAsync(U("/api/races/interests"), Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, (await owner.SetStateAsync(dismissed.Id, "Dismissed")).StatusCode);
        var ownerRunId = (await (await owner.SignUpAsync(signedUp.Id)).ReadAsync()).GetProperty("id").GetGuid();

        // The intruder marks the same races, clears them and signs up to the same race.
        Assert.Equal(HttpStatusCode.OK, (await intruder.SetStateAsync(dismissed.Id, "Interested")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await intruder.SetStateAsync(dismissed.Id, "None")).StatusCode);
        var intruderSignup = await intruder.SignUpAsync(signedUp.Id);
        Assert.Equal(HttpStatusCode.Created, intruderSignup.StatusCode);
        var intruderRunId = (await intruderSignup.ReadAsync()).GetProperty("id").GetGuid();
        Assert.NotEqual(ownerRunId, intruderRunId);
        Assert.Equal(HttpStatusCode.OK, (await intruder.Client.GetAsync(U($"/api/runs/{intruderRunId}"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync(U($"/api/runs/{intruderRunId}"), Ct)).StatusCode);

        // The owner's rows are intact, and the owner's duplicate signup names the owner's own run only.
        Assert.Equal(ownerInterests, await (await owner.Client.GetAsync(U("/api/races/interests"), Ct)).Content.ReadAsStringAsync(Ct));
        var ownerList = await owner.ProposalsAsync(includeDismissed: true);
        Assert.Equal("Dismissed", ownerList.ItemFor(dismissed.Id).GetProperty("state").GetString());
        Assert.Equal(ownerRunId, ownerList.ItemFor(signedUp.Id).GetProperty("linkedRunId").GetGuid());
        var duplicate = await (await owner.SignUpAsync(signedUp.Id)).ReadAsync();
        Assert.Equal(ownerRunId, duplicate.GetProperty("runId").GetGuid());
        var ownerRuns = await (await owner.Client.GetAsync(U($"/api/runs?from={RaceTestSupport.Today:yyyy-MM-dd}&to={RaceTestSupport.Today.AddDays(40):yyyy-MM-dd}"), Ct)).ReadAsync();
        Assert.Equal([ownerRunId], ownerRuns.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToArray());
        Assert.Equal(1, await factory.StateRowsAsync(await owner.GetUserIdAsync()));
    }
}
