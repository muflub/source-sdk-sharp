using System.Collections.Immutable;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Items;

using TF2.Descent.Rules.Loot;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Rules.Skills;

namespace TF2.Descent.Rules.Economy.Model;

/// <summary>
/// One depth of a run, after its town visit: how the party stands. A boss depth also carries
/// its boss fight (<see cref="Boss"/>) and the party's levels as it began (<see cref="BossLevels"/>).
/// </summary>
public sealed record DepthReport(
    int Depth,
    bool Cleared,
    double Seconds,
    IReadOnlyList<int> Levels,
    IReadOnlyList<double> Power,
    int Deaths,
    IReadOnlyList<long> Australium,
    int Kills,
    int Packs,
    int PotionsDrunk,
    long AustraliumEarned,
    long ItemValueFound,
    int UnusualsFound,
    FightResult? Boss = null,
    IReadOnlyList<int>? BossLevels = null);

/// <summary>A run: each depth reached, and whether it reached the bottom.</summary>
public sealed record RunReport(IReadOnlyList<string> Builds, ulong Seed, IReadOnlyList<DepthReport> Depths)
{
    public bool Cleared => Depths.Count > 0 && Depths.All(d => d.Cleared);

    public DepthReport Last => Depths[^1];
}

/// <summary>
/// RU14's economy model (plan_tf2_rpg 15, 2f, 2j, A.6; plan_descent_logic RU14): a seeded,
/// deterministic descent of a party from depth 1 to 15, built from the real rules and content
/// alone, its own assumptions in economy_model.json. An instrument, not the game.
/// <list type="bullet">
/// <item><b>A depth</b>: its layout's room count (<see cref="Layout.DepthTable.For"/>); a share of the
/// rooms hold a pack (<see cref="PackRules.RollPack"/> from the level seed, the party's size);
/// a boss level has its packs and then its boss (bosses.json's boss of the depth, <see cref="BossFight"/>,
/// the party whole as it goes in). Each room is
/// walked for its seconds, the party regenerating.</item>
/// <item><b>A pack</b> is fought by <see cref="ModelCombat"/>. When it is cleared each member
/// is paid each kill's XP (<see cref="ExperienceRules.KillXp"/>, all members in range, D10) and
/// levels (L3: a level-up refills), spending points by its build; each seat rolls each kill
/// (<see cref="LootRules.RollWithFind"/> with its own magic find); the kill's Australium is
/// split (<see cref="LootRules.Split"/>, each share raised by the member's Australium find); a
/// champion's table and a boss's are rolled per member (<see cref="LootRules.BossDrops"/>).
/// An item is rolled with <see cref="ItemRoller.Roll"/> in the kill's context and kept while the
/// backpack (<see cref="Inventory.InventoryRules.BackpackSlots"/>) has room; potions are carried
/// without limit.</item>
/// <item><b>Town</b>, after each depth: identify what the class can use, by tier, while the
/// Spy's fee is affordable; equip by the gear score (<see cref="ModelHeroes.Power"/>), slot by
/// slot, twice (gear can unlock gear); sell the rest; repair the equipped, weapons first, while
/// affordable; buy the policy's potions, the largest affordable first. No gambling.</item>
/// <item><b>A build</b> buys its points in its order, each skill to its rank as soon as the tree
/// allows; attribute points go where the build's weights are furthest behind, spilling to the
/// next attribute with room.</item>
/// </list>
/// </summary>
public sealed class EconomyModel
{
    private readonly ModelRules _r;
    private readonly ModelHeroes _heroes;
    private readonly ModelCombat _combat;

    public EconomyModel(DescentContent content)
    {
        _r = new ModelRules(content);
        _heroes = new ModelHeroes(_r);
        _combat = new ModelCombat(_r, _heroes);
    }

    public ModelRules Rules => _r;

    private ModelSettings M => _r.Model;

    private const string HeroClass = "soldier";

    //-------------------------------------------------------------------------
    // Heroes
    //-------------------------------------------------------------------------

    /// <summary>A level 1 Soldier of the build with TF's stock kit (a rocket launcher, a shotgun, a shovel) and two small health potions.</summary>
    public ModelHero NewHero(string build)
    {
        var hero = new ModelHero(M.Build(build), _r.Heroes.New(HeroClass, build));
        foreach (var (slot, baseId) in new[] { ("primary", "rocket_launcher"), ("secondary", "shotgun"), ("melee", "shovel") })
            hero.Equipped[slot] = Stock(baseId, 1);
        hero.Potions.AddRange(["health_small", "health_small"]);
        Refill(hero);
        return hero;
    }

