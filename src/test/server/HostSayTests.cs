using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Stock's Host_Say (client.cpp) under HL2MP's rules: the text checked
/// (CheckChatText), every other player who may hear it sent SayText2 --
/// the sender's index, chat, the format (HL2MP_Chat_All, or _Team), the
/// sender's name and the text -- reliable, then the sender; say_team only to
/// teammates, and HL2MP without teamplay has none; the log line and the
/// player_say event.
/// </summary>
public class HostSayTests
{
    private static readonly EntityHandle Alice = new(0x010001);
    private static readonly EntityHandle Bob = new(0x010002);

    private sealed class Clients : IConnectedClients
    {
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public float? AvgLatency(int entIndex) => null;

        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => "loopback";
        public string? ClientConVar(int entIndex, string name) => name == "name" ? (entIndex == 1 ? "alice" : "bob") : null;
        public int UserId(int entIndex) => entIndex + 10;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    private static (HostSay Chat, RecordingEngine Engine, RecordingLog Log) Rig()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsPlayer).Assembly);
        var services = Services(out var engine);
        engine.Returns("Entities.IsValid", true).Returns("Players.GetTeam", 0).Returns("Host.IsDedicatedServer", false)
              .ReturnsEachCall("Entities.GetIndex", () => engine.Calls[^1].Args[0] is EntityHandle h && h == Bob ? 2 : 1)
              .Returns("GameEvents.EventCreate", new IntPtr(7)).Returns("GameEvents.EventFire", true)
              .Ignores("GameEvents.EventSetInt", "GameEvents.EventSetString");
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        registry.KeyValue(Alice, EntityNames.Player, "targetname", "a");
        registry.KeyValue(Bob, EntityNames.Player, "targetname", "b");
        var log = new RecordingLog();
        return (new HostSay(log, engine, engine, registry, engine, new Clients(), engine, engine), engine, log);
    }

    private static List<(int Slot, SayText2 Msg)> Sent(RecordingEngine engine)
        => engine.AllOf("UserMessages.Send").Where(c => (string)c.Args[0]! == "SayText2").Select(c =>
        {
            Assert.True((bool)c.Args[3]!, "reliable");
            var reader = new UserMessageReader((byte[])c.Args[4]!);
            return ((int)c.Args[2]!, SayText2.Read(ref reader));
        }).ToList();

    [Fact]
    public void Say_reaches_everyone_the_sender_last_with_stocks_format()
    {
        var (chat, engine, _) = Rig();

        chat.Say(Alice, "hello there", teamOnly: false);

        var sent = Sent(engine);
        Assert.Equal(new[] { 2, 1 }, sent.Select(s => s.Slot).ToArray());
        var m = sent[0].Msg;
        Assert.Equal((1, true, "HL2MP_Chat_All"), ((int)m.Client, m.Chat, m.Name));
        Assert.Equal(new[] { "alice", "hello there", "", "" }, m.Params);
    }

    [Fact]
    public void Say_team_without_teamplay_reaches_only_the_sender()
    {
        var (chat, engine, _) = Rig();

        chat.Say(Alice, "team only", teamOnly: true);

        var sent = Sent(engine);
        Assert.Equal(1, Assert.Single(sent).Slot);
        Assert.Equal("HL2MP_Chat_Team", sent[0].Msg.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"\"")]
    [InlineData("\n")]
    public void Empty_text_sends_nothing(string text)
    {
        var (chat, engine, _) = Rig();
        chat.Say(Alice, text, teamOnly: false);
        Assert.Empty(Sent(engine));
    }

    [Fact]
    public void The_text_loses_its_quotes_ends_at_a_line_break_and_stops_at_127()
    {
        Assert.Equal("hi", HostSay.CheckChatText("\"hi\""));
        Assert.Equal("one", HostSay.CheckChatText("one\rtwo"));
        Assert.Equal(127, HostSay.CheckChatText(new string('x', 200))!.Length);
    }

    [Fact]
    public void The_server_console_is_heard_by_nobody_in_hl2mp()
    {
        var (chat, engine, _) = Rig();
        chat.Say(EntityHandle.None, "from the console", teamOnly: false);
        Assert.Empty(Sent(engine));
    }

    [Fact]
    public void It_is_logged_and_fired_as_player_say()
    {
        var (chat, engine, log) = Rig();
        chat.Say(Alice, "hello", teamOnly: false);
        Assert.Contains(log.Lines, l => l.Text.Contains("\"alice<11><STEAM_ID_LAN><Unassigned>\" say \"hello\"", StringComparison.Ordinal));
        Assert.Equal("player_say", (string)Assert.Single(engine.AllOf("GameEvents.EventCreate")).Args[0]!);
        Assert.Contains(engine.AllOf("GameEvents.EventSetString"), c => (string)c.Args[1]! == "text" && (string)c.Args[2]! == "hello");
    }
}

/// <summary>say and say_team are registered again, as Host_Say's commands, and retired no more.</summary>
public class SayCommandRegistrationTests
{
    [Theory]
    [InlineData(typeof(SayCommand), "say")]
    [InlineData(typeof(SayTeamCommand), "say_team")]
    public void The_command_is_registered_under_stocks_name(Type type, string name)
    {
        var attr = (ConCommandAttribute)Attribute.GetCustomAttribute(type, typeof(ConCommandAttribute))!;
        Assert.Equal(name, attr.Name);
        Assert.DoesNotContain(typeof(SayCommand).Assembly.GetTypes(),
            t => t.IsSubclassOf(typeof(RetiredDevCommand)) &&
                 ((ConCommandAttribute?)Attribute.GetCustomAttribute(t, typeof(ConCommandAttribute)))?.Name == name);
    }
}

/// <summary>
/// The connected clients answer a client's convar from the engine (the name
/// Host_Say says a line under). EngineClients fell through to the interface's
/// null default, so every chat line was "unconnected" (live chat step).
/// </summary>
public class EngineClientsConVarTests
{
    private sealed class Info : IClientInfo
    {
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public float? AvgLatency(int entIndex) => null;

        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => entIndex == 2 && name == "name" ? "bob" : null;
    }

    [Fact]
    public void A_clients_name_is_the_engines_answer()
    {
        var engine = TestServices.Fake();
        IConnectedClients clients = new EngineClients(new Info(), engine, engine, engine);
        Assert.Equal("bob", clients.ClientConVar(2, "name"));
    }
}
