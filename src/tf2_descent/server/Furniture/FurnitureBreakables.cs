using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Shared.Bones;

using TF2.Descent.Rules.Furniture;
using TF2.Descent.Shared;
using TF2.Server.Projectiles;

using NVector = System.Numerics.Vector3;

namespace TF2.Descent.Server.Furniture;

/// <summary>
/// The crates and barrels of a level, owned by ONE entity (the user, 2026-11-15: "a single breakable
/// prop manager as an entity that owns all the props on the level and their health + collision and
/// handles the task of breaking them and keeping it in sync with server"). At level ready, after the
/// furniture has its rows, the breakable rows (at most <see cref="BreakablesNetState.Max"/>) are the
/// entity's slots: their place, facing and model go out once with it, a broken bit each after.
/// A hit the entity takes is the nearest live slot's (<see cref="HitAt"/>), TF2's blast is each slot's
/// it reaches and sees (<see cref="Blast"/>); either strikes the slot's row
/// (<see cref="FurnitureService.Strike"/>), which breaks it and rolls, and the slot's bit is set at once,
/// so it stops a trace no more.
/// </summary>
[SideService(Side.Server)]
public sealed class FurnitureBreakables(FurnitureService furniture, IFurnitureHeroes heroes, IBreakablesSpawner spawner, IFurnitureWorld world, ILog log)
    : IGameLifecycle, IFrameLifecycle
{
    /// <summary>After the furniture (First + 850), whose rows it reads, in its block (+851 to +853 taken).</summary>
    public const int Order = ModStages.First + 855;

    /// <summary>How far from a hit point a live prop's box may lie and still be the one struck.</summary>
    public const float HitSlack = 8f;

    private readonly List<BreakableSpot> _props = [];
    private readonly List<FurnitureRow> _rows = [];
    private readonly HashSet<int> _broken = [];

    public int LifecycleOrder => Order;

    public int FrameOrder => Order;

    /// <summary>The level's entity; null when it has no crate or barrel (or no level).</summary>
    public BreakablesEntity? Entity { get; private set; }

    /// <summary>Every slot's prop, in slot order.</summary>
    public IReadOnlyList<BreakableSpot> Props => _props;

    /// <summary>The row each slot stands for.</summary>
    public IReadOnlyList<FurnitureRow> Rows => _rows;

    /// <summary>Crate and barrel rows past the entity's slots: left out of this level.</summary>
    public int Dropped { get; private set; }

    /// <summary>The world box all the live props lie in, as the entity's bounds were set.</summary>
    public (NVector Mins, NVector Maxs) Bounds { get; private set; }

    public bool IsBroken(int slot) => _broken.Contains(slot);

    public void OnLevelReady(string mapName)
    {
        Forget();
        if (!furniture.Ready)
            return;
        var rows = furniture.Rows.Where(r => r.Kind == FurnitureKind.Breakable).ToList();
        if (rows.Count == 0)
            return;
        Dropped = Math.Max(0, rows.Count - BreakablesNetState.Max);
        if (Dropped > 0)
            log.Warning($"[TF2Descent/Breakables] {rows.Count} crates and barrels, the entity holds {BreakablesNetState.Max}: {Dropped} left out\n");
        foreach (var row in rows.Take(BreakablesNetState.Max))
        {
            byte entry = FurnitureCatalog.Number(FurnitureKindId.Breakable, row.Variant);
            row.BreakableSlot = _props.Count;   // its number to the host (SDK 2.8 Break): the slot index, stable for a level copy
            _props.Add(BreakableGeometry.OnTheWire(_props.Count, entry, new NVector(row.Position.X, row.Position.Y, row.Position.Z), row.Yaw));
            _rows.Add(row);
        }

        if (spawner.Make() is not { } e)
        {
            log.Warning($"[TF2Descent/Breakables] the engine refused the breakables' entity: {_props.Count} crates and barrels stand for nothing\n");
            return;
        }

        Entity = e;
        Bounds = BoxOf(_props);
        e.Hold(this, _props, Bounds);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Breakables] level ready: {_props.Count} crates and barrels ({_props.Count(p => FurnitureCatalog.Of(p.Entry)?.Variant == "crate")} crates) on entity #{e.Handle.EntryIndex}, " +
            $"one edict, bounds ({Bounds.Mins.X:0} {Bounds.Mins.Y:0} {Bounds.Mins.Z:0}) to ({Bounds.Maxs.X:0} {Bounds.Maxs.Y:0} {Bounds.Maxs.Z:0})\n"));
    }

    public void OnLevelShutdownPreEntity() => Forget();

    private void Forget()
    {
        Entity = null;
        _props.Clear();
        _rows.Clear();
        _broken.Clear();
        Dropped = 0;
        Bounds = default;
    }

    /// <summary>Each slot whose row broke or is breaking (a hit through any path, the dev command) gets its bit.</summary>
    public void OnThink(float frameTime)
    {
        for (int slot = 0; slot < _rows.Count; slot++)
            if (_rows[slot].Gone && !_broken.Contains(slot))
                MarkBroken(slot);
    }

    private void MarkBroken(int slot)
    {
        _broken.Add(slot);
        Entity?.SetBroken(slot);
    }

    /// <summary>The box every prop lies in.</summary>
    public static (NVector Mins, NVector Maxs) BoxOf(IReadOnlyList<BreakableSpot> props)
    {
        var mins = new NVector(float.MaxValue);
        var maxs = new NVector(float.MinValue);
        foreach (var p in props)
        {
            var (lo, hi) = BreakableGeometry.WorldBox(p);
            mins = NVector.Min(mins, lo);
            maxs = NVector.Max(maxs, hi);
        }

        return (mins, maxs);
    }

    /// <summary>The live slot a hit at <paramref name="at"/> struck: the nearest whose box is within <see cref="HitSlack"/> of it; -1 for none.</summary>
    public int SlotAt(NVector at)
    {
        int best = -1;
        float bestDistance = HitSlack;
        foreach (var p in _props)
        {
            if (_broken.Contains(p.Slot))
                continue;
            float d = DistanceToBox(at, BreakableGeometry.WorldBox(p));
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = p.Slot;
            }
        }

        return best;
    }

    private static float DistanceToBox(NVector p, (NVector Mins, NVector Maxs) box) =>
        NVector.Distance(p, NVector.Clamp(p, box.Mins, box.Maxs));

    /// <summary>A hit of <paramref name="damage"/> and <paramref name="force"/> by <paramref name="attacker"/> at <paramref name="at"/> on the entity: the slot it struck breaks. Answers the slot, or -1.</summary>
    public int HitAt(NVector at, EntityHandle attacker, float damage, Vector force = default)
    {
        int slot = SlotAt(at);
        if (slot >= 0)
            Strike(slot, attacker, damage, force);
        return slot;
    }

    /// <summary>
    /// A blast's push on a prop, stock's <c>CalculateExplosiveDamageForce</c> (less its random
    /// fudge): along the line from <paramref name="source"/> to <paramref name="target"/>, 75 kg at
    /// 4 in/s for each point of <paramref name="damage"/>, and never more than 75 kg at 400 in/s.
    /// </summary>
    public static Vector BlastForce(NVector source, NVector target, float damage)
    {
        var line = target - source;
        float length = line.Length();
        if (!(length > 0f))
            return default;
        float scale = Math.Min(damage * DamageForceRules.PerDamagePoint, DamageForceRules.ExplosionForceCeiling) / length;
        return new Vector(line.X * scale, line.Y * scale, line.Z * scale);
    }

    /// <summary>
    /// TF2's blast from <paramref name="source"/>: every live slot whose box lies within the blast's
    /// radius and whose middle the blast sees (no world brush between) breaks, set off by
    /// <paramref name="attacker"/>. Answers how many.
    /// </summary>
    public int Blast(NVector source, float radius, float damage, EntityHandle attacker)
    {
        int broke = 0;
        var from = new Vector(source.X, source.Y, source.Z);
        foreach (var p in _props.ToList())
        {
            if (_broken.Contains(p.Slot))
                continue;
            var box = BreakableGeometry.WorldBox(p);
            if (DistanceToBox(source, box) > radius)
                continue;
            var mid = (box.Mins + box.Maxs) * 0.5f;
            if (!world.Clear(from, new Vector(mid.X, mid.Y, mid.Z)))
                continue;
            if (Strike(p.Slot, attacker, damage, BlastForce(source, mid, damage)))
                broke++;
        }

        return broke;
    }

    /// <summary>Slot <paramref name="slot"/>'s row is struck (<see cref="FurnitureService.Strike"/>) by the hero <paramref name="attacker"/> is, if any, with the hit's push; its bit is set at once.</summary>
    public bool Strike(int slot, EntityHandle attacker, float damage, Vector force = default)
    {
        if (slot < 0 || slot >= _rows.Count || _broken.Contains(slot))
            return false;
        var hero = heroes.Of(attacker)?.Player ?? EntityHandle.None;
        if (!furniture.Strike(_rows[slot], hero, damage, Entity?.Handle ?? EntityHandle.None, force))
            return false;
        MarkBroken(slot);
        return true;
    }

    /// <summary>The slots as lines (the dev command).</summary>
    public string Report()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"[TF2Descent/Breakables] {_props.Count} crates and barrels on {(Entity is { } e ? "entity #" + e.Handle.EntryIndex : "no entity")}, {_broken.Count} broken, {Dropped} left out\n");
        foreach (var p in _props)
        {
            var row = _rows[p.Slot];
            sb.Append(CultureInfo.InvariantCulture,
                $"[TF2Descent/Breakables]   slot {p.Slot} row #{row.Index} {row.Variant} on {row.Placed.Marker} at ({p.Foot.X:0} {p.Foot.Y:0} {p.Foot.Z:0}) yaw {p.Yaw:0} room {row.Room}: {row.StateText()}\n");
        }

        return sb.ToString();
    }
}

