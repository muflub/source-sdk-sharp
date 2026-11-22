using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Shared.Bones;

using TF2.Descent.Rules.Furniture;
using TF2.Descent.Server.Furniture;
using TF2.Descent.Shared;

using Xunit;

using NVector = System.Numerics.Vector3;

namespace TF2.Descent.Tests.Furniture;

/// <summary>Makes the breakables' entity through the real registry over the rig's recording engine.</summary>
internal sealed class RigBreakablesSpawner(ScriptedEntityRegistry registry) : IBreakablesSpawner
{
    public int Made { get; private set; }

    public BreakablesEntity? Make()
    {
        Made++;
        var h = new EntityHandle((uint)((1 << 16) | (900 + Made)));
        registry.Spawn(h, BreakablesNetState.ClassName);
        return registry.Get(h) as BreakablesEntity;
    }
}

/// <summary>
/// The one entity that owns a level's crates and barrels (the user, 2026-11-15): one entity for all,
/// its networked list, the trace it answers, a hit and TF2's blast on it, and its transmit rule.
/// </summary>
public sealed class FurnitureBreakablesTests
{
    private sealed class Rig
    {
        public Rig(ulong seed = 1)
        {
            Furniture = new FurnitureRig(seed, level: FurnitureRig.D1S1Breakables);
            Spawner = new RigBreakablesSpawner(Furniture.Registry);
            Breakables = new FurnitureBreakables(Furniture.Service, Furniture.Heroes, Spawner, Furniture.World, Furniture.Log);
            Breakables.OnLevelReady(Furniture.Level.Current!.Map);
        }

        public FurnitureRig Furniture { get; }
        public RigBreakablesSpawner Spawner { get; }
        public FurnitureBreakables Breakables { get; }
        public BreakablesEntity Entity => Breakables.Entity!;
    }

    private static NVector Middle(BreakableSpot p)
    {
        var (lo, hi) = BreakableGeometry.WorldBox(p);
        return (lo + hi) * 0.5f;
    }

    [Fact]
    public void One_entity_holds_every_crate_and_barrel_of_the_level()
    {
        var rig = new Rig();
        var rows = rig.Furniture.Service.Rows.Where(r => r.Kind == FurnitureKind.Breakable).ToList();

        Assert.NotEmpty(rows);
        Assert.Equal(1, rig.Spawner.Made);
        Assert.Equal(rows, rig.Breakables.Rows);
        Assert.Equal(rows.Count, rig.Entity.State.Count);
        Assert.All(rig.Breakables.Props, p =>
        {
            var row = rows[p.Slot];
            Assert.Equal((BreakableGeometry.Coord(row.Position.X), BreakableGeometry.Coord(row.Position.Y), BreakableGeometry.Coord(row.Position.Z)), (rig.Entity.State.X[p.Slot], rig.Entity.State.Y[p.Slot], rig.Entity.State.Z[p.Slot]));
            Assert.Equal(new NVector(rig.Entity.State.X[p.Slot], rig.Entity.State.Y[p.Slot], rig.Entity.State.Z[p.Slot]), p.Foot);
            Assert.Equal(FurnitureCatalog.Number(FurnitureKindId.Breakable, row.Variant), rig.Entity.State.Entry[p.Slot]);
        });
    }

    [Fact]
    public void Each_crate_and_barrel_is_numbered_by_its_slot_for_the_host()
    {
        var rig = new Rig();

        Assert.All(rig.Breakables.Props, p => Assert.Equal(p.Slot, rig.Breakables.Rows[p.Slot].BreakableSlot));
        Assert.True(rig.Breakables.Props.Count > 1, "the stimulus: more than one slot to number");
    }

