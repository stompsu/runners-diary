using System.Text.Json.Nodes;
using RunnersDiary.Ai.Tests.Evals;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights.Narrative.Languages;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Validation;

/// <summary>
/// G-L1 follow-ups (M10 plan §10): the Polish <c>genderedForm</c> and <c>wrongLanguage</c> rules must not hit the Polish texts the app already
/// ships or tests with: the web UI strings, the demo seed texts, the summary templates and demo summary, the Polish eval good drafts and the fake
/// model's texts. A hit here is either a false positive of a matcher or a text to reword. <c>wrongLanguage</c> is checked per string and with
/// each file's strings as one draft (its draft threshold).
/// </summary>
public sealed class PolishRuleCorpusTests
{
    public static TheoryData<string> PolishJsonFiles()
    {
        var files = new List<string>();
        files.AddRange(Directory.GetFiles(Path.Combine(RepoPaths.Root, "web", "src", "i18n", "pl"), "*.json"));
        files.Add(Path.Combine(RepoPaths.DemoSeed, "i18n", "pl.json"));
        files.Add(Path.Combine(RepoPaths.Prompts, "templates", "pl.json"));
        files.AddRange(Directory.GetFiles(Path.Combine(RepoPaths.Prompts, "demo", "pl"), "*.json"));
        files.AddRange(Directory.GetDirectories(EvalFiles.CasesDirectory)
            .Select(d => Path.Combine(d, "pl", "good-draft.json"))
            .Where(File.Exists));

        var data = new TheoryData<string>();
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(RepoPaths.Root, file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PolishJsonFiles))]
    public void No_shipped_Polish_text_is_a_gendered_form(string relativePath)
    {
        var texts = new List<string>();
        Collect(JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, relativePath))), texts);

        Assert.NotEmpty(texts);
        Assert.DoesNotContain(texts, PolishGenderedForms.Contains);
    }

    [Theory]
    [MemberData(nameof(PolishJsonFiles))]
    public void No_shipped_Polish_text_is_wrong_language(string relativePath)
    {
        var texts = new List<string>();
        Collect(JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, relativePath))), texts);

        Assert.NotEmpty(texts);
        Assert.Empty(PolishWrongLanguage.Flagged(texts).Select(i => texts[i]));
        Assert.Empty(texts.SelectMany(PolishWrongLanguage.EnglishWords).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public void The_fake_models_Polish_texts_are_not_wrong_language()
    {
        var texts = FakeTexts();

        Assert.NotEmpty(texts);
        Assert.Empty(PolishWrongLanguage.Flagged(texts).Select(i => texts[i]));
        Assert.Empty(texts.SelectMany(PolishWrongLanguage.EnglishWords).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public void The_fake_models_Polish_texts_are_not_gendered_forms()
    {
        var texts = FakeTexts();

        Assert.NotEmpty(texts);
        Assert.DoesNotContain(texts, PolishGenderedForms.Contains);
    }

    private static List<string> FakeTexts() =>
        [
            .. typeof(FakeSummaryTexts).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => (string)p.GetValue(FakeSummaryTexts.Polish)!)
                .Concat(typeof(FakePlanTexts).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => (string)p.GetValue(FakePlanTexts.Polish)!)),
        ];

    private static void Collect(JsonNode? node, List<string> texts)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, value) in obj)
                {
                    Collect(value, texts);
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Collect(item, texts);
                }

                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                texts.Add(text);
                break;
        }
    }
}
