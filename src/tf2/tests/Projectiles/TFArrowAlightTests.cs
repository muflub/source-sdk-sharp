using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Tests.Combat;
using TF2.Tests.Weapons;

using SourceSharp.Tests;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The Huntsman's arrow alight, from stock: a Pyro's flame on a teammate's
/// drawn bow lights the arrow the bow next fires
/// (<c>OnCollideWithTeammate</c>, <c>tf_weapon_flamethrower.cpp:3125-3144</c>,
/// the team test at <c>:2943</c>; the bow keeps the flag at :36, hands it to
/// the arrow and clears it at <c>tf_weapon_compound_bow.cpp:125</c> and
/// <c>:169</c>, and a deploy clears it at :92); the lit arrow then burns the
/// enemy it hits — ignite bit and flying-burn custom inside the enemy test
/// (<c>tf_projectile_arrow.cpp:478-490</c>).
/// </summary>
public class TFArrowAlightTests
{
    /// <summary>
    /// The manager wired as the game composes it: the owners behind the
    /// teammate arm's class gate and the directory behind its weapon lookup.
    /// </summary>
    private static TFFlameManager Wired(ManagerRig rig)
    {
        var manager = new TFFlameManager(rig.Engine, rig.Damage, rig.Engine, rig.Engine,
                                         rig.Conditions, rig.Scopes, rig.HitGroups, rig.Engine,
                                         rig.Owners, rig.Directory);
        manager.BindAreas(TestServices.Areas(rig.Engine));
        return manager;
    }

    /// <summary>
    /// One flame against <paramref name="trace"/>, the way
    /// <see cref="ManagerRig.Flame"/> drives it: the shell's deal, the first
    /// point, the tick's clock and trace, one think. The clock moves to 0.15
    /// (a tick at zero would not move the point at all), as the other flame
    /// facts drive it.
    /// </summary>
    private static void FlameOnce(TFFlameManager manager, ManagerRig rig, SurfaceTraceResult trace)
    {
        manager.UpdateDamage(WeaponFixtures.Parse("tf_weapon_flamethrower"),
                             new ShotDamage(TFFlamethrowerBehaviour.DamagePerTick),
                             TFFlameManagerDefs.BurnFrequency, attackerIsPyro: false);
        manager.AddPoint(default, new Vector(1f, 0f, 0f), default);
        rig.Engine.Returns("Host.CurrentTime", 0.15f);
        rig.Engine.Returns("WorldQuery.TraceBox", trace);
        manager.OnThink();
    }

    [Fact]
    public void AFlameOnATeammatesDrawnBowLightsTheArrow()
    {
        // OnCollideWithTeammate (:3128 the Sniper gate, :3139 the bow gate,
        // :3142 SetArrowAlight(true)): a same-team collide whose held weapon
        // is the CompoundBow lights the carried state.
        var rig = new ManagerRig();
        rig.SameTeams();
        rig.Engine.Returns("Entities.GetIndex", 3);
        rig.Owners.Classes[3] = TFClass.Sniper;
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_compound_bow");
        var bowHandle = new EntityHandle(0x0002_0000u | 7);
        rig.Engine.Returns("Players.GetActiveWeaponEntity", bowHandle);
        var bow = new SsWeapon { Handle = bowHandle };
        bow.State.BindLocal();
        rig.Directory.Holds(bowHandle, bow);

        FlameOnce(Wired(rig), rig, ManagerRig.Hit(ManagerRig.Victim));

        Assert.True(bow.State.ArrowAlight);
        Assert.Empty(rig.Damage.Hits); // the teammate arm of :2943 takes no damage
    }

