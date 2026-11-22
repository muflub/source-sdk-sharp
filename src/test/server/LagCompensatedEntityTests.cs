using System;
using System.Collections.Generic;
using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// One shooter (slot 1, at the origin, looking down +x) and one entity that is
/// not a player (a robot), opted in, running across his view along +y at 300
/// units a second: at tick N it stands at (400, -150 + 4.5 (N - 100), 0).
/// Recorded each tick from 100 to 120; the server is at 120.
/// </summary>
public sealed class EntityLagRig
{
    public const int Shooter = 1;
    public const float Speed = 300f;

    public LagWorld World { get; } = new();
    public LagSettings Settings { get; } = new();
    public PlayerCurrentCommands Commands { get; } = new();
    public RecordingLog Log { get; } = new();
    public LagEntities Entities { get; } = new();
    public LagPlayer Robot { get; }
    public LagEntities.Row Row { get; }
    public LagCompensation Lag { get; }

    public EntityLagRig(IHooks<ILagCompensationTargets>? targets = null, bool optedIn = true, bool record = true)
    {
        World.Add(Shooter);
        Robot = World.AddEntity(5);
        Row = Entities.Add(Robot.Handle);
        Row.OptedIn = optedIn;
        Row.MaxSpeed = Speed;
        Lag = new LagCompensation(World, Settings, Commands, Log, targets, Entities);

        if (record)
        {
            for (int tick = 100; tick <= 120; tick++)
                Frame(tick);
        }
    }

    public LagPlayer ShooterPlayer => World.Players[Shooter];

    public static Vector RobotAt(int tick) => new(400f, -150f + Speed * LagWorld.Tick * (tick - 100), 0f);

    /// <summary>The robot at tick <paramref name="tick"/>'s place, and the frame's record.</summary>
    public void Frame(int tick)
    {
        World.At(tick);
        Robot.Origin = RobotAt(tick);
        Lag.RecordFrame();
    }

    /// <summary>
    /// The shooter's attacking command, from a client 100 ms behind with two
    /// ticks (0.03 s) of interpolation: sent at tick 113 of the server's 120, it
    /// drew tick 111.
    /// </summary>
    public UserCmd LaggedCommand()
    {
        ShooterPlayer.Latency = 0.1f;
        ShooterPlayer.LerpTime = 0.03f;
        return new UserCmd { TickCount = World.TickCount - 7, CommandNumber = 50, Buttons = (int)InputButtons.Attack };
    }

    /// <summary>The tick the lagged client drew.</summary>
    public int DrawnTick => World.TickCount - 9;

    /// <summary>
    /// A hitscan shot from the shooter's eye through the middle of the robot
    /// as the client drew it: whether it passes through the robot's box where
    /// the robot stands in the world now.
    /// </summary>
    public bool ShotAtWhatTheClientSawHits()
    {
        Vector eye = new(0f, 0f, 36f);
        Vector drawn = RobotAt(DrawnTick) + new Vector(0f, 0f, 36f);
        Vector end = eye + (drawn - eye) * 2f;
        return RayHitsBox(eye, end, Robot.Origin + Robot.Mins, Robot.Origin + Robot.Maxs);
    }

    /// <summary>A line against an axis-aligned box: the slab test.</summary>
    private static bool RayHitsBox(Vector from, Vector to, Vector min, Vector max)
    {
        float enter = 0f;
        float leave = 1f;
        Span<float> start = [from.X, from.Y, from.Z];
        Span<float> delta = [to.X - from.X, to.Y - from.Y, to.Z - from.Z];
        Span<float> lo = [min.X, min.Y, min.Z];
        Span<float> hi = [max.X, max.Y, max.Z];

        for (int axis = 0; axis < 3; axis++)
        {
            if (MathF.Abs(delta[axis]) < 1e-6f)
            {
                if (start[axis] < lo[axis] || start[axis] > hi[axis])
                    return false;

                continue;
            }

            float t0 = (lo[axis] - start[axis]) / delta[axis];
            float t1 = (hi[axis] - start[axis]) / delta[axis];
            enter = MathF.Max(enter, MathF.Min(t0, t1));
            leave = MathF.Min(leave, MathF.Max(t0, t1));
        }

        return enter <= leave;
    }
}

