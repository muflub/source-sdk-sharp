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
}
