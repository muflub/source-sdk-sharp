using System;
using System.Collections.Generic;
using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// One shooter (slot 1, at the origin, looking down +x) and one target (slot
/// 2) who runs along +x at ten units a tick from x=100, recorded each tick
/// from 100 to 110: at tick N the target stood at x = 100 + 10 * (N - 100).
/// </summary>
public sealed class LagRig
{
    public const int Shooter = 1;
    public const int Target = 2;

    public LagWorld World { get; } = new();
    public LagSettings Settings { get; } = new();
    public PlayerCurrentCommands Commands { get; } = new();
    public RecordingLog Log { get; } = new();
    public LagCompensation Lag { get; }

    public LagRig(IHooks<ILagCompensationTargets>? targets = null, bool record = true)
    {
        World.Add(Shooter);
        LagPlayer target = World.Add(Target);
        target.Origin = new Vector(100f, 0f, 0f);
        Lag = new LagCompensation(World, Settings, Commands, Log, targets);

        if (record)
        {
            for (int tick = 100; tick <= 110; tick++)
                Frame(tick);
        }
    }

    public LagPlayer TargetPlayer => World.Players[Target];

    public LagPlayer ShooterPlayer => World.Players[Shooter];

    /// <summary>The target at tick <paramref name="tick"/>'s place, and the frame's record.</summary>
    public void Frame(int tick)
    {
        World.At(tick);
        TargetPlayer.Origin = new Vector(100f + 10f * (tick - 100), 0f, 0f);
        TargetPlayer.Cycle = (tick - 100) * 0.05f;
        Lag.RecordFrame();
    }

    /// <summary>
    /// A command sent at <paramref name="tick"/>, attacking along +x, with a
    /// latency that agrees with it (the server is at 110).
    /// </summary>
    public UserCmd Command(int tick, bool attack = true, int number = 50)
    {
        ShooterPlayer.Latency = (World.TickCount - tick) * LagWorld.Tick;
        return new UserCmd { TickCount = tick, CommandNumber = number, Buttons = attack ? (int)InputButtons.Attack : 0 };
    }

    public Vector TargetAt(int tick) => new(100f + 10f * (tick - 100), 0f, 0f);

    /// <summary>The shooter fires in command <paramref name="number"/> (NoteWeaponFired).</summary>
    public void Fired(int number)
    {
        var cmd = new UserCmd { CommandNumber = number, Buttons = (int)InputButtons.Attack };
        using (Commands.Begin(ShooterPlayer.Handle, in cmd))
            Lag.NoteWeaponFired(ShooterPlayer.Handle);
    }
}

/// <summary>
/// <c>CLagCompensationManager</c> (game/server/player_lagcompensation.cpp):
/// the history, the target time, the rewind, the restore and the gates.
/// </summary>
public class LagCompensationTests
{
    private static EntityHandle Shooter(LagRig rig) => rig.ShooterPlayer.Handle;

    //-------------------------------------------------------------------------
    // The history (FrameUpdatePostEntityThink)
    //-------------------------------------------------------------------------

    [Fact]
    public void TheHistoryRecordsEachTick()
    {
        var rig = new LagRig();

        IReadOnlyList<LagRecord> track = rig.Lag.History(LagRig.Target);

        Assert.Equal(11, track.Count);
        Assert.Equal(Enumerable.Range(100, 11).Select(t => t * LagWorld.Tick), track.Select(r => r.SimulationTime));
        Assert.Equal(Enumerable.Range(100, 11).Select(rig.TargetAt), track.Select(r => r.Origin));
        Assert.All(track, r => Assert.Equal(LagCompensation.Alive, r.Flags));
    }

    [Fact]
    public void AFrameWithNoNewSimulationTimeAddsNoRecord()
    {
        var rig = new LagRig();

        // The target ran no command this frame: its simulation time stands.
        rig.World.TickCount = 111;
        rig.World.CurTime = 111 * LagWorld.Tick;
        rig.Lag.RecordFrame();

        Assert.Equal(11, rig.Lag.History(LagRig.Target).Count);
    }

