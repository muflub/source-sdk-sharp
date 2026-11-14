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
///
/// <para>
/// A link the bot's route takes (lane G5e) is begun under the same rule but two of its parts: it is not
/// spaced from the jump before it (<see cref="RocketJumpEverySeconds"/> was made for a bot bouncing down a
/// corridor, and it made a chain of links wait out its follower's hold at the second, and the first link
/// after a stretch jump too), and a glance does not hold it back (the program stands the bot at the start
/// and turns the view itself). What keeps a chain from jumping the bot to death is the health: each link
/// is begun only with the health after its own blast over the floor, checked as the bot stands at it,
/// and a search takes a link only while it has that health to spend (<c>TFNavProfile.LinksFor</c>).
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
    private bool JumpAllowed => MayRocketJump && JumpRefusalNow == JumpLinkRefusal.None;

    /// <summary>Whether this bot rocket jumps at all: a Soldier with the ability and a book of jumps.</summary>
    private bool MayRocketJump => _class == TFClass.Soldier && _rocketJumpRow >= 0 && Jumps is not null;

    /// <summary>The first part of <see cref="JumpAllowed"/>'s rule that fails now, or none.</summary>
    private JumpLinkRefusal JumpRefusalNow
        => !HoldingLauncher ? JumpLinkRefusal.Launcher
           : _self.Clip < RocketJumpRockets ? JumpLinkRefusal.Clip
           : (_self.Flags & SelfFlags.Reloading) != 0 ? JumpLinkRefusal.Reloading
           : (_self.Flags & SelfFlags.Grounded) == 0 ? JumpLinkRefusal.Airborne
           : _targetSighted ? JumpLinkRefusal.Sighted
           : Carrying ? JumpLinkRefusal.Carrying
           : _self.Health < RocketJumpHealthFloor ? JumpLinkRefusal.Health
           : !_hasGoal || _move.Kind != MoveKind.Follow ? JumpLinkRefusal.NoGoal
           : _glance.Holding(_now) ? JumpLinkRefusal.Glance
           : _now < _nextJumpAt ? JumpLinkRefusal.Spacing
           : JumpLinkRefusal.None;

    /// <summary>
    /// Whether a link the route takes may be begun, refused for <paramref name="why"/> (<see cref="JumpRefusalNow"/>): every
    /// part of the rule but the spacing and a glance (lane G5e).
    /// </summary>
    private static bool LinkMayBegin(JumpLinkRefusal why) => why is JumpLinkRefusal.None or JumpLinkRefusal.Spacing or JumpLinkRefusal.Glance;

    /// <summary>
    /// The route's own jump: a blast link its follower says the bot stands at the start of, on its way to this decide's
    /// goal (lane G5c-2b: the follower only says it; the tree begins it), whose blast leaves the bot over its health floor.
    /// </summary>
    private bool RouteLinkAdvised([NotNullWhen(true)] out JumpPlan? plan)
    {
        plan = Jumps!.RouteLink(_bot.Slot, _now, _goal) is { } link && _self.Health - link.Health >= RocketJumpHealthFloor ? link : null;
        return plan is not null;
    }

    /// <summary>
    /// The jump the book advises for the stretch ahead, or false: false when the bot is not moving along a way or nothing
    /// is known for the stretch (it is asked about, for the next time), or when the jump's blast would take the bot under
    /// its health floor.
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

    /// <summary>
    /// A place the bot was sent to by hand (<c>ai_tf_errand</c>, for proving a jump), with nobody in sight and nothing
    /// carried: its goal this decide, whatever the tree chose. Runs after the tree and before the jump's rule.
    /// </summary>
    private void Errand()
    {
        if (Jumps is null || !Jumps.Errand(_bot.Slot, out AiVector goal) || _targetSighted || Carrying || !_self.Alive)
            return;
        GoTo(goal, MoveSpeed.Run);
        _leaf = "errand";
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
