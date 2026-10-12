using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The damage vignette points where the damage came from: its glow is
/// strongest on that edge and (near) zero on the opposite one.
/// </summary>
public class UiDamageTests
{
    private static readonly UiFontLibrary Fonts = new();

    /// <summary>The strongest alpha drawn on each edge band: left, right, top, bottom.</summary>
    private static (int Left, int Right, int Top, int Bottom) Glow(float yaw)
    {
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        var player = new FakeLocalPlayer { LastDamageAt = 99.9, LastDamageYaw = yaw };
        UiSamples.Render(ctx, HudSamples.Build(new UiSampleServices(), player, stamp: new FakeBuildStamp { Visible = false }), UiSamples.Screen(1920, 1080), 100.0);

        int left = 0, right = 0, top = 0, bottom = 0;
        var list = ctx.List.AsDrawList();
        var danger = SourceSharpTheme.Create().Color(Tone.Danger);
        foreach (var op in list.Ops)
        {
            if (op.Kind != UiPrimitiveKind.Quad)
                continue;
            var q = list.Quads[op.Index];
            if (q.Color with { A = 255 } != danger || q.Fade == UiFade.None)
                continue;
            int a = Math.Max(q.Color.A, q.AlphaEnd);
            var r = q.Rect;
            if (r.H >= 1080 && r.X == 0) left = Math.Max(left, a);
            else if (r.H >= 1080) right = Math.Max(right, a);
            else if (r.Y == 0) top = Math.Max(top, a);
            else bottom = Math.Max(bottom, a);
        }
        return (left, right, top, bottom);
    }

    [Theory]
    [InlineData(-90f, "left")]
    [InlineData(90f, "right")]
    [InlineData(0f, "front")]
    [InlineData(180f, "behind")]
    public void The_glow_is_on_the_side_the_damage_came_from(float yaw, string side)
    {
        var g = Glow(yaw);
        var (hit, opposite) = side switch
        {
            "left" => (g.Left, g.Right),
            "right" => (g.Right, g.Left),
            "front" => (g.Top, g.Bottom),
            _ => (g.Bottom, g.Top),
        };
        int strongest = Math.Max(Math.Max(g.Left, g.Right), Math.Max(g.Top, g.Bottom));
        Assert.True(hit > 60 && hit == strongest, $"{side}: hit edge {hit}, all {g}");
        Assert.True(opposite <= 8, $"{side}: opposite edge {opposite}, all {g}");
    }

    [Fact]
    public void Damage_from_an_unknown_direction_glows_on_every_edge()
    {
        var g = Glow(float.NaN);
        Assert.True(g.Left > 40 && g.Left == g.Right && g.Right == g.Top && g.Top == g.Bottom, $"{g}");
    }
}
