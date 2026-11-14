using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Races;

/// <summary>A user's race interests (ADR 0037 §1): one row per user, Owner-filtered. No row means proposals are off for that user.</summary>
public sealed class RaceInterestProfile : IUserOwned
{
    private readonly List<string> _regionCodes = [];

    private RaceInterestProfile()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>Bit set of <see cref="RaceKind"/>.</summary>
    public RaceKinds Kinds { get; private set; }

    /// <summary>Distinct ISO 3166-2 codes in catalogue order; empty means no region filter.</summary>
    public IReadOnlyList<string> RegionCodes => _regionCodes;

    public GeoPoint? Center { get; private set; }

    public string? CenterName { get; private set; }

    public int? RadiusMeters { get; private set; }

    public double? MinDistanceMeters { get; private set; }

    public double? MaxDistanceMeters { get; private set; }

    public int HorizonDays { get; private set; }

    public string? Keywords { get; private set; }

    public bool ShowInCalendar { get; private set; }

    /// <summary>"New since last visit" marker: a proposal is new when it was imported after this instant (ADR 0037 §3).</summary>
    public DateTime? ProposalsSeenUtc { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static RaceInterestProfile Create(Guid userId, RaceInterestValues values, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainRuleException("A race interest profile needs an owner.");
        }

        var profile = new RaceInterestProfile { UserId = userId };
        profile.Update(values, now);
        return profile;
    }

    /// <summary>Full replace (PUT semantics).</summary>
    public void Update(RaceInterestValues values, DateTimeOffset now)
    {
        RaceInterestRules.EnsureValid(values);
        Kinds = values.Kinds.ToSet();
        _regionCodes.Clear();
        var wanted = values.RegionCodes.Select(c => PolishRegions.Find(c)!.Code).ToHashSet(StringComparer.Ordinal);
        _regionCodes.AddRange(PolishRegions.All.Select(r => r.Code).Where(wanted.Contains));
        Center = values.Center;
        CenterName = string.IsNullOrWhiteSpace(values.CenterName) || values.Center is null ? null : values.CenterName.Trim();
        RadiusMeters = values.RadiusMeters;
        MinDistanceMeters = values.MinDistanceMeters;
        MaxDistanceMeters = values.MaxDistanceMeters;
        HorizonDays = values.HorizonDays;
        Keywords = RaceInterestRules.NormalizeKeywords(values.Keywords);
        ShowInCalendar = values.ShowInCalendar;
        UpdatedAt = TextFolding.TruncateUtc(now);
    }

    public void MarkProposalsSeen(DateTimeOffset now) => ProposalsSeenUtc = TextFolding.TruncateUtc(now);

    public RaceInterestValues ToValues() => new()
    {
        Kinds = Kinds.ToKinds(),
        RegionCodes = _regionCodes.ToArray(),
        Center = Center,
        CenterName = CenterName,
        RadiusMeters = RadiusMeters,
        MinDistanceMeters = MinDistanceMeters,
        MaxDistanceMeters = MaxDistanceMeters,
        HorizonDays = HorizonDays,
        Keywords = Keywords,
        ShowInCalendar = ShowInCalendar,
    };
}
