using System.Text;
using System.Text.Json;
using RunnersDiary.Domain.Goals.Planning;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>A released plan prompt version: the instructions (front matter removed) and the validator policy versioned with it.</summary>
public sealed record PlanPromptVersion(int Version, string Instructions, string Sha256, PlanValidationPolicy Policy);

/// <summary>
/// <c>prompts/training-plan/manifest.json</c> and the prompt files it lists (ADR 0040 §4, same rules as ADR 0026 §6): every listed version's file
/// exists inside the directory, its SHA-256 (LF line endings) equals the manifest, its front matter names the same version, and the manifest's
/// <c>planFactSheetVersion</c> equals the one the code builds. A released <c>vN.md</c> is immutable.
/// </summary>
public sealed class PlanPromptCatalog
{
    private PlanPromptCatalog(string directory, PlanPromptVersion current, IReadOnlyDictionary<int, PlanPromptVersion> versions, IReadOnlyList<string> languages)
    {
        Directory = directory;
        Current = current;
        Versions = versions;
        Languages = languages;
    }

    public string Directory { get; }

    public PlanPromptVersion Current { get; }

    public IReadOnlyDictionary<int, PlanPromptVersion> Versions { get; }

    public IReadOnlyList<string> Languages { get; }

    /// <summary>Loads and verifies the manifest. Throws <see cref="InvalidOperationException"/> on any mismatch.</summary>
    public static PlanPromptCatalog Load(string directory)
    {
        var root = PromptCatalog.ResolveDirectory(directory);
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"The plan prompt manifest was not found at '{manifestPath}'.");
        }

        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var m = manifest.RootElement;
            var factSheetVersion = m.GetProperty("planFactSheetVersion").GetInt32();
            if (factSheetVersion != PlanFactSheet.CurrentVersion)
            {
                throw new InvalidOperationException(
                    $"The plan manifest is for plan fact sheet version {factSheetVersion}, the code builds {PlanFactSheet.CurrentVersion}.");
            }

            var versions = new Dictionary<int, PlanPromptVersion>();
            foreach (var entry in m.GetProperty("versions").EnumerateObject())
            {
                var version = int.Parse(entry.Name, System.Globalization.CultureInfo.InvariantCulture);
                versions[version] = LoadVersion(root, version, entry.Value);
            }

            var current = m.GetProperty("current").GetInt32();
            var languages = m.GetProperty("languages").EnumerateArray().Select(l => l.GetString()!).ToList();
            if (languages.Count == 0)
            {
                throw new InvalidOperationException("The plan manifest lists no language.");
            }

            return new PlanPromptCatalog(
                root,
                versions.TryGetValue(current, out var currentVersion)
                    ? currentVersion
                    : throw new InvalidOperationException($"The current plan prompt version {current} is not listed."),
                versions,
                languages);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException)
        {
            throw new InvalidOperationException("The plan prompt manifest is not valid: " + ex.Message, ex);
        }
    }

    private static PlanPromptVersion LoadVersion(string root, int version, JsonElement entry)
    {
        var file = entry.GetProperty("file").GetString()!;
        var path = Path.GetFullPath(Path.Combine(root, file));
        if (Path.GetDirectoryName(path) != root.TrimEnd(Path.DirectorySeparatorChar) || !File.Exists(path))
        {
            throw new InvalidOperationException($"Plan prompt v{version}: file '{file}' is missing or outside the prompt directory.");
        }

        var bytes = File.ReadAllBytes(path);
        var expected = entry.GetProperty("sha256").GetString()!;
        var actual = PromptCatalog.Hash(bytes);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Plan prompt v{version} ('{file}') does not match its manifest hash (expected {expected}, actual {actual}). Released prompts are immutable: add a new version.");
        }

        var (frontMatterVersion, instructions) = PromptCatalog.SplitFrontMatter(Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal));
        if (frontMatterVersion != version)
        {
            throw new InvalidOperationException($"Plan prompt '{file}' declares version {frontMatterVersion}, the manifest lists it as {version}.");
        }

        var policy = new PlanValidationPolicy([.. entry.GetProperty("denyList").EnumerateArray().Select(d => d.GetString()!)]);
        return new PlanPromptVersion(version, instructions, actual, policy);
    }
}

/// <summary>
/// Fails host startup when the plan prompt manifest or a plan prompt hash is broken (resolving the catalog loads and verifies it), instead of
/// failing the first plan request. Not registered during build-time OpenAPI generation.
/// </summary>
internal sealed class PlanPromptStartupCheck(PlanPromptCatalog prompts) : Microsoft.Extensions.Hosting.IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = prompts.Current;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
