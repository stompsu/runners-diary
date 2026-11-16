using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Plans;

/// <summary>ADR 0040 §4 (ADR 0026 §6 rules): released plan prompts are immutable; the manifest hash is checked here and at startup.</summary>
public sealed class PlanPromptManifestTests
{
    [Fact]
    public void Every_released_plan_prompt_matches_its_manifest_hash()
    {
        var catalog = PlanPromptCatalog.Load(RepoPaths.PlanPrompts);

        Assert.Equal(1, catalog.Current.Version);
        Assert.Equal([1], catalog.Versions.Keys);
        Assert.Equal(PromptCatalog.Hash(File.ReadAllBytes(Path.Combine(RepoPaths.PlanPrompts, "v1.md"))), catalog.Current.Sha256);
        Assert.Equal(["en"], catalog.Languages);
        Assert.Equal(SummaryValidationPolicy.DefaultDenyList, catalog.Current.Policy.DenyList);
        Assert.Equal(PlanValidationPolicy.Default.DenyList, catalog.Current.Policy.DenyList);
        Assert.StartsWith("# Role", catalog.Current.Instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("version: 1", catalog.Current.Instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void The_copy_next_to_the_binaries_equals_the_repository_files()
    {
        foreach (var file in new[] { "manifest.json", "v1.md" })
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
        })
        {
            Assert.Contains(term, instructions, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_changed_plan_prompt_or_a_wrong_fact_sheet_version_fails_to_load()
    {
        var dir = Directory.CreateTempSubdirectory("rd-plan-prompts-");
        try
        {
            File.Copy(Path.Combine(RepoPaths.PlanPrompts, "manifest.json"), Path.Combine(dir.FullName, "manifest.json"));
            File.WriteAllText(Path.Combine(dir.FullName, "v1.md"), File.ReadAllText(Path.Combine(RepoPaths.PlanPrompts, "v1.md")) + "\nOne more line.\n");
            Assert.Contains("does not match its manifest hash", Assert.Throws<InvalidOperationException>(() => PlanPromptCatalog.Load(dir.FullName)).Message, StringComparison.Ordinal);

            File.Copy(Path.Combine(RepoPaths.PlanPrompts, "v1.md"), Path.Combine(dir.FullName, "v1.md"), overwrite: true);
            File.WriteAllText(
                Path.Combine(dir.FullName, "manifest.json"),
                File.ReadAllText(Path.Combine(RepoPaths.PlanPrompts, "manifest.json")).Replace("\"planFactSheetVersion\": 1", "\"planFactSheetVersion\": 2", StringComparison.Ordinal));
            Assert.Contains("plan fact sheet version 2", Assert.Throws<InvalidOperationException>(() => PlanPromptCatalog.Load(dir.FullName)).Message, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