    [Fact]
    public void The_entitys_state_fits_the_largest_blob_tier()
    {
        // The shells' largest standalone tier is 2048 bytes, the engine block included
        // (generic_nettable.h); a state past it is published with no table and refused at spawn.
        var bytes = (int)typeof(BreakablesNetState).GetProperty("StateBytes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(new BreakablesNetState())!;

        Assert.InRange(bytes, BreakablesNetState.EngineBlockBytes + 1, 2048);
    }

    [Fact]
    public void A_level_with_no_crate_or_barrel_makes_no_entity()
    {
        var rig = new Rig();
        var empty = new FurnitureBreakables(rig.Furniture.Service, rig.Furniture.Heroes, rig.Spawner, rig.Furniture.World, rig.Furniture.Log);
        rig.Furniture.Service.OnLevelShutdownPreEntity();

        empty.OnLevelReady("nothing");

        Assert.Null(empty.Entity);
        Assert.Equal(1, rig.Spawner.Made);
    }

    [Fact]
    public void The_entitys_box_holds_every_props_box()
    {
        var rig = new Rig();
        var (mins, maxs) = rig.Breakables.Bounds;

        Assert.All(rig.Breakables.Props, p =>
        {
            var (lo, hi) = BreakableGeometry.WorldBox(p);
            Assert.True(lo.X >= mins.X && lo.Y >= mins.Y && lo.Z >= mins.Z && hi.X <= maxs.X && hi.Y <= maxs.Y && hi.Z <= maxs.Z);
        });
    }

    [Fact]
    public void A_ray_into_a_prop_stops_at_it_and_names_its_slot()
    {
        var rig = new Rig();
        var prop = rig.Breakables.Props[0];
        var mid = Middle(prop);
        var tr = new BoneTrace();

        bool tested = rig.Entity.TestTrace(BoneRay.FromPoints(mid + new NVector(0, 0, 200), mid), BreakableGeometry.ContentsSolid, tr);

        Assert.True(tested);
        Assert.True(tr.Fraction < 1f);
        Assert.Equal(prop.Slot, tr.HitBox);
        Assert.Equal(BreakableGeometry.ContentsSolid, tr.Contents);
        Assert.Equal(new NVector(0, 0, 1), tr.PlaneNormal);
    }

    [Fact]
    public void A_hit_on_the_entity_breaks_the_prop_it_landed_on_and_a_ray_then_passes_through()
    {
        var rig = new Rig();
        var prop = rig.Breakables.Props[0];
        var mid = Middle(prop);
        var top = BreakableGeometry.WorldBox(prop).Maxs with { X = mid.X, Y = mid.Y };
        var hero = rig.Furniture.Stand(FurnitureRig.SlotA, rig.Breakables.Rows[0]);

        int answer = rig.Entity.OnDamagedAt(hero.Player, 20f, DamageType.Bullet, new Vector(top.X, top.Y, top.Z), default);
        var tr = new BoneTrace();
        rig.Entity.TestTrace(BoneRay.FromPoints(mid + new NVector(0, 0, 200), mid), BreakableGeometry.ContentsSolid, tr);

        Assert.Equal(0, answer);
        Assert.True(rig.Entity.IsBroken(prop.Slot));
        Assert.True(rig.Breakables.Rows[0].Gone);
        Assert.Equal(hero.Player, rig.Breakables.Rows[0].BrokenBy);
        Assert.Equal(rig.Entity.Handle, rig.Breakables.Rows[0].BreakSource);
        Assert.NotEqual(prop.Slot, tr.HitBox);
    }

    [Fact]
    public void A_swing_whose_hull_stopped_clear_of_the_box_breaks_the_prop_its_trace_hit()
    {
        // A melee hull stops with its centre half a hull from the box: the hit lands at the trace's
        // end, too far from the box to be its by distance, and is the traced prop's.
        var rig = new Rig();
        var prop = rig.Breakables.Props[0];
        var (lo, hi) = BreakableGeometry.WorldBox(prop);
        var mid = (lo + hi) * 0.5f;
        var swing = BoneRay.FromBox(mid + new NVector(0, 0, 120), mid, new NVector(-18, -18, -18), new NVector(18, 18, 18));
        var tr = new BoneTrace();
        rig.Entity.TestTrace(swing, BreakableGeometry.ContentsSolid, tr);

        rig.Entity.OnDamagedAt(EntityHandle.None, 65f, DamageType.Club, new Vector(tr.EndPos.X, tr.EndPos.Y, tr.EndPos.Z), default);

        Assert.True(tr.EndPos.Z - hi.Z > FurnitureBreakables.HitSlack);
        Assert.True(rig.Entity.IsBroken(prop.Slot));
    }

    [Fact]
    public void A_hit_far_from_every_prop_breaks_nothing()
    {
        var rig = new Rig();

        int slot = rig.Breakables.HitAt(new NVector(-99999, -99999, -99999), EntityHandle.None, 50f);

        Assert.Equal(-1, slot);
        Assert.All(rig.Breakables.Props, p => Assert.False(rig.Entity.IsBroken(p.Slot)));
    }

    [Fact]
    public void A_blast_breaks_each_prop_in_its_radius_it_sees_and_none_beyond()
    {
        var rig = new Rig();
        var prop = rig.Breakables.Props[0];
        var source = Middle(prop) + new NVector(0, 0, 100);
        var far = rig.Breakables.Props.Where(p => NVector.Distance(Middle(p), source) > 400).ToList();

        int broke = rig.Breakables.Blast(source, 146f, 90f, EntityHandle.None);

        Assert.True(broke >= 1);
        Assert.True(rig.Entity.IsBroken(prop.Slot));
        Assert.All(far, p => Assert.False(rig.Entity.IsBroken(p.Slot)));
    }

    [Fact]
    public void A_blast_behind_a_wall_breaks_nothing()
    {
        var rig = new Rig();
        rig.Furniture.World.Blocked = true;

        int broke = rig.Breakables.Blast(Middle(rig.Breakables.Props[0]), 146f, 90f, EntityHandle.None);

        Assert.Equal(0, broke);
    }

    [Fact]
    public void TF2s_blast_handed_to_the_entity_breaks_the_props_it_reaches()
    {
        var rig = new Rig();
        var prop = rig.Breakables.Props[0];
        var mid = Middle(prop);

        rig.Entity.TakeBlast(new Vector(mid.X, mid.Y, mid.Z + 50), TF2.Server.Projectiles.TFBlast.Of(90f, 146f, DamageType.Blast), EntityHandle.None, EntityHandle.None);

        Assert.True(rig.Entity.IsBroken(prop.Slot));
    }

    [Fact]
    public void A_hits_force_on_the_entity_is_the_struck_props_push()
    {
        var rig = new Rig();
        var prop = rig.Breakables.Props[0];
        var top = BreakableGeometry.WorldBox(prop).Maxs with { X = Middle(prop).X, Y = Middle(prop).Y };

        rig.Entity.OnDamagedAt(EntityHandle.None, 65f, DamageType.Club, new Vector(top.X, top.Y, top.Z), new Vector(19500f, 0f, 0f));

        Assert.Equal(new Vector(19500f, 0f, 0f), rig.Breakables.Rows[0].BreakForce);
    }

    [Fact]
    public void A_blasts_push_on_a_prop_is_stocks_explosive_force_away_from_the_blast()
    {
        // CalculateExplosiveDamageForce: along the line from the blast to the prop, 75 kg at 4 in/s
        // (300) per point of damage.
        var rig = new Rig();
        var mid = Middle(rig.Breakables.Props[0]);

        rig.Breakables.Blast(mid + new NVector(0, 0, 50), 146f, 90f, EntityHandle.None);

        var force = rig.Breakables.Rows[0].BreakForce;
        Assert.Equal(0f, force.X, 3);
        Assert.Equal(0f, force.Y, 3);
        Assert.Equal(-90f * 300f, force.Z, 1);
    }

    [Fact]
    public void A_big_blasts_push_is_held_at_stocks_ceiling()
    {
        // ImpulseScale(75, 400): no blast pushes harder than a 75 kg man at 400 in/s.
        var rig = new Rig();
        var mid = Middle(rig.Breakables.Props[0]);

        rig.Breakables.Blast(mid + new NVector(0, 0, 50), 146f, 500f, EntityHandle.None);

        Assert.Equal(-30000f, rig.Breakables.Rows[0].BreakForce.Z, 1);
    }

    [Fact]
    public void A_break_through_the_row_sets_the_slots_bit_at_the_next_think()
    {
        var rig = new Rig();
        var row = rig.Breakables.Rows[0];
        rig.Furniture.Service.Strike(row, EntityHandle.None, 5f);

        bool before = rig.Entity.IsBroken(0);
        rig.Breakables.OnThink(0.015f);

        Assert.False(before);
        Assert.True(rig.Entity.IsBroken(0));
    }

    [Fact]
    public void The_entity_is_sent_to_every_client_always_and_nothing_else_is_ruled()
    {
        var rig = new Rig();
        var transmit = new BreakablesTransmit(rig.Breakables);

        Assert.Equal(TransmitAnswer.Always, transmit.ShouldTransmit(rig.Entity.Handle, 1));
        Assert.Equal(TransmitAnswer.Default, transmit.ShouldTransmit(new EntityHandle(0x00010002u), 1));
    }

    [Fact]
    public void A_new_level_forgets_the_old_props()
    {
        var rig = new Rig();

        rig.Breakables.OnLevelShutdownPreEntity();

        Assert.Null(rig.Breakables.Entity);
        Assert.Empty(rig.Breakables.Props);
    }
}

/// <summary>The box test both sides answer traces with (<see cref="BreakableGeometry"/>).</summary>
public sealed class BreakableGeometryTests
{
    private static readonly BreakableSpot Crate = new(0, FurnitureCatalog.Number(FurnitureKindId.Breakable, "crate"), new NVector(100, 0, 0), 0f);
    private static readonly BreakableSpot Barrel = new(1, FurnitureCatalog.Number(FurnitureKindId.Breakable, "barrel"), new NVector(300, 0, 0), 0f);
    private static readonly BreakableSpot[] Both = [Crate, Barrel];

