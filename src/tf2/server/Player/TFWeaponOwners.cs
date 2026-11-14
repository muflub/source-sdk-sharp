//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using TF2.Server.Medigun;

namespace TF2.Server.Player;

/// <summary>
/// The server's <see cref="ITFWeaponOwners"/>: what a TF weapon asks of the
/// player holding it, answered from the player's <c>tf_player_data</c> carrier
/// — the class (<c>m_iClass</c>), the one condition set
/// (<see cref="TFPlayerCarrier.Conditions"/>, so the minigun's and the sniper
/// rifle's <c>TF_COND_AIMING</c>/<c>TF_COND_ZOOMED</c> are on the carrier the
/// movement reads), the damage-dealt crit multiplier
/// (<c>CTFPlayerShared::GetCritMult</c>), the round-win arm of
/// <c>CalcIsAttackCritical</c> (tf_weaponbase.cpp:1563) and the attributes of
/// the item behind the weapon (<see cref="TFPlayerItems"/>).
///
/// <para>
/// Dependency-light on purpose, as <see cref="TFPlayerCarriers"/> is: the
/// weapon parts that take it are built when the core's weapon catalogue is
/// first read, before the player composite is registered. The medigun is the
/// one cross-link, the pomson drain's road — required here, and cycle-free
/// because the medigun's own former owners link reads the items table
/// directly (<see cref="TFMedigunWeapon"/>), leaving no path back here.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFWeaponOwners)])]
public sealed class TFServerWeaponOwners(TFPlayerCarriers carriers, TFRoundWinners winners,
                                         TFPlayerItems items, IEnginePlayers players,
                                         IEntityDirectory directory, ITFPlayerStats stats,
                                         ITFMedigunWeapons mediguns, ItemSchema schema,
                                         IWeaponGiver arsenal, WeaponCatalog weapons) : ITFWeaponOwners
{
    public TFClass ClassOf(int playerIndex) => carriers.Get(playerIndex)?.Class() ?? TFClass.Undefined;

    public TFConditions? ConditionsOf(int playerIndex) => carriers.ConditionsOf(playerIndex);

    public float CritMult(int playerIndex) => carriers.Get(playerIndex)?.CritMultiplier() ?? 1f;

    public bool WinsCrits(int playerIndex)
        => winners.WinningTeam is TFTeam.Red or TFTeam.Blue
           && players.GetTeam(players.GetHandle(playerIndex)) == (int)winners.WinningTeam;

    public IAttributeProvider? ItemOf(int playerIndex, string weaponName) => items.Of(playerIndex, weaponName);

    /// <summary>The active weapon's item, as <c>GetActiveTFWeapon</c>'s name finds it.</summary>
    public IAttributeProvider? ActiveItemOf(int playerIndex)
    {
        string name = players.GetActiveWeapon(players.GetHandle(playerIndex));
        return name.Length > 0 ? items.Of(playerIndex, name) : null;
    }

    public IAttributeProvider? ManagerOf(int playerIndex) => items.ManagerOf(playerIndex);

    /// <summary>
    /// <c>IsShieldEquipped</c>'s walk (<c>GetEquippedDemoShield</c>,
    /// tf_player_shared.cpp:626-643): the demoshield item class over the
    /// player's worn wearables, the gate the speed walk's shield toll takes
    /// (:11005). No wearables part is no shield, as the null walk.
    /// </summary>
    public bool ShieldEquipped(int playerIndex)
    {
        if (carriers.Part<TFWearables>(playerIndex) is not { } wearables)
            return false;

        foreach (EntityHandle wearable in wearables.Wearables)
        {
            if (directory.Get(wearable) is TFWearableEntity entity
                && schema.Item(entity.State.ItemDefIndex)?.ItemClass == TFShieldCharge.ShieldItemClass)
                return true;
        }

        return false;
    }

    public bool OffGround(int playerIndex)
        => directory.Get(players.GetHandle(playerIndex)) is SsPlayer p
           && (p.State.Flags & GameMovement.FlOnGround) == 0;

    /// <summary>The movement state's water level, 0 for a player with none (as <c>TFAfterburn</c> reads it).</summary>
    public bool EyesUnderwater(int playerIndex)
        => directory.Get(players.GetHandle(playerIndex)) is SsPlayer player
           && player.State.WaterLevel == GameMovement.WlEyes;

    public bool DuckingOf(int playerIndex)
        => directory.Get(players.GetHandle(playerIndex)) is SsPlayer p
           && (p.State.Flags & GameMovement.FlDucking) != 0;

    /// <summary>
    /// The round's two numbers <c>CanFireRandomCriticalShot</c> measures the
    /// crit rate from (<c>tf_weaponbase.cpp:4644-4645</c>): stock's
    /// <c>FindPlayerStats</c> for this round, from the server's
    /// <see cref="ITFPlayerStats"/>. A player no damage was ever booked for
    /// reads as none booked — which is stock's early-out (no stats and zero
    /// stats answer the same way).
    /// </summary>
    public (int Total, int RandomCrit)? RangedCritDamageOf(int playerIndex)
        => (stats.Round(playerIndex, TFStat.DamageRanged), stats.Round(playerIndex, TFStat.DamageRangedCritRandom));

    public float NoAttackExpire(int playerIndex) => carriers.Get(playerIndex)?.NoAttackExpire ?? 0f;

    /// <summary>
    /// The owned Thermal Thruster's refire clock (<c>Weapon_OwnsThisID</c> →
    /// <c>GetRefireTime</c>, tf_player_shared.cpp:3137-3140): the hands'
    /// answer while the pack is out, the last holster's bank the rest of the
    /// time. No pack carried, no answer — stock's null <c>pRocketPack</c>.
    /// </summary>
    public bool RocketPackRefireOf(int playerIndex, out float refire)
    {
        refire = 0f;
        EntityHandle player = players.GetHandle(playerIndex);

        if (TFWeaponNames.IdOf(players.GetActiveWeapon(player)) == TFWeaponId.Rocketpack
            && directory.Get(players.GetActiveWeaponEntity(player)) is SsWeapon { State: { } live })
        {
            refire = live.NextSecondaryAttack;
            return true;
        }

        return PackRow >= 0
            && arsenal.CarriedRefireOf(player, PackRow, out refire);
    }

    /// <summary>The thruster's catalog row, resolved once from the side's table; -1 before there is one.</summary>
    private int PackRow => _packRow ??= weapons.IndexOf(TFWeaponNames.Name(TFWeaponId.Rocketpack).ToLowerInvariant());

    private int? _packRow;

    /// <summary>The carrier's banked decapitations, the death hook's count (<see cref="TFDecapitations"/>).</summary>
    public int DecapitationsOf(int playerIndex) => carriers.Get(playerIndex)?.Decapitations ?? 0;

    /// <summary>
    /// The drawn bow holds the jump (<c>CTFCompoundBow::OwnerCanJump</c>,
    /// tf_weapon_compound_bow.cpp:657-660, asked by <c>CanJump</c> at
    /// tf_gamemovement.cpp:1210): the active weapon the bow and its charge
    /// begun, the same shape the flame manager's arrow-lighting reads
    /// (<see cref="TFFlameManager"/>). No weapon named, or no weapon entity
    /// behind the name, is no refusal — the same fall-through stock's
    /// null-free world never reaches but ours must answer.
    /// </summary>
    public bool OwnerCanJump(int playerIndex)
    {
        EntityHandle player = players.GetHandle(playerIndex);

        return TFWeaponNames.IdOf(players.GetActiveWeapon(player)) != TFWeaponId.CompoundBow
            || directory.Get(players.GetActiveWeaponEntity(player)) is not SsWeapon { State: { } state }
            || !TFBowBehaviour.Drawn(state);
    }

    /// <summary>
    /// The pomson's ring hit (stock's <c>ApplyPostHitEffects</c>,
    /// <c>tf_weaponbase.cpp:5568</c>, :5592): the medic'sÜberCharge rides the
    /// medigun their class owns — stock's <c>Weapon_OwnsWhatever(TF_WEAPON_MEDIGUN)</c>
    /// walk (:5571), the port's <see cref="ITFMedigunWeapons.SubtractCharge"/>,
    /// whose service holds the canonical meter whichever hand holds the gun —
    /// the spy's cloak rides their own carrier meter; the caller's scaled
    /// percentages come off each, clamped at zero — the hundred-point cloak
    /// takes a hundred times the fraction (the port's cloak meter's own unit,
    /// <see cref="TFPlayerNetState.CloakMeter"/>). The medic's meter stands only
    /// while the release is not running — stock's <c>!IsReleasingCharge()</c>
    /// (:5572), the carrier's published release word. The medigun is the
    /// constructor's own (<see cref="TFMedigunWeapon"/>, whose former owners
    /// link reads the items table directly, leaving this cycle-free). Answers
    /// whether a meter moved, stock's <c>bDidDrain</c> (:5555): the drain's
    /// telling stands on it.
    /// </summary>
    public bool DrainOnHitMeters(int playerIndex, float medicFraction, float spyFraction)
    {
        bool drained = false;

        if (medicFraction > 0f && ClassOf(playerIndex) == TFClass.Medic
            && carriers.Get(playerIndex)?.ChargeReleasing != true
            && mediguns.SubtractCharge(playerIndex, medicFraction))
        {
            drained = true;
        }

        if (spyFraction > 0f && ClassOf(playerIndex) == TFClass.Spy
            && carriers.Get(playerIndex) is { } state)
        {
            state.CloakMeter = Math.Max(0f, state.CloakMeter - spyFraction * 100f);
            drained = true;
        }

        return drained;
    }
}

