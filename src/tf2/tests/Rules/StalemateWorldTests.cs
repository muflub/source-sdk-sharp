using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Buildings;
using TF2.Server.Entities.Items;
using TF2.Server.Projectiles;
using TF2.Server.Rules;
using TF2.Tests.Buildings;
using TF2.Tests.Entities.Items;
using TF2.Tests.Entities.Rooms;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// <see cref="CoreRoundWorld"/>'s stalemate half: the wipe
/// (<c>RemoveAllProjectiles</c>, tf_gamerules.cpp:4034-4040;
/// <c>RemoveAllBuildings</c>, :4045-4077) and the health kits
/// (<c>SetupOnStalemateStart</c> :5503-5513, <c>SetupOnStalemateEnd</c>
/// :5567-5579), over a real entity registry on a recording engine.
/// </summary>
public sealed class StalemateWorldTests
{
    private sealed class NoMapCleanup : IMapCleanup
    {
        public MapCleanupReport CleanUpMap(IMapCleanupFilter filter) => default;
    }

    private static CoreRoundWorld World(MapItemRig rig, FakeBuildings buildings)
    {
        rig.Services.AddInstance<ITFBuildings>(buildings);
        return new CoreRoundWorld(new NoMapCleanup(), rig.Registry.Directory, rig.Registry, rig.Engine,
                                  new RoundTimers(rig.Registry), rig.Services, new SourceSharp.Abi.RecordingLog());
    }

    /// <summary>Each named entity class answers its own handles to FindAllByClassname.</summary>
    private static void AnswerProjectilesWith(MapItemRig rig, params (string Class, EntityHandle Handle)[] found)
    {
        var byClass = found.GroupBy(f => f.Class).ToDictionary(g => g.Key, g => g.Select(f => f.Handle).ToArray());
        rig.Engine.ReturnsEachCall("Entities.FindAllByClassname",
            () => byClass.GetValueOrDefault((string)rig.Engine.Calls[^1].Arg(0)!, Array.Empty<EntityHandle>()));
    }

    [Fact]
    public void The_wipe_takes_every_projectile_in_the_air_and_destroys_every_standing_building()
    {
        var rig = new MapItemRig();
        var rocket = new EntityHandle(0x0020_0001u);
        var sticky = new EntityHandle(0x0020_0003u);
        AnswerProjectilesWith(rig, (TFProjectileEntityNames.Rocket, rocket), (TFProjectileEntityNames.Stickybomb, sticky));

        var buildings = new FakeBuildings();
        var buildingsRig = new BuildingsRig();
        var builder = buildingsRig.AddBuilder();
        var building = buildingsRig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(builder);
        buildings.Standing.Add(building);

        World(rig, buildings).RemoveProjectilesAndBuildings();

        var removed = rig.Engine.AllOf("Entities.Remove").Select(c => (EntityHandle)c.Arg(0)!).ToList();
        Assert.Equal([rocket, sticky], removed);
        Assert.True(building.IsDead);
    }

    [Fact]
    public void The_stalemate_disables_every_enabled_health_kit()
    {
        var rig = new MapItemRig();
        var full = rig.Spawn<HealthKit>(HealthKit.MapClass);
        var small = rig.Spawn<HealthKitSmall>(HealthKitSmall.SmallClass);

        World(rig, new FakeBuildings()).DisableWorldHealthKits();

        Assert.True(full.IsDisabled);
        Assert.True(small.IsDisabled);
    }

    [Fact]
    public void The_stalemate_end_restores_the_kits_it_disabled_and_only_those()
    {
        var rig = new MapItemRig();
        var enabled = rig.Spawn<HealthKit>(HealthKit.MapClass);
        var mapDisabled = rig.Spawn<HealthKitMedium>(HealthKitMedium.MediumClass);
        mapDisabled.SetDisabled(true);
        var world = World(rig, new FakeBuildings());

        world.DisableWorldHealthKits();
        Assert.True(enabled.IsDisabled);
        Assert.True(mapDisabled.IsDisabled);

        world.RestoreWorldHealthKits();

        // Stock records only the kits it disabled itself (:5507-5511): a kit
        // its mapper switched off stays off.
        Assert.False(enabled.IsDisabled);
        Assert.True(mapDisabled.IsDisabled);
    }
}
