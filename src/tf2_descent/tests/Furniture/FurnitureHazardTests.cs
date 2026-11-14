using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Furniture;
using TF2.Descent.Server.Furniture;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Furniture;

/// <summary>
/// Explosives and traps in the world (plan_descent_logic WO3): an explosive's hits, its blast on
/// heroes at the rules' damage and on robots through TF2's blast credited to the hero who set it
/// off, its chain at the rules' delay, gone for good; a trap tripped, fired after its fuse at the
/// rules' damage, and re-armed.
/// </summary>
public sealed class FurnitureHazardTests
{
    private static (FurnitureRig Rig, FurnitureRow Row) Barrel()
    {
        var rig = new FurnitureRig(FurnitureRig.SeedWith((FurnitureKind.Explosive, "barrel")));
        return (rig, rig.First(FurnitureKind.Explosive, "barrel"));
    }

    private static ExplosiveRow RowOf(string kind) => FurnitureRig.Rules.Content.Furniture.Explosive(kind);

    //-------------------------------------------------------------------------
    // Explosives
    //-------------------------------------------------------------------------

    [Fact]
    public void A_barrel_stands_with_the_rules_health_as_its_engine_health()
    {
        var (_, row) = Barrel();

        Assert.True(row.Entity!.Breakable);
        Assert.Equal((int)RowOf("barrel").Health, row.Entity.EngineHealth);
    }

    [Fact]
    public void A_hit_below_its_health_leaves_a_barrel_standing_with_what_is_left()
    {
        var (rig, row) = Barrel();

        int left = rig.Service.HurtRow(row, EntityHandle.None, 15);

        Assert.Equal((int)RowOf("barrel").Health - 15, left);
        Assert.Null(row.ExplodeAt);
        Assert.False(row.Went);
    }

    [Fact]
    public void A_barrel_goes_off_on_the_next_think_after_the_hit_that_ends_its_health()
    {
        var (rig, row) = Barrel();
        var hero = rig.Stand(FurnitureRig.SlotA, row, new Vector(2000, 0, 0));

        rig.Service.HurtRow(row, hero.Player, 1000);
        bool wentAtOnce = row.Went;
        rig.Think();

        Assert.False(wentAtOnce);
        Assert.True(row.Went);
        Assert.Single(rig.World.Explosions);
        Assert.Contains(rig.World.Sounds, s => s.Name == FurnitureSounds.BarrelExplode);
    }

    [Fact]
    public void A_barrel_hurts_a_hero_by_the_rules_damage_at_his_distance_with_itself_as_attacker()
    {
        var (rig, row) = Barrel();
        var hero = rig.Stand(FurnitureRig.SlotA, row, new Vector(60, 0, -41 + FurnitureService.BlastLift));
        var barrel = row.Entity!.Handle;
        double expected = FurnitureRig.Rules.Furniture.Explosives.DamageAt("barrel", 60);

        rig.Service.HurtRow(row, hero.Player, 1000);
        rig.Think();

        var hit = Assert.Single(rig.World.Hits, h => h.Victim == hero.Player);
        Assert.Equal(expected, hit.Damage, 3);
        Assert.Equal(barrel, hit.Attacker);
        Assert.Equal(DamageType.Blast, hit.Bits);
        Assert.InRange(expected, 1, RowOf("barrel").Damage - 1);
    }

    [Fact]
    public void A_hero_beyond_a_barrels_radius_is_not_hurt()
    {
        var (rig, row) = Barrel();
        var hero = rig.Stand(FurnitureRig.SlotA, row, new Vector((float)RowOf("barrel").Radius + 5, 0, -41 + FurnitureService.BlastLift));

        rig.Service.HurtRow(row, hero.Player, 1000);
        rig.Think();

        Assert.DoesNotContain(rig.World.Hits, h => h.Victim == hero.Player);
    }

    [Fact]
    public void A_barrels_blast_reaches_robots_through_TF2_credited_to_the_hero_who_set_it_off()
    {
        var (rig, row) = Barrel();
        var hero = rig.Stand(FurnitureRig.SlotA, row, new Vector(2000, 0, 0));
        var barrel = row.Entity!.Handle;

        rig.Service.HurtRow(row, hero.Player, 1000);
        rig.Think();

        var blast = Assert.Single(rig.World.Blasts);
        Assert.Equal((hero.Player, barrel, hero.Player), (blast.Attacker, blast.Inflictor, blast.Ignored));
        Assert.Equal(((float)RowOf("barrel").Damage, (float)RowOf("barrel").Radius), (blast.Damage, blast.Radius));
        Assert.True(blast.ScopeActive);
    }

