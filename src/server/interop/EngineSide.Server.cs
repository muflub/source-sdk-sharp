namespace SourceSharp.Abi;

/// <summary>
/// The server's half of <see cref="EngineSide"/> (plan_shim_areas_split phase 7).
///
/// <para>
/// Each side compiles its own <see cref="EngineSide"/> since phase 3, so what
/// differs by side is said once, here and in the client's file, rather than
/// chosen at run time: a slot both sides serve is called on
/// <see cref="OwnShims"/>, which is the server's container on the server and
/// the client's on the client. The two containers carry those slots under the
/// same names, so the shared source compiles against either. No <c>#if</c>:
/// the side is the assembly.
/// </para>
/// </summary>
internal sealed unsafe partial class EngineSide
{
    /// <summary>The side's own attached areas, the server's; throws before the attach.</summary>
    internal ShimAreas OwnShims => ServerShims;

    /// <summary>Whether the side's own areas are attached.</summary>
    internal bool OwnShimsAttached => ServerAttached;
}
