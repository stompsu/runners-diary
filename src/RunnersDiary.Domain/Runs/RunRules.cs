using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.RunTypes;
using RunnersDiary.Domain.Time;

namespace RunnersDiary.Domain.Runs;

/// <summary>All run invariants (api-contract.md, "Domain rules") as pure functions. Paths are camelCase JSON paths.</summary>
public static class RunRules
{
    public static readonly DateOnly MinDate = new(1950, 1, 1);
    public static readonly DateOnly MaxDate = new(2100, 12, 31);

    public const double MaxDistanceMeters = 1_000_000;
    public const int MaxDurationSeconds = 604_800;
    public const double MaxElevationMeters = 100_000;
    public const int MinHeartRate = 30;
    public const int MaxHeartRate = 250;

    public static IReadOnlyList<DomainError> Validate(RunValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        var errors = new List<DomainError>();

        if (v.Date < MinDate || v.Date > MaxDate)
        {
            errors.Add(new("date", $"Must be between {MinDate:yyyy-MM-dd} and {MaxDate:yyyy-MM-dd}."));
        }

        if (!IanaTimeZone.IsValid(v.TimeZoneId))
        {
            errors.Add(new("timeZoneId", "Must be a valid IANA time zone id (for example Europe/Warsaw)."));
        }

        if (!Enum.IsDefined(v.Status))
        {
            errors.Add(new("status", "Unknown status."));
        }

        if (v.Surface is { } surface && !Enum.IsDefined(surface))
        {
            errors.Add(new("surface", "Unknown surface."));
        }

        MaxLength(errors, "title", v.Title, 120);
        MaxLength(errors, "notes", v.Notes, 4000);
        MaxLength(errors, "gear", v.Gear, 100);
        MaxLength(errors, "weather", v.Weather, 200);

        var type = RunTypeCatalog.Find(v.RunTypeId);
        if (type is null)
        {
            errors.Add(new("runTypeId", "Unknown run type."));
        }

        ValidateMetrics(errors, v);
        ValidateLocation(errors, v.Location);

        if (v.Rating is { } rating)
        {
            if (v.Status != RunStatus.Completed)
            {
                errors.Add(new("rating", "A rating is only allowed on a completed run."));
            }

            if (rating.Stars is < 1 or > 5)
            {
                errors.Add(new("rating.stars", "Must be between 1 and 5."));
            }

            MaxLength(errors, "rating.comment", rating.Comment, 500);
        }

        if (v.Race is { } race)
        {
            if (type is not null && type.Category != RunTypeCategory.Race)
            {
                errors.Add(new("race", "Race details are only allowed for race run types."));
            }

            ValidateRace(errors, race, v.Status);
        }

        if (type is { IsRestDay: true })
        {
            RestDayNoValue(errors, "distanceMeters", v.DistanceMeters is not null);
            RestDayNoValue(errors, "durationSeconds", v.DurationSeconds is not null);
            RestDayNoValue(errors, "elevationGainMeters", v.ElevationGainMeters is not null);
            RestDayNoValue(errors, "elevationLossMeters", v.ElevationLossMeters is not null);
            RestDayNoValue(errors, "avgHeartRate", v.AvgHeartRate is not null);
            RestDayNoValue(errors, "maxHeartRate", v.MaxHeartRate is not null);
            RestDayNoValue(errors, "rpe", v.Rpe is not null);
            RestDayNoValue(errors, "rating", v.Rating is not null);
        }

        return errors;
    }

    /// <summary>Throws <see cref="DomainRuleException"/> when <see cref="Validate"/> reports any error.</summary>
    public static void EnsureValid(RunValues values)
    {
        var errors = Validate(values);
        if (errors.Count > 0)
        {
            throw new DomainRuleException(errors);
        }
    }

    private static void ValidateMetrics(List<DomainError> errors, RunValues v)
    {
        if (v.DistanceMeters is { } d && !(d > 0 && d <= MaxDistanceMeters))
        {
            errors.Add(new("distanceMeters", $"Must be greater than 0 and at most {MaxDistanceMeters:0}."));
        }

        if (v.DurationSeconds is { } s && s is < 1 or > MaxDurationSeconds)
        {
            errors.Add(new("durationSeconds", $"Must be between 1 and {MaxDurationSeconds}."));
        }

        Elevation(errors, "elevationGainMeters", v.ElevationGainMeters);
        Elevation(errors, "elevationLossMeters", v.ElevationLossMeters);

        var avgOk = HeartRate(errors, "avgHeartRate", v.AvgHeartRate);
        var maxOk = HeartRate(errors, "maxHeartRate", v.MaxHeartRate);
        if (avgOk && maxOk && v.AvgHeartRate is { } avg && v.MaxHeartRate is { } max && max < avg)
        {
            errors.Add(new("maxHeartRate", "Must be greater than or equal to avgHeartRate."));
        }

        if (v.Rpe is < 1 or > 10)
        {
            errors.Add(new("rpe", "Must be between 1 and 10."));
        }
    }

