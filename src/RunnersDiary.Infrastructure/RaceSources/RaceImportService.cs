using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Time;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Races;

namespace RunnersDiary.Infrastructure.RaceSources;

/// <summary>How a whole import run ended (ADR 0038 §1); the job maps it to the exit code.</summary>
public enum RaceImportOutcome
{
    /// <summary>The global or every source switch is off: nothing was done and no SQL connection was opened.</summary>
    Disabled = 0,

    /// <summary>Another execution holds the application lock; nothing was done.</summary>
    Locked = 1,

    /// <summary>Every enabled source completed or stopped by a rule that is not a failure.</summary>
    Completed = 2,

    /// <summary>A layout change, a robots failure, an unhandled exception or a SQL failure.</summary>
    Failed = 3,
}

public sealed record RaceImportSourceSummary(
    string Source,
    RaceScanOutcome ScanOutcome,
    RaceFetchStats Stats,
    int New,
    int Updated,
    int DateChanged,
    int Removed,
    int Restored,
    int Duplicates,
    int Purged,
    long DurationMs,
    string Outcome);

public sealed record RaceImportRunResult(RaceImportOutcome Outcome, IReadOnlyList<RaceImportSourceSummary> Sources)
{
    public int ExitCode => Outcome == RaceImportOutcome.Failed ? 1 : 0;
}

/// <summary>
/// The import run of <c>--job race-import</c> (ADR 0038 §1):
/// <list type="number">
/// <item>checks the switches and returns without any SQL connection when they are off;</item>
/// <item>takes the session application lock <c>race-import</c> (timeout 0) on a connection it keeps for the whole run, so two executions never overlap;</item>
/// <item>reads the stored races of the enabled sources once, then crawls with no query running;</item>
/// <item>per source applies <see cref="RaceCatalogueMerger"/>, the duplicate matcher and the retention purge in one transaction.</item>
/// </list>
/// A source that fails does not stop the others; any failure makes the run <see cref="RaceImportOutcome.Failed"/>.
/// All writes go through an anonymous context: <c>Races</c> is shared reference data (ADR 0036 §1).
/// </summary>
public sealed partial class RaceImportService
{
    /// <summary>The <c>sp_getapplock</c> resource of the import (the same name the tests hold from another session).</summary>
    public const string LockResource = "race-import";

    private const string AcquireLockSql =
        "DECLARE @result int; EXEC @result = sp_getapplock @Resource = N'" + LockResource + "', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 0; SELECT @result AS [Value];";

    private const string ReleaseLockSql = "EXEC sp_releaseapplock @Resource = N'" + LockResource + "', @LockOwner = N'Session';";

    private const string WarsawTimeZone = "Europe/Warsaw";
    private const int PurgeChunk = 500;

    private readonly IServiceScopeFactory _scopes;
    private readonly IReadOnlyList<IRaceSource> _sources;
    private readonly IOptions<RaceImportOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<RaceImportService> _logger;

    public RaceImportService(
        IServiceScopeFactory scopes,
        IEnumerable<IRaceSource> sources,
        IOptions<RaceImportOptions> options,
        TimeProvider clock,
        ILogger<RaceImportService> logger)
    {
        _scopes = scopes;
        _sources = sources.ToList();
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RaceImportRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var enabled = _sources.Where(s => options.IsEnabled(s.Source)).ToList();
        if (!options.Enabled || enabled.Count == 0)
        {
            LogDisabled(options.Enabled, enabled.Count);
            return new RaceImportRunResult(RaceImportOutcome.Disabled, []);
        }

        await using var lockScope = _scopes.CreateAsyncScope();
        await using var lockDb = NewDb(lockScope);
        try
        {
            await lockDb.Database.OpenConnectionAsync(cancellationToken);
            if (!await TryLockAsync(lockDb, cancellationToken))
            {
                LogLocked();
                return new RaceImportRunResult(RaceImportOutcome.Locked, []);
            }
        }
#pragma warning disable CA1031 // The job's contract is exit code 1 for any SQL failure; the type is logged, never the message (it can carry the server name).
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogSqlFailed(ex.GetType().Name);
            return new RaceImportRunResult(RaceImportOutcome.Failed, []);
        }

