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
}
