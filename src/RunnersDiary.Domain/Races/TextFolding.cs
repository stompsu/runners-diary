using System.Globalization;
using System.Text;

namespace RunnersDiary.Domain.Races;

/// <summary>Case- and diacritic-insensitive text folding shared by the normaliser, regions, matcher and keyword filter ("Łódź" and "lodz" fold alike).</summary>
internal static class TextFolding
{
    /// <summary>Lower-case, diacritics removed (<c>ł</c> becomes <c>l</c>), every run of non letter/digit characters collapsed to one space, trimmed.</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var raw in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(raw);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var c = char.ToLowerInvariant(raw);
            c = c switch
            {
                'ł' => 'l',
                'đ' => 'd',
                'ø' => 'o',
                'ß' => 's',
                _ => c,
            };

            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                }

                pendingSpace = false;
                sb.Append(c);
            }
            else
            {
                pendingSpace = true;
            }
        }

        return sb.ToString();
    }

    /// <summary>Truncates to UTC with second precision, the audit-timestamp convention of the model.</summary>
    public static DateTime TruncateUtc(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    }
}
