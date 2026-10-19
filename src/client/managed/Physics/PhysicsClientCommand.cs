using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Physics;

/// <summary>
/// cs_cl_physics: the client physics' counters -- whether the level has an
/// environment and managed's tables are on it, the steps taken, and the
/// impact sounds, solver asks, splashes and scrapes -- so a live run can see
/// each arm reach its consumer.
/// </summary>
[ConCommand("cs_cl_physics", Help = "Print the client physics counters (environment, tables, steps and their cost, impact sounds); 'reset' zeroes the step meter")]
public sealed class PhysicsClientCommand(ILog log, ClientPhysicsSystem system, ClientImpactSounds impacts,
                                          ClientCollisionSolver solver, ClientFluidEvents fluids,
                                          FrictionSounds scrapes) : IConCommand
{
    public void Execute(string args)
    {
        if (args.Trim() == "reset")
        {
            system.ResetMeter();
            log.Msg("[SourceSharp/Client] physics: the step meter is reset\n");
            return;
        }

        log.Msg($"[SourceSharp/Client] physics: environment {(system.Environment != 0 ? "yes" : "no")}, " +
                $"tables installed {(system.Installed ? "yes" : "no")} (installs {system.Installs}), steps {system.Steps} " +
                $"(step {system.StepNs:F0} ns, solver {system.SolverNs:F0} ns), " +
                   $"impact sounds queued {impacts.Queued}, played {impacts.Played}, " +
                   $"pairs asked {solver.Asked}, refused {solver.Refused}, splashes {fluids.Splashes} " +
                   $"(gunshot {fluids.EffectsOf(ClientSplashKind.GunshotSplash)}, slime {fluids.EffectsOf(ClientSplashKind.GunshotSlimeSplash)}, " +
                   $"ripple {fluids.EffectsOf(ClientSplashKind.WaterRipple)}), " +
                   $"scrapes started {scrapes.Started}, playing {scrapes.Active}\n");
    }
}
