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
/// <c>CTEMuzzleFlash</c>, played. The flash at the end of a barrel.
///
/// <para>
/// The whole of <c>c_te_muzzleflash.cpp</c>'s <c>C_TEMuzzleFlash::PostDataUpdate</c>:
/// </para>
/// <code>
/// //FIXME: Index is incorrect
/// g_pEffects-&gt;MuzzleFlash( m_vecOrigin, m_vecAngles, m_flScale, m_nType );
/// RecordMuzzleFlash( m_vecOrigin, m_vecAngles, m_flScale, m_nType );
/// </code>
///
/// <para>
/// <b><c>m_nType</c> IS PASSED THROUGH, AND THE CLIENT-SIDE <c>TE_MuzzleFlash</c>
/// PASSES A LITERAL ZERO.</b> That asymmetry is stock's and is reproduced
/// rather than tidied: a RECEIVED flash uses the type off the wire, and one a
/// client makes for itself is always type 0. Valve's <c>//FIXME: Index is
/// incorrect</c> sits over the first of those and not the second, so unifying
/// them would be finishing somebody's FIXME by guessing which way they meant it
/// to go. <c>shell_te_fwd_ce.cpp</c> keeps the zero.
/// </para>
///
/// <para>
/// THE ANGLES ARE READ AS A PLAIN COORD VECTOR, because that is how they were
/// sent. <see cref="Shared.TeMuzzleFlash"/> has the whole argument: the member
/// is a <c>CNetworkQAngle</c> and the prop is a <c>SendPropVector</c>, so a
/// negative pitch crosses the wire as a negative coordinate and arrives as one.
/// Nothing here re-ranges it into [0,360], and doing so would point the flash
/// the other way with nothing anywhere saying so.
/// </para>
///
/// <para>
/// LIVE FROM THE MOMENT IT REGISTERS, like <see cref="ClientTeSmoke"/> and for
/// the same reason: <c>CEffectsServer::MuzzleFlash</c> and
/// <c>UTIL_MuzzleFlash</c> both reach <c>te-&gt;MuzzleFlash</c>, and managed
/// weapons reach the first through <c>IEngineEffects.MuzzleFlashEffect</c>.
/// After this eviction the last link of that chain is the managed table instead
/// of the native one; nothing in between changed and nothing stopped firing.
/// </para>
///
/// <para>
/// NO LEDGER ROW. <c>g_pEffects-&gt;MuzzleFlash</c> switches over
/// <c>FX_MuzzleEffect</c>, <c>FX_GunshipMuzzleEffect</c> and
/// <c>FX_StriderMuzzleEffect</c> and prints a line for a type it does not know
/// — stock's own behaviour, left alone in the backend rather than turned into a
/// silent default. Stock's <c>Precache()</c> was empty.
/// </para>
/// </summary>
[ClientTempEnt("CTEMuzzleFlash")]
public sealed class ClientTeMuzzleFlash : ClientTempEnt<TeMuzzleFlash>
{
    private readonly IEngineEffects _effects;

    public ClientTeMuzzleFlash(IEngineEffects effects)
    {
        _effects = effects;
    }

    public override void Play(TeMuzzleFlash te)
    {
        // UNSIGNED, which is stock's own SPROP_UNSIGNED on a thirty-two bit
        // prop. Reading it signed would turn every type above 0x7FFFFFFF into a
        // negative the backend's switch has no arm for -- a value no caller
        // passes today, and exactly the kind of thing that becomes true later.

        _effects.ClientMuzzleFlash(te.Origin, te.Angles, te.Scale, te.Type);
    }
}
