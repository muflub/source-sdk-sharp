using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A pickup kind, defined entirely in C#.
///
/// This is the first actual gameplay content the mod has: stripping HL2MP left
/// it with no weapons and no items, and rather than porting C++ item classes
/// back in, an item is now a managed description plus one generic native shell
/// entity.
/// </summary>
public sealed record PickupKind(
    string Name,
    string Model,
    int    HealthGiven,
    float  RespawnDelay)
{
    /// <summary>Half-extents of the trigger a player walks into.</summary>
    public float SizeX { get; init; } = 16f;
    public float SizeY { get; init; } = 16f;
    public float SizeZ { get; init; } = 32f;
}

/// <summary>
/// Spawns and manages pickups.
///
/// Every pickup in the world is the same entity class (<see cref="SsPickup"/>,
/// on the standalone shell) wearing a different model and carrying a different
/// managed description — so adding an item kind is a C# change with no native
/// work at all, which is the whole point.
/// </summary>
public sealed class Pickups : IPickupDirectory, IGameLifecycle, IFrameLifecycle, ISaveCustom
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    // Entities do not survive a map change, so the pickup bookkeeping goes with
    // them or it refers to handles that no longer exist.
    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.Pickups;

    void IGameLifecycle.OnLevelInit(string mapName)
    {
        Reset();

        // Re-arm the first-frame placement. Without this the SECOND map of a
        // session gets no pickups at all: the flag latches on the first frame
        // and nothing else clears it. ServerModule cleared it from LevelInit
        // when the flag lived there, and it has to keep being cleared now that
        // it lives here.
        _placed = false;
    }

    /// <summary>The kinds this mod defines.</summary>
    public static readonly PickupKind Health = new(
        Name: "health",
        Model: "models/items/healthkit.mdl",
        HealthGiven: 25,
        RespawnDelay: 20f);

    public static readonly ImmutableArray<PickupKind> Kinds = [ Health ];

    private readonly IEngineEntities _entities;
    private readonly IEnginePlayers _players;
    private readonly IEngineEntityRender _render;

    /// <summary>
    /// A SERVICE takes its engine by constructor, unlike an entity, which takes
    /// it by property. There is no hierarchy here to cascade a parameter
    /// through, and being unable to construct this without an engine is the
    /// right outcome.
    /// </summary>
    public Pickups(ILog log, IEngineEntities entities, IEnginePlayers players, IEngineEntityRender render,
                   IGameClock clock)
    {
        _log = log;
        _entities = entities;
        _players = players;
        _render = render;
        _clock = clock;
    }

    private readonly IGameClock _clock;

    /// <summary>
    /// Whether the map's own pickups have been placed yet.
    ///
    /// Placement happens on the FIRST FRAME rather than at level init, and that
    /// is not a preference: the map's entities do not exist yet when LevelInit
    /// runs, so iterating spawn points there finds nothing.
    /// </summary>
    private bool _placed;

    // Last of the three former OnGameRulesThink lines.
    int IFrameLifecycle.FrameOrder => (int)FrameStage.Pickups;

    void IFrameLifecycle.OnThink(float frameTime)
    {
        if (!_placed)
        {
            _placed = true;
            PopulateFromSpawnPoints();
        }

        Think(_clock.Now);
    }


    /// <summary>Live pickups, by entity handle.</summary>
    private readonly Dictionary<uint, PickupKind> _live = new();

    /// <summary>
    /// Test seam: tracks a pickup as live without an engine to create the
    /// entity, because <see cref="Spawn"/> validates the created handle
    /// against the real engine and a test process has none. What the tests
    /// need this for is the TOUCH rules, which never look at the entity.
    /// </summary>
    internal void TrackForTest(EntityHandle entity, PickupKind kind)
        => _live[entity.Value] = kind;

    /// <summary>Pickups waiting to come back, and when.</summary>
    private readonly List<(PickupKind Kind, Vector Origin, float DueAt)> _pending = new();

    //-------------------------------------------------------------------------
    // SURVIVING A HOT RELOAD -- and this is THE ten-entity leak, found and named.
    //
    // `_placed` is what stops the level's spawn points being populated twice.
    // It is a field of this service, and this service is destroyed by the
    // unload -- so after cs_sv_reload it reads false again, the first frame
    // calls PopulateFromSpawnPoints a SECOND time, and dm_lockdown gains ten
    // ss_pickup it already had. Measured: 473 entities before the reload, 483
    // after, ten of one class, with the live gate green.
    //
    // tools/live-gate's hot-reload step has carried a comment about that leak
    // and asserts `entities >= before` rather than `==` BECAUSE of it. This is
    // the defect the tolerance existed for.
    //
    // `_live` and `_pending` go the same way and matter for the same reason a
    // count does not show: a collected kit's respawn timer is in `_pending`, so
    // a reload between collection and respawn loses the kit for the rest of the
    // map, and `_live` is what OnTouched consults -- every pickup standing in
    // the world stops being collectable at all.
    //-------------------------------------------------------------------------

    private const string RowKind = "kind";
    private const string RowLive = "live";
    private const string RowPending = "pending";

    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        writer.Write("placed", _placed);

        foreach (var (handle, kind) in _live)
        {
            writer.AddRow()
                  .Set(RowKind, RowLive)
                  .Set("e", new EntityHandle(handle))
                  .Set("k", kind.Name);
        }

        // AS SECONDS REMAINING, never as an absolute due time: the reload does
        // not stop the clock, so an absolute time written before the unload is
        // in the past by the time it is read and every waiting pickup would
        // respawn at once.
        float now = _clock.Now;

        foreach (var (kind, origin, dueAt) in _pending)
        {
            writer.AddRow()
                  .Set(RowKind, RowPending)
                  .Set("k", kind.Name)
                  .Set("o", origin)
                  .Set("d", dueAt - now);
        }
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        // THE FLAG FIRST AND ON ITS OWN. Even a record with no rows has to put
        // this back, because a reload with every pickup already collected still
        // must not re-place the map's.
        _placed = reader.ReadBool("placed", _placed);

        float now = _clock.Now;

        foreach (ReloadRow row in reader.Rows)
        {
            PickupKind? kind = KindNamed(row.GetString("k"));

            // A KIND THIS BUILD NO LONGER DEFINES is dropped and named -- the
            // same arm a map key a build stopped declaring takes. Silently
            // dropping it would leave an ss_pickup standing in the world that
            // nothing can collect.
            if (kind is null)
            {
                _log.Warning($"[SourceSharp/Pickup] reload: this build no longer defines the " +
                            $"pickup kind '{row.GetString("k")}'; that row is dropped\n");
                continue;
            }

            switch (row.GetString(RowKind))
            {
                case RowLive:
                {
                    EntityHandle entity = reader.Resolve(row.GetHandle("e"));
                    if (entity != EntityHandle.None)
                        _live[entity.Value] = kind;
                    break;
                }

                case RowPending:
                {
                    float delay = row.GetFloat("d");
                    _pending.Add((kind, row.GetVector("o"), now + (delay < 0f ? 0f : delay)));
                    break;
                }
            }
        }
    }

    /// <summary>The kind with this name, or null. Case-insensitive, as a map key is.</summary>
    private static PickupKind? KindNamed(string name)
    {
        foreach (PickupKind kind in Kinds)
        {
            if (string.Equals(kind.Name, name, System.StringComparison.OrdinalIgnoreCase))
                return kind;
        }

        return null;
    }

    public int LiveCount => _live.Count;
    public int PendingCount => _pending.Count;

    /// <summary>
    /// Spawns a pickup. Returns the handle, or None if the entity could not be
    /// created — which is what happens when the class is not registered.
    /// </summary>
    public EntityHandle Spawn(PickupKind kind, Vector origin)
    {
        // ss_pickup, the standalone shell — NOT sourcesharp_netentity, which
        // these rode on for as long as they existed. That shell networks no
        // origin, no angles and no model index, so every pickup worked and was
        // invisible; the fixture's transport was never a drawable half. See
        // SsPickup for the entity-shaped end of this service.
        var entity = _entities.Create(EntityNames.SsPickup);
        if (!_entities.IsValid(entity))
        {
            _log.Warning($"[SourceSharp/Pickup] could not create a '{kind.Name}' pickup\n");
            return EntityHandle.None;
        }

        // TRACKED BEFORE SPAWN, because spawning is what binds the managed
        // SsPickup instance, and the first thing that instance may hear is a
        // touch from a player already standing on the spawn point.
        _live[entity.Value] = kind;

        // Model before MakePickup: the collision bounds are set explicitly
        // afterwards, so a model that changes them cannot undo the trigger.
        _render.SetModel(entity, kind.Model);
        _entities.SetOrigin(entity, origin);

        // Spawned, not merely created. Between the origin and MakePickup on
        // purpose: before MakePickup, so the trigger solidity and bounds are
        // the last word over anything spawn does to collision; after the
        // origin, because spawning is what reads what was set beforehand,
        // which is the whole reason creation and spawning are two calls.
        _entities.Spawn(entity);

        _players.MakePickup(entity, kind.SizeX, kind.SizeY, kind.SizeZ);

        _log.Msg($"[SourceSharp/Pickup] spawned '{kind.Name}' at {origin}\n");
        return entity;
    }

    /// <summary>
    /// A player walked into a pickup. Returns true if it was collected.
    ///
    /// Collecting removes the entity and schedules its return, so the same
    /// pickup cannot be taken twice by a player standing in it — the touch fires
    /// repeatedly while they remain inside the trigger.
    /// </summary>
    public bool OnTouched(EntityHandle entity, EntityHandle other, float now)
    {
        if (!_live.TryGetValue(entity.Value, out var kind))
            return false;

        // A dead player collects nothing. Seen live: a roaming dead observer
        // drifted over a spawn point's health kit and "took 'health':
        // -150 -> -125" — a corpse being topped up, and the kit gone for
        // whoever spawned there.
        if (!_players.IsAlive(other))
            return false;

        int health = _entities.GetHealth(other);

        // Nothing to give: leave the pickup in the world rather than consuming
        // it for no benefit, which is how every Source health item behaves.
        if (health >= 100)
            return false;

        _entities.SetHealth(other, Math.Min(100, health + kind.HealthGiven));

        // Removed from the map BEFORE the entity goes away, so a second touch in
        // the same frame finds nothing to collect.
        _live.Remove(entity.Value);

        var origin = _entities.GetOrigin(entity);
        _entities.Remove(entity);

        if (kind.RespawnDelay > 0f)
            _pending.Add((kind, origin, now + kind.RespawnDelay));

        _log.Msg($"[SourceSharp/Pickup] {other} took '{kind.Name}': " +
                   $"{health} -> {_entities.GetHealth(other)} health\n");
        return true;
    }

    /// <summary>
    /// Respawns anything whose timer has elapsed. Called from the rules tick, so
    /// it returns immediately when nothing is pending.
    /// </summary>
    public void Think(float now)
    {
        if (_pending.Count == 0)
            return;

        // Reverse iteration so removing an entry cannot skip the next one.
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (now < _pending[i].DueAt)
                continue;

            var (kind, origin, _) = _pending[i];
            _pending.RemoveAt(i);
            Spawn(kind, origin);
        }
    }

    /// <summary>
    /// The player hull's half-width in X and Y.
    ///
    /// Quoted, not chosen: VEC_HULL_MIN/VEC_HULL_MAX are (-16,-16,0)/(16,16,72)
    /// in the view vectors both hierarchies read
    /// (<c>shell_gamerules.cpp</c>'s <c>g_DefaultViewVectors</c>, which is
    /// stock's table copied), and <c>CStandalonePlayer::Spawn</c> sets exactly
    /// those bounds. A player standing on a spawn point fills this much of it.
    /// </summary>
    public const float PlayerHalfWidth = 16f;

    /// <summary>
    /// How far from a spawn point its pickup is placed, along the point's own
    /// facing.
    ///
    /// <para>
    /// DERIVED, AND THE DERIVATION IS THE WHOLE POINT. The pickup's trigger is
    /// an axis-aligned box of <see cref="PickupKind.SizeX"/> by
    /// <see cref="PickupKind.SizeY"/> half-extents; the spawning player is an
    /// axis-aligned box of <see cref="PlayerHalfWidth"/>. Two such boxes miss
    /// each other when they are separated by the sum on EITHER axis — 32 units
    /// for the sizes this mod uses — and a facing at 45° splits the offset
    /// evenly, so the shortest offset that clears every yaw is 32·√2 ≈ 45.3.
    /// 48 is that with a little room.
    /// </para>
    ///
    /// <para>
    /// WHY THERE IS AN OFFSET AT ALL. There was not one, and the pickup went
    /// eight units directly above the spawn point — inside the player who
    /// spawned there: the trigger stands 32 units up from where it is put and
    /// the player 72, so the two boxes shared every axis.
    /// At full health nothing happens, because a health pickup with nothing to
    /// give is left in the world; but the FIRST damage a player takes while
    /// standing on their own spawn is undone in the same instant, and again
    /// every <see cref="PickupKind.RespawnDelay"/> seconds for as long as they
    /// stand there. Measured on dm_lockdown: hit for 17, the reply reads
    /// health 100 -&gt; 83, and the next line of the log is
    /// "player#1 took 'health': 83 -&gt; 100 health".
    /// </para>
    /// </summary>
    public const float ClearanceFromSpawn = 48f;

    /// <summary>
    /// Where a spawn point's pickup goes: <paramref name="heightAboveSpawn"/>
    /// up, and <see cref="ClearanceFromSpawn"/> along the way the point faces.
    ///
    /// <para>
    /// ALONG THE FACING rather than along an axis, because a spawn point's
    /// angles are where the player will be LOOKING when they arrive — the
    /// shell adopts them as the view angle — so a mapper has already pointed
    /// them at the open part of the room. An arbitrary axis would put a
    /// share of the map's pickups inside walls.
    /// </para>
    ///
    /// <para>
    /// FLATTENED to the XY plane, and renormalised. A spawn point with pitch
    /// would otherwise spend its offset going up or down, which buys none of
    /// the horizontal separation this exists for. A facing that is straight up
    /// or down has no direction left to flatten, so it falls back to +X: some
    /// direction is required, and none of them is better than the others.
    /// </para>
    ///
    /// <para>
    /// Pure, and public, so the geometry can be held by a fact rather than by
    /// a game nobody can run in CI.
    /// </para>
    /// </summary>
    public static Vector PlacementNear(Vector spawnOrigin, Vector spawnAngles,
                                       float heightAboveSpawn = 8f)
    {
        var forward = Angles.Forward(spawnAngles);

        float x = forward.X, y = forward.Y;
        float length = MathF.Sqrt((x * x) + (y * y));

        if (length < 1e-4f)
        {
            x = 1f;
            y = 0f;
            length = 1f;
        }

        return new Vector(spawnOrigin.X + (x / length * ClearanceFromSpawn),
                          spawnOrigin.Y + (y / length * ClearanceFromSpawn),
                          spawnOrigin.Z + heightAboveSpawn);
    }

    /// <summary>
    /// Places pickups around the map, one near each spawn point.
    ///
    /// Spawn points are what this map actually has: it was built for HL2MP and
    /// its item entities are classes this mod does not define, so there is
    /// nothing else to key off. Placing on spawn points is a real design choice
    /// a deathmatch mod could make, not a stand-in — and it means a map needs no
    /// special authoring to work with this mod.
    ///
    /// NEAR, and <see cref="PlacementNear"/> is what makes that word true: a
    /// pickup placed ON the spawn point is a pickup inside whoever spawns
    /// there.
    ///
    /// Returns how many were placed.
    /// </summary>
    public int PopulateFromSpawnPoints(float heightAboveSpawn = 8f)
    {
        int placed = 0;

        foreach (string className in SpawnPointClasses)
        {
            foreach (var spawnPoint in _entities.FindAllByClassname(className))
            {
                Spawn(Health, PlacementNear(_entities.GetOrigin(spawnPoint),
                                            _entities.GetAngles(spawnPoint),
                                            heightAboveSpawn));
                placed++;
            }
        }

        _log.Msg($"[SourceSharp/Pickup] placed {placed} pickup(s) near spawn points\n");
        return placed;
    }

    /// <summary>
    /// Where players enter the map. Both are checked because a map may use
    /// either, and a map with neither simply gets no pickups.
    /// </summary>
    public static readonly ImmutableArray<string> SpawnPointClasses =
    [
        EntityNames.InfoPlayerDeathmatch,
        EntityNames.InfoPlayerStart,
    ];

    /// <summary>Forgets everything. Called at level init, since entities do not survive a map change.</summary>
    public void Reset()
    {
        _live.Clear();
        _pending.Clear();
    }

    /// <summary>Test seam: whether a handle is a live pickup.</summary>
    public bool IsLive(EntityHandle entity) => _live.ContainsKey(entity.Value);
}