    private static int Trace(BoneRay ray, BoneTrace tr, Func<int, bool>? live = null, int mask = BreakableGeometry.ContentsSolid) =>
        BreakableGeometry.Trace(ray, mask, tr, Both, live ?? (_ => true));

    [Fact]
    public void A_ray_along_the_floor_stops_at_the_nearest_prop()
    {
        var tr = new BoneTrace();

        int slot = Trace(BoneRay.FromPoints(new NVector(0, 0, 10), new NVector(400, 0, 10)), tr);

        Assert.Equal(Crate.Slot, slot);
        Assert.Equal((100f - 21f) / 400f, tr.Fraction, 4);
        Assert.Equal(new NVector(-1, 0, 0), tr.PlaneNormal);
        Assert.Equal(79f, tr.EndPos.X, 3);
    }

    [Fact]
    public void A_broken_prop_lets_the_ray_on_to_the_next()
    {
        var tr = new BoneTrace();

        int slot = Trace(BoneRay.FromPoints(new NVector(0, 0, 10), new NVector(400, 0, 10)), tr, s => s != Crate.Slot);

        Assert.Equal(Barrel.Slot, slot);
        Assert.Equal((300f - 23f) / 400f, tr.Fraction, 4);
    }

    [Fact]
    public void A_ray_above_every_prop_hits_nothing_and_leaves_the_trace()
    {
        var tr = new BoneTrace();

        Assert.Equal(-1, Trace(BoneRay.FromPoints(new NVector(0, 0, 100), new NVector(400, 0, 100)), tr));
        Assert.Equal(1f, tr.Fraction);
    }

