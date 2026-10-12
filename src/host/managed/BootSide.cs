namespace SourceSharp.Bootstrap;

/// <summary>
/// What the Bootstrap keeps for one side between native's calls (plan_side_entries
/// SE2), and the fixed names that side's entry is found by. Everything the
/// side's own code needs lives behind <see cref="Session"/>, in the side's
/// load context; the Bootstrap holds only raw handles and never types them.
/// </summary>
internal sealed class BootSide(string name, bool server, string entryAssembly, string entryType)
{
    /// <summary>"Server" or "Client", for messages.</summary>
    public string Name { get; } = name;

    public bool Server { get; } = server;

    /// <summary>The side's load context's name.</summary>
    public string ContextName => Name + "ALC";

    /// <summary>The side root's name.</summary>
    public string RootName => Name + "Root";

    /// <summary>
    /// The side's root (plan_side_isolation §2.3): made by the side's first
    /// load and kept for the process, so its Abi, and the registry and session
    /// handles below that are typed by it, outlive every load context.
    /// </summary>
    public SideRoot? Root { get; set; }

    /// <summary>The side's Interop assembly, which declares its entry.</summary>
    public string EntryAssembly { get; } = entryAssembly;

    /// <summary>The entry's fixed full type name.</summary>
    public string EntryType { get; } = entryType;

    /// <summary>The Bootstrap's own console for this side, acquired at its load.</summary>
    public BootConsole Console { get; set; } = BootConsole.Stderr();

    /// <summary>
    /// The side's ManagedVtables registry as an opaque GCHandle: written by the
    /// side's first load and handed back on every load after it, so it
    /// outlives every load context (plan_managed_vtable_reload.md L3).
    /// </summary>
    public nint Vtables { get; set; }

    /// <summary>The live session's GCHandle, written by the side's Load and freed by its Unload; 0 when none.</summary>
    public nint Session { get; set; }

    /// <summary>The live load's context, unloaded and checked after the side's Unload.</summary>
    public GameLoadContext? Context { get; set; }

    /// <summary>The live load's entry, resolved fresh for it.</summary>
    public ISideEntry? Entry { get; set; }

    public static BootSide ForServer() => new("Server", server: true,
        "SourceSharp.Server.Interop", "SourceSharp.Server.Interop.ServerSideEntry");

    public static BootSide ForClient() => new("Client", server: false,
        "SourceSharp.Client.Interop", "SourceSharp.Client.Interop.ClientSideEntry");
}
