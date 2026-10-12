extern alias client;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables Phase B1: the engine API is one <see cref="EngineSide"/>
/// per side instead of one static class choosing a table by thread-static.
/// These facts hold the two sides apart, and hold the static forwarders to a
/// ceiling that Phase B lowers to zero.
/// </summary>
public unsafe class EngineSideTests
{
    [Fact]
    public void AFreshSideAnswersItsDefaultsBeforeItIsLoaded()
    {
        var side = new EngineSide(Side.Server);

        Assert.False(side.IsInitialized);
        Assert.Equal(-1, side.EntityGetIndex(new EntityHandle(1)));
    }

    [Fact]
    public void LoadingOneSideLeavesTheOtherUntouched()
    {
        var server = new EngineSide(Side.Server);
        var client = new EngineSide(Side.Client);

        // plan_client_vtables CC-last: no struct; the server's slot is its shim
        // area's, attached from a fake root with only that slot live.
        using var root = new FakeShimRoot();
        root.Live("SourceSharpEntitiesServer001", nameof(ShimEntitiesServerVtbl.EntityGetIndex),
                  (nint)(delegate* unmanaged<void*, uint, int>)&IndexSeven);
        server.Initialize();
        Assert.True(server.AttachShims(root.Bind(), out string why), why);

        Assert.True(server.IsInitialized);
        Assert.Equal(7, server.EntityGetIndex(new EntityHandle(1)));

        // Impossible to state before B1: one static table per side, chosen by
        // the thread. Here the client instance was never given the table.
        Assert.False(client.IsInitialized);
        Assert.Equal(-1, client.EntityGetIndex(new EntityHandle(1)));
    }

    /// <summary>
    /// The surfaceprop table is the side's own (plan_abi_hotreload Phase 1):
    /// PhysicsSurfaces.Of was a static over Engine.Current, which answered
    /// for whichever side the thread had active. Two engines, one attached:
    /// the attached side's table answers through its slot, the other's
    /// answers default, and the side's container is handed its own table.
    /// </summary>
    [Fact]
    public void EachSidesSurfaceTableReadsItsOwnRouting()
    {
        var server = new EngineSide(Side.Server);
        var other = new EngineSide(Side.Server);

        using var root = new FakeShimRoot();
        root.Live("SourceSharpMovementServer001", nameof(ShimMovementServerVtbl.MoveSurface),
                  (nint)(delegate* unmanaged<void*, int, MoveSurfaceBlob*, int>)&SurfaceIsSand);
        server.Initialize();
        Assert.True(server.AttachShims(root.Bind(), out string why), why);

        MoveSurface sand = server.Surfaces.Of(3);
        Assert.True(sand.Valid, "the attached side's table answers through its own slot");
        Assert.Equal('N', (char)sand.Material);
        Assert.Equal(1300f, sand.Density);

        Assert.False(other.Surfaces.Of(3).Valid, "an unattached side's table answers default");

        var services = new ServiceContainer().AddInstance(new StandaloneMirror());
        EngineServices.AddTo(services, server);
        Assert.Same(server.Surfaces, services.Require<PhysicsSurfaces>());
    }

    [UnmanagedCallersOnly]
    private static int SurfaceIsSand(void* self, int surfaceProps, MoveSurfaceBlob* blob)
    {
        if (surfaceProps != 3)
            return 0;
        blob->Valid = 1;
        blob->Material = 'N';
        blob->Density = 1300f;
        return 1;
    }

    [Fact]
    public void TwoContainersInOneProcessEachAnswerForTheirOwnSide()
    {
        // plan_engineapi_vtables B2: a side's container binds every engine area
        // to that side. A server object's call never reaches the client's table.
        var server = new EngineSide(Side.Server);
        var client = new EngineSide(Side.Client);

        using var root = new FakeShimRoot();
        root.Live("SourceSharpEntitiesServer001", nameof(ShimEntitiesServerVtbl.EntityGetIndex),
                  (nint)(delegate* unmanaged<void*, uint, int>)&IndexSeven);
        server.Initialize();
        Assert.True(server.AttachShims(root.Bind(), out string why), why);

        var serverServices = new ServiceContainer().AddInstance(new StandaloneMirror());
        var clientServices = new ServiceContainer().AddInstance(new StandaloneMirror());
        Assert.True(EngineServices.AddTo(serverServices, server) > 20, "every engine area registered");
        EngineServices.AddTo(clientServices, client);

        Assert.Equal(7, serverServices.Require<IEngineEntities>().GetIndex(new EntityHandle(1)));
        Assert.Equal(-1, clientServices.Require<IEngineEntities>().GetIndex(new EntityHandle(1)));
    }

