namespace RunnersDiary.Domain.Races;

/// <summary>What the merger needs to know about an existing catalogue row (identity, date, hash, scan counters).</summary>
public sealed record RaceIndexEntry(
    Guid Id,
    RaceSource Source,
    string ExternalId,
    DateOnly Date,
    ReadOnlyMemory<byte> FactsHash,
    byte MissedScans,
    DateTime? RemovedUtc);
