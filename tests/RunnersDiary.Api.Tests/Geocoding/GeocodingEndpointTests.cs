using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Maps;

namespace RunnersDiary.Api.Tests.Geocoding;

/// <summary>AC11 (plan section 1): GET /api/geocoding/search behind auth and the geocode limiter, provider None and a fake provider.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GeocodingEndpointTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri Search(string query) => new("/api/geocoding/search" + query, UriKind.Relative);

    private WebApplicationFactory<Program> WithFake(Action<Microsoft.AspNetCore.Hosting.IWebHostBuilder>? more = null) =>
        factory.WithWebHostBuilder(b =>
        {
            b.ConfigureServices(services => services.AddSingleton<IGeocoder, FakeGeocoder>());
            more?.Invoke(b);
        });

    [Fact]
    public async Task Without_a_provider_the_answer_is_503_feature_unavailable()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("nogeocoder");

        var response = await user.Client.GetAsync(Search("?q=Gliwice"), Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("urn:runnersdiary:problem:feature-unavailable", (await response.ReadAsync()).GetProperty("type").GetString());
    }

    [Fact]
    public async Task Without_a_token_the_answer_is_401()
    {
        factory.SkipIfDockerUnavailable();
        using var anonymous = WithFake().CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Search("?q=Gliwice"), Ct)).StatusCode);
    }

    [Fact]
    public async Task A_provider_answer_is_cut_to_five_places_in_the_contract_shape_for_users_and_guests()
    {
        factory.SkipIfDockerUnavailable();
        await using var fake = WithFake();
        var user = fake.EntraUser("geouser");
        var guest = await fake.GuestAsync("geoguest");

        foreach (var caller in new[] { user, guest })
        {
            var response = await caller.Client.GetAsync(Search("?q=%20Gliwice%20&lat=50.29&lon=18.67"), Ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var results = (await response.ReadAsync()).GetProperty("results");
            Assert.Equal(5, results.GetArrayLength());
            var first = results[0];
            Assert.Equal("Gliwice 1", first.GetProperty("name").GetString());
            Assert.Equal(50.30, first.GetProperty("latitude").GetDouble(), 6);
            Assert.Equal(18.67, first.GetProperty("longitude").GetDouble(), 6);
            Assert.Equal("locality", first.GetProperty("type").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, results[1].GetProperty("type").ValueKind);
        }

        Assert.Equal("Gliwice", FakeGeocoder.LastQuery);
        Assert.Equal((50.29, 18.67), FakeGeocoder.LastBias);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?q=a")]
    [InlineData("?q=%20%20a%20")]
    [InlineData("?q=Gliwice&lat=50.29")]
    [InlineData("?q=Gliwice&lon=18.67")]
    [InlineData("?q=Gliwice&lat=91&lon=18")]
    [InlineData("?q=Gliwice&lat=50&lon=181")]
    [InlineData("?q=Gliwice&lat=abc&lon=18")]
    public async Task Invalid_queries_are_a_400_validation_problem(string query)
    {
        factory.SkipIfDockerUnavailable();
        await using var fake = WithFake();
        var user = fake.EntraUser("geoinvalid");

        var response = await user.Client.GetAsync(Search(query), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_query_must_be_at_most_100_characters()
    {
        factory.SkipIfDockerUnavailable();
        await using var fake = WithFake();
        var user = fake.EntraUser("geolong");

        var ok = await user.Client.GetAsync(Search("?q=" + new string('a', 100)), Ct);
        var tooLong = await user.Client.GetAsync(Search("?q=" + new string('a', 101)), Ct);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("q", (await tooLong.ReadAsync()).GetProperty("errors").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task A_failing_provider_is_a_503_without_leaking_its_error()
    {
        factory.SkipIfDockerUnavailable();
        await using var fake = WithFake();
        var user = fake.EntraUser("geofail");
        FakeGeocoder.Throw = true;
        try
        {
            var response = await user.Client.GetAsync(Search("?q=Gliwice"), Ct);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(Ct);
            Assert.DoesNotContain("secret", body, StringComparison.Ordinal);
            Assert.Contains("feature-unavailable", body, StringComparison.Ordinal);
        }
        finally
        {
            FakeGeocoder.Throw = false;
        }
    }

    [Fact]
    public async Task Searches_are_rate_limited_per_caller_with_retry_after()
    {
        factory.SkipIfDockerUnavailable();
        await using var fake = WithFake(b =>
        {
            b.UseSetting("RateLimiting:Geocode:UserPermitLimit", "2");
            b.UseSetting("RateLimiting:Geocode:GuestPermitLimit", "1");
        });
        var user = fake.EntraUser("geolimited");
        var other = fake.EntraUser("geoquiet");
        var guest = await fake.GuestAsync("geolimitedguest");

        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync(Search("?q=Gliwice"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync(Search("?q=Gliwice"), Ct)).StatusCode);
        var limited = await user.Client.GetAsync(Search("?q=Gliwice"), Ct);
        Assert.Equal(HttpStatusCode.OK, (await guest.Client.GetAsync(Search("?q=Gliwice"), Ct)).StatusCode);
        var guestLimited = await guest.Client.GetAsync(Search("?q=Gliwice"), Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.TooManyRequests, guestLimited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.Client.GetAsync(Search("?q=Gliwice"), Ct)).StatusCode);
    }
}
