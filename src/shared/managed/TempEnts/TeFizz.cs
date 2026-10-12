using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEFizz</c> — bubbles out of a brush, and BEHAVIOUR LEDGER ROW S1
/// (plan_tmp.md Part 1.1). The one temp entity in this port that is not a
/// transcription.
///
/// <para>
/// From <c>te_fizz.cpp:88</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEFizz, DT_TEFizz)
///     SendPropInt( SENDINFO(m_nEntity), MAX_EDICT_BITS, SPROP_UNSIGNED ),
///     SendPropModelIndex( SENDINFO(m_nModelIndex) ),
///     SendPropInt( SENDINFO(m_nDensity), 8, SPROP_UNSIGNED ),
///     SendPropInt(SENDINFO(m_nCurrent), 16 ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// WHAT ROW S1 IS. <c>ITempEntsSystem::Fizz</c> takes a
/// <c>const CBaseEntity *</c> and hands it to <c>TE_Fizz</c>, whose parameter is
/// a <c>const IServerEntity *</c> — a conversion that exists only while
/// <c>CBaseEntity</c> is a class of its own. It is becoming an alias for
/// <c>IGameEntity</c>, and <c>igameentity.h</c> keeps that interface off
/// <c>IServerEntity</c> deliberately, so the conversion is on its way out and
/// <c>te.cpp:377</c> already carries the <c>#ifndef SS_CBASEENTITY_ALIAS</c>
/// that will delete the call. The effect has no managed route at all today.
/// </para>
///
/// <para>
/// AND THE INTERFACE WAS NEVER NEEDED. Look at the table: the only thing about
/// the entity that has ever gone on the wire is <c>m_nEntity</c>, an EDICT
/// INDEX. <c>TE_Fizz</c> takes the pointer solely to call
/// <c>ENTINDEX( entity->GetNetworkable()->GetEdict() )</c> on it and throw the
/// rest away. So the managed API is
/// <c>TempEnts.Fizz(filter, delay, int entIndex, modelIndex, density, current)</c>
/// — no interface, no hierarchy, and nothing to keep working when
/// <c>CBaseEntity</c> stops being a type. That signature deviation is the whole
/// of row S1, and it is the only place in this port where the managed API is
/// deliberately not shaped like stock's <c>ITempEntsSystem</c>.
/// </para>
///
/// <para>
/// THE SERVER HALF IS RESTORED. THE CLIENT HALF IS ROW C6 AND IS NOT DONE.
/// <c>c_te_fizz.cpp</c> resolves <c>m_nEntity</c> through
/// <c>cl_entitylist->GetEnt</c>, which answers NULL for every entity on the
/// parallel hierarchy — so a Fizz sent from here ARRIVES AND IS DROPPED, with no
/// diagnostic on either side, until C6 resolves the index through the managed
/// entity list instead. That is expected, it is tracked, and it is not fixed by
/// changing what this table sends. It is also why
/// <see cref="TeBubbles"/> — which carries the corners themselves and needs no
/// entity on the far side — is what <c>env_bubbles</c> still emits.
/// </para>
/// </summary>
[Precache(PrecacheKind.Model, "sprites/bubble.vmt")]   // CTEFizz::Precache, te_fizz.cpp:82
[TempEntity]
public class TeFizz : TeBaseTempEntity
{
    /// <summary>
    /// The brush the bubbles come out of, as an edict index. <c>MAX_EDICT_BITS</c>
    /// wide — a SYMBOL native resolves from <c>public/const.h</c>, never a number
    /// typed here, because a prop one bit narrower than the other side expects is
    /// a bitstream that stops making sense partway through with no log line
    /// anywhere.
    ///
    /// <para>This is the field ledger rows S1 and C6 are both about.</para>
    /// </summary>
    [TeProp(BitsFrom = TeBits.MaxEdict, Flags = TeFlags.Unsigned)]
    public int Entity;

    /// <summary>
    /// The bubble sprite — <c>sprites/bubble.vmt</c>, which stock's
    /// <c>CTEFizz::Precache</c> precaches and plan 3.6 moves to a managed
    /// <c>TempEnts.Precache()</c> at the rewiring phase. Signed, and
    /// <c>SP_MODEL_INDEX_BITS</c> wide.
    /// </summary>
    [TeProp(BitsFrom = TeBits.ModelIndex)]
    public int ModelIndex;

    /// <summary>How many bubbles. Eight bits unsigned.</summary>
    [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
    public int Density;

    /// <summary>
    /// How hard the water is moving sideways, and THE ONE SIGNED FIELD HERE:
    /// <c>SendPropInt(SENDINFO(m_nCurrent), 16 )</c> passes no flags argument at
    /// all, so it is not <c>SPROP_UNSIGNED</c> and a current can run either way
    /// along the axis. Sixteen bits with the sign is [-32768, 32767]; the same
    /// sixteen unsigned would send a leftward current as a very fast rightward
    /// one.
    /// </summary>
    [TeProp(Bits = 16, Flags = TeFlags.None)]
    public int Current;
}
