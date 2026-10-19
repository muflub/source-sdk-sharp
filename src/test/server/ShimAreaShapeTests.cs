using System.Reflection;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_shim_areas_split phase 2: each shim area is a class over its own
/// forwarder table, and the containers hold one instance per area. One fact per
/// class shape, each driving a live slot of a <see cref="FakeShimRoot"/> through
/// the area the container holds (and, for a derived area, through the base-named
/// view of it), so a class that read the wrong table answers wrong. Since phase
/// 9 a container has no calls of its own.
/// </summary>
public unsafe class ShimAreaShapeTests
{
    [UnmanagedCallersOnly]
    private static void* Globals41(void* self) => (void*)41;

    [UnmanagedCallersOnly]
    private static int IndexSeven(void* self, uint handle) => 7;

    [UnmanagedCallersOnly]
    private static int IndexNine(void* self, uint handle) => 9;

    [UnmanagedCallersOnly]
    private static uint FoundFive(void* self, uint start, byte* name) => 5;

    [UnmanagedCallersOnly]
    private static int PlayerThree(void* self) => 3;

    private static ShimAreas Server(FakeShimRoot root)
    {
        Assert.True(ShimAreas.TryAcquire(root.Bind(), out var areas, out string why), why);
        return areas!;
    }

    private static ClientShimAreas Client(FakeShimRoot root)
    {
        Assert.True(ClientShimAreas.TryAcquire(root.Bind(), out var areas, out string why), why);
        return areas!;
    }

    /// <summary>
    /// A shared base no server area extends (the engine base) is one class, and
    /// both containers hold it over the table their root serves.
    /// </summary>
    [Fact]
    public void ASharedBaseIsOneClassOnBothContainers()
    {
        using var root = new FakeShimRoot();
        root.Live("SourceSharpEngine001", nameof(ShimEngineFwd.GetGlobalVarsPtr),
                  (nint)(delegate* unmanaged<void*, void*>)&Globals41);
        var server = Server(root);
        var client = Client(root);

        Assert.IsType<EngineArea>(server.EngineBase);
        Assert.IsType<EngineArea>(client.EngineBase);
        Assert.Equal((nint)41, server.EngineBase.GetGlobalVarsPtr());
        Assert.Equal((nint)41, client.EngineBase.GetGlobalVarsPtr());
    }

    /// <summary>
    /// A server area that extends a shared base derives the base's class, and a
    /// base slot reached through it reads the server's table, whose first slots
    /// are the base's. The client's table for the same base answers something
    /// else, so a base class that read it would fail here.
    /// </summary>
    [Fact]
    public void ADerivedServerAreaReadsItsBaseSlotsFromTheServerTable()
    {
        using var root = new FakeShimRoot();
        root.Live("SourceSharpEntitiesServer001", nameof(ShimEntitiesServerFwd.EntityGetIndex),
                  (nint)(delegate* unmanaged<void*, uint, int>)&IndexSeven);
        root.Live("SourceSharpEntities001", nameof(ShimEntitiesFwd.EntityGetIndex),
                  (nint)(delegate* unmanaged<void*, uint, int>)&IndexNine);
        var server = Server(root);
        var client = Client(root);

        Assert.Equal(typeof(EntitiesArea), typeof(EntitiesServerArea).BaseType);
        Assert.Equal(7, server.EntitiesServer.EntityGetIndex(1));
        // And as its base, under the name the client holds the base by, so
        // code compiled into both sides calls a both-sides slot the same way.
        Assert.IsType<EntitiesServerArea>(server.Entities);
        Assert.Equal(7, server.Entities.EntityGetIndex(1));
        // The client's area is the base class itself, over the client's table.
        Assert.IsType<EntitiesArea>(client.Entities);
        Assert.Equal(9, client.Entities.EntityGetIndex(1));
    }

    /// <summary>A derived area's own slot is read from its own table.</summary>
    [Fact]
    public void ADerivedAreasOwnSlotIsItsTablesAlone()
    {
        using var root = new FakeShimRoot();
        root.Live("SourceSharpEntitiesServer001", nameof(ShimEntitiesServerFwd.FindEntityByClassname),
                  (nint)(delegate* unmanaged<void*, uint, byte*, uint>)&FoundFive);
        var server = Server(root);

        Assert.Equal(5u, server.EntitiesServer.FindEntityByClassname(0, null));
        Assert.Null(typeof(EntitiesArea).GetMethod(nameof(EntitiesServerArea.FindEntityByClassname)));
    }

    /// <summary>
    /// plan_shim_areas_split phase 9: a container holds areas and nothing to
    /// call. Each slot is called on the area that serves it, so a caller names
    /// the area it depends on rather than the whole side.
    /// </summary>
    [Fact]
    public void NeitherContainerHasACallOfItsOwn()
    {
        foreach (Type container in new[] { typeof(ShimAreas), typeof(ClientShimAreas) })
        {
            var calls = container.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                 .Where(m => !m.IsSpecialName)
                                 .Select(m => m.Name)
                                 .ToList();
            Assert.True(calls.Count == 0, $"{container.Name} declares calls: {string.Join(", ", calls)}");
            Assert.NotEmpty(container.GetFields(BindingFlags.Public | BindingFlags.Instance));
        }
    }

    /// <summary>A client-only area is a class of its own, held by the client container alone.</summary>
    [Fact]
    public void AClientOnlyAreaIsTheClientContainersAlone()
    {
        using var root = new FakeShimRoot();
        root.Live("SourceSharpHudClient001", nameof(ShimHudClientFwd.GetLocalPlayerIndex),
                  (nint)(delegate* unmanaged<void*, int>)&PlayerThree);
        var client = Client(root);

        Assert.Equal(3, client.HudClient.GetLocalPlayerIndex());
        Assert.Null(typeof(ShimAreas).GetField(nameof(ClientShimAreas.HudClient)));
        Assert.Null(typeof(ShimAreas).GetProperty(nameof(ClientShimAreas.HudClient)));
    }
}
