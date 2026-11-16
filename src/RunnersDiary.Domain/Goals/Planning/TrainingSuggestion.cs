using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>
/// One suggested session of a goal's plan (ADR 0041). Not a run: it reaches no total, insight or summary until it is accepted, which creates a
/// planned run and deletes the suggestion. The table has a composite FK to the goal (cascade) and none to the user.
/// </summary>
public sealed class TrainingSuggestion : IUserOwned
{
    public const short RestRunTypeId = 41;
    public const int DescriptionMaxLength = 300;
    public const double MinDistanceMeters = 1_000;
    public const double MaxDistanceMeters = 60_000;
    public const int MinDurationSeconds = 600;
    public const int MaxDurationSeconds = 21_600;

    /// <summary>
    /// True unless both distance and duration are set and their implied pace is outside <see cref="GoalRules.MinPaceSecondsPerKm"/>–<see
    /// cref="GoalRules.MaxPaceSecondsPerKm"/> (2:30–15:00 min/km). A positive distance is required for the check; a missing or non-positive
    /// one is reported by the distance rule.
    /// </summary>
    public static bool HasPlausibleImpliedPace(double? distanceMeters, int? durationSeconds)
    {
        if (distanceMeters is not { } distance || durationSeconds is not { } seconds || !(distance > 0))
        {
            return true;
        }

        var scaled = seconds * 1000.0;
        return scaled >= GoalRules.MinPaceSecondsPerKm * distance && scaled <= GoalRules.MaxPaceSecondsPerKm * distance;
    }

    private TrainingSuggestion()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid GoalId { get; private set; }

    public Guid GenerationId { get; private set; }

    public DateOnly Date { get; private set; }

    public short RunTypeId { get; private set; }

    public double? DistanceMeters { get; private set; }

    public int? DurationSeconds { get; private set; }

    public TrainingIntensity Intensity { get; private set; }

    /// <summary>The faster bound of the target pace, in seconds per km; both bounds or neither.</summary>
    public int? TargetPaceMinSecondsPerKm { get; private set; }

    public int? TargetPaceMaxSecondsPerKm { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public TrainingPhase Phase { get; private set; }

    public SuggestionStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>Training run types (ids 20-31) and rest (41) are the only ones a plan may suggest.</summary>
    public static bool IsSuggestibleRunType(short runTypeId) =>
        RunTypeCatalog.Find(runTypeId) is { } type && (type.Category == RunTypeCategory.Training || type.IsRestDay);

    public static TrainingSuggestion Create(Guid userId, Guid goalId, Guid generationId, PlannedSession session, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (userId == Guid.Empty || goalId == Guid.Empty)
        {
            throw new DomainRuleException("A suggestion needs an owner and a goal.");
        }

        var errors = Validate(session);
        if (errors.Count > 0)
        {
            throw new DomainRuleException(errors);
        }

        var utc = now.UtcDateTime;
        return new TrainingSuggestion
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            GoalId = goalId,
            GenerationId = generationId,
            Date = session.Date,
            RunTypeId = session.RunTypeId,
            DistanceMeters = session.DistanceMeters,
            DurationSeconds = session.DurationSeconds,
            Intensity = session.Intensity,
            TargetPaceMinSecondsPerKm = session.TargetPaceMinSecondsPerKm,
            TargetPaceMaxSecondsPerKm = session.TargetPaceMaxSecondsPerKm,
            Description = session.Description,
            Phase = session.Phase,
            Status = SuggestionStatus.Pending,
            CreatedAt = new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc),
        };
    }

    /// <summary>Marks the suggestion declined; idempotent.</summary>
    public void Dismiss() => Status = SuggestionStatus.Dismissed;

    /// <summary>
    /// The default accept mapping (ADR 0041 §2): a planned run on the suggestion's date with its type, distance and duration, the description as
    /// notes, no start time, in the caller's profile zone. The pace range is not stored on runs. Always passes <see cref="RunRules"/>.
    /// </summary>
    public RunValues ToRunValues(string profileTimeZoneId) => new()
    {
        Date = Date,
        StartTime = null,
        TimeZoneId = profileTimeZoneId,
        Status = RunStatus.Planned,
        RunTypeId = RunTypeId,
        DistanceMeters = DistanceMeters,
        DurationSeconds = DurationSeconds,
        Notes = Description,
    };

    private static List<DomainError> Validate(PlannedSession s)
    {
        var errors = new List<DomainError>();
        if (!IsSuggestibleRunType(s.RunTypeId))
        {
            errors.Add(new("runTypeId", "Only training run types and rest can be suggested."));
        }

        if (!Enum.IsDefined(s.Intensity))
        {
            errors.Add(new("intensity", "Unknown intensity."));
        }

        if (!Enum.IsDefined(s.Phase))
        {
            errors.Add(new("phase", "Unknown phase."));
        }

        if (string.IsNullOrWhiteSpace(s.Description) || s.Description.Length > DescriptionMaxLength)
        {
            errors.Add(new("description", $"Must be 1 to {DescriptionMaxLength} characters."));
        }

        var isRest = s.RunTypeId == RestRunTypeId;
        if (isRest != (s.Intensity == TrainingIntensity.Rest))
        {
            errors.Add(new("intensity", "Rest intensity goes with the rest run type only."));
        }

        if (isRest)
        {
            if (s.DistanceMeters is not null || s.DurationSeconds is not null || s.TargetPaceMinSecondsPerKm is not null || s.TargetPaceMaxSecondsPerKm is not null)
            {
                errors.Add(new("distanceMeters", "A rest day has no distance, duration or pace."));
            }

            return errors;
        }

        if (s.DistanceMeters is not { } distance || !(distance >= MinDistanceMeters && distance <= MaxDistanceMeters))
        {
            errors.Add(new("distanceMeters", $"Required, between {MinDistanceMeters:0} and {MaxDistanceMeters:0}."));
        }

        if (s.DurationSeconds is { } seconds && seconds is < MinDurationSeconds or > MaxDurationSeconds)
        {
            errors.Add(new("durationSeconds", $"Must be between {MinDurationSeconds} and {MaxDurationSeconds}."));
        }
        else if (!HasPlausibleImpliedPace(s.DistanceMeters, s.DurationSeconds))
        {
            errors.Add(new(
                "durationSeconds",
                $"With the distance, the implied pace must be between {GoalRules.MinPaceSecondsPerKm} and {GoalRules.MaxPaceSecondsPerKm} seconds per km."));
        }

        if ((s.TargetPaceMinSecondsPerKm is null) != (s.TargetPaceMaxSecondsPerKm is null))
        {
            errors.Add(new("targetPaceMinSecondsPerKm", "Both pace bounds or neither."));
        }
        else if (s.TargetPaceMinSecondsPerKm is { } min && s.TargetPaceMaxSecondsPerKm is { } max
            && (min < GoalRules.MinPaceSecondsPerKm || max > GoalRules.MaxPaceSecondsPerKm || min > max || max > min + 90))
        {
            errors.Add(new("targetPaceMinSecondsPerKm", "Paces must be 150 to 900 seconds per km, minimum at most maximum, at most 90 seconds apart."));
        }

        return errors;
    }
}
