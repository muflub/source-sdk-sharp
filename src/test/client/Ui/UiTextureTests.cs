using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The toolkit's texture leaf (the surface a weapon inspect or a vendor's
/// portrait draws through): ui.Texture emits one textured quad, sized and
/// anchored like a Rect, keyed like anything else, and says which engine
/// surface it wants. The pixels are the engine's; the toolkit's job is the
/// rect, the handle and the render-list entry.
/// </summary>
public class UiTextureTests
{
    private static (string Svg, UiElementInfo[] Elements, UiContext Ctx) DrawTextured(int handle = 7)
    {
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
        {
            using (ui.Column(Space.S, Align.Start).Size(Size.Du(320), Size.Du(240)).Name("host"))
            {
                ui.Texture(handle, Size.Du(300), Size.Du(200)).Name("surface");
                ui.Rect(Tone.Accent, Size.Du(300), Size.Du(20)).Name("cap");
            }
        });
        return (Svg(ctx), Elements(ctx), ctx);
    }

    [Fact]
    public void A_texture_leaf_emits_one_textured_quad_with_its_handle()
    {
        var (svg, elements, _) = DrawTextured(7);
        // One Image element, one textured rect naming the handle; the leaf
        // never draws a filled quad of its own (the fill stays the engine's).
        Assert.Single(elements, e => e.Kind == UiElementKind.Image);
        Assert.Equal(1, svg.Split("data-texture=\"7\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("data-texture=\"8\"", svg);
    }

    [Fact]
    public void A_texture_leaf_takes_its_size_like_a_rect_and_stacks_in_order()
    {
        // 1920x1080 is scale 1: the du sizes come through as px, the leaf
        // first in the column with the caption below it.
        var (_, _, ctx) = DrawTextured();
        var surface = Named(ctx, "surface").Rect;
        var cap = Named(ctx, "cap").Rect;
        Assert.Equal(300, surface.W, 1);
        Assert.Equal(200, surface.H, 1);
        Assert.True(surface.Y < cap.Y, $"stack order lost: {surface} then {cap}");
    }

    [Fact]
    public void Two_texture_leaves_keep_their_handles_apart()
    {
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
        {
            using (ui.Row(Space.S, Align.Centre))
            {
                ui.Texture(11, Size.Du(100), Size.Du(100)).Name("left");
                ui.Texture(12, Size.Du(100), Size.Du(100)).Name("right");
            }
        });
        var svg = Svg(ctx);
        Assert.Equal(1, svg.Split("data-texture=\"11\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, svg.Split("data-texture=\"12\"", StringSplitOptions.None).Length - 1);
        // Distinct rects: each leaf sits at its own place in the stack.
        var left = Named(ctx, "left").Rect;
        var right = Named(ctx, "right").Rect;
        Assert.True(left.X < right.X, $"stack order lost: {left} then {right}");
        Assert.True(left.W > 0 && left.H > 0);
    }

    [Fact]
    public void A_zero_opacity_texture_draws_no_quad()
    {
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
            ui.Texture(5, Size.Du(100), Size.Du(100)).Opacity(0f).Name("gone"));
        Assert.DoesNotContain("data-texture", Svg(ctx));
    }
    [Fact]
    public void A_texture_leaf_samples_the_whole_surface_by_default()
    {
        var (_, _, ctx) = DrawTextured(7);
        var quad = Assert.Single(ctx.List.AsDrawList().TexturedQuads.ToArray());
        Assert.Equal(new UiRect(0, 0, 1, 1), quad.Uv);
    }

    [Fact]
    public void A_texture_leaf_given_a_uv_samples_only_that_part()
    {
        // A second camera draws a 512x288 view into the top left of a 512x512
        // target: the leaf shows that part, not the whole square.
        var uv = new UiRect(0, 0, 1f, 0.5625f);
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
            ui.Texture(9, Size.Du(320), Size.Du(180), uv).Name("picture"));
        var quad = Assert.Single(ctx.List.AsDrawList().TexturedQuads.ToArray());
        Assert.Equal((9, uv), (quad.Texture, quad.Uv));
        Assert.Equal(320, Named(ctx, "picture").Rect.W, 1);
    }

    [Fact]
    public void A_untinted_texture_leaf_draws_at_full_colour()
    {
        var (_, _, ctx) = DrawTextured();
        var quad = Assert.Single(ctx.List.AsDrawList().TexturedQuads.ToArray());
        Assert.Equal(new UiColor(255, 255, 255, 255), quad.Tint);
    }

    [Fact]
    public void A_tinted_texture_leaf_multiplies_its_pixels_by_the_tint()
    {
        // Stock's health cross fades to the death-warning red when the player
        // is dying: the leaf's pixels carry that colour.
        var tint = UiPaint.FromGame(new UiColor(255, 40, 30, 255));
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
            ui.Texture(5, Size.Du(100), Size.Du(100)).Tint(tint).Name("cross"));
        var quad = Assert.Single(ctx.List.AsDrawList().TexturedQuads.ToArray());
        Assert.Equal(new UiColor(255, 40, 30, 255), quad.Tint);
    }

    [Fact]
    public void A_rotated_texture_leaf_emits_its_rotation()
    {
        // Stock's flag arrow spins to the flag's bearing about the panel's
        // centre (CTFArrowPanel::Paint): the leaf carries the rotation and
        // keeps its rect for layout and hit-testing.
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
            ui.Texture(5, Size.Du(100), Size.Du(40)).Rotate(MathF.PI / 4f).Name("needle"));
        var quad = Assert.Single(ctx.List.AsDrawList().TexturedQuads.ToArray());
        Assert.Equal(MathF.PI / 4f, quad.Rotation, 4);
        Assert.Equal(UiBlend.Normal, quad.Blend);
        Assert.Equal(100, Named(ctx, "needle").Rect.W, 1);
    }

    [Fact]
    public void An_additive_texture_leaf_blends_additively()
    {
        // The damage ring's glow adds to what is behind it
        // (tf_hud_damageindicator.cpp's additive material).
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
            ui.Texture(5, Size.Du(100), Size.Du(40)).Blend(UiBlend.Additive).Name("glow"));
        var quad = Assert.Single(ctx.List.AsDrawList().TexturedQuads.ToArray());
        Assert.Equal(UiBlend.Additive, quad.Blend);
        Assert.Equal(0f, quad.Rotation, 4);
    }

    [Fact]
    public void A_rotated_texture_draws_as_its_four_rotated_corners()
    {
        // Half a turn about the centre swaps opposite corners: the SVG names
        // them as one polygon (N rounds to two decimals, as every SVG number).
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
            ui.Texture(5, Size.Du(100), Size.Du(40)).Rotate(MathF.PI).Name("spin"));
        var svg = Svg(ctx);
        var rect = Named(ctx, "spin").Rect;
        float cx = rect.X + rect.W / 2f, cy = rect.Y + rect.H / 2f, hw = rect.W / 2f, hh = rect.H / 2f;
        string points = $"{N(cx + hw)},{N(cy + hh)} {N(cx - hw)},{N(cy + hh)} {N(cx - hw)},{N(cy - hh)} {N(cx + hw)},{N(cy - hh)}";
        Assert.Contains($"<polygon points=\"{points}\"", svg);
        Assert.Contains("data-texture=\"5\"", svg);
    }

    [Fact]
    public void An_additive_texture_says_so_in_the_svg()
    {
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
            ui.Texture(5, Size.Du(100), Size.Du(40)).Blend(UiBlend.Additive).Name("glow"));
        Assert.Contains("mix-blend-mode:screen", Svg(ctx));
    }

    private static string N(float v)
    {
        var r = MathF.Round(v, 2);
        return (r == 0f ? 0f : r).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
