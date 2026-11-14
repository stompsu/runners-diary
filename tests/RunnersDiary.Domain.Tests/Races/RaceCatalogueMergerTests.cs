using RunnersDiary.Domain.Races;
using static RunnersDiary.Domain.Tests.Races.RaceTestData;

namespace RunnersDiary.Domain.Tests.Races;

public sealed class RaceCatalogueMergerTests
{
    private static readonly DateOnly Today = new(2026, 11, 13);

    private static RaceFacts Future(string id, Func<RaceFacts, RaceFacts>? change = null) =>
        Facts(f => (change is null ? f : change(f)) with { ExternalId = id, Date = new DateOnly(2027, 3, 14) });

    private static RaceFacts OnDate(string id, DateOnly date) => Facts(f => f with { ExternalId = id, Date = date });

    private static MergePlan Plan(IEnumerable<Race> existing, IEnumerable<RaceFacts> scanned, bool complete = true, int threshold = 2) =>
        RaceCatalogueMerger.Plan([.. existing.Select(r => r.ToIndexEntry())], [.. scanned], complete, Today, Now, threshold);

    [Fact]
    public void New_race_is_inserted()
    {
        var plan = Plan([], [Future("a")]);

        Assert.Equal("a", Assert.Single(plan.Inserts).ExternalId);
        Assert.Empty(plan.Updates);
        Assert.Empty(plan.Seen);
        Assert.Empty(plan.Missed);
    }

    [Fact]
    public void Same_hash_is_only_seen()
    {
        var race = Race.Create(Future("a"), Now);

        var plan = Plan([race], [Future("a")]);

        Assert.Equal(race.Id, Assert.Single(plan.Seen));
        Assert.Empty(plan.Inserts);
        Assert.Empty(plan.Updates);
        Assert.Empty(plan.DateChanges);
        Assert.Empty(plan.Restores);
    }

    [Fact]
    public void Raw_facts_are_normalised_before_the_hash_is_compared()
    {
        var race = Race.Create(Future("a"), Now);

        var plan = Plan([race], [Future("a", f => f with { Name = "  Demo   Gliwice Night 10K  ", DistancesMeters = [10_000, 10_000] })]);

        Assert.Equal(race.Id, Assert.Single(plan.Seen));
        Assert.Empty(plan.Updates);
    }

    [Fact]
    public void Changed_hash_is_an_update_without_previous_date()
    {
        var race = Race.Create(Future("a"), Now);

        var plan = Plan([race], [Future("a", f => f with { PriceAmount = 90m })]);

        var update = Assert.Single(plan.Updates);
        Assert.Equal(race.Id, update.Id);
        Assert.Equal(90m, update.Facts.PriceAmount);
        Assert.Null(update.PreviousDate);
        Assert.Empty(plan.DateChanges);
        Assert.Empty(plan.Seen);
    }

    [Fact]
    public void Changed_date_records_the_previous_date()
    {
        var race = Race.Create(Future("a"), Now);
        var moved = Future("a") with { Date = new DateOnly(2027, 3, 21) };

        var plan = Plan([race], [moved]);

        var update = Assert.Single(plan.Updates);
        Assert.Equal(new DateOnly(2027, 3, 14), update.PreviousDate);
        var change = Assert.Single(plan.DateChanges);
        Assert.Equal(new RaceDateChange(race.Id, new DateOnly(2027, 3, 14), new DateOnly(2027, 3, 21)), change);
    }

    [Fact]
    public void Reappearing_removed_race_is_restored()
    {
        var race = Race.Create(Future("a"), Now);
        race.MarkMissed(Now, 1);

        var plan = Plan([race], [Future("a")]);

        Assert.Equal(race.Id, Assert.Single(plan.Restores));
        Assert.Equal(race.Id, Assert.Single(plan.Seen));
    }

    [Fact]
    public void Reappearing_removed_race_with_changed_facts_is_restored_and_updated()
    {
        var race = Race.Create(Future("a"), Now);
        race.MarkMissed(Now, 1);

        var plan = Plan([race], [Future("a", f => f with { Name = "Renamed" })]);

        Assert.Equal(race.Id, Assert.Single(plan.Restores));
        Assert.Equal(race.Id, Assert.Single(plan.Updates).Id);
    }

