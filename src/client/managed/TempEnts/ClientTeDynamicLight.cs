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
/// <c>CTEDynamicLight</c>, played. A coloured light that appears, lives and
/// decays.
///
/// <para>
/// <c>c_te_dynamiclight.cpp</c>'s <c>C_TEDynamicLight::PostDataUpdate</c> is one
/// call into that file's own <c>TE_DynamicLight</c>, and this is the whole of
/// what that does:
/// </para>
/// <code>
/// dlight_t *dl = effects-&gt;CL_AllocDlight( nLightIndex );
/// if ( !dl ) return;
/// dl-&gt;origin = *org;   dl-&gt;radius = radius;
/// dl-&gt;color.r = r;     dl-&gt;color.g = g;   dl-&gt;color.b = b;
/// dl-&gt;color.exponent = exponent;
/// dl-&gt;die   = gpGlobals-&gt;curtime + time;
/// dl-&gt;decay = decay;
/// </code>
///
/// <para>
/// <b>NO NEW ABI SLOT, AND THIS IS WHY THE BRIEF ASKED.</b>
/// <c>IEngineVisibility.DynamicLightUpdate</c> has been on the ABI since
/// <c>light_dynamic</c> was ported and already does eight of the nine lines
/// above — <c>CL_AllocDlight</c> keyed by <see cref="DynamicLight.Key"/>,
/// origin, radius, the three colour bytes, the exponent and
/// <c>die = curtime + LiveFor</c>. It did not do the ninth.
/// </para>
///
/// <para>
/// <b>THE NINTH IS <c>decay</c>, AND ITS ABSENCE WAS INVISIBLE UNTIL THIS
/// CLASS.</b> Every light on that slot before now — <c>ClientDynamicLight</c>
/// and <c>ClientSpotlightEnd</c> — is REFRESHED every frame by its entity, and
/// a light rewritten sixty times a second cannot fade: it is rewritten before
/// it has decayed a measurable amount, so <c>dlight_t::decay</c> being left at
/// zero was correct and untestable. A temp entity light is the opposite shape:
/// it is placed once and never touched again, so the decay IS the fade. It is a
/// FIELD on <see cref="DynamicLight"/> rather than a slot of its own, because
/// the primitive is the same primitive and a second one would be two
/// descriptions of a dlight.
/// </para>
///
/// <para>
/// <b><c>exponent</c> IS READ SIGNED.</b> It is the inventory's one signed
/// eight-bit int — stock's flags argument for it is literally <c>0</c> where
/// <c>r</c>, <c>g</c> and <c>b</c> three lines above are
/// <c>SPROP_UNSIGNED</c> — and <see cref="Shared.TeDynamicLight"/> has the whole
/// argument. Reading it unsigned here would be right for every value a caller
/// passes today and wrong for the ones they do not.
/// </para>
///
/// <para>
/// <b>THE KEY IS <c>LIGHT_INDEX_TE_DYNAMIC</c> AND IT IS SHARED ON PURPOSE.</b>
/// Every temp entity light in the build asks for the same pool slot, so a
/// second one fired before the first has died MOVES it rather than lighting the
/// map twice. That is stock's behaviour, from <c>cdll_util.h</c>'s constant, and
/// it is what keeps a firefight from exhausting a fixed-size pool.
/// </para>
///
/// <para>
/// NO LEDGER ROW. <c>effects-&gt;CL_AllocDlight</c> is the engine's own dlight
/// pool, which was never guarded, and stock's <c>Precache()</c> was empty. The
/// tool recording <c>TE_DynamicLight</c> carried is not carried;
/// docs/divergences.md covers all eighteen.
/// </para>
/// </summary>
[ClientTempEnt("CTEDynamicLight")]
public sealed class ClientTeDynamicLight : ClientTempEnt<TeDynamicLight>
{
    /// <summary>
    /// <c>LIGHT_INDEX_TE_DYNAMIC</c> — <c>cdll_util.h:102</c>'s
    /// <c>0x10000000</c>.
    ///
    /// <para>
    /// Deliberately far above any entity index, because entity indices are what
    /// every other caller of the pool keys with: a temp entity light must not
    /// land in the slot an entity's light is using. Stock picks the constant for
    /// that reason and <c>c_npc_advisor.cpp</c> adds an entity index to it for
    /// the same one.
    /// </para>
    /// </summary>
    public const int TeDynamicLightKey = 0x10000000;

    private readonly IEngineVisibility _visibility;

    public ClientTeDynamicLight(IEngineVisibility visibility)
    {
        _visibility = visibility;
    }

    public override void Play(TeDynamicLight te)
    {
        // THROUGH THE DECLARATION. r, g and b are SPROP_UNSIGNED and exponent
        // is not -- it is the only signed eight-bit int in all forty-four
        // tables -- and that is written down once, on TeDynamicLight, rather
        // than chosen again here between te.UInt and te.Int.

        _visibility.DynamicLightUpdate(new DynamicLight
        {
            Key      = TeDynamicLightKey,

            // NOT an elight. Stock's TE_DynamicLight asks CL_AllocDlight, which
            // is the pool that lights the WORLD as well as models -- a temp
            // entity light is a muzzle flash on a wall, and an elight would
            // leave the wall dark.
            IsElight = 0,

            OriginX = te.Origin.X,
            OriginY = te.Origin.Y,
            OriginZ = te.Origin.Z,

            Radius   = te.Radius,
            Red      = te.R,
            Green    = te.G,
            Blue     = te.B,
            Exponent = te.Exponent,

            // die = curtime + m_fTime, which is what LiveFor means.
            LiveFor = te.Time,

            // And the fade, which is the field this class is the first caller
            // of -- see the class note.
            Decay = te.Decay,
        });
    }
}
