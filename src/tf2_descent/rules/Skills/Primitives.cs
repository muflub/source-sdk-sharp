namespace TF2.Descent.Rules.Skills;

/// <summary>
/// The fixed set of effect primitives a skill or spell is composed from (plan_tf2_rpg 2e,
/// plan_descent_logic RU6). A descriptor says what happens and with which per-rank
/// numbers; the engine package (HE6) binds each primitive to the world. A new primitive is
/// a new member here, a row in <see cref="Primitives.All"/>, and a binder.
/// </summary>
public enum Primitive
{
    /// <summary>A projectile (a rocket) that deals <c>damage</c> in <c>radius</c> where it lands.</summary>
    Projectile,
    /// <summary>A trace that deals <c>damage</c> to what it hits within <c>range</c>.</summary>
    Hitscan,
    /// <summary>Damage in a sphere: at the caster, or at a marked point after <c>delay</c>, in <c>count</c> shells across <c>area</c>.</summary>
    AreaBurst,
    /// <summary>A timed field around the caster: everyone of <c>target</c> within <c>radius</c> gets its stats and conditions.</summary>
    Aura,
    /// <summary>Stats and conditions on the caster (and allies within <c>radius</c>) for <c>duration</c>.</summary>
    Buff,
    /// <summary>Stats and conditions on enemies hit or within <c>radius</c> for <c>duration</c>.</summary>
    Debuff,
    /// <summary><c>count</c> allies that live for <c>duration</c> with <c>life</c>.</summary>
    Summon,
    /// <summary>A launch: <c>impulse</c> along the aim, or to a point within <c>range</c>.</summary>
    Dash,
    /// <summary>Heals <c>amount</c> life plus <c>pctMaxLife</c> % of max life, over <c>duration</c>; its conditions cleanse.</summary>
    Heal,
    /// <summary>Stats that hold while the rank is held (an upgrade); <c>capacity</c> names a store the inventory rules size from the rank.</summary>
    Passive,
    /// <summary>Hold to build for <c>time</c> seconds (in <c>stages</c>), release to fire the effects after it.</summary>
    Charge,
    /// <summary>On while toggled, draining <c>drain</c> mana a second; its stats and conditions hold while on.</summary>
    Toggle,
    /// <summary>When its <c>trigger</c> happens: its stats and conditions for <c>duration</c>, a heal, a mana refund.</summary>
    Triggered,
    /// <summary>Robots within <c>radius</c> target the caster for <c>duration</c> (a threat-table entry).</summary>
    Threat,
}

/// <summary>A primitive's JSON name, the parameters it takes (each a <see cref="RankCurve"/>), and their defaults.</summary>
public sealed record PrimitiveSpec(
    Primitive Primitive,
    string Name,
    IReadOnlyList<string> Required,
    IReadOnlyDictionary<string, double> Optional,
    bool DealsDamage)
{
    public bool Takes(string param) => Required.Contains(param) || Optional.ContainsKey(param);
}

/// <summary>
/// The vocabulary of effects: primitives and their parameters, triggers, targets and
/// conditions. Closed sets in code because the binder implements each one; every NUMBER is
/// the table's. Members return fresh collections (a mod holds no static fields, SS0010).
/// </summary>
public static class Primitives
{
    /// <summary>The parameters whose value is a whole number: the resolved value is floored.</summary>
    public static IReadOnlyList<string> Whole => ["count", "stages"];

