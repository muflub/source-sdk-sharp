using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The airblast's server half (<c>DeflectProjectiles</c>,
/// <c>tf_weaponbase.cpp:5900-5972</c>): the sweep, the rocket sent back, the
/// enemy pushed by the cray force and the burning teammate put out.
/// </summary>
public class TFAirBlastFacts
{
    /// <summary>Teams asked in the order the blast asks them: the target's, then the blaster's.</summary>
    private static void Teams(FlameRig rig, int targetTeam, int blasterTeam)
    {
        var answers = new Queue<int>([targetTeam, blasterTeam]);
        rig.Engine.ReturnsEachCall("Players.GetTeam", () => answers.Dequeue());
    }

    /// <summary>Centers asked in the order the blast asks them: the target's, then the blaster's.</summary>
    private static void Centers(FlameRig rig, Vector targetCenter, Vector blasterCenter)
    {
        var answers = new Queue<Vector>([targetCenter, blasterCenter]);
        rig.Engine.ReturnsEachCall("Entities.GetWorldSpaceCenter", () => answers.Dequeue());
    }

    /// <summary>
    /// A target hull carrying the cone's axis point (128, 0, 64): the retest
    /// passes and the push lands.
    /// </summary>
    private static void InsideCone(FlameRig rig)
    {
        rig.Engine.Returns("Entities.GetOrigin", new Vector(128f, 0f, 0f));
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)));
    }

    [Fact]
    public void A_rocket_in_the_sphere_is_sent_back_at_its_old_speed()
    {
        // The rocket hangs at (256, 0, 64) with (0, 1000, 0); the aim's ray
        // stops at (128, 0, 64) — the direction to the rocket from it is
        // (-1, 0, 0) — and the flame rig's aim is straight down +X. The LOS
        // and the deflection share one trace answer: clear, ending at the stop.
        var rig = new FlameRig();
        rig.Directory.Instance = rig.Rocket;
        rig.Engine.FillsSphere(FlameRig.RocketHandle);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(256f, 0f, 64f));
        rig.Engine.Returns("Entities.GetVelocity", new Vector(0f, 1000f, 0f));
        rig.Engine.Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, new Vector(128f, 0f, 64f), default, EntityHandle.None));

        rig.Effects.AirBlast(Attack(), Script());

        var sent = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(sent);
        Assert.Equal(new Vector(-1000f, 0f, 0f), sent!.ArgAs<Vector>(1));

        var owned = rig.Engine.Last("Entities.SetOwner");
        Assert.NotNull(owned);
        Assert.Equal(FlameRig.RocketHandle, owned!.ArgAs<EntityHandle>(0));
        Assert.Equal(FlameRig.Player, owned.ArgAs<EntityHandle>(1));
        Assert.False(rig.Rocket.Critical);
    }

    [Fact]
    public void A_crit_boosted_deflector_makes_the_rocket_critical()
    {
        var rig = new FlameRig();
        rig.Directory.Instance = rig.Rocket;
        rig.Engine.FillsSphere(FlameRig.RocketHandle);
        rig.Conditions.Grant(FlameRig.Player, TFCond.Critboosted);

        rig.Effects.AirBlast(Attack(), Script());

        Assert.True(rig.Rocket.Critical);
    }

    [Fact]
    public void A_rocket_out_of_sight_is_left_alone()
    {
        var rig = new FlameRig();
        rig.Directory.Instance = rig.Rocket;
        rig.Engine.FillsSphere(FlameRig.RocketHandle);
        // Something solid stopped the sight line short, and it was not the rocket.
        rig.Engine.Returns("WorldQuery.TraceLine", new TraceResult(true, 0.5f, default, default, EntityHandle.None));

        rig.Effects.AirBlast(Attack(), Script());

        Assert.Null(rig.Engine.Last("Entities.SetVelocity"));
        Assert.Null(rig.Engine.Last("Entities.SetOwner"));
    }

    /// <summary>The rocket fact's geometry, for a grenade: hanging at (256, 0, 64).</summary>
    private static void GrenadeRig(FlameRig rig, ScriptedEntity grenade, EntityHandle handle)
    {
        rig.Directory.Instance = grenade;
        rig.Engine.FillsSphere(handle);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(256f, 0f, 64f));
        rig.Engine.Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, new Vector(128f, 0f, 64f), default, EntityHandle.None));
    }

    [Fact]
    public void A_pipe_in_the_sphere_is_sent_back_at_its_old_speed()
    {
        // The pipe hangs at (256, 0, 64); the aim's ray stops at (128, 0, 64),
        // so the deflection aims (-1, 0, 0) — and a pipe moving (0, 1000, 0)
        // leaves at its own 1000 along it, as the base DeflectEntity sends
        // every deflected thing (tf_weaponbase.cpp:6069-6083).
        var rig = new FlameRig();
        GrenadeRig(rig, rig.Pipebomb, FlameRig.PipebombHandle);
        rig.Engine.Returns("Entities.GetVelocity", new Vector(0f, 1000f, 0f));

        rig.Effects.AirBlast(Attack(), Script());

        var sent = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(sent);
        Assert.Equal(new Vector(-1000f, 0f, 0f), sent!.ArgAs<Vector>(1));
        Assert.Equal(1, rig.Pipebomb.DeflectedCount);
    }

    [Fact]
    public void A_deflected_pipe_is_the_deflectors()
    {
        // Deflected's regular-pipe arm (tf_weapon_grenade_pipebomb.cpp:1246-1257):
        // the deflector's team and launcher ride the pipe — the wire's team
        // and the weapon name the attributes read.
        var rig = new FlameRig();
        GrenadeRig(rig, rig.Pipebomb, FlameRig.PipebombHandle);

        rig.Effects.AirBlast(Attack(), Script());

        Assert.Equal(2, rig.Pipebomb.State.TeamNumber);
        Assert.Equal("tf_flamethrower", rig.Pipebomb.WeaponName);
    }

    [Fact]
    public void A_deflected_pipe_wears_the_new_teams_skin()
    {
        // IncrementDeflected's skin arm (tf_weapon_grenade_pipebomb.cpp:1094-1097):
        // blue is skin 1, red skin 0 — the deflector here is blue (3).
        var rig = new FlameRig();
        GrenadeRig(rig, rig.Pipebomb, FlameRig.PipebombHandle);
        rig.Engine.Returns("Players.GetTeam", 3);

        rig.Effects.AirBlast(Attack(), Script());

        var skinned = Assert.Single(rig.Skins.Set);
        Assert.Equal((FlameRig.PipebombHandle, 1), skinned);
    }

    [Fact]
    public void A_crit_boosted_deflectors_pipe_is_critical()
    {
        // Deflected's crit arm (:1253-1256): the deflector's boost is the
        // pipe's, on the entity and on the wire.
        var rig = new FlameRig();
        GrenadeRig(rig, rig.Pipebomb, FlameRig.PipebombHandle);
        rig.Conditions.Grant(FlameRig.Player, TFCond.Critboosted);

        rig.Effects.AirBlast(Attack(), Script());

        Assert.True(rig.Pipebomb.Critical);
        Assert.True(rig.Pipebomb.State.Critical);
    }

    [Fact]
    public void The_blast_leaves_a_teammates_grenade()
    {
        // Stock's own-team refusal (tf_weapon_flamethrower.cpp:1914-1920): a
        // grenade of the blaster's own team is not deflected at all.
        var rig = new FlameRig();
        GrenadeRig(rig, rig.Pipebomb, FlameRig.PipebombHandle);
        rig.Pipebomb.State.TeamNumber = 2; // the blaster's, as the rig's teams answer

        rig.Effects.AirBlast(Attack(), Script());

        Assert.Null(rig.Engine.Last("Entities.SetVelocity"));
        Assert.Equal(0, rig.Pipebomb.DeflectedCount);
    }

    [Fact]
    public void A_stickybomb_in_the_sphere_is_sent_back_and_keeps_its_team()
    {
        // Deflected's sticky arm (tf_weapon_grenade_pipebomb.cpp:1223-1245):
        // the bomb keeps its thrower and team — here the blue's, against the
        // blaster's red — and takes the blow only once stuck; in flight, as
        // now, stock's OnTakeDamage refuses and only the send-back lands.
        var rig = new FlameRig();
        GrenadeRig(rig, rig.Stickybomb, FlameRig.StickyHandle);
        rig.Stickybomb.State.TeamNumber = 3;
        rig.Engine.Returns("Entities.GetVelocity", new Vector(0f, 1000f, 0f));
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-16f, -16f, -36f), new Vector(16f, 16f, 36f)));
        rig.StickyVars.DeflectResetTime.Hold("10"); // the reset clock, refused blow or not

        rig.Effects.AirBlast(Attack(), Script());

        var sent = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(sent);
        Assert.Equal(new Vector(-1000f, 0f, 0f), sent!.ArgAs<Vector>(1));
        Assert.Equal(1, rig.Stickybomb.DeflectedCount);
        Assert.Equal(3, rig.Stickybomb.State.TeamNumber);
        Assert.Empty(rig.Skins.Set);
    }

    [Fact]
    public void A_stuck_stickybomb_is_blown_off_the_stick()
    {
        // The blow: the aim times DeflectionForce(WorldAlignSize, 90, 12)
        // (tf_weaponbase.cpp:6103) — the 32×32×72 hull gives 2767.5, capped
        // 1000 — scaled by tf_grenade_forcefrom_bullet's 0.7, past
        // tf_pipebomb_force_to_move's 80, at the bomb itself. The deflector
        // (the rig's player) is the enemy of the bomb's implied team.
        var rig = new FlameRig();
        GrenadeRig(rig, rig.Stickybomb, FlameRig.StickyHandle);
        rig.Engine.Returns("Entities.GetIndex", 0); // the world touch sticks it
        rig.Engine.Returns("Clock.Now", 5f);
        rig.Engine.Returns("Entities.GetVelocity", default(Vector));
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-16f, -16f, -36f), new Vector(16f, 16f, 36f)));
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == FlameRig.Player ? 2 : 3);
        rig.StickyVars.ForceFromBullet.Hold("0.7");
        rig.StickyVars.ForceToMove.Hold("80");
        rig.Stickybomb.OnTouch(new EntityHandle(0x0001_0000u)); // the world: it sticks

        rig.Effects.AirBlast(Attack(), Script());

        var blown = rig.Engine.Last("EntityPhysics.ApplyForceOffset");
        Assert.NotNull(blown);
        Assert.Equal(FlameRig.StickyHandle, blown!.ArgAs<EntityHandle>(0));
        Assert.Equal(new Vector(-700f, 0f, 0f), blown.ArgAs<Vector>(1));
        Assert.Equal(1, rig.Stickybomb.DeflectedCount);

        var woken = rig.Engine.Last("EntityPhysics.EnableMotion");
        Assert.NotNull(woken);
        Assert.True(woken!.ArgAs<bool>(1));
    }

    [Fact]
    public void A_stickys_deflected_count_resets_after_the_reset_time()
    {
        // IncrementDeflected's clock (tf_weapon_grenade_pipebomb.cpp:1100-1108):
        // ten seconds after the deflection the count is gone again, on the
        // thinks the bomb already keeps.
        var rig = new FlameRig();
        rig.Engine.Returns("Clock.Now", 5f);
        rig.StickyVars.DeflectResetTime.Hold("10"); // the stock default
        rig.Stickybomb.DeflectedBy(FlameRig.Player, new Vector(0f, 1000f, 0f));
        Assert.Equal(1, rig.Stickybomb.DeflectedCount);

        rig.Engine.Returns("Clock.Now", 16f);
        rig.Stickybomb.OnThink();

        Assert.Equal(0, rig.Stickybomb.DeflectedCount);
    }

    [Fact]
    public void The_reset_time_is_tf_pipebomb_deflect_reset_times()
    {
        // The knob (tf_weapon_grenade_pipebomb.cpp:954, read at :1082): three
        // seconds, not stock's ten — the count survives the 7 s think and is
        // gone once the 8 s one falls on the deadline (stock resets at
        // m_flDeflectedTime <= curtime, :1104).
        var rig = new FlameRig();
        rig.Engine.Returns("Clock.Now", 5f);
        rig.StickyVars.DeflectResetTime.Hold("3");
        rig.Stickybomb.DeflectedBy(FlameRig.Player, new Vector(0f, 1000f, 0f));
        Assert.Equal(1, rig.Stickybomb.DeflectedCount);

        rig.Engine.Returns("Clock.Now", 7f);
        rig.Stickybomb.OnThink();
        Assert.Equal(1, rig.Stickybomb.DeflectedCount);

        rig.Engine.Returns("Clock.Now", 8f);
        rig.Stickybomb.OnThink();
        Assert.Equal(0, rig.Stickybomb.DeflectedCount);
    }

    [Fact]
    public void An_enemy_is_pushed_by_the_cray_force()
    {
        // Standing still: no momentum to reflect, the whole 600 pushes, along
        // the aim with the direction to the target's yaw — the target is due
        // +Y of the blaster, so the push is (0, 600, 0). The cone retest sees
        // the hull inside it at (128, 0, 0).
        var rig = new FlameRig();
        rig.Directory.Instance = null;
        rig.Engine.FillsSphere(FlameRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        InsideCone(rig);
        Teams(rig, targetTeam: 3, blasterTeam: 2);
        Centers(rig, targetCenter: new Vector(0f, 512f, 64f), blasterCenter: new Vector(0f, 0f, 64f));

        rig.Effects.AirBlast(Attack(), Script());

        var pushed = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Assert.Equal(FlameRig.Victim, pushed!.ArgAs<EntityHandle>(0));
        Vector impulse = pushed.ArgAs<Vector>(1);
        Assert.Equal(0f, impulse.X, 2f);
        Assert.Equal(600f, impulse.Y, 2f);
        Assert.Equal(0f, impulse.Z, 2f);
        Assert.True(pushed.ArgAs<bool>(2)); // off the ground, as stock's FL_ONGROUND strip is

        Assert.Contains(rig.Conditions.Added, a => a.Player == FlameRig.Victim && a.Cond == TFCond.AirCurrent);
        Assert.Contains(rig.Conditions.Added, a => a.Player == FlameRig.Victim && a.Cond == TFCond.KnockedIntoAir);
        var footing = rig.Conditions.Added.Single(a => a.Cond == TFCond.LostFooting);
        Assert.Equal(TFAirBlastDefs.LoseFootingDuration, footing.Duration);
    }

    [Fact]
    public void A_target_running_at_the_blaster_is_reflected_first()
    {
        // Coming at the blaster along the aim at 100: the reflect is
        // 2 * 100 = 200 of push, which costs 100 of the power, leaving 500 —
        // 700 of push along the aim in all. The cone retest sees the hull
        // inside it at (128, 0, 0).
        var rig = new FlameRig();
        rig.Directory.Instance = null;
        rig.Engine.FillsSphere(FlameRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        InsideCone(rig);
        Teams(rig, targetTeam: 3, blasterTeam: 2);
        Centers(rig, targetCenter: new Vector(512f, 0f, 64f), blasterCenter: new Vector(0f, 0f, 64f));
        rig.Engine.Returns("Entities.GetVelocity", new Vector(-100f, 0f, 0f));

        rig.Effects.AirBlast(Attack(), Script());

        var pushed = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Vector impulse = pushed!.ArgAs<Vector>(1);
        Assert.Equal(700f, impulse.X, 1f);
        Assert.Equal(0f, impulse.Y, 1f);
        Assert.Equal(0f, impulse.Z, 1f);
    }

    [Fact]
    public void The_cray_power_wears_tf_pushbackscalescale()
    {
        // The debug scale lands in the power before the push
        // (tf_weapon_flamethrower.cpp:1330-1331): 2 doubles it, and a
        // standing target takes the whole 1200 along the aim.
        var rig = new FlameRig();
        rig.Convars.Declared<TFPushbackScaleConVar>().Bind(-1, "tf_pushbackscalescale", "2");
        rig.Directory.Instance = null;
        rig.Engine.FillsSphere(FlameRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        InsideCone(rig);
        Teams(rig, targetTeam: 3, blasterTeam: 2);
        Centers(rig, targetCenter: new Vector(0f, 512f, 64f), blasterCenter: new Vector(0f, 0f, 64f));

        rig.Effects.AirBlast(Attack(), Script());

        var pushed = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Vector impulse = pushed!.ArgAs<Vector>(1);
        Assert.Equal(0f, impulse.X, 2f);
        Assert.Equal(1200f, impulse.Y, 2f);
        Assert.Equal(0f, impulse.Z, 2f);
    }

    [Fact]
    public void The_cray_result_z_wears_tf_pushbackscalescale_vertical()
    {
        // The vertical scale lands once the ground work is done
        // (:1574-1577): the mirrored 300 of Z leaves as 600 under 2, the
        // across-push untouched.
        var rig = new FlameRig();
        rig.Convars.Declared<TFPushbackScaleVerticalConVar>().Bind(-1, "tf_pushbackscalescale_vertical", "2");
        rig.Directory.Instance = null;
        rig.Engine.FillsSphere(FlameRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        InsideCone(rig);
        rig.Engine.Returns("Entities.GetEyeAngles", new Vector(30f, 0f, 0f));
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("WorldQuery.TraceBox",
                           new SurfaceTraceResult(true, 1f, default, new Vector(0f, 0f, 1f), FlameRig.Victim, 0, 0));
        Teams(rig, targetTeam: 3, blasterTeam: 2);
        Centers(rig, targetCenter: new Vector(128f, 0f, 64f), blasterCenter: new Vector(0f, 0f, 64f));

        rig.Effects.AirBlast(Attack(), Script());

        var pushed = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Vector impulse = pushed!.ArgAs<Vector>(1);
        Assert.Equal(519.6f, impulse.X, 1f);
        Assert.Equal(0f, impulse.Y, 1f);
        Assert.Equal(600f, impulse.Z, 1f);
    }

    [Fact]
    public void A_player_in_the_sphere_but_outside_the_cone_is_not_pushed()
    {
        // The blaster eyes (0, 0, 64) down +X: the cone reaches 256 ahead with
        // a half-angle of 35°, so its widest radius is 256·tan35° ≈ 179. The
        // target's hull sits at x ≥ 112 with y ≥ 104 — its nearest point to
        // the axis is 104 out, past the cone's 100.8 there — inside the
        // sphere's faked answer, outside the cone.
        var rig = new FlameRig();
        rig.Directory.Instance = null;
        rig.Engine.FillsSphere(FlameRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetOrigin", new Vector(128f, 120f, 0f));
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)));
        Teams(rig, targetTeam: 3, blasterTeam: 2);

        rig.Effects.AirBlast(Attack(), Script());

        Assert.Null(rig.Engine.Last("Entities.ApplyVelocityImpulse"));
        Assert.Empty(rig.Conditions.Added);
    }

    [Fact]
    public void A_player_inside_the_cone_is_pushed()
    {
        // The same hull at (128, 0, 0) carries the axis point (128, 0, 64):
        // inside the sphere and inside the cone, so the push lands — 600 down
        // the aim with the target's yaw, which the centers make +X.
        var rig = new FlameRig();
        rig.Directory.Instance = null;
        rig.Engine.FillsSphere(FlameRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetOrigin", new Vector(128f, 0f, 0f));
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)));
        Teams(rig, targetTeam: 3, blasterTeam: 2);
        Centers(rig, targetCenter: new Vector(128f, 0f, 64f), blasterCenter: new Vector(0f, 0f, 64f));

        rig.Effects.AirBlast(Attack(), Script());

        var pushed = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Vector impulse = pushed!.ArgAs<Vector>(1);
        Assert.Equal(600f, impulse.X, 2f);
        Assert.Equal(0f, impulse.Y, 2f);
        Assert.Equal(0f, impulse.Z, 2f);
    }

    [Fact]
    public void A_push_into_the_ground_reflects_off_it()
    {
        // Aiming 30° down at a standing target on a flat floor: the 600 push
        // runs (519.6, 0, −300), into the ground, so the floor normal
        // (0, 0, 1) mirrors the 300 of into-ground momentum back up —
        // (519.6, 0, 300). The target is on the ground (EntityGetState).
        var rig = new FlameRig();
        rig.Directory.Instance = null;
        rig.Engine.FillsSphere(FlameRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        InsideCone(rig);
        rig.Engine.Returns("Entities.GetEyeAngles", new Vector(30f, 0f, 0f));
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("WorldQuery.TraceBox",
                           new SurfaceTraceResult(true, 1f, default, new Vector(0f, 0f, 1f), FlameRig.Victim, 0, 0));
        Teams(rig, targetTeam: 3, blasterTeam: 2);
        Centers(rig, targetCenter: new Vector(128f, 0f, 64f), blasterCenter: new Vector(0f, 0f, 64f));

        rig.Effects.AirBlast(Attack(), Script());

        var pushed = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Vector impulse = pushed!.ArgAs<Vector>(1);
        Assert.Equal(519.6f, impulse.X, 1f);
        Assert.Equal(0f, impulse.Y, 1f);
        Assert.Equal(300f, impulse.Z, 1f);
    }

    [Fact]
    public void A_grounded_push_is_lifted_to_the_minimum_z()
    {
        // A level blast at a standing target pushes (600, 0, 0): no into-ground
        // momentum to reflect, but the 100 of tf_airblast_cray_ground_minz has
        // to come from somewhere, so the XY pays for it and the redirect keeps
        // the magnitude — 600·√(35/36) = 591.6 across, 100 up.
        var rig = new FlameRig();
        rig.Directory.Instance = null;
        rig.Engine.FillsSphere(FlameRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        InsideCone(rig);
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("WorldQuery.TraceBox",
                           new SurfaceTraceResult(true, 1f, default, new Vector(0f, 0f, 1f), FlameRig.Victim, 0, 0));
        Teams(rig, targetTeam: 3, blasterTeam: 2);
        Centers(rig, targetCenter: new Vector(128f, 0f, 64f), blasterCenter: new Vector(0f, 0f, 64f));

        rig.Effects.AirBlast(Attack(), Script());

        var pushed = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Vector impulse = pushed!.ArgAs<Vector>(1);
        Assert.Equal(591.6f, impulse.X, 1f);
        Assert.Equal(0f, impulse.Y, 1f);
        Assert.Equal(100f, impulse.Z, 1f);
    }

    [Fact]
    public void An_airborne_target_gets_neither_ground_step()
    {
        // Off the ground: no ground trace, no lift — the whole 600 pushes level.
        var rig = new FlameRig();
        rig.Directory.Instance = null;
        rig.Engine.FillsSphere(FlameRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        InsideCone(rig);
        rig.Engine.Returns("Networking.EntityGetState", EntityState.None);
        Teams(rig, targetTeam: 3, blasterTeam: 2);
        Centers(rig, targetCenter: new Vector(128f, 0f, 64f), blasterCenter: new Vector(0f, 0f, 64f));

        rig.Effects.AirBlast(Attack(), Script());

        var pushed = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Vector impulse = pushed!.ArgAs<Vector>(1);
        Assert.Equal(600f, impulse.X, 1f);
        Assert.Equal(0f, impulse.Y, 1f);
        Assert.Equal(0f, impulse.Z, 1f);
        Assert.Equal(0, rig.Engine.CountOf("WorldQuery.TraceBox"));
    }

    [Fact]
    public void A_burning_teammate_is_put_out_and_not_pushed()
    {
        var rig = new FlameRig();
        rig.Directory.Instance = null;
        rig.Engine.FillsSphere(FlameRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        Teams(rig, targetTeam: 2, blasterTeam: 2); // the same team
        rig.Conditions.Grant(FlameRig.Victim, TFCond.Burning);

        rig.Effects.AirBlast(Attack(), Script());

        var removed = Assert.Single(rig.Conditions.Removed);
        Assert.Equal(FlameRig.Victim, removed.Player);
        Assert.Equal(TFCond.Burning, removed.Cond);
        Assert.Null(rig.Engine.Last("Entities.ApplyVelocityImpulse"));
    }

    [Fact]
    public void The_cray_convar_at_zero_pushes_the_old_way()
    {
        // tf_airblast_cray 0, the old arm (:1764-1830): the stun — the target
        // has no knocked-into-air yet — the AirBurstDamageForce push
        // (60·(48·48·82 / 32·32·72)·6 = 922.5, under the cap) along the aim,
        // plus the burst's 350 of lift, the velocity stomped and the push
        // re-founded on the cond.
        var rig = new FlameRig();
        rig.Convars.Declared<TFAirBlastCrayConVar>().Bind(-1, "tf_airblast_cray", "0");
        var stun = rig.AttachStun(FlameRig.Victim);
        rig.Directory.Instance = null;
        rig.Engine.FillsSphere(FlameRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        InsideCone(rig);
        Teams(rig, targetTeam: 3, blasterTeam: 2);
        Centers(rig, targetCenter: new Vector(0f, 512f, 64f), blasterCenter: new Vector(0f, 0f, 64f));

        rig.Effects.AirBlast(Attack(), Script());

        var stomped = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(stomped);
        Assert.Equal(FlameRig.Victim, stomped!.ArgAs<EntityHandle>(0));
        Assert.Equal(default(Vector), stomped.ArgAs<Vector>(1));

        var pushed = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Vector impulse = pushed!.ArgAs<Vector>(1);
        Assert.Equal(0f, impulse.X, 2f);
        Assert.Equal(922.5f, impulse.Y, 2f);
        Assert.Equal(350f, impulse.Z, 2f);
        Assert.True(pushed.ArgAs<bool>(2));

        Assert.Contains(rig.Conditions.Added, a => a.Player == FlameRig.Victim && a.Cond == TFCond.KnockedIntoAir);
        Assert.DoesNotContain(rig.Conditions.Added, a => a.Cond == TFCond.AirCurrent);
        Assert.DoesNotContain(rig.Conditions.Added, a => a.Cond == TFCond.LostFooting);

        (float Duration, float Amount) = (stun.Stuns[0].Duration, stun.Stuns[0].Amount);
        Assert.Equal(TFAirBlastDefs.OldAirblastStunDuration, Duration);
        Assert.Equal(255f, Amount);   // the 1.0 reduction, remapped 0..255
    }

    [Fact]
    public void The_old_push_and_lift_wear_the_debug_scales()
    {
        // The old arm's scales (:1789, :1811): the push runs
        // 2 · 922.5 = 1845 across, the lift 2 · 350 = 700 up.
        var rig = new FlameRig();
        rig.Convars.Declared<TFAirBlastCrayConVar>().Bind(-1, "tf_airblast_cray", "0");
        rig.Convars.Declared<TFPushbackScaleConVar>().Bind(-1, "tf_pushbackscalescale", "2");
        rig.Convars.Declared<TFPushbackScaleVerticalConVar>().Bind(-1, "tf_pushbackscalescale_vertical", "2");
        rig.AttachStun(FlameRig.Victim);
        rig.Directory.Instance = null;
        rig.Engine.FillsSphere(FlameRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        InsideCone(rig);
        Teams(rig, targetTeam: 3, blasterTeam: 2);
        Centers(rig, targetCenter: new Vector(0f, 512f, 64f), blasterCenter: new Vector(0f, 0f, 64f));

        rig.Effects.AirBlast(Attack(), Script());

        var pushed = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Vector impulse = pushed!.ArgAs<Vector>(1);
        Assert.Equal(0f, impulse.X, 2f);
        Assert.Equal(1845f, impulse.Y, 2f);
        Assert.Equal(700f, impulse.Z, 2f);
    }

    [Fact]
    public void The_old_push_never_leaves_a_ground_less_z_than_the_jump()
    {
        // The generic impulse's floor (tf_player.cpp:3388-3392): a grounded
        // target's push Z under JUMP_MIN_SPEED becomes it — the burst dialed
        // to 100, under the 268.3281572999747 floor.
        var rig = new FlameRig();
        rig.Convars.Declared<TFAirBlastCrayConVar>().Bind(-1, "tf_airblast_cray", "0");
        rig.Convars.Declared<TFFlameThrowerBurstZvelocityConVar>().Bind(-1, "tf_flamethrower_burst_zvelocity", "100");
        rig.AttachStun(FlameRig.Victim);
        rig.Directory.Instance = null;
        rig.Engine.FillsSphere(FlameRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        InsideCone(rig);
        Teams(rig, targetTeam: 3, blasterTeam: 2);
        Centers(rig, targetCenter: new Vector(0f, 512f, 64f), blasterCenter: new Vector(0f, 0f, 64f));

        rig.Effects.AirBlast(Attack(), Script());

        var pushed = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Vector impulse = pushed!.ArgAs<Vector>(1);
        Assert.Equal(922.5f, impulse.Y, 2f);
        Assert.Equal(TFAirBlastDefs.JumpMinSpeed, impulse.Z, 4f);
    }

    [Fact]
    public void A_teammate_who_is_not_burning_gets_nothing()
    {
        var rig = new FlameRig();
        rig.Directory.Instance = null;
        rig.Engine.FillsSphere(FlameRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        Teams(rig, targetTeam: 2, blasterTeam: 2);

        rig.Effects.AirBlast(Attack(), Script());

        Assert.Empty(rig.Conditions.Removed);
        Assert.Empty(rig.Conditions.Added);
    }

    [Fact]
    public void The_blaster_themself_is_skipped()
    {
        var rig = new FlameRig();
        rig.Directory.Instance = null;
        rig.Engine.FillsSphere(FlameRig.Player);
        rig.Engine.Returns("Entities.IsPlayer", true);

        rig.Effects.AirBlast(Attack(), Script());

        Assert.Null(rig.Engine.Last("Entities.ApplyVelocityImpulse"));
        Assert.Null(rig.Engine.Last("Entities.SetVelocity"));
    }

    private static WeaponAttackContext Attack(int owner = 3)
        => new(default!, null!, null!, 1f, true, null!, new WeaponCommand(1, 5, owner));

    private static TFWeaponScript Script() => WeaponFixtures.Parse("tf_weapon_flamethrower");
}

/// <summary>
/// The cone test itself (<c>TFAirBlastCone</c>): a box against stock's
/// truncated cone, apex (0, 0, 64) down +X, 256 long with a 35° half-angle —
/// the airblast's own numbers (<c>tf_weapon_flamethrower.cpp:1685-1699</c>).
/// </summary>
public class TFAirBlastConeFacts
{
    private static readonly Vector Apex = new(0f, 0f, 64f);
    private static readonly Vector Axis = new(1f, 0f, 0f);

    private const float Height = 2f * TFAirBlastDefs.DeflectionRadius;
    private const float HalfAngle = TFAirBlastDefs.AirBlastConeAngle;

    /// <summary>The player hull, as WorldAlignMins/Maxs.</summary>
    private static readonly Vector HullMins = new(-16f, -16f, 0f);
    private static readonly Vector HullMaxs = new(16f, 16f, 72f);

    [Fact]
    public void ABoxCarryingAnAxisPointInsideIt_Intersects()
    {
        // The hull at (128, 0, 0) carries (128, 0, 64), on the axis 128 along
        // a 256 cone.
        Assert.True(TFAirBlastCone.BoxIntersects(Apex + HullMins + new Vector(128f, 0f, 0f),
                                                 Apex + HullMaxs + new Vector(128f, 0f, 0f),
                                                 Apex, Axis, Height, HalfAngle));
    }

    [Fact]
    public void ABoxBesideTheConeAtItsWidest_DoesNot()
    {
        // 184 to the side at the base: the cone there is only 179 wide.
        Assert.False(TFAirBlastCone.BoxIntersects(new Vector(240f, 184f, 0f), new Vector(272f, 216f, 72f),
                                                  Apex, Axis, Height, HalfAngle));
    }

    [Fact]
    public void ABoxBehindTheApex_DoesNot()
    {
        Assert.False(TFAirBlastCone.BoxIntersects(new Vector(-64f, -16f, 0f), new Vector(-32f, 16f, 72f),
                                                  Apex, Axis, Height, HalfAngle));
    }

    [Fact]
    public void ABoxBeyondTheBase_DoesNot()
    {
        Assert.False(TFAirBlastCone.BoxIntersects(new Vector(272f, -16f, 0f), new Vector(304f, 16f, 72f),
                                                  Apex, Axis, Height, HalfAngle));
    }

    [Fact]
    public void ABoxStraddlingTheBaseOfADownwardCone_Intersects()
    {
        // Straight down from the origin, 256 deep: a hull spanning z −260..−100
        // carries axis points from −100 to −256. The caps read the box's two
        // faces in axis order, not mins/maxs order.
        Assert.True(TFAirBlastCone.BoxIntersects(new Vector(-16f, -16f, -260f), new Vector(16f, 16f, -100f),
                                                 new Vector(0f, 0f, 0f), new Vector(0f, 0f, -1f),
                                                 Height, HalfAngle));
    }

    [Fact]
    public void ABoxInTheNarrowNearTheApexButOffAxis_DoesNot()
    {
        // 24 to 56 along, but never closer than 48 to the axis: the cone is
        // only 39 wide at the box's far end.
        Assert.False(TFAirBlastCone.BoxIntersects(new Vector(24f, 48f, 48f), new Vector(56f, 80f, 96f),
                                                  Apex, Axis, Height, HalfAngle));
    }
}
