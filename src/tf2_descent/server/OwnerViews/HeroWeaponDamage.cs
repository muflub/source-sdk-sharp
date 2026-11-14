using SourceSharp.Abi;

using TF2.Descent.Server.Hero;
using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Descent.Server.OwnerViews;

/// <summary>The damage range a hero's weapon in hand hits for, as the sheet shows it (C.1).</summary>
public interface IHeroWeaponDamage
{
    /// <summary>The least and most one hit of the weapon <paramref name="hero"/> holds does; null with no weapon or no stats.</summary>
    (int Min, int Max)? RangeOf(HeroComponent hero);
}

/// <summary>
/// The sheet's damage range (plan_descent_logic UI3) over TF2's own numbers:
/// one hit is the weapon script's damage times its bullets a shot, times the
/// weapon's <c>mult_dmg</c> (the TF attribute walk, so a rolled affix shows),
/// times the hero's own multiplier for the weapon family's damage kinds
/// (<see cref="TF2.Descent.Rules.Hero.HeroStats.DamageMultiplier"/>, the one
/// <see cref="HeroDamage"/> applies). The range is TF's distance band around
/// it (<c>TFDamageModifyRules</c>: the variance is ± <c>tf_damage_range</c>,
/// 0.5, of the damage; the rocket launchers' near bonus is halved; the
/// grenade launchers' range is a fifth); a melee weapon has no band. Crits are
/// not in it.
/// </summary>
[SideService(Side.Server, As = [typeof(IHeroWeaponDamage)])]
public sealed class TFHeroWeaponDamage(ITFWeaponOwners owners, IEnginePlayers players, TFWeaponScripts scripts,
                                       AttributeHooks hooks, HeroHitKinds kinds, IHeroWeapons weapons) : IHeroWeaponDamage
{
    /// <summary><c>tf_damage_range</c>'s default: the variance's half-width as a fraction of the damage.</summary>
    public const double Range = 0.5;

    public (int Min, int Max)? RangeOf(HeroComponent hero)
    {
        if (hero.Stats is not { } stats || hero.Player == EntityHandle.None)
            return null;
        string weapon = players.GetActiveWeapon(hero.Player);
        if (weapon.Length == 0 || scripts.GetQuiet(weapon) is not { } script)
            return null;

        var mode = script.Primary;
        double damage = mode.Damage * Math.Max(1, mode.BulletsPerShot);
        damage *= hooks.Float(1f, "mult_dmg", owners.ActiveItemOf(hero.PlayerIndex));
        var family = kinds.FamilyOf(weapons.ActiveItemClass(hero.Player));
        damage *= stats.DamageMultiplier(family is null ? [] : HeroHitKinds.KindsOf(family));
        var (near, far) = Band(family?.Id);
        return ((int)Math.Round(damage * (1 - far), MidpointRounding.AwayFromZero),
                (int)Math.Round(damage * (1 + near), MidpointRounding.AwayFromZero));
    }

    /// <summary>The band's near bonus and far penalty for a family, as fractions of the damage.</summary>
    public static (double Near, double Far) Band(string? family) => family switch
    {
        "melee" => (0, 0),
        "rocket_launcher" => (Range / 2, Range),
        "grenade_launcher" => (Range / 5, Range / 5),
        _ => (Range, Range),
    };
}
