using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

namespace TF2.Server.Commands;

/// <summary>
/// <c>tf2_conds [player]</c>: the server's own line for a player — index, class,
/// team, health, origin, vertical velocity, active weapon, conditions, cloak and
/// charge meter — read through the engine seams and the player's carrier, so a
/// lane can prove item behaviour from server state rather than the client's
/// prediction lines. Behind <c>sv_cheats</c> as the other diagnostic cheats are
/// (<see cref="TFSetChargeCommand"/>'s gate): a readout only a cheats-enabled
/// console needs. With no index the rostered players are each printed, in the
/// round roster's team order, as <see cref="TFConditionCommands"/> walks them.
/// </summary>
[ConCommand("tf2_conds", Help = "One line of a player's server state. tf2_conds [player]")]
public sealed class TFCondsCommand(ILog log, SourceSharp.Shared.SvCheatsConVar cheats, IEnginePlayers players,
                                   IEngineEntities entities, ITFPlayerData playerData, ITeamRoundRoster roster)
{
    /// <summary>The named index once; every rostered player when the line names none.</summary>
    public void Execute(int index = 0)
    {
        if (!cheats.Value)
        {
            log.Warning("[TF2/Conds] tf2_conds runs only under sv_cheats\n");
            return;
        }

        if (index != 0)
        {
            Print(log, players, entities, playerData, players.GetHandle(index), index);
            return;
        }

        TFTeam[] teams = [TFTeam.Unassigned, TFTeam.Spectator, TFTeam.Red, TFTeam.Blue];
        foreach (TFTeam team in teams)
            foreach (EntityHandle player in roster.Players(team))
                Print(log, players, entities, playerData, player, entities.GetIndex(player));
    }

    /// <summary>
    /// The one line, from the seams alone: the named fields of the carrier's
    /// networked state beside the engine's own reads. Answers whether the
    /// player is still alive, which is what the watch's per-tick arm stops on.
    /// </summary>
    internal static bool Print(ILog log, IEnginePlayers players, IEngineEntities entities,
                               ITFPlayerData playerData, EntityHandle player, int index)
    {
        if (!entities.IsValid(player))
            return false;

        var state = playerData.Networked(index);
        string className = TFClassNames.NonLocalized(state is { } s ? (TFClass)s.PlayerClass : TFClass.Undefined);
        Vector origin = entities.GetOrigin(player);
        Vector velocity = entities.GetVelocity(player);

        var conds = new List<string>();
        if (state is { } carrier)
            foreach (TFCond cond in carrier.Conditions())
                conds.Add(TFConditionNames.Of(cond) ?? ((int)cond).ToString(CultureInfo.InvariantCulture));

        log.Msg($"[TF2/Conds] {index} {className} team {players.GetTeam(player)} hp {entities.GetHealth(player)} " +
                $"at {origin.X:F1} {origin.Y:F1} {origin.Z:F1} vz {(int)velocity.Z} " +
                $"weapon {players.GetActiveWeapon(player)} conds {(conds.Count > 0 ? string.Join(",", conds) : "-")} " +
                $"cloak {state?.CloakMeter ?? 0f:F0} charge {ChargeColumn(state)}\n");
        return players.IsAlive(player);
    }

    /// <summary>
    /// The charge column's number: the medic's ubercharge rides
    /// <see cref="TFPlayerNetState.MedicCharge"/> (0..1, what
    /// <c>SetChargeLevel</c> publishes and the Uberslice's kill moves), so it
    /// keeps its decimals where the shield's meter (<see cref="TFPlayerNetState.ChargeMeter"/>,
    /// 0..100) prints whole. An empty carrier answers zero.
    /// </summary>
    private static string ChargeColumn(TFPlayerNetState? state)
        => state is { } s && s.PlayerClass == (int)TFClass.Medic
            ? s.MedicCharge.ToString("0.##", CultureInfo.InvariantCulture)
            : (state?.ChargeMeter ?? 0f).ToString("F0", CultureInfo.InvariantCulture);
}
