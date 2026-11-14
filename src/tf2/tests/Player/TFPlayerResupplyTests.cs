using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// <c>CTFPlayer::Regenerate</c>, <c>GetMaxHealth</c>, <c>TakeHealth</c>,
/// <c>GetMaxAmmo</c> and <c>GiveAmmo</c> for the map's pickups, over the
/// shipped class scripts (scout 125 health, 32 primary; heavy 300, 200).
/// </summary>
public sealed class TFPlayerResupplyTests
{
    private static (PlayerRig Rig, TFPlayerResupply Resupply, TFPlayerConditions Conditions) Make(TFClass cls)
    {
        var rig = new PlayerRig().As(cls);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(), rig.Engine);
        return (rig, new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions, rig.Loadout, rig.Engine,
                                           rig.Items, new AttributeHooks(),
                                           scripts: new TFWeaponScripts(rig.Log, WeaponFixtures.ShippedWithClasses()),
                                           catalog: rig.Catalog, arsenal: LockerlessArsenal(rig),
                                           hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                           deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                           clock: rig.Engine),
                conditions);
    }

    /// <summary>An arsenal an empty rig walks: its carries stay unmade, so a
    /// clip reset or an effect-bar fill from the kit lands nowhere the facts
    /// read — what the old absent arsenal did.</summary>
    private static Arsenal LockerlessArsenal(PlayerRig rig) =>
        new(rig.Log, rig.Engine, rig.Engine, rig.Engine, rig.Engine, rig.Engine, rig.Engine, rig.Engine, rig.Engine,
            new ScriptedEntityRegistry(TestEngines.Server()), rig.Engine, new DamageHitGroups());

    private static int HealthSet(PlayerRig rig) => rig.Engine.AllOf("Entities.SetHealth").Last().ArgAs<int>(1);

    [Fact]
    public void A_resupply_fills_the_cloak_meter()
    {
        // Regenerate's SetSpyCloakMeter( 100.0f ) (tf_player.cpp:4096).
        var (rig, resupply, _) = Make(TFClass.Spy);
        rig.State.CloakMeter = 30f;

        resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(100f, rig.State.CloakMeter);
    }

    [Fact]
    public void Max_health_is_the_classs()
    {
        Assert.Equal(125, Make(TFClass.Scout).Resupply.MaxHealth(PlayerRig.Player));
        Assert.Equal(300, Make(TFClass.HeavyWeapons).Resupply.MaxHealth(PlayerRig.Player));
    }

    [Fact]
    public void Regenerate_fills_to_the_max_the_kit_gives()
    {
        // InitClass's SetHealth( GetMaxHealth() ): the kit's own grant is the
        // manager the maximum walks (a new kit's grants replacing the old), so
        // a buffing primary — the scout's scattergun carrying add_maxhealth
        // 50 — spawns full at 175, not the class's 125.
        var rig = new PlayerRig().As(TFClass.Scout);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(), rig.Engine);
        var inventory = new LocalInventory(new InventoryStore(new ModInfo(_ => null), rig.Schema, rig.Log),
                                           rig.Schema, new TFWeaponOwnersTests.NoClients(), rig.Log);
        inventory.OnClientActive(PlayerRig.Index);
        var scattergun = rig.Schema.BaseItem(TFClass.Scout, TFLoadoutSlot.Primary)!;
        var buffed = new ItemInstance(1, scattergun, scattergun.Quality, [AttributeValue.OfFloat(Buff(), 50f)]);
        inventory.Of(PlayerRig.Index)!.Add(buffed);
        Assert.True(inventory.Equip(PlayerRig.Index, TFClass.Scout, TFLoadoutSlot.Primary, buffed.Id),
                    "the buffing scattergun is equipped over the base one");
        var loadout = LoadoutDeps.Loadout(inventory, rig.Schema, classes: rig.Carriers, items: rig.Items,
                                          table: rig.Classes, catalog: rig.Catalog, carriers: rig.Carriers,
                                          hooks: new AttributeHooks(), players: rig.Engine,
                                          precache: rig.Engine, log: rig.Log);
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine,
                                            conditions, loadout, rig.Engine, rig.Items, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(rig.Log, WeaponFixtures.ShippedWithClasses()),
                                            catalog: rig.Catalog, arsenal: LockerlessArsenal(rig),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: rig.Engine);

        resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(resupply.MaxHealth(PlayerRig.Player), HealthSet(rig));
        Assert.Equal(175, HealthSet(rig));
    }

    [Fact]
    public void Max_health_adds_the_buffing_arm()
    {
        // GetMaxHealthForBuffing's add_maxhealth (tf_player.cpp:14268-14269), the
        // player's own walk over the class maximum.
        var resupply = OwnedResupply(out _, managers: true);

        Assert.Equal(175, resupply.MaxHealth(PlayerRig.Player));
    }

    [Fact]
    public void Max_health_adds_the_nonbuffed_arm_on_top()
    {
        // GetMaxHealth's add_maxhealth_nonbuffed (tf_player.cpp:14245-14246),
        // after the buffing arm, still floored at one.
        var resupply = OwnedResupply(out _, nonbuffed: true);

        Assert.Equal(135, resupply.MaxHealth(PlayerRig.Player));
    }

    [Fact]
    public void Take_health_scales_by_the_active_weapons_healing_received()
    {
        // TakeHealth's mult_healing_received (tf_player.cpp:8428-8434): the
        // active weapon's walk, before the clamp to the class max.
        var resupply = OwnedResupply(out var rig, healing: 2f);
        rig.Engine.Returns("Entities.GetHealth", 80);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_scattergun");

        Assert.Equal(45, resupply.TakeHealth(PlayerRig.Player, 25f));    // 50 asked, 45 the room left
        Assert.Equal(125, HealthSet(rig));
    }

    [Fact]
    public void ARegenerateForgetsADrainedMaximum()
    {
        // Regenerate's ResetMaxHealthDrain (tf_player.cpp:4031), ahead of its
        // recomputed maximum. The rate still reaching the player's own manager
        // would otherwise refill slowly from the drained bucket.
        var rig = new PlayerRig().As(TFClass.Scout);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(), rig.Engine);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_shotgun_primary");
        rig.Engine.Returns("Clock.Now", 10f);
        rig.Engine.Returns("Entities.GetHealth", 100);
        rig.Items.Set(PlayerRig.Index, new Dictionary<string, IAttributeProvider> { ["tf_weapon_shotgun_primary"] =
            Arm("mod_maxhealth_drain_rate", 0.5f, AttributeDescriptionFormat.Additive, 5010) });
        rig.Items.SetManager(PlayerRig.Index, Arm("mod_maxhealth_drain_rate", 0.5f, AttributeDescriptionFormat.Additive, 5011));
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine,
                                            conditions, rig.Loadout, rig.Engine, rig.Items, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(rig.Log, WeaponFixtures.ShippedWithClasses()),
                                            catalog: rig.Catalog, arsenal: LockerlessArsenal(rig),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: rig.Engine);
        Assert.Equal(105, resupply.MaxHealth(PlayerRig.Player));

        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_scattergun");
        resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(125, HealthSet(rig));
        Assert.Equal(125, resupply.MaxHealth(PlayerRig.Player));
    }

    [Fact]
    public void TheGlovesDrainMaxHealthWhileDeployed()
    {
        // GetMaxHealthForBuffing's drain block (tf_player.cpp:14293-14359): the
        // held Gloves drain the max toward tf_maxhealth_drain_hp_min at the
        // hook's per-second rate, once a tf_maxhealth_drain_deploy_cost on the
        // flip from refilling to deployed (:14306-14320). Scout maxes at 125,
        // the hp_min floor at 100: the deploy costs 20 at once, and each
        // second on top takes the hook's half. The health follows its own
        // percentage of the same rate (:14343-14352).
        var rig = new PlayerRig().As(TFClass.Scout);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(), rig.Engine);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_shotgun_primary");
        rig.Engine.Returns("Clock.Now", 10f);
        rig.Engine.Returns("Entities.GetHealth", 100);
        rig.Items.Set(PlayerRig.Index, new Dictionary<string, IAttributeProvider> { ["tf_weapon_shotgun_primary"] =
            Arm("mod_maxhealth_drain_rate", 0.5f, AttributeDescriptionFormat.Additive, 5010) });

        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine,
                                            conditions, rig.Loadout, rig.Engine, rig.Items, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(rig.Log, WeaponFixtures.ShippedWithClasses()),
                                            catalog: rig.Catalog, arsenal: LockerlessArsenal(rig),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: rig.Engine);

        // The deploy's first ask: the full cost at once — 125 down to 105 —
        // and the health by its percentage: 100 * (20 / 125) = 16 gone.
        Assert.Equal(105, resupply.MaxHealth(PlayerRig.Player));
        Assert.Equal(84, HealthSet(rig));

        // Same clock: no elapsed time, no further drain.
        Assert.Equal(105, resupply.MaxHealth(PlayerRig.Player));

        // Two seconds with the Gloves up take the hook's half per second:
        // the bucket holds 21, so the max is 125 - 21 = 104 — above the
        // hp_min floor of 100, which holds at one.
        rig.Engine.Returns("Clock.Now", 12f);
        Assert.Equal(104, resupply.MaxHealth(PlayerRig.Player));
    }

    [Fact]
    public void PuttingTheGlovesAwayRefillsThem()
    {
        // Stock's refill half of the drain block (:14360-14429): the attribute
        // off the held item, the max climbs back at the rate still reaching
        // the player's own manager, and the health with it. Drain two seconds
        // first — bucket 21 — then switch out: one second back at two a
        // second gives the bucket two of its 21, and the health its
        // percentage of the same two.
        var rig = new PlayerRig().As(TFClass.Scout);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(), rig.Engine);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_shotgun_primary");
        rig.Engine.Returns("Clock.Now", 10f);
        rig.Engine.Returns("Entities.GetHealth", 100);
        rig.Items.Set(PlayerRig.Index, new Dictionary<string, IAttributeProvider> { ["tf_weapon_shotgun_primary"] =
            Arm("mod_maxhealth_drain_rate", 0.5f, AttributeDescriptionFormat.Additive, 5010) });
        rig.Items.SetManager(PlayerRig.Index,
            Arm("mod_maxhealth_drain_rate", 2f, AttributeDescriptionFormat.Additive, 5011));

        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine,
                                            conditions, rig.Loadout, rig.Engine, rig.Items, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(rig.Log, WeaponFixtures.ShippedWithClasses()),
                                            catalog: rig.Catalog, arsenal: LockerlessArsenal(rig),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: rig.Engine);

        Assert.Equal(105, resupply.MaxHealth(PlayerRig.Player));   // the deploy's cost
        Assert.Equal(84, HealthSet(rig));
        rig.Engine.Returns("Clock.Now", 12f);
        rig.Engine.Returns("Entities.GetHealth", 84);
        Assert.Equal(104, resupply.MaxHealth(PlayerRig.Player));   // half a second's more

        // Put away: the held item is another weapon's, and its item carries no
        // drain; the manager's still answers two a second. A second off the
        // last stamp gives back two of the twenty-one — bucket 19, max 106 —
        // while the health's fraction (84 / 104 of two, less the 0.8 left by
        // the draining second) has not crossed one yet (:14399-14412).
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_pistol");
        rig.Engine.Returns("Clock.Now", 13f);
        Assert.Equal(106, resupply.MaxHealth(PlayerRig.Player));
        Assert.Equal(84, HealthSet(rig));

        // The next second crosses the whole: the bucket is 17 (max 108) and
        // two of health are back, the cap of the nonbuffed maximum never met.
        rig.Engine.Returns("Clock.Now", 14f);
        Assert.Equal(108, resupply.MaxHealth(PlayerRig.Player));
        Assert.Equal(86, HealthSet(rig));
    }

    /// <summary>
    /// A heavy who drains on one draining glove, re-equips the melee slot to
    /// another draining glove (<c>tf2_equip heavyweapons melee</c>) and dies
    /// comes back at the full class maximum: stock's <c>Respawn</c> resets the
    /// drain bookkeeping when the bucket is not empty and sets the health to
    /// the fresh maximum (tf_player.cpp:3938-3942), so the old life's full
    /// bucket does not clamp the new life to the hp_min floor. The same glove
    /// twice, whose item never changes, already comes back whole (:14373 — the
    /// held attribute still answers through the manager).
    /// </summary>
    [Fact]
    public void A_respawn_after_reequipping_the_gloves_comes_back_whole()
    {
        var rig = new PlayerRig().As(TFClass.HeavyWeapons);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(), rig.Engine);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_fists");
        rig.Engine.Returns("Clock.Now", 10f);
        rig.Engine.Returns("Entities.GetHealth", 300);
        rig.Items.Set(PlayerRig.Index, new Dictionary<string, IAttributeProvider> { ["tf_weapon_fists"] =
            Arm("mod_maxhealth_drain_rate", 2f, AttributeDescriptionFormat.Additive, 5010) });
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine,
                                            conditions, rig.Loadout, rig.Engine, rig.Items, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(rig.Log, WeaponFixtures.ShippedWithClasses()),
                                            catalog: rig.Catalog, arsenal: LockerlessArsenal(rig),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: rig.Engine);

        // The held first glove: the deploy's twenty at once, then two a second
        // — ninety seconds on the clock take the bucket across its cap of
        // 300 minus the hp_min hundred.
        Assert.Equal(280, resupply.MaxHealth(PlayerRig.Player));    // the deploy's cost only
        Assert.Equal(280, HealthSet(rig));
        rig.Engine.Returns("Entities.GetHealth", 100);
        rig.Engine.Returns("Clock.Now", 100f);
        Assert.Equal(100, resupply.MaxHealth(PlayerRig.Player));    // bucket at the cap

        // tf2_equip heavyweapons melee with the other draining glove — the
        // item behind the melee name changes defindex — then kill and respawn.
        rig.Items.Set(PlayerRig.Index, new Dictionary<string, IAttributeProvider> { ["tf_weapon_fists"] =
            Arm("mod_maxhealth_drain_rate", 2f, AttributeDescriptionFormat.Additive, 5012) });
        rig.Engine.Returns("Clock.Now", 112f);
        resupply.OnSpawning(PlayerRig.Player, 112f);

        // TFPlayerSpawns.OnSpawned's health arm: SetHealth(GetMaxHealth())
        // after the fresh kit (tf_player.cpp:4161-4162).
        var max = resupply.MaxHealth(PlayerRig.Player);
        ((IEngineEntities)rig.Engine).SetHealth(PlayerRig.Player, max);

        Assert.Equal(280, max);          // fresh bookkeeping: the new deploy's cost only
        Assert.Equal(280, HealthSet(rig));
    }

    /// <summary>A resupply over a rig whose player manager holds the asked arms; the
    /// active weapon is <c>tf_weapon_scattergun</c> with its own item when <paramref name="healing"/> is set.</summary>
    private static TFPlayerResupply OwnedResupply(out PlayerRig rig, bool managers = false, bool nonbuffed = false, float healing = 0f)
    {
        rig = new PlayerRig().As(TFClass.Scout);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(), rig.Engine);
        var hooks = new AttributeHooks();
        if (managers)
            rig.Items.SetManager(PlayerRig.Index, Arm("add_maxhealth", 50f, AttributeDescriptionFormat.Additive, 5001));
        if (nonbuffed)
            rig.Items.SetManager(PlayerRig.Index, Arm("add_maxhealth_nonbuffed", 10f, AttributeDescriptionFormat.Additive, 5002));
        if (healing > 0f)
        {
            rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_scattergun");
            rig.Items.Set(PlayerRig.Index, new Dictionary<string, IAttributeProvider> { ["tf_weapon_scattergun"] =
                Arm("mult_healing_received", healing, AttributeDescriptionFormat.Percentage, 5003) });
        }
        return new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine,
                                    conditions, rig.Loadout, rig.Engine, rig.Items, hooks,
                                    scripts: new TFWeaponScripts(rig.Log, WeaponFixtures.ShippedWithClasses()),
                                    catalog: rig.Catalog, arsenal: LockerlessArsenal(rig),
                                    hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                    deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                    clock: rig.Engine);
    }

    private static IAttributeProvider Arm(string name, float value, AttributeDescriptionFormat format, int defindex)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(defindex, $"attr {defindex}", name, format,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    /// <summary>The kit's <c>add_maxhealth</c> grant: the additive format, so
    /// the walk adds it to the class maximum.</summary>
    private static AttributeDefinition Buff() =>
        new(5001, "attr 5001", "add_maxhealth", AttributeDescriptionFormat.Additive,
            AttributeEffectType.Neutral, false, false, AttributeValueType.Default, MergedKeys.Block("x"));

    [Fact]
    public void A_classless_player_has_no_max_and_takes_nothing()
    {
        var (rig, resupply, _) = Make(TFClass.Undefined);

        Assert.Equal(0, resupply.MaxHealth(PlayerRig.Player));
        Assert.Equal(0, resupply.TakeHealth(PlayerRig.Player, 50f));
        Assert.Equal(0, rig.Engine.CountOf("Entities.SetHealth"));
    }

    [Fact]
    public void Take_health_stops_at_the_class_max()
    {
        var (rig, resupply, _) = Make(TFClass.Scout);
        rig.Engine.Returns("Entities.GetHealth", 100);

        Assert.Equal(25, resupply.TakeHealth(PlayerRig.Player, 60f));
        Assert.Equal(125, HealthSet(rig));
    }

    [Fact]
    public void Take_health_past_the_max_when_told_to()
    {
        var (rig, resupply, _) = Make(TFClass.Scout);
        rig.Engine.Returns("Entities.GetHealth", 120);

        Assert.Equal(30, resupply.TakeHealth(PlayerRig.Player, 30f, ignoreMaxHealth: true));
        Assert.Equal(150, HealthSet(rig));
    }

    [Fact]
    public void A_full_player_takes_nothing()
    {
        var (rig, resupply, _) = Make(TFClass.Scout);
        rig.Engine.Returns("Entities.GetHealth", 125);

        Assert.Equal(0, resupply.TakeHealth(PlayerRig.Player, 60f));
        Assert.Equal(0, rig.Engine.CountOf("Entities.SetHealth"));
    }

    [Fact]
    public void No_healing_under_the_no_healing_buff()
    {
        var (rig, resupply, conditions) = Make(TFClass.Scout);
        rig.Engine.Returns("Entities.GetHealth", 50);
        conditions.Add(PlayerRig.Player, TFCond.Nohealingdamagebuff);

        Assert.Equal(0, resupply.TakeHealth(PlayerRig.Player, 60f));
    }

    [Fact]
    public void Max_ammo_is_the_classs_and_one_grenade3()
    {
        var (_, resupply, _) = Make(TFClass.Scout);

        Assert.Equal(32, resupply.MaxAmmo(PlayerRig.Player, TFAmmo.Primary));
        Assert.Equal(36, resupply.MaxAmmo(PlayerRig.Player, TFAmmo.Secondary));
        Assert.Equal(1, resupply.MaxAmmo(PlayerRig.Player, TFAmmo.Grenades3));
    }

    /// <summary>
    /// The <c>mult_maxammo_secondary</c> arm (<c>tf_player_shared.cpp:13005-13008</c>)
    /// on the handle-keyed site: the player's own attribute manager, asked at
    /// the handle's index, scales the class maximum; an unhooked row keeps it.
    /// </summary>
    [Fact]
    public void Max_ammo_rides_the_attribute_manager_through_the_handle()
    {
        var rig = new PlayerRig().As(TFClass.Scout);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(), rig.Engine);
        // scout.txt AmmoMax "tf_ammo_secondary" "36" → ×2.
        rig.Items.SetManager(PlayerRig.Index, SecondaryManager());
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine,
                                            conditions, rig.Loadout, rig.Engine, rig.Items, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(rig.Log, WeaponFixtures.ShippedWithClasses()),
                                            catalog: rig.Catalog, arsenal: LockerlessArsenal(rig),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: rig.Engine);

        Assert.Equal(72, resupply.MaxAmmo(PlayerRig.Player, TFAmmo.Secondary));

        // An unhooked row keeps the class limit: the hook is per ammo row,
        // not a blanket scale.
        Assert.Equal(32, resupply.MaxAmmo(PlayerRig.Player, TFAmmo.Primary));
    }

    private static IAttributeProvider SecondaryManager()
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(5000, "attr 5000", "mult_maxammo_secondary",
                                    AttributeDescriptionFormat.Percentage,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), 2f));
        return container;
    }

    [Fact]
    public void Give_ammo_hands_the_tf_pool_to_the_core()
    {
        var (rig, resupply, _) = Make(TFClass.Scout);
        rig.Engine.Returns("Players.GiveAmmo", 12);

        Assert.Equal(12, resupply.GiveAmmo(PlayerRig.Player, 12, TFAmmo.Primary));

        var give = Assert.Single(rig.Engine.AllOf("Players.GiveAmmo"));
        Assert.Equal("TF_AMMO_PRIMARY", give.ArgAs<string>(2));
    }

    [Fact]
    public void The_eurekas_metal_pickup_half_is_the_packs_toll()
    {
        // mult_metal_pickup (tf_player.cpp:14139-14147): the non-resupply
        // metal grant, the count truncated under the multiplier — the
        // Eureka Effect's half-weight packs. A non-metal grant weighs full.
        var rig = new PlayerRig().As(TFClass.Engineer);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(), rig.Engine);
        rig.Items.SetManager(PlayerRig.Index, Arm("mult_metal_pickup", 0.5f, AttributeDescriptionFormat.Percentage, 5021));
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine,
                                            conditions, rig.Loadout, rig.Engine, rig.Items, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(rig.Log, WeaponFixtures.ShippedWithClasses()),
                                            catalog: rig.Catalog, arsenal: LockerlessArsenal(rig),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: rig.Engine);
        rig.Engine.Returns("Players.GiveAmmo", 50);

        Assert.Equal(50, resupply.GiveAmmo(PlayerRig.Player, 100, TFAmmo.Metal));
        Assert.Equal(50, rig.Engine.AllOf("Players.GiveAmmo").Last().ArgAs<int>(1));

        resupply.GiveAmmo(PlayerRig.Player, 40, TFAmmo.Primary);
        Assert.Equal(40, rig.Engine.AllOf("Players.GiveAmmo").Last().ArgAs<int>(1));
    }

    [Fact]
    public void A_denied_held_item_takes_no_grenade_from_the_pickup()
    {
        // AddSecondaryAmmo's denial (tf_player.cpp:14117-14136): outside the
        // resource-meter refill the held item's grenades1_resupply_denied
        // over zero leaves the sandvich's one-grenade grant at zero. The
        // stock hook answers the player themselves — the held item's manager.
        var rig = new PlayerRig().As(TFClass.HeavyWeapons);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(), rig.Engine);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_shotgun_hwg");
        rig.Items.Set(PlayerRig.Index, new Dictionary<string, IAttributeProvider> { ["tf_weapon_shotgun_hwg"] =
            Arm("grenades1_resupply_denied", 1f, AttributeDescriptionFormat.Additive, 5031) });
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine,
                                            conditions, rig.Loadout, rig.Engine, rig.Items, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(rig.Log, WeaponFixtures.ShippedWithClasses()),
                                            catalog: rig.Catalog, arsenal: LockerlessArsenal(rig),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: rig.Engine);
        rig.Engine.Returns("Players.GiveAmmo", 50);

        Assert.Equal(0, resupply.GiveAmmo(PlayerRig.Player, 1, TFAmmo.Grenades1));
        Assert.Equal(0, rig.Engine.CountOf("Players.GiveAmmo"));

        // Without the denial the same grant pays the pool.
        rig.Items.Set(PlayerRig.Index, new Dictionary<string, IAttributeProvider>());
        Assert.Equal(50, resupply.GiveAmmo(PlayerRig.Player, 1, TFAmmo.Grenades1));
        Assert.Equal(1, rig.Engine.AllOf("Players.GiveAmmo").Last().ArgAs<int>(1));
    }

    [Fact]
    public void Regenerate_fills_health_to_the_class_max()
    {
        var (rig, resupply, _) = Make(TFClass.HeavyWeapons);
        rig.Engine.Returns("Entities.GetHealth", 40);

        resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(300, HealthSet(rig));
    }

    [Fact]
    public void Regenerate_keeps_an_overheal()
    {
        var (rig, resupply, _) = Make(TFClass.Scout);
        rig.Engine.Returns("Entities.GetHealth", 180);

        resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(180, HealthSet(rig));
    }

    [Fact]
    public void Regenerate_keeps_the_inventory_and_fills_the_kit()
    {
        // Regenerate runs inside m_bRegenerating (tf_player.cpp:4033-4036), whose
        // InitClass → ManageRegularWeapons keeps every weapon whose item still
        // matches and gives only the missing ones (:4528-4662) — no
        // RemoveAllItems, and the guard skips the closing Weapon_Switch (:4700):
        // the locker draws nothing.
        var (rig, resupply, _) = Make(TFClass.Scout);

        resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(0, rig.Engine.CountOf("Players.RemoveAllItems"));
        Assert.Contains("tf_weapon_scattergun", rig.Engine.AllOf("Players.GiveItem").Select(c => c.ArgAs<string>(1)));
    }

    [Fact]
    public void Regenerate_ends_the_burning()
    {
        var (rig, resupply, conditions) = Make(TFClass.Scout);
        conditions.Add(PlayerRig.Player, TFCond.Burning, 4f);
        conditions.Add(PlayerRig.Player, TFCond.Zoomed);

        resupply.Regenerate(PlayerRig.Player);

        Assert.False(rig.State.InCond(TFCond.Burning));
        Assert.True(rig.State.InCond(TFCond.Zoomed));
    }

    [Fact]
    public void Regenerate_without_refill_keeps_health_and_kit()
    {
        var (rig, resupply, conditions) = Make(TFClass.Scout);
        rig.Engine.Returns("Entities.GetHealth", 60);
        conditions.Add(PlayerRig.Player, TFCond.Burning, 4f);

        resupply.Regenerate(PlayerRig.Player, refillHealthAndAmmo: false);

        Assert.Equal(125, HealthSet(rig));
        Assert.Equal(0, rig.Engine.CountOf("Players.RemoveAllItems"));
        Assert.True(rig.State.InCond(TFCond.Burning));
    }

    [Fact]
    public void A_resupply_resets_a_fired_clip_to_the_default()
    {
        // The bResetWeapons arm's GiveDefaultAmmo (tf_player.cpp:5209): the
        // clip the soldier fired down to is the default again
        // (basecombatweapon_shared.cpp:144-165), the reserve at its max from
        // the fill that follows, and no redraw (the m_bRegenerating guard,
        // :4700) — the deploy the first give made is the last stamp.
        var world = new LockerWorld();

        Assert.Equal(2, world.RocketClip); // two rockets gone

        world.Resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(LockerWorld.RocketClipDefault, world.RocketClip);
        Assert.True(world.Arsenal.PoolOf(PlayerRig.Player, TFAmmoNames.Name(TFAmmo.Primary), out int pool));
        Assert.Equal(world.Rig.Classes[TFClass.Soldier].MaxAmmo(TFAmmo.Primary), pool);
        Assert.Equal(world.DeployEpoch, world.RocketEpoch());
    }

    /// <summary>
    /// The locker's world: the carrier rig above joined to the core arsenal of
    /// the weapon rigs, the kit's calls routed through an
    /// <see cref="ArsenalPlayers"/> so the regen's gives and its clip reset
    /// land in the one carry the facts read.
    /// </summary>
    private sealed class LockerWorld
    {
        public const string Rocket = "tf_weapon_rocketlauncher";

        /// <summary>The script's clip_size and default alike (the fixture).</summary>
        public const int RocketClipDefault = 4;

        public readonly PlayerRig Rig = new PlayerRig().As(TFClass.Soldier);
        public readonly TFWeaponWorld Weapons = new();
        public readonly RecordingEngine Engine;
        public readonly ScriptedEntityRegistry Registry;
        public readonly Arsenal Arsenal;
        public readonly TFPlayerResupply Resupply;

        private readonly EntityHandle _weapon = new(0x0500u);
        private readonly EntityHandle _viewModel = new(0x0501u);

        /// <summary>The deploy stamp the setup's first give made.</summary>
        public int DeployEpoch { get; private set; }

        public LockerWorld()
        {
            var created = new Queue<EntityHandle>([_weapon, _viewModel]);
            Engine = new RecordingEngine()
                .Returns("Entities.IsValid", true)
                .Returns("Entities.GetIndex", PlayerRig.Index)
                .Returns("Entities.GetClassName", EntityNames.Player)
                .Returns("Entities.GetOrigin", default(Vector))
                .Returns("Entities.GetAngles", default(Vector))
                .Returns("Players.IsAlive", true)
                .Returns("Entities.GetEyeAngles", default(Vector))
                .Returns("EntityRender.LookupSequence", -1)
                .Returns("EntityRender.LookupSequenceByActivity", -1)
                .Returns("Clock.Now", 10f)
                .ReturnsEachCall("Entities.Create", () => created.Dequeue());

            Registry = new ScriptedEntityRegistry(TestEngines.Server());
            Registry.RegisterAll(typeof(SsWeapon).Assembly);
            Registry.Services = TestServices.Services(out _);
            Registry.KeyValue(PlayerRig.Player, EntityNames.Player, "targetname", "fixture_player");
            Registry.Spawn(_weapon, EntityNames.SsWeapon);
            Registry.Spawn(_viewModel, EntityNames.PredictedViewModel);

            var log = new RecordingLog();
            Weapons.Owners.Classes[PlayerRig.Index] = TFClass.Soldier;
            Arsenal = new Arsenal(log, Engine, Engine, Engine, Engine, Engine, Engine, Engine, Engine,
                                  Registry, Engine, new DamageHitGroups(),
                                  weapons: Weapons.Catalog, ammo: new AmmoCatalog(() => [new TFAmmoTypes()], log),
                                  carryLimits: new AmmoCarry(new ListHooks<IAmmoCarryRule>(
                                      new TFAmmoCarryRule(Rig.Classes, Weapons.Owners, new AttributeHooks())), log));

            Assert.True(Arsenal.GiveWeapon(PlayerRig.Player, Rocket), "the arsenal gives the rocket launcher");
            Arsenal.SetClip(PlayerRig.Player, Rocket, 2);

            DeployEpoch = RocketEpoch();

            var conditions = new TFPlayerConditions(Rig.Carriers, Rig.Engine, new ListHooks<ITFConditionHook>(), Rig.Engine);
            Resupply = new TFPlayerResupply(Rig.Carriers, Rig.Classes, new ArsenalPlayers(Rig.Engine, Arsenal),
                                            Rig.Engine, conditions, Rig.Loadout, Rig.Engine,
                                            new TFPlayerItems(), new AttributeHooks(),
                                            scripts: new TFWeaponScripts(new RecordingLog(), WeaponFixtures.ShippedWithClasses()),
                                            catalog: Weapons.Catalog, arsenal: Arsenal,
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: Engine);
        }

        /// <summary>The rocket's clip, as the carry holds it.</summary>
        public int RocketClip => Arsenal.Holdings(PlayerRig.Player).Single(h => h.ClassName == Rocket).Clip;

        public int RocketEpoch() => ((SsWeapon)Registry.Get(_weapon)!).State.DeployEpoch;
    }

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }
}