    /// <summary>A Stock item of a base at an item level, identified.</summary>
    public ItemInstance Stock(string baseId, int itemLevel) =>
        _r.Items.Roller.Roll(new ItemRollContext(HeroClass, 1, 1, itemLevel, 0, _r.Content.ItemSettings.ForcedBasePrefix + baseId, "vendor"), 1);

    /// <summary>A hero of the build at <paramref name="level"/>, its points spent by its build, in TF's stock kit.</summary>
    public ModelHero HeroAt(string build, int level)
    {
        var hero = NewHero(build);
        Gain(hero, _r.Heroes.XpAtLevel(level));
        return hero;
    }

    /// <summary>Full life and mana.</summary>
    public void Refill(ModelHero hero)
    {
        hero.View = null;
        var v = _heroes.View(hero, 0);
        hero.Life = v.Stats.MaxLife;
        hero.Mana = v.Stats.MaxMana;
    }

    /// <summary>What the hero wears and how its weapons fire, one line each: the model's working, for reports.</summary>
    public IReadOnlyList<string> Describe(ModelHero hero)
    {
        var lines = new List<string>();
        foreach (var (slot, item) in hero.Equipped.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            lines.Add($"{slot}: {item.BaseId} rarity {item.Rarity} ilvl {item.ItemLevel} dur {item.Durability}/{item.MaxDurability} "
                      + string.Join(", ", item.Affixes.Select(a => $"{a.Id}={_r.Items.Roller.Value(item, a)}")));
        var v = _heroes.View(hero, 0);
        foreach (var w in v.Weapons)
            lines.Add($"weapon {w.Slot} {w.Family} {w.DamageType}: per hit {w.PerHit:F1} cycle {w.Cycle:F3} hit {w.Hit:F2} splash {w.Splash:F2} crit {w.Crit:F2} dps {w.Dps:F1}");
        lines.Add($"life {v.Stats.MaxLife:F0} mana {v.Stats.MaxMana:F0} regen {v.Stats.ManaRegenPerSecond:F2}/s armour {v.Stats.Armour:F0} action x{v.ActionMultiplier:F2}");
        return lines;
    }

    /// <summary>What one hit of the weapon in <paramref name="slot"/> deals before the target, as the model fires it; 0 for none.</summary>
    public double WeaponPerHit(ModelHero hero, string slot) =>
        _heroes.View(hero, 0).Weapons.FirstOrDefault(w => w.Slot == slot)?.PerHit ?? 0;

    /// <summary>The gear score of <see cref="ModelHeroes.Power"/> against a robot of the depth's monster level.</summary>
    public double Power(ModelHero hero, int depth) => _heroes.Power(hero, _r.Robots.MonsterLevel(depth));

    /// <summary>Experience gained, levels taken, points spent by the build (L3: a level-up refills).</summary>
    public void Gain(ModelHero hero, long xp)
    {
        var gain = _r.Heroes.GainXp(hero.Sheet, xp);
        hero.Sheet = gain.Sheet;
        if (gain.LevelsGained <= 0) return;
        Spend(hero);
        if (gain.RefillsLifeAndMana) Refill(hero);
        hero.View = null;
    }

    private void Spend(ModelHero hero)
    {
        var b = hero.Build;
        while (hero.Sheet.AttributePoints > 0)
        {
            var cls = _r.Heroes.Class(hero.Sheet.ClassId);
            var open = cls.Attributes.Where(a => hero.Sheet.Base(a.Attribute) < a.Cap).ToList();
            if (open.Count == 0) break;
            var weighted = open.Where(a => b.Attributes.GetValueOrDefault(a.Attribute) > 0).ToList();
            var pick = weighted.Count > 0
                ? weighted.OrderBy(a => (hero.Sheet.Base(a.Attribute) - a.Start) / b.Attributes[a.Attribute]).ThenBy(a => a.Attribute, StringComparer.Ordinal).First()
                : open[0];
            hero.Sheet = _r.Heroes.Spend(hero.Sheet, pick.Attribute, 1).Sheet;
        }
        bool bought = true;
        while (hero.Sheet.SkillPoints > 0 && bought)
        {
            bought = false;
            foreach (var p in b.Points)
            {
                if (SkillRules.RealRank(hero.Sheet, p.Skill) >= p.Rank) continue;
                var buy = _r.Skills.Buy(hero.Sheet, p.Skill);
                if (!buy.Ok) continue;
                hero.Sheet = buy.Sheet;
                bought = true;
                break;
            }
        }
        hero.View = null;
    }

    //-------------------------------------------------------------------------
    // Fights
    //-------------------------------------------------------------------------

    /// <summary>A rolled pack's robots, ready to fight.</summary>
    public IReadOnlyList<ModelRobot> Robots(Pack pack) => _combat.Robots(pack);

    /// <summary><paramref name="count"/> robots of one template at a scale, at the depth's monster level, scaled for the party, no affixes.</summary>
    public IReadOnlyList<ModelRobot> Of(string template, double scale, int depth, int count, int partySize = 1)
    {
        var t = _r.Content.Robots[template];
        var stats = _r.Robots.Stats(t, scale, _r.Robots.MonsterLevel(depth), [], partySize);
        return [.. Enumerable.Range(0, count).Select(i => _combat.Robot(i, template, [], stats))];
    }

    /// <summary>A boss alone, at its fixed scale and its depth's monster level, scaled for the party.</summary>
    public IReadOnlyList<ModelRobot> Boss(string template, int depth, int partySize)
    {
        var t = _r.Content.Robots[template];
        var stats = _r.Robots.Stats(t, t.ScaleMin, _r.Robots.MonsterLevel(depth), [], partySize);
        return [_combat.Robot(0, template, [], stats)];
    }

    /// <summary>
    /// The depth's boss fight (bosses.json; <see cref="ModelCombat"/>'s boss rules), the party at
    /// the life and mana it has: the boss and its companion (the Tank, at the depth's monster
    /// level and scaled for the party as the boss is), its phases, specials and adds. The roof
    /// falls when the Tank has rolled economy_model.json's track at its speed and drilled each
    /// station (bosses.json's stop, and its final stop at the last). Null for a depth with no boss.
    /// </summary>
    public FightResult? BossFight(IReadOnlyList<ModelHero> party, int depth)
    {
        if (_r.Bosses.AtDepth(depth) is not { } script) return null;
        var robots = Boss(script.Id, depth, party.Count).ToList();
        ModelRobot? tank = null;
        double roofAt = double.PositiveInfinity;
        if (script.Companion.Length > 0)
        {
            var t = _r.Content.Robots[script.Companion];
            tank = _combat.Robot(1, script.Companion, [], _r.Robots.Stats(t, t.ScaleMin, _r.Robots.MonsterLevel(depth), [], party.Count));
            robots.Add(tank);
            if (_r.Bosses.IsTank(script.Companion))
            {
                var s = _r.Bosses.Settings.Tank;
                roofAt = M.Boss.TankTrackUnits / Math.Max(1, tank.Stats.Speed) + s.StopSeconds * (M.Boss.TankStations - 1) + s.FinalSeconds;
            }
        }
        return _combat.Fight(party, robots, new BossPlan(script, robots[0], tank, roofAt, depth));
    }

    /// <summary>One fight (<see cref="ModelCombat"/>), the party at the life and mana it has.</summary>
    public FightResult Fight(IReadOnlyList<ModelHero> party, IReadOnlyList<ModelRobot> robots) => _combat.Fight(party, robots);

    //-------------------------------------------------------------------------
    // The run
    //-------------------------------------------------------------------------

    /// <summary>A descent from depth 1 to <paramref name="toDepth"/> by a party of the builds, from <paramref name="seed"/>.</summary>
    public RunReport Run(IReadOnlyList<string> builds, ulong seed, int toDepth = 15) =>
        Run([.. builds.Select(NewHero)], seed, 1, toDepth);

    /// <summary>A descent by a party already made, from <paramref name="fromDepth"/>.</summary>
    public RunReport Run(IReadOnlyList<ModelHero> party, ulong seed, int fromDepth, int toDepth)
    {
        var reports = new List<DepthReport>();
        double seconds = 0;
        int deaths = 0, potions = 0, unusuals = 0;
        long earned = 0, value = 0;
        int unusual = _r.Items.Roller.UnusualRarity();

        for (int depth = fromDepth; depth <= toDepth; depth++)
        {
            ulong levelSeed = Seeded.From(seed, "level", depth).NextULong();
            var profile = Layout.DepthTable.For(Math.Clamp(depth, 1, Layout.DepthTable.Depths));
            var boss = _r.Content.Bosses.AtDepth(depth);
            int packs = boss is not null ? M.Run.BossRoomPacks : (int)Math.Round(profile.Rooms * M.Run.PackRoomsPct / 100, MidpointRounding.AwayFromZero);
            seconds += profile.Rooms * M.Run.SecondsPerRoom;
            bool cleared = true;
            int kills = 0;
            uint killSeq = 0;
            FightResult? bossFight = null;
            IReadOnlyList<int>? bossLevels = null;

            for (int room = 0; room <= packs; room++)
            {
                FightResult fight;
                if (room < packs)
                {
                    Walk(party);
                    fight = _combat.Fight(party, _combat.Robots(_r.Packs.RollPack(levelSeed, room, depth, party.Count, M.Run.MarkersPerRoom)));
                }
                else if (boss is not null)
                {
                    foreach (var h in party) Refill(h);   // a party goes into a boss's arena whole: it waits out its regeneration first
                    bossLevels = [.. party.Select(h => h.Sheet.Level)];
                    fight = bossFight = BossFight(party, depth)!;
                }
                else
                    break;
                seconds += fight.Seconds;
                deaths += fight.Deaths;
                potions += fight.Potions;
                cleared &= fight.Cleared;   // a pack not cleared is left behind; the run goes on, the depth is marked
                foreach (var r in fight.Killed)
                {
                    kills++;
                    foreach (var h in party) Gain(h, _r.Experience.KillXp(r.Stats, h.Sheet.Level));
                    var (e, v, u) = Reward(party, r, depth, levelSeed, killSeq++, unusual);
                    earned += e;
                    value += v;
                    unusuals += u;
                }
            }

            seconds += M.Run.TownSeconds;
            foreach (var h in party) Town(h, depth);
            reports.Add(new DepthReport(depth, cleared, seconds, [.. party.Select(h => h.Sheet.Level)],
                [.. party.Select(h => Power(h, Math.Min(depth + 1, 15)))], deaths, [.. party.Select(h => h.Australium)], kills, packs,
                potions, earned, value, unusuals, bossFight, bossLevels));
        }
        return new RunReport([.. party.Select(h => h.Build.Id)], seed, reports);
    }

    /// <summary>A room's walk: each member regenerates for its seconds.</summary>
    private void Walk(IReadOnlyList<ModelHero> party)
    {
        foreach (var h in party)
        {
            var v = _heroes.View(h, 0);
            h.Life = Math.Min(v.Stats.MaxLife, h.Life + v.Stats.LifeRegenPerSecond * M.Run.SecondsPerRoom);
            h.Mana = Math.Min(v.Stats.MaxMana, h.Mana + v.Stats.ManaRegenPerSecond * M.Run.SecondsPerRoom);
        }
    }

    /// <summary>One kill's loot for the party: each seat's roll, the Australium split, a champion's or boss's own table. Returns (Australium paid, sell value found, Unusuals found).</summary>
    private (long, long, int) Reward(IReadOnlyList<ModelHero> party, ModelRobot robot, int depth, ulong levelSeed, uint kill, int unusual)
    {
        var key = LootKey.Of(robot.Template, robot.Affixes).ToString();
        long earned = 0, value = 0;
        int found = 0;
        void Keep(ModelHero h, ItemInstance item)
        {
            var b = _r.Content.ItemBases[item.BaseId];
            if (b.Kind == ItemKinds.Potion) { h.Potions.Add(item.BaseId); return; }
            if (!b.IsGear) return;
            if (item.Rarity == unusual) found++;
            value += _r.Pricing.Price(item with { Identified = true }, PriceKind.Sell);
            if (h.Backpack.Count < _r.Inventory.BackpackSlots(h.Sheet)) h.Backpack.Add(item);
        }

        bool bossKill = _r.Content.Robots[robot.Template].Boss;
        if (!bossKill)
        {
            long australium = 0;
            for (int seat = 0; seat < party.Count; seat++)
            {
                var h = party[seat];
                var v = _heroes.View(h, 0);
                var roll = _r.Loot.RollWithFind(levelSeed, _r.Loot.KillSeq(kill, seat), key, depth, party.Count, v.Stats.Total("item_drop_pct"));
                if (seat == 0) australium = roll.Australium;
                if (roll.Outcome == LootOutcome.Potion && roll.Potion is { } potion) h.Potions.Add(potion);
                else if (roll.Item)
                {
                    var ctx = _r.Loot.KillContext(HeroClass, h.Sheet.Level, depth, roll.Tier);
                    Keep(h, _r.Items.Roller.Roll(ctx, Seeded.From(levelSeed, "item", kill, seat).NextULong()));
                }
            }
            var shares = LootRules.Split(australium, party.Count);
            for (int seat = 0; seat < party.Count; seat++)
            {
                long got = LootRules.FoundShare(shares[seat], _heroes.View(party[seat], 0).Stats.Total("australium_find_pct"));
                party[seat].Australium += got;
                earned += got;
            }
        }
        if (bossKill || robot.Affixes.Count > 0)
            for (int seat = 0; seat < party.Count; seat++)
            {
                var h = party[seat];
                var ctx = _r.Loot.KillContext(HeroClass, h.Sheet.Level, depth, -1, "boss");
                foreach (var drop in _r.Loot.BossDrops(ctx, key, Seeded.From(levelSeed, "boss_drops", kill, seat).NextULong()))
                    Keep(h, _r.Items.Codec.Instance(drop));
                if (bossKill)
                {
                    long au = _r.Loot.BossAustralium(robot.Template);
                    h.Australium += au;
                    earned += au;
                }
            }
        return (earned, value, found);
    }

    //-------------------------------------------------------------------------
    // Town
    //-------------------------------------------------------------------------

    /// <summary>The town policy (see the class summary).</summary>
    private void Town(ModelHero h, int depth)
    {
        int attacker = _r.Robots.MonsterLevel(Math.Min(depth + 1, 15));

        // Identify what the class can use, best tier first, while affordable.
        foreach (var item in h.Backpack.Where(i => !i.Identified && _r.Content.ItemBases[i.BaseId].AllowsClass(h.Sheet.ClassId))
                     .OrderByDescending(i => i.Rarity).ToList())
        {
            long fee = _r.Pricing.IdentifyFee(item);
            if (h.Australium < fee) break;
            h.Australium -= fee;
            h.Backpack[h.Backpack.IndexOf(item)] = item with { Identified = true };
        }

        // Equip by the gear score, slot by slot, twice.
        for (int pass = 0; pass < 2; pass++)
            foreach (var slot in _r.Content.InventorySettings.Equipment)
            {
                double best = _heroes.Power(h, attacker);
                ItemInstance? choice = null;
                h.Equipped.TryGetValue(slot.Id, out var current);
                foreach (var item in h.Backpack.ToList())
                {
                    var others = h.Gear.Where(g => g.Slot != slot.Id).ToList();
                    if (_r.Inventory.EquipRefusal(h.Sheet, slot.Id, item, others) is not null) continue;
                    h.Equipped[slot.Id] = item;
                    h.View = null;
                    double power = _heroes.Power(h, attacker);
                    if (power > best * 1.0001)
                    {
                        best = power;
                        choice = item;
                    }
                    if (current is null) h.Equipped.Remove(slot.Id);
                    else h.Equipped[slot.Id] = current;
                }
                if (choice is not null)
                {
                    h.Backpack.Remove(choice);
                    if (current is not null) h.Backpack.Add(current);
                    h.Equipped[slot.Id] = choice;
                }
                h.View = null;
            }

        // Sell the rest.
        foreach (var item in h.Backpack) h.Australium += _r.Pricing.Price(item, PriceKind.Sell);
        h.Backpack.Clear();

        // Repair, weapons first.
        foreach (var slot in h.Equipped.Keys.OrderBy(s => _r.Content.ItemBases[h.Equipped[s].BaseId].Kind == ItemKinds.Weapon ? 0 : 1).ToList())
        {
            var item = h.Equipped[slot];
            if (_r.Repairs.Refusal(item) is not null) continue;
            long price = _r.Repairs.Price(item);
            if (h.Australium < price) continue;
            h.Australium -= price;
            h.Equipped[slot] = Repairs.Restore(item);
        }
        h.View = null;

        // Potions, the largest affordable first.
        var p = M.Potions;
        Buy(h, "health", p.BeltHealth + p.SpareHealth);
        Buy(h, "mana", p.BeltMana + p.SpareMana);
        Refill(h);   // town's Medic heals (D15)
    }

    private void Buy(ModelHero h, string kind, int want)
    {
        var sizes = _r.Content.ItemBases.Rows.Where(b => b.Kind == ItemKinds.Potion && b.Id.StartsWith(kind, StringComparison.Ordinal))
            .OrderByDescending(b => b.Restore).ToList();
        while (h.Potions.Count(x => x.StartsWith(kind, StringComparison.Ordinal)) < want)
        {
            var size = sizes.FirstOrDefault(b => PotionPrice(b.Id) <= h.Australium);
            if (size is null) return;
            h.Australium -= PotionPrice(size.Id);
            h.Potions.Add(size.Id);
        }
    }

    private long PotionPrice(string baseId) => _r.Pricing.Price(Stock(baseId, 1), PriceKind.Buy);
}
