using Microsoft.EntityFrameworkCore;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Gpx;

/// <summary>
/// A per-user SQL Server application lock held until the surrounding transaction ends. Everything that changes the set of a user's
/// tracks (insert, replace, run delete) takes it, so the track cap is re-counted race-free and a run delete sees every track a
/// racing upload committed before it (and every later insert fails on the run's foreign key).
/// </summary>
internal static class UserTrackLock
{
    public static Task AcquireAsync(RunnersDiaryDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {"user-tracks:" + userId.ToString("N")}, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 50001, N'Could not lock the user''s tracks.', 1;
            """,
            cancellationToken);
}
