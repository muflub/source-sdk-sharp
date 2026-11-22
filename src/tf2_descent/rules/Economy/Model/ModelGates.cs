using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Items;
using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Rules.Economy.Model;

/// <summary>One template's threat per XP at one scale (2f's band).</summary>
public sealed record ThreatPerXp(string Template, string Size, double Scale, double Threat, double Xp)
{
    public double Ratio => Threat / Xp;
}

/// <summary>
/// Clearing packs: the mean seconds a pack, the deaths, whether every pack was cleared, and the
/// mean damage a pack dealt the party as a share of the party's life.
/// </summary>
public sealed record ClearTime(double MeanSeconds, int Deaths, int Packs, bool AllCleared, double MeanTakenShare);

/// <summary>
/// The measurements RU14's gates read, each from the model and the real rules:
/// <list type="bullet">
/// <item><see cref="ThreatPerXpRows"/>: every template a depth's pack can roll (<see cref="PackTemplates"/>):
/// its threat (<see cref="RobotStats.Threat"/>) over its XP (<see cref="RobotStats.Xp"/>, which
/// carries the temperament budget) at the low, middle and high end of its scale range, at
/// monster level 1. A template no depth rolls (a boss, the Tank, the sentry buster) is no
/// thing to farm, so the band does not read it.</item>
/// <item><see cref="MedianGear"/>: A.6's median gear, slot by slot the median-scored of the
/// model's rolls of a depth's drops.</item>
/// <item><see cref="Clear"/>: the mean time a party takes on fresh packs of a depth.</item>
/// </list>
/// </summary>
public sealed class ModelGates
{
    private readonly EconomyModel _model;

    public ModelGates(EconomyModel model) => _model = model;

    private ModelRules R => _model.Rules;

    /// <summary>The templates some depth's pack pool (levels.json) can roll, in robots.json's order.</summary>
    public IReadOnlyList<Robots.RobotTemplate> PackTemplates()
    {
        var pooled = R.Content.Levels.Rows.SelectMany(d => d.Templates).Where(w => w.Weight > 0).Select(w => w.Template).ToHashSet(StringComparer.Ordinal);
        return [.. R.Content.Robots.Rows.Where(t => !t.Boss && pooled.Contains(t.Id))];
    }

    public IReadOnlyList<ThreatPerXp> ThreatPerXpRows()
    {
        var rows = new List<ThreatPerXp>();
        foreach (var t in PackTemplates())
            foreach (double s in new[] { t.ScaleMin, (t.ScaleMin + t.ScaleMax) / 2, t.ScaleMax })
            {
                var stats = R.Robots.Stats(t, s, 1, []);
                rows.Add(new ThreatPerXp(t.Id, t.Size, s, stats.Threat, stats.Xp));
            }
        return rows;
    }

    /// <summary>
    /// Gate 1's measure: for each row, its threat per XP over its TF class's reference (the
    /// class's ordinary template at scale 1). The gate holds every row inside
    /// [1 / (1 + band), 1 + band], so neither a runt nor a giant of a class pays more XP for
    /// its threat than the band allows. A class with no ordinary template is its own reference
    /// at its first template's scale 1.
    /// </summary>
    public IReadOnlyList<(ThreatPerXp Row, double Relative)> ThreatPerXpRelative()
    {
        var rows = ThreatPerXpRows();
        var result = new List<(ThreatPerXp, double)>();
        foreach (var group in rows.GroupBy(r => R.Content.Robots[r.Template].Class))
        {
            var reference = PackTemplates().Where(t => t.Class == group.Key)
                .OrderBy(t => t.Size == "ordinary" ? 0 : 1).ThenBy(t => t.Id, StringComparer.Ordinal).First();
            var s = R.Robots.Stats(reference, 1.0, 1, []);
            double refRatio = s.Threat / s.Xp;
            result.AddRange(group.Select(r => (r, r.Ratio / refRatio)));
        }
        return result;
    }

    /// <summary>The worst row of gate 1: the largest of each relative ratio and its inverse (1.0 is the reference).</summary>
    public double ThreatPerXpWorst() => ThreatPerXpRelative().Max(x => Math.Max(x.Relative, 1 / x.Relative));

    /// <summary>Gate 1 holds: every row inside the band.</summary>
    public bool ThreatPerXpInBand() => ThreatPerXpWorst() <= 1 + R.Model.ThreatPerXpBandPct / 100 + 1e-9;

