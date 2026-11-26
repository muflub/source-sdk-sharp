using SourceSharp.Abi;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// What a bot's class play asks of its hands while its blast jump flies (round 2's S1): the keys it holds (a swing, a
/// shot), the keys it presses once (a parachute's toggle), where it looks (an enemy under it) and a weapon it takes out
/// (a melee weapon for a Market Garden). One ask is a decide's: the class asks again each decide it still wants it, and
/// a new ask is a new object, so the program tells a fresh ask from one it has already pressed. The program lends only
/// what <see cref="JumpHands.LendKeys"/> holds and only in the air after its own launch (<see cref="JumpHands"/>).
/// </summary>
/// <param name="Bot">The bot asking: an ask is read only by that bot's own program.</param>
public sealed record AirAsk(ActorId Bot)
{
    /// <summary>Keys held for as long as the ask stands.</summary>
    public int Hold { get; init; }

    /// <summary>Keys pressed once for the ask, on the first command of its life the program may lend them, and let go after it: an edge, as a toggle wants.</summary>
    public int Press { get; init; }

    /// <summary>Where the view is to come to, or null: the program's own look (the way it goes).</summary>
    public AiAngles? Look { get; init; }

    /// <summary>A weapon to select, as a <see cref="ActionKind.SelectWeapon"/> one-shot names it, or -1: none. Selected once for the ask, as its press.</summary>
    public int Weapon { get; init; } = -1;

    /// <summary>Whether the ask asks anything.</summary>
    public bool Empty => Hold == 0 && Press == 0 && Look is null && Weapon < 0;
}

/// <summary>
/// A blast jump in the air (round 2's S1): the bot flying it and the plan, from the command its program launched it to the
/// one it ended on. The program writes it (<see cref="JumpBook.FlightBegan"/>); the tree reads it to ask its class what the
/// hands do in the air (<see cref="IJumpAdvisor.Flying"/>), so the ask follows the program's own flight, however long its
/// approach was. A new flight is a new object, even of the same plan.
/// </summary>
public sealed record AirFlight(ActorId Bot, JumpPlan Plan);

/// <summary>
/// The hands in flight (round 2's S1): what a blast jump's program keeps and what it lends.
///
/// <list type="bullet">
/// <item><b>Kept: the launch.</b> From the approach to the last of its own triggers (the first rocket and a recipe's second,
/// <see cref="JumpPilot.LooksTo"/>) every key, the look and the move are the program's: nothing is lent before the blast has
/// been felt and the view has been let go to the way it goes, so a lent key never lands on the launch's command, the jump's,
/// or a second rocket's.</item>
/// <item><b>Kept: the flight.</b> The move (the pilot's air control to its landing) and the duck (a crouched recipe's air
/// crouch, held to the landing; an uncrouched recipe flies uncrouched) are the program's to the landing. A program's own key
/// wins a clash: a key it presses itself on a command (closing a parachute it lent open) is not lent on it, nor on the next,
/// so a lent press is not taken for its own.</item>
/// <item><b>Lent: the rest.</b> In the air after the launch the program lends <see cref="LendKeys"/> (the triggers, the reload,
/// the jump key), the look and a weapon to its bot's class play, as the class asks each decide (<see cref="AirAsk"/>): what
/// its play does in the air, it does there. An ask the program has seen for <see cref="AskStaleCommands"/> commands without
/// a fresh one is dropped: a play that stopped asking asks nothing.</item>
/// <item><b>A detonate.</b> No program flies a sticky jump yet (S3's sticky kind is the simulator's only); one that does keeps
/// the second trigger to its detonation, as the rocket's program keeps the first to its shots.</item>
/// <item><b>The parachute</b> (stock's B.A.S.E. Jumper, <c>CTFGameMovement::ToggleParachute</c>, tf_gamemovement.cpp:1117-1155):
/// a lent press of the jump key in the air opens it, a second closes it, and once closed it does not open again before the
/// landing (<c>tf_parachute_deploy_toggle_allowed</c> ships 0, :58). The program keeps a landing it needs: a parachute it lent
/// open is closed by its own press once the flight has been in the air <see cref="ChuteKeepSeconds"/> past the plan's time
/// in the air, so the high ground is kept that long at most and the landing comes well inside the program's commands.</item>
/// </list>
/// </summary>
public static class JumpHands
{
    /// <summary>The keys a program lends in flight: the triggers, the reload and the jump key.</summary>
    public const int LendKeys = GameMovement.InAttack | GameMovement.InAttack2 | GameMovement.InJump | (int)InputButtons.Reload;

    /// <summary>Commands an ask stands without a fresh one: two decides and a little over (a decide every <see cref="DecideSchedule.Period"/>).</summary>
    public const int AskStaleCommands = 2 * DecideSchedule.Period + 2;

    /// <summary>
    /// How long past the plan's own time in the air a parachute the program lent open is kept before the program closes it:
    /// at stock's fall of 100 u/s under it (<c>tf_parachute_maxspeed_z</c>, tf_gamemovement.cpp:55), 300 u more of height held.
    /// The row's commands (<c>TFAbilities.RocketJumpCommands</c>, 12 s) and the tree's hold (<c>RocketJumpMostSeconds</c>, 9 s)
    /// both outlast a plan's flight of 2 s and this.
    /// </summary>
    public const float ChuteKeepSeconds = 3f;
}
