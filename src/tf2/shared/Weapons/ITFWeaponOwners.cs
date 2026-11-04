using TF2.Shared.Conditions;
using TF2.Shared.Econ;

namespace TF2.Shared;

/// <summary>
/// What a TF weapon asks about the player holding it: the class, the
/// conditions, the crit multiplier and the item's attributes, keyed by the
/// player's entity index (the same on both sides). The answers are predicted
/// state on the per-player carrier (<c>tf_player_data</c>, plan §2.3), so a
/// behaviour reading them on the client's replay reads what the server read.
///
/// <para>
/// Optional: a weapon composed with none behaves as a classless player's,
/// out loud — no class (stock's <c>TF_CLASS_UNDEFINED</c>, whose ammo maxima
/// are zero, so the carry rule leaves the row's limit), no conditions, a crit
/// multiplier of 1, no attributes and no round-win crits
/// (<see cref="NoTFWeaponOwners"/>). The carrier answers it once it lands.
/// </para>
/// </summary>
public interface ITFWeaponOwners
{
    /// <summary>The player's class; <see cref="TFClass.Undefined"/> when none.</summary>
    TFClass ClassOf(int playerIndex);

    /// <summary>The player's conditions (predicted on the client), or null when the player has none.</summary>
    TFConditions? ConditionsOf(int playerIndex);

    /// <summary><c>CTFPlayer::GetCritMult</c>: the damage-dealt crit multiplier (1 = none).</summary>
    float CritMult(int playerIndex);

    /// <summary>
    /// <c>CalcIsAttackCritical</c>'s round-win arm: the round is in
    /// <c>GR_STATE_TEAM_WIN</c> and the player is on the winning team, so every
    /// shot crits (<c>tf_weaponbase.cpp:1559-1562</c>).
    /// </summary>
    bool WinsCrits(int playerIndex);

    /// <summary>The attributes of the item behind the weapon <paramref name="weaponName"/> the player holds, or null for none.</summary>
    IAttributeProvider? ItemOf(int playerIndex, string weaponName);

    /// <summary>
    /// The attributes of the item behind the player's ACTIVE weapon — stock's
    /// <c>GetActiveTFWeapon</c> as an ON_OTHER root
    /// (<c>CALL_ATTRIB_HOOK_..._ON_OTHER( pTFActiveWeapon, ... )</c>,
    /// <c>tf_player_shared.cpp:12860-12862</c>,
    /// <c>tf_gamemovement.cpp:1001-1004</c>), the weapon the movement's
    /// air-dash reads. Null when the player holds no weapon, as stock's null
    /// <c>pTFActiveWeapon</c>.
    /// </summary>
    IAttributeProvider? ActiveItemOf(int playerIndex) => null;

    /// <summary>
    /// The player's own attributes — stock's <c>CTFPlayer::m_AttributeManager</c>
    /// (<c>server/tf/tf_player.h:637-642</c>: <c>GetAttributeManager</c> hands
    /// out <c>&amp;m_AttributeManager</c>, and <c>GetAttributeContainer</c> and
    /// <c>GetAttributeOwner</c> stand at NULL, so the walk is the manager's own
    /// list and its providers). <c>CTFPlayer::GetMaxAmmo</c>
    /// (<c>shared/tf/tf_player_shared.cpp:12998-13030</c>) hooks the class's
    /// ammo maxima through it (<c>mult_maxammo_*</c>). Null when the player
    /// holds none.
    /// </summary>
    IAttributeProvider? ManagerOf(int playerIndex);

    /// <summary>
    /// Whether the player has left the ground. Stock asks
    /// <c>pPlayer-&gt;m_Shared.IsJumping()</c> for the sniper rifle's jump
    /// unzoom (<c>tf_weapon_sniperrifle.cpp:360-371</c>); ruled here as not
    /// <c>FL_ONGROUND</c> — the player's own predicted flags word.
    /// </summary>
    bool OffGround(int playerIndex);

    /// <summary>
    /// <c>GetWaterLevel() == WL_Eyes</c>: the player's eyes are under the
    /// water, which refuses the flamethrower's flame and its airblast
    /// (tf_weapon_flamethrower.cpp:827-828, 1175-1176).
    /// </summary>
    bool EyesUnderwater(int playerIndex);

    /// <summary>
    /// <c>GetFlags() &amp; FL_DUCKING</c>: the player is crouched, which the
    /// rocket launcher reads to fire its rocket from a higher offset
    /// (<c>CTFWeaponBaseGun::FireRocket</c>,
    /// <c>tf_weaponbase_gun.cpp:521-525</c>) — the same predicted flags word
    /// <see cref="OffGround"/> reads.
    /// </summary>
    bool DuckingOf(int playerIndex);

    /// <summary>
    /// The player's round's ranged damage and its random-crit share — stock's
    /// <c>CTF_GameStats.FindPlayerStats(pPlayer)-&gt;statsCurrentRound</c>
    /// <c>TFSTAT_DAMAGE_RANGED</c> / <c>TFSTAT_DAMAGE_RANGED_CRIT_RANDOM</c>
    /// (<c>tf_weaponbase.cpp:4639-4645</c>), which the random crit's observed
    /// rate is measured from. Null on every side that has no round to measure:
    /// the client's replay reads the weapon state's stored rate instead, the
    /// way stock's client arm compares the networked
    /// <c>m_flObservedCritChance</c> without touching stats at all.
    /// </summary>
    (int Total, int RandomCrit)? RangedCritDamageOf(int playerIndex) => null;

    /// <summary>
    /// Until when the player's last decloak refuses the attack — stock's
    /// <c>m_flStealthNoAttackExpire</c> (<c>tf_player_shared.cpp:12226</c>,
    /// the stamp at :7968), which <c>CanAttack</c> reads against curtime.
    /// 0 is no window, the carrier's zeroed start (:787).
    /// </summary>
    float NoAttackExpire(int playerIndex) => 0f;

    /// <summary>
    /// Whether the player's feet may leave the ground this command — the
    /// weapon word of <c>CTFPlayer::CanJump</c>, asked at the head of
    /// <c>CheckJumpButton</c> (tf_gamemovement.cpp:1210, before the scout's
    /// air-dash branch too). Only the drawn compound bow holds it
    /// (<c>CTFCompoundBow::OwnerCanJump</c>, tf_weapon_compound_bow.cpp:657-660);
    /// every other answer is yes.
    /// </summary>
    bool OwnerCanJump(int playerIndex) => true;
}

/// <summary>The answer when no carrier is composed: a classless player, as <see cref="ITFWeaponOwners"/> says.</summary>
public sealed class NoTFWeaponOwners : ITFWeaponOwners
{
    public TFClass ClassOf(int playerIndex) => TFClass.Undefined;
    public TFConditions? ConditionsOf(int playerIndex) => null;
    public float CritMult(int playerIndex) => 1f;
    public bool WinsCrits(int playerIndex) => false;
    public IAttributeProvider? ItemOf(int playerIndex, string weaponName) => null;
    public IAttributeProvider? ManagerOf(int playerIndex) => null;
    public bool OffGround(int playerIndex) => false;
    public bool EyesUnderwater(int playerIndex) => false;
    public bool DuckingOf(int playerIndex) => false;
}
