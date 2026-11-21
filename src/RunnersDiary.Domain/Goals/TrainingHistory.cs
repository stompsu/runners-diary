using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Goals;

/// <summary>One complete ISO week of running (Race and Training categories, completed sessions only).</summary>
public sealed record HistoryWeek(DateOnly Start, double DistanceMeters, long DurationSeconds, int Sessions, double LongestRunMeters, int HardSessions);

/// <summary>
/// What the goal estimate, the plan envelope and the plan fact sheet read from the caller's recent running (ADR 0039 §4, ADR 0040 §2-§3):
/// ten complete ISO weeks before the current one, oldest first. "Running" is the calendar <c>Totals</c> rule: Race and Training categories.
/// Pure: built from <see cref="RunFact"/> projections and an explicit "today".
/// </summary>
public sealed record TrainingHistory(
    DateOnly Today,
    IReadOnlyList<HistoryWeek> Weeks,
    double MeanWeeklyDistanceMeters,
    double LongestRunMeters,
    double SessionsPerWeek,
    double? MeanPaceSecondsPerKm,
    int RunningSessionsLast84Days,
    IReadOnlyList<DayOfWeek> UsualWeekdays,
    double? RecentEasyPaceSecondsPerKm = null)
{
    public const int MinEasyRuns = 3;
    public const int WeekCount = 10;
    public const int BaseWeeks = 4;
    public const int RecentDays = 84;
    public const double UsualWeekdayShare = 0.4;

    private static readonly HashSet<string> HardTypeCodes = new(StringComparer.Ordinal) { "tempo", "intervals", "racePace", "hillRepeats", "fartlek" };

    public static TrainingHistory From(IReadOnlyList<RunFact> facts, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var running = facts
            .Where(f => f.Status == RunStatus.Completed && f.Date <= today && RunTypeCatalog.Find(f.RunTypeId) is { CountsTowardRunningDistance: true })
            .ToList();

        var currentMonday = IsoWeek.MondayOf(today);
        var weeks = new List<HistoryWeek>(WeekCount);
        for (var i = WeekCount; i >= 1; i--)
        {
            var start = currentMonday.AddDays(-7 * i);
            var inWeek = running.Where(f => f.Date >= start && f.Date <= start.AddDays(6)).ToList();
            weeks.Add(new HistoryWeek(
                start,
                Rounding.Round(inWeek.Sum(f => f.DistanceMeters ?? 0), 1),
                inWeek.Sum(f => (long)(f.DurationSeconds ?? 0)),
                inWeek.Count,
                inWeek.Count == 0 ? 0 : Rounding.Round(inWeek.Max(f => f.DistanceMeters ?? 0), 1),
                inWeek.Count(IsHard)));
        }

        var recent = weeks.Skip(WeekCount - BaseWeeks).ToList();
        var recentRuns = running.Where(f => f.Date >= currentMonday.AddDays(-7 * BaseWeeks) && f.Date < currentMonday).ToList();
        var paceDistance = recentRuns.Where(f => f.DistanceMeters is > 0 && f.DurationSeconds is > 0).Sum(f => f.DistanceMeters!.Value);
        var paceDuration = recentRuns.Where(f => f.DistanceMeters is > 0 && f.DurationSeconds is > 0).Sum(f => (long)f.DurationSeconds!.Value);

        return new TrainingHistory(
            today,
            weeks,
            Rounding.Round(recent.Sum(w => w.DistanceMeters) / BaseWeeks, 1),
            recent.Max(w => w.LongestRunMeters),
            Rounding.Round(weeks.Sum(w => w.Sessions) / (double)WeekCount, 2),
            paceDistance > 0 ? Rounding.Round(paceDuration / paceDistance * 1000, 1) : null,
            running.Count(f => f.Date >= today.AddDays(-(RecentDays - 1))),
            UsualWeekdaysOf(running, weeks),
            EasyPace(recentRuns, paceDistance > 0 ? Rounding.Round(paceDuration / paceDistance * 1000, 1) : null));
    }

    /// <summary>
    /// The median real pace of easy, recovery and long runs of the base window (ADR 0040 amendment 2026-11-21): at least 3 runs of 1,000 m or more with a
    /// pace of 150-900 s/km, trail and treadmill excluded; else the mean pace, else null.
    /// </summary>
    private static double? EasyPace(List<RunFact> recentRuns, double? meanPace)
    {
        var paces = recentRuns
            .Where(f => RunTypeCatalog.Find(f.RunTypeId)?.Code is "easy" or "recovery" or "long"
                && f.Surface is not (Surface.Trail or Surface.Treadmill)
                && f.DistanceMeters is >= 1_000 && f.DurationSeconds is > 0)
            .Select(f => f.DurationSeconds!.Value / (f.DistanceMeters!.Value / 1000))
            .Where(p => p is >= RaceTimePredictor.MinEffortPaceSecondsPerKm and <= RaceTimePredictor.MaxEffortPaceSecondsPerKm)
            .ToList();
        return paces.Count >= MinEasyRuns ? Rounding.Round(RaceTimePredictor.Median(paces), 1) : meanPace;
    }

    /// <summary>A completed run of a tempo-like type, a race, or one with RPE 7 or more (ADR 0040 §3).</summary>
    internal static bool IsHard(RunFact fact)
    {
        var type = RunTypeCatalog.Find(fact.RunTypeId);
        return type is not null && (type.Category == RunTypeCategory.Race || HardTypeCodes.Contains(type.Code)) || fact.Rpe >= 7;
    }

    private static List<DayOfWeek> UsualWeekdaysOf(List<RunFact> running, List<HistoryWeek> weeks)
    {
        var usual = new List<DayOfWeek>();
        // Monday-first, like the calendar.
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
        {
            var weeksWithDay = weeks.Count(w => running.Any(f => f.Date.DayOfWeek == day && f.Date >= w.Start && f.Date <= w.Start.AddDays(6)));
            if (weeksWithDay >= UsualWeekdayShare * WeekCount)
            {
                usual.Add(day);
            }
        }

        return usual;
    }
}
