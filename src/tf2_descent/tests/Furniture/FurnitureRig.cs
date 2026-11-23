using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Server;
using TF2.Descent.Server.Furniture;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Level;
using TF2.Descent.Server.Loot;
using TF2.Descent.Shared;
using TF2.Descent.Tests.Floor;
using TF2.Descent.Tests.Level;

namespace TF2.Descent.Tests.Furniture;

/// <summary>Makes furniture entities through the real registry over a recording engine, and counts each made.</summary>
internal sealed class RigFurnitureSpawner(ScriptedEntityRegistry registry) : IFurnitureSpawner
{
    private int _next = 600;

    public int Made { get; private set; }

    public int EdictCount { get; set; } = 30;

    public FurnitureEntity? Make()
    {
        Made++;
        EdictCount++;
        var h = new EntityHandle((uint)((1 << 16) | _next++));
        registry.Spawn(h, FurnitureNetState.ClassName);
        return registry.Get(h) as FurnitureEntity;
    }

    public int Edicts() => EdictCount;

    public void NewLevel() => _next = 600 + Made;
}

/// <summary>The level's rooms, each awake unless a fact puts it to sleep.</summary>
internal sealed class RigRoomActivity(Func<LevelData?> level) : IRoomActivity
{
    public HashSet<int> Asleep { get; } = [];

    public int RoomAt(Vector at) => level()?.RoomAt(at) ?? -1;

    public bool Awake(int room) => room < 0 || !Asleep.Contains(room);
}

internal sealed class RigFurnitureLevel : ILevelData
{
    public LevelData? Current { get; set; }

    public TF2.Descent.Ai.Level.HordeLevel? Horde => null;
}

/// <summary>Heroes a fact puts down, with their life and ammo, and what was asked of them.</summary>
internal sealed class RigFurnitureHeroes : IFurnitureHeroes
{
    public List<FurnitureHero> Heroes { get; } = [];
    public Dictionary<int, int> LifeOf { get; } = [];
    public Dictionary<int, int> AmmoMissing { get; } = [];
    public Dictionary<int, string[]> Spells { get; } = [];
    public List<int> Refilled { get; } = [];
    public List<int> Recomputed { get; } = [];
    public List<(int Slot, int Amount)> Healed { get; } = [];
    public List<(int Slot, int Amount)> Ammo { get; } = [];
    public int Max { get; set; } = 200;

    public FurnitureHero Add(int slot, string key, Vector at)
    {
        var hero = new FurnitureHero(slot, key, new EntityHandle((uint)slot), at, at + new Vector(0, 0, 41));
        Heroes.RemoveAll(h => h.Slot == slot);
        Heroes.Add(hero);
        LifeOf.TryAdd(slot, Max);
        return hero;
    }

    public void Move(int slot, Vector at) => Add(slot, Heroes.Single(h => h.Slot == slot).Key, at);

    public IReadOnlyList<FurnitureHero> Living() => [.. Heroes.OrderBy(h => h.Slot)];

    public FurnitureHero? Of(EntityHandle player) => Heroes.FirstOrDefault(h => h.Player == player);

    public string? KeyOf(int slot) => Heroes.FirstOrDefault(h => h.Slot == slot)?.Key;

    public IReadOnlyCollection<string> SpellsKnown(int slot) => Spells.GetValueOrDefault(slot) ?? [];

    public void Refill(int slot)
    {
        Refilled.Add(slot);
        LifeOf[slot] = Max;
    }

    public void Recompute(int slot) => Recomputed.Add(slot);

    public (int Life, int Max) Life(EntityHandle player) => (LifeOf[(int)player.Value], Max);

    public int Heal(EntityHandle player, int amount)
    {
        int slot = (int)player.Value;
        int took = Math.Max(0, Math.Min(amount, Max - LifeOf[slot]));
        LifeOf[slot] += took;
        Healed.Add((slot, took));
        return took;
    }

    public int MissingAmmo(EntityHandle player) => AmmoMissing.GetValueOrDefault((int)player.Value);

    public int GiveAmmo(EntityHandle player, int count)
    {
        int slot = (int)player.Value;
        int took = Math.Max(0, Math.Min(count, AmmoMissing.GetValueOrDefault(slot)));
        AmmoMissing[slot] = AmmoMissing.GetValueOrDefault(slot) - took;
        Ammo.Add((slot, took));
        return took;
    }
}

/// <summary>The world a fact runs: a settable clock, robots at places, and every hit, blast, sound and explosion recorded.</summary>
internal sealed class RigFurnitureWorld(FurnitureBlastScope scope) : IFurnitureWorld
{
    public sealed record Hit(EntityHandle Victim, EntityHandle Attacker, float Damage, DamageType Bits, Vector At);

