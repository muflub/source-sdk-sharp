using System.Text.RegularExpressions;
using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The render-mode rules a sprite is drawn by (plan_entity.md §3.9, SHIM_WAVE
/// Lane 3 gate 2), which SpriteRenderer reads per draw.
///
/// <para>
/// ONE FACT PER RENDER MODE, which is what the gate asks for, and each of them
/// was RED before the move because there was nothing on this side to ask: the
/// mapping lived as five <c>rendermode ==</c> comparisons inside a draw path,
/// where the only way to observe it was to look at a screenshot of a corridor.
/// </para>
/// </summary>
public class SpriteRenderRuleTests
{
    // ------------------------------------------------------------------
    // A fact per render mode.
    // ------------------------------------------------------------------

    [Fact]
    public void KRenderNormalIsOpaque()
        => Assert.True(ClientSpriteRenderRules.Opaque((int)RenderMode.Normal));

    [Fact]
    public void KRenderNormalDoesNotUseTheGlowBlend()
        => Assert.False(ClientSpriteRenderRules.UsesGlowBlend((int)RenderMode.Normal));

    [Fact]
    public void KRenderGlowUsesTheGlowBlend()
        => Assert.True(ClientSpriteRenderRules.UsesGlowBlend((int)RenderMode.Glow));

    [Fact]
    public void KRenderGlowIsNotOpaque()
        => Assert.False(ClientSpriteRenderRules.Opaque((int)RenderMode.Glow));

    [Fact]
    public void KRenderGlowIsFixedSizeInScreenSpace()
        => Assert.True(ClientSpriteRenderRules.GlowIsScreenSpace((int)RenderMode.Glow));

    [Fact]
    public void KRenderWorldGlowUsesTheGlowBlend()
        => Assert.True(ClientSpriteRenderRules.UsesGlowBlend((int)RenderMode.WorldGlow));

    [Fact]
    public void KRenderWorldGlowKeepsItsWorldSize()
        => Assert.False(ClientSpriteRenderRules.GlowIsScreenSpace((int)RenderMode.WorldGlow));

    [Fact]
    public void KRenderTransAddIsBlendedButNotAGlow()
    {
        Assert.False(ClientSpriteRenderRules.Opaque((int)RenderMode.TransAdd));
        Assert.False(ClientSpriteRenderRules.UsesGlowBlend((int)RenderMode.TransAdd));
    }

    // ------------------------------------------------------------------
    // Whole-range invariants.
    // ------------------------------------------------------------------

    [Fact]
    public void ExactlyOneRenderModeIsOpaque()
        => Assert.Equal(1, Enumerable.Range(0, ClientSpriteRenderRules.RenderModes)
                                     .Count(ClientSpriteRenderRules.Opaque));

    [Fact]
    public void ExactlyTwoRenderModesUseTheGlowBlend()
        => Assert.Equal(2, Enumerable.Range(0, ClientSpriteRenderRules.RenderModes)
                                     .Count(ClientSpriteRenderRules.UsesGlowBlend));

    [Fact]
    public void EveryScreenSpaceModeIsAlsoAGlow()
    {
        for (int m = 0; m < ClientSpriteRenderRules.RenderModes; m++)
        {
            if (ClientSpriteRenderRules.GlowIsScreenSpace(m))
                Assert.True(ClientSpriteRenderRules.UsesGlowBlend(m), $"render mode {m}");
        }
    }

    [Fact]
    public void TheGlowReachesFullBrightnessAtTwelveHundredUnits()
        => Assert.Equal(1200.0f, ClientSpriteRenderRules.GlowFullDistance);

    [Fact]
    public void TheScreenSpaceGlowScaleDivisorIsTwoHundred()
        => Assert.Equal(200.0f, ClientSpriteRenderRules.GlowScaleDivisor);

}
