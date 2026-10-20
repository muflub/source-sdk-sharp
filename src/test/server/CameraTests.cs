using SourceSharp.Abi;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What a point_viewcontrol does that has an answer without a game.
///
/// Most of the class does not: holding a view, freezing a player and hiding a
/// viewmodel are all reaches across the ABI, and with no engine behind it every
/// one of them is a no-op that would test nothing. The part that IS testable
/// here is the part the divergence this port closed was about — walking a chain
/// of path nodes — because the chain is managed objects on both sides now, and
/// linking one needs no map.
///
/// Positions are all zero in this harness, since an entity's origin comes from
/// the engine. That does not weaken the walk: what is checked is the ORDER
/// nodes are passed in, which node's speed the camera takes, and that the chain
/// ends rather than looping on its last node — and a zero-length leg reaches the
/// next node in one step, which makes the sequence easier to see rather than
/// harder.
/// </summary>
public class CameraTests
{

    private static ScriptedEntityRegistry NewRegistry()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TriggerCamera).Assembly);
        registry.Services = Ignoring("Clock.Now", "Entities.GetOrigin")
            .AddInstance<IViewControlHolders>(new ViewControlHolders());
        return registry;
    }

    private static PathCorner Corner(ScriptedEntityRegistry registry, uint handle, string speed = "0")
    {
        var h = new EntityHandle(handle);

        registry.KeyValue(h, "path_corner", "speed", speed);
        registry.Spawn(h, "path_corner");

        return (PathCorner)registry.Get(h)!;
    }

    private static PathTrack Track(ScriptedEntityRegistry registry, uint handle, string speed = "0")
    {
        var h = new EntityHandle(handle);

        registry.KeyValue(h, "path_track", "speed", speed);
        registry.Spawn(h, "path_track");

        return (PathTrack)registry.Get(h)!;
    }

    private static TriggerCamera Camera(ScriptedEntityRegistry registry, uint handle)
    {
        var h = new EntityHandle(handle);

        registry.Spawn(h, "point_viewcontrol");

        return (TriggerCamera)registry.Get(h)!;
    }

    /// <summary>
    /// The whole point of the port, in one test.
    ///
    /// The first node is where the camera STARTS, not the first place it flies
    /// to — so it is passed immediately and the camera sets off for the second.
    /// Stock has it that way because a mapper puts the camera on top of its
    /// first corner, and a camera that flew to where it already was would sit
    /// still for a frame and then jerk.
    /// </summary>
    [Fact]
    public void CameraWalksAManagedCornerChain()
    {
        var registry = NewRegistry();

        var first = Corner(registry, 0x00C1_0001u);
        var second = Corner(registry, 0x00C1_0002u);
        var third = Corner(registry, 0x00C1_0003u);

        first.LinkTo(second);
        second.LinkTo(third);

        var camera = Camera(registry, 0x00C1_0010u);
        camera.StartOnPath(first);

        camera.Move(0.015f);
        Assert.True(first.OnPass.FireCount == 1, "the node the camera starts on is passed at once");
        Assert.True(camera.CurrentPathNode is not null, "and the camera is heading for the one after it");

        camera.Move(0.015f);
        Assert.True(second.OnPass.FireCount == 1, "the second node is passed in its turn");

        camera.Move(0.015f);
        Assert.True(third.OnPass.FireCount == 1, "and the third");

        Assert.True(camera.CurrentPathNode is null, "a chain that has run out leaves nothing to head for");

        camera.Move(0.015f);
        Assert.True(first.OnPass.FireCount == 1 && second.OnPass.FireCount == 1 && third.OnPass.FireCount == 1, "and walking past the end passes nothing twice");
    }

    /// <summary>
    /// A node's speed belongs to the leg LEAVING it, which is the same
    /// asymmetry func_train has and which reads as a bug until the reason is
    /// stated: a corner describes the journey out of itself.
    ///
    /// A node with no speed leaves the camera's own alone, so the speed here is
    /// on the second node and takes effect once that node has been reached.
    /// </summary>
    [Fact]
    public void CameraTakesTheNodeSpeed()
    {
        var registry = NewRegistry();

        var first = Corner(registry, 0x00C2_0001u);
        var second = Corner(registry, 0x00C2_0002u, speed: "150");

        first.LinkTo(second);

        var camera = Camera(registry, 0x00C2_0010u);
        camera.StartOnPath(first);

        // One step of a second, which is long enough for the acceleration to
        // have reached the target rather than to be part-way to it: the default
        // is 500 units per second per second and the target is 150.
        camera.Move(1.0f);

        Assert.True(Math.Abs(camera.CurrentSpeed - 150f) < 0.01f, "the camera accelerates toward the speed the node named");
    }

    /// <summary>
    /// A path_track chain works too, and its nodes never impose a wait.
    ///
    /// Stock reaches both through CBaseEntity, where GetDelay() is a virtual
    /// answering 0 that only path_corner overrides — so a camera has never
    /// waited at a path_track, and the managed wrapper says so out loud rather
    /// than leaving it to be inferred.
    /// </summary>
    [Fact]
    public void CameraWalksAPathTrackChain()
    {
        var registry = NewRegistry();

        var first = Track(registry, 0x00C3_0001u);
        var second = Track(registry, 0x00C3_0002u);

        first.LinkTo(second);

        var camera = Camera(registry, 0x00C3_0010u);
        camera.StartOnPath(first);

        camera.Move(0.015f);
        Assert.True(first.OnPass.FireCount == 1, "a path_track is passed like a corner is");

        camera.Move(0.015f);
        Assert.True(second.OnPass.FireCount == 1, "and the chain is followed forward");
        Assert.True(camera.CurrentPathNode is null, "a path_track chain ends where it ends");
    }

    /// <summary>
    /// The commonest point_viewcontrol of all: one that holds a view and stays
    /// where the mapper put it. Nothing to walk means nothing walked, rather
    /// than a first node passed at the origin.
    /// </summary>
    [Fact]
    public void CameraWithNoPathDoesNotMove()
    {
        var registry = NewRegistry();

        var camera = Camera(registry, 0x00C4_0010u);

        Assert.True(camera.CurrentPathNode is null, "a camera with no moveto has no path");

        camera.Move(0.015f);

        Assert.True(camera.CurrentPathNode is null, "and moving one is harmless");
        Assert.True(camera.CurrentSpeed == 0f, "and leaves its speed alone");
    }

    //-------------------------------------------------------------------------
    // Which camera holds which player's view: the module's table, which was a
    // static dictionary on TriggerCamera.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheHoldersTableRemembersAndForgetsAPlayersCamera()
    {
        var registry = NewRegistry();
        var camera = Camera(registry, 0x00C1_0020u);
        var player = new EntityHandle(0x0001_0001u);
        var holders = new ViewControlHolders();

        holders.Hold(player, camera);
        Assert.True(holders.TryGet(player, out var held) && ReferenceEquals(held, camera),
                    "a held view names its camera");

        holders.Release(player);
        Assert.False(holders.TryGet(player, out _), "and a released one names none");
    }

    [Fact]
    public void TwoHoldersTablesDoNotShareAPlayer()
    {
        var registry = NewRegistry();
        var camera = Camera(registry, 0x00C1_0021u);
        var player = new EntityHandle(0x0001_0001u);
        var mine = new ViewControlHolders();
        var theirs = new ViewControlHolders();

        theirs.Hold(player, camera);

        Assert.False(mine.TryGet(player, out _), "another module's camera does not hold this module's player");
    }
}
