using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Domain.Runs;
using static RunnersDiary.Ai.Tests.Support.Training;

namespace RunnersDiary.Ai.Tests.Evals;

/// <summary>A key requirement: at least one text in <paramref name="Section"/> (or anywhere for null) cites one of <paramref name="AnyOf"/>.</summary>
public sealed record KeyRequirement(string? Section, IReadOnlyList<string> AnyOf);

/// <summary>Keys that must not be cited in <paramref name="Section"/> (e.g. the taper week is not a watch-out).</summary>
public sealed record KeyProhibition(string Section, IReadOnlyList<string> Keys);

/// <summary>
/// One evaluation case (ADR 0026 §7): a report built with <see cref="InsightsReportBuilder"/> from a small fact list, so it always
/// follows metrics v1. Template cases never reach the model; the others have a committed good draft and adversarial drafts.
/// </summary>
public sealed record EvalCase(
    string Name,
    InsightsReport Report,
    SummaryTemplateKind? ExpectedTemplate,
    IReadOnlyList<KeyRequirement> Required,
    IReadOnlyList<KeyProhibition> Forbidden)
{
    public FactSheet Sheet { get; } = FactSheetBuilder.Build(Report, "en");

    public override string ToString() => Name;
}

public static class EvalCases
{
    public const string LoadSpike = "load-spike";
    public const string TaperBeforeRace = "taper-before-race";
    public const string EmptyMonth = "empty-month";
    public const string InsufficientHistory = "insufficient-history";
    public const string EstimatedRpe = "estimated-rpe";
    public const string PartialPeriod = "partial-period";
    public const string RaceWeekPeakDemo = "race-week-peak-demo";
    public const string WhollyFuture = "wholly-future";

    private static readonly DateOnly HistoryStart = D("2026-01-05");

    public static IReadOnlyList<EvalCase> All { get; } =
    [
        BuildLoadSpike(),
        BuildTaperBeforeRace(),
        BuildEmptyMonth(),
        BuildInsufficientHistory(),
        BuildEstimatedRpe(),
        BuildPartialPeriod(),
        BuildRaceWeekPeakDemo(),
        BuildWhollyFuture(),
    ];

    public static EvalCase Get(string name) => All.Single(c => c.Name == name);

    public static TheoryData<string> Names() => [.. All.Select(c => c.Name)];

    public static TheoryData<string> ModelNames() => [.. All.Where(c => c.ExpectedTemplate is null).Select(c => c.Name)];

    /// <summary>March 2026: steady weeks, then the week of 16 March at 1.8× (ACWR high), no race.</summary>
    private static EvalCase BuildLoadSpike()
    {
        var spikeMonday = D("2026-03-16");
        var log = new Training()
            .Weeks(HistoryStart, 10)
            .Week(spikeMonday, 1.8)
            .Weeks(D("2026-03-23"), 2);
        var report = log.Report(D("2026-03-01"), D("2026-03-31"), D("2026-04-15"));
        var spikeWeek = WeekIndex(report, spikeMonday);
        return new EvalCase(
            LoadSpike,
            report,
            null,
            [new KeyRequirement("watchOuts", ["acwr.peak.value", $"week.{spikeWeek}.acwrAtEnd", $"week.{spikeWeek}.load"])],
            []);
    }

    /// <summary>Six weeks to a half marathon on 17 May 2026: a 0.45× taper week before the race week.</summary>
    private static EvalCase BuildTaperBeforeRace()
    {
        var taperMonday = D("2026-05-04");
        var raceMonday = D("2026-05-11");
        var log = new Training()
            .Weeks(HistoryStart, 17)
            .Week(taperMonday, 0.45)
            .Run(raceMonday, "easy", 6000, 2160, 3)
            .Run(raceMonday.AddDays(2), "easy", 6000, 2160, 3)
            .Race(raceMonday.AddDays(6), "roadRace", RaceDistanceCategory.HalfMarathon, 21097.5, 6600, target: 6900);
        var report = log.Report(D("2026-04-06"), D("2026-05-17"), D("2026-06-01"));
        var taperWeek = WeekIndex(report, taperMonday);
        return new EvalCase(
            TaperBeforeRace,
            report,
            null,
            [new KeyRequirement(null, ["race.1.date", "race.1.resultTime", "race.1.category"])],
            [new KeyProhibition("watchOuts", [$"week.{taperWeek}.acwrAtEnd", $"week.{taperWeek}.acwrBand", $"week.{taperWeek}.load"])]);
    }