    [Fact]
    public void RecordsOlderThanTheWholeSecondBeforeMaxUnlagGo()
    {
        var rig = new LagRig(record: false);
        rig.Settings.MaxUnlag = 0.5f;

        // Ticks 100-200 are 1.5-3.0 s. At 3.0 s the dead time is (int)(3.0 - 0.5) = 2:
        // stock's int truncation keeps every record from 2.0 s, not from 2.5 s.
        for (int tick = 100; tick <= 200; tick++)
            rig.Frame(tick);

        IReadOnlyList<LagRecord> track = rig.Lag.History(LagRig.Target);
        Assert.True(track[0].SimulationTime >= 2f && track[0].SimulationTime < 2f + LagWorld.Tick,
                    $"oldest kept {track[0].SimulationTime}");
        Assert.Equal(200 * LagWorld.Tick, track[^1].SimulationTime);
    }

    [Fact]
    public void ALeftPlayersHistoryGoes()
    {
        var rig = new LagRig();
        rig.World.Players.Remove(LagRig.Target);

        rig.Frame2(111);

        Assert.Empty(rig.Lag.History(LagRig.Target));
    }

    [Fact]
    public void UnlagOffClearsTheHistory()
    {
        var rig = new LagRig();
        rig.Settings.Unlag = false;

        rig.Lag.RecordFrame();

        Assert.Empty(rig.Lag.History(LagRig.Target));
    }

    //-------------------------------------------------------------------------
    // The rewind (StartLagCompensation, BacktrackPlayer)
    //-------------------------------------------------------------------------

    [Fact]
    public void TheRewindPutsAMovedTargetWhereItWasAtTheShootersCommandTime()
    {
        var rig = new LagRig();
        UserCmd cmd = rig.Command(105);

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.True(rig.Lag.IsCurrentlyDoingLagCompensation);
        Assert.Equal(rig.TargetAt(105), rig.TargetPlayer.Origin);
        Assert.Equal([(rig.TargetPlayer.Handle, rig.TargetAt(105), default(QAngle))], rig.World.Relinks);
        Assert.Empty(rig.World.TouchingMoves);
    }

    [Fact]
    public void TheInterpolationTicksComeOffTheCommandsTick()
    {
        var rig = new LagRig();
        UserCmd cmd = rig.Command(107);
        // 0.045 s of interpolation is three ticks: the client drew tick 104.
        rig.ShooterPlayer.LerpTime = 0.045f;
        rig.ShooterPlayer.Latency = 3 * LagWorld.Tick;

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Equal(rig.TargetAt(104), rig.TargetPlayer.Origin);
    }

    [Fact]
    public void ATimeBetweenTwoRecordsIsInterpolated()
    {
        // Records only every other tick: 100, 102, ..., 110.
        var rig = new LagRig(record: false);
        for (int tick = 100; tick <= 110; tick += 2)
            rig.Frame(tick);
        UserCmd cmd = rig.Command(105);

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Equal(rig.TargetAt(105).X, rig.TargetPlayer.Origin.X, 3);
    }

    [Fact]
    public void ACommandThatDisagreesWithTheLatencyIsTakenAtTheLatencysTime()
    {
        var rig = new LagRig();
        UserCmd cmd = rig.Command(101);
        // The command claims tick 101 (0.135 s back); the channel says 0.045 s:
        // 0.09 s apart is believed...
        rig.ShooterPlayer.Latency = 3 * LagWorld.Tick;

        rig.Lag.Start(Shooter(rig), in cmd);
        Vector believed = rig.TargetPlayer.Origin;
        rig.Lag.Finish(Shooter(rig));

        // ...but a command 0.3 s off what the channel says is not: tick 107.
        var far = new LagRig();
        UserCmd lying = far.Command(90);
        far.ShooterPlayer.Latency = 3 * LagWorld.Tick;
        far.Lag.Start(Shooter(far), in lying);

        Assert.Equal(rig.TargetAt(101), believed);
        Assert.Equal(far.TargetAt(107), far.TargetPlayer.Origin);
    }

    [Fact]
    public void TheLatencysCorrectionIsClampedToMaxUnlag()
    {
        var rig = new LagRig();
        rig.Settings.MaxUnlag = 0.06f;
        UserCmd lying = rig.Command(90);
        // Half a second of latency, but no more than 0.06 s (four ticks) is undone.
        rig.ShooterPlayer.Latency = 0.5f;

        rig.Lag.Start(Shooter(rig), in lying);

        Assert.Equal(rig.TargetAt(106), rig.TargetPlayer.Origin);
    }

