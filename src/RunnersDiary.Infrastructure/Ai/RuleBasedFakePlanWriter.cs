using System.Globalization;
using System.Text.Json;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Insights.Narrative.Languages;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>
/// The plan mode of <see cref="RuleBasedFakeChatClient"/> (ADR 0040 §7): reads the canonical plan fact sheet and writes a deterministic plan
/// inside the envelope that always passes <see cref="TrainingPlanValidator"/>, so local development, CI and E2E never call Foundry.
/// <para>Per envelope week, on days without a running, rest or other-goal commitment (and not a dismissed date): a long run on the last free
/// usual weekday (else the last free day), an easy run on the first other free day, and in non-taper weeks a third, mid-week session
/// (base: a steady progression run; build/peak: a tempo run, only where no hard commitment is adjacent and outside the last three days before
/// the goal). At most <c>maxSessionsPerWeek</c> minus the week's running commitments, at most three. Distances share 85 % of the week's
/// available distance (long 45 %, quality 25 %, easy the rest; with fewer sessions long 60 % / 100 %), each capped at the long-run cap and
/// rounded down to 100 m. Paces follow the reference pace (easy +45..+90 s, steady +20..+60 s, tempo +0..+20 s per km), none on trail goals; the faster
/// bound of an easy session (long runs included) is never below the envelope's <c>easyPaceMinSecondsPerKm</c> (M9.1), so the <c>easyPace</c> clamp never
/// has to repair the fake's plan.</para>
/// <para>With <c>goal.ambitiousTarget</c> the notes add one sentence that the plan builds toward what recent runs support (prompt v4, M9.1); with
/// <c>goal.targetFarBeyondPrediction</c> too, that sentence says the target does not look realistic by its date.</para>
/// <para>Notes and descriptions are in the fact sheet's <c>language</c> (<c>en</c> or <c>pl</c>, ADR 0043 §5); any other value gets English.</para>
/// </summary>
public static class RuleBasedFakePlanWriter
{
    public const string Notes = FakePlanTexts.EnglishNotes;

    private const double BudgetShare = 0.85;

