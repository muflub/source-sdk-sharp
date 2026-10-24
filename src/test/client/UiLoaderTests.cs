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
        foreach (string name in UiLoader.Assemblies.Concat([ModUiAssembly, ModUiFixture]))
            foreach (string ext in new[] { ".dll", ".pdb" })
                if (File.Exists(Path.Combine(from, name + ext)))
                    File.Copy(Path.Combine(from, name + ext), Path.Combine(_dir, name + ext));
    }

    /// <summary>A real mod UI assembly, shipped beside the tests: TF2Descent's screens.</summary>
    private const string ModUiAssembly = "TF2.Descent.Client.Ui";

    /// <summary>The facts' own mod UI assembly: one HUD screen ("modprobe") over a core source.</summary>
    private const string ModUiFixture = "SourceSharp.Tests.ModUi";

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private readonly Ui.UiFidelityTests.RecordingSurface _surface = new();

    /// <summary>
    /// The loader over the fact's directory. <paramref name="listedInMods"/>:
    /// mods.yaml lists <paramref name="modAssemblies"/> too, as ClientModule
    /// hands both the loader and the composition IModInfo's list, so the
    /// composition places their screens.
    /// </summary>
    private UiLoader Loader(ClDrawHudConVar? drawHud = null, IReadOnlyList<string>? modAssemblies = null, bool listedInMods = false)
    {
        var hud = new Hud(new RecordingLog(), _engine, _engine, new DamageMessage(), new HudMsgMessage(),
                          new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());
        var services = UiHostKit.RuntimeServices(_engine, _surface, _dir,
            k => listedInMods && k == ModInfo.Keys.ClientUiAssemblies ? string.Join(';', modAssemblies ?? []) : null);
        var s = new UiRuntimeServices(services, new RecordingLog(), hud, new UiHostKit.NoArsenal(), null, new DefaultForwarders.NoEngineClient(), new DefaultForwarders.NoInputSystem());
        return new UiLoader(new RecordingLog(), _engine, _dir, () => s, parent: AssemblyLoadContext.Default,
                            modAssemblies: modAssemblies) { DrawHud = drawHud };
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

    // ---- the mod's own UI assemblies (stage 0.9) ----

    [Fact]
    public void The_mod_ui_assembly_loads_in_the_ui_context_not_the_cores()
    {
        var l = Loader(modAssemblies: [ModUiAssembly]);
        Assert.True(l.Load(), "the loader loaded with the mod assembly listed");
        // Held: the mod assembly answers from the UI's own collectible context.
        // Resolved through the parent instead, the name would answer with the
        // copy the test run itself loaded into the default context, and a
        // reload would keep running the old one — the failure with no symptom.
        // Each check is a frame of its own and holds nothing: an Assembly the
        // leak arm still names would pin its own context.
        AssertHeldByTheUiContext(l);
        Assert.True(l.Sequence == 0);                        // control: not yet reloaded
        l.Reload("command");
        Assert.Equal(1, l.Sequence);                         // control: it reloaded
        AssertHeldByTheUiContext(l);
        // Nothing outside the old context kept the old copy, or the old UI, alive.
        Assert.Equal("collected", l.SettleOldContext());
        l.Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AssertHeldByTheUiContext(UiLoader l)
    {
        var ui = AssemblyLoadContext.GetLoadContext(l.Current!.GetType().Assembly)!;
        var mod = ui.LoadFromAssemblyName(new System.Reflection.AssemblyName(ModUiAssembly));
        Assert.True(ui.IsCollectible);
        Assert.Same(ui, AssemblyLoadContext.GetLoadContext(mod));
    }

    [Fact]
    public void A_listed_mod_screen_is_placed_from_the_ui_contexts_copy_and_reloads()
    {
        var l = Loader(modAssemblies: [ModUiFixture], listedInMods: true);
        Assert.True(l.Load(), "the loader loaded with the mod's screen to place");
        AssertPlacedFromTheUiContext(l);
        l.Reload("command");
        Assert.Equal(1, l.Sequence);                         // control: it reloaded
        AssertPlacedFromTheUiContext(l);
        // The mod screen's build cached nothing outside the old context.
        Assert.Equal("collected", l.SettleOldContext());
        l.Dispose();
    }

    // Through reflection: the runtime's types are the UI context's copies, not
    // the ones this fact compiled against.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertPlacedFromTheUiContext(UiLoader l)
    {
        var runtime = l.Current!;
        var ui = AssemblyLoadContext.GetLoadContext(runtime.GetType().Assembly)!;
        var found = (IReadOnlyList<System.Reflection.Assembly>)runtime.GetType().Assembly
            .GetType("SourceSharp.Client.Vgui.UiModScreens", throwOnError: true)!
            .GetMethod("Assemblies")!.Invoke(null, [new[] { ModUiFixture }])!;
        Assert.Same(ui, AssemblyLoadContext.GetLoadContext(Assert.Single(found)));
        var layer = runtime.GetType().GetProperty("Layer")!.GetValue(runtime)!;
        Assert.True((bool)layer.GetType().GetMethod("Has")!.Invoke(layer, ["modprobe"])!, "the HUD layer holds the mod's screen");
    }

    [Fact]
    public void The_mod_ui_assembly_loads_after_the_core_three()
    {
        var l = Loader(modAssemblies: [ModUiAssembly, "SourceSharp.Client.Ui"]);   // a re-listed core name joins once
        Assert.Equal([.. UiLoader.Assemblies, ModUiAssembly], l.HeldAssemblies);
        l.Dispose();
    }

    [Fact]
    public void A_mod_ui_assembly_changed_on_disk_is_ui_only_and_reloads()
    {
        var l = Loader(modAssemblies: [ModUiAssembly]);
        l.Load();
        Assert.Null(l.CheckDeploy());
        File.AppendAllBytes(Path.Combine(_dir, ModUiAssembly + ".dll"), [0, 0, 0, 0]);   // new bytes, still loadable
        Assert.Null(l.CheckDeploy());                       // first look: still being written
        Assert.Equal("ui-only", l.CheckDeploy());           // second look: the same, so classified
        Assert.Equal(1, l.Sequence);                        // and it reloaded
        Assert.Equal([ModUiAssembly], l.Report()!.DeployChanged);
        l.Dispose();
    }

    [Fact]
    public void A_listed_mod_ui_assembly_that_is_not_there_refuses_the_load_by_name()
    {
        var log = new RecordingLog();
        var hud = new Hud(log, _engine, _engine, new DamageMessage(), new HudMsgMessage(),
                          new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());
        var services = new ServiceContainer();
        var s = new UiRuntimeServices(services, log, hud, new UiHostKit.NoArsenal(), null, new DefaultForwarders.NoEngineClient(), new DefaultForwarders.NoInputSystem());
        var l = new UiLoader(log, _engine, _dir, () => s, parent: AssemblyLoadContext.Default,
                             modAssemblies: ["NoSuchMod.Ui"])
        {
            DrawHud = null,
        };
        Assert.False(l.Load());
        Assert.Contains("NoSuchMod.Ui.dll is not in", log.Text);   // named, not silently skipped
        l.Dispose();
    }
}
