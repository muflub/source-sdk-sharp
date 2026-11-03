//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The top team-status bar's rows, stock's <c>CTFTeamStatus</c> walk
/// (tf_teamstatus.cpp): every connected player on a game team, in entity
/// index order, with the reads <c>CTFTeamStatusPlayerPanel::Update</c>
/// makes — the class, life and health of the base resource's row, the
/// TF rows' class health ceilings, and the respawn wait the clock
/// measures once its seconds are spent (:126-133).
/// </summary>
[SideService(Side.Client, As = [typeof(ITFTeamStatus)])]
public sealed class TFHudTeamStatus(TFClientPlayerResources resources, IClientStandalones standalones,
                                    IEngineHud screen, IGameClock clock) : ITFTeamStatus
{
    /// <summary>FIRST_GAME_TEAM (tf_team.h): the walk's floor, the same
    /// const <see cref="TFHudFlag"/> walks to TF_TEAM_BLUE.</summary>
    private const int FirstGameTeam = (int)TFTeam.Red;

    private const int Slots = TFPlayerResourceNetState.Slots;

    public bool Present
        => screen.LocalPlayerIndex is > 0 and < Slots
           && resources.Live is { } live
           && live.State.Team[screen.LocalPlayerIndex] >= FirstGameTeam;

    public int LocalPlayerIndex => screen.LocalPlayerIndex;

    public int Count
    {
        get
        {
            int count = 0;
            for (int i = 1; i < Slots; i++)
                if (Row(i, out _))
                    count++;
            return count;
        }
    }

    public TFTeamStatusPlayer Player(int index)
    {
        int seen = 0;
        for (int i = 1; i < Slots; i++)
        {
            if (!Row(i, out TFTeamStatusPlayer row))
                continue;
            if (seen++ == index)
                return row;
        }

        return default;
    }

    /// <summary>One walk step: connected on the base resource, a game team
    /// and class on the TF one, alive only with a class (Update :93-95),
    /// health only alive (:121-124), the respawn wait only dead with a
    /// class and floored at the second like the int assignment is
    /// (:127-133).</summary>
    private bool Row(int index, out TFTeamStatusPlayer row)
    {
        row = default;
        if (resources.Live is not { } live || index >= Slots)
            return false;

        if (Core() is not { } core || !core.State.IsConnected(index))
            return false;

        int team = live.State.Team[index];
        if (team < FirstGameTeam)
            return false;

        int classIndex = live.State.PlayerClass[index];
        bool alive = classIndex != (byte)TFClass.Undefined && core.State.IsAlive(index);
        int wait = -1;
        if (!alive && classIndex != (byte)TFClass.Undefined)
        {
            wait = (int)(live.State.NextRespawnTime[index] - clock.Now);
            if (wait <= 0)
                wait = -1;
        }

        row = new TFTeamStatusPlayer(
            index,
            team,
            classIndex,
            alive,
            alive ? (int)core.State.Health[index] : -1,
            live.State.MaxHealth[index],
            live.State.MaxBuffedHealth[index],
            wait);
        return true;
    }

    private ClientPlayerResource? Core()
        => standalones.OfClass(EntityNames.PlayerManager).OfType<ClientPlayerResource>().FirstOrDefault();
}
