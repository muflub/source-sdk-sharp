using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
using SourceSharp.UiPreview;

using TF2.Descent.Client.Ui;
using TF2.Descent.Client.Ui.Screens;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The Descent screens as the game composes them (plan_descent_logic UI1):
/// every screen of TF2.Descent.Client.Ui built by the UI's own mod-screen
/// path (<see cref="UiModScreens.Build"/>, the one that throws naming the
/// screen and the parameter it could not fill) over the sources
/// TF2.Descent.Client registers, and nothing else: no sample, no fake. A
/// source the client does not register stops the UI loading in game; here
/// it stops this composition.
/// </summary>
public sealed class DescentUiCompositionTests
{
    private static readonly Assembly Screens = typeof(DescentHud).Assembly;

    private static readonly Assembly Sources = typeof(VitalsSource).Assembly;

    private static readonly Assembly Contract = typeof(IDescentVitals).Assembly;

    private static readonly UiFontLibrary Fonts = new();

    /// <summary>The client's container as the module has it when the UI composes: the
    /// engine's areas (a fake that answers them), the core's services, and every
    /// <c>[SideService]</c> of TF2.Descent.Client.</summary>
    internal static ServiceContainer Services(RecordingEngine? engine = null)
    {
        engine ??= Fake().Returns("Hud.LocalPlayerHealth", 0).Returns("Hud.LocalPlayerIndex", 1).Returns("Environment.Globals", default(Globals))
                         .Returns("Hud.LocalPlayerOrigin", (Vector?)null);
        var services = UiHostKit.RuntimeServices(engine, new UiFidelityTests.RecordingSurface(), Path.GetTempPath());
        services.AddInstance<IEngineEntities>(engine);
        services.AddInstance<IEngineWorldQuery>(engine);
        // The module's log and the engine's client, as the game's container has them (the
        // owner's item cache says a content mismatch through the log: plan_descent_logic UI3).
        services.AddInstance<ILog>(new RecordingLog());
        services.AddInstance<IFwdEngineClient>(new ChatFeedTests.RecordingEngineClient([]));
        // The core's view services the automap and the robot bars read (ClientModule registers
        // both): a world with nobody in it and a view that sees nothing.
        services.AddInstance<IMinimapWorld>(new NoMinimapWorld());
        services.AddInstance<IMarkerView>(new NoMarkerView());
        SideServices.Register(services, Side.Client, Sources.GetTypes());
        // The client's convars, declared and published as the module's registry does for a mod.
        var convars = new ConVarRegistry(new NoEngineConVars());
        convars.RegisterTypes([.. Sources.GetTypes().Where(t => t.GetCustomAttribute<ConVarAttribute>() is not null)]);
        convars.PublishTo(services);
        return services;
    }

    private sealed class NoMinimapWorld : IMinimapWorld
    {
        public Vector LocalOrigin => default;
        public float LocalYaw => 0f;
        public void OtherPlayers(List<Vector> into) { }
        public bool OnScreen(Vector origin) => false;
    }

    private sealed class NoMarkerView : IMarkerView
    {
        public Vector LocalOrigin => default;

        public bool ToScreen(Vector world, out float x, out float y)
        {
            x = y = 0.5f;
            return false;
        }
    }

    private static IReadOnlyList<UiScreen> Built(ServiceContainer services, UiWindows? windows = null)
        => UiModScreens.Build(services, [Screens], windows ?? new UiWindows(new MemoryWindowStore()));