    [Fact]
    public void AnUnmovedTargetIsNotRelinked()
    {
        var rig = new LagRig(record: false);
        for (int tick = 100; tick <= 110; tick++)
        {
            rig.World.At(tick);
            rig.Lag.RecordFrame();   // the target stands at 100,0,0 throughout
        }
        UserCmd cmd = rig.Command(105);

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Empty(rig.World.Relinks);
    }

    [Fact]
    public void ADeathInTheHistoryLosesTheTrack()
    {
        var rig = new LagRig(record: false);
        for (int tick = 100; tick <= 110; tick++)
        {
            rig.TargetPlayer.Alive = tick > 106;
            rig.Frame(tick);
        }
        UserCmd cmd = rig.Command(105);

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Equal(rig.TargetAt(110), rig.TargetPlayer.Origin);
        Assert.Empty(rig.World.Relinks);
    }

    [Fact]
    public void ATeleportInTheHistoryLosesTheTrack()
    {
        var rig = new LagRig(record: false);
        for (int tick = 100; tick <= 110; tick++)
        {
            rig.Frame(tick);
            if (tick == 107)
            {
                // A jump far beyond the 64-unit teleport distance in one tick.
                rig.TargetPlayer.Origin = new Vector(1000f, 0f, 0f);
                rig.Lag.History(LagRig.Target)[^1].Origin = rig.TargetPlayer.Origin;
            }
        }
        UserCmd cmd = rig.Command(105);

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Empty(rig.World.Relinks);
    }

    [Fact]
    public void TheRewindTakesTheRecordsSizeAndAngles()
    {
        var rig = new LagRig(record: false);
        for (int tick = 100; tick <= 110; tick++)
        {
            bool ducked = tick <= 105;
            rig.TargetPlayer.Maxs = new Vector(16f, 16f, ducked ? 36f : 72f);
            rig.TargetPlayer.Angles = new QAngle(0f, ducked ? 90f : 0f, 0f);
            rig.Frame(tick);
        }
        UserCmd cmd = rig.Command(105);

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Equal(new Vector(16f, 16f, 36f), rig.TargetPlayer.Maxs);
        Assert.Equal(90f, rig.TargetPlayer.Angles.Yaw, 3);
    }

    [Fact]
    public void TheRewindPosesTheRecordsSequenceAndCycleAndFlushesTheBones()
    {
        var rig = new LagRig();
        UserCmd cmd = rig.Command(104);

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Equal(0.2f, rig.TargetPlayer.HeldCycle!.Value, 4);
        Assert.Equal([rig.TargetPlayer.Handle], rig.World.Flushes);
    }

    [Fact]
    public void TheBoneCacheStaysWithFlushingOff()
    {
        var rig = new LagRig();
        rig.Settings.FlushBoneCache = false;
        UserCmd cmd = rig.Command(104);

        rig.Lag.Start(Shooter(rig), in cmd);
        rig.Lag.Finish(Shooter(rig));

        Assert.Empty(rig.World.Flushes);
    }

    //-------------------------------------------------------------------------
    // The restore (FinishLagCompensation)
    //-------------------------------------------------------------------------

    [Fact]
    public void TheRestorePutsItBack()
    {
        var rig = new LagRig();
        UserCmd cmd = rig.Command(105);
        rig.Lag.Start(Shooter(rig), in cmd);

        rig.Lag.Finish(Shooter(rig));

        Assert.False(rig.Lag.IsCurrentlyDoingLagCompensation);
        Assert.Equal(rig.TargetAt(110), rig.TargetPlayer.Origin);
        Assert.Equal([(rig.TargetPlayer.Handle, rig.TargetAt(110))], rig.World.TouchingMoves);
        Assert.Null(rig.TargetPlayer.HeldCycle);
        Assert.Equal(110 * LagWorld.Tick, rig.TargetPlayer.SimulationTime);
    }

