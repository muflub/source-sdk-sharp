using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Conditions;
using TF2.Shared.Econ;

namespace TF2.Shared;

/// <summary>
/// The weapon a hook is asked about: its row, its script, and when an owner
/// is known (an attack), who holds it, their conditions, the item's
/// attributes and the player's own manager (stock's
/// <c>CTFPlayer::m_AttributeManager</c>, the walk a
/// <c>CALL_ATTRIB_HOOK_FLOAT_ON_OTHER</c> takes). Asked for a clip size or a
/// fire interval the core names no owner, so <see cref="OwnerIndex"/> is 0
/// and the rest null there.
/// </summary>
public readonly record struct TFWeaponHookContext(
    WeaponType Type,
    TFWeaponScript Script,
    int OwnerIndex = 0,
    TFConditions? Conditions = null,
    IAttributeProvider? Item = null,
    IAttributeProvider? Manager = null);

/// <summary>
/// A link in every TF weapon's chain (plan D10): <see cref="TFWeaponBehaviour"/>
/// asks each hook at the same points the core asks the behaviour, so a mod
/// layered on TF2 (Descent's item effects) changes a TF weapon by adding a hook,
/// never by subclassing one. TF2's own rules are the chain's first members:
/// the item attributes (<see cref="TFAttributeWeaponHook"/>) and the crit
/// boost (<see cref="TFCritBoostHook"/>).
///
/// <para>
/// The ORDER is the hook's assembly layer, then its <see cref="IOrderedHook.HookOrder"/>
/// (<see cref="TFWeaponHookChain"/>): TF2's hooks run before a mod's built on it
/// whatever their numbers. A link is handed the value so far and returns the
/// next; every default is "unchanged". Hooks run on the server and on every
/// client replay of the command, so they decide from their inputs alone.
/// </para>
/// </summary>
public interface ITFWeaponHook : IOrderedHook
{
    /// <summary>The full clip (<c>GetMaxClip1</c>'s <c>mult_clipsize</c>).</summary>
    int ModifyClip(in TFWeaponHookContext weapon, int clip) => clip;

    /// <summary>The seconds between shots (<c>ApplyFireDelay</c>'s <c>mult_postfiredelay</c>).</summary>
    float ModifyInterval(in TFWeaponHookContext weapon, float interval) => interval;

    /// <summary>
    /// The seconds a reload takes — <c>CTFWeaponBase::SetReloadTimer</c>'s
    /// scaled duration (<c>tf_weaponbase.cpp:2266-2331</c>): the script's or
    /// the animation's time as it would stand, which the hooks multiply. The
    /// reload's clock is predicted state, so the arms read from the context
    /// alone.
    /// </summary>
    float ModifyReloadTime(in TFWeaponHookContext weapon, float duration) => duration;

    /// <summary>A shot's damage before a crit (<c>GetProjectileDamage</c>'s <c>mult_dmg</c>).</summary>
    float ModifyDamage(in TFWeaponHookContext weapon, float damage) => damage;

    /// <summary>
    /// A fully charged sniper shot's damage (<c>sniper_full_charge_damage_bonus</c>,
    /// <c>CTFSniperRifle::GetProjectileDamage</c>, tf_weapon_sniperrifle.cpp:896-899),
    /// asked after the chain's <see cref="ModifyDamage"/> as stock asks it last.
    /// </summary>
    float ModifyFullChargeDamage(in TFWeaponHookContext weapon, float damage) => damage;