    /// <summary>
    /// No static field anywhere in the managed game holds an EngineSide
    /// (plan_abi_hotreload Phase 1): each side's load builds its own and its
    /// session holds it. The static Engine held one per side for the process.
    /// </summary>
    [Fact]
    public void NoStaticFieldHoldsAnEngineSide()
    {
        var assemblies = new[]
        {
            typeof(ILog).Assembly, typeof(EngineSide).Assembly, typeof(SourceSharp.Shared.Interop.SideLoad).Assembly,
            typeof(SourceSharp.Server.Interop.FrameBinding).Assembly, typeof(SourceSharp.Client.Interop.ClientSideEntry).Assembly,
            typeof(SourceSharp.Server.ServerModule).Assembly, typeof(client::SourceSharp.Client.ClientModule).Assembly,
        }.Distinct().ToList();

        // The positive arm: the walk sees a static of the type where one is.
        Assert.Equal([nameof(PlantedHolder.Held)], StaticEngineSides(typeof(PlantedHolder).Assembly)
                                                       .Where(f => f.DeclaringType == typeof(PlantedHolder))
                                                       .Select(f => f.Name));

        var found = assemblies.SelectMany(StaticEngineSides)
                              .Select(f => $"{f.DeclaringType!.FullName}.{f.Name}").ToList();
        Assert.True(found.Count == 0, "static EngineSide fields: " + string.Join(", ", found));
    }

    private static IEnumerable<System.Reflection.FieldInfo> StaticEngineSides(System.Reflection.Assembly assembly)
    {
        const System.Reflection.BindingFlags statics = System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.DeclaredOnly;
        return assembly.GetTypes().SelectMany(t => t.GetFields(statics))
                       .Where(f => typeof(EngineSide).IsAssignableFrom(f.FieldType));
    }

    private static class PlantedHolder
    {
        internal static readonly EngineSide? Held = null;
    }

    /// <summary>
    /// A side asks ITS OWN engine whether the view entity is valid. Through the
    /// static <c>EntityHandle.IsValid</c> it asked whichever side was active, so
    /// a side that knew the view entity was alive sent 0 (the player's own eyes)
    /// whenever the active side did not know the handle. The side's answer comes
    /// from its own liveness mirror here: the EntityIsValid slot suppresses the
    /// GC transition, so no managed fake can serve it.
    /// </summary>
    [Fact]
    public void TheViewEntityIsJudgedByTheSidesOwnEngine()
    {
        var server = new EngineSide(Side.Server);
        var view = new EntityHandle(40);
        var gone = new EntityHandle(41);

        using var root = new FakeShimRoot();
        root.Live("SourceSharpPlayers001", nameof(ShimPlayersVtbl.PlayerSetViewEntity),
                  (nint)(delegate* unmanaged<void*, uint, uint, void>)&RecordView);
        server.Initialize();
        Assert.True(server.AttachShims(root.Bind(), out string why), why);

        var mirror = new LivenessMirror();
        mirror.NoteCreated(view);
        mirror.NoteCreated(gone);
        mirror.NoteRemoved(gone);
        using var attached = server.AttachLiveness(mirror);
        Assert.True(server.EntityIsValid(view), "the side's own mirror knows the view entity");

        s_SentView = 0xDEADu;
        server.PlayerSetViewEntity(new EntityHandle(1), view);
        Assert.Equal(40u, s_SentView);

        // The control: a handle the side knows is gone gives the view back.
        server.PlayerSetViewEntity(new EntityHandle(1), gone);
        Assert.Equal(0u, s_SentView);
    }

    // What the fake PlayerSetViewEntity slot was last sent. Test-only; the slot
    // is a function pointer with no state of its own to record into.
    private static uint s_SentView;

    [UnmanagedCallersOnly]
    private static void RecordView(void* self, uint player, uint view) => s_SentView = view;

    [UnmanagedCallersOnly]
    private static int IndexSeven(void* self, uint handle) => 7;
}
