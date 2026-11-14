using System.Globalization;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Rules.Economy;

/// <summary>Why a recipe, a salvage, an identify or a crate was refused. Nothing is consumed on a refusal.</summary>
public static class CraftRefusals
{
    public const string UnknownRecipe = "unknown_recipe";
    public const string WrongInputs = "wrong_inputs";
    public const string NotEnoughMetal = "not_enough_metal";
    public const string NotIdentified = "not_identified";
    public const string AlreadyIdentified = "already_identified";
    public const string CannotUpgrade = "cannot_upgrade";
    public const string NoSuchAffix = "no_such_affix";
    public const string WrongClass = "wrong_class";
    public const string NotSalvageable = "not_salvageable";
    public const string NotACrate = "not_a_crate";
    public const string LegacyItem = "legacy_item";
}

/// <summary>
/// The Engineer's bench, the Spy and salvage (plan_tf2_rpg 2i, 2k; D22, D24). Every recipe is a
/// pure function (inputs, context, seed) to outputs, through <c>Craft</c>, with its inputs
/// consumed all at once or not at all. A recipe that costs metal takes metal stacks among its
/// inputs, consumes them all, and returns the change as stacks, largest rung first.
/// <list type="bullet">
/// <item><c>craft_item:&lt;base&gt;</c>: a random item of that base at the hero's level, its
/// rarity on the craft odds (Stock, Vintage, Strange: never Unusual or Australium).</item>
/// <item><c>reroll:&lt;index&gt;</c>: one affix's roll again; every other affix is untouched,
/// byte for byte. The cost rises each time on that item, and a cursed stat costs a multiple.</item>
/// <item><c>upgrade</c>: one rarity up (Stock to Vintage to Strange only), filling the new
/// rarity's affix count.</item>
/// <item><c>combine_potions</c>: three potions of one size make one of the next.</item>
/// <item><c>repair_kit</c>: a Repair Kit; <c>field_repair</c>: one kit on one item (<see cref="Repairs"/>).</item>
/// <item><c>metal_up</c> / <c>metal_down</c>: three of a rung make one of the next, and back.</item>
/// <item><c>identify</c>: a Dossier identifies one item in the field.</item>
/// </list>
/// Salvage turns gear (broken, unusable or unidentified alike) into metal by its rarity and
/// item level. The Spy identifies for a fee.
/// </summary>
public sealed class Crafting
{
    private readonly DescentContent _content;
    private readonly ItemRoller _roller;
    private readonly ItemCodec _codec;
    private readonly Repairs _repairs;
    private readonly Pricing _pricing;

    public Crafting(DescentContent content, ItemRoller roller, ItemCodec codec, Repairs repairs, Pricing pricing)
    {
        _content = content;
        _roller = roller;
        _codec = codec;
        _repairs = repairs;
        _pricing = pricing;
    }

    private EconomySettings E => _content.EconomySettings;

    private static ItemChange Refuse(string why) => new([], [], 0, why);

    private static ulong Seed(ulong seed, string what, int k = 0) => SeededRandom.Mix(seed, what, k.ToString(CultureInfo.InvariantCulture));

    private string Forced(string baseId) => _content.ItemSettings.ForcedBasePrefix + baseId;

    //-------------------------------------------------------------------------
    // Metal
    //-------------------------------------------------------------------------

    /// <summary>The metal an item is worth in units of the bottom rung (scrap): 0 for anything that is not metal.</summary>
    public int MetalUnits(ItemInstance item)
    {
        var b = _content.ItemBases[item.BaseId];
        return b.Kind == ItemKinds.Metal ? b.Metal * item.Count : 0;
    }

    /// <summary><paramref name="units"/> of metal as stacks, largest rung first, each at most its stack size.</summary>
    public IReadOnlyList<ItemInstance> Metal(int units, ItemRollContext ctx, ulong seed, string source)
    {
        var stacks = new List<ItemInstance>();
        int k = 0;
        foreach (var rung in E.MetalLadder.Reverse())
        {
            var b = _content.ItemBases[rung];
            int count = units / b.Metal;
            units -= count * b.Metal;
            while (count > 0)
            {
                int n = Math.Min(count, b.Stack);
                var stack = _roller.Roll(ctx with { Tier = 0, LootTable = Forced(rung), Source = source }, Seed(seed, "metal", k++));
                stacks.Add(stack with { Count = n });
                count -= n;
            }
        }
        return stacks;
    }

    /// <summary>The metal gear salvages into: floor(rarity salvage · (1 + perItemLevel · item level)) units.</summary>
    public int SalvageUnits(ItemInstance item) =>
        (int)Math.Floor(_content.Rarities.Rows[item.Rarity].Salvage * (1 + E.SalvagePerItemLevel * item.ItemLevel) + 1e-9);

