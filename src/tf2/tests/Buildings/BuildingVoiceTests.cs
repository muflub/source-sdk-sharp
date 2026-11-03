using SourceSharp.Abi;

using TF2.Server.Buildings;
using TF2.Server.Voice;
using TF2.Tests.Entities;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The buildings' voice (tf_obj.cpp): the builder's line as construction
/// starts (<c>MP_CONCEPT_BUILDING_OBJECT</c>, :1339) or as the carried one
/// goes back down (<c>MP_CONCEPT_REDEPLOY_BUILDING</c>, :1349), on the blow's
/// death (<c>MP_CONCEPT_LOST_OBJECT</c>, :2041-2045) and under the sapper
/// (<c>MP_CONCEPT_SPY_SAPPER</c>, :2620-2626) — each asked with the object
/// type word (<c>GetResponseRulesModifier</c>, :1293-1305) — and nowhere
/// else: the quiet dismantling speaks nothing.
/// </summary>
public sealed class BuildingVoiceTests
{
    private static (BuildingsRig Rig, TestBuilding Building, EntityHandle Builder) ABuilt()
    {
        var rig = new BuildingsRig();
        rig.Engine.Returns("Players.IsAlive", true)
                  .Returns("Entities.SetParent", true);   // the lift's mount, for the redeploy fact
        var builder = rig.AddBuilder();
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(builder);
        return (rig, building, builder);
    }

    [Fact]
    public void ConstructionSpeaksTheBuildingLine()
    {
        var (rig, building, builder) = ABuilt();

        rig.Clock.Now = 100f;
        building.StartBuilding();

        Assert.Equal(TFConceptSpeech.BuildingObject, rig.ConceptRules.Asked.Single().Concept);
        Assert.Contains(("objtype", "sentrygun"), rig.ConceptRules.Words);
        Assert.Contains(rig.Engine.AllOf("Sound.EntityEmitSound"),
            c => c!.Arg(0) is EntityHandle h && h == builder);
    }

    [Fact]
    public void MerelyPlacingTheBlueprintSpeaksNothing()
    {
        var (rig, _, _) = ABuilt();

        Assert.Empty(rig.ConceptRules.Asked);
    }

    [Fact]
    public void TheCarryDeploySpeaksTheRedeployLine()
    {
        var (rig, building, _) = ABuilt();

        building.StartRedeploy(Vector.Zero, Vector.Zero, 1f);

        Assert.Equal(TFConceptSpeech.RedeployBuilding, rig.ConceptRules.Asked.Single().Concept);
        Assert.Contains(("objtype", "sentrygun"), rig.ConceptRules.Words);
    }

    [Fact]
    public void TheFatalBlowTellsTheBuilderTheLoss()
    {
        var (rig, building, builder) = ABuilt();
        rig.Clock.Now = 100f;
        building.StartBuilding();
        rig.ConceptRules.Asked.Clear();

        BuildingsRig.Hit(building, EntityHandle.None, 5000f, DamageType.Bullet);

        Assert.Equal(TFConceptSpeech.LostObject, rig.ConceptRules.Asked.Single().Concept);
        Assert.Contains(("objtype", "sentrygun"), rig.ConceptRules.Words);
        Assert.Contains(rig.Engine.AllOf("Sound.EntityEmitSound"),
            c => c!.Arg(0) is EntityHandle h && h == builder);
    }

    [Fact]
    public void DismantlingTheBuildingSpeaksNothing()
    {
        var (rig, building, _) = ABuilt();
        rig.Clock.Now = 100f;
        building.StartBuilding();
        rig.ConceptRules.Asked.Clear();

        building.DestroyObject();

        Assert.Empty(rig.ConceptRules.Asked);
    }

    [Fact]
    public void TheSappedBuildingTellsItsBuilder()
    {
        var (rig, building, builder) = ABuilt();
        rig.Clock.Now = 100f;
        building.StartBuilding();
        rig.ConceptRules.Asked.Clear();

        building.OnAddSapper(rig.NewHandle());

        Assert.Equal(TFConceptSpeech.SpySapper, rig.ConceptRules.Asked.Single().Concept);
        Assert.Contains(("objtype", "sentrygun"), rig.ConceptRules.Words);
        Assert.Contains(rig.Engine.AllOf("Sound.EntityEmitSound"),
            c => c!.Arg(0) is EntityHandle h && h == builder);
    }
}
