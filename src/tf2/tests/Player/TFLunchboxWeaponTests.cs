using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Tests.Entities.Items;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// <see cref="TFLunchboxWeapon"/>: the server half of the sandvich — the eat
/// is the taunt ask, and the bite (<c>CTFLunchBox::ApplyBiteEffects</c>,
/// tf_weapon_lunchbox.cpp:381-432) heals, buffs the ceiling, or pays in
/// minicrits and ammo, by the held item's mode.
/// </summary>
public sealed class TFLunchboxWeaponTests
{
    private const string Lunchbox = "tf_weapon_lunchbox";

    private static (PlayerRig Rig, TFLunchboxWeapon Lunchbox, TFPlayerConditions Conditions, TFPlayerResupply Resupply)
        Rig(TFClass cls, AttributeContainer? item = null, MapItemRig? items = null)
    {
        var rig = new PlayerRig();
        rig.Extra.Add(new TFPlayerAttributes(rig.Carriers, rig.Items, rig.Schema, rig.Engine));
        rig.As(cls);

        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new TF2.Tests.Rules.ListHooks<ITFConditionHook>(), rig.Engine);
        var owners = new TFServerWeaponOwners(rig.Carriers, new TFRoundWinners(), rig.Items, rig.Engine,
                                              new NoEntities(), new TFPlayerStats(),
                                              new Combat.RigMediguns(), rig.Schema, new PoollessGiver(), rig.Catalog);
        var hooks = new AttributeHooks();
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions,
                                            rig.Loadout, rig.Engine, rig.Items, hooks,
                                            scripts: new TFWeaponScripts(rig.Log, TF2.Tests.Weapons.WeaponFixtures.ShippedWithClasses()),
                                            catalog: rig.Catalog,
                                            arsenal: new Arsenal(rig.Log, rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                                                 rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                                                 new ScriptedEntityRegistry(TestEngines.Server()), rig.Engine,
                                                                 new DamageHitGroups()),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: rig.Engine);
        var players = new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, resupply, conditions, rig.Carriers, rig.Items, new AttributeHooks());
        var taunts = new TFTauntCommand(players, rig.Engine, rig.Engine, rig.Engine, rig.Carriers,
                                        new TF2.Server.Voice.TFConceptSpeech(new TF2.Tests.Entities.FakeConceptRules(), rig.Carriers,
                                                                             new TF2.Tests.Entities.FakeConceptTeams(), rig.Engine,
                                                                             new TF2.Server.Voice.TFSceneImage(TF2.Tests.Voice.TFSceneImageTests.Files()),
                                                                             rig.Engine, new TF2.Tests.Entities.FakeRandom(),
                                                                             new LoadoutDeps.IdleRound(), new TF2.Tests.Weapons.FakeOwners(), rig.Log),
                                        owners, hooks,
                                        new TF2.Server.Voice.TFSceneImage(TF2.Tests.Voice.TFSceneImageTests.Files()));
        IEntityDirectory entities = items is null ? new NoEntities() : items.Registry;
        var lunchbox = new TFLunchboxWeapon(players, resupply, rig.Carriers, owners, hooks, taunts,
                                            rig.Engine, rig.Engine, rig.Engine, entities,
                                            DeclaredConVars.Fresh<TFScoutEnergydrinkConsumeRateConVar>(),
                                            new Arsenal(new RecordingLog(), rig.Engine, rig.Engine, rig.Engine,
                                                        rig.Engine, rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                                        entities, rig.Engine, new DamageHitGroups()));

        if (item is not null)
            rig.Items.Set(PlayerRig.Index, new Dictionary<string, IAttributeProvider> { [Lunchbox] = item });
        rig.Items.SetManager(PlayerRig.Index, new AttributeContainer());

        return (rig, lunchbox, conditions, resupply);
    }

    private static AttributeContainer Item(float mode, int defindex) => Item(TFLunchboxBehaviour.ModeAttribute, mode, defindex);

    private static AttributeContainer Item(string attribute, float value, int defindex)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(
            new AttributeDefinition(defindex, attribute, attribute, AttributeDescriptionFormat.Additive,
                AttributeEffectType.Neutral, false, false, AttributeValueType.Default, MergedKeys.Block("x")),
            value));
        return item;
    }

    [Fact]
    public void ThePlainBiteHealsSeventyFive()
    {
        var (rig, lunchbox, _, _) = Rig(TFClass.HeavyWeapons);
        rig.Engine.Returns("Entities.GetHealth", 150);

        lunchbox.Bite(PlayerRig.Index);

        // :407's 75 under the class's 300 (tf_player.cpp:14245's walk, no
        // arm on the plain sandvich's manager).
        Assert.Equal(225, rig.Engine.AllOf("Entities.SetHealth").Last().ArgAs<int>(1));
    }

    [Fact]
    public void TheDalokohsBiteHealsAQuarterPastTheCeiling()
    {
        var (rig, lunchbox, _, _) = Rig(TFClass.HeavyWeapons, Item(TFLunchboxBehaviour.ModeFishcake, 910));
        rig.Engine.Returns("Entities.GetHealth", 280);

        lunchbox.Bite(PlayerRig.Index);

        // :409-412 — 280 is under 350, so DMG_IGNORE_MAXHEALTH rides the
        // bite: the 25 lands past the class's 300 whole.
        Assert.Equal(305, rig.Engine.AllOf("Entities.SetHealth").Last().ArgAs<int>(1));
    }

    [Fact]
    public void TheDalokohsBiteRaisesTheHealthCeilingForThirtySeconds()
    {
        var (rig, lunchbox, _, resupply) = Rig(TFClass.HeavyWeapons, Item(TFLunchboxBehaviour.ModeChocolateBar, 911));

        lunchbox.Bite(PlayerRig.Index);

        // :392's hidden maxhealth on the player's own list, the same name the
        // GetMaxHealth walk reads (tf_player.cpp:14245-14246): 300 plus the
        // 50, for the expiry the runtime grant carries.
        Assert.Equal(350, resupply.MaxHealth(PlayerRig.Player));
        var manager = (rig.Items.ManagerOf(PlayerRig.Index) as AttributeContainer)!;
        Assert.Equal(1, manager.Attributes.Length);
    }

    [Fact]
    public void TheSteakBitePaysInMinicritsAndTheMeleeLock()
    {
        var (rig, lunchbox, _, _) = Rig(TFClass.HeavyWeapons, Item(TFLunchboxBehaviour.ModeAddsMinicrits, 912));
        rig.Engine.Returns("Entities.GetHealth", 100);

        lunchbox.Bite(PlayerRig.Index);

        // :394-404 — the conditions and nothing else: no heal on the return.
        Assert.True(rig.State.InCond(TFCond.EnergyBuff));
        Assert.True(rig.State.InCond(TFCond.CannotSwitchFromMelee));
        Assert.Equal(0, rig.Engine.CountOf("Entities.SetHealth"));
    }

    [Fact]
    public void TheAmmoBiteTopsThePrimaryPoolByAQuarterOfItsCarry()
    {
        var (rig, lunchbox, _, _) = Rig(TFClass.HeavyWeapons, Item(TFLunchboxBehaviour.ModeAddsAmmo, 913));

        lunchbox.Bite(PlayerRig.Index);

        // :426-431 — the heavy's 200 primary, a quarter back whatever the
        // heal did.
        Assert.Equal(50, rig.Engine.AllOf("Players.GiveAmmo").Last().ArgAs<int>(1));
        Assert.Equal("TF_AMMO_PRIMARY", rig.Engine.AllOf("Players.GiveAmmo").Last().ArgAs<string>(2));
    }

    [Fact]
    public void TheEatIsTheTauntAsk()
    {
        var (rig, lunchbox, _, _) = Rig(TFClass.HeavyWeapons);
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);

        // The bite's caller hands the attack of the machine's run; the ask is
        // the taunt command's own (CTFLunchBox::PrimaryAttack's Taunt(), :182).
        var files = TF2.Tests.Weapons.WeaponFixtures.WithExtras(TF2.Tests.Weapons.WeaponFixtures.Shipped());
        var world = new TF2.Tests.Weapons.TFWeaponWorld(files: files);
        var (w, vm) = world.Armed(Lunchbox, clip: 0, reserve: 1);
        var attack = new WeaponAttackContext(world.Row(Lunchbox), w, vm, 10f, true, world.Arms,
                                             new WeaponCommand(1, 0, PlayerRig.Index));
        var script = new TFWeaponScripts(new RecordingLog(), files).Get(Lunchbox)!;

        lunchbox.LunchboxEat(in attack, script);

        Assert.True(rig.State.InCond(TFCond.Taunting));
    }

    /// <summary>
    /// The throw's rig: the player's world plus a second, item-side one whose
    /// registry stands in as the weapon's directory — the plate it creates is
    /// a real <see cref="HealthKit"/> there, spawned before the throw names it.
    /// </summary>
    private static (PlayerRig Rig, MapItemRig Items, TFLunchboxWeapon Lunchbox) DropRig(TFClass cls = TFClass.HeavyWeapons)
    {
        var items = new MapItemRig();
        var (rig, lunchbox, _, _) = Rig(cls, items: items);
        return (rig, items, lunchbox);
    }

    /// <summary>The attack of one secondary press, the shape the behaviour hands on.</summary>
    private static WeaponAttackContext Attack(float now = 10f)
    {
        var files = TF2.Tests.Weapons.WeaponFixtures.WithExtras(TF2.Tests.Weapons.WeaponFixtures.Shipped());
        var world = new TF2.Tests.Weapons.TFWeaponWorld(files: files);
        var (w, vm) = world.Armed(Lunchbox, clip: 0, reserve: 1);
        return new WeaponAttackContext(world.Row(Lunchbox), w, vm, now, true, world.Arms,
                                       new WeaponCommand(1, 0, PlayerRig.Index));
    }

    [Theory]
    [InlineData(TFLunchboxBehaviour.ModeStandard, HealthKitMedium.MediumClass, TFLunchboxWeaponTests.PlainPlate, false)]
    [InlineData(TFLunchboxBehaviour.ModeChocolateBar, HealthKitSmall.SmallClass, TFLunchboxWeaponTests.ChocolatePlate, true)]
    [InlineData(TFLunchboxBehaviour.ModeAddsMinicrits, HealthKitMedium.MediumClass, TFLunchboxWeaponTests.SteakPlate, false)]
    [InlineData(TFLunchboxBehaviour.ModeStandardRobo, HealthKitMedium.MediumClass, TFLunchboxWeaponTests.RoboPlate, true)]
    [InlineData(TFLunchboxBehaviour.ModeStandardFestive, HealthKitMedium.MediumClass, TFLunchboxWeaponTests.FestivePlate, true)]
    [InlineData(TFLunchboxBehaviour.ModeAddsAmmo, HealthAmmoKit.MapClass, TFLunchboxWeaponTests.PlainPlate, false)]
    [InlineData(TFLunchboxBehaviour.ModeBanana, HealthKitSmall.SmallClass, TFLunchboxWeaponTests.BananaPlate, false)]
    [InlineData(TFLunchboxBehaviour.ModeFishcake, HealthKitSmall.SmallClass, TFLunchboxWeaponTests.FishcakePlate, true)]
    public void The_throw_becomes_the_modes_plate(int mode, string className, string plate, bool skinned)
    {
        // SecondaryAttack's shape (:195-295): the kit of the mode's class
        // (:218-232) under the eye and a little low (:214-215), aimed a
        // little down (:216), wearing the mode's plate (:249-275) — the
        // teams' skin on the plates that carry one (:266-274) — flung at
        // :278's 500 down that line, levelled (:281) and boxed (:282).
        var (rig, items, lunchbox) = DropRig();
        var kit = SpawnKit(items, className);
        rig.Engine.Returns("Entities.Create", kit.Handle);
        rig.Engine.Returns("Entities.GetEyePosition", new Vector(100f, 200f, 64f));
        rig.Engine.Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f));

        var attack = Attack();
        lunchbox.LunchboxDrop(in attack, null!, mode);

        Assert.Equal(className, rig.Engine.Last("Entities.Create")!.Value.ArgAs<string>(0));
        var placed = rig.Engine.Last("Entities.SetOrigin")!.Value;
        Assert.Equal(new Vector(100f, 200f, 56f), placed.ArgAs<Vector>(1)); // the eye, 8 low
        Assert.Contains("Entities.Spawn", rig.Engine.Calls.Select(c => c.Member));

        Assert.Equal(plate, rig.Engine.Last("EntityRender.SetModel")!.Value.ArgAs<string>(1));
        var skin = rig.Engine.Last("EntityRender.SetSkin");
        Assert.Equal(skinned, skin is not null);
        if (skin is not null)
            Assert.Equal(0, skin.Value.ArgAs<int>(1)); // red's skin

        var kitFlight = items.Engine.Last("Entities.SetVelocity")!.Value;
        Assert.Equal(kit.Handle, kitFlight.ArgAs<EntityHandle>(0));
        Vector launch = kitFlight.ArgAs<Vector>(1);
        Assert.Equal(492.4f, launch.X, 1); // cos 10° of the 500
        Assert.Equal(0f, launch.Y, 1);
        Assert.Equal(86.8f, launch.Z, 1); // the pitch's sine, up as the engine angles run

        Assert.True(kit.ThrownSingleInstance);
        Assert.Equal(PlayerRig.Player, kit.OwnerEntity);
        Assert.Equal(default(Vector), rig.Engine.Last("Entities.SetAngles")!.Value.ArgAs<Vector>(1));
        Assert.Equal(new Vector(-17f, -17f, -10f), rig.Engine.Last("Entities.SetBounds")!.Value.ArgAs<Vector>(1));
        Assert.Equal(new Vector(17f, 17f, 10f), rig.Engine.Last("Entities.SetBounds")!.Value.ArgAs<Vector>(2));
    }

    [Fact]
    public void A_blue_throwers_plate_wears_blue()
    {
        // :266's skin — the team's index on the plates that carry one.
        var (rig, items, lunchbox) = DropRig();
        rig.Engine.Returns("Players.GetTeam", (int)TFTeam.Blue);
        var kit = SpawnKit(items, HealthKitMedium.MediumClass);
        rig.Engine.Returns("Entities.Create", kit.Handle);
        rig.Engine.Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f));
        rig.Engine.Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f));

        var attack = Attack();
        lunchbox.LunchboxDrop(in attack, null!, TFLunchboxBehaviour.ModeStandardRobo);

        Assert.Equal(1, rig.Engine.Last("EntityRender.SetSkin")!.Value.ArgAs<int>(1));
    }

    [Fact]
    public void A_second_throw_replaces_the_first_plate()
    {
        // :209-212 — m_hThrownPowerup removed before the next throw: the one
        // thrown kit out there bearing the owner goes, the new plate stands.
        var (rig, items, lunchbox) = DropRig();
        var first = SpawnKit(items, HealthKitMedium.MediumClass);
        rig.Engine.Returns("Entities.Create", first.Handle);
        rig.Engine.Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f));
        rig.Engine.Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f));
        var attack = Attack();

        lunchbox.LunchboxDrop(in attack, null!, TFLunchboxBehaviour.ModeStandard);
        Assert.DoesNotContain("Entities.Remove", rig.Engine.Calls.Select(c => c.Member));

        var second = SpawnKit(items, HealthKitMedium.MediumClass);
        rig.Engine.Returns("Entities.Create", second.Handle);
        lunchbox.LunchboxDrop(in attack, null!, TFLunchboxBehaviour.ModeStandard);

        Assert.Equal(first.Handle, rig.Engine.Last("Entities.Remove")!.Value.ArgAs<EntityHandle>(0));
        Assert.Equal(second.Handle, rig.Engine.Last("Entities.SetOrigin")!.Value.ArgAs<EntityHandle>(0));
    }

    /// <summary>A kit of <paramref name="className"/>, live in the item world.</summary>
    private static HealthKit SpawnKit(MapItemRig items, string className) => items.Spawn<HealthKit>(className);

    internal const string PlainPlate = "models/items/plate.mdl";
    internal const string SteakPlate = "models/workshop/weapons/c_models/c_buffalo_steak/plate_buffalo_steak.mdl";
    internal const string RoboPlate = "models/items/plate_robo_sandwich.mdl";
    internal const string FestivePlate = "models/items/plate_sandwich_xmas.mdl";
    internal const string ChocolatePlate = "models/workshop/weapons/c_models/c_chocolate/plate_chocolate.mdl";
    internal const string BananaPlate = "models/items/banana/plate_banana.mdl";
    internal const string FishcakePlate = "models/workshop/weapons/c_models/c_fishcake/plate_fishcake.mdl";

    /// <summary>No scripted entity of ours anywhere: the owners' directory asks.</summary>
    private sealed class NoEntities : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h) => null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => false;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }
}
