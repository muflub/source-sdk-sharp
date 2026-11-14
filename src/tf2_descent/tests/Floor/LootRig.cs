using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Host.Contracts;
using SourceSharp.Tests;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Loot;
using TF2.Descent.Server.Mode;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Floor;

/// <summary>Makes loot entities through the real registry over a recording engine, and counts each made.</summary>
internal sealed class RigLootSpawner(ScriptedEntityRegistry registry) : ILootSpawner
{
    private int _next = 200;

    public int Made { get; private set; }

    /// <summary>The engine's edicts: the map's 30 and each entity made since.</summary>
    public int EdictCount { get; set; } = 30;

    public LootEntity? Make()
    {
        Made++;
        EdictCount++;
        var h = new EntityHandle((uint)((1 << 16) | _next++));
        registry.Spawn(h, LootNetState.ClassName);
        return registry.Get(h) as LootEntity;
    }

    public int Edicts() => EdictCount;

    /// <summary>A new level's entities have new indices: the engine removed the last level's.</summary>
    public void NewLevel() => _next = 200 + Made;
}

/// <summary>Rooms by X: room <c>floor(x / 512)</c>, each awake unless a fact puts it to sleep.</summary>
internal sealed class RigRooms : ILootRooms
{
    public HashSet<int> Asleep { get; } = [];

    public int RoomAt(Vector at) => at.X < 0 ? -1 : (int)(at.X / 512f);

    public bool Awake(int room) => room < 0 || !Asleep.Contains(room);
}

/// <summary>The heroes a fact puts down: a slot, a ledger name and a place each.</summary>
internal sealed class RigParty : ILootParty
{
    public List<LootMember> Members { get; } = [];

    public IReadOnlyList<LootMember> Near(Vector at, int killer) =>
        [.. Members.OrderBy(m => m.Player != killer).ThenBy(m => m.Player)];

    public string? HeroOf(int player) => Members.FirstOrDefault(m => m.Player == player)?.Hero;

    public Vector? Origin(int player) => Members.FirstOrDefault(m => m.Player == player)?.Origin;
}

internal sealed class RigMode : IDescentModeState
{
    public bool Active { get; set; } = true;
}

internal sealed class NoLedgerHooks : IHooks<IItemLedgerHook>
{
    public IReadOnlyList<IItemLedgerHook> All => [];
    public void Each(string what, Action<IItemLedgerHook> call) { }
}

/// <summary>
/// The floor over the real loot entities, registry, in-memory ledger and
/// rules, with a recording engine under them and fakes for the spawner, the
/// rooms and the party: the pool made at a level ready.
/// </summary>
internal sealed class LootRig
{
    public static readonly TF2DescentRules Rules = new();

    public const int SlotA = 3, SlotB = 4, SlotC = 5;
    public const string A = "hero-a", B = "hero-b", C = "hero-c";

    public LootRig(bool ready = true, IItemLedger? ledger = null)
    {
        Engine = RecordingEngine.Default();
        Engine.Returns("Entities.IsValid", true);
        Engine.ReturnsEachCall("Entities.IsPlayer", () => Engine.Calls[^1].ArgAs<EntityHandle>(0).Value is > 0 and < 64);
        Engine.ReturnsEachCall("Entities.GetIndex", () => (int)Engine.Calls[^1].ArgAs<EntityHandle>(0).EntryIndex);

        Registry = new ScriptedEntityRegistry(TestEngines.Server());
        Registry.RegisterAll(typeof(LootEntity).Assembly);
        var services = new ServiceContainer();
        foreach (var (contract, _) in EngineServices.Areas(new RecordingLog()))
            services.AddInstance(contract, Engine);
        services.AddInstance<IUserMessageSender>(Engine);
        services.AddInstance<ILog>(Log);
        services.AddInstance(new ConsoleLines());
        Registry.Services = services;

        Spawner = new RigLootSpawner(Registry);
        Memory = new InMemoryItemLedger(Rules, new NoLedgerHooks(), new StartingKit(Rules));
        Ledger = ledger ?? Memory;
        Party.Members.Add(new LootMember(SlotA, A, new Vector(100, 100, 0)));
        Party.Members.Add(new LootMember(SlotB, B, new Vector(120, 100, 0)));
        Floor = new LootFloor(Mode, Spawner, Rooms, Ledger, Party, Engine, Engine, new ClientMessages(Engine), Log);
        if (ready)
            Floor.OnLevelReady("tf2_descent-1-s1");
    }