    /// <summary><c>IGameRules.Salvage</c>: gear in, its metal out.</summary>
    public ItemChange Salvage(RolledItem item, ItemRollContext ctx, ulong seed)
    {
        if (item.SchemaVersion != ItemCodec.Schema) return Refuse(CraftRefusals.LegacyItem);
        var instance = _codec.Instance(item);
        if (!_content.ItemBases[instance.BaseId].IsGear) return Refuse(CraftRefusals.NotSalvageable);
        return new([0], [.. Metal(SalvageUnits(instance), ctx, seed, "salvage").Select(_codec.ToRolled)], 0);
    }

    //-------------------------------------------------------------------------
    // Identify
    //-------------------------------------------------------------------------

    /// <summary><c>IGameRules.Identify</c>: the Spy reveals the affixes rolled at drop, for his fee. Never re-rolls.</summary>
    public ItemChange Identify(RolledItem item, ulong seed)
    {
        if (item.SchemaVersion != ItemCodec.Schema) return Refuse(CraftRefusals.LegacyItem);
        var instance = _codec.Instance(item);
        if (instance.Identified) return Refuse(CraftRefusals.AlreadyIdentified);
        return new([0], [_codec.ToRolled(instance with { Identified = true })], _pricing.IdentifyFee(instance));
    }

    //-------------------------------------------------------------------------
    // Recipes
    //-------------------------------------------------------------------------

    /// <summary><c>IGameRules.Craft</c>: a recipe by name, with its argument after a colon.</summary>
    public ItemChange Craft(string recipe, IReadOnlyList<RolledItem> inputs, ItemRollContext ctx, ulong seed)
    {
        if (inputs.Any(i => i.SchemaVersion != ItemCodec.Schema)) return Refuse(CraftRefusals.LegacyItem);        int colon = recipe.IndexOf(':');
        string name = colon < 0 ? recipe : recipe[..colon];
        string? arg = colon < 0 ? null : recipe[(colon + 1)..];
        var items = inputs.Select(_codec.Instance).ToList();
        if (name == _content.DurabilitySettings.FieldRepairRecipe && arg is null) return _repairs.FieldRepair(inputs, ctx, seed);
        if (name == E.CraftItemRecipe && arg is not null) return CraftItem(arg, items, ctx, seed);
        if (name == E.RerollRecipe && arg is not null) return Reroll(arg, items, ctx, seed);
        if (arg is not null) return Refuse(CraftRefusals.UnknownRecipe);
        if (name == E.UpgradeRecipe) return Upgrade(items, ctx, seed);
        if (name == E.PotionRecipe) return CombinePotions(items, ctx, seed);
        if (name == E.RepairKitRecipe) return RepairKit(items, ctx, seed);
        if (name == E.MetalUpRecipe) return MetalUp(items, ctx, seed);
        if (name == E.MetalDownRecipe) return MetalDown(items, ctx, seed);
        if (name == E.IdentifyRecipe) return Dossier(items, ctx, seed);
        return Refuse(CraftRefusals.UnknownRecipe);
    }

    private bool IsMetal(ItemInstance i) => _content.ItemBases[i.BaseId].Kind == ItemKinds.Metal;

    /// <summary>
    /// A recipe that pays <paramref name="cost"/> metal for <paramref name="made"/> from the
    /// subject inputs at <paramref name="subjects"/>: every other input must be metal, all of it
    /// is consumed, and the change comes back as stacks.
    /// </summary>
    private ItemChange Pay(IReadOnlyList<ItemInstance> items, IReadOnlyList<int> subjects, int cost, Func<IReadOnlyList<ItemInstance>> made,
        ItemRollContext ctx, ulong seed)
    {
        var metal = Enumerable.Range(0, items.Count).Where(i => !subjects.Contains(i)).ToList();
        if (metal.Any(i => !IsMetal(items[i]))) return Refuse(CraftRefusals.WrongInputs);
        int units = metal.Sum(i => MetalUnits(items[i]));
        if (units < cost) return Refuse(CraftRefusals.NotEnoughMetal);
        var produced = made().Concat(Metal(units - cost, ctx, Seed(seed, "change"), "craft")).Select(_codec.ToRolled).ToList();
        return new([.. subjects.Concat(metal).Order()], produced, 0);
    }

