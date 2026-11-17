using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Demo;
using RunnersDiary.Domain.Time;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Goals;
using RunnersDiary.Infrastructure.Guests;
using RunnersDiary.Infrastructure.Storage;
using static RunnersDiary.Api.Tests.Goals.GoalTestSupport;

namespace RunnersDiary.Api.Tests.Goals;

/// <summary>AC6 (M9 step 6): every fresh guest gets the demo goal with its example plan, from rows, without a model call or quota use.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class DemoGoalSeedTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_fresh_guest_sees_the_demo_goal_and_its_example_plan_in_weeks_6_to_10()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("demo-plan");
        var userId = guest.UserId!.Value;

        var goals = (await (await guest.Client.GetAsync(U("/api/goals"), Ct)).ReadAsync()).GetProperty("items");
        var goal = Assert.Single(goals.EnumerateArray());
        Assert.Equal("Demo Beskids Trail 25K", goal.GetProperty("title").GetString());
        Assert.Equal("Distance", goal.GetProperty("type").GetString());
        Assert.Equal(25_000, goal.GetProperty("distanceMeters").GetDouble());
        Assert.Equal(900, goal.GetProperty("elevationGainMeters").GetDouble());
        Assert.Equal("Trail", goal.GetProperty("surface").GetString());
        Assert.Equal("Active", goal.GetProperty("status").GetString());
        var plan = goal.GetProperty("plan");
        Assert.Equal("Demo", plan.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("model").ValueKind);
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("promptVersion").ValueKind);
        Assert.False(plan.GetProperty("outdated").GetBoolean());
        Assert.False(plan.GetProperty("horizonCapped").GetBoolean());
        Assert.Equal(20, plan.GetProperty("pendingCount").GetInt32());
        Assert.Equal(0, plan.GetProperty("acceptedCount").GetInt32());

        // The target is the Sunday of week +10 in the guest's zone; the plan covers weeks +6 .. +10 and ends the day before.
        var target = DateOnly.Parse(goal.GetProperty("targetDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(DayOfWeek.Sunday, target.DayOfWeek);
        var weekSix = target.AddDays(-(4 * 7) - 6);
        Assert.Equal(DayOfWeek.Monday, weekSix.DayOfWeek);

        var response = await guest.Client.GetAsync(U($"/api/training-suggestions?from={weekSix:yyyy-MM-dd}&to={target:yyyy-MM-dd}"), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.ReadAsync()).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(20, items.Count);
        Assert.All(items, i => Assert.Equal(goal.GetProperty("id").GetGuid(), i.GetProperty("goalId").GetGuid()));
        Assert.All(items, i =>
        {
            var date = DateOnly.Parse(i.GetProperty("date").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(date, weekSix, target.AddDays(-1));
        });
        Assert.Equal(20, await factory.SuggestionRowsAsync(userId));
        Assert.Equal(1, await factory.GoalRowsAsync(userId));

        // A demo suggestion is a real row: it can be accepted into a planned run.
        var first = items[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created, (await guest.AcceptAsync(first)).StatusCode);
    }

    [Fact]
    public async Task Seeding_the_demo_plan_spends_no_ai_quota()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("demo-plan-quota");
        var userId = guest.UserId!.Value;

        Assert.Equal(0, await factory.WithDbAsync(db => db.AiGenerations.AsNoTracking().CountAsync(g => g.UserId == userId, Ct)));
    }

    [Fact]
    public async Task Cleanup_removes_the_demo_goal_and_suggestions_of_an_expired_guest_and_keeps_those_of_others()
    {
        factory.SkipIfDockerUnavailable();
        var expired = await factory.GuestSessionAsync("demo-plan-expired");
        var alive = await factory.GuestSessionAsync("demo-plan-alive");
        var expiredId = expired.UserId!.Value;
        var aliveId = alive.UserId!.Value;
        Assert.Equal(20, await factory.SuggestionRowsAsync(expiredId));

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await factory.SetGuestExpiryAsync(expiredId, clock.GetUtcNow().UtcDateTime.AddHours(-2));
        var cleanup = new GuestCleanup(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), factory.Services.GetRequiredService<IBlobStore>(), clock,
            Options.Create(new GuestOptions { Cleanup = { BatchSize = 1000 } }), NullLogger<GuestCleanup>.Instance);
        Assert.True(await cleanup.RunOnceAsync(Ct) >= 1);

        Assert.False(await factory.UserExistsAsync(expiredId));
        Assert.Equal(0, await factory.GoalRowsAsync(expiredId));
        Assert.Equal(0, await factory.SuggestionRowsAsync(expiredId));
        Assert.Equal(1, await factory.GoalRowsAsync(aliveId));
        Assert.Equal(20, await factory.SuggestionRowsAsync(aliveId));
    }

    [Fact]
    public async Task An_invalid_demo_plan_does_not_block_guest_creation_and_seeds_no_goal()
    {
        factory.SkipIfDockerUnavailable();
        var directory = Directory.CreateTempSubdirectory("rd-bad-plan").FullName;
        try
        {
            var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "seed", "demo", "training-plan.json"), Ct);
            await File.WriteAllTextAsync(Path.Combine(directory, "training-plan.json"), json.Replace("\"phase\": \"build\"", "\"phase\": \"bogus\"", StringComparison.Ordinal), Ct);
            var guestOptions = Options.Create(new GuestOptions { DemoDataPath = directory });
            var localizer = new DemoTextLocalizer(guestOptions);
            var badPlan = new DemoTrainingPlan(guestOptions, localizer);
            var loader = factory.Services.GetRequiredService<DemoDatasetLoader>();
            var seeder = new DemoDataSeeder(
                loader, badPlan, localizer, factory.Services.GetRequiredService<IBlobStore>(), factory.Services.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System, NullLogger<DemoDataSeeder>.Instance);

            var dataset = loader.Get();
            var today = UserToday.Get(TimeProvider.System, dataset.Template.TimeZoneId);
            Assert.Throws<DomainRuleException>(() => badPlan.Build(Guid.NewGuid(), DemoScheduleExpander.Expand(dataset.Template, today), today, DateTimeOffset.UtcNow));

            var guest = User.CreateGuest(DateTimeOffset.UtcNow);
            await seeder.SeedAsync(guest, Ct);

            Assert.True(await factory.UserExistsAsync(guest.Id));
            Assert.True(await factory.RunCountAsync(guest.Id) > 100);
            Assert.Equal(0, await factory.GoalRowsAsync(guest.Id));
            Assert.Equal(0, await factory.SuggestionRowsAsync(guest.Id));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void The_demo_plan_target_is_the_sunday_of_week_10()
    {
        var today = new DateOnly(2028, 3, 15);
        Assert.Equal(IsoWeek.MondayOf(today).AddDays(76), RunnersDiary.Domain.Goals.Planning.DemoTrainingPlanTemplate.DateOf(today, 10, DayOfWeek.Sunday));
    }
}