    /// <summary>
    /// Median gear for <paramref name="hero"/>: for each equipment slot in order, the model rolls
    /// <c>medianSets</c> sets of <c>dropsPerSlot</c> drops of the depth's table (a base for the
    /// slot the class may use at the level, the tier on the depth's weights, identified), each
    /// scored with the others worn; each set keeps its best the hero may equip, and the hero
    /// wears the median of the sets' bests (a slot it can wear none of stays as it is).
    /// </summary>
    public void MedianGear(ModelHero hero, int depth, ulong seed)
    {
        var table = R.Content.Loot.Table(R.Robots.Depth(depth).LootTable);
        var tierWeights = table.Tiers.Select(t => t.At(depth)).ToList();
        foreach (var slot in R.Content.InventorySettings.Equipment)
        {
            var bases = R.Content.ItemBases.Rows.Where(b => b.IsGear && b.Slot == slot.Accepts && b.AllowsClass(hero.Sheet.ClassId)
                                                            && b.Level <= hero.Sheet.Level).ToList();
            if (bases.Count == 0) continue;
            var scored = new List<(double Power, ItemInstance Item)>();
            hero.Equipped.TryGetValue(slot.Id, out var current);
            var roll = Seeded.From(seed, "median", slot.Id);
            for (int set = 0; set < R.Model.Gear.MedianSets; set++)
            {
                (double Power, ItemInstance Item)? best = null;
                for (int i = 0; i < R.Model.Gear.DropsPerSlot; i++)
                {
                    var b = bases[roll.Next(bases.Count)];
                    string rarity = table.Tiers[roll.Pick(tierWeights)].Rarity;
                    int tier = R.Content.Rarities.Rows.ToList().FindIndex(r => r.Id == rarity);
                    var ctx = R.Loot.KillContext(hero.Sheet.ClassId, hero.Sheet.Level, depth, tier) with
                    {
                        LootTable = R.Content.ItemSettings.ForcedBasePrefix + b.Id,
                    };
                    var item = R.Items.Roller.Roll(ctx, roll.NextULong()) with { Identified = true };
                    var others = hero.Gear.Where(g => g.Slot != slot.Id).ToList();
                    if (R.Inventory.EquipRefusal(hero.Sheet, slot.Id, item, others) is not null) continue;
                    hero.Equipped[slot.Id] = item;
                    double power = _model.Power(hero, depth);
                    if (best is null || power > best.Value.Power) best = (power, item);
                }
                if (best is { } kept) scored.Add(kept);
            }
            if (scored.Count == 0)
            {
                if (current is null) hero.Equipped.Remove(slot.Id);
                else hero.Equipped[slot.Id] = current;
                continue;
            }
            hero.Equipped[slot.Id] = scored.OrderBy(x => x.Power).ElementAt(scored.Count / 2).Item;
        }
        _model.Refill(hero);
    }

    /// <summary>
    /// The mean seconds the party takes on <paramref name="packs"/> fresh packs of the depth
    /// (rolled from <paramref name="seed"/>, sized and scaled for the party), each fight begun at
    /// full life and mana, and the deaths it cost.
    /// </summary>
    public ClearTime Clear(IReadOnlyList<ModelHero> party, int depth, int packs, ulong seed)
    {
        double total = 0, taken = 0;
        int deaths = 0;
        bool all = true;
        for (int i = 0; i < packs; i++)
        {
            foreach (var h in party)
            {
                _model.Refill(h);
                Stock(h, depth);
            }
            double life = party.Sum(h => h.Life);
            var pack = R.Packs.RollPack(seed, i, depth, party.Count, R.Model.Run.MarkersPerRoom);
            var fight = _model.Fight(party, _model.Robots(pack));
            total += fight.Seconds;
            taken += fight.DamageTaken / life;
            deaths += fight.Deaths;
            all &= fight.Cleared;
        }
        return new ClearTime(total / packs, deaths, packs, all, taken / packs);
    }

    /// <summary>
    /// Gate 2's hero: level-appropriate for the depth (its monster level, at most the cap), in
    /// median gear of the depth's drops.
    /// </summary>
    public ModelHero LevelMatched(string build, int depth, ulong seed) =>
        Geared(build, Math.Min(R.Heroes.LevelCap, R.Robots.MonsterLevel(depth)), depth, seed);

    /// <summary>
    /// The potions a geared hero carries into a fight: the policy's counts of the largest health
    /// and mana potion the depth's loot table drops. Drunk ones are not replaced between fights:
    /// <see cref="Clear"/> restocks before each pack.
    /// </summary>
    public void Stock(ModelHero hero, int depth)
    {
        var table = R.Content.Loot.Table(R.Robots.Depth(depth).LootTable);
        string Largest(string kind) => table.Potions.Select(p => R.Content.ItemBases[p.Base])
            .Where(b => b.Id.StartsWith(kind, StringComparison.Ordinal)).OrderByDescending(b => b.Restore).First().Id;
        var p = R.Model.Potions;
        hero.Potions.Clear();
        hero.Potions.AddRange(Enumerable.Repeat(Largest("health"), p.BeltHealth + p.SpareHealth));
        hero.Potions.AddRange(Enumerable.Repeat(Largest("mana"), p.BeltMana + p.SpareMana));
    }

    /// <summary>The depth's boss fought by the party at full life and mana; null for a depth with no boss.</summary>
    public FightResult? BossFight(IReadOnlyList<ModelHero> party, int depth)
    {
        if (R.Content.Bosses.AtDepth(depth) is not { } boss) return null;
        foreach (var h in party)
        {
            _model.Refill(h);
            Stock(h, depth);
        }
        return _model.BossFight(party, depth);
    }

