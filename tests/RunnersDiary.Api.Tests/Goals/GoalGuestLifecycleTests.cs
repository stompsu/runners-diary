using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Guests;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Storage;
using static RunnersDiary.Api.Tests.Goals.GoalTestSupport;

namespace RunnersDiary.Api.Tests.Goals;

/// <summary>AC2: guest cleanup removes goals, suggestions and runs also when a goal links a run and a run links the goal (ADR 0039 §3), through the real cleanup pass.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GoalGuestLifecycleTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(TestActor Guest, Guid GoalId)> GuestWithMutualLinksAsync()
    {
        var guest = await factory.GuestAsync("goal-cleanup-" + Guid.NewGuid().ToString("N")[..6]);
        var race = await guest.CreatePlannedRaceRunAsync(45);
        var goal = await guest.CreateGoalAsync(g =>
        {
            g["type"] = "Race";
            g["eventName"] = "Linked race";
            g["targetDate"] = Ahead(45);
            g["raceRunId"] = race.GetProperty("id").GetGuid(); // Goals.RaceRunId -> Runs
        });
        var goalId = goal.GetProperty("id").GetGuid();
        var ids = await factory.SeedSuggestionsAsync(guest, goalId, Session(10), Session(12), Session(14));
        Assert.Equal(System.Net.HttpStatusCode.Created, (await guest.AcceptAsync(ids[0])).StatusCode); // Runs.GoalId -> Goals
        return (guest, goalId);
    }

    [Fact]
    public async Task Cleanup_of_an_expired_guest_removes_goals_suggestions_and_linked_runs_and_nothing_of_others()
    {
        factory.SkipIfDockerUnavailable();
        var (expired, _) = await GuestWithMutualLinksAsync();
        var (alive, aliveGoal) = await GuestWithMutualLinksAsync();
        var expiredId = expired.UserId!.Value;
        var aliveId = alive.UserId!.Value;
        Assert.Equal(1, await factory.GoalRowsAsync(expiredId));
        Assert.Equal(2, await factory.SuggestionRowsAsync(expiredId));
        Assert.Equal(2, await factory.RunCountAsync(expiredId));

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await factory.SetGuestExpiryAsync(expiredId, clock.GetUtcNow().UtcDateTime.AddHours(-2));
        var cleanup = new GuestCleanup(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), factory.Services.GetRequiredService<IBlobStore>(), clock,
            Options.Create(new GuestOptions { Cleanup = { BatchSize = 1000 } }), NullLogger<GuestCleanup>.Instance);
        Assert.True(await cleanup.RunOnceAsync(Ct) >= 1);

        Assert.False(await factory.UserExistsAsync(expiredId));
        Assert.Equal(0, await factory.GoalRowsAsync(expiredId));
        Assert.Equal(0, await factory.SuggestionRowsAsync(expiredId));
        Assert.Equal(0, await factory.RunCountAsync(expiredId));

        Assert.Equal(1, await factory.GoalRowsAsync(aliveId));
        Assert.Equal(2, await factory.SuggestionRowsAsync(aliveId));
        Assert.Equal(2, await factory.RunCountAsync(aliveId));
        var survivor = await alive.GetGoalJsonAsync(aliveGoal);
        Assert.Equal(aliveGoal, survivor.GetProperty("id").GetGuid());
        Assert.Equal(1, await factory.WithDbAsync(db => db.Goals.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).CountAsync(g => g.RaceRunId != null && g.UserId == aliveId, Ct)));
    }
}
