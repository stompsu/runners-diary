namespace RunnersDiary.Domain.Goals;

public enum EffortKind : byte
{
    Race = 0,
    Training = 1,

    /// <summary>M9.1 fallback: no single run; the typical (median) training run of the last 6 weeks, scaled to a race effort (ADR 0039 amendment 2026-11-21).</summary>
    RecentTraining = 2,
}

/// <summary>A completed effort that can serve as the basis of a prediction (a race result, or a hard training run).</summary>
/// <param name="RunId">Null for <see cref="EffortKind.RecentTraining"/> (no single run).</param>
/// <param name="RunCount">1 for a race or a hard training run; the number of runs used for <see cref="EffortKind.RecentTraining"/>.</param>
public sealed record EffortFact(
    Guid? RunId,
    DateOnly Date,
    EffortKind Kind,
    double DistanceMeters,
    int DurationSeconds,
    double? ElevationGainMeters,
    int RunCount = 1);

public enum PredictionConfidence : byte
{
    High = 0,
    Low = 1,
}

/// <summary>A predicted time for the goal, the basis it came from and how far it was extrapolated.</summary>
/// <param name="PaceSecondsPerKm">The predicted time per real kilometre of the goal course (climbing included on a hilly goal).</param>
/// <param name="FlatPaceSecondsPerKm">
/// M9.1: the same prediction for the goal distance with no climbing (elevation 0), per kilometre: the pace the runner's fitness supports on flat ground.
/// Equals <see cref="PaceSecondsPerKm"/> on a flat goal; null only for predictions built without it (then <see cref="FitnessPaceSecondsPerKm"/> falls back).
/// </param>
public sealed record TimePrediction(
    int DurationSeconds, int PaceSecondsPerKm, PredictionConfidence Confidence, EffortFact Basis, int? FlatPaceSecondsPerKm = null)
{
    /// <summary>The flat-basis pace that plans are built from (ADR 0040 amendment): <see cref="FlatPaceSecondsPerKm"/>, else <see cref="PaceSecondsPerKm"/>.</summary>
    public int FitnessPaceSecondsPerKm => FlatPaceSecondsPerKm ?? PaceSecondsPerKm;
}

/// <summary>
/// Riegel prediction over "km-effort" (ADR 0039 §4): <c>E = distance + 10 × elevation gain</c> (100 m of climb ≈ 1 km flat) on both the basis and
/// the goal, <c>T_goal = T_basis × (E_goal / E_basis)^1.06</c>. A hint, never a promise.
/// </summary>
public static class RaceTimePredictor
{
    public const double RiegelExponent = 1.06;
    public const double ElevationFactor = 10;
    public const double MinBasisDistanceMeters = 3_000;
    public const double HighConfidenceMinRatio = 0.5;
    public const double HighConfidenceMaxRatio = 2.0;

    /// <summary>Fallback basis (ADR 0039 amendment 2026-11-21): a race over the typical training distance is taken as 10 % faster than the median training pace.</summary>
    public const double TrainingToRaceFactor = 0.90;

    public const int FallbackMinRuns = 3;
    public const int FallbackLookbackDays = 42;
    public const int MinEffortPaceSecondsPerKm = 150;
    public const int MaxEffortPaceSecondsPerKm = 900;

    public static double EffortDistance(double distanceMeters, double? elevationGainMeters) =>
        distanceMeters + (ElevationFactor * (elevationGainMeters ?? 0));

