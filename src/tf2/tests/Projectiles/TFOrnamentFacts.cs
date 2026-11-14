using SourceSharp.Abi;

using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Combat;

using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Tests.Combat;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The Wrap Assassin's ornament (<c>CTFBall_Ornament</c>,
/// <c>tf_weapon_bat.cpp</c>): the lob the throw births it with, the touch's
/// blast and the struck enemy's bleed and love-tap, and the flight's expiry.
/// </summary>
public sealed class TFOrnamentFacts
{
    /// <summary>The now every clock read answers, so aRemoveTime reads against it.</summary>
    private const float Now = 10f;

    private static TFWeaponScript GiftwrapScript() =>
        TF2.Tests.Weapons.WeaponFixtures.ParseExtra("tf_weapon_bat_giftwrap");

    private static WeaponAttackContext Attack(int owner) => new(default!, null!, null!, 1f, true,
        null!, new WeaponCommand(1, 5, owner));

    /// <summary>
    /// The ball over the projectile rig, plus the player data and bleeds its
    /// touch's victim arm reads: the bleed slice attached, the way the damage
    /// layer's facts arm theirs.
    /// </summary>
    private sealed class Rig
    {
        public readonly ProjectileRig Base = new();
        public readonly TFMovementFixtures.Players PlayerData = new();
        public readonly RigConditions Conditions;
        public readonly TFBleeds Bleeds;
        public readonly TFBleed VictimBleed;
        public readonly TFBallOrnament Ball;

        public Rig()
        {
            Base.Engine.Returns("Clock.Now", Now);
            Base.Engine.Returns("Players.IsAlive", true);
            Conditions = new RigConditions(PlayerData);
            Bleeds = new TFBleeds(PlayerData, Conditions, Base.Engine, Base.Engine, Base.Engine, Base.Scopes);
            PlayerData.Add(ProjectileRig.Victim.EntryIndex, TFClass.Scout);
            VictimBleed = (TFBleed)Bleeds.Create(ProjectileRig.Victim).Single();
            VictimBleed.Attach(ProjectileRig.Victim);

            Ball = new TFBallOrnament(Base.Engine, Base.Motion, Base.Engine, Base.Scopes, Base.HitGroups,
                                      Base.Engine, Conditions, Bleeds, Base.Blast, Base.Engine);
            Ball.BindAreas(TestServices.Areas(Base.Engine));
        }

        /// <summary>The victim a player the ball can see and a stranger's team.</summary>
        public void PlayersVisible()
        {
            Base.Engine.ReturnsEachCall("Entities.IsPlayer",
                () => Base.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim);
            Base.Engine.ReturnsEachCall("Players.GetTeam",
                () => Base.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim ? 2 : 3);
        }
    }

    [Fact]
    public void The_throw_births_the_ornament_at_the_lob()
    {
        // GetBallDynamics (tf_weapon_bat.cpp:236-244): 32 along the sight and
        // 14 under the eye, ten forward parts to one up, at the convar's 3000;
        // the shot is the bat's 35 until an item's mult_dmg says otherwise.
        var rig = new Rig();
        rig.Base.Directory.Instance = rig.Ball;

        rig.Base.Engine.Returns("Players.GetActiveWeaponEntity", rig.Ball.Handle);
        rig.Base.Effects.Projectile(Attack(ProjectileRig.Player.EntryIndex), GiftwrapScript(),
                                    new ShotDamage(35f), 0f);

        Assert.Equal(TFProjectileEntityNames.BallOrnament,
                     rig.Base.Engine.Last("Entities.Create")!.Value.ArgAs<string>(0));
        Assert.Equal(new Vector(32f, 0f, 50f),
                     rig.Base.Engine.Last("Entities.SetOrigin")!.Value.ArgAs<Vector>(1));
        Vector velocity = rig.Base.Engine.Last("Entities.SetVelocity")!.Value.ArgAs<Vector>(1);
        Assert.Equal(3000f, velocity.Length, 1);
        Assert.Equal(10f, velocity.X / velocity.Z, 3);
        Assert.Equal(35f, rig.Ball.ShotDamage, 3);
        Assert.Equal(TFWeaponId.BatGiftwrap, rig.Ball.Weapon);
    }

