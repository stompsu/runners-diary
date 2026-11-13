using RunnersDiary.Domain.Races;
using RunnersDiary.Infrastructure.Races;

namespace RunnersDiary.Infrastructure.RaceSources.Demo;

/// <summary>
/// The synthetic <see cref="RaceSource.Demo"/> source: <c>seed/races/demo-races.json</c> expanded for "today". No network, always a complete scan.
/// For local development, tests and CI only; the Production configuration validator rejects its switch (ADR 0035 §1).
/// </summary>
internal sealed class DemoRaceSource(DemoRaceCatalogue catalogue) : IRaceSource
{
    public RaceSource Source => RaceSource.Demo;

    public string Id => RaceSourceRegistry.DemoId;

    public Task<RaceScanResult> ScanAsync(RaceScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var facts = catalogue.Validate(context.Today);
        return Task.FromResult(new RaceScanResult(facts, Complete: true, RaceScanOutcome.Completed, RaceFetchStats.Empty, []));
    }
}
