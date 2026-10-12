//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// HOW A SPRITE'S RENDER MODE IS DRAWN (plan_entity.md §3.9, SHIM_WAVE Lane 3):
/// whether it is opaque, which modes use the glow blend, which glow keeps its
/// size on screen, and the glow's two magic numbers. <see cref="Effects.SpriteRenderer"/>
/// reads them per draw; native once read them as a table it was handed per
/// level (OnSpriteRenderRules), which went when the whole draw decision moved
/// here (lane c2-fx).
/// </summary>
public static class ClientSpriteRenderRules
{
    /// <summary><c>kRenderNormal</c> (0) through <c>kRenderNone</c> (10).</summary>
    public const int RenderModes = 11;

    /// <summary>
    /// <c>kRenderFxNoDissipation</c> — the FIFTEENTH member of
    /// <c>RenderFx_t</c>, so the number is <b>14</b>.
    ///
    /// <para>
    /// <b>THIS WAS 15 AND THAT WAS A BUG.</b> <c>RenderFx_t</c>
    /// (<c>src/public/const.h:368</c>) is an implicit enum — only
    /// <c>kRenderFxNone = 0</c> carries a value — so the number is the member's
    /// POSITION, and counting the members rather than the lines gives 14. The
    /// render fx arrives as an int off the wire, so this side spells the number.
    /// </para>
    ///
    /// <para>
    /// A wrong number here does not fail to compile on either side. It makes
    /// <c>StandardGlowBlend</c>'s comparison never match, and every glow marked
    /// no-dissipation quietly starts fading with distance — which reads as a
    /// lighting change rather than as a bug, and which neither
    /// <c>tools/7a-effect-gate</c> nor <c>tools/live-gate</c> noticed, because
    /// neither map has a no-dissipation sprite on it.
    /// <c>TheNoDissipationFxNumberIsCountedOutOfTheEnginesOwnEnum</c> counts it
    /// out of <c>const.h</c> so it cannot be typed wrong again.
    /// </para>
    /// </summary>
    public const int RenderFxNoDissipation = 14;

    /// <summary>
    /// A sprite is OPAQUE only in <see cref="RenderMode.Normal"/>. Every other
    /// mode is blended, which is what puts it in the translucent pass.
    /// </summary>
    public static bool Opaque(int mode) => mode == (int)RenderMode.Normal;

    /// <summary>
    /// TWO MODES USE THE GLOW BLEND FUNCTION and no others:
    /// <see cref="RenderMode.Glow"/> and <see cref="RenderMode.WorldGlow"/>.
    ///
    /// <para>
    /// It is the same pair <c>SpriteRules.IgnoresDepth</c> names, and
    /// deliberately a second statement rather than a call to it: ignoring the
    /// depth buffer is about which PASS a sprite is drawn in, and this is about
    /// how its brightness is computed. They agree today; a build that changed
    /// one should have to change the other on purpose.
    /// </para>
    /// </summary>
    public static bool UsesGlowBlend(int mode)
        => mode == (int)RenderMode.Glow || mode == (int)RenderMode.WorldGlow;

    /// <summary>
    /// A GLOW IS FIXED-SIZE IN SCREEN SPACE; A WORLD GLOW IS NOT.
    ///
    /// <para>
    /// That single difference is the whole of what separates the two modes once
    /// the fade is applied: a <see cref="RenderMode.Glow"/> keeps its size on
    /// screen as the viewer walks away, and a
    /// <see cref="RenderMode.WorldGlow"/> shrinks with distance like the
    /// geometry it is hung on. dm_lockdown's door indicators are the second.
    /// </para>
    /// </summary>
    public static bool GlowIsScreenSpace(int mode)
        => UsesGlowBlend(mode) && mode != (int)RenderMode.WorldGlow;

    /// <summary>
    /// The distance at which a glow is at full brightness. Stock's own
    /// <c>UNDONE: Tweak these magic numbers (1200 — distance at full
    /// brightness)</c>.
    /// </summary>
    public const float GlowFullDistance = 1200.0f;

    /// <summary>What the distance is divided by to get the screen-space scale.</summary>
    public const float GlowScaleDivisor = 200.0f;
}
