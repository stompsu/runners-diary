using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Tests.Runs;

public sealed class RunCatalogLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private static RunValues Values() => new()
    {
        Date = new DateOnly(2027, 3, 14),
        TimeZoneId = "Europe/Warsaw",
        Status = RunStatus.Planned,
        RunTypeId = 1,
        DistanceMeters = 10_000,
        Race = new RaceDetails("Demo 10K", RaceDistanceCategory.TenK, 10_000),
    };

    [Fact]
    public void New_runs_are_not_linked() => Assert.Null(Run.Create(Guid.NewGuid(), Values(), Now).CatalogRaceId);

    [Fact]
    public void Link_sets_the_catalogue_race_once()
    {
        var run = Run.Create(Guid.NewGuid(), Values(), Now);
        var raceId = Guid.NewGuid();

        run.LinkToCatalogRace(raceId);

        Assert.Equal(raceId, run.CatalogRaceId);
        Assert.Throws<DomainRuleException>(() => run.LinkToCatalogRace(Guid.NewGuid()));
        Assert.Throws<DomainRuleException>(() => Run.Create(Guid.NewGuid(), Values(), Now).LinkToCatalogRace(Guid.Empty));
    }

    [Fact]
    public void Update_keeps_the_link()
    {
        var run = Run.Create(Guid.NewGuid(), Values(), Now);
        var raceId = Guid.NewGuid();
        run.LinkToCatalogRace(raceId);

        run.Update(Values() with { Date = new DateOnly(2027, 3, 21), Title = "Moved" }, Now.AddDays(1));

        Assert.Equal(raceId, run.CatalogRaceId);
        Assert.Equal(new DateOnly(2027, 3, 21), run.Date);
    }

    [Fact]
    public void The_link_has_no_public_setter() =>
        Assert.False(typeof(Run).GetProperty(nameof(Run.CatalogRaceId))!.SetMethod!.IsPublic);
}
