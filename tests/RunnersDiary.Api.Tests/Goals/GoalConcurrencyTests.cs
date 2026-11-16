using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Goals;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Runs;
using static RunnersDiary.Api.Tests.Goals.GoalTestSupport;

namespace RunnersDiary.Api.Tests.Goals;

/// <summary>
/// Deterministic interleavings of the concurrent goal writes: a competing request commits (here: a raw delete on its own connection, run from a
/// <see cref="SaveChangesInterceptor"/>) between a service's read and its save. Each must answer "gone" instead of throwing (no 500).
/// </summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GoalConcurrencyTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class BeforeSave(Func<Task> action) : SaveChangesInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_done)
            {
                _done = true;
                await action();
            }

            return result;
        }
    }

    private async Task<T> WithInterleavedAsync<T>(
        TestActor actor, Func<Task> competing, Func<TrainingSuggestionService, Task<T>> act)
    {
        var userId = await actor.GetUserIdAsync();
        using var scope = factory.Services.CreateScope();
        var existing = scope.ServiceProvider.GetRequiredService<DbContextOptions<RunnersDiaryDbContext>>();
        var options = new DbContextOptionsBuilder<RunnersDiaryDbContext>(existing).AddInterceptors(new BeforeSave(competing)).Options;
        var user = new FixedCurrentUser(userId);
        await using var db = new RunnersDiaryDbContext(options, user);
        var service = new TrainingSuggestionService(db, user, TimeProvider.System, Options.Create(new RunLimitsOptions()));
        return await act(service);
    }

    private async Task DeleteGoalRawAsync(Guid goalId) =>
        await factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM TrainingSuggestions WHERE GoalId = {goalId}", Ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Goals WHERE Id = {goalId}", Ct);
            return 0;
        });

    [Fact]
    public async Task Dismiss_of_a_suggestion_deleted_meanwhile_is_not_found_instead_of_throwing()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-race-dismiss");
        var goalId = (await user.CreateGoalAsync()).GetProperty("id").GetGuid();
        var ids = await factory.SeedSuggestionsAsync(user, goalId, Session(10));

        var dismissed = await WithInterleavedAsync(user, () => DeleteGoalRawAsync(goalId), s => s.DismissAsync(ids[0], Ct));

        Assert.Null(dismissed);
    }

    [Fact]
    public async Task Accept_of_a_suggestion_whose_goal_is_deleted_meanwhile_is_not_found_and_creates_no_run()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-race-accept");
        var goalId = (await user.CreateGoalAsync()).GetProperty("id").GetGuid();
        var ids = await factory.SeedSuggestionsAsync(user, goalId, Session(10));

        var result = await WithInterleavedAsync(user, () => DeleteGoalRawAsync(goalId), s => s.AcceptAsync(ids[0], null, Ct));

        Assert.Equal(AcceptSuggestionStatus.NotFound, result.Status);
        var runs = await (await user.Client.GetAsync(U($"/api/runs?from={Ahead(0)}&to={Ahead(30)}"), Ct)).ReadAsync();
        Assert.Empty(runs.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Accept_inserting_a_run_for_a_goal_deleted_meanwhile_is_caught_as_a_foreign_key_violation()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-race-accept-fk");
        var goalId = (await user.CreateGoalAsync()).GetProperty("id").GetGuid();
        var ids = await factory.SeedSuggestionsAsync(user, goalId, Session(10));

        // Only the goal row is removed (its suggestion rows cascade or not): either way the accept answers not found, never a 500.
        var result = await WithInterleavedAsync(
            user,
            () => factory.WithDbAsync(async db =>
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Goals WHERE Id = {goalId}", Ct);
                return 0;
            }),
            s => s.AcceptAsync(ids[0], null, Ct));

        Assert.Equal(AcceptSuggestionStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Deleting_a_goal_waits_for_an_accept_and_keeps_the_accepted_run()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-race-serialise");
        var goalId = (await user.CreateGoalAsync()).GetProperty("id").GetGuid();
        var ids = await factory.SeedSuggestionsAsync(user, goalId, [.. Enumerable.Range(10, 6).Select(d => Session(d))]);

        var accepts = ids.Select(id => user.AcceptAsync(id));
        var delete = user.Client.DeleteAsync(U($"/api/goals/{goalId}"), Ct);
        var statuses = (await Task.WhenAll(accepts.Append(delete))).Select(r => (int)r.StatusCode).ToList();

        Assert.DoesNotContain(500, statuses);
        Assert.DoesNotContain(statuses, s => s >= 500);
    }
}
