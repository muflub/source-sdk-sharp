namespace SourceSharp.Abi;

/// <summary>
/// The client's half of <see cref="EngineSide"/> (plan_shim_areas_split phase 7).
///
/// <para>
/// Each side compiles its own <see cref="EngineSide"/> since phase 3, so what
/// differs by side is said once, here and in the server's file, rather than
/// chosen at run time: a slot both sides serve is called on
/// <see cref="OwnShims"/>, which is the client's container on the client and
/// the server's on the server. The two containers carry those slots under the
/// same names, so the shared source compiles against either. No <c>#if</c>:
/// the side is the assembly.
/// </para>
/// </summary>
internal sealed unsafe partial class EngineSide
{
    /// <summary>The side's own attached areas, the client's; throws before the attach.</summary>
    internal ClientShimAreas OwnShims => ClientShims;

    /// <summary>Whether the side's own areas are attached.</summary>
    internal bool OwnShimsAttached => ClientAttached;
}
