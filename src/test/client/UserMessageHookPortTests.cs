using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The six stock HOOK_MESSAGE functions the client compiled -- view_effects.cpp's
/// Shake and Fade, clientmode_shared.cpp's Rumble and VGUIMenu, voice_status.cpp's
/// VoiceMask and RequestState -- ported into the managed handlers (user ruling
/// 2026-10-04: all user messages in managed code). The handlers decide; the
/// client's native state is reached through IClientMessageEffects.
/// </summary>
public class UserMessageHookPortTests
{
    private sealed class Effects : IClientMessageEffects
    {
        public ViewportFacts Facts { get; set; } = ViewportFacts.PanelExists | ViewportFacts.InfoPanelAllowed;
        public List<string> Calls { get; } = new();
        public List<(string Panel, bool Show, List<KeyValuePair<string, string>> Data, ViewportActions Actions)> Shown { get; } = new();

        public void Shake(Shake m) => Calls.Add($"shake {m.Command} {m.Amplitude} {m.Frequency} {m.Duration}");
        public void Fade(Fade m) => Calls.Add($"fade {m.Duration} {m.HoldTime} {m.Flags} {m.Color}");
        public void Rumble(Rumble m) => Calls.Add($"rumble {m.Waveform} {m.Data} {m.Flags}");
        public ViewportFacts ViewportQuery(string panel) { Calls.Add($"query {panel}"); return Facts; }
        public void ViewportShow(string panel, bool show, IReadOnlyList<KeyValuePair<string, string>> data, ViewportActions actions)
            => Shown.Add((panel, show, data.ToList(), actions));
        public void VoiceMasks(VoiceMask m) => Calls.Add($"voice {m.Audible0} {m.Banned0} {m.Audible1} {m.Banned1} {m.ModEnable}");
        public void VoiceRequestState() => Calls.Add("voice-request");
    }

    private static byte[] Written<T>(T payload) where T : IUserMessagePayload<T>
    {
        var buffer = new byte[UserMessageSender.MaxPayloadBytes];
        int n = payload.Write(buffer);
        Assert.True(n >= 0);
        return buffer[..n];
    }

    private static void Dispatch<T>(IUserMessage handler, T payload) where T : IUserMessagePayload<T>
        => handler.OnReceived(new UserMessageReader(Written(payload)));

    /// <summary>The handler's log: the composition's ILog in the game.</summary>
    private readonly RecordingLog _log = new();

    private List<string> Warnings(Action act)
    {
        int before = _log.Lines.Count;
        act();
        return _log.Lines.Skip(before).Where(l => l.Warning).Select(l => l.Text).ToList();
    }

    //-------------------------------------------------------------------------
    // The four that only carry values to native state.
    //-------------------------------------------------------------------------

    [Fact]
    public void ShakeReachesTheViewEffects()
    {
        var fx = new Effects();
        Dispatch(new ShakeMessage(fx), new Shake { Command = 2, Amplitude = 4f, Frequency = 1f, Duration = 2f });
        Assert.Equal(["shake 2 4 1 2"], fx.Calls);
    }

    [Fact]
    public void FadeReachesTheViewEffects()
    {
        var fx = new Effects();
        Dispatch(new FadeMessage(fx), new Fade { Duration = 512, HoldTime = 1024, Flags = 1, Color = new Color32(1, 2, 3, 4) });
        Assert.Equal([$"fade 512 1024 1 {new Color32(1, 2, 3, 4)}"], fx.Calls);
    }

    [Fact]
    public void RumbleReachesTheRumbleEffect()
    {
        var fx = new Effects();
        Dispatch(new RumbleMessage(fx), new Rumble { Waveform = 7, Data = 8, Flags = 9 });
        Assert.Equal(["rumble 7 8 9"], fx.Calls);
    }

    [Fact]
    public void VoiceMaskAndRequestStateReachTheVoiceStatus()
    {
        var fx = new Effects();
        Dispatch(new VoiceMaskMessage(fx), new VoiceMask { Audible0 = 1, Banned0 = 2, Audible1 = 3, Banned1 = 4, ModEnable = true });
        Dispatch(new RequestStateMessage(fx), new RequestState());
        Assert.Equal(["voice 1 2 3 4 True", "voice-request"], fx.Calls);
    }