        var summaries = new List<RaceImportSourceSummary>();
        var failed = false;
        try
        {
            var now = _clock.GetUtcNow();
            var today = UserToday.Get(_clock, WarsawTimeZone);
            var known = await ReadKnownAsync(enabled, cancellationToken);
            foreach (var source in enabled)
            {
                var summary = await ImportSourceAsync(source, known[source.Source], today, now, cancellationToken);
                summaries.Add(summary);
                failed |= summary.Outcome == nameof(RaceImportOutcome.Failed);
            }
        }
#pragma warning disable CA1031 // Same contract as above: an unexpected failure is exit code 1, not a crash without a summary.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogRunFailed(ex.GetType().Name);
            failed = true;
        }
        finally
        {
            await ReleaseLockAsync(lockDb);
        }

        return new RaceImportRunResult(failed ? RaceImportOutcome.Failed : RaceImportOutcome.Completed, summaries);
    }

    private async Task<RaceImportSourceSummary> ImportSourceAsync(
        IRaceSource source, IReadOnlyDictionary<string, KnownRace> known, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        RaceScanResult scan;
        try
        {
            scan = await source.ScanAsync(new RaceScanContext(known, today, now), cancellationToken);
        }
#pragma warning disable CA1031 // One source failing must not hide the summary or stop the other sources; the type is logged.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogSourceFailed(source.Id, ex.GetType().Name);
            return Summary(source, RaceScanOutcome.StoppedByFailures, RaceFetchStats.Empty, null, started, nameof(RaceImportOutcome.Failed));
        }

        if (scan.HasNothingToApply && scan.Outcome != RaceScanOutcome.Completed)
        {
            return Summary(source, scan.Outcome, scan.Stats, null, started, scan.IsFailure ? nameof(RaceImportOutcome.Failed) : nameof(RaceImportOutcome.Completed));
        }

        try
        {
            var applied = await ApplyAsync(source, scan, today, now, cancellationToken);
            RaceImportMetrics.RecordUpserts(source.Id, "new", applied.New);
            RaceImportMetrics.RecordUpserts(source.Id, "updated", applied.Updated);
            RaceImportMetrics.RecordUpserts(source.Id, "removed", applied.Removed);
            return Summary(source, scan.Outcome, scan.Stats, applied, started, scan.IsFailure ? nameof(RaceImportOutcome.Failed) : nameof(RaceImportOutcome.Completed));
        }
