using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace SourceSharp.Bootstrap;

/// <summary>
/// WHAT IS STILL HOLDING A SIDE'S LOAD CONTEXT — by name, not by advice.
///
/// <para>
/// <c>Entry.UnloadModule</c> has printed
/// <c>"ServerALC did NOT unload — something still references it. Check for
/// native-cached managed pointers"</c> on every hot reload this port has ever
/// done. That sentence is a hypothesis, and it sent lane <c>console-warnings</c>
/// after the DebugApi accept thread — which was a real pin, was fixed, and was
/// NOT the pin: the warning stayed at 1 per reload afterwards, which that lane
/// reported honestly rather than as a cure.
/// </para>
///
/// <para>
/// A search for the next candidate has the same shape and the same ending, so
/// this asks the heap instead. Every assembly in the DEFAULT context outlives
/// every side, so a static field there is where a surviving reference has to
/// live; this walks those fields' object graphs and reports each path that ends
/// at something belonging to the context being unloaded.
/// </para>
///
/// <para>
/// WHAT COUNTS AS BELONGING. Four kinds, and the last two are the ones a "does
/// this object's type come from the ALC" test misses entirely:
/// an OBJECT whose type was declared there; a <see cref="Type"/> or other
/// <see cref="MemberInfo"/> declared there (a cache KEY is enough — a Type holds
/// its assembly and the assembly holds the context); an <see cref="Assembly"/>
/// loaded there; and a <see cref="Delegate"/> whose target or method belongs
/// there, which is how an event handler pins a side that has already gone.
/// </para>
///
/// <para>
/// NOT A GC ROOT ANALYSIS, and it says so rather than implying one. It walks
/// static fields, arrays, and the entries of any <see cref="IDictionary"/> or
/// <see cref="IEnumerable"/> it meets, breadth first, to a bounded depth and a
/// bounded node count. It cannot see a reference held only by a live thread's
/// stack, by a native GCHandle, or by a finalizer queue — so an EMPTY report is
/// not proof of no pin, and this says that at its call site rather than letting
/// a zero be read as a clean bill of health. What it IS reliable about is the
/// positive: a path it prints is a real chain of references that exists.
/// </para>
/// </summary>
internal static class ManagedPins
{
    /// <summary>How deep a field's object graph is walked before giving up.</summary>
    public const int MaxDepth = 6;

    /// <summary>How many distinct objects one scan may visit in total.</summary>
    public const int MaxNodes = 200_000;

    /// <summary>
    /// Every path from a default-context static field to something that belongs
    /// to <paramref name="going"/>, as readable strings, at most
    /// <paramref name="limit"/> of them.
    /// </summary>
    /// <remarks>
    /// EVERY CONTEXT BUT <paramref name="going"/>, not the default one only.
    /// Under component hosting the bootstrap and the ABI are NOT in
    /// <see cref="AssemblyLoadContext.Default"/> — hostfxr loads a component
    /// into an isolated context of its own — so a scan written against Default
    /// walks nothing and reports a clean heap, which is what the first run of
    /// this finder did. A static in the OTHER side's collectible context is a
    /// real pin too: on a listen server that side outlives this one.
    /// </remarks>
    public static IReadOnlyList<string> Find(AssemblyLoadContext going, int limit = 20)
    {
        var roots = new List<(object Value, string Path)>();

        foreach (var assembly in AssemblyLoadContext.All
                     .Where(context => context != going)
                     .SelectMany(context => context.Assemblies))
        {
            // Only ours. The framework's own statics are large, are not where a
            // game type can have got to, and walking them costs the node budget
            // that the interesting fields need.
            var name = assembly.GetName().Name ?? "";
            if (!name.StartsWith("SourceSharp", StringComparison.Ordinal))
                continue;

            CollectStatics(assembly, roots);
        }

        return Walk(roots, going, limit);
    }

