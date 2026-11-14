using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Ai.Server.Senses;

/// <summary>
/// The teams as every client has them (plan_bots_teams_tf2 B.2): a player's team
/// column and model, and the HUD's team scores. Under a mode that plays no teams a
/// client renders none, so every player reads team 0 and the core finds no friends.
/// </summary>
public interface IRenderedTeams
{
    /// <summary>Whether the active mode plays with teams.</summary>
    bool Teamplay { get; }

    /// <summary>The team a client renders <paramref name="player"/> on; 0 under a mode with no teams.</summary>
    int TeamOf(EntityHandle player);

    /// <summary>The score the HUD shows for <paramref name="team"/>, negative included; 0 under a mode with no teams, or for no such team.</summary>
    int Score(int team);

    /// <summary>Whether a teammate's fire hurts (<c>mp_friendlyfire</c>, replicated to every client).</summary>
    bool FriendlyFire { get; }

    /// <summary>How many teams the mode plays, 2 to 4, numbered from 2 (<c>mp_tdm_teams</c>, replicated); 0 with no teams.</summary>
    int TeamCount { get; }
}

/// <summary><see cref="IRenderedTeams"/> over the active mode and the engine's team store.</summary>
[SideService(Side.Server, As = [typeof(IRenderedTeams)])]
public sealed class EngineRenderedTeams(GameModes modes, IEnginePlayers players, MpFriendlyFireConVar friendlyFire, TdmTeamsConVar teamCount) : IRenderedTeams
{
    public int TeamCount => Teamplay ? Math.Clamp(teamCount.Value, 2, TeamRule.MostTeams) : 0;

    public bool FriendlyFire => friendlyFire.Value;

    public bool Teamplay => modes.Active?.Teamplay is true;

    public int TeamOf(EntityHandle player)
    {
        if (!Teamplay || player == EntityHandle.None)
            return 0;
        int team = players.GetTeam(player);
        return team < 0 ? 0 : team;
    }

    public int Score(int team)
    {
        if (!Teamplay || modes.Active?.Scoring is not { } scoring)
            return 0;
        return scoring.TeamScore(team) ?? 0;
    }
}