    public sealed record BlastCall(Vector Origin, float Damage, float Radius, EntityHandle Attacker, EntityHandle Inflictor, EntityHandle Ignored, bool ScopeActive,
                                   bool InflictorParked);

    /// <summary>Whether an entity is parked as a blast is dealt (the rig's parking table).</summary>
    public Func<EntityHandle, bool> IsParked { get; set; } = _ => false;

    public double Now { get; set; } = 100;
    public List<Hit> Hits { get; } = [];
    public List<BlastCall> Blasts { get; } = [];
    public List<(EntityHandle Source, string Name)> Sounds { get; } = [];
    public List<Vector> Explosions { get; } = [];
    public Dictionary<EntityHandle, Vector> Robots { get; } = [];
    public bool Blocked { get; set; }

    public void Hurt(EntityHandle victim, EntityHandle attacker, float damage, DamageType bits, Vector at) =>
        Hits.Add(new Hit(victim, attacker, damage, bits, at));

    public void Blast(Vector origin, float damage, float radius, EntityHandle attacker, EntityHandle inflictor, EntityHandle ignored)
    {
        scope.Enter();
        try
        {
            Blasts.Add(new BlastCall(origin, damage, radius, attacker, inflictor, ignored, scope.Active, IsParked(inflictor)));
        }
        finally
        {
            scope.Leave();
        }
    }

    public IReadOnlyList<EntityHandle> RobotsNear(Vector origin, float radius) =>
        [.. Robots.Where(r => FurnitureRig.Distance(r.Value, origin) <= radius).Select(r => r.Key)];

    public bool Clear(Vector from, Vector to) => !Blocked;

    public void Sound(EntityHandle source, string name) => Sounds.Add((source, name));

    public void Explosion(Vector origin, float radius) => Explosions.Add(origin);
}

/// <summary>The break world a fact runs: every shatter, mark, cloud and ambush recorded; a mark takes unless a fact refuses it.</summary>
internal sealed class RigBreakWorld : IFurnitureBreakWorld
{
    public sealed record AmbushCall(string[] Templates, Vector At, float Yaw, EntityHandle Hero, ulong Seed, double HuntSeconds);

    public List<string> Precached { get; } = [];
    public List<(string Model, Vector At, float Yaw, Vector Velocity)> Shattered { get; } = [];
    public List<(EntityHandle Hero, string Condition, double Seconds, EntityHandle Provider)> Marks { get; } = [];
    public List<Vector> Clouds { get; } = [];
    public List<AmbushCall> Ambushes { get; } = [];
    public bool MarkTakes { get; set; } = true;

    public void Precache(IEnumerable<string> models) => Precached.AddRange(models);

    public void Shatter(string model, Vector origin, float yaw, Vector velocity) => Shattered.Add((model, origin, yaw, velocity));

    public bool Mark(EntityHandle hero, string condition, double seconds, EntityHandle provider)
    {
        Marks.Add((hero, condition, seconds, provider));
        return MarkTakes;
    }

    public void Cloud(Vector origin) => Clouds.Add(origin);

    public IReadOnlyList<string> Ambush(IReadOnlyList<string> templates, Vector origin, float yaw, EntityHandle hero, ulong seed, double huntSeconds)
    {
        Ambushes.Add(new AmbushCall([.. templates], origin, yaw, hero, seed, huntSeconds));
        return [.. templates.Select(t => t + " woke")];
    }
}

internal sealed class RigHazards : IHooks<IFurnitureHazardHook>, IFurnitureHazardHook
{
    public List<int> Armed { get; } = [];
    public List<int> Gone { get; } = [];
    public IReadOnlyList<IFurnitureHazardHook> All => [this];
    public void Each(string what, Action<IFurnitureHazardHook> call) => call(this);
    public void HazardArmed(int furniture, FurnitureKindId kind, string variant, Vector at, float radius) => Armed.Add(furniture);
    public void HazardGone(int furniture) => Gone.Add(furniture);
}

internal sealed class RigSkins : IServerSkins
{
    public Dictionary<EntityHandle, int> Skin { get; } = [];
    public void SetSkin(EntityHandle entity, int skin) => Skin[entity] = skin;
    public void SetBody(EntityHandle entity, int body) { }
}

/// <summary>
/// The furniture service over the real entities, registry, rules, in-memory ledger and loot floor,
/// with a recording engine under them and fakes for the spawner, the rooms, the heroes and the
/// world; the level is the committed depth 1 seed 1 level (with its seed replaced when a fact asks).
/// </summary>
internal sealed class FurnitureRig
{
    public static readonly TF2DescentRules Rules = new();
    public static readonly LevelData D1S1 = LevelFixture.Committed().Read();

    public const int SlotA = 1, SlotB = 2;
    public const string A = "hero-a", B = "hero-b";