    public static TrainingPlanDraft Write(string planFactSheetJson)
    {
        ArgumentNullException.ThrowIfNull(planFactSheetJson);
        using var document = JsonDocument.Parse(planFactSheetJson);
        var root = document.RootElement;
        var texts = root.TryGetProperty("language", out var language) && language.GetString() == NarrativeLanguages.Polish ? FakePlanTexts.Polish : FakePlanTexts.English;
        var envelope = root.GetProperty("envelope");
        var goal = root.GetProperty("goal");

        var planStart = Date(envelope.GetProperty("planStart"));
        var planEnd = Date(envelope.GetProperty("planEnd"));
        var maxSessions = envelope.GetProperty("maxSessionsPerWeek").GetInt32();
        int? reference = envelope.TryGetProperty("referencePaceSecondsPerKm", out var r) ? r.GetInt32() : null;
        int? easyMin = envelope.TryGetProperty("easyPaceMinSecondsPerKm", out var em) ? em.GetInt32() : null;
        var paces = new Paces(reference, easyMin);
        var target = Date(goal.GetProperty("targetDate"));
        var trail = goal.TryGetProperty("surface", out var surface) && surface.GetString() == "trail";
        var ambitious = goal.TryGetProperty("ambitiousTarget", out var a) && a.ValueKind == JsonValueKind.True;
        var farBeyond = goal.TryGetProperty("targetFarBeyondPrediction", out var fb) && fb.ValueKind == JsonValueKind.True;
        var usual = root.GetProperty("history").GetProperty("usualWeekdays").EnumerateArray()
            .Select(d => Enum.Parse<DayOfWeek>(d.GetString()!, ignoreCase: true)).ToHashSet();

        var blocked = new HashSet<DateOnly>();
        var hardCommitments = new HashSet<DateOnly>();
        var runningCommitments = new List<DateOnly>();
        foreach (var c in root.GetProperty("commitments").EnumerateArray())
        {
            var date = Date(c.GetProperty("date"));
            var kind = c.TryGetProperty("kind", out var k) ? k.GetString() : "planned";
            var type = c.TryGetProperty("runType", out var code) ? RunTypeCatalog.FindByCode(code.GetString()!) : null;
            if (kind == "otherGoal" || type is { CountsTowardRunningDistance: true } || type is { IsRestDay: true })
            {
                blocked.Add(date);
            }

            if (type is { CountsTowardRunningDistance: true })
            {
                runningCommitments.Add(date);
            }

            if (type is not null && (type.Category == RunTypeCategory.Race || TrainingPlanValidator.HardTypeCodes.Contains(type.Code)))
            {
                hardCommitments.Add(date);
            }
        }

        foreach (var d in root.GetProperty("dismissed").EnumerateArray())
        {
            blocked.Add(Date(d.GetProperty("date")));
        }

        var hardLimit = maxSessions >= 6 ? 3 : 2;
        var sessions = new List<DraftSession>();
        foreach (var week in envelope.GetProperty("weeks").EnumerateArray())
        {
            var monday = Date(week.GetProperty("start"));
            var sunday = monday.AddDays(6);
            var phase = week.GetProperty("phase").GetString()!;
            var longCap = week.GetProperty("maxLongRunMeters").GetDouble();
            var budget = Math.Floor(week.GetProperty("availableDistanceMeters").GetDouble() * BudgetShare / 100) * 100;

            var first = monday < planStart ? planStart : monday;
            var last = sunday > planEnd ? planEnd : sunday;
            var free = new List<DateOnly>();
            for (var day = first; day <= last; day = day.AddDays(1))
            {
                if (!blocked.Contains(day))
                {
                    free.Add(day);
                }
            }

            var fixedRunning = runningCommitments.Count(d => d >= first && d <= last);
            var slots = Math.Min(Math.Min(3, maxSessions - fixedRunning), free.Count);
            if (slots <= 0 || budget < TrainingSuggestion.MinDistanceMeters)
            {
                continue;
            }

            var longDay = free.LastOrDefault(d => usual.Contains(d.DayOfWeek), free[^1]);
            var others = free.Where(d => d != longDay).ToList();
            DateOnly? easyDay = slots >= 2 && others.Count > 0 ? others.FirstOrDefault(d => usual.Contains(d.DayOfWeek), others[0]) : null;

            DateOnly? qualityDay = null;
            var hardQuality = phase is "build" or "peak";
            if (slots >= 3 && phase != "taper")
            {
                var hardInWeek = hardCommitments.Count(d => d >= monday && d <= sunday);
                var middle = monday.AddDays(3);
                qualityDay = others
                    .Where(d => d != easyDay)
                    .Where(d => !hardQuality
                        || (hardInWeek + 1 <= hardLimit && !hardCommitments.Contains(d.AddDays(-1)) && !hardCommitments.Contains(d.AddDays(1))
                            && !(d >= target.AddDays(-TrainingPlanValidator.TaperQuietDays) && d < target)))
                    .OrderBy(d => Math.Abs(d.DayNumber - middle.DayNumber)).ThenBy(d => d)
                    .Cast<DateOnly?>()
                    .FirstOrDefault();
            }

            var count = 1 + (easyDay is null ? 0 : 1) + (qualityDay is null ? 0 : 1);
            var longMeters = Round(Math.Min(count switch { 1 => budget, 2 => 0.6 * budget, _ => 0.45 * budget }, longCap));
            var qualityMeters = qualityDay is null ? 0 : Round(Math.Min(0.25 * budget, longCap));
            var easyMeters = easyDay is null ? 0 : Round(Math.Min(budget - longMeters - qualityMeters, longCap));

            var week_ = new List<DraftSession>();
            Add(week_, longDay, "long", "easy", phase, longMeters, paces, trail, 45,
                trail ? texts.LongTrail : texts.Long);
            if (easyDay is { } e)
            {
                Add(week_, e, "easy", "easy", phase, easyMeters, paces, trail, 45, texts.Easy);
            }

            if (qualityDay is { } q)
            {
                if (hardQuality)
                {
                    Add(week_, q, "tempo", "tempo", phase, qualityMeters, paces, trail, 0, texts.Tempo);
                }
                else
                {
                    Add(week_, q, "progression", "steady", phase, qualityMeters, paces, trail, 20, texts.Progression);
                }
            }

            sessions.AddRange(week_);
        }

        return new TrainingPlanDraft(texts.NotesFor(ambitious, farBeyond), [.. sessions.OrderBy(s => s.Date, StringComparer.Ordinal)]);
    }

