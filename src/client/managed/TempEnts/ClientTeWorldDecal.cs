//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>CTEWorldDecal</c>, played. A decal on world brushwork, stamped where it is
/// told — no entity, no trace.
///
/// <para>
/// The whole of <c>c_te_worlddecal.cpp</c>'s <c>PostDataUpdate</c>:
/// </para>
/// <code>
/// if ( r_decals.GetInt() )
/// {
///     IClientEntity *ent = cl_entitylist-&gt;GetClientEntity( 0 );
///     if ( ent )
///     {
///         bool bNoBlood = UTIL_IsLowViolence();
///         bool bIsBlood = false;
///         if ( bNoBlood )
///         {
///             const char *pchDecalName = decalsystem-&gt;GetDecalNameForIndex( m_nIndex );
///             if ( pchDecalName &amp;&amp; V_stristr( pchDecalName, "blood" ) )
///                 bIsBlood = true;
///         }
///         if ( !( bNoBlood &amp;&amp; bIsBlood ) )
///             effects-&gt;DecalShoot( m_nIndex, 0, ent-&gt;GetModel(), ent-&gt;GetAbsOrigin(),
///                                  ent-&gt;GetAbsAngles(), m_vecOrigin, 0, 0 );
///     }
/// }
/// RecordWorldDecal( &amp;m_vecOrigin, m_nIndex );
/// </code>
///
/// <para>
/// THE SUPPRESSION IS WHY THIS CLASS WAITED. Three of the six decals lane CD
/// ported end in <c>ClientDecalShoot</c> and so does this one — the last line
/// here is the same call <c>ClientTeBspDecal</c> makes. What it could not do was
/// decide whether to make it: <c>UTIL_IsLowViolence()</c>'s deciding term is
/// <c>engine-&gt;IsLowViolence()</c>, which managed code cannot reach at all,
/// and the name lookup runs the wrong way round from the one slot that existed.
/// <see cref="IEngineEffects.ClientIsLowViolence"/> and
/// <see cref="IEngineEffects.ClientDecalNameFromIndex"/> are lane CX's answer to
/// both.
/// </para>
///
/// <para>
/// NOT <see cref="IEngineEffects.ClientDecalIndexFromName"/> BACKWARDS. That
/// slot asks the ENGINE's precached decal table; this one asks the GAME's decal
/// emitter system, loaded from <c>scripts/decals_subrect.txt</c>. Stock reads
/// each in its own place and this test reads the second — a name that came back
/// from the wrong table would suppress the wrong decals, or none.
/// </para>
///
/// <para>
/// AND THE TWO TABLES REALLY DO DISAGREE, measured on a live join rather than
/// reasoned about. <c>decals/bloodstain_001</c> — engine index 206, which lane
/// B's gate 3 used — is <b>not</b> in <c>decals_subrect.txt</c> at all, so
/// <c>GetDecalNameForIndex</c> answers <c>""</c> for it and it paints even
/// with <c>violence_hblood 0</c>. <c>decals/blood1_subrect</c>, which the
/// script does name, is suppressed. Both were photographed. That asymmetry is
/// STOCK's — this class asks the table stock asks — and it is worth knowing
/// before concluding the switch is broken: a decal that a caller precached for
/// itself has an engine index the game's decal script never saw.
/// </para>
///
/// <para>
/// <c>r_decals</c> AND THE WORLD LOOKUP DID NOT MOVE, and did not need to:
/// <c>SourceSharp_ClientDecalShoot</c> checks <c>r_decals</c> and resolves
/// entity 0 through <c>cl_entitylist</c> on the native side, because
/// <c>DecalShoot</c> wants the world's <c>model_t*</c>. Both of stock's guards
/// are still in the path, one step further along — the same division
/// <c>ClientTeBspDecal</c> already documents.
/// </para>
///
/// <para>
/// NO LEDGER ROW. This class's decals go through the engine's own decal system,
/// which was never guarded; C2 and C3 belong to the two classes that reach
/// <c>C_BaseEntity::AddDecal</c> instead.
/// </para>
///
/// <para>
/// NOT PORTED: <c>RecordWorldDecal</c>, the tool-recording KeyValues message.
/// SFM is dead (user ruling 2026-10-19). Stock's <c>Precache()</c> was empty.
/// </para>
/// </summary>
[ClientTempEnt("CTEWorldDecal")]
public sealed class ClientTeWorldDecal : ClientTempEnt<TeWorldDecal>
{
    private readonly IEngineEffects _effects;
    private readonly IDecalSettings _settings;

    public ClientTeWorldDecal(IEngineEffects effects, IDecalSettings settings)
    {
        _effects = effects;
        _settings = settings;
    }

    public override void Play(TeWorldDecal te)
    {
        // UNSIGNED on the wire (SPROP_UNSIGNED), nine bits.

        // c_te_worlddecal.cpp's `if ( r_decals.GetInt() )`, before anything
        // else is asked.
        if (!_settings.Decals)
            return;

        // THE ORDER IS STOCK'S AND MATTERS. The name is looked up only when
        // violence is turned down, so an ordinary client never asks the decal
        // script anything -- this fires for every bullet hole on brushwork.
        bool noBlood = _effects.ClientIsLowViolence();
        bool isBlood = false;

        if (noBlood)
        {
            string name = _effects.ClientDecalNameFromIndex(te.Index);

            // V_stristr, which is case-INSENSITIVE: the script's names are
            // "Blood_Big", "blood", "SmallBloodSplatter" and friends, and an
            // ordinal Contains would let half of them through.
            if (name.Contains("blood", StringComparison.OrdinalIgnoreCase))
                isBlood = true;
        }

        // Stock's own shape, kept rather than simplified to `if (!isBlood)`.
        // They are equal -- isBlood can only be true when noBlood is -- and
        // the day somebody sets isBlood from something else, the two stop being
        // equal and stock's is the one that still means what it says.
        if (!(noBlood && isBlood))
            _effects.ClientDecalShoot(te.Index, 0, te.Origin, sAxis: null, flags: 0);
    }
}
