using System.Net;
using System.Net.Http.Json;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Calendar;

[Collection(ContainersFixtureGroup.Name)]
public sealed class CalendarEndpointTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> PostAsync(TestActor a, Dictionary<string, object?> body) =>
        a.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative), body, Ct);

    [Fact]
    public async Task Month_returns_the_monday_first_grid_the_runs_in_it_and_iso_week_totals()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("cal");
        // November 2026: the 1st is a Sunday, the 30th a Monday -> grid 2026-10-26 .. 2026-12-06 (6 weeks).
        await PostAsync(user, Json.Run("2026-10-27", r => { r["distanceMeters"] = 8000; r["durationSeconds"] = 2400; r["elevationGainMeters"] = 50; }));   // in grid, previous month
        await PostAsync(user, Json.Run("2026-11-01", r => { r["distanceMeters"] = 12000; r["durationSeconds"] = 3600; }));                                // Sunday of W44
        await PostAsync(user, Json.Run("2026-11-03", r => { r["status"] = "Planned"; r["distanceMeters"] = 15000; r["durationSeconds"] = null; r["rpe"] = null; })); // W45 planned
        await PostAsync(user, Json.Run("2026-11-04", r => { r["runTypeId"] = 40; r["distanceMeters"] = null; r["durationSeconds"] = 3000; }));              // cross-training: excluded from totals
        await PostAsync(user, Json.Run("2026-12-07", r => r["title"] = "outside the grid"));

        var response = await user.Client.GetAsync(new Uri("/api/calendar/months/2026/11", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var month = await response.ReadAsync();
        Assert.Equal(2026, month.GetProperty("year").GetInt32());
        Assert.Equal(11, month.GetProperty("month").GetInt32());
        Assert.Equal("2026-10-26", month.GetProperty("gridStart").GetString());
        Assert.Equal("2026-12-06", month.GetProperty("gridEnd").GetString());
        Assert.Equal(4, month.GetProperty("runs").GetArrayLength());

        var weeks = month.GetProperty("weeks").EnumerateArray().ToList();
        Assert.Equal(6, weeks.Count);
        Assert.Equal(["2026-10-26", "2026-11-02", "2026-11-09", "2026-11-16", "2026-11-23", "2026-11-30"], weeks.Select(w => w.GetProperty("start").GetString()!).ToArray());
        Assert.Equal(44, weeks[0].GetProperty("isoWeek").GetInt32());
        Assert.Equal(2026, weeks[0].GetProperty("isoYear").GetInt32());

        var w44 = weeks[0].GetProperty("completed");
        Assert.Equal(2, w44.GetProperty("runCount").GetInt32()); // 27 Oct + Sunday 1 Nov belong to the same ISO week
        Assert.Equal(20000, w44.GetProperty("distanceMeters").GetDouble());
        Assert.Equal(6000, w44.GetProperty("durationSeconds").GetInt32());
        Assert.Equal(50, w44.GetProperty("elevationGainMeters").GetDouble());

        var w45 = weeks[1];
        Assert.Equal(0, w45.GetProperty("completed").GetProperty("runCount").GetInt32()); // cross-training excluded
        Assert.Equal(1, w45.GetProperty("planned").GetProperty("runCount").GetInt32());
        Assert.Equal(15000, w45.GetProperty("planned").GetProperty("distanceMeters").GetDouble());
    }

    [Theory]
    [InlineData(1949, 5)]
    [InlineData(2101, 5)]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    public async Task Month_outside_the_allowed_range_returns_400(int year, int month)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("calbad");

        var response = await user.Client.GetAsync(new Uri($"/api/calendar/months/{year}/{month}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Empty_month_still_returns_the_grid_and_zero_totals()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("calempty");

        var month = await (await user.Client.GetAsync(new Uri("/api/calendar/months/2021/2", UriKind.Relative), Ct)).ReadAsync();

        Assert.Equal("2021-02-01", month.GetProperty("gridStart").GetString());
        Assert.Equal("2021-02-28", month.GetProperty("gridEnd").GetString());
        Assert.Equal(4, month.GetProperty("weeks").GetArrayLength());
        Assert.Equal(0, month.GetProperty("runs").GetArrayLength());
    }
}