    [Fact]
    public void Absent_future_race_is_missed_once_then_removed_at_two()
    {
        var race = Race.Create(Future("a"), Now);

        var first = Plan([race], [Future("b")]);
        var miss1 = Assert.Single(first.Missed);
        Assert.Equal(new RaceMissed(race.Id, 1, false), miss1);
        Assert.Empty(first.Removals);

        race.MarkMissed(Now, 2);
        var second = Plan([race], [Future("b")]);
        Assert.Equal(new RaceMissed(race.Id, 2, true), Assert.Single(second.Missed));
        Assert.Equal(race.Id, Assert.Single(second.Removals));
    }

    [Fact]
    public void Full_cycle_zero_one_two_removed_then_restored()
    {
        var race = Race.Create(Future("a"), Now);
        var other = Future("b");

        // Scan 1 and 2 without "a": apply the plan like the executor does.
        for (var scan = 1; scan <= 2; scan++)
        {
            var plan = Plan([race], [other]);
            foreach (var m in plan.Missed)
            {
                race.MarkMissed(Now.AddDays(7 * scan), 2);
                Assert.Equal(m.MissedScans, race.MissedScans);
                Assert.Equal(m.Removed, race.IsRemoved);
            }
        }

        Assert.Equal(2, race.MissedScans);
        Assert.True(race.IsRemoved);

        // Scan 3 lists it again.
        var back = Plan([race], [Future("a")]);
        Assert.Equal(race.Id, Assert.Single(back.Restores));
        race.MarkSeen(Now.AddDays(21));
        Assert.Equal(0, race.MissedScans);
        Assert.False(race.IsRemoved);
        Assert.Empty(Plan([race], [Future("a")]).Restores);
    }

    [Fact]
    public void Removed_race_is_not_counted_again()
    {
        var race = Race.Create(Future("a"), Now);
        race.MarkMissed(Now, 1);

        var plan = Plan([race], [Future("b")]);

        Assert.Empty(plan.Missed);
        Assert.Empty(plan.Removals);
    }

    [Fact]
    public void Past_and_todays_races_are_never_counted_as_missed()
    {
        var yesterday = Race.Create(OnDate("y", Today.AddDays(-1)), Now);
        var today = Race.Create(OnDate("t", Today), Now);
        var tomorrow = Race.Create(OnDate("m", Today.AddDays(1)), Now);

        var plan = Plan([yesterday, today, tomorrow], [Future("other")]);

        Assert.Equal(tomorrow.Id, Assert.Single(plan.Missed).Id);
    }

    [Fact]
    public void Incomplete_scan_changes_no_counters()
    {
        var race = Race.Create(Future("a"), Now);
        race.MarkMissed(Now, 5);

        var plan = Plan([race], [Future("b")], complete: false);

        Assert.Empty(plan.Missed);
        Assert.Empty(plan.Removals);
        Assert.Equal("b", Assert.Single(plan.Inserts).ExternalId);
    }

    [Fact]
    public void Races_of_a_source_that_was_not_scanned_are_left_alone()
    {
        var demo = Race.Create(Future("a"), Now);
        var scanned = Future("x", f => f with { Source = RaceSource.RunningLife, SourceUrl = "https://example.org/x" });

        var plan = Plan([demo], [scanned]);

        Assert.Empty(plan.Missed);
        Assert.Single(plan.Inserts);
    }

    [Fact]
    public void Same_external_id_in_another_source_is_a_different_race()
    {
        var demo = Race.Create(Future("a"), Now);
        var real = Future("a", f => f with { Source = RaceSource.RunningLife, SourceUrl = "https://example.org/a" });

        var plan = Plan([demo], [real]);

        Assert.Single(plan.Inserts);
        Assert.Empty(plan.Seen);
        Assert.Empty(plan.Missed);
    }

    [Fact]
    public void Empty_scan_has_no_source_to_judge_and_changes_nothing()
    {
        var race = Race.Create(Future("a"), Now);

        var plan = Plan([race], []);

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void First_occurrence_of_a_key_in_one_scan_wins()
    {
        var plan = Plan([], [Future("a", f => f with { Name = "First" }), Future("a", f => f with { Name = "Second" })]);

        Assert.Equal("First", Assert.Single(plan.Inserts).Name);
    }

    [Fact]
    public void Threshold_of_one_removes_at_the_first_miss()
    {
        var race = Race.Create(Future("a"), Now);

        var plan = Plan([race], [Future("b")], threshold: 1);

        Assert.Equal(new RaceMissed(race.Id, 1, true), Assert.Single(plan.Missed));
        Assert.Equal(race.Id, Assert.Single(plan.Removals));
    }

    [Fact]
    public void Threshold_below_one_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan([], [], threshold: 0));
}