    /// <summary>March 2026 without a single session (history before it): the empty template.</summary>
    private static EvalCase BuildEmptyMonth()
    {
        var log = new Training().Weeks(HistoryStart, 7);
        return new EvalCase(EmptyMonth, log.Report(D("2026-03-01"), D("2026-03-31"), D("2026-04-10")), SummaryTemplateKind.Empty, [], []);
    }

    /// <summary>A diary that started on 2 March 2026: two weeks later the ACWR still needs history.</summary>
    private static EvalCase BuildInsufficientHistory()
    {
        var log = new Training().Weeks(D("2026-03-02"), 2);
        return new EvalCase(
            InsufficientHistory,
            log.Report(D("2026-03-02"), D("2026-03-15"), D("2026-03-20")),
            null,
            [new KeyRequirement("dataNotes", ["acwr.asOf.unavailable", "acwr.asOf.insufficientHistory", "thresholds.acwrMinHistoryDays", "load.historyStart"])],
            []);
    }

    /// <summary>April 2026 with RPE recorded only on tempo runs: most of the load is estimated.</summary>
    private static EvalCase BuildEstimatedRpe()
    {
        var log = new Training().Weeks(HistoryStart, 12).Weeks(D("2026-03-30"), 5, recordRpe: false);
        return new EvalCase(
            EstimatedRpe,
            log.Report(D("2026-04-01"), D("2026-04-30"), D("2026-05-10")),
            null,
            [new KeyRequirement(null, ["dq.estimatedLoadShare", "dq.sessionsWithEstimatedRpe"])],
            []);
    }

    /// <summary>April 2026 viewed on 16 April: a partial period ("so far").</summary>
    private static EvalCase BuildPartialPeriod()
    {
        var log = new Training().Weeks(HistoryStart, 15);
        return new EvalCase(
            PartialPeriod,
            log.Report(D("2026-04-01"), D("2026-04-30"), D("2026-04-16")),
            null,
            [new KeyRequirement(null, ["period.asOf", "period.elapsedDays", "period.isPartial", "acwr.asOf.value", "acwr.asOf.band"])],
            []);
    }

    /// <summary>The demo's default view on 4 Nov 2026: the ACWR peak is the trail race (ADR 0024 amendment).</summary>
    private static EvalCase BuildRaceWeekPeakDemo() =>
        new(
            RaceWeekPeakDemo,
            DemoFacts.TwelveWeeks(D("2026-11-04")),
            null,
            [new KeyRequirement(null, ["acwr.peak.raceType", "race.2.date", "race.2.type"])],
            []);

    /// <summary>June 2026 viewed on 20 May with planned sessions only: the plannedOnly template.</summary>
    private static EvalCase BuildWhollyFuture()
    {
        var log = new Training().Weeks(HistoryStart, 19);
        for (var day = D("2026-06-02"); day.Month == 6; day = day.AddDays(3))
        {
            log.Run(day, "easy", 8000, 2880, null, RunStatus.Planned);
        }

        return new EvalCase(WhollyFuture, log.Report(D("2026-06-01"), D("2026-06-30"), D("2026-05-20")), SummaryTemplateKind.PlannedOnly, [], []);
    }

    private static int WeekIndex(InsightsReport report, DateOnly monday)
    {
        for (var i = 0; i < report.Weeks.Count; i++)
        {
            if (report.Weeks[i].Start == monday)
            {
                return i + 1;
            }
        }

        throw new InvalidOperationException($"No week bucket starts on {monday}.");
    }
}
