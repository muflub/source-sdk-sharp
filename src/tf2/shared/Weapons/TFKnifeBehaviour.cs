//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Combat;

namespace TF2.Shared;

/// <summary>
/// The spy's state as a seam. The knife hook below runs on both sides, but
/// only a server can trace a swing, deal a backstab and end a disguise, so
/// the hook asks this interface and the server slice
/// (<c>TF2.Server.Player.TFSpy</c>) answers it. On a client replay the slot
/// is empty and every call is a no-op: the backstab is the server's damage,
/// and a disguise's fields ride the predicted carrier.
/// </summary>
public interface ITFSpyState
{
    /// <summary>
    /// <c>CTFKnife::PrimaryAttack</c>'s swing (<c>tf_weapon_knife.cpp:180-280</c>):
    /// the line at <paramref name="range"/> from the owner's eyes
    /// (<c>CanPerformBackstabAgainstTarget</c> :399); when the first hit is a
    /// living enemy player standing behind them facing them (stock's three dot
    /// products, <c>IsBehindAndFacingTarget</c> :447-482), the backstab is
    /// dealt now — the victim's current health doubled, flagged, through the
    /// damage layer as <see cref="TFDamageCustom.Backstab"/> — and remembered
    /// so the swing's smack lands nothing. Stock deals it at the smack
    /// 0.1 s later (docs/plans/tf2/deferred.tsv); answers whether it did.
    /// </summary>
    bool Swing(int ownerIndex, float range);

    /// <summary>
    /// Whether the swing just made backstabbed, so its hit lands nothing
    /// (stock's smack on an already-dead victim is a miss). Stays answered
    /// until the next <see cref="Swing"/>.
    /// </summary>
    bool Smack(int ownerIndex);

    /// <summary><c>CTFPlayer::RemoveDisguise</c> (tf_player_shared.cpp:12917-12924): the
    /// disguise ends if one was on or going on (<c>CTFPlayerShared::RemoveDisguise</c>,
    /// tf_player_shared.cpp:8374-8388).</summary>
    void RemoveDisguise(int playerIndex);

    /// <summary><c>CTFPlayer::RemoveInvisibility</c> (tf_player.cpp:15988-16005):
    /// the cloak fades if it was on (<c>FadeInvis</c> 0.5).</summary>
    void RemoveInvisibility(int playerIndex);

    /// <summary>
    /// <c>CTFWeaponInvis::ActivateInvisibilityWatch</c>
    /// (<c>tf_weapon_invis.cpp:206-263</c>): a toggle — decloak when cloaked,
    /// cloak from a meter over 8.0 when not, and arm
    /// <c>m_flStealthNextChangeTime</c> 0.5 s after a real change, 0.1 s after
    /// a refused one. Answers whether the cloak started.
    /// </summary>
    bool ActivateInvisibilityWatch(int playerIndex);

    /// <summary>
    /// <c>CTFPlayerShared::Disguise</c> (tf_player_shared.cpp:8157-8269): a spy
    /// begins disguising as <paramref name="disguiseClass"/> on
    /// <paramref name="disguiseTeam"/> — instantly when already disguised
    /// (<c>TF_TIME_TO_QUICK_DISGUISE</c>), otherwise
    /// <c>TF_TIME_TO_DISGUISE</c> — completing on the per-frame think.
    /// Disguising as the player's own class and team ends the disguise
    /// instead. Answers whether a disguise (or its end) started.
    /// </summary>
    bool Disguise(int playerIndex, TFClass disguiseClass, TFTeam disguiseTeam);

    /// <summary>
    /// The <c>lastdisguise</c> command: the desired disguise remembered at the
    /// last one, the own-team note with it (<c>WasLastDisguiseAsOwnTeam</c>),
    /// or <paramref name="desired"/> when the command carried a class in
    /// range; with none yet, a random class but the scout and the spy, on the
    /// enemy team (<c>cc_tf_player_lastdisguise</c>, c_tf_player.cpp:9594-9633).
    /// Answers whether a disguise started.
    /// </summary>
    bool LastDisguise(int playerIndex, TFClass desired = TFClass.Undefined);
}

