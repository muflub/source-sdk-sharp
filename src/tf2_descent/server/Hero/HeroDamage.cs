using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Server.Mode;
using TF2.Shared.Combat;

namespace TF2.Descent.Server.Hero;

/// <summary><c>tf2_descent_debug_damage</c>: the hero damage layer prints each hit's arithmetic (cheat).</summary>
[ConVar("tf2_descent_debug_damage", "0", Flags = ConVarFlags.Cheat,
        Help = "Print each hit the hero damage layer scales: TF2's number, the hero's multiplier, armour and resistance.")]
public sealed class HeroDebugDamageConVar : BoolConVar { }

/// <summary>
/// What a hit is, in the rules' words (HE3): the damage KINDS a weapon
/// family's hit counts as (the outgoing multipliers, <see cref="HeroStats.DamageMultiplier"/>)
/// and the damage TYPE a TF hit is resisted as (<see cref="HeroStats.Resist"/>).
///
/// <para>
/// The rules name four kinds a weapon's hit can be (melee, blast, bullet,
/// projectile; "action" is a skill's) and give each family one damage type,
/// but do not say which kinds a family's hit is. The table here does, until
/// the families carry it: a launcher's hit is blast and projectile (the
/// rules' own example, a rocket), every hitscan gun's is bullet, a thrown or
/// fired object's is projectile, a melee weapon's is melee, and a
/// flamethrower's and a medigun's are none of them (the all-damage stat
/// still applies).
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class HeroHitKinds(TF2DescentRules rules)
{
    /// <summary>The trailing class word TF2 gives a class's copy of a shared weapon (<c>tf_weapon_shotgun_soldier</c>).</summary>
    private readonly string[] _classSuffixes = ["_scout", "_soldier", "_pyro", "_demoman", "_hwg", "_heavy", "_engineer", "_medic", "_sniper", "_spy", "_primary"];

    /// <summary>The kinds a hit of <paramref name="familyId"/> counts as; none for a family the table does not know.</summary>
    public static string[] KindsOf(string familyId) => familyId switch
    {
        "rocket_launcher" or "grenade_launcher" or "stickybomb_launcher" => ["blast", "projectile"],
        "scattergun" or "shotgun" or "pistol" or "revolver" or "smg" or "minigun" or "sniper_rifle" => ["bullet"],
        "flare_gun" or "syringe_gun" or "bow" or "energy_weapon" or "cleaver" => ["projectile"],
        "melee" => ["melee"],
        _ => [],
    };

    /// <summary>
    /// The family of a weapon's item class: the class itself, else the class
    /// without TF2's per-class word (the Soldier's stock shotgun is
    /// <c>tf_weapon_shotgun_soldier</c>; the family names <c>tf_weapon_shotgun</c>).
    /// </summary>
    public FamilyRow? FamilyOf(string? itemClass)
    {
        if (string.IsNullOrEmpty(itemClass))
            return null;
        if (rules.Content.TryGetFamilyOfItemClass(itemClass, out var family))
            return family;

        foreach (string suffix in _classSuffixes)
        {
            if (itemClass.EndsWith(suffix, StringComparison.Ordinal)
                && rules.Content.TryGetFamilyOfItemClass(itemClass[..^suffix.Length], out family))
                return family;
        }

        return null;
    }

    /// <summary>
    /// The damage type a TF hit is resisted as, from its bits, first match
    /// wins: burn or TF's ignite is fire; blast is blast; bullet, buckshot,
    /// club, slash or TF's melee bit is bullet; shock is shock. Anything else
    /// (a fall, a crush, drowning, a generic hit) is unresisted: null.
    /// </summary>
    public static string? ResistedAs(DamageType bits)
    {
        if ((bits & (DamageType.Burn | TFDamageBits.Ignite)) != 0)
            return "fire";
        if ((bits & DamageType.Blast) != 0)
            return "blast";
        if ((bits & (DamageType.Bullet | DamageType.Buckshot | DamageType.Club | DamageType.Slash | TFDamageBits.Melee)) != 0)
            return "bullet";
        if ((bits & DamageType.Shock) != 0)
            return "shock";
        return null;
    }
}

