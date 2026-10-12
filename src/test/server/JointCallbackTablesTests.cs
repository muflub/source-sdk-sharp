using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// vphysics' IPhysicsConstraintEvent and IMotionEvent as managed builds them
/// (plan_physics_bones_native_zero 2.2): each a ONE-slot table in Valve's
/// layout, built by the real registry and called through its block the way
/// vphysics calls it. The live gate found the registry refusing a one-slot
/// table ("at least its Release and Validate slots"), which no fact had
/// reached because none built these tables.
/// </summary>
public unsafe class JointCallbackTablesTests
{
    private sealed class Answers : IMotionAnswers
    {
        public (nint Controller, nint Object, float Dt) Asked;

        public int Simulate(nint controller, nint physicsObject, float deltaTime, out Vector linear, out Vector angular)
        {
            Asked = (controller, physicsObject, deltaTime);
            linear = new Vector(1f, 2f, 3f);
            angular = new Vector(4f, 5f, 6f);
            return 3;
        }
    }

    private static void* Slot0(ManagedVtable table) => (void*)(*(nint**)table.Pointer)[0];

    [Fact]
    public void TheConstraintEventTableIsBuiltAndItsSlotReachesTheTable()
    {
        using var vtables = new ManagedVtables();
        var crossings = new Crossings();
        nint heard = 0;
        var table = PhysicsConstraintEventBinding.Create(vtables, new PhysicsConstraintEventBinding(crossings, p => heard = p));

        Assert.Equal(1, table.Entries);
        ((delegate* unmanaged<void*, void*, void>)Slot0(table))(table.Pointer, (void*)0x4321);
        Assert.Equal((nint)0x4321, heard);
        Assert.Equal(1, crossings.Read(CrossingOp.ServerPhysicsConstraintBroken));
    }

    [Fact]
    public void TheMotionEventTableIsBuiltAndWritesItsAnswerWhereVphysicsReadsIt()
    {
        using var vtables = new ManagedVtables();
        var crossings = new Crossings();
        var answers = new Answers();
        var table = MotionEventBinding.Create(vtables, new MotionEventBinding(crossings, answers));

        Vector linear = default, angular = default;
        int result = ((delegate* unmanaged<void*, void*, void*, float, void*, void*, int>)Slot0(table))(
            table.Pointer, (void*)0x7001, (void*)0x2001, 0.015f, &linear, &angular);

        Assert.Equal(1, table.Entries);
        Assert.True(result == 3 && answers.Asked == ((nint)0x7001, (nint)0x2001, 0.015f));
        Assert.True(linear.Y == 2f && angular.Z == 6f, "the answer is written through vphysics' references");
        Assert.Equal(1, crossings.Read(CrossingOp.ServerPhysicsMotionSimulate));
    }

    [Fact]
    public void ANonValveTableOfOneSlotIsStillRefused()
    {
        using var vtables = new ManagedVtables();
        nint* slots = stackalloc nint[1];
        Assert.Throws<ArgumentException>(() =>
            vtables.Create("Probe", new object(), new ReadOnlySpan<nint>(slots, 1), [0u]));
    }
}