    /// <summary>
    /// TF2's blast traces from its centre to each victim passing only the hero it ignores: a barrel
    /// still standing would block its own blast (live, 2026-11-11: a runt 64 units off took nothing).
    /// </summary>
    [Fact]
    public void A_barrels_entity_is_parked_before_its_blast_is_dealt()
    {
        var (rig, row) = Barrel();
        var hero = rig.Stand(FurnitureRig.SlotA, row, new Vector(2000, 0, 0));

        rig.Service.HurtRow(row, hero.Player, 1000);
        rig.Think();

        Assert.True(Assert.Single(rig.World.Blasts).InflictorParked);
    }

    [Fact]
    public void A_barrel_set_off_by_no_hero_blasts_as_itself()
    {
        var (rig, row) = Barrel();
        var barrel = row.Entity!.Handle;

        rig.Service.HurtRow(row, EntityHandle.None, 1000);
        rig.Think();

        var blast = Assert.Single(rig.World.Blasts);
        Assert.Equal((barrel, barrel), (blast.Attacker, blast.Inflictor));
    }

    [Fact]
    public void An_entity_hit_from_a_hero_sets_the_barrel_off_in_his_name()
    {
        var (rig, row) = Barrel();
        var hero = rig.Stand(FurnitureRig.SlotA, row, new Vector(2000, 0, 0));

        int answered = row.Entity!.OnDamagedAt(hero.Player, 500, DamageType.Bullet, row.Position, default);

        Assert.Equal((int)RowOf("barrel").Health, answered);
        Assert.Equal(0, row.Entity.EngineHealth);
        Assert.Equal(hero.Player, row.SetOffBy);
    }

    [Fact]
    public void A_barrel_goes_off_once_however_often_it_is_hit()
    {
        var (rig, row) = Barrel();
        var hero = rig.Stand(FurnitureRig.SlotA, row, new Vector(2000, 0, 0));
        var entity = row.Entity!;

        entity.OnDamagedAt(hero.Player, 500, DamageType.Bullet, row.Position, default);
        entity.OnDamagedAt(hero.Player, 500, DamageType.Bullet, row.Position, default);
        rig.Think();
        rig.Service.HurtRow(row, hero.Player, 500);
        rig.Think(3);

        Assert.Single(rig.World.Explosions);
        Assert.Single(rig.World.Blasts);
    }

    [Fact]
    public void A_barrel_sets_off_the_explosives_in_its_chain_radius_after_the_rules_delay_in_the_same_heroes_name()
    {
        var (level, seed) = FurnitureRig.WithChain();
        var rig = new FurnitureRig(seed, level: level);
        var row = rig.Service.Rows.Single(r => r.Marker == level.Markers.Length - 2);
        var hero = rig.Stand(FurnitureRig.SlotA, row, new Vector(5000, 0, 0));
        double delay = RowOf("barrel").ChainDelay;
        var near = rig.Service.Rows.Where(r => r != row && r.Kind == FurnitureKind.Explosive
                                                 && FurnitureRig.Distance(r.Position, row.Position) <= RowOf("barrel").ChainRadius).ToList();
        var far = rig.Service.Rows.Where(r => r != row && r.Kind == FurnitureKind.Explosive && !near.Contains(r)).ToList();
        var schedule = FurnitureRig.Rules.Furniture.Explosives.Schedule(row.Placed.Marker,
            [.. rig.Service.Rows.Where(r => r.Kind == FurnitureKind.Explosive)
                .Select(r => new ExplosiveAt(r.Placed.Marker, r.Variant, new Point3(r.Position.X, r.Position.Y, r.Position.Z)))]);

        rig.Service.HurtRow(row, hero.Player, 1000);
        rig.Think();
        bool nearWentAtOnce = near.Any(r => r.Went);
        rig.World.Now += delay;
        rig.Think();

        Assert.NotEmpty(near);
        Assert.False(nearWentAtOnce);
        Assert.All(near, r => Assert.True(r.Went));
        Assert.All(near, r => Assert.Contains(schedule, s => s.Marker == r.Placed.Marker && Math.Abs(s.After - delay) < 1e-9));
        Assert.All(rig.World.Blasts, b => Assert.Equal(hero.Player, b.Attacker));
        Assert.All(far.Where(r => !schedule.Any(s => s.Marker == r.Placed.Marker)), r => Assert.False(r.Went));
    }

    [Fact]
    public void A_gone_barrel_stays_gone_across_sleep_and_wake()
    {
        var (rig, row) = Barrel();
        rig.Service.HurtRow(row, EntityHandle.None, 1000);
        rig.Think();

        rig.Rooms.Asleep.Add(row.Room);
        rig.Service.CheckRooms();
        rig.Rooms.Asleep.Clear();
        rig.Service.CheckRooms();

        Assert.True(row.Went);
        Assert.Null(row.Entity);
        Assert.Single(rig.Hazards.Gone, i => i == row.Index);
    }

