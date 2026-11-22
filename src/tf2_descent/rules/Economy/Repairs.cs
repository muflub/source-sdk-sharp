using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Rules.Economy;

/// <summary>Why a repair was refused.</summary>
public static class RepairRefusals
{
    /// <summary>Not gear (or a schema 1 item): nothing to repair.</summary>
    public const string NotRepairable = "not_repairable";
    /// <summary>Already at its maximum: a kit is not consumed (L7).</summary>
    public const string AtMaximum = "at_maximum";
    /// <summary>Maximum durability 0: nothing restores it (L7 as amended, open question 1).</summary>
    public const string Unusable = "unusable";
    /// <summary>The field repair recipe takes one Repair Kit and one item.</summary>
    public const string NeedsKitAndItem = "needs_kit_and_item";
    /// <summary>A field repair of what is worn needs a Repair Kit in the backpack.</summary>
    public const string NoKit = "no_kit";
    /// <summary>A field repair uses one kit: the host ends the kit it names, so a stack of them is not one.</summary>
    public const string KitStack = "kit_stack";
}

/// <summary>One step of a field repair: durability gained, maximum lost, and both after it.</summary>
public sealed record FieldRepairStep(int Gained, int MaxLost, int Durability, int MaxDurability);

/// <summary>A field repair: the item after it (never null when it ran: the item is never destroyed), its steps, or why it was refused.</summary>
public sealed record FieldRepairResult(ItemInstance? Item, IReadOnlyList<FieldRepairStep> Steps, string? Refusal)
{
    public bool Ok => Refusal is null;
}

/// <summary>
/// Repair (plan_tf2_rpg 2h, D21; ruling L7 as amended).
/// <list type="bullet">
/// <item><b>The Engineer's repair</b> (<c>IGameRules.Repair</c>) restores durability to the
/// maximum and never touches the maximum, for Australium: (base + perPoint · points missing) ·
/// (1 + perItemLevel · item level) · the rarity's repair multiplier, rounded up. Repair-all is
/// the sum.</item>
/// <item><b>The Repair Kit</b> (<c>Craft("field_repair", [kit, item])</c>) is Diablo 1's: each
/// step restores level + rand(level) durability (rand(n) is 0..n − 1) and lowers the maximum by
/// max(maximum / (level + 9), 1), until what it restored reaches the maximum. It never destroys
/// the item: a step that takes the maximum to 0 leaves it unusable (it cannot be equipped and
/// grants nothing; it can still be stashed, sold, salvaged and traded). A kit on an item at its
/// maximum, or on an unusable one, is refused and not consumed.</item>
/// </list>
/// </summary>
public sealed class Repairs
{
    private readonly DescentContent _content;
    private readonly ItemCodec _codec;

    public Repairs(DescentContent content, ItemCodec codec)
    {
        _content = content;
        _codec = codec;
    }

    private DurabilitySettings S => _content.DurabilitySettings;

    /// <summary>Points of durability missing.</summary>
    public static int Missing(ItemInstance item) => Math.Max(0, item.MaxDurability - item.Durability);

    /// <summary>Why the Engineer would refuse the item; null when he repairs it.</summary>
    public string? Refusal(ItemInstance item) =>
        !_content.ItemBases.TryGet(item.BaseId, out var b) || !b.IsGear ? RepairRefusals.NotRepairable
        : item.MaxDurability <= 0 ? RepairRefusals.Unusable
        : item.Durability >= item.MaxDurability ? RepairRefusals.AtMaximum
        : null;

    /// <summary>The Engineer's price, in Australium; 0 when nothing is missing.</summary>
    public long Price(ItemInstance item)
    {
        int missing = Missing(item);
        if (missing == 0) return 0;
        double price = (S.RepairBase + S.RepairPerPoint * missing) * (1 + S.RepairPerItemLevel * item.ItemLevel)
                       * _content.Rarities.Rows[item.Rarity].Repair;
        return (long)Math.Ceiling(price - 1e-9);
    }

    /// <summary>Repair-all: the sum of each repairable item's price.</summary>
    public long PriceAll(IEnumerable<ItemInstance> items) => items.Where(i => Refusal(i) is null).Sum(Price);

    /// <summary>The item at its maximum durability, the maximum untouched.</summary>
    public static ItemInstance Restore(ItemInstance item) => item with { Durability = item.MaxDurability, WearUses = 0 };

    /// <summary><c>IGameRules.Repair</c>: the item restored (changed in place by the host), at its price.</summary>
    public ItemChange Repair(RolledItem item)
    {
        if (item.SchemaVersion != ItemCodec.Schema) return new([], [], 0, RepairRefusals.NotRepairable);
        var instance = _codec.Instance(item);
        if (Refusal(instance) is { } refusal) return new([], [], 0, refusal);
        return new([0], [_codec.ToRolled(Restore(instance))], Price(instance));
    }

