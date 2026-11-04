using RunnersDiary.Domain.Insights.Narrative;

namespace RunnersDiary.Ai.Tests.Validation;

/// <summary>The strict reader of the model output: exactly the schema's shape or nothing.</summary>
public sealed class SummaryDraftJsonTests
{
    private const string Valid = """
        {"headline":"H","wentWell":[{"text":"a {x}"}],"watchOuts":[],"suggestion":{"text":"s"},"dataNotes":[]}
        """;

    [Fact]
    public void Valid_shape_round_trips()
    {
        var draft = SummaryDraftJson.Parse(Valid);
        Assert.Equal("H", draft.Headline);
        Assert.Equal("a {x}", draft.WentWell.Single().Text);
        Assert.Equal("s", draft.Suggestion!.Text);
        Assert.Equal(SummaryDraftJson.ToJson(draft), SummaryDraftJson.ToJson(SummaryDraftJson.Parse(SummaryDraftJson.ToJson(draft))));
    }

    [Fact]
    public void Null_suggestion_is_allowed() =>
        Assert.Null(SummaryDraftJson.Parse("""{"headline":"H","wentWell":[],"watchOuts":[],"suggestion":null,"dataNotes":[]}""").Suggestion);

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("""{"headline":"H","wentWell":[],"watchOuts":[],"suggestion":null}""")]
    [InlineData("""{"headline":"H","wentWell":[],"watchOuts":[],"suggestion":null,"dataNotes":[],"extra":1}""")]
    [InlineData("""{"headline":"H","headline":"I","wentWell":[],"watchOuts":[],"suggestion":null,"dataNotes":[]}""")]
    [InlineData("""{"headline":1,"wentWell":[],"watchOuts":[],"suggestion":null,"dataNotes":[]}""")]
    [InlineData("""{"headline":"H","wentWell":["a"],"watchOuts":[],"suggestion":null,"dataNotes":[]}""")]
    [InlineData("""{"headline":"H","wentWell":[{"text":"a","keys":[]}],"watchOuts":[],"suggestion":null,"dataNotes":[]}""")]
    [InlineData("""{"headline":"H","wentWell":[],"watchOuts":[],"suggestion":"s","dataNotes":[]}""")]
    [InlineData("""{"headline":"H","wentWell":null,"watchOuts":[],"suggestion":null,"dataNotes":[]}""")]
    [InlineData("""```json {"headline":"H"} ```""")]
    public void Anything_else_is_rejected(string json) => Assert.Null(SummaryDraftJson.TryParse(json));
}
