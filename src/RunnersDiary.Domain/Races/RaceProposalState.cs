using RunnersDiary.Domain.Common;

namespace RunnersDiary.Domain.Races;

/// <summary>A user's mark on a catalogue race (ADR 0037 §3): one row per user and race, Owner-filtered. Setting "none" deletes the row.</summary>
public sealed class RaceProposalState : IUserOwned
{
    private RaceProposalState()
    {
    }

    public Guid UserId { get; private set; }

    public Guid RaceId { get; private set; }

    public RaceProposalMark State { get; private set; }

    /// <summary>The race date when the user marked it; a difference to the current date is the Interested "date changed" signal.</summary>
    public DateOnly RaceDateWhenMarked { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static RaceProposalState Create(Guid userId, Guid raceId, RaceProposalMark state, DateOnly raceDate, DateTimeOffset now)
    {
        if (userId == Guid.Empty || raceId == Guid.Empty)
        {
            throw new DomainRuleException("A proposal state needs an owner and a race.");
        }

        var row = new RaceProposalState { UserId = userId, RaceId = raceId };
        row.Set(state, raceDate, now);
        return row;
    }

    /// <summary>Changes the mark and re-records the race date (re-sending Interested acknowledges a date change).</summary>
    public void Set(RaceProposalMark state, DateOnly raceDate, DateTimeOffset now)
    {
        if (!Enum.IsDefined(state))
        {
            throw new DomainRuleException([new DomainError("state", "Must be Interested or Dismissed.")]);
        }

        State = state;
        RaceDateWhenMarked = raceDate;
        UpdatedAt = TextFolding.TruncateUtc(now);
    }
}
