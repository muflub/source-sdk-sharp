//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Shared.Player;

namespace TF2.Shared.Buildings;

/// <summary>
/// <c>tf_object_manager</c> as the engineer's building status needs it: the
/// rows the building HUD reads (stock's <c>CTFPlayerResource</c> pattern —
/// one always-transmitted stand-alone carrying per-player tables). Stock
/// networks each building as an entity the client's
/// <c>C_BuildingStatusItem</c> looks up by handle; ours has no building
/// entities on the wire yet (the server's buildings are scripted server-side
/// objects), so the server writes the columns the HUD draws into this
/// carrier: four rows per player slot, one per building kind, indexed
/// <c>slot * Kinds + kind</c> (<see cref="TFPlayerResourceNetState"/>'s
/// shape, tf_player_resource.cpp's <c>DT_TFPlayerResource</c> arrays).
///
/// <para>
/// A row is empty when <see cref="Flags"/> is 0. The columns are what
/// hud_obj_*.res draw: the health pair for the bar (<c>CBuildingHealthBar</c>,
/// tf_hud_building_status.cpp:56-68), the build progress for
/// <c>BuildingProgress</c>, the shells for the sentry's <c>Shells</c> bar
/// (hud_obj_sentrygun.res:342-358) and the dispenser's metal for
/// <c>Ammo</c> (hud_obj_dispenser.res:274-290).
/// </para>
/// </summary>
[StandaloneNetState(TFEntityNames.BuildingResource)]
public partial class TFBuildingResourceNetState
{
    /// <summary>The player slots, as <see cref="TFPlayerResourceNetState.Slots"/>.</summary>
    public const int Slots = 34;

    /// <summary>Rows per slot: sentry, dispenser, teleporter entrance, teleporter exit.</summary>
    public const int Kinds = 4;

    /// <summary>Slot * Kinds + kind.</summary>
    public const int Rows = Slots * Kinds;

    /// <summary>Row kinds, in <see cref="Kinds"/> order.</summary>
    public const int Sentry = 0;
    public const int Dispenser = 1;
    public const int Entrance = 2;
    public const int Exit = 3;

    /// <summary>The row within a slot's block, the same order.</summary>
    public enum RowKind
    {
        Sentry = 0,
        Dispenser = 1,
        Entrance = 2,
        Exit = 3,
    }

    /// <summary><see cref="Flags"/>: the row exists.</summary>
    public const byte Present = 1;

    /// <summary><see cref="Flags"/>: still under construction (<c>m_bBuilding</c>).</summary>
    public const byte Building = 2;

    /// <summary><see cref="Flags"/>: sapped (<c>HasSapper</c>, the tray's sapper icon).</summary>
    public const byte Sapped = 4;

    /// <summary><see cref="Flags"/>: construction finished (<c>m_bBuilt</c>) — the panel switch with <see cref="Building"/>.</summary>
    public const byte Built = 8;

    /// <summary>
    /// <see cref="Flags"/>: lifted into its builder's arms
    /// (<c>m_bCarried</c>, tf_obj.cpp:165) — stock's status item keeps the
    /// row and shows the building's icon while it rides the player.
    /// </summary>
    public const byte Carried = 16;

    /// <summary>
    /// <see cref="Flags"/>: a teleporter entrance spinning its recharge clock
    /// (stock <c>TF_OBJ_STATE_RECHARGING</c>, tf_obj_teleporter.cpp) — the
    /// entrance's panel switch between <c>ChargingPanel</c> and
    /// <c>FullyChargedPanel</c> (hud_obj_tele_entrance.res:275,304).
    /// </summary>
    public const byte Recharging = 32;

    [NetArray(Rows)] public partial NetArray<byte> Flags { get; }


    /// <summary><c>GetLevel</c>: 1 until the upgrade milestone.</summary>
    [NetArray(Rows)] public partial NetArray<byte> Level { get; }

    /// <summary>0-100: <c>GetPercentageConstructed</c> (tf_obj.cpp) while building, 100 built.</summary>
    [NetArray(Rows)] public partial NetArray<byte> BuildProgress { get; }

