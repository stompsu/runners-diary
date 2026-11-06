using System.Text.Json;
using System.Text.Json.Serialization;
using RunnersDiary.Ai.Tests.Evals;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Domain.Runs;
using static RunnersDiary.Ai.Tests.Support.Training;

namespace RunnersDiary.Ai.Tests.FactSheets;

/// <summary>Plan AC1: the fact sheet is a verbatim projection of the report with only the allowed annotations.</summary>
public sealed class FactSheetBuilderTests
{
    /// <summary>Annotations the builder may add (list lengths and bucket indexes); every other number must come from the report.</summary>
    private static readonly HashSet<string> AnnotationKeys =
    [
        "weeks.count", "months.count", "types.count", "races.count", "races.completedCount", "races.plannedCount", "pace.pointCount",
        "acwr.peak.week", "acwr.peak.month",
    ];

    private static readonly JsonSerializerOptions ReportJson = new() { Converters = { new JsonStringEnumConverter() } };

    [Theory]
    [MemberData(nameof(EvalCases.Names), MemberType = typeof(EvalCases))]
    public void Every_number_is_copied_from_the_report(string name)
    {
        var evalCase = EvalCases.Get(name);
        var reportNumbers = new HashSet<double>();
        Collect(JsonSerializer.SerializeToElement(evalCase.Report, ReportJson), reportNumbers);

        foreach (var fact in evalCase.Sheet.Facts.Where(f => f.Value.Number is not null && !AnnotationKeys.Contains(f.Key)))
        {
            Assert.True(reportNumbers.Contains(fact.Value.Number!.Value), $"{fact.Key} = {fact.Value} is not a report value.");
        }
    }

    [Fact]
    public void Demo_values_are_verbatim_and_the_peak_is_annotated_as_the_trail_race()
    {
        var report = DemoFacts.TwelveWeeks(D("2026-11-04"));
        var sheet = FactSheetBuilder.Build(report, "en");

        Assert.Equal(report.Totals.DistanceMeters, sheet.Number("totals.distance"));
        Assert.Equal(report.Comparison!.Deltas.Load.Percent, sheet.Number("comparison.delta.load.percent"));
        Assert.Equal(report.Load.Peak!.Value, sheet.Number("acwr.peak.value"));
        Assert.Equal(report.Weeks[0].AcwrAtEnd, sheet.Number("week.1.acwrAtEnd"));
        Assert.Equal("high", sheet.Text("week.1.acwrBand"));
        Assert.Equal("low", sheet.Text("week.4.acwrBand"));
        Assert.Equal(report.PaceTrend.SlopeSecondsPerKmPerWeek, sheet.Number("pace.slope"));

        // The peak (23 Oct) lies in week 10; the trail race (18 Oct) is inside its 7-day acute window.
        Assert.Equal(10, sheet.Number("acwr.peak.week"));
        Assert.True(sheet.Flag("acwr.peak.isRaceWeek"));
        Assert.Equal("trailRace", sheet.Text("acwr.peak.raceType"));
        Assert.Equal("trailRace", sheet.Text("week.9.raceType"));
        Assert.Equal("roadRace", sheet.Text("week.5.raceType"));
        Assert.Equal("halfMarathon", sheet.Text("race.1.category"));
        Assert.True(sheet.Flag("race.1.isPersonalBest"));
        Assert.Equal(0, sheet.Number("dq.estimatedLoadShare"));
        Assert.Equal("en", sheet.Language);
        Assert.Equal(MetricsVersion.Current, sheet.MetricsVersion);
        Assert.Equal(2, sheet.FactSheetVersion);
    }

    [Fact]
    public void A_peak_without_a_race_in_its_acute_window_is_not_a_race_week()
    {
        var sheet = EvalCases.Get(EvalCases.LoadSpike).Sheet;
        Assert.False(sheet.Flag("acwr.peak.isRaceWeek"));
        Assert.False(sheet.Contains("acwr.peak.raceType"));
        Assert.Equal("high", sheet.Text("acwr.peak.band"));
    }

