using System.Diagnostics;
using System.Globalization;
using SourceSharp.Abi;

namespace SourceSharp.Client.Ragdolls;

/// <summary>
/// <c>cs_cl_ragdolls</c>: every client body, where the solver has it and
/// whether it sleeps -- the readout a live check reads over time to see a
/// corpse fall, settle and sleep (plan_physics_bones_native_zero Phase 3).
/// One <c>SSCLRAGDOLL</c> tab row per body after a prose line: owner,
/// elements, origin, asleep now, the update's last asleep, the cull box's
/// width, the settle clock, the state bits.
/// </summary>
[ConCommand("cs_cl_ragdolls", Help = "cs_cl_ragdolls: the client's jointed bodies -- owner, elements, origin, asleep, cull box.")]
public sealed class ClientRagdollsCommand(ILog log, IClientRagdolls ragdolls) : IConCommand
{
    public void Execute(string args)
    {
        if (ragdolls is not ClientRagdolls bodies)
        {
            log.Msg("[SourceSharp/Client] cs_cl_ragdolls: no ragdolls (vphysics did not answer)\n");
            return;
        }

        log.Msg($"[SourceSharp/Client] cs_cl_ragdolls: {bodies.Count} body(ies); made {bodies.Created}, " +
                $"destroyed {bodies.Destroyed}, refused {bodies.Refused}; last step wrote {bodies.LastStepped}, " +
                $"updated {bodies.LastUpdated}\n");

        // The cost meter: one SSCLRAGDOLLCOST row, ns per body-frame of the
        // step and ns per bone write, then zeroed when asked ("reset").
        double tickNs = 1e9 / Stopwatch.Frequency;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"SSCLRAGDOLLCOST\t{(bodies.StepBodyFrames == 0 ? 0 : bodies.StepTicks * tickNs / bodies.StepBodyFrames):F1}\t" +
            $"{bodies.StepBodyFrames}\t{(bodies.Writes == 0 ? 0 : bodies.WriteTicks * tickNs / bodies.Writes):F1}\t{bodies.Writes}\n"));
        if (args.Trim() == "reset")
            bodies.ResetCost();

        foreach (RagdollBody body in bodies.Bodies)
        {
            RagdollState state = bodies.State(body.Owner, out Vector origin);
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"SSCLRAGDOLL\t0x{body.Owner.Value:X8}\t{body.ListCount}\t{origin.X:F2}\t{origin.Y:F2}\t{origin.Z:F2}\t" +
                $"{(bodies.AsleepNow(body) ? 1 : 0)}\t{(body.AllAsleep ? 1 : 0)}\t{body.Maxs.X - body.Mins.X:F1}\t" +
                $"{body.LastOriginChangeTime:F2}\t{(int)state}\n"));
        }
    }
}