/// <summary>
/// The hero's numbers on a hit (HE3), after TF2's own layer
/// (<c>TFDamageLayer</c>, order 999,701: TF's crit, ramp-up and fall-off are
/// already in the running damage), for any victim the damage rules run for (a
/// player, or an entity that opts in).
///
/// <list type="number">
/// <item>OUTGOING: a hero's hit on anything but himself is multiplied by the
/// hero's <see cref="HeroStats.DamageMultiplier"/> for the hit's kinds — the
/// family of the attacker's active weapon (<see cref="IHeroWeapons"/>,
/// <see cref="HeroHitKinds"/>) — and the all-damage stat. A weapon in no
/// family takes the all-damage stat alone.</item>
/// <item>INCOMING: a hit on a hero from anyone else loses the armour's share
/// (<see cref="HeroRules.ArmourReduction"/>, against <see cref="IAttackerLevel"/>),
/// then the resistance of the hit's damage type (<see cref="HeroHitKinds.ResistedAs"/>,
/// capped by the rules). A hero's own hit on himself (a rocket jump) is not
/// multiplied and takes no armour: only the resistance.</item>
/// </list>
///
/// <para>
/// THE WEAPON is the attacker's active weapon when the hit lands. A rocket or
/// a pipe that lands after its owner switched weapons is scaled as the weapon
/// now in the hands (a shovel's bonus on a rocket); an afterburn tick, as the
/// weapon held when it ticks. Skill ("action") damage is not scaled here.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class HeroDamage(IDescentModeState mode, Heroes heroes, IEngineEntities entities, IHeroWeapons weapons,
                               HeroHitKinds hitKinds, IAttackerLevel levels, TF2DescentRules rules,
                               Skills.ISkillHits skillHits, HeroDebugDamageConVar debug, ILog log)
    : IDamageModifier, IOrderedHook
{
    /// <summary>After TF2's damage layer (999,701), in TF2 Descent's stats block.</summary>
    public const int Order = ModStages.First + 300;

    public int HookOrder => Order;

    /// <summary>The damage kinds of a skill's hit: the rules' action kind and nothing of a weapon's.</summary>
    private readonly string[] _actionKinds = ["action"];

    public float Modify(in DamageContext hit, float damage)
    {
        if (!mode.Active || !(damage > 0f))
            return damage;

        float tf = damage;
        bool self = hit.Attacker == hit.Victim;
        string trace = "";

        if (!self && Hero(hit.Attacker) is { Stats: { } attacker })
        {
            // A skill's hit is an action whatever is in hand (R17): the weapon's family and its
            // damage kinds are not its, so only the sheet's action and all-damage shares apply.
            string? skill = skillHits.Current();
            var family = skill is null ? hitKinds.FamilyOf(weapons.ActiveItemClass(hit.Attacker)) : null;
            string[] kinds = skill is not null ? _actionKinds : family is null ? [] : HeroHitKinds.KindsOf(family.Id);
            double multiplier = attacker.DamageMultiplier(kinds);
            damage = (float)(damage * multiplier);
            if (debug.Value)
                trace += string.Create(CultureInfo.InvariantCulture,
                    $" x{multiplier:0.####} ({(skill is not null ? "skill " + skill : family?.Id ?? "no family")}: {(kinds.Length > 0 ? string.Join('+', kinds) : "all only")})");
        }

        if (Hero(hit.Victim) is { Stats: { } victim })
        {
            double armour = self || hit.Attacker == EntityHandle.None
                ? 0
                : rules.Heroes.ArmourReduction(victim.Armour, levels.LevelOf(hit.Attacker));
            string? type = HeroHitKinds.ResistedAs((DamageType)hit.DamageType);
            double resist = type is null ? 0 : victim.Resist(type);
            damage = (float)(damage * (1 - armour) * (1 - resist));
            if (debug.Value)
                trace += string.Create(CultureInfo.InvariantCulture,
                    $" armour -{armour:0.####} resist {type ?? "none"} -{resist:0.####}");
        }

        if (debug.Value && trace.Length > 0)
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"[TF2Descent/Damage] #{entities.GetIndex(hit.Victim)} from #{entities.GetIndex(hit.Attacker)}: TF {tf:0.00}{trace} = {damage:0.00}\n"));

        return damage;
    }

    /// <summary>The hero <paramref name="player"/> is, or null (no player, a bot, no hero).</summary>
    private HeroComponent? Hero(EntityHandle player) =>
        player != EntityHandle.None && entities.IsPlayer(player) ? heroes.Of(entities.GetIndex(player)) : null;
}