    /// <summary>
    /// The static fields of one assembly, as walk roots. Reading a static field
    /// RUNS ITS TYPE INITIALISER, so every read is guarded: a cctor that throws
    /// during a diagnostic must not turn a leak report into a crash.
    /// </summary>
    private static void CollectStatics(Assembly assembly, List<(object, string)> roots)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t is not null).ToArray()!; }
        catch (Exception) { return; }

        foreach (var type in types)
        {
            // A generic definition has no statics of its own to read, and asking
            // for them throws.
            if (type.ContainsGenericParameters)
                continue;

            FieldInfo[] fields;
            try
            {
                fields = type.GetFields(BindingFlags.Static | BindingFlags.Public
                                      | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            }
            catch (Exception) { continue; }

            foreach (var field in fields)
            {
                if (field.FieldType.IsPrimitive || field.FieldType == typeof(string))
                    continue;

                object? value;
                try { value = field.GetValue(null); }
                catch (Exception) { continue; }

                if (value is not null)
                    roots.Add((value, $"{type.FullName}.{field.Name}"));
            }
        }
    }

    private static List<string> Walk(List<(object Value, string Path)> roots,
                                     AssemblyLoadContext going, int limit)
    {
        var found = new List<string>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(object Value, string Path, int Depth)>();

        foreach (var (value, path) in roots)
        {
            if (seen.Add(value))
                queue.Enqueue((value, path, 0));
        }

        int visited = 0;
        while (queue.Count > 0 && found.Count < limit && visited < MaxNodes)
        {
            var (value, path, depth) = queue.Dequeue();
            visited++;

            var why = Belongs(value, going);
            if (why is not null)
            {
                found.Add($"{path} -> {why}");
                // Not descended into: everything under a pinned object is
                // pinned too, and reporting the sub-paths as separate findings
                // is how one leak fills a twenty-line report.
                continue;
            }

            if (depth >= MaxDepth)
                continue;

            foreach (var (child, step) in Children(value))
            {
                if (child is null || !seen.Add(child))
                    continue;
                queue.Enqueue((child, path + step, depth + 1));
            }
        }

        return found;
    }

    /// <summary>
    /// Why <paramref name="value"/> belongs to <paramref name="going"/>, or
    /// null. The four kinds are the header's four kinds.
    /// </summary>
    internal static string? Belongs(object value, AssemblyLoadContext going)
    {
        switch (value)
        {
            case Assembly assembly when AssemblyLoadContext.GetLoadContext(assembly) == going:
                return $"assembly {assembly.GetName().Name}";

            case Type type when ContextOf(type) == going:
                return $"Type {type.FullName}";

            case MemberInfo member when member.DeclaringType is not null
                                     && ContextOf(member.DeclaringType) == going:
                return $"{member.MemberType} {member.DeclaringType.FullName}.{member.Name}";

            case Delegate d when d.Method.DeclaringType is not null
                              && ContextOf(d.Method.DeclaringType) == going:
                return $"delegate over {d.Method.DeclaringType.FullName}.{d.Method.Name}";
        }

        var owner = ContextOf(value.GetType());
        return owner == going ? $"instance of {value.GetType().FullName}" : null;
    }

    private static AssemblyLoadContext? ContextOf(Type type)
        => AssemblyLoadContext.GetLoadContext(type.Assembly);

    /// <summary>
    /// The references one object holds, each with the text to append to the path.
    /// </summary>
    /// <remarks>
    /// Dictionaries are enumerated as KEY AND VALUE both, and the key half is
    /// the one that matters here: every cache <see cref="ManagedCaches"/> exists
    /// for is keyed by a game <see cref="Type"/>, so a walk that only followed
    /// values would find nothing and report a clean heap.
    /// </remarks>
    private static IEnumerable<(object? Child, string Step)> Children(object value)
    {
        if (value is string || value.GetType().IsPrimitive)
            yield break;

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in Snapshot(dictionary))
            {
                yield return (entry.Key, "[key]");
                yield return (entry.Value, "[value]");
            }
            yield break;
        }

        if (value is Array array)
        {
            // Rank > 1 and non-zero lower bounds are not walked: nothing in this
            // tree has one, and GetValue over them is a different call shape.
            if (array.Rank != 1)
                yield break;
            foreach (var item in array)
                yield return (item, "[]");
            yield break;
        }

        if (value is IEnumerable enumerable && value is not System.Collections.Specialized.NameValueCollection)
        {
            foreach (var item in Snapshot(enumerable))
                yield return (item, "[item]");
            yield break;
        }

        if (value is Delegate del)
        {
            yield return (del.Target, ".Target");
            yield break;
        }

        foreach (var field in value.GetType()
                     .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType.IsPrimitive || field.FieldType == typeof(string))
                continue;

            object? child;
            try { child = field.GetValue(value); }
            catch (Exception) { continue; }

            yield return (child, "." + field.Name);
        }
    }

    /// <summary>
    /// A LIST FIRST, ALWAYS. A concurrent collection mutated by another thread
    /// mid-walk throws out of the middle of a diagnostic, and the diagnostic is
    /// running because something is already wrong.
    /// </summary>
    private static List<object?> Snapshot(IEnumerable source)
    {
        var items = new List<object?>();
        try
        {
            foreach (var item in source)
            {
                items.Add(item);
                if (items.Count >= 4096)
                    break;
            }
        }
        catch (Exception) { /* a collection that will not enumerate holds nothing we can name */ }
        return items;
    }

    private static List<DictionaryEntry> Snapshot(IDictionary source)
    {
        var items = new List<DictionaryEntry>();
        try
        {
            foreach (DictionaryEntry entry in source)
            {
                items.Add(entry);
                if (items.Count >= 4096)
                    break;
            }
        }
        catch (Exception) { /* as above */ }
        return items;
    }
}
