namespace RunnersDiary.Domain.Races;

/// <summary>Pure upsert planning per source (ADR 0036 §2). It never touches a race of a source that is not part of the scan.</summary>
public static class RaceCatalogueMerger
{
    /// <param name="existing">Catalogue rows (any source).</param>
    /// <param name="scanned">Facts reported by this run; every source present is merged. Raw facts are normalised here.</param>
    /// <param name="scanComplete">True only when every listing page was fetched and parsed. Otherwise nothing is counted as missed.</param>
    /// <param name="today">Today in Europe/Warsaw; races dated today or earlier are never counted as missed.</param>
    /// <param name="removeAfterMissedScans">Soft-removal threshold (default configuration: 2).</param>
    public static MergePlan Plan(
        IReadOnlyList<RaceIndexEntry> existing,
        IReadOnlyList<RaceFacts> scanned,
        bool scanComplete,
        DateOnly today,
        DateTimeOffset now,
        int removeAfterMissedScans)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(scanned);
        if (removeAfterMissedScans < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(removeAfterMissedScans), "Must be at least 1.");
        }

        _ = now; // The plan is time-free; the executor stamps the rows. Kept in the signature so callers pass one clock reading.

        var byKey = new Dictionary<(RaceSource, string), RaceIndexEntry>();
        foreach (var e in existing)
        {
            byKey[(e.Source, e.ExternalId)] = e;
        }

        var inserts = new List<RaceFacts>();
        var updates = new List<RaceUpdate>();
        var seen = new List<Guid>();
        var dateChanges = new List<RaceDateChange>();
        var restores = new List<Guid>();
        var seenKeys = new HashSet<(RaceSource, string)>();
        var sources = new HashSet<RaceSource>();

        foreach (var raw in scanned)
        {
            var facts = raw.Normalized();
            var key = (facts.Source, facts.ExternalId);
            sources.Add(facts.Source);
            if (!seenKeys.Add(key))
            {
                continue; // The first occurrence of a key in one scan wins.
            }

            if (!byKey.TryGetValue(key, out var entry))
            {
                inserts.Add(facts);
                continue;
            }

            if (entry.RemovedUtc is not null)
            {
                restores.Add(entry.Id);
            }

            if (entry.FactsHash.Span.SequenceEqual(RaceFactsHasher.Hash(facts)))
            {
                seen.Add(entry.Id);
                continue;
            }

            DateOnly? previous = null;
            if (facts.Date != entry.Date)
            {
                previous = entry.Date;
                dateChanges.Add(new RaceDateChange(entry.Id, entry.Date, facts.Date));
            }

            updates.Add(new RaceUpdate(entry.Id, facts, previous));
        }

        var missed = new List<RaceMissed>();
        var removals = new List<Guid>();
        if (scanComplete)
        {
            foreach (var e in existing)
            {
                if (!sources.Contains(e.Source) || e.RemovedUtc is not null || e.Date <= today || seenKeys.Contains((e.Source, e.ExternalId)))
                {
                    continue;
                }

                var count = (byte)Math.Min(byte.MaxValue, e.MissedScans + 1);
                var removed = count >= removeAfterMissedScans;
                missed.Add(new RaceMissed(e.Id, count, removed));
                if (removed)
                {
                    removals.Add(e.Id);
                }
            }
        }

        return new MergePlan(inserts, updates, seen, dateChanges, missed, removals, restores);
    }
}
