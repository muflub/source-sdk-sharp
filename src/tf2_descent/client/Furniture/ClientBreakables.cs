using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Interop;
using SourceSharp.Client.TempEnts;
using SourceSharp.Shared.Bones;

using TF2.Descent.Shared;

using NVector = System.Numerics.Vector3;

namespace TF2.Descent.Client.Furniture;

/// <summary>
/// The client's <c>tf2_descent_breakables</c>: the one entity that stands for the level's crates and
/// barrels. It is not drawn itself: <see cref="BreakableDrawing"/> draws each live prop as a client-only
/// model (a temp entity slot that never dies, the way the client draws its clientside props), and takes
/// it away when its broken bit arrives (the server's break sends the gibs). It answers prediction's
/// traces against its box as the server's does (<see cref="ICustomTraceTarget"/>, the same test over the
/// same list), so a hero's predicted step stops at a crate the server stops him at.
/// </summary>
[ClientStandaloneEntity(BreakablesNetState.ClassName)]
public sealed class ClientBreakablesEntity(IEngineEntityRender render, BreakableDrawing drawing) : ClientStandaloneEntity(render), ICustomTraceTarget
{
    public override string ClassName => BreakablesNetState.ClassName;

    [NetState] public BreakablesNetState State = new();

    private readonly List<BreakableSpot> _props = [];

    /// <summary>The props the state names, rebuilt when the list arrives.</summary>
    public IReadOnlyList<BreakableSpot> Props => _props;

    public bool IsBroken(int slot) => (State.Broken[slot / 32] & (1u << (slot % 32))) != 0;

    public override void OnSpawn()
    {
        Read();
        drawing.Sync(this);
    }

    public override void OnRemoved() => drawing.Forget(this);

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (_props.Count != State.Count)
            Read();
        drawing.Sync(this);
    }

    private void Read()
    {
        _props.Clear();
        int count = Math.Min((int)State.Count, BreakablesNetState.Max);
        for (int i = 0; i < count; i++)
            _props.Add(new BreakableSpot(i, State.Entry[i], new NVector(State.X[i], State.Y[i], State.Z[i]), BreakableGeometry.YawDegrees(State.Yaw[i])));
    }

    public bool TestTrace(in BoneRay ray, int contentsMask, BoneTrace tr)
    {
        BreakableGeometry.Trace(ray, contentsMask, tr, _props, slot => !IsBroken(slot));
        return true;
    }
}

/// <summary>Where the client draws the props: a never-dying temp entity slot of the prop's model, and its end.</summary>
public interface IBreakableSlots
{
    /// <summary>A slot drawing <paramref name="model"/> at <paramref name="origin"/> facing <paramref name="yaw"/>; -1 when the model is not loaded or the pool refused.</summary>
    int Draw(string model, Vector origin, float yaw);

    /// <summary>The slot's model is drawn no more.</summary>
    void Erase(int slot);
}

/// <summary>The client's temp entity pool: a high-priority, never-dying slot (FTENT_NEVERDIE), erased by clearing its die time (stock's IsActive).</summary>
[SideService(Side.Client, As = [typeof(IBreakableSlots)])]
public sealed class PoolBreakableSlots(ClientTempEntPool pool, IClientModelNames models) : IBreakableSlots
{
    public int Draw(string model, Vector origin, float yaw)
    {
        int index = models.IndexOf(model);
        if (index <= 0)
            return -1;
        var spawn = Spawn(yaw, alive: true);
        return pool.Spawn(origin, index, TempEntPriority.High, spawn);
    }

    public void Erase(int slot)
    {
        if (slot >= 0)
            pool.WriteSpawnInto(slot, Spawn(0f, alive: false));
    }

    private static TempEntSpawn Spawn(float yaw, bool alive)
    {
        var spawn = TempEntSpawn.Prepared(0f);
        spawn.Flags = (int)TempEntFlags.NeverDie;
        spawn.Die = alive ? 1f : 0f;
        spawn.Angles = new Vector(0f, yaw, 0f);
        spawn.RenderMode = (int)RenderMode.Normal;
        spawn.RenderAmt = alive ? 255 : 0;
        return spawn;
    }
}

/// <summary>
/// Each breakables entity's live props drawn, one slot each, and erased when they break. Forgotten
/// when the level ends: the pool's slots go with it, and the service outlives a level.
/// </summary>
[SideService(Side.Client)]
public sealed class BreakableDrawing(IBreakableSlots slots) : IClientLevelHook, IOrderedHook
{
    /// <summary>Beside the furniture's prompt (33).</summary>
    public int HookOrder => 34;

