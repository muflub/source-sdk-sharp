using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Stock's CalculatePhysicsImpactDamage (physics_impact_damage.cpp), written
/// before the port. Every expected number is worked by hand from stock's
/// formula and tables, shown beside it.
/// </summary>
public class PhysicsImpactDamageTests
{
    private static readonly ImpactParties Props = new(false, false, false, false);

    // Side 0 is "self" (the entity taking damage), side 1 the other.
    private static PhysicsEvent Hit(float selfMass, float otherMass, Vector otherPre,
                                    Vector selfPre = default, uint otherFlags = 0, bool otherStatic = false)
    {
        var ev = new PhysicsEvent
        {
            KindRaw = (uint)PhysicsEventKind.PostCollision,
            Entity0 = 3, Entity1 = 4,
            Mass0 = selfMass, Mass1 = otherMass,
            GameFlags1 = otherFlags,
            Static1 = otherStatic ? 1 : 0,
            Flags = PhysicsEvent.FlagMoveable0 | (otherStatic ? 0u : PhysicsEvent.FlagMoveable1),
            PreVelocity0X = selfPre.X, PreVelocity0Y = selfPre.Y, PreVelocity0Z = selfPre.Z,
            PreVelocity1X = otherPre.X, PreVelocity1Y = otherPre.Y, PreVelocity1Z = otherPre.Z,
        };
        return ev;
    }

    private static float Npc(in PhysicsEvent ev, float energy = 1f, bool allowStatic = false, ImpactParties? parties = null)
        => PhysicsImpactDamage.Calculate(in ev, 0, ImpactDamageTables.Npc, energy, allowStatic, false,
                                         parties ?? Props, out _);

    [Fact]
    public void A_fifty_kilo_crate_at_400_into_ten_kilos_does_100()
    {
        // other: (400-0)^2 * 50 = 8,000,000; self 0. x invMass 0.1 = 800,000.
        // NPC linear: 500^2=250,000 -> 100 is the last step under 1,000,000.
        var ev = Hit(10f, 50f, new Vector(400f, 0f, 0f));
        Assert.Equal(100f, Npc(in ev));
    }

    [Fact]
    public void A_walking_player_stopped_by_a_crate_dents_it_and_does_not_break_it()
    {
        // The gate's push, at stock's numbers: an 85 kg player shadow at 190 u/s
        // brought to a dead stop by a resting 30 kg wood crate (prop_data
        // health 30). other: 190^2 * 85 = 3,068,500; x invMass 1/30 = 102,283.
        // NPC linear: 250^2 = 62,500 -> 10 is the last step under it. So stock
        // dents the crate and never breaks it; a crate that VANISHED on a
        // walk-in (gate, ss-debugapi 05:43) had the 0 health A43 fixed.
        var ev = Hit(30f, 85f, new Vector(190f, 0f, 0f));

        float damage = Npc(in ev);

        Assert.Equal(10f, damage);
        Assert.True(damage < 30f, "a wood crate's prop_data health outlasts a walk-in");
    }

    [Fact]
    public void One_entity_never_damages_itself()
    {
        var ev = Hit(10f, 50f, new Vector(400f, 0f, 0f));
        ev.Entity1 = ev.Entity0;
        Assert.Equal(0f, Npc(in ev));
    }

    [Theory]
    [InlineData(PhysicsGameFlags.NoImpactDamage)]
    [InlineData(PhysicsGameFlags.ConstraintStatic)]
    public void An_object_flagged_no_damage_does_none(uint flag)
    {
        var ev = Hit(10f, 50f, new Vector(400f, 0f, 0f), otherFlags: flag);
        Assert.Equal(0f, Npc(in ev));
    }

    [Fact]
    public void No_energy_scale_no_damage()
    {
        var ev = Hit(10f, 50f, new Vector(400f, 0f, 0f));
        Assert.Equal(0f, Npc(in ev, energy: 0f));
    }

    [Fact]
    public void A_dissolving_object_always_does_1000_dissolve()
    {
        var ev = Hit(10f, 50f, Vector.Zero, otherFlags: PhysicsGameFlags.DissolveDamage);
        float damage = PhysicsImpactDamage.Calculate(in ev, 0, ImpactDamageTables.Npc, 1f, false, false, Props, out var type);
        Assert.Equal(1000f, damage);
        Assert.True((type & DamageType.Dissolve) != 0 && (type & DamageType.Crush) != 0);
    }

    [Fact]
    public void A_small_object_is_capped_at_five()
    {
        // 4 kg at 400: 160,000*4 = 640,000 x 0.1 = 64,000 -> 10 (250^2 step);
        // under the 5 kg small mass and not static, so clamped to 5.
        var ev = Hit(10f, 4f, new Vector(400f, 0f, 0f));
        Assert.Equal(5f, Npc(in ev));
    }

    [Fact]
    public void A_small_slow_object_does_nothing()
    {
        var ev = Hit(10f, 4f, new Vector(30f, 0f, 0f));   // under 36 in/s
        Assert.Equal(0f, Npc(in ev));
    }

    [Fact]
    public void Under_two_kilos_does_nothing()
    {
        var ev = Hit(10f, 1f, new Vector(400f, 0f, 0f));
        Assert.Equal(0f, Npc(in ev));
    }

