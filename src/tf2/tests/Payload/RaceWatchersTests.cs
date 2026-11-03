using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Entities;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Payload;

/// <summary>
/// The Payload Race shape (tf_logic_multiple_escort): one cart per team on its own track,
/// each watcher's cap area answering to its own team's pushers while the enemy still
/// blocks it, and the first cart at its goal winning that team — booking its progress
/// onto its own team's race-track before the winning call (the SetWinningTeam race arm,
/// tf_gamerules.cpp:9633-9650).
/// </summary>
public sealed class RaceWatchersTests
{
    /// <summary>Two straight tracks with a cart, a watcher and a cap area each, all round-activated and bound.</summary>
    private static (PayloadRig Rig, FuncTrackTrain BlueTrain, TeamTrainWatcher BlueWatcher, TriggerAreaCapture BlueArea,
                    FuncTrackTrain RedTrain, TeamTrainWatcher RedWatcher, TriggerAreaCapture RedArea) Race()
    {
        var rig = new PayloadRig();
        rig.Settings.CapLinear = false;   // both tracks' ends are cappable on their own

        var b1 = rig.Node("b1", Vector.Zero);
        var b2 = rig.Node("b2", new Vector(500f, 0f, 0f));
        var b3 = rig.Node("b3", new Vector(1000f, 0f, 0f));
        b1.LinkTo(b2);
        b2.LinkTo(b3);
        var blueTrain = rig.Train("bcart");
        var blueWatcher = rig.Watcher("bcart", "b1", "b3");
        _ = rig.Point("bcp", 0);
        var blueArea = rig.Area("bcp");

        var r1 = rig.Node("r1", Vector.Zero);
        var r2 = rig.Node("r2", new Vector(500f, 0f, 0f));
        var r3 = rig.Node("r3", new Vector(1000f, 0f, 0f));
        r1.LinkTo(r2);
        r2.LinkTo(r3);
        var redTrain = rig.Train("rcart");
        var redWatcher = rig.Watcher("rcart", "r1", "r3", ("TeamNum", "2"));
        _ = rig.Point("rcp", 1);
        var redArea = rig.Area("rcp");

        // Each area answers its own train as its parent: the binding scan asks
        // GetParent per area and must see the real pairings, not the single
        // value the rig's default answers (the last-call shape the hill fact on
        // TeamTrainWatcherTests uses for per-node spawn flags).
        rig.Engine.ReturnsEachCall("Entities.GetParent", () =>
            (EntityHandle)rig.Engine.Calls[^1].Args[0]! == blueArea.Handle ? blueTrain.Handle : redTrain.Handle);

        rig.StartRound();
        blueTrain.StartOn(b1);
        redTrain.StartOn(r1);

        // each watcher binds the area parented to its own train
        blueWatcher.WatcherActivate();
        redWatcher.WatcherActivate();

        return (rig, blueTrain, blueWatcher, blueArea, redTrain, redWatcher, redArea);
    }

