using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The UI's own load context (plan_client_ui U5): the toolkit, the screens and
/// the UI host loaded collectibly from a directory, swapped with their state
/// carried, the old context collected, and a deploy classified as UI-only or
/// as needing the full client reload. Over a copy of the three assemblies in a
/// directory of the fact's own.
/// </summary>
[Collection(ProcessWide.Name)]
public sealed class UiLoaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ss-uiloader-").FullName;
    private readonly RecordingEngine _engine = Fake().Returns("Hud.LocalPlayerHealth", 80).Returns("Clock.Now", 3f)
        .Returns("Hud.LocalPlayerIndex", 1).Returns("Console.FindConVarValue", "test server")
        // The scoreboard's maxClients load (docs/todo.md): unbound here, so the
        // header draws the array ceiling like before the read existed.
        .Returns("Environment.Globals", default(Globals));

    public UiLoaderTests()
    {
        string from = AppContext.BaseDirectory;
        foreach (string name in UiLoader.Assemblies)
            foreach (string ext in new[] { ".dll", ".pdb" })
                if (File.Exists(Path.Combine(from, name + ext)))
                    File.Copy(Path.Combine(from, name + ext), Path.Combine(_dir, name + ext));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private readonly Ui.UiFidelityTests.RecordingSurface _surface = new();

    private UiLoader Loader(ClDrawHudConVar? drawHud = null)
    {
        var hud = new Hud(new RecordingLog(), _engine, _engine, new DamageMessage(), new HudMsgMessage(),
                          new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());
        var services = new ServiceContainer()
            .AddInstance<IEngineHud>(_engine).AddInstance<IEngineConsole>(_engine).AddInstance<IGameClock>(_engine)
            .AddInstance<IModInfo>(new ModInfo(k => k == ModInfo.Keys.ManagedDir || k == ModInfo.Keys.GameDir ? _dir : null))
            .AddInstance<IUiSurface>(_surface)
            .AddInstance<IClientStandalones>(new UiHostKit.NoStandalones())
            .AddInstance<IPlayerNames>(new EnginePlayerNames(() => null, new IdentityNameFilter()))
            .AddInstance<IEngineEnvironment>(_engine)
            .AddInstance<Client.Ui.IKillFeedSource>(new NoKills())
            .AddInstance<Client.Ui.ICentrePrintSource>(new CentrePrint(_engine))
            .AddInstance<Client.Ui.IChatSource>(new ChatFeed(_engine, new RecordingLog(), _engine, new NoChatPlayers()))
            .AddInstance<Client.Ui.IChatInputSource>(new ChatInputUi(() => null))
            .AddInstance<Client.Ui.ILoadingDiscSource>(new LoadingDisc(_engine))
            .AddInstance<Client.Ui.IDebugOverlaySource>(new NoOverlay())
            .AddInstance<Client.Ui.IVoiceSource>(new VoiceSpeakers(new NoVoicePlayers()));
        var observer = new ObserverState(new NoObserverFields(), new NoSpectated(), () => null);
        services.AddInstance<Client.Ui.IObserverSource>(observer).AddInstance<Client.Ui.ISpectatorTargetSource>(observer);
        var s = new UiRuntimeServices(services, new RecordingLog(), hud, new UiHostKit.NoArsenal(), null, () => null, () => null);
        return new UiLoader(new RecordingLog(), _engine, _dir, () => s, parent: AssemblyLoadContext.Default) { DrawHud = drawHud };
    }

    private sealed class NoChatPlayers : IChatPlayers
    {
        public bool TryFind(int client, out string name, out int team) => ((name, team) = ("", 0)) == default;
    }

    private sealed class NoSpectated : ISpectatedPlayers
    {
        public bool TryFind(int client, out string name, out int team, out int health)
        {
            (name, team, health) = ("", 0, 0);
            return false;
        }
    }

    private sealed class NoVoicePlayers : IVoicePlayers
    {
        public bool TryFind(int client, out string name, out int team, out bool alive)
        {
            (name, team, alive) = ("", 0, true);
            return false;
        }
    }

    private sealed class NoOverlay : Client.Ui.IDebugOverlaySource
    {
        public int Count => 0;

        public Client.Ui.DebugOverlayLine Line(int i) => throw new ArgumentOutOfRangeException(nameof(i));
    }

    private sealed class NoKills : Client.Ui.IKillFeedSource
    {
        public int LineCount => 0;

        public Client.Ui.KillFeedLine Line(int i) => throw new ArgumentOutOfRangeException(nameof(i));
    }

    private static IReadOnlyList<string> Open(UiLoader l) => l.Current!.Debug.ReadMenu(false).Open ?? [];

    [Fact]
    public void With_cl_drawhud_off_the_ui_paints_nothing()
    {
        _engine.ReturnsEachCall("Hud.Localise", () => (string)_engine.Calls[^1].Arg(0)!);
        var drawHud = new ClDrawHudConVar();
        drawHud.NotifyChanged("1", "0");
        var l = Loader(drawHud);
        l.Load();
        l.Current!.Open("scoreboard");
        // The menu layer, which this fixture can paint (its HUD sources are stubs); the gate is the same for both.
        l.Paint(1, 1920, 1080);
        Assert.True(_surface.Draws > 0, "control: cl_drawhud 1 draws");
        int before = _surface.Draws;
        drawHud.NotifyChanged("0", "1");
        l.Paint(1, 1920, 1080);
        Assert.Equal(before, _surface.Draws);
        l.Dispose();
    }

    [Fact]
    public void The_ui_loads_in_a_collectible_context_of_its_own()
    {
        var l = Loader();
        Assert.True(l.Load());
        var context = AssemblyLoadContext.GetLoadContext(l.Current!.GetType().Assembly)!;
        Assert.True(context.IsCollectible);
        Assert.NotSame(AssemblyLoadContext.Default, context);
        Assert.NotSame(typeof(UiHost), l.Current.GetType());       // not the copy this fact's own context holds
        l.Dispose();
    }

    [Fact]
    public void A_reload_swaps_the_context_and_the_open_screen_stays_open()
    {
        var l = Loader();
        l.Load();
        l.Current!.Open("scoreboard");
        string? first = l.ContextName;

        var r = l.Reload("command")!;
        Assert.NotEqual(first, l.ContextName);
        Assert.Equal(("ui-only", 1, 2), (r.Kind, r.Sequence, r.Reopened));     // the scoreboard and the always-open plaque
        Assert.Contains("scoreboard", Open(l));
        l.Dispose();
    }

    [Fact]
    public void Without_the_carry_a_new_ui_opens_nothing()
    {
        var l = Loader();
        l.Load();
        Assert.Equal(["loadingdisc"], Open(l));     // only the screen that is open by default
        l.Dispose();
    }

    [Fact]
    public void The_old_context_is_collected()
    {
        var l = Loader();
        l.Load();
        Assert.Equal("pending", l.Reload("command")!.OldContext);
        Assert.Equal("collected", l.SettleOldContext());
        Assert.Equal("collected", l.Report()!.OldContext);
        l.Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object Hold(UiLoader l) => l.Current!;

    [Fact]
    public void A_ui_assembly_changed_on_disk_is_ui_only_and_reloads()
    {
        var l = Loader();
        l.Load();
        Assert.Null(l.CheckDeploy());
        File.AppendAllBytes(Path.Combine(_dir, "SourceSharp.Client.Ui.dll"), [0, 0, 0, 0]);   // new bytes, still loadable
        Assert.Null(l.CheckDeploy());                    // first look: still being written
        Assert.Equal("ui-only", l.CheckDeploy());        // second look: the same, so classified
        Assert.Equal(1, l.Sequence);
        Assert.Equal(["SourceSharp.Client.Ui"], l.Report()!.DeployChanged);
        l.Dispose();
    }

    [Fact]
    public void A_ui_assembly_rewritten_with_the_same_bytes_is_not_a_deploy()
    {
        var l = Loader();
        l.Load();
        string path = Path.Combine(_dir, "SourceSharp.Client.Ui.Toolkit.dll");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.Null(l.CheckDeploy());
        Assert.Null(l.CheckDeploy());
        Assert.Equal(0, l.Sequence);
        l.Dispose();
    }

    [Fact]
    public void Another_assembly_changed_needs_the_full_reload()
    {
        var l = Loader();
        l.Load();
        File.WriteAllBytes(Path.Combine(_dir, "SourceSharp.Client.dll"), [1, 2, 3]);
        l.CheckDeploy();
        Assert.Equal("full", l.CheckDeploy());
        Assert.Equal(0, l.Sequence);
        Assert.Equal(["SourceSharp.Client"], l.Report()!.DeployChanged);
        l.Dispose();
    }
}