    public FurnitureRig(ulong seed = 1, bool ready = true, LevelData? level = null, Func<IItemLedger, IItemLedger>? ledger = null)
    {
        Engine = RecordingEngine.Default();
        Engine.Returns("Entities.IsValid", true);
        Engine.ReturnsEachCall("Entities.IsPlayer", () => Engine.Calls[^1].ArgAs<EntityHandle>(0).Value is > 0 and < 64);
        Engine.ReturnsEachCall("Entities.GetIndex", () => (int)Engine.Calls[^1].ArgAs<EntityHandle>(0).EntryIndex);

        Registry = new ScriptedEntityRegistry(TestEngines.Server());
        Registry.RegisterAll(typeof(FurnitureEntity).Assembly);
        var services = new ServiceContainer();
        foreach (var (contract, _) in EngineServices.Areas(new RecordingLog()))
            services.AddInstance(contract, Engine);
        services.AddInstance<IUserMessageSender>(Engine);
        services.AddInstance<IServerSkins>(Skins);
        services.AddInstance<ILog>(Log);
        services.AddInstance(new ConsoleLines());
        Registry.Services = services;

        Level.Current = (level ?? D1S1) with { Seed = seed };
        Rooms = new RigRoomActivity(() => Level.Current);
        World = new RigFurnitureWorld(Scope);
        Spawner = new RigFurnitureSpawner(Registry);
        LootSpawner = new RigLootSpawner(Registry) { EdictCount = 0 };
        Ledger = new InMemoryItemLedger(Rules, new NoLedgerHooks(), new StartingKit(Rules));
        var used = ledger?.Invoke(Ledger) ?? Ledger;   // a fact may see what the service asks of the ledger
        Floor = new LootFloor(Mode, LootSpawner, Rooms, used, Party, Engine, Engine, new ClientMessages(Engine), Log);
        Service = new FurnitureService(Mode, Level, Rules, Spawner, Rooms, World, Heroes, used, Floor, new RigGround(), Buffs, Parking,
            Hazards, new ClientMessages(Engine), Log, Automap, Parties, Breaks);
        World.IsParked = Parking.IsParked;
        Heroes.Add(SlotA, A, new Vector(-5000, -5000, 0));
        if (ready)
            LevelReady();
    }

    public RecordingEngine Engine { get; }
    public ScriptedEntityRegistry Registry { get; }
    public RecordingLog Log { get; } = new();
    public RigSkins Skins { get; } = new();
    public RigFurnitureLevel Level { get; } = new();
    public RigRoomActivity Rooms { get; }
    public RigMode Mode { get; } = new();
    public FurnitureBlastScope Scope { get; } = new();
    public RigFurnitureWorld World { get; }
    public RigFurnitureHeroes Heroes { get; } = new();
    public RigFurnitureSpawner Spawner { get; }
    public RigLootSpawner LootSpawner { get; }
    public RigParty Party { get; } = new();
    public InMemoryItemLedger Ledger { get; }
    public LootFloor Floor { get; }
    public FurnitureBuffs Buffs { get; } = new();
    public FurnitureParking Parking { get; } = new();
    public RigHazards Hazards { get; } = new();
    public RigBreakWorld Breaks { get; } = new();
    public FurnitureService Service { get; }

    public void LevelReady()
    {
        Floor.OnLevelReady(Level.Current!.Map);
        Service.OnLevelReady(Level.Current.Map);
    }

    public void NextLevel(ulong seed)
    {
        Service.OnLevelShutdownPreEntity();
        Floor.OnLevelShutdownPreEntity();
        Spawner.NewLevel();
        LootSpawner.NewLevel();
        Level.Current = D1S1 with { Seed = seed };
        LevelReady();
    }

    /// <summary>The first row of a kind (and variant), or a fact-stopping failure naming the seed.</summary>
    /// <summary>What the furniture told the automap: the slots a sight shrine revealed every room to, and the shrines used.</summary>
    public RigAutomapReveal Automap { get; } = new();

    /// <summary>Every hero one party (the facts written before WO9), but for the slots named strangers.</summary>
    public TF2.Descent.Tests.Skills.SkillRig.RigParty Parties { get; } = new();

    /// <summary>The rows the pool stands entities on: every one but the crates and barrels, which the breakables' one entity owns.</summary>
    public IReadOnlyList<FurnitureRow> Pooled => [.. Service.Rows.Where(r => r.Kind != FurnitureKind.Breakable)];

    public FurnitureRow First(FurnitureKind kind, string? variant = null) =>
        Service.Rows.FirstOrDefault(r => r.Kind == kind && (variant is null || r.Variant == variant))
        ?? throw new InvalidOperationException($"seed {Level.Current!.Seed} placed no {kind} {variant}");

