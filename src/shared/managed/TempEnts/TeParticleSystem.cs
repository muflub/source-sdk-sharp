using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEParticleSystem</c> — a sort origin, and nothing else. The base of
/// seven concrete effects.
///
/// <para>
/// From <c>te_particlesystem.cpp:17</c>, taking the non-TF branch — <c>TF_DLL</c>
/// appears nowhere in this build (plan 2G), so <c>m_vecOrigin</c> is three
/// <c>SendPropFloat</c>s with <c>SPROP_COORD</c> rather than the
/// <c>SPROP_COORD_MP_INTEGRAL</c> form. Both sides agree, because neither define
/// is set in either target.
/// </para>
///
/// <para>
/// ONE C# FIELD, THREE PROPS. Stock declares it with three
/// <c>SENDINFO_VECTORELEM</c> lines and that is what
/// <c>Elems = 3, ElemStride = 4</c> plus <see cref="TeFlags.VectorElem"/>
/// reproduces, negated offset and all. Declaring three floats here instead would
/// give the writer three things to keep in step for a value that has a type.
/// </para>
///
/// <para>
/// IT CLAIMS THE CLASS ON BOTH SIDES, and it is the first BASE in this port to
/// do so. It was <c>RegistersClass = false</c> for as long as a native
/// <c>CTEParticleSystem</c> existed anywhere — stock's ServerClass, and its
/// last native subclass <c>CTEDust</c> in <c>shell_entity_orphans.cpp</c>. Lane
/// F took Dust and lane CP took the other six and the client half of all seven,
/// so nothing native derives from it or registers it on either side any more,
/// and <c>te_particlesystem.cpp</c> and the <c>C_TEParticleSystem</c> block in
/// <c>c_te_particlesystem.cpp</c> are both gone. Step 7b then took the whole of
/// <c>c_te_particlesystem.cpp</c>: what was left in it was
/// <c>CTEParticleRenderer</c>, a leaf emitter with no table and no entity, and
/// that moved verbatim to the shim and then to C# (lane c5-particles: TeParticles).
/// </para>
///
/// <para>
/// AND IT HAD TO GO BOTH WAYS AT ONCE. A ServerClass with no matching
/// ClientClass makes the client begin instantiating and stop (plan 2E) — this
/// one did exactly that on a live join, as <c>Client missing DT class
/// CTEParticleSystem</c> followed by <c>InitFastCopy - missing client class
/// 47</c> and a stack overflow. So evicting the client's native half without
/// evicting the server's is not a smaller step than doing both; it is a broken
/// one. <see cref="TeBaseTempEntity"/> stays <c>RegistersClass = false</c>,
/// because <c>CBaseTempEntity</c> IS still registered natively on both sides
/// and symmetrically, which is the state this one just left.
/// </para>
///
/// <para>
/// ITS <c>BaseTable</c> IS GONE NOW, and it was the last declaration in this
/// subtree still carrying one. It had to stay while this class's C# base was
/// <see cref="TempEntState"/> — the declaration root, which carries no
/// attribute for the rule "the C# base class's own <c>[TempEntity]</c> says it"
/// to read, so dropping the name resolved to null, meaning NOBASE, and
/// <c>DT_TEParticleSystem</c> silently stopped nesting <c>DT_BaseTempEntity</c>.
/// <see cref="TeBaseTempEntity"/> is a class now and carries that attribute, so
/// the base is said once, by the inheritance.
/// </para>
///
/// <para>
/// THE WIRE CANNOT MOVE ON THIS CHANGE, which is why it is safe to make late:
/// <c>DT_BaseTempEntity</c> declares no props and contributes no bytes, so only
/// the NAME of the nested table has to survive — and it does, resolved from the
/// base class instead of from a string.
/// </para>
/// </summary>
[TempEntity]
public class TeParticleSystem : TeBaseTempEntity
{
    /// <summary>
    /// The particle effect's sort origin. Three COORD floats.
    ///
    /// <para>
    /// ITS KIND STAYS EXPLICIT, where the seven derived classes' Vectors do
    /// not. A <c>Vector</c> member infers <see cref="TePropKind.Vector"/> — one
    /// <c>SendPropVector</c> — and this is three <c>SendPropFloat</c>s, which is
    /// what <c>SENDINFO_VECTORELEM</c> expands to and what stock's table
    /// declares. The type cannot tell those apart, so the declaration says which.
    /// </para>
    /// </summary>
    [TeProp(TePropKind.Float, Bits = -1,
            Flags = TeFlags.Coord | TeFlags.VectorElem, Elems = 3, ElemStride = 4)]
    public Vector Origin;
}
