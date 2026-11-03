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

    private static (HostSay Chat, RecordingEngine Engine, RecordingLog Log) Rig(IChatAudience? audience = null,
                                                                               params IPlayerChatHook[] hooks)
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
        foreach (var hook in hooks)
            services.AddInstance<IPlayerChatHook>(hook);
        var chain = hooks.Length > 0 ? new Hooks<IPlayerChatHook>(services, log) : null;
        if (audience is not null)
            services.AddInstance(audience);
        var listeners = audience is not null ? new Hooks<IChatAudience>(services, log) : null;
        return (new HostSay(log, engine, engine, registry, engine, new Clients(), engine, engine,
                            chatHooks: chain, audience: listeners), engine, log);
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

    /// <summary>The say path's hook, stock's CBasePlayer::CheckChatText override (client.cpp:313).</summary>
    private sealed class Remembering : IPlayerChatHook
    {
        public RecordingEngine? Engine;

        public readonly List<PlayerChat> Seen = new();

        // How many engine calls had happened when each hook ran: before the
        // relay, no SayText2 send is among them.
        public readonly List<int> CallsAtHook = new();

        public void OnPlayerChat(PlayerChat chat)
        {
            Seen.Add(chat);
            if (Engine is not null)
                CallsAtHook.Add(Engine.Calls.Count);
        }
    }

    [Fact]
    public void The_say_path_tells_the_chat_hooks_the_checked_line_before_the_relay()
    {
        var seen = new Remembering();
        var (chat, engine, _) = Rig(hooks: seen);
        seen.Engine = engine;

        chat.Say(Alice, "\"quoted line\"", teamOnly: false);
        chat.Say(Alice, "to the team", teamOnly: true);

        // The checked text (quotes gone), both flavours.
        Assert.Equal(new[] { (Alice, "quoted line", false), (Alice, "to the team", true) },
                     seen.Seen.Select(c => (c.Speaker, c.Text, c.TeamOnly)).ToArray());

        // The first hook ran before the relay's first send: the calls behind it
        // do not yet include that send.
        int firstSend = -1;
        for (int i = 0; i < engine.Calls.Count; i++)
        {
            if (engine.Calls[i].Member == "UserMessages.Send")
            {
                firstSend = i;
                break;
            }
        }
        Assert.True(firstSend > 0, "the line was relayed at all");
        Assert.True(seen.CallsAtHook.Count > 0 && seen.CallsAtHook[0] <= firstSend,
                    $"the hook ran at call {seen.CallsAtHook.FirstOrDefault()}, the first send is call {firstSend}");
    }

    [Fact]
    public void A_line_with_no_speaker_reaches_no_chat_hook()
    {
        var seen = new Remembering();
        var (chat, engine, _) = Rig(hooks: seen);
        seen.Engine = engine;

        chat.Say(EntityHandle.None, "from the console", teamOnly: false);

        Assert.Empty(seen.Seen);
        Assert.Empty(seen.CallsAtHook);
    }

    /// <summary>An audience that refuses one listener, the gate a game vets with.</summary>
    private sealed class RefusingListener : IChatAudience, IOrderedHook
    {
        public int HookOrder => 1;

        public bool CanHear(EntityHandle listener, EntityHandle speaker) => listener != Bob;
    }

    [Fact]
    public void A_refusing_audience_takes_its_listener_out_of_the_relay()
    {
        var (chat, engine, _) = Rig(audience: new RefusingListener());

        chat.Say(Alice, "hello", teamOnly: false);

        // Bob (slot 2) is refused; the sender still hears herself.
        Assert.Equal(new[] { 1 }, Sent(engine).Select(s => s.Slot).ToArray());
    }

    [Fact]
    public void Every_registered_audience_is_asked_in_its_order()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsPlayer).Assembly);
        var services = Services(out var engine);
        var log = new RecordingLog();
        services.AddInstance<RefusingListener>(new RefusingListener());
        services.AddInstance<PassingAudience>(new PassingAudience());
        var listeners = new Hooks<IChatAudience>(services, log);
        Assert.Equal([false, true], listeners.All.Select(a => a.CanHear(Bob, Alice)).ToArray());
    }

    private sealed class PassingAudience : IChatAudience, IOrderedHook
    {
        public int HookOrder => 2;

        public bool CanHear(EntityHandle listener, EntityHandle speaker) => true;
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