    [Fact]
    public void A_players_box_swept_into_a_crate_stops_by_its_half_width_short()
    {
        var tr = new BoneTrace();
        var ray = BoneRay.FromBox(new NVector(0, 0, 1), new NVector(200, 0, 1), new NVector(-24, -24, 0), new NVector(24, 24, 82));

        int slot = Trace(ray, tr);

        Assert.Equal(Crate.Slot, slot);
        Assert.Equal(100f - 21f - 24f, tr.EndPos.X, 3);
    }

    [Fact]
    public void A_box_starting_inside_a_prop_is_solid_at_once()
    {
        var tr = new BoneTrace();
        var ray = BoneRay.FromBox(new NVector(100, 0, 1), new NVector(100, 0, 1), new NVector(-24, -24, 0), new NVector(24, 24, 82));

        Trace(ray, tr);

        Assert.True(tr.StartSolid);
        Assert.True(tr.AllSolid);
        Assert.Equal(0f, tr.Fraction);
    }

    [Fact]
    public void A_mask_with_nothing_solid_passes_every_prop()
    {
        var tr = new BoneTrace();

        Assert.Equal(-1, Trace(BoneRay.FromPoints(new NVector(0, 0, 10), new NVector(400, 0, 10)), tr, mask: 0x20 /* CONTENTS_WATER */));
    }

    [Fact]
    public void A_crate_turned_a_quarter_turns_its_box()
    {
        var turned = Crate with { Yaw = 90f };

        var (lo, hi) = BreakableGeometry.WorldBox(turned);

        Assert.Equal(73f, hi.X - lo.X, 3);
        Assert.Equal(43f, hi.Y - lo.Y, 3);
    }

    [Fact]
    public void A_facing_survives_the_wire_to_within_a_256th_of_a_turn()
    {
        foreach (float yaw in new[] { 0f, 90f, 180f, 270f, 45f, 359f })
            Assert.InRange(Math.Abs(((BreakableGeometry.YawDegrees(BreakableGeometry.YawByte(yaw)) - yaw + 540f) % 360f) - 180f), 0f, 360f / 256f);
    }
}