    private readonly Dictionary<ClientBreakablesEntity, int[]> _drawn = [];

    /// <summary>The breakables entities the client holds (one a level).</summary>
    public IEnumerable<ClientBreakablesEntity> Entities => _drawn.Keys;

    /// <summary>Props drawn now, over every entity.</summary>
    public int Drawn => _drawn.Values.Sum(s => s.Count(x => x >= 0));

    /// <summary>Draws each live prop not yet drawn and erases each broken one still drawn.</summary>
    public void Sync(ClientBreakablesEntity e)
    {
        if (!_drawn.TryGetValue(e, out var held) || held.Length != e.Props.Count)
        {
            if (held is not null)
                foreach (int s in held)
                    slots.Erase(s);
            held = [.. Enumerable.Repeat(-1, e.Props.Count)];
            _drawn[e] = held;
        }

        foreach (var p in e.Props)
        {
            bool live = !e.IsBroken(p.Slot);
            if (live && held[p.Slot] < 0 && FurnitureCatalog.Of(p.Entry) is { } entry)
                held[p.Slot] = slots.Draw(entry.Model, new Vector(p.Foot.X, p.Foot.Y, p.Foot.Z + BreakableGeometry.Lift(p.Entry)), p.Yaw);
            else if (!live && held[p.Slot] >= 0)
            {
                slots.Erase(held[p.Slot]);
                held[p.Slot] = -1;
            }
        }
    }

    public void Forget(ClientBreakablesEntity e)
    {
        if (!_drawn.Remove(e, out var held))
            return;
        foreach (int s in held)
            slots.Erase(s);
    }

    public void OnLevelShutdown() => _drawn.Clear();

    /// <summary>What is drawn, as lines (the dev command).</summary>
    public string Report()
    {
        var sb = new StringBuilder();
        foreach (var (e, held) in _drawn)
            sb.Append(CultureInfo.InvariantCulture,
                $"[TF2Descent/Breakables] client entity #{e.Index}: {e.Props.Count} props, {e.Props.Count(p => e.IsBroken(p.Slot))} broken, {held.Count(s => s >= 0)} drawn\n");
        return sb.Length > 0 ? sb.ToString() : "[TF2Descent/Breakables] client holds no breakables entity\n";
    }
}

/// <summary>
/// Prediction's movement meets the live props as the server's does (<see cref="IPredictionTraceClip"/>):
/// the swept hull against each held entity's live boxes, the same test the server's entity answers its
/// traces with, so a predicted step stops where the server's stops.
/// </summary>
[SideService(Side.Client)]
public sealed class BreakablesPredictionClip(BreakableDrawing drawing) : IPredictionTraceClip, IOrderedHook
{
    /// <summary>The mod's only member of the chain.</summary>
    public int HookOrder => 35;

    public bool Clip(in Vector start, in Vector end, in Vector mins, in Vector maxs, int mask, out MoveTrace hit)
    {
        hit = default;
        bool any = false;
        var ray = BoneRay.FromBox(new NVector(start.X, start.Y, start.Z), new NVector(end.X, end.Y, end.Z),
                                  new NVector(mins.X, mins.Y, mins.Z), new NVector(maxs.X, maxs.Y, maxs.Z));
        var tr = new BoneTrace();
        foreach (var e in drawing.Entities)
        {
            if (BreakableGeometry.Trace(ray, mask, tr, e.Props, slot => !e.IsBroken(slot)) < 0)
                continue;
            any = true;
            hit = new MoveTrace
            {
                Fraction = tr.Fraction,
                EndPos = new Vector(tr.EndPos.X, tr.EndPos.Y, tr.EndPos.Z),
                PlaneNormal = new Vector(tr.PlaneNormal.X, tr.PlaneNormal.Y, tr.PlaneNormal.Z),
                PlaneDist = tr.PlaneDist,
                StartSolid = tr.StartSolid,
                AllSolid = tr.AllSolid,
                Contents = tr.Contents,
                HitEntity = e.Handle,
            };
        }

        return any;
    }
}

/// <summary><c>tf2_descent_breakables_cl</c>: the breakables entity this client holds and what it draws (dev).</summary>
[ConCommand("tf2_descent_breakables_cl", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_breakables_cl: the crates and barrels this client holds, broken and drawn")]
public sealed class ClientBreakablesCommand(BreakableDrawing drawing, ILog log)
{
    public void Execute() => log.Msg(drawing.Report());
}
