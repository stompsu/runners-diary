using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>The demo goal of <c>seed/demo/training-plan.json</c> (ADR 0041 §5); the target date is week-relative like <c>runs.json</c>.</summary>
public sealed record DemoPlanGoal(
    GoalType Type, string Title, double DistanceMeters, double? ElevationGainMeters, Surface? Surface, int Week, DayOfWeek Day)
{
    public DateOnly TargetDate(DateOnly today) => DemoTrainingPlanTemplate.DateOf(today, Week, Day);

    /// <summary>The planning fields (what the envelope and the validator see).</summary>
    public GoalSpec Spec(DateOnly today) => new(Type, DistanceMeters, ElevationGainMeters, TargetDate(today), null, Surface);
}

/// <summary>One week-relative example session; the codes are the model schema's (ADR 0040 §4).</summary>
public sealed record DemoPlanSession(
    int Week,
    DayOfWeek Day,
    string RunType,
    string Intensity,
    string Phase,
    double? DistanceMeters,
    int? DurationSeconds,
    int? PaceMinSecondsPerKm,
    int? PaceMaxSecondsPerKm,
    string Description);

/// <summary>
/// The guest demo plan (<c>seed/demo/training-plan.json</c>, ADR 0041 §5): the demo goal and a hand-written example plan, week-relative to the
/// current ISO week in the demo dataset's zone (week 0 = this week, like <c>seed/demo/runs.json</c>). <see cref="Draft"/> gives the plan with
/// real dates in the model's own shape, so the same <see cref="TrainingPlanValidator"/> checks it against the demo's own envelope.
/// Strict parsing: unknown members or values throw <see cref="DomainRuleException"/>.
/// </summary>
public sealed record DemoTrainingPlanTemplate(int Version, DemoPlanGoal Goal, string Notes, IReadOnlyList<DemoPlanSession> Sessions)
{
    public const int SupportedVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    /// <summary>The example plan for <paramref name="today"/> (the demo zone's local date) as a model-shaped draft.</summary>
    public TrainingPlanDraft Draft(DateOnly today) => new(
        Notes,
        [
            .. Sessions.Select(s => new DraftSession(
                DateOf(today, s.Week, s.Day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                s.RunType, s.Intensity, s.Phase, s.DistanceMeters, s.DurationSeconds, s.PaceMinSecondsPerKm, s.PaceMaxSecondsPerKm, s.Description)),
        ]);

    public static DateOnly DateOf(DateOnly today, int week, DayOfWeek day) =>
        IsoWeek.MondayOf(today).AddDays((week * 7) + (((int)day + 6) % 7));

    public static DemoTrainingPlanTemplate Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        FileDto file;
        try
        {
            file = JsonSerializer.Deserialize<FileDto>(json, Options) ?? throw new DomainRuleException("The demo training plan is empty.");
        }
        catch (JsonException ex)
        {
            throw new DomainRuleException("The demo training plan is not valid: " + ex.Message, ex);
        }

        if (file.Version != SupportedVersion)
        {
            throw new DomainRuleException($"Unsupported demo training plan version {file.Version}.");
        }

        var goal = file.Goal ?? throw new DomainRuleException("The demo training plan needs a goal.");
        if (string.IsNullOrWhiteSpace(goal.Title) || goal.Type is null || goal.DistanceMeters is not > 0 || file.Sessions is null || file.Notes is null)
        {
            throw new DomainRuleException("The demo training plan needs goal type, title and distance, notes and sessions.");
        }

        return new DemoTrainingPlanTemplate(
            file.Version,
            new DemoPlanGoal(goal.Type.Value, goal.Title, goal.DistanceMeters.Value, goal.ElevationGainMeters, goal.Surface, goal.Week, ParseDay(goal.Day)),
            file.Notes,
            [
                .. file.Sessions.Select(s => new DemoPlanSession(
                    s.Week,
                    ParseDay(s.Day),
                    s.Type ?? throw new DomainRuleException("A demo plan session needs a type."),
                    s.Intensity ?? throw new DomainRuleException("A demo plan session needs an intensity."),
                    s.Phase ?? throw new DomainRuleException("A demo plan session needs a phase."),
                    s.DistanceMeters,
                    s.DurationSeconds,
                    s.PaceMinSecondsPerKm,
                    s.PaceMaxSecondsPerKm,
                    s.Description ?? throw new DomainRuleException("A demo plan session needs a description."))),
            ]);
    }

    private static DayOfWeek ParseDay(string? day) => day switch
    {
        "Mon" => DayOfWeek.Monday,
        "Tue" => DayOfWeek.Tuesday,
        "Wed" => DayOfWeek.Wednesday,
        "Thu" => DayOfWeek.Thursday,
        "Fri" => DayOfWeek.Friday,
        "Sat" => DayOfWeek.Saturday,
        "Sun" => DayOfWeek.Sunday,
        _ => throw new DomainRuleException($"Unknown day '{day}' in the demo training plan."),
    };

    private sealed record FileDto(int Version, GoalDto? Goal, string? Notes, List<SessionDto>? Sessions);

    private sealed record GoalDto(GoalType? Type, string? Title, double? DistanceMeters, double? ElevationGainMeters, Surface? Surface, int Week, string? Day);

    private sealed record SessionDto(
        int Week,
        string? Day,
        string? Type,
        string? Intensity,
        string? Phase,
        double? DistanceMeters,
        int? DurationSeconds,
        int? PaceMinSecondsPerKm,
        int? PaceMaxSecondsPerKm,
        string? Description);
}
