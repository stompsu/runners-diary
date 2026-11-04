using System.Globalization;
using System.Text.Json;
using RunnersDiary.Ai.Tests.Evals;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights.Narrative;

namespace RunnersDiary.Ai.Tests.Validation;

/// <summary>
/// The wire contract between <see cref="SummaryValues.ToValue"/> and the SPA's <c>formatSummaryValue</c> (web/src/features/insights/summary/
/// summaryFormat.ts). The frontend assumes: <c>date</c> is <c>yyyy-MM-dd</c> text, <c>flag</c> is the text "true"/"false", <c>band</c>,
/// <c>raceCategory</c>, <c>raceStatus</c> and <c>runType</c> are camelCase codes it knows, numeric units carry a number, <c>race.N.resultTime</c>
/// is seconds and elevation keys contain "elevation". Checked on EVERY fact of the eval sheets and of the demo report across the year, so a
/// mismatch fails here and not on a user's screen.
/// </summary>
public sealed class ValueEncodingContractTests
{
    private static readonly string[] Bands = ["low", "optimal", "high"];
    private static readonly string[] Categories = ["fiveK", "tenK", "halfMarathon", "marathon", "other"];
    private static readonly string[] Statuses = ["planned", "completed"];

    private static readonly HashSet<string> RunTypeCodes = ReadSpaRunTypeCodes();

    private static HashSet<string> ReadSpaRunTypeCodes()
    {
        var path = Path.Combine(RepoPaths.Root, "web", "src", "i18n", "en", "runTypes.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return [.. doc.RootElement.EnumerateObject().Select(p => p.Name).Where(n => n != "unknown")];
    }

    public static TheoryData<string> EvalNames() => EvalCases.Names();

    public static TheoryData<DateOnly> DemoDays()
    {
        var data = new TheoryData<DateOnly>();
        for (var day = new DateOnly(2028, 1, 3); day.Year == 2028; day = day.AddDays(4))
        {
            data.Add(day);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EvalNames))]
    public void Every_fact_of_an_eval_sheet_has_the_encoding_the_spa_expects(string name) =>
        AssertEncodings(EvalCases.Get(name).Sheet, name);

    [Theory]
    [MemberData(nameof(DemoDays))]
    public void Every_fact_of_the_demo_report_has_the_encoding_the_spa_expects(DateOnly today) =>
        AssertEncodings(FactSheetBuilder.Build(DemoFacts.TwelveWeeks(today), "en"), today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    [Fact]
    public void The_demo_fixture_values_as_resolved_for_the_client_follow_the_same_encodings()
    {
        var fixture = DemoSummaryFixture.Parse(File.ReadAllText(Path.Combine(RepoPaths.Prompts, "demo", "en", "12w-previous.v1.json")));
        var sheet = FactSheetBuilder.Build(DemoFacts.TwelveWeeks(new DateOnly(2026, 11, 4)), "en");

        var content = SummaryValues.Resolve(fixture.Draft, sheet);

        Assert.NotEmpty(content.Values);
        foreach (var (key, value) in content.Values)
        {
            AssertValue(key, value, "demo fixture");
        }

        // The same after the ContentJson round trip (what a stored/stale summary returns).
        var parsed = SummaryContentJson.Parse(SummaryContentJson.ToJson(content));
        foreach (var (key, value) in parsed.Values)
        {
            AssertValue(key, value, "demo fixture after round trip");
        }
    }

    [Fact]
    public void The_sheets_exercise_every_unit_the_spa_formats_so_the_check_is_not_vacuous()
    {
        var units = EvalCases.All.SelectMany(c => c.Sheet.Facts).Concat(FactSheetBuilder.Build(DemoFacts.TwelveWeeks(new DateOnly(2026, 11, 4)), "en").Facts)
            .Select(f => f.Unit).ToHashSet();

        // Stars and Days may be absent from the sheets of a given report; everything else must be present somewhere.
        foreach (var unit in Enum.GetValues<FactUnit>().Where(u => u is not (FactUnit.Stars or FactUnit.Days)))
        {
            Assert.Contains(unit, units);
        }
    }

    private static void AssertEncodings(FactSheet sheet, string context)
    {
        foreach (var fact in sheet.Facts)
        {
            AssertValue(fact.Key, SummaryValues.ToValue(fact), context);
        }
    }

    private static void AssertValue(string key, SummaryValue value, string context)
    {
        var at = $"[{context}] {key} ({value.Unit.ToWire()})";
        switch (value.Unit)
        {
            case FactUnit.Date:
                Assert.Null(value.Number);
                Assert.True(
                    value.Text is { Length: 10 } && DateOnly.TryParseExact(value.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                    $"{at}: date must be yyyy-MM-dd text, was '{value.Text}'");
                break;
            case FactUnit.Flag:
                Assert.Null(value.Number);
                Assert.True(value.Text is "true" or "false", $"{at}: flag must be the text true/false, was '{value.Text}'");
                break;
            case FactUnit.Band:
                Assert.Null(value.Number);
                Assert.True(Bands.Contains(value.Text), $"{at}: unknown band '{value.Text}'");
                break;
            case FactUnit.RaceCategory:
                Assert.Null(value.Number);
                Assert.True(Categories.Contains(value.Text), $"{at}: unknown race category '{value.Text}'");
                break;
            case FactUnit.RaceStatus:
                Assert.Null(value.Number);
                Assert.True(Statuses.Contains(value.Text), $"{at}: unknown race status '{value.Text}'");
                break;
            case FactUnit.RunType:
                Assert.Null(value.Number);
                Assert.True(value.Text is not null && RunTypeCodes.Contains(value.Text), $"{at}: run type '{value.Text}' is not a code of web runTypes.json");
                break;
            default:
                Assert.Null(value.Text);
                Assert.True(value.Number is { } n && double.IsFinite(n), $"{at}: a numeric unit must carry a finite number");
                break;
        }

        if (key.EndsWith(".resultTime", StringComparison.Ordinal) || key.EndsWith(".targetTime", StringComparison.Ordinal))
        {
            Assert.True(value.Unit == FactUnit.Seconds, $"{at}: race times are seconds");
        }

        // The SPA tells elevation from distance by the key alone (both are "meters").
        if (value.Unit == FactUnit.Meters)
        {
            var elevation = key.Contains("elevation", StringComparison.OrdinalIgnoreCase);
            var distance = key.Contains("distance", StringComparison.OrdinalIgnoreCase);
            Assert.True(elevation ^ distance || (!elevation && !distance), $"{at}: a meters key must not be both elevation and distance");
        }

        // Absolute deltas are the only signed amounts the SPA prefixes by key suffix.
        if (key.StartsWith("comparison.delta.", StringComparison.Ordinal) && key.EndsWith(".percent", StringComparison.Ordinal))
        {
            Assert.Equal(FactUnit.Percent, value.Unit);
        }
    }
}
