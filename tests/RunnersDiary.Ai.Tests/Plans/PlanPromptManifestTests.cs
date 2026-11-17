using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Domain.Insights.Narrative.Languages;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Plans;

/// <summary>ADR 0040 §4 (ADR 0026 §6 rules): released plan prompts are immutable; the manifest hash is checked here and at startup.</summary>
public sealed class PlanPromptManifestTests
{
    [Fact]
    public void Every_released_plan_prompt_matches_its_manifest_hash()
    {
        var catalog = PlanPromptCatalog.Load(RepoPaths.PlanPrompts);

        Assert.Equal(3, catalog.Current.Version);
        Assert.Equal([1, 2, 3], catalog.Versions.Keys.Order());
        Assert.Equal(PromptCatalog.Hash(File.ReadAllBytes(Path.Combine(RepoPaths.PlanPrompts, "v3.md"))), catalog.Current.Sha256);
        Assert.Equal(PromptCatalog.Hash(File.ReadAllBytes(Path.Combine(RepoPaths.PlanPrompts, "v2.md"))), catalog.Versions[2].Sha256);
        Assert.Equal(PromptCatalog.Hash(File.ReadAllBytes(Path.Combine(RepoPaths.PlanPrompts, "v1.md"))), catalog.Versions[1].Sha256);
        Assert.Equal(catalog.Versions[1].Policy.DenyList, catalog.Current.Policy.DenyList);
        Assert.Equal(catalog.Versions[2].Policy.DenyList, catalog.Current.Policy.DenyList);
        Assert.Equal(["en", "pl"], catalog.Languages);
        Assert.Equal(SummaryValidationPolicy.DefaultDenyList, catalog.Current.Policy.DenyList);
        Assert.Equal(PlanValidationPolicy.Default.DenyList, catalog.Current.Policy.DenyList);
        Assert.Equal(NarrativeLanguages.DefaultPolishDenyList, catalog.Current.Policy.LanguageDenyLists[NarrativeLanguages.Polish]);
        Assert.Same(NarrativeLanguages.DefaultLanguageDenyLists, catalog.Versions[2].Policy.LanguageDenyLists);
        Assert.StartsWith("# Role", catalog.Current.Instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("version: 3", catalog.Current.Instructions, StringComparison.Ordinal);
    }

    /// <summary>Both features carry the same per-language deny lists (ADR 0043 §3).</summary>
    [Fact]
    public void The_plan_and_summary_manifests_have_the_same_deny_lists_and_languages()
    {
        var plan = PlanPromptCatalog.Load(RepoPaths.PlanPrompts);
        var summary = PromptCatalog.Load(RepoPaths.Prompts);

        Assert.Equal(summary.Languages, plan.Languages);
        Assert.Equal(summary.Current.Policy.DenyList, plan.Current.Policy.DenyList);
        Assert.Equal(summary.Current.Policy.LanguageDenyLists[NarrativeLanguages.Polish], plan.Current.Policy.LanguageDenyLists[NarrativeLanguages.Polish]);
    }

    [Fact]
    public void The_copy_next_to_the_binaries_equals_the_repository_files()
    {
        foreach (var file in new[] { "manifest.json", "v1.md", "v2.md", "v3.md" })
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(RepoPaths.PlanPrompts, file)), File.ReadAllBytes(Path.Combine(RepoPaths.PlanPromptsInOutput, file)));
        }

        Assert.False(File.Exists(Path.Combine(RepoPaths.PlanPromptsInOutput, "README.md")));
    }

    [Fact]
    public void The_prompt_states_the_limits_the_validator_enforces()
    {
        var instructions = PlanPromptCatalog.Load(RepoPaths.PlanPrompts).Current.Instructions;
        foreach (var term in new[]
        {
            "planStart", "planEnd", "availableDistanceMeters", "maxLongRunMeters", "maxSessionsPerWeek", "phase", "referencePaceSecondsPerKm",
            "usualWeekdays", "dismissed", "otherGoal", "120 characters", "three short sentences", "medical",
            "at most 90 more than the faster one", "up to 135 more", "`trailTraining`", "multiples of 500 m", "add up the `distanceMeters`", "previous plan was rejected",
        })
        {
            Assert.Contains(term, instructions, StringComparison.Ordinal);
        }
    }

    /// <summary>ADR 0043 §2: v3 writes in the requested language and has a Polish section; the English rules are those of v2.</summary>
    [Fact]
    public void The_prompt_names_the_language_and_has_the_polish_section()
    {
        var catalog = PlanPromptCatalog.Load(RepoPaths.PlanPrompts);
        var instructions = catalog.Current.Instructions;
        foreach (var term in new[]
        {
            "# Language", "(`en` English, `pl` Polish)", "are never translated", "# Polish (only when the language is `pl`)", "\"ty\"", "gender-neutral",
            "długie wybieganie", "bieg tempowy", "tempo progowe", "tapering", "decimal comma", "kontuzje",
        })
        {
            Assert.Contains(term, instructions, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("plain English", instructions, StringComparison.Ordinal);
        var english = instructions[..instructions.IndexOf("# Language", StringComparison.Ordinal)];
        foreach (var line in catalog.Versions[2].Instructions.Split('\n')
            .Where(l => l.TrimStart().StartsWith("- ", StringComparison.Ordinal) && !l.Contains("English", StringComparison.Ordinal)))
        {
            Assert.Contains(line, english, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_changed_plan_prompt_or_a_wrong_fact_sheet_version_fails_to_load()
    {
        var dir = Directory.CreateTempSubdirectory("rd-plan-prompts-");
        try
        {
            File.Copy(Path.Combine(RepoPaths.PlanPrompts, "manifest.json"), Path.Combine(dir.FullName, "manifest.json"));
            File.Copy(Path.Combine(RepoPaths.PlanPrompts, "v2.md"), Path.Combine(dir.FullName, "v2.md"));
            File.Copy(Path.Combine(RepoPaths.PlanPrompts, "v3.md"), Path.Combine(dir.FullName, "v3.md"));
            File.WriteAllText(Path.Combine(dir.FullName, "v1.md"), File.ReadAllText(Path.Combine(RepoPaths.PlanPrompts, "v1.md")) + "\nOne more line.\n");
            Assert.Contains("does not match its manifest hash", Assert.Throws<InvalidOperationException>(() => PlanPromptCatalog.Load(dir.FullName)).Message, StringComparison.Ordinal);

            File.Copy(Path.Combine(RepoPaths.PlanPrompts, "v1.md"), Path.Combine(dir.FullName, "v1.md"), overwrite: true);
            File.WriteAllText(
                Path.Combine(dir.FullName, "manifest.json"),
                File.ReadAllText(Path.Combine(RepoPaths.PlanPrompts, "manifest.json")).Replace("\"planFactSheetVersion\": 2", "\"planFactSheetVersion\": 1", StringComparison.Ordinal));
            Assert.Contains("plan fact sheet version 1", Assert.Throws<InvalidOperationException>(() => PlanPromptCatalog.Load(dir.FullName)).Message, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
