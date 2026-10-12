using System.Reflection;
using System.Runtime.Loader;

namespace SourceSharp.Abi;

/// <summary>
/// Every mod assembly loaded alongside a given one, and the types in them.
///
/// Discovery used to read a single assembly: a module scanned itself for its
/// commands and entity classes, and was handed the shared assembly for the
/// protocol declarations. That made WHERE a class lived part of whether it
/// worked at all — a ConCommand moved into Shared, or a second game assembly
/// dropped in beside the module, was simply never seen. Every registry now
/// scans all of them, so a declaration is found wherever it is written.
///
/// "All of them" is:
///  - the assemblies in the anchor's load context, which is the side's ALC in
///    the game and the default context in a test run,
///  - the default context's as well, which fills a test run's names (in the
///    game SourceSharp.Abi is the side root's, reached through the anchor's
///    references: see GameLoadContext),
///  - and the anchor's reference closure, force-loaded through the anchor's own
///    context — an assembly nothing has touched yet is not in Assemblies, and
///    "the class registered only once something happened to mention it" is not
///    a behaviour anyone can debug.
///
/// The set is then narrowed to assemblies referencing SourceSharp.Abi. The
/// attributes live here, so one that does not reference it has nothing to
/// declare, and that single rule keeps the framework out without a list of name
/// prefixes to maintain. It is a sharper rule than it looks: declaring anything
/// means using a type from the ABI, and the compiler drops a reference nothing
/// uses — so an assembly without that reference provably has nothing to declare.
/// The closure walk stops at those assemblies for the same reason, and anything
/// reached only through one is still found the moment it loads.
/// </summary>
public static class LoadedAssemblies
{
    /// <summary>The contract assembly, SourceSharp.Abi (this file left it in Phase 1; the anchor did not). Always scanned.</summary>
    private static readonly Assembly Abi = typeof(ILog).Assembly;

    private static readonly string AbiName = Abi.GetName().Name!;

    /// <summary>
    /// Every loaded mod assembly, ordered by name.
    ///
    /// The order is not tidiness. Several registries put their discovery order
    /// on the wire — user message indices, the entity layout fingerprint, the
    /// standalone net state list — and reflection promises no order at all, so
    /// the sequence has to come from something fixed. Within an assembly that is
    /// the metadata token (source declaration order, baked into the image); ACROSS
    /// assemblies a token means nothing, so the assembly name orders them first.
    /// Both sides derive the same sequence from the same names and the same
    /// bytes, which is what keeps the protocol agreed.
    ///
    /// <para>
    /// A SIDE SCANS ITS OWN SIDE. Since SourceSharp.Shared's source compiles
    /// into each side (plan_side_isolation P7) every shared declaration exists
    /// once per side assembly, so an anchor that is a side's assembly
    /// (<c>.Server</c>, <c>.Client</c>) never keeps an assembly of the other
    /// side. In the game the other side is never in the anchor's context, so
    /// this changes nothing there; in a process that loads both into one
    /// context (the cross-side tests) it is what keeps the server's scan from
    /// finding the client's copy of every declaration, and the other way round.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Assembly> All(Assembly anchor, ILog log)
    {
        string? otherSide = OtherSideSuffix(anchor);
        var alc   = AssemblyLoadContext.GetLoadContext(anchor) ?? AssemblyLoadContext.Default;
        var found = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var tried = new HashSet<string>(StringComparer.Ordinal);

        // Three sweeps rather than one, and the order they run in is the answer
        // to "which copy wins": a name can be loaded in two contexts at once —
        // the side's own Shared and a Shared in the default context — and the
        // one a registry gets handed has to be the side's, or it is given types
        // from an assembly its own side never loaded. So the anchor and
        // everything it reaches are settled first, its context next, and the
        // default context only fills what is still missing.
        Sweep(new[] { anchor });
        Sweep(alc.Assemblies);

        if (alc != AssemblyLoadContext.Default)
            Sweep(AssemblyLoadContext.Default.Assemblies);

        var ordered = new List<Assembly>(found.Values);
        ordered.Sort((a, b) => string.CompareOrdinal(a.GetName().Name, b.GetName().Name));
        return ordered;

        // Keeps each seed and walks out through its references, force-loading
        // any that nothing has touched yet.
        void Sweep(IEnumerable<Assembly> seeds)
        {
            var pending = new Queue<Assembly>(seeds);

            while (pending.Count > 0)
            {
                var assembly = pending.Dequeue();

                // False means it is not ours, or a name already held — and in
                // both cases its references are either not worth walking or
                // have been walked already.
                if (!Keep(assembly))
                    continue;

                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    var name = reference.Name;
                    if (name is null || !tried.Add(name))
                        continue;

                    Assembly? loaded;

                    try
                    {
                        loaded = alc.LoadFromAssemblyName(reference);
                    }
                    catch (Exception e)
                    {
                        // A reference that will not resolve is the compiler's
                        // business, not discovery's: whatever needs it will fail
                        // on its own terms. Scanning carries on without it.
                        log.Warning($"[SourceSharp] could not load referenced assembly " +
                                       $"{name} while scanning for declarations: {e.Message}\n");
                        continue;
                    }

                    // Kept here rather than when it comes back off the queue:
                    // it was reached through the anchor, and waiting would let a
                    // later sweep's copy of the same name take the slot.
                    if (Keep(loaded))
                        pending.Enqueue(loaded);
                }
            }
        }

