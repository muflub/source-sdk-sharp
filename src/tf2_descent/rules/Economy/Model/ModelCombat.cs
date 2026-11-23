using TF2.Descent.Rules.Bosses;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Items;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Rules.Skills;

namespace TF2.Descent.Rules.Economy.Model;

/// <summary>
/// A robot in a fight: who it is, its numbers, the life it has left, and until when it is
/// stunned. Its attack is robot_attacks.json's row for it: <see cref="PerAttack"/> is what one
/// attack deals (<see cref="RobotAttackRules.AttackDamage"/>), <see cref="Interval"/> the seconds an
/// attack takes sustained (the refire, plus a clip's reload spread over the clip).
/// </summary>
public sealed class ModelRobot
{
    internal ModelRobot(int index, string template, IReadOnlyList<string> affixes, RobotStats stats, string damageType, double perAttack, double interval, bool elite, bool alwaysCrit)
    {
        Index = index;
        Template = template;
        Affixes = affixes;
        Stats = stats;
        Life = stats.Life;
        DamageType = damageType;
        PerAttack = perAttack;
        Interval = interval;
        Elite = elite;
        AlwaysCrit = alwaysCrit;
    }

    public int Index { get; }
    public string Template { get; }
    public IReadOnlyList<string> Affixes { get; }
    public RobotStats Stats { get; }
    public double Life { get; internal set; }
    public string DamageType { get; }
    public double PerAttack { get; }
    public double Interval { get; }
    public bool Elite { get; }
    public bool AlwaysCrit { get; }
    internal double StunnedUntil { get; set; }
    public bool Alive => Life > 0;
}

/// <summary>
/// A fight's outcome: how long it took, whether every robot died (a boss fight: whether the boss
/// did), the deaths and potions it cost, the damage the party took, and the robots in the order
/// they died. A boss fight also says the largest single hit of the boss's own attack and of one
/// shot or slam of its special, each over the life of the hero it struck (the share a hero at
/// full life would lose), the damage taken over the party's life at the start, and whether the
/// Tank brought the roof down.
/// </summary>
public sealed record FightResult(double Seconds, bool Cleared, int Deaths, int Potions, double DamageTaken, IReadOnlyList<ModelRobot> Killed,
    double LargestHitShare = 0, double LargestSpecialShare = 0, double TakenShare = 0, bool RoofFell = false);

/// <summary>
/// A boss fight's script as the model carries it out (bosses.json, HO13): the boss, its script,
/// its companion (the Tank) and when the Tank's roof falls, and the fight's state: the phase
/// reached, the special winding up, the adds summoned (once a phase, again after a reset).
/// </summary>
internal sealed class BossPlan(BossScript script, ModelRobot boss, ModelRobot? tank, double roofAt, int depth)
{
    public BossScript Script => script;
    public ModelRobot Boss => boss;
    public ModelRobot? Tank => tank;
    public double RoofAt => roofAt;
    public int Depth => depth;
    public int Reached { get; set; }
    public int Resets { get; set; }
    public double NextSpecial { get; set; } = double.NaN;
    public double LandAt { get; set; } = double.NaN;
    public BossSpecial? Winding { get; set; }
    public HashSet<(int Phase, int Resets)> Summoned { get; } = [];
    public double LargestHit { get; set; }
    public double LargestSpecial { get; set; }
}

