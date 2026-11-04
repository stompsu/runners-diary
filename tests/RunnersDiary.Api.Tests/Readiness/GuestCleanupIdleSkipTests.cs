using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Guests;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Tests.Readiness;

/// <summary>ADR 0032 section 2: the hosted cleanup runs only if a SQL connection opened since the previous tick.</summary>
public sealed class GuestCleanupIdleSkipTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class CountingPass(Action? onRun = null, Exception? failure = null) : IGuestCleanupPass
    {
        public int Runs { get; private set; }

        public Task<int> RunOnceAsync(CancellationToken cancellationToken)
        {
            Runs++;
            onRun?.Invoke();
            return failure is null ? Task.FromResult(0) : Task.FromException<int>(failure);
        }
    }

    // The interceptor ignores both arguments, so the tests need not fabricate EF event data.
    private static void OpenConnection(SqlActivityInterceptor interceptor) => interceptor.ConnectionOpened(null!, null!);

    private static GuestCleanupService ServiceFor(IGuestCleanupPass pass, SqlActivityInterceptor interceptor) => new(
        pass, interceptor, Options.Create(new GuestOptions()), new FakeTimeProvider(), NullLogger<GuestCleanupService>.Instance);

    [Fact]
    public async Task A_tick_without_any_sql_connection_is_skipped()
    {
        var pass = new CountingPass();
        var service = ServiceFor(pass, new SqlActivityInterceptor());

        Assert.False(await service.TickAsync(Ct));
        Assert.False(await service.TickAsync(Ct));
        Assert.Equal(0, pass.Runs);
    }

    [Fact]
    public async Task A_tick_after_sql_activity_runs_once_and_the_next_idle_tick_is_skipped()
    {
        var interceptor = new SqlActivityInterceptor();
        var pass = new CountingPass();
        var service = ServiceFor(pass, interceptor);

        OpenConnection(interceptor);
        Assert.True(await service.TickAsync(Ct));
        Assert.False(await service.TickAsync(Ct));
        Assert.Equal(1, pass.Runs);

        await interceptor.ConnectionOpenedAsync(null!, null!, Ct);
        Assert.True(await service.TickAsync(Ct));
        Assert.Equal(2, pass.Runs);
    }

    [Fact]
    public async Task Connections_opened_by_the_cleanup_pass_itself_do_not_trigger_the_next_tick()
    {
        var interceptor = new SqlActivityInterceptor();
        var pass = new CountingPass(onRun: () => OpenConnection(interceptor));
        var service = ServiceFor(pass, interceptor);

        OpenConnection(interceptor);
        Assert.True(await service.TickAsync(Ct));
        Assert.False(await service.TickAsync(Ct));
        Assert.Equal(1, pass.Runs);
    }

    [Fact]
    public async Task A_failing_pass_is_swallowed_and_not_retried_until_there_is_new_activity()
    {
        var interceptor = new SqlActivityInterceptor();
        var pass = new CountingPass(failure: new InvalidOperationException("boom"));
        var service = ServiceFor(pass, interceptor);

        OpenConnection(interceptor);
        Assert.True(await service.TickAsync(Ct));
        Assert.False(await service.TickAsync(Ct));
        Assert.Equal(1, pass.Runs);
    }

    [Fact]
    public void The_interceptor_counts_every_opened_connection()
    {
        var interceptor = new SqlActivityInterceptor();
        Assert.Equal(0, interceptor.OpenedCount);

        OpenConnection(interceptor);
        OpenConnection(interceptor);

        Assert.Equal(2, interceptor.OpenedCount);
    }
}

/// <summary>The interceptor is wired into the real DbContext: a query through the host's options counts as activity.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class SqlActivityInterceptorWiringTests(ContainersApiFactory factory)
{
    [Fact]
    public async Task A_real_query_through_the_host_dbcontext_increments_the_counter()
    {
        factory.SkipIfDockerUnavailable();
        var interceptor = factory.Services.GetRequiredService<SqlActivityInterceptor>();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RunnersDiaryDbContext>();
        var before = interceptor.OpenedCount;

        _ = await db.Users.AsNoTracking().CountAsync(TestContext.Current.CancellationToken);

        Assert.True(interceptor.OpenedCount > before);
    }
}
