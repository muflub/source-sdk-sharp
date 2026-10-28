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
/// <c>CTEBSPDecal</c>, played. The sprayed map decal, stamped onto brushwork.
///
/// <para>
/// The whole of <c>c_te_bspdecal.cpp</c>'s <c>C_TEBSPDecal::PostDataUpdate</c>:
/// </para>
/// <code>
/// IClientEntity *ent;
/// if ( ( ent = cl_entitylist-&gt;GetClientEntity( m_nEntity ) ) == NULL )
/// {
///     DevMsg( 1, "Decal: entity = %i", m_nEntity );
///     return;
/// }
/// if ( r_decals.GetInt() )
/// {
///     effects-&gt;DecalShoot( m_nIndex, m_nEntity, ent-&gt;GetModel(),
///                          ent-&gt;GetAbsOrigin(), ent-&gt;GetAbsAngles(),
///                          m_vecOrigin, 0, FDECAL_PERMANENT );
/// }
/// </code>
///
/// <para>
/// THE PILOT FOR A NESTED BASE, and that is why this class rather than another
/// of the six. <c>DT_TEBSPDecal</c> nests <c>DT_BaseTempEntity</c>, which most
/// of this build's temp entities still nest natively, so this was the first
/// managed <c>ClientClass</c> whose <c>"baseclass"</c> prop points at a
/// <c>RecvTable</c> stock still owns — resolved by name out of the client's own
/// class list by <c>FindRegisteredTable</c> in
/// <c>shell_tempents_client.cpp</c>. See that function's header comment for the
/// whole finding; 2E-bis' refusal is about two tables of ONE NAME, and there is
/// still exactly one <c>DT_BaseTempEntity</c> here — it simply has one more
/// managed parent and one fewer native one.
/// </para>
///
/// <para>
/// WHAT MOVED AND WHAT DID NOT. The decode moved and so did the decision, which
/// for this class is three named reads and a call. The ENTITY RESOLUTION did
/// not, and cannot: <c>DecalShoot</c> wants a <c>model_t *</c>, an abs origin
/// and abs angles, none of which crosses the ABI, so
/// <c>SourceSharp_ClientDecalShoot</c> resolves the index through
/// <c>cl_entitylist</c> on the native side and checks <c>r_decals</c> there
/// too. Both of stock's guards are still in the path, one step further along.
/// </para>
///
/// <para>
/// NO LEDGER ROW. This class's decals go through <c>effects-&gt;DecalShoot</c>,
/// the engine's own decal system, which was never guarded — it is
/// <c>c_te_decal.cpp</c> and <c>c_te_projecteddecal.cpp</c> that carry rows C2
/// and C3, and they reach <c>C_BaseEntity::AddDecal</c> instead. Stock's
/// <c>Precache()</c> was empty, so nothing else left with the file.
/// </para>
/// </summary>
[ClientTempEnt("CTEBSPDecal")]
public sealed class ClientTeBspDecal : ClientTempEnt<TeBspDecal>
{
    /// <summary>
    /// <c>FDECAL_PERMANENT</c>, which <c>c_te_bspdecal.cpp</c> defines for
    /// itself at the top of the file with an <c>UNDONE: Get rid of this?</c>
    /// over it. It is <c>decals.h</c>'s bit and the engine reads it out of the
    /// flags word; a BSP decal is permanent because it is map geometry the
    /// player sprayed, not a bullet hole that ages out.
    /// </summary>
    private const int FDecalPermanent = 0x01;

    private readonly IEngineEffects _effects;
    private readonly IDecalSettings _settings;

    public ClientTeBspDecal(IEngineEffects effects, IDecalSettings settings)
    {
        _effects = effects;
        _settings = settings;
    }

    public override void Play(TeBspDecal te)
    {
        // Both UNSIGNED on the wire (SPROP_UNSIGNED), so read unsigned: an
        // entity index and a decal index are counts, never quantities.

        // c_te_bspdecal.cpp's `if ( r_decals.GetInt() )` around its DecalShoot.
        if (!_settings.Decals)
            return;

        _effects.ClientDecalShoot(te.Index, te.Entity, te.Origin, sAxis: null, flags: FDecalPermanent);
    }
}
