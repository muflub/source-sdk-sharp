using SourceSharp.Abi;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// The client half of one temp entity: read the blob, play the effect.
///
/// <para>
/// THIS IS WHAT PORTING A <c>c_te_*.cpp</c> LEAVES BEHIND. Stock spends a
/// <c>C_BaseTempEntity</c> subclass per effect — a class, a RecvTable, a
/// singleton and a <c>PostDataUpdate</c>. All of that except the last line is
/// machinery, and the machinery is now generic: one native shell, one table
/// built from the shared declaration, one blob. What is left is the decode and
/// the decision, which is this.
/// </para>
///
/// <para>
/// DISCOVERED BY ATTRIBUTE AND BY NOTHING ELSE (plan Part 10). There is no
/// shared list file, because a shared list is forty-three lanes editing one
/// line and a guaranteed conflict — the same reason <c>[ScriptedEntity]</c>,
/// <c>[UserMessage]</c> and <c>[TempEntity]</c> are all found this way. A
/// C-Phase 3 lane adds one file per class and touches nothing anybody else has.
/// </para>
/// </summary>
public interface IClientTempEnt
{
    /// <summary>
    /// One arrived. The reader borrows the native shell's storage and is valid
    /// only for this call.
    ///
    /// <para>
    /// EVERYTHING THE EFFECT NEEDS IS IN THE BLOB, including the base chain's
    /// fields: <c>m_vecOrigin</c> belongs to <c>DT_TEParticleSystem</c> and is
    /// read here by name, because a base nested at datatable offset zero puts
    /// its props at their own offsets in the SAME object.
    /// </para>
    /// </summary>
    void Play(in TempEntReader te);

    /// <summary>
    /// One was raised LOCALLY, by a native forward rather than by the wire —
    /// so it arrives as the declaration itself and there is no blob.
    ///
    /// <para>
    /// A RECEIVED temp entity comes off the network as bytes and has to be
    /// decoded, which is the overload above. A forwarded one is built in C#
    /// from the arguments <c>TE_DynamicLight</c> was called with, so writing it
    /// into a blob and reading it straight back would be two reflection passes
    /// to arrive at the object we already had.
    /// </para>
    /// </summary>
    void Play(TempEntState state);
}

/// <summary>
/// Names the temp entity a <see cref="IClientTempEnt"/> plays, by the class name
/// the two sides pair on.
///
/// <para>
/// THE WIRE NAME, not a C# one: <c>"CTESparks"</c> is what the server registers
/// its ServerClass under, what this side registers its ClientClass under, and
/// what <see cref="TempEntityAttribute.ClassName"/> declares. Three places
/// agreeing on one string is what makes the effect happen; a fourth spelling
/// would be a player nothing ever calls.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ClientTempEntAttribute : Attribute
{
    public ClientTempEntAttribute(string className)
    {
        ClassName = className;
    }

    public string ClassName { get; }
}
