using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Api.Auth;
using RunnersDiary.Api.RateLimiting;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Guests;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Api.Tests.Hardening;

/// <summary>Regression tests for the M2 review findings that need no Docker.</summary>
public sealed class ReviewFixesOfflineTests(OfflineApiFactory factory) : IClassFixture<OfflineApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- M1: forwarded headers must be a conscious choice ---------------------------------------------------------

    [Fact]
    public void Forwarded_headers_switch_must_be_configured_outside_Development()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("ForwardedHeaders:Enabled", ""));

        var ex = Assert.Throws<InvalidOperationException>(() => host.CreateClient());
        Assert.Contains("ForwardedHeaders:Enabled", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Forwarded_headers_switch_must_be_a_boolean()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("ForwardedHeaders:Enabled", "maybe"));

        Assert.Throws<InvalidOperationException>(() => host.CreateClient());
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void Forwarded_headers_switch_set_explicitly_starts(string value)
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("ForwardedHeaders:Enabled", value));

        using var client = host.CreateClient();
        Assert.NotNull(client);
    }

    [Fact]
    public void Forwarded_headers_switch_may_stay_unset_in_Development()
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("ForwardedHeaders:Enabled", "");
        });

        using var client = host.CreateClient();
        Assert.NotNull(client);
    }

    // ---- M2: partition keys ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("198.51.100.9", "198.51.100.9")]
    [InlineData("::ffff:198.51.100.9", "198.51.100.9")]
    [InlineData("2001:db8:1:2:3:4:5:6", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:ffff:ffff:ffff:ffff", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:3::1", "2001:db8:1:3::/64")]
    [InlineData("::1", "::/64")]
    public void Client_partition_key_collapses_ipv6_to_64_and_maps_ipv4_mapped_addresses(string address, string expected) =>
        Assert.Equal(expected, ClientPartition.Key(IPAddress.Parse(address)));

    [Fact]
    public void Client_partition_key_without_an_address_is_unknown() => Assert.Equal("unknown", ClientPartition.Key(null));

    // ---- L3: opportunistic cleanup does not inherit the request ---------------------------------------------------

    [Fact]
    public async Task Opportunistic_cleanup_runs_without_the_triggering_requests_http_context()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AppClaims.UserId, Guid.NewGuid().ToString())], "test")) },
        };
        var seen = new TaskCompletionSource<HttpContext?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new GuestCleanup(
            new RecordingScopeFactory(() => seen.TrySetResult(accessor.HttpContext)),
            new NoBlobs(),
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            Options.Create(new GuestOptions()),
            NullLogger<GuestCleanup>.Instance);

        cleanup.RequestOpportunisticRun();

        Assert.NotNull(accessor.HttpContext); // the caller's own flow is untouched
        Assert.Null(await seen.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct));
    }

    private sealed class RecordingScopeFactory(Action onCreate) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            onCreate();
            throw new InvalidOperationException("Recorded; no real scope is needed.");
        }
    }

    private sealed class NoBlobs : IBlobStore
    {
        public Task UploadAsync(string blobName, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<bool> AnyAsync(string prefix, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    // ---- L5/L6: client errors ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("/api/runs")]
    [InlineData("/api/guest-sessions")]
    public async Task Oversized_bodies_get_413_before_authentication_is_even_consulted(string path)
    {
        using var client = factory.CreateClient();
        using var content = new StringContent(new string('x', 70_000), System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync(new Uri(path, UriKind.Relative), content, Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // ---- L8: partial Entra configuration ----------------------------------------------------------------------------

    [Theory]
    [InlineData("Auth:Entra:TenantSubdomain", "contoso")]
    [InlineData("Auth:Entra:Audience", "11111111-1111-1111-1111-111111111111")]
    public void Any_single_Entra_identifier_is_a_partial_configuration_and_fails_startup(string key, string value)
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Auth:Entra:Enabled", "");
            b.UseSetting(key, value);
        });

        Assert.Throws<InvalidOperationException>(() => host.CreateClient());
    }

    [Fact]
    public void Partial_Entra_configuration_also_fails_in_Development_unless_explicitly_disabled()
    {
        using var partial = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Auth:Entra:Enabled", "");
            b.UseSetting("Auth:Entra:TenantSubdomain", "contoso");
        });
        Assert.Throws<InvalidOperationException>(() => partial.CreateClient());

        using var disabled = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Auth:Entra:Enabled", "false");
            b.UseSetting("Auth:Entra:TenantSubdomain", "contoso");
        });
        using var client = disabled.CreateClient();
        Assert.NotNull(client);
    }

    // ---- N1: OpenAPI endpoint ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Openapi_document_is_anonymous_in_Development_and_not_served_elsewhere()
    {
        using var development = factory.WithWebHostBuilder(b => b.UseEnvironment("Development"));
        using var devClient = development.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await devClient.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct)).StatusCode);

        using var production = factory.CreateClient();
        Assert.NotEqual(HttpStatusCode.OK, (await production.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct)).StatusCode);
    }

    // ---- N3: HttpCurrentUser -----------------------------------------------------------------------------------------

    [Fact]
    public void IsGuest_requires_an_authenticated_principal_with_a_user_id()
    {
        static HttpCurrentUser For(ClaimsIdentity identity) =>
            new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } });

        var id = Guid.NewGuid().ToString();
        Claim[] claims = [new(AppClaims.UserId, id), new(AppClaims.Kind, AppClaims.KindGuest)];

        Assert.True(For(new ClaimsIdentity(claims, "test")).IsGuest);
        Assert.False(For(new ClaimsIdentity(claims)).IsGuest);                                       // not authenticated
        Assert.False(For(new ClaimsIdentity([new Claim(AppClaims.Kind, AppClaims.KindGuest)], "test")).IsGuest); // no user id
        Assert.False(new HttpCurrentUser(new HttpContextAccessor()).IsGuest);
    }

    // ---- N4: garbage tokens with Entra disabled ----------------------------------------------------------------------

    [Theory]
    [InlineData("aaa.bbb.ccc")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.bm90LWpzb24.c2ln")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.W10.c2ln")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJpc3MiOjF9.c2ln")]
    [InlineData("....")]
    public async Task Garbage_tokens_are_401_when_Entra_is_disabled_too(string token)
    {
        using var client = factory.CreateClient().WithBearer(token);

        var response = await client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- N5: dataset validation at startup --------------------------------------------------------------------------

    [Fact]
    public void Missing_demo_dataset_fails_host_startup()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Guests:DemoDataPath", "does-not-exist"));

        Assert.ThrowsAny<Exception>(() => host.CreateClient());
    }

    [Fact]
    public void Failed_dataset_load_is_not_cached_forever()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rd-demo-" + Guid.NewGuid().ToString("N"));
        var loader = new DemoDatasetLoader(Options.Create(new GuestOptions { DemoDataPath = directory }));
        try
        {
            Assert.ThrowsAny<IOException>(() => loader.Get());
            Assert.ThrowsAny<IOException>(() => loader.Get());

            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "seed", "demo"), directory);

            Assert.NotEmpty(loader.Get().Template.Runs);
            Assert.Same(loader.Get(), loader.Get());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }
}