    [Fact]
    public void AShortShakeReachesNothing()
    {
        var fx = new Effects();
        ((IUserMessage)new ShakeMessage(fx)).OnReceived(new UserMessageReader(new byte[12]));
        Assert.Empty(fx.Calls);
    }

    //-------------------------------------------------------------------------
    // VGUIMenu: every decision MsgFunc_VGUIMenu made.
    //-------------------------------------------------------------------------

    private static (Effects Fx, RecordingEngine Console) Menu(ViewportFacts facts, string? takesShots = "0")
    {
        var console = Mock();
        console.Returns("Console.FindConVarValue", takesShots);
        return (new Effects { Facts = facts }, console);
    }

    private const ViewportFacts Both = ViewportFacts.PanelExists | ViewportFacts.InfoPanelAllowed;

    [Fact]
    public void APanelTheViewportDoesNotHaveIsIgnored()
    {
        var (fx, console) = Menu(ViewportFacts.InfoPanelAllowed);
        Dispatch(new VguiMenuMessage(fx, console, _log), new VguiMenu { Name = "team", Show = true });
        Assert.Empty(fx.Shown);
    }

    [Fact]
    public void APanelWithNoDataIsShownWithoutData()
    {
        var (fx, console) = Menu(Both);
        Dispatch(new VguiMenuMessage(fx, console, _log), new VguiMenu { Name = "team", Show = false });

        var only = Assert.Single(fx.Shown);
        Assert.Equal(("team", false, ViewportActions.Show), (only.Panel, only.Show, only.Actions));
        Assert.Empty(only.Data);
    }

    [Fact]
    public void AnMotdUrlThatIsNotHttpIsBlockedAndSaid()
    {
        var (fx, console) = Menu(Both);
        var warnings = Warnings(() => Dispatch(new VguiMenuMessage(fx, console, _log),
            new VguiMenu { Name = "info", Show = true, Keys = ["type", "msg"], Values = ["2", "file:///etc/passwd"] }));

        Assert.Empty(fx.Shown);
        Assert.Contains(warnings, w => w.Contains("Blocking MOTD URL 'file:///etc/passwd'"));
    }

    [Fact]
    public void AnHttpsMotdIsHandedOnAndMarkedDisplayed()
    {
        var (fx, console) = Menu(Both);
        Dispatch(new VguiMenuMessage(fx, console, _log),
            new VguiMenu { Name = "INFO", Show = true, Keys = ["type", "msg"], Values = ["2", "https://example.com/"] });

        var only = Assert.Single(fx.Shown);
        Assert.Equal(ViewportActions.SetData | ViewportActions.InfoPanelDisplayed | ViewportActions.Show, only.Actions);
        Assert.Equal([new("type", "2"), new("msg", "https://example.com/")], only.Data);
    }

    [Fact]
    public void TheWhitelistIsStocks()
    {
        Assert.True(VguiMenuMessage.IsAllowedMotdUrl("http://a"));
        Assert.True(VguiMenuMessage.IsAllowedMotdUrl("https://a"));
        Assert.True(VguiMenuMessage.IsAllowedMotdUrl("ABOUT:BLANK"));
        Assert.False(VguiMenuMessage.IsAllowedMotdUrl("HTTP://a"));   // Q_strncmp is case-sensitive
        Assert.False(VguiMenuMessage.IsAllowedMotdUrl("ftp://a"));
        Assert.False(VguiMenuMessage.IsAllowedMotdUrl("https:a"));      // the prefix is the whole scheme and both slashes
        Assert.False(VguiMenuMessage.IsAllowedMotdUrl("http:/a"));
        Assert.False(VguiMenuMessage.IsAllowedMotdUrl(""));
    }

    [Fact]
    public void AnInfoPanelTheClientModeRefusesGetsItsDataButIsNotShown()
    {
        var (fx, console) = Menu(ViewportFacts.PanelExists);
        Dispatch(new VguiMenuMessage(fx, console, _log),
            new VguiMenu { Name = "info", Show = true, Keys = ["title"], Values = ["Welcome"] });

        var only = Assert.Single(fx.Shown);
        Assert.Equal(ViewportActions.SetData, only.Actions);
    }

