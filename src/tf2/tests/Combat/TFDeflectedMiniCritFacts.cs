using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Combat;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Tests.Projectiles;

using SourceSharp.Tests;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// The reflected projectile's mini-crit for the touch-dealt shots: stock's arm
/// (<c>tf_gamerules.cpp:6159-6164</c>) asks any <c>CTFBaseRocket</c> inflictor
/// whether a deflection sent it back — the flare and the arrow as well as the
/// rocket, and the energy ball — and the arrow's and flare's touch name
/// themselves the hit's inflictor (<c>tf_projectile_arrow.cpp:531</c>,
/// <c>tf_projectile_flare.cpp:347</c>). Each deflected projectile here is sent
/// back the way every deflect sends one, through the base's retarget.
/// </summary>
public sealed class TFDeflectedMiniCritFacts
{
    private static readonly EntityHandle Deflector = new(0x0001_0000u | 7);

    /// <summary>The layer's answer to a ranged hit dealt inside <paramref name="inflictor"/>'s scope.</summary>
    private static TFHitRecord HitInside(CombatRig rig, ScriptedEntity inflictor)
    {
        if (inflictor.Handle == EntityHandle.None)
            inflictor.Handle = ProjectileRig.ProjectileHandle;
        rig.Entities.Placed[inflictor.Handle] = inflictor;
        using (rig.Inflictors.Push(inflictor.Handle, default))
            rig.Hit(10f, DamageType.Bullet);
        return rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value;
    }

    [Fact]
    public void A_deflected_arrow_mini_crits_through_its_scope()
    {
        var rig = new CombatRig();
        TFArrow arrow = new ProjectileRig().Arrow();
        arrow.DeflectBy(Deflector, new Vector(1f, 0f, 0f), deflectorCritBoosted: false, "tf_weapon_flamethrower");
        Assert.Equal(1, arrow.Deflections);

        var record = HitInside(rig, arrow);

        Assert.Equal(TFCritType.Mini, record.Crit);
        Assert.Equal(13.5f, record.Damage, 4);
    }

    [Fact]
    public void An_undeflected_arrow_is_plain_on_the_scope()
    {
        var rig = new CombatRig();
        TFArrow arrow = new ProjectileRig().Arrow();
        Assert.Equal(0, arrow.Deflections);

        var record = HitInside(rig, arrow);

        Assert.Equal(TFCritType.None, record.Crit);
        Assert.Equal(10f, record.Damage, 4);
    }

    [Fact]
    public void A_deflected_flare_mini_crits_through_its_scope()
    {
        var rig = new CombatRig();
        TFFlare flare = new ProjectileRig().Flare();
        flare.DeflectBy(Deflector, new Vector(1f, 0f, 0f), deflectorCritBoosted: false, "tf_weapon_flamethrower");

        var record = HitInside(rig, flare);

        Assert.Equal(TFCritType.Mini, record.Crit);
        Assert.Equal(13.5f, record.Damage, 4);
    }

    [Fact]
    public void An_undeflected_flare_is_plain_on_the_scope()
    {
        var rig = new CombatRig();
        TFFlare flare = new ProjectileRig().Flare();

        var record = HitInside(rig, flare);

        Assert.Equal(TFCritType.None, record.Crit);
        Assert.Equal(10f, record.Damage, 4);
    }

    [Fact]
    public void A_deflected_energy_balls_blast_mini_crits_through_its_scope()
    {
        var rig = new CombatRig();
        var projectiles = new ProjectileRig();
        TFProjectileEnergyBall ball = projectiles.Ball();
        ball.DeflectBy(Deflector, new Vector(0f, 0f, 1100f), deflectorCritBoosted: false, "tf_weapon_flamethrower");
        Assert.Equal(1, ball.Deflections);

        var record = HitInside(rig, ball);

        Assert.Equal(TFCritType.Mini, record.Crit);
        Assert.Equal(13.5f, record.Damage, 4);
    }

