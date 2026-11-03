namespace TF2.Shared.Rules;

/// <summary>
/// The round machine's state, whoever holds it: the server's flow (stock's
/// gamerules <c>State_Get</c>) or the client's resource mirror (the
/// <c>RoundState</c> word the server's resource think writes). The movement's
/// restart freeze asks it on both sides, so the prediction freezes with the
/// server — the same shape as <see cref="ITFRoundSetup"/>'s two halves.
/// </summary>
public interface ITFRoundStateView
{
    /// <summary>The round machine's state.</summary>
    RoundState State { get; }
}
