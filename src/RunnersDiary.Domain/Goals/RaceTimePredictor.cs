namespace RunnersDiary.Domain.Goals;

public enum EffortKind : byte
{
    Race = 0,
    Training = 1,
}

/// <summary>A completed effort that can serve as the basis of a prediction (a race result, or a hard training run).</summary>
public sealed record EffortFact(
    Guid RunId,
    DateOnly Date,
    EffortKind Kind,
    double DistanceMeters,
    int DurationSeconds,
    double? ElevationGainMeters);

public enum PredictionConfidence : byte
{
    High = 0,
    Low = 1,
}

/// <summary>A predicted time for the goal, the basis it came from and how far it was extrapolated.</summary>
public sealed record TimePrediction(int DurationSeconds, int PaceSecondsPerKm, PredictionConfidence Confidence, EffortFact Basis);

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

    public static double EffortDistance(double distanceMeters, double? elevationGainMeters) =>
        distanceMeters + (ElevationFactor * (elevationGainMeters ?? 0));

    /// <summary>
    /// The basis is the race whose effort distance is closest to the goal's (smallest |ln(E_goal / E_basis)|, ties to the most recent, then to
    /// the smaller id); without a usable race, the same rule over the training efforts. Efforts under 3 km or without a time are ignored.
    /// Null when there is no usable effort.
    /// </summary>
    public static TimePrediction? Predict(double distanceMeters, double? elevationGainMeters, IReadOnlyList<EffortFact> candidates)
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
            return null;
        }

        var ratio = goalEffort / EffortDistance(basis.DistanceMeters, basis.ElevationGainMeters);
        var seconds = (int)Math.Round(basis.DurationSeconds * Math.Pow(ratio, RiegelExponent), MidpointRounding.AwayFromZero);
        var pace = (int)Math.Round(seconds / (distanceMeters / 1000), MidpointRounding.AwayFromZero);
        var confidence = basis.Kind == EffortKind.Race && ratio is >= HighConfidenceMinRatio and <= HighConfidenceMaxRatio
            ? PredictionConfidence.High
            : PredictionConfidence.Low;
        return new TimePrediction(seconds, pace, confidence, basis);
    }

    private static EffortFact? Closest(IEnumerable<EffortFact> efforts, double goalEffort) =>
        efforts
            .OrderBy(e => Math.Abs(Math.Log(goalEffort / EffortDistance(e.DistanceMeters, e.ElevationGainMeters))))
            .ThenByDescending(e => e.Date)
            .ThenBy(e => e.RunId)
            .FirstOrDefault();
}
