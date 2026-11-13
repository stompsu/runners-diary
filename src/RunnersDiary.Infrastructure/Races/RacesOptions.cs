namespace RunnersDiary.Infrastructure.Races;

/// <summary>Race proposal settings (<c>Races</c> section, M8 plan §4.5).</summary>
public sealed class RacesOptions
{
    public const string SectionName = "Races";

    /// <summary>Master switch of the feature (surfaces as <c>client-config.races.enabled</c>); off by default, on in the AppHost and tests.</summary>
    public bool Enabled { get; set; }

    public DemoCatalogueOptions DemoCatalogue { get; set; } = new();

    public ProposalOptions Proposals { get; set; } = new();

    public sealed class DemoCatalogueOptions
    {
        /// <summary>Upserts <c>seed/races/demo-races.json</c> (source Demo) at startup. Development and Testing only: the Production configuration validator rejects it.</summary>
        public bool SeedOnStartup { get; set; }

        /// <summary>Directory holding <c>demo-races.json</c>; relative paths are resolved against the application base directory.</summary>
        public string Path { get; set; } = "seed/races";
    }

    public sealed class ProposalOptions
    {
        /// <summary>Upper bound of catalogue rows one proposal request reads; beyond it the response is marked truncated.</summary>
        public int MaxCatalogueRows { get; set; } = 1000;
    }
}
