using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Projectiles;
using TF2.Shared;

using SourceSharp.Tests;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The launch converges on the crosshair: <c>GetProjectileFireSetup</c>
/// (<c>tf_weaponbase.cpp:5762-5808</c>) traces the eye's line and sends the
/// shot from the offset muzzle at where it stopped. The rig's eye stands at
/// (0, 0, 64) looking along +x; the facts say what the eye's line meets.
/// </summary>
public sealed class TFProjectileConvergenceFacts
{
    private static readonly EntityHandle Target = new(0x0001_0000u | 5);

    private static TFWeaponScript Script(string name)
        => new TFWeaponScripts(new RecordingLog(), TF2.Tests.Weapons.WeaponFixtures.WithExtras(
                   TF2.Tests.Weapons.WeaponFixtures.ShippedWithClasses())).Get(name)
           ?? throw new InvalidOperationException($"no script {name}");

    private static WeaponAttackContext Attack(int owner, WeaponNetState? weapon = null) => new(default!, weapon!, null!, 1f, true,
        null!, new WeaponCommand(1, 5, owner));

    /// <summary>Where the launched shot's straight line crosses the plane x = <paramref name="x"/>.</summary>
    private static Vector Crossing(ProjectileRig rig, float x)
    {
        Vector origin = rig.Engine.Last("Entities.SetOrigin")!.ArgAs<Vector>(1);
        Vector velocity = rig.Engine.Last("Entities.SetVelocity")!.ArgAs<Vector>(1);
        Assert.True(velocity.X > 0f, "the shot flies along the eye");
        float t = (x - origin.X) / velocity.X;
        return origin + velocity * t;
    }

    /// <summary>
    /// A drawn arrow at a head centred in the crosshair 300 u out: its line
    /// crosses the head's plane at the head, not at the muzzle's 8 u to the
    /// side and 3 u below.
    /// </summary>
    [Fact]
    public void An_arrow_fired_at_a_centred_head_at_300_u_crosses_the_head()
    {
        var rig = new ProjectileRig();
        var arrow = rig.Arrow();
        rig.Directory.Instance = arrow;
        Vector head = new(300f, 0f, 64f);
        rig.Engine.Returns("WorldQuery.TraceLine", new TraceResult(true, 300f / TFProjectileEffects.FireSetupReach,
                                                                   head, new Vector(-1f, 0f, 0f), Target))
                  .Returns("Entities.IsPlayer", true)
                  .ReturnsEachCall("Players.GetTeam", () => rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == Target ? 3 : 2);
        var bow = new WeaponNetState();
        bow.BindLocal();

        rig.Effects.Projectile(Attack(3, bow), Script("tf_weapon_compound_bow"), new ShotDamage(50f), 1f);

        Vector crossed = Crossing(rig, head.X);
        Assert.Equal(head.Y, crossed.Y, 0.05f);
        Assert.Equal(head.Z, crossed.Z, 0.05f);
    }

    /// <summary>A rocket at a point on a wall 500 u out, below the eye: its line lands on that point.</summary>
    [Fact]
    public void A_rocket_fired_at_a_wall_point_lands_on_it()
    {
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rig.Directory.Instance = rocket;
        Vector spot = new(500f, 0f, 20f);
        rig.Engine.Returns("Entities.GetEyeAngles", Angles.FromVector(spot - new Vector(0f, 0f, 64f)))
                  .Returns("WorldQuery.TraceLine", new TraceResult(true, 0.25f, spot, new Vector(-1f, 0f, 0f), EntityHandle.None));

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_rocketlauncher"), new ShotDamage(90f), 0f);

        Vector crossed = Crossing(rig, spot.X);
        Assert.Equal(spot.Y, crossed.Y, 0.05f);
        Assert.Equal(spot.Z, crossed.Z, 0.05f);
    }

    /// <summary>
    /// Within a tenth of the reach (200 u) the line's far end is aimed at
    /// instead (:5800-5807): a wall 100 u out is not converged on.
    /// </summary>
    [Fact]
    public void A_near_wall_aims_the_shot_at_the_lines_far_end()
    {
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rig.Directory.Instance = rocket;
        rig.Engine.Returns("WorldQuery.TraceLine", new TraceResult(true, 0.05f, new Vector(100f, 0f, 64f),
                                                                   new Vector(-1f, 0f, 0f), EntityHandle.None));

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_rocketlauncher"), new ShotDamage(90f), 0f);

        Vector crossed = Crossing(rig, TFProjectileEffects.FireSetupReach);
        Assert.Equal(0f, crossed.Y, 0.05f);
        Assert.Equal(64f, crossed.Z, 0.05f);
    }

    /// <summary>
    /// The aim line looks past the shooter's teammates (CTraceFilterIgnoreTeammates,
    /// :5787-5790): a teammate in the line is passed and the line's next stop
    /// is the aim point.
    /// </summary>
    [Fact]
    public void A_teammate_in_the_line_is_looked_past()
    {
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rig.Directory.Instance = rocket;
        var mate = new EntityHandle(0x0001_0000u | 6);
        Vector wall = new(800f, 0f, 64f);
        rig.Engine.ReturnsEachCall("WorldQuery.TraceLine", () =>
                      rig.Engine.Calls[^1].Args[3] is EntityHandle ignore && ignore == mate
                          ? new TraceResult(true, 0.3f, wall, new Vector(-1f, 0f, 0f), EntityHandle.None)
                          : new TraceResult(true, 0.15f, new Vector(300f, 0f, 64f), new Vector(-1f, 0f, 0f), mate))
                  .Returns("Entities.IsPlayer", true)
                  .Returns("Players.GetTeam", 2);

        rig.Effects.Projectile(Attack(3), Script("tf_weapon_rocketlauncher"), new ShotDamage(90f), 0f);

        Assert.Equal(2, rig.Engine.CountOf("WorldQuery.TraceLine"));
        Vector crossed = Crossing(rig, wall.X);
        Assert.Equal(0f, crossed.Y, 0.05f);
        Assert.Equal(64f, crossed.Z, 0.05f);
    }
}
