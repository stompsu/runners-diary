using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Account;

[Collection(ContainersFixtureGroup.Name)]
public sealed class MeEndpointsTests(ContainersApiFactory factory)
{
    private static readonly Uri Me = new("/api/me", UriKind.Relative);

    [Fact]
    public async Task Entra_user_is_provisioned_just_in_time_with_defaults_and_the_row_is_reused()
    {
        factory.SkipIfDockerUnavailable();
        var oid = Guid.NewGuid();
        var first = factory.EntraUser("first", oid);
        var again = factory.EntraUser("again", oid);

        var a = await (await first.Client.GetAsync(Me, TestContext.Current.CancellationToken)).ReadAsync();
        var b = await (await again.Client.GetAsync(Me, TestContext.Current.CancellationToken)).ReadAsync();

        Assert.Equal(a.GetProperty("id").GetGuid(), b.GetProperty("id").GetGuid());
        Assert.Equal("User", a.GetProperty("kind").GetString());
        Assert.Equal("first", a.GetProperty("displayName").GetString());
        Assert.Equal("Etc/UTC", a.GetProperty("timeZoneId").GetString());
        Assert.True(a.GetProperty("timeZoneIsDefault").GetBoolean());
        Assert.Equal("Metric", a.GetProperty("unitSystem").GetString());
        Assert.Equal(JsonValueKind.Null, a.GetProperty("guestExpiresAt").ValueKind);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", a.GetProperty("today").GetString());
    }

    [Fact]
    public async Task Different_entra_identities_get_distinct_users()
    {
        factory.SkipIfDockerUnavailable();
        var u1 = factory.EntraUser("u1");
        var u2 = factory.EntraUser("u2");

        Assert.NotEqual(await u1.GetUserIdAsync(), await u2.GetUserIdAsync());
    }

    [Fact]
    public async Task Guest_sees_kind_expiry_and_today_in_Warsaw()
    {
        factory.SkipIfDockerUnavailable();
        // 23:30 UTC on 3 November is already 4 November in Warsaw (UTC+1).
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 23, 30, 0, TimeSpan.Zero));
        await using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock))));
        var guest = await host.GuestAsync("g", clock.GetUtcNow());

        var me = await (await guest.Client.GetAsync(Me, TestContext.Current.CancellationToken)).ReadAsync();

        Assert.Equal("Guest", me.GetProperty("kind").GetString());
        Assert.Equal("Europe/Warsaw", me.GetProperty("timeZoneId").GetString());
        Assert.Equal("2026-11-04", me.GetProperty("today").GetString());
        Assert.Equal("2026-11-04T23:30:00Z", me.GetProperty("guestExpiresAt").GetString());
    }

    [Fact]
    public async Task Put_updates_the_profile_and_existing_runs_keep_their_zone()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("tz");
        var created = await (await user.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative), Json.Run("2026-11-02"), TestContext.Current.CancellationToken)).ReadAsync();

        var put = await user.Client.PutAsJsonAsync(Me, new { displayName = "Zed", timeZoneId = "Europe/Warsaw", unitSystem = "Metric" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var me = await put.ReadAsync();
        Assert.Equal("Zed", me.GetProperty("displayName").GetString());
        Assert.Equal("Europe/Warsaw", me.GetProperty("timeZoneId").GetString());
        Assert.False(me.GetProperty("timeZoneIsDefault").GetBoolean());

        var run = await (await user.Client.GetAsync(new Uri("/api/runs/" + created.GetProperty("id").GetGuid(), UriKind.Relative), TestContext.Current.CancellationToken)).ReadAsync();
        Assert.Equal("Europe/Warsaw", run.GetProperty("timeZoneId").GetString()); // the run was created with an explicit zone
    }

    [Theory]
    [InlineData("""{"timeZoneId":"Central European Standard Time","unitSystem":"Metric"}""", "timeZoneId")]
    [InlineData("""{"timeZoneId":"nope","unitSystem":"Metric"}""", "timeZoneId")]
    [InlineData("""{"timeZoneId":"Europe/Warsaw","unitSystem":"Furlongs"}""", "")]
    [InlineData("""{"timeZoneId":"Europe/Warsaw","unitSystem":"Metric","userId":"00000000-0000-0000-0000-000000000001"}""", "")]
    [InlineData("""{"unitSystem":"Metric"}""", "")]
    public async Task Put_with_invalid_input_returns_400_problem(string body, string errorKey)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("bad");
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        var response = await user.Client.PutAsync(Me, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.ReadAsync();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        if (errorKey.Length > 0)
        {
            Assert.True(problem.GetProperty("errors").TryGetProperty(errorKey, out _), problem.ToString());
        }
    }
}
