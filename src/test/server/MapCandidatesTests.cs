using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The maps a vote or the admin menu offers (plan_bots_teams_tf2 D.3): the map
/// cycle's installed maps, each with the modes its entities play (unknown when
/// its lump cannot be read) and its nav state, and the modes a kept mode keeps.
/// </summary>
public class MapCandidatesTests
{
    private sealed class Level : ILevelInfo
    {
        public string? Map => "dm_lockdown";
        public bool IsReady => true;
    }

    private sealed class FlagMode(IRoundFlow rounds) : IGameMode
    {
        public string Id => "ctf";
        public IRoundFlow Rounds => rounds;
        public IModeSettings Settings => new ModeSettings("#CTF", [], ["item_teamflag"]);
    }

    private static T Declare<T>(string name, string value) where T : ConVarBase
        => new ConVarRegistry(new NamedConVars(new Dictionary<string, string> { [name] = value })).Declare<T>();

    /// <summary>A bsp whose entity lump is compressed, as a TF2 map's: a fourCC of its uncompressed size.</summary>
    private static byte[] CompressedBsp()
    {
        byte[] bsp = GameModeCatalogTests.Bsp("item_teamflag");
        BitConverter.GetBytes(4096).CopyTo(bsp, 20);
        return bsp;
    }

    /// <summary>
    /// The cycle a, ctf_x, gone (not installed), tf_lzma, a again; ctf_x has a
    /// flag, tf_lzma a compressed lump, only a has a nav; a flag mode registered.
    /// </summary>
    private static (MapCandidates Candidates, GameModeCatalogTests.Bsps Files) Rig(string modeNext = "", bool flagMode = true)
    {
        var rig = new TeamDeathmatchTests.Rig();
        if (flagMode)
            rig.Services.AddInstance(new FlagMode(rig.Rules));
        var files = new GameModeCatalogTests.Bsps();
        files.Files["cfg/mapcycle.txt"] = Encoding.UTF8.GetBytes("a\nctf_x\ngone\ntf_lzma\na\n");
        files.Files["maps/a.bsp"] = GameModeCatalogTests.Bsp("info_player_deathmatch");
        files.Files["maps/a.nav3d"] = [1];
        files.Files["maps/ctf_x.bsp"] = GameModeCatalogTests.Bsp("item_teamflag");
        files.Files["maps/tf_lzma.bsp"] = CompressedBsp();
        var catalog = new GameModeCatalog(rig.Modes, new BspEntityClasses(files), new Level());
        var candidates = new MapCandidates(files, Declare<MapCycleFileConVar>("mapcyclefile", "mapcycle.txt"), catalog,
                                           Declare<SvModeNextConVar>("sv_mode_next", modeNext));
        return (candidates, files);
    }

    [Fact]
    public void ACandidateIsAnInstalledMapOfTheCycleOnceInCycleOrder()
        => Assert.Equal(["a", "ctf_x", "tf_lzma"], Rig().Candidates.All().Select(c => c.Map));

    [Fact]
    public void ACandidateNamesTheModesItsEntitiesPlayAndNoneWhenTheyCannotBeRead()
    {
        var all = Rig().Candidates.All().ToDictionary(c => c.Map);

        Assert.Empty(all["a"].Modes!);
        Assert.Equal(["ctf"], all["ctf_x"].Modes);
        Assert.Null(all["tf_lzma"].Modes);
    }

    [Fact]
    public void TheLabelShowsTheKnownModesAndAMapWithNoNav()
        => Assert.Equal(["a", "ctf_x (ctf) [no nav]", "tf_lzma [no nav]"], Rig().Candidates.All().Select(c => c.Label));

    [Fact]
    public void WithOnlyModesThatPlayAnywhereNoBspIsRead()
    {
        var (candidates, files) = Rig(flagMode: false);

        Assert.Equal(3, candidates.All().Count);
        Assert.Empty(files.Reads);
    }

    [Fact]
    public void AKeptModeDropsAMapKnownNotToPlayItAndKeepsOneWhoseEntitiesAreUnknown()
        => Assert.Equal(["ctf_x", "tf_lzma"], Rig(modeNext: "ctf").Candidates.Votable().Select(c => c.Map));

    [Fact]
    public void AKeptModeThatPlaysAnywhereKeepsEveryMap()
        => Assert.Equal(["a", "ctf_x", "tf_lzma"], Rig(modeNext: "tdm").Candidates.Votable().Select(c => c.Map));

    [Fact]
    public void TheChangeLevelIssueOffersTheCandidatesByLabel()
    {
        var issue = new ChangeLevelVoteIssue(Rig().Candidates, Declare<VoteIssueChangeLevelAllowedConVar>("sv_vote_issue_changelevel_allowed", "1"));

        var choices = issue.Choices().ToList();

        Assert.Equal(["a", "ctf_x", "tf_lzma"], choices.Select(c => c.Id));
        Assert.Equal("ctf_x (ctf) [no nav]", choices[1].Label);
        Assert.Equal("changelevel ctf_x", choices[1].Command);
    }

    [Fact]
    public void TheNextMapIssueOffersTheCandidatesAndTheirChoice()
    {
        var issue = new NextLevelVoteIssue(Rig().Candidates, Declare<NextLevelConVar>("nextlevel", ""),
                                           Declare<VoteIssueNextLevelAllowedConVar>("sv_vote_issue_nextlevel_allowed", "1"),
                                           Declare<VoteIssueNextLevelChoicesModeConVar>("sv_vote_issue_nextlevel_choicesmode", "1"));

        var choices = issue.Choices().ToList();

        Assert.Equal(["a", "ctf_x", "tf_lzma", "nextmap"], choices.Select(c => c.Id));
        Assert.Equal(["a", "ctf_x", "tf_lzma"], choices[^1].Ballot!.Select(b => b.Label));
        Assert.All(choices, c => Assert.Equal(NextLevelVoteIssue.ChoicesQuorum, c.Quorum));
    }

    [Fact]
    public void WithoutChoicesModeTheNextMapIssueKeepsTheServersQuorum()
    {
        var issue = new NextLevelVoteIssue(Rig().Candidates, Declare<NextLevelConVar>("nextlevel", ""),
                                           Declare<VoteIssueNextLevelAllowedConVar>("sv_vote_issue_nextlevel_allowed", "1"),
                                           Declare<VoteIssueNextLevelChoicesModeConVar>("sv_vote_issue_nextlevel_choicesmode", "0"));

        Assert.All(issue.Choices(), c => Assert.Null(c.Quorum));
    }
}
