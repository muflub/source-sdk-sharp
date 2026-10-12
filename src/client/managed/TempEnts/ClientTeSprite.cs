//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>CTESprite</c>, played. One additive sprite that plays its animation once
/// and goes.
///
/// <para>
/// The whole of <c>c_te_sprite.cpp</c>'s <c>C_TESprite::PostDataUpdate</c>:
/// </para>
/// <code>
/// float a = ( 1.0 / 255.0 ) * m_nBrightness;
/// tempents-&gt;TempSprite( m_vecOrigin, vec3_origin, m_fScale, m_nModelIndex,
///                       kRenderTransAdd, 0, a, 0, FTENT_SPRANIMATE );
/// </code>
///
/// <para>
/// A LIFETIME OF ZERO IS NOT "no lifetime". <see cref="TempEntSpawns.TempSprite"/>
/// reads it as "work one out" — <c>frameCount * 0.1 + 1</c> seconds, which at
/// the frame rate that helper sets is one full cycle with a second to spare.
/// Passing a real lifetime here would make the sprite outlive or undercut its
/// own animation, and it is the difference between this class and
/// <see cref="ClientTeGlowSprite"/>, which sends one.
/// </para>
///
/// <para>
/// THE BRIGHTNESS BECOMES AN ALPHA, and the eighth of a byte it loses on the
/// way is stock's: <c>m_nBrightness</c> is eight unsigned bits, divided by 255
/// into a float here and multiplied back by 255 inside the helper. The round
/// trip is exact for every value the wire can carry, which is why stock does it
/// this way round rather than passing the byte.
/// </para>
///
/// <para>
/// NO LEDGER ROW. The effect this class produces is the same one the native
/// class produced, through the same helper, now in C#.
/// </para>
/// </summary>
[ClientTempEnt("CTESprite")]
public sealed class ClientTeSprite : ClientTempEnt<TeSprite>
{
    private readonly TempEntSpawns _spawns;

    public ClientTeSprite(TempEntSpawns spawns)
    {
        _spawns = spawns;
    }

    /// <summary><c>kRenderTransAdd</c>, which is what makes a sprite glow rather than sit.</summary>
    private const int RenderTransAdd = (int)RenderMode.TransAdd;

    public override void Play(TeSprite te)
    {
        // UNSIGNED both: a model index is thirteen bits and a brightness is
        // eight, and neither is ever negative on this wire.

        float alpha = (1f / 255f) * te.Brightness;

        _spawns.TempSprite(
            pos: te.Origin,

            // vec3_origin: a TE_Sprite does not move. The helper writes it
            // straight into the tempent's velocity, and FTENT_GRAVITY is not
            // among the flags below, so it stays where it was put.
            dir: Vector.Zero,

            scale: te.Scale,
            modelIndex: te.ModelIndex,
            renderMode: RenderTransAdd,

            // kRenderFxNone. Stock passes a literal 0 here rather than the
            // name; it is the same value and this says which.
            renderFx: 0,

            alpha: alpha,

            // See the class note: zero means "as long as the animation takes".
            life: 0f,

            flags: (int)TempEntFlags.SprAnimate);
    }
}
