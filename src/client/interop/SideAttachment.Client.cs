namespace SourceSharp.Abi;

/// <summary>
/// The client's half of <see cref="SideAttachment"/>: the areas the client
/// registers, its shared bases and its own (<see cref="ClientAreaRegistration"/>).
/// No <c>#if</c>: the side is the assembly.
/// </summary>
internal sealed partial class SideAttachment
{
    /// <summary>
    /// Attaches the client's shared bases and its own areas, acquired all or
    /// none from <paramref name="root"/> and registered by type. Called from
    /// LoadSideV2.
    /// </summary>
    internal bool AttachShims(ShimRoot root, out string why)
    {
        var areas = new ServiceContainer();
        if (!ClientAreaRegistration.TryAcquire(root, (type, area) => areas.AddInstance(type, area), out why))
            return false;

        Attached(root, areas);
        return true;
    }

    /// <summary>Every client area over no table: what the side holds before its attach.</summary>
    private static ServiceContainer DetachedAreas()
    {
        var areas = new ServiceContainer();
        ClientAreaRegistration.AddDetached((type, area) => areas.AddInstance(type, area));
        return areas;
    }

    /// <summary>
    /// The module engine the side's module is composed over: the client's own
    /// class, so the side's load never chooses one by asking which side it is.
    /// </summary>
    internal static ModuleEngine ModuleEngineOver(EngineSide engine) => new ClientModuleEngine(engine);
}