    public RecordingEngine Engine { get; }
    public ScriptedEntityRegistry Registry { get; }
    public RecordingLog Log { get; } = new();
    public RigLootSpawner Spawner { get; }
    public RigRooms Rooms { get; } = new();
    public RigParty Party { get; } = new();
    public RigMode Mode { get; } = new();
    public InMemoryItemLedger Memory { get; }
    public IItemLedger Ledger { get; }
    public LootFloor Floor { get; }

    /// <summary>Player <paramref name="slot"/>'s entity handle, as the engine names a player.</summary>
    public static EntityHandle PlayerOf(int slot) => new((uint)slot);

    private ulong _seed = 1;

    /// <summary>An item of a base, rolled as the rules roll one.</summary>
    public static ItemInstance Roll(string baseId, ulong seed = 1, bool identified = true, int tier = 1) =>
        Rules.ItemRules.Roller.Roll(new ItemRollContext("soldier", 1, 1, 5, tier, "base:" + baseId, "craft"), seed) with { Identified = identified };

    /// <summary>An item on the ledger's floor for <paramref name="hero"/>.</summary>
    public FloorItem Item(string hero, string baseId = "shovel", bool identified = true) =>
        Memory.PlaceOnFloor(Roll(baseId, _seed++, identified), hero, default);

    /// <summary>A drop at <paramref name="at"/> with an item each for A and B (B's a rocket launcher), and Australium as given.</summary>
    public FloorDrop Drop(Vector at, long australiumA = 0, long australiumB = 0, bool itemA = true, bool itemB = true) =>
        Floor.Place(at, [
            new LootShare(A, SlotA, itemA ? Item(A, "shovel") : null, australiumA),
            new LootShare(B, SlotB, itemB ? Item(B, "rocket_launcher") : null, australiumB),
        ])!;

    /// <summary>Room 0's middle.</summary>
    public static Vector InRoom(int room) => new(room * 512 + 256, 256, 0);

    /// <summary>The transmit rule's answer for <paramref name="drop"/>'s entity to <paramref name="slot"/>.</summary>
    public TransmitAnswer SentTo(FloorDrop drop, int slot) => Floor.Transmit(drop.Entity!.Handle, slot);

    /// <summary>Every seat message the engine was asked to send to <paramref name="slot"/>, decoded.</summary>
    public List<(LootSeat Seat, byte[] Bytes)> SeatsSentTo(int slot) =>
        [.. Engine.Calls.Where(c => c.Member == "UserMessages.Send" && c.ArgAs<string>(0).EndsWith(":" + LootSeatMessage.Name, StringComparison.Ordinal)
                                    && c.ArgAs<int>(2) == slot)
            .Select(c => (Decode((byte[])c.Arg(4)!), (byte[])c.Arg(4)!))];

    public static LootSeat Decode(byte[] bytes)
    {
        var reader = new UserMessageReader(bytes);
        var seat = LootSeat.Read(ref reader);
        Assert.False(reader.Overran, "the seat message did not decode");
        return seat;
    }

    /// <summary>Notices sent to <paramref name="slot"/>, as text and argument.</summary>
    public List<Notice> NoticesTo(int slot) =>
        [.. Engine.Calls.Where(c => c.Member == "UserMessages.Send" && c.ArgAs<string>(0).EndsWith(":" + NoticeMessage.Name, StringComparison.Ordinal)
                                    && c.ArgAs<int>(2) == slot)
            .Select(c =>
            {
                var reader = new UserMessageReader((byte[])c.Arg(4)!);
                return Notice.Read(ref reader);
            })];

    /// <summary>The next level: the engine removes the entities, the floor forgets, a new pool is made.</summary>
    public void NextLevel()
    {
        Floor.OnLevelShutdownPreEntity();
        Spawner.NewLevel();
        Floor.OnLevelReady("tf2_descent-1-s1");
    }
}
