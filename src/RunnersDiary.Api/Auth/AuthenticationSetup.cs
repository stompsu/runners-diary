using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Users;

namespace RunnersDiary.Api.Auth;

/// <summary>What <see cref="AuthenticationSetup.AddAppAuthentication"/> decided; Program logs it once the logger exists.</summary>
public sealed record AuthSetupResult(bool EntraEnabled, string? EntraDisabledReason, bool GuestKeyIsEphemeral, bool SpaClientIdMissing = false);

/// <summary>
/// Two JWT bearer schemes (Entra External ID, app-issued guest tokens) behind one policy scheme that picks the validator by the
/// unvalidated <c>iss</c> (ADR 0009). Default and fallback authorization policy: authenticated user.
/// </summary>
public static class AuthenticationSetup
{
    public const string SmartScheme = "Smart";
    public const string EntraScheme = "Entra";
    public const string GuestScheme = "Guest";

    private const string BearerPrefix = "Bearer ";
    private static readonly JsonWebTokenHandler TokenReader = new();

    /// <param name="relaxed">Build-time OpenAPI generation: never fail for missing Entra config or signing key.</param>
    public static AuthSetupResult AddAppAuthentication(this WebApplicationBuilder builder, bool relaxed)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var config = builder.Configuration;
        var entra = config.GetSection(EntraAuthOptions.SectionName).Get<EntraAuthOptions>() ?? new EntraAuthOptions();
        var guest = config.GetSection(GuestAuthOptions.SectionName).Get<GuestAuthOptions>() ?? new GuestAuthOptions();
        var isDevelopment = builder.Environment.IsDevelopment();

        // Guest signing key: Development (standalone API) and OpenAPI generation may use an ephemeral key, everything else must configure one.
        var ephemeral = string.IsNullOrWhiteSpace(guest.SigningKey);
        if (ephemeral && !isDevelopment && !relaxed)
        {
            throw new InvalidOperationException(
                "Auth:Guest:SigningKey is required outside Development (an Aspire secret parameter locally, Key Vault in Azure).");
        }

        var signingKey = ephemeral ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)) : guest.SigningKey!;
        var keyring = new GuestKeyring(guest.Issuer, guest.Audience, signingKey, guest.PreviousSigningKeys);
        builder.Services.AddSingleton(keyring);
        builder.Services.AddSingleton<GuestTokenIssuer>();

        var (entraEnabled, entraReason) = ResolveEntra(entra, isDevelopment || relaxed);

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

        var authentication = builder.Services.AddAuthentication(SmartScheme)
            .AddPolicyScheme(SmartScheme, displayName: null, options =>
                options.ForwardDefaultSelector = context => SelectScheme(context, keyring.Issuer, entraEnabled))
            .AddJwtBearer(GuestScheme, options => ConfigureGuest(options, keyring));

        if (entraEnabled)
        {
            authentication.AddJwtBearer(EntraScheme, options => ConfigureEntra(options, entra));
        }

        // Authenticated user (with the server-added user id) is the default AND the fallback: an endpoint without explicit
        // metadata is protected, and only .AllowAnonymous() opts out.
        var authenticated = new AuthorizationPolicyBuilder(SmartScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(AppClaims.UserId)
            .Build();
        builder.Services.AddAuthorizationBuilder()
            .SetDefaultPolicy(authenticated)
            .SetFallbackPolicy(authenticated);

        builder.Services.AddSingleton(Features.ClientConfig.GetClientConfig.Build(entra, entraEnabled));

        // A missing SPA client id is not a startup failure (ADR 0015): sign-in is optional, guests keep working; Program logs a warning.
        return new AuthSetupResult(entraEnabled, entraReason, ephemeral, entraEnabled && string.IsNullOrWhiteSpace(entra.SpaClientId));
    }

    private static (bool Enabled, string? Reason) ResolveEntra(EntraAuthOptions entra, bool lenient)
    {
        if (entra.Enabled == false)
        {
            return (false, "Auth:Entra:Enabled is false.");
        }

        // Any of the three identifiers means "somebody tried to configure Entra": an incomplete set is a startup failure, never a
        // silently disabled login (ADR 0009 section 4), in Development too unless Enabled=false is explicit.
        var anyValue = !string.IsNullOrWhiteSpace(entra.TenantId)
            || !string.IsNullOrWhiteSpace(entra.TenantSubdomain)
            || !string.IsNullOrWhiteSpace(entra.Audience);
        if (!anyValue && entra.Enabled != true)
        {
            return lenient
                ? (false, "Auth:Entra is not configured.")
                : throw new InvalidOperationException(
                    "Auth:Entra is not configured. Configure TenantId, TenantSubdomain and Audience, or set Auth:Entra:Enabled=false explicitly.");
        }

        if (!Guid.TryParse(entra.TenantId, out _) || string.IsNullOrWhiteSpace(entra.TenantSubdomain) || string.IsNullOrWhiteSpace(entra.Audience))
        {
            throw new InvalidOperationException("Auth:Entra requires TenantId (GUID), TenantSubdomain and Audience when enabled.");
        }

        return (true, null);
    }

    private static string SelectScheme(HttpContext context, string guestIssuer, bool entraEnabled)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                // Read WITHOUT validating: only to route to the right validator, which then does the real checks.
                if (string.Equals(TokenReader.ReadJsonWebToken(header[BearerPrefix.Length..].Trim()).Issuer, guestIssuer, StringComparison.Ordinal))
                {
                    return GuestScheme;
                }
            }
