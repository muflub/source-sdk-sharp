using System.Reflection;
using System.Runtime.Loader;

namespace SourceSharp.Bootstrap;

/// <summary>
/// One side's root (plan_side_isolation §2.3): a NON-collectible load context,
/// made at the side's first load and kept for the process, holding that side's
/// own copy of SourceSharp.Abi. Every game context the side makes resolves the
/// Abi to this copy (<see cref="GameLoadContext"/>), so:
/// <list type="bullet">
/// <item>the two sides share no managed type: the server's Abi and the client's
/// are two assemblies, and a type of one is not assignable to the other's;</item>
/// <item>within one side the Abi keeps its identity across every reload, so the
/// side's ManagedVtables registry and the session handles the Bootstrap keeps
/// (opaque GCHandles) stay valid while game contexts come and go.</item>
/// </list>
///
/// <para>
/// The Abi references nothing but the framework, so the Abi is the one
/// assembly a root loads; everything else falls through to the default
/// context's probing (null from <see cref="Load"/>). The Bootstrap still names
/// no Abi type: the root finds it by name and path.
/// </para>
///
/// <para>
/// THE ABI IS THE ONE ASSEMBLY A HOT RELOAD DOES NOT RELOAD, per side: a root
/// keeps its copy for the process. Editing anything in the Abi and reloading
/// changes nothing, and the reload reports every success it always reports
/// (found the hard way, running a live negative control that could not go
/// red). So the root remembers when the file it loaded was written, and
/// <see cref="AbiMoved"/> says so on the next load once it has been rebuilt:
/// what the reader needs is "restart the game", at the moment they are
/// wondering why nothing happened.
/// </para>
/// </summary>
internal sealed class SideRoot : AssemblyLoadContext
{
    /// <summary>The Abi's simple name: the one assembly a root loads, and the one a game context does not.</summary>
    internal const string AbiName = "SourceSharp.Abi";

    private readonly string _abiPath;

    /// <summary>The Abi file's write time as of the last load that looked; see <see cref="AbiMoved"/>.</summary>
    private DateTime _abiSeenAt;

    /// <summary>
    /// The side's root over <paramref name="managedDir"/>, with its Abi loaded:
    /// unlocked, from a copy in memory, as the game contexts load theirs.
    /// </summary>
    public SideRoot(string name, string managedDir)
        : base(name, isCollectible: false)
    {
        _abiPath = Path.Combine(managedDir, AbiName + ".dll");
        Abi = GameLoadContext.LoadUnlocked(this, _abiPath, out LoadedFile? read);
        AbiFile = read;
        _abiSeenAt = read?.WrittenUtc ?? default;
    }

    /// <summary>This side's copy of SourceSharp.Abi, for the process.</summary>
    public Assembly Abi { get; }

    /// <summary>
    /// The Abi as it was loaded: its hash and write time, taken ONCE from the
    /// bytes this root loaded (the Abi's row of every managed stamp of this
    /// side). Once, because a reload does not reload the Abi: re-hashing the
    /// file would move the stamp to describe an Abi.dll the side is not
    /// running. Null when the file could not be read for the hash.
    /// </summary>
    public LoadedFile? AbiFile { get; }

    protected override Assembly? Load(AssemblyName assemblyName)
        => assemblyName.Name == AbiName ? Abi : null;

    /// <summary>
    /// True once per rebuild of the Abi on disk since this root loaded it (or
    /// since the last time this answered true), so a session where the Abi is
    /// being edited is told once per rebuild, not once per reload. Not knowing
    /// the write time is not worth a message of its own.
    /// </summary>
    public bool AbiMoved()
    {
        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(_abiPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (_abiSeenAt == default)
        {
            _abiSeenAt = written;   // the load could not read it; this is the first look
            return false;
        }

        if (written == _abiSeenAt)
            return false;

        _abiSeenAt = written;
        return true;
    }
}