    /// <summary>Asked before a primary attack spends anything; false vetoes it.</summary>
    bool BeforeFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack) => true;

    /// <summary>Told after a primary attack has fired.</summary>
    void AfterFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack) { }

    /// <summary>The owner's side of the crit roll: whether it may crit, a boost, the chance's scale.</summary>
    TFCritOwner CritPolicy(in TFWeaponHookContext weapon, TFCritOwner policy) => policy;

    /// <summary>
    /// A kind's own crit, decided whole: <c>CTFWeaponBase::CalcIsAttackCritical</c>'s
    /// virtual helpers that a kind overrides — <c>CTFKnife</c>'s answer
    /// <c>IsBackstab()</c> (<c>tf_weapon_knife.cpp:487-500</c>) — so the melee
    /// helper never runs: no draw, no bucket paid, no check counted. The first
    /// hook that answers wins; null rolls as usual.
    /// </summary>
    TFCritResult? CritAnswer(in TFWeaponHookContext weapon) => null;

    /// <summary>
    /// The sniper charge's rate (<c>mult_sniper_charge_per_sec</c>,
    /// <c>CTFSniperRifle::ApplyChargeSpeedModifications</c>,
    /// tf_weapon_sniperrifle.cpp:643-645), asked before the accrual clamps it
    /// to twice the stock (:381-382).
    /// </summary>
    float ModifyChargeRate(in TFWeaponHookContext weapon, float rate) => rate;

    /// <summary>
    /// The seconds the minigun's wind-up takes (<c>mult_minigun_spinup_time</c>,
    /// <c>CTFMinigun::SharedAttack</c>'s idle case, tf_weapon_minigun.cpp:259-260).
    /// The damage and spread ramps keep the plain constant, as stock's
    /// <c>GetWindUpDuration() - TF_MINIGUN_SPINUP_TIME</c> does (:1001, :1024).
    /// </summary>
    float ModifySpinUpTime(in TFWeaponHookContext weapon, float seconds) => seconds;

    /// <summary>
    /// The rounds a second the spun minigun drains
    /// (<c>uses_ammo_while_aiming</c>, <c>CTFMinigun::SharedAttack</c>'s tail,
    /// tf_weapon_minigun.cpp:452-453): stock's <c>CALL_ATTRIB_HOOK_INT</c>
    /// over zero, so a percentage-format attribute drains nothing, as stock's
    /// does. The meter accrues over the commands and cuts whole rounds past
    /// one (:456-464).
    /// </summary>
    float AmmoDrainPerSecond(in TFWeaponHookContext weapon, float perSecond) => perSecond;

    /// <summary>
    /// The projectile sweep a fired shot asks
    /// (<c>attack_projectiles</c>, <c>CTFMinigun::SharedAttack</c>'s fired
    /// branch, <c>tf_weapon_minigun.cpp:376-377</c>): stock's
    /// <c>CALL_ATTRIB_HOOK_INT</c> over zero.
    /// </summary>
    int AttackEnemyProjectiles(in TFWeaponHookContext weapon, int count) => count;

    /// <summary>
    /// The ring of fire a spun command asks (<c>ring_of_fire_while_aiming</c>,
    /// <c>CTFMinigun::SharedAttack</c>, <c>tf_weapon_minigun.cpp:440-448</c>):
    /// stock's <c>CALL_ATTRIB_HOOK_INT</c> over zero; the value is the plasma
    /// each ringed enemy takes.
    /// </summary>
    int RingOfFireDamage(in TFWeaponHookContext weapon, int damage) => damage;

    /// <summary>
    /// The melee smack's reach (<c>melee_range_multiplier</c>,
    /// <c>tf_weaponbase_melee.cpp:420</c>), asked with the row's
    /// <see cref="WeaponType.MeleeRange"/>.
    /// </summary>
    float ModifyMeleeRange(in TFWeaponHookContext weapon, float range) => range;

    /// <summary>
    /// The melee swing's hull (<c>melee_bounds_multiplier</c>,
    /// <c>tf_weaponbase_melee.cpp:399</c>), asked with the 18-unit box.
    /// </summary>
    float ModifyMeleeBounds(in TFWeaponHookContext weapon, float bounds) => bounds;

    /// <summary>
    /// Whether the smack's hit asks the crit-from-behind arm
    /// (<c>crit_from_behind</c>, <c>tf_weaponbase_melee.cpp:819-820</c>).
    /// </summary>
    bool CritFromBehind(in TFWeaponHookContext weapon) => false;

    /// <summary>
    /// Whether the load leaves whole (<c>auto_fires_full_clip</c>,
    /// <c>CTFWeaponBase::AutoFiresFullClip</c>, tf_weaponbase.cpp:1871-1876):
    /// such a weapon reloads by its own arm and never the idle top-up.
    /// </summary>
    bool FiresFullClip(in TFWeaponHookContext weapon) => false;
}

