using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Account;

/// <summary>M10 AC3 (ADR 0042 §3): <c>MeResponse.language</c> and <c>PUT /api/me/language</c>.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class LanguageEndpointTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Uri Me = new("/api/me", UriKind.Relative);
    private static readonly Uri Language = new("/api/me/language", UriKind.Relative);

    [Fact]
    public async Task A_new_user_has_no_language_and_PUT_stores_it_for_the_next_GET()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("lang-a");

        var before = await (await user.Client.GetAsync(Me, Ct)).ReadAsync();
        Assert.Equal(JsonValueKind.Null, before.GetProperty("language").ValueKind);

        var put = await user.Client.PutAsJsonAsync(Language, new { language = "pl" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var after = await (await user.Client.GetAsync(Me, Ct)).ReadAsync();
        Assert.Equal("pl", after.GetProperty("language").GetString());

        // PUT /api/me (full replace of the profile) leaves the language alone.
        var profile = await user.Client.PutAsJsonAsync(Me, new { displayName = "Ada", timeZoneId = "Europe/Warsaw", unitSystem = "Metric" }, Ct);
        Assert.Equal("pl", (await profile.ReadAsync()).GetProperty("language").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PutAsJsonAsync(Language, new { language = "en" }, Ct)).StatusCode);
        Assert.Equal("en", (await (await user.Client.GetAsync(Me, Ct)).ReadAsync()).GetProperty("language").GetString());
    }

    [Theory]
    [InlineData("de")]
    [InlineData("PL")]
    [InlineData("pl-PL")]
    [InlineData("")]
    public async Task An_unsupported_language_is_a_400_problem_keyed_language_and_changes_nothing(string value)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("lang-bad");
        await user.Client.PutAsJsonAsync(Language, new { language = "pl" }, Ct);

        var response = await user.Client.PutAsJsonAsync(Language, new { language = value }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await response.ReadAsync()).GetProperty("errors").TryGetProperty("language", out _));
        Assert.Equal("pl", (await (await user.Client.GetAsync(Me, Ct)).ReadAsync()).GetProperty("language").GetString());
    }

    [Fact]
    public async Task An_unsupported_language_is_never_echoed()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("lang-echo");
        var marker = "zz<x>\"'echo" + new string('q', 200);

        var response = await user.Client.PutAsJsonAsync(Language, new { language = marker }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("echo", body, StringComparison.Ordinal);
        Assert.DoesNotContain("qqqq", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_or_null_language_is_a_400()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("lang-missing");

        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PutAsJsonAsync(Language, new { }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PutAsJsonAsync(Language, new { language = (string?)null }, Ct)).StatusCode);
    }

    [Fact]
    public async Task The_endpoint_writes_only_the_callers_row()
    {
        factory.SkipIfDockerUnavailable();
        var a = factory.EntraUser("lang-iso-a");
        var b = factory.EntraUser("lang-iso-b");
        await b.Client.GetAsync(Me, Ct);

        // The caller id comes from the token; there is no way to name another user.
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.PutAsJsonAsync(Language, new { language = "pl" }, Ct)).StatusCode);

        Assert.Equal(JsonValueKind.Null, (await (await b.Client.GetAsync(Me, Ct)).ReadAsync()).GetProperty("language").ValueKind);
        Assert.Equal("pl", (await (await a.Client.GetAsync(Me, Ct)).ReadAsync()).GetProperty("language").GetString());
    }

    [Fact]
    public async Task Without_a_token_the_endpoint_answers_401()
    {
        factory.SkipIfDockerUnavailable();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PutAsJsonAsync(Language, new { language = "pl" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_guest_can_set_its_language()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("lang-guest");

        Assert.Equal(HttpStatusCode.NoContent, (await guest.Client.PutAsJsonAsync(Language, new { language = "pl" }, Ct)).StatusCode);

        Assert.Equal("pl", (await (await guest.Client.GetAsync(Me, Ct)).ReadAsync()).GetProperty("language").GetString());
    }
}