    [Fact]
    public void ATargetTheAttackPushedIsRestoredWithThePush()
    {
        var rig = new LagRig();
        UserCmd cmd = rig.Command(105);
        rig.Lag.Start(Shooter(rig), in cmd);

        // The attack's knockback moved the rewound target five units up.
        rig.TargetPlayer.Origin += new Vector(0f, 0f, 5f);
        rig.Lag.Finish(Shooter(rig));

        Assert.Equal(rig.TargetAt(110) + new Vector(0f, 0f, 5f), rig.TargetPlayer.Origin);
    }

    [Fact]
    public void ATargetTheAttackMovedFarStaysWhereTheAttackPutIt()
    {
        var rig = new LagRig();
        UserCmd cmd = rig.Command(105);
        rig.Lag.Start(Shooter(rig), in cmd);

        var thrown = new Vector(500f, 500f, 0f);
        rig.TargetPlayer.Origin = thrown;
        rig.Lag.Finish(Shooter(rig));

        Assert.Equal(thrown, rig.TargetPlayer.Origin);
        Assert.Empty(rig.World.TouchingMoves);
    }

    [Fact]
    public void AnglesTheAttackChangedAreLeft()
    {
        var rig = new LagRig(record: false);
        for (int tick = 100; tick <= 110; tick++)
        {
            rig.TargetPlayer.Angles = new QAngle(0f, tick <= 105 ? 90f : 0f, 0f);
            rig.Frame(tick);
        }
        UserCmd cmd = rig.Command(105);
        rig.Lag.Start(Shooter(rig), in cmd);

        rig.TargetPlayer.Angles = new QAngle(0f, 45f, 0f);
        rig.Lag.Finish(Shooter(rig));

        Assert.Equal(45f, rig.TargetPlayer.Angles.Yaw);
    }

    [Fact]
    public void AnglesAndSizeTheAttackLeftAreRestored()
    {
        var rig = new LagRig(record: false);
        for (int tick = 100; tick <= 110; tick++)
        {
            bool ducked = tick <= 105;
            rig.TargetPlayer.Maxs = new Vector(16f, 16f, ducked ? 36f : 72f);
            rig.TargetPlayer.Angles = new QAngle(0f, ducked ? 90f : 0f, 0f);
            rig.Frame(tick);
        }
        UserCmd cmd = rig.Command(105);
        rig.Lag.Start(Shooter(rig), in cmd);

        rig.Lag.Finish(Shooter(rig));

        Assert.Equal((new Vector(16f, 16f, 72f), 0f), (rig.TargetPlayer.Maxs, rig.TargetPlayer.Angles.Yaw));
    }

