using System.Text.RegularExpressions;

namespace RunnersDiary.Infrastructure.Maps;

/// <summary>
/// Azure Maps settings (<c>Maps:AzureMaps</c>, M7 plan section 5.2). Nothing here is secret: the account's unique id, resource ids and
/// the identity's client id. The API authenticates keyless (managed identity); the browser only ever gets a short-lived SAS token.
/// </summary>
public sealed partial class AzureMapsOptions
{
    public const string DefaultDomain = "eu.atlas.microsoft.com";

    /// <summary>The Maps account's <c>uniqueId</c> (the <c>x-ms-client-id</c> header of Entra-authenticated calls).</summary>
    public string? AccountClientId { get; set; }

    /// <summary>Client id of the API's user-assigned identity. Unset: <c>DefaultAzureCredential</c> without interactive sources.</summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>Geography endpoint host: Europe keeps usage in West/North Europe (ADR 0033). Also handed to the browser SDK as its domain.</summary>
    public string Domain { get; set; } = DefaultDomain;

    /// <summary>ARM id of the Maps account, <c>listSas</c> is called on it. Needed only for display.</summary>
    public string? AccountResourceId { get; set; }

    /// <summary>Object id of the identity attached to the account that SAS tokens are issued for. Needed only for display.</summary>
    public string? SasPrincipalId { get; set; }

    /// <summary>Regions a token is valid in (for example <c>westeurope</c>); empty allows all.</summary>
    public List<string> SasRegions { get; set; } = [];

    /// <summary>Requests per second one token may make, per Azure location (1 to 500).</summary>
    public int SasMaxRatePerSecond { get; set; } = 10;

    /// <summary>Lifetime of an issued token (at most 24 hours).</summary>
    public TimeSpan SasLifetime { get; set; } = TimeSpan.FromHours(2);

    /// <summary>The shared token is renewed when less than this remains.</summary>
    public TimeSpan SasRefreshBefore { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Upper bound of one geocode call.</summary>
    public TimeSpan GeocodeTimeout { get; set; } = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Checks the settings the chosen providers need and returns one message per problem (key names only, never values). The geocoder sends
    /// an Entra token to <see cref="Domain"/>, so only Azure Maps hosts are accepted.
    /// </summary>
    public static IReadOnlyList<string> Validate(MapsOptions maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        var options = maps.AzureMaps;
        var failures = new List<string>();
        var geocoding = maps.Geocoding.Provider == GeocodingProvider.AzureMaps;
        var display = maps.Provider == MapProvider.AzureMaps;
        if (!geocoding && !display)
        {
            return failures;
        }

        if (!IsAzureMapsHost(options.Domain))
        {
            failures.Add("Maps:AzureMaps:Domain must be atlas.microsoft.com or a subdomain such as eu.atlas.microsoft.com.");
        }

        if (!IsHeaderSafe(options.AccountClientId))
        {
            failures.Add("Maps:AzureMaps:AccountClientId is required (the Maps account's unique id).");
        }

        if (geocoding && maps.Geocoding.GlobalDailyLimit < 1)
        {
            failures.Add("Maps:Geocoding:GlobalDailyLimit must be at least 1.");
        }

        if (display)
        {
            if (string.IsNullOrWhiteSpace(options.AccountResourceId) || !AccountResourceIdPattern().IsMatch(options.AccountResourceId))
            {
                failures.Add("Maps:AzureMaps:AccountResourceId must be the ARM id of the Maps account (/subscriptions/.../providers/Microsoft.Maps/accounts/<name>).");
            }

            if (!Guid.TryParse(options.SasPrincipalId, out _))
            {
                failures.Add("Maps:AzureMaps:SasPrincipalId must be the object id (GUID) of the SAS identity.");
            }

            if (options.SasMaxRatePerSecond is < 1 or > 500)
            {
                failures.Add("Maps:AzureMaps:SasMaxRatePerSecond must be between 1 and 500.");
            }

            if (options.SasLifetime <= options.SasRefreshBefore || options.SasLifetime > TimeSpan.FromHours(24) || options.SasRefreshBefore < TimeSpan.FromMinutes(1))
            {
                failures.Add("Maps:AzureMaps:SasLifetime must be at most 24 hours and longer than SasRefreshBefore (at least 1 minute).");
            }

            if (options.SasRegions.Any(r => string.IsNullOrWhiteSpace(r) || !RegionPattern().IsMatch(r)))
            {
                failures.Add("Maps:AzureMaps:SasRegions entries must be Azure region names such as westeurope.");
            }
        }

        return failures;
    }

    /// <summary>Throws <see cref="InvalidOperationException"/> listing every problem; no-op when the providers need nothing.</summary>
    public static void EnsureValid(MapsOptions maps)
    {
        var failures = Validate(maps);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException("Invalid Maps configuration: " + string.Join(' ', failures));
        }
    }

    private static bool IsAzureMapsHost(string? host) =>
        !string.IsNullOrWhiteSpace(host)
        && host.Length <= 100
        && HostPattern().IsMatch(host)
        && (host.Equals("atlas.microsoft.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".atlas.microsoft.com", StringComparison.OrdinalIgnoreCase));

    private static bool IsHeaderSafe(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 100 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();

    [GeneratedRegex(@"^/subscriptions/[0-9A-Fa-f-]{36}/resourceGroups/[A-Za-z0-9._()-]{1,90}/providers/Microsoft\.Maps/accounts/[A-Za-z0-9._-]{1,98}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccountResourceIdPattern();

    [GeneratedRegex(@"^[a-z0-9]{2,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex RegionPattern();
}
