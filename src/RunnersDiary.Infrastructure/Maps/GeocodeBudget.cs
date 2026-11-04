using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Maps;

/// <summary>
/// A global cap on provider geocode calls per UTC day (ADR 0033 §2), on top of the per-caller rate limit. The count is durable in
/// the <c>GeocodeUsage</c> table, so it is shared by all replicas and survives restarts. The cap exists to keep monthly usage under
/// the free tier.
/// </summary>
public sealed class GeocodeBudget(MapsOptions options, TimeProvider timeProvider, IServiceScopeFactory scopes)
{
    /// <summary>Rows older than this are purged by the guest cleanup.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(35);

    // One round trip, race-safe: the NOT EXISTS probe takes an update/range lock (UPDLOCK, HOLDLOCK) so two first calls of a day
    // cannot both insert, then a single conditional UPDATE increments and returns the new count; no row comes back once spent.
    private const string AcquireSql = """
        SET NOCOUNT ON;
        INSERT INTO [GeocodeUsage] ([Day], [Count])
        SELECT @day, 0 WHERE NOT EXISTS (SELECT 1 FROM [GeocodeUsage] WITH (UPDLOCK, HOLDLOCK) WHERE [Day] = @day);
        UPDATE [GeocodeUsage] SET [Count] = [Count] + 1 OUTPUT inserted.[Count] WHERE [Day] = @day AND [Count] < @limit;
        """;

    /// <summary>Takes one call from today's budget; false (with the time until 00:00 UTC) when it is spent.</summary>
    public async Task<(bool Granted, TimeSpan RetryAfter)> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        await using var scope = scopes.CreateAsyncScope();
        // Explicitly anonymous: the table has no owner, and no user data is touched.
        var dbOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<RunnersDiaryDbContext>>();
        await using var db = new RunnersDiaryDbContext(dbOptions, AnonymousCurrentUser.Instance);

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = AcquireSql;
            command.Parameters.Add(new SqlParameter("@day", System.Data.SqlDbType.Date) { Value = today.ToDateTime(TimeOnly.MinValue) });
            command.Parameters.Add(new SqlParameter("@limit", System.Data.SqlDbType.Int) { Value = options.Geocoding.GlobalDailyLimit });
            var granted = await command.ExecuteScalarAsync(cancellationToken) is not null;
            if (granted)
            {
                return (true, TimeSpan.Zero);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        var midnight = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return (false, midnight - now);
    }

    /// <summary>Deletes usage rows older than <see cref="Retention"/>; returns how many were removed.</summary>
    public static Task<int> PurgeOlderThanAsync(RunnersDiaryDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var cutoff = DateOnly.FromDateTime((now - Retention).UtcDateTime);
        return db.GeocodeUsages.Where(g => g.Day < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
