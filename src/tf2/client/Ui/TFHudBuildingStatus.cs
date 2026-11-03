//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Client.Buildings;
using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Buildings;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The building status's reads (stock's <c>CBuildingStatusContainer</c> and
/// its <c>CBuildingStatusItem</c>s, tf_hud_building_status.cpp:319-426): the
/// local player's own buildings off the carrier — stock reads each
/// <c>C_BaseObject</c> the container owns, ours reads the four rows the
/// builder's slot carries. The engineer's arm lists the slot's rows; the
/// spy's (the <c>BuildingStatus_Spy</c> container) lists the sapped rows
/// whose team is not the local player's. No carrier, no local player, or
/// another class is the empty panel — stock's containers simply do not
/// exist for those classes.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFBuildingStatus)])]
public sealed class TFHudBuildingStatus(IEngineHud screen, TFClientBuildingResources buildings,
                                        TFClientPlayerResources players, ITFPlayerData data) : ITFBuildingStatus
{
    private const int Slots = TFBuildingResourceNetState.Slots;
    private const int Kinds = TFBuildingResourceNetState.Kinds;

    private TFBuildingResourceNetState? _state;
    private int _local;
    private bool _engineer, _spy;
    private int _team;

    /// <summary>The frame's reads: the carrier, the local class and team —
    /// all asked again on every read, as stock's ShouldDraw asks the class
    /// each frame (tf_hud_building_status.cpp:1201-1206): a class chosen
    /// after the first read is seen.</summary>
    private TFBuildingResourceNetState? Resolve()
    {
        int local = screen.LocalPlayerIndex;
        _local = local;
        _state = buildings.Live?.State;
        _engineer = _spy = false;
        if (_state is null || local <= 0)
            return _state;

        var carrier = data.Networked(local);
        _engineer = carrier is { } mine && mine.Class() == TFClass.Engineer;
        _spy = carrier is { } spys && spys.Class() == TFClass.Spy;
        _team = TeamOf(local);
        return _state;
    }

    /// <summary>A slot's team off the player resource: a building's row is
    /// its builder's slot, so the builder's team is the row's (the carrier
    /// does not repeat it, which keeps its state inside the 2048-byte
    /// tier).</summary>
    private int TeamOf(int slot)
        => players.Live is { } row && slot > 0 && slot < TFPlayerResourceNetState.Slots ? row.State.Team[slot] : 0;

    public int Count
    {
        get
        {
            var state = Resolve();
            if (state is null)
                return 0;
            // The engineer's four panels are always up, an absent building's
            // reading not built (tf_hud_building_status.cpp:1186-1192, :183-186).
            if (_engineer)
                return Kinds;
            if (_spy)
            {
                int count = 0;
                for (int slot = 1; slot < Slots; slot++)
                    count += CountRows(state, slot * Kinds, Kinds, sappedOnly: true, enemyTeam: _team);
                return count;
            }

            return 0;
        }
    }

    public TFBuildingStatusLine Building(int i)
    {
        var state = Resolve();
        if (state is null)
            return default;

        int index = i;
        if (_engineer)
            return (uint)i < Kinds ? Line(state, _local * Kinds + i) : default;

        for (int slot = 1; slot < Slots; slot++)
        {
            int row = FindRow(state, ref index, slot * Kinds, Kinds, sappedOnly: true, enemyTeam: _team);
            if (row >= 0)
                return Line(state, row);
        }

        return default;
    }

    private int CountRows(TFBuildingResourceNetState state, int start, int kinds, bool sappedOnly, int enemyTeam)
    {
        int count = 0;
        for (int row = start; row < start + kinds; row++)
        {
            if (Wanted(state, row, sappedOnly, enemyTeam, TeamOf(row / Kinds)))
                count++;
        }

        return count;
    }

    private int FindRow(TFBuildingResourceNetState state, ref int index, int start, int kinds,
                               bool sappedOnly, int enemyTeam)
    {
        for (int row = start; row < start + kinds; row++)
        {
            if (!Wanted(state, row, sappedOnly, enemyTeam, TeamOf(row / Kinds)))
                continue;
            if (index-- == 0)
                return row;
        }

        return -1;
    }

    private static bool Wanted(TFBuildingResourceNetState state, int row, bool sappedOnly, int enemyTeam, int team)
    {
        byte flags = state.Flags[row];
        if ((flags & TFBuildingResourceNetState.Present) == 0)
            return false;
        return !sappedOnly
            || (flags & TFBuildingResourceNetState.Sapped) != 0 && team != enemyTeam;
    }

    private TFBuildingStatusLine Line(TFBuildingResourceNetState state, int row)
    {
        if (row < 0)
            return default;

        byte flags = state.Flags[row];
        var kind = (TFBuildingResourceNetState.RowKind)(row % Kinds) switch
        {
            TFBuildingResourceNetState.RowKind.Sentry => TFBuildingLineKind.Sentry,
            TFBuildingResourceNetState.RowKind.Dispenser => TFBuildingLineKind.Dispenser,
            TFBuildingResourceNetState.RowKind.Entrance => TFBuildingLineKind.TeleporterEntrance,
            _ => TFBuildingLineKind.TeleporterExit,
        };
        bool recharging = (flags & TFBuildingResourceNetState.Recharging) != 0;
        bool teleporter = kind is TFBuildingLineKind.TeleporterEntrance or TFBuildingLineKind.TeleporterExit;
        return new TFBuildingStatusLine(
            kind, state.Level[row], TeamOf(row / Kinds),
            Placing: (flags & TFBuildingResourceNetState.Built) == 0 && (flags & TFBuildingResourceNetState.Building) == 0,
            Building: (flags & TFBuildingResourceNetState.Building) != 0,
            Sapped: (flags & TFBuildingResourceNetState.Sapped) != 0,
            state.BuildProgress[row], state.Health[row], state.MaxHealth[row],
            Shells: kind == TFBuildingLineKind.Sentry ? TFBuildingResourceAmmo.Count(state.Ammo[row]) : 0,
            MaxShells: kind == TFBuildingLineKind.Sentry ? TFBuildingResourceAmmo.Count(state.MaxAmmo[row]) : 0,
            Ammo: kind == TFBuildingLineKind.Dispenser ? state.Ammo[row] : 0,
            Kills: state.Kills[row], Assists: state.Assists[row],
            Recharge: recharging ? TFBuildingResourceTeleporter.Progress(state.Ammo[row]) : -1,
            TimesUsed: teleporter ? TFBuildingResourceTeleporter.TimesUsed(state.Ammo[row]) : 0,
            Rockets: kind == TFBuildingLineKind.Sentry ? TFBuildingResourceAmmo.Pod(state.Ammo[row]) : 0,
            MaxRockets: kind == TFBuildingLineKind.Sentry ? TFBuildingResourceAmmo.Pod(state.MaxAmmo[row]) : 0);
    }
}
