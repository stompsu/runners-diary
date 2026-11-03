using System.Text.RegularExpressions;

namespace RunnersDiary.Domain.Time;

/// <summary>
/// IANA time zone ids. .NET on Windows and on ICU-enabled Linux/macOS also resolves Windows ids ("Central European Standard Time"),
/// which must not be stored, so the id has to look like an IANA id before it is resolved.
/// </summary>
public static partial class IanaTimeZone
{
    public const int MaxLength = 64;

    public static bool IsValid(string? id) => TryGet(id, out _);

    public static bool TryGet(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id) || id.Length > MaxLength || !LooksIana(id))
        {
            return false;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static TimeZoneInfo Get(string id) =>
        TryGet(id, out var zone) ? zone : throw new ArgumentException($"'{id}' is not a valid IANA time zone id.", nameof(id));

    // Area/Location[/Sub] (Europe/Warsaw, America/Argentina/Buenos_Aires, Etc/GMT+1) or the bare "UTC"/"GMT".
    private static bool LooksIana(string id) => id is "UTC" or "GMT" || IanaPattern().IsMatch(id);

    [GeneratedRegex(@"^[A-Za-z]+(/[A-Za-z0-9_+\-]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex IanaPattern();
}
