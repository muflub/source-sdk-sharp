using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Rules;

/// <summary>
/// The server's half of <see cref="ITFPlayerTeam"/>: the roster's answer, the
/// core's team word by handle (<see cref="ITeams.Team"/>) — the same read
/// <see cref="ITeamRoundRoster.TeamOf"/> makes, so the rule and the mask arm
/// cannot disagree about who is on which side.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFPlayerTeam)])]
public sealed class TFServerPlayerTeam(ITeams teams, IEnginePlayers players) : ITFPlayerTeam
{
    public TFTeam Of(int playerIndex)
    {
        // The real handle, serial and all: after a changelevel the slot's
        // handle carries one and a bare index crosses the engine's handle
        // table as another slot, so the team read would land on someone else.
        int team = teams.Team(players.GetHandle(playerIndex));
        return team is >= 0 and < (int)TFTeam.Count ? (TFTeam)team : TFTeam.Unassigned;
    }
}