    /// <summary>The bar's fill, against <see cref="MaxHealth"/>. A byte: no
    /// building's maximum passes 216 (150·1.2²), and the tier is
    /// 2048 bytes (TFNetStateTierTests) — every short here became a byte to
    /// pay for the kills/assists columns.</summary>
    [NetArray(Rows)] public partial NetArray<byte> Health { get; }

    [NetArray(Rows)] public partial NetArray<byte> MaxHealth { get; }

    /// <summary>The row's ammo: a sentry's shells, a dispenser's metal, a
    /// teleporter entrance's recharge (a row is one kind, so the columns
    /// share). The sentry's row packs its rocket pod into the high byte —
    /// shells and SENTRYGUN_MAX_ROCKETS each fit one (both ≤ 255); the
    /// entrance's packs its times-used there, its recharge progress in the
    /// low, hud_obj_tele_entrance.res's <c>Recharge</c> bar and
    /// <c>TimesUsedLabel</c> (a column of a row each would put the state
    /// past the 2048-byte tier the live bind refuses past,
    /// TFNetStateTierTests). <see cref="TFBuildingResourceAmmo"/> and
    /// <see cref="TFBuildingResourceTeleporter"/> pack and unpack.</summary>
    [NetArray(Rows)] public partial NetArray<short> Ammo { get; }

    /// <summary>What <see cref="Ammo"/> fills against: the sentry's max
    /// shells for its level with its max pod packed the same way, the
    /// dispenser's <c>DISPENSER_MAX_METAL_AMMO</c>, the entrance's 100.</summary>
    [NetArray(Rows)] public partial NetArray<short> MaxAmmo { get; }

    /// <summary>Kills credited to the building (<c>CBaseObject m_iKills</c>,
    /// tf_object.cpp) — the sentry's <c>KillsLabel</c>
    /// (hud_obj_sentrygun.res:307-324, "%numkills% (%numassists%)").</summary>
    [NetArray(Rows)] public partial NetArray<byte> Kills { get; }

    /// <summary>Assists credited to the building (<c>CBaseObject
    /// m_iAssists</c>) — the label's second half.</summary>
    [NetArray(Rows)] public partial NetArray<byte> Assists { get; }
}

/// <summary>
/// The sentry row's packed ammo pair: the shells and the level 3 rocket pod
/// ride one short each, pod in the high byte
/// (<see cref="TFBuildingResourceNetState.Ammo"/>).
/// </summary>
public static class TFBuildingResourceAmmo
{
    /// <summary>The pod's byte.</summary>
    public const int PodShift = 8;

    /// <summary>The count in the low byte, the pod in the high.</summary>
    public static int Pack(int count, int pod) => count + (pod << PodShift);

    /// <summary>The shells or the max shells.</summary>
    public static int Count(int packed) => packed & 0xFF;

    /// <summary>The pod, or the max pod.</summary>
    public static int Pod(int packed) => (packed >> PodShift) & 0xFF;
}

/// <summary>
/// The teleporter-entrance row's packed pair: the recharge progress (0-100,
/// hud_obj_tele_entrance.res's <c>Recharge</c>) in the low byte and the
/// times-used counter (<c>TimesUsedLabel</c>) in the high — the same shape
/// as the sentry's pod pack, against the same tier.
/// <see cref="TFBuildingResourceNetState.Ammo"/>.
/// </summary>
public static class TFBuildingResourceTeleporter
{
    /// <summary>Times used rides the high byte (the count cannot pass 255 there).</summary>
    public const int TimesUsedShift = 8;

    /// <summary>Progress in the low byte, the count clamped into the high.</summary>
    public static int Pack(int progress, int timesUsed) =>
        Math.Clamp(progress, 0, 255) + (Math.Clamp(timesUsed, 0, 255) << TimesUsedShift);

    /// <summary>The recharge progress, 0-100.</summary>
    public static int Progress(int packed) => packed & 0xFF;

    /// <summary><c>Times Used:</c>'s number.</summary>
    public static int TimesUsed(int packed) => (packed >> TimesUsedShift) & 0xFF;
}
