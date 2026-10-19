using System.Diagnostics;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_physics_cost</c>: what the server's managed physics paths cost
/// in the running game, in release as in debug (docs/todo.md's "physics paths
/// never measured in release"). One <c>SSCOST</c> row per meter
/// (<see cref="CostMeter.Line"/>): the whole physics frame per server frame
/// (units: steps), the solve alone, the frame less its solves, the player
/// shadow's step per command and its apply alone, and the timer's own floor
/// (a thousand empty timed calls). "reset" zeroes every meter after
/// printing, so a window is read, reset, played, read.
/// </summary>
[ConCommand("cs_sv_physics_cost",
    Help = "cs_sv_physics_cost [reset]: the managed physics frame, solve, player shadow step and apply, "
         + "timed in game: calls, means, min/median/p95/max ns. 'reset' zeroes them after printing.")]
public sealed class PhysicsCostReport(IEngineConsole console, PhysicsFrame frame, PlayerShadowBodies bodies,
                                      PlayerShadow shadow) : IConCommand
{
    public void Execute(string args)
    {
        long hz = Stopwatch.Frequency;
        console.Msg(CostMeter.Line("physics.frame", frame.FrameCost.Summary(hz)));
        console.Msg(CostMeter.Line("physics.simulate", frame.SimulateCost.Summary(hz)));
        console.Msg(CostMeter.Line("physics.managed", frame.ManagedCost.Summary(hz)));
        console.Msg(CostMeter.Line("shadow.step", shadow.StepCost.Summary(hz)));
        console.Msg(CostMeter.Line("shadow.apply", bodies.ApplyCost.Summary(hz)));
        console.Msg(CostMeter.Line("timer.empty", CostMeter.Calibrate(1000).Summary(hz)));

        if (args.Trim().Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            frame.FrameCost.Reset();
            frame.SimulateCost.Reset();
            frame.ManagedCost.Reset();
            shadow.StepCost.Reset();
            bodies.ApplyCost.Reset();
        }
    }
}
