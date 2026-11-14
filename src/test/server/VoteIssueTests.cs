using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The vote issues as a chain (plan_bots_teams_tf2 D.3): the list players get is
/// every issue's choices in chain order, a call's refusal is asked of the issue
/// of its kind, and the core's issues offer what their convars allow.
/// </summary>
public class VoteIssueTests
{
    internal sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Fixed(VoteKind kind, string? refusal, params string[] ids) : IVoteIssue
    {
        public VoteKind Kind => kind;
        public IEnumerable<VoteOption> Choices() => ids.Select(id => new VoteOption(kind, id, id, "say " + id));
        public string? Refusal(VoteOption choice) => refusal;
    }

    private sealed class Level : ILevelInfo
    {
        public string? Map => "dm_lockdown";
        public bool IsReady => true;
    }

    [Fact]
    public void TheListIsEveryIssuesChoicesInChainOrder()
    {
        var list = new VoteIssueList(new ListHooks<IVoteIssue>(new Fixed(VoteKind.Map, null, "dm_a", "dm_b"), new Fixed(VoteKind.Mode, null, "tdm")));

        Assert.Equal(["dm_a", "dm_b", "tdm"], list.Build().Select(o => o.Id));
    }

    [Fact]
    public void ARefusalIsAskedOfTheIssueOfTheChoicesKind()
    {
        var list = new VoteIssueList(new ListHooks<IVoteIssue>(new Fixed(VoteKind.Map, null, "dm_a"), new Fixed(VoteKind.Mode, "not here", "ctf")));
        var options = list.Build();

        Assert.Null(list.Refusal(options[0]));
        Assert.Equal("not here", list.Refusal(options[1]));
    }

    private static (TeamDeathmatchTests.Rig Rig, ModeVoteIssue Issue) ModeIssue(bool allowed)
    {
        var rig = new TeamDeathmatchTests.Rig();
        var catalog = new GameModeCatalog(rig.Modes, new BspEntityClasses(new GameModeCatalogTests.Bsps()), new Level());
        var convar = new ConVarRegistry(new NamedConVars(new Dictionary<string, string> { ["sv_vote_issue_mode_allowed"] = allowed ? "1" : "0" }))
            .Declare<VoteIssueModeAllowedConVar>();
        return (rig, new ModeVoteIssue(rig.Modes, catalog, convar));
    }

    [Fact]
    public void TheModeIssueOffersTheOtherModesByTheirTitles()
    {
        var (rig, issue) = ModeIssue(allowed: true);

        var tdm = Assert.Single(issue.Choices());
        Assert.Equal((VoteKind.Mode, "tdm", "#SS_UI_Mode_TeamDeathmatch", "sv_mode_next tdm; sv_mode tdm"), (tdm.Kind, tdm.Id, tdm.Label, tdm.Command));

        rig.Tdm();
        Assert.Equal(["null"], issue.Choices().Select(c => c.Id));
    }

    [Fact]
    public void TheModeIssueOffersNothingWhenTheServerDisallowsIt()
        => Assert.Empty(ModeIssue(allowed: false).Issue.Choices());
}