/// <summary>A target hook answering for opted-in entities only.</summary>
public sealed class OptedInTargets(Decision answer) : ILagCompensationTargets
{
    public List<LagCompensationTarget> Asked { get; } = [];

    public Decision WantsLagCompensationOnEntity(in LagCompensationTarget target)
    {
        Asked.Add(target);
        return target.OptedIn ? answer : Decision.UseStockRule;
    }
}

/// <summary>
/// The lag compensation of entities that are not players and opted in
/// (<see cref="ScriptedEntity.LagCompensated"/>): recorded, moved back for a
/// lagged shooter's command and put back as a player is, and a pooled body's
/// history started afresh when it becomes another character.
/// </summary>
public class LagCompensatedEntityTests
{
    private static EntityHandle Shooter(EntityLagRig rig) => rig.ShooterPlayer.Handle;

    //-------------------------------------------------------------------------
    // The gate: a shot where the lagged client saw the entity
    //-------------------------------------------------------------------------

    [Fact]
    public void ALaggedShotAtWhereTheClientSawAMovingEntityHitsWithCompensation()
    {
        var rig = new EntityLagRig();
        UserCmd cmd = rig.LaggedCommand();

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.True(rig.ShotAtWhatTheClientSawHits(), $"robot at {rig.Robot.Origin}");
    }

    [Fact]
    public void TheSameShotMissesAnEntityThatDidNotOptIn()
    {
        var rig = new EntityLagRig(optedIn: false);
        UserCmd cmd = rig.LaggedCommand();

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.False(rig.ShotAtWhatTheClientSawHits(), $"robot at {rig.Robot.Origin}");
        Assert.Equal(EntityLagRig.RobotAt(120), rig.Robot.Origin);
    }

    [Fact]
    public void TheRewindPutsTheEntityWhereItWasAtTheDrawnTick()
    {
        var rig = new EntityLagRig();
        UserCmd cmd = rig.LaggedCommand();

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Equal(EntityLagRig.RobotAt(rig.DrawnTick), rig.Robot.Origin);
        Assert.Equal([(rig.Robot.Handle, EntityLagRig.RobotAt(rig.DrawnTick), default(QAngle))],
                     rig.World.Relinks.Where(r => r.Player == rig.Robot.Handle));
    }

    [Fact]
    public void ATimeBetweenTwoEntityRecordsIsInterpolated()
    {
        // Records only every other tick, and the drawn tick (111) between two.
        var rig = new EntityLagRig(record: false);
        for (int tick = 100; tick <= 120; tick += 2)
            rig.Frame(tick);
        UserCmd cmd = rig.LaggedCommand();

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Equal(EntityLagRig.RobotAt(111).Y, rig.Robot.Origin.Y, 3);
    }

    [Fact]
    public void FinishPutsTheEntityBackAtItsPresentPlace()
    {
        var rig = new EntityLagRig();
        UserCmd cmd = rig.LaggedCommand();
        rig.Lag.Start(Shooter(rig), in cmd);
        Assert.NotEqual(EntityLagRig.RobotAt(120), rig.Robot.Origin);

        rig.Lag.Finish(Shooter(rig));

        Assert.Equal(EntityLagRig.RobotAt(120), rig.Robot.Origin);
        Assert.False(rig.Lag.IsCurrentlyDoingLagCompensation);
    }

    [Fact]
    public void FinishLeavesAnEntitysSimulationTimeAlone()
    {
        var rig = new EntityLagRig();
        rig.Robot.SimulationTime = -7f;
        UserCmd cmd = rig.LaggedCommand();
        rig.Lag.Start(Shooter(rig), in cmd);

        rig.Lag.Finish(Shooter(rig));

        Assert.Equal(-7f, rig.Robot.SimulationTime);
    }