        bool Keep(Assembly assembly)
        {
            // A dynamic assembly has no metadata file to scan and GetTypes on
            // one is a different contract entirely.
            if (assembly.IsDynamic || !DeclaresAgainstAbi(assembly))
                return false;

            var name = assembly.GetName().Name;
            if (name is null
             || (otherSide is not null && name.EndsWith(otherSide, StringComparison.Ordinal))
             || !found.TryAdd(name, assembly))
                return false;

            tried.Add(name);
            return true;
        }
    }

    /// <summary>
    /// Every type in <see cref="All"/>, in assembly-name order and, within one
    /// assembly, declaration order.
    /// </summary>
    public static IReadOnlyList<Type> Types(Assembly anchor, ILog log)
    {
        var types = new List<Type>();

        foreach (var assembly in All(anchor, log))
        {
            Type?[] declared;

            try
            {
                declared = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                // Whatever DID load is still worth scanning. A half-loadable
                // assembly usually means one type referencing something absent,
                // and dropping the other few hundred with it helps nobody.
                declared = e.Types;
                log.Warning($"[SourceSharp] {assembly.GetName().Name} has type(s) that " +
                               $"could not be loaded; scanning the rest\n");
            }

            int start = types.Count;

            foreach (var type in declared)
            {
                if (type is not null)
                    types.Add(type);
            }

            types.Sort(start, types.Count - start,
                       Comparer<Type>.Create((a, b) => a.MetadataToken.CompareTo(b.MetadataToken)));
        }

        return types;
    }

    /// <summary>
    /// The suffix of the side <paramref name="anchor"/> is NOT on (".Client" for
    /// a ".Server" assembly and the other way round), or null for an anchor that
    /// is no side's.
    /// </summary>
    internal static string? OtherSideSuffix(Assembly anchor)
    {
        string name = anchor.GetName().Name ?? string.Empty;

        return name.EndsWith(".Server", StringComparison.Ordinal) ? ".Client"
             : name.EndsWith(".Client", StringComparison.Ordinal) ? ".Server"
             : null;
    }

    /// <summary>A module's name: an assembly's name without its .Shared/.Server/.Client suffix (plan_side_isolation §2.2). User messages are keyed under it.</summary>
    public static string ModuleOf(Assembly assembly)
    {
        string name = assembly.GetName().Name ?? throw new InvalidOperationException("an assembly with no name");

        foreach (var suffix in (ReadOnlySpan<string>)[".Shared", ".Server", ".Client"])
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
                return name[..^suffix.Length];
        }

        return name;
    }

    /// <summary>
    /// Whether an assembly could declare anything discovery looks for, which is
    /// exactly whether it references the ABI the attributes live in.
    /// </summary>
    private static bool DeclaresAgainstAbi(Assembly assembly)
    {
        if (ReferenceEquals(assembly, Abi))
            return true;

        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            if (string.Equals(reference.Name, AbiName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
