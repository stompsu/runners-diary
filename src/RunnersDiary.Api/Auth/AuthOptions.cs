namespace RunnersDiary.Api.Auth;

/// <summary>Entra External ID settings (<c>Auth:Entra</c>). All values are non-secret identifiers.</summary>
public sealed class EntraAuthOptions
{
    public const string SectionName = "Auth:Entra";

    /// <summary>Explicit switch. When unset, Entra is enabled iff <see cref="TenantId"/> is configured.</summary>
    public bool? Enabled { get; set; }

    public string? TenantId { get; set; }

    public string? TenantSubdomain { get; set; }

    /// <summary>The API application (client) id; v2 access tokens carry it as <c>aud</c>.</summary>
    public string? Audience { get; set; }

    /// <summary>Overrides the derived issuer <c>https://{tenantId}.ciamlogin.com/{tenantId}/v2.0</c>.</summary>
    public IList<string> ValidIssuers { get; } = [];

    public string RequiredScope { get; set; } = "access_as_user";

    /// <summary>The SPA application (client) id handed to MSAL via <c>GET /api/client-config</c>. Optional: without it sign-in is hidden.</summary>
    public string? SpaClientId { get; set; }

    /// <summary>Scope the SPA requests; defaults to <c>api://{Audience}/{RequiredScope}</c>.</summary>
    public string? ApiScope { get; set; }
}

/// <summary>App-issued guest token settings (<c>Auth:Guest</c>). <see cref="SigningKey"/> is a secret.</summary>
public sealed class GuestAuthOptions
{
    public const string SectionName = "Auth:Guest";

    /// <summary>Secret, at least 32 bytes (UTF-8). Use a generated random value.</summary>
    public string? SigningKey { get; set; }

    /// <summary>Old keys that still validate (never sign) during rotation.</summary>
    public IList<string> PreviousSigningKeys { get; } = [];

    public string Issuer { get; set; } = "runnersdiary-guest";

    public string Audience { get; set; } = "runnersdiary-api";
}
