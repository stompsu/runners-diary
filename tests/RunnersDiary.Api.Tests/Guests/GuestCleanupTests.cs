using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Guests;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Api.Tests.Guests;

/// <summary>AC6: expired guests lose rows and blobs, everybody else is untouched, and a blob failure never orphans blobs.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GuestCleanupTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GuestCleanup CleanupWith(FakeTimeProvider clock, IBlobStore? blobs = null) => new(
        factory.Services.GetRequiredService<IServiceScopeFactory>(),
        blobs ?? factory.Services.GetRequiredService<IBlobStore>(),
        clock,
        Options.Create(new GuestOptions { Cleanup = { BatchSize = 1000 } }),
        NullLogger<GuestCleanup>.Instance);

    private static FakeTimeProvider ClockNow() => new(DateTimeOffset.UtcNow);

    [Fact]
    public async Task Expired_guests_lose_rows_runs_tracks_and_blobs_while_others_stay_untouched()
    {
        factory.SkipIfDockerUnavailable();
        var expired = await factory.GuestSessionAsync("expired");
        var alive = await factory.GuestSessionAsync("alive");
        var entra = factory.EntraUser("real-user");
        var entraId = await entra.GetUserIdAsync();
        var expiredId = expired.UserId!.Value;
        var aliveId = alive.UserId!.Value;

        var aliveRuns = await factory.RunCountAsync(aliveId);
        var aliveBlobs = await factory.BlobNamesAsync(BlobPaths.UserPrefix(aliveId));
        Assert.True(await factory.RunCountAsync(expiredId) > 100);
        Assert.NotEmpty(await factory.BlobNamesAsync(BlobPaths.UserPrefix(expiredId)));

        var clock = ClockNow();
        await factory.SetGuestExpiryAsync(expiredId, clock.GetUtcNow().UtcDateTime.AddHours(-2));

        var removed = await CleanupWith(clock).RunOnceAsync(Ct);

        Assert.True(removed >= 1);
        Assert.False(await factory.UserExistsAsync(expiredId));
        Assert.Equal(0, await factory.RunCountAsync(expiredId));
        Assert.Empty(await factory.TracksAsync(expiredId));
        Assert.Empty(await factory.BlobNamesAsync(BlobPaths.UserPrefix(expiredId)));

        Assert.True(await factory.UserExistsAsync(aliveId));
        Assert.Equal(aliveRuns, await factory.RunCountAsync(aliveId));
        Assert.Equal(aliveBlobs.Order(StringComparer.Ordinal), (await factory.BlobNamesAsync(BlobPaths.UserPrefix(aliveId))).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.OK, (await alive.Client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct)).StatusCode);
        Assert.True(await factory.UserExistsAsync(entraId));
    }

    [Fact]
    public async Task A_guest_is_removed_only_after_the_grace_period_has_passed()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("grace");
        var id = guest.UserId!.Value;
        var clock = ClockNow();
        var cleanup = CleanupWith(clock);

        // Expired 30 minutes ago: the token is dead already, but the row stays for the one-hour grace.
        await factory.SetGuestExpiryAsync(id, clock.GetUtcNow().UtcDateTime.AddMinutes(-30));
        await cleanup.RunOnceAsync(Ct);
        Assert.True(await factory.UserExistsAsync(id));

        clock.Advance(TimeSpan.FromMinutes(31));
        await cleanup.RunOnceAsync(Ct);
        Assert.False(await factory.UserExistsAsync(id));
        Assert.Empty(await factory.BlobNamesAsync(BlobPaths.UserPrefix(id)));
    }

    [Fact]
    public async Task Cleanup_removes_both_blobs_of_every_demo_track()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("two-blobs");
        var id = guest.UserId!.Value;
        Assert.Equal(12, (await factory.BlobNamesAsync(BlobPaths.UserPrefix(id))).Count);
        var clock = ClockNow();
        await factory.SetGuestExpiryAsync(id, clock.GetUtcNow().UtcDateTime.AddHours(-2));

        await CleanupWith(clock).RunOnceAsync(Ct);

        Assert.Empty(await factory.BlobNamesAsync(BlobPaths.UserPrefix(id)));
        Assert.Empty(await factory.TracksAsync(id));
    }

    [Fact]
    public async Task When_blob_deletion_fails_the_user_row_is_kept_and_the_next_run_finishes_the_job()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("faulty");
        var id = guest.UserId!.Value;
        var clock = ClockNow();
        await factory.SetGuestExpiryAsync(id, clock.GetUtcNow().UtcDateTime.AddHours(-3));
        var faulty = new FaultyBlobStore(factory.Services.GetRequiredService<IBlobStore>()) { FailDeletes = true };

        await CleanupWith(clock, faulty).RunOnceAsync(Ct);

        Assert.True(await factory.UserExistsAsync(id));
        Assert.True(await factory.RunCountAsync(id) > 0);
        Assert.NotEmpty(await factory.BlobNamesAsync(BlobPaths.UserPrefix(id)));

        faulty.FailDeletes = false;
        await CleanupWith(clock, faulty).RunOnceAsync(Ct);

        Assert.False(await factory.UserExistsAsync(id));
        Assert.Empty(await factory.BlobNamesAsync(BlobPaths.UserPrefix(id)));
    }

    [Fact]
    public async Task Cleanup_never_deletes_real_users_even_with_a_far_future_clock()
    {
        factory.SkipIfDockerUnavailable();
        var entra = factory.EntraUser("immortal");
        var id = await entra.GetUserIdAsync();
        var clock = ClockNow();
        clock.Advance(TimeSpan.FromDays(3650));

        await CleanupWith(clock).RunOnceAsync(Ct);

        Assert.True(await factory.UserExistsAsync(id));
    }

    [Fact]
    public async Task Creating_a_session_triggers_an_opportunistic_cleanup_when_enabled()
    {
        factory.SkipIfDockerUnavailable();
        var victim = await factory.GuestSessionAsync("victim");
        var id = victim.UserId!.Value;
        await factory.SetGuestExpiryAsync(id, DateTime.UtcNow.AddHours(-5));
        await using var enabled = factory.WithWebHostBuilder(b => b.UseSetting("Guests:Cleanup:Enabled", "true"));
        using var client = enabled.CreateClient();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync(new Uri("/api/guest-sessions", UriKind.Relative), content: null, Ct)).StatusCode);

        // The run is fire-and-forget; poll briefly.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (await factory.UserExistsAsync(id) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, Ct);
        }

        Assert.False(await factory.UserExistsAsync(id));
        Assert.Empty(await factory.BlobNamesAsync(BlobPaths.UserPrefix(id)));
    }
}
