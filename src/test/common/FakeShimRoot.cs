using System.Reflection;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A shim root in unmanaged memory serving every generated area with a zeroed
/// vtable, except the slots a test fills with <see cref="Live"/>. The root
/// object's second word holds a handle to this instance, so tests in parallel
/// classes each answer from their own table.
/// </summary>
internal sealed unsafe class FakeShimRoot : IDisposable
{
    private readonly List<nint> _blocks = new();
    private readonly Dictionary<string, nint> _areas = new();
    private readonly Dictionary<string, nint> _interfaces = new();
    private readonly Dictionary<string, (Type Vtbl, nint Table)> _tables = new();
    private GCHandle _self;

    public FakeShimRoot()
    {
        foreach (var field in typeof(ShimAreas).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            Type vtbl = field.FieldType.GetGenericArguments()[0];
            var table = Alloc(Marshal.SizeOf(vtbl) / sizeof(nint));
            var obj = Alloc(1);
            obj[0] = (nint)table;

            // IShimEngine is its field EngineBase (tools/shim-areas field()),
            // served as SourceSharpEngine001, on the server too since lane
            // c2-sims bound it for itself.
            string version = $"SourceSharp{(field.Name == "EngineBase" ? "Engine" : field.Name)}001";
            _areas[version] = (nint)obj;
            _tables[version] = (vtbl, (nint)table);
        }

        // And the client's areas the server's do not already name, so the same
        // root binds a client side (AttachClientShims). The client's engine
        // base is its field EngineBase, served as SourceSharpEngine001.
        foreach (var field in typeof(ClientShimAreas).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            string name = field.Name == "EngineBase" ? "Engine" : field.Name;
            string version = $"SourceSharp{name}001";
            if (_areas.ContainsKey(version))
                continue;

            Type vtbl = field.FieldType.GetGenericArguments()[0];
            var table = Alloc(Marshal.SizeOf(vtbl) / sizeof(nint));
            var obj = Alloc(1);
            obj[0] = (nint)table;
            _areas[version] = (nint)obj;
            _tables[version] = (vtbl, (nint)table);
        }
    }

    /// <summary>Fills one slot of one area, by the area's version and the slot's name.</summary>
    public FakeShimRoot Live(string version, string slot, nint fn)
    {
        var (vtbl, table) = _tables[version];
        ((nint*)table)[(int)Marshal.OffsetOf(vtbl, slot) / sizeof(nint)] = fn;
        return this;
    }

    /// <summary>
    /// Serves one interface through the root's AcquireInterface slot (an area
    /// that is not a shim-slot area, such as SourceSharpLevel001).
    /// </summary>
    public FakeShimRoot Interface(string version, void* self)
    {
        _interfaces[version] = (nint)self;
        return this;
    }

    /// <summary>The root, bound the way LoadSideV2 binds the real one.</summary>
    /// <summary>Serves <paramref name="self"/> as the area <paramref name="version"/> through Acquire.</summary>
    public FakeShimRoot Area(string version, void* self)
    {
        _areas[version] = (nint)self;
        return this;
    }

    public ShimRoot Bind()
    {
        void* root = Raw();
        Assert.True(ShimRoot.TryBind(root, ShimRoot.ExpectedEntries, out var bound, out string why), why);
        return bound!;
    }

    /// <summary>The root as native hands it over, unbound.</summary>
    public void* Raw()
    {
        var rootVtbl = Alloc(3);   // Acquire, AcquireInterface, Release
        rootVtbl[0] = (nint)(delegate* unmanaged<void*, byte*, void*, void*>)&RootAcquire;
        rootVtbl[1] = (nint)(delegate* unmanaged<void*, int, byte*, void*, byte*, int, void*>)&RootAcquireInterface;
        rootVtbl[2] = (nint)(delegate* unmanaged<void*, void>)&RootRelease;

        if (!_self.IsAllocated)
            _self = GCHandle.Alloc(this);
        var root = Alloc(2);
        root[0] = (nint)rootVtbl;
        root[1] = GCHandle.ToIntPtr(_self);
        return root;
    }

    private nint* Alloc(int words)
    {
        var p = (nint*)NativeMemory.AllocZeroed((nuint)(words * sizeof(nint)));
        _blocks.Add((nint)p);
        return p;
    }

    [UnmanagedCallersOnly]
    private static void* RootAcquire(void* self, byte* name, void* pEntries)
    {
        var owner = (FakeShimRoot)GCHandle.FromIntPtr(((nint*)self)[1]).Target!;
        string v = Marshal.PtrToStringUTF8((nint)name) ?? string.Empty;
        if (!owner._areas.TryGetValue(v, out nint area))
        {
            *(int*)pEntries = 0;
            return null;
        }

        *(int*)pEntries = 1000;   // at least every slot the layout knows
        return (void*)area;
    }

    [UnmanagedCallersOnly]
    private static void* RootAcquireInterface(void* self, int type, byte* name, void* pEntries, byte* error, int errorBytes)
    {
        var owner = (FakeShimRoot)GCHandle.FromIntPtr(((nint*)self)[1]).Target!;
        string v = Marshal.PtrToStringUTF8((nint)name) ?? string.Empty;
        if (!owner._interfaces.TryGetValue(v, out nint found))
        {
            if (pEntries != null)
                *(int*)pEntries = 0;
            return null;
        }

        if (pEntries != null)
            *(int*)pEntries = 1000;   // at least every slot the layout knows
        return (void*)found;
    }

    [UnmanagedCallersOnly]
    private static void RootRelease(void* self)
    {
    }

    public void Dispose()
    {
        foreach (nint block in _blocks)
            NativeMemory.Free((void*)block);
        _blocks.Clear();
        if (_self.IsAllocated)
            _self.Free();
    }
}