/// <summary>
/// One fight of a party against a pack, a tick at a time (economy_model.json's tick). The model
/// has no positions and no aiming: every number is an expected value, so a fight is a pure
/// function of the heroes and the pack.
/// <list type="bullet">
/// <item><b>The heroes</b> act in seat order. A hero not busy casts the first action of its
/// rotation that <see cref="CastRules.Cast"/> allows (mana, cooldown, BlastJumping); an Order is
/// cast only while it has none in force (or Orders stack). A cast keeps it from its weapon for
/// the cast time; its damage lands at once on its target and, with a radius, on up to
/// <c>splashTargets</c> others at the splash share. Otherwise it fires its best weapon against
/// its target: expected damage = hit × crit × damage ÷ seconds a hit, a tick's worth. Damage
/// is resisted by the robot's resistance to its type (immune: none) and raised against
/// champions, giants and bosses by <c>damage_vs_elite_pct</c>.</item>
/// <item><b>The target</b> is the robot whose death removes the most incoming damage per
/// second of work: its damage rate over its life left.</item>
/// <item><b>Blast jumps</b> (a build that jumps): every <c>jumpEverySeconds</c>, while its life is at
/// least <c>jumpAboveLifePct</c> of its most and once a jump pays (Stomp or Death From Above held), the hero takes
/// the self-damage (cut by <c>self_blast_damage_pct</c> and its blast resistance) and is
/// BlastJumping for <c>airSeconds</c>; landing sets off Stomp. A dash (Rocket Leap, Human
/// Rocket) is a jump without self-damage.</item>
/// <item><b>The robots</b> close in for <c>approachSeconds</c>, then up to <c>maxAttackers</c> a
/// living hero attack, each at its attack's damage × <c>robotHitPct</c> ÷ its attack's interval, spread
/// over the heroes in turn; a stunned robot does nothing. A hit is cut by the hero's armour
/// (against the robot's level), resistance and <c>damage_taken_pct</c>; an always-crit robot
/// crits unless the hero cannot be crit.</item>
/// <item><b>Sustain</b>: regeneration, leech on damage dealt (capped by the robot's life left),
/// Field Dressing per enemy struck, and potions under the policy's thresholds.</item>
/// <item><b>Death</b>: a solo hero who dies costs the death's seconds and comes back full, the
/// robots keeping their wounds; a party member who dies sits out the fight; a whole party down
/// is a wipe, as a solo death. More deaths than the policy allows, or a fight past its time
/// limit, is a pack not cleared.</item>
/// <item><b>A boss fight</b> (<see cref="BossPlan"/>) ends when the boss dies. Its phase follows its
/// life (<see cref="BossRules.Phase"/>); a phase begun summons its adds (rolled at the depth's
/// monster level, scaled for the party, each at the middle of its scale range) once. Its special
/// is asked every phase's <c>specialSeconds</c> and lands after its windup on each living hero: a
/// volley's shots each strike at <c>volleyHitPct</c>, a slam at <c>slamHitPct</c>, each the boss's
/// damage × the special's share (× the crit multiplier for a crit volley). A wipe resets the
/// boss whole in its first phase (bosses.json's reset: the arena empty); its adds and the Tank
/// stay. The Tank fights nobody: once its track is rolled and drilled (<see cref="BossPlan.RoofAt"/>,
/// counted with the walks back) with the Tank alive, the roof falls and the fight is lost.</item>
/// <item>Not modelled: movement and speed, cover, knock-ups, Juggle Doctrine, threat and taunts,
/// robot status effects, reload of a partial clip, the projectile flight, the order a pack
/// arrives in, levelling mid-fight.</item>
/// </list>
/// </summary>
internal sealed class ModelCombat
{
    private readonly ModelRules _r;
    private readonly ModelHeroes _heroes;

    public ModelCombat(ModelRules rules, ModelHeroes heroes)
    {
        _r = rules;
        _heroes = heroes;
    }

    private ModelSettings M => _r.Model;

    /// <summary>The robots of a rolled pack.</summary>
    public IReadOnlyList<ModelRobot> Robots(Pack pack) =>
        [.. pack.Robots.Select(r => Robot(r.Index, r.Template, r.Affixes, r.Stats))];

    public ModelRobot Robot(int index, string template, IReadOnlyList<string> affixes, RobotStats stats)
    {
        var t = _r.Content.Robots[template];
        bool elite = affixes.Count > 0 || t.Size is "giant" or "boss";
        var attack = _r.Attacks.Of(t);
        double interval = attack.IntervalSeconds + (attack.Clip > 0 ? attack.ReloadSeconds / attack.Clip : 0);
        return new ModelRobot(index, template, affixes, stats, _r.Attacks.DamageTypeOf(attack), RobotAttackRules.AttackDamage(stats, attack), interval, elite,
            t.Flags.Contains("always_crit"));
    }

    public FightResult Fight(IReadOnlyList<ModelHero> party, IReadOnlyList<ModelRobot> pack) => Fight(party, pack, null);

