using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Insights;

public sealed record PaceWeek(DateOnly Start, int RunCount, double DistanceMeters, long DurationSeconds, double PaceSecondsPerKm, double? AvgHeartRate);

public sealed record PaceTrend(IReadOnlyList<string> RunTypeCodes, double? SlopeSecondsPerKmPerWeek, int ExcludedRunCount, IReadOnlyList<PaceWeek> Weeks)
{
    public const string EasyCode = "easy";
    public const double MinDistanceMeters = 1000;
    public const double MinPaceSecondsPerKm = 150;
    public const double MaxPaceSecondsPerKm = 900;
    public const int MinPointsForSlope = 4;

    /// <summary>Easy-run pace per ISO week and its OLS slope (ADR 0024 §7). Input: completed sessions inside the period.</summary>
    public static PaceTrend Compute(IEnumerable<RunFact> completed)
    {
        ArgumentNullException.ThrowIfNull(completed);

        var excluded = 0;
        var used = new List<RunFact>();
        foreach (var fact in completed.Where(f => RunTypeCatalog.Find(f.RunTypeId)?.Code == EasyCode).OrderBy(f => f.Date).ThenBy(f => f.Id))
        {
            if (Qualifies(fact))
            {
                used.Add(fact);
            }
            else
            {
                excluded++;
            }
        }

        var weeks = new List<PaceWeek>();
        var paces = new List<(DateOnly Monday, double Pace)>();
        foreach (var group in used.GroupBy(f => IsoWeek.MondayOf(f.Date)).OrderBy(g => g.Key))
        {
            var distance = group.Sum(f => f.DistanceMeters!.Value);
            var duration = group.Sum(f => (long)f.DurationSeconds!.Value);
            var pace = duration / distance * 1000;
            var withHr = group.Where(f => f.AvgHeartRate is > 0).ToList();
            double? hr = null;
            if (withHr.Count > 0)
            {
                var weight = withHr.Sum(f => (long)f.DurationSeconds!.Value);
                hr = Rounding.Round(withHr.Sum(f => (double)f.AvgHeartRate!.Value * f.DurationSeconds!.Value) / weight, 1);
            }

            paces.Add((group.Key, pace));
            weeks.Add(new PaceWeek(group.Key, group.Count(), Rounding.Round(distance, 1), duration, Rounding.Round(pace, 1), hr));
        }

        return new PaceTrend([EasyCode], Slope(paces), excluded, weeks);
    }

    private static bool Qualifies(RunFact fact)
    {
        if (fact.Surface == Surface.Trail
            || fact.DistanceMeters is not { } distance || distance < MinDistanceMeters
            || fact.DurationSeconds is not { } duration || duration <= 0)
        {
            return false;
        }

        var pace = duration / distance * 1000;
        return pace is >= MinPaceSecondsPerKm and <= MaxPaceSecondsPerKm;
    }

    /// <summary>Ordinary least squares of pace against the week index (weeks since the first point); null with fewer than 4 points.</summary>
    internal static double? Slope(IReadOnlyList<(DateOnly Monday, double Pace)> points)
    {
        if (points.Count < MinPointsForSlope)
        {
            return null;
        }

        var first = points[0].Monday.DayNumber;
        var xs = points.Select(p => (p.Monday.DayNumber - first) / 7.0).ToList();
        var ys = points.Select(p => p.Pace).ToList();
        var meanX = xs.Average();
        var meanY = ys.Average();
        var sxx = xs.Sum(x => (x - meanX) * (x - meanX));
        var sxy = xs.Zip(ys, (x, y) => (x - meanX) * (y - meanY)).Sum();
        return sxx == 0 ? null : Rounding.Round(sxy / sxx, 2);
    }
}
