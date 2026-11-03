using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Calendar;

/// <summary>The fields of a run that weekly totals need.</summary>
public sealed record RunSummary(
    DateOnly Date,
    RunStatus Status,
    short RunTypeId,
    double? DistanceMeters,
    int? DurationSeconds,
    double? ElevationGainMeters);

/// <remarks>Durations are summed as <see cref="long"/>: a week of valid runs fits <see cref="int"/> in practice, but the sum must never wrap.</remarks>
public sealed record Totals(int RunCount, double DistanceMeters, long DurationSeconds, double ElevationGainMeters)
{
    public static Totals Empty { get; } = new(0, 0, 0, 0);
}

public sealed record WeekTotals(IsoWeek Week, DateOnly Start, DateOnly End, Totals Completed, Totals Planned);

/// <summary>Weekly totals over running-distance run types only (cross-training and rest are excluded; load comes in M5).</summary>
public static class WeeklyTotals
{
    public static IReadOnlyList<WeekTotals> Compute(CalendarGrid grid, IEnumerable<RunSummary> runs)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(runs);

        var byMonday = runs
            .Where(r => r.Date >= grid.Start && r.Date <= grid.End && RunTypeCatalog.Find(r.RunTypeId) is { CountsTowardRunningDistance: true })
            .GroupBy(r => IsoWeek.MondayOf(r.Date))
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<WeekTotals>(grid.WeekCount);
        for (var monday = grid.Start; monday <= grid.End; monday = monday.AddDays(7))
        {
            var inWeek = byMonday.GetValueOrDefault(monday) ?? [];
            result.Add(new WeekTotals(
                IsoWeek.Of(monday),
                monday,
                monday.AddDays(6),
                Sum(inWeek, RunStatus.Completed),
                Sum(inWeek, RunStatus.Planned)));
        }

        return result;
    }

    private static Totals Sum(List<RunSummary> runs, RunStatus status)
    {
        var matching = runs.Where(r => r.Status == status).ToList();
        return matching.Count == 0
            ? Totals.Empty
            : new Totals(
                matching.Count,
                matching.Sum(r => r.DistanceMeters ?? 0),
                matching.Sum(r => (long)(r.DurationSeconds ?? 0)),
                matching.Sum(r => r.ElevationGainMeters ?? 0));
    }
}
