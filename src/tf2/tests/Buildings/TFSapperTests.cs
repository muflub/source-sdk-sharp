using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server;
using TF2.Server.Buildings;
using TF2.Shared;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The sapper object (tf_obj_sapper.cpp): the attach, the drain, the death
/// with its building, and the stumble it leaves behind.
/// </summary>
public class TFSapperTests
{
    /// <summary>
    /// A standing sentry-table building of the red team, and the blue spy's
    /// sapper planted on it — finished, attached and draining.
    /// </summary>
    private static (BuildingsRig Rig, TestBuilding Building, TFSapper Sapper) ASappedBuilding(float at = 100f)
    {
        var rig = new BuildingsRig();
        EntityHandle engineer = rig.AddBuilder(TFTeam.Red);
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(engineer);
        rig.Clock.Now = at;
        building.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        building.OnThink();   // finished and active

        var sapper = rig.Spawn<TFSapper>(TFSapper.MapClass);
        sapper.StartPlacement(spy);
        sapper.AttachTo(building);
        sapper.StartBuilding();
        sapper.OnThink();     // the instant build: goes active attached

        return (rig, building, sapper);
    }

    [Fact]
    public void TheSapperAttachesToTheBuildingItWasPlantedOn()
    {
        var (rig, building, sapper) = ASappedBuilding();

        Assert.True(building.HasSapper);
        Assert.True(sapper.IsBuilt);
        Assert.False(sapper.IsPlacing);
    }

    [Fact]
    public void TheSapperCarriesSapperMaxHealth()
    {
        var (_, _, sapper) = ASappedBuilding();

        Assert.Equal(TFSapper.SapperMaxHealth, sapper.Health);
    }

    [Fact]
    public void TheSapperWearsThePlacementBodyPlacingAndThePlacedBodyRiding()
    {
        var rig = new BuildingsRig();
        EntityHandle engineer = rig.AddBuilder(TFTeam.Red);
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(engineer);
        rig.Clock.Now = 100f;
        building.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        building.OnThink();

        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        var sapper = rig.Spawn<TFSapper>(TFSapper.MapClass);
        sapper.StartPlacement(spy);
        sapper.SetObjectMode(0);

        // The ghost's body, Spawn's (tf_obj_sapper.cpp:88).
        Assert.Equal("models/buildables/sapper_placement.mdl", LastModelWorn(rig, sapper));

        sapper.AttachTo(building);
        sapper.StartBuilding();
        sapper.OnThink();   // the instant build

        // The attached body, OnGoActive's (tf_obj_sapper.cpp:265).
        Assert.Equal("models/buildables/sapper_placed.mdl", LastModelWorn(rig, sapper));
    }

    private static string LastModelWorn(BuildingsRig rig, ScriptedEntity entity)
        => rig.Engine.AllOf("EntityRender.SetModel")
                     .Where(c => c.ArgAs<EntityHandle>(0) == entity.Handle)
                     .Last()
                     .ArgAs<string>(1);

    [Fact]
    public void TheDrainRateConvarScalesTheSapping()
    {
        // Stock pays the seconds since the last think times obj_sapper_amount
        // (tf_obj_sapper.cpp:493-494).
        var (rig, _, sapper) = ASappedBuilding();
        rig.Convars.Get<ObjSapperAmountConVar>()!.Bind(-1, "obj_sapper_amount", "50");

        rig.Clock.Now += 1f;
        sapper.OnThink(TFSapper.SapperThinkContext);

        EngineCall blow = Assert.Single(rig.Engine.AllOf("Damage.TakeDamage"));
        Assert.Equal(50f, blow.ArgAs<float>(2));   // 50/s over the second
    }

    [Fact]
    public void TheSapperDrainsItsParentThroughTheDamagePath()
    {
        var (rig, building, sapper) = ASappedBuilding();

        rig.Clock.Now += 0.1f;
        sapper.OnThink(TFSapper.SapperThinkContext);

        EngineCall blow = Assert.Single(rig.Engine.AllOf("Damage.TakeDamage"));
        Assert.Equal(building.Handle, blow.ArgAs<EntityHandle>(0));
        Assert.Equal(sapper.Handle, blow.ArgAs<EntityHandle>(1));
        Assert.Equal(2f, blow.ArgAs<float>(2));   // 25/s over the tenth
        Assert.Equal(DamageType.Crush, blow.ArgAs<DamageType>(3));
    }

    [Fact]
    public void TheSapperCarriesTheFractionalDamageBetweenThinks()
    {
        var (rig, _, sapper) = ASappedBuilding();

        rig.Clock.Now += 0.1f;
        sapper.OnThink(TFSapper.SapperThinkContext);   // 2.5: 2 dealt, 0.5 owed
        rig.Clock.Now += 1f;
        sapper.OnThink(TFSapper.SapperThinkContext);   // 25 + the half owed: 25 dealt

        Assert.Equal(2, rig.Engine.AllOf("Damage.TakeDamage").Count());
        Assert.Equal(25f, rig.Engine.AllOf("Damage.TakeDamage").Last().ArgAs<float>(2));
    }

    [Fact]
    public void TheSapperDiesWithItsBuilding()
    {
        var (rig, building, sapper) = ASappedBuilding();

        // No sapper think: the death is the parent's removal taking its
        // child along — stock's orphaned-children cleanup at removal
        // (baseentity.cpp:2575-2585), which our sapper, no engine child,
        // has as an explicit cascade.
        building.OnKilled(rig.NewHandle());

        Assert.True(sapper.IsDead);
    }

    [Fact]
    public void TheDeadParentsSapperSurvivesItsThink()
    {
        var rig = new BuildingsRig();
        EntityHandle engineer = rig.AddBuilder(TFTeam.Red);
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(engineer);
        rig.Clock.Now = 100f;
        building.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        building.OnThink();

        // Attached but never activated: nothing registered the pair with
        // the building, so no removal cascade reaches it. Stock's
        // SapperThink returns on a dead or dying parent without destroying
        // it (tf_obj_sapper.cpp:486-488): the sapper stands by.
        var sapper = rig.Spawn<TFSapper>(TFSapper.MapClass);
        sapper.StartPlacement(spy);
        sapper.AttachTo(building);

        building.OnKilled(rig.NewHandle());
        sapper.OnThink(TFSapper.SapperThinkContext);

        Assert.False(sapper.IsDead);
    }

    [Fact]
    public void RemovingTheSapperTellsTheBuilding()
    {
        var (rig, building, sapper) = ASappedBuilding();

        sapper.DestroyObject();

        Assert.False(building.HasSapper);
        Assert.False(building.IsDisabled);
    }

    [Fact]
    public void TheBuildingIsDisabledWhileSapped()
    {
        var (rig, building, _) = ASappedBuilding();

        Assert.True(building.IsDisabled);
    }

    [Fact]
    public void AKilledSapperLeavesItsBuildingStumblingForTheRemoveDisableTime()
    {
        var (rig, building, sapper) = ASappedBuilding();

        sapper.OnKilled(rig.NewHandle());

        Assert.True(building.IsDisabled);
        Assert.True(building.IsPlasmaDisabled);

        rig.Clock.Now += TFSapper.RemoveDisableTime;
        Assert.False(building.IsDisabled);
        Assert.False(building.IsPlasmaDisabled);
    }
}
