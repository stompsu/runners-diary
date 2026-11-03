namespace RunnersDiary.Domain.Runs;

/// <summary>Where a run starts or takes place: a point plus an optional display name.</summary>
public sealed record RunLocation(GeoPoint Point, string? Name = null);
