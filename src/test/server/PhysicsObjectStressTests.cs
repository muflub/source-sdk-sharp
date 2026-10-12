using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// PhysicsObjectStress against stock's CalculateObjectStress
/// (physics_impact_damage.cpp:504): the cases of native object_stress_test.cpp,
/// which it replaces.
/// </summary>
public class PhysicsObjectStressTests
{
    private const float Gravity = 600f;

    private static StressSubject PlayerShadow(float mass = 85f, bool vphysics = false, bool moveable = true,
                                              bool isStatic = false, float gravity = Gravity,
                                              float timestep = 1f / 66f, float impulseZ = 0f)
        => new(mass, moveable, isStatic, vphysics, new Vector(0f, 0f, impulseZ), gravity, timestep);

    private static StressContact Floor(float force)
        => new(force, new Vector(0f, 0f, 1f), 0, Moveable: false, Friendly: false, PhysicallyControlled: false,
               IsOwnersGround: false, Mass: 0f);

    private static StressContact Pusher(nint key, float force, float nz, float mass = 50f, bool controlled = true,
                                        bool friendly = false, bool ground = false)
        => new(force, new Vector(0f, 0f, nz), key, Moveable: true, friendly, controlled, ground, mass);

    private static ObjectStress Stress(StressSubject s, params StressContact[] c) => PhysicsObjectStress.Compute(s, c);

    private static void Near(float expected, float actual) => Assert.Equal(expected, actual, 3);

    [Fact]
    public void Standing_on_the_floor_is_exerted_not_received()
    {
        var o = Stress(PlayerShadow(), Floor(85f * Gravity));
        Near(0f, o.ReceivedStress);
        Near(85f, o.ExertedStress);
        Assert.False(o.HasNonStaticStress);
    }

    [Fact]
    public void A_crate_on_top_is_two_way_force_divided_by_gravity()
    {
        var o = Stress(PlayerShadow(), Floor(1200f), Pusher(7, 600f, -1f));
        Near(600f / Gravity, o.ReceivedStress);
        Near(600f / Gravity, o.ExertedStress);
        Assert.True(o.HasNonStaticStress);
    }

    [Fact]
    public void A_shadow_does_not_feel_another_shadow()
    {
        var o = Stress(PlayerShadow(), Floor(1200f), Pusher(7, 600f, -1f, controlled: false));
        Near(0f, o.ReceivedStress);
        Assert.True(o.HasNonStaticStress, "the pusher still has its slot");
    }

    [Fact]
    public void A_vphysics_owner_feels_every_pusher()
        => Near(600f / Gravity, Stress(PlayerShadow(vphysics: true), Floor(1200f),
                                       Pusher(7, 600f, -1f, controlled: false)).ReceivedStress);

    [Fact]
    public void Friendly_force_is_cleared()
    {
        var o = Stress(PlayerShadow(), Floor(1200f), Pusher(7, 600f, -1f, friendly: true));
        Near(0f, o.ReceivedStress);
        Assert.False(o.HasNonStaticStress);
        Near(1200f / Gravity, o.ExertedStress);
    }

    [Fact]
    public void Contacts_with_no_force_are_skipped()
    {
        var o = Stress(PlayerShadow(), Floor(1200f), Pusher(7, 0f, -1f), Pusher(8, -5f, -1f));
        Assert.False(o.HasNonStaticStress);
        Near(1200f / Gravity, o.ExertedStress);
    }

    [Fact]
    public void Contacts_aggregate_per_pusher()
    {
        var subject = PlayerShadow(1000f);
        Near(0f, Stress(subject, Pusher(7, 600f, -1f), Pusher(7, 600f, 1f)).Sum);

        var two = Stress(subject, Pusher(7, 600f, -1f), Pusher(8, 600f, 1f));
        Near(1200f / Gravity, two.Sum);
        Near(600f / Gravity, two.ReceivedStress);
    }

    [Fact]
    public void A_large_object_counts_unless_it_is_the_ground()
    {
        Assert.True(Stress(PlayerShadow(), Pusher(7, 600f, -1f, 500f)).HasLargeObjectContact);
        Assert.False(Stress(PlayerShadow(), Pusher(7, 600f, -1f, 500f, ground: true)).HasLargeObjectContact);
        Assert.False(Stress(PlayerShadow(), Pusher(7, 600f, -1f, 499f)).HasLargeObjectContact);
    }

    [Fact]
    public void A_friendly_large_object_is_not_large()
    {
        Assert.False(Stress(PlayerShadow(), Pusher(7, 600f, -1f, 900f, friendly: true)).HasLargeObjectContact);
        Assert.True(Stress(PlayerShadow(), Pusher(7, 600f, -1f, 900f)).HasLargeObjectContact);
    }

    [Fact]
    public void An_unmoveable_object_feels_all_of_it_as_received()
    {
        var o = Stress(PlayerShadow(40f, moveable: false), Floor(1200f));
        Near(0f, o.ExertedStress);
        Near(1200f / Gravity, o.ReceivedStress);
        Near(1200f / Gravity + 40f, o.Sum);
        Near(1200f / Gravity, Stress(PlayerShadow(40f, moveable: false, isStatic: true), Floor(1200f)).Sum);
    }

    [Fact]
    public void A_moveable_object_at_rest_halves_the_excess_sum()
        => Near(1f + (3f - 1f) * 0.5f, Stress(PlayerShadow(1f, vphysics: true), Floor(3f * Gravity)).Sum);

    [Fact]
    public void The_controllers_impulse_is_redistributed()
    {
        // mass 10, impulse 1 per 0.1 s step: input 100. Internal 50 < 100, so
        // half again is added (delta 25) and sum loses 25: 25, over the mass of
        // 10, so the at-rest clamp makes it 10 + 15 / 2.
        var o = Stress(PlayerShadow(10f, gravity: 1f, timestep: 0.1f, impulseZ: 1f), Floor(50f));
        Near(75f, o.ExertedStress);
        Near(17.5f, o.Sum);
        Near(0f, o.ReceivedStress);
    }

    [Fact]
    public void The_redistribution_takes_it_from_the_received_stress()
    {
        var o = Stress(PlayerShadow(10f, gravity: 1f, timestep: 0.1f, impulseZ: 1f), Floor(150f), Pusher(7, 100f, -1f));
        Near(75f, o.ExertedStress);
        Near(75f, o.ReceivedStress);
    }

    [Fact]
    public void A_vphysics_owner_ignores_the_impulse()
        => Near(50f, Stress(PlayerShadow(10f, vphysics: true, gravity: 1f, timestep: 0.1f, impulseZ: 1f),
                            Floor(50f)).ExertedStress);
}
