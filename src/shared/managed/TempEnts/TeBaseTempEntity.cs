using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_BaseTempEntity</c> — the empty table every temp entity's chain ends at.
///
/// <para>
/// <c>IMPLEMENT_SERVERCLASS_ST_NOBASE(CBaseTempEntity, DT_BaseTempEntity)</c>
/// with nothing between it and <c>END_SEND_TABLE()</c>
/// (<c>basetempentity.cpp:21</c>). It carries no props and no blob, and that is
/// the whole declaration.
/// </para>
///
/// <para>
/// <c>RegistersClass = false</c>, AND AS OF PHASE 7 STEP 7b THAT IS THE END
/// STATE RATHER THAN A TRANSITIONAL ONE. The paragraph that used to be here
/// said stock's <c>CBaseTempEntity</c> ServerClass was still registered and
/// that this line would become <c>true</c> when
/// <c>basetempentity.cpp</c> was evicted. Both halves have since stopped being
/// true and the conclusion inverted:
/// </para>
///
/// <para>
/// <c>game/shared/basetempentity.cpp</c> is not in <c>server_csharp.vpc</c> and
/// has not been for several phases, so NOTHING registers a
/// <c>CBaseTempEntity</c> ServerClass in this build — and the server has been
/// running that way. 7b took <c>c_basetempentity.cpp</c> out of
/// <c>client_csharp.vpc</c>, which removes the matching native ClientClass, so
/// neither side registers a class of this name any more. That is SYMMETRIC,
/// which is the property that matters (plan 2E: a ServerClass with no receiver
/// makes the client begin instantiating and stop with nothing in any log).
/// Flipping this to <c>true</c> would register the class again on both sides —
/// a change with a live-join failure mode and no reader, for a table with no
/// props.
/// </para>
///
/// <para>
/// THE TABLE IS STILL BUILT, and now it is built HERE rather than found. While
/// <c>c_basetempentity.cpp</c> was compiled, the client shell's
/// <c>FindStockTableNamed</c> saw stock's <c>DT_BaseTempEntity</c> and refused
/// to build the managed one, and every managed derived table nested stock's
/// (proved on a live join by lane CD). With that TU evicted the refusal does not
/// fire, the managed table is the one in the process, and the derived tables
/// nest it. The offsets cannot move on that change because this table has no
/// props: it is empty in stock and empty here, and the port does not get to
/// tidy that up (plan 2E).
/// </para>
///
/// <para>
/// IT IS A CLASS NOW, AND TWENTY-ONE DECLARATIONS DERIVE FROM IT
/// (<c>docs/te-declaration-conversion.md</c>). That is what carries the base
/// table: <c>TeClassBuilder</c> takes <c>BaseTable</c> from the C# base's own
/// <c>[TempEntity]</c>, so <c>TeBspDecal : TeBaseTempEntity</c> says
/// <c>DT_BaseTempEntity</c> once instead of twice, and a caller can set an
/// inherited field in the same object initialiser as its own. An empty table
/// contributes no fields and no bytes, so every derived blob is laid out
/// exactly as it was before — this is the inheritance, not a layout change.
/// </para>
///
/// <para>
/// AND THE WIRE NAMES OF EVERYTHING BELOW IT ARE THE MEMBER NAMES. Dropping
/// <c>WireName</c> is safe because no native <c>RecvTable</c> pairs with a
/// managed <c>SendTable</c> any more — both halves build their props from this
/// one declaration — and stock's own spelling stays in each field's
/// <c>&lt;summary&gt;</c> and in the <c>IMPLEMENT_SERVERCLASS_ST</c> block
/// quoted at the top of each file, which is where the mapping is readable.
/// </para>
/// </summary>
[TempEntity("CBaseTempEntity", "DT_BaseTempEntity", RegistersClass = false)]
public class TeBaseTempEntity : TempEntState
{
    // No fields. DT_BaseTempEntity is empty in stock and empty here; the port
    // does not get to tidy that up, because the two sides pair on exactly the
    // prop list stock has (plan 2E).
}
