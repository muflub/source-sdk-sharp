using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Server.Projectiles;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Tests.Projectiles;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The blast jump: <c>CTFPlayer::SetBlastJumpState</c>'s condition and event
/// through the applier's self-blast arms (tf_player.cpp:8919-8922,
/// :9150-9182), the landing arm of <c>TFPlayerThink</c> (:1441-1483), and the
/// grounded blast that is no jump.
/// </summary>
public class TFBlastJumpTests
{
    private static readonly EntityHandle Projectile = new(0x0003_0009u);

    private static TFBlast RocketBlast()
        => TFBlast.Of(90f, TFProjectileDefs.RocketRadius, TFWeaponDamageTypes.Of(TFWeaponId.Rocketlauncher));

    /// <summary>The world the jump is claimed in: a soldier in the air, their
    /// carrier and blast-jump component attached, the real condition writer
    /// over both.</summary>
    private static (PlayerRig Rig, TFBlastApplier Blast, TFPlayerConditions Conditions, TFBlastJumpState Jump) JumpRig(
        TFClass cls = TFClass.Soldier)
    {
        var rig = new PlayerRig().As(cls);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine);
        var jump = new TFBlastJumpState(rig.Carriers, conditions, rig.Engine, new NoEntities(), rig.Engine, rig.Engine);
        jump.Attach(PlayerRig.Player);