    private ItemChange CraftItem(string baseId, IReadOnlyList<ItemInstance> items, ItemRollContext ctx, ulong seed)
    {
        if (!_content.ItemBases.TryGet(baseId, out var b) || !b.IsGear) return Refuse(CraftRefusals.WrongInputs);
        if (!b.AllowsClass(ctx.CharacterClass)) return Refuse(CraftRefusals.WrongClass);
        var odds = E.CraftOdds;
        var pick = SeededRandom.For(seed, "craft_odds").Weighted(odds, o => o.Weight);
        int rarity = Index(pick.Rarity);
        int level = Math.Clamp(ctx.CharacterLevel, 1, _content.ItemSettings.MaxItemLevel);
        var made = ctx with { ItemLevel = level, Tier = rarity, LootTable = Forced(baseId), Source = "craft" };
        return Pay(items, [], E.CraftItemCost, () => [_roller.Roll(made, Seed(seed, "craft_item"))], ctx, seed);
    }

    private int Index(string rarity)
    {
        var rows = _content.Rarities.Rows;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Id == rarity) return i;
        throw new KeyNotFoundException($"no rarity '{rarity}'");
    }

    /// <summary>The metal a re-roll of affix <paramref name="index"/> costs: cost · growth^re-rolls so far, times the cursed multiplier for a cursed stat.</summary>
    public int RerollCost(ItemInstance item, int index)
    {
        bool cursed = _content.Affixes[item.Affixes[index].Id].IsCursed;
        double cost = E.RerollCost * Math.Pow(E.RerollGrowth, item.RerollCount) * (cursed ? E.RerollCursedMultiplier : 1);
        return cost >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(cost - 1e-9);
    }

    /// <summary>The first gear input; the rest pay.</summary>
    private int Subject(IReadOnlyList<ItemInstance> items) =>
        items.Select((item, i) => (item, i)).Where(p => _content.ItemBases[p.item.BaseId].IsGear).Select(p => p.i).DefaultIfEmpty(-1).First();

    private ItemChange Reroll(string arg, IReadOnlyList<ItemInstance> items, ItemRollContext ctx, ulong seed)
    {
        int subject = Subject(items);
        if (subject < 0 || items.Count(i => _content.ItemBases[i.BaseId].IsGear) != 1) return Refuse(CraftRefusals.WrongInputs);
        var item = items[subject];
        if (!item.Identified) return Refuse(CraftRefusals.NotIdentified);
        if (!int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index >= item.Affixes.Length)
            return Refuse(CraftRefusals.NoSuchAffix);
        return Pay(items, [subject], RerollCost(item, index), () => [_roller.Reroll(item, index, Seed(seed, "reroll"))], ctx, seed);
    }

    private ItemChange Upgrade(IReadOnlyList<ItemInstance> items, ItemRollContext ctx, ulong seed)
    {
        int subject = Subject(items);
        if (subject < 0 || items.Count(i => _content.ItemBases[i.BaseId].IsGear) != 1) return Refuse(CraftRefusals.WrongInputs);
        var item = items[subject];
        if (!item.Identified) return Refuse(CraftRefusals.NotIdentified);
        if (!_roller.CanUpgrade(item)) return Refuse(CraftRefusals.CannotUpgrade);
        int cost = _content.Rarities.Rows[item.Rarity].UpgradeCost;
        return Pay(items, [subject], cost, () => [_roller.Upgrade(item, Seed(seed, "upgrade"))], ctx, seed);
    }

    private ItemChange RepairKit(IReadOnlyList<ItemInstance> items, ItemRollContext ctx, ulong seed) =>
        items.Count == 0 ? Refuse(CraftRefusals.NotEnoughMetal)
        : Pay(items, [], E.RepairKitCost, () =>
            [_roller.Roll(ctx with { Tier = 0, LootTable = Forced(_content.DurabilitySettings.Kit), Source = "craft" }, Seed(seed, "repair_kit"))], ctx, seed);

    private ItemChange CombinePotions(IReadOnlyList<ItemInstance> items, ItemRollContext ctx, ulong seed)
    {
        if (items.Count != E.PotionCombine || items.Any(i => i.Count != 1)) return Refuse(CraftRefusals.WrongInputs);
        var b = _content.ItemBases[items[0].BaseId];
        if (b.Kind != ItemKinds.Potion || b.Next is null || items.Any(i => i.BaseId != b.Id)) return Refuse(CraftRefusals.WrongInputs);
        var next = _roller.Roll(ctx with { Tier = 0, LootTable = Forced(b.Next), Source = "craft" }, Seed(seed, "potion"));
        return new([.. Enumerable.Range(0, items.Count)], [_codec.ToRolled(next)], 0);
    }

    private int Rung(ItemInstance item) => E.MetalLadder.ToList().IndexOf(item.BaseId);

    private ItemChange MetalUp(IReadOnlyList<ItemInstance> items, ItemRollContext ctx, ulong seed)
    {
        if (items.Count != 1 || !IsMetal(items[0])) return Refuse(CraftRefusals.WrongInputs);
        var stack = items[0];
        int rung = Rung(stack);
        if (rung < 0 || rung + 1 >= E.MetalLadder.Count) return Refuse(CraftRefusals.WrongInputs);
        if (stack.Count < E.MetalRatio) return Refuse(CraftRefusals.NotEnoughMetal);
        var up = _roller.Roll(ctx with { Tier = 0, LootTable = Forced(E.MetalLadder[rung + 1]), Source = "craft" }, Seed(seed, "metal_up"));
        var produced = new List<ItemInstance> { up };
        if (stack.Count > E.MetalRatio) produced.Add(stack with { Count = stack.Count - E.MetalRatio });
        return new([0], [.. produced.Select(_codec.ToRolled)], 0);
    }

    private ItemChange MetalDown(IReadOnlyList<ItemInstance> items, ItemRollContext ctx, ulong seed)
    {
        if (items.Count != 1 || !IsMetal(items[0])) return Refuse(CraftRefusals.WrongInputs);
        var stack = items[0];
        int rung = Rung(stack);
        if (rung <= 0) return Refuse(CraftRefusals.WrongInputs);
        var down = _roller.Roll(ctx with { Tier = 0, LootTable = Forced(E.MetalLadder[rung - 1]), Source = "craft" }, Seed(seed, "metal_down"))
            with { Count = E.MetalRatio };
        var produced = new List<ItemInstance> { down };
        if (stack.Count > 1) produced.Add(stack with { Count = stack.Count - 1 });
        return new([0], [.. produced.Select(_codec.ToRolled)], 0);
    }

    private ItemChange Dossier(IReadOnlyList<ItemInstance> items, ItemRollContext ctx, ulong seed)
    {
        if (items.Count != 2) return Refuse(CraftRefusals.WrongInputs);
        int dossier = items[0].BaseId == E.Dossier ? 0 : items[1].BaseId == E.Dossier ? 1 : -1;
        if (dossier < 0) return Refuse(CraftRefusals.WrongInputs);
        var item = items[1 - dossier];
        if (!_content.ItemBases[item.BaseId].IsGear) return Refuse(CraftRefusals.WrongInputs);
        if (item.Identified) return Refuse(CraftRefusals.AlreadyIdentified);
        var produced = new List<ItemInstance> { item with { Identified = true } };
        if (items[dossier].Count > 1) produced.Add(items[dossier] with { Count = items[dossier].Count - 1 });
        return new([0, 1], [.. produced.Select(_codec.ToRolled)], 0);
    }

    //-------------------------------------------------------------------------
    // Gambling (2j, D23)
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>IGameRules.OpenCrate</c>: a sealed crate opens into one of the bases it holds, at the
    /// opener's level ± the spread, its rarity on the published gambling odds, unidentified.
    /// </summary>
    public ItemChange OpenCrate(RolledItem crate, ItemRollContext ctx, ulong seed)
    {
        if (crate.SchemaVersion != ItemCodec.Schema) return Refuse(CraftRefusals.LegacyItem);        var instance = _codec.Instance(crate);
        var b = _content.ItemBases[instance.BaseId];
        if (b.Kind != ItemKinds.Crate) return Refuse(CraftRefusals.NotACrate);
        var r = SeededRandom.For(seed, "crate", instance.Seed.ToString(CultureInfo.InvariantCulture));
        int level = Math.Clamp(ctx.CharacterLevel + r.Between(-E.GambleLevelSpread, E.GambleLevelSpread), 1, _content.ItemSettings.MaxItemLevel);
        string baseId = b.Contains[r.Below(b.Contains.Count)];
        int rarity = Index(r.Weighted(E.GambleOdds, o => o.Weight).Rarity);
        var made = ctx with { ItemLevel = level, Tier = rarity, LootTable = Forced(baseId), Source = "crate" };
        var item = _roller.Roll(made, Seed(seed, "crate_item")) with { Identified = false };
        return new([0], [_codec.ToRolled(item)], 0);
    }

    /// <summary>The published chance a crate opens into each rarity, by rarity index.</summary>
    public IReadOnlyDictionary<int, double> GambleOdds()
    {
        double total = E.GambleOdds.Sum(o => o.Weight);
        return E.GambleOdds.ToDictionary(o => Index(o.Rarity), o => o.Weight / total);
    }

    /// <summary>
    /// The hook RU14's economy model reads for 2j's gate ("the expected Australium cost of an
    /// Unusual from gambling is above the cost from playing"): the expected Australium spent on
    /// crates per Unusual at a buyer's level. This package does not make the comparison.
    /// </summary>
    public double ExpectedCostPerUnusualFromGambling(int level)
    {
        int unusual = _roller.UnusualRarity();
        double p = GambleOdds().TryGetValue(unusual, out var chance) ? chance : 0;
        return p > 0 ? _pricing.CratePrice(level) / p : double.PositiveInfinity;
    }
}
