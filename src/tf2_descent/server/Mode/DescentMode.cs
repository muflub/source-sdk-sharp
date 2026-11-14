using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Rules;
using TF2.Shared;

namespace TF2.Descent.Server.Mode;

/// <summary>
/// TF2 Descent's game mode (plan_descent_logic HE1): a co-op dungeon crawl
/// over the TF2 mod's game, so none of TF2's round game applies.
///
/// <list type="bullet">
/// <item>No rounds: no round timer, no setup time, no win check, nothing a
/// reset clears (<see cref="DescentRounds"/>). The level ends by a hop, never
/// by a round (the core's other ends, <c>game_end</c> and
/// <c>cs_sv_end_round</c>, stay <see cref="RoundEndGate"/>'s to refuse).</item>
/// <item>One team of heroes, <see cref="HeroTeam"/>: stock Mann vs.
/// Machine's defenders (<c>TF_TEAM_PVE_DEFENDERS</c> is
/// <c>TF_TEAM_RED</c>, tf_shareddefs.h:50); the robots, when there are any,
/// are <see cref="RobotTeam"/>, MvM's invaders. A player is placed on it at
/// join (<see cref="HeroPlacement"/>) and may join no other
/// (<see cref="DescentJoinPolicy"/>): no team menu, no auto-balance.</item>
/// <item>No friendly fire between heroes, whatever <c>mp_friendlyfire</c>
/// says (<see cref="HeroFriendlyFire"/>). Heroes pass through each other as
/// TF2 teammates do: TF2's own collide rule answers that for every mode, so
/// <see cref="IGameMode.TeammatesPass"/> keeps the core's answer, as TF2's
/// modes do.</item>
/// <item>The respawn is the core's (<see cref="IGameMode.Respawn"/> null),
/// under <c>DeathService</c>'s gate.</item>
/// <item>No TF objectives and no objective entity: the mode is picked on any
/// map (<see cref="DescentModeSelector"/>).</item>
/// <item>TF2's loadout screen and equip commands are closed
/// (<see cref="HeroInventoryGate"/>): a hero's kit is Descent's.</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class DescentMode : IGameMode
{
    /// <summary>The id the selector names and <c>sv_mode</c> asks for.</summary>
    public const string ModeId = "descent";

    /// <summary>The heroes' team: stock MvM's defenders.</summary>
    public const TFTeam HeroTeam = TFTeam.Red;

    /// <summary>The robots' team: stock MvM's invaders.</summary>
    public const TFTeam RobotTeam = TFTeam.Blue;

    /// <summary>The class a hero joins as until heroes choose one of their own.</summary>
    public const TFClass DefaultClass = TFClass.Soldier;

    public string Id => ModeId;

    public IRoundFlow Rounds { get; } = new DescentRounds();

    /// <summary>Heroes are one team: a team line reaches the party.</summary>
    public bool Teamplay => true;

    public IJoinTeamPolicy? JoinPolicy { get; } = new DescentJoinPolicy();

    /// <summary>Any map, and none of the round limits TF2's modes offer.</summary>
    public IModeSettings? Settings { get; } = new ModeSettings("TF2 Descent", [], []);

    /// <summary>TF2's preserve list for a map clean-up: the TF entities the game runs on stay.</summary>
    public IMapCleanupFilter? Cleanup { get; } = new TFMapCleanupFilter();
}

/// <summary>
/// The round machine of a game with no rounds: nothing ticks, the round is
/// never over, a reset clears nothing, and a spawn or a death adds nothing to
/// what the core and TF2 already do.
/// </summary>
public sealed class DescentRounds : IRoundFlow
{
    public void Think(float now) { }

    public bool RoundOver => false;

    public void ResetRound() { }

    public void OnPlayerSpawned(EntityHandle player, float now) { }

    public void OnPlayerKilled(PlayerDeathArgs death) { }
}

/// <summary>
/// Who may join which team: a player joins the heroes and no other team, and
/// an auto-assign (<c>jointeam 0</c>) is the heroes. The spectators stay the
/// core's to admit (<see cref="Teams.Join"/> asks no policy for them).
/// </summary>
public sealed class DescentJoinPolicy : IJoinTeamPolicy, ITeamAutoAssign
{
    /// <summary>What a player asking for another team is told.</summary>
    public const string HeroesOnlyReason = "Heroes play on RED; BLU is the robots' team.";

    public bool MayJoin(EntityHandle player, int requested, out string reason)
    {
        bool hero = requested == (int)DescentMode.HeroTeam;
        reason = hero ? string.Empty : HeroesOnlyReason;
        return hero;
    }

    public int AutoTeam(EntityHandle player) => (int)DescentMode.HeroTeam;
}
