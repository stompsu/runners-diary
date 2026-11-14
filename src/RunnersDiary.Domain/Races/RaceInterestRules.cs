using RunnersDiary.Domain.Common;

namespace RunnersDiary.Domain.Races;

/// <summary>All race interest invariants (ADR 0037 §1) as pure functions. Paths are camelCase JSON paths, like <c>RunRules</c>.</summary>
public static class RaceInterestRules
{
    public const int MaxRegions = 16;
    public const int MinRadiusMeters = 5_000;
    public const int MaxRadiusMeters = 300_000;
    public const double MinDistanceMeters = 1_000;
    public const double MaxDistanceMeters = 250_000;
    public const int MinHorizonDays = 7;
    public const int MaxHorizonDays = 365;
    public const int KeywordsMaxLength = 100;
    public const int MaxKeywordTerms = 5;
    public const int MinKeywordTermLength = 2;
    public const int MaxKeywordTermLength = 30;
    public const int CenterNameMaxLength = 200;

    public static IReadOnlyList<DomainError> Validate(RaceInterestValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        var errors = new List<DomainError>();

        if (v.Kinds is null || v.Kinds.Count == 0)
        {
            errors.Add(new("kinds", "Select at least one kind."));
        }
        else if (v.Kinds.Any(k => !Enum.IsDefined(k)))
        {
            errors.Add(new("kinds", "Unknown kind."));
        }

        if (v.RegionCodes is null)
        {
            errors.Add(new("regionCodes", "Required (may be empty)."));
        }
        else
        {
            if (v.RegionCodes.Count > MaxRegions)
            {
                errors.Add(new("regionCodes", $"At most {MaxRegions} regions."));
            }

            if (v.RegionCodes.Any(c => !PolishRegions.IsKnown(c)))
            {
                errors.Add(new("regionCodes", "Unknown region code (expected ISO 3166-2 such as PL-24)."));
            }
        }

        if (v.Center is null && v.RadiusMeters is not null)
        {
            errors.Add(new("center", "Required together with radiusMeters."));
        }

        if (v.Center is not null && v.RadiusMeters is null)
        {
            errors.Add(new("radiusMeters", "Required together with center."));
        }

        if (v.RadiusMeters is { } radius && radius is < MinRadiusMeters or > MaxRadiusMeters)
        {
            errors.Add(new("radiusMeters", $"Must be between {MinRadiusMeters} and {MaxRadiusMeters}."));
        }

        if (v.CenterName is { Length: > CenterNameMaxLength })
        {
            errors.Add(new("centerName", $"Must be at most {CenterNameMaxLength} characters."));
        }

        var minOk = Distance(errors, "minDistanceMeters", v.MinDistanceMeters);
        var maxOk = Distance(errors, "maxDistanceMeters", v.MaxDistanceMeters);
        if (minOk && maxOk && v.MinDistanceMeters is { } min && v.MaxDistanceMeters is { } max && min > max)
        {
            errors.Add(new("maxDistanceMeters", "Must be greater than or equal to minDistanceMeters."));
        }

        if (v.HorizonDays is < MinHorizonDays or > MaxHorizonDays)
        {
            errors.Add(new("horizonDays", $"Must be between {MinHorizonDays} and {MaxHorizonDays}."));
        }

        ValidateKeywords(errors, v.Keywords);
        return errors;
    }

    /// <summary>Throws <see cref="DomainRuleException"/> when <see cref="Validate"/> reports any error.</summary>
    public static void EnsureValid(RaceInterestValues values)
    {
        var errors = Validate(values);
        if (errors.Count > 0)
        {
            throw new DomainRuleException(errors);
        }
    }

    /// <summary>The whitespace-separated, trimmed keyword terms (empty when none).</summary>
    public static IReadOnlyList<string> KeywordTerms(string? keywords) =>
        string.IsNullOrWhiteSpace(keywords) ? [] : keywords.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Single-spaced keywords, or null when empty.</summary>
    public static string? NormalizeKeywords(string? keywords)
    {
        var terms = KeywordTerms(keywords);
        return terms.Count == 0 ? null : string.Join(' ', terms);
    }

    private static void ValidateKeywords(List<DomainError> errors, string? keywords)
    {
        var terms = KeywordTerms(keywords);
        if (terms.Count == 0)
        {
            return;
        }

        if (string.Join(' ', terms).Length > KeywordsMaxLength)
        {
            errors.Add(new("keywords", $"Must be at most {KeywordsMaxLength} characters."));
        }

        if (terms.Count > MaxKeywordTerms)
        {
            errors.Add(new("keywords", $"At most {MaxKeywordTerms} terms."));
        }

        if (terms.Any(t => t.Length is < MinKeywordTermLength or > MaxKeywordTermLength))
        {
            errors.Add(new("keywords", $"Each term must be {MinKeywordTermLength} to {MaxKeywordTermLength} characters."));
        }
    }

    private static bool Distance(List<DomainError> errors, string path, double? value)
    {
        if (value is { } d && !(d >= MinDistanceMeters && d <= MaxDistanceMeters))
        {
            errors.Add(new(path, $"Must be between {MinDistanceMeters:0} and {MaxDistanceMeters:0}."));
            return false;
        }

        return true;
    }
}
