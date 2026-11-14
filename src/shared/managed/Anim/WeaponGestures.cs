//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared.Anim;

/// <summary>
/// Which weapon action asks the player-animation machine for a gesture: the
/// asks the stock fire and reload paths make of <c>DoAnimationEvent</c>
/// (<c>PLAYER_ATTACK1</c> from a fire, <c>PLAYER_RELOAD</c> from a reload),
/// named by what the weapon did rather than by who did it.
/// </summary>
public enum WeaponGestureTrigger
{
    /// <summary>A shot left the weapon (the clip or the reserve went down outside a reload).</summary>
    Shot,

    /// <summary>A melee swing, hit or miss alike.</summary>
    Swing,

    /// <summary>An alt-fire paid from the secondary pool.</summary>
    Secondary,

    /// <summary>A reload began.</summary>
    ReloadStart,

    /// <summary>A shell-by-shell reload put a round in.</summary>
    ReloadShell,

    /// <summary>A reload finished or was abandoned.</summary>
    ReloadEnd,
}

/// <summary>
/// A mode's answer to which player-animation event a weapon action shows: the
/// seam between the core's fire path and the gesture table, so a mode's own
/// weapons name their own events instead of a core name table guessing.
/// TF2 is why: stock's <c>CTFWeaponBase</c> fires
/// <c>DoAnimationEvent( PLAYER_ATTACK1 )</c> per family (tf_weaponbase_gun.cpp,
/// tf_weaponbase_melee.cpp) and <c>PLAYER_RELOAD</c> per reload shape
/// (tf_weaponbase.cpp), none of which an HL2MP <c>weapon_*</c> table spells.
/// </summary>
public interface IWeaponGestureRule
{
    /// <summary>The event to show for <paramref name="trigger"/> on a weapon of this class, or null for no opinion.</summary>
    /// <param name="trigger">What the weapon just did.</param>
    /// <param name="weaponClass">The weapon row's name, as the table spells it.</param>
    PlayerAnimEvent? Gesture(WeaponGestureTrigger trigger, string weaponClass);
}

/// <summary>
/// The player-animation event a weapon action shows: the registered
/// <see cref="IWeaponGestureRule"/>s in <see cref="IOrderedHook.HookOrder"/>
/// order, first claim wins; the core's HL2MP tables
/// (<see cref="PlayerAnimGestures"/>) stand behind them as the mode-less
/// default. Server-side only: the event goes out once, through
/// <c>TE_PlayerAnimEvent</c>, and every client plays it from the network.
/// </summary>
public sealed class WeaponGestures
{
    private readonly IHooks<IWeaponGestureRule>? _rules;
    private readonly ILog? _log;

    /// <param name="rules">The registered rules. Null is no rules: the HL2MP tables answer alone.</param>
    public WeaponGestures(IHooks<IWeaponGestureRule>? rules = null, ILog? log = null)
    {
        _rules = rules;
        _log = log;
    }

    /// <summary>No rules: the core's HL2MP answer, which is what a composition with no mod assemblies registers.</summary>
    public static WeaponGestures Stock() => new();

    /// <summary>
    /// The event the machine plays for a weapon action: the first rule with an
    /// opinion, else the HL2MP table the core has always answered. A rule that
    /// throws is named and skipped.
    /// </summary>
    public PlayerAnimEvent? Gesture(WeaponGestureTrigger trigger, string weaponClass)
    {
        if (_rules is not null)
        {
            foreach (var rule in _rules.All)
            {
                try
                {
                    if (rule.Gesture(trigger, weaponClass) is { } claimed)
                        return claimed;
                }
                catch (Exception e)
                {
                    _log?.Warning($"[SourceSharp] weapon gesture rule {rule.GetType().FullName} threw: {e.Message}; "
                                  + $"{weaponClass} keeps the core's answer\n");
                }
            }
        }

        return Default(trigger, weaponClass);
    }

    /// <summary>The core's own answer: the HL2MP acttables and fire paths.</summary>
    private static PlayerAnimEvent? Default(WeaponGestureTrigger trigger, string weaponClass) => trigger switch
    {
        WeaponGestureTrigger.Shot
            when PlayerAnimGestures.ShotShowsGesture(weaponClass) => PlayerAnimEvent.AttackPrimary,
        WeaponGestureTrigger.Swing
            when PlayerAnimGestures.SwingShowsGesture(weaponClass) => PlayerAnimEvent.AttackPrimary,
        WeaponGestureTrigger.Secondary
            when PlayerAnimGestures.SecondaryShowsGesture(weaponClass) => PlayerAnimEvent.AttackPrimary,
        WeaponGestureTrigger.ReloadStart
            when PlayerAnimGestures.ReloadShowsGesture(weaponClass) => PlayerAnimEvent.Reload,
        _ => null,
    };
}
