using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// func_useableladder and info_ladder_dismount on the server (stock's
/// CFuncLadder and CInfoLadderDismount, func_ladder.cpp): the mount points a
/// ladder settles at spawn, the dismount points it claims at activation, and
/// what the movement loop reads of it through <see cref="UseableLadders"/>.
/// </summary>
public sealed class UseableLadderEntityTests
{
    private static readonly EntityHandle LadderHandle = new(0x0001_0040u);

    private sealed class Rig
    {
        public RecordingEngine Engine = null!;
        public ScriptedEntityRegistry Registry = null!;
        public UseableLadders Ladders = null!;
        private uint _next = 0x0001_0100u;

        public static Rig Create()
        {
            var rig = new Rig();
            var ladders = new UseableLadders(ConVarRegistry.For(UnattachedConsole()), RecordingEngine.Default(),
                                             () => MoveConstantDefaults.Stock);
            var services = Services(out var engine, ladders);
            engine.Returns("Entities.IsValid", true);
            engine.Ignores("Entities.SetBounds", "Precache.PrecacheSound",
                           "Entities.GetName");

            // Every hull trace starts solid unless a fact says otherwise, so
            // the points stay where the map put them.
            engine.Returns("WorldQuery.EntityTraceHull", new EntityHullTrace(Vector.Zero, true, true));
            engine.Returns("Entities.GetOrigin", Vector.Zero);

            var registry = new ScriptedEntityRegistry(TestEngines.Server());
            registry.RegisterAll(typeof(FuncUseableLadder).Assembly);
            registry.Services = services;

            rig.Engine = engine;
            rig.Registry = registry;
            rig.Ladders = ladders;
            return rig;
        }

        public FuncUseableLadder Ladder(string point0, string point1, string name = "", EntityHandle? handle = null)
        {
            EntityHandle h = handle ?? LadderHandle;
            Registry.KeyValue(h, EntityNames.FuncUseableladder, "point0", point0);
            Registry.KeyValue(h, EntityNames.FuncUseableladder, "point1", point1);
            if (name.Length > 0)
                Registry.KeyValue(h, EntityNames.FuncUseableladder, "targetname", name);
            Registry.Spawn(h, EntityNames.FuncUseableladder);
            return (FuncUseableLadder)Registry.Get(h)!;
        }

        public InfoLadderDismount Dismount(Vector at, string target = "")
        {
            var h = new EntityHandle(_next++);
            Engine.Returns("Entities.GetOrigin", at);
            if (target.Length > 0)
                Registry.KeyValue(h, EntityNames.InfoLadderDismount, "target", target);
            Registry.Spawn(h, EntityNames.InfoLadderDismount);
            Engine.Returns("Entities.GetOrigin", Vector.Zero);
            return (InfoLadderDismount)Registry.Get(h)!;
        }
    }

    /// <summary>SetEndPoints: point0 is the top unless it is the lower one.</summary>
    [Fact]
    public void TheHigherPointIsTheTop()
    {
        var rig = Rig.Create();

        FuncUseableLadder ladder = rig.Ladder("0 0 0", "0 0 128");

        Assert.Equal(new Vector(0f, 0f, 128f), ladder.State.Top);
        Assert.Equal(Vector.Zero, ladder.State.Bottom);
    }

    /// <summary>
    /// Each point settles onto what is under it: a hull trace from four above
    /// that does not start solid moves the point to where it ended.
    /// </summary>
    [Fact]
    public void EachPointSettlesWhereTheTraceDownEnds()
    {
        var rig = Rig.Create();
        rig.Engine.Returns("WorldQuery.EntityTraceHull", new EntityHullTrace(new Vector(0f, 0f, 2f), false, false));

        FuncUseableLadder ladder = rig.Ladder("0 0 128", "0 0 0");

        Assert.Equal(new Vector(0f, 0f, 2f), ladder.State.Top);
        Assert.Equal(new Vector(0f, 0f, 2f), ladder.State.Bottom);

        EngineCall first = rig.Engine.AllOf("WorldQuery.EntityTraceHull").First();
        Assert.Equal(new Vector(0f, 0f, 132f), first.ArgAs<Vector>(1));   // from four above the top
        Assert.Equal(new Vector(0f, 0f, 128f), first.ArgAs<Vector>(2));
    }

    /// <summary>Spawnflag 1 is SF_LADDER_DONTGETON.</summary>
    [Fact]
    public void SpawnFlagOneIsAFakeLadder()
    {
        var rig = Rig.Create();
        rig.Engine.Returns("Entities.GetSpawnFlags", 1);

        FuncUseableLadder ladder = rig.Ladder("0 0 0", "0 0 128");

        Assert.True(ladder.State.Fake);
    }

