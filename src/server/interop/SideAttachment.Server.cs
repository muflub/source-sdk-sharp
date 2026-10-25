namespace SourceSharp.Abi;

/// <summary>
/// The server's half of <see cref="SideAttachment"/>: the areas the server
/// registers (<see cref="ServerAreaRegistration"/>). No <c>#if</c>: the side
/// is the assembly.
/// </summary>
internal sealed partial class SideAttachment
{
    /// <summary>
    /// Attaches every generated server area, acquired all or none from
    /// <paramref name="root"/> and registered by type. Called from LoadSideV2.
    /// </summary>
    internal bool AttachShims(ShimRoot root, out string why)
    {
        var areas = new ServiceContainer();
        if (!ServerAreaRegistration.TryAcquire(root, (type, area) => areas.AddInstance(type, area), out why))
            return false;

        Attached(root, areas);
        return true;
    }

    /// <summary>Every server area over no table: what the side holds before its attach.</summary>
    private static ServiceContainer DetachedAreas()
    {
        var areas = new ServiceContainer();
        ServerAreaRegistration.AddDetached((type, area) => areas.AddInstance(type, area));
        return areas;
    }
}
