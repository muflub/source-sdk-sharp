using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// A mod's own predicted state beside the local player's: the client replay's
/// hook for a further entity (plan_tf2_port.md §2.3, U3; TF2's per-player
/// <c>tf_player_data</c> carrier is the first user).
///
/// <para>
/// The replay predicts the local player (<see cref="ClientPlayerPrediction"/>)
/// and its weapon and viewmodel (<see cref="ClientArsenal"/>); both keep a
/// WORKING COPY of the decoded state, reset it to the server's word once a
/// frame and save every replayed command's result in a
/// <see cref="PredictionRing"/>. A participant does the same for its own
/// state, at the same two moments, and the code that writes the state during
/// a command (a movement rule, a weapon behaviour) writes the participant's
/// working copy rather than the decoded one, which stays the server's word.
/// On the server the same code writes the carrier entity's state directly,
/// found by player index (<c>PlayerComposites.Get&lt;T&gt;(int)</c>).
/// </para>
///
/// <para>
/// A chain: registered with the plain <c>[SideService(Side.Client)]</c> mark
/// and asked in <see cref="IOrderedHook.HookOrder"/> (implement it beside this
/// when a mod has more than one participant, or two mods each have one); a
/// throw is logged and the replay carries on with the next participant.
/// </para>
/// </summary>
public interface IClientPredictedState
{
    /// <summary>
    /// Once a frame while there is a local player, before the first replayed
    /// command and whether or not prediction runs this frame: put the working
    /// copy back to the decoded state, judging the acknowledged command's ring
    /// slot first when a fresh decode has arrived since the last frame.
    /// </summary>
    /// <param name="player">The local player being predicted.</param>
    /// <param name="acknowledged">The last command the server acknowledged.</param>
    void ResetBasis(EntityHandle player, int acknowledged);

    /// <summary>
    /// After one replayed command has run its movement and weapons, still
    /// inside the command's bracket: save the working copy under
    /// <paramref name="commandNumber"/>. <paramref name="firstTime"/> is
    /// false when the command is being predicted again on a later frame.
    /// </summary>
    void CommandRan(EntityHandle player, int commandNumber, bool firstTime);
}