    /// <summary>The arrow's touch deals its hit inside its own inflictor scope, where the arm looks.</summary>
    [Fact]
    public void The_arrows_touch_names_the_arrow_its_hits_inflictor()
    {
        var rig = new ProjectileRig();
        var probe = new InflictorProbe(rig.Inflictors);
        var arrow = new TFArrow(rig.Engine, rig.Motion, probe, rig.Scopes, rig.HitGroups,
                                rig.Directory, rig.Owners, rig.Hooks, rig.Engine, rig.Temps, rig.Engine,
                                rig.Engine, rig.Engine, rig.Inflictors);
        arrow.BindAreas(TestServices.Areas(rig.Engine));
        arrow.Handle = ProjectileRig.ProjectileHandle;
        arrow.ArmedBy(TFWeaponId.CompoundBow, "tf_weapon_compound_bow", critical: false, lit: false,
                      TFProjectileDefs.ArrowDamage(10f, 0f), TFProjectileDefs.ArrowGravity(0f));
        arrow.FiredBy(ProjectileRig.Player, new Vector(2000f, 0f, 0f));
        rig.Engine.Returns("Entities.IsValid", true)
                  .Returns("Entities.IsPlayer", true)
                  .Returns("Entities.GetVelocity", Vector.Zero)   // no flight to trace a hitbox on
                  .Returns("Entities.GetOrigin", new Vector(5f, 6f, 7f))
                  .ReturnsEachCall("Players.GetTeam", () =>
                      rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim ? 2 : 3);

        arrow.OnTouch(ProjectileRig.Victim);

        var (inflictor, origin) = Assert.Single(probe.Seen);
        Assert.Equal(ProjectileRig.ProjectileHandle, inflictor);
        Assert.Equal(new Vector(5f, 6f, 7f), origin);
    }

    /// <summary>The flare's touch on a player deals its hit inside its own inflictor scope.</summary>
    [Fact]
    public void The_flares_touch_names_the_flare_its_hits_inflictor()
    {
        var rig = new ProjectileRig();
        var probe = new InflictorProbe(rig.Inflictors);
        var flare = new TFFlare(rig.Engine, rig.Motion, probe, rig.Scopes, rig.HitGroups, rig.Conditions, rig.Engine,
                                rig.Blast, rig.Hooks, rig.Owners, rig.FlareTray, rig.Carriers, rig.Engine, rig.Random,
                                rig.Inflictors);
        flare.BindAreas(TestServices.Areas(rig.Engine));
        flare.Handle = ProjectileRig.ProjectileHandle;
        flare.NetStates = NetStateBinding.AttachLocal(flare, flare.Handle, new NetStateFields(new RecordingLog()));
        rig.Engine.Returns("Players.GetTeam", 3)
                  .Returns("Players.GetHandle", ProjectileRig.Player);
        flare.ArmedBy(30f, TFWeaponId.Flaregun, "tf_weapon_flaregun", critical: false,
                      TFProjectileDefs.FlareGravity, ownerIndex: 3, flareMode: TFProjectileBehaviour.FlareNormal);
        flare.FiredBy(ProjectileRig.Player, new Vector(2000f, 0f, 0f));
        rig.Engine.Returns("Entities.IsValid", true)
                  .Returns("Entities.IsPlayer", true)
                  .Returns("Entities.GetOrigin", new Vector(5f, 6f, 7f))
                  .ReturnsEachCall("Players.GetTeam", () =>
                      rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim ? 2 : 3);

        flare.OnTouch(ProjectileRig.Victim);

        var (inflictor, origin) = Assert.Single(probe.Seen);
        Assert.Equal(ProjectileRig.ProjectileHandle, inflictor);
        Assert.Equal(new Vector(5f, 6f, 7f), origin);
    }

    /// <summary>The damage seam as a probe: each hit records the inflictor scope open around it.</summary>
    private sealed class InflictorProbe(DamageInflictors inflictors) : IEngineDamage
    {
        public List<(EntityHandle Inflictor, Vector Origin)> Seen { get; } = new();

        public void Apply(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType)
        {
            if (inflictors.TryCurrent(out var inflictor, out var origin))
                Seen.Add((inflictor, origin));
        }

        public void ApplyAt(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType, Vector force,
                            Vector position) => Apply(victim, attacker, damage, damageType);
        public void SetForceServerRagdoll(EntityHandle entity, bool force) { }
        public TakeDamageMode SetTakeDamageMode(EntityHandle entity, TakeDamageMode mode) => mode;
        public void RegisterRules(DamageRuleMasks masks) { }
        public void TakeDamage(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType,
                               Vector? direction = null, Vector forceOrigin = default, bool physicsOnly = false)
            => Apply(entity, attacker, damage, damageType);
        public int TakeDamageAnswered(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType,
                                      Vector? direction = null, Vector forceOrigin = default, bool physicsOnly = false)
        {
            Apply(entity, attacker, damage, damageType);
            return damage > 0 ? 1 : 0;
        }
    }
}
