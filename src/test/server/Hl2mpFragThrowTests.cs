using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server half of a frag throw (weapon_frag.cpp, hl2mp: ThrowGrenade,
/// LobGrenade, RollGrenade and Fraggrenade_Create), driven through a real
/// arsenal: where the grenade starts, the velocity it leaves with (the
/// thrower's own plus the throw's), its spin about its own axes, and its
/// timer.
/// </summary>
public class Hl2mpFragThrowTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);
    private static readonly Vector Eye = new(0, 0, 64);
    private static readonly Vector Running = new(10, 0, 0);

    private sealed record Throw(RecordingEngine Engine, EntityHandle Grenade, Vector Velocity, float Timer);

    /// <summary>
    /// A player looking along +x, moving at <see cref="Running"/>, throwing
    /// one grenade with <paramref name="buttons"/>. Every sequence is zero
    /// long, so each event falls on the next command.
    /// </summary>
    private static Throw Thrown(int buttons)
    {
        float now = 10f;
        Vector swept = default;   // the hull trace meets nothing: it ends where it was asked to
        var engine = TestServices.Mock().Returns("Entities.IsValid", true).Returns("Entities.GetIndex", 1)
            .Returns("Entities.GetClassName", EntityNames.SsPlayer)
            .Returns("Entities.GetOrigin", new Vector(0, 0, 0))
            .Returns("Entities.GetEyeAngles", new Vector(0, 0, 0))
            .Returns("Entities.GetAngles", new Vector(0, 0, 0))
            .Returns("Entities.GetVelocity", Running)
            .Returns("Entities.GetWorldSpaceCenter", new Vector(0, 0, 36))
            .Returns("Players.GetShootOrigin", Eye)
            .Returns("Players.IsAlive", true)
            .Returns("WorldQuery.TraceLine", new TraceResult(true, 0.25f, new Vector(0, 0, 0), new Vector(0, 0, 1), EntityHandle.None))
            .WhenCalled("WorldQuery.EntityTraceHull", call => swept = (Vector)call.Args[2]!)
            .ReturnsEachCall("WorldQuery.EntityTraceHull", () => new EntityHullTrace(swept, false, false))
            .ReturnsEachCall("Clock.Now", () => now)
            .Returns("EntityRender.SequenceDuration", 0f)
            .Ignores("EntityRender.LookupSequenceByActivity");

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = TestServices.Services(out var spawned);
        spawned.Strict = false;   // what the thrown grenade asks of its own engine is FragGrenadeTests'.
        registry.KeyValue(Player, EntityNames.SsPlayer, "targetname", "fixture_player");

        var arsenal = new Arsenal(new RecordingLog(), new WeaponSwitchTests.SpawningEntities(engine, registry),
                                  engine, engine, engine, engine, engine, engine, engine, registry, engine, new DamageHitGroups());
        var launches = new List<(EntityHandle Flying, Vector Velocity, float Fuse)>();
        arsenal.Launched = (flying, _, velocity, fuse) => launches.Add((flying, velocity, fuse));

        Assert.True(arsenal.GiveWeapon(Player, EntityNames.WeaponFrag), "the frag can be given");
        arsenal.GiveAmmo(Player, 3, "Grenade");

        int command = 0;
        void Command(int held)
        {
            now += 0.015f;
            var cmd = new UserCmd { CommandNumber = ++command, Buttons = held };
            arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);
        }

        now = 11f;   // past the deploy
        for (int i = 0; i < 4; i++)
            Command(buttons);
        for (int i = 0; i < 4; i++)
            Command(buttons & (int)InputButtons.Duck);

        var launch = Assert.Single(launches);
        return new Throw(engine, launch.Flying, launch.Velocity, launch.Fuse);
    }

    private static Vector Arg(RecordingEngine engine, string member, EntityHandle of)
        => (Vector)engine.Calls.Last(c => c.Member == member && (EntityHandle)c.Args[0]! == of).Args[1]!;

    [Fact]
    public void AnOverhandThrowStartsAheadAndRightOfTheEye()
    {
        var thrown = Thrown((int)InputButtons.Attack);

        Assert.Equal(Eye + new Vector(18, -8, 0), Arg(thrown.Engine, "Entities.SetOrigin", thrown.Grenade));
    }

    [Fact]
    public void AnOverhandThrowLeavesAtTwelveHundredAndALittleUpPlusTheThrowers()
    {
        var thrown = Thrown((int)InputButtons.Attack);

        Assert.Equal(new Vector(1210, 0, 120), thrown.Velocity);
        Assert.Equal(new Vector(1210, 0, 120), Arg(thrown.Engine, "Entities.ApplyVelocityImpulse", thrown.Grenade));
    }

    [Fact]
    public void AnOverhandThrowTumblesEndOverEnd()
    {
        var thrown = Thrown((int)InputButtons.Attack);
        var spin = Arg(thrown.Engine, "Entities.SetAngularVelocity", thrown.Grenade);

        Assert.Equal(600f, spin.X);
        Assert.InRange(spin.Y, -1200f, 1200f);
        Assert.Equal(0f, spin.Z);
    }

    [Fact]
    public void ALobLeavesSlowAndLow()
    {
        var thrown = Thrown((int)InputButtons.Attack2);

        Assert.Equal(Eye + new Vector(18, -8, -8), Arg(thrown.Engine, "Entities.SetOrigin", thrown.Grenade));
        Assert.Equal(new Vector(360, 0, 50), thrown.Velocity);
    }

    [Fact]
    public void ARollStartsAtTheFeetOnItsSideAndRollsAlongTheFloor()
    {
        var thrown = Thrown((int)(InputButtons.Attack2 | InputButtons.Duck));

        Assert.Equal(new Vector(18, 0, 4), Arg(thrown.Engine, "Entities.SetOrigin", thrown.Grenade));
        Assert.Equal(new Vector(0, 0, -90), Arg(thrown.Engine, "Entities.SetAngles", thrown.Grenade));
        Assert.Equal(new Vector(710, 0, 0), thrown.Velocity);
    }

    [Fact]
    public void TheGrenadeIsOwnedByItsThrowerAndTimedForTwoAndAHalfSeconds()
    {
        var thrown = Thrown((int)InputButtons.Attack);

        var owner = thrown.Engine.Calls.Last(c => c.Member == "Entities.SetOwner" && (EntityHandle)c.Args[0]! == thrown.Grenade);
        Assert.Equal(Player, (EntityHandle)owner.Args[1]!);
        Assert.Equal(2.5f, thrown.Timer);
    }
}
