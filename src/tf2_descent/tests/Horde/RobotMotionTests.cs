using System.Diagnostics;
using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Motion;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Horde.Motion;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// A body's motion (plan_descent_logic 3.7, HO3), engine-free: the game's
/// movement over a box world, fake bodies, the motion ticked by hand.
/// </summary>
public sealed class RobotMotionTests
{
    private const float Tick = 0.015f;

    private static float Flat(AiVector a, AiVector b) => (a - b).Length2D;

    /// <summary>How far apart two standing hulls are on the floor plane, by the nearer axis's gap: a hull width or more is apart.</summary>
    private static float Apart(AiVector a, AiVector b) => MathF.Max(MathF.Abs(a.X - b.X), MathF.Abs(a.Y - b.Y));

    [Fact]
    public void Four_bodies_lent_on_one_point_spread_a_hull_width_apart_land_and_then_cost_nothing()
    {
        // A pack as the level lends it: every body on its one marker.
        var rig = new MotionRig(bodies: 4);
        for (int b = 1; b <= 4; b++)
            rig.Bodies.Lend(b, At(0, 0, 0));

        rig.Tick(90);

        for (int i = 1; i <= 4; i++)
        {
            for (int j = i + 1; j <= 4; j++)
                Assert.True(Apart(rig.At(i), rig.At(j)) >= 48f, $"bodies {i} and {j} at {rig.At(i)} and {rig.At(j)}");
            Assert.True(rig.Motion.TryView(i, out var view) && view.OnGround, $"body {i} is not on the ground");
            Assert.NotEqual(MotionGait.Air, rig.Bodies.LastGait[i]);
        }

        rig.Tick(30);
        long steps = rig.Motion.Stepper!.StepsRun;
        rig.Tick(30);
        Assert.Equal(steps, rig.Motion.Stepper.StepsRun);
    }

    [Fact]
    public void A_pack_spreading_keeps_the_facing_it_was_lent_with()
    {
        // Their eyes are their senses: a pack lent facing a door still faces it once spread.
        var rig = new MotionRig(bodies: 4);
        for (int b = 1; b <= 4; b++)
            rig.Bodies.Lend(b, At(0, 0, 0), yaw: 90f);

        rig.Tick(90);

        for (int b = 1; b <= 4; b++)
            Assert.Equal(90f, rig.Bodies.Yaw(b), 1);
    }