    /// <summary>Diablo 1's field repair at hero level <paramref name="level"/>, seeded (see the class summary).</summary>
    public FieldRepairResult Field(ItemInstance item, int level, ulong seed)
    {
        if (!_content.ItemBases.TryGet(item.BaseId, out var b) || !b.IsGear) return new(null, [], RepairRefusals.NotRepairable);
        if (item.MaxDurability <= 0) return new(null, [], RepairRefusals.Unusable);
        if (item.Durability >= item.MaxDurability) return new(null, [], RepairRefusals.AtMaximum);
        level = Math.Max(1, level);
        var r = SeededRandom.For(seed, "field_repair", item.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture), level.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var steps = new List<FieldRepairStep>();
        int durability = item.Durability, max = item.MaxDurability, restored = 0;
        do
        {
            int gained = level + r.Below(level);
            restored += gained;
            int lost = Math.Max(max / (level + S.LevelOffset), S.MinLoss);
            max = Math.Max(0, max - lost);
            steps.Add(new FieldRepairStep(gained, lost, Math.Min(durability + restored, max), max));
            if (max == 0) break;
        } while (restored + durability < max);
        durability = Math.Min(durability + restored, max);
        return new(item with { Durability = durability, MaxDurability = max, WearUses = 0 }, steps, null);
    }

    /// <summary>The kit a field repair uses up (durability.json's <c>fieldRepair.kit</c>).</summary>
    public string Kit => S.Kit;

    /// <summary>
    /// The hero level a field repair with <paramref name="kit"/> runs at: the hero's level when the
    /// kit came to him (its roll context's), the only hero level the contract's
    /// <c>Repair(item, Field, kit, seed)</c> carries; at least 1.
    /// </summary>
    public static int KitLevel(RolledItem kit) => Math.Max(1, kit.Context.CharacterLevel);

    /// <summary>
    /// <c>IGameRules.Repair(item, kind, kit, seed)</c> (Host.Contracts 1.1). A town repair is the
    /// Engineer's (<see cref="Repair(RolledItem)"/>: priced, the maximum untouched). A field repair
    /// takes one Repair Kit (<paramref name="kit"/>, a stack of one) and runs <see cref="Field"/> at
    /// <see cref="KitLevel"/>: the item comes back in place (<c>Produced[0]</c>), the kit is used up
    /// (<c>Consumed = [1]</c>), nothing is paid. Refused, nothing used, without a kit, on a stack of
    /// kits, or where <see cref="Field"/> refuses (at the maximum, unusable, no gear).
    /// </summary>
    public ItemChange Repair(RolledItem item, RepairKind kind, RolledItem? kit, ulong seed)
    {
        if (kind == RepairKind.Town) return Repair(item);
        if (kit is null || kit.BaseType != S.Kit || kit.SchemaVersion != ItemCodec.Schema || item.BaseType == S.Kit)
            return new([], [], 0, RepairRefusals.NeedsKitAndItem);
        if (kit.Count != 1) return new([], [], 0, RepairRefusals.KitStack);
        if (item.SchemaVersion != ItemCodec.Schema) return new([], [], 0, RepairRefusals.NotRepairable);
        var result = Field(_codec.Instance(item), KitLevel(kit), seed);
        return result.Ok ? new([1], [_codec.ToRolled(result.Item!)], 0) : new([], [], 0, result.Refusal);
    }

    /// <summary>
    /// <c>Craft("field_repair", [kit, item], ctx, seed)</c>: one Repair Kit and one item, at the
    /// hero's level (<see cref="ItemRollContext.CharacterLevel"/>). Consumes both and produces the
    /// repaired item (and the rest of a kit stack); refused, consuming nothing, when it may not run.
    /// </summary>
    public ItemChange FieldRepair(IReadOnlyList<RolledItem> inputs, ItemRollContext ctx, ulong seed)
    {
        if (inputs.Count != 2 || inputs.Any(i => i.SchemaVersion != ItemCodec.Schema))
            return new([], [], 0, RepairRefusals.NeedsKitAndItem);
        int kit = inputs[0].BaseType == S.Kit ? 0 : inputs[1].BaseType == S.Kit ? 1 : -1;
        if (kit < 0 || inputs[1 - kit].BaseType == S.Kit) return new([], [], 0, RepairRefusals.NeedsKitAndItem);
        var result = Field(_codec.Instance(inputs[1 - kit]), ctx.CharacterLevel, seed);
        if (!result.Ok) return new([], [], 0, result.Refusal);
        var produced = new List<RolledItem> { _codec.ToRolled(result.Item!) };
        var kitInstance = _codec.Instance(inputs[kit]);
        if (kitInstance.Count > 1) produced.Add(_codec.ToRolled(kitInstance with { Count = kitInstance.Count - 1 }));
        return new([0, 1], produced, 0);
    }
}
