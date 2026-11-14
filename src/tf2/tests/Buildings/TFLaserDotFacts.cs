using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Buildings;
using TF2.Server.Medigun;
using TF2.Shared;
using TF2.Shared.Econ;

using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The Wrangler's laser dot (<c>CTFLaserPointer</c>'s dot arms,
/// tf_weapon_laser_pointer.cpp:240-310): <c>CTFWeaponBaseMelee</c>-style the
/// entity stands beside the holder — the server's <see cref="TFSentryWrangle"/>
/// makes the one <c>env_laserdot</c> with the deploy's half-second past, stands
/// it where the view's trace stops every command, and takes it back with the
/// holster. The facts ride the seam: what the engine was asked, and what the
/// dot instance's wire state carries. The team sprite the client draws from
/// that byte is the client lane's (<c>ClientLaserDotTests</c>).
/// </summary>
public sealed class TFLaserDotFacts
{
    private static readonly EntityHandle DotHandle = new(0x0004_0021u);
    private static readonly EntityHandle PlayerHandle = new(0x0001_0003u);
    private static readonly EntityHandle EnemyHandle = new(0x0001_0009u);

    /// <summary>A dot directory that answers only once the spawn has gone through, the live shape.</summary>
    private sealed class DotDirectory(LaserDot dot) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h) => h == DotHandle ? dot : null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => true;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>One ask's world: the holder, the made dot, a team for it and a wall at four hundred.</summary>
    private static (RecordingEngine Engine, LaserDot Dot, DotDirectory Directory) World()
    {
        var engine = TestServices.Fake();
        var dot = new LaserDot(new EntityRoster<LaserDot>());
        dot.BindAreas(TestServices.Areas(engine));
        dot.NetStates = NetStateBinding.AttachLocal(dot, DotHandle, new NetStateFields(new RecordingLog()));
        dot.Handle = DotHandle;

        engine.Returns("Players.GetHandle", PlayerHandle)
              .Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f))
              .Returns("Entities.GetEyeAngles", Vector.Zero)
              .Returns("Entities.Create", DotHandle)
              .Returns("Entities.GetOrigin", Vector.Zero)
              .Returns("Entities.IsValid", true)
              .Returns("Entities.SetOwner", true)
              .Returns("Entities.GetIndex", 3)
              .Returns("Entities.IsPlayer", true)
              .Returns("Players.GetTeam", (int)TFTeam.Blue)
              .Returns("WorldQuery.TraceLine",
                        new TraceResult(true, 0.5f, new Vector(400f, 0f, 0f), new Vector(0f, 0f, 1f), EnemyHandle));

        var directory = new DotDirectory(dot);
        return (engine, dot, directory);
    }

    private static TFSentryWrangle Wrangle(RecordingEngine engine, DotDirectory directory) =>
        new(new TFBuildings(directory), engine, engine, engine, directory);

    /// <summary>The parts the real behaviour runs on: the wrangle under test is the seam's answer.</summary>
    private static TFWeaponParts Parts(ITFSentryWrangle wrangle)
    {
        var log = new RecordingLog();
        var shipped = WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses());
        var scripts = new TFWeaponScripts(log, shipped);
        var attributes = new AttributeHooks();
        var chain = TFWeaponHookChain.Of([new TFSwordHook(attributes), new TFAttributeWeaponHook(attributes),
                                         new TFCritBoostHook(), new TFKingReloadHook()]);
        var chargeRate = new WeaponMedigunChargeRateConVar();
        chargeRate.Bind(-1, "weapon_medigun_charge_rate", "40");
        var spread = new TFUseFixedWeaponspreadsConVar();
        spread.Bind(-1, "tf_use_fixed_weaponspreads", "0");
        var burstammo = new TFFlameThrowerBurstammoConVar();
        burstammo.Bind(-1, "tf_flamethrower_burstammo", "20");

        return new TFWeaponParts(scripts, chain, new TFCritBuckets(), new FixedCritSettings(), new FakeOwners(),
                                 new RecordingEffects(), new RecordingBuilderPlacement(), new FakePredictedData(),
                                 new FakeRoundSetup(), chargeRate, spread, new NobodyWantsAutoReload(), burstammo,
                                 new TFServerMedigunPrediction(), new RecordingSapperState(), wrangle,
                                 new EmptyRevengeCrits());
    }

    [Fact]
    public void The_first_ask_past_the_half_second_makes_the_dot()
    {
        var (engine, _, directory) = World();
        var behaviour = new TFWranglerBehaviour(Parts(Wrangle(engine, directory)));
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed("tf_weapon_laser_pointer", clip: -1, reserve: 0);

        // Deploy arms the dot's creation half a second past the switch
        // (:110); inside the arm nothing is made, the dot's ask itself waits
        // with it (:145-156).
        behaviour.Deploy(world.Attack(w, vm, 10f));
        behaviour.ItemPostFrame(world.Attack(w, vm, 10.4f));

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.Create");

        behaviour.ItemPostFrame(world.Attack(w, vm, 10.6f));

        Assert.Contains(engine.Calls, c => c.Member == "Entities.Create"
                                        && c.ArgAs<string>(0) == EntityNames.EnvLaserdot);
    }

    [Fact]
    public void The_ask_stands_the_dot_where_the_trace_stopped_and_makes_no_second_one()
    {
        var (engine, dot, directory) = World();
        var wrangle = Wrangle(engine, directory);

        wrangle.Aim(3);
        wrangle.Aim(3);

        // One dot per holder (:240's own guard): the entity is made once, and
        // every ask only moves it (:300-310). The last write is the position
        // the trace ended on, not the creation stand.
        Assert.Equal(1, engine.Calls.Count(c => c.Member == "Entities.Create"));
        EngineCall stood = engine.Last("Entities.SetOrigin")!.Value;
        Assert.Equal(DotHandle, stood.ArgAs<EntityHandle>(0));
        Assert.Equal(new Vector(400f, 0f, 0f), stood.ArgAs<Vector>(1));
        Assert.Equal(new Vector(0f, 0f, 1f), dot.SurfaceNormal);
    }

    [Fact]
    public void The_dot_wears_the_holders_team_and_names_the_owner_once()
    {
        var (engine, dot, directory) = World();
        var wrangle = Wrangle(engine, directory);

        wrangle.Aim(3);
        wrangle.Aim(3);

        // SetOwnerEntity rides the creation alone (:256); the team byte —
        // which sprite the client half binds (:41-42) — refreshes every ask.
        Assert.Equal(1, engine.Calls.Count(c => c.Member == "Entities.SetOwner"));
        Assert.Equal(3, dot.State.OwnerIndex);
        Assert.Equal((byte)TFTeam.Blue, dot.State.Team);
    }

    [Fact]
    public void The_dot_names_the_hurtable_it_stands_on()
    {
        var (engine, dot, directory) = World();
        var wrangle = Wrangle(engine, directory);

        wrangle.Aim(3);

        // The shared dot rule: the world (entry 0) is nobody and only what
        // takes damage is named; a player is hurtworthy at once.
        Assert.Equal(EnemyHandle, dot.TargetEntity);
    }

    [Fact]
    public void Holstering_the_pointer_takes_the_dot_back()
    {
        var (engine, _, directory) = World();
        var wrangle = Wrangle(engine, directory);

        wrangle.Aim(3);
        wrangle.Release(3);

        // DestroyLaserDot (:265-284): the pointer left the hands, the entity
        // goes with it.
        Assert.Contains(engine.Calls, c => c.Member == "Entities.Remove"
                                         && c.ArgAs<EntityHandle>(0) == DotHandle);

        // Gone from the world means gone from the holder's book: the next
        // ask makes the dot again rather than leaning on the stale handle.
        wrangle.Aim(3);
        Assert.Equal(2, engine.Calls.Count(c => c.Member == "Entities.Create"));
    }
}