    public static IReadOnlyList<PrimitiveSpec> All() =>
    [
        new(Primitive.Projectile, "projectile", ["damage"],
            new Dictionary<string, double> { ["radius"] = 0, ["speed"] = 0, ["count"] = 1, ["spreadDeg"] = 0, ["duration"] = 0 }, true),
        new(Primitive.Hitscan, "hitscan", ["damage", "range"],
            new Dictionary<string, double> { ["count"] = 1, ["spreadDeg"] = 0 }, true),
        new(Primitive.AreaBurst, "area_burst", ["damage", "radius"],
            new Dictionary<string, double> { ["delay"] = 0, ["count"] = 1, ["area"] = 0 }, true),
        new(Primitive.Aura, "aura", ["radius", "duration"], new Dictionary<string, double>(), false),
        new(Primitive.Buff, "buff", ["duration"], new Dictionary<string, double> { ["radius"] = 0 }, false),
        new(Primitive.Debuff, "debuff", ["duration"], new Dictionary<string, double> { ["radius"] = 0, ["bossDuration"] = 0 }, false),
        new(Primitive.Summon, "summon", ["count", "duration"], new Dictionary<string, double> { ["life"] = 0 }, false),
        new(Primitive.Dash, "dash", ["impulse"], new Dictionary<string, double> { ["range"] = 0 }, false),
        new(Primitive.Heal, "heal", [],
            new Dictionary<string, double> { ["amount"] = 0, ["pctMaxLife"] = 0, ["duration"] = 0, ["radius"] = 0 }, false),
        new(Primitive.Passive, "passive", [], new Dictionary<string, double>(), false),
        new(Primitive.Charge, "charge", ["time"],
            new Dictionary<string, double> { ["stages"] = 1, ["misfireAfter"] = 0, ["releaseTime"] = 0, ["manaPerStage"] = 0 }, false),
        new(Primitive.Toggle, "toggle", ["drain"], new Dictionary<string, double>(), false),
        new(Primitive.Triggered, "triggered", [],
            new Dictionary<string, double>
            {
                ["duration"] = 0, ["heal"] = 0, ["pctMaxLife"] = 0, ["manaRefund"] = 0, ["maxTargets"] = 0, ["perSecondCap"] = 0, ["delay"] = 0,
                ["bonusDamagePct"] = 0,
            }, false),
        new(Primitive.Threat, "threat", ["radius", "duration"], new Dictionary<string, double>(), false),
    ];

    public static PrimitiveSpec? Find(string name) => All().FirstOrDefault(p => p.Name == name);

    public static PrimitiveSpec Of(Primitive p) => All().First(s => s.Primitive == p);

    /// <summary>
    /// What fires a triggered effect (an effect with a trigger fires on it instead of on
    /// cast). <c>blast_jumping</c> holds while the hero's BlastJumping state does (stock
    /// TF_COND_BLASTJUMPING); <c>knock_airborne</c> is any robot the hero knocks off the ground
    /// (L12: robots are pushed as players are).
    /// </summary>
    public static IReadOnlyList<string> Triggers =>
    [
        "on_hit",                  // any hit with any weapon or action
        "on_direct_hit",           // a projectile's direct hit
        "on_melee_hit",
        "on_melee_kill",
        "on_kill_airborne",        // a kill while the hero is BlastJumping
        "on_melee_hit_airborne",   // a melee hit while the hero is BlastJumping
        "on_hit_airborne_target",  // hitting a target that is airborne
        "on_knock_airborne",       // knocking a robot airborne, by any means
        "blast_jump_landing",      // landing from a blast jump
        "blast_jumping",           // while BlastJumping
        "dash_landing",            // the end of this skill's own dash
        "no_damage_taken",         // after `delay` seconds without taking damage
        "per_10pct_health_missing",// its stats scale with each 10 % of max life missing
    ];

    /// <summary>
    /// Who an effect lands on: the caster, the caster and allies in its radius, enemies (or
    /// only the non-boss ones, or only bosses: EMP Charge stuns robots and slows bosses), or a
    /// point the caster marks.
    /// </summary>
    public static IReadOnlyList<string> Targets => ["self", "party", "enemies", "enemy_minions", "enemy_bosses", "point"];

    /// <summary>The conditions an effect can apply: hero conditions map to TF conditions, robot ones to Descent's status effects (E9).</summary>
    public static IReadOnlyList<string> Conditions =>
    [
        "crit_boost",        // every hit crits
        "mini_crit_marked",  // the target takes mini-crits from the whole party
        "melee_crit",        // melee hits crit
        "no_ammo",           // weapons use no ammo
        "no_reload",         // weapons need no reload
        "crit_immune",       // cannot be crit
        "no_fall_damage",
        "slow_fall",
        "air_control",
        "invulnerable",
        "knock_up",          // the burst lifts what it hits
        "emp_stun",          // a robot stops acting
        "stun",
        "slowed",
        "cleanse_burn",
        "cleanse_bleed",
        "cleanse_slow",
        "issue_all_orders",  // every Order the hero holds, at the hero's ranks
        "orders_stack",      // issuing an Order no longer cancels the one in force
        "blast_jumping",     // the hero is BlastJumping until it lands (a mana-cast jump counts as one)
        "burn",              // the target burns (E9's burn on a robot)
    ];

    /// <summary>What a cast requires of the hero besides mana and cooldown.</summary>
    public static IReadOnlyList<string> Requirements => ["blast_jumping"];

    /// <summary>The stores a passive's <c>capacity</c> may name; the inventory rules size them from the rank.</summary>
    public static IReadOnlyList<string> Capacities => ["backpack", "stash"];

    /// <summary>Tags a skill may carry: an <c>order</c> is one of the Orders (A.4).</summary>
    public static IReadOnlyList<string> Tags => ["order"];
}
