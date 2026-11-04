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
}

/// <summary><see cref="IPlayerStances"/> over a player's net state.</summary>
public sealed class PlayerStances(IEntityDirectory directory) : IPlayerStances
{
    public bool Ducked(EntityHandle player) => (directory.Get(player) as SsPlayer)?.State.Ducked ?? false;
}