#pragma warning disable CA1031 // A SQL failure of one source is exit code 1 with a summary line; the type is logged.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogSourceFailed(source.Id, ex.GetType().Name);
            return Summary(source, scan.Outcome, scan.Stats, null, started, nameof(RaceImportOutcome.Failed));
        }
    }

    private RaceImportSourceSummary Summary(
        IRaceSource source, RaceScanOutcome scanOutcome, RaceFetchStats stats, Applied? applied, long started, string outcome)
    {
        var a = applied ?? default;
        var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var summary = new RaceImportSourceSummary(
            source.Id, scanOutcome, stats, a.New, a.Updated, a.DateChanged, a.Removed, a.Restored, a.Duplicates, a.Purged, elapsedMs, outcome);
        LogSourceCompleted(
            source.Id, stats.ListingPages, stats.DetailPages, stats.NotModified, a.New, a.Updated, a.DateChanged, a.Removed, a.Restored,
            a.Duplicates, a.Purged, stats.Rejected + a.Rejected, elapsedMs, outcome == nameof(RaceImportOutcome.Failed) ? $"{outcome}:{scanOutcome}" : scanOutcome.ToString());
        return summary;
    }

    /// <summary>The single transaction of one source: merge, duplicates, purge. Everything is created inside the attempt, so the retrying execution strategy can rerun it.</summary>
    private async Task<Applied> ApplyAsync(IRaceSource source, RaceScanResult scan, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        await using var strategyScope = _scopes.CreateAsyncScope();
        await using var strategyDb = NewDb(strategyScope);
        var strategy = strategyDb.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var scope = _scopes.CreateAsyncScope();
            await using var db = NewDb(scope);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var rows = await db.Races.Include(r => r.Distances).Where(r => r.Source == source.Source).ToListAsync(cancellationToken);
            var plan = RaceCatalogueMerger.Plan(rows.Select(r => r.ToIndexEntry()).ToList(), scan.Facts, scan.Complete, today, now, options.RemoveAfterMissedScans);
            var byId = rows.ToDictionary(r => r.Id);
            var byExternalId = rows.ToDictionary(r => r.ExternalId, StringComparer.Ordinal);

            // Defence in depth: a source adapter validates its facts, but one item that still breaks a domain rule must cost that race only, not the source's transaction.
            var rejected = 0;
            var inserted = 0;
            var updated = 0;
            foreach (var facts in plan.Inserts)
            {
                try
                {
                    var race = Race.Create(facts, now);
                    db.Races.Add(race);
                    byExternalId[race.ExternalId] = race;
                    inserted++;
                }
                catch (DomainRuleException ex)
                {
                    rejected++;
                    LogRaceRejected(source.Id, facts.ExternalId, RulePaths(ex));
                }
            }

            foreach (var update in plan.Updates)
            {
                try
                {
                    byId[update.Id].ApplyFacts(update.Facts, now);
                    updated++;
                }
                catch (DomainRuleException ex)
                {
                    rejected++;
                    LogRaceRejected(source.Id, update.Facts.ExternalId, RulePaths(ex));
                }
            }

            foreach (var id in plan.Seen)
            {
                byId[id].MarkSeen(now);
            }

            foreach (var miss in plan.Missed)
            {
                byId[miss.Id].MarkMissed(now, options.RemoveAfterMissedScans);
            }

            foreach (var fetch in scan.DetailFetches)
            {
                if (byExternalId.TryGetValue(fetch.ExternalId, out var race))
                {
                    race.RecordDetailFetch(fetch.ETag, fetch.LastModified, now);
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            var duplicates = await ResolveDuplicatesAsync(db, today, cancellationToken);
            var purged = scan.Outcome == RaceScanOutcome.Completed
                ? await PurgeAsync(db, source.Source, today.AddDays(-options.RetainPastDays), cancellationToken)
                : 0;

            await transaction.CommitAsync(cancellationToken);
            return new Applied(inserted, updated, plan.DateChanges.Count, plan.Removals.Count, plan.Restores.Count, duplicates, purged, rejected);
        });
    }

    /// <summary>Runs the matcher over every live upcoming race (all sources) and stores the result; returns the number of upcoming duplicates.</summary>
    private static async Task<int> ResolveDuplicatesAsync(RunnersDiaryDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var candidates = await db.Races.Where(r => r.RemovedUtc == null && r.Date >= today).ToListAsync(cancellationToken);
        var resolved = RaceDuplicateMatcher.Resolve(
            candidates.Select(r => new RaceDuplicateCandidate(r.Id, r.Source, r.Name, r.Date, r.Point, r.LocationName, r.ImportedUtc)).ToList());

        var changed = false;
        foreach (var race in candidates)
        {
            var wanted = resolved[race.Id];
            if (race.DuplicateOfRaceId != wanted)
            {
                race.SetDuplicateOf(wanted);
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return candidates.Count(r => r.DuplicateOfRaceId is not null);
    }

    /// <summary>
    /// Deletes this source's races dated before <paramref name="cutoff"/>. A race that a surviving row still points to through <c>DuplicateOfRaceId</c>
    /// (NO ACTION foreign key) is skipped, and the skip is repeated until no surviving row references a doomed one. Distances and proposal states cascade in SQL
    /// and linked runs have their link set to NULL by SQL, so the job needs no right on <c>Runs</c> or <c>RaceProposalStates</c>.
    /// </summary>
    private static async Task<int> PurgeAsync(RunnersDiaryDbContext db, RaceSource source, DateOnly cutoff, CancellationToken cancellationToken)
    {
        var doomed = (await db.Races.AsNoTracking().Where(r => r.Source == source && r.Date < cutoff).Select(r => r.Id).ToListAsync(cancellationToken)).ToHashSet();
        if (doomed.Count == 0)
        {
            return 0;
        }

        var duplicates = await db.Races.AsNoTracking().Where(r => r.DuplicateOfRaceId != null)
            .Select(r => new { r.Id, Canonical = r.DuplicateOfRaceId!.Value }).ToListAsync(cancellationToken);
        bool skipped;
        do
        {
            var protectedIds = duplicates.Where(d => !doomed.Contains(d.Id) && doomed.Contains(d.Canonical)).Select(d => d.Canonical).ToList();
            skipped = protectedIds.Count > 0;
            doomed.ExceptWith(protectedIds);
        }
        while (skipped);

        var ids = doomed.ToList();
        var purged = 0;
        foreach (var chunk in ids.Chunk(PurgeChunk))
        {
            var part = chunk.ToList();
            // Rows that are deleted together may point at each other: clear the pointers first so the chunk order cannot violate the foreign key.
            await db.Races.Where(r => part.Contains(r.Id) && r.DuplicateOfRaceId != null).ExecuteUpdateAsync(s => s.SetProperty(r => r.DuplicateOfRaceId, (Guid?)null), cancellationToken);
        }

        foreach (var chunk in ids.Chunk(PurgeChunk))
        {
            var part = chunk.ToList();
            purged += await db.Races.Where(r => part.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        return purged;
    }

    private async Task<Dictionary<RaceSource, IReadOnlyDictionary<string, KnownRace>>> ReadKnownAsync(IReadOnlyList<IRaceSource> sources, CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        await using var db = NewDb(scope);
        var result = new Dictionary<RaceSource, IReadOnlyDictionary<string, KnownRace>>();
        foreach (var source in sources)
        {
            var races = await db.Races.AsNoTracking().Include(r => r.Distances).Where(r => r.Source == source.Source).ToListAsync(cancellationToken);
            result[source.Source] = races.ToDictionary(
                r => r.ExternalId,
                r => new KnownRace(r.Id, r.ToFacts(), r.DetailFetchedUtc, r.DetailETag, r.DetailLastModified),
                StringComparer.Ordinal);
        }

        return result;
    }

    private static RunnersDiaryDbContext NewDb(AsyncServiceScope scope)
    {
        // Explicitly anonymous: only the shared catalogue is touched, and the ownership guard would reject any owned-entity write.
        var dbOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<RunnersDiaryDbContext>>();
        return new RunnersDiaryDbContext(dbOptions, Identity.AnonymousCurrentUser.Instance);
    }

    private static async Task<bool> TryLockAsync(RunnersDiaryDbContext db, CancellationToken cancellationToken)
    {
        // Session-owned, timeout 0: granted (0 or 1) or refused at once (-1). It lives as long as this connection, which stays open for the run.
        var result = await db.Database.SqlQueryRaw<int>(AcquireLockSql).ToListAsync(cancellationToken);
        return result.Count == 1 && result[0] >= 0;
    }

    private async Task ReleaseLockAsync(RunnersDiaryDbContext db)
    {
        try
        {
            // The lock also ends with the connection; releasing explicitly keeps a quick rerun from seeing a stale holder.
            await db.Database.ExecuteSqlRawAsync(ReleaseLockSql, CancellationToken.None);
        }
#pragma warning disable CA1031 // Best effort: a broken connection already dropped the lock.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogReleaseFailed(ex.GetType().Name);
        }
    }

    private readonly record struct Applied(int New, int Updated, int DateChanged, int Removed, int Restored, int Duplicates, int Purged, int Rejected);

    [LoggerMessage(Level = LogLevel.Information, Message = "Race import disabled (RaceImport:Enabled={Enabled}, enabled sources: {EnabledSources}); no SQL connection was opened.")]
    private partial void LogDisabled(bool enabled, int enabledSources);

    [LoggerMessage(Level = LogLevel.Information, Message = "Race import skipped: another execution holds the application lock.")]
    private partial void LogLocked();

    [LoggerMessage(Level = LogLevel.Error, Message = "Race import: SQL failed before the crawl ({ErrorType}).")]
    private partial void LogSqlFailed(string errorType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Race import failed unexpectedly ({ErrorType}).")]
    private partial void LogRunFailed(string errorType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Race import: source {Source} failed ({ErrorType}).")]
    private partial void LogSourceFailed(string source, string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Race import: releasing the application lock failed ({ErrorType}).")]
    private partial void LogReleaseFailed(string errorType);

    [LoggerMessage(Level = LogLevel.Information, Message =
        "RaceImportSourceCompleted {Source} {ListingPages} {DetailPages} {NotModified} {New} {Updated} {DateChanged} {Removed} {Restored} {Duplicates} {Purged} {Rejected} {DurationMs} {Outcome}")]
    private partial void LogSourceCompleted(
        string source, int listingPages, int detailPages, int notModified, int @new, int updated, int dateChanged, int removed, int restored,
        int duplicates, int purged, int rejected, long durationMs, string outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Race import: source {Source} race {ExternalId} rejected by the domain rules ({Rules}); the other races are kept.")]
    private partial void LogRaceRejected(string source, string externalId, string rules);

    private static string RulePaths(DomainRuleException ex) => string.Join(',', ex.Errors.Select(e => e.Path).Distinct());
}
