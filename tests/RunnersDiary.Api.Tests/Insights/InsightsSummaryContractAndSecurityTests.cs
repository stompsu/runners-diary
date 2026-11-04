using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Domain.Time;
using static RunnersDiary.Api.Tests.Insights.SummaryTestSupport;

namespace RunnersDiary.Api.Tests.Insights;

/// <summary>
/// QA additions for M6 (plan section 6): the value encodings the SPA relies on as the real API produces them, model output that carries
/// markup being refused end to end, and the ledger holding no content.
/// </summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class InsightsSummaryContractAndSecurityTests(ContainersApiFactory factory)
{
    private static readonly string[] Bands = ["low", "optimal", "high"];
    private static readonly string[] Categories = ["fiveK", "tenK", "halfMarathon", "marathon", "other"];
    private static readonly string[] Statuses = ["planned", "completed"];
    private static readonly string[] NumericUnits =
        ["meters", "seconds", "load", "ratio", "percent", "share", "secondsPerKm", "secondsPerKmPerWeek", "count", "days", "stars"];

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The decoding rules of web/src/features/insights/summary/summaryFormat.ts, applied to one value of a response.</summary>
    private static void AssertSpaEncoding(string key, JsonElement value, string context)
    {
        var unit = value.GetProperty("unit").GetString()!;
        var number = value.GetProperty("number");
        var text = value.GetProperty("text");
        var at = $"[{context}] {key} ({unit})";
        var t = text.ValueKind == JsonValueKind.String ? text.GetString() : null;

        if (NumericUnits.Contains(unit))
        {
            Assert.True(number.ValueKind == JsonValueKind.Number, $"{at}: numeric unit without a number");
            Assert.True(text.ValueKind == JsonValueKind.Null, $"{at}: numeric unit with text");
            return;
        }

        Assert.True(number.ValueKind == JsonValueKind.Null, $"{at}: a text unit must have number null");
        switch (unit)
        {
            case "date":
                Assert.True(t is { Length: 10 } && DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), $"{at}: not yyyy-MM-dd: '{t}'");
                break;
            case "flag":
                Assert.True(t is "true" or "false", $"{at}: flag text was '{t}'");
                break;
            case "band":
                Assert.True(Bands.Contains(t), $"{at}: band '{t}'");
                break;
            case "raceCategory":
                Assert.True(Categories.Contains(t), $"{at}: race category '{t}'");
                break;
            case "raceStatus":
                Assert.True(Statuses.Contains(t), $"{at}: race status '{t}'");
                break;
            case "runType":
                Assert.False(string.IsNullOrEmpty(t), $"{at}: empty run type");
                Assert.Matches("^[a-z][A-Za-z0-9]*$", t!);
                break;
            default:
                Assert.Fail($"{at}: unknown unit");
                break;
        }
    }

    private static void AssertAllValues(JsonElement summary, string context)
    {
        var values = summary.GetProperty("values");
        Assert.True(values.EnumerateObject().Any(), $"[{context}] no values");
        foreach (var property in values.EnumerateObject())
        {
            AssertSpaEncoding(property.Name, property.Value, context);
        }

        foreach (var section in new[] { "wentWell", "watchOuts", "dataNotes" })
        {
            foreach (var item in summary.GetProperty(section).EnumerateArray())
            {
                AssertCited(item, values);
            }
        }

        AssertCited(summary.GetProperty("headline"), values);
        if (summary.GetProperty("suggestion").ValueKind == JsonValueKind.Object)
        {
            AssertCited(summary.GetProperty("suggestion"), values);
        }
    }

    private static void AssertCited(JsonElement text, JsonElement values)
    {
        var body = text.GetProperty("text").GetString()!;
        foreach (var key in text.GetProperty("keys").EnumerateArray().Select(k => k.GetString()!))
        {
            Assert.Contains("{" + key + "}", body, StringComparison.Ordinal);
            Assert.True(values.TryGetProperty(key, out _), $"cited key {key} has no value");
        }
    }

    [Fact]
    public async Task The_demo_summary_values_of_a_real_seeded_guest_follow_the_encodings_the_spa_decodes()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("sumenc-demo");
        var today = UserToday.Get(TimeProvider.System, "Europe/Warsaw");
        var (from, to, _) = DemoPresets.Range(DemoPresets.TwelveWeeksPrevious, today)!.Value;

        var body = await GetOkAsync(guest, $"?from={Iso(from)}&to={Iso(to)}&compare=previous");

        var summary = body.GetProperty("summary");
        Assert.Equal("demo", summary.GetProperty("source").GetString());
        AssertAllValues(summary, "demo");
        // The headline cites the half-marathon personal best: its race values must be the coded kinds.
        var values = summary.GetProperty("values");
        var units = values.EnumerateObject().Select(p => p.Value.GetProperty("unit").GetString()).ToHashSet();
        Assert.Contains("seconds", units);
        Assert.Contains("date", units);
    }

    [Fact]
    public async Task A_model_summary_values_follow_the_same_encodings_when_posted_and_when_read_back()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid();
        using var host = AiTestHost.Create(factory, client);
        var user = host.EntraUser("sumenc-model");
        await SeedRunsAsync(user);

        var posted = await PostOkAsync(user);
        AssertAllValues(posted.GetProperty("summary"), "model POST");
        var read = await GetOkAsync(user);
        AssertAllValues(read.GetProperty("summary"), "model GET");
    }

    [Theory]
    [InlineData("<script>alert(1)</script> {totals.runCount} runs")]
    [InlineData("<img src=x onerror=alert(1)> {totals.runCount}")]
    [InlineData("**Great** work: {totals.runCount} runs")]
    [InlineData("See https://evil.example/phish for {totals.runCount} runs")]
    [InlineData("Click [here](http://evil.example) {totals.runCount}")]
    [InlineData("![pixel](http://evil.example/p.png) {totals.runCount}")]
    [InlineData("javascript:alert(1) {totals.runCount}")]
    public async Task Model_output_with_html_markdown_or_links_is_refused_twice_and_never_stored_or_returned(string headline)
    {
        factory.SkipIfDockerUnavailable();
        var bad = JsonSerializer.Serialize(new { headline, wentWell = Array.Empty<object>(), watchOuts = Array.Empty<object>(), suggestion = (object?)null, dataNotes = Array.Empty<object>() });
        var client = new ScriptedChatClient().ReplyText(bad).ReplyText(bad);
        using var host = AiTestHost.Create(factory, client);
        var user = host.EntraUser("sumhtml-" + Math.Abs(headline.GetHashCode(StringComparison.Ordinal)));
        await SeedRunsAsync(user);

        var response = await PostAsync(user);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("evil.example", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await host.SummariesAsync(await user.GetUserIdAsync()));
        Assert.Equal("none", (await GetOkAsync(user)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task The_ledger_and_the_summary_tables_store_no_prompt_or_response_text_columns()
    {
        factory.SkipIfDockerUnavailable();

        var columns = await factory.WithDbAsync(db =>
        {
            var type = db.Model.FindEntityType(typeof(AiGeneration))!;
            return Task.FromResult(type.GetProperties().Select(p => (p.Name, Clr: p.ClrType, MaxLength: p.GetMaxLength())).ToList());
        });

        // A content column would be a string (or byte[]) of any real size; the ledger may only carry the short deployment name.
        foreach (var (name, clr, maxLength) in columns)
        {
            if (clr == typeof(string) || clr == typeof(byte[]))
            {
                Assert.True(name == nameof(AiGeneration.ModelDeployment) && maxLength is <= 64, $"AiGenerations.{name} could hold content");
            }
        }

        Assert.DoesNotContain(columns, c => c.Name.Contains("Prompt", StringComparison.Ordinal) && c.Clr != typeof(short));
        Assert.DoesNotContain(columns, c => c.Name.Contains("Response", StringComparison.OrdinalIgnoreCase) || c.Name.Contains("Content", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_guest_over_three_generations_cannot_get_a_fourth_even_for_a_different_period_or_regenerate()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid().ReplyValid().ReplyValid().ReplyValid();
        using var host = AiTestHost.Create(factory, client);
        var guest = await host.GuestAsync("sumg4");
        await SeedRunsAsync(guest);

        await PostOkAsync(guest);
        await PostOkAsync(guest, new { from = From, to = To, regenerate = true });
        await PostOkAsync(guest, new { from = From, to = To, regenerate = true });

        foreach (var body in new object[]
        {
            new { from = From, to = To, regenerate = true },
            new { from = From, to = "2025-03-12", regenerate = false }, // another key: a new generation
        })
        {
            var response = await PostAsync(guest, body);
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal("urn:runnersdiary:problem:ai-quota", (await response.ReadAsync()).ProblemType());
        }

        Assert.Equal(3, client.CallCount);
        Assert.Equal(3, (await host.LedgerAsync(guest.UserId!.Value)).Count);
    }
}
