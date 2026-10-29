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
/// <c>CTEFootprintDecal</c>, played. A footprint, with a facing.
///
/// <para>
/// ITS FILE IS <c>c_te_footprint.cpp</c>, NOT <c>c_te_footprintdecal.cpp</c>.
/// The class is <c>C_TEFootprintDecal</c> and the table is
/// <c>DT_TEFootprintDecal</c>; only the filename is short. The server's half is
/// <c>te_footprintdecal.cpp</c>, which IS the long name — so the two sides do
/// not even agree with each other. Lane E lost time to the same trap on
/// <c>te_glassshatter.cpp</c>, and a lane that greps for the long name here
/// concludes the file is already gone and evicts nothing.
/// </para>
///
/// <para>
/// The whole of <c>C_TEFootprintDecal::PostDataUpdate</c>:
/// </para>
/// <code>
/// // FIXME: Make this choose the decal based on material type
/// if ( r_decals.GetInt() )
/// {
///     IClientEntity *ent = cl_entitylist-&gt;GetClientEntity( m_nEntity );
///     if ( ent )
///     {
///         effects-&gt;DecalShoot( m_nIndex, m_nEntity, ent-&gt;GetModel(),
///             ent-&gt;GetAbsOrigin(), ent-&gt;GetAbsAngles(), m_vecOrigin,
///             &amp;m_vecDirection, 0 );
///     }
/// }
/// </code>
///
/// <para>
/// <c>m_chMaterialType</c> IS DECODED AND NOT USED, and that is stock's own
/// state rather than something this port dropped: the <c>FIXME</c> above the
/// body is Valve's, and neither <c>PostDataUpdate</c> nor the free
/// <c>TE_FootprintDecal</c> ever reads the byte. It is still on the wire, still
/// in the blob at its declared one-byte width, and still readable here the day
/// somebody finishes the FIXME — what would be wrong is quietly deleting the
/// field, because then the two sides' tables would stop pairing and the reason
/// would be a comment nobody kept.
/// </para>
///
/// <para>
/// THE DIRECTION IS THE DECAL'S S AXIS, which is what makes a footprint point
/// somewhere. It is the only one of this lane's six that passes a non-NULL
/// axis; the world and BSP decals pass NULL and let the engine pick. Stock
/// takes the address of its own member and the vector is already normalised —
/// <c>TE_FootprintDecal</c> on the server normalises the "right" vector before
/// it sends it.
/// </para>
///
/// <para>
/// NO LEDGER ROW. This reaches <c>effects-&gt;DecalShoot</c>, the engine's own
/// decal system, which was never guarded — and its entity resolution goes
/// through <c>cl_entitylist-&gt;GetClientEntity</c>, which answers for BOTH
/// hierarchies. C2 and C3 are the two classes that reach
/// <c>C_BaseEntity::AddDecal</c> instead. Stock's <c>Precache()</c> was empty.
/// </para>
/// </summary>
[ClientTempEnt("CTEFootprintDecal")]
public sealed class ClientTeFootprintDecal : ClientTempEnt<TeFootprintDecal>
{
    private readonly IEngineEffects _effects;
    private readonly IDecalSettings _settings;

    public ClientTeFootprintDecal(IEngineEffects effects, IDecalSettings settings)
    {
        _effects = effects;
        _settings = settings;
    }

    public override void Play(TeFootprintDecal te)
    {
        // c_te_footprint.cpp's `if ( r_decals.GetInt() )`. The entity lookup
        // is the backend's, where the model_t* it needs actually lives.
        if (!_settings.Decals)
            return;

        // Zero flags: a footprint is not FDECAL_PERMANENT.
        _effects.ClientDecalShoot(te.Index, te.Entity, te.Origin, sAxis: te.Direction, flags: 0);
    }
}
