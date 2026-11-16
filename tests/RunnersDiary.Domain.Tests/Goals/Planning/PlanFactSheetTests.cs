using System.Globalization;
using System.Reflection;
using System.Text.Json;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Tests.Insights;

namespace RunnersDiary.Domain.Tests.Goals.Planning;

public sealed class PlanFactSheetTests
{
    private static readonly DateOnly Today = TrainingHistoryTests.Today;

    private static GoalSpec Goal(DateOnly? target = null) =>
        new(GoalType.Performance, 10_000, null, target ?? new DateOnly(2027, 1, 24), 2_700, Surface.Road);

    private static (PlanFactSheet Sheet, byte[] Bytes) Sheet(
        List<RunFact>? facts = null,
        IReadOnlyList<CommitmentFact>? commitments = null,
        IReadOnlyList<DismissedHint>? dismissed = null,
        DateOnly? target = null,
        List<RaceFact>? races = null)
    {
        facts ??= TrainingHistoryTests.Facts();
        races ??= [InsightsTestData.Race("2026-11-07", RaceDistanceCategory.TenK, result: 2_900, id: 4, distance: 10_000)];
        var goal = Goal(target);
        var history = TrainingHistory.From(facts, Today);
        var estimate = GoalEstimator.Estimate(goal, Today, facts, races);
        var report = InsightsReportBuilder.Build(new InsightsPeriod(Today.AddDays(-364), Today), ComparisonKind.None, Today, facts, facts, races, null);
        var envelope = PlanEnvelopeBuilder.Build(goal, Today, history, commitments ?? [], PlanEnvelopeBuilder.ReferencePace(goal, estimate.Prediction, history));
        var sheet = PlanFactSheetBuilder.Build(goal, estimate, Today, report, history, commitments ?? [], dismissed ?? [], envelope, "en");
        return (sheet, PlanFactSheetJson.ToUtf8Bytes(sheet));
    }

    private static JsonElement Parse(byte[] bytes) => JsonDocument.Parse(bytes).RootElement;

