//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Combat;
using TF2.Shared.Player;

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

    /// <summary>
    /// <c>CTFKnife::ShouldDisguiseOnBackstab</c>
    /// (<c>tf_weapon_knife.cpp:335-343</c>): the knife's own item answers
    /// <c>set_disguise_on_backstab</c> one — the silent disguise's attribute
    /// half of stock's tail branch (:246).
    /// </summary>
    bool ShouldDisguiseOnBackstab(int ownerIndex);

    /// <summary>
    /// <c>CTFKnife::DisguiseOnKill</c> (<c>tf_weapon_knife.cpp:315-330</c>):
    /// the successful backstab (the victim gone, the spy carrying no flag)
    /// disguises the spy as the victim's team and class; stock's think's
    /// 0.2 s delay applies at the press here, like the hit itself
    /// (docs/plans/tf2/deferred.tsv). Answers whether the disguise started.
    /// </summary>
    bool DisguiseOnKill(int ownerIndex);

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
    /// The spy arm of <c>CTFPlayer::OnTakeDamage</c>
    /// (<c>tf_player.cpp:9673-9688</c>) over <c>SpyDeadRingerDeath</c>
    /// (<c>:15674-15697</c>): a hit that lands on an armed spy plays them
    /// dead — the cloak meter halves, the burning and the bleed go off, and
    /// the feign-death, stealth and boost conditions ride. Answers whether
    /// the spy played dead. <paramref name="attacker"/> is the hit's inflictor
    /// owner, what the fake death's notice and pack name as the killer
    /// (<c>tf_player.cpp:15736</c>); <see cref="EntityHandle.None"/> for a hit
    /// from the world.
    /// </summary>
    bool FeignDeathHit(int victimIndex, EntityHandle attacker = default);

    /// <summary>
    /// <c>CTFKnife::CanDeploy</c> (<c>tf_weapon_knife.cpp:521-531</c>): the
    /// Spy-cicle's melted knife answers no until
    /// <c>now - m_flKnifeMeltTimestamp &gt; m_flKnifeRegenerateDuration</c> (:523);
    /// with no melt on the knife the answer is yes, as stock's fresh
    /// <c>m_bKnifeExists</c> (:77). The press's own clock is
    /// <paramref name="now"/>.
    /// </summary>
    bool KnifeReady(int ownerIndex, float now);

    /// <summary>
    /// <c>CTFKnife::ApplyOnInjuredAttributes</c>
    /// (<c>tf_weapon_knife.cpp:118-157</c>): the holder's own item answering
    /// <c>melts_in_fire</c> one and up, burned or ignited, melts the knife for
    /// its seconds (:124-131) — only while it exists (:126), the melt sound
    /// and all (:136); the forced switch to the sapper (:142-153) waits on the
    /// building lanes with the rest of its kind. Marks at the hit's time,
    /// gates the swings through <see cref="KnifeReady"/>.
    /// </summary>
    void MeltKnife(int ownerIndex, float now, float seconds);

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
/// sanctioned extension). The shared half: the identity each side's hook
/// carries — the row, the band and the knife test — over the side's own
/// dependencies (<see cref="TF2.Server.Weapons.TFKnifeServerHook"/> through
/// the spy slice, <see cref="TF2.Client.Weapons.TFKnifeClientHook"/> through
/// the predicted carrier).
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
public abstract class TFKnifeHook : ITFWeaponHook
{
    /// <summary>The knife's row (<c>TF_WEAPON_KNIFE</c>'s script).</summary>
    public const string KnifeScript = "tf_weapon_knife";

    /// <summary>Stage 2.12's band, after the component source and the commands: the
    /// server hook's stage (<see cref="TF2.Server.Weapons.TFKnifeServerHook"/>); the
    /// client's replay takes the band's next (<see cref="TF2.Client.Weapons.TFKnifeClientHook"/>).</summary>
    public const int Order = 1_000_000 - 1_000 + 984;

    public virtual int HookOrder => Order;

    protected static bool IsKnife(in TFWeaponHookContext weapon) =>
        string.Equals(weapon.Type.Name, KnifeScript, StringComparison.Ordinal);
}