        var blast = new TFBlastApplier(rig.Engine, rig.Engine, rig.Engine, new TFDamageScopes(), new DamageHitGroups(),
            new DamageInflictors(), rig.Carriers, rig.Log, rig.Engine, new ProjectileRig.FixedOwners(),
            new AttributeHooks(), new ProjectileRig.Resupply(), rig.Engine, rig.Engine,
            new ProjectileRig.DirectoryView(), rig.Carriers);
        return (rig, blast, conditions, jump);
    }

    /// <summary>The attacker at 100, airworthy: the self pass reaches and the
    /// arms read an airborne soldier.</summary>
    private static void AirborneAtHundred(PlayerRig rig, EntityState state)
    {
        rig.Engine.Returns("WorldQuery.EntitiesInSphere", 0);
        rig.Engine.Returns("Networking.EntityGetState", state);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(100f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(100f, 0f, 0f));
        rig.Engine.Returns("Entities.GetBounds", (default(Vector), default(Vector)));
        rig.Engine.Returns("WorldQuery.TraceLine", new TraceResult(true, 1f, default, default, EntityHandle.None));
        rig.Engine.Returns("Damage.TakeDamageAnswered", 0);
        rig.Engine.ReturnsEachCall("GameEvents.EventCreate", () => new IntPtr(7));
        rig.Engine.Returns("GameEvents.EventFire", true);
        rig.Engine.Returns("Players.GetUserId", 9);
    }

    private static void RocketJump(PlayerRig rig, TFBlastApplier blast)
        => blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), PlayerRig.Player, Projectile, ignored: Projectile,
                       TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false,
                       selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

    /// <summary>A self blast off the ground: the condition and the rocket_jump
    /// event with its userid (:8679-8701).</summary>
    [Fact]
    public void A_self_blast_off_the_ground_sets_the_blast_jump()
    {
        var (rig, blast, conditions, _) = JumpRig();
        AirborneAtHundred(rig, EntityState.None);

        RocketJump(rig, blast);

        Assert.True(conditions.InCond(PlayerRig.Player, TFCond.Blastjumping));
        var fired = rig.Engine.Last("GameEvents.EventCreate");
        Assert.Equal("rocket_jump", fired.ArgAs<string>(0));
        Assert.Equal("userid", rig.Engine.Last("GameEvents.EventSetInt")!.ArgAs<string>(1));
        Assert.Equal(9, rig.Engine.Last("GameEvents.EventSetInt")!.ArgAs<int>(2));
        Assert.Equal("playsound", rig.Engine.Last("GameEvents.EventSetBool")!.ArgAs<string>(1));
        Assert.False(rig.Engine.Last("GameEvents.EventSetBool")!.ArgAs<bool>(2));
        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventFire"));
    }

    /// <summary>The landing (:1441-1483): the grounded tick clears the
    /// condition and fires the landed event, whose only word is the userid.</summary>
    [Fact]
    public void Landing_clears_the_blast_jump_and_fires_the_landed_event()
    {
        var (rig, blast, conditions, jump) = JumpRig();
        AirborneAtHundred(rig, EntityState.None);
        RocketJump(rig, blast);
        Assert.True(conditions.InCond(PlayerRig.Player, TFCond.Blastjumping));

        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        jump.Tick(5f);

        Assert.False(conditions.InCond(PlayerRig.Player, TFCond.Blastjumping));
        Assert.Equal(TFBlastJump.None, jump.State);
        Assert.Equal(5f, jump.LandTime);
        Assert.Equal("rocket_jump_landed", rig.Engine.Last("GameEvents.EventCreate")!.ArgAs<string>(0));
        Assert.Equal(9, rig.Engine.Last("GameEvents.EventSetInt")!.ArgAs<int>(2));
        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventSetBool")); // the jump's, not the landing's
        Assert.Equal(2, rig.Engine.CountOf("GameEvents.EventFire"));
    }

    /// <summary>Both bits ride: the sticky jump is named first on the landing
    /// (:1462-1466).</summary>
    [Fact]
    public void The_sticky_jump_is_named_on_the_landing_over_the_rocket()
    {
        var (rig, blast, conditions, jump) = JumpRig();
        AirborneAtHundred(rig, EntityState.None);
        rig.Engine.Returns("Players.GetUserId", 9);
        jump.Set(TFBlastJump.RocketJumped);
        jump.Set(TFBlastJump.StickyJumped);
        Assert.True(conditions.InCond(PlayerRig.Player, TFCond.Blastjumping));

        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        jump.Tick(7f);

        Assert.Equal("sticky_jump_landed", rig.Engine.Last("GameEvents.EventCreate")!.ArgAs<string>(0));
    }

    /// <summary>A grounded blast is no jump (:8921): no condition, no event —
    /// the demoman half, whose only claim is the airborne sticky arm.</summary>
    [Fact]
    public void A_grounded_blast_with_no_lift_is_no_blast_jump()
    {
        var (rig, blast, conditions, _) = JumpRig(TFClass.Demoman);
        AirborneAtHundred(rig, EntityState.OnGround);

        RocketJump(rig, blast);

        Assert.False(conditions.InCond(PlayerRig.Player, TFCond.Blastjumping));
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventCreate"));
    }

    /// <summary>The push's own claim (tf_player.cpp:10275): the soldier's
    /// <c>ApplyPushFromDamage</c> names the jump from the ground too — no
    /// health gate, stock's silent <c>bPlaySound</c> — so the grounded self
    /// blast that throws him jumps as well, once.</summary>
    [Fact]
    public void The_soldiers_push_claims_the_jump_from_the_ground()
    {
        var (rig, blast, conditions, _) = JumpRig();
        AirborneAtHundred(rig, EntityState.OnGround);
        rig.Engine.Returns("Damage.TakeDamageAnswered", 50);

        RocketJump(rig, blast);

        Assert.True(conditions.InCond(PlayerRig.Player, TFCond.Blastjumping));
        Assert.Equal("rocket_jump", rig.Engine.Last("GameEvents.EventCreate")!.ArgAs<string>(0));
        Assert.False(rig.Engine.Last("GameEvents.EventSetBool")!.ArgAs<bool>(2));
        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventFire"));
    }

    /// <summary>An airborne tick between the blast and the landing keeps both
    /// (:1441-1447's airborne arm).</summary>
    [Fact]
    public void The_airborne_tick_keeps_the_blast_jump()
    {
        var (rig, blast, conditions, jump) = JumpRig();
        AirborneAtHundred(rig, EntityState.None);
        RocketJump(rig, blast);

        jump.Tick(3f);

        Assert.True(conditions.InCond(PlayerRig.Player, TFCond.Blastjumping));
        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventFire"));
    }

    /// <summary>The other's blast latches the victim's next rise
    /// (tf_player.cpp:10474-10477): the enemy pass raises the latch, no rise
    /// reads it, the rise claims the enemy-blasted jump — the condition with
    /// no jump event (:8679-8689) — and only then.</summary>
    [Fact]
    public void An_enemy_blast_latches_the_next_rise_and_fires_no_jump_event()
    {
        var (rig, blast, conditions, jump) = JumpRig();
        AirborneAtHundred(rig, EntityState.None);
        rig.Engine.Returns("Damage.TakeDamageAnswered", 50);
        rig.Engine.Returns("Entities.GetOrigin", new Vector(0f, 0f, 1f));   // the victim at the blast
        rig.Engine.FillsSphere(PlayerRig.Player);
        var attacker = new EntityHandle(0x0002_000Bu);                       // somebody else fires

        EnemyBlast(rig, blast, attacker);

        // The latch waits for a rise: no condition yet, and the blast itself
        // fires no jump event.
        Assert.False(conditions.InCond(PlayerRig.Player, TFCond.Blastjumping));
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventCreate"));

        jump.EnemyBlasted();                                                  // the rise, z over 250

        Assert.True(conditions.InCond(PlayerRig.Player, TFCond.Blastjumping));
        Assert.Equal(TFBlastJump.EnemyBlastedMe, jump.State);
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventCreate"));       // the enemy bit fires nothing
    }

    /// <summary>The latch lives exactly one command (:335): the end of the
    /// command drops it, and a later rise claims nothing.</summary>
    [Fact]
    public void The_latch_lives_one_command()
    {
        var (_, _, conditions, jump) = JumpRig();

        jump.NoteEnemyBlast();
        jump.EndCommand();
        jump.EnemyBlasted();

        Assert.False(conditions.InCond(PlayerRig.Player, TFCond.Blastjumping));
        Assert.Equal(TFBlastJump.None, jump.State);
    }

    /// <summary>The spawn clears both (:3746, :3748): a death in the air
    /// lands under the new life with no jump and no held latch.</summary>
    [Fact]
    public void The_spawn_clears_the_state_and_the_latch()
    {
        var (_, _, _, jump) = JumpRig();

        jump.Set(TFBlastJump.RocketJumped);
        jump.NoteEnemyBlast();
        jump.ClearOnSpawn();
        Assert.Equal(TFBlastJump.None, jump.State);

        jump.EnemyBlasted();
        Assert.Equal(TFBlastJump.None, jump.State);
    }

    /// <summary>The other's rocket at the victim, an attacker that is not the
    /// victim: the sweep's enemy pass.</summary>
    private static void EnemyBlast(PlayerRig rig, TFBlastApplier blast, EntityHandle attacker)
        => blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), attacker, Projectile, ignored: Projectile,
                       TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false);

    /// <summary>No entity anywhere: the directory half of the water test.</summary>
    private sealed class NoEntities : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h) => null;
        public ScriptedEntity? FindByName(string name) => null;
        public System.Collections.Generic.IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => false;
        public System.Collections.Generic.IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }
}
