using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The crosshair screen is stock's <c>CHudTFCrosshair::Paint</c>
/// (tf_hud_crosshair.cpp:143-217): the custom <c>cl_crosshair_file</c>
/// binds Paint's <c>vgui/crosshairs/&lt;file&gt;</c> material (:150-158)
/// and draws the whole quad at <c>iX±iWidth</c> (:215) — full size
/// <c>2*iWidth</c>, <c>iWidth = (int)(scale*32 + 0.5f)</c> (:205-206) —
/// while the empty file takes the base arm's centred sprite as the
/// toolkit's shape (:175-178, hud_crosshair.cpp:275-280). Both arms
/// modulate by the three <c>cl_crosshair_*</c> ints at full alpha
/// (:200, hud_crosshair.cpp:262-264); where the material binds nothing
/// the shape stands in, our divergence from stock's error texture.
/// </summary>
public sealed class TFCrosshairScreenTests
{
    private static readonly UiFontLibrary Fonts = new();

    /// <summary>A material library that answers the named path with a real
    /// handle and records every ask (the stock arm binds whole, as the
    /// scope's quadrants do).</summary>
    /// <summary>A material library that answers the named path with a real
    /// handle and records every ask (the stock arm binds whole, as the
    /// scope's quadrants do). With a size it also answers the file's
    /// extents, the way <c>About</c> does for a bound texture.</summary>
    private sealed class CrosshairMaterials(string answered, int wide = 0, int tall = 0) : IUiTextureLibrary
    {
        public readonly List<string> Asked = [];

        public int Material(string name)
        {
            Asked.Add(name);
            return name == answered ? 0x77 : 0;
        }

        public int Find(string name) => Material(name) != 0 ? 0x77 : 0;

        public UiTextureInfo? About(string name) =>
            name == answered && wide > 0 && tall > 0 ? new UiTextureInfo(0x77, wide, tall, "") : null;

        public UiTextureIcon? Icon(string name) => null;
    }

