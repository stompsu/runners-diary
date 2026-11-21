using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>The constants of the plan envelope (ADR 0040 §2; v2 = v1 plus the 500 m cap step; v3 = M9.1: reference pace from fitness, easy-pace bound). Changing one bumps <see cref="PlanFactSheet.CurrentVersion"/>.</summary>
public static class PlanEnvelopeRules
{
    public const int Version = 3;

    /// <summary>
    /// Weekly caps, available distance and long-run caps are rounded <b>down</b> to this step (v2, ADR 0040 amendment "plan reliability"): the
    /// model hits round numbers it can add up (28,000 m, not 28,286 m), and the validator checks the same rounded values.
    /// </summary>
    public const double CapStepMeters = 500;
    public const int MaxHorizonWeeks = 16;
    public const double WeeklyGrowth = 1.10;
    public const double MinBaseVolumeMeters = 10_000;
    public const double WeeklyCeilingMeters = 160_000;
    public const double LongRunFloorMeters = 8_000;
    public const int MinSessionsPerWeek = 3;
    public const int MaxSessionsPerWeek = 7;
    public const double ReferencePaceFromMeanPace = 0.90;

    /// <summary>Easy and recovery sessions may be at most ~5 % faster than the runner's recent easy pace (v3).</summary>
    public const double EasyPaceShareOfRecent = 0.95;

    /// <summary>A <c>racePace</c> session's faster bound is at least this share of the reference pace (a 3 % band, v3).</summary>
    public const double RacePaceShareOfReference = 0.97;

    /// <summary>Rounds a non-negative cap down to <see cref="CapStepMeters"/>.</summary>
    public static double FloorToStep(double meters) =>
        meters <= 0 ? 0 : Math.Floor((meters + 1e-6) / CapStepMeters) * CapStepMeters;

    /// <summary>Taper length in weeks by goal distance: under 15 km 1, under 30 km 2, otherwise 3.</summary>
    public static int TaperWeeks(double goalDistanceMeters) =>
        goalDistanceMeters < 15_000 ? 1 : goalDistanceMeters < 30_000 ? 2 : 3;

    /// <summary>The factors on the last pre-taper cap, first taper week first.</summary>
    public static IReadOnlyList<double> TaperFactors(int taperWeeks) => taperWeeks switch
    {
        1 => [0.6],
        2 => [0.75, 0.55],
        _ => [0.8, 0.65, 0.5],
    };

    /// <summary>Hard ceiling of the long run by goal distance: under 15 km 18 km, under 30 km 24 km, up to 42.2 km 35 km, longer min(0.6 × goal, 50 km).</summary>
    public static double LongRunCeilingMeters(double goalDistanceMeters) =>
        goalDistanceMeters < 15_000 ? 18_000
        : goalDistanceMeters < 30_000 ? 24_000
        : goalDistanceMeters <= 42_200 ? 35_000
        : Math.Min(0.6 * goalDistanceMeters, 50_000);

    /// <summary>The run type codes a plan may use: the training types and rest.</summary>
    public static IReadOnlyList<string> AllowedRunTypeCodes { get; } =
        [.. RunTypeCatalog.All.Where(t => t.Category == RunTypeCategory.Training || t.IsRestDay).OrderBy(t => t.Id).Select(t => t.Code)];
}
