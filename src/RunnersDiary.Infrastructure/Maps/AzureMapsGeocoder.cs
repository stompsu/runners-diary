using System.Globalization;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace RunnersDiary.Infrastructure.Maps;

/// <summary>
/// Azure Maps Search "Get Geocoding" (<c>api-version=2026-01-01</c>) behind <see cref="IGeocoder"/> (ADR 0022 section 4, ADR 0033).
/// Keyless: an Entra token for <c>https://atlas.microsoft.com/.default</c> plus the account's <c>x-ms-client-id</c>. The caller's identity
/// never reaches the provider, and neither the query text nor the coordinates are logged (they are personal data).
/// </summary>
public sealed partial class AzureMapsGeocoder(
    HttpClient http, TokenCredential credential, AzureMapsOptions options, ILogger<AzureMapsGeocoder> logger) : IGeocoder
{
    public const string ApiVersion = "2026-01-01";
    public const string TokenScope = "https://atlas.microsoft.com/.default";
    private const int MaxResults = 5;
    private const int MaxNameLength = 200;

    public bool IsAvailable => true;

    public async Task<IReadOnlyList<GeocodedPlace>> SearchAsync(
        string query, double? biasLatitude, double? biasLongitude, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var url = $"geocode?api-version={ApiVersion}&top={MaxResults}&query={Uri.EscapeDataString(query)}";
        if (biasLatitude is { } lat && biasLongitude is { } lon)
        {
            url += string.Create(CultureInfo.InvariantCulture, $"&coordinates={lon:0.######},{lat:0.######}");
        }

        var token = await credential.GetTokenAsync(new TokenRequestContext([TokenScope]), cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token.Token);
        request.Headers.TryAddWithoutValidation("x-ms-client-id", options.AccountClientId);
        request.Headers.TryAddWithoutValidation("Accept", "application/geo+json, application/json");

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LogProviderStatus(logger, (int)response.StatusCode);
            throw new HttpRequestException("Azure Maps geocoding failed.", inner: null, response.StatusCode);
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        return Map(document.RootElement);
    }

    internal static IReadOnlyList<GeocodedPlace> Map(JsonElement root)
    {
        var places = new List<GeocodedPlace>(MaxResults);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
        {
            return places;
        }

        foreach (var feature in features.EnumerateArray())
        {
            if (places.Count == MaxResults)
            {
                break;
            }

            if (feature.ValueKind != JsonValueKind.Object
                || !TryReadPoint(feature, out var latitude, out var longitude)
                || !TryReadName(feature, out var name))
            {
                continue;
            }

            places.Add(new GeocodedPlace(name, latitude, longitude, MapType(feature)));
        }

        return places;
    }

    /// <summary>GeoJSON order is [longitude, latitude]; anything non-finite or out of range is dropped.</summary>
    private static bool TryReadPoint(JsonElement feature, out double latitude, out double longitude)
    {
        latitude = longitude = 0;
        if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object
            || !geometry.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind != JsonValueKind.Array
            || coordinates.GetArrayLength() < 2
            || coordinates[0].ValueKind != JsonValueKind.Number || coordinates[1].ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        longitude = coordinates[0].GetDouble();
        latitude = coordinates[1].GetDouble();
        return double.IsFinite(latitude) && double.IsFinite(longitude) && Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180;
    }

    private static bool TryReadName(JsonElement feature, out string name)
    {
        name = string.Empty;
        if (!feature.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object
            || !properties.TryGetProperty("address", out var address) || address.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var text = ReadString(address, "formattedAddress") ?? ReadString(address, "locality");
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        name = text.Length > MaxNameLength ? text[..MaxNameLength] : text;
        return true;
    }

    private static string? MapType(JsonElement feature) =>
        feature.GetProperty("properties") is { ValueKind: JsonValueKind.Object } properties ? properties.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString() switch
            {
                "PopulatedPlace" => "locality",
                "Neighborhood" => "neighborhood",
                "Address" => "address",
                "RoadBlock" or "RoadIntersection" => "street",
                "Postcode1" => "postcode",
                "AdminDivision1" or "AdminDivision2" => "region",
                "CountryRegion" => "country",
                _ => null,
            }
            : null : null;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Azure Maps geocoding answered HTTP {StatusCode}.")]
    private static partial void LogProviderStatus(ILogger logger, int statusCode);
}