    [Fact]
    public void TheScoreboardFiresTheScreenshotEventAndTakesOneWhenAsked()
    {
        var (fx, console) = Menu(Both, takesShots: "1");
        Dispatch(new VguiMenuMessage(fx, console, _log), new VguiMenu { Name = "scores", Show = true });
        Assert.Equal(ViewportActions.ScreenShot | ViewportActions.ScreenShotEvent | ViewportActions.Show, Assert.Single(fx.Shown).Actions);

        var (fx0, console0) = Menu(Both, takesShots: "0");
        Dispatch(new VguiMenuMessage(fx0, console0, _log), new VguiMenu { Name = "scores", Show = true });
        Assert.Equal(ViewportActions.ScreenShotEvent | ViewportActions.Show, Assert.Single(fx0.Shown).Actions);
    }

    [Fact]
    public void ANameSetTwiceKeepsItsLastValue()
    {
        var data = VguiMenuMessage.DataOf(new VguiMenu { Keys = ["a", "b", "A"], Values = ["1", "2", "3"] });
        Assert.Equal([new("a", "3"), new("b", "2")], data);
    }

    //-------------------------------------------------------------------------
    // The client's half, and its wiring.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheClientsEffectsReachTheirServicesAndOnlyTheViewportQueryIsASlot()
    {
        var engine = Mock();
        engine.Returns("Hud.ViewportQuery", ViewportFacts.PanelExists);
        var rumbles = new List<Rumble>();
        var fx = new ClientMessageEffects(engine, null, rumble: rumbles.Add);

        fx.Shake(new Shake { Command = 1, Amplitude = 2f, Frequency = 3f, Duration = 4f });
        fx.Fade(new Fade { Duration = 5, HoldTime = 6, Flags = 7, Color = new Color32(8, 9, 10, 11) });
        fx.Rumble(new Rumble { Waveform = 1, Data = 2, Flags = 3 });
        Assert.Equal(ViewportFacts.PanelExists, fx.ViewportQuery("team"));

        // Rumble's effect is the managed mixer's (lane c6-input), and Shake's
        // and Fade's the managed view effects' (C7): none is a slot now.
        Assert.Equal(["Hud.ViewportQuery"], engine.Calls.Select(c => c.Member));
        Assert.Equal([new Rumble { Waveform = 1, Data = 2, Flags = 3 }], rumbles);
    }

    private sealed class VoiceEngine : Client.Voice.IVoiceEngine
    {
        public readonly List<string> Sent = [];

        public string? GuidOf(int client) => null;

        public void ServerCommand(string line, bool reliable) => Sent.Add(line);

        public void EnableVoice() { }
    }

    private sealed class NoBanFile : Client.Voice.IVoiceBanFile
    {
        public byte[]? Read() => null;

        public void Write(byte[] file) { }
    }

    [Fact]
    public void VoiceMaskAndRequestStateReachTheManagedVoiceManagerAndNoNativeSlot()
    {
        var engine = Mock();
        var voiceEngine = new VoiceEngine();
        var voice = new Client.Voice.VoiceStatus(voiceEngine, new FixedClock(), new RecordingLog(), new NoBanFile())
        {
            LevelInitialized = true,
        };
        var fx = new ClientMessageEffects(engine, voice: () => voice);

        fx.VoiceMasks(new VoiceMask { Audible0 = 1, Banned0 = 2, Audible1 = 3, Banned1 = 4, ModEnable = true });
        Assert.True(voice.IsAudible(1));
        fx.VoiceRequestState();
        Assert.Equal(["VModEnable 1", "vban 0 0"], voiceEngine.Sent);
        Assert.Empty(engine.Calls);
    }

    private sealed class FixedClock : IGameClock
    {
        public float Now => 5f;
    }

    [Fact]
    public void TheContainerHandsTheHandlersTheClientsEffects()
    {
        var fx = new Effects();
        var services = new ServiceContainer().AddInstance<IClientMessageEffects>(fx);
        UserMessageRegistry.AddHandlersTo(services, typeof(ShakeMessage).Assembly, new RecordingLog());

        Dispatch(services.Require<ShakeMessage>(), new Shake { Command = 0, Amplitude = 1f });
        Assert.Single(fx.Calls);
    }
}
