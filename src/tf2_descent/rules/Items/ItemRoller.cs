using System.Collections.Immutable;
using System.Globalization;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Items;

/// <summary>
/// The item roll (plan_tf2_rpg 2b, 2i; R28; plan_descent_logic RU3), pure in (context, seed):
/// the base, the rarity, the positive affixes from the prefix and suffix pools gated by item
/// level, cursed stats beside them, the unusual chance by power, and the history operations
/// (re-roll, upgrade) the host replays. Every number is the content's.
///
/// <para><b>Power.</b> W is the sum of the item's positive affixes' power weights. A positive
/// affix rolls r in [0, 1]; its value sits at r·C of its range, where C = 1 + the budgets of
/// the item's cursed stats (the ceiling), so on a cursed item it can pass the uncursed top.
/// A cursed stat of severity s costs s·budget·W. Net power = Σ w·r·C − Σ s·budget·W: a curse
/// costs, on average, exactly what its budget buys, so cursed is a trade. The unusual power
/// is affixShare·(net / W) + (1 − affixShare)·(item level / max item level), clamped to
/// 0..1, and P(Unusual) = the rarity's unusualBase × power².</para>
/// </summary>
public sealed class ItemRoller
{
    private readonly DescentContent _content;

    public ItemRoller(DescentContent content) => _content = content;

    private ItemSettings S => _content.ItemSettings;

    //-------------------------------------------------------------------------
    // The roll
    //-------------------------------------------------------------------------

    /// <summary>The item for <paramref name="ctx"/> and <paramref name="seed"/>; the same inputs give the same item.</summary>
    public ItemInstance Roll(ItemRollContext ctx, ulong seed)
    {
        var r = SeededRandom.For(seed, "item", ctx.CharacterClass, Num(ctx.CharacterLevel), Num(ctx.Depth), Num(ctx.ItemLevel),
            Num(ctx.Tier), ctx.LootTable, ctx.Source);
        var rarities = _content.Rarities;
        int ilvl = Math.Clamp(ctx.ItemLevel, 1, S.MaxItemLevel);
        int rarity = Math.Clamp(ctx.Tier, 0, rarities.Count - 1);
        var source = S.SourceOf(ctx.Source);
        string? forced = ctx.LootTable.StartsWith(S.ForcedBasePrefix, StringComparison.Ordinal) ? ctx.LootTable[S.ForcedBasePrefix.Length..] : null;
        if (forced is not null && !_content.ItemBases.Contains(forced))
            throw new ArgumentException($"loot table '{ctx.LootTable}' names '{forced}', which is not an item base", nameof(ctx));

        var item = new ItemInstance { BaseId = "", Context = ctx, Seed = seed, ItemLevel = ilvl, Tier = ctx.Tier };

        // A unique rarity rolls a hand-authored unique; with none to give, the rarity steps down.
        while (rarities.Rows[rarity].Unique)
        {
            var unique = PickUnique(ctx, ilvl, forced, r);
            if (unique is not null)
                return Unique(item, unique, rarity, source);
            rarity--;
        }

        var itemBase = forced is not null ? _content.ItemBases[forced] : PickBase(ctx.CharacterClass, ilvl, r);
        if (!itemBase.IsGear)
            return item with { BaseId = itemBase.Id, Rarity = 0, Identified = true };

        var rr = rarities.Rows[rarity];
        item = item with
        {
            BaseId = itemBase.Id,
            Rarity = rarity,
            Identified = source.Identified ?? rr.Identified,
            Durability = itemBase.Durability,
            MaxDurability = itemBase.Durability,
        };

        int positives = r.Between(rr.MinAffixes, rr.MaxAffixes);
        var curses = new List<RolledAffix>();
        if (rr.Curses && source.Curses)
        {
            double chance = S.Curses.ChanceAt(ctx.Depth);
            for (int k = 0; k < S.Curses.MaxCount; k++)
            {
                if (r.Unit() >= chance) break;
                var curse = PickAffix(itemBase, ilvl, AffixPools.Cursed, Stats(curses), r);
                if (curse is null) break;
                curses.Add(new RolledAffix(curse.Id, r.Between(0, S.RollScale)));
                chance *= S.Curses.SecondChance;
            }
        }
        var rolled = AddPositives([], curses, itemBase, ilvl, positives, r);
        item = item with { Affixes = [.. rolled, .. curses] };

        // A drop may become Unusual by its power (2b); the top-up fills Unusual's affix count.
        int unusual = UnusualRarity();
        if (source.Promote && rr.UnusualBase > 0 && unusual >= 0 && PromotesToUnusual(rarity, UnusualPower(item), r))
        {
            var ur = rarities.Rows[unusual];
            int target = r.Between(Math.Max(rolled.Count, ur.MinAffixes), ur.MaxAffixes);
            rolled = AddPositives(rolled, curses, itemBase, ilvl, target - rolled.Count, r);
            item = item with
            {
                Rarity = unusual,
                Affixes = [.. rolled, .. curses],
                Identified = source.Identified ?? ur.Identified,
            };
        }
        if (rarities.Rows[item.Rarity].Unusual)
            item = item with { UnusualEffect = r.Weighted(_content.UnusualEffects.Rows, e => e.Weight).Id };
        return item;
    }