    [Fact]
    public void The_canonical_json_has_the_documented_shape_and_hand_computed_values()
    {
        var (sheet, bytes) = Sheet(commitments:
        [
            new CommitmentFact(new DateOnly(2026, 11, 19), 20, 10_000, 3_000, CommitmentKind.Planned, false),
            new CommitmentFact(new DateOnly(2026, 11, 22), 22, 15_000, null, CommitmentKind.Planned, true),
            new CommitmentFact(new DateOnly(2026, 12, 13), null, null, null, CommitmentKind.OtherGoal, false),
        ], dismissed: [new DismissedHint(new DateOnly(2026, 11, 24), 23)]);
        var root = Parse(bytes);

        Assert.Equal(PlanFactSheet.CurrentVersion, root.GetProperty("factSheetVersion").GetInt32());
        Assert.Equal("2026-11-16", root.GetProperty("today").GetString());
        Assert.Equal("en", root.GetProperty("language").GetString());

        var goal = root.GetProperty("goal");
        Assert.Equal("performance", goal.GetProperty("type").GetString());
        Assert.Equal(10_000, goal.GetProperty("distanceMeters").GetDouble());
        Assert.Equal("road", goal.GetProperty("surface").GetString());
        Assert.Equal(2_700, goal.GetProperty("targetDurationSeconds").GetInt32());
        Assert.Equal(270, goal.GetProperty("targetPaceSecondsPerKm").GetInt32());
        Assert.Equal(sheet.Goal.PredictedDurationSeconds, goal.GetProperty("predictedDurationSeconds").GetInt32());
        Assert.Equal(9, goal.GetProperty("weeksToGoal").GetInt32()); // 69 days
        Assert.False(goal.TryGetProperty("elevationGainMeters", out _));

        var history = root.GetProperty("history");
        var weeks = history.GetProperty("weeks");
        Assert.Equal(10, weeks.GetArrayLength());
        Assert.Equal("2026-09-07", weeks[0].GetProperty("start").GetString());
        var last = weeks[9];
        Assert.Equal(38_000, last.GetProperty("distanceMeters").GetDouble());
        Assert.Equal(12_600, last.GetProperty("durationSeconds").GetInt64());
        Assert.Equal(3, last.GetProperty("sessions").GetInt32());
        Assert.Equal(20_000, last.GetProperty("longestRunMeters").GetDouble());
        Assert.Equal(1, last.GetProperty("hardSessions").GetInt32());
        Assert.True(last.GetProperty("load").GetDouble() > 0);
        Assert.Equal(["tuesday"], history.GetProperty("usualWeekdays").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(0.7, history.GetProperty("sessionsPerWeek").GetDouble());
        Assert.Equal(JsonValueKind.Object, history.GetProperty("latestAcwr").ValueKind);
        var race = Assert.Single(history.GetProperty("races").EnumerateArray());
        Assert.Equal("2026-11-07", race.GetProperty("date").GetString());
        Assert.Equal(2_900, race.GetProperty("durationSeconds").GetInt32());
        Assert.Equal("roadRace", race.GetProperty("runType").GetString());

        var commitments = root.GetProperty("commitments");
        Assert.Equal(3, commitments.GetArrayLength());
        Assert.Equal("easy", commitments[0].GetProperty("runType").GetString());
        Assert.False(commitments[0].TryGetProperty("kind", out _)); // planned is the default
        Assert.False(commitments[0].TryGetProperty("fromThisGoal", out _));
        Assert.True(commitments[1].GetProperty("fromThisGoal").GetBoolean());
        Assert.Equal("otherGoal", commitments[2].GetProperty("kind").GetString());
        Assert.False(commitments[2].TryGetProperty("runType", out _));

        var dismissed = Assert.Single(root.GetProperty("dismissed").EnumerateArray());
        Assert.Equal("tempo", dismissed.GetProperty("runType").GetString());

        var envelope = root.GetProperty("envelope");
        Assert.Equal("2026-11-17", envelope.GetProperty("planStart").GetString());
        Assert.Equal("2027-01-23", envelope.GetProperty("planEnd").GetString());
        Assert.False(envelope.GetProperty("horizonCapped").GetBoolean());
        Assert.Equal(270, envelope.GetProperty("referencePaceSecondsPerKm").GetInt32());
        Assert.Equal(13, envelope.GetProperty("allowedRunTypes").GetArrayLength());
        var week1 = envelope.GetProperty("weeks")[0];
        Assert.Equal("2026-11-16", week1.GetProperty("start").GetString());
        Assert.Equal("base", week1.GetProperty("phase").GetString());
        Assert.Equal(sheet.Envelope.Weeks[0].MaxDistanceMeters, week1.GetProperty("maxDistanceMeters").GetDouble());
        Assert.Equal(25_000, week1.GetProperty("fixedDistanceMeters").GetDouble()); // 10 km Thursday + 15 km Sunday
    }

    [Fact]
    public void The_same_inputs_give_identical_bytes_and_fingerprints_and_a_change_changes_them()
    {
        var (_, first) = Sheet();
        var (sheetB, second) = Sheet();
        var (sheetC, third) = Sheet(dismissed: [new DismissedHint(new DateOnly(2026, 11, 24), 23)]);

        Assert.Equal(first, second);
        Assert.Equal(PlanFactSheetJson.Fingerprint(sheetB), PlanFactSheetJson.Fingerprint(Sheet().Sheet));
        Assert.Equal(32, PlanFactSheetJson.Fingerprint(sheetB).Length);
        Assert.NotEqual(first, third);
        Assert.NotEqual(PlanFactSheetJson.Fingerprint(sheetB), PlanFactSheetJson.Fingerprint(sheetC));
    }

    [Fact]
    public void The_json_is_culture_independent()
    {
        var (_, expected) = Sheet();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
            var (_, actual) = Sheet();
            Assert.Equal(expected, actual);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static readonly HashSet<string> ForbiddenMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "name", "notes", "location", "locationName", "eventName", "id", "runId", "goalId", "userId", "gear", "weather", "heartRate", "avgHeartRate",
    };

    [Fact]
    public void No_record_of_the_fact_sheet_has_a_free_text_or_identifier_member()
    {
        var seen = new HashSet<Type>();
        var offenders = new List<string>();
        Walk(typeof(PlanFactSheet), seen, offenders);

        Assert.Contains(typeof(CommitmentFact), seen);
        Assert.Contains(typeof(PlanWeek), seen);
        Assert.Contains(typeof(PlanRaceFact), seen);
        Assert.Empty(offenders);
    }

    private static void Walk(Type type, HashSet<Type> seen, List<string> offenders)
    {
        if (type.Namespace is null || !type.Namespace.StartsWith("RunnersDiary.Domain", StringComparison.Ordinal) || !seen.Add(type))
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (ForbiddenMembers.Contains(property.Name))
            {
                offenders.Add($"{type.Name}.{property.Name}");
            }

            var propertyType = property.PropertyType;
            var inner = propertyType.IsGenericType ? propertyType.GetGenericArguments()[0] : propertyType;
            Walk(Nullable.GetUnderlyingType(inner) ?? inner, seen, offenders);
        }
    }

