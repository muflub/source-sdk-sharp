//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Conditions;

namespace TF2.Server.Player;

/// <summary>
/// The condition cheat commands, behind <c>sv_cheats</c> as stock's are
/// (<c>CTFPlayer::ClientCommand</c>, tf_player.cpp:7195-7292):
///
/// <para>
/// — <c>addcond &lt;condition&gt; [duration] [target]</c>: the condition on
///   the player (or on the first player whose name contains
///   <c>target</c>, stock's <c>Q_strstr</c> walk over the slots), for
///   <c>duration</c> seconds or permanent. The condition is a number, or a
///   <c>TF_COND_*</c> name whenever the number is not one
///   (<see cref="TFConditionNames.FromName"/>).
/// </para>
/// <para>
/// — <c>removecond &lt;condition|all&gt; [target]</c>: the one condition, or
///   every condition on <c>all</c>.
/// </para>
///
/// A number outside the conditions, or a name nothing matches, is refused
/// with stock's <c>Failed to &lt;add|remove&gt;cond</c> warning; without
/// <c>sv_cheats</c> or with no condition argument nothing happens, as stock's
/// silent returns leave it.
/// </summary>
[SideService(Side.Server)]
public sealed class TFConditionCommands(ITFPlayerConditions conditions, ITeamRoundRoster roster,
                                        IEngineConsole console, ILog log)
    : IClientCommandHook, IOrderedHook
{
    public const string Add = "addcond";
    public const string Remove = "removecond";

    /// <summary><see cref="TFPlayerOrders.ConditionCommands"/>, the player band's command slot.</summary>
    public int HookOrder => TFPlayerOrders.ConditionCommands;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        bool add = string.Equals(command, Add, StringComparison.OrdinalIgnoreCase);
        if (!add && !string.Equals(command, Remove, StringComparison.OrdinalIgnoreCase))
            return false;

        // sv_cheats read live, fail-closed: missing or not a number is off
        // (the damage dev commands' reading).
        if (!(float.TryParse(console.FindConVarValue("sv_cheats"), NumberStyles.Float, CultureInfo.InvariantCulture, out float on) && on != 0f))
            return true;

        if (args.Length < 1)
            return true;

        // Stock's optional target: the first player whose name contains the
        // argument, the walk ending back at the speaker when nothing matches.
        EntityHandle target = player;
        int nameArg = add ? 2 : 1;
        if (args.Length > nameArg)
        {
            // Stock's walk is over the slots 1..maxClients; here the round
            // roster's four teams, each in its own stable order.
            TFTeam[] teams = [TFTeam.Unassigned, TFTeam.Spectator, TFTeam.Red, TFTeam.Blue];
            bool found = false;
            for (int team = 0; !found && team < teams.Length; team++)
                foreach (EntityHandle candidate in roster.Players(teams[team]))
                    if (roster.Name(candidate).Contains(args[nameArg], StringComparison.Ordinal))
                    {
                        target = candidate;
                        found = true;
                        break;
                    }
        }

        if (add)
            AddCond(target, args);
        else if (string.Equals(args[0], "all", StringComparison.OrdinalIgnoreCase))
            conditions.RemoveAll(target);
        else
            RemoveCond(target, args);
        return true;
    }

    private void AddCond(EntityHandle target, ReadOnlySpan<string> args)
    {
        TFCond cond = Parse(args[0]);
        if (cond == TFCond.Invalid)
        {
            Failed("addcond", "to", args[0], target);
            return;
        }

        // The duration argument: absent is permanent (AddCond's default);
        // present parses as a float, which stock's atof makes 0 of garbage.
        float duration = TFConditions.Permanent;
        if (args.Length >= 2)
            duration = float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : 0f;

        conditions.Add(target, cond, duration);
    }

    private void RemoveCond(EntityHandle target, ReadOnlySpan<string> args)
    {
        TFCond cond = Parse(args[0]);
        if (cond == TFCond.Invalid)
        {
            Failed("removecond", "from", args[0], target);
            return;
        }

        conditions.Remove(target, cond);
    }

    private void Failed(string verb, string preposition, string token, EntityHandle target)
        => log.Warning($"[TF2/Cond] Failed to {verb} {token} {preposition} player {roster.Name(target)}\n");

    /// <summary>
    /// Stock's number-or-name read (tf_player.cpp:7217-7221): an integer, and
    /// a <see cref="TFConditionNames"/> lookup whenever that integer is not
    /// one — except a leading <c>0</c>, which stays condition 0.
    /// </summary>
    private static TFCond Parse(string token)
    {
        int id = int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
        if (id == 0 && (token.Length == 0 || token[0] != '0'))
            id = (int)TFConditionNames.FromName(token);

        return id >= 0 && id < TFConditions.Count ? (TFCond)id : TFCond.Invalid;
    }
}