    private static void Add(
        List<DraftSession> sessions, DateOnly date, string runType, string intensity, string phase, double meters, Paces paces, bool trail,
        int paceOffset, string description)
    {
        if (meters < TrainingSuggestion.MinDistanceMeters)
        {
            return;
        }

        int? min = null;
        int? max = null;
        var reference = paces.Reference;
        if (reference is { } r && !trail)
        {
            var spread = intensity == "tempo" ? 20 : intensity == "steady" ? 40 : 45;
            var low = intensity is "easy" or "recovery" && paces.EasyMin is { } easyMin ? Math.Max(r + paceOffset, easyMin) : r + paceOffset;
            var high = low + spread;
            if (low >= 150 && high <= 900)
            {
                (min, max) = (low, high);
            }
        }

        var perKm = max ?? reference ?? 360;
        var duration = (int)Math.Round(meters / 1000 * perKm);
        int? durationSeconds = duration is >= TrainingSuggestion.MinDurationSeconds and <= TrainingSuggestion.MaxDurationSeconds ? duration : null;

        sessions.Add(new DraftSession(
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), runType, intensity, phase,
            Math.Min(meters, TrainingSuggestion.MaxDistanceMeters), durationSeconds, min, max, description));
    }

    /// <summary>The envelope's reference pace and easy-pace bound (M9.1), both seconds per km.</summary>
    private readonly record struct Paces(int? Reference, int? EasyMin);

    private static double Round(double meters) => Math.Max(0, Math.Floor(meters / 100) * 100);

    private static DateOnly Date(JsonElement element) =>
        DateOnly.ParseExact(element.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>The fixed texts of <see cref="RuleBasedFakePlanWriter"/> per language.</summary>
internal sealed record FakePlanTexts(
    string Notes, string Long, string LongTrail, string Easy, string Tempo, string Progression, string Ambitious, string FarBeyond)
{
    /// <summary>
    /// The plan notes; with an ambitious target (<c>goal.ambitiousTarget</c>, M9.1) one more sentence says what the plan builds toward, and a target
    /// far beyond the prediction (<c>goal.targetFarBeyondPrediction</c>) is said not to look realistic by its date (prompt v4).
    /// </summary>
    public string NotesFor(bool ambitiousTarget, bool farBeyondPrediction = false) =>
        !ambitiousTarget ? Notes : Notes + " " + (farBeyondPrediction ? FarBeyond : Ambitious);

    public const string EnglishNotes =
        "A gradual plan toward your goal: easy running, a weekly long run and one quality session in the build and peak weeks, then a lighter taper.";

    public static FakePlanTexts English { get; } = new(
        EnglishNotes,
        "Long run at an easy, conversational effort.",
        "Long run on hilly trails at an easy effort; walk the steep climbs.",
        "Easy run at a comfortable effort.",
        "Tempo run: warm up, a sustained block at tempo effort, cool down.",
        "Progression run: start easy and finish at a steady effort.",
        "Your target is ambitious, so the plan builds toward what your recent runs support.",
        "Your target does not look realistic by this date, so the plan builds toward what your recent runs support.");

    /// <summary>Glossary terms of <c>docs/i18n.md</c>; imperatives and noun phrases only (gender-neutral).</summary>
    public static FakePlanTexts Polish { get; } = new(
        "Stopniowy plan do twojego celu: spokojne bieganie, co tydzień długie wybieganie i jeden mocniejszy trening w tygodniach budowania i szczytu formy, a na koniec lżejszy tapering.",
        "Długie wybieganie w spokojnym tempie, swobodnie, tak aby móc rozmawiać.",
        "Długie wybieganie po pagórkowatych szlakach w spokojnym tempie; strome podejścia pokonuj marszem.",
        "Bieg spokojny w komfortowym tempie.",
        "Bieg tempowy: rozgrzewka, dłuższy odcinek w równym, mocnym tempie, schłodzenie.",
        "Bieg z narastającą prędkością: zacznij spokojnie i zakończ w równym tempie.",
        "Cel jest ambitny, dlatego plan rozwija formę od poziomu, na który wskazują ostatnie biegi.",
        "Cel nie wygląda realnie na ten termin, dlatego plan rozwija formę od poziomu, na który wskazują ostatnie biegi.");
}
