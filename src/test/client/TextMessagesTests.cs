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

    private static (TextMessages Text, CentrePrint Centre, RecordingLog Log, Chat Chat) Make(RecordingEngine? engine = null)
    {
        engine ??= Fake();
        var e = engine;
        e.Returns("Hud.TextMessageGet", null);     // titles.txt has none of these: the token goes to the tables
        e.ReturnsEachCall("Hud.Localise", () => Tables.TryGetValue((string)e.Calls[^1].Arg(0)!, out var v) ? v : (string)e.Calls[^1].Arg(0)!);
        var centre = new CentrePrint(new Clock());
        var log = new RecordingLog();
        var chat = new Chat();
        return (new TextMessages(e, log, centre, chat: chat), centre, log, chat);
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
    public void Construct_replaces_s1_to_s4_and_leaves_other_percents()
        => Assert.Equal("a b c d 100%", TextMessages.Construct("%s1 %s2 %s3 %s4 100%", "a", "b", "c", "d"));
}
