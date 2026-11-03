using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Server.Buildings;
using TF2.Server.Entities.Rooms;
using TF2.Server.Rules;
using TF2.Shared;

namespace TF2.Tests.Buildings;

/// <summary>Vector has no equality operator; the fakes compare componentwise.</summary>
internal static class RigVectors
{
    public static bool Same(Vector a, Vector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
}

/// <summary>The metal economy, as the purse seam answers: one pool, and the record of what it paid.</summary>
internal sealed class FakePurse : ITFAmmoPurse
{
    public Dictionary<string, int> Pockets { get; } = new() { [TFAmmoNames.Name(TFAmmo.Metal)] = 0 };
    public List<(EntityHandle Player, int Count, string Name)> Paid { get; } = new();

    public int TakeAmmo(EntityHandle player, int count, string ammoName)
    {
        int have = Pockets.GetValueOrDefault(ammoName);
        int taken = Math.Min(Math.Max(count, 0), Math.Max(have, 0));
        Pockets[ammoName] = have - taken;
        Paid.Add((player, taken, ammoName));
        return taken;
    }

    public IReadOnlyDictionary<string, int> Pools(EntityHandle player) => Pockets;
}

/// <summary>A func_nobuild that holds one point, or the whole world while told to.</summary>
internal sealed class FakeNoBuildAreas : ITFNoBuildAreas
{
    public bool Refuses { get; set; }
    public Vector? BlockedAt { get; set; }

    public bool PointInNoBuild(Vector origin, TFObjectType type, int team)
        => Refuses && (BlockedAt is null || RigVectors.Same(BlockedAt.Value, origin));
}

/// <summary>A respawn room that holds one point, or everywhere while told to,
/// and a visualizer the placement's far-edge ray may or may not cross.</summary>
internal sealed class FakeRespawnRooms : ITFRespawnRooms
{
    public bool InRoom { get; set; }
    public Vector? RoomAt { get; set; }

    /// <summary>When set, every asked far-edge segment crosses the enemy visualizer.</summary>
    public bool CrossesVisualizer { get; set; }

    /// <summary>The segments the placement has asked about, in order.</summary>
    public List<(Vector Start, Vector End)> VisualizerAsks { get; } = new();

    public bool InRespawnRoom(EntityHandle player, bool sameTeamOnly) => false;

    public bool PointInRespawnRoom(Vector point)
        => InRoom && (RoomAt is null || RigVectors.Same(RoomAt.Value, point));

    public bool PointInRespawnRoom(EntityHandle target, Vector origin, bool touchingSameTeamOnly)
        => InRoom && (RoomAt is null || RigVectors.Same(RoomAt.Value, origin));

    public bool PointsCrossRespawnRoomVisualizer(Vector start, Vector end, int teamToIgnore = 0)
    {
        VisualizerAsks.Add((start, end));
        return CrossesVisualizer;
    }

    public int TouchCount(EntityHandle player) => 0;
}

/// <summary>
/// The spawner as the registry does it: a live scripted instance per birth,
/// with the engine's placement recorded and the refused ones listed out.
/// </summary>
internal sealed class RigObjectSpawner : ITFObjectSpawner
{
    private readonly BuildingsRig _rig;
    private readonly IEngineEntities _entities;

    public RigObjectSpawner(BuildingsRig rig)
    {
        _rig = rig;
        _entities = rig.Engine;
    }

    public Dictionary<EntityHandle, Vector> Placed { get; } = new();
    public List<EntityHandle> Removed { get; } = new();

    public TFBaseObject? Spawn(TFObjectType type, Vector origin, Vector angles)
    {
        EntityHandle handle = _rig.NewHandle();
        _entities.SetOrigin(handle, origin);
        _entities.SetAngles(handle, angles);
        _rig.Registry.Spawn(handle, TFObjectClassNames.Raw(type));
        if (_rig.Registry.Get(handle) is not TFBaseObject building)
            return null;

        Placed[handle] = origin;
        return building;
    }

    public Vector CenterOf(TFBaseObject building) => _entities.GetWorldSpaceCenter(building.Handle);

    public Vector OriginOf(TFBaseObject building) => _entities.GetOrigin(building.Handle);

    public void Remove(TFBaseObject building)
    {
        Removed.Add(building.Handle);
        _entities.Remove(building.Handle);
    }
}

/// <summary>The arms the builder comes out on, as the asks recorded.</summary>
internal sealed class FakeBuilderArms : ITFBuilderArms
{
    public List<string> Asked { get; } = new();
    public SwitchOutcome Next { get; set; } = SwitchOutcome.Switched;
    public bool Grants { get; set; } = true;

    public SwitchOutcome Switch(EntityHandle player, string className)
    {
        Asked.Add($"switch {player} {className}");
        return Next;
    }

    public bool GiveWeapon(EntityHandle player, string className)
    {
        Asked.Add($"give {player} {className}");
        return Grants;
    }

