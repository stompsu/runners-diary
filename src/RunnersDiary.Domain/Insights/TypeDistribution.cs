using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Insights;

public sealed record TypeShare(short RunTypeId, string Code, RunTypeCategory Category, int RunCount, double DistanceMeters, long DurationSeconds, double Load);

/// <summary>Completed sessions grouped by run type (ADR 0024 §6). Rest is excluded; ordered by duration descending, then catalog order.</summary>
public static class TypeDistribution
{
    public static IReadOnlyList<TypeShare> Compute(IEnumerable<RunFact> completed)
    {
        ArgumentNullException.ThrowIfNull(completed);

        var shares = new List<(TypeShare Share, short Order)>();
        foreach (var group in completed.Where(f => RunTypeCatalog.Find(f.RunTypeId) is { IsRestDay: false }).GroupBy(f => f.RunTypeId))
        {
            var type = RunTypeCatalog.Get(group.Key);
            var ordered = group.OrderBy(f => f.Date).ThenBy(f => f.Id).ToList();
            var rpeSeconds = ordered.Sum(f => type.CountsTowardLoad ? SessionLoad.Of(f, type).RpeSeconds : 0);
            shares.Add((
                new TypeShare(
                    type.Id,
                    type.Code,
                    type.Category,
                    ordered.Count,
                    Rounding.Round(ordered.Sum(f => f.DistanceMeters ?? 0), 1),
                    ordered.Sum(f => (long)(f.DurationSeconds ?? 0)),
                    Rounding.Au(rpeSeconds)),
                type.SortOrder));
        }

        return [.. shares.OrderByDescending(s => s.Share.DurationSeconds).ThenBy(s => s.Order).Select(s => s.Share)];
    }
}
