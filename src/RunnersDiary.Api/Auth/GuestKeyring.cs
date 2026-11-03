using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace RunnersDiary.Api.Auth;

/// <summary>The resolved guest signing material: one signing key plus validation-only previous keys.</summary>
public sealed class GuestKeyring
{
    public const int MinKeyBytes = 32;

    public GuestKeyring(string issuer, string audience, string signingKey, IEnumerable<string> previousKeys)
    {
        Issuer = issuer;
        Audience = audience;
        SigningKey = ToKey(signingKey);
        ValidationKeys = [SigningKey, .. previousKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(ToKey)];
    }

    public string Issuer { get; }

    public string Audience { get; }

    public SymmetricSecurityKey SigningKey { get; }

    public IReadOnlyList<SecurityKey> ValidationKeys { get; }

    private static SymmetricSecurityKey ToKey(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return bytes.Length < MinKeyBytes
            ? throw new InvalidOperationException($"Auth:Guest signing keys must be at least {MinKeyBytes} bytes.")
            : new SymmetricSecurityKey(bytes);
    }
}
