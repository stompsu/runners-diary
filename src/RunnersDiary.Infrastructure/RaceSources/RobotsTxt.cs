using System.Text.RegularExpressions;

namespace RunnersDiary.Infrastructure.RaceSources;

/// <summary>
/// The RFC 9309 subset the importer honours (ADR 0035 §4): <c>User-agent</c> groups (our product token wins over <c>*</c>, groups of the same
/// agent are merged), <c>Allow</c>/<c>Disallow</c> with <c>*</c> and a trailing <c>$</c>, the longest matching rule wins and <c>Allow</c> wins a tie,
/// an empty <c>Disallow</c> allows everything. <c>Content-Signal</c> lines are collected for the log. Anything that is neither a comment, a blank
/// line nor a <c>name: value</c> line makes the file <see cref="TryParse">unparsable</see>, which aborts the source for the run.
/// </summary>
public sealed class RobotsTxt
{
    private readonly List<Rule> _rules;

    private RobotsTxt(List<Rule> rules, IReadOnlyList<string> contentSignals)
    {
        _rules = rules;
        ContentSignals = contentSignals;
    }

    /// <summary>A file that allows everything (a 4xx answer, RFC 9309 section 2.3.1.3).</summary>
    public static RobotsTxt AllowAll { get; } = new([], []);

    /// <summary>The <c>Content-Signal</c> values found in the file (logged; race facts never feed the AI anyway).</summary>
    public IReadOnlyList<string> ContentSignals { get; }

    /// <summary>Parses <paramref name="text"/>; false when it is not a robots.txt (for example an HTML error page served with status 200).</summary>
    public static bool TryParse(string text, string productToken, out RobotsTxt robots)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(productToken);

        var specific = new List<Rule>();
        var wildcard = new List<Rule>();
        var signals = new List<string>();
        var currentAgents = new List<string>();
        var groupHasRules = false;
        var specificGroupSeen = false;
        var understood = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine;
            var hash = line.IndexOf('#', StringComparison.Ordinal);
            if (hash >= 0)
            {
                line = line[..hash];
            }

            line = line.Trim().TrimStart('﻿');
            if (line.Length == 0)
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                robots = AllowAll;
                return false;
            }

            var name = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            switch (name)
            {
                case "user-agent":
                    if (groupHasRules)
                    {
                        currentAgents = [];
                        groupHasRules = false;
                    }

                    currentAgents.Add(value);
                    specificGroupSeen |= string.Equals(value, productToken, StringComparison.OrdinalIgnoreCase);
                    understood++;
                    break;
                case "allow":
                case "disallow":
                    groupHasRules = true;
                    understood++;
                    AddRule(name == "allow", value, currentAgents, productToken, specific, wildcard);
                    break;
                case "content-signal":
                    signals.Add(value.Length > 200 ? value[..200] : value);
                    understood++;
                    break;
                default:
                    // sitemap, crawl-delay, host and unknown extensions are ignored (RFC 9309 section 2.2).
                    understood++;
                    break;
            }
        }

        if (understood == 0 && text.Trim().Length > 0)
        {
            robots = AllowAll;
            return false;
        }

        // A group naming our product token replaces the * group entirely, even when it only has an empty Disallow.
        robots = new RobotsTxt(specificGroupSeen ? specific : wildcard, signals);
        return true;
    }

    /// <summary>Whether <paramref name="pathAndQuery"/> (starting with <c>/</c>) may be fetched.</summary>
    public bool IsAllowed(string pathAndQuery)
    {
        ArgumentNullException.ThrowIfNull(pathAndQuery);
        Rule? best = null;
        foreach (var rule in _rules)
        {
            if (!rule.Matches(pathAndQuery))
            {
                continue;
            }

            if (best is null || rule.Length > best.Length || (rule.Length == best.Length && rule.Allow && !best.Allow))
            {
                best = rule;
            }
        }

        return best is null || best.Allow;
    }

    private static void AddRule(bool allow, string value, List<string> agents, string productToken, List<Rule> specific, List<Rule> wildcard)
    {
        // An empty Disallow means "allow all" and an empty Allow means nothing: neither contributes a rule.
        if (value.Length == 0)
        {
            return;
        }

        var rule = new Rule(allow, value);
        foreach (var agent in agents)
        {
            if (string.Equals(agent, productToken, StringComparison.OrdinalIgnoreCase))
            {
                specific.Add(rule);
            }
            else if (agent == "*")
            {
                wildcard.Add(rule);
            }
        }
    }

    private sealed class Rule
    {
        private readonly Regex _pattern;

        public Rule(bool allow, string path)
        {
            Allow = allow;
            Length = path.Length;
            var anchored = path.EndsWith('$');
            var body = anchored ? path[..^1] : path;
            var regex = "^" + Regex.Escape(body).Replace("\\*", ".*", StringComparison.Ordinal) + (anchored ? "$" : string.Empty);
            // Linear-time matching only: the rule text comes from a third party.
            _pattern = new Regex(regex, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        }

        public bool Allow { get; }

        public int Length { get; }

        public bool Matches(string path) => _pattern.IsMatch(path);
    }
}
