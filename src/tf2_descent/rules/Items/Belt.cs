using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;

namespace TF2.Descent.Rules.Items;

/// <summary>Why a drink was refused. A refused drink uses nothing.</summary>
public enum DrinkRefusal
{
    /// <summary>The item is no potion (consumables.json names no row for its base).</summary>
    NotAPotion,
    /// <summary>The belt's shared cooldown is still running.</summary>
    OnCooldown,
    /// <summary>What the potion restores is already full (consumables.json's <c>refuseAtFull</c>).</summary>
    Full,
}

/// <summary>
/// A drink: why it was refused, else what it restores (<see cref="PotionResources"/>), how
/// much (before the pool's maximum clips it), and the belt's cooldown it starts, in seconds.
/// </summary>
public sealed record DrinkResult(DrinkRefusal? Refusal, string Restores, double Amount, double CooldownSeconds)
{
    public bool Ok => Refusal is null;
}

/// <summary>
/// The potion belt's rules (plan_descent_logic HE8; plan_tf2_rpg D4, D16; A.5 Field Rations),
/// pure over the content:
/// <list type="bullet">
/// <item>A potion restores its base's <c>restore</c> percent of the maximum of its pool (life
/// or mana), raised by the hero's restore stat (Field Rations: +8 % a rank): instantly.</item>
/// <item>Every potion shares one cooldown, the belt's, shortened by the hero's cooldown stat
/// (Field Rations: -6 % a rank), never below zero.</item>
/// <item>With <c>refuseAtFull</c>, a potion whose pool is full is refused and nothing is used.</item>
/// </list>
/// What leaves the belt (one from the slot's stack, and the backpack's refill) is
/// <see cref="Inventory.InventoryRules.Drink"/>'s.
/// </summary>
public sealed class BeltRules
{
    private readonly DescentContent _content;

    public BeltRules(DescentContent content)
    {
        _content = content;
    }

    private ConsumableSettings S => _content.Consumables;

    /// <summary>The potion row of a base, or null for anything that is no potion.</summary>
    public PotionRow? Potion(string baseId) => S.Potion(baseId);

    /// <summary>What a potion of <paramref name="baseId"/> restores for a hero with <paramref name="stats"/>, out of a pool of <paramref name="max"/>.</summary>
    public double RestoreOf(string baseId, HeroStats stats, double max) =>
        _content.ItemBases[baseId].Restore / 100 * max * Math.Max(0, 1 + stats.Total(S.RestoreStat) / 100);

    /// <summary>The belt's cooldown in seconds for a hero with <paramref name="stats"/>.</summary>
    public double CooldownOf(HeroStats stats) => S.BeltCooldown * Math.Max(0, 1 + stats.Total(S.CooldownStat) / 100);

    /// <summary>
    /// Drinks <paramref name="potion"/>: refused when it is no potion, while
    /// <paramref name="cooldownLeft"/> seconds of the belt's cooldown remain, or when its pool
    /// is full (<c>refuseAtFull</c>); else what it restores out of the pool's maximum and the
    /// cooldown it starts.
    /// </summary>
    public DrinkResult Drink(ItemInstance potion, HeroStats stats, double life, double lifeMax, double mana, double manaMax,
                             double cooldownLeft)
    {
        if (Potion(potion.BaseId) is not { } row)
            return new DrinkResult(DrinkRefusal.NotAPotion, "", 0, 0);
        if (cooldownLeft > 0)
            return new DrinkResult(DrinkRefusal.OnCooldown, row.Restores, 0, 0);
        bool isLife = row.Restores == PotionResources.Life;
        double now = isLife ? life : mana, max = isLife ? lifeMax : manaMax;
        if (S.RefuseAtFull && now >= max)
            return new DrinkResult(DrinkRefusal.Full, row.Restores, 0, 0);
        return new DrinkResult(null, row.Restores, RestoreOf(potion.BaseId, stats, max), CooldownOf(stats));
    }
}
