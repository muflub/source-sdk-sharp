using SourceSharp.Abi;
using SourceSharp.Shared;
using TF2.Server.Buildings;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// <c>SpawnBread</c> (tf_obj_teleporter.cpp:1027-1032, :1460-1520): an
/// arrival rolls for lunch — the soldier one time in three, everyone else
/// one in twenty — and the winner gets a physics loaf launched off the
/// exit's crown, thrown forward and up with a tumble and removed after ten
/// seconds. The tree throws it as a <c>gib</c> (the divergence is
/// documented on the arm); these facts pin the roll, the bound it rolls
/// against, the model table and the launch.
/// </summary>
public class TFTeleporterBreadFacts
{
    /// <summary>The whole trip: booked, sent, and the arrival released with its roll pending.</summary>
    private static EntityHandle Arrive(BuildingsRig rig, TFTeleporter entrance, TFTeleporter exit)
    {
        TeleporterHarness.Tick(rig, entrance, exit, 1f);   // the pair to READY
        EntityHandle rider = TeleporterHarness.AStandingRider(rig);
        entrance.OnTouch(rider);
        TeleporterHarness.Tick(rig, entrance, exit, 11f);   // the send and the trip
        return rider;
    }

    [Fact]
    public void TheTripThrowsALoafOffTheExitsCrown()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        rig.Classes.Class = TFClass.Scout;
        _ = Arrive(rig, entrance, exit);                    // default draws: the roll lands on 0

        // :1465 at the fake's floor draws the first of the nine bread models.
        EngineCall made = Assert.Single(rig.Engine.AllOf("Entities.Create"),
                                       c => c.ArgAs<string>(0) == EntityNames.Gib);
        EntityHandle bread = rig.LastGib;
        Assert.NotEqual(EntityHandle.None, bread);

        EngineCall model = Assert.Single(rig.Engine.AllOf("EntityRender.SetModel"),
                                         c => c.ArgAs<EntityHandle>(0) == bread);
        Assert.Equal("models/weapons/c_models/c_bread/c_bread_baguette.mdl", model.ArgAs<string>(1));

        // :1479-1480: the exit's own origin, TELEPORTER_MAXS.z (12) + 50 of
        // headroom — the crown, not the deck the rider lands on.
        EngineCall placed = Assert.Single(rig.Engine.AllOf("Entities.SetOrigin"),
                                          c => c.ArgAs<EntityHandle>(0) == bread);
        Assert.Equal(new Vector(0f, 0f, 62f), placed.ArgAs<Vector>(1));

        // :1507-1510: forward at 100, up at 200, and the jitter at the
        // fake's floor (-50 per axis) — the stock launch off zero angles.
        EngineCall thrown = Assert.Single(rig.Engine.AllOf("Entities.SetVelocity"),
                                         c => c.ArgAs<EntityHandle>(0) == bread);
        Vector launch = thrown.ArgAs<Vector>(1);
        Assert.Equal(50f, launch.X, 2);
        Assert.Equal(-50f, launch.Y, 2);
        Assert.Equal(150f, launch.Z, 2);

        // The solver takes it as debris (:1476, :1482), and a solver-owned
        // body takes no game-side angular velocity.
        EngineCall init = Assert.Single(rig.Engine.AllOf("EntityPhysics.InitPhysics"),
                                        c => c.ArgAs<EntityHandle>(0) == bread);
        Assert.True(init.ArgAs<bool>(2));
        Assert.False(rig.Engine.Called("Entities.SetAngularVelocity"));
    }

    [Fact]
    public void AFailedRollKeepsLunchOnTheTable()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        rig.Classes.Class = TFClass.Scout;
        TeleporterHarness.Tick(rig, entrance, exit, 1f);
        EntityHandle rider = TeleporterHarness.AStandingRider(rig);
        entrance.OnTouch(rider);
        rig.Random.Ints.Enqueue(1);                         // the roll misses (:1029)
        TeleporterHarness.Tick(rig, entrance, exit, 11f);

        // The miss happens before the model draw: no loaf, and no draw after it.
        Assert.False(rig.Engine.Called("Entities.Create"));
        Assert.Equal((0, 19), rig.Random.IntDraws.Single());
    }

    [Fact]
    public void TheSoldierRollsOneTimeInThree()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        rig.Classes.Class = TFClass.Soldier;
        TeleporterHarness.Tick(rig, entrance, exit, 1f);
        EntityHandle rider = TeleporterHarness.AStandingRider(rig);
        entrance.OnTouch(rider);
        rig.Random.Ints.Enqueue(5);                         // outside either bound
        TeleporterHarness.Tick(rig, entrance, exit, 11f);

        // :1028: the soldier's nMax is 2 — one in three, not one in twenty.
        Assert.Contains((0, 2), rig.Random.IntDraws);
        Assert.DoesNotContain((0, 19), rig.Random.IntDraws);
        Assert.Equal(EntityHandle.None, rig.LastGib);
    }

    [Fact]
    public void TheRestOfThemRollOneTimeInTwenty()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        rig.Classes.Class = TFClass.Scout;
        TeleporterHarness.Tick(rig, entrance, exit, 1f);
        EntityHandle rider = TeleporterHarness.AStandingRider(rig);
        entrance.OnTouch(rider);
        rig.Random.Ints.Enqueue(5);                         // outside either bound
        TeleporterHarness.Tick(rig, entrance, exit, 11f);

        // :1028: everyone else draws against 19 — the stock comment's 1/20.
        Assert.Contains((0, 19), rig.Random.IntDraws);
        Assert.Equal(EntityHandle.None, rig.LastGib);
    }

    [Fact]
    public void TheModelDrawsFromTheNineAndTheClassRangeBoundsIt()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        rig.Classes.Class = TFClass.Scout;
        TeleporterHarness.Tick(rig, entrance, exit, 1f);
        EntityHandle rider = TeleporterHarness.AStandingRider(rig);
        entrance.OnTouch(rider);
        rig.Random.Ints.Enqueue(0);                         // the roll lands
        rig.Random.Ints.Enqueue(3);                         // the model draw
        TeleporterHarness.Tick(rig, entrance, exit, 11f);

        // :1465: the draw is the normal-class range — LAST - FIRST - 1 = 8 —
        // over g_pszBreadModels (shared/tf/tf_shareddefs.cpp:184-195).
        Assert.Contains((0, 8), rig.Random.IntDraws);
        EngineCall model = Assert.Single(rig.Engine.AllOf("EntityRender.SetModel"),
                                         c => c.ArgAs<EntityHandle>(0) == rig.LastGib);
        Assert.Equal("models/weapons/c_models/c_bread/c_bread_cornbread.mdl", model.ArgAs<string>(1));
    }
}
