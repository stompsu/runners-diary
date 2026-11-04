using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Insights;

/// <summary>Totals of completed sessions over a period (ADR 0024 §5). Running totals cover running-distance types; load also covers cross-training.</summary>
public sealed record PeriodTotals(
    int RunCount,
    double DistanceMeters,
    long DurationSeconds,
    double ElevationGainMeters,
    double? AveragePaceSecondsPerKm,
    double? LongestRunMeters,
    double Load,
    int LoadSessionCount,
    int ActiveDays,
    int RestDays)
{
    public static PeriodTotals Empty { get; } = Compute([]);

    /// <summary>Totals over facts that are already restricted to completed sessions inside the period.</summary>
    public static PeriodTotals Compute(IEnumerable<RunFact> completed)
    {
        ArgumentNullException.ThrowIfNull(completed);

        var runCount = 0;
        double distance = 0;
        long duration = 0;
        double elevation = 0;
        double paceDistance = 0;
        long paceDuration = 0;
        double? longest = null;
        long rpeSeconds = 0;
        var loadSessions = 0;
        var activeDates = new HashSet<DateOnly>();
        var restDates = new HashSet<DateOnly>();

        foreach (var fact in completed.OrderBy(f => f.Date).ThenBy(f => f.Id))
        {
            if (RunTypeCatalog.Find(fact.RunTypeId) is not { } type)
            {
                continue;
            }

            if (type.IsRestDay)
            {
                restDates.Add(fact.Date);
                continue;
            }

            activeDates.Add(fact.Date);

            if (type.CountsTowardLoad)
            {
                var load = SessionLoad.Of(fact, type);
                rpeSeconds += load.RpeSeconds;
                if (!load.MissingDuration)
                {
                    loadSessions++;
                }
            }

            if (!type.CountsTowardRunningDistance)
            {
                continue;
            }

            runCount++;
            distance += fact.DistanceMeters ?? 0;
            duration += fact.DurationSeconds ?? 0;
            elevation += fact.ElevationGainMeters ?? 0;
            if (fact.DistanceMeters is > 0 and var d)
            {
                longest = longest is { } l && l >= d ? l : d;
                if (fact.DurationSeconds is > 0 and var s)
                {
                    paceDistance += d;
                    paceDuration += s;
                }
            }
        }

        return new PeriodTotals(
            runCount,
            Rounding.Round(distance, 1),
            duration,
            Rounding.Round(elevation, 1),
            paceDistance > 0 ? Rounding.Round(paceDuration / paceDistance * 1000, 1) : null,
            longest is { } max ? Rounding.Round(max, 1) : null,
            Rounding.Au(rpeSeconds),
            loadSessions,
            activeDates.Count,
            restDates.Count);
    }

    /// <summary>Bucket totals (the calendar's shape) for running-distance sessions of the given status.</summary>
    internal static Totals Bucket(IEnumerable<RunFact> facts, RunStatus status)
    {
        var count = 0;
        double distance = 0;
        long duration = 0;
        double elevation = 0;
        foreach (var fact in facts.Where(f => f.Status == status).OrderBy(f => f.Date).ThenBy(f => f.Id))
        {
            if (RunTypeCatalog.Find(fact.RunTypeId) is not { CountsTowardRunningDistance: true })
            {
                continue;
            }

            count++;
            distance += fact.DistanceMeters ?? 0;
            duration += fact.DurationSeconds ?? 0;
            elevation += fact.ElevationGainMeters ?? 0;
        }

        return new Totals(count, Rounding.Round(distance, 1), duration, Rounding.Round(elevation, 1));
    }
}
