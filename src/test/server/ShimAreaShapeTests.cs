using System.Reflection;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_shim_areas_split phase 2: each shim area is a class over its own
/// forwarder table, and each side's registration hands one instance per area,
/// by type (phase 11: nothing holds them as a set). One fact per class shape,
/// each driving a live slot of a <see cref="FakeShimRoot"/> through the area
/// the registration handed (and, for a derived area, through the instance it
/// handed under the base's type), so a class that read the wrong table answers
/// wrong.
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

    private static Dictionary<Type, object> Server(FakeShimRoot root)
    {
        var areas = new Dictionary<Type, object>();
        Assert.True(ServerAreaRegistration.TryAcquire(root.Bind(), (type, area) => areas.Add(type, area), out string why), why);
        return areas;
    }

    private static Dictionary<Type, object> Client(FakeShimRoot root)
    {
        var areas = new Dictionary<Type, object>();
        Assert.True(ClientAreaRegistration.TryAcquire(root.Bind(), (type, area) => areas.Add(type, area), out string why), why);
        return areas;
    }

    private static T Area<T>(Dictionary<Type, object> areas) => Assert.IsType<T>(areas[typeof(T)], exactMatch: false);

    /// <summary>
    /// A shared base no server area extends (the engine base) is one class, and
    /// both registrations hand it over the table their root serves.
    /// </summary>
    [Fact]
    public void ASharedBaseIsOneClassOnBothSides()
    {
        using var root = new FakeShimRoot();
        root.Live("SourceSharpEngine001", nameof(ShimEngineFwd.GetGlobalVarsPtr),
                  (nint)(delegate* unmanaged<void*, void*>)&Globals41);
        var server = Server(root);
        var client = Client(root);

        Assert.IsType<EngineArea>(server[typeof(IEngineArea)]);
        Assert.IsType<EngineArea>(client[typeof(IEngineArea)]);
        Assert.Equal((nint)41, Area<IEngineArea>(server).GetGlobalVarsPtr());
        Assert.Equal((nint)41, Area<IEngineArea>(client).GetGlobalVarsPtr());
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
        Assert.Equal(7, Area<IEntitiesServerArea>(server).EntityGetIndex(1));
        // And as its base, under the type the client registers the base by, so
        // code compiled into both sides asks for a both-sides area the same way.
        Assert.Same(server[typeof(IEntitiesServerArea)], server[typeof(IEntitiesArea)]);
        Assert.Equal(7, Area<IEntitiesArea>(server).EntityGetIndex(1));
        // The client's area is the base class itself, over the client's table.
        Assert.IsType<EntitiesArea>(client[typeof(IEntitiesArea)]);
        Assert.Equal(9, Area<IEntitiesArea>(client).EntityGetIndex(1));
    }

    /// <summary>A derived area's own slot is read from its own table.</summary>
    [Fact]
    public void ADerivedAreasOwnSlotIsItsTablesAlone()
    {
        using var root = new FakeShimRoot();
        root.Live("SourceSharpEntitiesServer001", nameof(ShimEntitiesServerFwd.FindEntityByClassname),
                  (nint)(delegate* unmanaged<void*, uint, byte*, uint>)&FoundFive);
        var server = Server(root);

        Assert.Equal(5u, Area<IEntitiesServerArea>(server).FindEntityByClassname(0, null));
        Assert.Null(typeof(IEntitiesArea).GetMethod(nameof(IEntitiesServerArea.FindEntityByClassname)));
    }

    /// <summary>
    /// plan_shim_areas_split phase 11: there is no container. Each side's
    /// registration is a static class with no state, which hands the areas it
    /// took and keeps none, so a consumer can only be given its areas, never
    /// reach them through a set.
    /// </summary>
    [Fact]
    public void ARegistrationHoldsNoAreas()
    {
        foreach (Type registration in new[] { typeof(ServerAreaRegistration), typeof(ClientAreaRegistration) })
        {
            Assert.True(registration.IsAbstract && registration.IsSealed, $"{registration.Name} is not static");
            Assert.Empty(registration.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        }

        Assert.Null(typeof(ShimRoot).Assembly.GetType("SourceSharp.Abi.ShimAreas"));
        Assert.Null(typeof(ShimRoot).Assembly.GetType("SourceSharp.Abi.ClientShimAreas"));
    }

    /// <summary>
    /// All or none: a root that serves every area but one hands nothing, and
    /// says which one it refused. A registration that handed the areas it had
    /// taken before the refusal would leave a side half attached.
    /// </summary>
    [Fact]
    public void ARefusedAreaHandsNone()
    {
        using var root = new FakeShimRoot();
        root.Withhold("SourceSharpNetworkServer001");
        var handed = new List<Type>();

        Assert.False(ServerAreaRegistration.TryAcquire(root.Bind(), (type, _) => handed.Add(type), out string why));
        Assert.Contains("SourceSharpNetworkServer001", why);
        Assert.Empty(handed);
    }

    /// <summary>
    /// A side with no engine registers the same areas, by the same types, as an
    /// attached one, over no table: so everything a composition builds can be
    /// built on it, and its attach guards refuse every call.
    /// </summary>
    [Fact]
    public void TheDetachedSetIsTheAttachedOnesTypes()
    {
        using var root = new FakeShimRoot();
        foreach (var (acquire, detached) in new (Func<ShimRoot, Action<Type, object>, bool>, Action<Action<Type, object>>)[]
                 {
                     ((r, add) => ServerAreaRegistration.TryAcquire(r, add, out _), ServerAreaRegistration.AddDetached),
                     ((r, add) => ClientAreaRegistration.TryAcquire(r, add, out _), ClientAreaRegistration.AddDetached),
                 })
        {
            var attached = new List<Type>();
            var none = new List<Type>();
            Assert.True(acquire(root.Bind(), (type, _) => attached.Add(type)));
            detached((type, _) => none.Add(type));

            Assert.NotEmpty(attached);
            Assert.Equal(attached, none);
        }
    }

    /// <summary>A client-only area is a class of its own, handed by the client's registration alone.</summary>
    [Fact]
    public void AClientOnlyAreaIsTheClientRegistrationsAlone()
    {
        using var root = new FakeShimRoot();
        root.Live("SourceSharpHudClient001", nameof(ShimHudClientFwd.GetLocalPlayerIndex),
                  (nint)(delegate* unmanaged<void*, int>)&PlayerThree);
        var client = Client(root);

        Assert.Equal(3, Area<IHudClientArea>(client).GetLocalPlayerIndex());
        Assert.False(Server(root).ContainsKey(typeof(IHudClientArea)));
    }
}
