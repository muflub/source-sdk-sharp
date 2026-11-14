using SourceSharp.Abi;

namespace SourceSharp.Ai.Server.Experience;

/// <summary>
/// What the game lets the jump watch read of a player (lane G6a, plan_bots_teams_tf2_part_g G.6, <see cref="ShownJumpFeed"/>):
/// its body as each command finds it (the state the command loop published after the command before) and the blasts the
/// game holds on it (its own rocket, its own sticky, another's). A game that keeps learned jumps registers one (TF2's);
/// without one no jump is watched (HL2MP keeps no store). Asked each command of a watched player only, on the game's
/// thread; what it answers goes to the watch and the store, never to a bot's view.
/// </summary>
public interface IExperienceJumps
{
    /// <summary>
    /// <paramref name="player"/>'s body, false when it has none to read. Whether it stands on another player is asked only
    /// with <paramref name="askGround"/> (a landing), since it takes an engine call.
    /// </summary>
    bool Read(EntityHandle player, bool askGround, out ShownJumpBody body);

    /// <summary>The blasts the game holds on <paramref name="player"/> now.</summary>
    JumpBlast BlastOf(EntityHandle player);
}