    public FightResult Fight(IReadOnlyList<ModelHero> party, IReadOnlyList<ModelRobot> pack, BossPlan? boss)
    {
        double dt = M.TickSeconds;
        double t = 0, extra = 0, taken = 0;
        int deaths = 0, potions = 0;
        var robots = pack.ToList();
        var killed = new List<ModelRobot>();
        foreach (var h in party) Begin(h);
        double life = party.Sum(h => h.Life);
        double debuffUntil = 0, debuffPct = 0;
        double Debuff(double now) => now < debuffUntil ? 1 + debuffPct / 100 : 1;
        double Through(HeroView v, string type, int level) =>
            (1 - _r.Heroes.ArmourReduction(v.Stats.Armour, level)) * (1 - v.Stats.Resist(type)) * Math.Max(0, 1 + v.DamageTakenPct / 100);
        FightResult Result(bool cleared) =>
            new(t + extra, cleared, deaths, potions, taken, killed, boss?.LargestHit ?? 0, boss?.LargestSpecial ?? 0, taken / Math.Max(1, life),
                boss is not null && boss.Tank is { Alive: true } && t + extra >= boss.RoofAt);

        while (boss is null ? robots.Any(r => r.Alive) : boss.Boss.Alive)
        {
            if (t + extra > M.MaxFightSeconds || deaths > M.Death.MaxPerPack)
                return Result(false);
            if (boss is not null)
            {
                if (boss.Tank is { Alive: true } && t + extra >= boss.RoofAt)
                    return Result(false);   // the roof comes down on the arena: the fight is lost
                BossTurn(boss, party, robots, t, Debuff(t), Through, ref taken);
            }
            foreach (var h in party)
            {
                if (h.Dead) continue;
                var v = ViewOf(h, t);
                Jump(h, v, t, robots);
                if (t >= h.BusyUntil && !TryCast(h, v, t, party, robots, ref debuffUntil, ref debuffPct) && Target(robots) is { } target)
                    Fire(h, ViewOf(h, t), target, robots, dt);
                Sustain(h, ViewOf(h, t), dt);
            }

            // The robots.
            var alive = party.Where(h => !h.Dead).ToList();
            if (t >= M.ApproachSeconds && alive.Count > 0)
            {
                var attackers = robots.Where(r => r.Alive && r.StunnedUntil <= t).Take(M.MaxAttackers * alive.Count).ToList();
                for (int i = 0; i < attackers.Count; i++)
                {
                    var r = attackers[i];
                    var h = alive[i % alive.Count];
                    var v = ViewOf(h, t);
                    double hitRate = M.RobotHitPct / 100 / r.Interval;
                    double one = r.PerAttack * Debuff(t) * (r.AlwaysCrit && !v.CritImmune ? M.CritMultiplier : 1) * Through(v, r.DamageType, r.Stats.Level);
                    if (boss is not null && r == boss.Boss)
                        boss.LargestHit = Math.Max(boss.LargestHit, one / Math.Max(1, v.Stats.MaxLife));
                    double dmg = one * hitRate * dt;
                    h.Life -= Math.Max(0, dmg);
                    h.HitsTaken += hitRate * dt;
                    taken += Math.Max(0, dmg);
                }
            }

            foreach (var h in party)
            {
                if (h.Dead) continue;
                if (Potion(h, ViewOf(h, t), t)) potions++;
                if (h.Life > 0) continue;
                deaths++;
                h.Deaths++;
                if (party.Count == 1 || party.All(p => p.Dead || p == h))
                {
                    extra += M.Death.Seconds;   // a wipe: the walk back, the robots keep their wounds
                    foreach (var p in party) Revive(p);
                    if (boss is not null) Reset(boss, t);
                }
                else
                    h.Dead = true;
            }

            foreach (var r in robots.Where(r => !r.Alive && !killed.Contains(r)))
                killed.Add(r);
            t += dt;
        }
        foreach (var r in robots.Where(r => !r.Alive && !killed.Contains(r))) killed.Add(r);
        foreach (var h in party) End(h);
        return Result(true);
    }

