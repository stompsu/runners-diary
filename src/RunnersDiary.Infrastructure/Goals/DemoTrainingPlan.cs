using RunnersDiary.Domain.Demo;
using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Localization;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Calendar;
using RunnersDiary.Infrastructure.Guests;

namespace RunnersDiary.Infrastructure.Goals;

/// <summary>A guest's demo goal and its pre-built example suggestions, ready to be inserted (ADR 0041 §5).</summary>
public sealed record DemoPlanRows(Goal Goal, IReadOnlyList<TrainingSuggestion> Suggestions);

/// <summary>
/// Loads <c>seed/demo/training-plan.json</c> (next to <c>runs.json</c>) and turns it into the demo goal plus suggestions for a guest.
/// The example plan is validated by the same <see cref="TrainingPlanValidator"/> as a model plan, against the fact sheet of the pristine
/// demo diary for the same "today" (ADR 0041 §5). No model call, no ledger row. The file is parsed once per process; a failed load is not cached.
/// </summary>
public sealed class DemoTrainingPlan(IOptions<GuestOptions> options, DemoTextLocalizer localizer)
{
    public const string FileName = "training-plan.json";

    private DemoTrainingPlanTemplate? _template;

    public DemoTrainingPlanTemplate Template
    {
        get
        {
            if (Volatile.Read(ref _template) is { } current)
            {
                return current;
            }

            var directory = options.Value.DemoDataPath;
            var path = Path.Combine(Path.IsPathRooted(directory) ? directory : Path.Combine(AppContext.BaseDirectory, directory), FileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Demo training plan not found at '{path}'.", path);
            }

            var loaded = DemoTrainingPlanTemplate.Parse(File.ReadAllText(path));
            return Interlocked.CompareExchange(ref _template, loaded, null) ?? loaded;
        }
    }

    /// <summary>
    /// The goal (with its demo plan metadata) and the suggestions for a guest whose diary is <paramref name="demoRuns"/> expanded for <paramref name="today"/>.
    /// Throws <see cref="DomainRuleException"/> when the example plan does not validate against that diary (a packaging or demo-data error).
    /// </summary>
    /// <param name="language">The guest's language tag (ADR 0042 §7): the goal title, plan notes and session descriptions come from the translation map (English where an entry is missing; those texts are added to <paramref name="missing"/>), and <c>Goal.PlanLanguage</c> is set.</param>
    public DemoPlanRows Build(
        Guid userId, IReadOnlyList<DemoRun> demoRuns, DateOnly today, DateTimeOffset now, string language = SupportedLanguages.English, ICollection<string>? missing = null)
    {
        ArgumentNullException.ThrowIfNull(demoRuns);
        var template = localizer.Localize(Template, language, missing);
        var target = template.Goal.TargetDate(today);
        var sheet = Sheet(template.Goal.Spec(today), today, demoRuns, language);
        var result = TrainingPlanValidator.Validate(template.Draft(today), sheet, PlanValidationPolicy.Default);
        if (!result.IsValid)
        {
            throw new DomainRuleException("The demo training plan is not valid for the demo diary: " + string.Join(", ", result.Violations));
        }

        var goal = Goal.Create(
            userId,
            new GoalValues(
                template.Goal.Type, template.Goal.Title, target, template.Goal.DistanceMeters, template.Goal.ElevationGainMeters, null,
                template.Goal.Surface, null, null, null, null, GoalStatus.Active),
            today, null, now);
        var generationId = Guid.CreateVersion7();
        goal.RecordPlan(new GoalPlanRecord(generationId, now, template.Notes, null, Goal.DemoModelDeployment, null, target.AddDays(-1), false, language), now);
        var trailGoal = TrainingPlanValidator.IsTrailGoal(sheet);
        var suggestions = result.Kept.Select(s => TrainingSuggestion.Create(userId, goal.Id, generationId, s, now, trailGoal)).ToList();
        return new DemoPlanRows(goal, suggestions);
    }

    /// <summary>Builds the fact sheet as <c>TrainingPlanService</c> does, from the in-memory demo diary instead of the database.</summary>
    private static PlanFactSheet Sheet(GoalSpec goal, DateOnly today, IReadOnlyList<DemoRun> demoRuns, string language)
    {
        var facts = new List<RunFact>();
        var races = new List<RaceFact>();
        var index = 0;
        foreach (var run in demoRuns)
        {
            var v = run.Values;
            var id = new Guid(++index, 0, 0, new byte[8]);
            facts.Add(new RunFact(id, v.Date, v.Status, v.RunTypeId, v.DistanceMeters, v.DurationSeconds, v.ElevationGainMeters, v.Rpe, v.Surface, v.AvgHeartRate));
            if (v.Race is { } race)
            {
                races.Add(new RaceFact(
                    id, v.Date, v.Status, v.RunTypeId, race.Name, race.DistanceCategory, race.OfficialDistanceMeters, v.DistanceMeters,
                    v.DurationSeconds, race.ResultTimeSeconds, race.TargetTimeSeconds, race.OverallPlace, race.CategoryPlace, race.CategoryName, v.Rating?.Stars));
            }
        }

        var recent = facts.Where(f => f.Status == RunStatus.Completed && f.Date >= today.AddDays(-(GoalEstimator.TrainingLookbackDays - 1)) && f.Date <= today).ToList();
        var history = TrainingHistory.From(recent, today);
        var estimate = GoalEstimator.Estimate(goal, today, recent, races);
        var reference = PlanEnvelopeBuilder.ReferencePace(goal, estimate.Prediction, history);
        var horizon = PlanEnvelopeBuilder.Build(goal, today, history, [], reference);
        var commitments = Commitments(facts, horizon.PlanStart, horizon.PlanEnd, goal.TargetDate);
        var envelope = PlanEnvelopeBuilder.Build(goal, today, history, commitments, reference);
        var report = InsightsReportBuilder.Build(new InsightsPeriod(today.AddDays(-364), today), ComparisonKind.None, today, recent, recent, races, null);
        return PlanFactSheetBuilder.Build(goal, estimate, today, report, history, commitments, [], envelope, language);
    }

    private static List<CommitmentFact> Commitments(IReadOnlyList<RunFact> facts, DateOnly planStart, DateOnly planEnd, DateOnly target)
    {
        var commitments = new List<CommitmentFact>();
        foreach (var f in facts.Where(f => f.Date >= planStart && f.Date <= target).OrderBy(f => f.Date))
        {
            var isRace = RunTypeCatalog.Find(f.RunTypeId)?.Category == RunTypeCategory.Race;
            if (f.Date <= planEnd)
            {
                commitments.Add(new CommitmentFact(f.Date, f.RunTypeId, f.DistanceMeters, f.DurationSeconds, isRace ? CommitmentKind.Race : CommitmentKind.Planned, false));
            }
            else if (isRace && f.Status == RunStatus.Planned)
            {
                commitments.Add(new CommitmentFact(f.Date, f.RunTypeId, f.DistanceMeters, f.DurationSeconds, CommitmentKind.Race, false));
            }
        }

        return commitments;
    }
}