    //-------------------------------------------------------------------------
    // The targets hook, asked the same way
    //-------------------------------------------------------------------------

    [Fact]
    public void ARefusedEntityIsNotMoved()
    {
        var targets = new OptedInTargets(Decision.No);
        var rig = new EntityLagRig(new LagHooks<ILagCompensationTargets>(targets));
        UserCmd cmd = rig.LaggedCommand();

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Single(targets.Asked);
        Assert.Equal(EntityLagRig.RobotAt(120), rig.Robot.Origin);
        Assert.Equal(0, rig.World.MovesOf(rig.Robot.Handle));
    }

    [Fact]
    public void TheHookIsAskedAboutTheEntityAsOptedInWithItsDeclaredSpeed()
    {
        var targets = new OptedInTargets(Decision.Yes);
        var rig = new EntityLagRig(new LagHooks<ILagCompensationTargets>(targets));
        UserCmd cmd = rig.LaggedCommand();

        rig.Lag.Start(Shooter(rig), in cmd);

        LagCompensationTarget asked = Assert.Single(targets.Asked);
        Assert.True(asked.OptedIn);
        Assert.Equal(rig.Robot.Handle, asked.Target);
        Assert.Equal(EntityLagRig.Speed, asked.TargetMaxSpeed);
        Assert.Equal(EntityLagRig.RobotAt(120), asked.TargetOrigin);
    }

    [Fact]
    public void APlayerTargetIsNotAskedAsOptedIn()
    {
        var targets = new OptedInTargets(Decision.UseStockRule);
        var rig = new EntityLagRig(new LagHooks<ILagCompensationTargets>(targets));
        rig.Row.OptedIn = false;
        rig.World.Add(2).Origin = new Vector(300f, 0f, 0f);
        rig.Frame(121);
        UserCmd cmd = rig.LaggedCommand();

        rig.Lag.Start(Shooter(rig), in cmd);

        LagCompensationTarget asked = Assert.Single(targets.Asked);
        Assert.False(asked.OptedIn);
    }

    [Fact]
    public void AnEntityOutsideTheConeAndBeyondItsReachIsNotMoved()
    {
        var rig = new EntityLagRig();
        rig.Row.MaxSpeed = 0f;
        UserCmd cmd = rig.LaggedCommand();
        cmd.Yaw = 180f;   // looking away, down -x

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Equal(EntityLagRig.RobotAt(120), rig.Robot.Origin);
    }

    //-------------------------------------------------------------------------
    // The history
    //-------------------------------------------------------------------------

    [Fact]
    public void AnOptedInEntityIsRecordedEachTickAtTheFramesTime()
    {
        var rig = new EntityLagRig();

        IReadOnlyList<LagRecord> track = rig.Lag.EntityHistory(rig.Robot.Handle);

        Assert.Equal(21, track.Count);
        Assert.Equal(Enumerable.Range(100, 21).Select(t => t * LagWorld.Tick), track.Select(r => r.SimulationTime));
        Assert.Equal(Enumerable.Range(100, 21).Select(EntityLagRig.RobotAt), track.Select(r => r.Origin));
        Assert.All(track, r => Assert.Equal(LagCompensation.Alive, r.Flags));
    }

    [Fact]
    public void AnEntityThatDidNotOptInHasNoHistory()
    {
        var rig = new EntityLagRig(optedIn: false);

        Assert.Empty(rig.Lag.EntityHistory(rig.Robot.Handle));
    }

