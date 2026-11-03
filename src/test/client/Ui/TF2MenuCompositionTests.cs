using System.Reflection;
using System.Runtime.Loader;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Vgui;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

using TF2.Client.Ui;
using TF2.Shared;
using TF2.Shared.Player;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The TF2 menus as the live game composes them (stage 3.2): the real loader
/// over the container the module has when it calls <c>Load</c> — the mod's
/// <c>client_ui</c> screens, their sources and the menu navigation, the class
/// menu's convars published. The composition builds every screen's source
/// through the container, so a screen whose source chain asks for the loader
/// (the team and class menus' navigation does) can only build when the loader
/// is registered by then; when it was not, the composition threw into the
/// loader's caught arm, the log said <c>the UI did not load</c> naming
/// <see cref="TFMenuNavigation"/>, and the live HUD never painted.
/// </summary>
public sealed class TF2MenuCompositionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ss-tf2menu-").FullName;
    private readonly RecordingEngine _engine = Fake().Returns("Hud.LocalPlayerHealth", 80).Returns("Clock.Now", 3f)
        .Returns("Hud.LocalPlayerIndex", 1).Returns("Console.FindConVarValue", "test server")
        .Returns("Hud.Localise", "").Returns("Environment.Globals", default(Globals))
        // The menu's Play reads the mod's maplist off the GAME paths (the
        // composition asks once, at the build): this mod names ctf_2fort.
        .Returns("Files.ReadGameFileBytes", "ctf_2fort"u8.ToArray());

    private readonly Ui.UiFidelityTests.RecordingSurface _surface = new();

    public TF2MenuCompositionTests()
    {
        // The UI's assemblies beside the tests, as a deploy puts them in the
        // mod's managed dir the loader reads (the UiLoaderTests way).
        string from = AppContext.BaseDirectory;
        foreach (string name in UiLoader.Assemblies.Concat([ModUiAssembly]))
            foreach (string ext in new[] { ".dll", ".pdb" })
                if (File.Exists(Path.Combine(from, name + ext)))
                    File.Copy(Path.Combine(from, name + ext), Path.Combine(_dir, name + ext));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>No players at all: every user id is gone (the composition
    /// only needs the service registered).</summary>
    private sealed class EmptyPlayers : IKillFeedPlayers
    {
        public bool TryFind(int userId, out string name, out int team, out bool local)
        {
            (name, team, local) = ("", 0, false);
            return false;
        }
    }

    /// <summary>No prediction: every weapon reads its decoded state (the
    /// meter then rides the carrier the decode brought).</summary>
    private sealed class NoPrediction : IWeaponDrawStates
    {
        public WeaponNetState? PredictedStateOf(ClientSsWeapon weapon) => null;
    }

    /// <summary>The combat text's hud projection, still: the risen z comes
    /// back as x and the y as y, and nothing else is asked of it.</summary>
    private sealed class StillProjector : IClientBeamExecutor
    {
        public Vector ScreenPosition(in Vector p) => new(p.Z, p.Y, 0f);

        public void View(bool insideView, Span<float> out42) { }

        public bool ModelInfo(int modelIndex, out int frameCount)
        {
            frameCount = 0;
            return false;
        }

        public int ModelIndex(string name) => -1;

        public EntityHandle EntityAt(int entry) => EntityHandle.None;

        public bool EntityHasModel(EntityHandle entity) => false;

        public int EntityFacts(EntityHandle entity, int attachment, bool tryAttachment, Span<float> out9) => 0;

        public void BoxVisibility(in Vector mins, in Vector maxs, out bool visible, out bool culled)
        {
            visible = false;
            culled = false;
        }

        public int RenderableAdd(in Vector origin, in Vector mins, in Vector maxs) => 0;

        public void RenderableChanged(int renderable, in Vector origin, in Vector mins, in Vector maxs) { }

        public void RenderableRemove(int renderable) { }

        public bool BindSprite(int modelIndex, int frame, int renderMode, bool wireframe) => false;

        public void SetHdrColorScale(int modelIndex, int renderMode, float scale) { }

        public bool Glow(int modelIndex, bool bind) => false;

        public void Strip(ReadOnlySpan<float> vertices) { }

        public void Mesh(int primitive, int primitives, int colorKind, ReadOnlySpan<float> vertices) { }
    }

    /// <summary>The TF2 mod's own UI assembly, as mods.yaml's <c>client_ui</c>
    /// names it and ClientModule hands it to both the loader and the composition.</summary>
    private const string ModUiAssembly = "TF2.Client.Ui";

    [Fact]
    public void The_ui_loads_with_the_TF2_menus_as_the_game_registers_them()
    {
        var hud = new Hud(new RecordingLog(), _engine, _engine, new DamageMessage(), new HudMsgMessage(),
                          new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());
        var services = UiHostKit.RuntimeServices(_engine, _surface, _dir,
            k => k == ModInfo.Keys.ClientUiAssemblies ? ModUiAssembly : null);

        // What the module's container holds when it reaches the UI section,
        // added as the composition asks them: the forwarders' engine client,
        // every convar the TF2 assemblies declare (ConVarRegistry.PublishTo, as
        // the module does, so a panel reading a new one needs no edit here),
        // the log and file access the
        // side services call, and the arsenal.
        services.AddInstance<IFwdEngineClient>(new DefaultForwarders.NoEngineClient());
        // ClientModule's slice of the same engine client: the menu background's name.
        services.AddInstance<IMenuBackgroundName>(new DefaultForwarders.NoEngineClient());
        var convars = ConVarRegistry.For(new RecordingEngine { Strict = false }.Returns("Console.FindConVarFlags", -1));
        convars.RegisterTypes([.. typeof(TFClassLimitConVar).Assembly.GetTypes(), .. typeof(TFHudFlag).Assembly.GetTypes(),
                               .. typeof(ClMpTimeLimitConVar).Assembly.GetTypes()]);
        convars.PublishTo(services);
        services.AddInstance<ILog>(new RecordingLog()).AddInstance<IEngineFiles>(_engine);
        services.AddInstance<ILocalArsenal>(new UiHostKit.NoArsenal());
        // The medic charge meter's predicted carrier (the module registers the
        // arsenal, which answers both): the composition's fake predicts nothing.
        services.AddInstance<IWeaponDrawStates>(new NoPrediction());
        // The weapon table, as the module registers it: the core rows plus
        // whatever the registered sources append, built on the first read.
        services.AddInstance(new WeaponCatalog(() => services.ResolveAll<IWeaponTypeSource>(),
                                               new RecordingLog()));
        // The kill feed's player resolver (the module registers KillFeedPlayers):
        // the composition's fake answers the empty resource.
        services.AddInstance<IKillFeedPlayers>(new EmptyPlayers());
        // The target ID's view slots (the module registers the real engine's):
        // the fake throws on a query the screen never makes.
        services.AddInstance<IPixelVisSlots>(new ViewSlots());
        // The damage indicator's frame view (the module registers the engine's):
        // a still camera; the adapter only reads it at a hit.
        services.AddInstance<ICurrentView>(new StillView());
        // The damage indicator's and combat text's message-time main view (the
        // module registers the engine's, by ClientEntityFetches before the side
        // services): every member its default; the composition only builds them.
        services.AddInstance<IFxRender>(
            (IFxRender)DispatchProxy.Create(typeof(IFxRender), typeof(FakeClientEngineAdapters.DefaultAnswers)));
        // The damage indicator's noise draws (the module registers the engine's
        // stream): the lowest draw, deterministic and in range.
        services.AddInstance<IUniformRandom>(new DefaultForwarders.LowestRandom());
        // ClientModule's wall clock: the main menu's holiday art reads it.
        services.AddInstance<TimeProvider>(TimeProvider.System);
        // The combat text's projection (the module registers the engine's):
        // the still projector answers the one read the paint makes.
        services.AddInstance<IClientBeamExecutor>(new StillProjector());
        services.AddInstance(new ViewReadout());
        services.AddInstance(NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog()));
        // The game events' own objects, as the module's container holds them
        // before its UI section (ClientModule): the death link's EventLink
        // takes the death event through its constructor.
        GameEventRegistry.AddEventsTo(services, typeof(PlayerDeathEvent).Assembly, new RecordingLog());
        // Every engine area the game's composition holds (the fake answers them all).
        foreach (Type area in _engine.GetType().GetInterfaces().Where(i => i.Namespace == "SourceSharp.Abi" && i.Name.StartsWith("IEngine")))
            if (!services.IsRegistered(area))
                services.AddInstance(area, _engine);
        SideServices.Register(services, Side.Client, typeof(TFMenuNavigation).Assembly.GetTypes());

        var runtime = new UiRuntimeServices(services, new RecordingLog(), hud, new UiHostKit.NoArsenal(), null,
                                            new DefaultForwarders.NoEngineClient(), new DefaultForwarders.NoInputSystem());
        var log = new RecordingLog();
        var loader = new UiLoader(log, _engine, _dir, () => runtime,
                                  parent: AssemblyLoadContext.Default, modAssemblies: [ModUiAssembly]);

        Assert.True(loader.Load(),
            "the UI loaded with the TF2 team and class menus on its screens; the loader logged: " +
            string.Join(" / ", log.Lines.Select(l => l.Text.TrimEnd())));
        Assert.True(loader.Current!.Has("tf2teammenu"), "the team menu is on the layer");
        Assert.True(loader.Current.Has("tf2classmenu"), "and the class menu");
        Assert.True(loader.Current.Has("tf2textwindow"), "and the text window, whose motd data the container answers for");
        Assert.True(loader.Current.Has("tf2mainmenu"),
                    "and the main menu, over the core's IMainMenuSource and TF2's art and actions");
        loader.Dispose();
    }

    private sealed class StillView : ICurrentView
    {
        public CurrentViewState Read() => new(0, new Vector(0f, 0f, 64f), new Vector(1f, 0f, 0f),
                                             new Vector(0f, 1f, 0f), new Vector(0f, 0f, 1f));
    }
}
