using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// Each projectile's hit carries the weapon's own bits
/// (<see cref="TFWeaponDamageTypes"/>): the rocket's blast falls off by half,
/// the demoman's two launchers' is a half-falloff blast, the syringe gun's a
/// near-ramped bullet that pushes nobody.
/// </summary>
public sealed class TFProjectileEntityFacts
{
    /// <summary>A victim 60 units out, in the open, scored at the trace's end.</summary>
    private static ProjectileRig VictimAtSixty()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", default(Vector)); // the blast goes off here
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 1f, new Vector(60f, 0f, 0f), default, EntityHandle.None));
        return rig;
    }

    /// <summary>The same, 100 units out.</summary>
    private static ProjectileRig VictimAtHundred()
    {
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(100f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", default(Vector));
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 1f, new Vector(100f, 0f, 0f), default, EntityHandle.None));
        return rig;
    }

    /// <param name="member">the channel: the blast asks answered, a
    /// direct-on-touch hit (the syringe) takes the void one.</param>
    private static float SeenDamage(ProjectileRig rig,
                                    string member = "Damage.TakeDamageAnswered")
        => EngineCallFacts.ArgAs<float>(rig.Engine.Last(member), 2);

    /// <summary>The damage the blast dealt <paramref name="victim"/>, by handle.</summary>
    private static float DamageTo(ProjectileRig rig, EntityHandle victim)
        => EngineCallFacts.ArgAs<float>(rig.Engine.Calls.Last(
            c => c.Member == "Damage.TakeDamageAnswered" && victim.Equals(c.Args[0])), 2);

    /// <summary>A second player, exactly 100 from the blast along x.</summary>
    private static readonly EntityHandle SecondVictim = new(0x0001_0000u | 6);

    /// <summary>
    /// Per-handle positions on the blast's own height (source z is the
    /// origin's +1); anything unplaced — the attacker's own pass — sits at
    /// the source itself.
    /// </summary>
    private static void Place(ProjectileRig rig, EntityHandle a, Vector aAt, EntityHandle b, Vector bAt)
    {
        rig.Engine.ReturnsEachCall("Entities.GetWorldSpaceCenter", () =>
            rig.Engine.Calls[^1].Args[0] is not EntityHandle h ? new Vector(0f, 0f, 1f)
            : h == a ? aAt : h == b ? bAt : new Vector(0f, 0f, 1f));
        rig.Engine.ReturnsEachCall("Entities.GetOrigin", () =>
            rig.Engine.Calls[^1].Args[0] is not EntityHandle h ? new Vector(0f, 0f, 1f)
            : h == a ? aAt : h == b ? bAt : new Vector(0f, 0f, 1f));
    }

    private static DamageType SeenBits(ProjectileRig rig,
                                        string member = "Damage.TakeDamageAnswered")
        => EngineCallFacts.ArgAs<DamageType>(rig.Engine.Last(member), 3);

    // ==== The direct hit's stun (CheckForStunOnImpact,
    // tf_weaponbase_rocket.cpp:530-559): the touched enemy eats the owner's
    // rocket_specialist level of stun.

    /// <summary>
    /// A launched specialist rocket whose touch's enemy is the victim: the
    /// victim a player on the other team, everyone else the rocket's blue.
    /// </summary>
    private static TFRocket StunRig(ProjectileRig rig, int level)
    {
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim);
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim
                ? (int)TFTeam.Red : (int)TFTeam.Blue);
        // The shockwave's table entry, as the precache swept it.
        rig.Engine.Returns("Precache.ParticleSystemIndex", 7);
        rig.Engine.Returns("Entities.GetOrigin", StunAt);
        rig.Owners.Item = rig.AttributeItem("rocket_specialist", AttributeDescriptionFormat.Additive, level);

        var rocket = rig.Rocket();
        rocket.OnSpawn();
        rocket.ArmedBy(90f, TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher",
                       critical: false, ownerIndex: 3);
        rocket.FiredBy(ProjectileRig.Player, new Vector(1100f, 0f, 0f));
        return rocket;
    }

    /// <summary>Where the stun rig's rocket stands, for the effect's origin.</summary>
    private static readonly Vector StunAt = new(448f, 1368f, 320f);

    [Fact]
    public void The_stun_on_impact_shockwaves_at_the_rocket()
    {
        // CheckForStunOnImpact's last act (tf_weaponbase_rocket.cpp:556-558):
        // a CPVSFilter at GetAbsOrigin and the mvm_soldier_shockwave
        // ParticleEffect there, world-position form — no entity rides the
        // record, Start repeats the origin, the angles stand at zero.
        var rig = new ProjectileRig();
        rig.AttachStun(ProjectileRig.Victim);
        var rocket = StunRig(rig, level: 2);

        rocket.OnTouch(ProjectileRig.Victim);

        var sent = Assert.Single(rig.Temps.Effects);
        Assert.Equal("ParticleEffect", sent.Name);
        Assert.Equal(0f, sent.Delay);
        Assert.Equal(TeFilterKind.Pvs, sent.Filter.Kind);
        Assert.Equal(StunAt, sent.Filter.Origin);
        Assert.Equal(StunAt, sent.Data.Origin);
        Assert.Equal(StunAt, sent.Data.Start);
        Assert.Equal(default(Vector), sent.Data.Angles);
        Assert.Equal(7, sent.Data.HitBox);
        Assert.Equal(1f, sent.Data.Scale);
    }

    [Fact]
    public void The_direct_hit_stuns_the_enemy_for_the_levels_time()
    {
        var rig = new ProjectileRig();
        TFStunStack stuns = rig.AttachStun(ProjectileRig.Victim);
        var rocket = StunRig(rig, level: 2);

        rocket.OnTouch(ProjectileRig.Victim);

        // RemapValClamped(2, 1, 4, 0.5, 0.75) = 0.583333 s, full strength,
        // movement only and no effects, dealt by the owner
        // (tf_weaponbase_rocket.cpp:544-548).
        Assert.NotNull(stuns.ActiveStun);
        Assert.Equal(0.5833f, stuns.ActiveStun!.Duration, 4);
        Assert.Equal(255f, stuns.ActiveStun.Amount, 2);
        Assert.Equal(TFStunFlags.Movement | TFStunFlags.NoEffects, stuns.ActiveStun.Flags);
        Assert.Equal(ProjectileRig.Player, stuns.ActiveStun.Attacker);

        // The feet stop first (SetAbsVelocity(vec3_origin), :547).
        var stopped = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(stopped);
        Assert.Equal(ProjectileRig.Victim, stopped!.ArgAs<EntityHandle>(0));
        Assert.Equal(default(Vector), stopped.ArgAs<Vector>(1));
    }

    [Fact]
    public void The_level_four_stun_lasts_three_quarters_of_a_second()
    {
        var rig = new ProjectileRig();
        TFStunStack stuns = rig.AttachStun(ProjectileRig.Victim);
        var rocket = StunRig(rig, level: 4);

        rocket.OnTouch(ProjectileRig.Victim);

        Assert.Equal(0.75f, stuns.ActiveStun!.Duration, 4);
    }

    [Fact]
    public void The_splash_hit_stuns_nobody()
    {
        var rig = new ProjectileRig();
        TFStunStack stuns = rig.AttachStun(ProjectileRig.Victim);
        var rocket = StunRig(rig, level: 2);

        rocket.OnTouch(EntityHandle.None); // the wall

        Assert.Null(stuns.ActiveStun);
        Assert.Empty(rig.Temps.Effects); // no stun, no shockwave
    }

    [Fact]
    public void The_rocket_without_the_specialist_stuns_nobody()
    {
        var rig = new ProjectileRig();
        TFStunStack stuns = rig.AttachStun(ProjectileRig.Victim);
        var rocket = StunRig(rig, level: 0);

        rocket.OnTouch(ProjectileRig.Victim);

        Assert.Null(stuns.ActiveStun);
        Assert.Empty(rig.Temps.Effects); // no stun, no shockwave
    }

    [Fact]
    public void The_rockets_touch_explodes_once_at_the_launchers_bits()
    {
        var rig = VictimAtSixty();
        var rocket = rig.Rocket();
        rocket.OnSpawn();
        rocket.ArmedBy(90f, TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher",
                       critical: false, ownerIndex: 0);
        rocket.FiredBy(ProjectileRig.Player, new Vector(1100f, 0f, 0f));

        rocket.OnTouch(EntityHandle.None); // the wall

        Assert.Equal(71.5f, SeenDamage(rig), 1f);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Rocketlauncher), SeenBits(rig));
        // The touch removes it once; the explosion does not remove it again.
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void The_pipes_fuse_explodes_at_the_demomans_grenade_bits()
    {
        var rig = VictimAtSixty();
        var pipe = rig.Pipebomb();
        pipe.OnSpawn();
        pipe.ThrownBy(ProjectileRig.Player, new Vector(1200f, 0f, 0f), 90f, 146f,
                      TFWeaponId.Grenadelauncher, "tf_weapon_grenadelauncher", critical: false);

        pipe.OnThink(); // the fuse

        // 60% of 90 at the fuse, half kept at the edge: 54 - 27 * 60/146.
        Assert.Equal(42.9f, SeenDamage(rig), 1f);
        // GetWeaponID answers TF_WEAPON_GRENADE_DEMOMAN for a regular pipe
        // (tf_weapon_grenade_pipebomb.cpp:142), whose
        // g_aWeaponDamageTypes row is a half-falloff blast with no
        // DMG_USEDISTANCEMOD (tf_shareddefs.cpp:770).
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.GrenadeDemoman), SeenBits(rig));
    }

    // ==== The touch's enemy test (PipebombTouch, tf_weapon_grenade_pipebomb.cpp:759):
    // an enemy the game will not damage (m_takedamage == DAMAGE_NO) is passed by.

    /// <summary>
    /// The toucher answers red and everyone else blue: an enemy touch whose
    /// own team the thrower's blue, read per call so the throw-side team
    /// write still sees the thrower.
    /// </summary>
    private static void EnemyTouch(ProjectileRig rig)
    {
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim
                ? (int)TFTeam.Red : (int)TFTeam.Blue);
    }

    /// <summary>A thrown pipe, frozen at the enemy's feet: 90 over 146.</summary>
    private static TFPipebomb ThrownPipe(ProjectileRig rig)
    {
        var pipe = rig.Pipebomb();
        pipe.OnSpawn();
        pipe.ThrownBy(ProjectileRig.Player, new Vector(1200f, 0f, 0f), 90f, 146f,
                      TFWeaponId.Grenadelauncher, "tf_weapon_grenadelauncher", critical: false);
        return pipe;
    }

    /// <summary>A managed toucher answering for the enemy, damage-taking as the fact says.</summary>
    private sealed class TouchTarget(bool takesDamage) : ScriptedEntity
    {
        public override bool TakesDamage => takesDamage;
    }

    [Fact]
    public void The_pipes_touch_explodes_on_an_enemy_player()
    {
        // A player's m_takedamage is always damage-taking, so the player arm
        // needs no managed half to ask (:759 passes on IsPlayer alone).
        var rig = VictimAtSixty();
        EnemyTouch(rig);
        rig.Engine.Returns("Entities.IsPlayer", true);
        var pipe = ThrownPipe(rig);

        pipe.OnTouch(ProjectileRig.Victim);

        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void The_pipes_touch_explodes_on_a_hurtable_enemy_prop()
    {
        var rig = new ProjectileRig();
        EnemyTouch(rig);
        rig.Directory.Instance = new TouchTarget(true);
        var pipe = ThrownPipe(rig);

        pipe.OnTouch(ProjectileRig.Victim);

        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void The_pipes_touch_passes_an_enemy_that_takes_no_damage()
    {
        // The team alone is not the stock test: an enemy with no managed half
        // to answer for its m_takedamage is not "an enemy we can damage".
        var rig = new ProjectileRig();
        EnemyTouch(rig);
        var pipe = ThrownPipe(rig);

        pipe.OnTouch(ProjectileRig.Victim);

        Assert.Equal(0, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void The_pipes_touch_passes_an_enemy_prop_that_takes_none()
    {
        var rig = new ProjectileRig();
        EnemyTouch(rig);
        rig.Directory.Instance = new TouchTarget(false);
        var pipe = ThrownPipe(rig);

        pipe.OnTouch(ProjectileRig.Victim);

        Assert.Equal(0, rig.Engine.CountOf("Entities.Remove"));
    }

    /// <summary><c>SURF_SKY</c> (<c>bspflags.h:82</c>), as Bolt and the core rocket name it.</summary>
    private const int SurfSky = 0x0004;

    [Fact]
    public void The_pipe_disappears_into_the_skybox()
    {
        // PipebombTouch :730-742: a trace along the velocity — from 32 behind
        // to 32 ahead, MASK_SOLID, the pipe ignored — and if it lands on the
        // skybox the pipe is gone: removed, not exploded.
        var rig = VictimAtSixty();
        EnemyTouch(rig);
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetVelocity", new Vector(1200f, 0f, 0f));
        rig.Engine.Returns("WorldQuery.TraceLineSurface",
                           new SurfaceTraceResult(true, 0.5f, new Vector(0f, 0f, 320f),
                                                  new Vector(0f, 0f, 1f), EntityHandle.None, 0, SurfSky));
        var pipe = ThrownPipe(rig);

        pipe.OnTouch(ProjectileRig.Victim);

        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
    }

    [Fact]
    public void The_pipes_first_world_contact_scales_its_fuse_damage()
    {
        // VPhysicsCollision :883-885: the first world contact runs
        // SetDamage(GetDamageScaleOnWorldContact() * GetDamage()) — the
        // launcher item's grenade_damage_reduction_on_world_contact hook
        // (:1378-1386) — and the fuse goes off at that damage. The m_bTouched
        // gate makes the scaling happen once, not once per bounce.
        var rig = VictimAtSixty();
        rig.Owners.Item = rig.AttributeItem("grenade_damage_reduction_on_world_contact",
                                            AttributeDescriptionFormat.Percentage, 0.75f);
        var pipe = ThrownPipe(rig);

        pipe.OnTouch(EntityHandle.None);
        pipe.OnTouch(EntityHandle.None);
        pipe.OnThink(); // the fuse, at the scaled damage

        // 90 * 0.6 (the throw's timer reduction, :476) * 0.75 — a second
        // contact must not take the 0.75 again — half kept at the victim's
        // 60 over 146: 40.5 - 20.25 * 60/146.
        Assert.Equal(32.2f, SeenDamage(rig), 1f);
    }

    [Fact]
    public void The_pipes_touch_passes_an_enemy_after_the_world_was_touched()
    {
        // :753-754: once a surface has been touched the pipe is no longer an
        // on-contact bomb — a player walking into a landed pipe does not set
        // it off; it waits for its fuse.
        var rig = VictimAtSixty();
        EnemyTouch(rig);
        rig.Engine.Returns("Entities.IsPlayer", true);
        var pipe = ThrownPipe(rig);

        pipe.OnTouch(EntityHandle.None);    // the first world contact
        pipe.OnTouch(ProjectileRig.Victim); // then an enemy walks into it

        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
        Assert.Equal(0, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void The_lochs_shattering_pipe_fizzles_on_its_first_contact()
    {
        // VPhysicsCollision's shatter arm (tf_weapon_grenade_pipebomb.cpp:859-864):
        // the stamped pipe's first collision is Fizzle then Detonate, whose
        // m_bFizzle arm (:606-617) sparks and Destroys — no blast, no fuse.
        // The plain launcher's pipe takes the contact and waits for its fuse.
        var rig = VictimAtSixty();
        rig.Owners.Item = rig.AttributeItem("set_detonate_mode", AttributeDescriptionFormat.Additive, 2);
        var loch = ThrownPipe(rig);
        Assert.True(loch.WallShatter);

        loch.OnTouch(EntityHandle.None);

        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamageAnswered"));

        var plain = VictimAtSixty();
        var pipe = ThrownPipe(plain);
        Assert.False(pipe.WallShatter);

        pipe.OnTouch(EntityHandle.None);

        Assert.Equal(0, plain.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void The_launchers_explosion_radius_scales_the_pipes_blast()
    {
        // The pipe's GetDamageRadius is the base one
        // (tf_weaponbase_grenadeproj.cpp:137-142): the thrown radius under the
        // launcher's mult_explosion_radius — the Loch-n-Load's 0.75 of 90 is
        // 109.5, so the fuse's 54 at 60 out keeps 54 - 27 * 60/109.5.
        var rig = VictimAtSixty();
        rig.Owners.Item = rig.AttributeItem("mult_explosion_radius",
                                            AttributeDescriptionFormat.Percentage, 0.75f);
        var pipe = ThrownPipe(rig);

        pipe.OnThink(); // the fuse

        Assert.Equal(39.2f, SeenDamage(rig), 1f);
    }

    [Fact]
    public void The_iron_bombers_fuse_is_the_shorter_one()
    {
        // Spawn's fuse booking (tf_weapon_grenade_pipebomb.cpp:521) through
        // SetDetonateTimerLength (tf_weaponbase_grenadeproj.cpp:481-490): the
        // 2 s fuse under the launcher's fuse_mult — the Iron Bomber's 0.7 —
        // books the think at 1.4.
        var rig = new ProjectileRig();
        rig.Owners.Item = rig.AttributeItem("fuse_mult", AttributeDescriptionFormat.Percentage, 0.7f);
        var pipe = rig.Pipebomb();
        var thinks = new ThinkScheduler();
        pipe.BindThinks(thinks);

        pipe.ThrownBy(ProjectileRig.Player, new Vector(1200f, 0f, 0f), 90f, 146f,
                      TFWeaponId.Grenadelauncher, "tf_weapon_grenadelauncher", critical: false);

        Assert.Equal(1.4f, Assert.Single(thinks.Bookings()).Time, 4);
    }

    [Fact]
    public void The_iron_bombers_pipe_sinks_its_first_bounce()
    {
        // VPhysicsCollision's no-bounce arm (tf_weapon_grenade_pipebomb.cpp:887-899):
        // grenade_no_bounce on the launcher — the Iron Bomber's — takes the
        // first world contact's bounce to a tenth, the fuse damage scale
        // beside it untouched.
        var rig = VictimAtSixty();
        rig.Owners.Item = rig.AttributeItem("grenade_no_bounce", AttributeDescriptionFormat.Additive, 1);
        var pipe = ThrownPipe(rig);
        rig.Engine.Returns("Entities.GetVelocity", new Vector(600f, 0f, 200f));
        rig.Engine.Returns("WorldQuery.TraceLineSurface",
                           new SurfaceTraceResult(true, 1f, new Vector(600f, 0f, 200f),
                                                  new Vector(0f, 0f, 1f), EntityHandle.None, 0, 0));

        pipe.OnTouch(EntityHandle.None);

        var slowed = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(slowed);
        Assert.Equal(60f, slowed!.ArgAs<Vector>(1).X, 1);
    }

    // ==== The Loose Cannon's ball (PipebombTouch's TF_GL_MODE_CANNONBALL arm,
    // tf_weapon_grenade_pipebomb.cpp:772-817) ====

    /// <summary>The thrown ball: cannonball mode, the cannon's name.</summary>
    private static TFPipebomb ThrownCannonball(ProjectileRig rig)
    {
        var ball = rig.Pipebomb();
        ball.OnSpawn();
        ball.ThrownBy(ProjectileRig.Player, new Vector(1200f, 0f, 0f), 90f, 146f,
                      TFWeaponId.Cannon, "tf_weapon_cannonball", critical: false,
                      TFProjectileDefs.GrenadeModeCannonball);
        return ball;
    }

    [Fact]
    public void The_cannonball_is_the_reshelled_pipe()
    {
        // Spawn's CANNONBALL arm (tf_weapon_grenade_pipebomb.cpp:513-516):
        // the pipe entity carries w_cannonball and the mode stamp the touch
        // and the blast bits read.
        var rig = new ProjectileRig();
        var ball = ThrownCannonball(rig);

        Assert.Equal(TFProjectileDefs.GrenadeModeCannonball, ball.PipebombMode);
        var shell = rig.Engine.Last("EntityRender.SetModel");
        Assert.NotNull(shell);
        Assert.Equal(TFProjectileDefs.CannonballModel, shell!.ArgAs<string>(1));
    }

    [Fact]
    public void The_cannonballs_touch_pushes_the_enemy_and_flies_on()
    {
        // The impact (:776-817): the wound at the distance's remap — the
        // centres 300 apart, the near end's 50 — on the cannon's row, the
        // ground stun and the shove 400 out and 350 up; no blast, the ball
        // flies on (:816), and the second touch on the same body is neither
        // (:779's already-through).
        var rig = new ProjectileRig();
        EnemyTouch(rig);
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.ReturnsEachCall("Entities.GetOrigin", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim
                ? new Vector(300f, 0f, 0f) : default(Vector));
        rig.Engine.ReturnsEachCall("Entities.GetWorldSpaceCenter", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim
                ? new Vector(300f, 0f, 0f) : default(Vector));
        rig.Engine.Returns("Entities.GetBounds",
                           (new Vector(-24f, -24f, 0f), new Vector(24f, 24f, 82f)));
        TFStunStack stuns = rig.AttachStun(ProjectileRig.Victim);
        var ball = ThrownCannonball(rig);

        ball.OnTouch(ProjectileRig.Victim);

        Assert.Equal(50f, SeenDamage(rig, "Damage.TakeDamage"), 1);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Cannon), SeenBits(rig, "Damage.TakeDamage"));
        Assert.NotNull(stuns.ActiveStun);
        Assert.Equal(0.5f, stuns.ActiveStun!.Duration, 3);
        var push = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(push);
        Assert.Equal(new Vector(400f, 0f, 350f), push!.ArgAs<Vector>(1));
        Assert.Equal(0, rig.Engine.CountOf("Entities.Remove"));

        ball.OnTouch(ProjectileRig.Victim);

        Assert.Equal(1, rig.Engine.CountOf("Damage.TakeDamage"));
    }

    [Fact]
    public void The_push_opens_the_donks_window_on_the_shoved_victim()
    {
        // AddDonkVictim (:808-814): the push enters the victim on the
        // thrower's list for tf_double_donk_window — and the second touch, the
        // already-through arm, adds nothing new to it.
        var rig = new ProjectileRig();
        EnemyTouch(rig);
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.ReturnsEachCall("Entities.GetOrigin", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim
                ? new Vector(300f, 0f, 0f) : default(Vector));
        rig.Engine.ReturnsEachCall("Entities.GetWorldSpaceCenter", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim
                ? new Vector(300f, 0f, 0f) : default(Vector));
        rig.Engine.Returns("Entities.GetBounds",
                           (new Vector(-24f, -24f, 0f), new Vector(24f, 24f, 82f)));
        rig.AttachStun(ProjectileRig.Victim);
        rig.Engine.Returns("Clock.Now", 10f);
        var donks = new TFDonkVictims(rig.Carriers, rig.Engine, DeclaredConVars.Fresh<TFDoubleDonkWindowConVar>());
        donks.Attach(ProjectileRig.Player);
        var ball = ThrownCannonball(rig);

        ball.OnTouch(ProjectileRig.Victim);

        Assert.True(donks.Donked(ProjectileRig.Victim));

        rig.Engine.Returns("Clock.Now", 10.6f);

        Assert.False(donks.Donked(ProjectileRig.Victim));
    }

    [Fact]
    public void The_cannonball_explodes_on_the_hostile_thing_the_push_skips()
    {
        // The arm's own gate (:776): no thrower or a victim that is neither
        // player nor building, and the touch falls through to the blast every
        // pipe takes (:820-826).
        var rig = VictimAtSixty();
        EnemyTouch(rig);
        rig.Directory.Instance = new TouchTarget(true);
        var ball = ThrownCannonball(rig);

        ball.OnTouch(ProjectileRig.Victim);

        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Cannon), SeenBits(rig));
    }

    [Fact]
    public void The_cannonballs_blast_carries_the_cannons_bits()
    {
        // GetWeaponID (tf_weapon_grenade_pipebomb.cpp:137-140): the ball's
        // blast is TF_WEAPON_CANNON's row, where the plain pipe's is the
        // grenade's (:142).
        var rig = VictimAtSixty();
        var ball = ThrownCannonball(rig);

        ball.OnThink(); // the fuse

        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Cannon), SeenBits(rig));
    }

    [Fact]
    public void The_cannonballs_charged_fuse_is_the_window_left()
    {
        // FireProjectileInternal's charged fuse
        // (tf_weapon_grenadelauncher.cpp:277-287): the held shot's remaining
        // window replaces the spawn's 2 s, the same SetDetonateTimerLength
        // under the launcher's fuse_mult.
        var rig = new ProjectileRig();
        var ball = rig.Pipebomb();
        var thinks = new ThinkScheduler();
        ball.BindThinks(thinks);
        ball.OnSpawn();
        ball.ThrownBy(ProjectileRig.Player, new Vector(1200f, 0f, 0f), 90f, 146f,
                      TFWeaponId.Cannon, "tf_weapon_cannonball", critical: false,
                      TFProjectileDefs.GrenadeModeCannonball);

        ball.ChargedFuse(1.5f);

        Assert.Equal(1.5f, Assert.Single(thinks.Bookings()).Time, 3);
    }

    [Fact]
    public void The_held_nothing_shot_goes_off_where_it_was_born()
    {
        // :281-286 — a window spent to none detonates the ball at once, at
        // the fuse damage it was born with. Stock's trace to the eye would
        // move the blast behind a wall; born at the muzzle, here it goes off
        // where it stands (the divergence ChargedFuse's doc carries).
        var rig = VictimAtSixty();
        var ball = ThrownCannonball(rig);

        ball.ChargedFuse(0f);

        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
        Assert.Equal(42.9f, SeenDamage(rig), 1);
    }

    [Fact]
    public void The_stickys_detonation_carries_the_pipebomb_launchers_bits()
    {
        var rig = VictimAtSixty();
        var sticky = rig.Sticky();
        sticky.OnSpawn();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        sticky.OnThink(); // arms

        sticky.Detonate();

        // Never touched the world, so the blast comes in at the air-burst
        // scale (GetDamageRadius, tf_weapon_grenade_pipebomb.cpp:1417-1434):
        // 146 * 0.85 = 124.1, and 90 - 45 * 60/124.1.
        Assert.Equal(68.24f, SeenDamage(rig), 1f);
        // GetDamageType (tf_weapon_grenade_pipebomb.cpp:148-162): a
        // remote-detonate pipe within 5 s of its creation ORs
        // DMG_USEDISTANCEMOD onto its row — this sticky was thrown the
        // command before it went, so it is young.
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Pipebomblauncher) | TFDamageBits.UseDistanceMod,
                     SeenBits(rig));
    }

    [Fact]
    public void The_stickybomb_jumpers_practice_bomb_detonates_on_the_bare_row()
    {
        // GetDamageType's gate (tf_weapon_grenade_pipebomb.cpp:153): only the
        // remote-detonate mode ORs DMG_USEDISTANCEMOD — the jumper's practice
        // bomb (TF_GL_MODE_REMOTE_DETONATE_PRACTICE, Create :446-450) reads
        // the bare row however young.
        var rig = VictimAtSixty();
        var sticky = rig.Sticky();
        sticky.OnSpawn();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false,
                        TFProjectileDefs.GrenadeModeRemoteDetonatePractice);
        sticky.OnThink(); // arms

        sticky.Detonate();

        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Pipebomblauncher), SeenBits(rig));
    }

    [Fact]
    public void The_syringes_touch_hurts_with_the_syringe_guns_bits()
    {
        var rig = new ProjectileRig();
        var syringe = rig.Syringe();
        syringe.OnSpawn();
        syringe.ArmedBy(TFWeaponId.SyringegunMedic, "tf_weapon_syringegun_medic", critical: false);

        syringe.OnTouch(ProjectileRig.Victim);

        // The syringe's hurt is direct on touch, not a blast: the void channel.
        Assert.Equal(TFProjectileDefs.SyringeDamage,
                     SeenDamage(rig, "Damage.TakeDamage"), 1f);
        // A bullet that ramps up near and applies no physics force.
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.SyringegunMedic),
                     SeenBits(rig, "Damage.TakeDamage"));
    }

    [Fact]
    public void The_rockets_blast_is_stocks_radius_even_when_the_script_names_one()
    {
        // CTFBaseRocket::GetRadius (tf_weaponbase_rocket.cpp:579-582) is
        // TF_ROCKET_RADIUS, scaled only by the mult_explosion_radius
        // attribute — the weapon script's DamageRadius is not a source. A
        // script naming 200 still blasts at the stock 146: the 60-out victim
        // keeps 90 - 45 * 60/146 = 71.5.
        var rig = VictimAtSixty();
        var rocket = rig.Rocket();
        rocket.OnSpawn();
        rocket.ArmedBy(90f, TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false, ownerIndex: 3);
        rocket.FiredBy(ProjectileRig.Player, new Vector(1100f, 0f, 0f));

        rocket.OnTouch(EntityHandle.None);

        Assert.Equal(71.5f, SeenDamage(rig), 1f);
    }

    [Fact]
    public void The_launchers_explosion_radius_attribute_scales_the_blast()
    {
        // GetRadius (tf_weaponbase_rocket.cpp:581-582): mult_explosion_radius
        // on the launcher, under TF_ROCKET_RADIUS. 0.3 of it — the Direct
        // Hit's value — is 43.8, which does not reach a player whose box
        // starts 76 out (the sphere query is a box test, and stock's
        // CalcNearestPoint retest is what spares them).
        var rig = VictimAtHundred();
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-24f, -24f, 0f), new Vector(24f, 24f, 82f)));
        Place(rig, ProjectileRig.Victim, new Vector(100f, 0f, 1f),
              ProjectileRig.Victim, new Vector(100f, 0f, 1f));
        rig.Owners.Item = rig.AttributeItem("mult_explosion_radius",
                                            AttributeDescriptionFormat.Percentage, 0.3f);
        var rocket = rig.Rocket();
        rocket.OnSpawn();
        rocket.ArmedBy(90f, TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false, ownerIndex: 3);
        rocket.FiredBy(ProjectileRig.Player, new Vector(1100f, 0f, 0f));

        rocket.OnTouch(EntityHandle.None);

        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
    }

    [Fact]
    public void A_refused_blast_hit_pushes_nobody()
    {
        // ApplyPushFromDamage runs inside OnTakeDamage_Alive (tf_player.cpp:10232):
        // damage the victim refuses never reaches the push, so a refused hit
        // pushes nobody. The seam answered nothing, so today a refusal still
        // pushed; TakeDamageAnswered answers what landed and a zero keeps the
        // player standing. The self pass is refused too (IsPlayer true puts
        // the attacker through its own pass), so no impulse goes out at all.
        var rig = VictimAtSixty();
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Damage.TakeDamageAnswered", 0);
        var rocket = rig.Rocket();
        rocket.OnSpawn();
        rocket.ArmedBy(90f, TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher",
                       critical: false, ownerIndex: 0);
        rocket.FiredBy(ProjectileRig.Player, new Vector(1100f, 0f, 0f));

        rocket.OnTouch(EntityHandle.None);

        // the stimulus reached it: the victim in the sweep and the attacker
        // in its own pass — both asks, both refused, neither pushed.
        Assert.Equal(2, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
        Assert.Equal(0, rig.Engine.CountOf("Entities.ApplyVelocityImpulse"));
    }

    [Fact]
    public void The_rocket_specialist_widens_a_direct_hit_blast()
    {
        // GetRadius's specialist arm (tf_weaponbase_rocket.cpp:587-599):
        // rocket_specialist on the owner and a direct hit on an enemy player
        // widen the blast by RemapValClamped(level, 1, 4, 1.15, 1.6) — level
        // 2 is 1.3, so 189.8. The direct hit itself is scored at distance
        // zero and keeps the full 90 either way; the widening shows on a
        // second victim 100 out, who the stock 146 leaves
        // 90 - 45 * 100/146 = 59.2 and the widened blast
        // 90 - 45 * 100/189.8 = 66.3.
        var rig = VictimAtSixty();
        rig.Engine.FillsSphere(ProjectileRig.Victim, SecondVictim);
        Place(rig, ProjectileRig.Victim, new Vector(60f, 0f, 1f),
              SecondVictim, new Vector(100f, 0f, 1f));
        // An integer hook attribute rides the float walk as an additive one
        // (stock's ApplyAttribute has no int format): 0 + 2 = level 2.
        rig.Owners.Item = rig.AttributeItem("rocket_specialist",
                                            AttributeDescriptionFormat.Additive, 2f);
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim
                ? (int)TFTeam.Red : (int)TFTeam.Blue);
        // The direct hit stuns, and the stun's shockwave asks the table.
        rig.Engine.Returns("Precache.ParticleSystemIndex", 7);
        var rocket = rig.Rocket();
        rocket.OnSpawn();
        rocket.ArmedBy(90f, TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false, ownerIndex: 3);
        rocket.FiredBy(ProjectileRig.Player, new Vector(1100f, 0f, 0f));

        rocket.OnTouch(ProjectileRig.Victim); // the direct hit

        Assert.Equal(90f, DamageTo(rig, ProjectileRig.Victim), 0.1f);
        Assert.Equal(66.29f, DamageTo(rig, SecondVictim), 0.1f);
    }

    /// <summary>
    /// A sticky in flight at 2400 u/s along +x from 448, the victim scored
    /// red and the thrower blue, and the thrower's own angular velocity to
    /// read back. The clock stands at 10, where <see cref="ProjectileRig.Sticky"/>
    /// leaves it — the same tick the bomb was thrown.
    /// </summary>
    private static (ProjectileRig Rig, TFStickybomb Sticky) StickyInFlight()
    {
        var rig = new ProjectileRig();
        var sticky = rig.Sticky();
        sticky.OnSpawn();
        sticky.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                        TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        rig.Engine.Returns("Frame.GetFrameTime", 1f / 66f);
        rig.Engine.Returns("Entities.GetVelocity", new Vector(2400f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(448f, 1368f, 300f));
        rig.Engine.Returns("Entities.GetAngularVelocity", new Vector(40f, 0f, 0f));
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 0.5f, new Vector(640f, 1368f, 300f),
                                           new Vector(-1f, 0f, 0f), ProjectileRig.Victim));
        return (rig, sticky);
    }

    private static Vector SeenVelocity(ProjectileRig rig)
        => EngineCallFacts.ArgAs<Vector>(rig.Engine.Last("Entities.SetVelocity"), 1);

    private static Vector SeenAngular(ProjectileRig rig)
        => EngineCallFacts.ArgAs<Vector>(rig.Engine.Last("Entities.SetAngularVelocity"), 1);

    [Fact]
    public void The_stickys_ray_cast_bounces_an_enemy_player_reflected_elastic_and_halved()
    {
        // VPhysicsUpdate's server branch (tf_weaponbase_grenadeproj.cpp:774-793):
        // the ray reaches one tick ahead (start + vel * frametime, :738) and
        // the enemy hit reflects the velocity off the normal (:781), absorbs
        // the 0.45 elasticity (:784) and is half again (:786-789): 2400
        // straight at a -x wall of player reflects to -2400, then -1080, then
        // -540. The angular velocity is reversed and halved (:791).
        var (rig, sticky) = StickyInFlight();
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim
                ? (int)TFTeam.Red : (int)TFTeam.Blue);

        sticky.OnPhysicsUpdate();

        Assert.Equal(new Vector(448f + 2400f / 66f, 1368f, 300f),
                     EngineCallFacts.ArgAs<Vector>(rig.Engine.Last("WorldQuery.TraceLine"), 1));
        Assert.Equal(new Vector(-540f, 0f, 0f), SeenVelocity(rig));
        Assert.Equal(new Vector(-20f, 0f, 0f), SeenAngular(rig));
    }

    [Fact]
    public void The_stickys_ray_cast_absorbs_the_elasticity_off_a_collidable_teammate()
    {
        // bHitFriendly (tf_weaponbase_grenadeproj.cpp:741): the same
        // reflection, but no enemy halving — the teammate keeps -1080 of the
        // 2400 — and CanCollideWithTeammates (baseprojectile.h:53) only past
        // its 0.25 s delay, so the clock is moved on first.
        var (rig, sticky) = StickyInFlight();
        rig.Engine.ReturnsEachCall("Players.GetTeam", () => (int)TFTeam.Blue);
        rig.Engine.Returns("Clock.Now", 11f); // past the collide delay

        sticky.OnPhysicsUpdate();

        Assert.Equal(new Vector(-1080f, 0f, 0f), SeenVelocity(rig));
        Assert.Equal(new Vector(-20f, 0f, 0f), SeenAngular(rig));
    }

    [Fact]
    public void A_teammate_within_stocks_collide_delay_leaves_the_sticky_alone()
    {
        // ResetCollideWithTeammates (baseprojectile.cpp:213-229): the first
        // 0.25 s of life answers no, so a young sticky's ray passes a
        // teammate without a bounce.
        var (rig, sticky) = StickyInFlight();
        rig.Engine.ReturnsEachCall("Players.GetTeam", () => (int)TFTeam.Blue);

        int launched = rig.Engine.CountOf("Entities.SetVelocity"); // ThrownBy's own
        sticky.OnPhysicsUpdate(); // the clock still stands at the throw

        Assert.Equal(launched, rig.Engine.CountOf("Entities.SetVelocity"));
        Assert.Equal(0, rig.Engine.CountOf("Entities.SetAngularVelocity"));
    }

    [Fact]
    public void Nothing_in_the_way_leaves_the_stickys_velocity_unchanged()
    {
        var (rig, sticky) = StickyInFlight();
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(false, 1f, default, default, EntityHandle.None));

        int launched = rig.Engine.CountOf("Entities.SetVelocity"); // ThrownBy's own
        sticky.OnPhysicsUpdate();

        Assert.Equal(launched, rig.Engine.CountOf("Entities.SetVelocity"));
        Assert.Equal(0, rig.Engine.CountOf("Entities.SetAngularVelocity"));
    }

    /// <summary>
    /// The pipe's side of the shared ray: both bombs are the same stock
    /// entity class, so <c>VPhysicsUpdate</c> runs for the pipe too. A pipe
    /// in flight at 2400 u/s along +x from 448, the victim scored red and
    /// the thrower blue, angular velocity to read back, and the clock at 10
    /// where the throw leaves it.
    /// </summary>
    private static (ProjectileRig Rig, TFPipebomb Pipe) PipeInFlight()
    {
        var rig = new ProjectileRig();
        rig.Engine.Returns("Clock.Now", 10f); // before the throw reads it
        var pipe = rig.Pipebomb();
        pipe.OnSpawn();
        pipe.ThrownBy(ProjectileRig.Player, new Vector(2400f, 0f, 0f), 90f, 146f,
                      TFWeaponId.Pipebomblauncher, "tf_weapon_pipebomblauncher", critical: false);
        rig.Engine.Returns("Frame.GetFrameTime", 1f / 66f);
        rig.Engine.Returns("Entities.GetVelocity", new Vector(2400f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(448f, 1368f, 300f));
        rig.Engine.Returns("Entities.GetAngularVelocity", new Vector(40f, 0f, 0f));
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 0.5f, new Vector(640f, 1368f, 300f),
                                           new Vector(-1f, 0f, 0f), ProjectileRig.Victim));
        return (rig, pipe);
    }

    [Fact]
    public void The_pipes_ray_cast_bounces_an_enemy_player_reflected_elastic_and_halved()
    {
        // CTFWeaponBaseGrenadeProj::VPhysicsUpdate serves both bombs
        // (tf_weaponbase_grenadeproj.cpp:716-795): the ray reaches one tick
        // ahead (start + vel * frametime, :738) and the enemy hit reflects
        // the velocity off the normal (:781), absorbs the 0.45 elasticity
        // (:784) and is half again (:786-789): 2400 straight at a -x wall of
        // player reflects to -2400, then -1080, then -540. The angular
        // velocity is reversed and halved (:791). Without the pipe's own
        // override this stays red: only the sticky answers the physics
        // update today.
        var (rig, pipe) = PipeInFlight();
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim
                ? (int)TFTeam.Red : (int)TFTeam.Blue);

        pipe.OnPhysicsUpdate();

        Assert.Equal(new Vector(448f + 2400f / 66f, 1368f, 300f),
                     EngineCallFacts.ArgAs<Vector>(rig.Engine.Last("WorldQuery.TraceLine"), 1));
        Assert.Equal(new Vector(-540f, 0f, 0f), SeenVelocity(rig));
        Assert.Equal(new Vector(-20f, 0f, 0f), SeenAngular(rig));
    }

    [Fact]
    public void The_pipes_ray_cast_absorbs_the_elasticity_off_a_collidable_teammate()
    {
        // The enemy halving is the enemy arm only (:786-789): the teammate
        // keeps -1080 of the 2400, and the ray stops for one only past the
        // 0.25 s collide delay (baseprojectile.h:53), so the clock moves
        // past it first.
        var (rig, pipe) = PipeInFlight();
        rig.Engine.ReturnsEachCall("Players.GetTeam", () => (int)TFTeam.Blue);
        rig.Engine.Returns("Clock.Now", 11f); // past the collide delay

        pipe.OnPhysicsUpdate();

        Assert.Equal(new Vector(-1080f, 0f, 0f), SeenVelocity(rig));
        Assert.Equal(new Vector(-20f, 0f, 0f), SeenAngular(rig));
    }

    [Fact]
    public void A_teammate_within_stocks_collide_delay_leaves_the_pipe_alone()
    {
        // ResetCollideWithTeammates (baseprojectile.cpp:213-229): the first
        // 0.25 s of life answers no, so a young pipe's ray passes a
        // teammate without a bounce.
        var (rig, pipe) = PipeInFlight();
        rig.Engine.ReturnsEachCall("Players.GetTeam", () => (int)TFTeam.Blue);

        int launched = rig.Engine.CountOf("Entities.SetVelocity"); // ThrownBy's own
        pipe.OnPhysicsUpdate(); // the clock still stands at the throw

        Assert.Equal(launched, rig.Engine.CountOf("Entities.SetVelocity"));
        Assert.Equal(0, rig.Engine.CountOf("Entities.SetAngularVelocity"));
    }

    [Fact]
    public void Nothing_in_the_way_leaves_the_pipes_velocity_unchanged()
    {
        var (rig, pipe) = PipeInFlight();
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(false, 1f, default, default, EntityHandle.None));

        int launched = rig.Engine.CountOf("Entities.SetVelocity"); // ThrownBy's own
        pipe.OnPhysicsUpdate();

        Assert.Equal(launched, rig.Engine.CountOf("Entities.SetVelocity"));
        Assert.Equal(0, rig.Engine.CountOf("Entities.SetAngularVelocity"));
    }
}
