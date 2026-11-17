using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Goals;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using static RunnersDiary.Api.Tests.Goals.GoalTestSupport;

namespace RunnersDiary.Api.Tests.Goals;

/// <summary>
/// Deterministic tests of the per-user goal lock (<c>sp_getapplock</c>, owner = transaction): the test holds the lock in its own transaction
/// and shows that a goal write of the same user waits, that another user's write does not, and that commit and rollback both release it.
/// Also the hard goal cap: the count runs under the lock, so concurrent creates at cap-1 produce exactly one goal.
/// </summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GoalWriteLockTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Long enough to call a request "still waiting"; short enough to keep the suite fast.</summary>
    private static readonly TimeSpan Waiting = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);

    /// <summary>A transaction on its own connection that owns the user's goal lock until <see cref="ReleaseAsync"/>.</summary>
    private sealed class HeldLock : IAsyncDisposable
    {
        private readonly IServiceScope _scope;
        private readonly RunnersDiaryDbContext _db;
        private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _transaction;

        private HeldLock(IServiceScope scope, RunnersDiaryDbContext db)
        {
            _scope = scope;
            _db = db;
        }

        public static async Task<HeldLock> AcquireAsync(ContainersApiFactory factory, Guid userId)
        {
            var scope = factory.Services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<RunnersDiaryDbContext>>();
            var held = new HeldLock(scope, new RunnersDiaryDbContext(options, new FixedCurrentUser(userId)));
            held._transaction = await held._db.Database.BeginTransactionAsync(Ct);
            await GoalWriteLock.AcquireAsync(held._db, userId, Ct);
            return held;
        }

        public async Task ReleaseAsync(bool commit)
        {
            if (_transaction is null)
            {
                return;
            }

            if (commit)
            {
                await _transaction.CommitAsync(Ct);
            }
            else
            {
                await _transaction.RollbackAsync(Ct);
            }

            await _transaction.DisposeAsync();
            _transaction = null;
        }

        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync(commit: false);
            await _db.DisposeAsync();
            _scope.Dispose();
        }
    }

    private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout) =>
        await Task.WhenAny(task, Task.Delay(timeout, Ct)) == task;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_create_of_the_same_user_waits_for_the_held_lock_and_proceeds_after_commit_or_rollback(bool commit)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser($"goal-lock-create-{commit}");
        var held = await HeldLock.AcquireAsync(factory, await user.GetUserIdAsync());
        await using var _ = held;

        var create = user.PostGoalAsync(Goal());

        Assert.False(await CompletesWithinAsync(create, Waiting), "The create must wait while another transaction holds the user's goal lock.");
        await held.ReleaseAsync(commit);
        Assert.True(await CompletesWithinAsync(create, Generous), "The create must proceed once the lock is released.");
        Assert.Equal(HttpStatusCode.Created, (await create).StatusCode);
    }

    [Fact]
    public async Task A_delete_of_the_same_user_waits_for_the_held_lock()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-lock-delete");
        var goalId = (await user.CreateGoalAsync()).GetProperty("id").GetGuid();
        var held = await HeldLock.AcquireAsync(factory, await user.GetUserIdAsync());
        await using var _ = held;

        var delete = user.Client.DeleteAsync(U($"/api/goals/{goalId}"), Ct);

        Assert.False(await CompletesWithinAsync(delete, Waiting));
        await held.ReleaseAsync(commit: true);
        Assert.True(await CompletesWithinAsync(delete, Generous));
        Assert.Equal(HttpStatusCode.NoContent, (await delete).StatusCode);
    }

    [Fact]
    public async Task Another_users_goal_writes_are_not_blocked_by_the_held_lock()
    {
        factory.SkipIfDockerUnavailable();
        var holder = factory.EntraUser("goal-lock-holder");
        var other = factory.EntraUser("goal-lock-other");
        var held = await HeldLock.AcquireAsync(factory, await holder.GetUserIdAsync());
        await using var _ = held;

        var otherCreate = other.PostGoalAsync(Goal());

        Assert.True(await CompletesWithinAsync(otherCreate, Generous), "A different user's write must not wait for the holder's lock.");
        Assert.Equal(HttpStatusCode.Created, (await otherCreate).StatusCode);
        await held.ReleaseAsync(commit: false);
    }

    [Fact]
    public async Task The_lock_is_free_again_after_a_released_transaction()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-lock-reacquire");
        var userId = await user.GetUserIdAsync();
        await using (var first = await HeldLock.AcquireAsync(factory, userId))
        {
            await first.ReleaseAsync(commit: false);
        }

        // Re-acquiring succeeds at once (it would wait the full lock timeout if the rollback had leaked the lock).
        var second = HeldLock.AcquireAsync(factory, userId);
        Assert.True(await CompletesWithinAsync(second, Generous));
        await (await second).DisposeAsync();
    }

    [Fact]
    public async Task Concurrent_creates_at_the_cap_minus_one_yield_exactly_one_goal_and_one_goal_limit()
    {
        factory.SkipIfDockerUnavailable();
        using var capped = factory.WithWebHostBuilder(b => b.UseSetting("Goals:MaxGoalsPerUser", "2"));
        var user = capped.EntraUser("goal-cap-hard");
        await user.CreateGoalAsync();
        var held = await HeldLock.AcquireAsync(factory, await user.GetUserIdAsync());
        await using var _ = held;

        // Both creates are queued behind the held lock, then released together: before the count moved under the lock, both read "1 < 2".
        var first = user.PostGoalAsync(Goal());
        var second = user.PostGoalAsync(Goal());
        Assert.False(await CompletesWithinAsync(Task.WhenAll(first, second), Waiting));
        await held.ReleaseAsync(commit: true);
        var responses = await Task.WhenAll(first, second);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var rejected = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("urn:runnersdiary:problem:goal-limit", (await rejected.ProblemAsync(HttpStatusCode.Conflict)).ProblemType());
        var list = await (await user.Client.GetAsync(U("/api/goals"), Ct)).ReadAsync();
        Assert.Equal(2, list.GetProperty("items").GetArrayLength());
    }
}
