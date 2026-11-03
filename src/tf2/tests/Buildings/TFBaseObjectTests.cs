using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Server.Buildings;
using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Shared;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// A building to hold the base up: the shared flow with a sentry's object
/// table row, and none of the sentry's own business.
/// </summary>
[ScriptedEntity(TestBuilding.MapClass, ScriptedEntityKind.Logical)]
public sealed class TestBuilding : TFBaseObject
{
    public const string MapClass = "obj_test_building";

    public TestBuilding(ITFMapPlayers players, ITFObjectAnnouncer announcer, IEnginePrecache precache,
                        IEngineEntityRender render, ITFAmmoPurse purse, TFPlayerCarriers? carriers = null,
                        TF2.Server.Voice.TFConceptSpeech? speech = null)
        : base(players, announcer, precache, render, purse, carriers, speech) { }

    public override TFObjectType Type => TFObjectType.Sentrygun;
}

public class TFBaseObjectTests
{
    private static (BuildingsRig Rig, TestBuilding Building, EntityHandle Builder) APlacedBuilding()
    {
        var rig = new BuildingsRig();
        var builder = rig.AddBuilder();
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(builder);
        return (rig, building, builder);
    }

    private static (BuildingsRig Rig, TestBuilding Building, EntityHandle Builder) ABuildingUnderConstruction(float at = 100f)
    {
        var (rig, building, builder) = APlacedBuilding();
        rig.Clock.Now = at;
        building.StartBuilding();
        return (rig, building, builder);
    }

    [Fact]
    public void APlacedBuildingIsOnItsBuildersTeam()
    {
        var rig = new BuildingsRig();
        var builder = rig.AddBuilder(TFTeam.Blue);
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);

        building.StartPlacement(builder);

