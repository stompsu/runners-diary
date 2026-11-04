namespace RunnersDiary.Domain.Gpx;

/// <summary>Client file names are display-only (ADR 0020 §5): last path segment, no control or format (bidi override, zero-width) chars, no <c>"\/</c>, max 255, must end in .gpx.</summary>
public static class GpxFileName
{
    public const int MaxLength = 255;

    public static bool TryNormalize(string? raw, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var lastSeparator = raw.LastIndexOfAny(['/', '\\']);
        var segment = lastSeparator >= 0 ? raw[(lastSeparator + 1)..] : raw;
        var cleaned = new string(segment.Where(c => !char.IsControl(c) && c != '"' && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray()).Trim();
        if (cleaned.Length > MaxLength)
        {
            // Keep the extension when cutting.
            cleaned = cleaned.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase)
                ? string.Concat(cleaned.AsSpan(0, MaxLength - 4), cleaned.AsSpan(cleaned.Length - 4))
                : cleaned[..MaxLength];
        }

        if (!cleaned.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase) || cleaned.Length == 4)
        {
            return false;
        }

        name = cleaned;
        return true;
    }
}
