using System.Reflection;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables Phase C0: <see cref="ShimAreas"/> acquires every
/// generated area from the shim root, and refuses a root that is missing one or
/// serves one with fewer slots than its generated layout. The root is a fake
/// in unmanaged memory answering version strings from a table this test fills.
/// </summary>
public unsafe class ShimAreasTests : IDisposable
{
    private readonly List<nint> _blocks = new();

    // What the fake root serves: version -> slot count. -1 = not served.
    private static readonly Dictionary<string, int> s_served = new();
    private static nint s_area;

    [Fact]
    public void EveryAreaServedBinds()
    {
        ShimRoot root = Root(_ => true, _ => 0);

        Assert.True(ShimAreas.TryAcquire(root, out var areas, out string why), why);
        Assert.NotNull(areas);
    }

    [Fact]
    public void AMissingAreaIsRefusedByName()
    {
        string victim = Versions()[3];
        ShimRoot root = Root(v => v != victim, _ => 0);

        Assert.False(ShimAreas.TryAcquire(root, out var areas, out string why));
        Assert.Null(areas);
        Assert.Contains($"does not serve {victim}", why);
    }

    [Fact]
    public void AnAreaWithTooFewSlotsIsRefusedNamingBoth()
    {
        string victim = Versions()[5];
        ShimRoot root = Root(_ => true, v => v == victim ? -1 : 0);

        Assert.False(ShimAreas.TryAcquire(root, out var areas, out string why));
        Assert.Null(areas);
        Assert.Contains(victim, why);
        Assert.Contains("needs", why);
    }

    [Fact]
    public void TheCountMatchesTheGeneratedTable()
        => Assert.Equal(25, ShimAreas.Count);   // 25 since plan_physics_bones_native_zero 2.4 (the three joint tables left); 28 since the debug-api lane's IShimDiagnostics; 27 since plan_side_isolation P2: IShimUserMessages, and IShimEvents bound for itself

    // Every version the generated class asks for, read from its own fields'
    // layouts so this test cannot drift from the generator.
    private static List<string> Versions()
        => typeof(ShimAreas).GetFields(BindingFlags.Public | BindingFlags.Instance)
               .Select(f => $"SourceSharp{Stem(f.Name)}001")
               .ToList();

    // IShimEngine's field is EngineBase and its version SourceSharpEngine001
    // (tools/shim-areas field()); every other field is its version's stem.
    private static string Stem(string field) => field == "EngineBase" ? "Engine" : field;

    private static int Needs(string version)
    {
        string stem = version["SourceSharp".Length..^"001".Length];
        var field = typeof(ShimAreas).GetField(stem == "Engine" ? "EngineBase" : stem)!;
        var vtbl = field.FieldType.GetGenericArguments()[0];
        return Marshal.SizeOf(vtbl) / sizeof(nint);
    }

    // serve: whether a version is served; delta: added to its exact slot count.
    private ShimRoot Root(Func<string, bool> serve, Func<string, int> delta)
    {
        s_served.Clear();
        foreach (string v in Versions())
            s_served[v] = serve(v) ? Needs(v) + delta(v) : -1;

        // One shared, zeroed area object: nothing here is called through it.
        var areaVtbl = Alloc(64);
        var area = Alloc(1);
        area[0] = (nint)areaVtbl;
        s_area = (nint)area;

        var rootVtbl = Alloc(3);   // Acquire, AcquireInterface, Release
        rootVtbl[0] = (nint)(delegate* unmanaged<void*, byte*, void*, void*>)&RootAcquire;
        rootVtbl[2] = (nint)(delegate* unmanaged<void*, void>)&RootRelease;
        var root = Alloc(1);
        root[0] = (nint)rootVtbl;

        Assert.True(ShimRoot.TryBind(root, ShimRoot.ExpectedEntries, out var bound, out string why), why);
        return bound!;
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
        string v = Marshal.PtrToStringUTF8((nint)name) ?? string.Empty;
        int n = s_served.TryGetValue(v, out int c) ? c : -1;
        *(int*)pEntries = n < 0 ? 0 : n;
        return n < 0 ? null : (void*)s_area;
    }

    [UnmanagedCallersOnly]
    private static void RootRelease(void* self)
    {
    }

    public void Dispose()
    {
        foreach (nint block in _blocks)
            NativeMemory.Free((void*)block);
    }
}