    private static string Num(long n) => n.ToString(CultureInfo.InvariantCulture);

    private ItemInstance Unique(ItemInstance item, UniqueRow unique, int rarity, SourceRule source)
    {
        var b = _content.ItemBases[unique.Base];
        var rr = _content.Rarities.Rows[rarity];
        return item with
        {
            BaseId = b.Id,
            Rarity = rarity,
            Unique = unique.Id,
            Affixes = [.. unique.Affixes.Select(a => new RolledAffix(a.Affix, (int)Math.Round(a.Roll * S.RollScale, MidpointRounding.AwayFromZero)))],
            UnusualEffect = unique.Effect,
            Identified = source.Identified ?? rr.Identified,
            Durability = b.Durability,
            MaxDurability = b.Durability,
        };
    }

    /// <summary>The index of the first rarity that carries an unusual effect; -1 when none does.</summary>
    public int UnusualRarity()
    {
        var rows = _content.Rarities.Rows;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Unusual) return i;
        return -1;
    }

    /// <summary>
    /// A drop's base: by drop weight among the drop kinds, at or below the item level, that the
    /// class may use; failing that any class's; failing that the lowest-level base.
    /// </summary>
    public ItemBaseRow PickBase(string classId, int itemLevel, SeededRandom r)
    {
        var droppable = _content.ItemBases.Rows.Where(b => b.DropWeight > 0 && S.DropKinds.Contains(b.Kind)).ToList();
        var pool = droppable.Where(b => b.Level <= itemLevel && b.AllowsClass(classId)).ToList();
        if (pool.Count == 0) pool = [.. droppable.Where(b => b.Level <= itemLevel)];
        if (pool.Count == 0) pool = [.. droppable.Where(b => b.Level == droppable.Min(d => d.Level))];
        return r.Weighted(pool, b => b.DropWeight);
    }

    private UniqueRow? PickUnique(ItemRollContext ctx, int itemLevel, string? forced, SeededRandom r)
    {
        var all = _content.Uniques.Rows.Where(u => forced is null || u.Base == forced).ToList();
        if (all.Count == 0) return null;
        var pool = all.Where(u => u.Level <= itemLevel && _content.ItemBases[u.Base].AllowsClass(ctx.CharacterClass)).ToList();
        if (pool.Count == 0) pool = [.. all.Where(u => u.Level <= itemLevel)];
        if (pool.Count == 0) pool = [.. all.Where(u => u.Level == all.Min(a => a.Level))];
        return r.Weighted(pool, u => u.Weight);
    }

    /// <summary>The stat keys already on an item: two affixes never grant the same stat.</summary>
    private HashSet<string> Stats(IEnumerable<RolledAffix> affixes) =>
        [.. affixes.Select(a => Key(_content.Affixes[a.Id]))];

    private static string Key(AffixRow a) => a.Kind + ":" + a.Stat;

    /// <summary>One affix of the pool that fits the base at the level and grants none of <paramref name="taken"/>; null when none does.</summary>
    private AffixRow? PickAffix(ItemBaseRow itemBase, int itemLevel, string pool, HashSet<string> taken, SeededRandom r)
    {
        var fits = _content.Affixes.Rows.Where(a => a.Pool == pool && a.Fits(itemBase, itemLevel) && !taken.Contains(Key(a))).ToList();
        return fits.Count == 0 ? null : r.Weighted(fits, a => a.Weight);
    }

    /// <summary>
    /// Adds <paramref name="count"/> positive affixes to <paramref name="have"/>: prefixes and
    /// suffixes alternate, the pool with fewer on the item going first (a prefix on a tie); a
    /// pool with nothing left that fits gives way to the other; when neither has, fewer roll.
    /// </summary>
    private List<RolledAffix> AddPositives(List<RolledAffix> have, List<RolledAffix> curses, ItemBaseRow itemBase, int itemLevel, int count, SeededRandom r)
    {
        var list = new List<RolledAffix>(have);
        for (int i = 0; i < count; i++)
        {
            var taken = Stats(list.Concat(curses));
            int prefixes = list.Count(a => _content.Affixes[a.Id].Pool == AffixPools.Prefix);
            string first = prefixes <= list.Count - prefixes ? AffixPools.Prefix : AffixPools.Suffix;
            string second = first == AffixPools.Prefix ? AffixPools.Suffix : AffixPools.Prefix;
            var affix = PickAffix(itemBase, itemLevel, first, taken, r) ?? PickAffix(itemBase, itemLevel, second, taken, r);
            if (affix is null) break;
            list.Add(new RolledAffix(affix.Id, r.Between(0, S.RollScale)));
        }
        return list;
    }

    //-------------------------------------------------------------------------
    // Values and power
    //-------------------------------------------------------------------------

    /// <summary>The positive roll ceiling: 1 + the budgets of the item's cursed stats (R28).</summary>
    public double Ceiling(ItemInstance item) =>
        1 + item.Affixes.Select(a => _content.Affixes[a.Id]).Where(a => a.IsCursed).Sum(a => a.Budget);

    /// <summary>Where the affix sits in its range: roll/scale, times the ceiling for a positive one.</summary>
    public double Position(ItemInstance item, RolledAffix affix)
    {
        double t = (double)affix.Roll / S.RollScale;
        return _content.Affixes[affix.Id].IsCursed ? t : t * Ceiling(item);
    }

    /// <summary>An affix's value: From + position·(To − From), rounded to its step.</summary>
    public double Value(ItemInstance item, RolledAffix affix)
    {
        var a = _content.Affixes[affix.Id];
        double v = a.From + Position(item, affix) * (a.To - a.From);
        return Math.Round(Math.Round(v / a.Step, MidpointRounding.AwayFromZero) * a.Step, 6);
    }

    /// <summary>W: the sum of the item's positive affixes' power weights.</summary>
    public double PositiveWeight(ItemInstance item) =>
        item.Affixes.Select(a => _content.Affixes[a.Id]).Where(a => !a.IsCursed).Sum(a => a.Power);

    /// <summary>Net power: Σ w·r·C over positives minus Σ s·budget·W over curses (see the class summary).</summary>
    public double NetPower(ItemInstance item)
    {
        double w = PositiveWeight(item);
        double net = 0;
        foreach (var ra in item.Affixes)
        {
            var a = _content.Affixes[ra.Id];
            double t = (double)ra.Roll / S.RollScale;
            net += a.IsCursed ? -t * a.Budget * w : a.Power * t * Ceiling(item);
        }
        return net;
    }

    /// <summary>The power the unusual chance reads, 0..1: affixShare·(net/W) + (1 − affixShare)·(ilvl / max ilvl).</summary>
    public double UnusualPower(ItemInstance item)
    {
        double w = PositiveWeight(item);
        double affixes = w > 0 ? NetPower(item) / w : 0;
        double level = (double)item.ItemLevel / S.MaxItemLevel;
        return Math.Clamp(S.AffixShare * affixes + (1 - S.AffixShare) * level, 0, 1);
    }

    /// <summary>P(Unusual) = the rarity's unusualBase × power², power clamped to 0..1.</summary>
    public double UnusualChance(int rarity, double power)
    {
        double p = Math.Clamp(power, 0, 1);
        return _content.Rarities.Rows[Math.Clamp(rarity, 0, _content.Rarities.Count - 1)].UnusualBase * p * p;
    }

    /// <summary>The roll's one draw for the unusual chance: true with probability <see cref="UnusualChance"/>.</summary>
    public bool PromotesToUnusual(int rarity, double power, SeededRandom r) => r.Unit() < UnusualChance(rarity, power);

    /// <summary>
    /// The band a rarity's net power lies in at an item level: from every positive at roll 0
    /// under the heaviest curses at full severity, to every positive at the cursed ceiling.
    /// It bounds the roll; the facts sample it.
    /// </summary>
    public (double Min, double Max) NetPowerBand(int rarity, int itemLevel)
    {
        var rr = _content.Rarities.Rows[rarity];
        var positives = _content.Affixes.Rows.Where(a => !a.IsCursed && a.MinLevel <= itemLevel).ToList();
        double heaviest = positives.Count == 0 ? 0 : positives.Max(a => a.Power);
        double maxW = rr.MaxAffixes * heaviest;
        double budget = rr.Curses
            ? AffixTable.MostBudget(_content.Affixes.Rows.Where(a => a.MinLevel <= itemLevel), S.Curses.MaxCount)
            : 0;
        return (-budget * maxW, (1 + budget) * maxW);
    }

    //-------------------------------------------------------------------------
    // History: re-roll and upgrade (D24), replayed by the host
    //-------------------------------------------------------------------------

    /// <summary>
    /// The item with affix <paramref name="index"/>'s roll rolled again from <paramref name="seed"/>.
    /// The affix keeps its identity (so the ceiling and every other affix stay byte-identical);
    /// the re-roll count rises and the history records it.
    /// </summary>
    public ItemInstance Reroll(ItemInstance item, int index, ulong seed)
    {
        if (index < 0 || index >= item.Affixes.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var r = SeededRandom.For(seed, "reroll", item.Seed.ToString(CultureInfo.InvariantCulture), Num(index), Num(item.RerollCount));
        var affix = item.Affixes[index] with { Roll = r.Between(0, S.RollScale) };
        return item with
        {
            Affixes = item.Affixes.SetItem(index, affix),
            RerollCount = item.RerollCount + 1,
            History = item.History.Add(new ItemEvent(ItemOps.Reroll, index, seed)),
        };
    }

    /// <summary>True when a recipe may raise the item's rarity one step (Stock to Vintage to Strange only).</summary>
    public bool CanUpgrade(ItemInstance item) =>
        item.Unique is null && _content.ItemBases[item.BaseId].IsGear
        && _content.Rarities.Rows[item.Rarity].UpgradeCost > 0
        && item.Rarity + 1 < _content.Rarities.Count && _content.Rarities.Rows[item.Rarity + 1].Craftable;

    /// <summary>The item one rarity up, with positive affixes added from <paramref name="seed"/> to fill the new rarity's count. No curse is added.</summary>
    public ItemInstance Upgrade(ItemInstance item, ulong seed)
    {
        if (!CanUpgrade(item)) throw new InvalidOperationException($"'{item.BaseId}' of rarity {item.Rarity} cannot be upgraded");
        var r = SeededRandom.For(seed, "upgrade", item.Seed.ToString(CultureInfo.InvariantCulture), Num(item.Rarity));
        var next = _content.Rarities.Rows[item.Rarity + 1];
        var itemBase = _content.ItemBases[item.BaseId];
        var positives = item.Affixes.Where(a => !_content.Affixes[a.Id].IsCursed).ToList();
        var curses = item.Affixes.Where(a => _content.Affixes[a.Id].IsCursed).ToList();
        int target = r.Between(Math.Max(positives.Count, next.MinAffixes), next.MaxAffixes);
        var rolled = AddPositives(positives, curses, itemBase, item.ItemLevel, target - positives.Count, r);
        return item with
        {
            Rarity = item.Rarity + 1,
            Affixes = [.. rolled, .. curses],
            History = item.History.Add(new ItemEvent(ItemOps.Upgrade, -1, seed)),
        };
    }

    /// <summary>The rolled half again: <see cref="Roll"/> from the item's context and seed, then each history event with its seed.</summary>
    public ItemInstance Replay(ItemInstance item)
    {
        var again = Roll(item.Context, item.Seed);
        foreach (var e in item.History)
            again = e.Op switch
            {
                ItemOps.Reroll => Reroll(again, e.Index, e.Seed),
                ItemOps.Upgrade => Upgrade(again, e.Seed),
                _ => throw new InvalidDataException($"unknown history op '{e.Op}'"),
            };
        return again;
    }

    /// <summary>
    /// True when <paramref name="item"/> is what its context, seed and history give, and its
    /// mutable half is inside its bounds: durability 0..maximum, maximum 0..the base's, a
    /// count 1..the base's stack, a re-roll count equal to the re-rolls recorded, and
    /// identified if it rolled identified.
    /// </summary>
    public bool Replays(ItemInstance item)
    {
        ItemInstance again;
        try
        {
            if (!_content.ItemBases.TryGet(item.BaseId, out var b)) return false;
            again = Replay(item);
            if (item.Durability < 0 || item.Durability > item.MaxDurability || item.MaxDurability > b.Durability) return false;
            if (item.Count < 1 || item.Count > b.Stack || item.WearUses < 0) return false;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or InvalidDataException or KeyNotFoundException)
        {
            return false;
        }
        return RolledHalfEquals(item, again)
               && item.RerollCount == item.History.Count(e => e.Op == ItemOps.Reroll)
               && (item.Identified || !again.Identified);
    }

    /// <summary>Equal rolled halves: base, item level, rarity, tier, unique, effect, and each affix and its roll in order.</summary>
    public static bool RolledHalfEquals(ItemInstance a, ItemInstance b) =>
        a.BaseId == b.BaseId && a.ItemLevel == b.ItemLevel && a.Rarity == b.Rarity && a.Tier == b.Tier
        && a.Unique == b.Unique && a.UnusualEffect == b.UnusualEffect && a.Seed == b.Seed && a.Context == b.Context
        && a.Affixes.SequenceEqual(b.Affixes);
}
