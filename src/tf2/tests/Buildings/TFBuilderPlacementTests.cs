using SourceSharp.Abi;
using TF2.Server.Buildings;
using TF2.Shared;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The click-to-place half: the order walks the blueprint, the trigger
/// places it, the alt-fire turns it, and the carried building comes back
/// out of the arms. (CalculatePlacementPos, tf_weapon_builder.cpp's
/// StartPlacement/StopPlacement.)
/// </summary>
public class TFBuilderPlacementTests
{
    /// <summary>The build distance over both hulls' 2D radii plus the 4 buffer (baseobject_shared.cpp:542).</summary>
    private static readonly float BuildDistance = 2f * MathF.Sqrt(2f * 24f * 24f) + TFBuilderPlacement.BuildBuffer;

    /// <summary>A funded engineer at the origin, centre (0,0,41), facing yaw 0, over the placement.</summary>
    private static (BuildingsRig Rig, TFBuilderPlacement Placement, EntityHandle Engineer) AField(
        bool groundless = false, int metal = 200)
    {
        var rig = new BuildingsRig();
        EntityHandle engineer = rig.AddBuilder();
        rig.Engine.Returns("Players.GetHandle", engineer)
                  .Returns("Entities.GetEyeAngles", Vector.Zero)
                  .Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f))
                  .Returns("Entities.SetParent", true);   // the lift's mount, for the carried fact
        rig.Centers[engineer] = new Vector(0f, 0f, 41f);
        rig.Purse.Pockets[TFAmmoNames.Name(TFAmmo.Metal)] = metal;
        rig.Groundless = groundless;
        return (rig, rig.Placement(), engineer);
    }

    /// <summary>The build origin an eye-yaw-0 walk gives: a step of <see cref="BuildDistance"/> along +X.</summary>
    private static Vector BuildOrigin => new(BuildDistance, 0f, 41f);

    [Fact]
    public void AnOrderWalksTheBlueprintOntoFlatGround()
    {
        var (rig, placement, engineer) = AField();

        Assert.True(placement.BeginOrder(engineer, TFObjectType.Sentrygun, 0));
        placement.StartPlacement(3);
        Assert.Single(rig.Spawner.Placed);    // the blueprint born

        placement.UpdatePlacement(3);

        (Vector origin, Vector angles) = rig.LastTeleport;
        Assert.Equal(BuildDistance, origin.X, 3);
        Assert.Equal(0f, origin.Y, 3);
        Assert.Equal(0f, origin.Z, 3);        // the ground plane, the hull's floor
        Assert.Equal(0f, angles.Y, 3);        // the eye yaw, no turns yet
    }

    [Fact]
    public void AWallRefusesTheSpotAndTheWalkSaysSo()
    {
        var (rig, placement, engineer) = AField(groundless: true);
        Assert.True(placement.BeginOrder(engineer, TFObjectType.Sentrygun, 0));
        placement.StartPlacement(3);
        placement.UpdatePlacement(3);

        // The hull sweep found nothing, so the blueprint wears the error
        // origin — the build origin dropped half a box height (:591-593).
        (Vector origin, _) = rig.LastTeleport;
        Vector error = BuildOrigin - new Vector(0f, 0f, 33f);   // half the sentry's 66-tall box
        Assert.Equal(error.X, origin.X, 3);
        Assert.Equal(error.Z, origin.Z, 3);
        Assert.False(((TFBaseObject)rig.Registry.Get(rig.Spawner.Placed.Keys.Single())!).IsDead);

        Assert.False(placement.Place(3));    // a refused spot is no place
        Assert.False(((TFBaseObject)rig.Registry.Get(rig.Spawner.Placed.Keys.Single())!).IsDead);
    }

    [Fact]
    public void TheTriggerPaysTheMetalAndStartsTheBuild()
    {
        var (rig, placement, engineer) = AField();
        Assert.True(placement.BeginOrder(engineer, TFObjectType.Sentrygun, 0));
        placement.StartPlacement(3);

        Assert.True(placement.Place(3));

        (EntityHandle player, int count, string name) = Assert.Single(rig.Purse.Paid);
        Assert.Equal(engineer, player);
        Assert.Equal(130, count);             // the sentry's Cost (TFObjectDefs)
        Assert.Equal(TFAmmoNames.Name(TFAmmo.Metal), name);

        EntityHandle handle = Assert.Single(rig.Spawner.Placed).Key;
        TFBaseObject building = (TFBaseObject)rig.Registry.Get(handle)!;
        Assert.True(building.IsBuilding);
        Assert.False(building.IsPlacing);

        int walks = rig.Teleports;
        placement.UpdatePlacement(3);         // placed: the builder's tail no longer walks
        Assert.Equal(walks, rig.Teleports);
    }

    [Fact]
    public void AShortPurseTakesTheBlueprintBackDown()
    {
        var (rig, placement, engineer) = AField(metal: 130);   // enough to order, then it runs out
        Assert.True(placement.BeginOrder(engineer, TFObjectType.Sentrygun, 0));
        placement.StartPlacement(3);
        rig.Purse.Pockets[TFAmmoNames.Name(TFAmmo.Metal)] = 0;

        Assert.False(placement.Place(3));

        EntityHandle handle = Assert.Single(rig.Spawner.Placed).Key;
        Assert.True(((TFBaseObject)rig.Registry.Get(handle)!).IsDead);   // the blueprint came down
    }

    [Fact]
    public void TheAltFireTurnsTheBlueprintAndTheNextWalkWearsIt()
    {
        var (rig, placement, engineer) = AField();
        Assert.True(placement.BeginOrder(engineer, TFObjectType.Sentrygun, 0));
        placement.StartPlacement(3);

        placement.Rotate(3);
        placement.Rotate(3);

        TFBaseObject blueprint = (TFBaseObject)rig.Registry.Get(Assert.Single(rig.Spawner.Placed).Key)!;
        Assert.Equal(2, blueprint.DesiredBuildRotations);

        placement.UpdatePlacement(3);
        (_, Vector angles) = rig.LastTeleport;
        Assert.Equal(180f, angles.Y, 3);      // yaw 0 plus two quarter turns
    }

    [Fact]
    public void AHolsterTakesTheFreshBlueprintDown()
    {
        var (rig, placement, engineer) = AField();
        Assert.True(placement.BeginOrder(engineer, TFObjectType.Sentrygun, 0));
        placement.StartPlacement(3);
        EntityHandle handle = Assert.Single(rig.Spawner.Placed).Key;

        placement.StopPlacement(3);

        Assert.True(((TFBaseObject)rig.Registry.Get(handle)!).IsDead);   // the fresh blueprint is down
    }

    [Fact]
    public void TheCarriedBuildingComesBackOutOfTheArms()
    {
        var (rig, placement, engineer) = AField();
        TFSentryGun carried = rig.Spawn<TFSentryGun>("obj_sentrygun");
        carried.StartPlacement(engineer);     // the engineer's own
        carried.MakeCarriedObject(engineer);

        placement.StartPlacement(3);

        Assert.True(carried.IsPlacing);       // ResumePlacement's StartPlacement
        placement.Rotate(3);
        Assert.Equal(1, carried.DesiredBuildRotations);

        placement.StopPlacement(3);           // the builder leaves the hands
        Assert.True(carried.IsCarried);       // back into the arms, not removed
        Assert.False(carried.IsDead);
    }

    // ---- EstimateValidBuildPos's respawn-room arms (tf_obj.cpp:707-715) --------------

    [Fact]
    public void ASpotInsideARespawnRoomIsRefusedByName()
    {
        var (rig, placement, engineer) = AField();
        rig.Rooms.InRoom = true;              // an active room holds every point
        Assert.True(placement.BeginOrder(engineer, TFObjectType.Sentrygun, 0));
        placement.StartPlacement(3);

        Assert.False(placement.Place(3));

        Assert.Contains(rig.Log.Lines,
                        line => line.Text.Contains("spot refused", System.StringComparison.Ordinal)
                             && line.Text.Contains("respawn room", System.StringComparison.Ordinal));
        TFBaseObject blueprint = (TFBaseObject)rig.Registry.Get(Assert.Single(rig.Spawner.Placed).Key)!;
        Assert.False(blueprint.IsDead);       // a refused spot leaves the blueprint placing
        Assert.Empty(rig.Purse.Paid);         // and never pays
    }

    [Fact]
    public void ASpotWhoseFarEdgeCrossesAVisualizerIsRefused()
    {
        var (rig, placement, engineer) = AField();
        rig.Rooms.CrossesVisualizer = true;   // an enemy visualizer stands behind the spot
        Assert.True(placement.BeginOrder(engineer, TFObjectType.Sentrygun, 0));
        placement.StartPlacement(3);

        Assert.False(placement.Place(3));

        // EstimateValidBuildPos (tf_obj.cpp:713-715): the segment runs from the
        // player's WorldSpaceCenter to the build origin led another distance-plus-8
        // along the eye's forward.
        (Vector start, Vector end) = Assert.Single(rig.Rooms.VisualizerAsks);
        Assert.Equal(0f, start.X, 3);
        Assert.Equal(0f, start.Y, 3);
        Assert.Equal(41f, start.Z, 3);        // the engineer's centre, not the eye
        Assert.Equal(2f * BuildDistance + 8f, end.X, 3);
        Assert.Equal(0f, end.Y, 3);
        Assert.Equal(0f, end.Z, 3);

        Assert.Contains(rig.Log.Lines,
                        line => line.Text.Contains("spot refused", System.StringComparison.Ordinal)
                             && line.Text.Contains("visualizer", System.StringComparison.Ordinal));
        TFBaseObject blueprint = (TFBaseObject)rig.Registry.Get(Assert.Single(rig.Spawner.Placed).Key)!;
        Assert.False(blueprint.IsDead);
    }

    [Fact]
    public void AnOpenSpotBesideTheRoomStillPlaces()
    {
        var (rig, placement, engineer) = AField();
        rig.Rooms.InRoom = true;              // a room exists ...
        rig.Rooms.RoomAt = new Vector(10000f, 0f, 0f);   // ... nowhere near the spot
        Assert.True(placement.BeginOrder(engineer, TFObjectType.Sentrygun, 0));
        placement.StartPlacement(3);

        Assert.True(placement.Place(3));

        Assert.Single(rig.Purse.Paid);        // open: the metal went in
        Assert.NotEmpty(rig.Rooms.VisualizerAsks);   // the far edge was asked and crossed nothing
    }

    [Fact]
    public void AnOrderTheGateRefusesRecordsNothing()
    {
        var (rig, placement, engineer) = AField();
        EntityHandle soldier = rig.AddBuilder(TFTeam.Red, TFClass.Soldier);
        rig.Engine.Returns("Players.GetHandle", soldier);

        Assert.False(placement.BeginOrder(soldier, TFObjectType.Sentrygun, 0));

        placement.StartPlacement(3);
        Assert.Empty(rig.Spawner.Placed);
        Assert.Empty(rig.Purse.Paid);
    }
}