    private static UiContext Painted(TF2.Client.Ui.ITFCrosshair source, IUiTextureLibrary? textures = null)
    {
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, new TF2.Client.Ui.TFCrosshair(source, textures), UiSamples.Screen(1920, 1080), 1.0);
        return ctx;
    }

    /// <summary>The colours of the quads the element named crosshair drew
    /// (the shape's ticks and their shade ride in their own element).</summary>
    private static List<UiColor> CrosshairQuadColours(UiContext ctx)
    {
        var list = ctx.List.AsDrawList();
        var elements = UiTestKit.Elements(ctx);
        var colours = new List<UiColor>();
        for (int i = 0; i < elements.Length; i++)
            if (UiTestKit.NameOf(ctx, elements[i]) == "crosshair")
                foreach (var op in list.Ops)
                    if (op.Kind == UiPrimitiveKind.Quad && op.Element == i)
                        colours.Add(list.Quads[op.Index].Color);
        return colours;
    }

    private static UiTexturedQuad[] Textured(UiContext ctx)
    {
        var list = ctx.List.AsDrawList();
        return [.. list.TexturedQuads];
    }

    [Fact]
    public void The_default_crosshair_is_the_shape_in_the_three_convar_ints()
    {
        var ctx = Painted(new TF2.Client.Ui.FakeCrosshair());

        Assert.Equal(UiElementKind.Shape, UiTestKit.Named(ctx, "crosshair").Kind);
        // Paint's Color(r, g, b, 255) at the convars' stock 200s (:200),
        // not the theme's text tone the shape used to carry.
        Assert.Contains(new UiColor(200, 200, 200, 255), CrosshairQuadColours(ctx));
    }

    [Fact]
    public void The_shape_follows_the_colour_convars_when_the_player_moves_them()
    {
        var source = new TF2.Client.Ui.FakeCrosshair { Red = 12, Green = 34, Blue = 56 };

        var ctx = Painted(source);

        Assert.Contains(new UiColor(12, 34, 56, 255), CrosshairQuadColours(ctx));
    }

    [Fact]
    public void A_custom_file_binds_the_material_path_and_draws_the_full_quad()
    {
        // Paint's custom arm (:150-158, :215): the iX±iWidth quad — the
        // full size, centred — modulated by the same convar colour.
        var lib = new CrosshairMaterials("vgui/crosshairs/crosshair01");
        var source = new TF2.Client.Ui.FakeCrosshair { Material = "vgui/crosshairs/crosshair01" };

        var ctx = Painted(source, lib);

        Assert.Equal(UiElementKind.Image, UiTestKit.Named(ctx, "crosshair").Kind);
        var quad = Assert.Single(Textured(ctx));
        Assert.Equal(0x77, quad.Texture);
        Assert.Equal(new UiColor(200, 200, 200, 255), quad.Tint);
        // iWidth = (int)(32 * 1 + 0.5) = 32, the quad 2*iWidth on a side, centred.
        Assert.True(MathF.Abs(quad.Rect.W - 64f) < 0.5f, $"the quad must be 64 wide, was {quad.Rect.W}");
        Assert.True(MathF.Abs(quad.Rect.H - 64f) < 0.5f, $"the quad must be 64 tall, was {quad.Rect.H}");
        Assert.True(MathF.Abs(quad.Rect.X + quad.Rect.W / 2f - 960f) < 0.5f,
            $"the quad must sit centred horizontally, its rect is {quad.Rect}");
        Assert.True(MathF.Abs(quad.Rect.Y + quad.Rect.H / 2f - 540f) < 0.5f,
            $"the quad must sit centred vertically, its rect is {quad.Rect}");

        // The ask is Paint's built path, and the only one (:153-154).
        Assert.Equal(["vgui/crosshairs/crosshair01"], lib.Asked.Distinct().ToArray());
    }

    [Fact]
    public void The_custom_quad_doubles_its_sides_when_the_scale_convar_doubles()
    {
        var lib = new CrosshairMaterials("vgui/crosshairs/crosshair01");
        var source = new TF2.Client.Ui.FakeCrosshair
        {
            Material = "vgui/crosshairs/crosshair01",
            Scale = 2f,
        };

        var ctx = Painted(source, lib);

        // iWidth = (int)(64 + 0.5) = 64 (tf_hud_crosshair.cpp:201, :205);
        // the quad is 2*iWidth on a side (:215).
        var quad = Assert.Single(Textured(ctx));
        Assert.True(MathF.Abs(quad.Rect.W - 128f) < 0.5f, $"the quad must be 128 wide, was {quad.Rect.W}");
        Assert.True(MathF.Abs(quad.Rect.H - 128f) < 0.5f, $"the quad must be 128 tall, was {quad.Rect.H}");
    }

    [Fact]
    public void The_shape_box_is_the_width_across_at_the_scale()
    {
        // The script rect of 32 across, doubled by the scale convar.
        var ctx = Painted(new TF2.Client.Ui.FakeCrosshair
        {
            Scale = 2f, ScriptFile = "sprites/crosshairs", ScriptWide = 32, ScriptTall = 32,
        });

        // The base arm's sprite box: iWidth across, centred
        // (hud_crosshair.cpp:275-280 at flWidth, tf_hud_crosshair.cpp:201).
        var e = UiTestKit.Named(ctx, "crosshair");
        Assert.Equal(UiElementKind.Shape, e.Kind);
        Assert.True(MathF.Abs(e.Rect.W - 64f) < 0.5f, $"the box must be 64 wide, was {e.Rect.W}");
        Assert.True(MathF.Abs(e.Rect.H - 64f) < 0.5f, $"the box must be 64 tall, was {e.Rect.H}");
        Assert.True(MathF.Abs(e.Rect.X + e.Rect.W / 2f - 960f) < 0.5f,
            $"the box must sit centred horizontally, its rect is {e.Rect}");
        Assert.True(MathF.Abs(e.Rect.Y + e.Rect.H / 2f - 540f) < 0.5f,
            $"the box must sit centred vertically, its rect is {e.Rect}");
    }

    [Fact]
    public void A_material_that_binds_nothing_falls_back_to_the_shape()
    {
        // The documented divergence from stock's error texture: nothing
        // bound (the preview, a missing file) draws the shape — but the
        // ask still happened, Paint's path built as stock builds it.
        var lib = new CrosshairMaterials("something/else");
        var source = new TF2.Client.Ui.FakeCrosshair { Material = "vgui/crosshairs/crosshair01" };

        var ctx = Painted(source, lib);

        Assert.Equal(UiElementKind.Shape, UiTestKit.Named(ctx, "crosshair").Kind);
        Assert.Empty(Textured(ctx));
        Assert.Equal(["vgui/crosshairs/crosshair01"], lib.Asked.Distinct().ToArray());
    }

    /// <summary>How many elements the frame carries under the given name.</summary>
    private static int NamedCount(UiContext ctx, string name)
    {
        var elements = UiTestKit.Elements(ctx);
        int n = 0;
        for (int i = 0; i < elements.Length; i++)
            if (UiTestKit.NameOf(ctx, elements[i]) == name)
                n++;
        return n;
    }

    [Fact]
    public void The_empty_file_binds_the_script_s_own_path_not_a_prefixed_one()
    {
        // The empty cl_crosshair_file takes BaseClass::Paint (:175-178):
        // stock binds the script's file whole through DrawSetTextureFile
        // (hud.cpp:723), with no vgui/crosshairs/ prefix.
        var lib = new CrosshairMaterials("sprites/crosshairs", wide: 64, tall: 32);

        Painted(new TF2.Client.Ui.FakeCrosshair(), lib);

        Assert.Equal(["sprites/crosshairs"], lib.Asked.Distinct().ToArray());
    }

    /// <summary>
    /// The base arm's draw (hud_crosshair.cpp:253-280): the script rect's
    /// size at the folded scale, each side rounded on its own (:268-271),
    /// centred (:275-276), tinted (:263). The crop is the rect over the file's
    /// size, DrawSelfCropped's interpolation (hud.cpp:645-656).
    /// </summary>
    [Fact]
    public void The_script_crosshair_draws_the_rect_scaled_and_cropped_to_its_pixels()
    {
        var lib = new CrosshairMaterials("sprites/crosshairs", wide: 64, tall: 32);
        var source = new TF2.Client.Ui.FakeCrosshair
        {
            ScriptFile = "sprites/crosshairs",
            ScriptX = 6,
            ScriptY = 2,
            ScriptWide = 24,
            ScriptTall = 16,
            Scale = 2f,
        };

        var ctx = Painted(source, lib);

        Assert.Equal(UiElementKind.Image, UiTestKit.Named(ctx, "crosshair").Kind);
        var quad = Assert.Single(Textured(ctx));
        Assert.Equal(0x77, quad.Texture);
        Assert.Equal(new UiColor(200, 200, 200, 255), quad.Tint);

        // iWidth = (int)(2*24 + 0.5) = 48, iHeight = (int)(2*16 + 0.5) = 32 —
        // each side rounded alone, the box centred.
        Assert.True(MathF.Abs(quad.Rect.W - 48f) < 0.5f, $"the quad must be 48 wide, was {quad.Rect.W}");
        Assert.True(MathF.Abs(quad.Rect.H - 32f) < 0.5f, $"the quad must be 32 tall, was {quad.Rect.H}");
        Assert.True(MathF.Abs(quad.Rect.X + quad.Rect.W / 2f - 960f) < 0.5f,
            $"the quad must sit centred horizontally, its rect is {quad.Rect}");
        Assert.True(MathF.Abs(quad.Rect.Y + quad.Rect.H / 2f - 540f) < 0.5f,
            $"the quad must sit centred vertically, its rect is {quad.Rect}");

        // The crop: (6,2) over (24,16) of the 64×32 file.
        Assert.True(MathF.Abs(quad.Uv.X - 0.09375f) < 0.001f, $"u0 must be 6/64, was {quad.Uv.X}");
        Assert.True(MathF.Abs(quad.Uv.Y - 0.0625f) < 0.001f, $"v0 must be 2/32, was {quad.Uv.Y}");
        Assert.True(MathF.Abs(quad.Uv.W - 0.375f) < 0.001f, $"uw must be 24/64, was {quad.Uv.W}");
        Assert.True(MathF.Abs(quad.Uv.H - 0.5f) < 0.001f, $"vh must be 16/32, was {quad.Uv.H}");
    }

    /// <summary>The doubled player scale doubles the rect-sized box too, the
    /// way Paint doubles the fixed-size one (:201, :268-271).</summary>
    [Fact]
    public void The_script_box_doubles_with_the_scale_on_the_shipped_rect()
    {
        // The fake's default entry is the shipped heavy's rect: the 64×64
        // crop at (64,64) of sprites/crosshairs (tf_weapon_minigun.txt).
        var lib = new CrosshairMaterials("sprites/crosshairs", wide: 32, tall: 32);
        var source = new TF2.Client.Ui.FakeCrosshair { Scale = 2f };

        var ctx = Painted(source, lib);

        var quad = Assert.Single(Textured(ctx));
        Assert.True(MathF.Abs(quad.Rect.W - 128f) < 0.5f, $"the quad must be 128 wide, was {quad.Rect.W}");
        Assert.True(MathF.Abs(quad.Rect.H - 128f) < 0.5f, $"the quad must be 128 tall, was {quad.Rect.H}");
    }

    /// <summary>Both stock arms answer the absent texture with nothing drawn:
    /// the empty custom file with no script entry (the base Paint's
    /// null-texture guard, hud_crosshair.cpp:235-236).</summary>
    [Fact]
    public void An_empty_script_crosshair_draws_nothing()
    {
        var lib = new CrosshairMaterials("sprites/crosshairs", wide: 64, tall: 32);

        var ctx = Painted(new TF2.Client.Ui.FakeCrosshair { ScriptFile = "" }, lib);

        Assert.Equal(0, NamedCount(ctx, "crosshair"));
        Assert.Empty(Textured(ctx));
        Assert.Empty(lib.Asked);
    }
}
