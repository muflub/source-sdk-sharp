using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEBeamEnts</c> — a beam between two entities, and nothing else.
///
/// <para>
/// From <c>te_beaments.cpp:81</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEBeamEnts, DT_TEBeamEnts)
///     SendPropInt( SENDINFO(m_nStartEntity), 24, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(m_nEndEntity),   24, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// Everything else a beam needs is <c>DT_BaseBeam</c>'s, which this nests: the
/// two props of its own start at blob offset 60, where the base's fifteen end.
/// Nothing here writes that number down — <c>TeClassBuilder</c> is handed the
/// base's blob size by the registry and <c>EntityLayout</c> does the rest.
/// </para>
/// </summary>
[TempEntity]
public class TeBeamEnts : TeBaseBeam
{
    /// <summary>Index and attachment of the entity it starts at, 24 bits unsigned.</summary>
    [TeProp(Bits = 24, Flags = TeFlags.Unsigned)]
    public int StartEntity;

    /// <summary>Index and attachment of the entity it ends at.</summary>
    [TeProp(Bits = 24, Flags = TeFlags.Unsigned)]
    public int EndEntity;
}
