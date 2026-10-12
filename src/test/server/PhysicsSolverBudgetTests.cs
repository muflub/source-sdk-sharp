using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_physics_bones_native_zero 2.1: ShouldFreezeContacts and
/// AdditionalCollisionChecksThisTick, managed. From native's
/// CCollisionEvent::ShouldFreezeContacts (physics.cpp 1312-1343) and
/// ss_physics_collisionevent.h's 1200 cap at dcae1517d, which are stock's
/// (physics.cpp:741-768, physics_collisionevent.h).
/// </summary>
public class PhysicsSolverBudgetTests
{
    private int _tick;
    private readonly List<string> _printed = new();
    private readonly PhysicsSolver _solver;

    public PhysicsSolverBudgetTests()
    {
        var world = new OpenSolverWorld();
        var objects = new FakeSolverObjects();
        _solver = new PhysicsSolver(world, objects, new PhysicsSolverRules(), new EntityPairCollisions(world, objects),
                                    new PhysicsPenetrationLedger(), new SolverClock(), _ => { }, new RecordingCrushes(),
                                    () => _tick, _printed.Add);
    }

    [Fact]
    public void Under_twelve_hundred_checks_the_rest_of_the_budget_is_granted()
    {
        Assert.Equal(1200, _solver.AdditionalCollisionChecksThisTick(0));
        Assert.Equal(1, _solver.AdditionalCollisionChecksThisTick(1199));
        Assert.Equal(0, _solver.BudgetRefusals);
        Assert.Contains(_printed, p => p.Contains("getting expensive"));
    }

    [Fact]
    public void At_twelve_hundred_nothing_more_is_granted()
    {
        Assert.Equal(0, _solver.AdditionalCollisionChecksThisTick(1200));
        Assert.Equal(0, _solver.AdditionalCollisionChecksThisTick(5000));
        Assert.Equal(2, _solver.BudgetAsks);
        Assert.Equal(2, _solver.BudgetRefusals);
        Assert.Equal(5000, _solver.WorstChecksDone);
        Assert.Contains(_printed, p => p.Contains("exceeded collision check limit (5000)"));
    }

    [Fact]
    public void A_large_friction_system_is_never_frozen()
    {
        Assert.False(_solver.ShouldFreezeContacts(300));
        Assert.Equal(1, _solver.LargeFrictionSystems);
    }

    [Fact]
    public void The_large_friction_warning_prints_once_per_run_of_consecutive_ticks()
    {
        _tick = 100;
        _solver.ShouldFreezeContacts(300);   // prints: the last was tick 0
        _solver.ShouldFreezeContacts(300);   // same tick: quiet
        _tick = 101;
        _solver.ShouldFreezeContacts(300);   // the next tick: still the same run
        _tick = 105;
        _solver.ShouldFreezeContacts(301);   // a gap: prints again

        Assert.Equal(new[]
        {
            "Performance Warning: large friction system (300 objects)!!!\n",
            "Performance Warning: large friction system (301 objects)!!!\n",
        }, _printed);
    }
}

/// <summary>
/// The table vphysics calls: every slot counted on its own crossing op and
/// answered by the server, none forwarded to native (2.1).
/// </summary>
public unsafe class PhysicsCollisionSolverBindingTests
{
    private sealed class Answers : IPhysicsSolverAnswers
    {
        public readonly List<string> Seen = new();
        public int ShouldCollide(nint a, nint b, nint g0, nint g1) { Seen.Add($"collide {a:x} {b:x} {g0:x} {g1:x}"); return 0; }
        public int ShouldSolvePenetration(nint a, nint b, nint g0, nint g1) { Seen.Add($"penetrate {a:x} {b:x} {g0:x} {g1:x}"); return 0; }
        public bool ShouldFreezeObject(nint o) { Seen.Add($"freeze {o:x}"); return false; }
        public bool ShouldFreezeContacts(int n) { Seen.Add($"contacts {n}"); return true; }
        public int AdditionalCollisionChecksThisTick(int done) { Seen.Add($"budget {done}"); return 7; }
    }

    [Fact]
    public void Every_slot_is_answered_by_the_server_and_counted()
    {
        var crossings = new Crossings();
        var answers = new Answers();
        var sink = new PhysicsCollisionSolverBinding(crossings, answers);

        Assert.Equal(0, sink.ShouldCollide((void*)0x10, (void*)0x20, (void*)0x40, (void*)0x50));
        Assert.Equal(0, sink.ShouldSolvePenetration((void*)0x10, (void*)0x20, (void*)0x40, (void*)0x50, 0.015f));
        Assert.Equal((byte)0, sink.ShouldFreezeObject((void*)0x30));
        Assert.Equal((byte)1, sink.ShouldFreezeContacts(null, 4));
        Assert.Equal(7, sink.AdditionalCollisionChecksThisTick(1300));

        Assert.Equal(new[] { "collide 10 20 40 50", "penetrate 10 20 40 50", "freeze 30", "contacts 4", "budget 1300" }, answers.Seen);
        Assert.Equal(1, crossings.Read(CrossingOp.ServerPhysicsSolverShouldCollide));
        Assert.Equal(1, crossings.Read(CrossingOp.ServerPhysicsSolverShouldSolvePenetration));
        Assert.Equal(1, crossings.Read(CrossingOp.ServerPhysicsSolverShouldFreezeObject));
        Assert.Equal(1, crossings.Read(CrossingOp.ServerPhysicsSolverShouldFreezeContacts));
        Assert.Equal(1, crossings.Read(CrossingOp.ServerPhysicsSolverAdditionalCollisionChecksThisTick));
    }
}
