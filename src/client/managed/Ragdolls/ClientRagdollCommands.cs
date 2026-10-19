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
        // The same meters' distributions: the step per frame (units: bodies)
        // and the write per call, min/median/p95/max over the latest calls.
        log.Msg(CostMeter.Line("clragdoll.step", bodies.StepCost.Summary(Stopwatch.Frequency)));
        log.Msg(CostMeter.Line("clragdoll.write", bodies.WriteCost.Summary(Stopwatch.Frequency)));
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

/// <summary>
/// <c>cs_cl_boltpins</c>: the crossbow pins (RagdollBoltPin) -- one
/// <c>SSCLBOLTPIN</c> tab row per remembered pin after a prose line: owner,
/// physics bone, the bolt point, how far the line's entry into the element was
/// from it when the pin was made, where the element's end of the ballsocket
/// is now and how far that is from the bolt (the constraint pulls it to 0 and
/// holds it there), or "gone" once the body went.
/// </summary>
[ConCommand("cs_cl_boltpins", Help = "cs_cl_boltpins: the ragdoll elements crossbow bolts pinned to the world, and how far each is from its bolt now.")]
public sealed class ClientBoltPinsCommand(ILog log, IRagdollBoltPin pins) : IConCommand
{
    public void Execute(string args)
    {
        if (pins is not RagdollBoltPin made)
        {
            log.Msg("[SourceSharp/Client] cs_cl_boltpins: no pins (no ragdolls on this client)\n");
            return;
        }

        log.Msg($"[SourceSharp/Client] cs_cl_boltpins: {made.Pins} pin(s) made, {made.Made.Count} remembered\n");

        foreach (BoltPinRecord pin in made.Made)
        {
            Vector b = pin.BoltPoint;
            Vector e = pin.EntryPoint - b;
            float entry = MathF.Sqrt(e.X * e.X + e.Y * e.Y + e.Z * e.Z);
            string now = "gone";
            if (made.AttachedWorld(pin, out Vector at))
            {
                Vector d = at - b;
                float distance = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
                now = string.Create(CultureInfo.InvariantCulture, $"{at.X:F2}\t{at.Y:F2}\t{at.Z:F2}\t{distance:F2}");
            }

            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"SSCLBOLTPIN\t0x{pin.Owner.Value:X8}\t{pin.PhysicsBone}\t{b.X:F2}\t{b.Y:F2}\t{b.Z:F2}\t{entry:F2}\t{now}\n"));
        }
    }
}
