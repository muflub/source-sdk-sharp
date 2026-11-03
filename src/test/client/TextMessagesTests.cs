using SourceSharp.Client.Ui;
using SourceSharp.Abi;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// TextMsg as stock prints it (CBaseHudChat::MsgFunc_TextMsg, hud_basechat.cpp;
/// CHudTextMessage::LookupString, text_message.cpp): '#' strings through
/// titles.txt, then the localisation tables, the parameters into %s1..%s4, and
/// each destination: centre, notify, console, talk.
/// </summary>
public class TextMessagesTests
{
    private sealed class Clock : IGameClock
    {
        public float Now => 10f;
    }

    private sealed class Chat : ITextMessageChat
    {
        public readonly List<string> Lines = [];

        public void Print(string line) => Lines.Add(line);
    }

    private static readonly Dictionary<string, string> Tables = new()
    {
        ["#Game_connected"] = "%s1 connected",
        ["#Cstrike_Name"] = "Gordon",
    };

    /// <summary>The mod's copy (IUiLocalisation): what the UI's chain answers, and how often it was asked.</summary>
    private sealed class Copy : IUiLocalisation
    {
        private readonly Dictionary<string, string> _tokens;

        public Copy(Dictionary<string, string> tokens) => _tokens = tokens;

        public int Asks { get; private set; }

        public readonly List<string> Asked = [];

        public string? Find(string token)
        {
            Asks++;
            Asked.Add(token);
            return _tokens.TryGetValue(token, out var t) ? t : null;
        }
    }

    private static (TextMessages Text, CentrePrint Centre, RecordingLog Log, Chat Chat) Make(RecordingEngine? engine = null,
                                                                                              IServiceLocator? services = null)
    {
        engine ??= Fake();
        var e = engine;
        e.Returns("Hud.TextMessageGet", null);     // titles.txt has none of these: the token goes to the tables
        e.ReturnsEachCall("Hud.Localise", () => Tables.TryGetValue((string)e.Calls[^1].Arg(0)!, out var v) ? v : (string)e.Calls[^1].Arg(0)!);
        var centre = new CentrePrint(new Clock());
        var log = new RecordingLog();
        var chat = new Chat();
        return (new TextMessages(e, log, centre, chat: chat, services: services), centre, log, chat);
    }

    private static TextMsg Msg(int dest, string text, params string[] p)
        => new() { Destination = (byte)dest, Text = text, Params = p };

    [Fact]
    public void A_localised_token_with_a_parameter_reaches_the_centre_print()
    {
        var (t, centre, _, _) = Make();
        t.Print(Msg(TextMessages.PrintCenter, "#Game_connected", "Alyx"));
        Assert.Equal("Alyx connected", centre.CenterPrint);
    }

    [Fact]
    public void A_parameter_can_be_a_token_too()
    {
        var (t, centre, _, _) = Make();
        t.Print(Msg(TextMessages.PrintCenter, "#Game_connected", "#Cstrike_Name"));
        Assert.Equal("Gordon connected", centre.CenterPrint);
    }

    [Fact]
    public void Console_and_notify_go_to_the_console_with_a_newline()
    {
        var (t, centre, log, _) = Make();
        t.Print(Msg(TextMessages.PrintConsole, "plain text"));
        t.Print(Msg(TextMessages.PrintNotify, "already ends\n"));
        Assert.Equal(["plain text\n", "already ends\n"], log.Lines.Select(l => l.Text));
        Assert.Equal("", centre.CenterPrint);
    }

    [Fact]
    public void Talk_goes_to_the_chat_and_the_console()
    {
        var (t, _, log, chat) = Make();
        t.Print(Msg(TextMessages.PrintTalk, "#Game_connected", "Barney"));
        Assert.Equal(["Barney connected\n"], chat.Lines);
        Assert.Equal(["Barney connected\n"], log.Lines.Select(l => l.Text));
    }

    [Fact]
    public void A_parameter_not_localised_loses_its_end_newline()
    {
        var (t, centre, _, _) = Make();
        t.Print(Msg(TextMessages.PrintCenter, "#Game_connected", "Kleiner\n"));
        Assert.Equal("Kleiner connected", centre.CenterPrint);
    }

    [Fact]
    public void Carriage_returns_are_new_lines_in_the_centre()
    {
        var (t, centre, _, _) = Make();
        t.Print(Msg(TextMessages.PrintCenter, "one\rtwo"));
        Assert.Equal("one\ntwo", centre.CenterPrint);
    }

    [Fact]
    public void Cl_showtextmsg_0_prints_nothing()
    {
        var engine = Fake();
        engine.ReturnsEachCall("Hud.Localise", () => (string)engine.Calls[^1].Arg(0)!);
        var centre = new CentrePrint(new Clock());
        var show = new ClShowTextMsgConVar();
        show.NotifyChanged("0", "1");
        new TextMessages(engine, new RecordingLog(), centre, show).Print(Msg(TextMessages.PrintCenter, "x"));
        Assert.Equal("", centre.CenterPrint);
    }

    [Fact]
    public void The_mods_language_chain_answers_what_the_engine_tables_lack()
    {
        // TF2's join-class notice (TFJoinClassCommand): the server sends
        // #game_respawn_as with the class file's localize_name as its
        // parameter — which the class files carry WITHOUT stock's '#'
        // (TFClassDataTests), as joinclass sent it live. Both tokens live in
        // the game's own English file, which no table of this client loads
        // into the engine; stock's one table takes the token with or without
        // the '#', so before the chain existed the player read the tokens and
        // now the chain is asked with the '#' the file keys them by.
        var copy = new Copy(new()
        {
            ["#game_respawn_as"] = "%s1 has respawned.",
            ["#TF_Class_Name_Scout"] = "Scout",
        });
        var services = new ServiceContainer().AddInstance<IUiLocalisation>(copy);
        var (t, _, log, chat) = Make(services: services);
        t.Print(Msg(TextMessages.PrintTalk, "#game_respawn_as", "TF_Class_Name_Scout"));
        Assert.Equal(["Scout has respawned.\n"], chat.Lines);
        Assert.Equal(["Scout has respawned.\n"], log.Lines.Select(l => l.Text));
        Assert.Contains("#TF_Class_Name_Scout", copy.Asked);   // the ask carried the file's key
        Assert.True(copy.Asks >= 2);               // the stimulus reached the chain
    }

    [Fact]
    public void The_engine_table_wins_and_the_chain_is_never_asked_for_its_token()
    {
        // #Game_connected is in the engine's tables (Make's) and in the copy:
        // stock's one-table precedence means the engine's words print, and
        // the chain is never asked for that token (its ask here is the plain
        // parameter, which no table holds).
        var copy = new Copy(new() { ["#Game_connected"] = "%s1 joined the server" });
        var services = new ServiceContainer().AddInstance<IUiLocalisation>(copy);
        var (t, _, _, chat) = Make(services: services);
        t.Print(Msg(TextMessages.PrintTalk, "#Game_connected", "Barney"));
        Assert.Equal(["Barney connected\n"], chat.Lines);
        Assert.DoesNotContain("#Game_connected", copy.Asked);
    }

    [Fact]
    public void Construct_replaces_s1_to_s4_and_leaves_other_percents()
        => Assert.Equal("a b c d 100%", TextMessages.Construct("%s1 %s2 %s3 %s4 100%", "a", "b", "c", "d"));
}
