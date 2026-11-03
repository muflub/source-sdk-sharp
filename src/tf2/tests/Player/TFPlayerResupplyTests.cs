using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

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
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine);
        return (rig, new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions, rig.Loadout, rig.Engine),
                conditions);
    }

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

    /// <summary>A resupply over a rig whose player manager holds the asked arms; the
    /// active weapon is <c>tf_weapon_scattergun</c> with its own item when <paramref name="healing"/> is set.</summary>
    private static TFPlayerResupply OwnedResupply(out PlayerRig rig, bool managers = false, bool nonbuffed = false, float healing = 0f)
    {
        rig = new PlayerRig().As(TFClass.Scout);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine);
        var owners = new TF2.Tests.Weapons.FakeOwners();
        var hooks = new AttributeHooks();
        if (managers)
            owners.Managers[PlayerRig.Index] = Arm("add_maxhealth", 50f, AttributeDescriptionFormat.Additive, 5001);
        if (nonbuffed)
            owners.Managers[PlayerRig.Index] = Arm("add_maxhealth_nonbuffed", 10f, AttributeDescriptionFormat.Additive, 5002);
        if (healing > 0f)
        {
            rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_scattergun");
            owners.ByName[PlayerRig.Index] = new() { ["tf_weapon_scattergun"] =
                Arm("mult_healing_received", healing, AttributeDescriptionFormat.Percentage, 5003) };
        }
        return new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine,
                                    conditions, rig.Loadout, rig.Engine, owners, hooks);
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
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine);
        var owners = new TF2.Tests.Weapons.FakeOwners();

        // scout.txt AmmoMax "tf_ammo_secondary" "36" → ×2.
        owners.Managers[PlayerRig.Index] = SecondaryManager();
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine,
                                            conditions, rig.Loadout, rig.Engine, owners, new AttributeHooks());

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
                                      new TFAmmoCarryRule(Rig.Classes, Weapons.Owners)), log));

            Assert.True(Arsenal.GiveWeapon(PlayerRig.Player, Rocket), "the arsenal gives the rocket launcher");
            Arsenal.SetClip(PlayerRig.Player, Rocket, 2);

            DeployEpoch = RocketEpoch();

            var conditions = new TFPlayerConditions(Rig.Carriers, Rig.Engine);
            Resupply = new TFPlayerResupply(Rig.Carriers, Rig.Classes, new ArsenalPlayers(Rig.Engine, Arsenal),
                                            Rig.Engine, conditions, Rig.Loadout, Rig.Engine,
                                            Weapons.Owners, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(new RecordingLog(), WeaponFixtures.ShippedWithClasses()),
                                            catalog: Weapons.Catalog, arsenal: Arsenal);
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
