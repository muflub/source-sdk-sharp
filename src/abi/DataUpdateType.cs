namespace SourceSharp.Abi;

/// <summary>
/// Why a networked entity's state changed. Values match Source's
/// <c>DataUpdateType_t</c> (iclientnetworkable.h) — the shell passes it straight
/// through as an int32, so these must not be renumbered.
///
/// <para>
/// It lived in NetEntity.cs, which went with the deleted runtime-networked
/// shell. The enum did not: it is an engine constant, and every client entity
/// on the parallel hierarchy takes one.
/// </para>
/// </summary>
public enum DataUpdateType
{
    /// <summary>Created and entered the PVS. The first update an entity gets.</summary>
    Created = 0,

    /// <summary>An ordinary state change on an entity that already existed.</summary>
    DataTableChanged = 1,
}