    /// <summary>A boss's turn before the heroes act: its phase and adds, then its special's windup or landing.</summary>
    private void BossTurn(BossPlan boss, IReadOnlyList<ModelHero> party, List<ModelRobot> robots, double t, double debuff,
        Func<HeroView, string, int, double> through, ref double taken)
    {
        var script = boss.Script;
        int phase = BossRules.Phase(script, boss.Boss.Life, boss.Boss.Stats.Life, boss.Reached);
        for (int p = boss.Reached + 1; p <= phase; p++)
        {
            if (!boss.Summoned.Add((p, boss.Resets))) continue;
            foreach (var add in BossRules.AddsOf(script, p))
            {
                var t0 = _r.Content.Robots[add.Template];
                var stats = _r.Robots.Stats(t0, (t0.ScaleMin + t0.ScaleMax) / 2, _r.Robots.MonsterLevel(boss.Depth), [], party.Count);
                for (int i = 0; i < add.Count; i++)
                    robots.Add(Robot(robots.Count, add.Template, [], stats));
            }
        }
        boss.Reached = phase;

        if (double.IsNaN(boss.NextSpecial))
            boss.NextSpecial = t + script.Phases[boss.Reached].SpecialSeconds;
        if (boss.Winding is { } special && t >= boss.LandAt)
        {
            boss.Winding = null;
            bool volley = special.Kind == BossSpecial.Volley;
            string type = special.DamageType.Length > 0 ? special.DamageType : _r.Attacks.DamageTypeOf(_r.Attacks.Of(special.Attack));
            double strikes = volley ? special.Shots * M.Boss.VolleyHitPct / 100 : M.Boss.SlamHitPct / 100;
            foreach (var h in party.Where(h => !h.Dead))
            {
                var v = ViewOf(h, t);
                double one = boss.Boss.Stats.Damage * special.DamageShare * debuff * (special.Crit && !v.CritImmune ? M.CritMultiplier : 1)
                             * through(v, type, boss.Boss.Stats.Level);
                boss.LargestSpecial = Math.Max(boss.LargestSpecial, one / Math.Max(1, v.Stats.MaxLife));
                double dmg = Math.Max(0, one * strikes);
                h.Life -= dmg;
                taken += dmg;
            }
        }
        else if (boss.Winding is null && t >= boss.NextSpecial)
        {
            var row = script.Phases[boss.Reached];
            if (_r.Bosses.SpecialOf(script, boss.Reached) is { } ask)
            {
                boss.Winding = ask;
                boss.LandAt = t + ask.WindupSeconds;
            }
            boss.NextSpecial = t + Math.Max(M.TickSeconds, row.SpecialSeconds);
        }
    }

    /// <summary>A wipe: the arena empties, so the boss is whole again in its first phase (bosses.json's reset); its adds and the Tank stay.</summary>
    private static void Reset(BossPlan boss, double t)
    {
        boss.Boss.Life = boss.Boss.Stats.Life;
        boss.Reached = 0;
        boss.Resets++;
        boss.Winding = null;
        boss.NextSpecial = t + boss.Script.Phases[0].SpecialSeconds;
    }

    private HeroView ViewOf(ModelHero h, double t)
    {
        if (h.View is null || h.Buffs.Values.Any(b => b.Until <= t))
        {
            foreach (var key in h.Buffs.Where(b => b.Value.Until <= t).Select(b => b.Key).ToList())
                h.Buffs.Remove(key);
            h.View = _heroes.View(h, t);
        }
        return h.View;
    }

    private static void Begin(ModelHero h)
    {
        h.Caster = h.Caster with { Now = 0, ReadyAt = h.Caster.ReadyAt.Clear(), Orders = h.Caster.Orders.Clear() };
        h.Buffs.Clear();
        h.View = null;
        h.BusyUntil = 0;
        h.AirUntil = double.NegativeInfinity;
        h.LandPending = false;
        h.NextJump = 0;
        h.PotionReady = 0;
        h.Dead = false;
    }

    private void End(ModelHero h)
    {
        if (h.Dead) Revive(h);
        h.Buffs.Clear();
        h.View = null;
    }

    /// <summary>Back at full life and mana, its buffs gone.</summary>
    private void Revive(ModelHero h)
    {
        h.Dead = false;
        h.Buffs.Clear();
        var v = _heroes.View(h, 0);
        h.View = v;
        h.Life = v.Stats.MaxLife;
        h.Mana = v.Stats.MaxMana;
    }

    /// <summary>The robot whose death removes the most incoming damage a second per point of its life left.</summary>
    private static ModelRobot? Target(IReadOnlyList<ModelRobot> robots) =>
        robots.Where(r => r.Alive).OrderByDescending(r => r.PerAttack / r.Interval / Math.Max(1, r.Life)).ThenBy(r => r.Index).FirstOrDefault();

