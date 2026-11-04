namespace RunnersDiary.Infrastructure.Maps;

/// <summary>
/// Provider geocode calls spent on one UTC day, across all replicas. A system table without the Owner filter, like the AI usage
/// ledger: it holds no user data and is only read or written by <see cref="GeocodeBudget"/> (and the cleanup's purge through it).
/// </summary>
public sealed class GeocodeUsage
{
    public DateOnly Day { get; set; }

    public int Count { get; set; }
}
