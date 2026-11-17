using System.Text.Encodings.Web;
using System.Text.Json;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights.Narrative;

namespace RunnersDiary.Ai.Tests.Evals;

/// <summary>The committed files of an eval case: <c>Evals/cases/&lt;case&gt;/{fact-sheet.json, good-draft.json, bad-*.json}</c>.</summary>
internal static class EvalFiles
{
    public static string CasesDirectory => Path.Combine(RepoPaths.TestsDirectory, "Evals", "cases");

    public static string CaseDirectory(string name) => Path.Combine(CasesDirectory, name);

    /// <summary>English drafts sit in the case directory, the drafts of another language in its <c>&lt;lang&gt;/</c> subdirectory.</summary>
    public static string CaseDirectory(string name, string language) => language == "en" ? CaseDirectory(name) : Path.Combine(CaseDirectory(name), language);

    public static string GoodDraft(string name) => File.ReadAllText(Path.Combine(CaseDirectory(name), "good-draft.json"));

    public static string GoodDraft(string name, string language) => File.ReadAllText(Path.Combine(CaseDirectory(name, language), "good-draft.json"));

    /// <summary>The adversarial drafts of a non-English language: <c>cases/&lt;case&gt;/&lt;lang&gt;/bad-*.json</c>.</summary>
    public static TheoryData<string, string> BadDrafts(string language)
    {
        var data = new TheoryData<string, string>();
        foreach (var caseDir in Directory.GetDirectories(CasesDirectory).Order(StringComparer.Ordinal))
        {
            var dir = Path.Combine(caseDir, language);
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir, "bad-*.json").Order(StringComparer.Ordinal))
                {
                    data.Add(Path.GetFileName(caseDir), Path.GetFileName(file));
                }
            }
        }

        return data;
    }

    public static TheoryData<string, string> PolishBadDrafts() => BadDrafts("pl");

    /// <summary>Adversarial drafts: <c>bad-&lt;ruleCode&gt;[-variant].json</c> with the rule code they must violate.</summary>
    public static TheoryData<string, string> BadDrafts()
    {
        var data = new TheoryData<string, string>();
        foreach (var caseDir in Directory.GetDirectories(CasesDirectory).Order(StringComparer.Ordinal))
        {
            foreach (var file in Directory.GetFiles(caseDir, "bad-*.json").Order(StringComparer.Ordinal))
            {
                data.Add(Path.GetFileName(caseDir), Path.GetFileName(file));
            }
        }

        return data;
    }

    public static string ExpectedCode(string badFileName) => Path.GetFileNameWithoutExtension(badFileName)["bad-".Length..].Split('-')[0];

    /// <summary>The golden fact sheet: the canonical JSON, indented one fact per line so diffs stay readable.</summary>
    public static string Indented(FactSheet sheet)
    {
        using var document = JsonDocument.Parse(FactSheetJson.ToJson(sheet));
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            var root = document.RootElement;
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject().Where(p => p.Name != "facts"))
            {
                property.WriteTo(writer);
            }

            writer.WriteStartArray("facts");
            writer.Flush();
            foreach (var fact in root.GetProperty("facts").EnumerateArray())
            {
                fact.WriteTo(writer);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan).ReplaceLineEndings("\n") + "\n";
    }
}
