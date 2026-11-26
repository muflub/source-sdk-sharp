using SourceSharp.Abi;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// The body and movement a game's navigation is derived for, and what each of
/// its players can reach (plan_bots_teams_tf2 P6 step 2, C.7). The binding of
/// one game registers the one implementation, beside its <see cref="IBotGame"/>:
/// the loader derives the level's graph by <see cref="Numbers"/>, and a player's
/// obstacle state carries <see cref="Reach"/>, so every search of that player's
/// leaves out the edges of a reach its class lacks, and its team's cells of the
/// map's <see cref="TeamSolids"/>.
/// </summary>
public interface INavProfile
{
    /// <summary>
    /// The derivation's numbers for a level whose <c>.nav3d</c> was built with a jump
    /// height of <paramref name="fileJumpHeight"/> (the file's own crouch-jump reach,
    /// which a game may take as its crouch jump's, as HL2MP does).
    /// </summary>
    NavNumbers Numbers(float fileJumpHeight);

    /// <summary>The jump reaches <paramref name="player"/> has beyond a plain and a crouched jump, as its class moves now.</summary>
    JumpReach Reach(EntityHandle player);

    /// <summary>
    /// The volumes of <paramref name="entities"/>' map solid to every team but one (TF's respawn room
    /// visualizers), which each team's players route round; none for a game without them.
    /// </summary>
    IReadOnlyList<TeamSolid> TeamSolids(MapEntities entities);

    /// <summary>
    /// The links beside the level's graph that <paramref name="player"/>'s searches may take (TF2's blast
    /// jumps, lane G5c-2b), and the health it may spend on one as it stands now: none for a game without
    /// them, which every HL2MP player is.
    /// </summary>
    NavLinkAllowance LinksFor(EntityHandle player) => default;

    /// <summary>
    /// How deadly each cell is for <paramref name="player"/>'s side, <paramref name="team"/> (the engine's team, as its
    /// team's solids are chosen by), from what players did on the level (lane G4a), and how much it minds that: none
    /// for a game that keeps no experience, which every HL2MP player is.
    /// </summary>
    NavHazardAllowance HazardFor(EntityHandle player, int team) => default;

    /// <summary>
    /// What <paramref name="player"/>'s searches over <paramref name="graph"/> (the level's) mind, its
    /// <paramref name="obstacles"/> otherwise filled (its reach, links and team's cells): its team's hazard
    /// (<see cref="HazardFor(EntityHandle, int)"/>), or a field of the player's own composed with it (TF2's varied
    /// routes, lane Groutes). The navigation asks this one.
    /// </summary>
    NavHazardAllowance HazardFor(EntityHandle player, int team, NavGraph graph, NavObstacleState obstacles) => HazardFor(player, team);
}

/// <summary>A side's hazard over the level's cells, null for none, and how much a player minds it (<see cref="NavObstacleState.HazardWeight"/>).</summary>
public readonly record struct NavHazardAllowance(NavCellHazard? Hazard, float Weight);

/// <summary>
/// The links a player's searches may take, null for none, the health it may spend on one (<see cref="NavObstacleState.LinkHealth"/>),
/// the links of them the experience leaves out (<see cref="NavObstacleState.LinksShut"/>, lane G5e), null for none, and what it
/// spends on each (<see cref="NavObstacleState.LinkSpends"/>, round 2's S13), null for what the links say.
/// </summary>
public readonly record struct NavLinkAllowance(NavLinks? Links, float Health, NavLinkMask? Shut = null, IReadOnlyList<float>? Spends = null);
