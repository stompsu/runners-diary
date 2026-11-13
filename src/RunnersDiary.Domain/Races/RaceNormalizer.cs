using System.Globalization;
using System.Text.RegularExpressions;

namespace RunnersDiary.Domain.Races;

/// <summary>Pure text normalisation of source values (ADR 0035 §6): distance text to metres, kind text to <see cref="RaceKind"/>, region text to an ISO code.</summary>
public static partial class RaceNormalizer
{
    public const double HalfMarathonMeters = 21_097.5;
    public const double MarathonMeters = 42_195;
    public const double MileMeters = 1_609.344;

    /// <summary>
    /// Parses one distance label such as "10 km", "10,0 km", "21.0975 km", "5K", "10 000 m", "100 mil", "półmaraton" or "maraton".
    /// Returns false for junk, children's runs ("1 km dla dzieci"), and anything outside 1,000 to 250,000 m. The result is rounded to 0.1 m.
    /// A bare number without a unit is junk.
    /// </summary>
    public static bool TryParseDistanceMeters(string? text, out double meters)
    {
        meters = 0;
        var folded = TextFolding.Fold(text);
        if (folded.Length == 0 || IsKidsLabel(folded))
        {
            return false;
        }

        var value = ParseNumberWithUnit(text!);
        if (value is null)
        {
            if (folded.Contains("polmaraton", StringComparison.Ordinal) || folded.Contains("half marathon", StringComparison.Ordinal) || folded.Contains("halfmarathon", StringComparison.Ordinal))
            {
                value = HalfMarathonMeters;
            }
            else if (folded.Contains("ultra", StringComparison.Ordinal))
            {
                return false;
            }
            else if (folded.Contains("maraton", StringComparison.Ordinal) || folded.Contains("marathon", StringComparison.Ordinal))
            {
                value = MarathonMeters;
            }
        }

        if (value is not { } m || !double.IsFinite(m))
        {
            return false;
        }

        m = Math.Round(m, 1, MidpointRounding.AwayFromZero);
        if (m < RaceFacts.MinDistanceMeters || m > RaceFacts.MaxDistanceMeters)
        {
            return false;
        }

        meters = m;
        return true;
    }

    /// <summary>Parses a list or a slash/semicolon/pipe separated label ("5K / 10K"); junk entries are dropped. Result is deduplicated, ascending and capped at 12.</summary>
    public static IReadOnlyList<double> ParseDistances(params IEnumerable<string?> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var result = new SortedSet<double>();
        foreach (var text in texts)
        {
            if (text is null)
            {
                continue;
            }

            foreach (var part in text.Split(['/', ';', '|', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryParseDistanceMeters(part, out var m))
                {
                    result.Add(m);
                }
            }
        }

        return result.Take(RaceFacts.MaxDistances).ToList();
    }

    /// <summary>Maps a Polish or English type label to a kind. Priority: obstacle, ultra, cross-country, mountain, trail, road; anything else is <see cref="RaceKind.Other"/>.</summary>
    public static RaceKind ParseKind(string? text)
    {
        var folded = TextFolding.Fold(text);
        if (folded.Length == 0)
        {
            return RaceKind.Other;
        }

        var tokens = folded.Split(' ');
        if (tokens.Contains("ocr") || folded.Contains("przeszkod", StringComparison.Ordinal) || folded.Contains("obstacle", StringComparison.Ordinal))
        {
            return RaceKind.Ocr;
        }

        if (folded.Contains("ultra", StringComparison.Ordinal))
        {
            return RaceKind.Ultra;
        }

        if (folded.Contains("przelaj", StringComparison.Ordinal) || tokens.Contains("cross") || folded.Contains("cross country", StringComparison.Ordinal))
        {
            return RaceKind.CrossCountry;
        }

        if (folded.Contains("gorsk", StringComparison.Ordinal) || folded.Contains("mountain", StringComparison.Ordinal))
        {
            return RaceKind.Mountain;
        }

        if (folded.Contains("teren", StringComparison.Ordinal) || folded.Contains("trail", StringComparison.Ordinal))
        {
            return RaceKind.Trail;
        }

        if (folded.Contains("miejsk", StringComparison.Ordinal) || folded.Contains("uliczn", StringComparison.Ordinal)
            || tokens.Contains("city") || tokens.Contains("street") || tokens.Contains("road") || tokens.Contains("asfalt"))
        {
            return RaceKind.Road;
        }

        return RaceKind.Other;
    }

    /// <summary>Region text (English or Polish name, alias or code) to an ISO 3166-2 code; null when unknown.</summary>
    public static string? ParseRegionCode(string? text) => PolishRegions.TryGetCode(text, out var code) ? code : null;

    private static bool IsKidsLabel(string folded) =>
        folded.Contains("dzieci", StringComparison.Ordinal) || folded.Contains("dziec", StringComparison.Ordinal)
        || folded.Contains("kids", StringComparison.Ordinal) || folded.Contains("child", StringComparison.Ordinal)
        || folded.Contains("przedszkol", StringComparison.Ordinal);

    private static double? ParseNumberWithUnit(string text)
    {
        // "10 000 m" -> "10000 m": merge digit groups of exactly three.
        var merged = ThousandsGroup().Replace(text.Replace((char)0xA0, ' '), static m => m.Value.Replace(" ", string.Empty, StringComparison.Ordinal));
        var match = NumberUnit().Match(merged);
        if (!match.Success)
        {
            return null;
        }

        var number = match.Groups["n"].Value.Replace(',', '.');
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
        {
            return null;
        }

        var unit = TextFolding.Fold(match.Groups["u"].Value);
        return unit switch
        {
            "k" or "km" => n * 1000,
            "m" => n,
            _ when unit.StartsWith("kilomet", StringComparison.Ordinal) => n * 1000,
            _ when unit.StartsWith("met", StringComparison.Ordinal) => n,
            _ when unit is "mi" or "mil" or "mile" or "miles" || unit.StartsWith("mil", StringComparison.Ordinal) => n * MileMeters,
            _ => null,
        };
    }

    [GeneratedRegex(@"(?<![\d.,])\d{1,3}(?: \d{3})+(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex ThousandsGroup();

    [GeneratedRegex(@"(?<![\d.,])(?<n>\d{1,6}(?:[.,]\d{1,4})?)\s*(?<u>kilomet\w*|km|k|met\w*|mil\w*|mi|m)(?![\p{L}\d])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NumberUnit();
}
