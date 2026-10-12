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
/// <c>CTEArmorRicochet</c>, played — the same sparks as
/// <see cref="ClientTeMetalSparks"/> AND the ricochet sound, which is the whole
/// difference and is stock's own comment on <c>IEffects</c>.
///
/// <para>
/// The whole of <c>c_te_armorricochet.cpp</c>'s
/// <c>C_TEArmorRicochet::PostDataUpdate</c>, minus the decode:
/// </para>
/// <code>
/// g_pEffects->Ricochet( m_vecPos, m_vecDir );
/// RecordArmorRicochet( m_vecPos, m_vecDir );
/// </code>
///
/// <para>
/// <b>IT READS FIELDS IT DOES NOT DECLARE, AND THAT IS THE POINT OF THE PAIR.</b>
/// <c>DT_TEArmorRicochet</c> declares no props at all
/// (<c>IMPLEMENT_SERVERCLASS_ST( CTEArmorRicochet, DT_TEArmorRicochet)</c>
/// followed immediately by <c>END_SEND_TABLE()</c>); everything on the wire
/// belongs to the nested <c>DT_TEMetalSparks</c>. A base nested at datatable
/// offset zero puts its props at their own offsets in the SAME object, so
/// <c>m_vecPos</c> and <c>m_vecDir</c> are in this class's blob and are read
/// here by name. On the C++ side stock spells that as
/// <c>class C_TEArmorRicochet : public C_TEMetalSparks</c> and inherits the
/// members; on this side the blob carries them and
/// <see cref="TempEntReader"/> addresses them, which is one mechanism instead
/// of two.
/// </para>
///
/// <para>
/// <b>WHY IT IS A SEPARATE CLASS AT ALL</b>, when it is prop for prop identical
/// to the table it nests: because the CLIENT does something different with it —
/// <c>Ricochet</c> rather than <c>MetalSparks</c>, which is the sound — and
/// because the set of registered names must be exactly stock's (plan 2E). This
/// is not a place to tidy up, and a "simplification" that routed both names at
/// one backend would be a silent ricochet.
/// </para>
///
/// <para>
/// <b>THE ONLY CHAINED BASE IN THE INVENTORY MOVED AS ONE UNIT.</b>
/// <c>DT_TEMetalSparks</c> is the only table that is both a concrete effect and
/// somebody else's base, and it has exactly one user —
/// <c>CTEArmorRicochet</c> — so 2E-bis's rule (a table moves to the managed
/// client only when EVERY native user of its name is evicted) is satisfied by
/// this lane and by this lane alone. Both classes are in one file,
/// <c>c_te_armorricochet.cpp</c>, so the eviction that frees one name frees the
/// other in the same edit.
/// </para>
///
/// <para>
/// <b>THE FIRST CLASS TO PROVE THE NESTED-DATATABLE PATH ON A LIVE WIRE.</b>
/// <see cref="ClientTeEnergySplash"/> says what a green gate 3 on IT could not
/// prove: <c>DT_TEEnergySplash</c> is <c>NOBASE</c>, so nothing about it
/// exercises a <c>"baseclass"</c> prop, and lane S4 had found a crash down that
/// path — a datatable prop at offset zero is marked <c>SPROP_COLLAPSIBLE</c> by
/// the sender and applied by the receiver at the OBJECT BASE. Both builders
/// refuse or warn about the shape and <c>tempent_plan_test.cpp</c> pins the
/// baseclass exemption, but until this class nothing had put one on a wire.
/// It has now: <c>cs_sv_te_dump</c> and <c>cs_cl_te_dump</c> both read
/// <c>DT_TEArmorRicochet</c> as one <c>datatable</c> prop named
/// <c>baseclass</c> at offset 0 over <c>DT_TEMetalSparks</c>, 1 of 1 offsets
/// agreeing, and a fired ricochet decoded the nested table's two vectors and
/// drew. That is the sentence <c>ClientTeEnergySplash</c> left for a later
/// class to write.
/// </para>
///
/// <para>
/// <c>RecordArmorRicochet</c> lapses with the eviction exactly as
/// <c>RecordMetalSparks</c> does; see <see cref="ClientTeMetalSparks"/> for why
/// that is written down rather than quietly dropped, and why the fix belongs in
/// C-Phase 2's backend file rather than here.
/// </para>
/// </summary>
[ClientTempEnt("CTEArmorRicochet")]
public sealed class ClientTeArmorRicochet : ClientTempEnt<TeArmorRicochet>
{
    private readonly IEngineEffects _effects;

    public ClientTeArmorRicochet(IEngineEffects effects)
    {
        _effects = effects;
    }

    public override void Play(TeArmorRicochet te)
    {
        // The base's two fields, read out of the derived class's blob. See the
        // class note: DT_TEArmorRicochet declares nothing of its own, so this is
        // entirely TeMetalSparks' pair -- and the declaration reader walks the
        // chain root-first, which is what makes reading them here a plain
        // Read<T> rather than a lookup that has to know where they live.

        // Ricochet, NOT MetalSparks. The sparks are the same; the sound is not.
        _effects.ClientRicochet(te.Position, te.Direction);
    }
}
