//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using TF2.Shared;

namespace TF2.Server.Entities.Items;

/// <summary>
/// What the map's items, rooms and triggers do to a TF player: the
/// <c>CTFPlayer</c> and <c>CTFPlayerShared</c> members the kits, the packs,
/// the resupply locker and the condition triggers call.
///
/// <para>
/// The TF player answers it (<c>TF2.Server.Player.TFPlayerMapPlayers</c>,
/// Stage 2.3) over its own seams: the conditions through
/// <c>ITFPlayerConditions</c> (so each change runs the condition arms), the
/// health, ammo and the full resupply through <c>ITFPlayerResupply</c>.
/// </para>
/// </summary>
public interface ITFMapPlayers
{
    /// <summary><c>IsAlive</c>.</summary>
    bool IsAlive(EntityHandle player);

    /// <summary><c>GetTeamNumber</c>.</summary>
    int Team(EntityHandle player);

    /// <summary><c>GetPlayerClass()-&gt;GetClassIndex()</c>; <see cref="TFClass.Undefined"/> when none.</summary>
    TFClass ClassOf(EntityHandle player);

    /// <summary><c>GetHealth</c>.</summary>
    int Health(EntityHandle player);

    /// <summary><c>CTFPlayer::GetMaxHealth</c>: the class's maximum.</summary>
    int MaxHealth(EntityHandle player);

    /// <summary>
    /// <c>CTFPlayer::TakeHealth( flHealth, DMG_GENERIC )</c>: adds up to the
    /// maximum and answers what was added (0 under
    /// <see cref="TFCond.Nohealingdamagebuff"/>).
    /// </summary>
    int TakeHealth(EntityHandle player, float health);

    /// <summary><c>m_Shared.InCond</c>.</summary>
    bool InCond(EntityHandle player, TFCond cond);

    /// <summary><c>m_Shared.AddCond( cond, duration )</c>.</summary>
    void AddCond(EntityHandle player, TFCond cond, float duration);

    /// <summary><c>m_Shared.RemoveCond</c>.</summary>
    void RemoveCond(EntityHandle player, TFCond cond);

    /// <summary><c>m_Shared.RemoveAllCond</c>.</summary>
    void RemoveAllCond(EntityHandle player);

    /// <summary><c>m_Shared.IsInvulnerable</c>.</summary>
    bool IsInvulnerable(EntityHandle player);

    /// <summary><c>CTFPlayer::GetMaxAmmo</c>.</summary>
    int MaxAmmo(EntityHandle player, TFAmmo ammo);

    /// <summary><c>CTFPlayer::GiveAmmo</c>: answers how much was taken (0 for a full player).</summary>
    int GiveAmmo(EntityHandle player, int count, TFAmmo ammo);

    /// <summary><c>CTFPlayer::Regenerate</c>: the resupply locker's full refill.</summary>
    void Regenerate(EntityHandle player);

    /// <summary><c>CTFPlayerShared::SelfBurn</c>: set alight with no attacker.</summary>
    void SelfBurn(EntityHandle player, float duration);

    /// <summary>
    /// <c>CTFPlayerShared::AddToSpyCloakMeter</c>
    /// (<c>shared/tf/tf_player_shared.cpp:9725-9758</c>) as the items call it
    /// (<c>bForce == false</c>): an ammo pack asks for
    /// <c>100 * pack ratio</c> and, when the meter moved, the pack counts as
    /// taken (<c>entity_ammopack.cpp:86-89</c>, <c>tf_ammo_pack.cpp:366-369</c>).
    /// The arms are the spy's own: only the spy who owns the watch
    /// (<c>Weapon_OwnsThisID(TF_WEAPON_INVIS)</c>) takes cloak, the watch's
    /// <c>mod_cloak_no_regen_from_items</c> refuses it, a watch that refuses
    /// pickup while cloaked (<c>NoCloakWhenCloaked</c>) takes none while
    /// stealthed, and <c>ReducedCloakFromAmmo</c> shrinks the amount; the meter
    /// clamps to 0..100 and the answer is whether it moved (added to a
    /// positive amount under the cap).
    /// </summary>
    bool AddToSpyCloakMeter(EntityHandle player, float amount);
}
