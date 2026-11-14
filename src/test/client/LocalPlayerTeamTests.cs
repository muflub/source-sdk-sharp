using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The local player's team as the HUD's team line reads it (<see cref="LocalPlayerUi"/>
/// as IPlayerTeam): the local slot's team off the player resource, and team play
/// read from the roster (<see cref="TeamPlay"/>): one connected player on a playing
/// team (2 or 3) is team play; everyone on 0 is deathmatch.
/// </summary>
public class LocalPlayerTeamTests
{
    private sealed class Standalones : IClientStandalones
    {
        public readonly List<ClientStandaloneEntity> All = new();

        public IReadOnlyList<ClientStandaloneEntity> Live() => All;

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    private static (Standalones Standalones, ClientPlayerResource Resource) Roster(params (int Slot, int Team)[] players)
    {
        var resource = new ClientPlayerResource(new RecordingEngine());
        resource.State.BindLocal();
        foreach (var (slot, team) in players)
        {
            resource.State.Flags[slot] = PlayerResourceNetState.FlagConnected | PlayerResourceNetState.FlagAlive;
            resource.State.Team[slot] = (byte)team;
        }
        var standalones = new Standalones();
        standalones.All.Add(resource);
        return (standalones, resource);
    }

    private static LocalPlayerUi Player(IClientStandalones standalones, int local = 1, TeamNames? names = null)
    {
        var engine = Fake().Returns("Hud.LocalPlayerHealth", 80).Returns("Hud.LocalPlayerIndex", local);
        var hud = new Hud(new RecordingLog(), engine, engine, new DamageMessage(), new HudMsgMessage(),
                          new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());
        return new LocalPlayerUi(engine, new UiHostKit.NoArsenal(), hud, standalones, teamNames: names);
    }

    [Fact]
    public void APlayerOnAPlayingTeamMakesItTeamPlay()
    {
        var (standalones, _) = Roster((1, 2), (2, 3));
        var player = Player(standalones);

        player.Refresh();

        Assert.True(player.Teamplay);
    }

    [Fact]
    public void EveryoneOnTeamZeroIsDeathmatch()
    {
        var (standalones, _) = Roster((1, 0), (2, 0));
        var player = Player(standalones);

        player.Refresh();

        Assert.False(player.Teamplay);
    }

    [Fact]
    public void SpectatorsAloneAreNotTeamPlay()
    {
        var (standalones, _) = Roster((1, 1), (2, 0));
        var player = Player(standalones);

        player.Refresh();

        Assert.False(player.Teamplay);
    }

    [Fact]
    public void ADisconnectedSlotsTeamSaysNothing()
    {
        var (standalones, resource) = Roster((1, 0));
        resource.State.Team[5] = 2;     // a stale team on a slot nobody holds
        var player = Player(standalones);

        player.Refresh();

        Assert.False(player.Teamplay);
    }

    [Fact]
    public void TheTeamIsTheLocalSlotsWithItsName()
    {
        var (standalones, _) = Roster((1, 3), (2, 2));
        var player = Player(standalones, local: 1);

        player.Refresh();

        Assert.Equal((3, "Rebels"), (player.Team, player.TeamName));
    }

    [Fact]
    public void ATeamChangeIsReadOnTheNextRefresh()
    {
        var (standalones, resource) = Roster((1, 3), (2, 2));
        var player = Player(standalones, local: 1);
        player.Refresh();
        string before = player.TeamName;

        resource.State.Team[1] = 2;
        player.Refresh();

        Assert.Equal(("Rebels", 2, "Combine"), (before, player.Team, player.TeamName));
    }

    [Fact]
    public void WithNoResourceThereIsNoTeam()
    {
        var player = Player(new Standalones());

        player.Refresh();

        Assert.Equal((false, 0), (player.Teamplay, player.Team));
    }

    [Theory]
    [InlineData(4, "Metro Police")]
    [InlineData(5, "Resistance")]
    public void TeamDeathmatchsThirdAndFourthTeamsHaveTheirNames(int team, string name)
    {
        var (standalones, _) = Roster((1, team), (2, 2));
        var player = Player(standalones, local: 1);

        player.Refresh();

        Assert.Equal((true, team, name), (player.Teamplay, player.Team, player.TeamName));
    }
}