/// <summary>Makes the breakables' one entity.</summary>
public interface IBreakablesSpawner
{
    /// <summary>A <c>tf2_descent_breakables</c>, or null when the engine refuses one.</summary>
    BreakablesEntity? Make();
}

/// <summary>The engine's create, spawn and directory.</summary>
[SideService(Side.Server, As = [typeof(IBreakablesSpawner)])]
public sealed class EngineBreakablesSpawner(IEngineEntities entities, IEntityDirectory directory) : IBreakablesSpawner
{
    public BreakablesEntity? Make()
    {
        var h = entities.Create(BreakablesNetState.ClassName);
        if (h == EntityHandle.None)
            return null;

        entities.Spawn(h);
        if (directory.Get(h) is BreakablesEntity made)
            return made;

        entities.Remove(h);
        return null;
    }
}

/// <summary>
/// <c>tf2_descent_breakables</c>: the one entity that stands for a level's crates and barrels. Not
/// drawn itself (each client draws the props from its state); solid over the box all of them lie in,
/// with custom ray and box tests, so every trace and step against that box asks it
/// (<see cref="ICustomTraceTarget"/>: the live props' boxes, the slot as the hitbox); it takes a hit
/// as the nearest prop's and TF2's blast whole (<see cref="ITFBlastReceiver"/>), and it is sent to
/// every client always.
/// </summary>
[ScriptedEntity(BreakablesNetState.ClassName, ScriptedEntityKind.Item)]
public sealed class BreakablesEntity(IEngineEntityRender render) : ScriptedEntity, ICustomTraceTarget, ITFBlastReceiver
{
    [NetState] public BreakablesNetState State = new();