    [Fact]
    public void A_low_week_right_before_a_race_week_is_a_taper_and_a_high_week_without_a_race_is_a_spike()
    {
        var taper = EvalCases.Get(EvalCases.TaperBeforeRace).Sheet;
        Assert.Equal("low", taper.Text("week.5.acwrBand"));
        Assert.True(taper.Flag("week.5.taper"));
        Assert.Equal("roadRace", taper.Text("week.6.raceType"));
        Assert.DoesNotContain(taper.Facts, f => f.Key.EndsWith(".loadSpike", StringComparison.Ordinal));
        Assert.Single(taper.Facts, f => f.Key.EndsWith(".taper", StringComparison.Ordinal));

        var spike = EvalCases.Get(EvalCases.LoadSpike).Sheet;
        Assert.Equal("high", spike.Text("week.4.acwrBand"));
        Assert.True(spike.Flag("week.4.loadSpike"));
        Assert.Single(spike.Facts, f => f.Key.EndsWith(".loadSpike", StringComparison.Ordinal));
        Assert.DoesNotContain(spike.Facts, f => f.Key.EndsWith(".taper", StringComparison.Ordinal));

        // Steady training: neither flag.
        foreach (var name in new[] { EvalCases.PartialPeriod, EvalCases.EstimatedRpe, EvalCases.InsufficientHistory })
        {
            Assert.DoesNotContain(EvalCases.Get(name).Sheet.Facts, f => f.Key.EndsWith(".taper", StringComparison.Ordinal) || f.Key.EndsWith(".loadSpike", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Demo_flags_the_low_week_before_the_road_race_as_a_taper_and_the_race_week_is_no_spike()
    {
        var sheet = EvalCases.Get(EvalCases.RaceWeekPeakDemo).Sheet;

        // Week 4 (low) precedes the road race of week 5; week 9 (high) holds the trail race; week 1 (high) has no race.
        Assert.True(sheet.Flag("week.4.taper"));
        Assert.False(sheet.Contains("week.9.loadSpike"));
        Assert.True(sheet.Flag("week.1.loadSpike"));
        Assert.False(sheet.Contains("week.2.taper"));
        Assert.False(sheet.Contains("week.6.taper"));
    }

    [Fact]
    public void A_low_week_before_a_planned_race_is_a_taper()
    {
        var log = new Training()
            .Weeks(D("2026-01-05"), 17)
            .Week(D("2026-05-04"), 0.45)
            .Race(D("2026-05-17"), "roadRace", RaceDistanceCategory.HalfMarathon, 21097.5, 0, RunStatus.Planned, target: 6900);
        var sheet = FactSheetBuilder.Build(log.Report(D("2026-04-06"), D("2026-05-17"), D("2026-05-10")), "en");

        Assert.Equal("low", sheet.Text("week.5.acwrBand"));
        Assert.True(sheet.Flag("week.5.taper"));
        Assert.Equal("planned", sheet.Text("race.1.status"));
    }

    [Theory]
    [InlineData("2026-01-05", "2026-04-26", true)]   // 16 ISO weeks: weekly buckets
    [InlineData("2026-01-05", "2026-05-03", false)]  // 17 ISO weeks: monthly buckets
    public void Weeks_up_to_sixteen_else_months(string from, string to, bool weekly)
    {
        var log = new Training().Weeks(D("2025-11-03"), 30);
        var report = log.Report(D(from), D(to), D("2026-06-30"));
        var sheet = FactSheetBuilder.Build(report, "en");

        Assert.Equal(weekly, sheet.Contains("week.1.start"));
        Assert.Equal(!weekly, sheet.Contains("month.1.start"));
        Assert.Equal(weekly ? report.Weeks.Count : report.Months.Count, weekly ? sheet.Number("weeks.count") : sheet.Number("months.count"));
        Assert.Equal(weekly, sheet.Contains("acwr.peak.week"));
        Assert.Equal(!weekly, sheet.Contains("acwr.peak.month"));
    }

    [Fact]
    public void A_wholly_future_period_has_no_as_of_facts()
    {
        var sheet = EvalCases.Get(EvalCases.WhollyFuture).Sheet;

        Assert.DoesNotContain(sheet.Facts, f => f.Key.StartsWith("acwr.asOf.", StringComparison.Ordinal));
        Assert.False(sheet.Contains("period.asOf"));
        Assert.Equal(0, sheet.Number("period.elapsedDays"));
        Assert.True(sheet.Flag("week.1.future"));
        Assert.True(sheet.Number("planned.runCount") > 0);
    }

    [Fact]
    public void No_comparison_means_no_comparison_keys()
    {
        var log = new Training().Weeks(D("2026-01-05"), 12);
        var sheet = FactSheetBuilder.Build(log.Report(D("2026-03-01"), D("2026-03-31"), D("2026-04-30"), ComparisonKind.None), "en");

        Assert.DoesNotContain(sheet.Facts, f => f.Key.StartsWith("comparison.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_zero_previous_value_has_an_absolute_delta_but_no_percent()
    {
        var log = new Training().Weeks(D("2026-03-02"), 4);
        var sheet = FactSheetBuilder.Build(log.Report(D("2026-03-02"), D("2026-03-29"), D("2026-04-30")), "en");

        Assert.True(sheet.Number("comparison.delta.distance.absolute") > 0);
        Assert.False(sheet.Contains("comparison.delta.distance.percent"));
    }

    [Fact]
    public void Insufficient_history_is_stated_with_its_reason()
    {
        var sheet = EvalCases.Get(EvalCases.InsufficientHistory).Sheet;

        Assert.True(sheet.Flag("acwr.asOf.unavailable"));
        Assert.True(sheet.Flag("acwr.asOf.insufficientHistory"));
        Assert.False(sheet.Contains("acwr.asOf.value"));
        Assert.False(sheet.Contains("acwr.peak.value"));
        Assert.Equal(35, sheet.Number("thresholds.acwrMinHistoryDays"));
    }

    [Fact]
    public void No_free_text_heart_rate_or_ids_reach_the_sheet()
    {
        var log = new Training().Weeks(D("2026-01-05"), 12)
            .Race(D("2026-03-29"), "roadRace", RaceDistanceCategory.TenK, 10000, 2700);
        var report = log.Report(D("2026-03-01"), D("2026-03-31"), D("2026-04-30"));
        var json = FactSheetJson.ToJson(FactSheetBuilder.Build(report, "en"));

        Assert.DoesNotContain("Canary", json, StringComparison.Ordinal);
        Assert.DoesNotContain("M30", json, StringComparison.Ordinal);
        Assert.DoesNotContain("place", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("heart", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(report.Races[0].RunId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("00000000-", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_lists_are_capped_and_keep_their_counts()
    {
        var log = new Training().Weeks(D("2026-01-05"), 12);
        var types = new[] { "recovery", "intervals", "fartlek", "hillRepeats", "progression", "racePace", "stridesDrills", "trailTraining", "treadmill", "crossTraining" };
        for (var i = 0; i < types.Length; i++)
        {
            log.Run(D("2026-03-02").AddDays(i), types[i], types[i] == "crossTraining" ? null : 5000, 1800, 4);
        }

        for (var i = 0; i < 8; i++)
        {
            log.Race(D("2026-03-01").AddDays(i * 3), "roadRace", RaceDistanceCategory.FiveK, 5000, 1300 + i);
        }

        var report = log.Report(D("2026-03-01"), D("2026-03-31"), D("2026-04-30"));
        var sheet = FactSheetBuilder.Build(report, "en");

        Assert.Equal(report.TypeDistribution.Count, sheet.Number("types.count"));
        Assert.True(sheet.Contains($"type.{FactSheetBuilder.MaxListedTypes}.code"));
        Assert.False(sheet.Contains($"type.{FactSheetBuilder.MaxListedTypes + 1}.code"));
        Assert.Equal(8, sheet.Number("races.count"));
        Assert.False(sheet.Contains($"race.{FactSheetBuilder.MaxListedRaces + 1}.date"));
        // The most recent races are listed, oldest first.
        Assert.Equal(FactValue.Of(report.Races[^1].Date), sheet.Find($"race.{FactSheetBuilder.MaxListedRaces}.date")!.Value);
    }

    [Fact]
    public void A_busy_sixteen_week_period_stays_under_the_size_cap()
    {
        var log = new Training().Weeks(D("2025-09-01"), 40);
        var types = new[] { "recovery", "intervals", "fartlek", "hillRepeats", "progression", "racePace", "stridesDrills", "trailTraining", "treadmill", "crossTraining" };
        for (var w = 0; w < 16; w++)
        {
            var monday = D("2026-01-05").AddDays(7 * w);
            log.Run(monday.AddDays(1), types[w % types.Length], types[w % types.Length] == "crossTraining" ? null : 6000, 2000, 5, RunStatus.Completed);
            log.Race(monday.AddDays(5), "roadRace", RaceDistanceCategory.TenK, 10000, 2700 - w);
            log.Run(monday.AddDays(3), "easy", 7000, 2500, 3, RunStatus.Planned);
        }

        var report = log.Report(D("2026-01-05"), D("2026-04-26"), D("2026-03-11"));
        var json = FactSheetJson.ToJson(FactSheetBuilder.Build(report, "en"));

        Assert.Equal(16, report.Weeks.Count);
        Assert.True(json.Length <= 24_000, $"{json.Length} characters.");
    }

    [Fact]
    public void Display_strings_use_english_metric_formatting()
    {
        var sheet = FactSheetBuilder.Build(DemoFacts.TwelveWeeks(D("2026-11-04")), "en");

        Assert.Equal("382.6 km", sheet.Find("totals.distance")!.Display);
        Assert.Equal("37:20:12", sheet.Find("totals.duration")!.Display);
        Assert.Equal("5:51 /km", sheet.Find("totals.averagePace")!.Display);
        Assert.Equal("+14.2 %", sheet.Find("comparison.delta.distance.percent")!.Display);
        Assert.Equal("1.79", sheet.Find("acwr.peak.value")!.Display);
        Assert.Equal("-0.48 s/km per week", sheet.Find("pace.slope")!.Display);
        Assert.Equal("17 Aug 2026", sheet.Find("period.from")!.Display);
        Assert.Equal("trail race", sheet.Find("acwr.peak.raceType")!.Display);
        Assert.Equal("half marathon", sheet.Find("race.1.category")!.Display);
        Assert.Equal("0 %", sheet.Find("dq.estimatedLoadShare")!.Display);
        Assert.Equal("5 of 5", sheet.Find("race.1.stars")!.Display);
    }

    private static void Collect(JsonElement element, HashSet<double> numbers)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                numbers.Add(element.GetDouble());
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, numbers);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, numbers);
                }

                break;
            default:
                break;
        }
    }
}
