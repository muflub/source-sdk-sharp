using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace SourceSharp.Bootstrap;

/// <summary>
/// A side's own entry, as the Bootstrap calls it (plan_side_entries SE2): four
/// calls with raw arguments only. <see cref="SideEntryPoints"/> is the real one,
/// over function pointers into the side's load context; the tests drive the
/// Bootstrap's load with fakes.
/// </summary>
internal unsafe interface ISideEntry
{
    /// <summary>The side's load: binds its shims, makes its module and its managed root.</summary>
    int Load(void* pShimRoot, void* pShimRootTable, void** ppManagedRoot, nint* pVtables, nint* pSession, byte* pFacts);

    /// <summary>The managed build stamp, once the module has initialised (the integrator's ruling A).</summary>
    int Stamped(nint session, byte* pFacts);

    /// <summary>On the OLD build, before a reload: 0 to go ahead, 1 with the reason.</summary>
    int ReloadCheck(nint session, byte* pWhy, int nWhy);

    /// <summary>The side's half of its unload; frees the session.</summary>
    int Unload(nint session);
}

/// <summary>
/// The side's entry, resolved by its FIXED NAME in the side's freshly made load
/// context, on every load: a function pointer into a collectible context
/// dangles once that context unloads, so none is kept past it.
/// </summary>
internal sealed unsafe class SideEntryPoints : ISideEntry
{
    /// <summary>
    /// The four methods, each named for its side (LoadServer, StampedClient,
    /// ...) as every crossing op is, with the parameters the Bootstrap passes.
    /// </summary>
    internal static (string Name, Type[] Parameters)[] Methods(string side) =>
    [
        ("Load" + side, [typeof(void*), typeof(void*), typeof(void**), typeof(nint*), typeof(nint*), typeof(byte*)]),
        ("Stamped" + side, [typeof(nint), typeof(byte*)]),
        ("ReloadCheck" + side, [typeof(nint), typeof(byte*), typeof(int)]),
        ("Unload" + side, [typeof(nint)]),
    ];

    private readonly delegate* unmanaged<void*, void*, void**, nint*, nint*, byte*, int> _load;
    private readonly delegate* unmanaged<nint, byte*, int> _stamped;
    private readonly delegate* unmanaged<nint, byte*, int, int> _reloadCheck;
    private readonly delegate* unmanaged<nint, int> _unload;

    private SideEntryPoints(nint[] pointers)
    {
        _load = (delegate* unmanaged<void*, void*, void**, nint*, nint*, byte*, int>)pointers[0];
        _stamped = (delegate* unmanaged<nint, byte*, int>)pointers[1];
        _reloadCheck = (delegate* unmanaged<nint, byte*, int, int>)pointers[2];
        _unload = (delegate* unmanaged<nint, int>)pointers[3];
    }

    public int Load(void* pShimRoot, void* pShimRootTable, void** ppManagedRoot, nint* pVtables, nint* pSession, byte* pFacts)
        => _load(pShimRoot, pShimRootTable, ppManagedRoot, pVtables, pSession, pFacts);

    public int Stamped(nint session, byte* pFacts) => _stamped(session, pFacts);

    public int ReloadCheck(nint session, byte* pWhy, int nWhy) => _reloadCheck(session, pWhy, nWhy);

    public int Unload(nint session) => _unload(session);

    /// <summary>
    /// <paramref name="typeName"/> in <paramref name="assemblyName"/>, loaded
    /// into <paramref name="context"/>, with its four methods for
    /// <paramref name="side"/>; null with the reason in words when either is
    /// missing or the type lacks one of the four.
    /// </summary>
    internal static SideEntryPoints? Resolve(AssemblyLoadContext context, string assemblyName, string typeName,
                                             string side, out string why)
    {
        Assembly assembly;
        try
        {
            assembly = context.LoadFromAssemblyName(new AssemblyName(assemblyName));
        }
        catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            why = $"the side's entry assembly {assemblyName} did not load: {e.Message}";
            return null;
        }

        return Resolve(assembly, typeName, side, out why);
    }

    /// <inheritdoc cref="Resolve(AssemblyLoadContext, string, string, string, out string)"/>
    internal static SideEntryPoints? Resolve(Assembly assembly, string typeName, string side, out string why)
    {
        if (assembly.GetType(typeName) is not { } type)
        {
            why = $"{assembly.GetName().Name} declares no {typeName}: the side's entry is resolved by that fixed name";
            return null;
        }

        var methods = Methods(side);
        var pointers = new nint[methods.Length];
        var missing = new List<string>();
        for (int i = 0; i < methods.Length; i++)
        {
            var (name, parameters) = methods[i];
            MethodInfo? method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, parameters);
            if (method is null || method.ReturnType != typeof(int)
                || method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>() is null)
            {
                missing.Add(name);
                continue;
            }

            pointers[i] = method.MethodHandle.GetFunctionPointer();
        }

        if (missing.Count > 0)
        {
            why = $"{typeName} has no [UnmanagedCallersOnly] static int {string.Join(", ", missing)} of the expected "
                + $"signature: the side's entry is resolved by the fixed names {string.Join(", ", methods.Select(m => m.Name))}";
            return null;
        }

        why = string.Empty;
        return new SideEntryPoints(pointers);
    }
}
