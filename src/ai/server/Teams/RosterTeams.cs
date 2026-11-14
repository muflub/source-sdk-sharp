using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Senses;

namespace SourceSharp.Ai.Server.Teams;

/// <summary>
/// The teams as the roster needs them (plan_bots_teams_tf2 B.6): whether the mode plays
/// teams, the team a bot is on, how many people each team has, and a bot's
/// <c>jointeam</c>, the route a player's team menu takes.
/// </summary>
public interface IRosterTeams
{
    bool Teamplay { get; }

    /// <summary>How many teams play, numbered from 2 (2 to 4); 0 with no teams.</summary>
    int TeamCount { get; }

    /// <summary>The team <paramref name="bot"/> is on as every client sees it; 0 none.</summary>
    int TeamOf(ActorId bot);

    /// <summary>How many people (not bots) are on <paramref name="team"/>.</summary>
    int HumansOn(int team);

    /// <summary>Whether <paramref name="bot"/> is alive: a balance move waits for it to be dead.</summary>
    bool IsAlive(ActorId bot);

    /// <summary>
    /// The bot asks to join <paramref name="team"/> as a player's <c>jointeam</c> does
    /// (0 leaves the choice to the mode's rule); the mode may refuse, and says so.
    /// </summary>
    void Join(ActorId bot, int team);
}

/// <summary><see cref="IRosterTeams"/> over the rendered teams, the team service's join and the player directory.</summary>
[SideService(Side.Server, As = [typeof(IRosterTeams)])]
public sealed class EngineRosterTeams(IRenderedTeams rendered, ITeams teams, AiBots bots, IPlayerDirectory directory, IBotPlayers players,
                                      IEnginePlayers life, IBotGame game, IBotClientCommand commands) : IRosterTeams
{
    public bool Teamplay => rendered.Teamplay;

    public int TeamCount => rendered.TeamCount;

    public int TeamOf(ActorId bot) => rendered.TeamOf(bots.Applier.PlayerOf(bot));

    public int HumansOn(int team)
    {
        int n = 0;
        foreach ((EntityHandle player, PlayerRecord _) in directory.All)
        {
            if (!players.IsBot(player) && rendered.TeamOf(player) == team)
                n++;
        }

        return n;
    }

    public bool IsAlive(ActorId bot)
    {
        EntityHandle player = bots.Applier.PlayerOf(bot);
        return player != EntityHandle.None && life.IsAlive(player);
    }

    public void Join(ActorId bot, int team)
    {
        EntityHandle player = bots.Applier.PlayerOf(bot);
        if (player != EntityHandle.None)
            RosterJoin.Join(game, commands, teams, player, team);
    }
}

/// <summary>A bot's join, as a player's team menu makes it in its game (plan_bots_teams_tf2 P6 step 7).</summary>
public static class RosterJoin
{
    /// <summary>
    /// <paramref name="player"/> asks to join <paramref name="team"/>: the game's own word for it
    /// (<see cref="IBotGame.JoinTeamWord"/>) through the mods' commands, as a player's key sends it,
    /// else the core's team service, which the core's <c>jointeam N</c> asks too.
    /// </summary>
    public static void Join(IBotGame game, IBotClientCommand commands, ITeams teams, EntityHandle player, int team)
    {
        if (game.JoinTeamWord(team) is { } word)
            commands.Run(player, word);
        else
            teams.Join(player, team);
    }
}
