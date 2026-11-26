using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Bestiary;
using TF2.Descent.Rules.Death;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Rules.Layout;
using TF2.Descent.Rules.Loot;
using TF2.Descent.Rules.Sheets;
using TF2.Descent.Rules.Skills;
using TF2.Descent.Rules.Spells;

namespace TF2.Descent.Rules;

/// <summary>
/// TF2Descent's rules module: what the hosting service replays rolls with
/// (Host.Contracts' IGameRules, plan_host_sdk §2.1). It starts as the host's
/// Host.FakeRules, behaviour for behaviour, so the host's fixtures predict it;
/// TF2Descent's own tables replace these as the game gains them.
///
/// <para>
/// Deterministic: every roll is a hash of its inputs, with no clock, no
/// unseeded randomness and no I/O, because the service replays it in a process
/// of its own and must get the same answer.
/// </para>
/// </summary>
public sealed class TF2DescentRules : IGameRules
{
    /// <summary>
    /// The one constructor, the one the host calls (Activator) and the server's registry
    /// fills: it loads the content shipped in this assembly, and throws naming every
    /// error when that content does not load.
    /// </summary>
    public TF2DescentRules()
    {
        Content = DescentContent.Shipped();
        Heroes = new HeroRules(Content);
        SheetCodec = new SheetCodec(Heroes);
        Evidence = new EvidenceCodec();
        Deaths = new DeathRules(Content);
        Downed = new DownedRules(Content);
        Bestiary = new BestiaryRules(Content);
        Furniture = new FurnitureRules(Content);
        Robots = new RobotRules(Content);
        Temperaments = new TemperamentRules(Content, Robots);
        Packs = new PackRules(Content, Robots, Temperaments);
        Statuses = new StatusRules(Content);
        StatusBook = new StatusBook(Content);
        Conditions = new RobotConditionRules(Content);
        BlastPush = new BlastPushRules(Content);
        Attacks = new RobotAttackRules(Content);
        Bosses = new Bosses.BossRules(Content);
        Skills = new SkillRules(Content, Heroes);
        Casting = new CastRules(Skills);
        Spells = new SpellRules(Content, Skills, Casting);
        ItemRules = new ItemRules(Content, Heroes);
        Repairs = new Repairs(Content, ItemRules.Codec);
        Pricing = new Pricing(Content, ItemRules.Roller);
        Crafting = new Crafting(Content, ItemRules.Roller, ItemRules.Codec, Repairs, Pricing);
        Inventory = new InventoryRules(Content, ItemRules);
        Trades = new Trades(Inventory);
        Vendors = new Vendors(Content, ItemRules.Roller, ItemRules.Codec);
        Loot = new LootRules(Content, Robots, ItemRules);
        Experience = new ExperienceRules(Content, Robots);
        Checkpoints = new CheckpointRules(Heroes, SheetCodec, Evidence, Experience);
        EquipSlots = Inventory.EquipSlotIds;
    }

    /// <summary>The XP a kill pays a hero, and the most a kill could pay (RU4, D10).</summary>
    public ExperienceRules Experience { get; }

    /// <summary>Kill rolls per seat, the Australium split, boss, champion and container loot (RU4).</summary>
    public LootRules Loot { get; }

    /// <summary>Tomes, scrolls and casting spells (RU6, R27).</summary>
    public SpellRules Spells { get; }

    /// <summary>The skill tree's rules: buying ranks, bonus ranks, resolving a skill, its contributions (RU6).</summary>
    public SkillRules Skills { get; }

    /// <summary>Casting: mana, cooldowns, toggles, Orders (RU6).</summary>
    public CastRules Casting { get; }

    /// <summary>The content tables these rules read (RU1). Built here, held by this instance (SS0010).</summary>
    public DescentContent Content { get; }

    /// <summary>The hero sheet's rules over that content (RU2).</summary>
    public HeroRules Heroes { get; }

    /// <summary>The sheet's bytes, schema 3 (RU13, the skill bar); schemas 1 and 2 still decode.</summary>
    public SheetCodec SheetCodec { get; }

    /// <summary>The checkpoint evidence's bytes (RU13).</summary>
    public EvidenceCodec Evidence { get; }