    /// <summary>
    /// The basis is the race whose effort distance is closest to the goal's (smallest |ln(E_goal / E_basis)|, ties to the most recent, then to
    /// the smaller id); without a usable race, the same rule over the training efforts. Efforts under 3 km or without a time are ignored.
    /// Null when there is no usable effort.
    /// </summary>
    /// <param name="recentTrainingRuns">
    /// Optional fallback input: ordinary completed training runs of the last <see cref="FallbackLookbackDays"/> days (already windowed by the caller).
    /// Used only when no race and no hard training effort is usable; see <see cref="FromRecentTraining"/>.
    /// </param>
    public static TimePrediction? Predict(
        double distanceMeters, double? elevationGainMeters, IReadOnlyList<EffortFact> candidates, IReadOnlyList<EffortFact>? recentTrainingRuns = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (!(distanceMeters > 0))
        {
            return null;
        }

        var usable = candidates.Where(c => c.DistanceMeters >= MinBasisDistanceMeters && c.DurationSeconds > 0).ToList();
        var goalEffort = EffortDistance(distanceMeters, elevationGainMeters);

        var basis = Closest(usable.Where(c => c.Kind == EffortKind.Race), goalEffort)
            ?? Closest(usable.Where(c => c.Kind == EffortKind.Training), goalEffort);
        if (basis is null)
        {
            return recentTrainingRuns is null ? null : FromRecentTraining(distanceMeters, elevationGainMeters, recentTrainingRuns);
        }

        var ratio = goalEffort / EffortDistance(basis.DistanceMeters, basis.ElevationGainMeters);
        var seconds = (int)Math.Round(basis.DurationSeconds * Math.Pow(ratio, RiegelExponent), MidpointRounding.AwayFromZero);
        var pace = (int)Math.Round(seconds / (distanceMeters / 1000), MidpointRounding.AwayFromZero);
        var flatSeconds = basis.DurationSeconds * Math.Pow(distanceMeters / EffortDistance(basis.DistanceMeters, basis.ElevationGainMeters), RiegelExponent);
        var flatPace = (int)Math.Round(flatSeconds / (distanceMeters / 1000), MidpointRounding.AwayFromZero);
        var confidence = basis.Kind == EffortKind.Race && ratio is >= HighConfidenceMinRatio and <= HighConfidenceMaxRatio
            ? PredictionConfidence.High
            : PredictionConfidence.Low;
        return new TimePrediction(seconds, pace, confidence, basis, flatPace);
    }

    /// <summary>
    /// The M9.1 fallback: with at least <see cref="FallbackMinRuns"/> usable runs (3 km or more, with a time, effort pace 150-900 s/km), <c>p</c> is the
    /// median effort pace and <c>E_b</c> the median effort distance; <c>T_b = 0.90 × p × E_b / 1000</c>, then Riegel to the goal. Always Low confidence.
    /// </summary>
    public static TimePrediction? FromRecentTraining(double distanceMeters, double? elevationGainMeters, IReadOnlyList<EffortFact> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        if (!(distanceMeters > 0))
        {
            return null;
        }

        var usable = runs
            .Where(r => r.DistanceMeters >= MinBasisDistanceMeters && r.DurationSeconds > 0)
            .Select(r => (Run: r, Effort: EffortDistance(r.DistanceMeters, r.ElevationGainMeters)))
            .Select(x => (x.Run, x.Effort, Pace: x.Run.DurationSeconds / (x.Effort / 1000)))
            .Where(x => x.Pace is >= MinEffortPaceSecondsPerKm and <= MaxEffortPaceSecondsPerKm)
            .ToList();
        if (usable.Count < FallbackMinRuns)
        {
            return null;
        }

        var medianPace = Median(usable.Select(x => x.Pace));
        var medianEffort = Median(usable.Select(x => x.Effort));
        var baseSeconds = TrainingToRaceFactor * medianPace * medianEffort / 1000;
        var goalEffort = EffortDistance(distanceMeters, elevationGainMeters);
        var seconds = (int)Math.Round(baseSeconds * Math.Pow(goalEffort / medianEffort, RiegelExponent), MidpointRounding.AwayFromZero);
        var pace = (int)Math.Round(seconds / (distanceMeters / 1000), MidpointRounding.AwayFromZero);
        var basis = new EffortFact(
            null,
            usable.Max(x => x.Run.Date),
            EffortKind.RecentTraining,
            Median(usable.Select(x => x.Run.DistanceMeters)),
            (int)Math.Round(baseSeconds, MidpointRounding.AwayFromZero),
            Median(usable.Select(x => x.Run.ElevationGainMeters ?? 0)),
            usable.Count);
        var flatSeconds = baseSeconds * Math.Pow(distanceMeters / medianEffort, RiegelExponent);
        var flatPace = (int)Math.Round(flatSeconds / (distanceMeters / 1000), MidpointRounding.AwayFromZero);
        return new TimePrediction(seconds, pace, PredictionConfidence.Low, basis, flatPace);
    }

    /// <summary>The median; an even count takes the mean of the two middle values.</summary>
    internal static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static EffortFact? Closest(IEnumerable<EffortFact> efforts, double goalEffort) =>
        efforts
            .OrderBy(e => Math.Abs(Math.Log(goalEffort / EffortDistance(e.DistanceMeters, e.ElevationGainMeters))))
            .ThenByDescending(e => e.Date)
            .ThenBy(e => e.RunId)
            .FirstOrDefault();
}