    /// <summary>
    /// Activation claims the dismount points within 100 of the top and the
    /// bottom, and not one further away.
    /// </summary>
    [Fact]
    public void ActivationClaimsThePointsNearItsEnds()
    {
        var rig = Rig.Create();
        FuncUseableLadder ladder = rig.Ladder("0 0 0", "0 0 128");
        InfoLadderDismount near = rig.Dismount(new Vector(0f, 40f, 129f));
        rig.Dismount(new Vector(500f, 0f, 0f));

        ladder.OnServerActivate();

        Assert.Equal(new[] { near }, ladder.DismountPoints);
        Assert.Equal(new[] { new Vector(0f, 40f, 129f) }, ladder.DismountOrigins);
    }

    /// <summary>
    /// A long ladder probes every 40 units up it: a point 90 off the middle of
    /// a 400-unit ladder is over 100 from both ends and is still claimed.
    /// </summary>
    [Fact]
    public void ALongLadderClaimsPointsAlongItsLength()
    {
        var rig = Rig.Create();
        FuncUseableLadder ladder = rig.Ladder("0 0 0", "0 0 400");
        InfoLadderDismount middle = rig.Dismount(new Vector(90f, 0f, 200f));

        ladder.OnServerActivate();

        Assert.Equal(new[] { middle }, ladder.DismountPoints);
    }

    /// <summary>
    /// A point with a target is claimed only by the ladder it names: the
    /// point naming another ladder is left out, the one naming this is taken.
    /// </summary>
    [Fact]
    public void APointWithATargetBelongsToTheLadderItNames()
    {
        var rig = Rig.Create();
        FuncUseableLadder ladder = rig.Ladder("0 0 0", "0 0 128", name: "ladder_a");
        rig.Ladder("1000 0 0", "1000 0 128", name: "ladder_b", handle: new EntityHandle(0x0001_0041u));
        InfoLadderDismount mine = rig.Dismount(new Vector(0f, 40f, 0f), target: "ladder_a");
        rig.Dismount(new Vector(0f, -40f, 0f), target: "ladder_b");

        ladder.OnServerActivate();

        Assert.Equal(new[] { mine }, ladder.DismountPoints);
    }

    /// <summary>The Disable and Enable inputs move the networked flag the loop reads as Enabled.</summary>
    [Fact]
    public void DisableAndEnableAreWhatTheLoopSees()
    {
        var rig = Rig.Create();
        rig.Ladder("0 0 0", "0 0 128");

        rig.Registry.Input(LadderHandle, "Disable", "", 0f, EntityHandle.None);
        Assert.False(rig.Ladders.At(0).Enabled);

        rig.Registry.Input(LadderHandle, "Enable", "", 0f, EntityHandle.None);
        Assert.True(rig.Ladders.At(0).Enabled);
    }

    /// <summary>
    /// A hot reload rebuilds the ladder and its points in a new composition:
    /// both join that composition's ladders, and the rebuilt ladder finds its
    /// dismount points when the loop first asks (activation does not run again).
    /// </summary>
    [Fact]
    public void AReloadedLadderJoinsTheNewLaddersWithItsPoints()
    {
        var rig = Rig.Create();
        rig.Ladder("0 0 0", "0 0 128");
        rig.Dismount(new Vector(0f, 40f, 129f));
        var store = new ReloadStore(Side.Server);
        rig.Registry.SaveForReload(store);

        var rebuilt = Rig.Create();
        Assert.Equal(2, rebuilt.Registry.RestoreAfterReload(store));

        Assert.Equal(1, rebuilt.Ladders.Count);
        Assert.Equal(new[] { new Vector(0f, 40f, 129f) }, rebuilt.Ladders.Dismounts(LadderHandle.EntryIndex));
    }

    /// <summary>The loop finds a ladder by its entity index, with its two points.</summary>
    [Fact]
    public void TheLoopFindsALadderByEntityIndex()
    {
        var rig = Rig.Create();
        rig.Ladder("0 0 0", "0 0 128");

        Assert.True(rig.Ladders.TryGet(LadderHandle.EntryIndex, out UseableLadder found));
        Assert.Equal(new Vector(0f, 0f, 128f), found.Top);
        Assert.False(rig.Ladders.TryGet(LadderHandle.EntryIndex + 1, out _));
    }
}