    /// <summary>What a checkpoint must satisfy (RU13).</summary>
    public CheckpointRules Checkpoints { get; }

    /// <summary>What a death drops, who loots the corpse, the Lost &amp; Found and hardcore (RU10).</summary>
    public DeathRules Deaths { get; }

    /// <summary>The party's downed state and revives (RU10, R26).</summary>
    public DownedRules Downed { get; }

    /// <summary>What a hero's kills of a robot template let it see (RU12, L6).</summary>
    public BestiaryRules Bestiary { get; }

    /// <summary>Shrines, containers, the Dispenser, explosives and traps, and where they stand (RU11).</summary>
    public FurnitureRules Furniture { get; }

    /// <summary>Robot templates, scale, stat curves, champion affixes, resistances (RU7).</summary>
    public RobotRules Robots { get; }

    /// <summary>The temperament point-buy (RU7, B.11).</summary>
    public TemperamentRules Temperaments { get; }

    /// <summary>The roster roll: one pack's robots (RU7).</summary>
    public PackRules Packs { get; }

    /// <summary>Robot status effects (RU7, E9).</summary>
    public StatusRules Statuses { get; }

    /// <summary>The same status rules over a robot's fixed slots, allocating nothing (HO8: the roster's).</summary>
    public StatusBook StatusBook { get; }

    /// <summary>What a skill's condition words do to robots, and which robots a target word is for (HO8).</summary>
    public RobotConditionRules Conditions { get; }

    /// <summary>The push a blast gives a robot (RU7, L12).</summary>
    public BlastPushRules BlastPush { get; }

    /// <summary>A robot's attack in numbers and a medic's heal (HO6, HO7).</summary>
    public RobotAttackRules Attacks { get; }

    /// <summary>The bosses' scripts and phases, the Tank, the camps a kill opens (HO13).</summary>
    public Bosses.BossRules Bosses { get; }
    /// <summary>The items' rules (RU3): the roll, the codec, the views, equipment.</summary>
    public ItemRules ItemRules { get; }

    /// <summary>The Engineer's repair and the Repair Kit (RU8).</summary>
    public Repairs Repairs { get; }

    /// <summary>Item values, the identify fee and crate prices (RU9).</summary>
    public Pricing Pricing { get; }

    /// <summary>Recipes, salvage, identify and crates (RU9).</summary>
    public Crafting Crafting { get; }

    /// <summary>The backpack, equipment, belt, stash and vendors' buy, sell and buy-back (RU5).</summary>
    public InventoryRules Inventory { get; }

    /// <summary>The two-phase trade (RU5).</summary>
    public Trades Trades { get; }

    /// <summary>Vendor stock (RU5).</summary>
    public Vendors Vendors { get; }

    /// <summary>
    /// What a new hero of <paramref name="classId"/> starts with (starting_kits.json, HE0): the
    /// gear bases for the backpack and the potions for the belt. An empty kit for a class the
    /// table does not name.
    /// </summary>
    public StartingKitRow StartingKit(string classId) =>
        Content.StartingKits.TryGet(classId, out var kit) ? kit : new StartingKitRow(classId, [], []);

    /// <summary>The content's SHA-256, for the game and the host to log side by side.</summary>
    public string ContentHash => Content.Hash;

    /// <summary>Added to every kill roll's hash: two modules with different salts replay differently.</summary>
    public ulong Salt { get; init; }

    /// <summary>The rarity names, in tier order (rarities.json).</summary>
    public IReadOnlyList<string> Tiers => ItemRules.Tiers;
    public ISheetCodec Sheets => SheetCodec;
    public IItemCodec Items => ItemRules.Codec;

