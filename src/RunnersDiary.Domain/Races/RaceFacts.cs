using System.Text;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Races;

/// <summary>
/// The normalised facts of one race as an importer reports them (ADR 0035 §2: facts only, no prose, no personal data).
/// Use <see cref="Normalized"/> to clean raw values and <see cref="Validate"/> for the invariants; <see cref="Race.Create"/>, the hasher and the merger do both.
/// </summary>
public sealed record RaceFacts(
    RaceSource Source,
    string ExternalId,
    string Name,
    DateOnly Date,
    TimeOnly? StartTime,
    string? LocationName,
    GeoPoint? Point,
    string CountryCode,
    string? RegionCode,
    RaceKind Kind,
    Surface? Surface,
    IReadOnlyList<double> DistancesMeters,
    double? ElevationGainMeters,
    string? SignupUrl,
    string? EventUrl,
    string? SourceUrl,
    decimal? PriceAmount,
    string? PriceCurrency,
    DateOnly? SignupDeadline,
    string? OrganizerName)
{
    public const int NameMaxLength = 200;
    public const int ExternalIdMaxLength = 100;
    public const int LocationNameMaxLength = 200;
    public const int OrganizerNameMaxLength = 150;
    public const int UrlMaxLength = 2048;
    public const int MaxDistances = 12;
    public const double MinDistanceMeters = 1_000;
    public const double MaxDistanceMeters = 250_000;
    public const decimal MaxPriceAmount = 9_999_999.99m;
    public static readonly DateOnly MinDate = new(1950, 1, 1);
    public static readonly DateOnly MaxDate = new(2100, 12, 31);

    /// <summary>
    /// Cleans raw values and returns a canonical copy; idempotent. Name and place lose control characters and surplus white space,
    /// empty strings become null, codes are upper-cased, distances are rounded to 0.1 m, deduplicated and sorted, the start time loses sub-second digits,
    /// and an organiser name that looks like an e-mail address or a phone number becomes null.
    /// </summary>
    public RaceFacts Normalized() => this with
    {
        ExternalId = (ExternalId ?? string.Empty).Trim(),
        Name = CleanText(Name) ?? string.Empty,
        StartTime = StartTime is { } t ? new TimeOnly(t.Hour, t.Minute, t.Second) : null,
        LocationName = CleanText(LocationName),
        CountryCode = (CountryCode ?? string.Empty).Trim().ToUpperInvariant(),
        RegionCode = Blank(RegionCode)?.ToUpperInvariant(),
        DistancesMeters = CleanDistances(DistancesMeters),
        SignupUrl = Blank(SignupUrl),
        EventUrl = Blank(EventUrl),
        SourceUrl = Blank(SourceUrl),
        PriceCurrency = Blank(PriceCurrency)?.ToUpperInvariant(),
        OrganizerName = LooksPersonal(CleanText(OrganizerName)) ? null : CleanText(OrganizerName),
    };

    /// <summary>Invariants of the catalogue (data-model.md, "Races"). Call on normalised facts; paths are camelCase.</summary>
    public IReadOnlyList<DomainError> Validate()
    {
        var errors = new List<DomainError>();

        if (!Enum.IsDefined(Source))
        {
            errors.Add(new("source", "Unknown source."));
        }

        if (string.IsNullOrEmpty(ExternalId) || ExternalId.Length > ExternalIdMaxLength)
        {
            errors.Add(new("externalId", $"Must be 1 to {ExternalIdMaxLength} characters."));
        }

        if (string.IsNullOrEmpty(Name) || Name.Length > NameMaxLength)
        {
            errors.Add(new("name", $"Must be 1 to {NameMaxLength} characters."));
        }

        if (Date < MinDate || Date > MaxDate)
        {
            errors.Add(new("date", $"Must be between {MinDate:yyyy-MM-dd} and {MaxDate:yyyy-MM-dd}."));
        }

        if (LocationName is { Length: > LocationNameMaxLength })
        {
            errors.Add(new("locationName", $"Must be at most {LocationNameMaxLength} characters."));
        }

        if (CountryCode is not { Length: 2 } || !CountryCode.All(c => c is >= 'A' and <= 'Z'))
        {
            errors.Add(new("countryCode", "Must be an ISO 3166-1 alpha-2 code."));
        }

        if (RegionCode is { } region && !(region.Length is >= 4 and <= 6 && region[2] == '-' && region.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-')))
        {
            errors.Add(new("regionCode", "Must be an ISO 3166-2 code such as PL-24."));
        }

        if (!Enum.IsDefined(Kind))
        {
            errors.Add(new("kind", "Unknown kind."));
        }

        if (Surface is { } surface && !Enum.IsDefined(surface))
        {
            errors.Add(new("surface", "Unknown surface."));
        }

        ValidateDistances(errors);

        if (ElevationGainMeters is { } e && !(e >= 0 && e <= RunRules.MaxElevationMeters))
        {
            errors.Add(new("elevationGainMeters", $"Must be between 0 and {RunRules.MaxElevationMeters:0}."));
        }

        Url(errors, "signupUrl", SignupUrl);
        Url(errors, "eventUrl", EventUrl);
        Url(errors, "sourceUrl", SourceUrl);
        if (Source != RaceSource.Demo && SourceUrl is null)
        {
            errors.Add(new("sourceUrl", "Required for real sources (attribution link)."));
        }

        if ((PriceAmount is null) != (PriceCurrency is null))
        {
            errors.Add(new("priceAmount", "Price amount and currency are set together or not at all."));
        }

        if (PriceAmount is { } price && !(price >= 0 && price <= MaxPriceAmount))
        {
            errors.Add(new("priceAmount", $"Must be between 0 and {MaxPriceAmount}."));
        }

        if (PriceCurrency is { } currency && (currency.Length != 3 || !currency.All(c => c is >= 'A' and <= 'Z')))
        {
            errors.Add(new("priceCurrency", "Must be an ISO 4217 code of three letters."));
        }

        if (SignupDeadline is { } deadline && (deadline < MinDate || deadline > MaxDate))
        {
            errors.Add(new("signupDeadline", $"Must be between {MinDate:yyyy-MM-dd} and {MaxDate:yyyy-MM-dd}."));
        }

        if (OrganizerName is { } organizer && (organizer.Length > OrganizerNameMaxLength || LooksPersonal(organizer)))
        {
            errors.Add(new("organizerName", $"Must be at most {OrganizerNameMaxLength} characters and no e-mail address or phone number."));
        }

        return errors;
    }

    /// <summary>Normalises, then validates; returns false with the errors when the facts are not storable.</summary>
    public static bool TryNormalize(RaceFacts raw, out RaceFacts facts, out IReadOnlyList<DomainError> errors)
    {
        ArgumentNullException.ThrowIfNull(raw);
        facts = raw.Normalized();
        errors = facts.Validate();
        return errors.Count == 0;
    }

    /// <summary>True when the text looks like an e-mail address or a phone number (never stored as an organiser name).</summary>
    internal static bool LooksPersonal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Contains('@', StringComparison.Ordinal))
        {
            return true;
        }

        var digits = 0;
        var other = 0;
        foreach (var c in text)
        {
            if (char.IsDigit(c))
            {
                digits++;
            }
            else if (char.IsLetter(c))
            {
                other++;
            }
        }

        // A phone number is mostly digits (with + - ( ) and spaces); "Bieg 2000" stays a name.
        return digits >= 7 && digits > other;
    }

    private void ValidateDistances(List<DomainError> errors)
    {
        if (DistancesMeters is null)
        {
            errors.Add(new("distancesMeters", "Required (may be empty)."));
            return;
        }

        if (DistancesMeters.Count > MaxDistances)
        {
            errors.Add(new("distancesMeters", $"At most {MaxDistances} distances."));
        }

        if (DistancesMeters.Any(d => !(d >= MinDistanceMeters && d <= MaxDistanceMeters)))
        {
            errors.Add(new("distancesMeters", $"Each distance must be between {MinDistanceMeters:0} and {MaxDistanceMeters:0} metres."));
        }
    }

    private static void Url(List<DomainError> errors, string path, string? value)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > UrlMaxLength)
        {
            errors.Add(new(path, $"Must be at most {UrlMaxLength} characters."));
        }
        else if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add(new(path, "Must be an absolute http(s) URL."));
        }
    }

    private static string? Blank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? CleanText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var sb = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (char.IsControl(c))
            {
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    private static List<double> CleanDistances(IReadOnlyList<double>? distances) =>
        (distances ?? [])
            .Select(d => double.IsFinite(d) ? Math.Round(d, 1, MidpointRounding.AwayFromZero) : d)
            .Distinct()
            .Order()
            .ToList();
}