    [Fact]
    public void The_json_never_contains_a_forbidden_member_name_and_every_string_value_is_a_date_or_a_code()
    {
        var (_, bytes) = Sheet(
            commitments: [new CommitmentFact(new DateOnly(2026, 11, 19), 20, 10_000, 3_000, CommitmentKind.Planned, true)],
            dismissed: [new DismissedHint(new DateOnly(2026, 11, 24), 23)]);
        var names = new List<string>();
        var strings = new List<string>();
        Scan(Parse(bytes), names, strings);

        Assert.DoesNotContain(names, n => ForbiddenMembers.Contains(n));
        Assert.All(strings, s => Assert.Matches("^([0-9]{4}-[0-9]{2}-[0-9]{2}|[a-z][A-Za-z]*)$", s));
    }

    private static void Scan(JsonElement element, List<string> names, List<string> strings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    names.Add(property.Name);
                    Scan(property.Value, names, strings);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Scan(item, names, strings);
                }

                break;
            case JsonValueKind.String:
                strings.Add(element.GetString()!);
                break;
        }
    }

    [Fact]
    public void Commitments_and_dismissed_hints_are_capped_nearest_first()
    {
        var commitments = Enumerable.Range(0, 250)
            .Select(i => new CommitmentFact(Today.AddDays(1 + (i % 100)), 20, 5_000 + i, null, CommitmentKind.Planned, false))
            .Reverse()
            .ToList();
        var dismissed = Enumerable.Range(0, 80).Select(i => new DismissedHint(Today.AddDays(1 + i), 20)).Reverse().ToList();

        var (sheet, _) = Sheet(commitments: commitments, dismissed: dismissed);

        Assert.Equal(200, sheet.Commitments.Count);
        Assert.Equal(60, sheet.Dismissed.Count);
        Assert.Equal(sheet.Commitments.OrderBy(c => c.Date), sheet.Commitments);
        Assert.Equal(Today.AddDays(1), sheet.Dismissed[0].Date);
        Assert.Equal(Today.AddDays(60), sheet.Dismissed[^1].Date);
    }

    [Fact]
    public void A_sixteen_week_sheet_with_200_commitments_stays_within_the_size_cap()
    {
        // Worst case the caps allow: 200 commitments over the whole horizon (half with distance and duration, half cross-training), 60 hints, 6 races.
        var commitments = Enumerable.Range(0, 200)
            .Select(i => i % 2 == 0
                ? new CommitmentFact(Today.AddDays(1 + (i % 110)), 20, 8_000, 2_700, CommitmentKind.Planned, i % 10 == 0)
                : new CommitmentFact(Today.AddDays(1 + (i % 110)), 42, null, 3_600, CommitmentKind.Planned, false))
            .ToList();
        var dismissed = Enumerable.Range(0, 60).Select(i => new DismissedHint(Today.AddDays(1 + i), 23)).ToList();
        var races = Enumerable.Range(0, 6)
            .Select(i => InsightsTestData.Race(Today.AddDays(-20 - (i * 30)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), RaceDistanceCategory.TenK, result: 3_000 + i, id: 800 + i, distance: 10_000))
            .ToList();
        var facts = TrainingHistoryTests.Facts();

        var (sheet, bytes) = Sheet(facts, commitments, dismissed, target: Today.AddDays(210), races: races);

        Assert.Equal(16, sheet.Envelope.Weeks.Count);
        Assert.Equal(6, sheet.History.Races.Count);
        Assert.True(bytes.Length <= 24_000, $"The fact sheet is {bytes.Length} characters.");
    }

    [Fact]
    public void Only_the_six_latest_completed_races_of_the_last_365_days_are_listed_oldest_first()
    {
        var races = Enumerable.Range(0, 8)
            .Select(i => InsightsTestData.Race(Today.AddDays(-10 - (i * 40)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), RaceDistanceCategory.TenK, result: 3_000, id: 700 + i, distance: 10_000))
            .ToList();
        races.Add(InsightsTestData.Race("2025-11-16", RaceDistanceCategory.TenK, result: 3_000, id: 780, distance: 10_000)); // 365 days ago: outside

        var (sheet, _) = Sheet(races: races);

        Assert.Equal(6, sheet.History.Races.Count);
        Assert.Equal(sheet.History.Races.OrderBy(r => r.Date), sheet.History.Races);
        Assert.Equal(Today.AddDays(-10), sheet.History.Races[^1].Date);
        Assert.Equal(Today.AddDays(-210), sheet.History.Races[0].Date);
    }
}
