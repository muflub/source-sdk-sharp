using SourceSharp.Abi;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_forwarders_everywhere.md phase 5: the object vphysics is handed as a
/// callback is libss_fwd's, built in the managed block's reserved words over
/// the block, so it lives and dies with the block and survives a reload's patch.
/// The library is faked (FakePhysicsCallbacks); fwd_physics_test drives the
/// real one as vphysics does.
/// </summary>
public unsafe class PhysicsCallbackObjectsTests
{
    private sealed class Constraints : IPhysicsConstraintEventSink
    {
        public void ConstraintBroken(void* pConstraint) { }
    }

    private static ManagedVtable Block(ManagedVtables vtables, string? instance = null)
        => IPhysicsConstraintEventSink__ManagedVtable.Create(vtables, new Constraints(), instance);

    [Fact]
    public void ACallbackTableReservesTheWordsTheObjectIsBuiltIn()
        => Assert.Equal(3, IPhysicsConstraintEventSink__ManagedVtable.ReservedWords);   // three since the trace filter holds its type (T4f)

    [Fact]
    public void TheObjectIsBuiltInTheBlocksReservedWordsOverTheBlock()
    {
        using var vtables = new ManagedVtables();
        using var callbacks = new FakePhysicsCallbacks();
        ManagedVtable block = Block(vtables);

        nint built = callbacks.Objects.ConstraintEvent(block);

        Assert.Equal((nint)block.Pointer + sizeof(nint), built);
        Assert.Equal((FakePhysicsCallbacks.ConstraintEvent, block.Address), FakePhysicsCallbacks.Built(built));
        // The block's own word 0 is still its table: the object sits after it.
        Assert.NotEqual(FakePhysicsCallbacks.ConstraintEvent, *(nint*)block.Pointer);
    }

    [Fact]
    public void EachKindBuildsItsOwnObject()
    {
        using var vtables = new ManagedVtables();
        using var callbacks = new FakePhysicsCallbacks();
        PhysicsCallbackObjects o = callbacks.Objects;

        Assert.Equal(FakePhysicsCallbacks.ObjectEvent, FakePhysicsCallbacks.Built(o.ObjectEvent(Block(vtables, "a"))).Kind);
        Assert.Equal(FakePhysicsCallbacks.CollisionEvent, FakePhysicsCallbacks.Built(o.CollisionEvent(Block(vtables, "b"))).Kind);
        Assert.Equal(FakePhysicsCallbacks.CollisionSolver, FakePhysicsCallbacks.Built(o.CollisionSolver(Block(vtables, "c"))).Kind);
        Assert.Equal(FakePhysicsCallbacks.ConstraintEvent, FakePhysicsCallbacks.Built(o.ConstraintEvent(Block(vtables, "d"))).Kind);
        Assert.Equal(FakePhysicsCallbacks.MotionEvent, FakePhysicsCallbacks.Built(o.MotionEvent(Block(vtables, "e"))).Kind);
        Assert.Equal(FakePhysicsCallbacks.KeyHandler, FakePhysicsCallbacks.Built(o.KeyHandler(Block(vtables, "f"))).Kind);
    }

    [Fact]
    public void ALibraryThatBuildsInMoreThanTheBlockReservesIsRefusedNamingBoth()
    {
        using var vtables = new ManagedVtables();
        using var callbacks = new FakePhysicsCallbacks(sizeOf: 32);

        var thrown = Assert.Throws<InvalidOperationException>(() => callbacks.Objects.ConstraintEvent(Block(vtables)));

        Assert.Contains("32 byte(s)", thrown.Message);
        Assert.Contains("reserves 24", thrown.Message);
    }

    [Fact]
    public void AReloadsPatchKeepsTheObjectWhereVPhysicsHoldsIt()
    {
        // The block a reload patches in place keeps its address and its
        // reserved words, so the object vphysics was handed is the same.
        using var vtables = new ManagedVtables();
        using var callbacks = new FakePhysicsCallbacks();
        ManagedVtable first = Block(vtables);
        nint built = callbacks.Objects.ConstraintEvent(first);

        vtables.Detach();
        ManagedVtable patched = Block(vtables);

        Assert.Equal(first.Address, patched.Address);
        Assert.Equal((FakePhysicsCallbacks.ConstraintEvent, first.Address), FakePhysicsCallbacks.Built(built));
        Assert.Equal(built, callbacks.Objects.ConstraintEvent(patched));
    }
}
