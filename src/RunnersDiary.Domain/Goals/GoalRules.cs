using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Goals;

/// <summary>All goal invariants (ADR 0039 §2) as pure functions. Paths are camelCase JSON paths.</summary>
public static class GoalRules
{
    public const double MinDistanceMeters = 1_000;
    public const double MaxDistanceMeters = 250_000;
    public const double MaxElevationMeters = 10_000;
    public const int MinTargetDays = 7;
    public const int MaxTargetDays = 730;
    public const int MinPaceSecondsPerKm = 150;
    public const int MaxPaceSecondsPerKm = 900;
    public const int TitleMaxLength = 120;
    public const int EventNameMaxLength = 150;
    public const int NotesMaxLength = 1_000;
    public const int LocationNameMaxLength = 200;

    /// <param name="today">The caller's local today.</param>
    /// <param name="previousTargetDate">The stored target date on an update, null on create. An unchanged date is not re-checked, so an approaching goal stays editable.</param>
    /// <param name="raceRun">The caller's own run named by <c>raceRunId</c>; null when none was found (then a set id is "Unknown run.").</param>
    /// <param name="previousRaceRunId">The stored <c>raceRunId</c> on an update, null on create. An unchanged link only has to still name one of the caller's runs: the run may have been completed or moved since.</param>
    public static IReadOnlyList<DomainError> Validate(
        GoalValues values, DateOnly today, DateOnly? previousTargetDate, LinkedRaceRun? raceRun, Guid? previousRaceRunId = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        var errors = new List<DomainError>();

        if (!Enum.IsDefined(values.Type))
        {
            errors.Add(new("type", "Unknown goal type."));
        }

        if (!Enum.IsDefined(values.Status))
        {
            errors.Add(new("status", "Unknown status."));
        }

        if (values.Surface is { } surface && !Enum.IsDefined(surface))
        {
            errors.Add(new("surface", "Unknown surface."));
        }

        if (string.IsNullOrWhiteSpace(values.Title))
        {
            errors.Add(new("title", "Required."));
        }

        MaxLength(errors, "title", values.Title, TitleMaxLength);
        MaxLength(errors, "eventName", values.EventName, EventNameMaxLength);
        MaxLength(errors, "notes", values.Notes, NotesMaxLength);

        var distanceValid = values.DistanceMeters is >= MinDistanceMeters and <= MaxDistanceMeters;
        if (!distanceValid)
        {
            errors.Add(new("distanceMeters", $"Must be between {MinDistanceMeters:0} and {MaxDistanceMeters:0}."));
        }

        if (values.ElevationGainMeters is { } gain && !(gain >= 0 && gain <= MaxElevationMeters))
        {
            errors.Add(new("elevationGainMeters", $"Must be between 0 and {MaxElevationMeters:0}."));
        }

        if (previousTargetDate is null || previousTargetDate != values.TargetDate)
        {
            var earliest = today.AddDays(MinTargetDays);
            var latest = today.AddDays(MaxTargetDays);
            if (values.TargetDate < earliest || values.TargetDate > latest)
            {
                errors.Add(new("targetDate", $"Must be between {earliest:yyyy-MM-dd} and {latest:yyyy-MM-dd} ({MinTargetDays} to {MaxTargetDays} days from today)."));
            }
        }

        ValidateTarget(errors, values, distanceValid);
        ValidateLocation(errors, values.Location);
        ValidateRaceRun(errors, values, raceRun, previousRaceRunId);
        return errors;
    }

