using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// A new match began on the level that is up: the players' scores were reset
/// (plan_bots_teams_tf2 A.6). A CHAIN the rules raise wherever they zero the
/// players' scores without a level change: the core's <c>mp_restartgame</c>
/// (<see cref="GameRules.RestartGame"/>), a mode switch
/// (<see cref="GameModes"/>), and a mod's own rules (TF2's
/// <c>ResetScores</c> player arm, stock's
/// <c>CTeamplayRoundBasedRules::ResetScores</c> with
/// <c>m_bResetPlayerScores</c>). A level change is not raised here: every
/// member already hears <see cref="IGameLifecycle.OnLevelInit"/>.
/// </summary>
public interface IMatchResetHook
{
    /// <summary>The players' scores were reset; <paramref name="why"/> names the reset, for the console.</summary>
    void OnMatchReset(string why);
}

/// <summary>Raising <see cref="IMatchResetHook"/>: every member, each call isolated.</summary>
public static class MatchResets
{
    /// <summary>Tells every <see cref="IMatchResetHook"/> that a new match began.</summary>
    public static void Raise(this IHooks<IMatchResetHook> hooks, string why) =>
        hooks.Each(nameof(IMatchResetHook.OnMatchReset), h => h.OnMatchReset(why));
}
