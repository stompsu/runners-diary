using System.Globalization;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Ai.Tests.Support;

/// <summary>Synthetic training logs for eval cases and unit tests, built from a weekly pattern (ids are sequential).</summary>
internal sealed class Training
{
    private readonly List<RunFact> _facts = [];
    private readonly List<RaceFact> _races = [];

    public static DateOnly D(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public IReadOnlyList<RunFact> Facts => _facts;

    public IReadOnlyList<RaceFact> Races => _races;

    /// <summary>One session. <paramref name="rpe"/> null means "not recorded" (the load uses the type's default RPE).</summary>
    public Training Run(DateOnly date, string type, double? meters, int? seconds, int? rpe, RunStatus status = RunStatus.Completed)
    {
        _facts.Add(new RunFact(NextId(), date, status, Type(type), meters, seconds, null, rpe, Surface.Road, 140));
        return this;
    }

    /// <summary>The base week from Monday: easy Mon/Fri, tempo Wed, long Sun (about 1 083 AU at scale 1).</summary>
    public Training Week(DateOnly monday, double scale = 1, bool recordRpe = true)
    {
        int S(int seconds) => (int)Math.Round(seconds * scale);
        double M(double meters) => Math.Round(meters * scale);
        Run(monday, "easy", M(8000), S(2880), recordRpe ? 3 : null);
        Run(monday.AddDays(2), "tempo", M(9000), S(2700), 7);
        Run(monday.AddDays(4), "easy", M(8000), S(2880), recordRpe ? 3 : null);
        Run(monday.AddDays(6), "long", M(16000), S(5760), recordRpe ? 5 : null);
        return this;
    }

    public Training Weeks(DateOnly firstMonday, int count, double scale = 1, bool recordRpe = true)
    {
        for (var i = 0; i < count; i++)
        {
            Week(firstMonday.AddDays(7 * i), scale, recordRpe);
        }

        return this;
    }

    public Training Race(DateOnly date, string type, RaceDistanceCategory category, double meters, int seconds, RunStatus status = RunStatus.Completed, int? target = null)
    {
        var id = NextId();
        var completed = status == RunStatus.Completed;
        _facts.Add(new RunFact(id, date, status, Type(type), completed ? meters : null, completed ? seconds : null, null, completed ? 9 : null, Surface.Road, 170));
        _races.Add(new RaceFact(
            id, date, status, Type(type), "Canary Race Name", category, meters, completed ? meters : null, completed ? seconds : null,
            completed ? seconds : null, target, completed ? 12 : null, completed ? 3 : null, "M30", completed ? 4 : null));
        return this;
    }

    public InsightsReport Report(DateOnly from, DateOnly to, DateOnly today, ComparisonKind compare = ComparisonKind.Previous) =>
        InsightsReportBuilder.Build(new InsightsPeriod(from, to), compare, today, _facts, _facts, _races, null);

    private static short Type(string code) => RunTypeCatalog.FindByCode(code)!.Id;

    private Guid NextId() => new(_facts.Count + 1, 0, 0, new byte[8]);
}