    [Fact]
    public void EntityRecordsOlderThanTheWholeSecondBeforeMaxUnlagGo()
    {
        var rig = new EntityLagRig(record: false);
        rig.Settings.MaxUnlag = 0.5f;

        // Ticks 100-200 are 1.5-3.0 s; at 3.0 s the dead time is (int)(3.0 - 0.5) = 2.
        for (int tick = 100; tick <= 200; tick++)
            rig.Frame(tick);

        IReadOnlyList<LagRecord> track = rig.Lag.EntityHistory(rig.Robot.Handle);
        Assert.True(track[0].SimulationTime >= 2f && track[0].SimulationTime < 2f + LagWorld.Tick,
                    $"oldest kept {track[0].SimulationTime}");
        Assert.Equal(200 * LagWorld.Tick, track[^1].SimulationTime);
    }

    [Fact]
    public void UnlagOffClearsTheEntityHistory()
    {
        var rig = new EntityLagRig();
        rig.Settings.Unlag = false;

        rig.Lag.RecordFrame();

        Assert.Empty(rig.Lag.EntityHistory(rig.Robot.Handle));
    }

    //-------------------------------------------------------------------------
    // A pooled body is many characters
    //-------------------------------------------------------------------------

    [Fact]
    public void AnEntityThatOptsOutLosesItsHistory()
    {
        var rig = new EntityLagRig();
        rig.Row.OptedIn = false;   // parked

        rig.Frame(121);

        Assert.Empty(rig.Lag.EntityHistory(rig.Robot.Handle));
    }

    [Fact]
    public void AnEntityThatOptsInAgainStartsAFreshHistory()
    {
        var rig = new EntityLagRig();
        rig.Row.OptedIn = false;
        rig.Frame(121);
        rig.Row.OptedIn = true;

        rig.Frame(122);

        IReadOnlyList<LagRecord> track = rig.Lag.EntityHistory(rig.Robot.Handle);
        Assert.Equal([122 * LagWorld.Tick], track.Select(r => r.SimulationTime));
    }

    [Fact]
    public void AnEntityTeleportedFurtherThanTheTeleportDistanceIsNotRewoundPastTheJump()
    {
        var rig = new EntityLagRig(record: false);
        for (int tick = 100; tick <= 115; tick++)
            rig.Frame(tick);

        // Put down 500 units away and recorded there since, with no restart.
        for (int tick = 116; tick <= 120; tick++)
        {
            rig.World.At(tick);
            rig.Robot.Origin = EntityLagRig.RobotAt(tick) + new Vector(0f, 500f, 0f);
            rig.Lag.RecordFrame();
        }

        UserCmd cmd = rig.LaggedCommand();   // the client drew tick 111: before the jump
        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Equal(EntityLagRig.RobotAt(120) + new Vector(0f, 500f, 0f), rig.Robot.Origin);
    }

    [Fact]
    public void ARestartedHistoryIsNotRewoundIntoTheEarlierPastByAShortHop()
    {
        var rig = new EntityLagRig(record: false);
        for (int tick = 100; tick <= 115; tick++)
            rig.Frame(tick);

        // Lent to another character 20 units away (under the 64-unit teleport
        // distance, which cannot see it), the history restarted, and still there.
        rig.Row.Serial++;
        Vector there = EntityLagRig.RobotAt(115) + new Vector(20f, 0f, 0f);
        for (int tick = 116; tick <= 120; tick++)
        {
            rig.World.At(tick);
            rig.Robot.Origin = there;
            rig.Lag.RecordFrame();
        }

        UserCmd cmd = rig.LaggedCommand();   // the client drew tick 111: the earlier character's
        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Equal(there, rig.Robot.Origin);
        Assert.Equal(5, rig.Lag.EntityHistory(rig.Robot.Handle).Count);
    }

    [Fact]
    public void AHistoryRestartedSinceTheFramesRecordIsNotUsed()
    {
        var rig = new EntityLagRig();

        // Parked and lent again inside this tick: the records are still the
        // earlier character's, and the shot comes before the next frame.
        rig.Row.Serial++;
        UserCmd cmd = rig.LaggedCommand();
        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Equal(EntityLagRig.RobotAt(120), rig.Robot.Origin);
        Assert.Equal(0, rig.World.MovesOf(rig.Robot.Handle));
    }

