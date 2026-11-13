using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Races;

/// <summary>
/// A catalogue race: shared reference data written only by the import job (ADR 0036 §1). It is deliberately not <see cref="IUserOwned"/>:
/// no user id, no Owner filter. Facts are stored only through <see cref="RaceFacts"/>.
/// </summary>
public sealed class Race
{
    private readonly List<double> _distances = [];

    private Race()
    {
    }

    public Guid Id { get; private set; }

    public RaceSource Source { get; private set; }

    public string ExternalId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public DateOnly Date { get; private set; }

    public TimeOnly? StartTime { get; private set; }

    /// <summary>IANA zone derived from the country (<see cref="CountryTimeZones"/>); null when unknown.</summary>
    public string? TimeZoneId { get; private set; }

    public string? LocationName { get; private set; }

    public GeoPoint? Point { get; private set; }

    public string CountryCode { get; private set; } = string.Empty;

    public string? RegionCode { get; private set; }

    public RaceKind Kind { get; private set; }

    public Surface? Surface { get; private set; }

    /// <summary>Ascending, deduplicated, at most 12.</summary>
    public IReadOnlyList<double> DistancesMeters => _distances;

    public double? ElevationGainMeters { get; private set; }

    public string? SignupUrl { get; private set; }

    public string? EventUrl { get; private set; }

    public string? SourceUrl { get; private set; }

    public decimal? PriceAmount { get; private set; }

    public string? PriceCurrency { get; private set; }

    public DateOnly? SignupDeadline { get; private set; }

    public string? OrganizerName { get; private set; }

    /// <summary>SHA-256 of the canonical facts (<see cref="RaceFactsHasher"/>).</summary>
#pragma warning disable CA1819 // Justification: binary(32) column; EF maps byte[] and the hash is compared, never mutated.
    public byte[] FactsHash { get; private set; } = [];
#pragma warning restore CA1819

    public DateTime ImportedUtc { get; private set; }

    public DateTime LastSeenUtc { get; private set; }

    public DateTime UpdatedUtc { get; private set; }

    /// <summary>The date before the last date change.</summary>
    public DateOnly? PreviousDate { get; private set; }

    public DateTime? DateChangedUtc { get; private set; }

    /// <summary>Complete scans in which this future race was absent; reset when seen.</summary>
    public byte MissedScans { get; private set; }

    public DateTime? RemovedUtc { get; private set; }

    public Guid? DuplicateOfRaceId { get; private set; }

    public string? DetailETag { get; private set; }

    public string? DetailLastModified { get; private set; }

    public DateTime? DetailFetchedUtc { get; private set; }

    public bool IsRemoved => RemovedUtc is not null;

    public static Race Create(RaceFacts facts, DateTimeOffset now)
    {
        var normalized = Prepare(facts);
        var utc = TextFolding.TruncateUtc(now);
        var race = new Race
        {
            Id = Guid.CreateVersion7(),
            Source = normalized.Source,
            ExternalId = normalized.ExternalId,
            ImportedUtc = utc,
            LastSeenUtc = utc,
            UpdatedUtc = utc,
        };
        race.Assign(normalized);
        return race;
    }

    /// <summary>
    /// Replaces the facts (same source and external id). A changed date goes through <see cref="ChangeDate"/>, so <see cref="PreviousDate"/> is set.
    /// The race was seen in the scan that reported the facts, so this also clears the missed-scan state.
    /// </summary>
    public void ApplyFacts(RaceFacts facts, DateTimeOffset now)
    {
        var normalized = Prepare(facts);
        if (normalized.Source != Source || !string.Equals(normalized.ExternalId, ExternalId, StringComparison.Ordinal))
        {
            throw new DomainRuleException("Facts of a different race cannot be applied.");
        }

        ChangeDate(normalized.Date, now);
        Assign(normalized);
        UpdatedUtc = TextFolding.TruncateUtc(now);
        MarkSeen(now);
    }

    /// <summary>The race was reported by a scan: refresh <see cref="LastSeenUtc"/>, reset the missed counter and restore a removed race.</summary>
    public void MarkSeen(DateTimeOffset now)
    {
        LastSeenUtc = TextFolding.TruncateUtc(now);
        MissedScans = 0;
        RemovedUtc = null;
    }

    /// <summary>A complete scan did not list this future race. Returns true when the race is (now) soft-removed.</summary>
    public bool MarkMissed(DateTimeOffset now, int removeAfterMissedScans)
    {
        if (removeAfterMissedScans < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(removeAfterMissedScans), "Must be at least 1.");
        }

        if (MissedScans < byte.MaxValue)
        {
            MissedScans++;
        }

        if (MissedScans >= removeAfterMissedScans)
        {
            Remove(now);
        }

        return IsRemoved;
    }

    public void Remove(DateTimeOffset now) => RemovedUtc ??= TextFolding.TruncateUtc(now);

    public void Restore() => RemovedUtc = null;

    /// <summary>Moves the race to another date and records the previous one; a no-op when the date is unchanged.</summary>
    public void ChangeDate(DateOnly newDate, DateTimeOffset now)
    {
        if (newDate == Date)
        {
            return;
        }

        PreviousDate = Date;
        DateChangedUtc = TextFolding.TruncateUtc(now);
        Date = newDate;
    }

    /// <summary>Marks this race as a duplicate of <paramref name="canonicalId"/> (ADR 0036 §4); null clears the mark.</summary>
    public void SetDuplicateOf(Guid? canonicalId)
    {
        if (canonicalId == Id)
        {
            throw new DomainRuleException("A race cannot be a duplicate of itself.");
        }

        DuplicateOfRaceId = canonicalId;
    }

    /// <summary>Stores the conditional-request validators of the last detail fetch.</summary>
    public void RecordDetailFetch(string? eTag, string? lastModified, DateTimeOffset now)
    {
        DetailETag = Truncate(eTag, 200);
        DetailLastModified = Truncate(lastModified, 64);
        DetailFetchedUtc = TextFolding.TruncateUtc(now);
    }

    public RaceSnapshot ToSnapshot() => new(
        Id, Source, Name, Date, StartTime, TimeZoneId, LocationName, Point, CountryCode, RegionCode, Kind, Surface,
        _distances.ToArray(), ElevationGainMeters, ImportedUtc, RemovedUtc, DuplicateOfRaceId);

    public RaceIndexEntry ToIndexEntry() => new(Id, Source, ExternalId, Date, FactsHash, MissedScans, RemovedUtc);

    private static RaceFacts Prepare(RaceFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!RaceFacts.TryNormalize(facts, out var normalized, out var errors))
        {
            throw new DomainRuleException(errors);
        }

        return normalized;
    }

    private void Assign(RaceFacts f)
    {
        Name = f.Name;
        Date = f.Date;
        StartTime = f.StartTime;
        TimeZoneId = CountryTimeZones.Find(f.CountryCode);
        LocationName = f.LocationName;
        Point = f.Point;
        CountryCode = f.CountryCode;
        RegionCode = f.RegionCode;
        Kind = f.Kind;
        Surface = f.Surface;
        _distances.Clear();
        _distances.AddRange(f.DistancesMeters);
        ElevationGainMeters = f.ElevationGainMeters;
        SignupUrl = f.SignupUrl;
        EventUrl = f.EventUrl;
        SourceUrl = f.SourceUrl;
        PriceAmount = f.PriceAmount;
        PriceCurrency = f.PriceCurrency;
        SignupDeadline = f.SignupDeadline;
        OrganizerName = f.OrganizerName;
        FactsHash = RaceFactsHasher.Hash(f);
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
