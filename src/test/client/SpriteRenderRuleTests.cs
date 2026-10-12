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

    /// <summary>
    /// THE NO-DISSIPATION RENDER FX IS THE ENGINE'S OWN NUMBER, counted out of
    /// <c>const.h</c> rather than typed here.
    ///
    /// <para>
    /// The native fallback spells the SYMBOL (<c>kRenderFxNoDissipation</c>)
    /// and this side has to spell the NUMBER, because a render fx arrives here
    /// as an int off the wire. A wrong number does not fail to compile on
    /// either side: it makes the comparison never match, and every glow marked
    /// no-dissipation quietly starts fading with distance — which looks like a
    /// lighting change and not like a bug.
    /// </para>
    ///
    /// <para>
    /// <c>RenderFx_t</c> is an IMPLICIT enum — only its first member carries a
    /// value — so the number is its POSITION, and that is what this counts.
    /// </para>
    /// </summary>
    [Fact]
    public void TheNoDissipationFxNumberIsCountedOutOfTheEnginesOwnEnum()
    {
        string? path = RepoLayout.FindSdkFile("src/public/const.h");
        Assert.True(path is not null, "src/public/const.h was not found from the test's working directory");

        string text = File.ReadAllText(path!);
        int at = text.IndexOf("enum RenderFx_t", StringComparison.Ordinal);
        Assert.True(at >= 0, "const.h no longer declares RenderFx_t");

        int close = text.IndexOf("};", at, StringComparison.Ordinal);
        Assert.True(close > at, "RenderFx_t's body did not close");

        // Strip the // comments that sit after several members, then take the
        // members in order. kRenderFxNone = 0 and nothing else carries a value.
        string body = Regex.Replace(text[at..close], @"//[^\n]*", "");
        var members = Regex.Matches(body, @"\bkRenderFx\w+")
                           .Select(m => m.Value)
                           .ToList();

        Assert.True(members.Count > 20,
            $"RenderFx_t parsed as {members.Count} member(s) — the pattern stopped matching "
          + "rather than found anything wrong");
        Assert.Equal("kRenderFxNone", members[0]);
        Assert.Contains("kRenderFxNone = 0", body);

        int index = members.IndexOf("kRenderFxNoDissipation");
        Assert.True(index > 0, "const.h no longer declares kRenderFxNoDissipation");
        Assert.Equal(ClientSpriteRenderRules.RenderFxNoDissipation, index);
    }
}
