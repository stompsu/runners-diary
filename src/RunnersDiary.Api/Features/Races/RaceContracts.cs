using System.ComponentModel.DataAnnotations;
using RunnersDiary.Api.Features.Runs;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Api.Features.Races;

/// <summary>The caller's race interests (ADR 0037 §1). Distances and radius are SI (metres); the UI edits km.</summary>
public sealed record RaceInterestsResponse
{
    /// <summary>False when the caller has not saved interests yet: the other members are then the defaults and proposals are off.</summary>
    public required bool Configured { get; init; }

    public required IReadOnlyList<RaceKind> Kinds { get; init; }

    /// <summary>ISO 3166-2:PL codes such as <c>PL-24</c>; empty means no region filter.</summary>
    public required IReadOnlyList<string> RegionCodes { get; init; }

    public required LocationDto? Center { get; init; }

    public required int? RadiusMeters { get; init; }

    public required double? MinDistanceMeters { get; init; }

    public required double? MaxDistanceMeters { get; init; }

    public required int HorizonDays { get; init; }

    public required string? Keywords { get; init; }

    public required bool ShowInCalendar { get; init; }

    /// <summary>When the interests were last saved (UTC); null while not configured.</summary>
    public required DateTime? UpdatedAt { get; init; }
}

/// <summary>Body of <c>PUT /api/races/interests</c> (full replace). Never carries a user id; unknown members are rejected.</summary>
public sealed record UpdateRaceInterestsRequest : IValidatableObject
{
    public required IReadOnlyList<RaceKind> Kinds { get; init; }

    public required IReadOnlyList<string> RegionCodes { get; init; }

    public LocationDto? Center { get; init; }

    public int? RadiusMeters { get; init; }

    public double? MinDistanceMeters { get; init; }

    public double? MaxDistanceMeters { get; init; }

    public required int HorizonDays { get; init; }

    public string? Keywords { get; init; }

    public required bool ShowInCalendar { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var centerValid = Center is null || GeoPoint.IsValid(Center.Latitude, Center.Longitude);
        if (!centerValid)
        {
            yield return new ValidationResult("Latitude must be in -90..90 and longitude in -180..180.", ["center"]);
        }

        foreach (var error in RaceInterestRules.Validate(ToValues(centerValid)))
        {
            // The domain rule names the flattened member; the contract keys it by its JSON path.
            yield return new ValidationResult(error.Message, [error.Path == "centerName" ? "center.name" : error.Path]);
        }

        if (RegionCodes is { Count: > 0 } && RegionCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != RegionCodes.Count)
        {
            yield return new ValidationResult("Region codes must not repeat.", ["regionCodes"]);
        }
    }

    internal RaceInterestValues ToValues(bool includeCenter = true) => new()
    {
        Kinds = Kinds ?? [],
        RegionCodes = RegionCodes ?? [],
        Center = includeCenter && Center is { } c ? new GeoPoint(c.Latitude, c.Longitude) : null,
        CenterName = Center?.Name,
        RadiusMeters = RadiusMeters,
        MinDistanceMeters = MinDistanceMeters,
        MaxDistanceMeters = MaxDistanceMeters,
        HorizonDays = HorizonDays,
        Keywords = Keywords,
        ShowInCalendar = ShowInCalendar,
    };
}

public sealed record RaceLocationDto
{
    public required double Latitude { get; init; }

    public required double Longitude { get; init; }
}

public sealed record RacePriceDto
{
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }
}

/// <summary>One proposal (a catalogue race that matches the caller's interests) as the diary and the list show it.</summary>
public sealed record RaceProposalDto
{
    public required Guid RaceId { get; init; }

    public required string Name { get; init; }

    public required DateOnly Date { get; init; }

    public required TimeOnly? StartTime { get; init; }

    public required string? LocationName { get; init; }

    public required string? RegionCode { get; init; }

    public required string CountryCode { get; init; }

    public required RaceLocationDto? Location { get; init; }

    public required RaceKind Kind { get; init; }

    /// <summary>Ascending, in metres.</summary>
    public required IReadOnlyList<double> DistancesMeters { get; init; }

