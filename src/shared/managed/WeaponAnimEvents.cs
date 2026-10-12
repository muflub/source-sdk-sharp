using System.Collections.Immutable;
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What each weapon's view model is expected to fire, per animation.
///
/// <para>
/// Every row here was DUMPED from the .mdl rather than guessed, and the
/// dumping is the point: the first draft of this table, written the way
/// anybody would write it — "each weapon's primary attack fires
/// AE_MUZZLEFLASH" — is wrong about three of the nine.
/// </para>
///
/// <list type="bullet">
///   <item><description>
///     <b>The crowbar fires nothing at all.</b> Nine sequences, zero events on
///     any of them. A melee hit is the weapon machine's business and the model
///     has no opinion about it.
///   </description></item>
///   <item><description>
///     <b>The crossbow's primary fires nothing</b> either — it carries events
///     on <c>draw</c> and <c>reload</c> and none on <c>fire</c>.
///   </description></item>
///   <item><description>
///     <b>The RPG's is an OLD-SYSTEM event</b>, a bare 5001 with no name, so an
///     expectation that could only be written as a name could not describe it
///     and a check that only understood names would report a false MISSING.
///   </description></item>
///   <item><description>
///     <b>The grenade's primary is a THROW</b> (<c>ACT_VM_THROW</c>, which is
///     already one of the fallbacks the resolver tries) and its event is the
///     old-system 3005.
///   </description></item>
/// </list>
///
/// <para>
/// An empty row is therefore a real answer and not a gap: it says this model
/// was looked at and carries nothing on that animation. What is NOT recorded
/// here is anything nobody has checked — a weapon absent from the switch below
/// reports "no expectations declared" out loud, because a silent pass is how
/// an assumption gets back in.
/// </para>
///
/// <para>
/// SIDE-NESS: bucket 5, a shared declaration. These are facts about model
/// files, and both halves load the same files.
/// </para>
/// </summary>
public static class WeaponAnimEvents
{
    /// <summary><c>AE_MUZZLEFLASH</c>, the named event five of the nine carry.</summary>
    public const string MuzzleFlash = "AE_MUZZLEFLASH";

    /// <summary><c>AE_CL_PLAYSOUND</c>, on the reloads that sound themselves.</summary>
    public const string PlaySound = "AE_CL_PLAYSOUND";

    /// <summary>The RPG's unnamed primary-attack event, dumped from v_rpg.mdl.</summary>
    public const int RpgFireEvent = 5001;

    /// <summary>The grenade's unnamed throw event, dumped from v_grenade.mdl.</summary>
    public const int GrenadeThrowEvent = 3005;

    private static readonly ImmutableArray<AnimationEventExpectation> None = [];

    /// <summary>
    /// What this weapon class's view model should carry, or an empty list for a
    /// model that genuinely carries nothing.
    ///
    /// <para>
    /// Null — distinct from empty — for a class nobody has looked at, so the
    /// report can say "no expectations declared" instead of "all clear".
    /// </para>
    /// </summary>
    public static IReadOnlyList<AnimationEventExpectation>? For(string className) => className switch
    {
        // Five muzzle flashes, all named, all confirmed present.
        EntityNames.WeaponPistol   => [AnimationEventExpectation.Named(WeaponAnim.Primary, MuzzleFlash)],
        EntityNames.Weapon357      => [AnimationEventExpectation.Named(WeaponAnim.Primary, MuzzleFlash),
                                      AnimationEventExpectation.Named(WeaponAnim.Reload,  PlaySound)],
        EntityNames.WeaponSmg1     => [AnimationEventExpectation.Named(WeaponAnim.Primary, MuzzleFlash)],
        EntityNames.WeaponAr2      => [AnimationEventExpectation.Named(WeaponAnim.Primary, MuzzleFlash),
                                      AnimationEventExpectation.Named(WeaponAnim.Reload,  PlaySound)],
        EntityNames.WeaponShotgun  => [AnimationEventExpectation.Named(WeaponAnim.Primary, MuzzleFlash)],

        // The two old-system ones. Written as numbers because that is what the
        // models carry; writing them as names would be a check that always
        // failed and would be read as a broken model.
        EntityNames.WeaponRpg      => [AnimationEventExpectation.Numbered(WeaponAnim.Primary, RpgFireEvent)],
        EntityNames.WeaponFrag     => [AnimationEventExpectation.Numbered(WeaponAnim.Primary, GrenadeThrowEvent)],

        // The two that carry nothing on any animation anyone fires. Recorded
        // as an EMPTY expectation set rather than left out, so the report says
        // "checked, carries nothing" rather than "never looked".
        EntityNames.WeaponCrowbar  => None,
        EntityNames.WeaponCrossbow => None,

        _ => null,
    };
}
