using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Maps;

namespace RunnersDiary.Api.Tests.Maps;

/// <summary>The durable global daily geocode budget (ADR 0033 section 2) against real SQL Server. Each test uses its own far-future UTC day.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GeocodeBudgetTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MapsOptions Limit(int limit) => new() { Geocoding = { GlobalDailyLimit = limit } };

    // Days are unique per test so tests sharing the database never see each other's counts.
    private static FakeTimeProvider TimeOn(int dayOffset, TimeSpan? at = null) =>
        new(new DateTimeOffset(2200, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(dayOffset) + (at ?? TimeSpan.FromHours(12)));

    private GeocodeBudget Budget(MapsOptions options, TimeProvider time)
    {
        factory.SkipIfDockerUnavailable();
        return new GeocodeBudget(options, time, factory.Services.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task The_default_budget_is_75_per_day()
    {
        Assert.Equal(75, new MapsOptions().Geocoding.GlobalDailyLimit);
        var budget = Budget(new MapsOptions(), TimeOn(1));

        var granted = 0;
        for (var i = 0; i < 80; i++)
        {
            if ((await budget.TryAcquireAsync(Ct)).Granted)
            {
                granted++;
            }
        }

        Assert.Equal(75, granted);
    }

    [Fact]
    public async Task Calls_over_the_limit_are_refused_with_the_time_until_midnight_utc()
    {
        var budget = Budget(Limit(2), TimeOn(2, TimeSpan.FromHours(22.5)));

        Assert.True((await budget.TryAcquireAsync(Ct)).Granted);
        Assert.True((await budget.TryAcquireAsync(Ct)).Granted);
        var (granted, retryAfter) = await budget.TryAcquireAsync(Ct);

        Assert.False(granted);
        Assert.Equal(TimeSpan.FromMinutes(90), retryAfter);
    }

    [Fact]
    public async Task The_budget_resets_at_utc_midnight()
    {
        var time = TimeOn(3, new TimeSpan(23, 59, 59));
        var budget = Budget(Limit(1), time);
        Assert.True((await budget.TryAcquireAsync(Ct)).Granted);
        Assert.False((await budget.TryAcquireAsync(Ct)).Granted);

        time.Advance(TimeSpan.FromSeconds(1));

        Assert.True((await budget.TryAcquireAsync(Ct)).Granted);
        var (granted, retryAfter) = await budget.TryAcquireAsync(Ct);
        Assert.False(granted);
        Assert.Equal(TimeSpan.FromHours(24), retryAfter);
    }

    [Fact]
    public async Task A_restarted_process_sees_the_count_already_spent()
    {
        var time = TimeOn(4);
        var before = Budget(Limit(3), time);
        Assert.True((await before.TryAcquireAsync(Ct)).Granted);
        Assert.True((await before.TryAcquireAsync(Ct)).Granted);

        var afterRestart = Budget(Limit(3), time); // a new instance, as after a replica restart or on another replica

        Assert.True((await afterRestart.TryAcquireAsync(Ct)).Granted);
        Assert.False((await afterRestart.TryAcquireAsync(Ct)).Granted);
        Assert.False((await before.TryAcquireAsync(Ct)).Granted);
    }

    [Fact]
    public async Task Concurrent_callers_across_instances_never_exceed_the_limit()
    {
        var time = TimeOn(5);
        var budgets = Enumerable.Range(0, 3).Select(_ => Budget(Limit(40), time)).ToArray();

        var results = await Task.WhenAll(Enumerable.Range(0, 200).Select(async i =>
            (await Task.Run(() => budgets[i % budgets.Length].TryAcquireAsync(Ct), Ct)).Granted));

        Assert.Equal(40, results.Count(granted => granted));
    }

    [Fact]
    public async Task Rows_older_than_the_retention_are_purged_and_recent_ones_kept()
    {
        var old = TimeOn(6);
        var recent = TimeOn(7);
        await Budget(Limit(5), old).TryAcquireAsync(Ct);
        await Budget(Limit(5), recent).TryAcquireAsync(Ct);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RunnersDiary.Infrastructure.Persistence.RunnersDiaryDbContext>();
        // "Now" is just over the retention after the old day, and one day after the recent one.
        await GeocodeBudget.PurgeOlderThanAsync(db, old.GetUtcNow() + GeocodeBudget.Retention + TimeSpan.FromDays(1), Ct);

        var days = await db.GeocodeUsages.Select(g => g.Day).ToListAsync(Ct);
        Assert.DoesNotContain(DateOnly.FromDateTime(old.GetUtcNow().UtcDateTime), days);
        Assert.Contains(DateOnly.FromDateTime(recent.GetUtcNow().UtcDateTime), days);
    }
}