    private double Against(HeroView v, ModelRobot r, string type, double amount) =>
        RobotRules.DamageTaken(r.Stats, type, amount) * (r.Elite ? 1 + v.ElitePct / 100 : 1);

    /// <summary>The hero's extra damage percent now: Pain Tolerance by the life missing, Death From Above while BlastJumping.</summary>
    private static double ExtraPct(ModelHero h, HeroView v, double t)
    {
        double missing = 1 - Math.Clamp(h.Life / Math.Max(1, v.Stats.MaxLife), 0, 1);
        return v.PainPctPer10 * Math.Floor(missing * 10 + 1e-9) + (t < h.AirUntil ? v.AirbornePct : 0);
    }

    private void Fire(ModelHero h, HeroView v, ModelRobot target, IReadOnlyList<ModelRobot> robots, double dt)
    {
        if (v.Weapons.Count == 0) return;
        var w = v.Weapons.OrderByDescending(x => Against(v, target, x.DamageType, x.Dps) * (1 + x.Splash)).ThenBy(x => x.Slot).First();
        double scale = 1 + ExtraPct(h, v, h.Caster.Now) / 100;
        double per = w.Dps * dt * scale;
        int struck = Hit(h, v, target, robots, w.DamageType, per, w.Splash);
        double hits = dt / w.Cycle;
        Heal(h, v, struck * hits, dt);
        h.WeaponUses += hits;
        if (h.Equipped.TryGetValue(w.Slot, out var item))
        {
            int uses = (int)Math.Floor(h.WeaponUses);
            if (uses > 0)
            {
                h.WeaponUses -= uses;
                var worn = _r.Items.Durability.Use(item, uses);
                h.Equipped[w.Slot] = worn.Item;
                if (worn.BrokeNow) h.View = null;
            }
        }
    }

    /// <summary>Deals <paramref name="amount"/> of <paramref name="type"/> to the target and the splash share to others; returns how many it struck.</summary>
    private int Hit(ModelHero h, HeroView v, ModelRobot target, IReadOnlyList<ModelRobot> robots, string type, double amount, double splash)
    {
        int struck = 0;
        void Deal(ModelRobot r, double a)
        {
            double before = r.Life;
            double dealt = Math.Min(before, Against(v, r, type, a));
            if (dealt <= 0) return;
            r.Life -= dealt;
            struck++;
            h.Life = Math.Min(v.Stats.MaxLife, h.Life + v.Stats.LifeFromHit(dealt, before));
            h.Mana = Math.Min(v.Stats.MaxMana, h.Mana + v.Stats.ManaFromHit(dealt, before));
        }
        Deal(target, amount);
        if (splash > 0)
            foreach (var r in robots.Where(r => r.Alive && r != target).Take(M.SplashTargets).ToList())
                Deal(r, amount * splash);
        return struck;
    }

    /// <summary>Field Dressing: heal a hit's worth per enemy struck, at most its targets a hit, at most its cap a second.</summary>
    private static void Heal(ModelHero h, HeroView v, double struckHits, double dt)
    {
        if (v.HealPerTarget <= 0) return;
        double heal = Math.Min(v.HealPerTarget * struckHits, v.HealPerSecondCap * dt);
        h.Life = Math.Min(v.Stats.MaxLife, h.Life + heal);
    }

    private void Jump(ModelHero h, HeroView v, double t, IReadOnlyList<ModelRobot> robots)
    {
        h.Caster = h.Caster with { Now = t };
        if (h.LandPending && t >= h.AirUntil)
        {
            h.LandPending = false;
            if (v.StompDamage > 0 && Target(robots) is { } target)
                Hit(h, v, target, robots, "blast", v.StompDamage * v.ActionMultiplier * v.Crit * (1 + ExtraPct(h, v, t) / 100), M.SplashShare * 2);
        }
        if (h.Build.JumpEverySeconds <= 0 || t < h.NextJump || t < h.AirUntil || !robots.Any(r => r.Alive)) return;
        if (h.Life < v.Stats.MaxLife * M.JumpAboveLifePct / 100) return;   // a hero low on life does not spend it on a jump
        if (v.StompDamage <= 0 && v.AirbornePct <= 0) return;               // nor on one that pays nothing (no Stomp, no Death From Above yet)
        h.NextJump = t + h.Build.JumpEverySeconds;
        double self = M.SelfBlastDamage * Math.Max(0, 1 + v.SelfBlastPct / 100) * (1 - v.Stats.Resist("blast"));
        h.Life -= self;
        h.AirUntil = t + M.AirSeconds;
        h.LandPending = true;
    }

