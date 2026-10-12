using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEArmorRicochet</c> — a round off armour. THE ONLY CHAINED BASE IN THE
/// INVENTORY: the only table whose base is itself a concrete effect.
///
/// <para>
/// From <c>te_armorricochet.cpp:114</c>, in full:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST( CTEArmorRicochet, DT_TEArmorRicochet)
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// NO PROPS OF ITS OWN AND THAT IS THE WHOLE TABLE. Everything on the wire is
/// <see cref="TeMetalSparks"/>' two COORD vectors, reached through the nested
/// <c>DT_TEMetalSparks</c>. A class with no fields of its own is exactly right
/// and <see cref="TeBaseTempEntity"/> is the precedent; what makes this one
/// different is that the table it nests is not a base — it is a sibling effect
/// with a ServerClass and callers of its own.
/// </para>
///
/// <para>
/// SO ITS DECLARATION DERIVES FROM A CONCRETE EFFECT'S, which is the one place
/// in the port where <c>TempEntState</c>'s inheritance does something more than
/// spell a base out: <c>new TeArmorRicochet { Position = …, Direction = … }</c>
/// sets two fields the class itself does not declare, and
/// <c>TempEntDeclarationWriter.WriteChain</c> writes them root-first at the
/// offsets the base's own layout gave them.
/// </para>
///
/// <para>
/// SO WHY IS IT A SEPARATE CLASS AT ALL, when it is prop for prop identical to
/// the table it nests? Because the CLIENT does something different with it —
/// <c>c_te_armorricochet.cpp</c> plays a ricochet sound and a different
/// particle set from <c>c_te_metalsparks.cpp</c> — and because the set of
/// registered names must be exactly stock's (plan 2E). Same reasoning as lane
/// A's <c>DT_TEBeamLaser</c> against <c>DT_TEBeamEnts</c>. This is not a place
/// to tidy up.
/// </para>
///
/// <para>
/// Its live caller is <c>CEffectsServer::Ricochet</c>
/// (<c>EffectsServer.cpp:182</c>), which is how a managed weapon's armour hit
/// reaches the wire today.
/// </para>
/// </summary>
[TempEntity]
public class TeArmorRicochet : TeMetalSparks
{
    // No fields of its own. DT_TEArmorRicochet declares no props in stock and
    // declares none here; a caller reaches Position and Direction because they
    // are INHERITED -- the base's fields are in the base's TABLE and in THIS
    // class's blob (TempEntType.Fields), and now in this class's C# type too.
    //
    // BaseTable is gone from the attribute because ": TeMetalSparks" already
    // says it, and this is the inventory's ONLY chained pair -- the only place
    // where inheriting the base rather than naming it has anything to get
    // wrong. It got something wrong: TempEntRegistry ordered base-before-derived
    // and computed baseBytes from attr.BaseTable ALONE, so this class would have
    // been built as NOBASE with a nought-byte blob and DT_TEMetalSparks' two
    // vectors reading off the end of it. The registry asks
    // TeClassBuilder.BaseTableOf the same question now.
}