        Assert.Equal((int)TFTeam.Blue, building.Team);
        Assert.Equal(builder, building.Builder);
        Assert.True(building.IsPlacing);
    }

    [Fact]
    public void ConstructionStartsAtStocksStartingHealth()
    {
        var (_, building, _) = ABuildingUnderConstruction();

        Assert.Equal(1, building.Health);   // OBJECT_CONSTRUCTION_STARTINGHEALTH: 0.1 absolute, its ceil
        Assert.True(building.IsBuilding);
        Assert.False(building.IsPlacing);
    }

    [Fact]
    public void ConstructionAddsToWhatTheBlowsLeft()
    {
        var (rig, building, _) = ABuildingUnderConstruction();

        rig.Clock.Now += 0.1f;
        building.OnThink();                     // one construction tick climbs
        int climbed = building.Health;

        Assert.Equal(1, BuildingsRig.Hit(building, EntityHandle.None, 1f, DamageType.Bullet));
        Assert.Equal(climbed - 1, building.Health);

        rig.Clock.Now += 0.1f;
        building.OnThink();                     // the next tick

        Assert.True(building.Health > climbed - 1);   // construction added to the wounded value
        Assert.True(building.Health <= climbed);      // and did not rewrite the wound away
    }

    [Fact]
    public void ConstructionRaisesHealthAsTheClockClimbs()
    {
        var (rig, building, _) = ABuildingUnderConstruction();

        rig.Clock.Now += 5f;   // half of the sentry's ten second build
        building.OnThink();

        Assert.Equal(75, building.Health);   // the start 1 plus half the remaining 149, floors whole
    }

    [Fact]
    public void ABuildingFinishesAtItsBuildTimeAtFullHealth()
    {
        var (rig, building, _) = ABuildingUnderConstruction();

        rig.Clock.Now += 10f;
        building.OnThink();

        Assert.True(building.IsBuilt);
        Assert.False(building.IsBuilding);
        Assert.Equal(150, building.Health);
        Assert.Contains("built Sentrygun 0", rig.Announcer.Said);
    }

    [Fact]
    public void APlacedBuildingRefusesAllDamage()
    {
        var (rig, building, builder) = APlacedBuilding();
        var enemy = rig.AddBuilder(TFTeam.Blue);

        Assert.Equal(0, BuildingsRig.Hit(building, enemy, 60, DamageType.Bullet));
        Assert.Equal(0, building.Health);
        Assert.False(building.IsDead);
    }

    [Fact]
    public void TheBuildersOwnTeamRefusesToHurtTheBuilding()
    {
        var (rig, building, builder) = ABuildingUnderConstruction();

        Assert.Equal(0, BuildingsRig.Hit(building, builder, 60, DamageType.Bullet));
        Assert.Equal(1, building.Health);
    }

    [Fact]
    public void AnEnemyShotWearsTheBuildingDownToItsDeath()
    {
        var (rig, building, _) = ABuildingUnderConstruction();
        var enemy = rig.AddBuilder(TFTeam.Blue);

        Assert.Equal(100, BuildingsRig.Hit(building, enemy, 100, DamageType.Bullet));
        Assert.Equal(0, building.Health);   // the shell clamps at zero and kills it, not the subtraction

        Assert.True(building.IsDead);
        Assert.Contains("destroyed Sentrygun 0", rig.Announcer.Said);
    }

    [Fact]
    public void DestroyTellsTheBuilderAndRemoves()
    {
        var (rig, building, _) = ABuildingUnderConstruction();
        rig.Clock.Now += 10f;
        building.OnThink();

        building.DestroyObject();

        Assert.True(building.IsDead);
        Assert.Contains("destroyed Sentrygun 0", rig.Announcer.Said);
    }

    [Fact]
    public void ADeadBuildingTakesNoMoreBlows()
    {
        var (rig, building, _) = ABuildingUnderConstruction();
        var enemy = rig.AddBuilder(TFTeam.Blue);
        BuildingsRig.Hit(building, enemy, 60, DamageType.Bullet);

        building.DestroyObject();

        // The killing blow announced once; the destroy on a corpse says nothing.
        Assert.Contains("destroyed Sentrygun 0", rig.Announcer.Said);
        Assert.Single(rig.Announcer.Said, s => s.StartsWith("destroyed", StringComparison.Ordinal));
    }

    [Fact]
    public void ADetonationIsTheKillNotTheQuietRemoval()
    {
        var (rig, building, _) = ABuildingUnderConstruction();
        rig.Clock.Now += 10f;
        building.OnThink();
        Assert.True(building.IsBuilt);

        building.DetonateObject();   // tf_obj.cpp:804-809, Killed by itself

        Assert.True(building.IsDead);
        // The kill path clears what DestroyObject leaves standing: the
        // quiet removal is not a death, and a no-build area's sweep is.
        Assert.False(building.IsBuilt);
    }

    [Fact]
    public void TheBaseThinkKeepsTheStockPeriod()
    {
        var (_, building, _) = ABuildingUnderConstruction();

        // The clock sits at 100, so the remaining delay is 100.1 - 100 in floats.
        Assert.Equal(TFBaseObject.BaseObjectThinkDelay, building.NextThinkDelay(), 3);
    }

    [Fact]
    public void ADestroyedBuildingStopsThinking()
    {
        var (rig, building, _) = ABuildingUnderConstruction();

        building.DestroyObject();
        building.OnThink();

        // The destroy cancels the think and the dead guard refuses the call:
        // a corpse schedules nothing, where a live think keeps the 0.1 s beat.
        Assert.True(building.NextThinkDelay() < 0f,
                    $"a destroyed building kept a think booking ({building.NextThinkDelay()} s)");
    }

    // ---- the life in the world: the models --------------------------------

    [Fact]
    public void SpawningPrecachesTheStatesModelsAndWearsTheBlueprint()
    {
        var rig = new BuildingsRig();
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);

        IEnumerable<string> precached = rig.Engine.AllOf("Precache.PrecacheModel")
                                                 .Select(c => c.ArgAs<string>(0));
        Assert.Contains(TFObjectModels.Placement(TFObjectType.Sentrygun, 0), precached);
        Assert.Contains(TFObjectModels.Building(TFObjectType.Sentrygun), precached);
        Assert.Contains(TFObjectModels.Finished(TFObjectType.Sentrygun), precached);

        EngineCall worn = Assert.Single(rig.Engine.AllOf("EntityRender.SetModel"),
                                        c => c.ArgAs<EntityHandle>(0) == building.Handle);
        Assert.Equal("models/buildables/sentry1_blueprint.mdl", worn.ArgAs<string>(1));
    }

    [Fact]
    public void TheTeleportersBlueprintNamesItsEnd()
    {
        var rig = new BuildingsRig();
        var builder = rig.AddBuilder(TFTeam.Red);
        var teleporter = rig.Spawn<TFTeleporter>(TFTeleporter.MapClass);

        teleporter.StartPlacement(builder);
        teleporter.SetObjectMode(1);

        // The spawn's blueprint was the entrance's; the mode names the exit's.
        Assert.Equal("models/buildables/teleporter_blueprint_exit.mdl", LastModelWorn(rig, teleporter));
    }

    [Fact]
    public void ConstructionWearsTheBuildModelAndTheFinishTheLevelModel()
    {
        var (rig, building, _) = ABuildingUnderConstruction();

        Assert.Equal(TFObjectModels.Building(TFObjectType.Sentrygun), LastModelWorn(rig, building));

        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        building.OnThink();

        Assert.Equal(TFObjectModels.Finished(TFObjectType.Sentrygun), LastModelWorn(rig, building));
    }

    [Fact]
    public void ConstructionRaisesTheObjectSolidOnItsHull()
    {
        var (rig, building, _) = ABuildingUnderConstruction();

        // Bounds land before the solidity, so the partition insert carries
        // the box (ScriptedEntity's rule, stock's SetSolidToPlayers world).
        var (mins, maxs) = TFObjectModels.Hull(TFObjectType.Sentrygun);
        EngineCall bounds = Assert.Single(rig.Engine.AllOf("Entities.SetBounds"),
                                          c => c.ArgAs<EntityHandle>(0) == building.Handle);
        Assert.Equal(mins, bounds.ArgAs<Vector>(1));
        Assert.Equal(maxs, bounds.ArgAs<Vector>(2));

        // SOLID_BBOX with no flags: every player is stopped, teammate and
        // enemy alike — the default group's rule (baseobject_shared.cpp:464-496).
        EngineCall solid = Assert.Single(rig.Engine.AllOf("Entities.SetSolidity"),
                                         c => c.ArgAs<EntityHandle>(0) == building.Handle);
        Assert.Equal(SolidType.BoundingBox, solid.ArgAs<SolidType>(1));
        Assert.Equal(SolidFlags.None, solid.ArgAs<SolidFlags>(2));
    }

    [Fact]
    public void ConstructionPlaysTheAssemblingSequenceAtHalfTheBuildRate()
    {
        var rig = new BuildingsRig();
        rig.Engine.Returns("EntityRender.LookupSequenceByActivity", 9);
        var builder = rig.AddBuilder();
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(builder);

        building.StartBuilding();

        EngineCall asked = Assert.Single(rig.Engine.AllOf("EntityRender.LookupSequenceByActivity"),
                                         c => c.ArgAs<EntityHandle>(0) == building.Handle);
        Assert.Equal("ACT_OBJ_ASSEMBLING", asked.ArgAs<string>(1));

        EngineCall played = Assert.Single(rig.Engine.AllOf("EntityRender.PlaySequence"),
                                          c => c.ArgAs<EntityHandle>(0) == building.Handle);
        Assert.Equal(9, played.ArgAs<int>(1));
        Assert.Equal(0.5f, played.ArgAs<float>(2), 3);   // the anim is authored for one builder
        Assert.True(played.ArgAs<bool>(3));
    }

    [Fact]
    public void TheRedeploysBuildRateCarriesIntoThePlayback()
    {
        var rig = new BuildingsRig();
        rig.Engine.Returns("EntityRender.LookupSequenceByActivity", 9);
        var builder = rig.AddBuilder();
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(builder);

        building.StartBuilding(2f);   // the redeploy's +2 sentry

        EngineCall played = Assert.Single(rig.Engine.AllOf("EntityRender.PlaySequence"),
                                          c => c.ArgAs<EntityHandle>(0) == building.Handle);
        Assert.Equal(1f, played.ArgAs<float>(2), 3);
    }

    [Fact]
    public void TheSapperWearsNoModelAndIsNotDrawn()
    {
        var rig = new BuildingsRig();
        var sapper = rig.Spawn<TFSapper>(TFSapper.MapClass);

        Assert.DoesNotContain(rig.Engine.AllOf("EntityRender.SetModel"),
                               c => c.ArgAs<EntityHandle>(0) == sapper.Handle);
        Assert.False(rig.Engine.Last("EntityRender.SetVisible")!.Value.ArgAs<bool>(1));
    }

    private static string LastModelWorn(BuildingsRig rig, ScriptedEntity building)
        => rig.Engine.AllOf("EntityRender.SetModel")
                     .Where(c => c.ArgAs<EntityHandle>(0) == building.Handle)
                     .Last()
                     .ArgAs<string>(1);
}
