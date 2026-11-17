using System.Globalization;

namespace RunnersDiary.Domain.Localization;

/// <summary>
/// Reduces an <c>Accept-Language</c> header to a supported UI language (ADR 0042). The value is untrusted: it is bounded, parsed
/// leniently, and only an allowlisted tag (never the raw text) is returned.
/// </summary>
public static class AcceptLanguage
{
    private const int MaxLength = 256;
    private const int MaxEntries = 16;

    /// <summary>
    /// The supported language with the highest quality value (ties: first listed); region subtags are ignored ("pl-PL" is <c>pl</c>);
    /// <c>q=0</c> entries are skipped. Missing, malformed or unsupported input gives <see cref="SupportedLanguages.English"/>.
    /// </summary>
    public static string Reduce(string? header)
    {
        if (string.IsNullOrWhiteSpace(header) || header.Length > MaxLength)
        {
            return SupportedLanguages.English;
        }

        var best = SupportedLanguages.English;
        var bestQuality = -1.0;
        var entries = 0;
        foreach (var raw in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (++entries > MaxEntries)
            {
                break;
            }

            var parts = raw.Split(';', StringSplitOptions.TrimEntries);
            var primary = parts[0].Split('-', 2)[0].ToLowerInvariant();
            if (!SupportedLanguages.IsSupported(primary))
            {
                continue;
            }

            var quality = 1.0;
            for (var i = 1; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(parts[i][2..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var q)
                    && double.IsFinite(q))
                {
                    quality = Math.Clamp(q, 0, 1);
                }
            }

            if (quality > 0 && quality > bestQuality)
            {
                best = primary;
                bestQuality = quality;
            }
        }

        return SupportedLanguages.OrDefault(best);
    }
}
