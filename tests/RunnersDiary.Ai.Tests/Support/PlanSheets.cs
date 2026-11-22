using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Ai.Tests.Support;

/// <summary>
/// Builds plan fact sheets from in-memory facts exactly as <c>TrainingPlanService</c> does from the database (history of the last 112 days,
/// commitments as <c>TrainingHistoryReader.ReadCommitmentsAsync</c> projects them, the M5 report over the last 365 days), plus synthetic logs.
/// </summary>
internal static class PlanSheets
{
    /// <summary>A Monday.</summary>
    public static readonly DateOnly Today = new(2026, 11, 16);

    public static PlanFactSheet Build(
        GoalSpec goal,
        DateOnly today,
        IReadOnlyList<RunFact> facts,
        IReadOnlyList<RaceFact> races,
        IReadOnlyList<DateOnly>? otherGoalDates = null,
        IReadOnlyList<DismissedHint>? dismissed = null,
        IReadOnlySet<Guid>? fromThisGoal = null)
    {
        var recent = facts.Where(f => f.Status == RunStatus.Completed && f.Date >= today.AddDays(-(GoalEstimator.TrainingLookbackDays - 1)) && f.Date <= today).ToList();
        var history = TrainingHistory.From(recent, today);
        var estimate = GoalEstimator.Estimate(goal, today, recent, races);
        var reference = PlanEnvelopeBuilder.ReferencePace(goal, estimate.Prediction, history);
        var horizon = PlanEnvelopeBuilder.Build(goal, today, history, [], reference);
        var commitments = Commitments(facts, horizon.PlanStart, horizon.PlanEnd, goal.TargetDate, otherGoalDates ?? [], fromThisGoal);
        var envelope = PlanEnvelopeBuilder.Build(goal, today, history, commitments, reference);
        var report = InsightsReportBuilder.Build(new InsightsPeriod(today.AddDays(-364), today), ComparisonKind.None, today, recent, recent, races, null);
        return PlanFactSheetBuilder.Build(goal, estimate, today, report, history, commitments, dismissed ?? [], envelope, "en");
    }

    /// <summary>Mirrors <c>TrainingHistoryReader.ReadCommitmentsAsync</c>.</summary>
    public static List<CommitmentFact> Commitments(
        IReadOnlyList<RunFact> facts, DateOnly planStart, DateOnly planEnd, DateOnly target, IReadOnlyList<DateOnly> otherGoalDates, IReadOnlySet<Guid>? fromThisGoal = null)
    {
        var commitments = new List<CommitmentFact>();
        foreach (var f in facts.Where(f => f.Date >= planStart && f.Date <= target).OrderBy(f => f.Date))
        {
            var isRace = RunTypeCatalog.Find(f.RunTypeId)?.Category == RunTypeCategory.Race;
            var fromGoal = fromThisGoal?.Contains(f.Id) == true;
            if (f.Date <= planEnd)
            {
                commitments.Add(new CommitmentFact(f.Date, f.RunTypeId, f.DistanceMeters, f.DurationSeconds, isRace ? CommitmentKind.Race : CommitmentKind.Planned, fromGoal));
            }
            else if (isRace && f.Status == RunStatus.Planned)
            {
                commitments.Add(new CommitmentFact(f.Date, f.RunTypeId, f.DistanceMeters, f.DurationSeconds, CommitmentKind.Race, fromGoal));
            }
        }

        commitments.AddRange(otherGoalDates.Where(d => d >= planStart && d <= planEnd).Distinct()
            .Select(d => new CommitmentFact(d, null, null, null, CommitmentKind.OtherGoal, false)));
        return commitments;
    }

    /// <summary>
    /// Twelve complete weeks of a recreational runner before <paramref name="today"/>: Tuesday easy, Thursday tempo, Sunday long (about
    /// 30 km a week), and a completed 10K race (48:00) five weeks ago on a Saturday.
    /// </summary>
    public static (List<RunFact> Facts, List<RaceFact> Races) RecreationalLog(DateOnly today, double scale = 1.0)
    {
        var facts = new List<RunFact>();
        var monday = IsoWeek.MondayOf(today);
        var id = 0;
        RunFact Run(DateOnly date, short type, double meters, int secondsPerKm, int rpe, RunStatus status = RunStatus.Completed) =>
            new(new Guid(++id, 0, 0, new byte[8]), date, status, type, meters, (int)(meters / 1000 * secondsPerKm), 40, status == RunStatus.Completed ? rpe : null, Surface.Road, null);

        for (var w = 12; w >= 1; w--)
        {
            var start = monday.AddDays(-7 * w);
            facts.Add(Run(start.AddDays(1), 20, Math.Round(8_000 * scale), 345, 4));
            facts.Add(Run(start.AddDays(3), 23, Math.Round(8_000 * scale), 290, 7));
            facts.Add(Run(start.AddDays(6), 22, Math.Round(14_000 * scale), 360, 5));
        }

        var raceDay = monday.AddDays(-7 * 5 + 5);
        var race = Run(raceDay, 1, 10_000, 288, 9);
        facts.Add(race);
        var races = new List<RaceFact>
        {
            new(race.Id, raceDay, RunStatus.Completed, 1, "Test 10K", RaceDistanceCategory.TenK, 10_000, 10_000, 2_880, 2_880, null, null, null, null, null),
        };
        return (facts, races);
    }

    /// <summary>
    /// The M9.1 user scenario (plan §3.1, §5): ten flat easy runs of 8 km at 6:00/km in the four complete weeks before <paramref name="today"/>
    /// (Tuesday, Thursday and Saturday in the first and third week, Tuesday and Saturday in the others), RPE 4; no race, no hard run.
    /// </summary>
    public static List<RunFact> EasyOnlyLog(DateOnly today)
    {
        var monday = IsoWeek.MondayOf(today);
        var facts = new List<RunFact>();
        var id = 0;
        for (var w = 4; w >= 1; w--)
        {
            var start = monday.AddDays(-7 * w);
            var days = w % 2 == 0 ? new[] { 1, 3, 5 } : [1, 5];
            foreach (var day in days)
            {
                facts.Add(new RunFact(new Guid(500 + ++id, 0, 0, new byte[8]), start.AddDays(day), RunStatus.Completed, 20, 8_000, 2_880, 0, 4, Surface.Road, null));
            }
        }

        return facts;
    }

    /// <summary>A planned run (a commitment) on <paramref name="date"/>.</summary>
    public static RunFact Planned(DateOnly date, short type = 20, double? meters = 8_000, int index = 0) =>
        new(new Guid(900 + index, 0, 0, new byte[8]), date, RunStatus.Planned, type, meters, null, null, null, Surface.Road, null);
}
