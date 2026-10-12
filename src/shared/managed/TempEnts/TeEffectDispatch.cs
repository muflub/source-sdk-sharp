using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEEffectDispatch</c> — one temp entity standing for fifty-two client
/// effects, and the busiest one in the build.
///
/// <para>
/// From <c>te_effect_dispatch.cpp:55</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST( CTEEffectDispatch, DT_TEEffectDispatch )
///     SendPropDataTable( SENDINFO_DT( m_EffectData ), &amp;REFERENCE_SEND_TABLE( DT_EffectData ) )
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// ONE PROP, AND IT IS THE INVENTORY'S ONLY NESTED DATATABLE. Forty-three tables
/// nest a BASE — a <c>SendPropDataTable</c> the engine names <c>"baseclass"</c>
/// and puts first — and this one nests a table as a FIELD, under its own name,
/// which is a different shape. The distinction is not cosmetic: the client's
/// <c>c_te_effect_dispatch.cpp:164</c> declares
/// <c>RecvPropDataTable( RECVINFO_DT( m_EffectData ), 0, ... )</c> ALONGSIDE the
/// <c>baseclass</c> its <c>IMPLEMENT_CLIENTCLASS_EVENT_DT</c> supplies, and the
/// two sides pair prop by prop by name. Flattening <c>DT_EffectData</c>'s
/// twenty-eight props into this table would leave the client with a datatable
/// prop nothing matches and twenty-eight send props with no home.
/// </para>
///
/// <para>
/// THIS STRUCT HAS ONE FIELD OF THE NESTED TYPE because
/// <c>SendProxy_DataTableToDataTable</c> shifts the object pointer by the
/// datatable prop's offset, so <c>DT_EffectData</c>'s props read at their own
/// offsets out of THIS blob — which means the blob has to CONTAIN a
/// <see cref="TeEffectData"/>, sized by <c>EntityLayout</c> rather than by a
/// constant somebody maintains.
/// </para>
///
/// <para>
/// <b>And it does NOT sit at offset zero, which cost this lane a segfault to
/// learn.</b> <c>TeClassBuilder</c> puts four bytes of nothing in front of it,
/// for one line of <c>dt_send.cpp</c>:
/// <c>SendPropDataTable</c> marks a prop <c>SPROP_COLLAPSIBLE</c> when the
/// proxy is the identity AND THE OFFSET IS ZERO, and a collapsed datatable is
/// applied by the receiving side at the OBJECT BASE rather than through the
/// prop's own offset. With the nested table at zero, the client wrote
/// <c>CEffectData</c>'s first twelve bytes over <c>C_TEEffectDispatch</c>'s
/// vtable pointer and took a NULL virtual call inside
/// <c>engine-&gt;FireEvents()</c> — after a clean join, with
/// <c>cs_sv_te_dump</c> agreeing 28 of 28. Stock cannot reach it:
/// <c>SENDINFO_DT( m_EffectData )</c> is an <c>offsetof</c> inside a class with
/// a vtable and is never zero. See <c>TeClassBuilder.NestedTablePad</c>, and
/// <c>TempEntEffectDataTests.TheNestedTableMayNotSitAtOffsetZero</c>, which
/// reads that engine line off disk.
/// </para>
///
/// <para>
/// <b>The branch this build takes.</b> <c>DT_EffectData</c> has two
/// preprocessor forks in it (<c>HL2_DLL</c> for the origins,
/// <c>TF_DLL</c> for the normal) and <see cref="TeEffectData"/> takes the
/// <c>#else</c> of both. Plan 2G asserts that; this lane checked it rather than
/// trusting it — neither symbol appears in any <c>$PreprocessorDefinitions</c>
/// anywhere in THIS build's own <c>$Include</c> chain (the tree still carries
/// five <c>.vpc</c> files that do define them, and every one of them builds a
/// different mod), which is what
/// <c>TempEntEffectDataTests.NeitherHl2NorTfIsDefinedInThisBuild</c> reads off
/// disk. Taking the HL2 arm would have produced coordinates a few inches out
/// with nothing anywhere saying so.
/// </para>
///
/// <para>
/// <b>Behaviour ledger row S4 is N/A, not a gap.</b>
/// <c>te_effect_dispatch.cpp:80</c> guards its <c>AddString</c> with
/// <c>#ifndef SS_CBASEENTITY_ALIAS</c>, and BOTH ARMS ARE THE SAME CALL:
/// <c>CBaseEntity::IsServer()</c> is <c>baseentity.h:2077</c>'s
/// <c>return true</c>, a compile-time constant that exists for debugging shared
/// code, and this file is <c>game/server/</c> only. The forward in
/// <c>shell_te_fwd_s4.cpp</c> passes <c>true</c> and nothing is lost. Row S4 is
/// on the ledger because somebody had to check that, not because anything is
/// missing.
/// </para>
/// </summary>
[TempEntity]
public class TeEffectDispatch : TeBaseTempEntity
{
    /// <summary>
    /// The whole payload, nested as a table rather than spelled out — see the
    /// class note. Its wire name is <c>m_EffectData</c> and the client matches
    /// on it.
    /// </summary>
    [TeProp(TePropKind.DataTable, SubTable = "DT_EffectData")]
    public TeEffectData EffectData;
}