    /// <summary>The first level seed from 1 whose roll places each of <paramref name="wanted"/> (kind, optional variant).</summary>
    public static ulong SeedWith(params (FurnitureKind Kind, string? Variant)[] wanted)
    {
        var rooms = LevelAdapters.ToFurniture(D1S1);
        for (ulong seed = 1; seed < 5000; seed++)
        {
            var placed = Rules.Furniture.Placement.Furnish(seed, rooms).Placed;
            if (wanted.All(w => placed.Any(p => p.Kind == w.Kind && (w.Variant is null || p.Variant == w.Variant))))
                return seed;
        }

        throw new InvalidOperationException("no seed below 5000 places all of them");
    }

    /// <summary>
    /// Depth 1 seed 1 with two more barrel markers 100 units apart in a room that has none (its
    /// own markers never stand within a chain radius of each other), and the first seed from 1 whose
    /// roll places both: a chain.
    /// </summary>
    public static (LevelData Level, ulong Seed) WithChain()
    {
        var room = D1S1.Rooms.First(r => !D1S1.In(r.Id).Any(m => m.Kind == LevelMarkerKind.Barrel));
        var floor = room.Centre with { Z = room.Min.Z };
        int next = D1S1.Markers.Length;
        var level = D1S1 with
        {
            Markers = D1S1.Markers
                .Add(new LevelMarker(next, "descent_mark_barrel", LevelMarkerKind.Barrel, floor, 0, room.Id, ""))
                .Add(new LevelMarker(next + 1, "descent_mark_barrel", LevelMarkerKind.Barrel, floor + new Vector(100, 0, 0), 0, room.Id, "")),
        };
        var rooms = LevelAdapters.ToFurniture(level);
        string[] pair = [LevelAdapters.MarkerKey(next), LevelAdapters.MarkerKey(next + 1)];
        for (ulong seed = 1; seed < 5000; seed++)
        {
            var placed = Rules.Furniture.Placement.Furnish(seed, rooms).Placed;
            if (pair.All(m => placed.Any(p => p.Marker == m && p.Kind == FurnitureKind.Explosive)) && placed.First(p => p.Marker == pair[0]).Variant == "barrel")
                return (level, seed);
        }

        throw new InvalidOperationException("no seed below 5000 places the pair");
    }

    /// <summary>
    /// Depth 1 seed 1 with the breakable markers a level built today carries: the committed fixture
    /// predates them (it has container and barrel markers but no <c>descent_mark_breakable_*</c>).
    /// The room dresser writes each as an <c>info_poi</c> at the model's foot, turned by yaw only, at
    /// most 8 a room (d51fd6356); here every room holds a pile of two, a crate and a barrel 80 units
    /// apart on its floor at its middle, appended after the fixture's own markers so theirs keep
    /// their ids.
    /// </summary>
    public static readonly LevelData D1S1Breakables = WithBreakables(D1S1);

    /// <summary><paramref name="level"/> with a crate and a barrel marker on each room's floor (<see cref="D1S1Breakables"/>).</summary>
    public static LevelData WithBreakables(LevelData level)
    {
        var markers = level.Markers.ToBuilder();
        foreach (var room in level.Rooms)
        {
            var floor = room.Centre with { Z = room.Min.Z };
            markers.Add(new LevelMarker(markers.Count, "descent_mark_breakable_crate", LevelMarkerKind.BreakableCrate, floor - new Vector(40, 0, 0), 90, room.Id, ""));
            markers.Add(new LevelMarker(markers.Count, "descent_mark_breakable_barrel", LevelMarkerKind.BreakableBarrel, floor + new Vector(40, 0, 0), 0, room.Id, ""));
        }
        return level with { Markers = markers.ToImmutable() };
    }

    /// <summary>Puts hero <paramref name="slot"/> at <paramref name="row"/>'s place, plus <paramref name="offset"/>.</summary>
    public FurnitureHero Stand(int slot, FurnitureRow row, Vector offset = default)
    {
        string key = slot == SlotA ? A : slot == SlotB ? B : "hero-" + slot;
        return Heroes.Add(slot, key, row.Position + offset);
    }

    public FurnitureHero Hero(int slot) => Heroes.Heroes.Single(h => h.Slot == slot);

    public void Think(int frames = 1)
    {
        for (int i = 0; i < frames; i++)
            Service.OnThink(0.015f);
    }

    public static double Distance(Vector a, Vector b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

/// <summary>The automap's reveal seam, recording what it was asked.</summary>
internal sealed class RigAutomapReveal : TF2.Descent.Server.Automap.IAutomapReveal
{
    public List<int> RevealedAll { get; } = [];
    public List<string> ShrinesUsed { get; } = [];

    public void RevealAll(int playerIndex) => RevealedAll.Add(playerIndex);

    public void ShrineUsed(string markerKey) => ShrinesUsed.Add(markerKey);
}
