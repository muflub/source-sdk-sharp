using System.Diagnostics;
using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Shared.Bones;

namespace SourceSharp.Client.Ragdolls;

/// <summary>
/// <c>cs_cl_ragdoll_bench</c>: the ragdoll half of the physics perf pass's
/// instrument (plan_physics_bones_perf lane B). Times, on the live bodies, the
/// parts of a body's frame that change nothing: the step's scan of the moved
/// bodies (per frame), a body's bone write (per body), and the reads its
/// update makes (asleep, origin and the exact box; per body). One
/// <c>SSCLRAGDOLLBENCH</c> tab row per item: item, min ns, median ns, calls
/// per round. The settle and sleep are not timed: they act on the body.
/// </summary>
[ConCommand("cs_cl_ragdoll_bench",
    Help = "cs_cl_ragdoll_bench [rounds]: time a client body's scan, bone write and update reads, ns per call.")]
public sealed class ClientRagdollBench(ILog log, IClientRagdolls ragdolls) : IConCommand
{
    /// <summary>Calls per round.</summary>
    public const int CallsPerRound = 20_000;

    public void Execute(string args)
    {
        int rounds = int.TryParse(args.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) && r > 0
            ? Math.Min(r, 50)
            : 7;

        if (ragdolls is not ClientRagdolls bodies || bodies.Count == 0)
        {
            log.Msg("[SourceSharp/Client] cs_cl_ragdoll_bench: no bodies to time\n");
            return;
        }

        RagdollBody body = bodies.Bodies.First();
        RagdollSolver solver = bodies.Solver;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp/Client] cs_cl_ragdoll_bench: {bodies.Count} body(ies), timing 0x{body.Owner.Value:X8} " +
            $"({body.ListCount} elements), {rounds} round(s)\n"));

        Report("scan", rounds, () =>
        {
            int sink = 0;
            for (int i = 0; i < CallsPerRound; i++)
                sink += bodies.ScanMoved();
            return sink;
        });

        var bones = new BoneMatrix3x4[256];
        var owned = new int[RagdollBody.MaxElements];
        Report("write-bones", rounds, () =>
        {
            int sink = 0;
            for (int i = 0; i < CallsPerRound; i++)
                sink += bodies.WriteBones(body.Owner, bones, owned);
            return sink;
        });

        Report("update-reads", rounds, () =>
        {
            int sink = 0;
            for (int i = 0; i < CallsPerRound; i++)
            {
                bool asleep = solver.IsAsleep(body);
                Vector origin = solver.Origin(body);
                (Vector mins, _) = solver.ComputeExactBbox(body, origin);
                sink += (asleep ? 1 : 0) + (int)mins.Z;
            }

            return sink;
        });
    }

    private void Report(string item, int rounds, Func<int> round)
    {
        // Two untimed rounds first: the tiered JIT promotes the loop before it counts.
        round();
        round();

        var ns = new double[rounds];
        for (int i = 0; i < rounds; i++)
        {
            long start = Stopwatch.GetTimestamp();
            round();
            ns[i] = (Stopwatch.GetTimestamp() - start) * 1e9 / Stopwatch.Frequency / CallsPerRound;
        }

        Array.Sort(ns);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"SSCLRAGDOLLBENCH\t{item}\t{ns[0]:F1}\t{ns[rounds / 2]:F1}\t{CallsPerRound}\n"));
    }
}
