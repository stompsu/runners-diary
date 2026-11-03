using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RunnersDiary.Domain.Common;

namespace RunnersDiary.Infrastructure.Persistence;

/// <summary>
/// Defence in depth for ADR 0010: no owned entity of another user may be added, modified or deleted, and a context without a
/// caller (background work, migrator, design time) may not write owned entities at all - code acting for a user must say so
/// explicitly (see the demo seeder, which passes its own <c>FixedCurrentUser</c>). Bulk <c>ExecuteDelete</c>/<c>ExecuteUpdate</c>
/// bypass SaveChanges and are used only on the Users table by guest cleanup.
/// A violation is a programming error (the "Owner" filter already hides foreign rows), so it throws instead of returning a user error.
/// Stateless: the caller is read from the context, so one instance serves all contexts.
/// </summary>
public sealed class OwnershipGuardInterceptor : SaveChangesInterceptor
{
    public static OwnershipGuardInterceptor Instance { get; } = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Check(DbContext? context)
    {
        if (context is not RunnersDiaryDbContext db)
        {
            return;
        }

        var callerId = db.CurrentUserId;
        foreach (var entry in context.ChangeTracker.Entries<IUserOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (callerId is null)
            {
                throw new InvalidOperationException(
                    $"Ownership violation: {entry.Metadata.ClrType.Name} {entry.State} for user {entry.Entity.UserId} in a context without a caller.");
            }

            if (entry.Entity.UserId != callerId)
            {
                throw new InvalidOperationException(
                    $"Ownership violation: {entry.Metadata.ClrType.Name} {entry.State} for user {entry.Entity.UserId} while the caller is {callerId}.");
            }
        }
    }
}
