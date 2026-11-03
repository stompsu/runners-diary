using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Auth;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Security;

[Collection(ContainersFixtureGroup.Name)]
public sealed class GuestTokenIssuerTests(ContainersApiFactory factory)
{
    [Fact]
    public async Task Tokens_from_the_production_issuer_are_accepted_for_that_guest_with_expiry_equal_to_the_account()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestAsync("issued");
        var issuer = factory.Services.GetRequiredService<GuestTokenIssuer>();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(24);
        var token = issuer.Issue(guest.UserId!.Value, expiresAt);

        using var client = factory.CreateClient().WithBearer(token);
        var response = await client.GetAsync(new Uri("/api/me", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(expiresAt.ToUnixTimeSeconds(), new DateTimeOffset(jwt.ValidTo).ToUnixTimeSeconds());
        Assert.Equal("guest", jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.Equal(guest.UserId.Value.ToString("D"), jwt.Subject);
        Assert.Equal("HS256", jwt.Header.Alg);
    }
}