    /// <summary><c>SignedUp</c> is derived from the caller's linked run and wins over a stored mark.</summary>
    public required ProposalState State { get; init; }

    public required Guid? LinkedRunId { get; init; }

    /// <summary>Imported after the caller last opened the list.</summary>
    public required bool IsNew { get; init; }

    /// <summary>The linked run's date differs from the race date, or an Interested mark was made on another date.</summary>
    public required bool DateChanged { get; init; }

    /// <summary>Source id, see <c>client-config.races.sources</c>.</summary>
    public required string Source { get; init; }
}

public sealed record RaceProposalListResponse
{
    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    /// <summary>The requested window clamped to [today, today + horizon] in the caller's zone; <c>EffectiveTo &lt; EffectiveFrom</c> means empty.</summary>
    public required DateOnly EffectiveFrom { get; init; }

    public required DateOnly EffectiveTo { get; init; }

    public required bool ProfileConfigured { get; init; }

    /// <summary>Latest time any catalogue race was seen by an import (UTC); null while the catalogue is empty.</summary>
    public required DateTime? CatalogueUpdatedUtc { get; init; }

    /// <summary>The window held more catalogue rows than <c>Races:Proposals:MaxCatalogueRows</c>; the list is cut at the cap.</summary>
    public required bool Truncated { get; init; }

    public required IReadOnlyList<RaceProposalDto> Items { get; init; }
}

/// <summary>A catalogue race with everything the details view shows (all <see cref="RaceProposalDto"/> members plus the extras).</summary>
public sealed record RaceDetailsDto
{
    public required Guid RaceId { get; init; }

    public required string Name { get; init; }

    public required DateOnly Date { get; init; }

    public required TimeOnly? StartTime { get; init; }

    public required string? LocationName { get; init; }

    public required string? RegionCode { get; init; }

    public required string CountryCode { get; init; }

    public required RaceLocationDto? Location { get; init; }

    public required RaceKind Kind { get; init; }

    public required IReadOnlyList<double> DistancesMeters { get; init; }

    public required ProposalState State { get; init; }

    public required Guid? LinkedRunId { get; init; }

    public required bool IsNew { get; init; }

    public required bool DateChanged { get; init; }

    public required string Source { get; init; }

    public required string? TimeZoneId { get; init; }

    public required Surface? Surface { get; init; }

    public required double? ElevationGainMeters { get; init; }

    public required RacePriceDto? Price { get; init; }

    public required DateOnly? SignupDeadline { get; init; }

    public required string? OrganizerName { get; init; }

    /// <summary>Absolute http(s) URLs as the source published them; the SPA renders them through <c>safeUrl</c>.</summary>
    public required string? SignupUrl { get; init; }

    public required string? EventUrl { get; init; }

    public required string? SourceUrl { get; init; }

    /// <summary>The source no longer lists the race (check with the organiser).</summary>
    public required bool Removed { get; init; }

    public required DateOnly? PreviousDate { get; init; }

    /// <summary>Set when this id is a duplicate of another race; the SPA navigates to the canonical one.</summary>
    public required Guid? CanonicalId { get; init; }

    public required DateTime LastSeenUtc { get; init; }

    public required DateTime UpdatedUtc { get; init; }
}

/// <summary>The marks a caller may set; <c>SignedUp</c> is derived and therefore not accepted (a JSON 400 on <c>state</c>).</summary>
public enum RaceStateChoice
{
    None = 0,
    Interested = 1,
    Dismissed = 2,
}

public sealed record SetRaceProposalStateRequest
{
    public required RaceStateChoice State { get; init; }
}

public sealed record RaceUserStateDto
{
    public required Guid RaceId { get; init; }

    public required ProposalState State { get; init; }

    public required Guid? LinkedRunId { get; init; }

    public required bool DateChanged { get; init; }
}

/// <summary>Body of <c>POST /api/races/{raceId}/signup</c>; every member is optional (the body itself may be empty).</summary>
public sealed record SignUpForRaceRequest
{
    /// <summary>Must equal one of the race's distances; required when the race lists more than one.</summary>
    public double? DistanceMeters { get; init; }

