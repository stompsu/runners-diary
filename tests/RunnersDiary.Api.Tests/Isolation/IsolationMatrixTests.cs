using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Isolation;

/// <summary>
/// AC4: for every ordered pair of callers (two guests, two Entra users, guest vs user in both directions) nobody can list, read,
/// update or delete someone else's run, and foreign ids are indistinguishable from missing ones.
/// Guests here are Guest-kind rows created directly in the database; step 6 extends this to real guest sessions.
/// </summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class IsolationMatrixTests(ContainersApiFactory factory)
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

    private async Task<TestActor> ActorAsync(string kind) => kind.StartsWith("guest", StringComparison.Ordinal)
        ? await factory.GuestAsync(kind + "-" + Guid.NewGuid().ToString("N")[..6])
        : factory.EntraUser(kind + "-" + Guid.NewGuid().ToString("N")[..6]);

    private static Uri RunUri(Guid id) => new($"/api/runs/{id}", UriKind.Relative);

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task Intruder_cannot_list_read_update_or_delete_the_owners_run(string ownerKind, string intruderKind)
    {
        factory.SkipIfDockerUnavailable();
        var owner = await ActorAsync(ownerKind);
        var intruder = await ActorAsync(intruderKind);

        var created = await (await owner.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative), Json.Run("2026-11-12", r => r["title"] = "secret"), Ct)).ReadAsync();
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetString();
        var own = await (await intruder.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative), Json.Run("2026-11-12", r => r["title"] = "mine"), Ct)).ReadAsync();
        var missingId = Guid.NewGuid();

        // read
        var foreignGet = await intruder.Client.GetAsync(RunUri(id), Ct);
        var missingGet = await intruder.Client.GetAsync(RunUri(missingId), Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreignGet.StatusCode);
        Assert.Equal("application/problem+json", foreignGet.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await Shape(missingGet), await Shape(foreignGet));

        // update (with the owner's real, current version: only ownership can stop it)
        var put = await intruder.Client.PutAsJsonAsync(RunUri(id), Json.Run("2026-11-12", r => { r["title"] = "hijacked"; r["version"] = version; }), Ct);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);

        // delete
        var delete = await intruder.Client.DeleteAsync(RunUri(id), Ct);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        // lists only contain the intruder's own data
        var list = await (await intruder.Client.GetAsync(new Uri("/api/runs?from=2026-11-01&to=2026-11-30", UriKind.Relative), Ct)).ReadAsync();
        Assert.Equal([own.GetProperty("id").GetGuid()], list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToArray());
        var month = await (await intruder.Client.GetAsync(new Uri("/api/calendar/months/2026/11", UriKind.Relative), Ct)).ReadAsync();
        Assert.Equal([own.GetProperty("id").GetGuid()], month.GetProperty("runs").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToArray());
        Assert.Equal(1, month.GetProperty("weeks").EnumerateArray().Sum(w => w.GetProperty("completed").GetProperty("runCount").GetInt32()));

        // the owner's data is untouched
        var after = await (await owner.Client.GetAsync(RunUri(id), Ct)).ReadAsync();
        Assert.Equal("secret", after.GetProperty("title").GetString());
        Assert.Equal(version, after.GetProperty("version").GetString());
        var ownerList = await (await owner.Client.GetAsync(new Uri("/api/runs?from=2026-11-01&to=2026-11-30", UriKind.Relative), Ct)).ReadAsync();
        Assert.Equal(1, ownerList.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task The_insights_report_never_includes_another_callers_data(string ownerKind, string intruderKind)
    {
        factory.SkipIfDockerUnavailable();
        var owner = await ActorAsync(ownerKind);
        var intruder = await ActorAsync(intruderKind);
        var query = new Uri("/api/insights/report?from=2025-05-26&to=2025-06-08&compare=previous", UriKind.Relative);
        var before = await (await intruder.Client.GetAsync(query, Ct)).Content.ReadAsStringAsync(Ct);

        // The owner adds a heavy week and a race (and an earlier race that would be the intruder's "PB at the time" if it leaked).
        await owner.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative), Json.Run("2025-05-30", r => { r["distanceMeters"] = 42_000; r["durationSeconds"] = 14_400; r["rpe"] = 9; }), Ct);
        await owner.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative), Json.Run("2025-06-01", r =>
        {
            r["runTypeId"] = 1;
            r["distanceMeters"] = 10_000;
            r["durationSeconds"] = 1_800;
            r["rpe"] = 10;
            r["race"] = new { name = "Owner 10K", distanceCategory = "TenK", resultTimeSeconds = 1_800 };
        }), Ct);
        await owner.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative), Json.Run("2025-04-28", r => { r["distanceMeters"] = 30_000; r["durationSeconds"] = 9_000; }), Ct);
        var own = await (await owner.Client.GetAsync(query, Ct)).ReadAsync();
        var after = await (await intruder.Client.GetAsync(query, Ct)).Content.ReadAsStringAsync(Ct);

        // The owner sees the data (so the test could fail), the intruder's report is byte-for-byte unchanged.
        Assert.Equal(2, own.GetProperty("totals").GetProperty("runCount").GetInt32());
        Assert.Equal(1, own.GetProperty("races").GetArrayLength());
        Assert.True(own.GetProperty("load").GetProperty("historyStart").GetString() == "2025-04-28");
        Assert.Equal(before, after);
        var intruderReport = JsonDocument.Parse(after).RootElement;
        Assert.Equal(0, intruderReport.GetProperty("totals").GetProperty("runCount").GetInt32());
        Assert.Equal(0, intruderReport.GetProperty("races").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, intruderReport.GetProperty("load").GetProperty("historyStart").ValueKind);
        Assert.Equal(0, intruderReport.GetProperty("comparison").GetProperty("totals").GetProperty("runCount").GetInt32());
    }

    [Fact]
    public async Task Run_ids_cannot_be_chosen_by_the_client_to_collide_with_foreign_rows()
    {
        factory.SkipIfDockerUnavailable();
        var owner = factory.EntraUser("idowner");
        var intruder = factory.EntraUser("idintruder");
        var created = await (await owner.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative), Json.Run("2026-11-12"), Ct)).ReadAsync();

        // The id in the body is an unknown member and is rejected; a new run always gets a server-generated id.
        var body = Json.Run("2026-11-12", r => r["id"] = created.GetProperty("id").GetGuid());
        var response = await intruder.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative), body, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<string> Shape(HttpResponseMessage response)
    {
        var json = await response.ReadAsync();
        return $"{(int)response.StatusCode}|{json.GetProperty("title").GetString()}|{json.GetProperty("type").GetString()}";
    }
}
