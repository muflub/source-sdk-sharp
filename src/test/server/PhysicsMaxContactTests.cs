using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// PhysicsMaxContact against stock's FindMaxContact (physics.cpp:628): the
/// cases of native find_max_contact_test.cpp, which it replaces.
/// </summary>
public class PhysicsMaxContactTests
{
    private const nint Crate = 0x100, Feather = 0x200, Pinned = 0x300, Strong = 0x400, Weak = 0x500;

    private static FrictionContact Push(nint other, float otherMass, float force, float px, float nz,
                                        bool moveable = true)
        => new(other, moveable, otherMass, force, new Vector(px, 0f, 0f), new Vector(0f, 0f, nz));

    [Fact]
    public void A_heavier_moveable_object_pressing_past_the_threshold_is_found()
    {
        var found = PhysicsMaxContact.Find(2f, 2f * 10, [Push(Crate, 40f, 30f, 7f, 1f)]);

        Assert.NotNull(found);
        Assert.Equal(Crate, found.Value.Other);
        Assert.Equal(7f, found.Value.Position.X);
        Assert.Equal(30f, found.Value.Force.Z);   // the surface normal scaled by the normal force
    }

    [Fact]
    public void Under_the_threshold_nothing_is_found()
        => Assert.Null(PhysicsMaxContact.Find(2f, 2f * 10, [Push(Crate, 40f, 20f, 0f, 1f)]));

    [Fact]
    public void A_lighter_or_pinned_object_does_not_count()
        => Assert.Null(PhysicsMaxContact.Find(2f, 2f * 10,
            [Push(Feather, 1f, 500f, 0f, 1f), Push(Pinned, 90f, 500f, 0f, 1f, moveable: false)]));

    [Fact]
    public void Of_two_over_the_threshold_the_last_walked_wins_as_in_stock()
    {
        var found = PhysicsMaxContact.Find(1f, 1f * 10,
            [Push(Strong, 50f, 900f, 1f, 1f), Push(Weak, 50f, 15f, 2f, 1f)]);

        Assert.Equal(Weak, found!.Value.Other);
    }

    [Fact]
    public void An_equal_mass_does_not_count()
        => Assert.Null(PhysicsMaxContact.Find(40f, 10f, [Push(Crate, 40f, 500f, 0f, 1f)]));
}
