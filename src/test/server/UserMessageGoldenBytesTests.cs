using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The bytes every converted user message carries today, written down before
/// the payload model replaced the hand-written writers and readers, so the
/// conversion is held to the same wire.
///
/// <para>
/// A literal per message. Each one is produced by the PRODUCTION sender where
/// one is managed (an entity input, a service call), so a change to the sender
/// shows here and not only a change to an encoder. A message only native code
/// sends is held from the receiving side: the literal decodes, through the
/// managed reader, to the values stock writes.
/// </para>
/// </summary>
public class UserMessageGoldenBytesTests
{
    //-------------------------------------------------------------------------
    // The literals.
    //-------------------------------------------------------------------------

    /// <summary>game_text: channel 3, x -1, y 0.5, colours 1 2 3 4 / 5 6 7 8, effect 2, 1 / 0.5 / 2 / 0.25 s, "Hi".</summary>
    internal static readonly byte[] HudMsg =
    [
        0x03,
        0x00, 0x00, 0x80, 0xBF,
        0x00, 0x00, 0x00, 0x3F,
        0x01, 0x02, 0x03, 0x04,
        0x05, 0x06, 0x07, 0x08,
        0x02,
        0x00, 0x00, 0x80, 0x3F,
        0x00, 0x00, 0x00, 0x3F,
        0x00, 0x00, 0x00, 0x40,
        0x00, 0x00, 0x80, 0x3E,
        (byte)'H', (byte)'i', 0x00,
    ];

    /// <summary>env_message / the chapter title: the titles.txt name, NUL-terminated.</summary>
    internal static readonly byte[] HudText = [.. "GAMESAVED"u8, 0x00];

    /// <summary>env_hudhint: a count of one, then the hint.</summary>
    internal static readonly byte[] KeyHintText = [0x01, .. "Press E"u8, 0x00];

    /// <summary>The server's chat line: speaker 0, the line with its newline, then "not a player's chat".</summary>
    internal static readonly byte[] SayText = [0x00, .. "hello\n"u8, 0x00, 0x00];

    /// <summary>A start, amplitude 4, frequency 1, duration 2.</summary>
    internal static readonly byte[] Shake =
    [
        0x00,
        0x00, 0x00, 0x80, 0x40,
        0x00, 0x00, 0x80, 0x3F,
        0x00, 0x00, 0x00, 0x40,
    ];

    /// <summary>1 s and 2 s in unsigned 7.9 fixed point, FFADE_IN, colour 1 2 3 4.</summary>
    internal static readonly byte[] Fade =
    [
        0x00, 0x02,
        0x00, 0x04,
        0x01, 0x00,
        0x01, 0x02, 0x03, 0x04,
    ];

    /// <summary>The reconnect request carries nothing.</summary>
    internal static readonly byte[] Reconnect = [];

    /// <summary>The layout handshake: six little-endian ints.</summary>
    internal static readonly byte[] Layout =
    [
        0x78, 0x56, 0x34, 0x12,
        0x07, 0x00, 0x00, 0x00,
        0xEF, 0xBE, 0xAD, 0xDE,
        0x01, 0x00, 0x00, 0x00,
        0x02, 0x00, 0x00, 0x00,
        0x03, 0x00, 0x00, 0x00,
    ];

    /// <summary>Messages only native code sends: what stock writes, as the managed reader takes it.</summary>
    internal static readonly byte[] Geiger      = [0x05];
    internal static readonly byte[] Train       = [0x03];
    internal static readonly byte[] TextMsg     = [0x04, .. "#Game_x"u8, 0x00, 0x01, .. "p"u8, 0x00];
    internal static readonly byte[] ItemPickup  = [.. "item_battery"u8, 0x00];
    internal static readonly byte[] Battery     = [0x64, 0x00];
    internal static readonly byte[] AmmoDenied  = [0x07, 0x00];

    /// <summary>Armour 10, health 20, bits 8, origin (1, 2, 0.5).</summary>
    internal static readonly byte[] Damage      =
    [
        0x0A, 0x14, 0x08, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x3F,
    ];

    //-------------------------------------------------------------------------
    // The managed senders.
    //-------------------------------------------------------------------------

    private static readonly EntityHandle Activator = new(0x0001_0004u);

    private sealed class NoInfo : IClientInfo
    {
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public float? AvgLatency(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => null;

        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => null;
    }

