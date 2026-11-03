using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace RunnersDiary.Api.Tests.Infrastructure;

/// <summary>
/// Token minting for tests: an Entra-like issuer (RSA test key, v2 access-token claims) and guest tokens (HS256).
/// Nothing here is a real secret; the Entra tenant is fictional and the key lives only in this process.
/// </summary>
internal static class TestTokens
{
    public const string TenantId = "11111111-1111-1111-1111-111111111111";
    public const string TenantSubdomain = "testtenant";
    public const string ApiAudience = "22222222-2222-2222-2222-222222222222";
    public const string EntraIssuer = "https://" + TenantId + ".ciamlogin.com/" + TenantId + "/v2.0";
    public const string GuestSigningKey = "test-only-guest-signing-key-0123456789abcdef";
    public const string GuestIssuer = "runnersdiary-guest";
    public const string GuestAudience = "runnersdiary-api";

    private static readonly RSA Rsa = RSA.Create(2048);
    private static readonly RsaSecurityKey EntraKey = new(Rsa) { KeyId = "test-entra-key" };
    private static readonly JsonWebTokenHandler Handler = new();

    /// <summary>Static OIDC metadata for the Entra scheme, so the real validation path runs without network access.</summary>
    public static OpenIdConnectConfiguration EntraConfiguration()
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = EntraIssuer };
        configuration.SigningKeys.Add(EntraKey);
        return configuration;
    }

    public static string Entra(
        Guid objectId,
        string? scope = "access_as_user",
        string? name = null,
        string tenantId = TenantId,
        string issuer = EntraIssuer,
        string audience = ApiAudience,
        bool signWithForeignKey = false)
    {
        var claims = new Dictionary<string, object> { ["oid"] = objectId.ToString("D"), ["tid"] = tenantId, ["ver"] = "2.0" };
        if (scope is not null)
        {
            claims["scp"] = scope;
        }

        if (name is not null)
        {
            claims["name"] = name;
        }

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now.AddMinutes(-1),
            Expires = now.AddHours(1),
            Claims = claims,
            SigningCredentials = new SigningCredentials(signWithForeignKey ? new RsaSecurityKey(RSA.Create(2048)) : EntraKey, SecurityAlgorithms.RsaSha256),
        };
        return Handler.CreateToken(descriptor);
    }

    public static string Guest(
        Guid userId,
        string key = GuestSigningKey,
        string issuer = GuestIssuer,
        string audience = GuestAudience,
        TimeSpan? lifetime = null,
        bool unsigned = false)
    {
        var now = DateTime.UtcNow;
        var expires = now + (lifetime ?? TimeSpan.FromHours(24));
        // Negative lifetimes produce already-expired tokens: shift NotBefore/IssuedAt back so Expires stays after NotBefore.
        var notBefore = expires < now ? expires.AddHours(-1) : now.AddMinutes(-1);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = notBefore,
            NotBefore = notBefore,
            Expires = expires,
            Claims = new Dictionary<string, object> { ["sub"] = userId.ToString("D"), ["role"] = "guest", ["jti"] = Guid.NewGuid().ToString("N") },
            SigningCredentials = unsigned
                ? null
                : new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256),
        };
        return Handler.CreateToken(descriptor);
    }
}
