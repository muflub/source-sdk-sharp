using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;

namespace TF2.Server.Commands;

/// <summary>
/// <c>tf2_dev_bot &lt;red|blue&gt; &lt;class&gt; [distance]</c>: stands a bot on
/// the named team in front of whoever typed the line, in that class and facing
/// them — a live target for the kill and buff readings without a second client.
/// The bot comes in through <see cref="IBotPlayers.Add"/>, as every bot does,
/// and then follows the human join path: the team through
/// <see cref="ITeams.Join"/> and the class through
/// <see cref="TFJoinClassCommand.JoinClass"/>, which — a fresh bot being
/// classless — respawns it into the class at once. The placement puts the bot
/// <paramref name="distance"/> units along the caller's flattened view,
/// turned back toward the caller. One console line closes the command, naming
/// the bot's entity index, team, class and position. Behind
/// <c>sv_cheats</c>, as <see cref="TFCondsCommand"/> is: scaffolding only a
/// cheats-enabled console needs.
/// </summary>
[ConCommand("tf2_dev_bot", Help = "Stand a bot on a team in front of you. tf2_dev_bot <red|blue> <class> [distance]")]
public sealed class TFDevBotCommand(ILog log, SourceSharp.Shared.SvCheatsConVar cheats, IEnginePlayers players,
                                    IEngineEntities entities, IBotPlayers bots, ITeams teams, TFJoinClassCommand joinclass)
{
    /// <summary>Units in front of the caller when the line names none.</summary>
    public const float DefaultDistance = 100f;

    public void Execute(string team, string cls, float distance = DefaultDistance)
    {
        if (!cheats.Value)
        {
            log.Warning("[TF2/Bots] tf2_dev_bot runs only under sv_cheats\n");
            return;
        }

        TFTeam wanted = TeamOf(team);
        if (wanted is TFTeam.Unassigned or TFTeam.Spectator)
        {
            log.Warning("[TF2/Bots] tf2_dev_bot takes red or blue\n");
            return;
        }

        // joinclass's own names (the class scripts: heavyweapons, demoman, ...),
        // and "heavy" for the class everyone calls that. An unknown name would
        // add a classless bot that falls out of the world, so it is refused.
        if (string.Equals(cls, "heavy", StringComparison.OrdinalIgnoreCase))
            cls = "heavyweapons";
        if (TFJoinClassCommand.Named(cls) == TFClass.Undefined)
        {
            log.Warning($"[TF2/Bots] tf2_dev_bot: no class '{cls}' (scout, soldier, pyro, demoman, heavyweapons, engineer, medic, sniper, spy)\n");
            return;
        }

        EntityHandle caller = DevContext.WhoTyped(players);
        if (caller == EntityHandle.None)
        {
            log.Warning("[TF2/Bots] tf2_dev_bot needs a player in the game\n");
            return;
        }

        EntityHandle bot = bots.Add(cls, [], out string refusal, (int)wanted);
        if (bot == EntityHandle.None)
        {
            log.Warning($"[TF2/Bots] {refusal}\n");
            return;
        }

        // The human join path for a bot the wanted-team list will not assign:
        // the team first (what TFJoinTeamCommand does), then the class through
        // joinclass itself, which takes a classless player straight out of
        // spawn in the chosen class. The balance policy answers human joins;
        // the dev bot's team is the caller's own choice, so a refused join
        // still lands the bot on the named team — this command alone forces
        // it, through the one ABI store the accepted join would have used
        // (IEnginePlayers.SetTeam).
        if (teams.Join(bot, (int)wanted) != JoinTeamResult.Playing)
            players.SetTeam(bot, (int)wanted);
        joinclass.JoinClass(bot, cls);

        // In front of the caller along their view, flattened to the ground
        // plane so a look up or down walks the bot out rather than into the
        // sky, and turned back to face the caller.
        Vector callerOrigin = entities.GetOrigin(caller);
        Vector forward = Flatten(Angles.Forward(entities.GetEyeAngles(caller)));
        Vector origin = callerOrigin + forward * distance;
        entities.Teleport(bot, origin, Angles.FromVector(new Vector(-forward.X, -forward.Y, forward.Z)), zeroVelocity: true);

        log.Msg($"[TF2/Bots] bot {entities.GetIndex(bot)} {(wanted == TFTeam.Blue ? "blue" : "red")} " +
                $"{TFClassNames.NonLocalized(TFJoinClassCommand.Named(cls))} " +
                $"at {origin.X:F1} {origin.Y:F1} {origin.Z:F1}\n");
    }

    /// <summary>The two playing teams, named as TFJoinTeamCommand names them; anything else is no team.</summary>
    internal static TFTeam TeamOf(string word)
    {
        if (string.Equals(word, "blue", StringComparison.OrdinalIgnoreCase))
            return TFTeam.Blue;
        if (string.Equals(word, "red", StringComparison.OrdinalIgnoreCase))
            return TFTeam.Red;
        return TFTeam.Unassigned;
    }

    /// <summary>The view direction on the ground plane: a straight-ahead
    /// look keeps the forward axis, since a flat view of straight up answers
    /// a zero pair here.</summary>
    internal static Vector Flatten(Vector direction)
    {
        float length = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        return length < 1e-6f
             ? new Vector(1f, 0f, 0f)
             : new Vector(direction.X / length, direction.Y / length, 0f);
    }
}

/// <summary>
/// <c>tf2_dev_bot_kick</c>: every bot <see cref="TFDevBotCommand"/> added, sent
/// back out through <see cref="IBotPlayers.Kick"/>, as one console line. The
/// bots to kick are the ones the bot service itself made, in the order they
/// came in. Cheats-gated like the add.
/// </summary>
[ConCommand("tf2_dev_bot_kick", Help = "Kick every bot tf2_dev_bot added.")]
public sealed class TFDevBotKickCommand(ILog log, SourceSharp.Shared.SvCheatsConVar cheats, IBotPlayers bots)
{
    public void Execute()
    {
        if (!cheats.Value)
        {
            log.Warning("[TF2/Bots] tf2_dev_bot_kick runs only under sv_cheats\n");
            return;
        }

        int kicked = 0;
        foreach (BotPlayer bot in bots.Bots)
            if (bots.Kick(bot.Player, "tf2_dev_bot_kick"))
                kicked++;

        log.Msg($"[TF2/Bots] kicked {kicked} bot{(kicked == 1 ? "" : "s")}\n");
    }
}
