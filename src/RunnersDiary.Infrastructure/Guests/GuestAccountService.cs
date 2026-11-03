using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Guests;

public sealed record GuestAccount(Guid UserId, DateTimeOffset ExpiresAt);

public enum GuestCreationFailure
{
    /// <summary>The global cap of active guests is reached.</summary>
    AtCapacity,

    /// <summary>All seeding slots stayed busy for the whole wait.</summary>
    Busy,

    /// <summary>Seeding failed (database or blob storage unavailable); everything it had written was rolled back.</summary>
    SeedingFailed,
}

/// <summary>Either the new account or the reason none was created (with a suggested retry delay).</summary>
public sealed record GuestCreationResult(GuestAccount? Account, GuestCreationFailure? Failure, TimeSpan RetryAfter)
{
    public static GuestCreationResult Created(GuestAccount account) => new(account, null, TimeSpan.Zero);

    public static GuestCreationResult Failed(GuestCreationFailure failure, TimeSpan retryAfter) => new(null, failure, retryAfter);
}

/// <summary>
/// Creates guest accounts (ADR 0011 §1, §6): enforces the active-guest cap, limits concurrent seedings process-wide and hands the
/// account to the <see cref="DemoDataSeeder"/>. The cap is checked again after a seeding slot is acquired (requests that queued for
/// a slot must not all pass on a stale count). The check and the insert are still not atomic, so creations that hold a slot at the
/// same time can overshoot <see cref="GuestOptions.MaxActive"/> by at most <c>SeedingConcurrency - 1</c> per API replica, which is
/// acceptable for an abuse cap.
/// </summary>
public sealed partial class GuestAccountService(
    IServiceScopeFactory scopes,
    DemoDataSeeder seeder,
    GuestCleanup cleanup,
    TimeProvider clock,
    IOptions<GuestOptions> options,
    ILogger<GuestAccountService> logger) : IDisposable
{
    private static readonly TimeSpan CapacityRetryAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BusyRetryAfter = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _seedingSlots = new(Math.Max(1, options.Value.SeedingConcurrency));

    public async Task<GuestCreationResult> CreateAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();

        // Cheap early exit before queueing for a slot.
        if (await IsAtCapacityAsync(now, settings.MaxActive, cancellationToken))
        {
            return GuestCreationResult.Failed(GuestCreationFailure.AtCapacity, CapacityRetryAfter);
        }

        if (!await _seedingSlots.WaitAsync(settings.SeedingWaitTimeout, cancellationToken))
        {
            return GuestCreationResult.Failed(GuestCreationFailure.Busy, BusyRetryAfter);
        }

        try
        {
            // The count above may be stale after waiting for the slot: re-check now that this request is the one about to insert.
            now = clock.GetUtcNow();
            if (await IsAtCapacityAsync(now, settings.MaxActive, cancellationToken))
            {
                return GuestCreationResult.Failed(GuestCreationFailure.AtCapacity, CapacityRetryAfter);
            }

            var guest = User.CreateGuest(now);
            await seeder.SeedAsync(guest, cancellationToken);
            cleanup.RequestOpportunisticRun();
            return GuestCreationResult.Created(new GuestAccount(guest.Id, new DateTimeOffset(guest.ExpiresAt!.Value, TimeSpan.Zero)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The seeder already removed what it wrote; the caller gets a retryable 503 instead of a half-built account.
            LogSeedingFailed(ex);
            return GuestCreationResult.Failed(GuestCreationFailure.SeedingFailed, BusyRetryAfter);
        }
        finally
        {
            _seedingSlots.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Creating a guest account failed while seeding the demo data.")]
    private partial void LogSeedingFailed(Exception exception);

    public void Dispose() => _seedingSlots.Dispose();

    private async Task<bool> IsAtCapacityAsync(DateTimeOffset now, int maxActive, CancellationToken cancellationToken)
    {
        if (await CountActiveAsync(now, cancellationToken) < maxActive)
        {
            return false;
        }

        // Expired guests do not count, but try to free their storage while the system is full.
        cleanup.RequestOpportunisticRun();
        return true;
    }

    private async Task<int> CountActiveAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RunnersDiaryDbContext>();
        var cutoff = now.UtcDateTime;
        return await db.Users.AsNoTracking().CountAsync(u => u.Kind == UserKind.Guest && u.ExpiresAt > cutoff, cancellationToken);
    }
}
