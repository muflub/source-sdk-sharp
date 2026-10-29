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

    void IGameLifecycle.OnLevelInit(string mapName) => Reset();

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

    // Last of the three former OnGameRulesThink lines.
    int IFrameLifecycle.FrameOrder => (int)FrameStage.Pickups;

    void IFrameLifecycle.OnThink(float frameTime) => Think(_clock.Now);


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
    // SURVIVING A HOT RELOAD.
    //
    // `_live` and `_pending` are fields of this service, which the unload
    // destroys, and they matter for a reason a count does not show: a
    // collected kit's respawn timer is in `_pending`, so
    // a reload between collection and respawn loses the kit for the rest of the
    // map, and `_live` is what OnTouched consults -- every pickup standing in
    // the world stops being collectable at all.
    //-------------------------------------------------------------------------

    private const string RowKind = "kind";
    private const string RowLive = "live";
    private const string RowPending = "pending";

    void ISaveCustom.OnSave(ISaveWriter writer)
    {
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
