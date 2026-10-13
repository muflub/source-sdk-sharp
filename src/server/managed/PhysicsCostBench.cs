using System.Diagnostics;
using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_physics_bench</c>: the physics perf pass's instrument
/// (plan_physics_bones_perf lane B). Times, on the live level's own objects,
/// the managed costs the pass works on: one vphysics getter through its
/// vtable, GetVelocity, the solver's ShouldCollide answer over the level's
/// object pairs, and each motion controller kind's Simulate. Every item runs
/// several rounds and reports the fastest and the median, in nanoseconds per
/// call, as one <c>SSPHYSBENCH</c> tab row:
/// item, min ns, median ns, calls per round.
///
/// <para>
/// It changes nothing a live check reads: the solver is asked through
/// <see cref="PhysicsSolver.Decide(nint, nint, nint, nint, out CollisionRefusal)"/>, with the game data
/// vphysics would hand, which skips the ask ledger, and each
/// controller kind is simulated from a fresh <see cref="MotionState"/> that no
/// controller holds. Only release numbers are a speed result.
/// </para>
/// </summary>
[ConCommand("cs_sv_physics_bench",
    Help = "cs_sv_physics_bench [rounds]: time the managed physics hot paths on the live level "
         + "(getter, GetVelocity, ShouldCollide, motion Simulate), ns per call.")]
