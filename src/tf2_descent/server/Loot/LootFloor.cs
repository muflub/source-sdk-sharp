using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules.Inventory;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Mode;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Loot;

/// <summary>One hero's seat of a drop: what is his alone (R4), and his client while he is connected.</summary>
public sealed class LootSeatState
{
    internal LootSeatState(string hero, int player, FloorItem? item, long australium)
    {
        Hero = hero;
        Player = player;
        Item = item;
        Australium = australium;
    }

    /// <summary>The hero, as the item ledger names him.</summary>
    public string Hero { get; }

    /// <summary>His client slot while he is connected; 0 while he is not.</summary>
    public int Player { get; internal set; }

    /// <summary>The item, as the ledger has it on its floor; null once taken (or for Australium alone).</summary>
    public FloorItem? Item { get; internal set; }

    /// <summary>His share of the Australium; 0 once taken.</summary>
    public long Australium { get; internal set; }

    /// <summary>A take in flight: another waits for it.</summary>
    internal bool Taking { get; set; }

    public bool Empty => Item is null && Australium <= 0;

    public LootHolds Holds => (Item is null ? LootHolds.None : LootHolds.Item) | (Australium > 0 ? LootHolds.Australium : LootHolds.None);
}

/// <summary>
/// One drop: where it lies, its room, its seats (one a hero, up to
/// <see cref="LootFloor.Seats"/>), and the entity it stands on while its room
/// is awake and the pool has one (null: it is data).
/// </summary>
public sealed class FloorDrop
{
    internal FloorDrop(ushort number, Vector position, int room, long made, LootSeatState[] seats)
    {
        Number = number;
        Position = position;
        Room = room;
        Made = made;
        SeatList = seats;
    }

    /// <summary>The floor's number for the drop (never 0): what its seat messages carry.</summary>
    public ushort Number { get; }

    public Vector Position { get; }

    /// <summary>The room it lies in; -1 for none.</summary>
    public int Room { get; }

    /// <summary>The order drops were made in: the higher, the newer.</summary>
    public long Made { get; }

    internal LootSeatState[] SeatList { get; }

    public IReadOnlyList<LootSeatState> Seats => SeatList;

    /// <summary>The entity it stands on; null while it is data.</summary>
    public LootEntity? Entity { get; internal set; }

    public bool OnEntity => Entity is not null;

    public bool Empty => SeatList.All(s => s.Empty);

    /// <summary>The seat of the hero in client slot <paramref name="player"/>; null when he has none.</summary>
    public LootSeatState? SeatOf(int player) => player <= 0 ? null : SeatList.FirstOrDefault(s => s.Player == player);
}

/// <summary>One hero's part of a new drop: his item (already on the ledger's floor) and his Australium.</summary>
public sealed record LootShare(string Hero, int Player, FloorItem? Item, long Australium);

/// <summary>What a take did.</summary>
public enum TakeOutcome
{
    /// <summary>The seat is empty now, or what could be taken was.</summary>
    Taken,

    /// <summary>The ledger refused (a full backpack): the item stays, and the hero was told.</summary>
    Refused,

    /// <summary>The player owns no seat of the drop, or his seat is empty: nothing to take.</summary>
    NotYours,

    /// <summary>A touch of a seat with no Australium (an item is taken by use).</summary>
    Nothing,

    /// <summary>A take of the same seat is in flight.</summary>
    Busy,

    /// <summary>The drop is not on the floor, or is data (its room sleeps).</summary>
    NotThere,

    /// <summary>The level ended while the ledger answered: nothing more is done.</summary>
    LevelEnded,
}