    [Fact]
    public void Every_descent_screen_builds_from_the_games_own_sources()
    {
        var screens = Built(Services());

        // Every screen the assembly ships, by the game's own discovery.
        Assert.Equal(UiScreens.Discover(Screens).Count, screens.Count);
        Assert.Equal(["automap", "charsheet", "craftbench", "crate", "depthpicker", "descenthud", "downed",
                      "inventory", "party", "robotbars", "skilltree", "spellbook", "stash", "vendor"],
                     screens.Select(s => s.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_source_the_client_does_not_register_stops_the_composition_naming_it()
    {
        // The negative arm: the same container without the vitals' source. The
        // HUD's orbs cannot be built, and the error says which and why.
        var services = Services();
        var without = new ServiceContainer();
        foreach (var type in services.ServiceTypes.Where(t => t != typeof(IDescentVitals)))
            without.AddFactory(type, _ => services.Resolve(type)!);

        var error = Assert.ThrowsAny<Exception>(() => Built(without));

        Assert.Contains("IDescentVitals", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_town_windows_are_built_over_the_live_town_sources_and_stop_without_them()
    {
        // plan_descent_logic UI4: the vendor, the stash, the bench, the crate stall and the depth
        // picker read the client's live sources, the status line and the vendor's picks among them.
        var services = Services();
        Assert.Equal("VendorSource", services.Resolve(typeof(IDescentVendor))!.GetType().Name);
        Assert.Equal("TownStatusSource", services.Resolve(typeof(IDescentTownStatus))!.GetType().Name);
        Assert.Equal("VendorPicks", services.Resolve(typeof(IDescentVendorPicks))!.GetType().Name);
        Assert.Contains(Built(services), s => s.Name == "vendor");

        var without = new ServiceContainer();
        foreach (var type in services.ServiceTypes.Where(t => t != typeof(IDescentVendor)))
            without.AddFactory(type, _ => services.Resolve(type)!);

        var error = Assert.ThrowsAny<Exception>(() => Built(without));
        Assert.Contains("IDescentVendor", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_descent_screen_draws_a_frame_on_the_games_sources()
    {
        var windows = new UiWindows(new MemoryWindowStore());
        var lang = UiSamplesTests.Language();
        var problems = new List<string>();
        foreach (var screen in Built(Services(), windows))
        {
            try
            {
                var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, lang);
                UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), 10.0, settle: 2);
                // A window opened on nothing to show still opens: its frame draws (an
                // empty state inside), or it would be open, unseen, holding the keyboard.
                if (screen is IUiWindowed && !UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == "wnd-frame"))
                    problems.Add($"{screen.Name}: opened, and drew no window");
            }
            catch (Exception e)
            {
                problems.Add($"{screen.Name}: {e.GetType().Name}: {e.Message}");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_descent_source_is_the_clients_own_and_no_sample_is_registered()
    {
        var services = Services();
        // Every source and action interface the screens' contract declares (the skill
        // tab is a row a source hands out, not a service).
        var contracts = Contract.GetTypes().Where(t => t.IsInterface && t != typeof(IDescentSkillTab)).ToList();
        Assert.True(contracts.Count >= 40, $"only {contracts.Count} contract interfaces were read");

        var wrong = new List<string>();
        foreach (var contract in contracts)
        {
            var source = services.Resolve(contract);
            if (source is null)
                wrong.Add($"{contract.Name}: nothing registered");
            else if (source.GetType().Assembly != Sources)
                wrong.Add($"{contract.Name}: answered by {source.GetType().FullName} of {source.GetType().Assembly.GetName().Name}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void The_client_never_references_the_screens_so_their_samples_cannot_reach_the_game()
    {
        var names = Sources.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.Contains("TF2.Descent.Client.Interface", names);    // the reader sees references
        Assert.DoesNotContain(Screens.GetName().Name, names);
        Assert.DoesNotContain(Sources.GetTypes(), t => t.Name.StartsWith("Fake", StringComparison.Ordinal));
    }

    [Fact]
    public void The_tf2_descent_mod_loads_tf2s_ui_then_its_own()
    {
        var mod = SourceSharp.Mods.ModsConfig.Load(RepoLayout.Repo("game", "mods.yaml")).Select("tf2_descent");

        Assert.Equal(["TF2.Client.Ui", "TF2.Descent.Client.Ui"], mod.ClientUiAssemblies);
    }

    //-------------------------------------------------------------------------
    // Panels and the commands that open them
    //-------------------------------------------------------------------------

    [Fact]
    public void Every_descent_window_claims_its_descent_panel()
    {
        var menus = Built(Services()).Where(s => s.Layer == UiLayerKind.Menu).ToList();

        Assert.Equal(11, menus.Count);
        Assert.All(menus, s => Assert.Equal(DescentScreens.PanelPrefix + s.Name, s.Panel));
    }

    [Fact]
    public void The_clients_screen_list_is_the_descent_windows()
    {
        var menus = Built(Services()).Where(s => s.Layer == UiLayerKind.Menu).Select(s => s.Name).Order(StringComparer.Ordinal);

        Assert.Equal(menus, DescentScreens.Names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_server_opens_a_descent_window_by_its_panel()
    {
        var windows = new UiWindows(new MemoryWindowStore());
        var host = UiHostKit.Build(new UiFidelityTests.RecordingSurface(), [],
                                   [.. Built(Services(), windows).Where(s => s.Layer == UiLayerKind.Menu)], windows: windows);
        Assert.False(host.Menu.IsOpen("inventory"));

        Assert.True(host.TakePanel("descent_inventory", true, []));

        Assert.True(host.Menu.IsOpen("inventory"));
    }

    //-------------------------------------------------------------------------
    // Escape (L8)
    //-------------------------------------------------------------------------

    private sealed class FakeMainMenu : IMainMenuSource, IMainMenuCommands
    {
        public void Run(string command) { }

        public bool InLevel => true;

        public bool CanPlay => false;

        public void Play() { }

        public void Resume() { }

        public void OpenOptions() { }

        public void Quit() { }
    }

    /// <summary>The menu layer as the game has it: the pause menu and the Descent windows,
    /// sharing the layer's windows, with a clock that moves a second each read (one
    /// Escape a frame, <c>UiHost.ClaimEscape</c>).</summary>
    private static UiHost EscapeHost()
    {
        var windows = new UiWindows(new MemoryWindowStore());
        var fake = new FakeMainMenu();
        UiScreen[] menus = [new MainMenu(fake, fake, new BuildStampPanel(new FakeBuildStamp())),
                            .. Built(Services(), windows).Where(s => s.Layer == UiLayerKind.Menu)];
        float seconds = 3;
        var engine = Fake().ReturnsEachCall("Clock.Now", () => seconds += 1).Returns("Hud.LocalPlayerHealth", 0)
                           .Returns("Hud.LocalPlayerIndex", 1).Returns("Environment.Globals", default(Globals));
        return UiHostKit.Build(new UiFidelityTests.RecordingSurface(), [], menus, null, engine, windows: windows);
    }

    private static void Escape(UiHost host)
    {
        host.Input((int)UiInputKind.Key, 0, 0, (int)UiKey.Escape, 0, 0f);
        host.Paint(1, 1920, 1080);   // a frame between presses: the windows lay out, and a queued key would land
    }

    [Fact]
    public void Escape_closes_the_top_descent_window_then_the_next_then_opens_the_main_menu()
    {
        var host = EscapeHost();
        host.Open(DescentScreens.Inventory);
        host.Paint(1, 1920, 1080);
        host.Open(DescentScreens.CharSheet);
        host.Paint(1, 1920, 1080);
        Assert.Equal(DescentScreens.CharSheet, host.Windows.Top);       // before: two windows, the sheet on top

        Escape(host);
        Assert.Equal((false, true, false), (host.Menu.IsOpen(DescentScreens.CharSheet), host.Menu.IsOpen(DescentScreens.Inventory),
                                            host.Menu.IsOpen("mainmenu")));

        Escape(host);
        Assert.Equal((false, false), (host.Menu.IsOpen(DescentScreens.Inventory), host.Menu.IsOpen("mainmenu")));

        Escape(host);
        Assert.True(host.Menu.IsOpen("mainmenu"));
    }

    [Fact]
    public void Escape_closes_the_depth_picker_and_does_not_open_the_main_menu()
    {
        // Live (the whole run, 2026-11-14): Esc over the town's depth picker opened the main
        // menu on top of it and never closed it, because a dialog is not one of the windows
        // the host's Escape closes.
        var host = EscapeHost();
        host.Open("depthpicker");
        host.Paint(1, 1920, 1080);
        Assert.True(host.Menu.IsOpen("depthpicker"));   // before: the picker is up

        Escape(host);

        Assert.Equal((false, false), (host.Menu.IsOpen("depthpicker"), host.Menu.IsOpen("mainmenu")));
    }

    [Fact]
    public void An_empty_stash_says_the_backpack_is_empty_on_the_backpack_side()
    {
        // Live (the whole run): both sides of an empty stash read "The locker is empty."
        var host = EscapeHost();
        host.Open("stash");
        host.Paint(1, 1920, 1080);

        var texts = host.Debug.ReadMenu(false).Draws.Where(d => d.Kind == "Text").Select(d => d.Text).ToList();
        int Count(string token) => texts.Count(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase));
        Assert.Equal((1, 1), (Count("#Descent_Stash_Empty"), Count("#Descent_Stash_BackpackEmpty")));
    }

    //-------------------------------------------------------------------------
    // A click reaches a town window (UI4)
    //-------------------------------------------------------------------------

    [Fact]
    public void A_click_reaches_the_vendor_window_when_no_item_is_inspected()
    {
        // The inspect modal's overlay took every click while nothing was inspected: live, the
        // vendor's tabs, rows and buttons answered nothing (the Inventory's UI3 bug, again).
        var windows = new UiWindows(new MemoryWindowStore());
        var services = Services();
        var town = (TF2.Descent.Client.Town.TownCache)services.Resolve(typeof(TF2.Descent.Client.Town.ITownView))!;
        var catalog = (TF2.Descent.Client.Content.DescentCatalog)services.Resolve(typeof(TF2.Descent.Client.Content.DescentCatalog))!;
        town.OnTownWindow(new TF2.Descent.Shared.TownWindowView(TF2.Descent.Shared.TownScreen.Vendor, 0, TF2.Descent.Shared.TownStance.Weapons,
                                                                 catalog.Fingerprint, 20, 0, 0, [], [], "#Descent_Vendor_Hale", "", true));
        var fake = new FakeMainMenu();
        UiScreen[] menus = [new MainMenu(fake, fake, new BuildStampPanel(new FakeBuildStamp())),
                            .. Built(services, windows).Where(s => s.Layer == UiLayerKind.Menu)];
        var engine = Fake().Returns("Clock.Now", 3f).Returns("Hud.LocalPlayerHealth", 0)
                           .Returns("Hud.LocalPlayerIndex", 1).Returns("Environment.Globals", default(Globals));
        var host = UiHostKit.Build(new UiFidelityTests.RecordingSurface(), [], menus, null, engine, windows: windows);
        Assert.True(host.TakePanel("descent_vendor", true, []));
        host.Paint(1, 1920, 1080);
        static bool SellButton(Debug.Api.Client.Interfaces.DebugClientUiDraw d) =>
            d.Kind == "Text" && d.Text.Contains("Btn_Sell", StringComparison.OrdinalIgnoreCase);
        var tab = host.Debug.ReadMenu(false).Draws.First(d => d.Kind == "Text" && d.Text.Contains("Tab_Sell", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(host.Debug.ReadMenu(false).Draws, SellButton);

        foreach (var kind in new[] { UiInputKind.MouseMove, UiInputKind.MouseDown, UiInputKind.MouseUp })
        {
            host.Input((int)kind, tab.X0 + 8, tab.Y0 + 8, kind == UiInputKind.MouseMove ? 0 : 107, 0, 0f);
            host.Paint(1, 1920, 1080);
        }
        host.Paint(1, 1920, 1080);

        Assert.Contains(host.Debug.ReadMenu(false).Draws, SellButton);
    }
}
