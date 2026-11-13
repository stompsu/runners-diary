using System.Collections.Frozen;

namespace RunnersDiary.Domain.Races;

/// <summary>IANA zone of a race's country (ADR 0036 §1). M8 is Poland-only; an unknown country yields null so a signup falls back to the caller's zone.</summary>
public static class CountryTimeZones
{
    private static readonly FrozenDictionary<string, string> ByCountry =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["PL"] = "Europe/Warsaw" }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static string? Find(string? countryCode) =>
        countryCode is not null && ByCountry.TryGetValue(countryCode.Trim(), out var zone) ? zone : null;
}