    [Fact]
    public void TheRestoresSweepIsMadeWithTheRestoredBox()
    {
        var rig = new LagRig(record: false);
        for (int tick = 100; tick <= 110; tick++)
        {
            rig.TargetPlayer.Maxs = new Vector(16f, 16f, tick <= 105 ? 36f : 72f);
            rig.Frame(tick);
        }
        UserCmd cmd = rig.Command(105);
        rig.Lag.Start(Shooter(rig), in cmd);
        Assert.Equal(36f, rig.TargetPlayer.Maxs.Z);

        rig.Lag.Finish(Shooter(rig));

        Assert.Equal([(new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f))], rig.World.SweptBoxes);
    }

    [Fact]
    public void ARestoreBlockedAtItsEndGoesAsFarAsTheSweepAllows()
    {
        var rig = new LagRig();
        UserCmd cmd = rig.Command(105);
        rig.Lag.Start(Shooter(rig), in cmd);

        // Something now stands where the target was: the sweep from the rewound
        // place stops half way, and the restore goes 95% of that.
        rig.World.Solid = p => p.Equals(rig.TargetAt(110));
        rig.Lag.Finish(Shooter(rig));

        Vector from = rig.TargetAt(105), to = rig.TargetAt(110);
        Assert.Equal((from + (to - from) * (0.5f * 0.95f)).X, rig.TargetPlayer.Origin.X, 3);
    }

    [Fact]
    public void ARestoreBlockedBothWaysLeavesThePlayerRewound()
    {
        var rig = new LagRig();
        UserCmd cmd = rig.Command(105);
        rig.Lag.Start(Shooter(rig), in cmd);

        rig.World.Solid = _ => true;
        rig.Lag.Finish(Shooter(rig));

        Assert.Equal(rig.TargetAt(105), rig.TargetPlayer.Origin);
    }

    //-------------------------------------------------------------------------
    // The gates
    //-------------------------------------------------------------------------

    [Fact]
    public void UnlagOffDisablesIt()
    {
        var rig = new LagRig();
        rig.Settings.Unlag = false;
        UserCmd cmd = rig.Command(105);

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.False(rig.Lag.IsCurrentlyDoingLagCompensation);
        Assert.Equal(rig.TargetAt(110), rig.TargetPlayer.Origin);
        Assert.Empty(rig.World.Relinks);
    }

    [Theory]
    [InlineData("bot")]
    [InlineData("observer")]
    [InlineData("no cl_lagcompensation")]
    [InlineData("single player")]
    public void AShooterTheGatesRefuseMovesNobody(string gate)
    {
        var rig = new LagRig();
        switch (gate)
        {
            case "bot": rig.ShooterPlayer.Bot = true; break;
            case "observer": rig.ShooterPlayer.Observer = true; break;
            case "no cl_lagcompensation": rig.ShooterPlayer.WantsLagCompensation = false; break;
            default: rig.World.MaxClients = 1; break;
        }
        UserCmd cmd = rig.Command(105);

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Equal(rig.TargetAt(110), rig.TargetPlayer.Origin);
    }

    [Fact]
    public void TheShooterIsNeverMoved()
    {
        var rig = new LagRig();
        UserCmd cmd = rig.Command(105);

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.DoesNotContain(rig.World.Relinks, r => r.Player == Shooter(rig));
    }

    [Fact]
    public void ACommandNotAttackingLongAfterTheLastShotMovesNobody()
    {
        var rig = new LagRig();
        rig.Fired(40);
        UserCmd cmd = rig.Command(105, attack: false, number: 46);

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Empty(rig.World.Relinks);
    }

    [Fact]
    public void ACommandNotAttackingJustAfterAShotIsCompensated()
    {
        var rig = new LagRig();
        rig.Fired(45);
        UserCmd cmd = rig.Command(105, attack: false, number: 50);

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Equal(rig.TargetAt(105), rig.TargetPlayer.Origin);
    }

    [Fact]
    public void AFarTargetOutsideTheConeIsNotMoved()
    {
        var rig = new LagRig();
        // 210 units is beyond 1.5 * 100 * 1 s; the shooter looks down -x.
        rig.TargetPlayer.MaxSpeed = 100f;
        UserCmd cmd = rig.Command(105);
        cmd.Yaw = 180f;

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Empty(rig.World.Relinks);
    }

    [Fact]
    public void AFarTargetInsideTheConeIsMoved()
    {
        var rig = new LagRig();
        rig.TargetPlayer.MaxSpeed = 100f;
        UserCmd cmd = rig.Command(105);

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Equal(rig.TargetAt(105), rig.TargetPlayer.Origin);
    }

    private sealed class Refuses : ILagCompensationTargets
    {
        public readonly List<LagCompensationTarget> Asked = [];

        public Decision WantsLagCompensationOnEntity(in LagCompensationTarget target)
        {
            Asked.Add(target);
            return Decision.No;
        }
    }

    [Fact]
    public void AModsRuleDecidesInPlaceOfTheCores()
    {
        var refuses = new Refuses();
        var rig = new LagRig(new LagHooks<ILagCompensationTargets>(refuses));
        UserCmd cmd = rig.Command(105);

        rig.Lag.Start(Shooter(rig), in cmd);

        Assert.Empty(rig.World.Relinks);
        LagCompensationTarget asked = Assert.Single(refuses.Asked);
        Assert.Equal((Shooter(rig), rig.TargetPlayer.Handle, rig.TargetAt(110), 1f),
                     (asked.Shooter, asked.Target, asked.TargetOrigin, asked.MaxUnlag));
    }

    [Fact]
    public void ASecondStartBeforeAFinishIsRefusedOutLoud()
    {
        var rig = new LagRig();
        UserCmd cmd = rig.Command(105);
        rig.Lag.Start(Shooter(rig), in cmd);
        int relinks = rig.World.Relinks.Count;

        rig.Lag.Start(rig.TargetPlayer.Handle, in cmd);

        Assert.Equal(relinks, rig.World.Relinks.Count);
        Assert.Contains(rig.Log.Lines, l => l.Warning && l.Text.Contains("already active"));
    }

    [Fact]
    public void StartWithoutACommandReadsTheCurrentOne()
    {
        var rig = new LagRig();
        UserCmd cmd = rig.Command(105);

        rig.Lag.Start(Shooter(rig));
        rig.Lag.Finish(Shooter(rig));
        Vector withNone = rig.TargetPlayer.Origin;

        using (rig.Commands.Begin(Shooter(rig), in cmd))
            rig.Lag.Start(Shooter(rig));

        Assert.Equal(rig.TargetAt(110), withNone);
        Assert.Equal(rig.TargetAt(105), rig.TargetPlayer.Origin);
        Assert.False(rig.Commands.TryGet(Shooter(rig), out _));
    }
}

