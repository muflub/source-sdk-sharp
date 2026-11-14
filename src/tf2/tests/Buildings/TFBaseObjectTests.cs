using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Server.Buildings;
using TF2.Server.Combat;
using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Tests.Entities.Items;   // FakeNotices, the pack's notices
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
                        IEngineEntityRender render, ITFAmmoPurse purse, TFPlayerCarriers carriers,
                        TF2.Server.Voice.TFConceptSpeech speech, ITFWeaponOwners owners,
                        AttributeHooks hooks, TFDamageScopes scopes, ITFRevengeCrits crits)
        : base(players, announcer, precache, render, purse, carriers, speech, owners, hooks, scopes, crits) { }

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

    /// <summary>
    /// The services the pack's own constructor asks the container for: the
    /// three throw bands (stock defaults through the rig's convar registry),
    /// the pickup notices and the kit stat. Without them the registry builds
    /// no instance behind the pack's handle.
    /// </summary>
    private static void ServePack(BuildingsRig rig)
    {
        rig.Services.AddInstance<ITFPickupNotices>(new FakeNotices());
        rig.Services.AddInstance<ITFPlayerStats>(new TFPlayerStats());
        rig.Services.AddInstance(rig.Convars.Declared<TF2.Server.TFWeaponRagdollVelocityMinConVar>());
        rig.Services.AddInstance(rig.Convars.Declared<TF2.Server.TFWeaponRagdollVelocityMaxConVar>());
        rig.Services.AddInstance(rig.Convars.Declared<TF2.Server.TFWeaponRagdollMaxSpeedConVar>());
        rig.Services.AddInstance(rig.Convars.Declared<TF2.Server.TFObjGibVelocityMinConVar>());
        rig.Services.AddInstance(rig.Convars.Declared<TF2.Server.TFObjGibVelocityMaxConVar>());
        rig.Services.AddInstance(rig.Convars.Declared<TF2.Server.TFObjGibMaxSpeedConVar>());
    }

    /// <summary>
    /// The break list the dying building's packs are made from: <paramref name="count"/>
    /// pieces, named by index so a fact can tell the packs apart by model.
    /// </summary>
    private static void ArmBreakPieces(BuildingsRig rig, int count)
    {
        rig.Engine.Returns("EntityRender.GetModelName", "models/buildables/sentry1.mdl");
        rig.Engine.Returns("Precache.BreakPieceCount", count);

        var pieces = new (string Model, BreakPiece Piece)[count];
        for (int i = 0; i < count; i++)
            pieces[i] = ($"pieces/gib{i}.mdl", new BreakPiece());

        rig.Engine.Returns("Precache.BreakPieceAt.pieces",
                           (IReadOnlyList<(string Model, BreakPiece Piece)>)pieces);
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

    /// <summary>
    /// The wire's copy of the build state is what the client's shadow arm
    /// reads (<c>GetShadowRenderBounds</c>, c_obj_sentrygun.cpp:588), so it
    /// must move with <see cref="TFBaseObject.IsPlacing"/> and not only with
    /// the server's own field.
    /// </summary>
    [Fact]
    public void ThePlacingFlagRidesTheWireWithThePrivateOne()
    {
        var (_, building, _) = APlacedBuilding();

        Assert.True(building.State.Placing,
            "a blueprint is placing, and the wire says so for the client's shadow");

        building.StartBuilding();

        Assert.False(building.State.Placing,
            "a built sentry is no longer placing on the wire, so its shadow leaves the hull arm");
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

    /// <summary>The builder's manager holding one named attribute, as the hooks' walk reads it.</summary>
    private static void Arms(BuildingsRig rig, EntityHandle builder, string attribute, float value,
                             AttributeDescriptionFormat format = AttributeDescriptionFormat.Additive)
    {
        var manager = new AttributeContainer(null, AttributeProviderType.Generic);
        manager.Set(AttributeValue.OfFloat(
            new AttributeDefinition(6060, "attr 6060", attribute, format,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        rig.Owners.Managers[builder.EntryIndex] = manager;
    }

    [Fact]
    public void TheBuildersSentryBuildRateAttrClimbsFaster()
    {
        var (rig, building, builder) = ABuildingUnderConstruction();
        Arms(rig, builder, "sentry_build_rate_multiplier", 1f);   // GetObjectBuildSpeedMultiplier's +1

        rig.Clock.Now += 5f;   // half the sentry's ten seconds, at twice the rate
        building.OnThink();

        // The multiplier ask (tf_obj.cpp:2208) rides every construction tick,
        // so half the clock is the whole build where the plain sentry (the
        // fact above) sits at half health still building.
        Assert.True(building.IsBuilt);
        Assert.Equal(150, building.Health);
    }

    [Fact]
    public void AMultiplyingBuildRateAttrClimbsFasterToo()
    {
        // The walk starts from stock's 1 (tf_player.cpp:8239): a percentage-format
        // item's two times is the same plus one (:8259), not zero.
        var (rig, building, builder) = ABuildingUnderConstruction();
        Arms(rig, builder, "sentry_build_rate_multiplier", 2f, AttributeDescriptionFormat.Percentage);

        rig.Clock.Now += 5f;
        building.OnThink();

        Assert.True(building.IsBuilt);
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

    /// <summary>
    /// The packs a kill will make: one instance per break piece, registered
    /// up front, handed out by <c>Entities.Create</c> in piece order.
    /// </summary>
    private static TFAmmoPack[] QueuePackHandles(BuildingsRig rig, int count)
    {
        var packs = new TFAmmoPack[count];
        for (int i = 0; i < count; i++)
            packs[i] = rig.Spawn<TFAmmoPack>(TFAmmoPack.Class);

        var queue = new Queue<EntityHandle>(packs.Select(p => p.Handle));
        rig.Engine.ReturnsEachCall("Entities.Create", () => queue.Dequeue());
        return packs;
    }

    /// <summary>The last model one pack was given — what it ends the kill wearing.</summary>
    private static EngineCall LastModelFor(BuildingsRig rig, EntityHandle handle)
        => rig.Engine.AllOf("EntityRender.SetModel").Last(c => c.ArgAs<EntityHandle>(0) == handle);

    /// <summary>
    /// <c>CreateObjectGibs</c> (:2244-2274): the destroyed building throws one
    /// pack per break piece of the model it wears when it dies, and the metal —
    /// half the cost (:2254-2255), split evenly over the pieces with the int
    /// remainder on the first (:2258-2259, :2272) — adds up across them.
    /// </summary>
    [Fact]
    public void AKilledBuildingDropsOnePackPerBreakPieceAndTheMetalAddsUp()
    {
        var (rig, building, _) = ABuildingUnderConstruction();
        ServePack(rig);
        ArmBreakPieces(rig, 3);
        var packs = QueuePackHandles(rig, 3);

        building.OnKilled(rig.NewHandle());   // Killed → Explode → the gibs

        Assert.Equal(3, rig.Engine.AllOf("Entities.Create").Count());   // one pack per piece
        Assert.Equal(TFAmmoPack.Class, (rig.Engine.Last("Entities.Create") ?? default).ArgAs<string>(0));

        // BuildGibList's defaults, the pair SetModel hands it (tf_obj.cpp:3552).
        EngineCall piece = rig.Engine.Last("Precache.BreakPieceAt") ?? default;
        Assert.Equal(1f, piece.ArgAs<float>(2), 3);
        Assert.Equal(CollisionGroup.None, piece.ArgAs<CollisionGroup>(3));

        // The sentry's 130 cost halves to 65; over three pieces that is
        // 21 each with the leftover 2 on the first (:2272: "make sure we
        // drop all metal include left over from int math").
        Assert.Equal(23, packs[0].AmmoMetal);
        Assert.Equal(21, packs[1].AmmoMetal);
        Assert.Equal(21, packs[2].AmmoMetal);
        Assert.Equal(65, packs.Sum(p => p.AmmoMetal));   // half the build cost, nothing lost
    }

    /// <summary>
    /// A mini's packs carry nothing (:2261-2266) but still each get the mini's
    /// 0.6 scale (:2317-2320), every pack one scale, not just the first.
    /// </summary>
    [Fact]
    public void TheMiniLeavesEmptyPacksScaledDown()
    {
        var (rig, building, _) = ABuildingUnderConstruction();
        building.MakeMiniBuilding();
        ServePack(rig);
        ArmBreakPieces(rig, 2);
        var packs = QueuePackHandles(rig, 2);

        building.OnKilled(rig.NewHandle());

        // The mini zeroes the split and the remainder alike (:2261-2266),
        // and every pack — not one shared — takes the 0.6 scale (:2317-2320).
        Assert.Equal(0, packs[0].AmmoMetal);
        Assert.Equal(0, packs[1].AmmoMetal);

        foreach (TFAmmoPack pack in packs)
        {
            EngineCall scale = rig.Engine.AllOf("EntityRender.SetModelScale")
                                         .Last(c => c.ArgAs<EntityHandle>(0) == pack.Handle);
            Assert.Equal(0.6f, scale.ArgAs<float>(1), 3);   // SetModelScale(0.6f), the stock value
        }
    }

    /// <summary>
    /// Each pack wears its PIECE's model, not the medium box, and the team's
    /// skin: red 0, blu 1 (:2309). The building's team, not the picker's.
    /// </summary>
    [Fact]
    public void ThePacksWearTheirPieceModelAndTheTeamsSkin()
    {
        // The blu arm: a blue builder, skin 1 on every pack.
        var rig = new BuildingsRig();
        var builder = rig.AddBuilder(TFTeam.Blue);
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(builder);
        ServePack(rig);
        ArmBreakPieces(rig, 2);
        var packs = QueuePackHandles(rig, 2);

        building.OnKilled(rig.NewHandle());

        // The last model each pack was given is its piece's, not ammopack_medium.
        Assert.Equal("pieces/gib0.mdl", LastModelFor(rig, packs[0].Handle).ArgAs<string>(1));
        Assert.Equal("pieces/gib1.mdl", LastModelFor(rig, packs[1].Handle).ArgAs<string>(1));

        foreach (TFAmmoPack pack in packs)
        {
            EngineCall skin = rig.Engine.AllOf("EntityRender.SetSkin")
                                        .Last(c => c.ArgAs<EntityHandle>(0) == pack.Handle);
            Assert.Equal(1, skin.ArgAs<int>(1));   // blu (TF_TEAM_BLUE) dresses skin 1 (:2309)
        }

        // The red arm: the helper's default builder is red, skin 0.
        var (redRig, redBuilding, _) = ABuildingUnderConstruction();
        ServePack(redRig);
        ArmBreakPieces(redRig, 1);
        var redPacks = QueuePackHandles(redRig, 1);

        redBuilding.OnKilled(redRig.NewHandle());

        EngineCall redSkin = redRig.Engine.AllOf("EntityRender.SetSkin")
                                          .Last(c => c.ArgAs<EntityHandle>(0) == redPacks[0].Handle);
        Assert.Equal(0, redSkin.ArgAs<int>(1));   // red dresses skin 0 (:2309)
    }

    /// <summary>
    /// The kill's packs, stock's <c>CreateAmmoPack</c> shape (tf_obj.cpp:2276-2285):
    /// the piece model is named on the entity BEFORE the spawn, so at the
    /// spawn's return each pack already wears its piece — the solver's object
    /// is born with the piece's hull, not the medium box's.
    /// </summary>
    [Fact]
    public void TheGibPackIsNamedItsPieceBeforeTheSpawnAndWearsItAtTheReturn()
    {
        var (rig, building, _) = ABuildingUnderConstruction();
        ServePack(rig);
        ArmBreakPieces(rig, 2);
        var packs = QueuePackHandles(rig, 2);

        building.OnKilled(rig.NewHandle());   // Killed → Explode → the gibs

        var calls = rig.Engine.Calls.ToList();
        for (int i = 0; i < packs.Length; i++)
        {
            EntityHandle h = packs[i].Handle;
            int named = calls.FindIndex(c => c.Member == "Entities.SetKeyValue"
                                             && c.ArgAs<EntityHandle>(0) == h
                                             && c.ArgAs<string>(1) == "model");
            int spawned = calls.FindIndex(c => c.Member == "Entities.Spawn"
                                               && c.ArgAs<EntityHandle>(0) == h);
            Assert.True(named >= 0 && spawned > named,
                        $"piece {i}'s model is named on the entity before the spawn");
            Assert.Equal($"pieces/gib{i}.mdl", calls[named].ArgAs<string>(2));

            // What the spawn found on the entity is what the pack wears at the return.
            Assert.Equal($"pieces/gib{i}.mdl", LastModelFor(rig, h).ArgAs<string>(1));
        }
    }

    [Fact]
    public void TheQuietRemovalDropsNoPack()
    {
        var (rig, building, _) = ABuildingUnderConstruction();

        building.DestroyObject();

        // DestroyObject (:814) is the quiet removal: no Explode, no gibs —
        // only a kill leaves metal behind.
        Assert.Null(rig.Engine.Last("Entities.Create"));
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

    /// <summary>
    /// mult_dmg_vs_buildings (stock's CALL_ATTRIB_HOOK_FLOAT_ON_OTHER behind
    /// the weapon-caused gate, tf_obj.cpp:1922-1927): the held weapon's item
    /// multiplier rides the health the object loses, and without the item the
    /// blow lands exactly as asked.
    /// </summary>
    [Fact]
    public void DamageVsBuildingsScalesAWeaponsHitOnABuilding()
    {
        // The plain arm first: no item, the blow is the damage asked.
        var (plainRig, plainBuilding, _) = ABuildingUnderConstruction();
        EntityHandle plainEnemy = plainRig.AddBuilder(TFTeam.Blue);
        Assert.Equal(40, BuildingsRig.Hit(plainBuilding, plainEnemy, 40f, DamageType.Bullet));

        // The hook arm: the held weapon's Percentage item scales it (the rig
        // answers GetIndex with one player index, as the blast facts do).
        var (rig, building, _) = ABuildingUnderConstruction();
        rig.Owners.Held[0] = "tf_weapon_shotgun";
        rig.Owners.Items[0] = rig.AttributeItem("mult_dmg_vs_buildings",
                                                AttributeDescriptionFormat.Percentage, 1.5f);
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);

        Assert.Equal(60, BuildingsRig.Hit(building, enemy, 40f, DamageType.Bullet));
        Assert.True(building.IsDead);   // the scaled blow is the one that kills: 1 health, 60 taken
    }

    /// <summary>
    /// The launcher's item rides the blast's scope as stock's
    /// <c>info.GetWeapon()</c> (<c>shared/takedamageinfo.h:49-52</c>): a rocket
    /// fired while the attributed item was up still scales off it after its
    /// owner switched to a plain weapon — stock asks the weapon that made the
    /// blow, not the one held when it lands (tf_obj.cpp:1922-1927).
    /// </summary>
    [Fact]
    public void TheLaunchingItemScalesTheBlowAfterItsOwnerSwitchesWeapons()
    {
        var (rig, building, _) = ABuildingUnderConstruction();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);

        // The held answer is the plain one — no item behind the row; the
        // scope's launcher carries the multiplier the launch had on it.
        rig.Owners.Held[0] = "";
        rig.Owners.Items[0] = rig.AttributeItem("mult_dmg_vs_buildings",
                                                AttributeDescriptionFormat.Percentage, 1.5f);

        using (rig.Scopes.Push(TFDamageCustom.None, TFWeaponId.Rocketlauncher,
                               "tf_weapon_rocketlauncher", rig.Owners.Items[0]))
            Assert.Equal(60, BuildingsRig.Hit(building, enemy, 40f, DamageType.Bullet));
    }

    /// <summary>
    /// The reverse: fired plain, switched to the attributed one — the blow
    /// lands unscaled, stock reading the launcher item that made it rather
    /// than what the hand holds at the landing (tf_obj.cpp:1922-1927).
    /// </summary>
    [Fact]
    public void APlainLaunchStaysPlainAfterItsOwnerSwitchesToAnAttributedWeapon()
    {
        var (rig, building, _) = ABuildingUnderConstruction();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);

        // The held row now bears the multiplier; the launch did not.
        rig.Owners.Held[0] = "tf_weapon_shotgun";
        rig.Owners.Items[0] = rig.AttributeItem("mult_dmg_vs_buildings",
                                                AttributeDescriptionFormat.Percentage, 1.5f);
        IAttributeProvider plain = rig.AttributeItem("melee_range_multiplier",
                                                     AttributeDescriptionFormat.Percentage, 3f);

        using (rig.Scopes.Push(TFDamageCustom.None, TFWeaponId.Rocketlauncher,
                               "tf_weapon_rocketlauncher", plain))
            Assert.Equal(40, BuildingsRig.Hit(building, enemy, 40f, DamageType.Bullet));
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

    private static string LastModelWorn(BuildingsRig rig, ScriptedEntity building)
        => rig.Engine.AllOf("EntityRender.SetModel")
                     .Where(c => c.ArgAs<EntityHandle>(0) == building.Handle)
                     .Last()
                     .ArgAs<string>(1);
}

/// <summary>
/// The plasma arms of <c>CBaseObject::OnTakeDamage</c> (tf_obj.cpp:1896-1908):
/// a hit whose scope carries TF_DMG_CUSTOM_PLASMA keeps only a fifth of its
/// blow on the building, and the charged shot's TF_DMG_CUSTOM_PLASMA_CHARGED
/// also holds the building off for PLASMA_DISABLE_TIME (:126). The custom
/// rides the blast's scope beside the hit, as the sapper's wrench-fix gate
/// reads its own.
/// </summary>
public sealed class TFBuildingPlasmaFacts
{
    private static (BuildingsRig Rig, TFSentryGun Sentry) ABuiltSentry()
    {
        var rig = new BuildingsRig();
        var builder = rig.AddBuilder();
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);
        rig.Clock.Now = 100f;
        sentry.StartBuilding();
        return (rig, sentry);
    }

    [Fact]
    public void APlasmaHitKeepsOnlyAFifthOfItsBlowOnTheBuilding()
    {
        var (rig, sentry) = ABuiltSentry();
        var scopes = rig.Services.Resolve<TFDamageScopes>()!;

        // :1896-1899: twenty under the plasma arm lands as four.
        using (scopes.Push(TFDamageCustom.Plasma, TFWeaponId.Raygun, "tf_weapon_raygun"))
        {
            Assert.Equal(4, BuildingsRig.Hit(sentry, EntityHandle.None, 20f, DamageType.Bullet));
        }
    }

    [Fact]
    public void WithoutAPlasmaScopeTheBlowStandsWhole()
    {
        var (rig, sentry) = ABuiltSentry();

        // The control: nothing on the scope, no scale — the plain twenty.
        Assert.Equal(20, BuildingsRig.Hit(sentry, EntityHandle.None, 20f, DamageType.Bullet));
    }

    [Fact]
    public void AChargedPlasmaBlowHoldsTheBuildingOffForItsWindow()
    {
        var (rig, sentry) = ABuiltSentry();
        var scopes = rig.Services.Resolve<TFDamageScopes>()!;

        using (scopes.Push(TFDamageCustom.PlasmaCharged, TFWeaponId.Raygun, "tf_weapon_raygun"))
            BuildingsRig.Hit(sentry, EntityHandle.None, 20f, DamageType.Bullet);

        // PLASMA_DISABLE_TIME is 4 (:126): still off inside the window...
        rig.Clock.Now = 103.5f;
        Assert.True(sentry.IsPlasmaDisabled);
        // ...and back on at its close.
        rig.Clock.Now = 104f;
        Assert.False(sentry.IsPlasmaDisabled);
    }

    [Fact]
    public void APlainPlasmaHitDoesNotDisableTheBuilding()
    {
        var (rig, sentry) = ABuiltSentry();
        var scopes = rig.Services.Resolve<TFDamageScopes>()!;

        // The charged-only gate (:1903): the plain plasma custom scales the
        // blow but leaves the building working.
        using (scopes.Push(TFDamageCustom.Plasma, TFWeaponId.Raygun, "tf_weapon_raygun"))
            BuildingsRig.Hit(sentry, EntityHandle.None, 20f, DamageType.Bullet);

        Assert.False(sentry.IsPlasmaDisabled);
    }
}
