using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Time;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Runs;

namespace RunnersDiary.Infrastructure.Races;

public enum RaceSignupStatus
{
    Created,
    NotFound,
    Invalid,
    AlreadyLinked,
    RunLimit,
}

/// <summary>Outcome of <see cref="RaceSignupService.SignUpAsync"/>; exactly the member that belongs to <see cref="Status"/> is set.</summary>
public sealed record RaceSignupResult(
    RaceSignupStatus Status,
    Run? Run = null,
    Guid? ExistingRunId = null,
    IReadOnlyList<DomainError>? Errors = null,
    string? LimitDetail = null);

/// <summary>
/// "I signed up" (ADR 0036 §5): maps a catalogue race to a planned race run for the caller through the pure <see cref="RaceRunMapper"/>,
/// applies the shared run caps and relies on the filtered unique index <c>UX_Runs_User_CatalogRace</c> for "one run per race".
/// Order of answers: unknown, removed or duplicate race (not found); already linked; invalid choice; run cap.
/// </summary>
public sealed class RaceSignupService(
    RunnersDiaryDbContext db, ICurrentUser currentUser, TimeProvider clock, IOptions<RunLimitsOptions> limits)
{
    private const int SqlDuplicateKeyIndex = 2601;
    private const int SqlDuplicateKeyConstraint = 2627;

    public async Task<RaceSignupResult> SignUpAsync(Guid raceId, RaceSignupChoice choice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(choice);
        var userId = currentUser.UserId;

        var row = await db.Races.AsNoTracking()
            .Where(r => r.Id == raceId && r.RemovedUtc == null && r.DuplicateOfRaceId == null)
            .Select(r => new
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
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return new RaceSignupResult(RaceSignupStatus.NotFound);
        }

        if (await ExistingRunIdAsync(raceId, cancellationToken) is { } existing)
        {
            return new RaceSignupResult(RaceSignupStatus.AlreadyLinked, ExistingRunId: existing);
        }

        var zone = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken) ?? User.DefaultEntraTimeZoneId;
        var snapshot = new RaceSnapshot(
            row.Id, row.Source, row.Name, row.Date, row.StartTime, row.TimeZoneId, row.LocationName, row.Point, row.CountryCode, row.RegionCode,
            row.Kind, row.Surface, row.Distances.Order().ToList(), row.ElevationGainMeters, row.ImportedUtc);

        if (!RaceRunMapper.TryToPlannedRun(snapshot, choice, zone, UserToday.Get(clock, zone), out var values, out var errors))
        {
            return new RaceSignupResult(RaceSignupStatus.Invalid, Errors: errors);
        }

        if (await RunCaps.CheckCreateAsync(db, currentUser, limits.Value, values.Date, cancellationToken) is { } limitDetail)
        {
            return new RaceSignupResult(RaceSignupStatus.RunLimit, LimitDetail: limitDetail);
        }

        // The owner is always the caller (never from the request).
        var run = Run.Create(userId, values, clock.GetUtcNow());
        run.LinkToCatalogRace(raceId);
        db.Runs.Add(run);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // A concurrent signup of the same caller won the unique index.
            db.ChangeTracker.Clear();
            if (await ExistingRunIdAsync(raceId, cancellationToken) is not { } winner)
            {
                throw;
            }

            return new RaceSignupResult(RaceSignupStatus.AlreadyLinked, ExistingRunId: winner);
        }

        return new RaceSignupResult(RaceSignupStatus.Created, Run: run);
    }

    private Task<Guid?> ExistingRunIdAsync(Guid raceId, CancellationToken cancellationToken) =>
        db.Runs.AsNoTracking().Where(r => r.CatalogRaceId == raceId).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken);

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: SqlDuplicateKeyIndex or SqlDuplicateKeyConstraint };
}