/// <summary>
/// Spawns a pickup where the caller is standing, for trying it out.
///
/// <para>
/// The bookkeeping arrives in the constructor. It used to be a static property
/// the module assigned during init — which worked, and hid a real failure mode:
/// a build where that assignment was missed registered the command, accepted it
/// from the console, and did nothing at all, because a null Owner and a
/// successful spawn look identical from the far side of a null-conditional. A
/// constructor parameter cannot be forgotten; the command is refused at load
/// with a message naming what it wanted.
/// </para>
/// </summary>
[ConCommand("cs_sv_spawn_pickup", Help = "Spawn a health pickup at the first player's feet")]
public sealed class SpawnPickupCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IPickupDirectory _pickups;
    private readonly IEnginePlayers _players;

    private readonly IEngineEntities _entities;

    public SpawnPickupCommand(ILog log, IPickupDirectory pickups, IEnginePlayers players, IEngineEntities entities)
    {

        _log = log;
        _pickups = pickups;
        _players = players;
        _entities = entities;
    }

    public void Execute(string args)
    {
        var player = _players.GetHandle(1);
        if (!_entities.IsValid(player))
        {
            _log.Warning("[SourceSharp/Pickup] no player to spawn a pickup near\n");
            return;
        }

        _pickups.Spawn(Pickups.Health, _entities.GetOrigin(player));
    }
}
