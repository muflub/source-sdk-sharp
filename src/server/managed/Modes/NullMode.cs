using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The core's own mode: today's deathmatch, reached through the mode
/// interfaces (plan 0.7: "today's deathmatch behaviour moved behind the
/// interfaces, so the proxy is exercised by the test mod from day one").
///
/// <para>
/// It does not reimplement the round — <see cref="GameRules"/>'s frag/time-limit
/// machine IS the round machine, and this class is the route to it, because a
/// proxy that copied the machine would be a second total to disagree with and
/// the no-owner facts would then measure the copy instead of the game.
/// <see cref="Rounds"/> therefore answers with the rules object ITSELF: the
/// proxy and the rules ask "does a mode own the round?" by reference
/// comparison, and identity — not a wrapper that forwards — is the honest
/// answer to "whose machine is this".
/// </para>
///
/// <para>
/// The optional concerns are the core's answers by absence: respawn is
/// <see cref="Players.RespawnDelay"/>, spawns are the module's own walk,
/// scoring is the scoreboard alone, setup prepares nothing.
/// </para>
///
/// <para>
/// Not sealed, because it is the base a mod varies rather than replaces (D10):
/// a mode that wants the deathmatch machine with one concern its own
/// subclasses this and overrides the concern — giving its <see cref="Rounds"/>
/// a <see cref="CoreRoundFlow"/>-derived wrapper, which is NOT the rules
/// object by reference and so is dispatched to as a mode's own machine, whose
/// <c>Think</c> calls <see cref="GameRules.ThinkStock"/> (the one-way door:
/// the delegation lives in <see cref="GameRules.Think"/>, and a flow that
/// came back through it would ask itself the question it had just been
/// asked). A mode that wants a machine of its own registers its own
/// <see cref="IGameMode"/> under its own id and never comes near this.
/// </para>
/// </summary>
public class NullMode : IGameMode
{
    private readonly GameRules _core;

    public NullMode(GameRules core)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
    }

    /// <summary><see cref="GameModes.NullModeId"/>.</summary>
    public virtual string Id => GameModes.NullModeId;

    /// <summary>The core's machine itself — the same object, by reference.</summary>
    public virtual IRoundFlow Rounds => _core;

    /// <summary>The core rules this mode fronts, for subclasses and the dev view.</summary>
    protected GameRules Core => _core;

    /// <summary>
    /// The reusable wrapper shape for a mode that keeps the deathmatch
    /// machine and adds its own tick around it: the arms today's deathmatch
    /// does not use are the no-ops they were before the interface existed,
    /// and <c>Think</c> goes through the one-way door.
    /// </summary>
    protected class CoreRoundFlow(GameRules core) : IRoundFlow
    {
        public void Think(float now) => core.ThinkStock();

        /// <summary>
        /// The core's own latch. There is no second one to read: the core's
        /// checks are what ended the round, and a second latch is a total
        /// that can disagree with the first.
        /// </summary>
        public bool RoundOver => core.RoundOver;

        public void ResetRound() => core.ResetRound();

        public void OnPlayerSpawned(EntityHandle player, float now) { }

        public void OnPlayerKilled(PlayerDeathArgs death) { }
    }
}
