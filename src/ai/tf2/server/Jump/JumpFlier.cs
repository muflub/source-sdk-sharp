using SourceSharp.Abi;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// What a piloted jump did in the simulator. <paramref name="Flew"/>: it left
/// the ground after the jump's command and came back to it.
/// <paramref name="Touch"/> is where it came down and <paramref name="Rest"/>
/// where it stopped sliding (where the run ended, when it never came down),
/// <paramref name="OnGround"/> whether it stood there.
/// <paramref name="Seconds"/> is from the first command to the touch,
/// <paramref name="AirSeconds"/> the part of it off the ground.
/// <paramref name="Apex"/> is the highest the origin rose over the takeoff,
/// <paramref name="BlastDamage"/> what its own blasts handed the damage layer.
/// </summary>
public readonly record struct JumpOutcome(bool Flew, Vector Touch, Vector Rest, bool OnGround, float Seconds, float AirSeconds, float Apex,
                                          float BlastDamage, int Rockets, int Commands);

/// <summary>
/// Flies a recipe in the simulator with the pilot in the loop
/// (plan_bots_teams_tf2_part_g G.5 steps 2 and 4): each command is
/// <see cref="JumpPilot.Step"/>'s answer to where the last one left the body,
/// as a live controller's will be to where the game has the player. It holds
/// one simulator, so it serves one thread at a time.
/// </summary>
public sealed class JumpFlier
{
    /// <summary>Commands after the touch a run goes on for at most, while the body slides to a stop.</summary>
    public const int SettleCommands = 66;

    /// <summary>The speed over the ground under which a landed body has stopped, units a second.</summary>
    public const float RestSpeed = 1f;

    private readonly BlastJumpSim _sim;
    private readonly JumpPilotLimits _limits;

    public JumpFlier(BlastJumpSim sim, in JumpPilotLimits limits)
    {
        _sim = sim;
        _limits = limits;
    }

    /// <summary>The numbers its pilot flies by.</summary>
    public JumpPilotLimits Limits => _limits;

    /// <summary>The simulator under it, for a caller that flies a list of commands through the same world.</summary>
    public BlastJumpSim Sim => _sim;

    /// <summary>
    /// Flies <paramref name="recipe"/> from <paramref name="start"/> for at
    /// most <paramref name="maxCommands"/> commands, to its rest after the
    /// landing. <paramref name="track"/> and <paramref name="sent"/>, when
    /// given, take the body after each command and the command.
    /// </summary>
    public JumpOutcome Fly(in JumpRecipe recipe, in JumpStart start, int maxCommands, List<JumpPoint>? track = null, List<JumpCommand>? sent = null)
    {
        _sim.Begin(start);
        float tick = _limits.TickSeconds;
        JumpPilotState state = default;
        var body = new JumpBody(start.Origin, start.Velocity, start.View, start.OnGround);
        int airFrom = -1, touchAt = -1, commands = 0;
        float top = start.Origin.Z, takeoffZ = start.Origin.Z;
        Vector touch = start.Origin;
        bool onGround = start.OnGround;
        for (int i = 0; i < maxCommands; i++)
        {
            JumpPilotStep step = JumpPilot.Step(recipe, state, body, _limits);
            if (touchAt < 0)
            {
                if (step.Next.Phase == JumpPhase.Landed)
                {
                    // The body handed in was back on the ground: the command before this one put it there.
                    touchAt = i;
                    touch = body.Origin;
                }
                else if (airFrom < 0 && step.Next.Airborne)
                {
                    airFrom = i;
                }
            }

            if (touchAt >= 0)
            {
                float vx = body.Velocity.X, vy = body.Velocity.Y;
                if (i - touchAt >= SettleCommands || (i > touchAt && vx * vx + vy * vy < RestSpeed * RestSpeed))
                    break;
            }

            JumpPoint point = _sim.Step(step.Command);
            commands++;
            state = step.Next;
            body = new JumpBody(point.Origin, point.Velocity, step.Command.View, point.OnGround);
            onGround = point.OnGround;
            if (touchAt < 0)
            {
                if (point.OnGround && airFrom < 0)
                    top = takeoffZ = point.Origin.Z;
                else
                    top = MathF.Max(top, point.Origin.Z);
            }

            track?.Add(point);
            sent?.Add(step.Command);
        }

        bool flew = touchAt >= 0 && airFrom >= 0;
        return new JumpOutcome(flew, flew ? touch : body.Origin, body.Origin, onGround, (touchAt >= 0 ? touchAt : commands) * tick,
                               airFrom < 0 ? 0f : ((touchAt >= 0 ? touchAt : commands) - airFrom) * tick,
                               airFrom < 0 ? 0f : MathF.Max(0f, top - takeoffZ), _sim.BlastDamage, _sim.Rockets, commands);
    }
}
