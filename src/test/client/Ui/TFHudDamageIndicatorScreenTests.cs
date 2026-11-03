using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The damage indicator's screen (tf_hud_damageindicator.cpp with the account
/// panel's numbers): the taken hits draw quads only — stock's quad carries no
/// number — and the dealt damage floats where the read's projection puts it
/// (tf_hud_account.cpp's m_bWorldSpace paint).
/// </summary>
public class TFHudDamageIndicatorScreenTests
{
    private static readonly UiFontLibrary Fonts = new();

    private static (UiSample Sample, UiContext Ctx, UiElementInfo[] Elements) Rendered(string slug, int w = 1280, int h = 720,
                                                                                       IUiTextureLibrary? hudArt = null)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfdamageindicator" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(hudArt), UiSamples.Screen(w, h), sample.Now);
        return (sample, ctx, UiTestKit.Elements(ctx));
    }

    private static int[] Named(UiElementInfo[] elements, string name, UiContext ctx)
        => Enumerable.Range(0, elements.Length)
            .Where(i => UiTestKit.NameOf(ctx, elements[i]) == name)
            .ToArray();

    [Fact]
    public void The_taken_quads_carry_no_number()
    {
        var (_, ctx, elements) = Rendered("three-hits-and-a-heavy");

        Assert.Empty(Named(elements, "damage number", ctx));
        Assert.NotEmpty(Named(elements, "indicator", ctx));
    }

    [Fact]
    public void The_indicator_is_the_additive_glow_turned_to_the_hit()
    {
        // Stock draws the VGUI/damageindicator quad additive, white, turned
        // about its centre (DrawDamageIndicator, tf_hud_damageindicator.cpp:
        // 182-231; the angle is GetDamagePosition's flRotation negated). The
        // sample's one hit sits at yaw -90, so the leaf turns -90°.
        var lib = new TFHudArtTests.RecordingLibrary();
        var (_, ctx, elements) = Rendered("hit-from-the-left", hudArt: lib);
        Assert.Contains("vgui/damageindicator", lib.Asked);

        int index = Named(elements, "indicator", ctx).Single();
        var ops = ctx.List.AsDrawList().Ops.ToArray();
        var quads = ctx.List.AsDrawList().TexturedQuads.ToArray();
        var op = Assert.Single(ops, o => o.Element == index && o.Kind == UiPrimitiveKind.TexturedQuad);
        Assert.Equal(UiBlend.Additive, quads[op.Index].Blend);
        Assert.Equal(-90f * MathF.PI / 180f, quads[op.Index].Rotation, 4);
    }

    [Fact]
    public void The_dealt_numbers_float_where_the_read_projects()
    {
        var (_, ctx, elements) = Rendered("three-hits-and-a-heavy");

        var pills = Named(elements, "combat text", ctx);
        Assert.Equal(2, pills.Length);
        // The sample projector's ladder of spots, du over the pixel scale:
        // each number where its projection named, later index right and down.
        Assert.True(elements[pills[0]].Rect.X < elements[pills[1]].Rect.X,
                    $"the older pill at {elements[pills[0]].Rect.X} must sit left of the newer at {elements[pills[1]].Rect.X}");
        Assert.True(elements[pills[0]].Rect.Y < elements[pills[1]].Rect.Y,
                    $"the older pill at {elements[pills[0]].Rect.Y} must sit above the newer at {elements[pills[1]].Rect.Y}");
    }
}