/// <summary>
/// The loot on the floor (plan_descent_logic WO2): every drop of the level,
/// as data, and the pool of <see cref="PoolSize"/> <c>tf2_descent_loot</c>
/// entities the drops in woken rooms stand on.
///
/// <list type="bullet">
/// <item><b>The pool.</b> Made once at level ready when the Descent mode
/// plays (<see cref="OnLevelReady"/>), parked, lent and parked again, never
/// made or removed in play; at level shutdown it is forgotten with the
/// level's entities.</item>
/// <item><b>Seats.</b> A drop holds a seat a hero, each with an item (already
/// on the item ledger's floor for him) and his share of Australium. His seat
/// goes to his client alone by <see cref="LootSeatMessage"/>; the entity
/// reaches only the clients of heroes with a full seat
/// (<see cref="Transmit"/>, read by <see cref="LootTransmit"/>).</item>
/// <item><b>The pool's limit.</b> A drop in a woken room takes a free entity;
/// with none free, the oldest drop on an entity folds to data and the new
/// drop takes its entity. A drop that is data loses nothing: when an entity
/// frees, the newest drop that is data in a woken room takes it.</item>
/// <item><b>Sleep and wake.</b> Every <see cref="RoomCheckEvery"/> frames: a
/// drop whose room sleeps folds to data, its entity back in the pool; a drop
/// that is data in a woken room takes a free entity. It comes back at the
/// same place with the same seats.</item>
/// <item><b>Taking.</b> A hero's use takes his seat: the item through the
/// ledger's <see cref="IItemLedger.Claim"/> (refused when his backpack is
/// full: the item stays and he is told) and the Australium through
/// <see cref="IItemLedger.CollectAustralium"/>. His touch takes the Australium
/// alone. A seat emptied is taken away from his client; a drop whose seats
/// are all empty leaves the floor and its entity parks.</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class LootFloor(IDescentModeState mode, ILootSpawner spawner, ILootRooms rooms, IItemLedger ledger, ILootParty party,
                              IEngineEntities entities, IUserMessageSender sender, ClientMessages messages, ILog log)
    : IGameLifecycle, IFrameLifecycle, IClientLifecycle
{
    /// <summary>The floor's entities a level (section 2's budget).</summary>
    public const int PoolSize = 64;

    /// <summary>A drop's seats: the party's most members (robot_rules' <c>party.maxMembers</c>, <c>LootRules.Seats</c>).</summary>
    public const int Seats = 4;

    /// <summary>Frames between the checks of the drops' rooms.</summary>
    public const int RoomCheckEvery = 16;

    /// <summary>The notice a hero gets when his backpack cannot take the item.</summary>
    public const string FullToken = "#Descent_Loot_Full";

    /// <summary>The notice for any other refusal; its <c>%s1</c> is the ledger's reason.</summary>
    public const string RefusedToken = "#Descent_Loot_Refused";

    /// <summary>After the horde (First + 500) and the kill seam (+540), in Descent's floor block.</summary>
    public const int Order = ModStages.First + 800;

    private readonly List<FloorDrop> _drops = [];
    private readonly Stack<LootEntity> _free = new();
    private readonly List<LootEntity> _pool = [];
    private readonly uint[] _mine = new uint[EngineLimits.MaxEdicts];
    private readonly ulong[] _viewers = new ulong[EngineLimits.MaxEdicts];
    private readonly Dictionary<LootEntity, FloorDrop> _onEntity = [];
    private ushort _nextNumber;
    private long _made;
    private int _frames;

    public int LifecycleOrder => Order;

    public int FrameOrder => Order;

    public int ClientLifecycleOrder => Order;

    /// <summary>Counts levels: a take or a drop that was waiting on the ledger across a level change does nothing.</summary>
    public int Level { get; private set; }

    /// <summary>Whether this level has its pool.</summary>
    public bool Ready { get; private set; }

    /// <summary>Entities this level's pool was made with (<see cref="PoolSize"/> unless the engine refused one).</summary>
    public int Made => _pool.Count;

    /// <summary>Pool entities made since the server started: <see cref="PoolSize"/> a level with a pool.</summary>
    public int MadeEver { get; private set; }

    /// <summary>Entities parked now.</summary>
    public int Free => _free.Count;

    /// <summary>Every drop of the level, on an entity or data.</summary>
    public IReadOnlyList<FloorDrop> Drops => _drops;

    /// <summary>The engine's edicts before and after this level's pool was made.</summary>
    public (int Before, int After) PoolEdicts { get; private set; }

    //-------------------------------------------------------------------------
    // The level
    //-------------------------------------------------------------------------

    public void OnLevelReady(string mapName)
    {
        Reset();
        if (!mode.Active)
            return;

        int before = spawner.Edicts();
        for (int i = 0; i < PoolSize; i++)
        {
            var e = spawner.Make();
            if (e is null)
            {
                log.Warning($"[TF2Descent/Loot] the engine refused loot entity {i + 1} of {PoolSize}: the pool holds {_pool.Count}\n");
                break;
            }

            e.Join(this);
            e.Park();
            _pool.Add(e);
            _mine[EntryOf(e)] = e.Handle.Value;
            MadeEver++;
        }

        for (int i = _pool.Count - 1; i >= 0; i--)
            _free.Push(_pool[i]);
        Ready = true;
        PoolEdicts = (before, spawner.Edicts());
        log.Msg($"[TF2Descent/Loot] level ready: loot pool of {_pool.Count} made and parked, entities {PoolEdicts.Before} -> {PoolEdicts.After}\n");
    }

    /// <summary>The level is going, its entities with it: the floor is forgotten (the ledger forgets its own at the next level).</summary>
    public void OnLevelShutdownPreEntity()
    {
        if (_drops.Count > 0)
            log.Msg($"[TF2Descent/Loot] level over: {_drops.Count} drops left on the floor are lost\n");
        Reset();
    }

    private void Reset()
    {
        Level++;
        Ready = false;
        _drops.Clear();
        _free.Clear();
        _pool.Clear();
        _onEntity.Clear();
        Array.Clear(_mine);
        Array.Clear(_viewers);
        PoolEdicts = default;
    }

    public void OnThink(float frameTime)
    {
        if (!Ready || ++_frames < RoomCheckEvery)
            return;
        _frames = 0;
        CheckRooms();
    }

    /// <summary>Folds every drop on an entity whose room sleeps, then lends free entities to drops in woken rooms; answers how many folded.</summary>
    public int CheckRooms()
    {
        int folded = 0;
        foreach (var drop in _drops)
        {
            if (drop.Entity is { } e && !rooms.Awake(drop.Room))
            {
                Fold(drop);
                Release(e);
                folded++;
            }
        }

        Refill();
        return folded;
    }

    //-------------------------------------------------------------------------
    // Clients
    //-------------------------------------------------------------------------

    /// <summary>A hero comes back: his seats are his client's again, and sent.</summary>
    public void OnClientActive(int entIndex)
    {
        if (party.HeroOf(entIndex) is not { } hero)
            return;

        foreach (var drop in _drops)
        {
            foreach (var seat in drop.SeatList)
            {
                if (seat.Hero != hero || seat.Empty)
                    continue;
                seat.Player = entIndex;
                SendSeat(drop, seat);
            }

            UpdateViewers(drop);
        }
    }

    /// <summary>A hero leaves: his seats stay, owned by no client, so whoever takes his slot sees none of them.</summary>
    public void OnClientDisconnected(int entIndex)
    {
        foreach (var drop in _drops)
        {
            foreach (var seat in drop.SeatList)
                if (seat.Player == entIndex)
                    seat.Player = 0;
            UpdateViewers(drop);
        }
    }

    //-------------------------------------------------------------------------
    // Transmit
    //-------------------------------------------------------------------------

    /// <summary>
    /// The transmit rule's answer for <paramref name="entity"/> to the client in
    /// <paramref name="recipientSlot"/>: a pool entity reaches a client only
    /// when that client's hero owns a full seat of the drop it holds (and the
    /// ordinary check agrees); a parked one reaches no one. Anything else is
    /// none of the floor's business. One array read.
    /// </summary>
    public TransmitAnswer Transmit(EntityHandle entity, int recipientSlot)
    {
        int i = entity.EntryIndex;
        if ((uint)i >= (uint)_mine.Length || _mine[i] == 0 || _mine[i] != entity.Value)
            return TransmitAnswer.Default;
        return recipientSlot is > 0 and < 64 && (_viewers[i] & (1UL << recipientSlot)) != 0
            ? TransmitAnswer.Default
            : TransmitAnswer.Never;
    }

    //-------------------------------------------------------------------------
    // Placing
    //-------------------------------------------------------------------------

    /// <summary>
    /// A new drop at <paramref name="at"/> with a seat for each share that holds
    /// something (at most <see cref="Seats"/>). On an entity at once when its
    /// room is awake and the pool lends one (folding the oldest drop on an
    /// entity when none is free), else data. Null when no share holds anything.
    /// </summary>
    public FloorDrop? Place(Vector at, IReadOnlyList<LootShare> shares)
    {
        var seats = shares.Where(s => s.Item is not null || s.Australium > 0).Take(Seats)
            .Select(s => new LootSeatState(s.Hero, s.Player, s.Item, Math.Max(0, s.Australium))).ToArray();
        if (seats.Length == 0)
            return null;

        if (++_nextNumber == 0)
            _nextNumber = 1;
        var drop = new FloorDrop(_nextNumber, at, rooms.RoomAt(at), ++_made, seats);
        _drops.Add(drop);
        if (Ready && rooms.Awake(drop.Room))
        {
            var e = _free.Count > 0 ? _free.Pop() : Evict();
            if (e is not null)
                Embody(drop, e);
        }

        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Loot] drop {drop.Number} at ({at.X:0} {at.Y:0} {at.Z:0}) room {drop.Room}: {Describe(drop)}, {(drop.OnEntity ? "entity #" + EntryOf(drop.Entity!) : "data")}\n"));
        return drop;
    }

    /// <summary>The oldest drop on an entity gives its entity up; null when none stands on one.</summary>
    private LootEntity? Evict()
    {
        FloorDrop? oldest = null;
        foreach (var d in _drops)
            if (d.Entity is not null && (oldest is null || d.Made < oldest.Made))
                oldest = d;
        if (oldest is null)
            return null;

        var e = oldest.Entity!;
        Fold(oldest);
        e.Park();
        return e;
    }

    /// <summary>The newest drops that are data in woken rooms take the free entities.</summary>
    private void Refill()
    {
        while (_free.Count > 0)
        {
            FloorDrop? newest = null;
            foreach (var d in _drops)
                if (d.Entity is null && (newest is null || d.Made > newest.Made) && rooms.Awake(d.Room))
                    newest = d;
            if (newest is null)
                return;
            Embody(newest, _free.Pop());
        }
    }

    private void Embody(FloorDrop drop, LootEntity e)
    {
        drop.Entity = e;
        _onEntity[e] = drop;
        e.Lend(drop.Number, drop.Position);
        UpdateViewers(drop);
        foreach (var seat in drop.SeatList)
            if (!seat.Empty)
                SendSeat(drop, seat);
    }

    /// <summary>The drop becomes data: its owners' clients lose its seats; its entity is the caller's to park or reuse.</summary>
    private void Fold(FloorDrop drop)
    {
        if (drop.Entity is not { } e)
            return;
        foreach (var seat in drop.SeatList)
            if (!seat.Empty)
                SendSeat(drop, seat, gone: true);
        _viewers[EntryOf(e)] = 0;
        _onEntity.Remove(e);
        drop.Entity = null;
    }

    private void Release(LootEntity e)
    {
        e.Park();
        _free.Push(e);
    }

    private void UpdateViewers(FloorDrop drop)
    {
        if (drop.Entity is not { } e)
            return;
        ulong mask = 0;
        foreach (var seat in drop.SeatList)
            if (!seat.Empty && seat.Player is > 0 and < 64)
                mask |= 1UL << seat.Player;
        _viewers[EntryOf(e)] = mask;
    }

    private static int EntryOf(LootEntity e) => e.Handle.EntryIndex;

    //-------------------------------------------------------------------------
    // The owner's client
    //-------------------------------------------------------------------------

    /// <summary>The seat as its owner's client is told it: never an affix, nothing of an unidentified item but its base, rarity and level.</summary>
    public static LootSeat SeatPayload(FloorDrop drop, LootSeatState seat, int entity, bool gone = false)
    {
        var payload = new LootSeat { Entity = (short)entity, Drop = drop.Number, Holds = 0, Base = "", Unique = "" };
        if (gone)
            return payload;

        payload.Holds = (byte)seat.Holds;
        if (seat.Item is { Item: var item })
        {
            payload.Base = item.BaseId;
            payload.Rarity = (byte)Math.Clamp(item.Rarity, 0, 255);
            payload.ItemLevel = (byte)Math.Clamp(item.ItemLevel, 0, 255);
            payload.Identified = item.Identified;
            payload.Unique = item.Identified ? item.Unique ?? "" : "";
        }

        payload.Australium = (int)Math.Clamp(seat.Australium, 0, int.MaxValue);
        return payload;
    }

    private void SendSeat(FloorDrop drop, LootSeatState seat, bool gone = false)
    {
        if (seat.Player <= 0 || drop.Entity is not { } e)
            return;
        sender.Send(SeatPayload(drop, seat, EntryOf(e), gone || seat.Empty), seat.Player);
    }

    //-------------------------------------------------------------------------
    // Taking
    //-------------------------------------------------------------------------

    /// <summary>A +USE of <paramref name="e"/> by <paramref name="activator"/>: his seat, item and Australium.</summary>
    internal void Use(LootEntity e, EntityHandle activator)
    {
        if (_onEntity.GetValueOrDefault(e) is { } drop && PlayerOf(activator) is int player and > 0)
            _ = Take(drop, player, touch: false);
    }

    /// <summary>A touch of <paramref name="e"/> by <paramref name="toucher"/>: his seat's Australium.</summary>
    internal void Touch(LootEntity e, EntityHandle toucher)
    {
        if (_onEntity.GetValueOrDefault(e) is { } drop && PlayerOf(toucher) is int player and > 0
            && drop.SeatOf(player) is { Australium: > 0, Taking: false })
            _ = Take(drop, player, touch: true);
    }

    private int PlayerOf(EntityHandle h) =>
        h != EntityHandle.None && entities.IsValid(h) && entities.IsPlayer(h) ? entities.GetIndex(h) : 0;

    /// <summary>The drop on <paramref name="e"/>; null when it is parked.</summary>
    public FloorDrop? DropOn(LootEntity e) => _onEntity.GetValueOrDefault(e);

    /// <summary>
    /// The player in <paramref name="player"/> takes his seat of <paramref name="drop"/>:
    /// by use (<paramref name="touch"/> false) the item and the Australium, by
    /// touch the Australium alone. What the ledger refuses stays, and the hero is
    /// told. Completes when the ledger has answered; a level that ends meanwhile
    /// leaves everything as the next level finds it.
    /// </summary>
    public async Task<TakeOutcome> Take(FloorDrop drop, int player, bool touch)
    {
        if (!drop.OnEntity || !_drops.Contains(drop))
            return TakeOutcome.NotThere;
        if (drop.SeatOf(player) is not { Empty: false } seat)
            return TakeOutcome.NotYours;
        if (touch && seat.Australium <= 0)
            return TakeOutcome.Nothing;
        if (seat.Taking)
            return TakeOutcome.Busy;

        int level = Level;
        string? refusal = null;
        seat.Taking = true;
        try
        {
            if (!touch && seat.Item is { } item)
            {
                var r = await ledger.Claim(seat.Hero, item.Id);
                if (level != Level)
                    return TakeOutcome.LevelEnded;
                if (r.Ok)
                    log.Msg($"[TF2Descent/Loot] player {player} took {item.Item.BaseId} (#{item.Id}) from drop {drop.Number}\n");
                if (r.Ok || r.Refusal == LedgerRefusals.NotOnFloor)
                    seat.Item = null;
                else
                    refusal = r.Refusal;
            }

            if (seat.Australium > 0)
            {
                long amount = seat.Australium;
                var r = await ledger.CollectAustralium(seat.Hero, amount);
                if (level != Level)
                    return TakeOutcome.LevelEnded;
                if (r.Ok)
                {
                    seat.Australium = 0;
                    log.Msg($"[TF2Descent/Loot] player {player} took {amount} Australium from drop {drop.Number}\n");
                }
                else
                {
                    refusal ??= r.Refusal;
                }
            }
        }
        finally
        {
            seat.Taking = false;
        }

        if (refusal is not null)
        {
            log.Msg($"[TF2Descent/Loot] player {player} could not take drop {drop.Number}: {refusal}\n");
            if (refusal == InventoryRefusals.BackpackFull)
                messages.Notice(player, FullToken);
            else
                messages.Notice(player, RefusedToken, refusal);
        }

        Settle(drop, seat);
        return refusal is null ? TakeOutcome.Taken : TakeOutcome.Refused;
    }

    /// <summary>After a take: the seat's owner is told what is left of it; an empty drop leaves the floor and parks its entity.</summary>
    private void Settle(FloorDrop drop, LootSeatState seat)
    {
        if (!_drops.Contains(drop))
            return;

        SendSeat(drop, seat);
        UpdateViewers(drop);
        if (!drop.Empty)
            return;

        _drops.Remove(drop);
        if (drop.Entity is { } e)
        {
            Fold(drop);
            Release(e);
            Refill();
        }

        log.Msg($"[TF2Descent/Loot] drop {drop.Number} is empty and leaves the floor\n");
    }

    /// <summary>The nearest drop on an entity holding a full seat of the player's, within <paramref name="reach"/> of him; null for none.</summary>
    public FloorDrop? Nearest(int player, float reach)
    {
        if (party.Origin(player) is not { } at)
            return null;
        FloorDrop? best = null;
        double bestDistance = reach;
        foreach (var d in _drops)
        {
            if (!d.OnEntity || d.SeatOf(player) is not { Empty: false })
                continue;
            double dist = Distance(d.Position, at);
            if (dist <= bestDistance)
            {
                best = d;
                bestDistance = dist;
            }
        }

        return best;
    }

    //-------------------------------------------------------------------------
    // Reading
    //-------------------------------------------------------------------------

    /// <summary>The floor as lines: each drop, its room, entity or data, and its seats; then the pool.</summary>
    public string Report()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"[TF2Descent/Loot] floor: {_drops.Count} drops ({_drops.Count(d => d.OnEntity)} on entities, {_drops.Count(d => !d.OnEntity)} data), pool {_pool.Count}, free {_free.Count}\n");
        foreach (var d in _drops)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"[TF2Descent/Loot]   drop {d.Number} at ({d.Position.X:0} {d.Position.Y:0} {d.Position.Z:0}) room {d.Room} {(d.Entity is { } e ? "entity #" + EntryOf(e) : "data")}: {Describe(d)}\n");
        }

        return sb.ToString();
    }

    private static string Describe(FloorDrop d) =>
        string.Join(", ", d.SeatList.Select(s =>
            $"{s.Hero}{(s.Player > 0 ? "@" + s.Player.ToString(CultureInfo.InvariantCulture) : "")} " +
            (s.Empty ? "empty" : $"{(s.Item is { } i ? $"{i.Item.BaseId} r{i.Item.Rarity} #{i.Id}{(i.Item.Identified ? "" : " unidentified")}" : "")}" +
                                 $"{(s.Item is not null && s.Australium > 0 ? " + " : "")}{(s.Australium > 0 ? s.Australium.ToString(CultureInfo.InvariantCulture) + " Australium" : "")}")));

    internal static double Distance(Vector a, Vector b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

/// <summary>
/// The floor's transmit rule (<see cref="LootFloor.Transmit"/>): a pool entity
/// reaches only the clients of heroes who own a full seat of the drop it
/// holds, and a parked one no client. Each entity of the pool is flagged for
/// the full check at its spawn, so the engine asks this for them. Registered
/// plain: naming <see cref="ITransmitRule"/> in <c>As</c> registers it twice
/// (HO0's trap).
/// </summary>
[SideService(Side.Server)]
public sealed class LootTransmit(LootFloor floor) : ITransmitRule, IOrderedHook
{
    /// <summary>Beside the horde's (First + 500), in the floor's block.</summary>
    public const int Order = ModStages.First + 810;

    public int HookOrder => Order;

    public TransmitAnswer ShouldTransmit(EntityHandle entity, int recipientSlot) => floor.Transmit(entity, recipientSlot);
}
