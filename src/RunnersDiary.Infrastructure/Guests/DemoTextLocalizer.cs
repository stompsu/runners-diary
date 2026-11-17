using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Demo;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Localization;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Infrastructure.Guests;

/// <summary>
/// Translates the English demo texts (run notes, gear, weather, rating comments, and the demo goal and plan texts) into the guest's language
/// from <c>seed/demo/i18n/&lt;lang&gt;.json</c> (<c>{ "version": 1, "texts": { "&lt;exact English text&gt;": "&lt;translation&gt;" } }</c>, ADR 0042 §7).
/// English needs no file. A text without an entry falls back to English and is counted, so callers can report it (never a failure at seeding time: a
/// guest must always get a diary). Run <b>titles</b> are mostly proper names (race names) and are translated only when the map has an entry; they are not
/// part of <see cref="RequiredTexts"/>. The maps are read once per process; a missing file is an empty map; a malformed file throws (startup check).
/// </summary>
public sealed class DemoTextLocalizer(IOptions<GuestOptions> options)
{
    public const string DirectoryName = "i18n";

    private const int SupportedVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _maps = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>True when a translation file exists for <paramref name="language"/> (always false for English).</summary>
    public bool HasMap(string language) => language != SupportedLanguages.English && File.Exists(PathFor(language));

    /// <summary>
    /// <paramref name="text"/> in <paramref name="language"/>; the English original for English, for null/empty text and for a text without an entry
    /// (then <paramref name="missing"/> is added to when it is not null).
    /// </summary>
    public string? Translate(string? text, string language, ICollection<string>? missing = null)
    {
        if (string.IsNullOrEmpty(text) || language == SupportedLanguages.English)
        {
            return text;
        }

        if (Map(language).TryGetValue(text, out var translated))
        {
            return translated;
        }

        missing?.Add(text);
        return text;
    }

    /// <summary>A run with its translatable texts in <paramref name="language"/>; <paramref name="missing"/> collects required texts that had no entry.</summary>
    public DemoRun Localize(DemoRun run, string language, ICollection<string>? missing = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (language == SupportedLanguages.English)
        {
            return run;
        }

        var v = run.Values;
        return run with
        {
            Values = v with
            {
                // Titles are optional (race names are proper names): no "missing" report.
                Title = Translate(v.Title, language),
                Notes = Translate(v.Notes, language, missing),
                Gear = Translate(v.Gear, language, missing),
                Weather = Translate(v.Weather, language, missing),
                Rating = v.Rating is { Comment: { Length: > 0 } c } r ? r with { Comment = Translate(c, language, missing) } : v.Rating,
            },
        };
    }

    /// <summary>The demo plan template with its goal title, notes and session descriptions in <paramref name="language"/>.</summary>
    public DemoTrainingPlanTemplate Localize(DemoTrainingPlanTemplate template, string language, ICollection<string>? missing = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (language == SupportedLanguages.English)
        {
            return template;
        }

        return template with
        {
            Goal = template.Goal with { Title = Translate(template.Goal.Title, language, missing)! },
            Notes = Translate(template.Notes, language, missing)!,
            Sessions = [.. template.Sessions.Select(s => s with { Description = Translate(s.Description, language, missing)! })],
        };
    }

    /// <summary>Every English text of the seed that must have an entry in a non-English map (distinct, in first-seen order).</summary>
    public static IReadOnlyList<string> RequiredTexts(DemoTemplate runs, DemoTrainingPlanTemplate plan)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(plan);
        var texts = new List<string>();
        foreach (var run in runs.Runs)
        {
            texts.AddRange(new[] { run.Notes, run.Gear, run.Weather, run.Rating?.Comment }
                .Where(t => !string.IsNullOrEmpty(t))
                .Select(t => t!));
        }

        texts.Add(plan.Goal.Title);
        texts.Add(plan.Notes);
        texts.AddRange(plan.Sessions.Select(s => s.Description));
        return [.. texts.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The required seed texts that <paramref name="language"/> has no entry for (empty for English).</summary>
    public IReadOnlyList<string> MissingTexts(DemoTemplate runs, DemoTrainingPlanTemplate plan, string language)
    {
        if (language == SupportedLanguages.English)
        {
            return [];
        }

        var map = Map(language);
        return [.. RequiredTexts(runs, plan).Where(t => !map.ContainsKey(t))];
    }

    private IReadOnlyDictionary<string, string> Map(string language)
    {
        if (!SupportedLanguages.IsSupported(language))
        {
            throw new ArgumentException("Unsupported language.", nameof(language));
        }

        lock (_gate)
        {
            if (_maps.TryGetValue(language, out var cached))
            {
                return cached;
            }

            var loaded = Load(PathFor(language));
            _maps[language] = loaded;
            return loaded;
        }
    }

    private string PathFor(string language)
    {
        // The language is an allowlisted tag (SupportedLanguages), so it cannot escape the directory.
        var directory = options.Value.DemoDataPath;
        var root = Path.IsPathRooted(directory) ? directory : Path.Combine(AppContext.BaseDirectory, directory);
        return Path.Combine(root, DirectoryName, language + ".json");
    }

    private static Dictionary<string, string> Load(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        FileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The demo translation map '{path}' is not valid: {ex.Message}", ex);
        }

        if (file is not { Version: SupportedVersion, Texts: { } texts })
        {
            throw new InvalidOperationException($"The demo translation map '{path}' needs \"version\": {SupportedVersion} and a \"texts\" object.");
        }

        if (texts.Any(t => string.IsNullOrWhiteSpace(t.Value)))
        {
            throw new InvalidOperationException($"The demo translation map '{path}' has an empty translation.");
        }

        return new Dictionary<string, string>(texts, StringComparer.Ordinal);
    }

    private sealed record FileDto(int Version, Dictionary<string, string>? Texts);
}