    //-------------------------------------------------------------------------
    // Cost
    //-------------------------------------------------------------------------

    [Fact]
    public void RecordingFortyNineEntitiesATickAllocatesNothingInSteadyState()
    {
        var world = new LagWorld();
        world.Add(1);
        world.Add(2);
        var entities = new LagEntities();
        var bodies = new List<LagPlayer>();
        for (int e = 1; e <= 49; e++)
        {
            LagPlayer body = world.AddEntity(e);
            entities.Add(body.Handle);
            bodies.Add(body);
        }

        var lag = new LagCompensation(world, new LagSettings(), new PlayerCurrentCommands(), new RecordingLog(), null, entities);
        int tick = 100;

        void Ticks(int count)
        {
            for (int i = 0; i < count; i++, tick++)
            {
                world.At(tick);
                for (int b = 0; b < bodies.Count; b++)
                    bodies[b].Origin = new Vector(b * 50f, tick * 4.5f % 400f, 0f);
                lag.RecordFrame();
            }
        }

        // Three seconds fills every track to stock's length and the spare pool.
        Ticks(200);

        long quietest = long.MaxValue;
        for (int window = 0; window < 3; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Ticks(100);
            quietest = Math.Min(quietest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, quietest);
        Assert.NotEmpty(lag.EntityHistory(bodies[48].Handle));
    }
}

/// <summary>
/// <see cref="ScriptedLagCompensatedEntities"/>: the opt-in read from each live
/// <see cref="ScriptedEntity"/>.
/// </summary>
public class ScriptedLagCompensatedEntitiesTests
{
    private sealed class Plain : ScriptedEntity
    {
    }

    private sealed class Robot : ScriptedEntity
    {
        public bool In = true;

        public override bool LagCompensated => In;

        public override float LagCompensationMaxSpeed => 250f;

        public void Relent() => RestartLagHistory();
    }

    private static (EntityDirectory Directory, Robot Robot, Plain Plain) Directory()
    {
        var directory = new EntityDirectory(new RecordingLog());
        var robot = new Robot { Handle = new EntityHandle(0x0001_0010u) };
        var plain = new Plain { Handle = new EntityHandle(0x0001_0011u) };
        directory.Bind(robot.Handle, robot);
        directory.Bind(plain.Handle, plain);
        return (directory, robot, plain);
    }

    [Fact]
    public void OnlyTheEntitiesThatOptInAreCollected()
    {
        (EntityDirectory directory, Robot robot, _) = Directory();
        var into = new List<LagCompensatedEntity>();

        new ScriptedLagCompensatedEntities(directory).Collect(into);

        Assert.Equal([new LagCompensatedEntity(robot.Handle, 0, 250f)], into);
    }

    [Fact]
    public void AnEntityAnsweringNoIsNotCollected()
    {
        (EntityDirectory directory, Robot robot, _) = Directory();
        robot.In = false;
        var into = new List<LagCompensatedEntity>();

        new ScriptedLagCompensatedEntities(directory).Collect(into);

        Assert.Empty(into);
    }

    [Fact]
    public void RestartLagHistoryMovesTheSerialOn()
    {
        (EntityDirectory directory, Robot robot, _) = Directory();
        var entities = new ScriptedLagCompensatedEntities(directory);
        Assert.True(entities.TryGet(robot.Handle, out LagCompensatedEntity before));

        robot.Relent();

        Assert.True(entities.TryGet(robot.Handle, out LagCompensatedEntity after));
        Assert.Equal(before.HistorySerial + 1, after.HistorySerial);
    }

    [Fact]
    public void AnEntityThatDidNotOptInIsNotFound()
    {
        (EntityDirectory directory, _, Plain plain) = Directory();

        Assert.False(new ScriptedLagCompensatedEntities(directory).TryGet(plain.Handle, out _));
    }
}