public sealed class PhysicsCostBench(PhysicsSolver solver, IPhysicsSolverWorld solverWorld, PhysicsMotionControllers motion,
                                     IVPhysicsInspect inspect, IEngineEntityPhysics physics,
                                     IEngineConsole console, VPhysicsTables vphysics) : IConCommand
{
    /// <summary>The most objects the solver item pairs up: 48 makes 1,128 pairs.</summary>
    public const int MaxObjects = 48;

    /// <summary>Calls per round for the single-object items.</summary>
    public const int CallsPerRound = 200_000;

    public void Execute(string args)
    {
        int rounds = int.TryParse(args.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) && r > 0
            ? Math.Min(r, 50)
            : 7;

        nint environment = physics.EnvironmentPointer();
        if (environment == 0)
        {
            console.Msg("[SourceSharp] cs_sv_physics_bench: no physics environment (no level)\n");
            return;
        }

        nint[] all = inspect.Objects(environment);
        var objects = new VPhysicsSolverObjects(vphysics);
        nint moveable = 0;
        foreach (nint o in all)
        {
            if (objects.IsMoveable(o))
            {
                moveable = o;
                break;
            }
        }

        // The pairs the solver is really asked: entity-owned objects (game data
        // set) and the world, so each ask runs the whole rule rather than the
        // unowned early answer.
        nint world = physics.WorldObject();
        var owned = new List<nint>(MaxObjects);
        if (world != 0)
            owned.Add(world);
        foreach (nint o in all)
        {
            if (owned.Count >= MaxObjects)
                break;
            if (o != world && objects.GameData(o) != 0)
                owned.Add(o);
        }

        nint[] paired = owned.ToArray();

        // Each object's game data, read once: vphysics hands it with every ask.
        nint[] gameData = Array.ConvertAll(paired, objects.GameData);
        console.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp] cs_sv_physics_bench: {all.Length} object(s), {paired.Length} paired, "
            + $"moveable 0x{moveable:X}, {rounds} round(s)\n"));

        if (moveable == 0)
        {
            console.Msg("[SourceSharp] cs_sv_physics_bench: no moveable object to time\n");
            return;
        }

        var calls = new VPhysicsMotionCalls(vphysics);

        Report("getter", rounds, CallsPerRound, () =>
        {
            uint sink = 0;
            for (int i = 0; i < CallsPerRound; i++)
                sink += objects.GameFlags(moveable);
            return sink;
        });

        Report("velocity", rounds, CallsPerRound, () =>
        {
            float sink = 0;
            for (int i = 0; i < CallsPerRound; i++)
                sink += calls.Velocity(moveable).Linear.X;
            return (uint)sink;
        });

        int pairs = paired.Length * (paired.Length - 1) / 2;
        int passes = pairs == 0 ? 0 : Math.Max(1, CallsPerRound / 4 / pairs);
        if (pairs > 0)
        {
            Report("should-collide", rounds, pairs * passes, () =>
            {
                uint sink = 0;
                for (int p = 0; p < passes; p++)
                {
                    for (int i = 0; i < paired.Length; i++)
                    {
                        for (int j = i + 1; j < paired.Length; j++)
                            sink += (uint)solver.Decide(paired[i], paired[j], gameData[i], gameData[j], out _);
                    }
                }

                return sink;
            });
        }

        // An object nobody owns against an owned one: two thirds of the asks
        // dm_lockdown makes (the perf pass's ask dump at 532c1a078).
        nint unowned = Array.Find(all, o => o != world && objects.GameData(o) == 0);
        if (unowned != 0 && paired.Length > 1)
        {
            Report("should-collide-unowned", rounds, CallsPerRound, () =>
            {
                uint sink = 0;
                for (int i = 0; i < CallsPerRound; i++)
                    sink += (uint)solver.Decide(unowned, paired[1], 0, gameData[1], out _);
                return sink;
            });
        }

        // What one owned side costs to read: its entity's facts (the engine
        // block, the move type, IsPlayer), the part of an owned ask that is
        // not vphysics'.
        if (paired.Length > 1 && PhysicsGameData.Owner(gameData[1]) is { } entity && entity != EntityHandle.None)
        {
            Report("solver-entity", rounds, CallsPerRound, () =>
            {
                uint sink = 0;
                for (int i = 0; i < CallsPerRound; i++)
                {
                    solverWorld.TryEntity(entity, out SolverEntity facts);
                    sink += (uint)facts.CollisionGroup;
                }

                return sink;
            });
        }

        const int motionCalls = CallsPerRound / 4;
        foreach (MotionControllerKind kind in new[]
                 {
                     MotionControllerKind.ConstantForce, MotionControllerKind.Motor,
                     MotionControllerKind.KeepUpright, MotionControllerKind.Medium,
                 })
        {
            MotionState state = BenchState(kind);
            Report("motion-" + kind.ToString().ToLowerInvariant(), rounds, motionCalls, () =>
            {
                float sink = 0;
                for (int i = 0; i < motionCalls; i++)
                {
                    motion.SimulateState(state, moveable, 0.015f, out Vector linear, out Vector angular);
                    sink += linear.X + angular.Z;
                }

                return (uint)sink;
            });
        }
    }

    /// <summary>A state of each kind with every arm of its Simulate switched on.</summary>
    public static MotionState BenchState(MotionControllerKind kind)
    {
        var state = new MotionState(kind);
        switch (kind)
        {
            case MotionControllerKind.ConstantForce:
                state.Active = true;
                state.Linear = new Vector(1f, 2f, 3f);
                state.Angular = new Vector(0f, 0f, 1f);
                break;
            case MotionControllerKind.Motor:
                state.Axis = new Vector(0f, 0f, 1f);
                state.Speed = 90f;
                state.MaxTorque = 1000f;
                break;
            case MotionControllerKind.KeepUpright:
                state.Active = true;
                state.AngularLimit = 30f;
                break;
            case MotionControllerKind.Medium:
                state.GravityScale = 0.5f;
                state.AddAirDensity = 2f;
                state.LinearLimit = 100f;
                state.LinearScale = 0.9f;
                state.MediumAngularLimit = 90f;
                state.AngularScale = 0.9f;
                break;
        }

        return state;
    }

    private void Report(string item, int rounds, int callsPerRound, Func<uint> round)
    {
        // One untimed round first: the tiered JIT promotes the loop before it counts.
        round();
        round();

        var ns = new double[rounds];
        for (int i = 0; i < rounds; i++)
        {
            long start = Stopwatch.GetTimestamp();
            round();
            long ticks = Stopwatch.GetTimestamp() - start;
            ns[i] = ticks * 1e9 / Stopwatch.Frequency / callsPerRound;
        }

        Array.Sort(ns);
        console.Msg(string.Create(CultureInfo.InvariantCulture,
            $"SSPHYSBENCH\t{item}\t{ns[0]:F1}\t{ns[rounds / 2]:F1}\t{callsPerRound}\n"));
    }
}
