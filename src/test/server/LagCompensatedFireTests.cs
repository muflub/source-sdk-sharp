/*
 * THE CORE'S ATTACKS RUN INSIDE THE LAG COMPENSATION (CHL2MP_Player::FireBullets,
 * CBaseHL2MPBludgeonWeapon::PrimaryAttack): the arsenal's traces see the other
 * players where the shooter's command saw them, and they are put back after.
 *
 * The target (slot 2) ran along +x and is at x=200 now; the shooter's command
 * was sent at tick 105, when the target stood at x=150. The world's trace hits
 * the target only if it stands at x=150 when the trace is made.
 */

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class LagCompensatedFireTests
{
    private const int Attack = (int)InputButtons.Attack;

    private static EntityHandle Shooter => new(0x0001_0001u);

    private static (Arsenal Arsenal, RecordingEngine Engine, LagRig Rig) Armed(string row, bool compensated)
    {
        var rig = new LagRig();
        Assert.Equal(Shooter, rig.ShooterPlayer.Handle);
        EntityHandle target = rig.TargetPlayer.Handle;
        Vector seen = rig.TargetAt(105);

        // The world: a round (and a swing's line) along +x hits the target where
        // it stands, if it stands where the shooter saw it.
        TraceResult LineAt()
            => rig.TargetPlayer.Origin.Equals(seen)
                ? new TraceResult(true, 0.5f, seen, new Vector(-1f, 0f, 0f), target)
                : new TraceResult(false, 1f, default, default, EntityHandle.None);

        SurfaceTraceResult SurfaceAt()
            => rig.TargetPlayer.Origin.Equals(seen)
                ? new SurfaceTraceResult(true, 0.5f, seen, new Vector(-1f, 0f, 0f), target, 13, 0, HitGroup.Chest)
                : new SurfaceTraceResult(false, 1f, default, default, EntityHandle.None, -1, 0);

        var engine = Mock().Returns("Entities.IsValid", true).Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f))
            .Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f))
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 0f))
            .Returns("Players.IsAlive", true)
            .Returns("Clock.Now", 10f);
        engine.ReturnsEachCall("WorldQuery.TraceLine", () => LineAt());
        engine.ReturnsEachCall("WorldQuery.TraceLineSurface", () => SurfaceAt());
        engine.Strict = false;

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);
        registry.KeyValue(Shooter, EntityNames.Player, "targetname", "fixture_player");

        var arsenal = new Arsenal(new RecordingLog(), new WeaponSwitchTests.SpawningEntities(engine, registry),
                                  engine, engine, engine, engine, engine, engine, engine, registry, engine,
                                  new DamageHitGroups());
        if (compensated)
            arsenal.LagCompensation = rig.Lag;

        Assert.True(arsenal.GiveWeapon(Shooter, row), "the row can be given");
        return (arsenal, engine, rig);
    }

    /// <summary>One attacking command, sent at tick 105, run as the module runs it: the current command.</summary>
    private static List<EntityHandle> Attack1(Arsenal arsenal, RecordingEngine engine, LagRig rig)
    {
        var hit = new List<EntityHandle>();
        engine.WhenCalled("Damage.ApplyAt", call => hit.Add((EntityHandle)call.Args[0]!));

        engine.Returns("Clock.Now", 30f);
        UserCmd cmd = rig.Command(105);
        cmd.Buttons = Attack;
        using (rig.Commands.Begin(Shooter, in cmd))
            arsenal.OnPlayerCommand(Shooter, in cmd, firstTime: true);

        return hit;
    }

    [Fact]
    public void ABulletHitsTheTargetWhereTheShooterSawItAndTheTargetIsPutBack()
    {
        var (arsenal, engine, rig) = Armed("weapon_smg1", compensated: true);

        List<EntityHandle> hit = Attack1(arsenal, engine, rig);

        Assert.Contains(rig.TargetPlayer.Handle, hit);
        Assert.Equal(rig.TargetAt(110), rig.TargetPlayer.Origin);
        Assert.False(rig.Lag.IsCurrentlyDoingLagCompensation);
    }

    [Fact]
    public void WithoutTheCompensationTheSameBulletMisses()
    {
        var (arsenal, engine, rig) = Armed("weapon_smg1", compensated: false);

        List<EntityHandle> hit = Attack1(arsenal, engine, rig);

        Assert.DoesNotContain(rig.TargetPlayer.Handle, hit);
    }

    [Fact]
    public void WithUnlagOffTheSameBulletMisses()
    {
        var (arsenal, engine, rig) = Armed("weapon_smg1", compensated: true);
        rig.Settings.Unlag = false;

        List<EntityHandle> hit = Attack1(arsenal, engine, rig);

        Assert.DoesNotContain(rig.TargetPlayer.Handle, hit);
    }

    [Fact]
    public void ACrowbarSwingHitsTheTargetWhereTheShooterSawIt()
    {
        var (arsenal, engine, rig) = Armed(EntityNames.WeaponCrowbar, compensated: true);

        List<EntityHandle> hit = Attack1(arsenal, engine, rig);

        Assert.Contains(rig.TargetPlayer.Handle, hit);
        Assert.Equal(rig.TargetAt(110), rig.TargetPlayer.Origin);
    }

    [Fact]
    public void TheShotIsNotedForTheNextCommandsCompensation()
    {
        var (arsenal, engine, rig) = Armed("weapon_smg1", compensated: true);
        Attack1(arsenal, engine, rig);

        // Three commands later, not attacking: still within HL2MP's five.
        UserCmd idle = rig.Command(105, attack: false, number: 53);
        rig.Lag.Start(Shooter, in idle);

        Assert.Equal(rig.TargetAt(105), rig.TargetPlayer.Origin);
    }
}
