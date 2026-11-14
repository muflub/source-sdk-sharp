using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's copy of the game-mode catalog (plan_bots_teams_tf2 D.2): its own
/// build before a server's arrives, the server's pieces joined, a level going
/// dropping them; and team play read from the playing mode, so team deathmatch
/// is team play before anyone joins a team.
/// </summary>
public class ClientModeCatalogTests
{
    private static ClientModeCatalog Catalog(string active)
        => new(DeclaredConVars.Holding<ClModeActiveConVar>(active), new RecordingLog());

    private static void Send(ClientModeCatalog catalog, IReadOnlyList<CatalogMode> modes)
    {
        var pieces = AdminMenuText.Pieces(ModeCatalogText.Write(modes));
        for (int i = 0; i < pieces.Count; i++)
            catalog.OnModeCatalogPiece(new ModeCatalogPiece { Index = (byte)i, Count = (byte)pieces.Count, Text = pieces[i] });
    }

    private static readonly CatalogMode Ctf = new(new ModeSpec("ctf", "#TF2_Gametype_Ctf", true, [CoreModeSpecs.TimeLimit()], ["item_teamflag"]),
                                                  new MapSupport(false, "dm_lockdown has no item_teamflag"));

    private static PlayerResourceNetState Roster(params int[] teams)
    {
        var resource = new ClientPlayerResource(new RecordingEngine());
        resource.State.BindLocal();
        for (int i = 0; i < teams.Length; i++)
        {
            resource.State.Flags[i + 1] = PlayerResourceNetState.FlagConnected;
            resource.State.Team[i + 1] = (byte)teams[i];
        }
        return resource.State;
    }

    [Fact]
    public void WithNoServersCopyItIsTheCoresOwnModes()
    {
        var catalog = Catalog("");

        Assert.False(catalog.FromServer);
        Assert.Equal(["null", "tdm"], catalog.Modes.Select(m => m.Spec.Id));
        Assert.Null(catalog.ActiveModeId);
        Assert.Null(catalog.ActiveMode);
    }

    [Fact]
    public void TheServersPiecesReplaceItAndALevelGoingDropsThem()
    {
        var catalog = Catalog("ctf");
        var modes = Enumerable.Range(0, 12).Select(i => Ctf with { Spec = Ctf.Spec with { Id = $"ctf{i}" } }).Append(Ctf).ToList();

        Send(catalog, modes);

        Assert.True(catalog.FromServer);
        Assert.Equal(13, catalog.Modes.Count);
        Assert.Equal("dm_lockdown has no item_teamflag", catalog.Modes[^1].OnThisMap.Reason);
        Assert.Equal("ctf", catalog.ActiveMode!.Id);

        ((IClientLevelHook)catalog).OnLevelShutdown();

        Assert.False(catalog.FromServer);
        Assert.Null(catalog.ActiveMode);
    }

    [Fact]
    public void TeamDeathmatchIsTeamPlayBeforeAnyoneJoinsATeam()
    {
        var catalog = Catalog("tdm");

        Assert.True(TeamPlay.Of(catalog, Roster(0, 0)));
    }

    [Fact]
    public void DeathmatchIsNotTeamPlayWhateverTheRosterSays()
    {
        var catalog = Catalog("null");

        Assert.False(TeamPlay.Of(catalog, Roster(2, 3)));
    }

    [Fact]
    public void AModeTheClientDoesNotKnowLeavesItToTheRoster()
    {
        var catalog = Catalog("arena");

        Assert.True(TeamPlay.Of(catalog, Roster(2, 0)));
        Assert.False(TeamPlay.Of(catalog, Roster(0, 0)));
        Assert.False(TeamPlay.Of(null, Roster(0, 0)));
    }
}
