using RunnersDiary.Domain.Gpx;

namespace RunnersDiary.Domain.Races;

/// <summary>Filtering, state and ordering of proposals over one projection (ADR 0037 §2 and §3). Pure; no I/O.</summary>
public static class RaceProposalBuilder
{
    public const double UltraThresholdMeters = RaceNormalizer.MarathonMeters;

    /// <param name="profile">The caller's interests.</param>
    /// <param name="today">Today in the caller's zone.</param>
    /// <param name="from">Requested window start (inclusive).</param>
    /// <param name="to">Requested window end (inclusive).</param>
    /// <param name="races">Candidate catalogue rows (the SQL step already selected the window).</param>
    /// <param name="states">The caller's stored marks by race id.</param>
    /// <param name="linkedRuns">The caller's runs created from a race, by race id.</param>
    /// <param name="seenUtc">Normally <c>profile.ProposalsSeenUtc</c>; null means never opened, so everything is new.</param>
    /// <param name="includeDismissed">Include races the caller dismissed.</param>
    public static IReadOnlyList<RaceProposal> Build(
        RaceInterestProfile profile,
        DateOnly today,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<RaceSnapshot> races,
        IReadOnlyDictionary<Guid, RaceProposalState> states,
        IReadOnlyDictionary<Guid, LinkedRun> linkedRuns,
        DateTime? seenUtc,
        bool includeDismissed)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(races);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(linkedRuns);

        var effectiveFrom = from > today ? from : today;
        var horizonEnd = today.AddDays(profile.HorizonDays);
        var effectiveTo = to < horizonEnd ? to : horizonEnd;
        var terms = RaceInterestRules.KeywordTerms(profile.Keywords).Select(t => TextFolding.Fold(t)).Where(t => t.Length > 0).ToList();

        var ranked = new List<(RaceProposal Proposal, int Rank, double Proximity)>();
        foreach (var race in races)
        {
            if (race.DuplicateOfRaceId is not null || race.Date < from || race.Date > to)
            {
                continue;
            }

            var linked = linkedRuns.GetValueOrDefault(race.Id);
            states.TryGetValue(race.Id, out var stored);

            ProposalState state;
            if (linked is not null)
            {
                state = ProposalState.SignedUp;
            }
            else
            {
                if (race.RemovedUtc is not null || race.Date < effectiveFrom || race.Date > effectiveTo)
                {
                    continue;
                }

                if (!Matches(profile, race, terms))
                {
                    continue;
                }

                state = stored?.State switch
                {
                    RaceProposalMark.Interested => ProposalState.Interested,
                    RaceProposalMark.Dismissed => ProposalState.Dismissed,
                    _ => ProposalState.None,
                };
                if (state == ProposalState.Dismissed && !includeDismissed)
                {
                    continue;
                }
            }

            var dateChanged = linked is not null
                ? linked.Date != race.Date
                : state == ProposalState.Interested && stored is not null && stored.RaceDateWhenMarked != race.Date;
            var isNew = seenUtc is null || race.ImportedUtc > seenUtc;
            var rank = state switch { ProposalState.SignedUp => 0, ProposalState.Interested => 1, _ => 2 };
            ranked.Add((new RaceProposal(race, state, linked?.RunId, isNew, dateChanged), rank, Proximity(profile, race)));
        }

        return ranked
            .OrderBy(r => r.Proposal.Race.Date)
            .ThenBy(r => r.Rank)
            .ThenBy(r => r.Proximity)
            .ThenBy(r => r.Proposal.Race.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Proposal.Race.Id)
            .Select(r => r.Proposal)
            .ToList();
    }

    /// <summary>The profile's own filters (kind, location, distance, keywords); window, state and removal are handled by <see cref="Build"/>.</summary>
    public static bool Matches(RaceInterestProfile profile, RaceSnapshot race, IReadOnlyList<string> foldedTerms)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(race);
        ArgumentNullException.ThrowIfNull(foldedTerms);
        return KindMatches(profile, race) && LocationMatches(profile, race) && DistanceMatches(profile, race) && KeywordsMatch(race, foldedTerms);
    }

    private static bool KindMatches(RaceInterestProfile profile, RaceSnapshot race) =>
        profile.Kinds.Contains(race.Kind)
        || (profile.Kinds.Contains(RaceKind.Ultra) && race.DistancesMeters.Any(d => d > UltraThresholdMeters));

    private static bool LocationMatches(RaceInterestProfile profile, RaceSnapshot race)
    {
        var hasCenter = profile.Center is not null && profile.RadiusMeters is not null;
        if (profile.RegionCodes.Count == 0 && !hasCenter)
        {
            return true;
        }

        if (race.RegionCode is { } region && profile.RegionCodes.Contains(region, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return hasCenter && race.Point is { } point && DistanceToCenter(profile, point) <= profile.RadiusMeters;
    }

    private static bool DistanceMatches(RaceInterestProfile profile, RaceSnapshot race)
    {
        if (profile.MinDistanceMeters is null && profile.MaxDistanceMeters is null)
        {
            return true;
        }

        var min = profile.MinDistanceMeters ?? double.NegativeInfinity;
        var max = profile.MaxDistanceMeters ?? double.PositiveInfinity;
        return race.DistancesMeters.Any(d => d >= min && d <= max);
    }

    private static bool KeywordsMatch(RaceSnapshot race, IReadOnlyList<string> foldedTerms)
    {
        if (foldedTerms.Count == 0)
        {
            return true;
        }

        var haystack = TextFolding.Fold($"{race.Name} {race.LocationName}");
        return foldedTerms.All(t => haystack.Contains(t, StringComparison.Ordinal));
    }

    private static double DistanceToCenter(RaceInterestProfile profile, Runs.GeoPoint point) =>
        Haversine.Distance(profile.Center!.Value.Latitude, profile.Center.Value.Longitude, point.Latitude, point.Longitude);

    // Nearest first when a centre is set; races without coordinates sort after those with. Without a centre every race ties at 0.
    private static double Proximity(RaceInterestProfile profile, RaceSnapshot race)
    {
        if (profile.Center is null)
        {
            return 0;
        }

        return race.Point is { } p ? DistanceToCenter(profile, p) : double.MaxValue;
    }
}