    private FurnitureBreakables? _owner;
    private IReadOnlyList<BreakableSpot> _props = [];

    public override bool SpawnsPhysics => false;

    public override bool TouchEveryTick => false;

    public override bool TakesDamage => _owner is not null;

    public override void OnSpawn()
    {
        render.SetTransmitFullCheck(Handle, true);
        SetVisible(false);
        SetSolidity(SolidType.None, SolidFlags.None);
    }

    /// <summary>Takes the level's props: writes them to the state, and stands solid over their box.</summary>
    internal void Hold(FurnitureBreakables owner, IReadOnlyList<BreakableSpot> props, (NVector Mins, NVector Maxs) bounds)
    {
        _owner = owner;
        _props = props;
        State.Count = (byte)props.Count;
        foreach (var p in props)
        {
            State.X[p.Slot] = BreakableGeometry.Coord(p.Foot.X);
            State.Y[p.Slot] = BreakableGeometry.Coord(p.Foot.Y);
            State.Z[p.Slot] = BreakableGeometry.Coord(p.Foot.Z);
            State.Yaw[p.Slot] = BreakableGeometry.YawByte(p.Yaw);
            State.Entry[p.Slot] = p.Entry;
        }

        for (int i = 0; i < State.Broken.Length; i++)
            State.Broken[i] = 0;
        Entities.SetOrigin(Handle, Vector.Zero);
        Entities.SetBounds(Handle, new Vector(bounds.Mins.X, bounds.Mins.Y, bounds.Mins.Z), new Vector(bounds.Maxs.X, bounds.Maxs.Y, bounds.Maxs.Z));
        SetSolidity(SolidType.BoundingBox, SolidFlags.CustomRayTest | SolidFlags.CustomBoxTest);
        Health = 1000;
    }