    public SwitchOutcome SwitchLast(EntityHandle player)
    {
        Asked.Add($"last {player}");
        return Next;
    }
}

/// <summary>The build command's world: purse, spot refusals, spawner, and the subjects over them.</summary>
internal sealed partial class BuildingsRig
{
    public FakePurse Purse { get; } = new();
    public FakeNoBuildAreas NoBuild { get; } = new();
    public FakeRespawnRooms Rooms { get; } = new();
    public FakeBuilderArms BuilderArms { get; } = new();
    public RecordingLog Log { get; } = new();

    private RigObjectSpawner? _spawner;
    private bool _seamsEnsured;

    public RigObjectSpawner Spawner => _spawner ??= new RigObjectSpawner(this);

    /// <summary>When set, the world's hull walks find no ground and the corners none.</summary>
    public bool Groundless { get; set; }

    /// <summary>The rig's fakes step in where the real seams would answer the builder's asks.</summary>
    public void EnsureBuilderSeams()
    {
        if (_seamsEnsured)
            return;

        _seamsEnsured = true;
        Services.AddInstance<ITFAmmoPurse>(Purse);
        Services.AddInstance<ITFNoBuildAreas>(NoBuild);
        Services.AddInstance<ITFRespawnRooms>(Rooms);
        Services.AddInstance<ITFObjectSpawner>(Spawner);
    }

    /// <summary>The rig's one buildings directory, as the builder sees it.</summary>
    public TFBuildings Buildings() => (TFBuildings)Services.Resolve<ITFBuildings>()!;

    /// <summary>The real builder over the rig's fakes and the real buildings directory.</summary>
    public TFObjectBuilder Builder()
    {
        EnsureBuilderSeams();
        return new TFObjectBuilder(Players, Buildings(), NoBuild, Rooms, Purse, Spawner, Log);
    }

    /// <summary>The real lift over the rig's fakes; a round state is handed in when a fact needs one.</summary>
    public TFBuildingPickup Pickup(ITeamRoundState? rounds = null, ITFBuilderPlacement? builder = null)
        => new(Players, Buildings(), Engine, Engine, builder ?? Placement(), rounds, Carriers);

    /// <summary>
    /// The click-to-place half over the rig: the traces answer flat ground at
    /// z=0 (or nothing, when <see cref="Groundless"/>) until a fact overrides.
    /// </summary>
    public TFBuilderPlacement Placement(ITFObjectBuilder? builder = null, ITFBuilderArms? arms = null)
    {
        EnsureBuilderSeams();
        StubWorld();
        StubPlacementTraces();
        return new TFBuilderPlacement(Engine, Engine, Engine, NoBuild, Rooms, Buildings(),
                                      builder ?? Builder(), Purse, Spawner, arms ?? BuilderArms, Log);
    }

    /// <summary>
    /// The placement traces: the hull walk's box (brushes, its mins at z 0)
    /// sweeps to the ground plane; the corner lines hit flat; the fit and
    /// the sight hulls (the solid mask, and brushes at −2) find nothing.
    /// </summary>
    public void StubPlacementTraces()
    {
        Engine.ReturnsEachCall("WorldQuery.TraceBox", () =>
        {
            var call = Engine.Last("WorldQuery.TraceBox") ?? default;
            Vector start = call.ArgAs<Vector>(0);
            Vector mins = call.ArgAs<Vector>(2);
            var mask = call.ArgAs<TraceMask>(4);

            if (!Groundless && mask == TraceMask.Brushes && mins.Z == 0f)
                return new SurfaceTraceResult(true, 0.5f, new Vector(start.X, start.Y, 0f),
                                              new Vector(0f, 0f, 1f), EntityHandle.None, -1, 0);

            return new SurfaceTraceResult(false, 1f, call.ArgAs<Vector>(1), Vector.Zero, EntityHandle.None, -1, 0);
        });
        Engine.ReturnsEachCall("WorldQuery.TraceLineSurface", () =>
        {
            var call = Engine.Last("WorldQuery.TraceLineSurface") ?? default;
            Vector start = call.ArgAs<Vector>(0);

            return Groundless
                ? new SurfaceTraceResult(false, 1f, start, Vector.Zero, EntityHandle.None, -1, 0)
                : new SurfaceTraceResult(true, 0.1f, new Vector(start.X, start.Y, 0f),
                                         new Vector(0f, 0f, 1f), EntityHandle.None, -1, 0);
        });
    }

    /// <summary>Where the world was last told to put an entity (the walked blueprint among them).</summary>
    public (Vector Origin, Vector Angles) LastTeleport => Engine.Calls
        .Where(c => c.Member == "Entities.Teleport")
        .Select(c => (c.ArgAs<Vector>(1), c.ArgAs<Vector>(2)))
        .LastOrDefault();

    public int Teleports => Engine.Calls.Count(c => c.Member == "Entities.Teleport");

    /// <summary>The build command over the rig, over the real placement unless one is handed in.</summary>
    public TFBuildCommand BuildCommand(ITFBuilderPlacement? placement = null,
                                       TF2.Server.Rules.ITeamRoundState? round = null,
                                       TF2.Shared.ITFStalemateSettings? stalemate = null)
        => new(Players, Pickup(), placement ?? Placement(), Log, round, stalemate);

    public TFDestroyCommand DestroyCommand(ITFObjectBuilder? builder = null)
        => new(Players, builder ?? Builder(), Log);
}
