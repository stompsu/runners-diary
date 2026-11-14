namespace RunnersDiary.Domain.Races;

/// <summary>An existing race whose facts changed. <see cref="PreviousDate"/> is set when the date moved.</summary>
public sealed record RaceUpdate(Guid Id, RaceFacts Facts, DateOnly? PreviousDate);

public sealed record RaceDateChange(Guid Id, DateOnly OldDate, DateOnly NewDate);

public sealed record RaceMissed(Guid Id, byte MissedScans, bool Removed);

/// <summary>
/// The outcome of <see cref="RaceCatalogueMerger.Plan"/> (ADR 0036 §2). Infrastructure applies it in one transaction.
/// <see cref="Seen"/> holds unchanged races that were listed again; <see cref="Restores"/> lists races whose soft removal is lifted
/// (they also appear in <see cref="Seen"/> or <see cref="Updates"/>); <see cref="Missed"/> carries the new counter, with <c>Removed</c> when it reached the threshold.
/// </summary>
public sealed record MergePlan(
    IReadOnlyList<RaceFacts> Inserts,
    IReadOnlyList<RaceUpdate> Updates,
    IReadOnlyList<Guid> Seen,
    IReadOnlyList<RaceDateChange> DateChanges,
    IReadOnlyList<RaceMissed> Missed,
    IReadOnlyList<Guid> Removals,
    IReadOnlyList<Guid> Restores)
{
    public static MergePlan Empty { get; } = new([], [], [], [], [], [], []);

    public bool IsEmpty => Inserts.Count + Updates.Count + Seen.Count + Missed.Count + Removals.Count + Restores.Count == 0;
}
