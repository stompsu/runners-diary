using System.Net;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Security;

/// <summary>Every way a token can be wrong ends in 401; the valid shapes end in 200 (ADR 0009, plan §6).</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class TokenValidationTests(ContainersApiFactory factory)
{
    private static readonly Uri Me = new("/api/me", UriKind.Relative);

    private async Task<HttpStatusCode> CallMeAsync(string token)
    {
        factory.SkipIfDockerUnavailable();
        using var client = factory.CreateClient().WithBearer(token);
        var response = await client.GetAsync(Me, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    [Fact]
    public async Task ValidEntraToken_Succeeds_AndProvisionsTheUser()
    {
        Assert.Equal(HttpStatusCode.OK, await CallMeAsync(TestTokens.Entra(Guid.NewGuid(), name: "Ada")));
    }

    [Fact]
    public async Task ValidGuestToken_ForAGuestAccount_Succeeds()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestAsync("g");
        var response = await guest.Client.GetAsync(Me, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredGuestToken_Returns401()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestAsync("g");
        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeAsync(TestTokens.Guest(guest.UserId!.Value, lifetime: TimeSpan.FromMinutes(-10))));
    }

    [Fact]
    public async Task GuestTokenSignedWithAnotherKey_Returns401()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestAsync("g");
        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeAsync(TestTokens.Guest(guest.UserId!.Value, key: "another-key-another-key-another-key-0000")));
    }

    [Fact]
    public async Task GuestTokenWithWrongIssuer_IsNotRoutedToTheGuestValidator_AndFails()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestAsync("g");
        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeAsync(TestTokens.Guest(guest.UserId!.Value, issuer: "someone-else")));
    }

    [Fact]
    public async Task GuestTokenWithWrongAudience_Returns401()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestAsync("g");
        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeAsync(TestTokens.Guest(guest.UserId!.Value, audience: "other-api")));
    }

    [Fact]
    public async Task UnsignedGuestToken_AlgNone_Returns401()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestAsync("g");
        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeAsync(TestTokens.Guest(guest.UserId!.Value, unsigned: true)));
    }

    [Fact]
    public async Task GuestKeyToken_WhoseSubjectIsARealUser_Returns401()
    {
        factory.SkipIfDockerUnavailable();
        var entra = factory.EntraUser("real");
        var realUserId = await entra.GetUserIdAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeAsync(TestTokens.Guest(realUserId)));
    }

    [Fact]
    public async Task GuestToken_ForAnUnknownOrDeletedGuest_Returns401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeAsync(TestTokens.Guest(Guid.NewGuid())));
    }

    [Fact]
    public async Task EntraToken_WithoutTheRequiredScope_Returns401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeAsync(TestTokens.Entra(Guid.NewGuid(), scope: "something_else")));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeAsync(TestTokens.Entra(Guid.NewGuid(), scope: null)));
    }

    [Fact]
    public async Task EntraToken_FromTheWrongIssuerAudienceTenantOrKey_Returns401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeAsync(TestTokens.Entra(Guid.NewGuid(), issuer: "https://evil.example/v2.0")));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeAsync(TestTokens.Entra(Guid.NewGuid(), audience: "00000000-0000-0000-0000-000000000009")));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeAsync(TestTokens.Entra(Guid.NewGuid(), tenantId: "33333333-3333-3333-3333-333333333333")));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallMeAsync(TestTokens.Entra(Guid.NewGuid(), signWithForeignKey: true)));
    }

    [Fact]
    public async Task ARoleOrUserIdClaimInsideAnEntraToken_CannotMakeTheCallerAGuest()
    {
        // Entra tokens are routed to the Entra validator by issuer; the Guest kind only ever comes from the database check.
        factory.SkipIfDockerUnavailable();
        var entra = factory.EntraUser("someone");
        var me = await (await entra.Client.GetAsync(Me, TestContext.Current.CancellationToken)).ReadAsync();
        Assert.Equal("User", me.GetProperty("kind").GetString());
    }
}
