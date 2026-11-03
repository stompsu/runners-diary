using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Users;

/// <summary>
/// Maps authenticated identities to internal users (ADR 0010): just-in-time provisioning for Entra <c>(tid, oid)</c> and the
/// <c>Kind = Guest</c> + unexpired check for guest tokens. Positive results are memory-cached, but never beyond the account's own
/// expiry, and guest cleanup evicts the entry of every guest it deletes (<see cref="EvictGuest"/>), so a deleted or expired guest is
/// refused immediately. It uses its own DI scope so the request's DbContext is not polluted.
/// </summary>
public sealed partial class UserProvisioner(IServiceScopeFactory scopes, IMemoryCache cache, TimeProvider clock, ILogger<UserProvisioner> logger)
{
    public static readonly TimeSpan EntraCacheDuration = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan GuestCacheDuration = TimeSpan.FromMinutes(5);

    public async Task<Guid> GetOrCreateEntraUserAsync(Guid tenantId, Guid objectId, string? displayName, CancellationToken cancellationToken)
    {
        var key = $"user:entra:{tenantId:N}:{objectId:N}";
        if (cache.TryGetValue(key, out Guid cached))
        {
            return cached;
        }

        var id = await FindAsync(tenantId, objectId, cancellationToken);
        if (id is null)
        {
            id = await CreateAsync(tenantId, objectId, displayName, cancellationToken);
        }

        cache.Set(key, id.Value, EntraCacheDuration);
        return id.Value;
    }

    /// <summary>
    /// True when <paramref name="userId"/> refers to an existing guest account whose <c>ExpiresAt</c> is still in the future
    /// (defence in depth: a token can outlive a lagging cleanup, never the account).
    /// </summary>
    public async Task<bool> IsActiveGuestAsync(Guid userId, CancellationToken cancellationToken)
    {
        var key = GuestKey(userId);
        if (cache.TryGetValue(key, out bool _))
        {
            return true;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RunnersDiaryDbContext>();
        var expiresAt = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.Kind == UserKind.Guest && u.ExpiresAt > now)
            .Select(u => u.ExpiresAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (expiresAt is not { } expiry)
        {
            return false;
        }

        // Cache only until the account itself expires, so the entry can never outlive it.
        var lifetime = DateTime.SpecifyKind(expiry, DateTimeKind.Utc) - now;
        cache.Set(key, true, lifetime < GuestCacheDuration ? lifetime : GuestCacheDuration);
        return true;
    }

    /// <summary>Forgets the cached "active guest" verdict, e.g. after the account was deleted.</summary>
    public void EvictGuest(Guid userId) => cache.Remove(GuestKey(userId));

    private static string GuestKey(Guid userId) => $"user:guest:{userId:N}";

    [LoggerMessage(Level = LogLevel.Information, Message = "Provisioned user {UserId} for an Entra identity.")]
    private partial void LogProvisioned(Guid userId);

    private async Task<Guid?> FindAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RunnersDiaryDbContext>();
        var found = await db.Users.AsNoTracking()
            .Where(u => u.ExternalTenantId == tenantId && u.ExternalObjectId == objectId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return found;
    }

    private async Task<Guid> CreateAsync(Guid tenantId, Guid objectId, string? displayName, CancellationToken cancellationToken)
    {
        var user = User.CreateEntra(tenantId, objectId, Truncate(displayName), clock.GetUtcNow());
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RunnersDiaryDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
            LogProvisioned(user.Id);
            return user.Id;
        }
        catch (DbUpdateException)
        {
            // A concurrent first request won the unique index (ExternalTenantId, ExternalObjectId); read the winner.
            var existing = await FindAsync(tenantId, objectId, cancellationToken);
            return existing ?? throw new InvalidOperationException("User provisioning failed and no existing user was found.");
        }
    }

    private static string? Truncate(string? name) =>
        name is { Length: > User.DisplayNameMaxLength } ? name[..User.DisplayNameMaxLength] : name;
}
