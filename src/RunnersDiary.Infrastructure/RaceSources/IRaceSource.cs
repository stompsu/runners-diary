using RunnersDiary.Domain.Races;

namespace RunnersDiary.Infrastructure.RaceSources;

/// <summary>
/// A catalogue row the importer already knows: the detail-page bookkeeping (for conditional requests and the refresh rule of ADR 0035 §4)
/// and the stored <see cref="Facts"/>, which a 304 or an exhausted detail budget keeps instead of re-reading the page.
/// </summary>
public sealed record KnownRace(
    Guid Id,
    RaceFacts Facts,
    DateTime? DetailFetchedUtc,
    string? DetailETag,
    string? DetailLastModified);

/// <summary>What a source scan starts from: the stored races of that source (by external id), "today" in the catalogue's zone and the clock reading of the run.</summary>
public sealed record RaceScanContext(IReadOnlyDictionary<string, KnownRace> Known, DateOnly Today, DateTimeOffset Now);

/// <summary>A detail page that was read (200) or confirmed unchanged (304) in this run; the importer stores the validators on the race.</summary>
public sealed record DetailFetchRecord(string ExternalId, string? ETag, string? LastModified);

/// <summary>How a scan ended. <see cref="LayoutChanged"/>, <see cref="RobotsDisallowed"/> and <see cref="RobotsUnavailable"/> are failures (exit code 1, ADR 0038 §1).</summary>
public enum RaceScanOutcome
{
    Completed = 0,

    /// <summary>HTTP 403 or 429: the source is left alone for this run (a warning, not a failure).</summary>
    StoppedByServer = 1,

    /// <summary>Three consecutive 5xx answers or timeouts, an unusable listing page or an exhausted listing budget: partial result, no failure.</summary>
    StoppedByFailures = 2,

    /// <summary>A 200 listing page without a single event: the parser no longer understands the site.</summary>
    LayoutChanged = 3,

    /// <summary>The path we need is disallowed by robots.txt.</summary>
    RobotsDisallowed = 4,

    /// <summary>robots.txt answered 5xx, timed out or could not be parsed: the source is aborted (RFC 9309 section 2.3.1.4).</summary>
    RobotsUnavailable = 5,
}

/// <summary>Counters of one scan (ADR 0038 §5 summary line).</summary>
public sealed record RaceFetchStats
{
    public int ListingPages { get; init; }

    public int DetailPages { get; init; }

    public int NotModified { get; init; }

    public int Requests { get; init; }

    /// <summary>Listing items or detail pages dropped by validation, a foreign host or a failed fetch.</summary>
    public int Skipped { get; init; }

    /// <summary>New races whose detail page was not fetched (budget or failure); they are picked up by a later run.</summary>
    public int Deferred { get; init; }

    public int MalformedJsonLd { get; init; }

    public static RaceFetchStats Empty { get; } = new();
}

/// <summary>
/// The outcome of one source scan: normalised facts (already merged with stored detail facts where a 304 or the budget applied), whether the scan
/// saw the whole listing (<see cref="Complete"/>; only then may the merger count a race as missed) and the detail pages to record.
/// </summary>
public sealed record RaceScanResult(
    IReadOnlyList<RaceFacts> Facts,
    bool Complete,
    RaceScanOutcome Outcome,
    RaceFetchStats Stats,
    IReadOnlyList<DetailFetchRecord> DetailFetches)
{
    public static RaceScanResult Failed(RaceScanOutcome outcome, RaceFetchStats? stats = null) =>
        new([], false, outcome, stats ?? RaceFetchStats.Empty, []);

    /// <summary>Whether the outcome makes the job exit with 1.</summary>
    public bool IsFailure => Outcome is RaceScanOutcome.LayoutChanged or RaceScanOutcome.RobotsDisallowed or RaceScanOutcome.RobotsUnavailable;

    /// <summary>Nothing may be written for these outcomes: the facts are empty and the scan is incomplete.</summary>
    public bool HasNothingToApply => Facts.Count == 0 && DetailFetches.Count == 0;
}

/// <summary>One catalogue source (ADR 0035 §1). A scan never touches the database; the importer owns all writes.</summary>
public interface IRaceSource
{
    RaceSource Source { get; }

    /// <summary>Stable id, as in <c>RaceSourceRegistry</c> and the <c>RaceImport:Sources:&lt;Id&gt;</c> settings.</summary>
    string Id { get; }

    Task<RaceScanResult> ScanAsync(RaceScanContext context, CancellationToken cancellationToken);
}
