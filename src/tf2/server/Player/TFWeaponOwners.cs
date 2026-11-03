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
/// first read, before the player composite is registered.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFWeaponOwners)])]
public sealed class TFServerWeaponOwners(TFPlayerCarriers carriers, TFRoundWinners winners,
                                         TFPlayerItems items, IEnginePlayers players,
                                         IEntityDirectory directory, ITFPlayerStats stats) : ITFWeaponOwners
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
