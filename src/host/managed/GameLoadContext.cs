using System.Reflection;
using System.Runtime.Loader;

namespace SourceSharp.Bootstrap;

/// <summary>
/// Collectible load context for one side's game assemblies.
///
/// Two rules make this actually unloadable (plan.md Part 5):
///  - SourceSharp.Abi resolves to the side's own <see cref="SideRoot"/>, a
///    non-collectible context kept for the process (plan_side_isolation §2.3),
///    so this context holds no Abi of its own and the side's Abi keeps its
///    identity across reloads.
///  - Assemblies load from a byte[], never from a path, so the .dll on disk is
///    not locked and can be overwritten while the game runs.
/// </summary>
internal sealed class GameLoadContext : AssemblyLoadContext
{
    private readonly string _managedDir;

    /// <summary>The side's root, whose Abi this context hands every assembly it loads.</summary>
    private readonly SideRoot _root;

    public GameLoadContext(string name, string managedDir, SideRoot root)
        : base(name, isCollectible: true)
    {
        _managedDir = managedDir;
        _root = root;
    }

    /// <summary>
    /// The bytes this context actually loaded, hashed as they went past.
    ///
    /// <para>
    /// STAMPED HERE AND NOWHERE ELSE, because this is the only place that sees
    /// what was loaded rather than what is on disk. Re-reading the directory
    /// afterwards would produce a stamp of the files as they are NOW — and the
    /// case this whole feature exists for is exactly the one where those two
    /// differ: somebody rebuilt, did not reload, and needs to be told that the
    /// game is running the older code.
    /// </para>
    ///
    /// <para>
    /// Free: the file has been read into a byte[] already, one line above the
    /// call. Nothing is read twice.
    /// </para>
    /// </summary>
    public IReadOnlyCollection<LoadedFile> Loaded => _loaded.Values;

    private readonly SortedDictionary<string, LoadedFile> _loaded = new(StringComparer.Ordinal);

    /// <summary>The row a file's bytes stamp; a context re-reading a name replaces its row.</summary>
    private static LoadedFile Row(string path, ReadOnlySpan<byte> bytes)
        => new(Path.GetFileName(path), LoadFacts.HashOf(bytes), File.GetLastWriteTimeUtc(path));

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (string.IsNullOrEmpty(name))
            return null;

        // The Abi MUST be the side root's instance, otherwise this context gets
        // a copy of its own: a reload would then see a second ManagedVtables
        // type and the registry handle the Bootstrap kept would no longer cast.
        //
        // Returning the Assembly explicitly rather than null: null would defer
        // to default-context probing, which does not reliably find it under
        // component hosting (the harness caught this), and would hand both
        // sides one copy.
        if (name == SideRoot.AbiName)
            return _root.Abi;

        return LoadFromManagedDir(name);
    }

    /// <summary>Loads from a copy in memory so the file stays unlocked.</summary>
    public Assembly? LoadFromManagedDir(string simpleName)
    {
        var path = Path.Combine(_managedDir, simpleName + ".dll");
        if (!File.Exists(path))
            return null; // fall through to the default context

        Assembly assembly = LoadUnlocked(this, path, out LoadedFile? read);
        if (read is { } r)
            _loaded[r.Name] = r;
        return assembly;
    }

    /// <summary>
    /// <paramref name="path"/> loaded into <paramref name="into"/> from a copy
    /// in memory, with <paramref name="read"/> the row its bytes stamp (null
    /// when a debug run could not read them). The one way the Bootstrap loads
    /// a managed assembly: the game contexts and the side roots alike.
    /// </summary>
    internal static Assembly LoadUnlocked(AssemblyLoadContext into, string path, out LoadedFile? read)
    {
        /*
         * Under a debugger, from the file instead.
         *
         * The copy in memory exists so the .dll can be overwritten while the
         * game runs, and nobody rebuilds an assembly in the middle of a
         * debugging session. What the file buys is everything a debugger expects
         * of a module: a path, symbols found beside it, and a non-empty
         * Assembly.Location — which the memory load leaves blank, and which
         * anything trying to find its own files has to be told instead.
         */
        if (Environment.GetEnvironmentVariable("SOURCESHARP_DEBUG") is { Length: > 0 } debug
            && debug != "0")
        {
            read = null;
            try
            {
                read = Row(path, File.ReadAllBytes(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A stamp missing a part is worth having; a load that failed
                // because a debugging aid could not read a file is not.
            }
            return into.LoadFromAssemblyPath(path);
        }

        var bytes = File.ReadAllBytes(path);
        read = Row(path, bytes);

        var pdbPath = Path.ChangeExtension(path, ".pdb");
        if (File.Exists(pdbPath))
        {
            using var asmStream = new MemoryStream(bytes);
            using var pdbStream = new MemoryStream(File.ReadAllBytes(pdbPath));
            return into.LoadFromStream(asmStream, pdbStream);
        }

        using var stream = new MemoryStream(bytes);
        return into.LoadFromStream(stream);
    }
}
