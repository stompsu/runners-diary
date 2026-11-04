using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Insights;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Tests.Insights;

[Collection(ContainersFixtureGroup.Name)]
public sealed class InsightsEndpointTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri Report(string query) => new("/api/insights/report" + query, UriKind.Relative);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<JsonElement> GetReportAsync(TestActor actor, string query)
    {
        var response = await actor.Client.GetAsync(Report(query), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync();
    }

    private static async Task CreateAsync(TestActor actor, Dictionary<string, object?> body)
    {
        var response = await actor.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative), body, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public static TheoryData<string, string> BadQueries() => new()
    {
        { "", "from" },
        { "?to=2026-11-08", "from" },
        { "?from=2026-11-02", "to" },
        { "?from=2026-13-01&to=2026-11-08", "from" },
        { "?from=2026-11-10&to=2026-11-01", "to" },
        { "?from=2025-01-01&to=2026-01-02", "to" },          // 367 days
        { "?from=1949-12-31&to=1950-01-05", "from" },
        { "?from=2100-12-01&to=2101-01-01", "to" },
        { "?from=2026-11-02&to=2026-11-08&compare=bogus", "compare" },
        { "?from=2026-11-02&to=2026-11-08&compare=Previous", "compare" },
        { "?from=2026-11-02&to=2026-11-08&compare=", "compare" },
    };

    [Theory]
    [MemberData(nameof(BadQueries))]
    public async Task Invalid_input_returns_a_400_problem_with_the_camel_case_key(string query, string key)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("insbad");

        var response = await user.Client.GetAsync(Report(query), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.ReadAsync();
        Assert.True(problem.GetProperty("errors").TryGetProperty(key, out _), problem.ToString());
    }

    [Fact]
    public async Task A_366_day_span_is_accepted_and_compare_defaults_to_previous()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("ins366");

        var report = await GetReportAsync(user, "?from=2024-01-01&to=2024-12-31"); // leap year: exactly 366 days

        Assert.Equal(366, report.GetProperty("period").GetProperty("days").GetInt32());
        Assert.Equal("previous", report.GetProperty("comparison").GetProperty("kind").GetString());
        Assert.Equal(53, report.GetProperty("weeks").GetArrayLength()); // Mon 2024-01-01 .. Sun 2025-01-05
        Assert.Equal(12, report.GetProperty("months").GetArrayLength());
    }

    [Fact]
    public async Task The_report_has_the_documented_shape_and_is_never_cached()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("insshape");
        await CreateAsync(user, Json.Run("2025-03-04", r => { r["distanceMeters"] = 10_000; r["durationSeconds"] = 3000; r["rpe"] = 6; }));

        var response = await user.Client.GetAsync(Report("?from=2025-03-03&to=2025-03-09&compare=none"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.Private);

        var report = await response.ReadAsync();
        Assert.Equal(1, report.GetProperty("metricsVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("comparison").ValueKind);
        var period = report.GetProperty("period");
        Assert.Equal("2025-03-03", period.GetProperty("from").GetString());
        Assert.False(period.GetProperty("isPartial").GetBoolean());
        Assert.Equal(7, period.GetProperty("elapsedDays").GetInt32());
        var thresholds = report.GetProperty("thresholds");
        Assert.Equal(0.8, thresholds.GetProperty("acwrLow").GetDouble());
        Assert.Equal(1.3, thresholds.GetProperty("acwrHigh").GetDouble());
        Assert.Equal(2.0, thresholds.GetProperty("monotonyHigh").GetDouble());
        Assert.Equal(35, thresholds.GetProperty("acwrMinHistoryDays").GetInt32());

        var totals = report.GetProperty("totals");
        Assert.Equal(1, totals.GetProperty("runCount").GetInt32());
        Assert.Equal(10_000, totals.GetProperty("distanceMeters").GetDouble());
        Assert.Equal(300.0, totals.GetProperty("load").GetDouble()); // 50 min x RPE 6
        Assert.Equal(300.0, totals.GetProperty("averagePaceSecondsPerKm").GetDouble());

        var load = report.GetProperty("load");
        Assert.Equal("2025-03-04", load.GetProperty("historyStart").GetString());
        Assert.Equal("insufficientHistory", load.GetProperty("atAsOf").GetProperty("unavailableReason").GetString());
        Assert.Equal(7, load.GetProperty("daily").GetArrayLength());
        Assert.Equal(7, load.GetProperty("bandDays").GetProperty("unavailable").GetInt32());

        Assert.Equal(1, report.GetProperty("weeks").GetArrayLength());
        var week = report.GetProperty("weeks")[0];
        Assert.Equal(2025, week.GetProperty("isoYear").GetInt32());
        Assert.Equal(10, week.GetProperty("isoWeek").GetInt32());
        Assert.True(week.GetProperty("complete").GetBoolean());
        Assert.Equal(1, report.GetProperty("months").GetArrayLength());
        Assert.Equal("easy", report.GetProperty("typeDistribution")[0].GetProperty("code").GetString());
        Assert.Equal("Training", report.GetProperty("typeDistribution")[0].GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Array, report.GetProperty("races").ValueKind);
        Assert.Equal(["easy"], report.GetProperty("paceTrend").GetProperty("runTypeCodes").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(1, report.GetProperty("dataQuality").GetProperty("completedSessions").GetInt32());
    }

    [Fact]
    public async Task An_account_without_runs_gets_zeros_and_empty_lists()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("insempty");

        var report = await GetReportAsync(user, "?from=2025-03-03&to=2025-03-09");

        var totals = report.GetProperty("totals");
        Assert.Equal(0, totals.GetProperty("runCount").GetInt32());
        Assert.Equal(0.0, totals.GetProperty("load").GetDouble());
        Assert.Equal(JsonValueKind.Null, totals.GetProperty("averagePaceSecondsPerKm").ValueKind);
        Assert.Equal(0, report.GetProperty("races").GetArrayLength());
        Assert.Equal(0, report.GetProperty("typeDistribution").GetArrayLength());
        Assert.Equal(0, report.GetProperty("paceTrend").GetProperty("weeks").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("load").GetProperty("historyStart").ValueKind);
        var comparison = report.GetProperty("comparison");
        Assert.Equal(0, comparison.GetProperty("totals").GetProperty("runCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, comparison.GetProperty("deltas").GetProperty("runCount").GetProperty("percent").ValueKind);
    }

    [Fact]
    public async Task The_previous_year_comparison_totals_the_other_range_only()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("insyear");
        await CreateAsync(user, Json.Run("2024-03-06", r => { r["distanceMeters"] = 10_000; r["durationSeconds"] = 3000; }));
        await CreateAsync(user, Json.Run("2023-03-08", r => { r["distanceMeters"] = 8_000; r["durationSeconds"] = 2700; }));
        await CreateAsync(user, Json.Run("2023-03-20", r => r["distanceMeters"] = 50_000)); // after the comparison window

        // Monday 2024-03-04 .. Sunday 2024-03-17 (14 days) -> -364 days = 2023-03-06 .. 2023-03-19
        var report = await GetReportAsync(user, "?from=2024-03-04&to=2024-03-17&compare=previousYear");

        var comparison = report.GetProperty("comparison");
        Assert.Equal("previousYear", comparison.GetProperty("kind").GetString());
        Assert.Equal("2023-03-06", comparison.GetProperty("from").GetString());
        Assert.Equal("2023-03-19", comparison.GetProperty("to").GetString());
        Assert.Equal(8_000, comparison.GetProperty("totals").GetProperty("distanceMeters").GetDouble());
        Assert.Equal(10_000, report.GetProperty("totals").GetProperty("distanceMeters").GetDouble());
        Assert.Equal(2_000, comparison.GetProperty("deltas").GetProperty("distanceMeters").GetProperty("absolute").GetDouble());
        Assert.Equal(25.0, comparison.GetProperty("deltas").GetProperty("distanceMeters").GetProperty("percent").GetDouble());
    }

    [Fact]
    public async Task Editing_a_run_changes_the_next_report()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("insedit");
        var created = await (await user.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative),
            Json.Run("2025-03-04", r => { r["distanceMeters"] = 10_000; r["durationSeconds"] = 3000; }), Ct)).ReadAsync();
        var before = await GetReportAsync(user, "?from=2025-03-03&to=2025-03-09");

        var update = await user.Client.PutAsJsonAsync(new Uri("/api/runs/" + created.GetProperty("id").GetGuid(), UriKind.Relative),
            Json.Run("2025-03-04", r => { r["distanceMeters"] = 15_000; r["durationSeconds"] = 3000; r["version"] = created.GetProperty("version").GetString(); }), Ct);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var after = await GetReportAsync(user, "?from=2025-03-03&to=2025-03-09");

        Assert.Equal(10_000, before.GetProperty("totals").GetProperty("distanceMeters").GetDouble());
        Assert.Equal(15_000, after.GetProperty("totals").GetProperty("distanceMeters").GetDouble());
    }

    [Fact]
    public async Task An_expired_guest_token_is_rejected()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestAsync("insexpired");
        using var client = factory.CreateClient().WithBearer(TestTokens.Guest(guest.UserId!.Value, lifetime: TimeSpan.FromMinutes(-10)));

        var response = await client.GetAsync(Report("?from=2026-11-02&to=2026-11-08"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_fresh_guest_sees_the_demo_story_and_the_numbers_match_the_domain_on_the_same_facts()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("insdemo");
        var me = await (await guest.Client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct)).ReadAsync();
        var today = DateOnly.Parse(me.GetProperty("today").GetString()!, CultureInfo.InvariantCulture);
        var monday = IsoWeek.MondayOf(today);
        var from = monday.AddDays(-77);
        var to = monday.AddDays(6);

        var report = await GetReportAsync(guest, $"?from={Iso(from)}&to={Iso(to)}&compare=previous");

        // The story: spike (Sunday of week -11) is high, taper (Sunday of week -8) is low.
        var daily = report.GetProperty("load").GetProperty("daily").EnumerateArray().ToDictionary(d => d.GetProperty("date").GetString()!);
        Assert.Equal("high", daily[Iso(monday.AddDays(-71))].GetProperty("band").GetString());
        Assert.Equal("low", daily[Iso(monday.AddDays(-50))].GetProperty("band").GetString());
        Assert.Equal(0, report.GetProperty("dataQuality").GetProperty("sessionsWithEstimatedRpe").GetInt32());
        Assert.True(report.GetProperty("paceTrend").GetProperty("weeks").GetArrayLength() >= 4);
        Assert.NotEqual(JsonValueKind.Null, report.GetProperty("paceTrend").GetProperty("slopeSecondsPerKmPerWeek").ValueKind);
        var races = report.GetProperty("races").EnumerateArray().ToList();
        Assert.True(races.Count(r => r.GetProperty("status").GetString() == "Completed" && r.GetProperty("resultTimeSeconds").ValueKind == JsonValueKind.Number) >= 2);
        Assert.Equal(1, report.GetProperty("metricsVersion").GetInt32());

        // The same facts through the Domain directly, bypassing the service.
        var (facts, raceFacts) = await factory.WithDbAsync(async db =>
        {
            var runs = await db.Runs.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).AsNoTracking()
                .Where(r => r.UserId == guest.UserId)
                .ToListAsync(Ct);
            var f = runs.Select(r => new RunFact(r.Id, r.Date, r.Status, r.RunTypeId, r.DistanceMeters, r.DurationSeconds, r.ElevationGainMeters, r.Rpe, r.Surface, r.AvgHeartRate)).ToList();
            var rf = runs.Where(r => r.Race is not null).Select(r => new RaceFact(
                r.Id, r.Date, r.Status, r.RunTypeId, r.Race!.Name, r.Race.DistanceCategory, r.Race.OfficialDistanceMeters, r.DistanceMeters, r.DurationSeconds,
                r.Race.ResultTimeSeconds, r.Race.TargetTimeSeconds, r.Race.OverallPlace, r.Race.CategoryPlace, r.Race.CategoryName, r.Rating?.Stars)).ToList();
            return (f, rf);
        });
        var expected = InsightsReportBuilder.Build(new InsightsPeriod(from, to), ComparisonKind.Previous, today, facts, facts, raceFacts, null);

        var totals = report.GetProperty("totals");
        Assert.Equal(expected.Totals.RunCount, totals.GetProperty("runCount").GetInt32());
        Assert.Equal(expected.Totals.DistanceMeters, totals.GetProperty("distanceMeters").GetDouble());
        Assert.Equal(expected.Totals.DurationSeconds, totals.GetProperty("durationSeconds").GetInt64());
        Assert.Equal(expected.Totals.Load, totals.GetProperty("load").GetDouble());
        Assert.Equal(expected.Load.AtAsOf.Value, report.GetProperty("load").GetProperty("atAsOf").GetProperty("value").GetDouble());
        Assert.Equal(expected.Weeks.Select(w => w.Load).ToArray(), report.GetProperty("weeks").EnumerateArray().Select(w => w.GetProperty("load").GetDouble()).ToArray());
        Assert.Equal(expected.Weeks.Select(w => w.AcwrAtEnd).ToArray(), report.GetProperty("weeks").EnumerateArray().Select(w => w.GetProperty("acwrAtEnd").ValueKind == JsonValueKind.Null ? (double?)null : w.GetProperty("acwrAtEnd").GetDouble()).ToArray());
        Assert.Equal(expected.Comparison!.Totals.Load, report.GetProperty("comparison").GetProperty("totals").GetProperty("load").GetDouble());
        Assert.Equal(expected.Races.Select(r => r.RunId).ToArray(), races.Select(r => r.GetProperty("runId").GetGuid()).ToArray());
        Assert.Equal(expected.Races.Select(r => r.IsPersonalBest).ToArray(), races.Select(r => r.GetProperty("isPersonalBest").GetBoolean()).ToArray());
        Assert.Equal(expected.PaceTrend.SlopeSecondsPerKmPerWeek, report.GetProperty("paceTrend").GetProperty("slopeSecondsPerKmPerWeek").GetDouble());
    }

    [Fact]
    public async Task A_worst_case_report_runs_at_most_five_projection_queries_that_never_read_notes_or_tracks()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("insqueries");
        var interceptor = new CommandRecorder();
        var connection = new SqlConnectionStringBuilder(factory.SqlConnectionString) { InitialCatalog = "runnersdiary_api_tests" }.ConnectionString;
        var options = new DbContextOptionsBuilder<RunnersDiaryDbContext>()
            .UseSqlServer(connection, sql => sql.UseNetTopologySuite())
            .AddInterceptors(interceptor)
            .Options;
        var caller = new FixedCurrentUser(guest.UserId!.Value, isGuest: true);
        await using var db = new RunnersDiaryDbContext(options, caller);
        var service = new InsightsReportService(db, caller, TimeProvider.System);

        // 366 days of the current year-ish with a previous-year comparison: the largest allowed request.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = await service.GetReportAsync(today.AddDays(-365), today, ComparisonKind.PreviousYear, Ct);

        Assert.True(report.Totals.RunCount > 0);
        Assert.InRange(interceptor.Commands.Count, 1, 5);
        Assert.All(interceptor.Commands, sql =>
        {
            Assert.DoesNotContain("[Notes]", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("GpxTracks", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("Location_", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("Rating_Comment", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("Race_ResultsUrl", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("Race_BibNumber", sql, StringComparison.Ordinal);
        });
    }

    private sealed class CommandRecorder : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override async ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