    [Fact]
    public void Two_robots_ordered_through_each_others_place_both_arrive()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0));
        rig.Stand(2, At(400, 0));

        rig.Motion.Order(1, MotionRig.Follow(At(400, 0)), LookIntent.AlongPath, long.MaxValue);
        rig.Motion.Order(2, MotionRig.Follow(At(0, 0)), LookIntent.AlongPath, long.MaxValue);
        rig.Tick(240);

        Assert.True(Flat(rig.At(1), At(400, 0)) < 40f, $"body 1 ended at {rig.At(1)}");
        Assert.True(Flat(rig.At(2), At(0, 0)) < 40f, $"body 2 ended at {rig.At(2)}");
    }

    [Fact]
    public void A_body_thrown_onto_anothers_place_lands_and_they_separate()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0));
        rig.Stand(2, At(150, 0));

        // A blast throws body 2 up and back onto body 1.
        rig.Motion.Push(2, At(-300, 0, 320));
        rig.Tick(150);

        var landed = rig.Motion.Landed(2);
        Assert.True(landed.Ever && landed.PeakRise > 20f, $"body 2 was not thrown and landed: {landed}, at {rig.At(2)}");
        Assert.True(Apart(rig.At(1), rig.At(2)) >= 48f, $"still inside each other at {rig.At(1)} and {rig.At(2)}");
        Assert.True(rig.Motion.TryView(1, out var one) && one.OnGround);
        Assert.True(rig.Motion.TryView(2, out var two) && two.OnGround);
    }

    /// <summary>A corridor a hull wide, along X: the pushaway presses a body passing a robot into its walls.</summary>
    private static BoxMotionWorld Corridor() => new BoxMotionWorld()
        .Put(new Vector(-1000, 25, 0), new Vector(1000, 1000, 300))
        .Put(new Vector(-1000, -1000, 0), new Vector(1000, -25, 300));

    [Fact]
    public void A_body_passes_a_robot_standing_in_a_corridor_and_is_not_reported_held_up()
    {
        var rig = new MotionRig(world: Corridor());
        rig.Stand(1, At(0, 0));
        rig.Stand(2, At(-200, 0));

        rig.Motion.Order(2, MotionRig.Follow(At(300, 0)), LookIntent.AlongPath, long.MaxValue);
        var seen = BlockedReport.Clear;
        for (int t = 0; t < 180; t++)
        {
            rig.Tick();
            if (rig.Motion.Blocked(2).Blocked)
                seen = rig.Motion.Blocked(2);
        }

        Assert.True(Flat(rig.At(2), At(300, 0)) < 40f, $"body 2 ended at {rig.At(2)}");
        Assert.Equal(BlockedReport.Clear, seen);
    }

    [Fact]
    public void A_body_pushing_through_a_crowd_in_a_corridor_reports_a_robot_of_it()
    {
        var rig = new MotionRig(bodies: 5, world: Corridor());
        for (int b = 1; b <= 4; b++)
            rig.Stand(b, At((b - 1) * 48, 0));
        rig.Stand(5, At(-200, 0));

        rig.Motion.Order(5, MotionRig.Follow(At(500, 0)), LookIntent.AlongPath, long.MaxValue);
        var seen = BlockedReport.Clear;
        for (int t = 0; t < 240 && seen.Kind != BlockerKind.Robot; t++)
        {
            rig.Tick();
            seen = rig.Motion.Blocked(5);
        }

        Assert.True(seen.Kind == BlockerKind.Robot, $"seen {seen}; body 5 at {rig.At(5)}");
        Assert.InRange(seen.Who, 1, 4);
        Assert.True(seen.Last - seen.Since + 1 >= MotionSettings.Default.CrowdTicks, $"reported after {seen.Last - seen.Since + 1} ticks");
    }

    [Fact]
    public void Separating_48_bodies_lent_on_one_point_allocates_nothing_on_the_main_thread()
    {
        // The code warmed on one rig; a fresh pack measured from its lend, separation running throughout.
        static MotionRig Pack()
        {
            var r = new MotionRig(bodies: 48);
            for (int b = 1; b <= 48; b++)
                r.Bodies.Lend(b, At(0, 0, 0));
            return r;
        }

        Pack().Tick(200);

        // A steady allocation shows for every pack; a one-off (the runtime re-jitting a hot method while the box is
        // loaded) for one: the quietest of three fresh packs is what separating costs.
        long allocated = long.MaxValue;
        MotionRig rig = null!;
        for (int pack = 0; pack < 3 && allocated != 0; pack++)
        {
            rig = Pack();
            rig.Tick();
            long before = GC.GetAllocatedBytesForCurrentThread();
            rig.Tick(60);
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.True(rig.Motion.Stepper!.StepsRun > 48 * 30, $"the pack hardly moved: {rig.Motion.Stepper.StepsRun} steps");
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void A_body_reaches_its_corners_in_order_at_its_speed_and_holds_at_the_last()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0));
        AiVector[] corners = [At(400, 0), At(400, 400), At(0, 400)];
        rig.Motion.Order(1, MotionRig.Follow(corners), LookIntent.AlongPath, long.MaxValue);

        var reached = new int[3];
        var track = new List<AiVector>();
        for (int t = 1; t <= 700; t++)
        {
            rig.Tick();
            track.Add(rig.At(1));
            for (int c = 0; c < 3; c++)
                if (reached[c] == 0 && Flat(rig.At(1), corners[c]) < 30f)
                    reached[c] = t;
        }

        Assert.All(reached, r => Assert.True(r > 0, "a corner was never reached"));
        Assert.True(reached[0] < reached[1] && reached[1] < reached[2], $"reached at {string.Join(",", reached)}");

        // At its speed on the first straight, once accelerated: 230 a second.
        float speed = Flat(track[59], track[19]) / (40 * Tick);
        Assert.InRange(speed, 230f * 0.98f, 230f * 1.02f);

        // Held at the last: near it, and no longer moving.
        Assert.True(Flat(track[^1], corners[2]) < 64f, $"ended {track[^1]}");
        Assert.Equal(track[^50], track[^1]);
    }

    [Fact]
    public void A_fast_body_comes_to_rest_at_its_last_corner_not_past_it()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0), speed: 420f);
        rig.Motion.Order(1, MotionRig.Follow(At(600, 0)), LookIntent.AlongPath, long.MaxValue);

        float most = 0f;
        for (int t = 0; t < 300; t++)
        {
            rig.Tick();
            most = MathF.Max(most, rig.At(1).X);
        }

        Assert.InRange(rig.At(1).X, 600f - 32f, 600f + 4f);
        Assert.True(most <= 600f + 4f, $"slid to {most}, past the corner");
    }

    [Fact]
    public void An_expired_order_holds()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0));
        rig.Motion.Order(1, MotionRig.Follow(At(2000, 0)), LookIntent.AlongPath, rig.Now + 20);

        rig.Tick(100);
        var held = rig.At(1);
        rig.Tick(50);

        Assert.Equal(held, rig.At(1));
        Assert.InRange(held.X, 20f, 20 * 230f * Tick + 60f);
    }

    [Fact]
    public void Hold_stops_a_moving_body()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0));
        rig.Motion.Order(1, MotionRig.Follow(At(2000, 0)), LookIntent.AlongPath, long.MaxValue);
        rig.Tick(60);
        var atHold = rig.At(1);

        rig.Motion.Hold(1);
        rig.Tick(60);
        var stopped = rig.At(1);
        rig.Tick(30);

        Assert.Equal(stopped, rig.At(1));
        Assert.True(Flat(stopped, atHold) < 60f, $"slid {Flat(stopped, atHold)} after the hold");
    }

    [Fact]
    public void A_stunned_robot_does_not_move()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0));
        rig.Bodies.Stun(1, true);
        var start = rig.At(1);

        rig.Motion.Order(1, MotionRig.Follow(At(2000, 0)), LookIntent.AlongPath, long.MaxValue);
        rig.Tick(60);

        Assert.Equal(start, rig.At(1));
    }

    [Fact]
    public void The_accept_clips_a_move_into_a_blocker_and_the_next_step_starts_from_the_clipped_place()
    {
        // The step's world knows only the floor (as the nav voxels know no prop); the accept's knows a crate.
        var stepWorld = new BoxMotionWorld();
        var acceptWorld = new BoxMotionWorld().Put(new Vector(100, -64, 0), new Vector(140, 64, 64), entityIndex: 77);
        var recorder = new RecordingStep(GameMovementStep.Create(stepWorld, Tick));
        var rig = new MotionRig(world: acceptWorld, stepper: _ => new InlineMotionStepper(recorder, 8, new ThreadCpuClock()));
        rig.Stand(1, At(0, 0));
        rig.Motion.Order(1, MotionRig.Follow(At(400, 0)), LookIntent.AlongPath, long.MaxValue);

        float most = 0f;
        for (int t = 0; t < 120; t++)
        {
            rig.Tick();
            most = MathF.Max(most, rig.At(1).X);
        }

        // Never inside the crate: its near face less the hull's half-width.
        Assert.InRange(most, 70f, 100f - 24f);
        Assert.True(rig.Motion.Clips > 0);
        Assert.Equal(HordeVectors.ToEngine(rig.Motion.Written(1)), recorder.Last.State.Origin);
    }

    [Fact]
    public void A_body_blocked_by_a_prop_reports_the_prop()
    {
        var rig = new MotionRig(world: new BoxMotionWorld().Put(new Vector(100, -64, 0), new Vector(140, 64, 64), entityIndex: 77));
        rig.Stand(1, At(0, 0));
        rig.Motion.Order(1, MotionRig.Follow(At(400, 0)), LookIntent.AlongPath, long.MaxValue);

        rig.Tick(80);

        var r = rig.Motion.Blocked(1);
        Assert.Equal(BlockerKind.Prop, r.Kind);
        Assert.Equal(77, r.Who);
        Assert.True(r.Ticks > 10, $"blocked {r.Ticks} ticks");
    }

    [Fact]
    public void A_body_blocked_by_a_hero_reports_the_heros_seat_and_goes_on_when_he_steps_aside()
    {
        var world = new BoxMotionWorld().Put(new Vector(100, -24, 0), new Vector(148, 24, 82), entityIndex: 3, isPlayer: true);
        var rig = new MotionRig(world: world);
        rig.Stand(1, At(0, 0));
        rig.Motion.Order(1, MotionRig.Follow(At(400, 0)), LookIntent.AlongPath, long.MaxValue);

        rig.Tick(80);
        var blocked = rig.Motion.Blocked(1);
        world.Boxes.Clear();
        rig.Tick(120);

        Assert.Equal(new BlockedReport(BlockerKind.Hero, 3, blocked.Since, blocked.Last, blocked.Ticks), blocked);
        Assert.True(rig.At(1).X > 300f, $"at {rig.At(1)} after the hero left");
        Assert.False(rig.Motion.Blocked(1).Blocked);
    }

    [Fact]
    public void A_body_blocked_by_another_body_reports_that_bodys_index()
    {
        var rig = new MotionRig();
        int other = rig.Bodies.Handle(2).EntryIndex;
        rig.World.Put(new Vector(100, -24, 0), new Vector(148, 24, 82), entityIndex: other);
        rig.Stand(1, At(0, 0));
        rig.Motion.Order(1, MotionRig.Follow(At(400, 0)), LookIntent.AlongPath, long.MaxValue);

        rig.Tick(80);

        Assert.Equal(BlockerKind.Robot, rig.Motion.Blocked(1).Kind);
        Assert.Equal(2, rig.Motion.Blocked(1).Who);
    }

    [Fact]
    public void A_giants_hull_refuses_a_gap_its_width_does_not_fit_and_stops_there()
    {
        // Walls at x 200..240 leave a gap 80 wide: a standing hull (48) passes, a giant's (84) does not.
        static BoxMotionWorld Gap() => new BoxMotionWorld()
            .Put(new Vector(200, -1000, 0), new Vector(240, -40, 300))
            .Put(new Vector(200, 40, 0), new Vector(240, 1000, 300));

        var normal = new MotionRig(world: Gap());
        normal.Stand(1, At(0, 0));
        normal.Motion.Order(1, MotionRig.Follow(At(500, 0)), LookIntent.AlongPath, long.MaxValue);
        var giant = new MotionRig(world: Gap());
        giant.Stand(1, At(0, 0), scale: 1.75f);
        giant.Motion.Order(1, MotionRig.Follow(At(500, 0)), LookIntent.AlongPath, long.MaxValue);

        float giantMost = 0f, giantStep = 0f;
        var last = giant.At(1);
        for (int t = 0; t < 200; t++)
        {
            normal.Tick();
            giant.Tick();
            giantMost = MathF.Max(giantMost, giant.At(1).X);
            giantStep = MathF.Max(giantStep, Flat(giant.At(1), last));
            last = giant.At(1);
        }

        Assert.True(normal.At(1).X > 400f, $"the standing body stopped at {normal.At(1)}");
        Assert.True(giantMost <= 200f - 42f, $"the giant reached {giantMost}");
        Assert.True(giantStep < 5f, $"the giant jumped {giantStep} in a tick");
        Assert.Equal(BlockerKind.World, giant.Motion.Blocked(1).Kind);
    }

    [Fact]
    public void A_push_throws_the_body_as_the_movement_says_and_it_lands()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0));
        float floor = rig.At(1).Z;

        Assert.True(rig.Motion.Push(1, At(0, 0, 300)));
        float peak = floor;
        for (int t = 0; t < 120; t++)
        {
            rig.Tick();
            peak = MathF.Max(peak, rig.At(1).Z);
        }

        // v^2 / 2g at TF's gravity of 800: 56.25.
        Assert.InRange(peak - floor, 54f, 58f);
        var landed = rig.Motion.Landed(1);
        Assert.True(landed.Ever);
        Assert.InRange(landed.PeakRise, 54f, 58f);
        Assert.InRange(landed.AirTicks, 46, 54);
        Assert.InRange(landed.FallSpeed, 270f, 320f);
        Assert.Equal(floor, rig.At(1).Z, 1);
    }

    /// <summary>
    /// A body stepping down off a ledge lower than a step (the root of a live stall on depth 1 seed
    /// 1: bodies on the widedrift room's 8-unit ledge sat still, following, not blocked, for good):
    /// the movement walks it across and down onto the floor below, and the accept, which swept
    /// that step as one straight line, struck the ledge it stood on at once (a floor's face, so no
    /// clip and no block) and kept the body where it was, tick after tick. It walks off now.
    /// </summary>
    [Fact]
    public void A_body_walks_down_off_a_ledge_lower_than_a_step()
    {
        var rig = new MotionRig(world: new BoxMotionWorld { FloorZ = 0f }.Put(new Vector(-1024, -1024, 0), new Vector(100, 1024, 8)));
        rig.Stand(1, At(80, 0, 8));
        Assert.InRange(rig.At(1).Z, 7.9f, 9.1f);

        rig.Motion.Order(1, MotionRig.Follow(At(400, 0, 0)), LookIntent.AlongPath, long.MaxValue);
        rig.Tick(66);

        Assert.True(rig.At(1).X > 200f, $"the body stands at {rig.At(1)}: it never stepped down off the ledge");
        Assert.Equal(0f, rig.At(1).Z, 0.1f);
    }

    [Fact]
    public void Facing_turns_at_the_bounded_rate()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0));
        float step = MotionSettings.Default.TurnRate * Tick;

        rig.Motion.Order(1, MoveIntent.Hold, new LookIntent(LookKind.Point, At(-500, 1, 60), default, AimPoint.Body, 0f, 0), long.MaxValue);
        var yaws = new List<float> { rig.Bodies.Yaw(1) };
        for (int t = 0; t < 40; t++)
        {
            rig.Tick();
            yaws.Add(rig.Bodies.Yaw(1));
        }

        for (int i = 1; i < yaws.Count; i++)
            Assert.True(MathF.Abs(MotionAngles.Difference(yaws[i], yaws[i - 1])) <= step + 1e-3f, $"turned {yaws[i - 1]} -> {yaws[i]}");
        Assert.True(MathF.Abs(MotionAngles.Difference(yaws[^1], 180f)) < 1f, $"faces {yaws[^1]}");
    }

    [Fact]
    public void Facing_tracks_an_actors_current_eye_not_where_it_was()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0));
        var hero = new ActorId(50, 1);
        rig.Actors.Eyes[hero] = At(0, 500, 64);
        rig.Motion.Order(1, MoveIntent.Hold, new LookIntent(LookKind.Actor, default, hero, AimPoint.Body, 0f, 0), long.MaxValue);
        rig.Tick(30);
        Assert.Equal(90f, rig.Bodies.Yaw(1), 1);

        rig.Actors.Eyes[hero] = At(500, 0, 64);
        rig.Tick();
        Assert.True(rig.Bodies.Yaw(1) < 90f, "the first tick after the actor moved still faces where it was");
        rig.Tick(30);
        Assert.Equal(0f, rig.Bodies.Yaw(1), 1);
    }

    [Fact]
    public void The_move_blend_follows_the_velocity_in_the_bodys_own_frame()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0));
        var strafe = new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, At(0, 1, 0), false, 0);
        var ahead = new LookIntent(LookKind.Point, At(1_000_000, 0, 64), default, AimPoint.Body, 0f, 0);

        rig.Motion.Order(1, strafe, ahead, long.MaxValue);
        rig.Tick(40);

        var pose = rig.Bodies.LastPose[1];
        Assert.Equal(0f, rig.Bodies.Yaw(1), 1);
        Assert.Equal(0f, pose.MoveX, 1);
        Assert.Equal(-1f, pose.MoveY, 1);
    }

    [Fact]
    public void The_gait_is_written_only_when_it_changes()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0));
        int before = rig.Bodies.Gaits[1];

        rig.Motion.Order(1, MotionRig.Follow(At(1500, 0)), LookIntent.AlongPath, long.MaxValue);
        rig.Tick(150);

        Assert.Equal(MotionGait.Run, rig.Bodies.LastGait[1]);
        Assert.Equal(before + 1, rig.Bodies.Gaits[1]);
    }

    [Fact]
    public void A_body_lent_on_the_floors_plane_starts_a_unit_up_and_lands_on_it()
    {
        // The engine counts a hull exactly on a floor's plane as in solid, and a body there never moved.
        var rig = new MotionRig();
        rig.Bodies.Lend(1, At(0, 0, 0));

        rig.Tick(20);

        Assert.True(rig.Motion.TryView(1, out var view));
        Assert.True(view.OnGround);
        Assert.InRange(rig.At(1).Z, 0.01f, 0.2f);
        Assert.True(rig.Motion.Landed(1).Ever);
    }

    [Fact]
    public void A_body_lent_inside_another_is_put_down_beside_it()
    {
        var rig = new MotionRig();
        int other = rig.Bodies.Handle(2).EntryIndex;
        rig.World.Put(new Vector(-24, -24, 0), new Vector(24, 24, 82), entityIndex: other);
        rig.Bodies.Lend(1, At(0, 0, 0));

        rig.Tick(20);

        var at = rig.At(1);
        Assert.True(at.Length2D >= 48f, $"still inside the other body at {at}");
        Assert.True(at.Length2D <= 2 * 50f + 1f, $"put too far away at {at}");
        Assert.True(rig.Motion.TryView(1, out var view) && view.OnGround);
    }

    [Fact]
    public void A_body_whose_hull_reaches_into_a_wall_is_put_down_clear_of_it_on_its_own_side()
    {
        // A giant put down by a corridor wall: its middle is clear, its hull is not.
        var rig = new MotionRig(world: new BoxMotionWorld().Put(new Vector(-1000, 40, 0), new Vector(1000, 1000, 300)));
        rig.Bodies.Lend(1, At(0, 30, 0), scale: 1.9f);

        rig.Tick(20);

        var at = rig.At(1);
        Assert.True(at.Y <= 40f - 24f * 1.9f, $"still in the wall at {at}");
        Assert.True(rig.Motion.TryView(1, out var view) && view.OnGround);
    }

    [Fact]
    public void A_body_lent_inside_a_prop_is_never_put_down_behind_a_wall()
    {
        // The prop round the marker; a wall 60 to the east with open floor beyond it, and open floor to the west.
        var world = new BoxMotionWorld()
            .Put(new Vector(-30, -30, 0), new Vector(30, 30, 60), entityIndex: 77)
            .Put(new Vector(60, -1000, 0), new Vector(64, 1000, 300));
        var rig = new MotionRig(world: world);
        rig.Bodies.Lend(1, At(0, 0, 0));

        rig.Tick(20);

        var at = rig.At(1);
        Assert.True(at.X < 60f, $"put behind the wall at {at}");
        Assert.True(MathF.Abs(at.X) >= 54f || MathF.Abs(at.Y) >= 54f, $"still in the prop at {at}");
    }

    [Fact]
    public void A_parked_bodys_order_is_dropped()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0));
        rig.Bodies.Park(1);

        rig.Motion.Order(1, MotionRig.Follow(At(400, 0)), LookIntent.AlongPath, long.MaxValue);
        rig.Tick();

        Assert.Equal(1, rig.Motion.OrdersDropped);
        Assert.False(rig.Motion.TryView(1, out _));
    }

    [Fact]
    public void A_body_lent_again_starts_without_the_last_lends_order()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0));
        rig.Motion.Order(1, MotionRig.Follow(At(2000, 0)), LookIntent.AlongPath, long.MaxValue);
        rig.Tick(10);
        rig.Bodies.Park(1);
        rig.Tick();

        rig.Stand(1, At(0, 300));
        var start = rig.At(1);
        rig.Tick(30);

        Assert.Equal(start, rig.At(1));
        Assert.True(rig.Motion.TryView(1, out var view));
        Assert.False(view.Ordered);
    }

    [Fact]
    public void A_stalled_worker_leaves_the_body_holding_and_the_main_thread_never_waits()
    {
        var world = new BoxMotionWorld();
        var blocking = new BlockingStep(GameMovementStep.Create(world, Tick));
        WorkerMotionStepper? worker = null;
        var rig = new MotionRig(world: world, stepper: _ => worker = new WorkerMotionStepper(blocking, 8, new ThreadCpuClock()));
        rig.Bodies.Lend(1, At(0, 0, 1));
        rig.Motion.Order(1, MotionRig.Follow(At(3000, 0)), LookIntent.AlongPath, long.MaxValue);
        for (int t = 0; t < 40; t++)
            TickAndSettle(rig, worker!);
        var moving = rig.At(1);
        Assert.True(moving.X > 50f, $"never walked: {moving}");

        // The worker stalls inside a step; the main thread goes on ticking.
        blocking.Gate.Reset();
        rig.Tick();
        SpinUntil(() => blocking.Waiting > 0);
        var held = rig.At(1);
        long holdsBefore = rig.Motion.Holds;
        rig.Tick(30);

        // A gate, never a clock: the step is still held when the thirty ticks are done, so none of them waited for it.
        Assert.True(blocking.Waiting > 0, "the main thread waited: the held step had left by the thirtieth tick");
        Assert.Equal(held, rig.At(1));
        Assert.True(rig.Motion.Holds - holdsBefore >= 29, $"held {rig.Motion.Holds - holdsBefore} ticks");

        // Released: the stale set is dropped, never applied, and the body walks on from where it held.
        blocking.Gate.Set();
        SpinUntil(() => blocking.Waiting == 0);
        TickAndSettle(rig, worker!);
        TickAndSettle(rig, worker!);
        var resumed = rig.At(1);
        Assert.True(Flat(resumed, held) < 12f, $"jumped {Flat(resumed, held)} on resuming");
        for (int t = 0; t < 20; t++)
            TickAndSettle(rig, worker!);
        Assert.True(rig.At(1).X > resumed.X + 30f, "it did not walk on");
    }

    [Fact]
    public void A_worker_set_more_than_a_tick_old_is_dropped_and_a_fresh_one_taken()
    {
        var world = new BoxMotionWorld();
        using var worker = new WorkerMotionStepper(GameMovementStep.Create(world, Tick), 4, new ThreadCpuClock());
        var input = new StepInput { Body = 1, Serial = 1, State = BodyKinematics.At(new Vector(0, 0, 1), new Vector(0, 0, 72)), MaxSpeed = 230f, Scale = 1f };

        Assert.True(worker.Submit(10, [input]));
        SpinUntil(() => worker.StepsRun == 1);
        Assert.False(worker.TryTake(12, out _, out _));
        Assert.Equal(1, worker.Stale);

        Assert.True(worker.Submit(11, [input]));
        SpinUntil(() => worker.StepsRun == 2);
        Assert.True(worker.TryTake(12, out var outputs, out long tick));
        Assert.Equal(11, tick);
        Assert.Equal(1, outputs.Length);
    }

    [Fact]
    public void A_worker_step_is_applied_one_tick_after_the_tick_that_asked_for_it()
    {
        var world = new BoxMotionWorld();
        var recorder = new RecordingStep(GameMovementStep.Create(world, Tick));
        WorkerMotionStepper? worker = null;
        var rig = new MotionRig(world: world, stepper: _ => worker = new WorkerMotionStepper(recorder, 8, new ThreadCpuClock()));
        rig.Bodies.Lend(1, At(0, 0, 1));
        rig.Motion.Order(1, MotionRig.Follow(At(3000, 0)), LookIntent.AlongPath, long.MaxValue);
        rig.Motion.Costs.Start();

        for (int t = 0; t < 30; t++)
            TickAndSettle(rig, worker!);

        Assert.Contains("1 tick 29", rig.Motion.Costs.Report());
        Assert.Contains("0 ticks 0", rig.Motion.Costs.Report());
    }

    [Fact]
    public void Steady_ticks_of_48_moving_bodies_allocate_nothing_on_the_main_thread()
    {
        var rig = new MotionRig(bodies: 48);
        for (int b = 1; b <= 48; b++)
        {
            rig.Bodies.Lend(b, At(b * 100, 0, 1));
            rig.Motion.Order(b, MotionRig.Follow(At(b * 100, 9000), At(9000, 9000), At(9000, -9000), At(-9000, -9000)), LookIntent.AlongPath,
                             long.MaxValue);
        }

        rig.Tick(100);
        long before = GC.GetAllocatedBytesForCurrentThread();
        rig.Tick(1000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(rig.At(48).Y > 3000f, $"the bodies did not move: {rig.At(48)}");
        Assert.Equal(0, allocated);
    }

    /// <summary>A tick, then until the worker has run what it was handed (<see cref="VoxelStepperTests.Settle"/>: a hand-over the worker's lock refused is ticked again).</summary>
    private static void TickAndSettle(MotionRig rig, WorkerMotionStepper worker) => VoxelStepperTests.Settle(rig, worker);

    private static void SpinUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            // A cap against a hang, not a pace: on a loaded box a pool thread can wait seconds for a core.
            if (watch.ElapsedMilliseconds > 60_000)
                throw new TimeoutException("the worker never finished");
            Thread.Sleep(1);
        }
    }
}

/// <summary>A step that remembers the last command it was given.</summary>
internal sealed class RecordingStep(IMotionStep inner) : IMotionStep
{
    public StepInput Last { get; private set; }

    public void Run(in StepInput input, out StepOutput output)
    {
        Last = input;
        inner.Run(in input, out output);
    }
}

/// <summary>A step that waits at a gate a fact closes: a stalled worker.</summary>
internal sealed class BlockingStep(IMotionStep inner) : IMotionStep
{
    private int _waiting;

    public ManualResetEventSlim Gate { get; } = new(true);

    public int Waiting => Volatile.Read(ref _waiting);

    public void Run(in StepInput input, out StepOutput output)
    {
        Interlocked.Increment(ref _waiting);
        Gate.Wait();
        Interlocked.Decrement(ref _waiting);
        inner.Run(in input, out output);
    }
}
