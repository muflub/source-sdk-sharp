using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;
using SourceSharp.Tests.ModUi;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// A mod's own screens (stage 0.9): every UiScreen in the UI assemblies
/// mods.yaml lists is built through the container and placed on the layer it
/// names, after the core's, by Order. Over SourceSharp.Tests.ModUi, a UI
/// assembly of the facts' own with one HUD screen.
/// </summary>
public sealed class UiModScreensTests : IDisposable
{
    private const string Fixture = "SourceSharp.Tests.ModUi";

    private readonly string _dir = Directory.CreateTempSubdirectory("ss-uimod-").FullName;
    private readonly UiFidelityTests.RecordingSurface _surface = new();
    private readonly RecordingEngine _engine = Fake().Returns("Hud.LocalPlayerHealth", 80).Returns("Clock.Now", 3f)
        .Returns("Hud.LocalPlayerIndex", 1).Returns("Console.FindConVarValue", "test server")
        .Returns("Environment.Globals", default(Globals));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Assembly FixtureAssembly => typeof(ModProbe).Assembly;

    /// <summary>The game's composition, with mods.yaml's client_ui list as <paramref name="uiAssemblies"/>.</summary>
    private UiHost Compose(params string[] uiAssemblies)
    {
        var services = UiHostKit.RuntimeServices(_engine, _surface, _dir,
                                                 k => k == ModInfo.Keys.ClientUiAssemblies ? string.Join(';', uiAssemblies) : null);
        var hud = new Hud(new RecordingLog(), _engine, _engine, new DamageMessage(), new HudMsgMessage(),
                          new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());
        return ClientUi.Build(services, new RecordingLog(), hud, hud.Messages, hud.Hints, hud.Build, new UiHostKit.NoArsenal(),
                              null, new DefaultForwarders.NoEngineClient(), new DefaultForwarders.NoInputSystem());
    }

    [Fact]
    public void A_mod_hud_screen_lands_in_the_hud_layer()
    {
        Assert.False(Compose().Layer.Has("modprobe"));            // control: no mod list, no mod screen
        var host = Compose(Fixture);
        Assert.True(host.Layer.Has("modprobe"));
        Assert.False(host.Menu.Has("modprobe"));
        Assert.True(host.Layer.Has("hud") && host.Menu.Has("scoreboard"));   // the core's are still there
    }

    private sealed class Screen(string name, UiLayerKind layer, int order) : UiScreen
    {
        public override string Name => name;

        public override UiLayerKind Layer => layer;

        public override int Order => order;

        public override void Describe(ref UiFrame ui) { }
    }

    private static string[] Names(IEnumerable<UiScreen> screens) => [.. screens.Select(s => s.Name)];

    [Fact]
    public void A_mod_screen_on_any_other_layer_lands_in_the_menu_layer()
    {
        var (hud, menu) = UiModScreens.Place([], [], [new Screen("modmenu", UiLayerKind.Menu, 0)]);
        Assert.Empty(hud);
        Assert.Equal(["modmenu"], Names(menu));
    }

    [Fact]
    public void Core_screens_keep_their_order_and_the_mods_follow_by_order()
    {
        var (hud, menu) = UiModScreens.Place(
            [new Screen("core-b", UiLayerKind.Hud, 50), new Screen("core-a", UiLayerKind.Hud, 0)],
            [new Screen("core-menu", UiLayerKind.Menu, 9)],
            [new Screen("late", UiLayerKind.Hud, 30), new Screen("menu-mod", UiLayerKind.Menu, 1),
             new Screen("early", UiLayerKind.Hud, 10), new Screen("tie", UiLayerKind.Hud, 10)]);
        Assert.Equal(["core-b", "core-a", "early", "tie", "late"], Names(hud));
        Assert.Equal(["core-menu", "menu-mod"], Names(menu));
    }

    [Fact]
    public void The_container_fills_a_mod_screens_sources_and_builds_its_parts()
    {
        var services = UiHostKit.RuntimeServices(_engine, _surface, _dir);
        var screens = UiModScreens.Build(services, [FixtureAssembly], new UiWindows(new MemoryWindowStore()));
        var probe = Assert.IsType<ModProbe>(Assert.Single(screens));
        Assert.Same(services.Require<ICentrePrintSource>(), probe.Source);   // the side's own adapter
        Assert.NotNull(probe.Line);                                          // the part, built from the mod's assembly
    }

    [Fact]
    public void A_mod_screen_the_container_cannot_fill_is_refused_by_name()
    {
        var e = Assert.Throws<ServiceResolutionException>(
            () => UiModScreens.Build(new ServiceContainer(), [FixtureAssembly], new UiWindows(new MemoryWindowStore())));
        Assert.Contains("ModProbe", e.Message);
        Assert.Contains(nameof(ICentrePrintSource), e.Message);
    }

    [Fact]
    public void The_preview_and_the_game_list_the_same_mod_screens()
    {
        var game = Names(UiModScreens.Build(UiHostKit.RuntimeServices(_engine, _surface, _dir), [FixtureAssembly],
                                            new UiWindows(new MemoryWindowStore())));
        // The preview's discovery (PreviewRun.AllSamples runs it over the listed assemblies).
        string[] preview = [.. UiSamples.Discover(FixtureAssembly).Where(s => !s.Gallery).Select(s => s.Group).Distinct()];
        Assert.Equal(["modprobe"], game);
        Assert.Equal(game, preview);
    }
}