    private bool TryCast(ModelHero h, HeroView v, double t, IReadOnlyList<ModelHero> party, IReadOnlyList<ModelRobot> robots,
        ref double debuffUntil, ref double debuffPct)
    {
        var target = Target(robots);
        if (target is null) return false;
        foreach (var id in h.Build.Rotation)
        {
            if (_r.Skills.Find(id) is not { } skill || SkillRules.RealRank(h.Sheet, id) <= 0) continue;
            bool stacks = _r.Skills.HoldsPassive(h.Sheet, "orders_stack");
            var inForce = h.Caster.Orders.Where(o => o.Value > t).Select(o => o.Key).ToList();
            if (skill.HasTag("order") && (inForce.Contains(id) || (inForce.Count > 0 && !stacks))) continue;
            var state = h.Caster with { Now = t, Mana = h.Mana, BlastJumping = t < h.AirUntil };
            var cast = _r.Casting.Cast(h.Sheet, v.Stats, v.Bonuses, state, id);
            if (!cast.Ok || cast.Skill is null) continue;
            h.Mana = cast.State.Mana;
            h.Caster = cast.State;
            h.BusyUntil = t + _heroes.CastTime(cast.Skill);
            Apply(h, v, t, skill, cast.Skill, party, robots, target, ref debuffUntil, ref debuffPct);
            foreach (var ended in cast.OrdersEnded)
                foreach (var p in party)
                    foreach (var key in p.Buffs.Keys.Where(k => k.StartsWith(ended + "/", StringComparison.Ordinal)).ToList())
                        p.Buffs.Remove(key);
            foreach (var begun in cast.OrdersBegun.Where(b => b != id))
            {
                var order = _r.Skills.Resolve(h.Sheet, v.Stats, v.Bonuses, begun);
                double until = cast.State.Orders.TryGetValue(begun, out var end) ? end : t;
                Buff(party, h, begun, order, until, t);
            }
            foreach (var p in party) p.View = null;
            return true;
        }
        return false;
    }

    private void Apply(ModelHero h, HeroView v, double t, SkillDef skill, ResolvedSkill s, IReadOnlyList<ModelHero> party,
        IReadOnlyList<ModelRobot> robots, ModelRobot target, ref double debuffUntil, ref double debuffPct)
    {
        string type = skill.DamageType ?? "blast";
        double scale = v.Crit * (1 + ExtraPct(h, v, t) / 100);
        int stages = ModelHeroes.Stages(s);
        foreach (var e in s.Effects)
        {
            switch (e.Primitive)
            {
                case Primitive.Projectile or Primitive.Hitscan or Primitive.AreaBurst when e.Trigger is null or "dash_landing":
                {
                    double hits = Math.Max(1, e.Params.GetValueOrDefault("count", 1)) * stages;
                    double amount = e.Params["damage"] * hits * v.ActionMultiplier * scale;
                    int struck = Hit(h, v, target, robots, type, amount, e.Params.GetValueOrDefault("radius") > 0 ? M.SplashShare : 0);
                    Heal(h, v, struck * hits, M.CastSeconds);
                    break;
                }
                case Primitive.Buff or Primitive.Aura when e.Target is "self" or "party":
                    if (e.Stats.Count > 0 || e.Conditions.Count > 0)
                    {
                        var targets = e.Target == "party" ? party : [h];
                        var stats = SkillRules.AsBuff(skill.Id, e);
                        foreach (var p in targets)
                            Put(p, skill.Id + "/" + e.Primitive, new ModelBuff(t + e.Params.GetValueOrDefault("duration"), stats, e.Conditions), t);
                    }
                    break;
                case Primitive.Debuff when e.Conditions.Contains("emp_stun") || e.Conditions.Contains("stun"):
                    foreach (var r in robots.Where(r => r.Alive && _r.Content.Robots[r.Template].Size != "boss" && !_r.Bosses.Shrugs(r.Template, "emp_stun"))
                                 .OrderBy(r => r == target ? 0 : 1).Take(1 + (e.Params.GetValueOrDefault("radius") > 0 ? M.SplashTargets : 0)))
                        r.StunnedUntil = Math.Max(r.StunnedUntil, t + e.Params.GetValueOrDefault("duration"));
                    break;
                case Primitive.Debuff when e.Stats.Any(x => x.Stat == "damage_all_pct"):
                    debuffUntil = t + e.Params.GetValueOrDefault("duration");
                    debuffPct = e.Stats.Where(x => x.Stat == "damage_all_pct").Sum(x => x.Amount);
                    break;
                case Primitive.Heal:
                    foreach (var p in e.Target == "party" ? party : [h])
                    {
                        var pv = ViewOf(p, t);
                        p.Life = Math.Min(pv.Stats.MaxLife, p.Life + pv.Stats.MaxLife * e.Params.GetValueOrDefault("pctMaxLife") / 100);
                    }
                    break;
                case Primitive.Dash when e.Conditions.Contains("blast_jumping"):
                    h.AirUntil = t + M.AirSeconds;
                    h.LandPending = true;
                    break;
            }
        }
    }

