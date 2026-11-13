using RunnersDiary.Domain.Races;

namespace RunnersDiary.Infrastructure.Races;

/// <summary>One source as the SPA shows it (attribution list): <c>client-config.races.sources</c>.</summary>
public sealed record RaceSourceInfo(string Id, string Name, string Url);

/// <summary>
/// Small static registry keyed by <see cref="RaceSource"/>: the stable string id used in <c>RaceProposalDto.source</c> and the attribution
/// (name, URL) of every real source (ADR 0035 §5). The synthetic <see cref="RaceSource.Demo"/> source has an id but no attribution entry,
/// so it never appears in <c>client-config</c>.
/// </summary>
public static class RaceSourceRegistry
{
    public const string RunningLifeId = "running-life";
    public const string DemoId = "demo";

    private static readonly RaceSourceInfo[] Attributed =
    [
        new(RunningLifeId, "running.life", "https://running.life/"),
    ];

    /// <summary>The sources the SPA lists as attribution (real sources only).</summary>
    public static IReadOnlyList<RaceSourceInfo> Sources => Attributed;

    /// <summary>The stable id of <paramref name="source"/> (<c>RaceProposalDto.source</c>).</summary>
    public static string IdOf(RaceSource source) => source switch
    {
        RaceSource.RunningLife => RunningLifeId,
        RaceSource.Demo => DemoId,
        _ => source.ToString().ToLowerInvariant(),
    };
}
