using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain.Time;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Goals;

/// <summary>The caller's profile zone and "today" in it (ADR 0008).</summary>
internal static class CallerClock
{
    public static async Task<(string Zone, DateOnly Today)> GetAsync(
        RunnersDiaryDbContext db, ICurrentUser currentUser, TimeProvider clock, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var zone = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken) ?? User.DefaultEntraTimeZoneId;
        return (zone, UserToday.Get(clock, zone));
    }
}
