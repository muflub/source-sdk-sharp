using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The airblast's reflect of a flare and an arrow (stock <c>DeflectEntity</c>, <c>tf_weaponbase.cpp:6043-6090</c>, and
/// each one's <c>Deflected</c>: <c>tf_projectile_flare.cpp:589-615</c>, <c>tf_projectile_arrow.cpp:1149-1180</c>): sent
/// back at its old speed along the aim, the deflector's from then on, a crit under the deflector's boost; a teammate's
/// left alone. The projectile is the projectile rig's (its own engine double); the blast is the flame rig's.
/// </summary>
public class TFPyroDeflectFacts
{
    /// <summary>The flare's or arrow's handle in the sphere (the projectile rig hands out none of its own).</summary>
    private static readonly EntityHandle ThingHandle = new(0x0003_0000u | 23);

    /// <summary>The flare or arrow hangs at (256, 0, 64) moving (0, 1000, 0); the aim's ray stops at (128, 0, 64), so it leaves along (-1, 0, 0).</summary>
    private static FlameRig Rig(ScriptedEntity thing)
    {
        thing.Handle = ThingHandle;
        var rig = new FlameRig();
        rig.Directory.Instance = thing;
        rig.Engine.FillsSphere(thing.Handle);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(256f, 0f, 64f));
        rig.Engine.Returns("Entities.GetVelocity", new Vector(0f, 1000f, 0f));
        rig.Engine.Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, new Vector(128f, 0f, 64f), default, EntityHandle.None));
        return rig;
    }

    /// <summary>A blue flare (team 3) of player 5's, in the projectile rig.</summary>
    private static (ProjectileRig Shots, TFFlare Flare) BlueFlare()
    {
        var shots = new ProjectileRig();
        shots.Engine.Returns("Players.GetTeam", 2);
        TFFlare flare = shots.Flare();
        flare.ArmedBy(30f, TFWeaponId.Flaregun, "tf_weapon_flaregun", critical: false, gravity: 0.3f, ownerIndex: 5, flareMode: 0, team: 3);
        return (shots, flare);
    }

    private static WeaponAttackContext Attack() => new(default!, null!, null!, 1f, true, null!, new WeaponCommand(1, 5, 3));

    private static TFWeaponScript Script() => WeaponFixtures.Parse("tf_weapon_flamethrower");

    [Fact]
    public void An_enemy_flare_in_the_sphere_is_sent_back_at_its_old_speed()
    {
        (_, TFFlare flare) = BlueFlare();
        FlameRig rig = Rig(flare);

        rig.Effects.AirBlast(Attack(), Script());

        var sent = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(sent);
        Assert.Equal(new Vector(-1000f, 0f, 0f), sent!.ArgAs<Vector>(1));
        Assert.True(flare.Deflected);
    }

    [Fact]
    public void A_deflected_flare_is_the_deflectors()
    {
        (_, TFFlare flare) = BlueFlare();
        FlameRig rig = Rig(flare);

        rig.Effects.AirBlast(Attack(), Script());

        Assert.Equal((FlameRig.Player.EntryIndex, 2, "tf_flamethrower"), (flare.OwnerIndex, flare.State.TeamNumber, flare.WeaponName));
    }

    [Fact]
    public void A_crit_boosted_deflectors_flare_is_critical()
    {
        (_, TFFlare flare) = BlueFlare();
        FlameRig rig = Rig(flare);
        rig.Conditions.Grant(FlameRig.Player, TFCond.Critboosted);

        rig.Effects.AirBlast(Attack(), Script());

        Assert.True(flare.Critical);
    }

    [Fact]
    public void The_blast_leaves_a_teammates_flare()
    {
        var shots = new ProjectileRig();
        TFFlare flare = shots.Flare();
        flare.ArmedBy(30f, TFWeaponId.Flaregun, "tf_weapon_flaregun", critical: false, gravity: 0.3f, ownerIndex: 4, flareMode: 0, team: 2);
        FlameRig rig = Rig(flare);

        rig.Effects.AirBlast(Attack(), Script());

        Assert.Null(rig.Engine.Last("Entities.SetVelocity"));
        Assert.False(flare.Deflected);
    }

    /// <summary>An enemy's arrow (its owner on team 3, the blaster on 2) is sent back and is the deflector's.</summary>
    [Fact]
    public void An_enemy_arrow_in_the_sphere_is_sent_back_and_is_the_deflectors()
    {
        var shots = new ProjectileRig();
        TFArrow arrow = shots.Arrow();
        arrow.FiredBy(ProjectileRig.Victim, new Vector(0f, 1000f, 0f));
        FlameRig rig = Rig(arrow);
        int asked = 0;
        // The arrow's owner's team first (blue), then the blaster's (red) for every ask after.
        rig.Engine.ReturnsEachCall("Players.GetTeam", () => asked++ == 0 ? 3 : 2);

        rig.Effects.AirBlast(Attack(), Script());

        var sent = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(sent);
        Assert.Equal(new Vector(-1000f, 0f, 0f), sent!.ArgAs<Vector>(1));
        Assert.Equal((true, FlameRig.Player.EntryIndex, "tf_flamethrower"), (arrow.Deflected, arrow.OwnerIndex, arrow.WeaponName));
    }

    /// <summary>A teammate's arrow (its owner on the blaster's team) is left alone.</summary>
    [Fact]
    public void The_blast_leaves_a_teammates_arrow()
    {
        var shots = new ProjectileRig();
        TFArrow arrow = shots.Arrow();
        arrow.FiredBy(ProjectileRig.Victim, new Vector(0f, 1000f, 0f));
        FlameRig rig = Rig(arrow);

        rig.Effects.AirBlast(Attack(), Script());

        Assert.False(arrow.Deflected);
    }

    /// <summary>
    /// A reflected Scorch Shot flare is a plain flare (stock re-points its launcher to the deflector's flamethrower,
    /// tf_projectile_flare.cpp:596, and Explode's dynamic_cast to a flare gun, :225, is then null): no Scorch shove, no
    /// detonation, the plain flare's burning crit.
    /// </summary>
    [Fact]
    public void A_reflected_scorch_flare_is_a_plain_flare()
    {
        var shots = new ProjectileRig();
        shots.Engine.Returns("Players.GetTeam", 2);
        TFFlare flare = shots.Flare();
        flare.ArmedBy(20f, TFWeaponId.Flaregun, "tf_weapon_flaregun", critical: false, gravity: 0.3f, ownerIndex: 5,
                      flareMode: TFProjectileBehaviour.FlareScorch, team: 3);
        Assert.Equal(TFProjectileBehaviour.FlareScorch, flare.FlareMode);

        flare.DeflectBy(FlameRig.Player, new Vector(-1000f, 0f, 0f), deflectorCritBoosted: false, "tf_weapon_flamethrower");

        Assert.Equal(TFProjectileBehaviour.FlareNormal, flare.FlareMode);
    }

    /// <summary>
    /// An arrow sent back at the Sniper who loosed it strikes him: the mover's sweep skips its owner, and the deflect hands
    /// it the deflector before the new flight (Retarget, then Steer). Steered first, the sweep's hit on the Sniper was
    /// thrown away every step and the arrow flew through him.
    /// </summary>
    [Fact]
    public void An_arrow_sent_back_strikes_the_player_who_loosed_it()
    {
        var rig = new ProjectileRig();
        var damage = new TF2.Tests.Combat.ScopedDamage(rig.Scopes);
        var arrow = new TFArrow(rig.Engine, rig.Motion, damage, rig.Scopes, rig.HitGroups, rig.Directory, rig.Owners, rig.Hooks,
                                rig.Engine, rig.Temps, rig.Engine);
        arrow.BindAreas(TestServices.Areas(rig.Engine));
        arrow.Handle = ProjectileRig.ProjectileHandle;
        arrow.NetStates = NetStateBinding.AttachLocal(arrow, arrow.Handle, new NetStateFields(new RecordingLog()));
        rig.Directory.Instance = arrow;
        var struck = new TraceResult(true, 0.5f, new Vector(-7.5f, 0f, 0f), new Vector(1f, 0f, 0f), ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetParent", EntityHandle.None)
                  .Returns("Entities.GetOrigin", Vector.Zero)
                  .Returns("Entities.IsPlayer", true)
                  // The Sniper (the rig's victim) blue, the Pyro red: no teammate pass.
                  .ReturnsEachCall("Players.GetTeam", () => rig.Engine.Last("Players.GetTeam")!.ArgAs<EntityHandle>(0) == ProjectileRig.Victim ? 3 : 2)
                  .Returns("WorldQuery.TraceLine", struck)
                  .Returns("WorldQuery.TraceBox", struck)
                  .Ignores("Entities.SetOrigin", "Entities.SetVelocity", "Entities.SetAngles");

        rig.MotionDirectory.Bind(ProjectileRig.ProjectileHandle, arrow);
        arrow.FiredBy(ProjectileRig.Victim, new Vector(1000f, 0f, 0f));
        arrow.DeflectBy(ProjectileRig.Player, new Vector(-1000f, 0f, 0f), deflectorCritBoosted: false, "tf_weapon_flamethrower");
        rig.Motion.RunFrame(0.015f);

        Assert.Contains(damage.Hits, h => h.Victim == ProjectileRig.Victim);
    }

    /// <summary>
    /// A flare sent back at the player who fired it strikes him (review 2: the flare's Retarget-then-Steer order, held by no
    /// fact before): the mover's sweep skips its owner, and the deflect hands it the deflector first.
    /// </summary>
    [Fact]
    public void A_flare_sent_back_strikes_the_player_who_fired_it()
    {
        var rig = new ProjectileRig();
        TFFlare flare = rig.Flare();
        flare.Handle = ProjectileRig.ProjectileHandle;
        rig.Directory.Instance = flare;
        var struck = new TraceResult(true, 0.5f, new Vector(-7.5f, 0f, 0f), new Vector(1f, 0f, 0f), ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetParent", EntityHandle.None)
                  .Returns("Entities.GetOrigin", Vector.Zero)
                  .ReturnsEachCall("Entities.IsPlayer", () => rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim)
                  .ReturnsEachCall("Players.GetTeam", () => rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim ? 3 : 2)
                  .Returns("WorldQuery.TraceLine", struck)
                  .Returns("WorldQuery.TraceBox", struck)
                  .Ignores("Entities.SetOrigin", "Entities.SetVelocity", "Entities.SetAngles");

        rig.MotionDirectory.Bind(ProjectileRig.ProjectileHandle, flare);
        flare.ArmedBy(30f, TFWeaponId.Flaregun, "tf_weapon_flaregun", critical: false, gravity: 0.3f, ownerIndex: ProjectileRig.Victim.EntryIndex,
                      flareMode: TFProjectileBehaviour.FlareNormal, team: 3);
        flare.FiredBy(ProjectileRig.Victim, new Vector(2000f, 0f, 0f));
        flare.DeflectBy(ProjectileRig.Player, new Vector(-2000f, 0f, 0f), deflectorCritBoosted: false, "tf_weapon_flamethrower");
        rig.Motion.RunFrame(0.015f);

        Assert.Contains(rig.Engine.Calls, c => c.Member == "Damage.TakeDamage" && c.Args[0] is EntityHandle h && h == ProjectileRig.Victim);
    }
}
