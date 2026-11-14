using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Infrastructure.Guests;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Api.Tests.Races;

/// <summary>AC2: a guest gets the demo interest profile, and cleanup removes the profile and the states with the guest while the shared catalogue stays.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class RaceGuestLifecycleTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_guest_gets_the_demo_interest_profile()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("race-profile");
        var id = guest.UserId!.Value;

        var profile = await factory.WithDbAsync(db => db.RaceInterestProfiles.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).AsNoTracking()
            .SingleAsync(p => p.UserId == id, Ct));

        Assert.Equal(RaceKinds.Road | RaceKinds.Trail | RaceKinds.Mountain, profile.Kinds);
        Assert.Equal(["PL-24"], profile.RegionCodes);
        Assert.Equal(new GeoPoint(50.2945, 18.6714), profile.Center);
        Assert.Equal("Gliwice", profile.CenterName);
        Assert.Equal(60_000, profile.RadiusMeters);
        Assert.Equal(5_000, profile.MinDistanceMeters);
        Assert.Equal(42_195, profile.MaxDistanceMeters);
        Assert.Equal(120, profile.HorizonDays);
        Assert.True(profile.ShowInCalendar);
    }

    [Fact]
    public async Task Guest_cleanup_removes_the_profile_and_states_of_an_expired_guest_and_nothing_of_others_or_the_catalogue()
    {
        factory.SkipIfDockerUnavailable();
        var expired = await factory.GuestSessionAsync("race-expired");
        var alive = await factory.GuestSessionAsync("race-alive");
        var expiredId = expired.UserId!.Value;
        var aliveId = alive.UserId!.Value;

        var race = Race.Create(new RaceFacts(
            RaceSource.Demo, "cleanup-" + Guid.NewGuid().ToString("N"), "Demo Cleanup Race", new DateOnly(2027, 1, 10), null, "Gliwice",
            new GeoPoint(50.2945, 18.6714), "PL", "PL-24", RaceKind.Road, Surface.Road, [10_000], null, null, null, null, null, null, null, null), DateTimeOffset.UtcNow);
        await factory.WithDbAsync(async db =>
        {
            db.Races.Add(race);
            await db.SaveChangesAsync(Ct);
            return 0;
        });
        foreach (var id in new[] { expiredId, aliveId })
        {
            await WithOwnerAsync(factory, id, async db =>
            {
                db.RaceProposalStates.Add(RaceProposalState.Create(id, race.Id, RaceProposalMark.Interested, race.Date, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync(Ct);
            });
        }

        Assert.Equal(1, await CountAsync(factory, expiredId));
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await factory.SetGuestExpiryAsync(expiredId, clock.GetUtcNow().UtcDateTime.AddHours(-2));

        var cleanup = new GuestCleanup(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), factory.Services.GetRequiredService<IBlobStore>(), clock,
            Options.Create(new GuestOptions { Cleanup = { BatchSize = 1000 } }), NullLogger<GuestCleanup>.Instance);
        Assert.True(await cleanup.RunOnceAsync(Ct) >= 1);

        Assert.False(await factory.UserExistsAsync(expiredId));
        Assert.Equal(0, await CountAsync(factory, expiredId));
        Assert.Equal(0, await factory.WithDbAsync(db => db.RaceInterestProfiles.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).CountAsync(p => p.UserId == expiredId, Ct)));
        Assert.Equal(0, await factory.WithDbAsync(db => db.RaceProposalStates.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).CountAsync(s => s.UserId == expiredId, Ct)));

        Assert.Equal(1, await CountAsync(factory, aliveId));
        Assert.Equal(1, await factory.WithDbAsync(db => db.RaceInterestProfiles.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).CountAsync(p => p.UserId == aliveId, Ct)));
        Assert.Equal(1, await factory.WithDbAsync(db => db.Races.CountAsync(r => r.Id == race.Id, Ct)));
    }

    private static Task<int> CountAsync(ContainersApiFactory factory, Guid userId) =>
        factory.WithDbAsync(db => db.RaceProposalStates.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).CountAsync(s => s.UserId == userId, Ct));

    private static async Task WithOwnerAsync(ContainersApiFactory factory, Guid userId, Func<RunnersDiaryDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<RunnersDiaryDbContext>>();
        await using var db = new RunnersDiaryDbContext(options, new RunnersDiary.Infrastructure.Identity.FixedCurrentUser(userId, isGuest: true));
        await action(db);
    }
}