    /// <summary>Slot <paramref name="slot"/>'s broken bit.</summary>
    internal void SetBroken(int slot) => State.Broken[slot / 32] |= 1u << (slot % 32);

    public bool IsBroken(int slot) => (State.Broken[slot / 32] & (1u << (slot % 32))) != 0;

    public bool TestTrace(in BoneRay ray, int contentsMask, BoneTrace tr)
    {
        int slot = BreakableGeometry.Trace(ray, contentsMask, tr, _props, s => !IsBroken(s));
        if (slot >= 0)
            LastTraced = (slot, tr.EndPos);
        return true;
    }

    /// <summary>
    /// The slot the last trace that hit the entity stopped at, and where (a bullet's or a swing's
    /// trace, whose hit follows at once): the hit at that point is that slot's, whatever box is
    /// nearer the point (a swing's hull stops with its centre clear of the box).
    /// </summary>
    public (int Slot, NVector At) LastTraced { get; private set; } = (-1, default);

    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
    {
        var at = new NVector(position.X, position.Y, position.Z);
        if (_owner is not { } owner)
            return 0;
        if (LastTraced.Slot >= 0 && NVector.Distance(LastTraced.At, at) <= 1f)
            owner.Strike(LastTraced.Slot, attacker, damage, force);
        else
            owner.HitAt(at, attacker, damage, force);
        return 0;
    }

    public void TakeBlast(Vector source, in TFBlast blast, EntityHandle attacker, EntityHandle inflictor) =>
        _owner?.Blast(new NVector(source.X, source.Y, source.Z), blast.Radius, blast.Damage, attacker);
}

/// <summary>The breakables' entity is sent to every client whatever the PVS says: its props lie across the whole level.</summary>
[SideService(Side.Server)]
public sealed class BreakablesTransmit(FurnitureBreakables breakables) : ITransmitRule, IOrderedHook
{
    /// <summary>In TF2 Descent's furniture block, beside the furniture's transmit rule (+853).</summary>
    public const int Order = ModStages.First + 856;

    public int HookOrder => Order;

    public TransmitAnswer ShouldTransmit(EntityHandle entity, int recipientSlot) =>
        breakables.Entity is { } e && e.Handle == entity ? TransmitAnswer.Always : TransmitAnswer.Default;
}
