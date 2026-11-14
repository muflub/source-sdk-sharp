using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// How other clients draw a player (plan_hl2mp_ai §4.3, §3.4.1): whether its model is crouched. What
/// they are sent is stock's FL_DUCKING, set when a duck has finished and cleared when a stand has, and
/// their animation turns on it (MultiplayerAnimState.HandleDucking). Not the eye, which is the player's
/// own client's and starts down at a duck's first tick, 0.2 s before anyone could see.
/// </summary>
public interface IPlayerStances
{
    bool Ducked(EntityHandle player);

    /// <summary>
    /// Whether the player stands on the ground (FL_ONGROUND): what its own client predicts of
    /// itself, which a bot's own view reads (plan_bots_teams_tf2 P6: a Soldier rocket-jumps only from
    /// the ground).
    /// </summary>
    bool OnGround(EntityHandle player);
}

/// <summary><see cref="IPlayerStances"/> over a player's net state.</summary>
public sealed class PlayerStances(IEntityDirectory directory) : IPlayerStances
{
    public bool Ducked(EntityHandle player) => (directory.Get(player) as SsPlayer)?.State.Ducked ?? false;

    /// <summary><c>FL_ONGROUND</c>.</summary>
    public const uint FlOnGround = 1u << 0;

    public bool OnGround(EntityHandle player) => ((directory.Get(player) as SsPlayer)?.State.Flags & FlOnGround) is > 0;
}
