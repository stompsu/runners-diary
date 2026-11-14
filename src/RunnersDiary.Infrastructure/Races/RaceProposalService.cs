using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Time;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Races;

/// <summary>The caller's view of one race: the derived state, the linked run and the "date changed" signal (ADR 0036 §3, 0037 §3).</summary>
public sealed record RaceUserState(ProposalState State, Guid? LinkedRunId, bool DateChanged);

public sealed record RaceProposalList(
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    bool ProfileConfigured,
    DateTime? CatalogueUpdatedUtc,
    bool Truncated,
    IReadOnlyList<RaceProposal> Items);

/// <summary>A catalogue race as <c>getRace</c> shows it. <see cref="IsNew"/> follows the caller's "seen" timestamp.</summary>
public sealed record RaceDetails(Race Race, RaceUserState User, bool IsNew);

public enum SetRaceStateStatus
{
    Ok,
    NotFound,
    LimitReached,
}

public sealed record SetRaceStateResult(SetRaceStateStatus Status, RaceUserState? User = null);

/// <summary>
/// Reads for the race proposals and the per-user state writes (ADR 0037). Proposals take three small no-tracking reads (the catalogue
/// window, the caller's states, the caller's linked runs) and hand them to the pure <see cref="RaceProposalBuilder"/>. Everything per user
/// goes through the Owner-filtered sets; the catalogue is shared reference data.
/// </summary>
public sealed partial class RaceProposalService(
    RunnersDiaryDbContext db, ICurrentUser currentUser, TimeProvider clock, IOptions<RacesOptions> options, ILogger<RaceProposalService> logger)
{
    private const int StateWriteAttempts = 2;

    public async Task<RaceProposalList> ListAsync(DateOnly windowFrom, DateOnly windowTo, bool includeDismissed, CancellationToken cancellationToken)
    {
        var today = await TodayAsync(cancellationToken);
        var profile = await db.RaceInterestProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var horizon = profile?.HorizonDays ?? RaceInterestValues.Defaults.HorizonDays;
        var horizonEnd = today.AddDays(horizon);
        var effectiveFrom = windowFrom > today ? windowFrom : today;
        var effectiveTo = windowTo < horizonEnd ? windowTo : horizonEnd;

        // LastSeenUtc has no index (no migration here) and the catalogue is small, so this is a cheap scan. The "catalogue updated" stamp is part of the
        // contract even for callers without a profile (RaceEndpointTests pins it), hence it is read before the early return.
        var updated = await db.Races.MaxAsync(r => (DateTime?)r.LastSeenUtc, cancellationToken);

        // No profile means proposals are off for this caller: no catalogue window read.
        if (profile is null)
        {
            return new RaceProposalList(effectiveFrom, effectiveTo, false, updated, false, []);
        }

        // The caller's own runs created from a race whose date is in the window: always shown, even outside the horizon.
        var linkedRows = await (
            from run in db.Runs.AsNoTracking()
            join race in db.Races on run.CatalogRaceId equals (Guid?)race.Id
            where race.Date >= windowFrom && race.Date <= windowTo
            select new { run.Id, run.Date, RaceId = race.Id }).ToListAsync(cancellationToken);
        var linked = linkedRows.ToDictionary(l => l.RaceId, l => new LinkedRun(l.Id, l.Date));

        var max = Math.Max(1, options.Value.Proposals.MaxCatalogueRows);
        var snapshots = new List<RaceSnapshot>();
        var truncated = false;
        if (effectiveTo >= effectiveFrom)
        {
            // A seek on the clustered Date index.
            var window = await ToSnapshotsAsync(
                db.Races.AsNoTracking()
                    .Where(r => r.RemovedUtc == null && r.DuplicateOfRaceId == null && r.Date >= effectiveFrom && r.Date <= effectiveTo)
                    .OrderBy(r => r.Date).Take(max + 1),
                cancellationToken);
            truncated = window.Count > max;
            if (truncated)
            {
                LogTruncated(max, effectiveFrom, effectiveTo);
            }

            snapshots.AddRange(truncated ? window.Take(max) : window);
        }

        var missingLinked = linked.Keys.Except(snapshots.Select(s => s.Id)).ToList();
        if (missingLinked.Count > 0)
        {
            snapshots.AddRange(await ToSnapshotsAsync(db.Races.AsNoTracking().Where(r => missingLinked.Contains(r.Id)), cancellationToken));
        }

        var ids = snapshots.Select(s => s.Id).ToList();
        var states = ids.Count == 0
            ? []
            : await db.RaceProposalStates.AsNoTracking().Where(s => ids.Contains(s.RaceId)).ToDictionaryAsync(s => s.RaceId, cancellationToken);

        var items = RaceProposalBuilder.Build(profile, today, windowFrom, windowTo, snapshots, states, linked, profile.ProposalsSeenUtc, includeDismissed);
        return new RaceProposalList(effectiveFrom, effectiveTo, true, updated, truncated, items);
    }

    /// <summary>The race with the caller's state, or null for an unknown race and for a removed one the caller is neither interested in nor linked to.</summary>
    public async Task<RaceDetails?> GetDetailsAsync(Guid raceId, CancellationToken cancellationToken)
    {
        var race = await db.Races.AsNoTracking().Include(r => r.Distances).FirstOrDefaultAsync(r => r.Id == raceId, cancellationToken);
        if (race is null)
        {
            return null;
        }

        var user = await UserStateAsync(race.Id, race.Date, cancellationToken);
        if (race.IsRemoved && user.State is not (ProposalState.Interested or ProposalState.SignedUp))
        {
            return null;
        }

        var seen = await db.RaceInterestProfiles.AsNoTracking().Select(p => p.ProposalsSeenUtc).FirstOrDefaultAsync(cancellationToken);
        var hasProfile = await db.RaceInterestProfiles.AnyAsync(cancellationToken);
        var isNew = !hasProfile || seen is null || race.ImportedUtc > seen;
        return new RaceDetails(race, user, isNew);
    }

    /// <summary>
    /// Sets or clears (<paramref name="mark"/> null) the caller's mark on a race. Unknown races are not found; a removed or duplicate race
    /// can only be cleared. A new row is refused beyond the per-account cap.
    /// </summary>
    public async Task<SetRaceStateResult> SetStateAsync(Guid raceId, RaceProposalMark? mark, CancellationToken cancellationToken)
    {
        var race = await db.Races.AsNoTracking()
            .Where(r => r.Id == raceId)
            .Select(r => new { r.Date, r.RemovedUtc, r.DuplicateOfRaceId })
            .FirstOrDefaultAsync(cancellationToken);
        if (race is null || (mark is not null && (race.RemovedUtc is not null || race.DuplicateOfRaceId is not null)))
        {
            return new SetRaceStateResult(SetRaceStateStatus.NotFound);
        }

        var userId = currentUser.UserId;
        for (var attempt = 1; ; attempt++)
        {
            var row = await db.RaceProposalStates.FirstOrDefaultAsync(s => s.RaceId == raceId, cancellationToken);
            if (mark is null)
            {
                if (row is null)
                {
                    break;
                }

                db.RaceProposalStates.Remove(row);
            }
            else if (row is not null)
            {
                row.Set(mark.Value, race.Date, clock.GetUtcNow());
            }
            else
            {
                var cap = currentUser.IsGuest ? options.Value.Proposals.MaxStatesPerGuest : options.Value.Proposals.MaxStatesPerUser;
                if (await db.RaceProposalStates.CountAsync(cancellationToken) >= cap)
                {
                    return new SetRaceStateResult(SetRaceStateStatus.LimitReached);
                }

                db.RaceProposalStates.Add(RaceProposalState.Create(userId, raceId, mark.Value, race.Date, clock.GetUtcNow()));
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException) when (attempt < StateWriteAttempts)
            {
                // A concurrent request of the same caller created or removed the row first: forget the pending change and decide again.
                db.ChangeTracker.Clear();
            }
        }

        return new SetRaceStateResult(SetRaceStateStatus.Ok, await UserStateAsync(raceId, race.Date, cancellationToken));
    }

    /// <summary>Records that the caller has opened the proposals list ("new" badges); a no-op without a profile.</summary>
    public async Task MarkSeenAsync(CancellationToken cancellationToken)
    {
        var profile = await db.RaceInterestProfiles.FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return;
        }

        profile.MarkProposalsSeen(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The derived state of one race for the caller (no-tracking reads, Owner-filtered).</summary>
    public async Task<RaceUserState> UserStateAsync(Guid raceId, DateOnly raceDate, CancellationToken cancellationToken)
    {
        var linked = await db.Runs.AsNoTracking()
            .Where(r => r.CatalogRaceId == raceId)
            .Select(r => new LinkedRun(r.Id, r.Date))
            .FirstOrDefaultAsync(cancellationToken);
        var stored = await db.RaceProposalStates.AsNoTracking().FirstOrDefaultAsync(s => s.RaceId == raceId, cancellationToken);
        return Derive(raceDate, stored, linked);
    }

    private static RaceUserState Derive(DateOnly raceDate, RaceProposalState? stored, LinkedRun? linked)
    {
        if (linked is not null)
        {
            return new RaceUserState(ProposalState.SignedUp, linked.RunId, linked.Date != raceDate);
        }

        return stored?.State switch
        {
            RaceProposalMark.Interested => new RaceUserState(ProposalState.Interested, null, stored.RaceDateWhenMarked != raceDate),
            RaceProposalMark.Dismissed => new RaceUserState(ProposalState.Dismissed, null, false),
            _ => new RaceUserState(ProposalState.None, null, false),
        };
    }

    private async Task<DateOnly> TodayAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var zone = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken) ?? User.DefaultEntraTimeZoneId;
        return UserToday.Get(clock, zone);
    }

    private static async Task<List<RaceSnapshot>> ToSnapshotsAsync(IQueryable<Race> query, CancellationToken cancellationToken)
    {
        var rows = await query.Select(r => new
        {
            r.Id,
            r.Source,
            r.Name,
            r.Date,
            r.StartTime,
            r.TimeZoneId,
            r.LocationName,
            r.Point,
            r.CountryCode,
            r.RegionCode,
            r.Kind,
            r.Surface,
            Distances = r.Distances.Select(d => d.DistanceMeters).ToList(),
            r.ElevationGainMeters,
            r.ImportedUtc,
            r.RemovedUtc,
            r.DuplicateOfRaceId,
        }).ToListAsync(cancellationToken);

        return rows.ConvertAll(r => new RaceSnapshot(
            r.Id, r.Source, r.Name, r.Date, r.StartTime, r.TimeZoneId, r.LocationName, r.Point, r.CountryCode, r.RegionCode, r.Kind, r.Surface,
            r.Distances.Order().ToList(), r.ElevationGainMeters, r.ImportedUtc, r.RemovedUtc, r.DuplicateOfRaceId));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Race proposals truncated to {Max} catalogue rows for the window {From} to {To}.")]
    private partial void LogTruncated(int max, DateOnly from, DateOnly to);
}