    private static void ValidateLocation(List<DomainError> errors, RunLocation? location)
    {
        if (location is null)
        {
            return;
        }

        if (!double.IsFinite(location.Point.Latitude) || location.Point.Latitude is < -90 or > 90)
        {
            errors.Add(new("location.latitude", "Must be between -90 and 90."));
        }

        if (!double.IsFinite(location.Point.Longitude) || location.Point.Longitude is < -180 or > 180)
        {
            errors.Add(new("location.longitude", "Must be between -180 and 180."));
        }

        MaxLength(errors, "location.name", location.Name, 200);
    }

    private static void ValidateRace(List<DomainError> errors, RaceDetails race, RunStatus status)
    {
        if (string.IsNullOrWhiteSpace(race.Name))
        {
            errors.Add(new("race.name", "Required."));
        }

        MaxLength(errors, "race.name", race.Name, 150);

        if (!Enum.IsDefined(race.DistanceCategory))
        {
            errors.Add(new("race.distanceCategory", "Unknown distance category."));
        }

        if (race.OfficialDistanceMeters is { } od && !(od > 0 && od <= MaxDistanceMeters))
        {
            errors.Add(new("race.officialDistanceMeters", $"Must be greater than 0 and at most {MaxDistanceMeters:0}."));
        }

        MaxLength(errors, "race.bibNumber", race.BibNumber, 20);
        MaxLength(errors, "race.categoryName", race.CategoryName, 50);

        PositiveSeconds(errors, "race.targetTimeSeconds", race.TargetTimeSeconds);
        PositiveSeconds(errors, "race.resultTimeSeconds", race.ResultTimeSeconds);
        Place(errors, "race.overallPlace", race.OverallPlace);
        Place(errors, "race.categoryPlace", race.CategoryPlace);

        if (status != RunStatus.Completed)
        {
            CompletedOnly(errors, "race.resultTimeSeconds", race.ResultTimeSeconds is not null);
            CompletedOnly(errors, "race.overallPlace", race.OverallPlace is not null);
            CompletedOnly(errors, "race.categoryPlace", race.CategoryPlace is not null);
        }

        if (race.ResultsUrl is { } url)
        {
            if (url.Length > 2048)
            {
                errors.Add(new("race.resultsUrl", "Must be at most 2048 characters."));
            }
            else if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add(new("race.resultsUrl", "Must be an absolute http(s) URL."));
            }
        }
    }

    private static void Elevation(List<DomainError> errors, string path, double? value)
    {
        if (value is { } e && !(e >= 0 && e <= MaxElevationMeters))
        {
            errors.Add(new(path, $"Must be between 0 and {MaxElevationMeters:0}."));
        }
    }

    private static bool HeartRate(List<DomainError> errors, string path, int? value)
    {
        if (value is { } hr && hr is < MinHeartRate or > MaxHeartRate)
        {
            errors.Add(new(path, $"Must be between {MinHeartRate} and {MaxHeartRate}."));
            return false;
        }

        return true;
    }

    private static void PositiveSeconds(List<DomainError> errors, string path, int? value)
    {
        if (value is { } s && s is < 1 or > MaxDurationSeconds)
        {
            errors.Add(new(path, $"Must be between 1 and {MaxDurationSeconds}."));
        }
    }

    private static void Place(List<DomainError> errors, string path, int? value)
    {
        if (value is < 1)
        {
            errors.Add(new(path, "Must be at least 1."));
        }
    }

    private static void CompletedOnly(List<DomainError> errors, string path, bool present)
    {
        if (present)
        {
            errors.Add(new(path, "Only allowed on a completed run."));
        }
    }

    private static void RestDayNoValue(List<DomainError> errors, string path, bool present)
    {
        if (present)
        {
            errors.Add(new(path, "Not allowed on a rest day."));
        }
    }

    private static void MaxLength(List<DomainError> errors, string path, string? value, int max)
    {
        if (value is not null && value.Length > max)
        {
            errors.Add(new(path, $"Must be at most {max} characters."));
        }
    }
}
