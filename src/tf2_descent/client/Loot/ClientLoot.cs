using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;

using TF2.Descent.Shared;

namespace TF2.Descent.Client.Loot;

/// <summary>
/// The client's <c>tf2_descent_loot</c> (plan_descent_logic WO2): a drop this
/// client's hero owns a seat of, the only kind the server sends it. Its own
/// seat arrives by message (<see cref="LootOnFloor"/>); the entity draws the
/// seat's model over the one every client is sent (<see cref="LootLooks.ModelFor"/>:
/// the currency pack for Australium alone, else the gift).
/// </summary>
[ClientStandaloneEntity(LootNetState.ClassName)]
public sealed class ClientLootEntity(IEngineEntityRender render, LootOnFloor floor) : ClientStandaloneEntity(render)
{
    public override string ClassName => LootNetState.ClassName;

    [NetState] public LootNetState State = new();

    private string _override = "";

    /// <summary>Whether the server has stopped sending it (its seat was taken, or its room slept).</summary>
    public bool Dormant => Render.IsDormant(Handle);

    public Vector Origin => Entities.GetOrigin(Handle);

    /// <summary>The model drawn over the networked one; empty for none.</summary>
    public string ModelOverride => _override;

    public override void OnSpawn() => floor.Track(this);

    public override void OnRemoved() => floor.Untrack(this);

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        floor.Track(this);
        Look();
    }

    /// <summary>Draws this client's seat of the drop: its model, or the networked one with no seat known.</summary>
    internal void Look()
    {
        string model = floor.SeatOf(this) is { } seat ? LootLooks.ModelFor(seat.Holds) : "";
        if (model == LootLooks.ItemModel)
            model = "";   // the networked model already
        if (model == _override)
            return;
        _override = model;
        Render.SetModelOverride(Handle, model);
    }
}

/// <summary>One drop the local hero can see: where it lies and what his seat holds.</summary>
public sealed record LootOnFloorEntry(int Entity, int Drop, Vector Position, LootSeatView Seat);

/// <summary>The local hero's loot on the floor, for the UI to read (plan_descent_logic WO2).</summary>
public interface ILootOnFloor
{
    /// <summary>Every drop the client holds, not dormant, with the local hero's seat of it: by entity index.</summary>
    IReadOnlyList<LootOnFloorEntry> Visible();
}

/// <summary>
/// The local hero's seats, as the server sends them (<see cref="LootSeatMessage"/>),
/// joined to the loot entities the client holds. A seat is kept by the
/// entity's index and counted only while the entity holds the same drop (the
/// pool lends one entity to many drops). Everything is forgotten when the
/// level ends: a seat never outlives its level (client state outlives a
/// level otherwise).
/// </summary>
[SideService(Side.Client)]
public sealed class LootOnFloor : ILootOnFloor, ILootSeatHook, IClientLevelHook, IOrderedHook
{
    /// <summary>Beside the mod's other client level hooks (LevelDownloads 10, HudNotices 11, the skill bar 20).</summary>
    public int HookOrder => 31;

    private readonly Dictionary<int, LootSeatView> _seats = [];
    private readonly List<ClientLootEntity> _live = [];

    /// <summary>Seats held now, whatever the entities.</summary>
    public int SeatCount => _seats.Count;

    /// <summary>Loot entities the client holds, dormant or not.</summary>
    public IReadOnlyList<ClientLootEntity> Live => _live;

    public void Track(ClientLootEntity e)
    {
        if (!_live.Contains(e))
            _live.Add(e);
    }

    public void Untrack(ClientLootEntity e) => _live.Remove(e);

    public void OnLootSeat(LootSeatView seat)
    {
        if (seat.Holds == LootHolds.None)
        {
            if (_seats.TryGetValue(seat.Entity, out var held) && held.Drop == seat.Drop)
                _seats.Remove(seat.Entity);
        }
        else
        {
            _seats[seat.Entity] = seat;
        }

        foreach (var e in _live)
            if (e.Index == seat.Entity)
                e.Look();
    }

    /// <summary>This client's seat of the drop <paramref name="e"/> holds now; null when it has none.</summary>
    public LootSeatView? SeatOf(ClientLootEntity e) =>
        _seats.TryGetValue(e.Index, out var seat) && seat.Drop == e.State.Drop && e.State.Drop != 0 ? seat : null;

    public IReadOnlyList<LootOnFloorEntry> Visible()
    {
        var visible = new List<LootOnFloorEntry>();
        foreach (var e in _live)
            if (!e.Dormant && SeatOf(e) is { } seat)
                visible.Add(new LootOnFloorEntry(e.Index, e.State.Drop, e.Origin, seat));
        visible.Sort((a, b) => a.Entity.CompareTo(b.Entity));
        return visible;
    }

    public void OnLevelShutdown() => _seats.Clear();
}

/// <summary>The client's seat-message dispatch (<see cref="ILootSeatHook"/>), which only a registrar can name.</summary>
[SideService(Side.Client)]
public sealed class LootClientRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<ILootSeatHook>();
}

/// <summary><c>tf2_descent_loot_cl</c>: the loot this client holds, and the local hero's seats (dev).</summary>
[ConCommand("tf2_descent_loot_cl", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_loot_cl: the loot entities this client holds and the local hero's seat of each")]
public sealed class ClientLootCommand(LootOnFloor floor, ILog log)
{
    public void Execute()
    {
        var visible = floor.Visible();
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"[TF2Descent/Loot] client holds {floor.Live.Count} loot entities ({floor.Live.Count(e => e.Dormant)} dormant), {floor.SeatCount} seats, {visible.Count} visible\n");
        foreach (var e in floor.Live)
        {
            var seat = floor.SeatOf(e);
            string holds = seat is null
                ? "no seat"
                : string.Create(CultureInfo.InvariantCulture,
                    $"{seat.Holds}{(seat.HasItem ? $" {seat.Base} r{seat.Rarity} il{seat.ItemLevel}{(seat.Identified ? "" : " unidentified")}{(seat.Unique.Length > 0 ? " " + seat.Unique : "")}" : "")} australium {seat.Australium}");
            string model = e.ModelOverride.Length > 0 ? e.ModelOverride : LootLooks.ItemModel;
            sb.Append(CultureInfo.InvariantCulture,
                $"[TF2Descent/Loot]   #{e.Index} drop {e.State.Drop} dormant {e.Dormant} at ({e.Origin.X:0} {e.Origin.Y:0} {e.Origin.Z:0}) model {model}: {holds}\n");
        }

        log.Msg(sb.ToString());
    }
}
