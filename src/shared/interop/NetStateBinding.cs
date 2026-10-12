using System.Reflection;
using System.Runtime.Loader;

namespace SourceSharp.Abi;

/// <summary>
/// Finds the <c>[NetState]</c> fields on an entity class and wires them up.
///
/// <para>
/// Both registries call this at the same moment — as soon as an instance exists
/// and knows its handle — so neither an entity class nor whoever wrote it has to
/// remember. That matters more than it looks: a state object nobody bound is not
/// an error anywhere, it is an entity that replicates zeroes, which is the exact
/// failure this project has already spent a session chasing once.
/// </para>
///
/// <para>
/// The field may be left null. It is created here if so, which is why the
/// client's half can declare one and never think about where it comes from.
/// </para>
/// </summary>
public static class NetStateBinding
{
    /// <summary>
    /// The <c>[NetState]</c> fields declared on a type and its bases: the
    /// uncached walk. A registry asks through its <see cref="NetStateFields"/>,
    /// which remembers the answer per type for its load.
    /// </summary>
    public static FieldInfo[] FieldsOn(Type type)
    {
        var found = new List<FieldInfo>();

        for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public
                                              | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.GetCustomAttribute<NetStateAttribute>() is not null)
                    found.Add(field);
            }
        }

        return found.ToArray();
    }

    /// <summary>
    /// Creates any missing state objects and gives each LOCAL storage: what the
    /// client's registry gives its entities, which receive their bytes borrowed
    /// per update, and what a server entity with no shell gets. See
    /// <see cref="AttachBound"/> for the other arm.
    ///
    /// <para>
    /// TWO ARMS AND NOT ONE WITH A NULLABLE SIDE. It was
    /// <c>Attach(instance, handle, bindTo: null)</c>, and SideAuditTests' walk
    /// cannot see a constant null, so the client's registry read as reaching
    /// the server-only blob bind (lane side-audit, 2026-10-02). This arm never
    /// names the server blob bind (NetStateServerBinding).
    /// </para>
    /// </summary>
    public static NetState[] AttachLocal(object instance, EntityHandle handle, NetStateFields known)
    {
        var fields = known.FieldsOn(instance.GetType());
        if (fields.Length == 0)
            return Array.Empty<NetState>();

        var states = new NetState[fields.Length];

        for (int i = 0; i < fields.Length; i++)
        {
            if (StateIn(instance, fields[i], known.Log) is not { } state)
                continue;

            state.BindLocal();
            states[i] = state;
        }

        return states;
    }


    /// <summary>
    /// The state in <paramref name="field"/>, created and stored if the field
    /// was left null; null (and said) for a field that is not a state.
    /// </summary>
    internal static NetState? StateIn(object instance, FieldInfo field, ILog log)
    {
        if (field.GetValue(instance) is NetState state)
            return state;

        if (Activator.CreateInstance(field.FieldType) is not NetState created)
        {
            log.Warning($"[SourceSharp] [NetState] {instance.GetType().Name}." +
                           $"{field.Name} is a {field.FieldType.Name}, which is " +
                           $"not a NetState; it will never replicate\n");
            return null;
        }

        field.SetValue(instance, created);
        return created;
    }
}

/// <summary>
/// The <c>[NetState]</c> fields per type, remembered (statics to zero): one per
/// registry, so the memo lives and dies with the load whose types it holds.
/// Reflection once per class rather than once per entity: a map spawns
/// hundreds of these and the answer cannot change.
/// </summary>
public sealed class NetStateFields
{
    private readonly Dictionary<Type, FieldInfo[]> _byType = new();

    /// <param name="log">Where a field that is not a state is said (statics to zero).</param>
    public NetStateFields(ILog log) => Log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>The registry's log, which the binding warns through.</summary>
    public ILog Log { get; }

    /// <summary><see cref="NetStateBinding.FieldsOn"/>, remembered per type.</summary>
    public FieldInfo[] FieldsOn(Type type)
    {
        lock (_byType)
        {
            if (!_byType.TryGetValue(type, out var fields))
                _byType[type] = fields = NetStateBinding.FieldsOn(type);
            return fields;
        }
    }
}
