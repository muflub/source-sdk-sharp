using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The player list (plan_gameui_managed.md G3), as stock's player list and
/// mute dialogs behaved: the players by slot, refreshed on open, the first
/// selected; mute and unmute flip the selected player's voice; a bot or this
/// client cannot be muted.
/// </summary>
public class PlayerListTests
{
    [Fact]
    public void Opened_it_refreshes_and_selects_the_first_player()
    {
        var players = PlayerListSamples.Game();
        var screen = PlayerListSamples.Build(new UiSampleServices(), players);
        screen.Open();
        Assert.Equal(1, players.Refreshes);
        Assert.Equal(0, screen.Selected);
    }

    [Theory]
    [InlineData(0, false)]   // this client
    [InlineData(1, true)]
    [InlineData(3, false)]   // a bot
    public void Only_another_player_can_be_muted(int row, bool can)
    {
        var players = PlayerListSamples.Game();
        var screen = PlayerListSamples.Build(new UiSampleServices(), players);
        screen.Open();
        screen.Selected = row;
        Assert.Equal(can, screen.CanMute);
        bool before = players.Player(row).Muted;
        screen.ToggleMute();
        Assert.Equal(can ? !before : before, players.Player(row).Muted);
    }

    [Fact]
    public void With_nobody_there_nothing_is_selected_and_nothing_mutes()
    {
        var screen = PlayerListSamples.Build(new UiSampleServices(), new FixedPlayerList());
        screen.Open();
        Assert.Equal(-1, screen.Selected);
        Assert.False(screen.CanMute);
        screen.ToggleMute();
    }

    private sealed class Engine(Dictionary<int, ClientPlayerInfo> infos, int local) : IFwdEngineClient
    {
        public bool TryGetPlayerInfo(int entIndex, out ClientPlayerInfo info) => infos.TryGetValue(entIndex, out info);
        public int LocalPlayerSlot() => local;
        public void SubmitConsoleLine(string line) { }
        public void SpectatorCommand(SpectatorKeyCommand command) { }
        public void SpectatorServerCommand(string line) { }
        public bool IsInGame() => true;
        public void VoiceServerCommand(string line, bool reliable) { }
        public int SentenceIndexFromName(string name) => -1;
        public string? LevelName() => null;
        public string? GameDirectory() => null;
        public uint ProtocolVersion() => 0;
        public bool CopyLocalFile(string source, string destination) => false;
        public void JoinTeam(int team) { }
        public void ConnectFromSteamLaunch(string connectLine) { }
        public void Reconnect() { }
        public void RequestManagedReload() { }
        public void ConsolePrintAt(int position, string text) { }
        public void ConsolePrintAt(int position, float timeToLive, float red, float green, float blue, bool fixedWidthFont, string text) { }
        public bool TryLightForPoint(in Vector position, bool clamp, out Vector light) { light = default; return false; }
        public bool TryLightForPointFast(in Vector position, bool clamp, out Vector light) { light = default; return false; }
    }

    private sealed class Mutes : IVoiceBlockEvents
    {
        public HashSet<int> Blocked { get; } = [];
        public bool IsBlocked(int client) => Blocked.Contains(client);
        public void ToggleBlocked(int client)
        {
            if (!Blocked.Remove(client))
                Blocked.Add(client);
        }
    }

    private static (PlayerListUi List, Mutes Mutes) Game()
    {
        var engine = new Engine(new()
        {
            [1] = new ClientPlayerInfo("Gordon", 2, false),
            [3] = new ClientPlayerInfo("Bot01", 4, true),
            [5] = new ClientPlayerInfo("Alyx", 6, false),
        }, local: 1);
        var mutes = new Mutes();
        mutes.Blocked.Add(5);
        var env = TestServices.Fake().Returns("Environment.Globals", default(Globals));
        return (new PlayerListUi(new EnginePlayerNames(engine, new IdentityNameFilter()), engine, env, mutes), mutes);
    }

    [Fact]
    public void The_source_lists_every_slot_with_a_player_with_its_mute_bot_and_local_marks()
    {
        var (list, _) = Game();
        list.Refresh();
        Assert.Equal([new PlayerListEntry("Gordon", false, false, true), new PlayerListEntry("Bot01", false, true, false),
                      new PlayerListEntry("Alyx", true, false, false)],
                     Enumerable.Range(0, list.Count).Select(list.Player));
    }

    [Fact]
    public void The_source_mutes_a_player_by_slot_and_never_a_bot_or_this_client()
    {
        var (list, mutes) = Game();
        list.Refresh();
        list.ToggleMute(2);
        Assert.DoesNotContain(5, mutes.Blocked);
        Assert.False(list.Player(2).Muted);
        list.ToggleMute(0);
        list.ToggleMute(1);
        Assert.Empty(mutes.Blocked);
    }
}