    /// <summary>An Order begun by another cast (Supreme Commander): its aura's stats on the party until its end.</summary>
    private static void Buff(IReadOnlyList<ModelHero> party, ModelHero caster, string id, ResolvedSkill order, double until, double now)
    {
        foreach (var e in order.Effects.Where(e => e.Primitive is Primitive.Aura or Primitive.Buff && (e.Stats.Count > 0 || e.Conditions.Count > 0)))
            foreach (var p in e.Target == "party" ? party : [caster])
                Put(p, id + "/" + e.Primitive, new ModelBuff(until, SkillRules.AsBuff(id, e), e.Conditions), now);
    }

    /// <summary>
    /// One skill's buff on a hero: the same skill cast by two members does not stack; the
    /// stronger (by the size of its stats) stands while it lasts.
    /// </summary>
    private static void Put(ModelHero hero, string key, ModelBuff buff, double now)
    {
        static double Size(ModelBuff b) => b.Stats.Sum(s => Math.Abs(s.Amount));
        if (hero.Buffs.TryGetValue(key, out var old) && old.Until > now && Size(old) > Size(buff)) return;
        hero.Buffs[key] = buff;
    }

    private static void Sustain(ModelHero h, HeroView v, double dt)
    {
        h.Life = Math.Min(v.Stats.MaxLife, h.Life + v.Stats.LifeRegenPerSecond * dt);
        h.Mana = Math.Min(v.Stats.MaxMana, h.Mana + v.Stats.ManaRegenPerSecond * dt);
    }

    /// <summary>The potion policy: health below its threshold, else mana below its, each when the shared cooldown allows.</summary>
    private bool Potion(ModelHero h, HeroView v, double t)
    {
        if (t < h.PotionReady) return false;
        var p = M.Potions;
        string? drink = null;
        if (h.Life < v.Stats.MaxLife * p.HealthAtLifePct / 100) drink = Best(h, "health");
        else if (h.Mana < v.Stats.MaxMana * p.ManaAtManaPct / 100) drink = Best(h, "mana");
        if (drink is null) return false;
        var b = _r.Content.ItemBases[drink];
        double restore = b.Restore / 100 * (1 + v.Stats.Total("potion_restore_pct") / 100);
        if (drink.StartsWith("health", StringComparison.Ordinal)) h.Life = Math.Min(v.Stats.MaxLife, h.Life + v.Stats.MaxLife * restore);
        else h.Mana = Math.Min(v.Stats.MaxMana, h.Mana + v.Stats.MaxMana * restore);
        h.Potions.Remove(drink);
        h.PotionsDrunk++;
        h.PotionReady = t + p.CooldownSeconds * Math.Max(0.1, 1 + v.Stats.Total("potion_cooldown_pct") / 100);
        return true;
    }

    /// <summary>The largest potion of a kind the hero carries.</summary>
    private string? Best(ModelHero h, string kind) =>
        h.Potions.Where(p => p.StartsWith(kind, StringComparison.Ordinal))
            .OrderByDescending(p => _r.Content.ItemBases[p].Restore).ThenBy(p => p, StringComparer.Ordinal).FirstOrDefault();
}
