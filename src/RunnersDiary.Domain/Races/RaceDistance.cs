namespace RunnersDiary.Domain.Races;

/// <summary>
/// One listed distance of a <see cref="Race"/>: the row type of the <c>RaceDistances</c> child table (ADR 0036 §1).
/// Consumers read <see cref="Race.DistancesMeters"/>; this type only exists so the set can be persisted as an ordinary one-to-many.
/// </summary>
public sealed class RaceDistance
{
    private RaceDistance()
    {
    }

    internal RaceDistance(Guid raceId, double distanceMeters)
    {
        RaceId = raceId;
        DistanceMeters = distanceMeters;
    }

    public Guid RaceId { get; private set; }

    public double DistanceMeters { get; private set; }
}