    [Fact]
    public void EachTeamsCartMovesOnlyForItsOwnPushers()
    {
        var (rig, blueTrain, blueWatcher, blueArea, redTrain, redWatcher, redArea) = Race();

        var blueGuy = rig.Players.Add(1, TFTeam.Blue);
        var redGuy = rig.Players.Add(2, TFTeam.Red);

        // the cart's standing speed, before any team pushes: what an
        // untouched cart must still be answering
        float idleSpeed = redTrain.CurrentSpeed;

        // Blue's pusher stands in blue's cap: blue's cart answers, red's does not.
        rig.Registry.Touch(blueArea.Handle, blueGuy);
        rig.ThinkAt(blueArea, 0.5f);

        // the count arrived through each watcher's own parent-bound area
        Assert.Equal(blueArea.Handle, blueWatcher.AreaCap!.Handle);

        Assert.Equal(TeamTrainWatcher.SpeedOneCapper * blueTrain.MaxSpeed, blueTrain.CurrentSpeed);
        Assert.Equal(idleSpeed, redTrain.CurrentSpeed);
        rig.ThinkAt(blueWatcher, 0.6f);
        Assert.Equal(1, rig.Mode.Cappers[(int)TFTeam.Blue]);
        Assert.False(rig.Mode.Cappers.ContainsKey((int)TFTeam.Red),
            "blue's pusher must not write red's row");

        // Red's pusher stands in red's cap: red's cart answers on its own band,
        // and blue's cart keeps the speed its own team earned.
        rig.Registry.Touch(redArea.Handle, redGuy);
        rig.ThinkAt(redArea, 1f);

        Assert.True(redArea.TouchingEntities.Contains(redGuy), "the touch must reach red's area");
        Assert.NotNull(redArea.ControlPoint);
        Assert.Equal((int)TFTeam.Red, redArea.TeamInZone);
        Assert.Equal((int)TFTeam.Red, redArea.CapturingTeam);
        Assert.Equal(redArea.Handle, redWatcher.AreaCap!.Handle);
        Assert.Equal(TeamTrainWatcher.SpeedOneCapper * redTrain.MaxSpeed, redTrain.CurrentSpeed);
        Assert.Equal(TeamTrainWatcher.SpeedOneCapper * blueTrain.MaxSpeed, blueTrain.CurrentSpeed);
        rig.ThinkAt(redWatcher, 1.1f);
        Assert.Equal(1, rig.Mode.Cappers[(int)TFTeam.Red]);

        // the enemy still blocks: blue stepping into red's cap freezes red's cart
        // (-1 through the area's blocked report), and blue's cart keeps rolling
        rig.Registry.Touch(redArea.Handle, blueGuy);
        rig.ThinkAt(redArea, 1.5f);
        Assert.True(redArea.IsBlocked, "the block needs both teams in red's cap for this fact to mean anything");
        Assert.Equal(0f, redTrain.CurrentSpeed);
        Assert.Equal(TeamTrainWatcher.SpeedOneCapper * blueTrain.MaxSpeed, blueTrain.CurrentSpeed);
        rig.ThinkAt(redWatcher, 1.6f);
        Assert.Equal(-1, rig.Mode.Cappers[(int)TFTeam.Red]);
        Assert.Equal(1, rig.Mode.Cappers[(int)TFTeam.Blue]);
    }

    [Fact]
    public void TheFirstCartAtItsGoalWinsForItsTeam()
    {
        var (rig, blueTrain, blueWatcher, _, redTrain, redWatcher, _) = Race();

        // blue's cart reaches its goal first: the completion edge books the
        // progress on blue's own track total and wins the round for blue
        blueTrain.FinishStep();
        rig.EngineOrigin = new Vector(1000f, 0f, 0f);
        rig.ThinkAt(blueWatcher, 0.1f);

        Assert.Single(rig.Control.Wins);
        Assert.Equal(TFTeam.Blue, rig.Control.Wins[0].Team);
        Assert.Equal(1f, rig.Mode.TrackTotals[(int)TFTeam.Blue], 2);
        Assert.False(rig.Mode.TrackTotals.ContainsKey((int)TFTeam.Red),
            "blue's cart books blue's track only");

        // red's cart reaches its own goal after: it books its own total, and its
        // winning call lands behind blue's — the first cart keeps the win
        redTrain.FinishStep();
        rig.ThinkAt(redWatcher, 0.2f);

        Assert.Equal(1f, rig.Mode.TrackTotals[(int)TFTeam.Red], 2);
        Assert.Equal(1f, rig.Mode.TrackTotals[(int)TFTeam.Blue], 2);
        Assert.Equal(2, rig.Control.Wins.Count);
        Assert.Equal(TFTeam.Blue, rig.Control.Wins[0].Team);
        Assert.Equal(TFTeam.Red, rig.Control.Wins[1].Team);
    }
}