    [Fact]
    public void AnEnemysFlameDoesNotLightTheArrow()
    {
        // The rig's default teams alternate (victim Red against the Blue
        // attacker), so the collide is an enemy's: damage, and the bow keeps
        // whatever it stood at. Everything the teammate arm reads is served —
        // the only thing standing between the flame and the flag is the team.
        var rig = new ManagerRig();
        rig.Engine.Returns("Entities.GetIndex", 3);
        rig.Owners.Classes[3] = TFClass.Sniper;
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_compound_bow");
        rig.Engine.Returns("Players.GetActiveWeaponEntity", RigEntities.Weapon);
        var bow = new SsWeapon { Handle = RigEntities.Weapon };
        bow.State.BindLocal();
        rig.Directory.Holds(RigEntities.Weapon, bow);

        FlameOnce(Wired(rig), rig, ManagerRig.Hit(ManagerRig.Victim));

        Assert.Single(rig.Damage.Hits); // the enemy arm burns
        Assert.False(bow.State.ArrowAlight);
    }

    [Fact]
    public void HolsteringClearsTheLitArrow()
    {
        // Stock clears the flag at the bow's WeaponReset (:92), and the
        // holster's clear (:380) arrives through the next deploy's state
        // reset: the deploy's clear of the behaviour's own fields takes the
        // lit flag with it.
        var bow = new SsWeapon { Handle = RigEntities.Weapon };
        bow.State.BindLocal();
        bow.State.ArrowAlight = true;

        bow.State.ResetBehaviourState();

        Assert.False(bow.State.ArrowAlight);
    }

    [Fact]
    public void ALitArrowBurnsItsVictim()
    {
        // The arrow's Impact, lit arm (:486-490): the bow's own bits with the
        // ignite bit added, the flying-burn custom, and the charge-scaled
        // shot — at full charge a 10-damage row's arrow lands 50 + 10.
        var rig = new ProjectileRig();
        var hits = new ScopedDamage(rig.Scopes);
        var arrow = new TFArrow(rig.Engine, rig.Motion, hits, rig.Scopes, rig.HitGroups);
        arrow.BindAreas(TestServices.Areas(rig.Engine));
        arrow.ArmedBy(TFWeaponId.CompoundBow, "tf_weapon_compound_bow", critical: false, lit: true,
                      TFProjectileDefs.ArrowDamage(10f, 1f), TFProjectileDefs.ArrowGravity(1f));
        arrow.FiredBy(ProjectileRig.Player, new Vector(500f, 0f, 0f));

        arrow.OnTouch(ProjectileRig.Victim);

        var hit = Assert.Single(hits.Hits);
        Assert.Equal(ProjectileRig.Victim, hit.Victim);
        Assert.Equal(ProjectileRig.Player, hit.Attacker);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.CompoundBow) | TFDamageBits.Ignite, hit.Type);
        Assert.Equal(TFDamageCustom.Flyingburn, hit.Custom);
        Assert.Equal(60f, hit.Damage, 2f);
    }

    [Fact]
    public void AnUnlitArrowDoesNot()
    {
        // The same Impact on an arrow the flame never reached: the enemy test
        // of :478 still holds — plain bow damage, no ignite bit, no custom.
        // An uncharged bow shoots at its row's damage: the 50 base.
        var rig = new ProjectileRig();
        var hits = new ScopedDamage(rig.Scopes);
        var arrow = new TFArrow(rig.Engine, rig.Motion, hits, rig.Scopes, rig.HitGroups);
        arrow.BindAreas(TestServices.Areas(rig.Engine));
        arrow.ArmedBy(TFWeaponId.CompoundBow, "tf_weapon_compound_bow", critical: false, lit: false,
                      TFProjectileDefs.ArrowDamage(10f, 0f), TFProjectileDefs.ArrowGravity(0f));
        arrow.FiredBy(ProjectileRig.Player, new Vector(500f, 0f, 0f));

        arrow.OnTouch(ProjectileRig.Victim);

        var hit = Assert.Single(hits.Hits);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.CompoundBow), hit.Type);
        Assert.Equal(TFDamageCustom.None, hit.Custom);
        Assert.Equal(50f, hit.Damage, 2f);
    }
}
