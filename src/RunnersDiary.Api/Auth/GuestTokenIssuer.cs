using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace RunnersDiary.Api.Auth;

/// <summary>Mints the guest JWT (ADR 0009 §3): HS256, <c>sub</c> = internal user id, <c>exp</c> = the guest account's expiry.</summary>
public sealed class GuestTokenIssuer(GuestKeyring keyring, TimeProvider clock)
{
    private static readonly JsonWebTokenHandler Handler = new();

    public string Issue(Guid userId, DateTimeOffset expiresAt)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = keyring.Issuer,
            Audience = keyring.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString("D"),
                ["role"] = "guest",
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
            SigningCredentials = new SigningCredentials(keyring.SigningKey, SecurityAlgorithms.HmacSha256),
        };
        return Handler.CreateToken(descriptor);
    }
}
