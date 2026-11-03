using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The scope screen's four quads are stock's vertex table
/// (CHudScope::Paint, tf_hud_scope.cpp:440-500): the region centred, each
/// quadrant half of it, the named corner's material in the named corner —
/// upper left's rect runs (xLeft,yTop)..(xMid,yMid) — and every quadrant
/// samples the whole texture minus the half-pixel inset (:446).
/// </summary>
public class TFScopeScreenTests
{
    private static readonly UiFontLibrary Fonts = new();

    /// <summary>A texture library that answers every material ask with a
    /// real handle, so the quads are drawn textured and record their ask
    /// order (the screen asks ul, ur, lr, ll, in that order).</summary>
    private sealed class MaterialLibrary : IUiTextureLibrary
    {
        public readonly List<string> Asked = new();

        public int Material(string name)
        {
            Asked.Add(name);
            return 0x77;
        }

        public int Find(string name) => 0;

        public UiTextureInfo? About(string name) => null;

        public UiTextureIcon? Icon(string name) => null;
    }

    private static (UiElementInfo[] Elements, UiContext Ctx, MaterialLibrary Lib) Zoomed()
    {
        var lib = new MaterialLibrary();
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, new TF2.Client.Ui.TFScope(new TF2.Client.Ui.FakeScope(), lib),
                         UiSamples.Screen(1280, 720), 1.0);
        return (UiTestKit.Elements(ctx), ctx, lib);
    }

    private static int[] Named(UiElementInfo[] elements, string name, UiContext ctx)
        => Enumerable.Range(0, elements.Length)
            .Where(i => UiTestKit.NameOf(ctx, elements[i]) == name)
            .ToArray();

    [Fact]
    public void Each_quadrant_sits_in_its_named_corner_about_the_centre()
    {
        var (elements, ctx, _) = Zoomed();

        float centreX = 1280 / 2f, centreY = 720 / 2f;

        var upperLeft = elements[Named(elements, "upper left", ctx).Single()].Rect;
        var upperRight = elements[Named(elements, "upper right", ctx).Single()].Rect;
        var lowerRight = elements[Named(elements, "lower right", ctx).Single()].Rect;
        var lowerLeft = elements[Named(elements, "lower left", ctx).Single()].Rect;

        // Stock's corners: the upper quads' bottoms and the lower quads' tops
        // meet the screen's middle; the left pair's rights and the right
        // pair's lefts meet it too ((xMid,yMid) is shared by all four, :441).
        Assert.True(upperLeft.Y + upperLeft.H <= centreY,
            $"upper left must sit above the centre; its rect is {upperLeft}");
        Assert.True(upperRight.Y + upperRight.H <= centreY,
            $"upper right must sit above the centre; its rect is {upperRight}");
        Assert.True(lowerRight.Y >= centreY,
            $"lower right must sit below the centre; its rect is {lowerRight}");
        Assert.True(lowerLeft.Y >= centreY,
            $"lower left must sit below the centre; its rect is {lowerLeft}");

        Assert.True(upperLeft.X + upperLeft.W <= centreX && lowerLeft.X + lowerLeft.W <= centreX,
            "the left pair must sit left of the centre");
        Assert.True(upperRight.X >= centreX && lowerRight.X >= centreX,
            "the right pair must sit right of the centre");
    }

    [Fact]
    public void The_four_quads_are_half_the_region_and_tile_it_without_a_gap()
    {
        var (elements, ctx, _) = Zoomed();

        // The 4:3 region on a 1280x720 screen is 960 wide, the full height
        // (:409-420): each quadrant 480x360, the outer edges on the region's.
        var upperLeft = elements[Named(elements, "upper left", ctx).Single()].Rect;
        var upperRight = elements[Named(elements, "upper right", ctx).Single()].Rect;
        var lowerRight = elements[Named(elements, "lower right", ctx).Single()].Rect;
        var lowerLeft = elements[Named(elements, "lower left", ctx).Single()].Rect;

        foreach (var (name, r) in new[] { ("upper left", upperLeft), ("upper right", upperRight),
                                          ("lower right", lowerRight), ("lower left", lowerLeft) })
        {
            Assert.True(MathF.Abs(r.W - 480f) < 0.5f, $"{name} must be 480 wide, was {r.W}");
            Assert.True(MathF.Abs(r.H - 360f) < 0.5f, $"{name} must be 360 tall, was {r.H}");
        }

        Assert.Equal(upperLeft.X + upperLeft.W, upperRight.X);
        Assert.Equal(lowerLeft.X + lowerLeft.W, lowerRight.X);
        Assert.Equal(upperLeft.Y + upperLeft.H, lowerLeft.Y);
        Assert.Equal(upperRight.Y + upperRight.H, lowerRight.Y);
    }

    [Fact]
    public void Every_quadrant_samples_the_whole_texture_minus_the_half_pixel_inset()
    {
        var (_, ctx, _) = Zoomed();
        var list = ctx.List.AsDrawList();
        var quads = list.TexturedQuads.ToArray();
        var ops = list.Ops.ToArray();

        // (0.5/256) inset each side (:446).
        var inset = 0.5f / 256f;
        var expected = new UiRect(inset, inset, 1f - 2f * inset, 1f - 2f * inset);

        int textured = 0;
        foreach (var op in ops)
        {
            if (op.Kind != UiPrimitiveKind.TexturedQuad)
                continue;
            var quad = quads[op.Index];
            Assert.Equal(expected.X, quad.Uv.X, 4);
            Assert.Equal(expected.Y, quad.Uv.Y, 4);
            Assert.Equal(expected.W, quad.Uv.W, 4);
            Assert.Equal(expected.H, quad.Uv.H, 4);
            textured++;
        }

        Assert.Equal(4, textured);
    }

    [Fact]
    public void The_corner_named_asked_its_own_stock_material()
    {
        var (_, _, lib) = Zoomed();

        string[] expected =
        [
            "HUD/scope_sniper_ul",
            "HUD/scope_sniper_ur",
            "HUD/scope_sniper_lr",
            "HUD/scope_sniper_ll",
        ];

        // The frame is described per layout pass; the cycle of four repeats.
        Assert.Equal(expected, lib.Asked.Take(4).ToArray());
        Assert.Equal(0, lib.Asked.Count % 4);
    }

    [Fact]
    public void The_scope_lands_under_the_hud_panels_as_stocks_z_pos_minus_one()
    {
        // CHudScope::SetZPos(-1) (tf_hud_scope.cpp:366-367) moves the black
        // bars behind the other HUD elements, so the corner panels keep
        // reading over them while zoomed. UiModScreens.Place keeps that on
        // the Hud layer by Order: the scope joins ahead of the hud screen
        // and paints first.
        var hudScreen = TF2.Client.Ui.TFHudSamples.Build(new UiSampleServices());
        var scopeScreen = new TF2.Client.Ui.TFScope(new TF2.Client.Ui.FakeScope());
        var (layer, _) = UiModScreens.Place([], [], [hudScreen, scopeScreen]);
        int iScope = System.Array.IndexOf(layer, scopeScreen);
        int iHud = System.Array.IndexOf(layer, hudScreen);
        Assert.True(iScope >= 0 && iHud >= 0 && iScope < iHud,
                    $"the scope ({iScope}) must draw before the hud panels ({iHud})");
    }
}