/// <summary>
/// Who won the round while the round machine says so: stock's
/// <c>State_Get() == GR_STATE_TEAM_WIN</c> with <c>GetWinningTeam()</c>, kept
/// here by <see cref="TFRoundWinCrits"/> from the machine's
/// <see cref="Rules.ITeamRoundListener"/> calls, so the weapons can ask without
/// the machine (which composes after them). <see cref="TFTeam.Unassigned"/>
/// outside a team win.
/// </summary>
[SideService(Side.Server)]
public sealed class TFRoundWinners
{
    /// <summary>The team in <c>GR_STATE_TEAM_WIN</c>, or <see cref="TFTeam.Unassigned"/>.</summary>
    public TFTeam WinningTeam { get; set; } = TFTeam.Unassigned;
}

/// <summary>
/// The attributes of the items behind each player's weapons, by player and
/// weapon row: what the loadout last handed out (<c>CEconItemView</c> on
/// each <c>CTFWeaponBase</c> in stock), so a weapon's
/// <see cref="ITFWeaponOwners.ItemOf"/> finds its item. Each player also
/// holds their own attribute manager — stock's <c>CTFPlayer::m_AttributeManager</c>,
/// what <c>CTFPlayer::GetMaxAmmo</c>'s <c>mult_maxammo_*</c> hooks walk
/// (<c>tf_player_shared.cpp:12998-13030</c>) — under
/// <see cref="ManagerOf"/>. Kept per player index and dropped when the
/// player leaves or the level ends.
/// </summary>
[SideService(Side.Server)]
public sealed class TFPlayerItems : IGameLifecycle
{
    private readonly Dictionary<int, Dictionary<string, IAttributeProvider>> _byPlayer = new();
    private readonly Dictionary<int, IAttributeProvider> _managers = new();

