using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The live backend's textured-quad path: a rotated or additive leaf goes to
/// the surface as its four corners about the centre (CTFArrowPanel::Paint's
/// rotation, tf_hud_damageindicator.cpp's additive glow), while the plain
/// unrotated leaf keeps the textured rect it always drew.
/// </summary>
public class VguiTexturedQuadTests
{
    private static (UiFidelityTests.RecordingSurface Surface, UiContext Ctx) Draw(Func<UiBox, UiBox> shape)
    {
        var surface = new UiFidelityTests.RecordingSurface();
        var backend = new VguiBackend(surface, new UiFontLibrary(), new UiFontFiles([]));
        var ctx = Context(PlainTheme());
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) => shape(ui.Texture(5, Size.Du(100), Size.Du(40)).Name("leaf")));
        backend.Draw(ctx.List.AsDrawList());
        return (surface, ctx);
    }

    [Fact]
    public void A_rotated_texture_draws_as_the_corners_about_its_centre()
    {
        // Half a turn about the centre swaps opposite corners; the uv corners
        // stay with their positions ((0,0) TL … (0,1) BL) and the leaf draws
        // opaque white, not additive.
        var (surface, ctx) = Draw(t => t.Rotate(MathF.PI));
        var quad = Assert.Single(surface.Quads);
        var rect = Named(ctx, "leaf").Rect;
        float cx = rect.X + rect.W / 2f, cy = rect.Y + rect.H / 2f, hw = rect.W / 2f, hh = rect.H / 2f;
        float[] want =
        [
            cx + hw, cy + hh, 0f, 0f,
            cx - hw, cy + hh, 1f, 0f,
            cx - hw, cy - hh, 1f, 1f,
            cx + hw, cy - hh, 0f, 1f,
        ];
        Assert.Equal(want.Length, quad.Xyuv.Length);
        for (int i = 0; i < want.Length; i++)
            Assert.Equal(want[i], quad.Xyuv[i], 3f);
        Assert.False(quad.Additive);
        Assert.Equal(0xFFFFFFFFu, quad.Rgba);
    }

    [Fact]
    public void An_additive_texture_draws_through_the_slot_additively()
    {
        // The damage ring's glow: the pixels add to what is behind.
        var (surface, _) = Draw(t => t.Blend(UiBlend.Additive));
        var quad = Assert.Single(surface.Quads);
        Assert.True(quad.Additive);
    }

    [Fact]
    public void An_unrotated_opaque_leaf_still_draws_as_the_plain_rect()
    {
        var (surface, _) = Draw(t => t);
        Assert.Empty(surface.Quads);
        Assert.Equal(1, surface.TexturedRects);
    }
}