    [Fact]
    public void Static_damage_counts_my_own_lost_speed_against_the_world()
    {
        // self 10 kg 300 -> 0: 90,000*10 = 900,000 x 0.1 = 90,000 -> 10.
        // The world is 0 kg, so without allowStatic the min-mass gate refuses.
        var ev = Hit(10f, 0f, Vector.Zero, selfPre: new Vector(300f, 0f, 0f), otherStatic: true);
        Assert.Equal(10f, Npc(in ev, allowStatic: true));
        Assert.Equal(0f, Npc(in ev, allowStatic: false));
    }

    [Fact]
    public void A_large_mass_falling_counts_five_times_over()
    {
        // 600 kg, 300 down -> 0: 90,000*600 = 54e6 x4 (large) = 216e6; falling
        // factor 1 -> x5 = 1.08e9; self 10,000 kg: x1e-4 = 108,000 -> 10.
        var falling = Hit(10000f, 600f, new Vector(0f, 0f, -300f));
        Assert.Equal(10f, Npc(in falling));

        // Sideways: no falling factor: 216e6 x 1e-4 = 21,600 -> under 150^2 -> 0.
        var sideways = Hit(10000f, 600f, new Vector(300f, 0f, 0f));
        Assert.Equal(0f, Npc(in sideways));
    }

    [Fact]
    public void A_heavy_object_counts_as_the_large_mass_at_double_energy()
    {
        // HEAVY: other mass = 500, energy >= 2. 100^2*500 = 5e6 x4 = 2e7;
        // self 100 kg: x0.01 x2 = 400,000 -> 100.
        var heavy = Hit(100f, 10f, new Vector(100f, 0f, 0f), otherFlags: PhysicsGameFlags.HeavyObject);
        Assert.Equal(100f, Npc(in heavy));

        var plain = Hit(100f, 10f, new Vector(100f, 0f, 0f));
        Assert.Equal(0f, Npc(in plain));   // 1e4*10 x0.01 = 1,000 -> 0
    }

    [Fact]
    public void The_player_table_filters_my_own_small_speed_losses()
    {
        // myMinVelocity 320: a loss of 300 is filtered; 400 is not:
        // 160,000*80 = 12.8e6 x (1/80) = 160,000 -> player 250^2 step = 10.
        var slow = Hit(80f, 0f, Vector.Zero, selfPre: new Vector(300f, 0f, 0f), otherStatic: true);
        var fast = Hit(80f, 0f, Vector.Zero, selfPre: new Vector(400f, 0f, 0f), otherStatic: true);
        Assert.Equal(0f, PhysicsImpactDamage.Calculate(in slow, 0, ImpactDamageTables.Player, 1f, true, false, Props, out _));
        Assert.Equal(10f, PhysicsImpactDamage.Calculate(in fast, 0, ImpactDamageTables.Player, 1f, true, false, Props, out _));
    }

    [Fact]
    public void A_spinning_sharp_object_slashes()
    {
        // SLICE, spinning 400 deg/s about z (> 360^2), inertia 100 per axis:
        // |I . w| = 40,000 -> NPC angular 200^2 step = 50, and DMG_SLASH.
        var ev = Hit(10f, 50f, Vector.Zero, otherFlags: PhysicsGameFlags.SliceDamage);
        ev.PreAngularVelocity1Z = 400f;
        ev.Inertia1X = ev.Inertia1Y = ev.Inertia1Z = 100f;
        float damage = PhysicsImpactDamage.Calculate(in ev, 0, ImpactDamageTables.Npc, 1f, false, false, Props, out var type);
        Assert.Equal(50f, damage);
        Assert.True((type & DamageType.Slash) != 0);
    }

    [Fact]
    public void A_player_cannot_damage_what_he_is_holding()
    {
        var ev = Hit(10f, 50f, new Vector(400f, 0f, 0f));
        ev.GameFlags0 = PhysicsGameFlags.PlayerHeld;
        Assert.Equal(0f, Npc(in ev, parties: new ImpactParties(false, true, false, false)));
    }

    [Fact]
    public void A_no_npc_damage_object_spares_players()
    {
        var ev = Hit(10f, 50f, new Vector(400f, 0f, 0f), otherFlags: PhysicsGameFlags.NoNpcImpactDamage);
        Assert.Equal(0f, Npc(in ev, parties: new ImpactParties(true, false, false, false)));
        Assert.Equal(100f, Npc(in ev));   // a prop is still hurt
    }

    [Fact]
    public void A_floating_entity_takes_three_times_the_energy_unless_the_world_hit_it()
    {
        // 800,000 x3 = 2.4e6 -> 500.
        var ev = Hit(10f, 50f, new Vector(400f, 0f, 0f));
        Assert.Equal(500f, Npc(in ev, parties: new ImpactParties(false, false, false, true)));
        Assert.Equal(100f, Npc(in ev, parties: new ImpactParties(false, false, true, true)));
    }

    [Theory]
    [InlineData(200000f, 75f)]    // 200^2 <= x < 500^2
    [InlineData(300000f, 500f)]   // past 500^2 the out-of-order 250^2 step answers
    [InlineData(600f, 0f)]        // under 25^2
    public void The_glass_table_reads_as_stock_wrote_it(float impulse, float damage)
    {
        Assert.Equal(damage, PhysicsImpactDamage.ReadTable(ImpactDamageTables.Glass.Linear, impulse));
    }
}
