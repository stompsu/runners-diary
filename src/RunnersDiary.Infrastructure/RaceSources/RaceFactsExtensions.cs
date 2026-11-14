using RunnersDiary.Domain.Races;

namespace RunnersDiary.Infrastructure.RaceSources;

internal static class RaceFactsExtensions
{
    /// <summary>The stored facts of a catalogue row, as an importer reports them (what a 304 or a spent detail budget keeps).</summary>
    public static RaceFacts ToFacts(this Race race)
    {
        ArgumentNullException.ThrowIfNull(race);
        return new RaceFacts(
            race.Source, race.ExternalId, race.Name, race.Date, race.StartTime, race.LocationName, race.Point, race.CountryCode, race.RegionCode,
            race.Kind, race.Surface, race.DistancesMeters, race.ElevationGainMeters, race.SignupUrl, race.EventUrl, race.SourceUrl,
            race.PriceAmount, race.PriceCurrency, race.SignupDeadline, race.OrganizerName);
    }
}
