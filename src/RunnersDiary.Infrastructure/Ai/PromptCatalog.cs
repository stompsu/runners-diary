using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RunnersDiary.Domain.Insights.Narrative;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>A released prompt version: the instructions (front matter removed) and the validator rules versioned with it.</summary>
public sealed record PromptVersion(int Version, string Instructions, string Sha256, SummaryValidationPolicy Policy);

/// <summary>
/// <c>prompts/training-insights/manifest.json</c> and the prompt files it lists (ADR 0026 §6). Loading verifies every listed version:
/// the file exists, its SHA-256 (LF line endings) equals the manifest, and its front matter names the same version. A released
/// <c>vN.md</c> is immutable, so a wording change needs a new version and makes cached summaries stale.
/// </summary>
public sealed class PromptCatalog
{
    private PromptCatalog(string directory, PromptVersion current, IReadOnlyDictionary<int, PromptVersion> versions, IReadOnlyList<string> languages)
    {
        Directory = directory;
        Current = current;
        Versions = versions;
        Languages = languages;
    }

    public string Directory { get; }

    public PromptVersion Current { get; }

    public IReadOnlyDictionary<int, PromptVersion> Versions { get; }

    /// <summary>The languages with templates and a prompt line (the API's <c>lang</c> values).</summary>
    public IReadOnlyList<string> Languages { get; }

