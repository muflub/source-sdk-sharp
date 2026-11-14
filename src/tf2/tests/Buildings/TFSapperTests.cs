using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server;
using TF2.Server.Buildings;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
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
        sapper.StartPlacement(spy);   // no SetObjectMode: the ghost's flow never calls one

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
        rig.Convars.Declared<ObjSapperAmountConVar>().Bind(-1, "obj_sapper_amount", "50");

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

    /// <summary>Stock's drain walk (tf_obj_sapper.cpp:493-495): the builder's active item
    /// scales the drain by <c>mult_sapper_damage</c> — the double pays double over the
    /// second the plain rate would pay its 25.</summary>
    [Fact]
    public void TheBuildersHeldItemScalesTheDrain()
    {
        var (rig, _, sapper) = ASappedBuilding();
        rig.Owners.Held[0] = "tf_weapon_builder";
        rig.Owners.Items[0] = rig.AttributeItem("mult_sapper_damage", AttributeDescriptionFormat.Percentage, 2f);

        rig.Clock.Now += 1f;
        sapper.OnThink(TFSapper.SapperThinkContext);

        EngineCall blow = Assert.Single(rig.Engine.AllOf("Damage.TakeDamage"));
        Assert.Equal(50f, blow.ArgAs<float>(2));   // the doubled 25/s
    }

    /// <summary>Stock stamps the recorder's kind when the builder's item degenerates the
    /// building (tf_obj_sapper.cpp:515-519): the blow rides the scope so the kill feed can
    /// name it — the kind, no weapon of its own.</summary>
    [Fact]
    public void ARecodingDeathCarriesTheRecorderKindOnItsBlow()
    {
        var (rig, _, sapper) = ASappedBuilding();
        rig.Owners.Held[0] = "tf_weapon_builder";
        rig.Owners.Items[0] = rig.AttributeItem("sapper_degenerates_buildings", AttributeDescriptionFormat.Additive, 1f);

        (TFDamageCustom, TFWeaponId, string)? scope = null;
        rig.Engine.WhenCalled("Damage.TakeDamage", _ =>
        {
            rig.Scopes.TryCurrent(out var custom, out var weapon, out var name);
            scope = (custom, weapon, name);
        });

        rig.Clock.Now += 1f;
        sapper.OnThink(TFSapper.SapperThinkContext);

        Assert.Single(rig.Engine.AllOf("Damage.TakeDamage"));
        Assert.Equal((TFDamageCustom.SapperRecorderDeath, TFWeaponId.None, ""), scope);
    }

    /// <summary>Stock's Wheatley machine (tf_weapon_builder.cpp:584-589, :751-771): only an
    /// item holding <c>sapper_voice_pak</c> == 1 speaks a placement, and only to its owner —
    /// the rig's coin answers its floor on both draws, the PW line.</summary>
    [Fact]
    public void APakSapperSpeaksItsLineToItsOwnerWhenPlaced()
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

        rig.Owners.Held[0] = "tf_weapon_builder";
        rig.Owners.Items[0] = rig.AttributeItem("sapper_voice_pak", AttributeDescriptionFormat.Additive, 1f);

        var sapper = rig.Spawn<TFSapper>(TFSapper.MapClass);
        sapper.StartPlacement(spy);
        sapper.AttachTo(building);
        sapper.StartBuilding();
        sapper.OnThink();   // the instant build: OnGoActive speaks

        // The building's own concept words ride the seam too; the pak's line is the last.
        EngineCall speak = rig.Engine.AllOf("Sound.EntityEmitSound").Last();
        Assert.Equal(spy, speak.ArgAs<EntityHandle>(0));
        Assert.Equal("PSap.AttachedPW", speak.ArgAs<string>(1));
    }

    /// <summary>The gate before the machine (:586): a plain held item, however present,
    /// keeps the placement silent.</summary>
    [Fact]
    public void APlainSapperPlacementSpeaksNothing()
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

        rig.Owners.Held[0] = "tf_weapon_builder";
        rig.Owners.Items[0] = rig.AttributeItem("mult_sapper_damage", AttributeDescriptionFormat.Percentage, 2f);

        var sapper = rig.Spawn<TFSapper>(TFSapper.MapClass);
        sapper.StartPlacement(spy);
        sapper.AttachTo(building);
        sapper.StartBuilding();
        sapper.OnThink();

        // The building's concept words still ride the seam; what must stay absent
        // is the pak's line for the plain item.
        Assert.DoesNotContain("PSap.AttachedPW",
                              rig.Engine.AllOf("Sound.EntityEmitSound").Select(c => c.ArgAs<string>(1)));
    }

    /// <summary>Stock's sapper-crit arm (tf_obj.cpp:269-283): the building that goes
    /// while a live sapper rides it pays the sapper's owner one stored crit before
    /// the riding sapper is taken down itself.</summary>
    [Fact]
    public void ARemovedBuildingPaysItsSappersOwnerACrit()
    {
        var rig = new BuildingsRig();
        var bucket = new RecordingCrits();
        rig.Services.AddInstance<ITFRevengeCrits>(bucket);
        EntityHandle engineer = rig.AddBuilder(TFTeam.Red);
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(engineer);
        rig.Clock.Now = 100f;
        building.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        building.OnThink();

        var sapper = rig.Spawn<TFSapper>(TFSapper.MapClass);
        sapper.StartPlacement(spy);
        sapper.AttachTo(building);
        sapper.StartBuilding();
        sapper.OnThink();   // live and riding

        building.OnKilled(rig.NewHandle());

        Assert.Equal(spy, Assert.Single(bucket.Granted));   // the owner, before the take-down
        Assert.True(sapper.IsDead);
    }

    /// <summary>The bucket as the buildings' facts see it: who was paid.</summary>
    private sealed class RecordingCrits : ITFRevengeCrits
    {
        public readonly List<EntityHandle> Granted = new();

        public void Grant(EntityHandle owner) => Granted.Add(owner);

        public void SpendOnShot(int ownerIndex) { }
    }
}