    /// <summary>A hero of the build at the level, in median gear for the depth.</summary>
    public ModelHero Geared(string build, int level, int depth, ulong seed)
    {
        var hero = _model.HeroAt(build, level);
        MedianGear(hero, depth, seed);
        return hero;
    }

    /// <summary>The hero with its primary weapon swapped for a Stock one of the same base.</summary>
    public void StockPrimary(ModelHero hero)
    {
        if (!hero.Equipped.TryGetValue("primary", out var weapon)) return;
        hero.Equipped["primary"] = _model.Stock(weapon.BaseId, weapon.ItemLevel);
        _model.Refill(hero);
    }

    /// <summary>
    /// 2j's two costs of an Unusual: the Scout's expected crates (<see cref="Crafting.ExpectedCostPerUnusualFromGambling"/>)
    /// at <paramref name="level"/>, and play's: the Australium earned plus the sell value of what
    /// was found, over the Unusuals found, across <paramref name="runs"/>.
    /// </summary>
    public (double Gambling, double Playing, int Unusuals) UnusualCosts(IReadOnlyList<RunReport> runs, int level)
    {
        double income = runs.Sum(r => (double)r.Last.AustraliumEarned + r.Last.ItemValueFound);
        int unusuals = runs.Sum(r => r.Last.UnusualsFound);
        return (R.Crafting.ExpectedCostPerUnusualFromGambling(level), unusuals > 0 ? income / unusuals : double.PositiveInfinity, unusuals);
    }

    /// <summary>Solo pacing (RU14): a solo run's hours over the party's on the same seed, inside <c>pacing.soloHoursMaxRatio</c>.</summary>
    public bool SoloHoursHold(RunReport solo, RunReport party) =>
        solo.Last.Seconds <= party.Last.Seconds * R.Model.Pacing.SoloHoursMaxRatio + 1e-9;

    /// <summary>Solo pacing (RU14): a solo run's deaths at most <c>pacing.soloDeathsPerDepthMax</c> a depth reached.</summary>
    public bool SoloDeathsHold(RunReport solo) => solo.Last.Deaths <= R.Model.Pacing.SoloDeathsPerDepthMax * solo.Depths.Count + 1e-9;

    /// <summary>A run's boss fights: the depth, the party's levels as it went in, and the fight.</summary>
    public static IReadOnlyList<(int Depth, IReadOnlyList<int> Levels, FightResult Fight)> BossFights(RunReport run) =>
        [.. run.Depths.Where(d => d.Boss is not null).Select(d => (d.Depth, d.BossLevels ?? [], d.Boss!))];

    /// <summary>Boss gate 1: no single hit of the boss's own attack took more than <c>ordinaryHitMaxLifePct</c> of the life of the hero it struck.</summary>
    public bool OrdinaryHitHolds(FightResult fight) => fight.LargestHitShare <= R.Model.Boss.OrdinaryHitMaxLifePct / 100 + 1e-9;

    /// <summary>Boss gate 1's special half: no single shot or slam of its special took <c>specialHitMaxLifePct</c> or more of a hero's life.</summary>
    public bool SpecialHitHolds(FightResult fight) => fight.LargestSpecialShare < R.Model.Boss.SpecialHitMaxLifePct / 100;

    /// <summary>Boss gate 2: a solo fight at <paramref name="depth"/> is won, and lasts inside the depth's band.</summary>
    public bool SoloLengthHolds(int depth, FightResult fight) =>
        R.Model.Boss.Band(depth) is { } band && fight.Cleared && fight.Seconds >= band.MinSeconds && fight.Seconds <= band.MaxSeconds;

    /// <summary>Boss gate 3: a party's fight is not trivial: it took at least <c>partyMinTakenPct</c> of the party's life and lasted at least <c>partyMinSeconds</c>.</summary>
    public bool PartyNotTrivial(FightResult fight) =>
        fight.TakenShare >= R.Model.Boss.PartyMinTakenPct / 100 && fight.Seconds >= R.Model.Boss.PartyMinSeconds;

    /// <summary>
    /// The damage types the build deals: its weapons' and its rotation's damaging actions'.
    /// A robot immune to every one of them could not be cleared by it.
    /// </summary>
    public IReadOnlySet<string> DamageTypes(ModelHero hero)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in hero.Equipped.Values)
        {
            var b = R.Content.ItemBases[item.BaseId];
            if (b.Kind == ItemKinds.Weapon && b.Family is { } f && R.Model.Weapon(f) is not null) types.Add(R.Content.Families[f].DamageType);
        }
        foreach (var id in hero.Build.Rotation)
            if (R.Skills.Find(id) is { DealsDamage: true, DamageType: { } type } && Skills.SkillRules.RealRank(hero.Sheet, id) > 0)
                types.Add(type);
        return types;
    }
}
