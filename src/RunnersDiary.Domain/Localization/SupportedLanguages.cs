namespace RunnersDiary.Domain.Localization;

/// <summary>
/// The UI languages the app supports (ADR 0042 §1): bare ISO 639-1 tags. English is the default and the fallback.
/// Every <c>lang</c>/<c>language</c> value from a client is checked against this list before it is stored or used.
/// </summary>
public static class SupportedLanguages
{
    public const string English = "en";

    public const string Polish = "pl";

    /// <summary>Longest stored tag (<c>varchar(8)</c> columns).</summary>
    public const int MaxLength = 8;

    private static readonly string[] Tags = [English, Polish];

    public static IReadOnlyList<string> All => Tags;

    /// <summary>True for an exact, lower-case supported tag (no trimming, no region subtags).</summary>
    public static bool IsSupported(string? language) => language is not null && Array.IndexOf(Tags, language) >= 0;

    /// <summary>The supported tag equal to <paramref name="language"/> (the canonical instance), or <see cref="English"/> for null or unsupported values.</summary>
    public static string OrDefault(string? language) => IsSupported(language) ? language! : English;
}