    /// <summary>A stage of its own in the TF player's band.</summary>
    public int LifecycleOrder => TFPlayerOrders.PlayerItems;

    /// <summary>The player's items, by the weapon row each one became (a new kit replaces the old).</summary>
    public void Set(int playerIndex, IReadOnlyDictionary<string, IAttributeProvider> byWeapon)
        => _byPlayer[playerIndex] = new Dictionary<string, IAttributeProvider>(byWeapon, StringComparer.Ordinal);

    /// <summary>The item behind weapon row <paramref name="weaponName"/> of the player, or null.</summary>
    public IAttributeProvider? Of(int playerIndex, string weaponName)
        => _byPlayer.TryGetValue(playerIndex, out var items) ? items.GetValueOrDefault(weaponName) : null;

    /// <summary>How many items the player holds.</summary>
    public int Count(int playerIndex) => _byPlayer.GetValueOrDefault(playerIndex)?.Count ?? 0;

    /// <summary>
    /// The player's own attribute manager (stock's <c>m_AttributeManager</c>,
    /// a new kit's grants replacing the old). Stock populates it at spawn
    /// through <c>CAttributeManager::InitializeAttributes</c>
    /// (<c>server/tf/tf_player.cpp:3301</c>) and the runtime grants (buffs,
    /// the level's attribute manager); this SDK subset carries only the
    /// initialization, so the manager enters when its grant does.
    /// </summary>
    public void SetManager(int playerIndex, IAttributeProvider manager)
        => _managers[playerIndex] = manager;

    /// <summary>The player's own attribute manager, or null when they hold none.</summary>
    public IAttributeProvider? ManagerOf(int playerIndex) => _managers.GetValueOrDefault(playerIndex);

    /// <summary>The player left.</summary>
    public void Forget(int playerIndex)
    {
        _byPlayer.Remove(playerIndex);
        _managers.Remove(playerIndex);
    }

    void IGameLifecycle.OnLevelShutdown()
    {
        _byPlayer.Clear();
        _managers.Clear();
    }
}