/// <summary>
/// The <see cref="ITFWeaponHook"/> chain, ordered by assembly layer and then
/// <see cref="IOrderedHook.HookOrder"/>. The layer is the assembly's name
/// without its side suffix (<c>TF2.Server</c> and <c>TF2.Client</c> are both
/// <c>TF2</c>), so the order is the same on both sides; a raw name would put
/// <c>TF2.Descent.Server</c> before <c>TF2.Server</c> and <c>TF2.Client</c>
/// before <c>TF2.Descent.Client</c>. A hook that throws is reported by name
/// and the value before it stands.
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFWeaponHookChain(IHooks<ITFWeaponHook>? hooks = null, ILog? log = null)
{
    private IReadOnlyList<ITFWeaponHook>? _sorted;

    /// <summary>A chain over a fixed list (facts, and composition without a container).</summary>
    public static TFWeaponHookChain Of(params ITFWeaponHook[] hooks) => new(new FixedHooks(hooks));

    /// <summary>The hooks in the order they are asked.</summary>
    public IReadOnlyList<ITFWeaponHook> All => _sorted ??= Sort(hooks?.All ?? []);

    /// <summary>An assembly's layer, the core's rule (<see cref="HookOrder.Layer(Assembly)"/>): its name without the side segments.</summary>
    public static string Layer(Assembly assembly) => HookOrder.Layer(assembly);

    public static IReadOnlyList<ITFWeaponHook> Sort(IEnumerable<ITFWeaponHook> hooks) =>
        hooks.OrderBy(h => Layer(h.GetType().Assembly), StringComparer.Ordinal)
             .ThenBy(h => h.HookOrder)
             .ToArray();

    public int ModifyClip(in TFWeaponHookContext weapon, int clip)
    {
        foreach (var hook in All)
            clip = Guard(hook, nameof(ITFWeaponHook.ModifyClip), clip, weapon, static (h, w, v) => h.ModifyClip(in w, v));
        return clip;
    }

    public float ModifyInterval(in TFWeaponHookContext weapon, float interval)
    {
        foreach (var hook in All)
            interval = Guard(hook, nameof(ITFWeaponHook.ModifyInterval), interval, weapon, static (h, w, v) => h.ModifyInterval(in w, v));
        return interval;
    }

    public float ModifyReloadTime(in TFWeaponHookContext weapon, float duration)
    {
        foreach (var hook in All)
            duration = Guard(hook, nameof(ITFWeaponHook.ModifyReloadTime), duration, weapon, static (h, w, v) => h.ModifyReloadTime(in w, v));
        return duration;
    }

    public float ModifyDamage(in TFWeaponHookContext weapon, float damage)
    {
        foreach (var hook in All)
            damage = Guard(hook, nameof(ITFWeaponHook.ModifyDamage), damage, weapon, static (h, w, v) => h.ModifyDamage(in w, v));
        return damage;
    }

    public float ModifyFullChargeDamage(in TFWeaponHookContext weapon, float damage)
    {
        foreach (var hook in All)
            damage = Guard(hook, nameof(ITFWeaponHook.ModifyFullChargeDamage), damage, weapon, static (h, w, v) => h.ModifyFullChargeDamage(in w, v));
        return damage;
    }

    public float ModifyChargeRate(in TFWeaponHookContext weapon, float rate)
    {
        foreach (var hook in All)
            rate = Guard(hook, nameof(ITFWeaponHook.ModifyChargeRate), rate, weapon, static (h, w, v) => h.ModifyChargeRate(in w, v));
        return rate;
    }

    public float ModifySpinUpTime(in TFWeaponHookContext weapon, float seconds)
    {
        foreach (var hook in All)
            seconds = Guard(hook, nameof(ITFWeaponHook.ModifySpinUpTime), seconds, weapon, static (h, w, v) => h.ModifySpinUpTime(in w, v));
        return seconds;
    }

    public float AmmoDrainPerSecond(in TFWeaponHookContext weapon, float perSecond)
    {
        foreach (var hook in All)
            perSecond = Guard(hook, nameof(ITFWeaponHook.AmmoDrainPerSecond), perSecond, weapon, static (h, w, v) => h.AmmoDrainPerSecond(in w, v));
        return perSecond;
    }

    public int AttackEnemyProjectiles(in TFWeaponHookContext weapon, int count)
    {
        foreach (var hook in All)
            count = Guard(hook, nameof(ITFWeaponHook.AttackEnemyProjectiles), count, weapon, static (h, w, v) => h.AttackEnemyProjectiles(in w, v));
        return count;
    }

    public int RingOfFireDamage(in TFWeaponHookContext weapon, int damage)
    {
        foreach (var hook in All)
            damage = Guard(hook, nameof(ITFWeaponHook.RingOfFireDamage), damage, weapon, static (h, w, v) => h.RingOfFireDamage(in w, v));
        return damage;
    }

    public float ModifyMeleeRange(in TFWeaponHookContext weapon, float range)
    {
        foreach (var hook in All)
            range = Guard(hook, nameof(ITFWeaponHook.ModifyMeleeRange), range, weapon, static (h, w, v) => h.ModifyMeleeRange(in w, v));
        return range;
    }

    public float ModifyMeleeBounds(in TFWeaponHookContext weapon, float bounds)
    {
        foreach (var hook in All)
            bounds = Guard(hook, nameof(ITFWeaponHook.ModifyMeleeBounds), bounds, weapon, static (h, w, v) => h.ModifyMeleeBounds(in w, v));
        return bounds;
    }

    /// <summary>Any hook asking for the behind-crit arm turns the question yes.</summary>
    public bool CritFromBehind(in TFWeaponHookContext weapon)
    {
        foreach (var hook in All)
        {
            try
            {
                if (hook.CritFromBehind(in weapon))
                    return true;
            }
            catch (Exception e)
            {
                Report(hook, nameof(ITFWeaponHook.CritFromBehind), e);
            }
        }

        return false;
    }

    /// <summary>Any hook loading the weapon whole turns the question yes.</summary>
    public bool FiresFullClip(in TFWeaponHookContext weapon)
    {
        foreach (var hook in All)
        {
            try
            {
                if (hook.FiresFullClip(in weapon))
                    return true;
            }
            catch (Exception e)
            {
                Report(hook, nameof(ITFWeaponHook.FiresFullClip), e);
            }
        }

        return false;
    }

    public TFCritOwner CritPolicy(in TFWeaponHookContext weapon, TFCritOwner policy)
    {
        foreach (var hook in All)
            policy = Guard(hook, nameof(ITFWeaponHook.CritPolicy), policy, weapon, static (h, w, v) => h.CritPolicy(in w, v));
        return policy;
    }

    /// <summary>The first hook that answers a kind's whole crit, else null.</summary>
    public TFCritResult? CritAnswer(in TFWeaponHookContext weapon)
    {
        foreach (var hook in All)
        {
            try
            {
                if (hook.CritAnswer(in weapon) is { } answer)
                    return answer;
            }
            catch (Exception e)
            {
                Report(hook, nameof(ITFWeaponHook.CritAnswer), e);
            }
        }

        return null;
    }

    /// <summary>Every hook is asked (a veto does not stop the others); any false vetoes.</summary>
    public bool BeforeFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack)
    {
        bool fire = true;
        foreach (var hook in All)
        {
            try
            {
                fire &= hook.BeforeFire(in weapon, in attack);
            }
            catch (Exception e)
            {
                Report(hook, nameof(ITFWeaponHook.BeforeFire), e);
            }
        }
        return fire;
    }

    public void AfterFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack)
    {
        foreach (var hook in All)
        {
            try
            {
                hook.AfterFire(in weapon, in attack);
            }
            catch (Exception e)
            {
                Report(hook, nameof(ITFWeaponHook.AfterFire), e);
            }
        }
    }

    private T Guard<T>(ITFWeaponHook hook, string what, T value, in TFWeaponHookContext weapon,
                       Func<ITFWeaponHook, TFWeaponHookContext, T, T> call)
    {
        try
        {
            return call(hook, weapon, value);
        }
        catch (Exception e)
        {
            Report(hook, what, e);
            return value;
        }
    }

    private void Report(ITFWeaponHook hook, string what, Exception e) =>
        log?.Warning($"[TF2/Weapons] ITFWeaponHook.{what} on {hook.GetType().FullName} threw: {e.Message}; the value before it stands\n");

    private sealed class FixedHooks(ITFWeaponHook[] hooks) : IHooks<ITFWeaponHook>
    {
        public IReadOnlyList<ITFWeaponHook> All => hooks;

        public void Each(string what, Action<ITFWeaponHook> call)
        {
            foreach (var hook in hooks)
                call(hook);
        }
    }
}

/// <summary>Registers the <see cref="ITFWeaponHook"/> dispatch on each side.</summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFWeaponHooksRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<ITFWeaponHook>();
}
