using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_physics_solver</c>: what vphysics asked managed's
/// <c>IPhysicsCollisionSolver</c> and what it was told
/// (plan_physics_bones_native_zero 2.1). The counters native's ask ledger,
/// <c>ss_physworld_penetration</c> and the collision-check budget kept, now
/// managed's; the dev API's <c>/counters?group=solver</c> reads the same
/// objects (<see cref="DebugCounterGroups.Solver"/>).
/// </summary>
[ConCommand("cs_sv_physics_solver",
    Help = "What vphysics asked managed's collision solver: ShouldCollide asks and refusals by clause, "
         + "penetration asks, freezes, crushes and the collision-check budget.")]
public sealed class PhysicsSolverCommand(PhysicsSolver solver, IEngineConsole console) : IConCommand
{
    public void Execute(string args)
    {
        console.Msg("[SourceSharp] physics solver (managed IPhysicsCollisionSolver)\n");
        foreach (var counter in DebugCounterGroups.Solver(solver))
            console.Msg($"  {counter.Name,-28} {counter.Value}\n");
    }
}
