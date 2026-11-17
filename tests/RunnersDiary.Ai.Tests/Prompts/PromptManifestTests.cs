using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Domain.Insights.Narrative.Languages;
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

        Assert.Equal(3, catalog.Current.Version);
        Assert.Equal(PromptCatalog.Hash(File.ReadAllBytes(Path.Combine(RepoPaths.Prompts, "v3.md"))), catalog.Current.Sha256);
        Assert.Equal([1, 2, 3], catalog.Versions.Keys.Order());
        Assert.Equal(PromptCatalog.Hash(File.ReadAllBytes(Path.Combine(RepoPaths.Prompts, "v1.md"))), catalog.Versions[1].Sha256);
        Assert.Equal(PromptCatalog.Hash(File.ReadAllBytes(Path.Combine(RepoPaths.Prompts, "v2.md"))), catalog.Versions[2].Sha256);
        Assert.Equal(["en", "pl"], catalog.Languages);
        Assert.Equal(0.25, catalog.Current.Policy.EstimatedLoadThreshold);
        Assert.Equal(SummaryValidationPolicy.DefaultDenyList, catalog.Current.Policy.DenyList);
        Assert.StartsWith("# Role", catalog.Current.Instructions, StringComparison.Ordinal);
    }

    /// <summary>ADR 0043 §3: v3 carries a deny list per language; the Polish one mirrors the validator's built-in list (M10 C1).</summary>
    [Fact]
    public void Version_3_has_a_deny_list_per_language_and_the_older_versions_keep_their_flat_english_list()
    {
        var catalog = PromptCatalog.Load(RepoPaths.Prompts);

        var v3 = catalog.Current.Policy;
        Assert.Equal(NarrativeLanguages.English, v3.Language);
        Assert.Equal(SummaryValidationPolicy.DefaultDenyList, v3.DenyList);
        Assert.Equal(NarrativeLanguages.DefaultPolishDenyList, v3.LanguageDenyLists[NarrativeLanguages.Polish]);
        Assert.Equal([NarrativeLanguages.Polish], v3.LanguageDenyLists.Keys);
        Assert.Equal(NarrativeLanguages.Polish, v3.ForLanguage("pl").Language);

        foreach (var version in new[] { 1, 2 })
        {
            var policy = catalog.Versions[version].Policy;
            Assert.Equal(SummaryValidationPolicy.DefaultDenyList, policy.DenyList);
            Assert.Same(NarrativeLanguages.DefaultLanguageDenyLists, policy.LanguageDenyLists);
        }
    }

    [Theory]
    [InlineData("\"en\": [\"injur*\"", "\"pl\": [\"injur*\"", "has no 'en' entry")]
    [InlineData("\"pl\": [\"kontuzj*\"", "\"de\": [\"kontuzj*\"", "without validator rules")]
    [InlineData("\"languages\": [\"en\", \"pl\"]", "\"languages\": [\"en\", \"de\"]", "without validator rules")]
    [InlineData("\"languages\": [\"en\", \"pl\"]", "\"languages\": [\"en\", \"en\"]", "twice")]
    [InlineData("\"languages\": [\"en\", \"pl\"]", "\"languages\": []", "lists no language")]
    public void A_broken_language_entry_fails_to_load(string find, string replace, string message)
    {
        var dir = CopyPrompts();
        var manifest = Path.Combine(dir, "manifest.json");
        var json = File.ReadAllText(manifest);
        Assert.Contains(find, json, StringComparison.Ordinal);
        File.WriteAllText(manifest, json.Replace(find, replace, StringComparison.Ordinal));

        var error = Assert.Throws<InvalidOperationException>(() => PromptCatalog.Load(dir));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_current_version_without_a_polish_deny_list_fails_while_pl_is_listed()
    {
        var dir = CopyPrompts();
        var manifest = Path.Combine(dir, "manifest.json");
        var json = File.ReadAllText(manifest);
        var start = json.IndexOf(",\n        \"pl\": [", StringComparison.Ordinal);
        var end = json.IndexOf(']', start);
        File.WriteAllText(manifest, json.Remove(start, end - start + 1));

        var error = Assert.Throws<InvalidOperationException>(() => PromptCatalog.Load(dir));
        Assert.Contains("no deny list for 'pl'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_copy_next_to_the_binaries_equals_the_repository_files()
    {
        foreach (var relative in new[]
        {
            "manifest.json", "v1.md", "v2.md", "v3.md", Path.Combine("templates", "en.json"), Path.Combine("templates", "pl.json"),
            Path.Combine("demo", "en", "12w-previous.v1.json"), Path.Combine("demo", "pl", "12w-previous.v1.json"),
        })
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(RepoPaths.Prompts, relative)), File.ReadAllBytes(Path.Combine(RepoPaths.PromptsInOutput, relative)));
        }

        Assert.False(File.Exists(Path.Combine(RepoPaths.PromptsInOutput, "README.md")));
        Assert.Equal(["en", "pl"], DemoSummaryCatalog.Load(PromptCatalog.Load(RepoPaths.PromptsInOutput)).Fixtures.Select(f => f.Language));
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

        Assert.Equal(3, PromptCatalog.Load(dir).Current.Version);
    }

    [Theory]
    [InlineData("\"current\": 3", "\"current\": 4", "is not listed")]
    [InlineData("\"file\": \"v1.md\"", "\"file\": \"../v1.md\"", "outside the prompt directory")]
    [InlineData("\"factSheetVersion\": 2", "\"factSheetVersion\": 1", "fact sheet version")]
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
        var oldHash = PromptCatalog.Load(RepoPaths.Prompts).Versions[1].Sha256;
        File.WriteAllText(manifest, json.Replace(oldHash, PromptCatalog.Hash(File.ReadAllBytes(path)), StringComparison.Ordinal));

        var error = Assert.Throws<InvalidOperationException>(() => PromptCatalog.Load(dir));
        Assert.Contains("declares version 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_states_the_rules_the_validator_enforces()
    {
        var text = PromptCatalog.Load(RepoPaths.Prompts).Current.Instructions;
        foreach (var phrase in new[]
        {
            "{totals.distance}", "write no digits", "ordinal words", "Never write a brace anywhere else", "isRaceWeek", "week.N.taper",
            "week.N.loadSpike", "so far", "acwr.asOf.unavailable", "dq.estimatedLoadShare", "No links, no HTML, no Markdown", "at most 3 items", "`null`",
        })
        {
            Assert.Contains(phrase, text, StringComparison.Ordinal);
        }
    }

    /// <summary>ADR 0043 §2: the v3 Polish section states the grammar, number-word, safety and glossary rules.</summary>
    [Fact]
    public void The_prompt_has_the_polish_section()
    {
        var text = PromptCatalog.Load(RepoPaths.Prompts).Current.Instructions;
        foreach (var phrase in new[]
        {
            "# Polish (only when the language is `pl`)", "(`en` English, `pl` Polish)", "\"ty\"", "gender-neutral", "nominative", "after a colon",
            "Never put a counted noun right after a count placeholder", "\"dwa\"", "\"pierwszy\"", "\"dwukrotnie\"", "\"półmaraton\" are fine",
            "kontuzje", "ból", "wskaźnik ACWR", "typowe obciążenie", "tempo spokojnych biegów", "tapering", "rekord życiowy", "w tygodniu od {week.3.start}",
        })
        {
            Assert.Contains(phrase, text, StringComparison.Ordinal);
        }

        // The English rules are those of v2: v3 only adds the language line, the display note and the Polish section.
        var v2 = PromptCatalog.Load(RepoPaths.Prompts).Versions[2].Instructions;
        var englishPart = text[..text.IndexOf("# Polish", StringComparison.Ordinal)];
        foreach (var line in v2.Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal) && !l.Contains("display", StringComparison.Ordinal) && !l.Contains("language", StringComparison.Ordinal)))
        {
            Assert.Contains(line, englishPart, StringComparison.Ordinal);
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
