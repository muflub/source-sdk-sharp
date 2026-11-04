using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Rules;

/// <summary>
/// The live gate's stimulus (stage2 2.1): <c>tf2_dev_win &lt;team&gt;</c> ends
/// the running round as a win for team 2 (Red) or 3 (Blue), and
/// <c>tf2_dev_win 0</c> as a stalemate, through the same
/// <see cref="ITeamRoundControl"/> the objectives use. Bare, it says the state.
/// </summary>
[ConCommand("tf2_dev_win", Help = "tf2_dev_win <2|3|0>: end the round as a win for red/blue, or a stalemate; bare prints the round state")]
public sealed class TFDevWinCommand(ILog log, IServiceLocator services)
{
    public void Execute([Complete("2", "3", "0")] int? team = null)
    {
        // Resolved at execute: commands bind at registration, before the mods' services exist.
        if (services.Resolve<ITeamRoundControl>() is not { } rounds || services.Resolve<ITeamRoundState>() is not { } state)
        {
            log.Warning("[TF2/Rounds] tf2_dev_win: the round machine is not up yet\n");
            return;
        }

        if (team is null)
        {
            log.Msg($"[TF2/Rounds] state {state.State}, winner {state.WinningTeam}, waiting {state.InWaitingForPlayers}\n");
            return;
        }

        log.Msg($"[TF2/Rounds] tf2_dev_win {team} (in {state.State})\n");
        if (team is (int)TFTeam.Red or (int)TFTeam.Blue)
            rounds.SetWinningTeam((TFTeam)team, WinReason.AllPointsCaptured);
        else
            rounds.SetStalemate(StalemateReason.Timer);
    }
}