    [Fact]
    public void The_blast_rule_refuses_a_hero_inside_the_TF2_pass_and_nowhere_else()
    {
        var rig = new FurnitureRig();
        var rule = new FurnitureBlastRule(rig.Mode, rig.Scope, rig.Engine);
        var hero = new EntityHandle(1);
        var robot = new EntityHandle((1 << 16) | 900);

        var outside = rule.Decide(new DamageContext(hero, new EntityHandle(2), 10, (int)DamageType.Blast));
        rig.Scope.Enter();
        var inside = rule.Decide(new DamageContext(hero, new EntityHandle(2), 10, (int)DamageType.Blast));
        var robotInside = rule.Decide(new DamageContext(robot, new EntityHandle(2), 10, (int)DamageType.Blast));
        rig.Scope.Leave();

        Assert.Equal((Decision.UseStockRule, Decision.No, Decision.UseStockRule), (outside, inside, robotInside));
    }

    //-------------------------------------------------------------------------
    // Traps
    //-------------------------------------------------------------------------

    private static (FurnitureRig Rig, FurnitureRow Row) Trap(string variant)
    {
        var rig = new FurnitureRig(FurnitureRig.SeedWith((FurnitureKind.Trap, variant)));
        return (rig, rig.First(FurnitureKind.Trap, variant));
    }

    [Fact]
    public void A_hero_on_a_trap_trips_it_and_its_tell_sounds()
    {
        var (rig, row) = Trap("dart");
        rig.Stand(FurnitureRig.SlotA, row, new Vector(10, 0, 0));

        rig.Think();

        Assert.Equal(TrapPhase.Tripped, row.Trap!.Phase);
        Assert.Contains(rig.World.Sounds, s => s.Name == FurnitureSounds.DartTell);
        Assert.Empty(rig.World.Hits);
        Assert.Equal(FurnitureFlags.Tripped, (FurnitureFlags)row.Entity!.State.Flags);
    }

    [Fact]
    public void A_tripped_trap_fires_the_rules_damage_at_the_hero_still_on_it_when_its_fuse_runs()
    {
        var (rig, row) = Trap("dart");
        var hero = rig.Stand(FurnitureRig.SlotA, row, new Vector(10, 0, 0));
        var fire = FurnitureRig.Rules.Furniture.Traps.Fire("dart");
        rig.Think();

        rig.World.Now += FurnitureRig.Rules.Content.Furniture.Trap("dart").Fuse;
        rig.Think();

        var hit = Assert.Single(rig.World.Hits);
        Assert.Equal((hero.Player, row.Entity!.Handle, (float)fire.Damage, DamageType.Bullet), (hit.Victim, hit.Attacker, hit.Damage, hit.Bits));
    }

    [Fact]
    public void A_hero_who_steps_off_before_the_fuse_runs_is_spared()
    {
        var (rig, row) = Trap("dart");
        rig.Stand(FurnitureRig.SlotA, row, new Vector(10, 0, 0));
        rig.Think();
        rig.Heroes.Move(FurnitureRig.SlotA, row.Position + new Vector(300, 0, 0));

        rig.World.Now += 5;
        rig.Think();

        Assert.Empty(rig.World.Hits);
        Assert.Equal(TrapPhase.Rearming, row.Trap!.Phase);
    }

    [Fact]
    public void A_trap_is_armed_again_after_the_rules_rearm_time()
    {
        var (rig, row) = Trap("dart");
        var trap = FurnitureRig.Rules.Content.Furniture.Trap("dart");
        rig.Stand(FurnitureRig.SlotA, row, new Vector(10, 0, 0));
        rig.Think();
        rig.World.Now += trap.Fuse;
        rig.Think();
        rig.Heroes.Move(FurnitureRig.SlotA, row.Position + new Vector(300, 0, 0));

        rig.World.Now += trap.Rearm - 0.01;
        rig.Think();
        var before = row.Trap!.Phase;
        rig.World.Now += 0.02;
        rig.Think();

        Assert.Equal((TrapPhase.Rearming, TrapPhase.Armed), (before, row.Trap.Phase));
    }

    [Fact]
    public void A_robot_on_a_trap_trips_it_and_takes_its_damage()
    {
        var (rig, row) = Trap("dart");
        var robot = new EntityHandle((1 << 16) | 950);
        rig.World.Robots[robot] = row.Position + new Vector(5, 0, 0);
        rig.Think();

        rig.World.Now += FurnitureRig.Rules.Content.Furniture.Trap("dart").Fuse;
        rig.Think();

        var hit = Assert.Single(rig.World.Hits);
        Assert.Equal(robot, hit.Victim);
    }

    [Fact]
    public void A_trap_of_a_sleeping_room_is_not_tripped()
    {
        var (rig, row) = Trap("dart");
        rig.Rooms.Asleep.Add(row.Room);
        rig.Service.CheckRooms();
        rig.Stand(FurnitureRig.SlotA, row, new Vector(10, 0, 0));

        rig.Think();

        Assert.Equal(TrapPhase.Armed, row.Trap!.Phase);
    }
}
