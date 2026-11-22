using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The admin menu's folders (plan_bots_teams_tf2 D.4): the maps by candidate
/// and the next map, the modes with the ones this map cannot play shown
/// unavailable, each player's folder, and the votes an admin calls as the
/// server or cancels.
/// </summary>
public class AdminMenuFoldersTests
{
    private static readonly MapCandidate[] Candidates = [new("dm_a", [], true), new("ctf_b", ["ctf"], false)];

    [Fact]
    public void EachCandidateChangesToItsMapByItsLabel()
    {
        var ran = new List<string>();
        var maps = AdminMenuBuilder.Maps(Candidates, ran.Add);

        Assert.Equal(["dm_a", "ctf_b (ctf) [no nav]", "#SS_UI_Admin_NextMap"], maps.Select(n => n.Label));
        maps[1].Run!();
        Assert.Equal(["changelevel ctf_b"], ran);
    }

    [Fact]
    public void TheNextMapFolderSetsNextlevel()
    {
        var ran = new List<string>();
        var next = AdminMenuBuilder.Maps(Candidates, ran.Add)[^1];

        AdminNode.Find([next], "nextmap:dm_a")!.Run!();

        Assert.Equal(["nextlevel dm_a"], ran);
    }

    [Fact]
    public void NoCandidatesNoNextMapFolder()
        => Assert.Empty(AdminMenuBuilder.Maps([], _ => { }));

    private static readonly ModeSpec[] Specs =
    [
        new(GameModes.NullModeId, "#SS_UI_Mode_Deathmatch", false, [], []),
        new("ctf", "ctf", true, [], ["item_teamflag"]),
    ];

    [Fact]
    public void AModeThisMapCannotPlayIsUnavailableWithTheReason()
    {
        var modes = AdminMenuBuilder.Modes(Specs, id => id == "ctf" ? new MapSupport(false, "dm_a has no item_teamflag") : new MapSupport(true, ""), _ => { });

        Assert.Null(modes[0].Unavailable);
        Assert.Equal("dm_a has no item_teamflag", modes[1].Unavailable);
    }

    [Fact]
    public void TakingAModeSwitchesToIt()
    {
        var switched = new List<string>();
        var modes = AdminMenuBuilder.Modes(Specs, _ => new MapSupport(true, ""), switched.Add);

        Assert.Null(modes[1].Take());

        Assert.Equal(["ctf"], switched);
    }

    [Fact]
    public void AnUnavailableModeSwitchesNothing()
    {
        var switched = new List<string>();
        var modes = AdminMenuBuilder.Modes(Specs, _ => new MapSupport(false, "no"), switched.Add);

        Assert.Equal("no", modes[1].Take());

        Assert.Empty(switched);
    }

    private static readonly AdminMenuBuilder.AdminPlayer Host = new(1, new EntityHandle(0x10001u), 2, "Host", false);
    private static readonly AdminMenuBuilder.AdminPlayer Alice = new(3, new EntityHandle(0x10003u), 7, "Alice", false);
    private static readonly AdminMenuBuilder.AdminPlayer Watcher = new(4, new EntityHandle(0x10004u), 9, "Watcher", true);

    private sealed class Moves : IAdminPlayerActions
    {
        public IEnumerable<AdminNode> ActionsFor(EntityHandle player, int userId) => [new("#Move", $"move:{userId}", () => { })];
    }

    private static List<AdminNode> Players(int self, bool dedicated = false, IAdminPlayerActions[]? actions = null,
                                           List<string>? done = null)
        => AdminMenuBuilder.Players([Host, Alice, Watcher], self, dedicated, actions ?? [],
                                    p => done?.Add("kick " + p.Name), p => done?.Add("spectate " + p.Name));

    private static string[] Ids(AdminNode folder) => [.. folder.Children!.Select(c => c.Id)];

    [Fact]
    public void EachPlayerHasAFolderByName()
        => Assert.Equal(["Host", "Alice", "Watcher"], Players(self: 1).Select(f => f.Label));

    [Fact]
    public void AnAdminCannotKickThemselvesNorAListenServersHost()
    {
        var folders = Players(self: 3);

        Assert.Equal(["players:spectate:2"], Ids(folders[0]));
        Assert.Equal(["players:spectate:7"], Ids(folders[1]));
        Assert.Equal(["kick:9"], Ids(folders[2]));
    }

    [Fact]
    public void OnADedicatedServerSlotOneIsKickable()
        => Assert.Equal(["kick:2", "players:spectate:2"], Ids(Players(self: 3, dedicated: true)[0]));

