using SourceSharp.Abi;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The game's notices as stock's ClientModeShared::FireGameEvent printed them
/// (clientmode_shared.cpp): joins, leaves, team changes, renames and notify
/// convars, into the chat and the console, with the names stock would not show
/// yet left out.
/// </summary>
public class ChatNoticesTests
{
    private sealed class Chat : ITextMessageChat
    {
        public readonly List<string> Lines = [];

        public void Print(string line) => Lines.Add(line);
    }

    private static (ChatNotices Notices, Chat Chat, RecordingLog Log) Make(Dictionary<string, string>? tables = null)
    {
        var e = Fake();
        tables ??= new() { ["#game_player_joined_game"] = "%s1 is here", ["#Kicked"] = "kicked by the admin" };
        e.ReturnsEachCall("Hud.Localise", () => tables.TryGetValue((string)e.Calls[^1].Arg(0)!, out var v) ? v : (string)e.Calls[^1].Arg(0)!);
        var chat = new Chat();
        var log = new RecordingLog();
        return (new ChatNotices(e, log, chat), chat, log);
    }

    [Fact]
    public void A_join_reads_the_tables_format_into_the_chat_and_the_console()
    {
        var (n, chat, log) = Make();
        n.OnConnect(new PlayerConnectClientArgs("alyx", 2, 7, "BOT", true));
        Assert.Equal(["alyx is here"], chat.Lines);
        Assert.Equal(["alyx is here\n"], log.Lines.Select(l => l.Text));
    }

    [Fact]
    public void A_format_the_tables_lack_is_valve_englishs()
    {
        var (n, chat, _) = Make(new());
        n.OnChangeName(new PlayerChangeNameArgs(7, "alyx", "gordon"));
        n.OnServerCvar(new ServerCvarArgs("mp_timelimit", "20"));
        Assert.Equal(["Player alyx changed name to gordon", "Server cvar 'mp_timelimit' changed to 20"], chat.Lines);
    }

    [Fact]
    public void A_leave_localises_a_token_reason_and_keeps_a_plain_one()
    {
        var (n, chat, _) = Make();
        n.OnDisconnect(new PlayerDisconnectArgs("alyx", 7, "#Kicked"));
        n.OnDisconnect(new PlayerDisconnectArgs("barney", 8, "Disconnect by user."));
        Assert.Equal(["Player alyx left the game (kicked by the admin)", "Player barney left the game (Disconnect by user.)"],
                     chat.Lines);
    }

    [Fact]
    public void A_team_change_names_the_team_and_says_when_the_game_chose_it()
    {
        var (n, chat, _) = Make();
        n.OnTeam(new PlayerTeamArgs(7, 3, 0, false, AutoTeam: false, Name: "alyx"));
        n.OnTeam(new PlayerTeamArgs(8, 2, 0, false, AutoTeam: true, Name: "barney"));
        Assert.Equal(["Player alyx joined team Rebels", "Player barney was automatically assigned to team Combine"], chat.Lines);
    }

    [Fact]
    public void A_silent_or_disconnecting_team_change_says_nothing()
    {
        var (n, chat, _) = Make();
        n.OnTeam(new PlayerTeamArgs(7, 3, 0, false, Silent: true, Name: "alyx"));
        n.OnTeam(new PlayerTeamArgs(7, 0, 3, true, Name: "alyx"));
        n.OnTeam(new PlayerTeamArgs(7, 1, 3, false, Name: "alyx"));
        Assert.Equal(["Player alyx joined team Spectator"], chat.Lines);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unconnected")]
    [InlineData("UNCONNECTED player")]
    [InlineData("nullname")]
    public void A_name_not_set_yet_says_nothing(string name)
    {
        var (n, chat, _) = Make();
        n.OnConnect(new PlayerConnectClientArgs(name, 2, 7, "", false));
        n.OnDisconnect(new PlayerDisconnectArgs(name, 7, "x"));
        n.OnTeam(new PlayerTeamArgs(7, 2, 0, false, Name: name));
        n.OnChangeName(new PlayerChangeNameArgs(7, name, "gordon"));
        n.OnConnect(new PlayerConnectClientArgs("alyx", 2, 7, "", false));
        Assert.Equal(["alyx is here"], chat.Lines);
    }
}