#pragma warning disable CA1031 // Reading an unvalidated token can throw many types (JSON, Base64, argument, security); none may become a 500.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Malformed token: fall through to the default scheme, whose validator answers 401.
            }
        }

        return entraEnabled ? EntraScheme : GuestScheme;
    }

    private static void ConfigureGuest(JwtBearerOptions options, GuestKeyring keyring)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = keyring.Issuer,
            ValidateAudience = true,
            ValidAudience = keyring.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keyring.ValidationKeys,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = "role",
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                if (!Guid.TryParse(context.Principal?.FindFirstValue("sub"), out var userId))
                {
                    context.Fail("Guest token has no valid subject.");
                    return;
                }

                // A leaked guest key can never impersonate a real user: the subject must be an existing Guest-kind account.
                var provisioner = context.HttpContext.RequestServices.GetRequiredService<UserProvisioner>();
                if (!await provisioner.IsActiveGuestAsync(userId, context.HttpContext.RequestAborted))
                {
                    context.Fail("Guest account does not exist.");
                    return;
                }

                AddIdentityClaims(context.Principal!, userId, AppClaims.KindGuest);
            },
        };
    }

    private static void ConfigureEntra(JwtBearerOptions options, EntraAuthOptions entra)
    {
        var tenantId = entra.TenantId!;
        var issuers = entra.ValidIssuers.Count > 0
            ? entra.ValidIssuers.ToArray()
            : [$"https://{tenantId}.ciamlogin.com/{tenantId}/v2.0"];

        options.Authority = $"https://{entra.TenantSubdomain}.ciamlogin.com/{tenantId}/v2.0";
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = issuers,
            ValidateAudience = true,
            ValidAudience = entra.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromMinutes(2),
            RoleClaimType = "role",
            NameClaimType = "name",
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal!;
                var scopes = principal.FindFirstValue("scp")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
                if (!scopes.Contains(entra.RequiredScope, StringComparer.Ordinal))
                {
                    context.Fail("Required scope is missing.");
                    return;
                }

                if (!Guid.TryParse(principal.FindFirstValue("tid"), out var tid)
                    || !Guid.TryParse(principal.FindFirstValue("oid"), out var oid)
                    || !string.Equals(tid.ToString(), tenantId, StringComparison.OrdinalIgnoreCase))
                {
                    context.Fail("Token has no valid tenant/object id for this tenant.");
                    return;
                }

                var provisioner = context.HttpContext.RequestServices.GetRequiredService<UserProvisioner>();
                var userId = await provisioner.GetOrCreateEntraUserAsync(tid, oid, principal.FindFirstValue("name"), context.HttpContext.RequestAborted);
                AddIdentityClaims(principal, userId, AppClaims.KindUser);
            },
        };
    }

    private static void AddIdentityClaims(ClaimsPrincipal principal, Guid userId, string kind)
    {
        var identity = (ClaimsIdentity)principal.Identity!;
        // Whatever a token claims about our internal claim types is discarded; only the database decides.
        foreach (var stale in identity.FindAll(c => c.Type is AppClaims.UserId or AppClaims.Kind).ToList())
        {
            identity.RemoveClaim(stale);
        }

        identity.AddClaim(new Claim(AppClaims.UserId, userId.ToString("D")));
        identity.AddClaim(new Claim(AppClaims.Kind, kind));
    }
}
