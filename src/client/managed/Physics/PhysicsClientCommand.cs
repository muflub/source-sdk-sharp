using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Physics;

/// <summary>
/// cs_cl_physics: the client physics callbacks' counters (lane c6-input) --
/// whether the level's environment has managed's table, and the impact sounds
/// queued and played -- so a live run can see the arm reach its consumer.
/// </summary>
[ConCommand("cs_cl_physics", Help = "Print the client physics callbacks' counters (managed table, impact sounds)")]
public sealed class PhysicsClientCommand(ILog log, ClientPhysicsCallbacks callbacks, ClientImpactSounds impacts,
                                          ClientCollisionSolver solver, ClientFluidEvents fluids,
                                          FrictionSounds scrapes) : IConCommand
{
    public void Execute(string args)
        => log.Msg($"[SourceSharp/Client] physics: table installed {(callbacks.Installed ? "yes" : "no")} " +
                   $"(installs {callbacks.Installs}), impact sounds queued {impacts.Queued}, played {impacts.Played}, " +
                   $"pairs asked {solver.Asked}, refused {solver.Refused}, splashes {fluids.Splashes} " +
                   $"(gunshot {fluids.EffectsOf(ClientSplashKind.GunshotSplash)}, slime {fluids.EffectsOf(ClientSplashKind.GunshotSlimeSplash)}, " +
                   $"ripple {fluids.EffectsOf(ClientSplashKind.WaterRipple)}), " +
                   $"scrapes started {scrapes.Started}, playing {scrapes.Active}\n");
}