    [Fact]
    public void KickAndSpectateActOnThatPlayer()
    {
        var done = new List<string>();
        var folders = Players(self: 1, done: done);

        AdminNode.Find(folders, "kick:7")!.Run!();
        AdminNode.Find(folders, "players:spectate:7")!.Run!();

        Assert.Equal(["kick Alice", "spectate Alice"], done);
    }

    [Fact]
    public void AModesActionsJoinEachPlayersFolder()
        => Assert.Equal(["kick:7", "players:spectate:7", "move:7"], Ids(Players(self: 1, actions: [new Moves()])[1]));

    private static readonly VoteOption[] Choices =
    [
        new(VoteKind.Map, "dm_a", "dm_a", "changelevel dm_a"),
        new(VoteKind.Map, "ctf_b", "ctf_b", "changelevel ctf_b"),
        new(VoteKind.Mode, "ctf", "ctf", "sv_mode ctf"),
        new(VoteKind.Restart, "restart", "", "mp_restartgame 1"),
        new(VoteKind.Setting, "mp_friendlyfire=1", "#SS_UI_On", "ss_setting mp_friendlyfire 1"),
    ];

    [Fact]
    public void TheCallFolderGroupsTheIssuesByKind()
    {
        var call = AdminMenuBuilder.Votes(Choices, _ => null, _ => null, () => null, running: false)[0];

        Assert.Equal("#SS_UI_Admin_VoteCall", call.Label);
        Assert.Equal(["#SS_UI_Vote_ChangeMap", "#SS_UI_Vote_Mode", "#SS_UI_Vote_Restart", CoreModeSpecs.SettingToken("mp_friendlyfire")],
                     call.Children!.Select(c => c.Label));
        Assert.Equal("vote:restart:restart", call.Children![2].Id);
    }

    [Fact]
    public void ACallIsTheServersAndARefusedOneIsUnavailable()
    {
        var called = new List<VoteOption>();
        var votes = AdminMenuBuilder.Votes(Choices, o => o.Kind == VoteKind.Mode ? "dm_a has no item_teamflag" : null,
                                           o => { called.Add(o); return null; }, () => null, running: false);

        Assert.Null(AdminNode.Find(votes, "vote:map:ctf_b")!.Take());
        Assert.Equal("dm_a has no item_teamflag", AdminNode.Find(votes, "vote:mode:ctf")!.Take());

        Assert.Equal(["ctf_b"], called.Select(c => c.Id));
    }

    [Fact]
    public void CancelIsUnavailableWithNoVoteRunning()
    {
        int cancelled = 0;
        var idle = AdminMenuBuilder.Votes(Choices, _ => null, _ => null, () => { cancelled++; return null; }, running: false)[1];
        var busy = AdminMenuBuilder.Votes(Choices, _ => null, _ => null, () => { cancelled++; return null; }, running: true)[1];

        Assert.Equal("no vote is running", idle.Take());
        Assert.Null(busy.Take());

        Assert.Equal(1, cancelled);
    }

    private sealed class Hides(params string[] hidden) : IAdminMenuScope
    {
        public bool Offers(string folder) => !hidden.Contains(folder);
    }

    private static List<(string Key, Func<AdminNode> Make)> CoreSix(List<string> built)
        => [.. AdminCoreFolders.All.Select(key => (key, (Func<AdminNode>)(() => { built.Add(key); return new AdminNode(key, key, () => { }); })))];

    [Fact]
    public void WithNoScopeEveryCoreFolderIsShownInOrder()
    {
        var built = new List<string>();

        var shown = AdminMenuBuilder.CoreFolders(CoreSix(built), []);

        Assert.Equal(["map", "mode", "players", "settings", "votes", "restart"], shown.Select(n => n.Label));
    }

    [Fact]
    public void AFolderAScopeDoesNotOfferIsLeftOutAndNeverBuilt()
    {
        var built = new List<string>();

        var shown = AdminMenuBuilder.CoreFolders(CoreSix(built), [new Hides(AdminCoreFolders.Mode, AdminCoreFolders.Restart)]);

        Assert.Equal(["map", "players", "settings", "votes"], shown.Select(n => n.Label));
        Assert.Equal(["map", "players", "settings", "votes"], built);
    }

    [Fact]
    public void AFolderIsShownOnlyWhileEveryScopeOffersIt()
    {
        var shown = AdminMenuBuilder.CoreFolders(CoreSix([]), [new Hides(AdminCoreFolders.Map), new Hides(AdminCoreFolders.Votes)]);

        Assert.Equal(["mode", "players", "settings", "restart"], shown.Select(n => n.Label));
    }
}
