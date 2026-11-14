using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RunnersDiary.Domain.Races;

/// <summary>
/// <c>FactsHash</c> (ADR 0036 §2): SHA-256 over the canonical JSON of the normalised facts. Distances are sorted, coordinates rounded to
/// 6 decimals, URLs taken as stored; property order is fixed, so equal facts always give equal hashes.
/// </summary>
public static class RaceFactsHasher
{
    public const int HashLength = 32;

    public static byte[] Hash(RaceFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return SHA256.HashData(CanonicalJson(facts));
    }

    /// <summary>The canonical UTF-8 JSON that is hashed (exposed for tests and diagnostics).</summary>
    public static byte[] CanonicalJson(RaceFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var f = facts.Normalized();

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("source", (byte)f.Source);
            w.WriteString("externalId", f.ExternalId);
            w.WriteString("name", f.Name);
            w.WriteString("date", f.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Nullable(w, "startTime", f.StartTime?.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            Nullable(w, "locationName", f.LocationName);
            if (f.Point is { } p)
            {
                w.WriteStartObject("point");
                w.WriteNumber("lat", Math.Round(p.Latitude, 6, MidpointRounding.AwayFromZero));
                w.WriteNumber("lon", Math.Round(p.Longitude, 6, MidpointRounding.AwayFromZero));
                w.WriteEndObject();
            }
            else
            {
                w.WriteNull("point");
            }

            w.WriteString("countryCode", f.CountryCode);
            Nullable(w, "regionCode", f.RegionCode);
            w.WriteNumber("kind", (byte)f.Kind);
            if (f.Surface is { } surface)
            {
                w.WriteNumber("surface", (byte)surface);
            }
            else
            {
                w.WriteNull("surface");
            }

            w.WriteStartArray("distancesMeters");
            foreach (var d in f.DistancesMeters)
            {
                w.WriteNumberValue(d);
            }

            w.WriteEndArray();
            if (f.ElevationGainMeters is { } e)
            {
                w.WriteNumber("elevationGainMeters", e);
            }
            else
            {
                w.WriteNull("elevationGainMeters");
            }

            Nullable(w, "signupUrl", f.SignupUrl);
            Nullable(w, "eventUrl", f.EventUrl);
            Nullable(w, "sourceUrl", f.SourceUrl);
            if (f.PriceAmount is { } price)
            {
                w.WriteString("priceAmount", price.ToString("0.##", CultureInfo.InvariantCulture));
            }
            else
            {
                w.WriteNull("priceAmount");
            }

            Nullable(w, "priceCurrency", f.PriceCurrency);
            Nullable(w, "signupDeadline", f.SignupDeadline?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Nullable(w, "organizerName", f.OrganizerName);
            w.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static string CanonicalJsonString(RaceFacts facts) => Encoding.UTF8.GetString(CanonicalJson(facts));

    private static void Nullable(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null)
        {
            w.WriteNull(name);
        }
        else
        {
            w.WriteString(name, value);
        }
    }
}