    [Fact]
    public void A_touch_blasts_and_then_bleeds_and_taps_the_enemy()
    {
        // PipebombTouch (tf_weapon_bat.cpp:1223-1244): the 0.9 blast at every
        // touch, the enemy's five-second bleed (:1185) and the shot again
        // (:1194-1212), the bleed before the hurt (:1185, :1211).
        var rig = new Rig();
        rig.PlayersVisible();
        rig.Ball.ArmedBy(40f, TFWeaponId.BatGiftwrap, "tf_weapon_bat_giftwrap", critical: false);
        rig.Base.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Base.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        rig.Base.Engine.FillsSphere(ProjectileRig.Victim);

        rig.Ball.FiredBy(ProjectileRig.Player, new Vector(0f, 0f, 1f));
        rig.Ball.OnTouch(ProjectileRig.Victim);

        var blast = rig.Base.Engine.Calls.Where(c => c.Member == "Damage.TakeDamageAnswered").Single();
        Assert.Equal(ProjectileRig.Victim, blast.ArgAs<EntityHandle>(0));
        // 0.9 of the 40, a unit down the ground lift's share (36/50 edge).
        Assert.Equal(35.8f, blast.ArgAs<float>(2), 1);

        var tap = rig.Base.Engine.Last("Damage.TakeDamage")!.Value;
        Assert.Equal(ProjectileRig.Victim, tap.ArgAs<EntityHandle>(0));
        Assert.Equal(ProjectileRig.Player, tap.ArgAs<EntityHandle>(1));
        Assert.Equal(40f, tap.ArgAs<float>(2), 1);

        var bleed = Assert.Single(rig.VictimBleed.State.Bleeds);
        Assert.Equal(TFWeaponId.BatGiftwrap, bleed.Weapon);
        Assert.Equal("tf_weapon_bat_giftwrap", bleed.WeaponName);
        Assert.Equal(TFProjectileDefs.OrnamentBleedSeconds, bleed.RemoveTime - Now, 3);
    }

    [Fact]
    public void A_teammates_touch_takes_the_blast_and_no_more()
    {
        var rig = new Rig();
        rig.Ball.ArmedBy(40f, TFWeaponId.BatGiftwrap, "tf_weapon_bat_giftwrap", critical: false);
        rig.Base.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Base.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        rig.Base.Engine.FillsSphere(ProjectileRig.Victim);

        rig.Ball.FiredBy(ProjectileRig.Player, new Vector(0f, 0f, 1f));
        rig.Ball.OnTouch(ProjectileRig.Victim);

        Assert.Single(rig.Base.Engine.Calls, c => c.Member == "Damage.TakeDamageAnswered");
        Assert.DoesNotContain(rig.Base.Engine.Calls, c => c.Member == "Damage.TakeDamage");
        Assert.Empty(rig.VictimBleed.State.Bleeds);
    }

    [Fact]
    public void A_long_flight_is_a_crit()
    {
        // FLIGHT_TIME_TO_MAX_STUN (tf_weapon_bat.cpp:708, the hit's
        // :1176-1181): 0.8 s aloft, the tap the crit.
        var rig = new Rig();
        rig.PlayersVisible();
        rig.Ball.ArmedBy(40f, TFWeaponId.BatGiftwrap, "tf_weapon_bat_giftwrap", critical: false);
        rig.Base.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Base.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        rig.Base.Engine.FillsSphere(ProjectileRig.Victim);

        rig.Ball.FiredBy(ProjectileRig.Player, new Vector(0f, 0f, 1f));
        rig.Base.Engine.Returns("Clock.Now", Now + 1f);
        rig.Ball.OnTouch(ProjectileRig.Victim);

        var tap = rig.Base.Engine.Last("Damage.TakeDamage")!.Value;
        Assert.True(tap.ArgAs<DamageType>(3).HasFlag(TFDamageBits.Critical));
    }

    [Fact]
    public void The_flight_expires_quarter_past_nothing()
    {
        // CTFStunBall::Spawn's context think (tf_weapon_bat.cpp:672): 15 s
        // aloft and the ball is gone, loose or not.
        var rig = new Rig();

        rig.Ball.OnSpawn();

        rig.Base.Engine.Returns("Clock.Now", Now + TFProjectileDefs.OrnamentLifetime);
        rig.Ball.OnThink(TFBallOrnament.ExpiryContext);

        Assert.Equal(rig.Ball.Handle,
                     rig.Base.Engine.Last("Entities.Remove")!.Value.ArgAs<EntityHandle>(0));
    }
}