/// <summary>
/// The knife's links in the chain (Stage 2.12), beside the melee behaviour it
/// runs under (no subclass of <see cref="TFMeleeBehaviour"/>: hooks are the
/// sanctioned extension):
///
/// <para>
/// — THE BACKSTAB: the swing names a victim through
/// <see cref="ITFSpyState.Swing"/>; the chain is asked for the hit's damage
/// twice (the roll's cost read at the swing, the hit itself at the smack),
/// and a backstab answers zero to both — the damage has already left through
/// the seam, and a zero hit is refused before anything is recorded
/// (<c>SsPlayer.TakeHit</c>).
/// </para>
/// <para>
/// — THE ROLL (<c>CTFKnife::CalcIsAttackCriticalHelper</c> and
/// <c>...NoCrits</c>, tf_weapon_knife.cpp:487-500): the knife answers
/// <c>IsBackstab()</c> whole — the melee helper never runs, so no draw is
/// made and the crit bucket is never charged or paid. The backstab's kill
/// is not a roll: it is the damage's flag, dealt by the seam.
/// </para>
/// <para>
/// — THE TAIL: every primary attack ends the disguise and the cloak
/// (<c>ShouldRemoveDisguiseOnPrimaryAttack</c> and
/// <c>ShouldRemoveInvisibilityOnPrimaryAttack</c>, both true:
/// tf_weaponbase_gun.cpp:136, 210, tf_weaponbase_melee.cpp:222,
/// tf_weapon_knife.cpp:224, 263).
/// </para>
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFKnifeHook(ITFSpyState? spies = null) : ITFWeaponHook
{
    /// <summary>The knife's row (<c>TF_WEAPON_KNIFE</c>'s script).</summary>
    public const string KnifeScript = "tf_weapon_knife";

    /// <summary>Stage 2.12's band, after the component source and the commands.</summary>
    public const int Order = 1_000_000 - 1_000 + 984;

    public int HookOrder => Order;

    private static bool IsKnife(in TFWeaponHookContext weapon) =>
        string.Equals(weapon.Type.Name, KnifeScript, StringComparison.Ordinal);

    /// <summary>
    /// The knife's crit is <c>IsBackstab()</c> (tf_weapon_knife.cpp:487-500):
    /// answered whole, so no melee roll runs and the bucket is untouched.
    /// Without a spy slice (a client replay) the answer is no: the backstab
    /// is the server's damage (docs/divergences.md).
    /// </summary>
    public TFCritResult? CritAnswer(in TFWeaponHookContext weapon) =>
        IsKnife(in weapon) ? new TFCritResult(spies is { } state && state.Smack(weapon.OwnerIndex)) : null;

    public bool BeforeFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack)
    {
        if (IsKnife(in weapon) && attack.Command.OwnerIndex > 0 && attack.EffectsLive && spies is { } state)
            state.Swing(attack.Command.OwnerIndex, attack.Type.MeleeRange);
        return true;
    }

    /// <summary>
    /// The smack's half of the backstab: <see cref="TFMeleeBehaviour"/>'s
    /// <c>ItemPostFrame</c> asks the chain for the hit's damage at the smack
    /// time (and the roll asks at the swing), and a backstab answers zero so
    /// the swing's own arm hits for nothing.
    /// </summary>
    public float ModifyDamage(in TFWeaponHookContext weapon, float damage) =>
        IsKnife(in weapon) && weapon.OwnerIndex > 0 && spies is { } state && state.Smack(weapon.OwnerIndex)
            ? 0f
            : damage;

    public void AfterFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack)
    {
        if (attack.Command.OwnerIndex <= 0 || spies is not { } state)
            return;

        state.RemoveInvisibility(attack.Command.OwnerIndex);
        state.RemoveDisguise(attack.Command.OwnerIndex);
    }
}
