using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The joint ids (plan_physics_bones_native_zero 2.4), held to the native id
/// table's rules for the three kinds it served (physics_shell.h, carried by
/// physics_ids_test): a serial that makes a stale id answer nothing, kinds
/// that never answer for each other, a freed slot reused first, and a reload
/// that puts an entry back under its own id.
/// </summary>
public class PhysicsJointTableTests
{
    private sealed class Entry;

    [Fact]
    public void AnIdAnswersItsEntryUnderItsOwnKindOnly()
    {
        var table = new PhysicsJointTable();
        var entry = new Entry();
        uint id = table.Bind(PhysicsJointKind.Constraint, entry);

        Assert.Same(entry, table.Lookup<Entry>(id, PhysicsJointKind.Constraint));
        Assert.Null(table.Lookup<Entry>(id, PhysicsJointKind.Spring));
    }

    [Fact]
    public void AFreedIdIsStaleEvenWhenItsSlotIsReused()
    {
        var table = new PhysicsJointTable();
        uint first = table.Bind(PhysicsJointKind.Controller, new Entry());
        Assert.True(table.Free(first, PhysicsJointKind.Controller));

        uint second = table.Bind(PhysicsJointKind.Controller, new Entry());

        Assert.Equal(new PhysicsId(first).Index, new PhysicsId(second).Index);
        Assert.NotEqual(first, second);
        Assert.Null(table.Lookup<Entry>(first, PhysicsJointKind.Controller));
        Assert.False(table.Free(first, PhysicsJointKind.Controller));
    }

    [Fact]
    public void ZeroIsNeverAnId()
    {
        var table = new PhysicsJointTable();
        Assert.NotEqual(0u, table.Bind(PhysicsJointKind.Spring, new Entry()));
        Assert.Equal(0u, table.Bind(PhysicsJointKind.None, new Entry()));
        Assert.Null(table.Lookup<Entry>(0u, PhysicsJointKind.Spring));
    }

    [Fact]
    public void AFullTableRefusesAndCountsTheRefusal()
    {
        var table = new PhysicsJointTable();
        for (int i = 0; i < PhysicsId.MaxSlots; i++)
            Assert.NotEqual(0u, table.Bind(PhysicsJointKind.Spring, new Entry()));

        Assert.Equal(0u, table.Bind(PhysicsJointKind.Spring, new Entry()));
        Assert.Equal(1, table.Refused);
    }

    [Fact]
    public void CountsAndFreeAllAreByKind()
    {
        var table = new PhysicsJointTable();
        table.Bind(PhysicsJointKind.Constraint, new Entry());
        table.Bind(PhysicsJointKind.Constraint, new Entry());
        uint spring = table.Bind(PhysicsJointKind.Spring, new Entry());

        Assert.Equal(2, table.FreeAll(PhysicsJointKind.Constraint));
        Assert.True(table.CountOf(PhysicsJointKind.Constraint) == 0 && table.CountOf(PhysicsJointKind.Spring) == 1);
        Assert.NotNull(table.Lookup<Entry>(spring, PhysicsJointKind.Spring));
    }

    [Fact]
    public void ARestorePutsAnEntryBackUnderItsIdAndTheSlotIsNotHandedOutAgain()
    {
        var before = new PhysicsJointTable();
        before.Bind(PhysicsJointKind.Constraint, new Entry());
        uint id = before.Bind(PhysicsJointKind.Constraint, new Entry());

        var after = new PhysicsJointTable();
        var entry = new Entry();
        Assert.True(after.Restore(id, PhysicsJointKind.Constraint, entry));
        Assert.Same(entry, after.Lookup<Entry>(id, PhysicsJointKind.Constraint));
        Assert.False(after.Restore(id, PhysicsJointKind.Constraint, new Entry()));

        uint fresh = after.Bind(PhysicsJointKind.Constraint, new Entry());
        Assert.NotEqual(new PhysicsId(id).Index, new PhysicsId(fresh).Index);
    }
}