/// <summary><c>cs_sv_lagcomp</c>: the history report and the rewind probe.</summary>
public class LagCompensationCommandTests
{
    private static (LagCompensationCommand Command, RecordingEngine Engine, LagRig Rig) Command()
    {
        var rig = new LagRig();
        var engine = TestServices.Mock();
        engine.Strict = false;
        Vector seen = rig.TargetAt(105);
        engine.ReturnsEachCall("WorldQuery.TraceLine",
            () => rig.TargetPlayer.Origin.Equals(seen)
                ? new TraceResult(true, 0.5f, seen, default, rig.TargetPlayer.Handle)
                : new TraceResult(false, 1f, default, default, EntityHandle.None));
        return (new LagCompensationCommand(rig.Lag, rig.World, engine, engine), engine, rig);
    }

    private static string Printed(RecordingEngine engine)
        => string.Concat(engine.Calls.Where(c => c.Member == "Console.Msg").Select(c => (string)c.Args[0]!));

    [Fact]
    public void TheReportNamesEachSlotsRecordsAndClientWords()
    {
        var (command, engine, rig) = Command();
        rig.ShooterPlayer.Latency = 0.05f;

        command.RunLine("");

        string text = Printed(engine);
        Assert.Contains("slot 2: records 11 (1.500..1.650)", text);
        Assert.Contains("slot 1: records 11", text);
        Assert.Contains("wants True lerp 0.000 latency 0.050", text);
    }

    [Fact]
    public void TheProbeReadsThePlaceAndTheTraceDuringAndAfterTheRewind()
    {
        var (command, engine, rig) = Command();
        rig.ShooterPlayer.Latency = 5 * LagWorld.Tick;

        command.RunLine("1 5");

        string text = Printed(engine);
        Assert.Contains("compensating True", text);
        Assert.Contains($"slot 2: before {rig.TargetAt(110)} during {rig.TargetAt(105)} record {rig.TargetAt(105)} after {rig.TargetAt(110)}", text);
        Assert.Contains("trace at the rewound place during hits the player; after the restore hits nothing", text);
        Assert.Equal(rig.TargetAt(110), rig.TargetPlayer.Origin);
    }

    [Fact]
    public void TheProbesCommandLooksAtTheTargetSoAFarOneIsNotRefusedByTheCone()
    {
        var (command, engine, rig) = Command();
        // 800 units off along -x: out of reach, and behind a command at yaw 0.
        rig.ShooterPlayer.Origin = new Vector(1000f, 0f, 0f);
        rig.ShooterPlayer.Latency = 5 * LagWorld.Tick;

        command.RunLine("1 5");

        Assert.Contains($"during {rig.TargetAt(105)}", Printed(engine));
    }

    [Fact]
    public void TheProbeSaysWhenTheSlotHoldsNoPlayer()
    {
        var (command, engine, _) = Command();

        command.RunLine("3 5");

        Assert.Contains("no player in slot 3", Printed(engine));
    }
}

internal static class LagRigFrames
{
    /// <summary>A frame with no target moved (one may be gone).</summary>
    public static void Frame2(this LagRig rig, int tick)
    {
        rig.World.At(tick);
        rig.Lag.RecordFrame();
    }
}