    /// <summary>Local start, <c>HH:mm</c>; defaults to the race's start time.</summary>
    public TimeOnly? StartTime { get; init; }

    /// <summary>A race-category run type; defaults from the race kind.</summary>
    public short? RunTypeId { get; init; }

    [Range(1, 604_800)]
    public int? TargetTimeSeconds { get; init; }
}

internal static class RaceMapping
{
    public static RaceProposalDto ToDto(RaceProposal p) => new()
    {
        RaceId = p.Race.Id,
        Name = p.Race.Name,
        Date = p.Race.Date,
        StartTime = p.Race.StartTime,
        LocationName = p.Race.LocationName,
        RegionCode = p.Race.RegionCode,
        CountryCode = p.Race.CountryCode,
        Location = Location(p.Race.Point),
        Kind = p.Race.Kind,
        DistancesMeters = p.Race.DistancesMeters,
        State = p.State,
        LinkedRunId = p.LinkedRunId,
        IsNew = p.IsNew,
        DateChanged = p.DateChanged,
        Source = Infrastructure.Races.RaceSourceRegistry.IdOf(p.Race.Source),
    };

    public static RaceDetailsDto ToDto(Infrastructure.Races.RaceDetails d)
    {
        var r = d.Race;
        return new RaceDetailsDto
        {
            RaceId = r.Id,
            Name = r.Name,
            Date = r.Date,
            StartTime = r.StartTime,
            LocationName = r.LocationName,
            RegionCode = r.RegionCode,
            CountryCode = r.CountryCode,
            Location = Location(r.Point),
            Kind = r.Kind,
            DistancesMeters = r.DistancesMeters,
            State = d.User.State,
            LinkedRunId = d.User.LinkedRunId,
            IsNew = d.IsNew,
            DateChanged = d.User.DateChanged,
            Source = Infrastructure.Races.RaceSourceRegistry.IdOf(r.Source),
            TimeZoneId = r.TimeZoneId,
            Surface = r.Surface,
            ElevationGainMeters = r.ElevationGainMeters,
            Price = r.PriceAmount is { } amount && r.PriceCurrency is { } currency ? new RacePriceDto { Amount = amount, Currency = currency } : null,
            SignupDeadline = r.SignupDeadline,
            OrganizerName = r.OrganizerName,
            SignupUrl = r.SignupUrl,
            EventUrl = r.EventUrl,
            SourceUrl = r.SourceUrl,
            Removed = r.IsRemoved,
            PreviousDate = r.PreviousDate,
            CanonicalId = r.DuplicateOfRaceId,
            LastSeenUtc = r.LastSeenUtc,
            UpdatedUtc = r.UpdatedUtc,
        };
    }

    public static RaceInterestsResponse ToResponse(RaceInterestValues v, bool configured, DateTime? updatedAt) => new()
    {
        Configured = configured,
        Kinds = v.Kinds,
        RegionCodes = v.RegionCodes,
        Center = v.Center is { } c ? new LocationDto { Latitude = c.Latitude, Longitude = c.Longitude, Name = v.CenterName } : null,
        RadiusMeters = v.RadiusMeters,
        MinDistanceMeters = v.MinDistanceMeters,
        MaxDistanceMeters = v.MaxDistanceMeters,
        HorizonDays = v.HorizonDays,
        Keywords = v.Keywords,
        ShowInCalendar = v.ShowInCalendar,
        UpdatedAt = updatedAt,
    };

    public static Dictionary<string, string[]> ToErrors(IEnumerable<DomainError> errors)
    {
        var keyed = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var group in errors.GroupBy(e => e.Path == "race.targetTimeSeconds" ? "targetTimeSeconds" : e.Path))
        {
            keyed[group.Key] = [.. group.Select(e => e.Message)];
        }

        return keyed;
    }

    private static RaceLocationDto? Location(GeoPoint? point) =>
        point is { } p ? new RaceLocationDto { Latitude = p.Latitude, Longitude = p.Longitude } : null;
}