    private static List<(string Key, int Player, byte[] Payload)> Sent(RecordingEngine engine) =>
        engine.Calls.Where(c => c.Member == "UserMessages.Send")
                    .Select(c => ((string)c.Arg(0)!, (int)c.Arg(2)!, (byte[])c.Arg(4)!))
                    .ToList();

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine, EntityHandle Handle) Spawn(
        string className, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(GameText).Assembly);
        registry.Services = Services(out var engine);

        engine.Returns("Entities.GetSpawnFlags", 0);
        engine.Returns("Entities.IsPlayer", true);
        engine.Returns("Entities.GetIndex", 4);

        var handle = new EntityHandle(0x0002_0040u);
        foreach (var (key, value) in keys)
            registry.KeyValue(handle, className, key, value);

        registry.Spawn(handle, className);
        return (registry, engine, handle);
    }

    [Fact]
    public void GameTextSendsTheGoldenHudMsg()
    {
        var (registry, engine, at) = Spawn(EntityNames.GameText,
            ("message", "Hi"), ("channel", "3"), ("x", "-1"), ("y", "0.5"),
            ("color", "1 2 3 4"), ("color2", "5 6 7 8"), ("effect", "2"),
            ("fadein", "1"), ("fadeout", "0.5"), ("holdtime", "2"), ("fxtime", "0.25"));

        Assert.True(registry.Input(at, "Display", "", 0f, Activator), "game_text takes Display");

        var only = Assert.Single(Sent(engine));
        Assert.Equal(("HudMsg", 4), (only.Key, only.Player));
        Assert.Equal(HudMsg, only.Payload);
    }

    [Fact]
    public void EnvMessageSendsTheGoldenHudText()
    {
        var (registry, engine, at) = Spawn(EntityNames.EnvMessage, ("message", "GAMESAVED"));

        Assert.True(registry.Input(at, "ShowMessage", "", 0f, Activator), "env_message takes ShowMessage");

        var only = Assert.Single(Sent(engine));
        Assert.Equal(("HudText", 4), (only.Key, only.Player));
        Assert.Equal(HudText, only.Payload);
    }

    [Fact]
    public void TheChapterTitleSendsTheGoldenHudText()
    {
        var engine = Mock();
        var title = new ChapterTitle(new RecordingLog(), engine);
        title.LevelInit("GAMESAVED");

        Assert.True(title.OnPlayerSpawned(Activator, 4), "the first spawn is announced to");

        var only = Assert.Single(Sent(engine));
        Assert.Equal(("HudText", 4), (only.Key, only.Player));
        Assert.Equal(HudText, only.Payload);
    }

    [Fact]
    public void EnvHudHintSendsTheGoldenKeyHintText()
    {
        var (registry, engine, at) = Spawn(EntityNames.EnvHudhint, ("message", "Press E"));

        Assert.True(registry.Input(at, "ShowHudHint", "", 0f, Activator), "env_hudhint takes ShowHudHint");

        var only = Assert.Single(Sent(engine));
        Assert.Equal(("KeyHintText", 4), (only.Key, only.Player));
        Assert.Equal(KeyHintText, only.Payload);
    }

    [Fact]
    public void ChatSendsTheGoldenSayText()
    {
        var engine = Mock();
        new Chat(engine).Print(4, "hello");

        var only = Assert.Single(Sent(engine));
        Assert.Equal(("SayText", 4), (only.Key, only.Player));
        Assert.Equal(SayText, only.Payload);
    }

    [Fact]
    public void ScreenEffectsSendTheGoldenShake()
    {
        var (fx, engine) = ScreenEffectsTests.World(new ScreenEffectsTests.Player(2, new Vector(0, 0, 0), true));

        fx.Shake(new Vector(0, 0, 0), 4f, 1f, 2f, 0f);

        var only = Assert.Single(Sent(engine));
        Assert.Equal(("Shake", 2), (only.Key, only.Player));
        Assert.Equal(Shake, only.Payload);
    }

    [Fact]
    public void ScreenEffectsSendTheGoldenFade()
    {
        var (fx, engine) = ScreenEffectsTests.World(new ScreenEffectsTests.Player(7, default, true));

        fx.Fade(new EntityHandle(1007), 1, 2, 3, 4, 1f, 2f, FadeFlags.In);

        var only = Assert.Single(Sent(engine));
        Assert.Equal(("Fade", 7), (only.Key, only.Player));
        Assert.Equal(Fade, only.Payload);
    }

    [Fact]
    public void TheHostSendsTheGoldenReconnect()
    {
        var engine = Mock();
        engine.Returns("Players.GetHandle", Activator);

        new EngineClients(new NoInfo(), engine, engine, engine).Reconnect(4);

        var only = Assert.Single(Sent(engine));
        Assert.Equal((UserMessageKey.Of<ReconnectMessage>(), 4), (only.Key, only.Player));
        Assert.Equal(Reconnect, only.Payload);
    }

    //-------------------------------------------------------------------------
    // The readers, for the literals no managed code sends.
    //-------------------------------------------------------------------------

    private static UserMessageReader Reader(byte[] bytes) => new(bytes);

    private static byte[] Written<T>(T payload) where T : IUserMessagePayload<T>
    {
        var buffer = new byte[UserMessageSender.MaxPayloadBytes];
        int n = payload.Write(buffer);
        Assert.True(n >= 0, "the payload fits a user message");
        return buffer[..n];
    }

    [Fact]
    public void TheHudPayloadsWriteTheirPinnedLayouts()
    {
        Assert.Equal(HudMsg, Written(new HudMsg
        {
            Channel = 3, X = -1f, Y = 0.5f, Color = new Color32(1, 2, 3, 4), Color2 = new Color32(5, 6, 7, 8),
            Effect = 2, FadeIn = 1f, FadeOut = 0.5f, HoldTime = 2f, FxTime = 0.25f, Message = "Hi",
        }));
        Assert.Equal(HudText, Written(new HudText { Name = "GAMESAVED" }));
        Assert.Equal(KeyHintText, Written(new KeyHintText { Hints = ["Press E"] }));
        Assert.Equal(SayText, Written(new SayText { Client = 0, Text = "hello\n", Chat = false }));
    }

    [Fact]
    public void TheRemainingStockPayloadsWriteTheirPinnedLayouts()
    {
        Assert.Equal([.. "#Caption"u8, 0, 25, 0, 1],
                     Written(new CloseCaption { Token = "#Caption", Duration = 25, Flags = 1 }));
        Assert.Equal([1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4, 0, 0, 0, 1],
                     Written(new VoiceMask { Audible0 = 1, Banned0 = 2, Audible1 = 3, Banned1 = 4, ModEnable = true }));
        Assert.Equal([.. "info"u8, 0, 1, 1, .. "type"u8, 0, 1, .. "2"u8, 0],
                     Written(new VguiMenu { Name = "info", Show = true, Keys = ["type"], Values = ["2"] }));
        Assert.Equal([3, 1, .. "#Chat"u8, 0, 2, .. "a"u8, 0, .. "b"u8, 0],
                     Written(new SayText2 { Client = 3, Chat = true, Name = "#Chat", Params = ["a", "b"] }));
        Assert.Equal([7, 8, 9], Written(new Rumble { Waveform = 7, Data = 8, Flags = 9 }));
        Assert.Equal([0x3F, 0x00, 0xFF, 1, .. "1. Go"u8, 0],
                     Written(new ShowMenu { ValidSlots = 0x3F, DisplayTime = 0xFF, NeedMore = true, Text = "1. Go" }));
        Assert.Empty(Written(new RequestState()));
    }

    [Fact]
    public void AKeyHintCountOverTheMostIsRefusedWhole()
    {
        var handler = new KeyHintTextMessage();
        int fired = 0;
        handler.Fired += _ => fired++;

        ((IUserMessage)handler).OnReceived(Reader([9, .. Enumerable.Repeat((byte)0, 9)]));
        Assert.Equal(0, fired);

        ((IUserMessage)handler).OnReceived(Reader([8, .. Enumerable.Repeat((byte)0, 8)]));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void AnEmptyHudTextIsNotShown()
    {
        var handler = new HudTextMessage();
        int fired = 0;
        handler.Fired += _ => fired++;

        ((IUserMessage)handler).OnReceived(Reader([0]));
        Assert.Equal(0, fired);
    }

    [Fact]
    public void TheGoldenHudMsgDecodes()
    {
        var handler = new HudMsgMessage();
        HudMessageArgs? got = null;
        handler.Fired += a => got = a;

        ((IUserMessage)handler).OnReceived(Reader(HudMsg));

        Assert.Equal(new HudMessageArgs(3, -1f, 0.5f, new Color32(1, 2, 3, 4), new Color32(5, 6, 7, 8),
                                        2, 1f, 0.5f, 2f, 0.25f, "Hi"), got);
    }

    [Fact]
    public void TheGoldenHudTextDecodes()
    {
        var handler = new HudTextMessage();
        string? got = null;
        handler.Fired += t => got = t;

        ((IUserMessage)handler).OnReceived(Reader(HudText));

        Assert.Equal("GAMESAVED", got);
    }

    [Fact]
    public void TheGoldenKeyHintTextDecodes()
    {
        var handler = new KeyHintTextMessage();
        List<string>? got = null;
        handler.Fired += h => got = h.ToList();

        ((IUserMessage)handler).OnReceived(Reader(KeyHintText));

        Assert.Equal(["Press E"], got);
    }

    [Fact]
    public void TheGoldenDamageDecodes()
    {
        var handler = new DamageMessage();
        DamageArgs? got = null;
        handler.Fired += a => got = a;

        ((IUserMessage)handler).OnReceived(Reader(Damage));

        Assert.Equal(new DamageArgs(10, 20, 8, new Vector(1f, 2f, 0.5f)), got);   // the origin reaches the HUD (plan_client_ui U3)
        Assert.Equal(Damage, Written(new Damage { Armour = 10, Health = 20, DamageBits = 8, Origin = new Vector(1f, 2f, 0.5f) }));
    }

    [Fact]
    public void TheGoldenLayoutDecodes()
    {
        var handler = new LayoutCheckMessage();
        ((IUserMessage)handler).OnReceived(Reader(Layout));

        Assert.Equal((0x12345678u, 7, 0xDEADBEEFu, 1u, 2u, 3u),
                     (handler.Fingerprint, handler.LayoutCount, handler.BlockFingerprint,
                      handler.StringTablesHash, handler.TempEntsHash, handler.NetStatesHash));
        Assert.Equal(Layout, Written(new LayoutCheck
        {
            Fingerprint = 0x12345678u, LayoutCount = 7, BlockFingerprint = 0xDEADBEEFu,
            StringTablesHash = 1, TempEntsHash = 2, NetStatesHash = 3,
        }));
    }

    [Fact]
    public void TheScreenPayloadsWriteTheirPinnedLayouts()
    {
        Assert.Equal(Shake, Written(ScreenEffects.ShakeOf(ShakeCommand.Start, 4f, 1f, 2f)));
        Assert.Equal(Fade, Written(ScreenEffects.FadeOf(1f, 2f, FadeFlags.In, 1, 2, 3, 4)));
        Assert.Equal(Reconnect, Written(new Reconnect()));
    }

    [Fact]
    public void TheGoldenNativeOnlyMessagesDecode()
    {
        var geiger = new GeigerMessage();
        ((IUserMessage)geiger).OnReceived(Reader(Geiger));
        Assert.Equal(5, geiger.Last.Range);
        Assert.Equal(Geiger, Written(new Geiger { Range = 5 }));

        var train = new TrainMessage();
        ((IUserMessage)train).OnReceived(Reader(Train));
        Assert.Equal(3, train.Last.Position);
        Assert.Equal(Train, Written(new Train { Position = 3 }));

        var text = new TextMsgMessage();
        ((IUserMessage)text).OnReceived(Reader(TextMsg));
        Assert.Equal((4, "#Game_x", "p"), ((int)text.Last.Destination, text.Last.Text, Assert.Single(text.Last.Params)));
        Assert.Equal(TextMsg, Written(new TextMsg { Destination = 4, Text = "#Game_x", Params = ["p"] }));

        var pickup = new ItemPickupMessage();
        ((IUserMessage)pickup).OnReceived(Reader(ItemPickup));
        Assert.Equal("item_battery", pickup.Last.Item);
        Assert.Equal(ItemPickup, Written(new ItemPickup { Item = "item_battery" }));

        var battery = new BatteryMessage();
        ((IUserMessage)battery).OnReceived(Reader(Battery));
        Assert.Equal(100, battery.Last.Value);
        Assert.Equal(Battery, Written(new Battery { Value = 100 }));

        var denied = new AmmoDeniedMessage();
        ((IUserMessage)denied).OnReceived(Reader(AmmoDenied));
        Assert.Equal(7, denied.Last.AmmoType);
        Assert.Equal(AmmoDenied, Written(new AmmoDenied { AmmoType = 7 }));

        var say = new SayTextMessage();
        ((IUserMessage)say).OnReceived(Reader(SayText));
        Assert.Equal((0, "hello\n", false), (say.Last.Client, say.Last.Text, say.Last.Chat));
    }
}
