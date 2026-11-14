using System.Diagnostics.CodeAnalysis;
using SourceSharp.Ai.TF2.Jump;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Soldier's rocket jump as the tree decides it (plan_bots_teams_tf2_part_g G.5 step 6). The
/// tree says only whether a jump is allowed now; where one pays, and its recipe, is read from the
/// level's book of jumps (<see cref="IJumpAdvisor"/>), which a worker fills from the simulator: a
/// decide never flies or searches.
///
/// <para>
/// Allowed: a Soldier with its launcher in hand, loaded with a rocket to spare and not being
/// reloaded, on the ground and moving along a way to a goal, nobody in sight, not carrying, not in
/// the middle of a glance, and with the health to pay for it. Then the book is asked about the
/// stretch ahead (the bot's place, its heading, its goal), and the jump it answers is begun when
/// the health left after its blast stays over the floor: one ability one-shot that carries the
/// plan's number to the program that flies it (<see cref="RocketJumpProgram"/>).
/// </para>
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>The level's jumps, or null: a tree given none makes no rocket jump.</summary>
    public IJumpAdvisor? Jumps { get; init; }

    /// <summary>A jump is begun with at least this many rockets loaded: one for the jump, one to spare.</summary>
    public const int RocketJumpRockets = 2;

    /// <summary>The longest a jump begun is taken to be under way, whatever the bot's feet say.</summary>
    public const float RocketJumpMostSeconds = 9f;

    private bool _jumping;
    private Tick _jumpBegan;

    /// <summary>The plan of the last jump begun, for the facts.</summary>
    public JumpPlan? LastJump { get; private set; }

    /// <summary>
    /// Whether a rocket jump may be asked about now: everything but the route's say. The health is
    /// the floor's here; what the jump itself costs is known with its plan.
    /// </summary>
    private bool JumpAllowed
        => _class == TFClass.Soldier && _rocketJumpRow >= 0 && Jumps is not null && HoldingLauncher && _self.Clip >= RocketJumpRockets && _now >= _nextJumpAt
           && (_self.Flags & (SelfFlags.Grounded | SelfFlags.Reloading)) == SelfFlags.Grounded && !_targetSighted && !Carrying
           && _self.Health >= RocketJumpHealthFloor && _hasGoal && _move.Kind == MoveKind.Follow && !_glance.Holding(_now);

    /// <summary>
    /// The jump the book advises for the stretch ahead of the bot, or false: the bot is not moving
    /// along a way, nothing is known for the stretch (it is asked about, for the next time), or
    /// the jump's blast would take the bot under its health floor.
    /// </summary>
    private bool JumpAdvised([NotNullWhen(true)] out JumpPlan? plan)
    {
        plan = null;
        var way = new AiVector(_self.Velocity.X, _self.Velocity.Y, 0f);
        if (way.Length < RocketJumpSpeed)
            return false;
        JumpPlan? advised = Jumps!.Advise(new JumpStretchAsk(_self.Origin, way.Normalised(), _goal, _now));
        if (advised is null || _self.Health - advised.Health < RocketJumpHealthFloor)
            return false;
        plan = advised;
        return true;
    }

    /// <summary>A jump begins: the hands are off until <see cref="JumpUnderWay"/> says it is over.</summary>
    private void JumpBegun(JumpPlan plan)
    {
        (_jumping, _jumpBegan, LastJump) = (true, _now, plan);
    }

    /// <summary>
    /// Whether the jump last begun still has the bot: through its approach
    /// (<see cref="RocketJumpBusySeconds"/> from the one-shot) and then for as long as the bot is
    /// off the ground, the hands kept off a decide ahead. A bot that died in it, or one
    /// <see cref="RocketJumpMostSeconds"/> on, has it no longer.
    /// </summary>
    private bool JumpUnderWay()
    {
        if (!_jumping)
            return false;
        float since = (_now - _jumpBegan) * _tick;
        bool approach = since <= RocketJumpBusySeconds;
        // A death clears the hold: the next life's first decide finds it gone.
        if (_jumpUntil.Value == 0 || since > RocketJumpMostSeconds || (!approach && (_self.Flags & SelfFlags.Grounded) != 0))
        {
            _jumping = false;
            return false;
        }

        // The hold is kept a decide ahead: the hands are asked before this is (Engage runs first), so a hold that ended on this decide would let one shot go.
        if (_jumpUntil < _now + DecideSchedule.Period + 1)
            _jumpUntil = _now + DecideSchedule.Period + 1;
        return true;
    }
}
