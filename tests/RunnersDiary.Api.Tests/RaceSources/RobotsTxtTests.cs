using RunnersDiary.Infrastructure.RaceSources;

namespace RunnersDiary.Api.Tests.RaceSources;

/// <summary>RFC 9309 subset (ADR 0035 §4).</summary>
public sealed class RobotsTxtTests
{
    private static RobotsTxt Parse(string text)
    {
        Assert.True(RobotsTxt.TryParse(text, RaceImportOptions.ProductToken, out var robots));
        return robots;
    }

    [Fact]
    public void The_allow_fixture_blocks_only_the_map_paths_and_the_longest_match_wins()
    {
        var robots = Parse(RaceSourceTestSupport.Fixture("robots-allow.txt"));

        Assert.True(robots.IsAllowed("/pl/wydarzenie/bieg-testowy"));
        Assert.True(robots.IsAllowed("/kalendarz-biegow/polska?page=2"));
        Assert.False(robots.IsAllowed("/pl/map/"));
        Assert.False(robots.IsAllowed("/demo-live"));
        Assert.True(robots.IsAllowed("/pl/map/public/x")); // Allow /pl/map/public is longer than Disallow /pl/map/
        Assert.Equal(["ai-train=no, search=yes"], robots.ContentSignals);
    }

    [Fact]
    public void A_group_for_our_product_token_replaces_the_wildcard_group()
    {
        var robots = Parse(RaceSourceTestSupport.Fixture("robots-disallow.txt"));

        Assert.False(robots.IsAllowed("/pl/wydarzenie/bieg-testowy"));
        Assert.True(robots.IsAllowed("/kalendarz-biegow/polska?page=1"));
        Assert.True(robots.IsAllowed("/pl/map/"), "The * group (Disallow /pl/map/) does not apply once our own group exists.");
    }

    [Fact]
    public void The_product_token_is_matched_case_insensitively_and_an_empty_disallow_allows_everything()
    {
        var robots = Parse("User-agent: *\nDisallow: /\n\nUser-agent: runnersdiaryraceimport\nDisallow:\n");

        Assert.True(robots.IsAllowed("/anything"));
    }

    [Fact]
    public void Wildcards_and_end_anchors_work_and_a_tie_goes_to_allow()
    {
        var robots = Parse("User-agent: *\nDisallow: /*.json$\nDisallow: /private\nAllow: /private\n");

        Assert.False(robots.IsAllowed("/data/events.json"));
        Assert.True(robots.IsAllowed("/data/events.json?x=1"));
        Assert.True(robots.IsAllowed("/private/page"), "Equal length: Allow wins.");
    }

    [Fact]
    public void Consecutive_user_agent_lines_share_their_rules_and_unknown_directives_are_ignored()
    {
        var robots = Parse("User-agent: other\nUser-agent: RunnersDiaryRaceImport\nCrawl-delay: 10\nDisallow: /x/\n");

        Assert.False(robots.IsAllowed("/x/y"));
        Assert.True(robots.IsAllowed("/z"));
    }

    [Fact]
    public void Comments_blank_lines_and_a_bom_are_tolerated_and_an_empty_file_allows_everything()
    {
        var robots = Parse("﻿# hello\n\nUser-agent: * # everyone\nDisallow: /a # why\n");

        Assert.False(robots.IsAllowed("/a/b"));
        Assert.True(RobotsTxt.TryParse(string.Empty, RaceImportOptions.ProductToken, out var empty));
        Assert.True(empty.IsAllowed("/a"));
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html><body>Not found</body></html>")]
    [InlineData("just some words without a colon")]
    public void A_file_that_is_not_a_robots_file_is_unparsable(string text)
    {
        Assert.False(RobotsTxt.TryParse(text, RaceImportOptions.ProductToken, out _));
    }

    [Fact]
    public void A_hostile_pattern_cannot_cause_catastrophic_matching()
    {
        var robots = Parse("User-agent: *\nDisallow: /*a*a*a*a*a*a*a*a*a*a*a*b\n");

        var path = "/" + new string('a', 5000);
        Assert.True(robots.IsAllowed(path));
    }
}
