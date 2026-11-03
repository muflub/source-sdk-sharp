//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Anim;

/// <summary>
/// Stock's weapon-type enum (<c>tf_item_constants.h:17-42</c>): which
/// act-table <c>CTFWeaponBase::ActivityList</c> picks
/// (<c>tf_weaponbase.cpp:4208-4260</c>), via
/// <c>GetActivityWeaponRole</c>'s <c>m_iWeaponType</c> (:4185-4205). The
/// values are the enum's, spelled here in the managed case; stock's own
/// misspelling of the ball member (<c>TF_WPM_TYPE_PASSTIME_BALL</c>) is
/// carried as the value, not the name.
///
/// <para>
/// Roles past <see cref="Item2"/> have no script word
/// (<c>TFWeaponScriptParser.Category</c> maps only primary…item2, like
/// stock's item schema): they arrive through the econ item's
/// <c>GetAnimationSlot</c> override (:4192-4197, deferred with the rest of
/// econ) or through weapon code setting the type directly. The switch's
/// default arm — the Primary table — is what the roles with no table of
/// their own (<c>GRENADE</c>, <c>HEAD</c>, <c>MISC</c>,
/// <c>PASSTIME_BALL</c>) answer with (:4228-4231).
/// </para>
/// </summary>
public enum TFWeaponRole
{
    Primary = 0,
    Secondary = 1,
    Melee = 2,
    Grenade = 3,
    Building = 4,
    Pda = 5,
    Item1 = 6,
    Item2 = 7,
    Head = 8,
    Misc = 9,
    MeleeAllclass = 10,
    Secondary2 = 11,
    Primary2 = 12,
    Item3 = 13,
    Item4 = 14,
    PasstimeBall = 15,
}

/// <summary>
/// What the anim layer needs to know about a player's active weapon:
/// <see cref="Role"/> picks the act-table, <see cref="Id"/> is the arm that
/// reads the individual weapon (the minigun's crouch exception
/// <c>tf_playeranimstate.cpp:1372-1381</c>, the sniper rifles' and bow's
/// swim and hold-deployed arms — <c>WeaponID_IsSniperRifleOrBow</c>).
/// </summary>
/// <param name="Role">The weapon's act-table role.</param>
/// <param name="Id">The weapon's <see cref="TF2.Shared.TFWeaponId"/>.</param>
public readonly record struct TFAnimWeapon(TFWeaponRole Role, TFWeaponId Id);

/// <summary>
/// The active-weapon seam the anim layer asks its per-player weapon facts
/// through — stock's <c>pPlayer-&gt;GetActiveTFWeapon()</c> at the
/// translation sites. The two sides' owners read it off the engine's active
/// weapon and the weapon scripts; an empty read (no weapon entity behind the
/// read yet — the client's unwired arm, a weapon the scripts do not name)
/// answers <c>null</c>, and the layer then runs stock's no-weapon arm: no
/// table applies and the base activity names the body.
/// </summary>
public interface ITFAnimWeapons
{
    /// <summary>
    /// The player's active weapon, or null when nothing stands behind the
    /// read (no player at the index, no active weapon, an unnamed weapon).
    /// Never throws for a bad index — "no weapon" is the answer.
    /// </summary>
    TFAnimWeapon? ActiveWeapon(int playerIndex);

    /// <summary>
    /// The spy's <i>disguise weapon</i> — the ghost the server gives the
    /// disguise target's weapon (<c>m_hDisguiseWeapon</c>,
    /// <c>tf_player_shared.cpp:8514-8527</c>, a networked EHANDLE) — whose
    /// table answers for the spy's own as stock's
    /// <c>CTFWeaponBase::ActivityList</c> CLIENT arm does
    /// (<c>tf_weaponbase.cpp:4212-4222</c>), so an enemy viewer's third
    /// person animates the disguised spy as the disguise. Stock's gate:
    /// <c>TF_COND_DISGUISED</c> (which the layer checks off the net state,
    /// as stock did before <c>GetDisguiseWeapon</c>),
    /// <c>IsEnemyPlayer</c>, and the disguise weapon existing and not being
    /// the carried weapon (<c>pDisguiseWeapon != this</c>); the last two are
    /// the side's facts (the view relation, the entity identity), so this
    /// read answers them — null when no swap applies, and always from the
    /// server side, whose read is not the client view stock's
    /// <c>#ifdef CLIENT_DLL</c> arm ran under.
    /// </summary>
    TFAnimWeapon? DisguiseWeapon(int playerIndex);
}
