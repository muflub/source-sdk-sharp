using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The menu background (plan_gameui_managed.md G1): the stock GameUI's
/// backdrop, <c>console/&lt;the engine's background name&gt;</c> with
/// <c>_widescreen</c> on screens of aspect 1.6 or wider, drawn while no level
/// is up (docs/gameui-census.md §5).
/// </summary>
public class MenuBackgroundTests
{
    private sealed class Engine(string? name) : IMenuBackgroundName
    {
        public string? Name { get; set; } = name;
        public int Asks { get; private set; }

        public string? MainMenuBackgroundName()
        {
            Asks++;
            return Name;
        }
    }

    private sealed class Menu : IMainMenuSource
    {
        public bool InLevel { get; set; }
        public bool CanPlay => false;
        public void Play() { }
        public void Resume() { }
        public void OpenOptions() { }
        public void Quit() { }
    }

    /// <summary>Binds every name, keeping the ones asked.</summary>
    private sealed class Art : IUiTextureLibrary
    {
        public List<string> Asked { get; } = [];
        public int Find(string name)
        {
            Asked.Add(name);
            return 1;
        }
        public int Material(string name) => 1;
        public UiTextureInfo? About(string name) => null;
        public UiTextureIcon? Icon(string name) => null;
    }

    [Fact]
    public void The_source_names_both_materials_from_the_engines_background()
    {
        var source = new MenuBackgroundUi(new Engine("background02"), new Menu());
        Assert.Equal("console/background02", source.Material);
        Assert.Equal("console/background02_widescreen", source.WidescreenMaterial);
    }

    [Fact]
    public void The_source_names_nothing_while_a_level_is_up_and_asks_again_after()
    {
        var engine = new Engine("background01");
        var menu = new Menu();
        var source = new MenuBackgroundUi(engine, menu);
        Assert.Equal("console/background01", source.Material);
        Assert.Equal(1, engine.Asks);                        // cached at the menu
        Assert.Equal("console/background01", source.Material);
        Assert.Equal(1, engine.Asks);

        menu.InLevel = true;
        Assert.Null(source.Material);
        Assert.Null(source.WidescreenMaterial);

        engine.Name = "background05";                         // a background map changed it
        menu.InLevel = false;
        Assert.Equal("console/background05", source.Material);
        Assert.Equal(2, engine.Asks);
    }

    [Fact]
    public void An_engine_that_names_no_background_draws_none()
    {
        var source = new MenuBackgroundUi(new Engine(null), new Menu());
        Assert.Null(source.Material);
        Assert.Null(source.WidescreenMaterial);
    }

    [Theory]
    [InlineData(1920, 1080, "console/background01_widescreen")]
    [InlineData(2560, 1080, "console/background01_widescreen")]
    [InlineData(1024, 768, "console/background01")]
    [InlineData(1280, 1024, "console/background01")]
    public void The_screen_draws_the_material_for_its_aspect_full_screen(int w, int h, string expected)
    {
        var art = new Art();
        var screen = new MenuBackground(new MenuBackgroundUi(new Engine("background01"), new Menu()), art);
        var ctx = Context();
        Frame(ctx, UiTestKit.Screen(w, h), (ref UiFrame ui) => screen.Describe(ref ui));
        Assert.Equal([expected], art.Asked);
        var drawn = Named(ctx, "background").Rect;
        Assert.Equal((0f, 0f, (float)w, (float)h), (drawn.X, drawn.Y, drawn.W, drawn.H));
    }

    [Fact]
    public void The_screen_draws_nothing_while_a_level_is_up()
    {
        var art = new Art();
        var screen = new MenuBackground(new MenuBackgroundUi(new Engine("background01"), new Menu { InLevel = true }), art);
        var ctx = Context();
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
        Assert.Empty(art.Asked);
        Assert.DoesNotContain(Elements(ctx), e => NameOf(ctx, e) == "background");
    }
}
