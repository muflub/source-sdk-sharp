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
/// <c>CTEGlowSprite</c>, played. An additive sprite that loops its animation
/// for a stated time.
///
/// <para>
/// The whole of <c>c_te_glowsprite.cpp</c>'s <c>C_TEGlowSprite::PostDataUpdate</c>:
/// </para>
/// <code>
/// float a = ( 1.0 / 255.0 ) * m_nBrightness;
/// C_LocalTempEntity *ent = tempents-&gt;TempSprite( m_vecOrigin, vec3_origin, m_fScale,
///     m_nModelIndex, kRenderTransAdd, 0, a, m_fLife,
///     FTENT_SPRANIMATE | FTENT_SPRANIMATELOOP );
/// if ( ent ) { ent-&gt;bounceFactor = 0.2; }
/// </code>
///
/// <para>
/// THE SECOND CLASS THAT ONE HELPER UNBLOCKED, which is why
/// <c>docs/te-legacy-pool-survey.md</c> put <c>TempSprite</c> first in its
/// order. The three differences from <see cref="ClientTeSprite"/> are all on
/// the same call: a real lifetime, the LOOP flag, and the bounce factor.
/// </para>
///
/// <para>
/// <b>THE BOUNCE FACTOR IS THE ONE PLACE THIS PORT MOVED A LINE.</b> Stock takes
/// the tempent back from the helper and writes the field on it; here it is an
/// argument to the same call. <see cref="TempEntSpawns.TempSprite"/> says why at
/// length — briefly, writing one field of a live tempent from managed code would
/// have meant either growing the per-frame lifetime view with a field the expiry
/// decision does not read, or an ABI slot for one caller's one value that is set
/// once at spawn. A spawn record already carries fields set once at spawn.
/// </para>
///
/// <para>
/// AND IT IS NOT DECORATION. <c>bounceFactor</c> is what <c>Frame</c>'s
/// collision arm multiplies the velocity by when a tempent lands, so 0.2 is a
/// glow that stops nearly dead where the default 1 would keep it skipping. That
/// arm is still native and still reads the field, so the value has to arrive —
/// it is not one of the ones this build ignores.
/// </para>
///
/// <para>
/// NO LEDGER ROW.
/// </para>
/// </summary>
[ClientTempEnt("CTEGlowSprite")]
public sealed class ClientTeGlowSprite : ClientTempEnt<TeGlowSprite>
{
    private readonly TempEntSpawns _spawns;

    public ClientTeGlowSprite(TempEntSpawns spawns)
    {
        _spawns = spawns;
    }

    /// <summary>Stock's, on the line after the helper returns.</summary>
    private const float GlowBounceFactor = 0.2f;

    public override void Play(TeGlowSprite te)
    {
        float alpha = (1f / 255f) * te.Brightness;

        _spawns.TempSprite(
            pos: te.Origin,
            dir: Vector.Zero,
            scale: te.Scale,
            modelIndex: te.ModelIndex,
            renderMode: (int)RenderMode.TransAdd,
            renderFx: 0,
            alpha: alpha,

            // A REAL LIFETIME, unlike CTESprite. With the loop flag below there
            // is nothing else to stop it -- an animation that wraps never runs
            // off its last frame, so Frame() never asks it to die.
            life: te.Life,

            flags: (int)(TempEntFlags.SprAnimate | TempEntFlags.SprAnimateLoop),
            normal: default,
            bounceFactor: GlowBounceFactor);
    }
}
