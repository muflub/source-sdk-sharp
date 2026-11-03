/*
 * A TEAM'S NAME IS THE MOD'S.
 *
 * The core names teams as HL2MP does (Combine, Rebels); a mod folds its own
 * through ITeamNameRule (HookOrder, ties refused) on each side. No rule: the
 * caller's own name, which is every fact below's control arm.
 */

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class TeamNamesTests
{
    private sealed class Names(int order, params (int Team, string Name)[] names) : ITeamNameRule, IOrderedHook
    {
        public int HookOrder => order;

        public string Name(int team, string name)
        {
            foreach (var (t, n) in names)
                if (t == team)
                    return n;
            return name;
        }
    }

    private sealed class Suffix(int order, string suffix) : ITeamNameRule, IOrderedHook
    {
        public int HookOrder => order;
        public string Name(int team, string name) => name + suffix;
    }

    private sealed class Throws(int order) : ITeamNameRule, IOrderedHook
    {
        public int HookOrder => order;
        public string Name(int team, string name) => throw new InvalidOperationException("broken rule");
    }

    private sealed class AnswersNull(int order) : ITeamNameRule, IOrderedHook
    {
        public int HookOrder => order;
        public string Name(int team, string name) => null!;
    }

    private static TeamNames Over(RecordingLog log, params ITeamNameRule[] rules)
    {
        var container = new ServiceContainer();
        foreach (var rule in rules)
            container.AddInstance(rule.GetType(), rule);
        return new TeamNames(new Hooks<ITeamNameRule>(container, log), log);
    }

    [Fact]
    public void With_no_rules_a_team_keeps_the_callers_name()
    {
        Assert.Equal("Combine", TeamNames.Stock().Of(2, "Combine"));
        Assert.Equal("Combine", Over(new RecordingLog()).Of(2, "Combine"));
    }

    [Fact]
    public void A_rule_names_the_teams_it_knows_and_passes_the_rest()
    {
        var names = Over(new RecordingLog(), new Names(1, (2, "Red"), (3, "Blue")));

        Assert.Equal("Red", names.Of(2, "Combine"));
        Assert.Equal("Blue", names.Of(3, "Rebels"));
        Assert.Equal("Spectator", names.Of(1, "Spectator"));
    }

    [Fact]
    public void Rules_fold_in_hook_order()
    {
        var names = Over(new RecordingLog(), new Suffix(2, "!"), new Names(1, (2, "Red")));

        Assert.Equal("Red!", names.Of(2, "Combine"));
    }

    [Fact]
    public void A_rule_that_throws_is_named_and_the_name_before_it_stands()
    {
        var log = new RecordingLog();
        var names = Over(log, new Names(1, (2, "Red")), new Throws(2));

        Assert.Equal("Red", names.Of(2, "Combine"));
        Assert.Contains(log.Lines, l => l.Text.Contains("team name rule") && l.Text.Contains("broken rule"));
    }

    [Fact]
    public void A_null_answer_is_no_opinion()
    {
        var names = Over(new RecordingLog(), new AnswersNull(1));

        Assert.Equal("Combine", names.Of(2, "Combine"));
    }
}