    public static string ResolveDirectory(string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path));

    /// <summary>Loads and verifies the manifest. Throws <see cref="InvalidOperationException"/> on any mismatch.</summary>
    public static PromptCatalog Load(string directory)
    {
        var root = ResolveDirectory(directory);
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"The prompt manifest was not found at '{manifestPath}'.");
        }

        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var m = manifest.RootElement;
            var factSheetVersion = m.GetProperty("factSheetVersion").GetInt32();
            if (factSheetVersion != FactSheet.CurrentVersion)
            {
                throw new InvalidOperationException($"The manifest is for fact sheet version {factSheetVersion}, the code builds {FactSheet.CurrentVersion}.");
            }

            var versions = new Dictionary<int, PromptVersion>();
            foreach (var entry in m.GetProperty("versions").EnumerateObject())
            {
                var version = int.Parse(entry.Name, System.Globalization.CultureInfo.InvariantCulture);
                versions[version] = LoadVersion(root, version, entry.Value);
            }

            var current = m.GetProperty("current").GetInt32();
            var languages = m.GetProperty("languages").EnumerateArray().Select(l => l.GetString()!).ToList();
            if (languages.Count == 0)
            {
                throw new InvalidOperationException("The manifest lists no language.");
            }

            return new PromptCatalog(
                root,
                versions.TryGetValue(current, out var currentVersion) ? currentVersion : throw new InvalidOperationException($"The current prompt version {current} is not listed."),
                versions,
                languages);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException)
        {
            throw new InvalidOperationException("The prompt manifest is not valid: " + ex.Message, ex);
        }
    }

    /// <summary>SHA-256 (lower-case hex) of a prompt file with CRLF normalized to LF, so a Windows checkout hashes the same.</summary>
    public static string Hash(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var text = Encoding.UTF8.GetString(content).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static PromptVersion LoadVersion(string root, int version, JsonElement entry)
    {
        var file = entry.GetProperty("file").GetString()!;
        var path = Path.GetFullPath(Path.Combine(root, file));
        if (Path.GetDirectoryName(path) != root.TrimEnd(Path.DirectorySeparatorChar) || !File.Exists(path))
        {
            throw new InvalidOperationException($"Prompt v{version}: file '{file}' is missing or outside the prompt directory.");
        }

        var bytes = File.ReadAllBytes(path);
        var expected = entry.GetProperty("sha256").GetString()!;
        var actual = Hash(bytes);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Prompt v{version} ('{file}') does not match its manifest hash (expected {expected}, actual {actual}). Released prompts are immutable: add a new version.");
        }

        var (frontMatterVersion, instructions) = SplitFrontMatter(Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal));
        if (frontMatterVersion != version)
        {
            throw new InvalidOperationException($"Prompt '{file}' declares version {frontMatterVersion}, the manifest lists it as {version}.");
        }

        var policy = new SummaryValidationPolicy(
            entry.GetProperty("estimatedLoadThreshold").GetDouble(),
            [.. entry.GetProperty("denyList").EnumerateArray().Select(d => d.GetString()!)]);
        return new PromptVersion(version, instructions, actual, policy);
    }

    private static (int Version, string Body) SplitFrontMatter(string text)
    {
        const string fence = "---\n";
        if (!text.StartsWith(fence, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A prompt file must start with front matter.");
        }

        var end = text.IndexOf("\n" + fence, fence.Length - 1, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException("The prompt front matter is not closed.");
        }

        var header = text[fence.Length..(end + 1)];
        int? version = null;
        foreach (var line in header.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2 && parts[0].Trim() == "version")
            {
                version = int.Parse(parts[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return (version ?? throw new InvalidOperationException("The prompt front matter has no version."), text[(end + 1 + fence.Length)..].Trim());
    }
}

/// <summary>The deterministic templates per language (<c>templates/&lt;lang&gt;.json</c>, ADR 0026 §4).</summary>
public sealed class TemplateCatalog
{
    private readonly Dictionary<string, SummaryTemplateSet> _byLanguage;

    private TemplateCatalog(Dictionary<string, SummaryTemplateSet> byLanguage) => _byLanguage = byLanguage;

    /// <summary>The wording of <paramref name="kind"/> that applies to <paramref name="sheet"/> (see <see cref="SummaryTemplateSet"/>).</summary>
    public SummaryDraft Get(string language, SummaryTemplateKind kind, FactSheet sheet) =>
        _byLanguage.TryGetValue(language, out var set) ? set.Get(kind, sheet) : throw new KeyNotFoundException($"No templates for '{language}'.");

    public static TemplateCatalog Load(PromptCatalog prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        var sets = new Dictionary<string, SummaryTemplateSet>(StringComparer.Ordinal);
        foreach (var language in prompts.Languages)
        {
            var path = Path.Combine(prompts.Directory, "templates", language + ".json");
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"Templates for '{language}' were not found at '{path}'.");
            }

            var set = SummaryTemplateSet.Parse(File.ReadAllText(path));
            sets[language] = set.Language == language ? set : throw new InvalidOperationException($"'{path}' declares language '{set.Language}'.");
        }

        return new TemplateCatalog(sets);
    }
}

/// <summary>The demo example summaries (<c>demo/&lt;lang&gt;/*.json</c>, ADR 0028 §5).</summary>
public sealed class DemoSummaryCatalog
{
    private DemoSummaryCatalog(IReadOnlyList<DemoSummaryFixture> fixtures) => Fixtures = fixtures;

    public IReadOnlyList<DemoSummaryFixture> Fixtures { get; }

    public DemoSummaryFixture? Find(string preset, string language) =>
        Fixtures.FirstOrDefault(f => f.Preset == preset && f.Language == language);

    public static DemoSummaryCatalog Load(PromptCatalog prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        var fixtures = new List<DemoSummaryFixture>();
        foreach (var language in prompts.Languages)
        {
            var directory = Path.Combine(prompts.Directory, "demo", language);
            if (!System.IO.Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in System.IO.Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal))
            {
                var fixture = DemoSummaryFixture.Parse(File.ReadAllText(file));
                if (fixture.Language != language || fixture.FactSheetVersion != FactSheet.CurrentVersion)
                {
                    throw new InvalidOperationException($"Demo fixture '{file}' has language '{fixture.Language}' / fact sheet v{fixture.FactSheetVersion}.");
                }

                if (DemoPresets.Range(fixture.Preset, new DateOnly(2026, 1, 1)) is null)
                {
                    throw new InvalidOperationException($"Demo fixture '{file}' names an unknown preset '{fixture.Preset}'.");
                }

                fixtures.Add(fixture);
            }
        }

        return new DemoSummaryCatalog(fixtures);
    }
}
