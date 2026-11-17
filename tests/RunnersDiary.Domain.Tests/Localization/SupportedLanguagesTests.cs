using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Localization;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Users;

namespace RunnersDiary.Domain.Tests.Localization;

public sealed class SupportedLanguagesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_supported_list_is_en_and_pl()
    {
        Assert.Equal(["en", "pl"], SupportedLanguages.All);
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("pl", true)]
    [InlineData("PL", false)]
    [InlineData("pl-PL", false)]
    [InlineData(" pl", false)]
    [InlineData("de", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupported_is_an_exact_allowlist(string? tag, bool expected) =>
        Assert.Equal(expected, SupportedLanguages.IsSupported(tag));

    [Theory]
    [InlineData("pl", "pl")]
    [InlineData("en", "en")]
    [InlineData("de", "en")]
    [InlineData(null, "en")]
    public void OrDefault_falls_back_to_english(string? tag, string expected) =>
        Assert.Equal(expected, SupportedLanguages.OrDefault(tag));

    [Fact]
    public void A_new_user_has_no_language_and_SetLanguage_stores_a_supported_one()
    {
        var user = User.CreateEntra(Guid.NewGuid(), Guid.NewGuid(), "Ada", Now);
        Assert.Null(user.Language);

        user.SetLanguage("pl");

        Assert.Equal("pl", user.Language);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("PL")]
    [InlineData("")]
    public void SetLanguage_rejects_unsupported_tags_with_a_language_field_error(string tag)
    {
        var user = User.CreateGuest(Now);

        var ex = Assert.Throws<DomainRuleException>(() => user.SetLanguage(tag));

        Assert.Contains(ex.Errors, e => e.Path == "language");
        Assert.Null(user.Language);
    }

    [Fact]
    public void A_guest_can_be_created_with_a_language_but_not_an_unsupported_one()
    {
        Assert.Equal("pl", User.CreateGuest(Now, language: "pl").Language);
        Assert.Null(User.CreateGuest(Now).Language);
        Assert.Throws<DomainRuleException>(() => User.CreateGuest(Now, language: "xx"));
    }

    [Fact]
    public void RecordPlan_stores_the_plan_language_and_rejects_unsupported_ones()
    {
        var today = new DateOnly(2026, 11, 17);
        var goal = Goal.Create(
            Guid.NewGuid(),
            new GoalValues(GoalType.Performance, "10K", today.AddDays(60), 10_000, null, 3_000, Surface.Road, null, null, null, null, GoalStatus.Active),
            today, null, Now);
        var plan = new GoalPlanRecord(Guid.NewGuid(), Now, "Notes", 1, "gpt", "m", today.AddDays(50), false);

        goal.RecordPlan(plan, Now);
        Assert.Null(goal.PlanLanguage);

        goal.RecordPlan(plan with { Language = "pl" }, Now);
        Assert.Equal("pl", goal.PlanLanguage);

        Assert.Throws<DomainRuleException>(() => goal.RecordPlan(plan with { Language = "xx" }, Now));
    }
}
