extern alias client;

using SourceSharp.Abi;

using Xunit;

using Adapter = client::TF2.Client.Ui.TFHudBuildingStatus;
using BuildingResource = client::TF2.Client.Buildings.ClientTFBuildingResource;
using BuildingResources = client::TF2.Client.Buildings.TFClientBuildingResources;
using Carrier = client::TF2.Shared.Player.TFPlayerNetState;
using PlayerData = client::TF2.Shared.Player.ITFPlayerData;
using ClientViews = client::TF2.Shared.Player.TFPlayerNetStateViews;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using PlayerResource = client::TF2.Client.Player.ClientTFPlayerResource;
using PlayerResources = client::TF2.Client.Player.TFClientPlayerResources;
using TFClassId = client::TF2.Shared.TFClass;
using F = client::TF2.Shared.Buildings.TFBuildingResourceNetState;

// The contract assembly is unaliased: the interface, the kind and the line
// record are global, as the kill feed's are.
using LineKind = TF2.Client.Ui.TFBuildingLineKind;

namespace TF2.Tests.Client;

/// <summary>
/// The building status's reads (stock's <c>CBuildingStatusContainer</c>
/// over its <c>CBuildingStatusItem</c>s, tf_hud_building_status.cpp): the
/// engineer's own buildings, the spy's sapped-enemy arm, and the switches
/// and bars each line carries straight off the carrier's row.
/// </summary>
public sealed class TFHudBuildingStatusTests
{
    private const int Local = 1;

    private sealed class Rig
    {
        public readonly Engine Engine = new();
        public readonly BuildingResources Buildings = new();
        public readonly PlayerResources Players = new();
        public readonly CarriersList Carriers = new();
        public readonly BuildingResource Resource;
        public readonly Adapter Source;
        public readonly PlayerResource PlayerRows;

        public Rig(TFClassId localClass = TFClassId.Engineer, int localTeam = 2)
        {
            Engine.Returns("Hud.LocalPlayerIndex", Local);
            Resource = new BuildingResource(Engine, Buildings);
            Resource.State.BindLocal();
            Resource.OnSpawn();
            var carrier = new Carrier();
            carrier.BindLocal();
            ClientViews.SetClass(carrier, localClass);
            Carriers.Serve(Local, carrier);

            // The tf_player_manager rows: the local player's team, the spy
            // arm's comparison, and each builder's team (a building's row is
            // its builder's slot).
            PlayerRows = new PlayerResource(Engine, Players);
            PlayerRows.State.BindLocal();
            PlayerRows.State.Team[Local] = (byte)localTeam;
            PlayerRows.OnSpawn();

            Source = new Adapter(Engine, Buildings, Players, Carriers);
        }

        /// <summary>Row <paramref name="slot"/> * kinds + <paramref name="kind"/>.</summary>
        public int Row(int slot, int kind) => slot * 4 + kind;
    }

    /// <summary>The player-data seam: one carrier per index, the target ID's
    /// own fake reshaped.</summary>
    private sealed class CarriersList : PlayerData
    {
        private readonly Dictionary<int, Carrier> _states = new();

        public Carrier Serve(int index, Carrier carrier)
        {
            _states[index] = carrier;
            return carrier;
        }

        public Carrier? Predicted(int playerIndex) => null;

        public Carrier? Networked(int playerIndex) => _states.TryGetValue(playerIndex, out var state) ? state : null;
    }

    [Fact]
    public void AnEngineerWithNothingBuiltReadsFourNotBuiltPanels()
    {
        // The engineer's container adds its four panels at construction
        // (tf_hud_building_status.cpp:1186-1192; the disposable sentry's
        // fifth draws only with its attribute) and an item with no object
        // shows its NotBuiltPanel (:183-186).
        var rig = new Rig();

        Assert.Equal(4, rig.Source.Count);
        Assert.Equal((LineKind.Sentry, LineKind.Dispenser, LineKind.TeleporterEntrance, LineKind.TeleporterExit),
                     (rig.Source.Building(0).Kind, rig.Source.Building(1).Kind,
                      rig.Source.Building(2).Kind, rig.Source.Building(3).Kind));
        Assert.True(rig.Source.Building(0).Placing && rig.Source.Building(3).Placing, "an absent building reads not built");
    }

    [Fact]
    public void AClassChosenAfterTheFirstReadIsSeen()
    {
        // Stock's ShouldDraw asks the class every frame
        // (tf_hud_building_status.cpp:1201-1206): a panel first read before
        // joinclass draws once the engineer is chosen.
        var rig = new Rig(TFClassId.Undefined);
        Assert.Equal(0, rig.Source.Count);

        ClientViews.SetClass(rig.Carriers.Networked(Local)!, TFClassId.Engineer);
        Assert.Equal(4, rig.Source.Count);
    }

