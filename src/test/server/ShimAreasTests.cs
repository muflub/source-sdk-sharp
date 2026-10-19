using System.Reflection;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables Phase C0: <see cref="ShimAreas"/> acquires every
/// generated area from the shim root, and refuses a root that is missing one or
/// serves its forwarder table (plan_forwarders_everywhere.md) with fewer slots
/// than the generated struct reads. The root is a fake in unmanaged memory
/// answering version strings and table names from tables this test fills.
/// </summary>
public unsafe class ShimAreasTests : IDisposable
{
    private readonly List<nint> _blocks = new();

    // What the fake root serves: an area's version -> its object, and its
    // table's name -> the table's header. A name in neither is not served.
    private static readonly Dictionary<string, nint> s_served = new();

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
        Assert.Contains($"with {Needs(victim) - 1} forwarder(s)", why);
        Assert.Contains($"reads {Needs(victim)}", why);
    }

    [Fact]
    public void TheCountMatchesTheGeneratedTable()
        => Assert.Equal(22, ShimAreas.Count);   // 22 since plan_server_native_v2 T3 (the string tables folded; IShimStringTablesServer retired); 23 since plan_server_native_v2 T2 (IShimWeapons retired: RegisterWeaponType went, RegisterAmmoType is the client's); 24 since the same plan (IShimInventory left with its last slot); 25 since plan_physics_bones_native_zero 2.4 (the three joint tables left); 28 since the debug-api lane's IShimDiagnostics; 27 since plan_side_isolation P2: IShimUserMessages, and IShimEvents bound for itself

    // Every version the generated class asks for, read from its own fields'
    // layouts so this test cannot drift from the generator.
    private static List<string> Versions()
        => typeof(ShimAreas).GetFields(BindingFlags.Public | BindingFlags.Instance)
               .Select(f => $"SourceSharp{Stem(f.Name)}001")
               .ToList();

    // IShimEngine's field is EngineBase and its version SourceSharpEngine001
    // (tools/shim-areas field()); every other field is its version's stem.
    private static string Stem(string field) => field == "EngineBase" ? "Engine" : field;

    private static Type Fwd(string version)
    {
        string stem = version["SourceSharp".Length..^"001".Length];
        var field = typeof(ShimAreas).GetField(stem == "Engine" ? "EngineBase" : stem)!;
        return ShimAreaTypes.FwdOf(field.FieldType);
    }

    private static int Needs(string version) => Marshal.SizeOf(Fwd(version)) / sizeof(nint);

    private static string TableName(string version)
        => (string)Fwd(version).GetProperty("TableName", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    // serve: whether a version is served; delta: added to its exact slot count.
    private ShimRoot Root(Func<string, bool> serve, Func<string, int> delta)
    {
        s_served.Clear();

        // One shared, zeroed area object: nothing here is called through it.
        var area = Alloc(1);

        foreach (string v in Versions())
        {
            if (serve(v))
                s_served[v] = (nint)area;

            int count = Needs(v) + delta(v);
            var header = (ForwarderTableHeader*)Alloc(sizeof(ForwarderTableHeader) / sizeof(nint));
            header->Slots = (void**)Alloc(Math.Max(count, 1));
            header->SlotCount = count;
            s_served[TableName(v)] = (nint)header;
        }

        var rootSlots = Alloc(3);   // the root's forwarder table: Acquire, AcquireInterface, Release
        rootSlots[0] = (nint)(delegate* unmanaged<void*, byte*, void*>)&RootAcquire;
        rootSlots[2] = (nint)(delegate* unmanaged<void*, void>)&RootRelease;
        var rootTable = (ForwarderTableHeader*)Alloc(sizeof(ForwarderTableHeader) / sizeof(nint));
        rootTable->Slots = (void**)rootSlots;
        rootTable->SlotCount = 3;

        Assert.True(ShimRoot.TryBind(Alloc(1), rootTable, out var bound, out string why), why);
        return bound!;
    }

    private nint* Alloc(int words)
    {
        var p = (nint*)NativeMemory.AllocZeroed((nuint)(words * sizeof(nint)));
        _blocks.Add((nint)p);
        return p;
    }

    [UnmanagedCallersOnly]
    private static void* RootAcquire(void* self, byte* name)
    {
        string v = Marshal.PtrToStringUTF8((nint)name) ?? string.Empty;
        return s_served.TryGetValue(v, out nint answer) ? (void*)answer : null;
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
