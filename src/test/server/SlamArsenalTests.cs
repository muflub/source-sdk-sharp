/*
 * weapon_slam through the server's Arsenal: the pool a pickup brings, the
 * satchel and the tripmine the server's arms make, the detonator, and the
 * weapon going away when it is spent. The viewmodel answers from v_slam.mdl's
 * sequence table (SlamMachineTests.VSlam).
 */

using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class SlamArsenalTests
{
    private const uint PlayerHandle = 0x0001_0001;

    private static EntityHandle Player => new(PlayerHandle);

    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private const float Tick = 0.015f;

    private sealed class Rig
    {
        public required Arsenal Arsenal;
        public required RecordingEngine Engine;
        public required WeaponSwitchTests.SpawningEntities World;
        public required ScriptedEntityRegistry Registry;
        public float Now = 10f;

        /// <summary>Commands at a fixed tick for <paramref name="seconds"/>, the clock moving with them.</summary>
        public void Hold(int buttons, float seconds)
        {
            float until = Now + seconds;
            for (; Now < until; Now += Tick)
            {
                Engine.Returns("Clock.Now", Now);
                var cmd = new UserCmd { Buttons = buttons };
                Arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);
            }
        }

        public int Pool => Arsenal.Pools(Player).TryGetValue("slam", out int n) ? n : 0;

        public string? Active => Arsenal.Holdings(Player).Where(h => h.Active).Select(h => h.ClassName).FirstOrDefault();
    }

    private static Rig Armed(WallProbe ahead = WallProbe.Nothing, string? first = null)
    {
        bool wall = ahead != WallProbe.Nothing;

        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", 1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f))
            .Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f))
            .Returns("Entities.GetWorldSpaceCenter", new Vector(0f, 0f, 36f))
            .Returns("Entities.GetVelocity", new Vector(0f, 0f, 0f))
            .Returns("Entities.IsPlayer", ahead == WallProbe.Character)
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            .Returns("WorldQuery.TraceLine",
                     new TraceResult(wall, wall ? 0.5f : 1f, new Vector(20f, 0f, 64f),
                                     new Vector(-1f, 0f, 0f), wall ? new EntityHandle(0x0002_0009) : EntityHandle.None))
            .Returns("Clock.Now", 10f);

        engine.ReturnsEachCall("EntityRender.LookupSequenceByActivity",
            () => Array.FindIndex(SlamMachineTests.VSlam, s => s.Activity == engine.Calls[^1].ArgAs<string>(1)));
        engine.ReturnsEachCall("EntityRender.SequenceDuration", () =>
        {
            int sequence = engine.Calls[^1].ArgAs<int>(1);
            return sequence >= 0 && sequence < SlamMachineTests.VSlam.Length ? SlamMachineTests.VSlam[sequence].Duration : 0f;
        });

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out RecordingEngine entityEngine);

        // What the satchel and the tripmine ask of the engine as they spawn (their
        // glow, their facing) is not under test here.
        entityEngine.Strict = false;
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");

        var world = new WeaponSwitchTests.SpawningEntities(engine, registry);

        var arsenal = new Arsenal(new RecordingLog(), world, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups());

        if (first is not null)
            Assert.True(arsenal.GiveWeapon(Player, first), $"armed with {first} first");

        Assert.True(arsenal.GiveWeapon(Player, EntityNames.WeaponSlam), "armed with the SLAM");

        if (first is not null)
            Assert.Equal(SwitchOutcome.Switched, arsenal.Switch(Player, EntityNames.WeaponSlam));

        // The carried pair exists from here on: the viewmodel answers its sequences.
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;

        var rig = new Rig { Arsenal = arsenal, Engine = engine, World = world, Registry = registry };
        rig.Hold(0, 1.2f);
        return rig;
    }

    [Fact]
    public void AFirstPickupBringsThreeCharges()
    {
        var rig = Armed();

        Assert.Equal(3, rig.Pool);
        Assert.Equal(EntityNames.WeaponSlam, rig.Active);
    }

    [Fact]
    public void ADuplicatePickupBringsThreeMoreUpToTheCarryLimit()
    {
        var rig = Armed();

        Assert.True(rig.Arsenal.GiveWeapon(Player, EntityNames.WeaponSlam), "the pool had room");

        Assert.Equal(5, rig.Pool);
        Assert.Single(rig.Arsenal.Holdings(Player));
    }

    [Fact]
    public void ThePrimaryThrowsASatchelAlongTheAim()
    {
        var rig = Armed();

        rig.Hold(Attack, Tick);
        rig.Hold(0, 0.6f);

        Assert.Contains(EntityNames.NpcSatchel, rig.World.CreatedClasses);
        Assert.Equal(2, rig.Pool);

        var shove = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.True(shove is not null, "the satchel is thrown, not dropped");
        Assert.Equal(new Vector(500f, 0f, 0f), shove!.Value.ArgAs<Vector>(1));
    }

    [Fact]
    public void TheThrownSatchelIsLiveAndBelongsToItsThrower()
    {
        var rig = Armed();

        rig.Hold(Attack, Tick);
        rig.Hold(0, 0.6f);

        var satchel = Assert.Single(rig.Registry.OfClass(EntityNames.NpcSatchel).OfType<Satchel>());
        Assert.True(satchel.IsLive);
        Assert.Equal(Player, satchel.Thrower);
        Assert.False(satchel.IsAttached);
    }

    [Fact]
    public void TheDetonatorSetsOffTheThrowersSatchel()
    {
        var rig = Armed();

        rig.Hold(Attack, Tick);
        rig.Hold(0, 2.4f);
        rig.Engine.Clear();

        rig.Hold(Attack2, Tick);

        var satchel = Assert.Single(rig.Registry.OfClass(EntityNames.NpcSatchel).OfType<Satchel>());
        Assert.True(satchel.NextThinkDelay("explode") > 0f, "InputExplode is queued 0.2 s out");
        Assert.Contains(rig.Engine.AllOf("Sound.EntityEmitSound"),
                        c => c.ArgAs<string>(1) == SlamMachine.DetonateSound);
    }

    [Fact]
    public void ThePrimaryAtAWallPlantsATripmineOnIt()
    {
        var rig = Armed(WallProbe.Surface);

        rig.Hold(Attack, Tick);
        rig.Hold(0, 0.4f);

        Assert.Contains(EntityNames.NpcTripmine, rig.World.CreatedClasses);
        Assert.DoesNotContain(EntityNames.NpcSatchel, rig.World.CreatedClasses);
        Assert.Equal(2, rig.Pool);

        // VectorAngles( normal ) with pitch + 90: the wall faces -X, so yaw 180
        // and the mine lies on it at pitch 90.
        var turned = rig.Engine.Last("Entities.SetAngles");
        Assert.True(turned is not null, "the tripmine is turned onto the wall");
        Assert.Equal(new Vector(90f, 180f, 0f), turned!.Value.ArgAs<Vector>(1));

        var planted = Assert.Single(rig.Registry.OfClass(EntityNames.NpcTripmine).OfType<Tripmine>());
        Assert.Equal(Player, planted.Planter);
    }

    [Fact]
    public void ASpentSlamGoesAndTheHandsTakeTheWeaponBefore()
    {
        var rig = Armed(first: EntityNames.WeaponCrowbar);

        // Throw all three, then press the detonator.
        for (int i = 0; i < 3; i++)
        {
            rig.Hold(Attack, Tick);
            rig.Hold(0, 2.6f);
        }

        Assert.Equal(0, rig.Pool);
        Assert.Equal(EntityNames.WeaponSlam, rig.Active);

        rig.Hold(Attack2, Tick);
        rig.Hold(0, 1.5f);

        Assert.Equal(EntityNames.WeaponCrowbar, rig.Active);
        Assert.DoesNotContain(rig.Arsenal.Holdings(Player), h => h.ClassName == EntityNames.WeaponSlam);
        Assert.DoesNotContain(EntityNames.WeaponSlam, rig.World.CreatedClasses);
    }

    [Fact]
    public void TheSlamsStateFollowsItThroughASwitch()
    {
        var rig = Armed(first: EntityNames.WeaponCrowbar);

        rig.Hold(Attack, Tick);
        rig.Hold(0, 2.6f);

        Assert.Equal(SwitchOutcome.Switched, rig.Arsenal.Switch(Player, EntityNames.WeaponCrowbar));
        Assert.Equal(SwitchOutcome.Switched, rig.Arsenal.Switch(Player, EntityNames.WeaponSlam));

        // Deploy re-arms from what is still out: the satchel thrown before the switch.
        var (weapon, _) = rig.Arsenal.CarriedPair(Player);
        var state = Assert.IsType<SsWeapon>(rig.Registry.Get(weapon)).State;
        Assert.True(SlamBits.Has(state.Special, SlamBits.DetonatorArmed));
        Assert.Equal(SlamActivities.DetonatorThrowDraw, SlamBits.Activity(state.Special));
    }
}