    [Fact]
    public void AnEngineersPlacedBuildingIsItsKindsLine()
    {
        var rig = new Rig();
        int row = rig.Row(Local, (int)LineKind.Sentry);
        rig.Resource.State.Flags[row] = client::TF2.Shared.Buildings.TFBuildingResourceNetState.Present;

        Assert.Equal(4, rig.Source.Count);
        var line = rig.Source.Building(0);
        Assert.Equal((LineKind.Sentry, 2), (line.Kind, line.Team));
    }

    [Fact]
    public void ConstructionAndTheSapperRideTheLine()
    {
        var rig = new Rig();
        int row = rig.Row(Local, (int)LineKind.Dispenser);
        rig.Resource.State.Flags[row] = (byte)(F.Present | F.Building | F.Sapped);
        rig.Resource.State.BuildProgress[row] = 40;

        var line = rig.Source.Building(1);   // the dispenser's panel, stock's second
        Assert.True(line.Building);
        Assert.True(line.Sapped);
        Assert.False(line.Placing);
        Assert.Equal(40, line.BuildProgress);
    }

    [Fact]
    public void APlacingBuildingReadsPlacing()
    {
        var rig = new Rig();
        int row = rig.Row(Local, (int)LineKind.Sentry);
        rig.Resource.State.Flags[row] = client::TF2.Shared.Buildings.TFBuildingResourceNetState.Present;

        Assert.True(rig.Source.Building(0).Placing);
    }

    [Fact]
    public void TheBarsReadTheirPairs()
    {
        var rig = new Rig();
        int sentry = rig.Row(Local, (int)LineKind.Sentry);
        int dispenser = rig.Row(Local, (int)LineKind.Dispenser);
        rig.Resource.State.Flags[sentry] = (byte)(F.Present | F.Built);
        rig.Resource.State.Health[sentry] = 90;
        rig.Resource.State.MaxHealth[sentry] = 150;
        rig.Resource.State.Ammo[sentry] = 60;
        rig.Resource.State.MaxAmmo[sentry] = 150;
        rig.Resource.State.Flags[dispenser] = (byte)(F.Present | F.Built);
        rig.Resource.State.Ammo[dispenser] = 200;
        rig.Resource.State.MaxAmmo[dispenser] = 400;

        Assert.Equal((90, 150, 60, 150), (rig.Source.Building(0).Health, rig.Source.Building(0).MaxHealth,
                                          rig.Source.Building(0).Shells, rig.Source.Building(0).MaxShells));
        // The shared column reads by the row's kind: the sentry's is no metal,
        // the dispenser's no shells.
        Assert.Equal((0, 200, 0), (rig.Source.Building(0).Ammo, rig.Source.Building(1).Ammo,
                                   rig.Source.Building(1).Shells));
    }

    [Fact]
    public void ANonEngineerReadsNoBuildingOfItsOwn()
    {
        var rig = new Rig(TFClassId.Soldier);
        rig.Resource.State.Flags[rig.Row(Local, (int)LineKind.Sentry)] =
            client::TF2.Shared.Buildings.TFBuildingResourceNetState.Present;

        Assert.Equal(0, rig.Source.Count);
    }

    [Fact]
    public void TheSpyReadsTheSappedEnemies()
    {
        var rig = new Rig(TFClassId.Spy, localTeam: 2);
        rig.Resource.State.Flags[rig.Row(3, (int)LineKind.Sentry)] = (byte)(F.Present | F.Sapped);
        rig.PlayerRows.State.Team[3] = 3;      // an enemy's
        rig.Resource.State.Flags[rig.Row(4, (int)LineKind.Dispenser)] = (byte)(F.Present | F.Sapped);
        rig.PlayerRows.State.Team[4] = 2;      // a teammate's

        Assert.Equal(1, rig.Source.Count);
        Assert.Equal(LineKind.Sentry, rig.Source.Building(0).Kind);
    }

    [Fact]
    public void TheTeleporterRowsPartByMode()
    {
        var rig = new Rig();
        int entrance = rig.Row(Local, (int)LineKind.TeleporterEntrance);
        int exit = rig.Row(Local, (int)LineKind.TeleporterExit);
        rig.Resource.State.Flags[entrance] = (byte)(F.Present | F.Built);
        rig.Resource.State.Flags[exit] = (byte)(F.Present | F.Built);

        Assert.Equal((LineKind.TeleporterEntrance, LineKind.TeleporterExit),
                     (rig.Source.Building(2).Kind, rig.Source.Building(3).Kind));
        Assert.False(rig.Source.Building(2).Placing || rig.Source.Building(3).Placing);
    }
}
