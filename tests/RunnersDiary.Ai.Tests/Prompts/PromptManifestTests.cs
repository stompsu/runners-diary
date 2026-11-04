using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Prompts;

/// <summary>Plan AC4 / ADR 0026 §6: released prompts are immutable; the manifest hash is checked here and at startup.</summary>
public sealed class PromptManifestTests
{
    [Fact]
    public void Every_released_prompt_matches_its_manifest_hash()
    {
        // Load verifies every listed version and throws on a mismatch: editing v1.md without a new version fails CI here.
        var catalog = PromptCatalog.Load(RepoPaths.Prompts);

        Assert.Equal(1, catalog.Current.Version);
        Assert.Equal(PromptCatalog.Hash(File.ReadAllBytes(Path.Combine(RepoPaths.Prompts, "v1.md"))), catalog.Current.Sha256);
        Assert.Equal(["en"], catalog.Languages);
        Assert.Equal(0.25, catalog.Current.Policy.EstimatedLoadThreshold);
        Assert.Equal(SummaryValidationPolicy.DefaultDenyList, catalog.Current.Policy.DenyList);
        Assert.StartsWith("# Role", catalog.Current.Instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void The_copy_next_to_the_binaries_equals_the_repository_files()
    {
        foreach (var relative in new[] { "manifest.json", "v1.md", Path.Combine("templates", "en.json"), Path.Combine("demo", "en", "12w-previous.v1.json") })
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(RepoPaths.Prompts, relative)), File.ReadAllBytes(Path.Combine(RepoPaths.PromptsInOutput, relative)));
        }

        Assert.False(File.Exists(Path.Combine(RepoPaths.PromptsInOutput, "README.md")));
        Assert.Single(DemoSummaryCatalog.Load(PromptCatalog.Load(RepoPaths.PromptsInOutput)).Fixtures);
    }

    [Fact]
    public void A_changed_prompt_fails_to_load()
    {
        var dir = CopyPrompts();
        File.AppendAllText(Path.Combine(dir, "v1.md"), "\nOne more instruction.\n");

        var error = Assert.Throws<InvalidOperationException>(() => PromptCatalog.Load(dir));
        Assert.Contains("does not match its manifest hash", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_endings_do_not_change_the_hash()
    {
        var dir = CopyPrompts();
        var path = Path.Combine(dir, "v1.md");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal(1, PromptCatalog.Load(dir).Current.Version);
    }

    [Theory]
    [InlineData("\"current\": 1", "\"current\": 2", "is not listed")]
    [InlineData("\"file\": \"v1.md\"", "\"file\": \"../v1.md\"", "outside the prompt directory")]
    [InlineData("\"factSheetVersion\": 1", "\"factSheetVersion\": 2", "fact sheet version")]
    public void A_broken_manifest_fails_to_load(string find, string replace, string message)
    {
        var dir = CopyPrompts();
        var manifest = Path.Combine(dir, "manifest.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(find, replace, StringComparison.Ordinal));

        var error = Assert.Throws<InvalidOperationException>(() => PromptCatalog.Load(dir));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_front_matter_version_that_differs_from_the_manifest_fails()
    {
        var dir = CopyPrompts();
        var path = Path.Combine(dir, "v1.md");
        File.WriteAllText(path, File.ReadAllText(path).Replace("version: 1", "version: 2", StringComparison.Ordinal));
        var manifest = Path.Combine(dir, "manifest.json");
        var json = File.ReadAllText(manifest);
        var oldHash = PromptCatalog.Load(RepoPaths.Prompts).Current.Sha256;
        File.WriteAllText(manifest, json.Replace(oldHash, PromptCatalog.Hash(File.ReadAllBytes(path)), StringComparison.Ordinal));

        var error = Assert.Throws<InvalidOperationException>(() => PromptCatalog.Load(dir));
        Assert.Contains("declares version 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_states_the_rules_the_validator_enforces()
    {
        var text = PromptCatalog.Load(RepoPaths.Prompts).Current.Instructions;
        foreach (var phrase in new[] { "{totals.distance}", "Never write digits", "isRaceWeek", "taper", "so far", "acwr.asOf.unavailable", "dq.estimatedLoadShare", "No links, no HTML, no Markdown", "at most 3 items", "`null`" })
        {
            Assert.Contains(phrase, text, StringComparison.Ordinal);
        }
    }

    private static string CopyPrompts()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rd-prompts-" + Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.GetFiles(RepoPaths.Prompts, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dir, Path.GetRelativePath(RepoPaths.Prompts, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return dir;
    }
}