    /// <summary>
    /// The subset the realistic-target estimate needs (ADR 0039 §4): known enums, distance, elevation, a target date from today to today + 730 and,
    /// when a target time is given, the implied pace. Unlike <see cref="Validate"/> it has no 7-day minimum and no type-specific requirements, so the
    /// hint keeps working while a goal is edited or the target time is still empty.
    /// </summary>
    public static IReadOnlyList<DomainError> ValidateEstimate(GoalSpec spec, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var errors = new List<DomainError>();
        if (!Enum.IsDefined(spec.Type))
        {
            errors.Add(new("type", "Unknown goal type."));
        }

        if (spec.Surface is { } surface && !Enum.IsDefined(surface))
        {
            errors.Add(new("surface", "Unknown surface."));
        }

        var distanceValid = spec.DistanceMeters is >= MinDistanceMeters and <= MaxDistanceMeters;
        if (!distanceValid)
        {
            errors.Add(new("distanceMeters", $"Must be between {MinDistanceMeters:0} and {MaxDistanceMeters:0}."));
        }

        if (spec.ElevationGainMeters is { } gain && !(gain >= 0 && gain <= MaxElevationMeters))
        {
            errors.Add(new("elevationGainMeters", $"Must be between 0 and {MaxElevationMeters:0}."));
        }

        var latest = today.AddDays(MaxTargetDays);
        if (spec.TargetDate < today || spec.TargetDate > latest)
        {
            errors.Add(new("targetDate", $"Must be between {today:yyyy-MM-dd} and {latest:yyyy-MM-dd}."));
        }

        if (spec.TargetDurationSeconds is { } seconds)
        {
            if (seconds < 1)
            {
                errors.Add(new("targetDurationSeconds", "Must be at least 1."));
            }
            else if (distanceValid
                && (seconds * 1000.0 < MinPaceSecondsPerKm * spec.DistanceMeters || seconds * 1000.0 > MaxPaceSecondsPerKm * spec.DistanceMeters))
            {
                errors.Add(new("targetDurationSeconds", $"The implied pace must be between {MinPaceSecondsPerKm} and {MaxPaceSecondsPerKm} seconds per km (2:30 to 15:00 min/km)."));
            }
        }

        return errors;
    }

    /// <summary>Throws <see cref="DomainRuleException"/> when <see cref="Validate"/> reports any error.</summary>
    public static void EnsureValid(
        GoalValues values, DateOnly today, DateOnly? previousTargetDate, LinkedRaceRun? raceRun, Guid? previousRaceRunId = null)
    {
        var errors = Validate(values, today, previousTargetDate, raceRun, previousRaceRunId);
        if (errors.Count > 0)
        {
            throw new DomainRuleException(errors);
        }
    }

    private static void ValidateTarget(List<DomainError> errors, GoalValues v, bool distanceValid)
    {
        switch (v.Type)
        {
            case GoalType.Race when string.IsNullOrWhiteSpace(v.EventName):
                errors.Add(new("eventName", "Required for a Race goal."));
                break;
            case GoalType.Performance when v.TargetDurationSeconds is null:
                errors.Add(new("targetDurationSeconds", "Required for a Performance goal."));
                break;
            case GoalType.Distance when v.TargetDurationSeconds is not null:
                errors.Add(new("targetDurationSeconds", "Not allowed for a Distance goal; use a Performance goal for a target time."));
                break;
        }

        if (v.TargetDurationSeconds is not { } seconds)
        {
            return;
        }

        if (seconds < 1)
        {
            errors.Add(new("targetDurationSeconds", "Must be at least 1."));
        }
        else if (distanceValid)
        {
            // Implied pace = seconds / (metres / 1000), compared without dividing so the bounds are exact.
            var scaled = seconds * 1000.0;
            if (scaled < MinPaceSecondsPerKm * v.DistanceMeters || scaled > MaxPaceSecondsPerKm * v.DistanceMeters)
            {
                errors.Add(new("targetDurationSeconds", $"The implied pace must be between {MinPaceSecondsPerKm} and {MaxPaceSecondsPerKm} seconds per km (2:30 to 15:00 min/km)."));
            }
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

        MaxLength(errors, "location.name", location.Name, LocationNameMaxLength);
    }

    private static void ValidateRaceRun(List<DomainError> errors, GoalValues v, LinkedRaceRun? raceRun, Guid? previousRaceRunId)
    {
        if (v.RaceRunId is null)
        {
            return;
        }

        if (raceRun is null || raceRun.Id != v.RaceRunId)
        {
            errors.Add(new("raceRunId", "Unknown run."));
            return;
        }

        if (previousRaceRunId == v.RaceRunId)
        {
            return;
        }

        if (!raceRun.IsRaceCategory)
        {
            errors.Add(new("raceRunId", "Must be a race run."));
        }
        else if (raceRun.Status != RunStatus.Planned)
        {
            errors.Add(new("raceRunId", "Must be a planned run."));
        }
        else if (raceRun.Date != v.TargetDate)
        {
            errors.Add(new("raceRunId", "The run must be dated on the target date."));
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
