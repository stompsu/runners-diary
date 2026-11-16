using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Goals;

/// <summary>
/// A per-user SQL Server application lock held until the surrounding transaction ends. Goal delete, suggestion accept and plan commit take it,
/// so they serialise: an accept cannot insert a run for a goal that a delete is clearing, and a commit cannot insert suggestions for a goal
/// that is being deleted. The foreign keys stay the safety net (see <see cref="IsForeignKeyViolation"/>).
/// </summary>
internal static class GoalWriteLock
{
    private const int SqlForeignKeyViolation = 547;

    public static Task AcquireAsync(RunnersDiaryDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {"user-goals:" + userId.ToString("N")}, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 50001, N'Could not lock the user''s goals.', 1;
            """,
            cancellationToken);

    /// <summary>A write that referenced a goal (or run) that a concurrent request deleted first.</summary>
    public static bool IsForeignKeyViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: SqlForeignKeyViolation };
}
