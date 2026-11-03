using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Guests;

/// <summary>
/// AC4 with real guest sessions: callers whose diaries were filled by the demo seeder can neither see nor change each other's data.
/// (The sibling IsolationMatrixTests covers hand-made guests and Entra users with identical dates.)
/// </summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GuestIsolationTests(ContainersApiFactory factory)
{
    private static readonly string[] Kinds = ["guestA", "guestB", "userA"];

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

    private async Task<TestActor> ActorAsync(string kind) => kind.StartsWith("guest", StringComparison.Ordinal)
        ? await factory.GuestSessionAsync(kind)
        : factory.EntraUser(kind + "-" + Guid.NewGuid().ToString("N")[..6]);

    private static async Task<(DateOnly Today, List<System.Text.Json.JsonElement> Runs)> MonthAsync(TestActor actor)
    {
        var me = await (await actor.Client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct)).ReadAsync();
        var today = DateOnly.Parse(me.GetProperty("today").GetString()!, CultureInfo.InvariantCulture);
        var uri = new Uri($"/api/runs?from={today.AddDays(-45):yyyy-MM-dd}&to={today.AddDays(40):yyyy-MM-dd}", UriKind.Relative);
        var list = await (await actor.Client.GetAsync(uri, Ct)).ReadAsync();
        return (today, list.GetProperty("items").EnumerateArray().ToList());
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task Intruder_cannot_read_update_or_delete_and_never_lists_the_owners_runs(string ownerKind, string intruderKind)
    {
        factory.SkipIfDockerUnavailable();
        var owner = await ActorAsync(ownerKind);
        var intruder = await ActorAsync(intruderKind);

        // Give both something to protect: guests already own seeded runs, the Entra user creates one for today.
        foreach (var actor in new[] { owner, intruder }.Where(a => a.Name.StartsWith("userA", StringComparison.Ordinal)))
        {
            var me = await (await actor.Client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct)).ReadAsync();
            await actor.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative), Json.Run(me.GetProperty("today").GetString()!), Ct);
        }

        var (_, ownerRuns) = await MonthAsync(owner);
        var (_, intruderRuns) = await MonthAsync(intruder);
        Assert.NotEmpty(ownerRuns);
        var ownerIds = ownerRuns.Select(r => r.GetProperty("id").GetGuid()).ToHashSet();
        Assert.All(intruderRuns, r => Assert.DoesNotContain(r.GetProperty("id").GetGuid(), ownerIds));

        var target = ownerRuns[0];
        var id = target.GetProperty("id").GetGuid();
        var uri = new Uri($"/api/runs/{id}", UriKind.Relative);
        var version = target.GetProperty("version").GetString();

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.GetAsync(uri, Ct)).StatusCode);
        var put = await intruder.Client.PutAsJsonAsync(
            uri, Json.Run(target.GetProperty("date").GetString()!, r => { r["title"] = "hijacked"; r["version"] = version; }), Ct);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.DeleteAsync(uri, Ct)).StatusCode);

        var after = await (await owner.Client.GetAsync(uri, Ct)).ReadAsync();
        Assert.Equal(target.GetProperty("title").ValueKind, after.GetProperty("title").ValueKind);
        Assert.NotEqual("hijacked", after.GetProperty("title").GetString());
        Assert.Equal(version, after.GetProperty("version").GetString());
    }
}
