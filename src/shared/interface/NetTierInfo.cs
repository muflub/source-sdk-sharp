namespace SourceSharp.Abi;

/// <summary>
/// One tier, as native registered it.
///
/// <para>
/// A reading and not a projection. The prop count in particular is what native
/// BUILT: Part 3's table says six real props in front of the payload, which is
/// the parallel hierarchy's number — the runtime-networked entity puts three
/// there, because it networks no origin, no angles and no model index and never
/// has. A formula right about one shell would have been quietly wrong about the
/// other forever, so there is no formula.
/// </para>
/// </summary>
public readonly record struct NetTierInfo(
    int Index, string Table, int PayloadBytes, int Words, int Props, bool Live);