    public static ulong Hash(params object[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)));
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    /// <summary>One item for a context (RU3): <see cref="ItemRoller.Roll"/>, in schema 2 bytes.</summary>
    public RolledItem RollItem(ItemRollContext ctx, ulong seed) => ItemRules.Roll(ctx, seed);

    /// <summary>True when the item is what its context, seed and history give (RU3); a schema 1 item by its own formula.</summary>
    public bool Replays(RolledItem item) => ItemRules.Replays(item);

    /// <summary>
    /// One party seat's roll of a kill (RU4, <see cref="LootRules.Roll"/>): <paramref name="killSeq"/>
    /// is <see cref="LootRules.KillSeq"/>(kill, seat), so each eligible player rolls once (R4,
    /// X1); seat 0's roll carries the kill's Australium. <paramref name="robotTemplate"/> may
    /// name champion affixes (<see cref="LootKey"/>). <see cref="Salt"/> is mixed into the roll.
    /// </summary>
    public KillRoll KillRoll(ulong instanceSeed, uint killSeq, string robotTemplate, int depth, int partySize) =>
        LootRules.ToContract(Loot.Roll(instanceSeed, killSeq, robotTemplate, depth, partySize, Salt));

    /// <summary>A boss's own table (D13) or a champion's, for one player; nothing for an ordinary robot (RU4, <see cref="LootRules.BossDrops"/>).</summary>
    public IReadOnlyList<RolledItem> BossDrops(ItemRollContext ctx, string robotTemplate, ulong seed) =>
        Loot.BossDrops(ctx, robotTemplate, seed);

    /// <summary>Backpack capacity, derived from the sheet's skill ranks and never stored (RU5, R13, D4).</summary>
    public int BackpackSlots(CharacterSheet sheet) => Inventory.BackpackSlots(SheetCodec.Read(sheet));

    /// <summary>Stash capacity: the highest storage rank among the account's living characters (RU5, D4).</summary>
    public int StashSlots(IReadOnlyList<CharacterSheet> living) => Inventory.StashSlots(living.Select(SheetCodec.Read));

    /// <summary>A level 1 hero of the class at its class starts (schema 3). An unknown class throws, naming the nine.</summary>
    public CharacterSheet NewCharacter(string className, string name, bool hardcore) =>
        SheetCodec.Write(Heroes.New(className, name) with { Hardcore = hardcore });

    public ValidationResult ValidateCheckpoint(CharacterSheet before, CharacterSheet after, CheckpointEvidence evidence) =>
        Checkpoints.Validate(before, after, evidence);

    /// <summary>An operator's edit (contract 1.3, plan X4): <see cref="CheckpointRules.ValidateEdit"/>, so an edit may raise XP and level.</summary>
    public ValidationResult ValidateEdit(CharacterSheet before, CharacterSheet after) => Checkpoints.ValidateEdit(before, after);

    /// <summary>The host's last check of a confirmed trade (RU5, <see cref="Trades.Apply"/>).</summary>
    public TradeResult ApplyTrade(TradeState t) => Trades.Apply(t);

    private readonly LevelLayout _layout = new();

    /// <summary>
    /// The level for a layout key: <see cref="LevelLayout"/>'s when the depth's rooms are named
    /// in the layout codec (<see cref="RoomName"/>, E8), else the three-room row a library of
    /// free names (the host's fake libraries) has always had. A pool that mixes the two, or one
    /// the generator cannot make a level from, throws naming why.
    /// </summary>
    public LevelPlan GenerateLayout(LayoutKey key)
    {
        var rooms = key.Rooms.Where(r => key.Depth >= r.MinDepth && key.Depth <= r.MaxDepth).ToList();
        if (rooms.Count == 0) rooms = [.. key.Rooms];
        if (rooms.Any(r => RoomName.TryParse(r.Name, out _, out _)))
        {
            var level = _layout.Generate(key.Depth, key.Seed, key.Rooms);
            return level.Plan ?? throw new InvalidOperationException(
                $"no layout of library '{key.Library}' at depth {key.Depth}, seed {key.Seed}: {level.Error}");
        }
        return Row(key, rooms);
    }

    /// <summary>The placeholder layout: arrival, one room, exit, in a row.</summary>
    private static LevelPlan Row(LayoutKey key, List<RoomInfo> rooms)
    {
        var up = rooms.FirstOrDefault(r => r.Tags.Contains("start"))?.Name ?? rooms[0].Name;
        var down = rooms.FirstOrDefault(r => r.Tags.Contains("stairs"))?.Name ?? rooms[^1].Name;
        var placements = new List<Placement> { new(up, 0, 0, 0, "up") };
        var fill = rooms.Where(r => r.Name != up && r.Name != down).ToList();
        if (fill.Count > 0)
            placements.Add(new Placement(fill[(int)(Hash("layout", key.Seed, key.Depth) % (ulong)fill.Count)].Name, 0, 1, 90, "fill"));
        placements.Add(new Placement(down, 0, placements.Count, 0, "down"));
        return new LevelPlan(1, placements.Count, placements, up, down);
    }

    /// <summary>
    /// The host's level row for a depth, from levels.json (RU7): its item level, loot table and
    /// monster level. A depth past the deepest row reads the deepest; one below 1 reads depth 1.
    /// </summary>
    public LevelRow LevelTable(int depth)
    {
        var row = Robots.Depth(depth);
        return new LevelRow(depth, row.ItemLevel, row.LootTable, row.MonsterLevel);
    }

    /// <summary>A vendor's stock for one seed (RU5); "saxton@12@soldier" names the buyer's level and class (<see cref="Vendors"/>).</summary>
    public IReadOnlyList<RolledItem> VendorStock(string vendor, ulong seed) => Vendors.Stock(vendor, seed);

    /// <summary>
    /// A buy, a sell or the vendor value, in Australium (RU9, <see cref="Pricing"/>). A schema 1
    /// item keeps the first rules' price: 10 · (rarity + 1) · item level · count, a quarter to sell.
    /// </summary>
    public long Price(RolledItem item, PriceKind kind)
    {
        if (item.SchemaVersion == ItemCodec.Schema) return Pricing.Price(ItemRules.Codec.Instance(item), kind);
        var buy = 10L * (item.Rarity + 1) * Math.Max(1, item.ItemLevel) * item.Count;
        return kind switch { PriceKind.Buy => buy, PriceKind.Sell => buy / 4, _ => buy / 2 };
    }

    /// <summary>A sealed crate opened on the published odds (RU9, D23).</summary>
    public ItemChange OpenCrate(RolledItem crate, ItemRollContext ctx, ulong seed) => Crafting.OpenCrate(crate, ctx, seed);

    /// <summary>A recipe at the Engineer's bench, the Repair Kit or a Dossier (RU8, RU9, D24).</summary>
    public ItemChange Craft(string recipe, IReadOnlyList<RolledItem> inputs, ItemRollContext ctx, ulong seed) =>
        Crafting.Craft(recipe, inputs, ctx, seed);

    /// <summary>The Spy identifies for a fee scaled by item level (RU9, D22); identifying never rolls again.</summary>
    public ItemChange Identify(RolledItem item, ulong seed) => Crafting.Identify(item, seed);

    /// <summary>The Engineer's repair (RU8): durability to its maximum, never the maximum, priced.</summary>
    public ItemChange Repair(RolledItem item) => Repairs.Repair(item);

    /// <summary>Gear to metal by rarity and item level (RU9, D24).</summary>
    public ItemChange Salvage(RolledItem item, ItemRollContext ctx, ulong seed) => Crafting.Salvage(item, ctx, seed);

    //-------------------------------------------------------------------------
    // Host.Contracts 1.1: the three containers, wear, both repairs, use, the kit, containers
    //-------------------------------------------------------------------------

    /// <summary>The equipment slots in inventory.json's order (contract 1.1): an equipped item's slot is its index here.</summary>
    public IReadOnlyList<string> EquipSlots { get; }

    /// <summary>The backpack's capacity by the sheet, the nine equipment slots, the belt's four (contract 1.1).</summary>
    public int ContainerSlots(CharacterSheet sheet, ItemContainer container) => container switch
    {
        ItemContainer.Backpack => BackpackSlots(sheet),
        ItemContainer.Equipped => EquipSlots.Count,
        ItemContainer.Belt => Inventory.BeltSlots,
        _ => 0,
    };

    /// <summary>
    /// The inventory's refusal of a placement (contract 1.1, <see cref="InventoryRules.PlaceRefusal"/>):
    /// potions only on the belt, gear in the slot its base takes for a hero who may wear it. An
    /// item of an older schema goes only in the backpack.
    /// </summary>
    public string? PlaceRefusal(CharacterSheet sheet, RolledItem item, ItemContainer container, int slot)
    {
        if (item.SchemaVersion != ItemCodec.Schema)
            return container == ItemContainer.Backpack ? null : InventoryRefusals.WrongSlot;
        return Inventory.PlaceRefusal(SheetCodec.Read(sheet), ItemRules.Codec.Instance(item), container, slot);
    }

    /// <summary>A potion drunk, a scroll read (contract 1.1, <see cref="InventoryRules.UseUp"/>): the last ends the item, else the stack is re-encoded one lower.</summary>
    public ItemChange Consume(CharacterSheet sheet, RolledItem item, int count) =>
        item.SchemaVersion != ItemCodec.Schema ? new([], [], 0, InventoryRefusals.NotConsumable)
        : Inventory.UseUp(ItemRules.Codec.Instance(item), count);

    /// <summary>
    /// Wear (contract 1.1): <paramref name="amount"/> uses of a weapon or hits on armour, by the
    /// rules' <see cref="Durability"/>, wear progress kept: <c>Produced[0]</c> is the item in place.
    /// Anything that does not wear comes back as it was; an amount below 1 is refused (<c>bad_amount</c>).
    /// </summary>
    public ItemChange Wear(RolledItem item, int amount)
    {
        if (amount < 1) return new([], [], 0, WearRefusals.BadAmount);
        if (item.SchemaVersion != ItemCodec.Schema) return new([], [], 0, RepairRefusals.NotRepairable);
        return new([], [ItemRules.Codec.ToRolled(ItemRules.Durability.Worn(ItemRules.Codec.Instance(item), amount, amount))], 0);
    }

    /// <summary>Whether <paramref name="sheet"/>'s perks waive the Engineer's price (<see cref="Repairs.TownFree"/> over the sheet's skill stats).</summary>
    public bool TownFree(HeroSheet sheet) => Repairs.TownFree(Skills.Stats(sheet, SkillBonuses.None));

    /// <summary>
    /// What a new hero of <paramref name="className"/> starts with (contract 1.1; starting_kits.json):
    /// each gear base equipped in the first equipment slot its base takes, each potion on the belt
    /// slot by slot, every item a Stock item of its base at item level 1 rolled under
    /// <see cref="StartingKitRow.Source"/> from <paramref name="seed"/> (so each replays). The
    /// unhosted ledger starts a hero with the same bases (gear in the backpack, which the first kit
    /// then equips; the potions on the belt).
    /// </summary>
    public IReadOnlyList<StartingItem> StartingKit(string className, CharacterSheet sheet, ulong seed)
    {
        var hero = SheetCodec.Read(sheet);
        var row = StartingKit(className);
        var kit = new List<StartingItem>();
        var taken = new HashSet<int>();
        int n = 0;
        RolledItem Roll(string baseId) =>
            ItemRules.Roll(new ItemRollContext(className, hero.Level, 0, 1, 0, Content.ItemSettings.ForcedBasePrefix + baseId, StartingKitRow.Source),
                Hash("starting_kit", seed, className, n++));
        var slots = Content.InventorySettings.Equipment;
        foreach (var b in row.Backpack)
        {
            string accepts = Content.ItemBases[b].Slot;
            int slot = Enumerable.Range(0, slots.Count).FirstOrDefault(i => slots[i].Accepts == accepts && !taken.Contains(i), -1);
            taken.Add(slot);
            kit.Add(new StartingItem(Roll(b), ItemContainer.Equipped, slot));
        }
        for (int i = 0; i < row.Belt.Count; i++)
            kit.Add(new StartingItem(Roll(row.Belt[i]), ItemContainer.Belt, i));
        return kit;
    }

    /// <summary>
    /// What a world container gives one opener (contract 1.1): the rules' container roll for the
    /// instance, the container's number and the opener (<see cref="LootRules.ContainerDrops"/>).
    /// </summary>
    public IReadOnlyList<RolledItem> ContainerDrops(ulong instanceSeed, uint containerSeq, int depth, CharacterSheet opener)
    {
        var hero = SheetCodec.Read(opener);
        return Loot.ContainerDrops(instanceSeed, containerSeq, depth, hero.Name, hero.ClassId, hero.Level);
    }

    //-------------------------------------------------------------------------
    // Host.Contracts 1.2: the Australium pile, repairs by the hero, breakables, merges
    //-------------------------------------------------------------------------

    /// <summary>
    /// A kill's Australium pile divided when it is picked up (contract 1.2; the user's ruling L24):
    /// <see cref="LootRules.Split"/> among <paramref name="killerFirst"/> (the remainder one apiece
    /// from the killer), each share raised by its member's own <c>australium_find_pct</c>
    /// (<see cref="LootRules.FoundShare"/>: Scavenger, read from the sheet's skill ranks), and never
    /// past the whole pile.
    /// </summary>
    public IReadOnlyList<long> AustraliumShares(long total, IReadOnlyList<CharacterSheet> killerFirst)
    {
        if (killerFirst.Count == 0)
            return [];
        var split = LootRules.Split(Math.Max(0, total), killerFirst.Count);
        return [.. killerFirst.Select((s, i) => Math.Min(Math.Max(0, total), LootRules.FoundShare(split[i], AustraliumFindPct(SheetCodec.Read(s)))))];
    }

    /// <summary>The Australium a hero finds beyond his share, in percent: his sheet's skill stats' <c>australium_find_pct</c>.</summary>
    public double AustraliumFindPct(HeroSheet sheet) => Skills.Stats(sheet, SkillBonuses.None).Total("australium_find_pct");

    /// <summary>
    /// A repair by a hero (contract 1.2, <see cref="Repairs.Repair(RolledItem, RepairKind, RolledItem?, int, bool, ulong)"/>):
    /// a field repair runs at the repairer's level, a town repair is free when his perks waive it
    /// (<see cref="TownFree"/>). The 1.1 member, which carries no sheet, is the contract's default:
    /// the town's price, and no field repair.
    /// </summary>
    public ItemChange Repair(RolledItem item, RepairKind kind, RolledItem? kit, CharacterSheet sheet, ulong seed)
    {
        var hero = SheetCodec.Read(sheet);
        return Repairs.Repair(item, kind, kit, hero.Level, TownFree(hero), seed);
    }

    /// <summary>
    /// One numbered breakable broken by <paramref name="opener"/> (contract 1.2): the outcome keyed on
    /// the instance, the number and the depth (<see cref="BreakRules.Hosted"/>), so every hero agrees;
    /// a share of his own for Australium, a potion or an item (<see cref="LootRules.Breakable"/> from
    /// <see cref="BreakRules.HostedRequest"/>, its item as a container's); the empty roll for nothing
    /// and for a blast, a cloud or an ambush, which the game carries out from the same outcome.
    /// </summary>
    public BreakableRoll BreakableDrops(ulong instanceSeed, uint breakableSeq, int depth, CharacterSheet opener)
    {
        var outcome = Furniture.Breaks.Hosted(instanceSeed, breakableSeq, depth);
        if (!BreakableSettings.PerPlayer(outcome))
            return new(0, []);
        var hero = SheetCodec.Read(opener);
        var request = Furniture.Breaks.HostedRequest(instanceSeed, breakableSeq, hero.Name, hero.ClassId);
        var share = Loot.Breakable(request, outcome, depth);
        var item = Loot.ContainerItem(hero.ClassId, hero.Level, depth, share.Item ? share.Tier : -1,
            share.Outcome == LootOutcome.Potion ? share.Potion : null, request.Seed);
        return new(share.Australium, item is null ? [] : [ItemRules.Codec.ToRolled(item)]);
    }

    /// <summary>Two stacks of one kind become one (contract 1.2, <see cref="InventoryRules.Merge"/>); an item of an older schema does not stack.</summary>
    public ItemChange Merge(RolledItem source, RolledItem target) =>
        source.SchemaVersion != ItemCodec.Schema || target.SchemaVersion != ItemCodec.Schema
            ? new([], [], 0, InventoryRefusals.NotStackable)
            : Inventory.Merge(ItemRules.Codec.Instance(source), ItemRules.Codec.Instance(target));
}

/// <summary>Why the contract's <c>Wear</c> was refused.</summary>
public static class WearRefusals
{
    /// <summary>An amount of wear below 1 (the host refuses one itself, with the same word).</summary>
    public const string BadAmount = "bad_amount";
}
