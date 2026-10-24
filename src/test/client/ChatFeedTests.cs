using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The chat as stock's CBaseHudChat keeps it (hud_basechat.cpp): SayText2 a
/// player said is the speaker, the line and the format's (TEAM) and *DEAD*
/// tags; another SayText2 is its format with the parameters; SayText is its
/// line; each lasts hud_saytext_time ("12") and fades over its last second
/// (CHATLINE_FADE_TIME 1.0). And the input line (messagemode/messagemode2).
/// </summary>
public class ChatFeedTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    private sealed class Players : IChatPlayers
    {
        public bool TryFind(int client, out string name, out int team)
        {
            (name, team) = client switch { 1 => ("Gordon", 3), 2 => ("Alyx", 2), _ => ("", 0) };
            return name.Length > 0;
        }
    }

    private static (ChatFeed Feed, Clock Clock, RecordingLog Log) Make()
    {
        var clock = new Clock();
        var log = new RecordingLog();
        var engine = Fake();
        engine.ReturnsEachCall("Hud.Localise", () => (string)engine.Calls[^1].Arg(0)!);
        return (new ChatFeed(clock, log, engine, new Players()), clock, log);
    }

    private static SayText2 Say(int client, string format, params string[] p) => new() { Client = (byte)client, Chat = true, Name = format, Params = p };

    [Fact]
    public void A_players_line_is_the_speaker_their_team_and_the_text()
    {
        var (f, _, log) = Make();
        f.OnSayText2(Say(2, "#HL2MP_Chat_All", "Alyx", "follow me"));
        Assert.Equal(1, f.Count);
        var l = f.Line(0);
        Assert.Equal(("Alyx", 2, "follow me", false, false, 1f), (l.Speaker, l.Team, l.Text, l.TeamOnly, l.Dead, l.Alpha));
        Assert.Equal(["Alyx :  follow me\n"], log.Lines.Select(x => x.Text));     // stock's format, to the console
    }

    [Fact]
    public void A_format_named_without_its_hash_is_still_stocks()
    {
        var (f, _, log) = Make();
        f.OnSayText2(Say(2, "HL2MP_Chat_Team", "Alyx", "here"));
        Assert.True(f.Line(0).TeamOnly);
        Assert.Equal(["(TEAM) Alyx :  here\n"], log.Lines.Select(x => x.Text));
    }

    [Fact]
    public void The_format_says_team_only_and_dead()
    {
        var (f, _, _) = Make();
        f.OnSayText2(Say(1, "#HL2MP_Chat_TeamDead", "Gordon", "cover me"));
        Assert.Equal((true, true), (f.Line(0).TeamOnly, f.Line(0).Dead));
    }

    [Fact]
    public void A_server_line_is_its_format_with_the_parameters()
    {
        var (f, _, _) = Make();
        f.OnSayText2(new SayText2 { Client = 1, Chat = false, Name = "#HL2MP_Name_Change", Params = ["Gordon", "Freeman"] });
        Assert.Equal(("", "* Gordon changed name to Freeman"), (f.Line(0).Speaker, f.Line(0).Text));
    }

    [Fact]
    public void SayText_is_its_line()
    {
        var (f, _, _) = Make();
        f.OnSayText(new SayText { Client = 0, Text = "Server restarting", Chat = false });
        Assert.Equal(("", "Server restarting"), (f.Line(0).Speaker, f.Line(0).Text));
    }

    [Fact]
    public void A_line_lasts_twelve_seconds_and_fades_over_its_last()
    {
        var (f, clock, _) = Make();
        f.OnSayText2(Say(2, "#HL2MP_Chat_All", "Alyx", "hi"));
        clock.Now = 21f;                            // 11 s in: whole
        Assert.Equal(1f, f.Line(0).Alpha);
        clock.Now = 21.5f;                          // half a second left
        Assert.Equal(0.5f, f.Line(0).Alpha, 3);
        clock.Now = 22.1f;
        Assert.Equal(0, f.Count);
    }

    [Fact]
    public void Past_sixteen_lines_the_oldest_goes()
    {
        var (f, _, _) = Make();
        for (int i = 0; i < 17; i++)
            f.OnSayText2(Say(2, "#HL2MP_Chat_All", "Alyx", "line " + i));
        Assert.Equal((ChatFeed.MaxLines, "line 1"), (f.Count, f.Line(0).Text));
    }

    [Fact]
    public void The_input_sends_say_or_say_team_quoted_and_closes()
    {
        var sent = new List<string>();
        var client = new RecordingEngineClient(sent);
        var input = new ChatInputUi(client);
        bool closed = false;
        input.Closed = () => closed = true;
        input.Begin(teamOnly: false);
        foreach (char c in "say \"hi\"")
            input.Char(c);
        input.Key(UiKey.Enter);
        input.Begin(teamOnly: true);
        input.Char('x');
        input.Key(UiKey.Enter);
        Assert.Equal(["say \"say 'hi'\"", "say_team \"x\""], sent);
        Assert.True(closed);
    }

    [Fact]
    public void Escape_closes_and_sends_nothing()
    {
        var sent = new List<string>();
        var input = new ChatInputUi(new RecordingEngineClient(sent));
        bool closed = false;
        input.Closed = () => closed = true;
        input.Begin(false);
        input.Char('a');
        input.Key(UiKey.Escape);
        Assert.Empty(sent);
        Assert.True(closed);
    }

    internal sealed class RecordingEngineClient(List<string> sent) : IFwdEngineClient
    {
        public void SubmitConsoleLine(string line) => sent.Add(line);

        public void SpectatorCommand(SpectatorKeyCommand command) => sent.Add(command.ToString());
        public void SpectatorServerCommand(string line) => sent.Add("server: " + line);
        public bool IsInGame() => true;
        public void VoiceServerCommand(string line, bool reliable) => sent.Add("voice: " + line);
        public int SentenceIndexFromName(string name) => -1;
        public int LocalPlayerSlot() => 0;
        public string? LevelName() => null;
        public string? GameDirectory() => null;
        public uint ProtocolVersion() => 0;
        public bool TryGetPlayerInfo(int entIndex, out ClientPlayerInfo info) { info = default; return false; }
        public void JoinTeam(int team) { }
        public void ConnectFromSteamLaunch(string connectLine) { }
        public void Reconnect() => sent.Add("reconnect");
        public void RequestManagedReload() => sent.Add("reload");
        public void ConsolePrintAt(int position, string text) { }
        public void ConsolePrintAt(int position, float timeToLive, float red, float green, float blue, bool fixedWidthFont, string text) { }
        public bool TryLightForPoint(in Vector position, bool clamp, out Vector light) { light = default; return false; }
        public bool TryLightForPointFast(in Vector position, bool clamp, out Vector light) { light = default; return false; }
    }
}
