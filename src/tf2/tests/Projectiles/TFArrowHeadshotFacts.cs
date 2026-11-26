using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Tests.Combat;

using SourceSharp.Tests;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The Huntsman's headshot, from stock: the touch traces on along the arrow's
/// flight against hitboxes (<c>ArrowTouch</c>, <c>tf_projectile_arrow.cpp:797-808</c>),
/// and a head box on an enemy crits under <c>TF_DMG_CUSTOM_HEADSHOT</c>
/// (<c>StrikeTarget</c>, :446-450, :492-496), from a scorer's arrow that is not
/// the rescue claw (<c>CanHeadshot</c>, :295-309).
/// </summary>
public sealed class TFArrowHeadshotFacts
{
    private static readonly EntityHandle Third = new(0x0001_0000u | 7);

    private const float Speed = 2000f;
    private const float FrameTime = 0.015f;

    /// <summary>An arrow whose touch's trace answers <paramref name="group"/> on <paramref name="struck"/>.</summary>
    private static (TFArrow Arrow, ScopedDamage Hits, ProjectileRig Rig) Rig(HitGroup group, EntityHandle struck,
                                                                              TFProjectile projectile = TFProjectile.Arrow)
    {
        var rig = new ProjectileRig();
        var hits = new ScopedDamage(rig.Scopes, rig.HitGroups);
        var arrow = new TFArrow(rig.Engine, rig.Motion, hits, rig.Scopes, rig.HitGroups,
                                rig.Directory, rig.Owners, rig.Hooks, rig.Engine, rig.Temps, rig.Engine,
                                rig.Engine, rig.Engine, rig.Inflictors);
        arrow.BindAreas(TestServices.Areas(rig.Engine));
        arrow.ArmedBy(TFWeaponId.CompoundBow, "tf_weapon_compound_bow", critical: false, lit: false,
                      TFProjectileDefs.ArrowDamage(10f, 0f), TFProjectileDefs.ArrowGravity(0f), projectile);
        arrow.FiredBy(ProjectileRig.Player, new Vector(Speed, 0f, 0f));

        rig.Engine.Returns("Entities.IsValid", true);
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetVelocity", new Vector(Speed, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(100f, 0f, 70f));
        rig.Engine.Returns("Frame.GetFrameTime", FrameTime);
        rig.Engine.Returns("WorldQuery.TraceLineSurface",
                           new SurfaceTraceResult(true, 0.3f, new Vector(110f, 0f, 70f), new Vector(-1f, 0f, 0f),
                                                  struck, 0, 0, group));

        // The shooter and the third player one team, the victim the other.
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim ? 2 : 3);
        return (arrow, hits, rig);
    }

    /// <summary>The head box on an enemy: the hit is critical under the headshot custom.</summary>
    [Fact]
    public void AnArrowIntoTheHeadCrits()
    {
        var (arrow, hits, _) = Rig(HitGroup.Head, ProjectileRig.Victim);

        arrow.OnTouch(ProjectileRig.Victim);

        var hit = Assert.Single(hits.Hits);
        Assert.Equal(ProjectileRig.Victim, hit.Victim);
        Assert.Equal(ProjectileRig.Player, hit.Attacker);
        Assert.True(hit.Critical);
        Assert.Equal(TFDamageCustom.Headshot, hit.Custom);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.CompoundBow), hit.Type);
    }

    /// <summary>The control: the chest box on the same enemy is a plain hit.</summary>
    [Fact]
    public void AnArrowIntoTheBodyDoesNotCrit()
    {
        var (arrow, hits, _) = Rig(HitGroup.Chest, ProjectileRig.Victim);

        arrow.OnTouch(ProjectileRig.Victim);

        var hit = Assert.Single(hits.Hits);
        Assert.False(hit.Critical);
        Assert.Equal(TFDamageCustom.None, hit.Custom);
    }

    /// <summary>The trace's head box on someone other than the struck player names nothing: a body shot.</summary>
    [Fact]
    public void AHeadBoxOnAnotherEntityIsNoHeadshot()
    {
        var (arrow, hits, _) = Rig(HitGroup.Head, Third);

        arrow.OnTouch(ProjectileRig.Victim);

        var hit = Assert.Single(hits.Hits);
        Assert.False(hit.Critical);
        Assert.Equal(TFDamageCustom.None, hit.Custom);
    }

    /// <summary>The rescue claw cannot headshot (<c>CanHeadshot</c>, :301-306).</summary>
    [Fact]
    public void TheRescueClawCannotHeadshot()
    {
        var (arrow, hits, _) = Rig(HitGroup.Head, ProjectileRig.Victim, TFProjectile.BuildingRepairBolt);

        arrow.OnTouch(ProjectileRig.Victim);

        var hit = Assert.Single(hits.Hits);
        Assert.False(hit.Critical);
        Assert.Equal(TFDamageCustom.None, hit.Custom);
    }

    /// <summary>
    /// The touch's line (:799-801): from the arrow (here a little behind it, on
    /// the box face the sweep stopped it at) on along its velocity for a frame,
    /// against hitboxes, past the arrow itself.
    /// </summary>
    [Fact]
    public void TheTouchTracesOnAlongTheFlightForAFrame()
    {
        var (arrow, _, rig) = Rig(HitGroup.Head, ProjectileRig.Victim);

        arrow.OnTouch(ProjectileRig.Victim);

        var call = Assert.Single(rig.Engine.Calls, c => c.Member == "WorldQuery.TraceLineSurface");
        Assert.Equal(new Vector(100f - TFArrow.HitboxTraceBackoff, 0f, 70f), call.Args[0]);
        Assert.Equal(new Vector(100f + Speed * FrameTime, 0f, 70f), call.Args[1]);
        Assert.Equal(TraceMask.Shot, call.Args[2]);
        Assert.Equal(arrow.Handle, call.Args[3]);
    }

    /// <summary>
    /// A sent-back arrow's headshot is its new shooter's: the team test reads
    /// the shooter at the touch. The airblast's deflect re-launches the arrow
    /// under the deflector (the Pyro lane's <c>DeflectBy</c>); the second
    /// <c>FiredBy</c> stands in for it here, the same shooter change.
    /// </summary>
    [Fact]
    public void ASentBackArrowHeadshotsItsNewShootersEnemies()
    {
        var (arrow, hits, _) = Rig(HitGroup.Head, ProjectileRig.Player);
        arrow.FiredBy(ProjectileRig.Victim, new Vector(-Speed, 0f, 0f));

        arrow.OnTouch(ProjectileRig.Player);

        var hit = Assert.Single(hits.Hits);
        Assert.Equal(ProjectileRig.Player, hit.Victim);
        Assert.Equal(ProjectileRig.Victim, hit.Attacker);
        Assert.True(hit.Critical);
        Assert.Equal(TFDamageCustom.Headshot, hit.Custom);
    }

    /// <summary>
    /// A head box on a teammate of the shooter is no headshot (the enemy test,
    /// :478): the arrow flown past the teammate window
    /// (<see cref="TFProjectileDefs.CollideWithTeammatesDelay"/>) strikes him plain.
    /// </summary>
    [Fact]
    public void AHeadBoxOnATeammateIsNoHeadshot()
    {
        var (arrow, hits, rig) = Rig(HitGroup.Head, Third);
        arrow.Handle = ProjectileRig.ProjectileHandle;
        rig.Directory.Instance = arrow;
        rig.Engine.Returns("Entities.GetParent", EntityHandle.None)
                  .Returns("Entities.GetAngles", Vector.Zero)
                  .Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None))
                  .Ignores("Entities.SetOrigin", "Entities.SetVelocity", "Entities.SetAngles");
        // The jar facts' recipe (TFJarFacts): bound on the projectile handle and
        // re-launched, one frame flies the age past the grace.
        rig.MotionDirectory.Bind(ProjectileRig.ProjectileHandle, arrow);
        arrow.FiredBy(ProjectileRig.Player, new Vector(Speed, 0f, 0f));
        rig.Motion.RunFrame(0.3f);
        Assert.True(rig.Motion.SecondsSinceLaunch(arrow.Handle) > TFProjectileDefs.CollideWithTeammatesDelay);

        arrow.OnTouch(Third);

        var hit = Assert.Single(hits.Hits);
        Assert.Equal(Third, hit.Victim);
        Assert.False(hit.Critical);
        Assert.NotEqual(TFDamageCustom.Headshot, hit.Custom);
    }
}
