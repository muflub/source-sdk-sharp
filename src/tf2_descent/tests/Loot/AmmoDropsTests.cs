using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared.Bones;
using SourceSharp.Tests;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Loot;
using TF2.Descent.Server.Loot;
using TF2.Descent.Tests.Floor;
using TF2.Server;
using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Descent.Tests.Loot;

/// <summary>
/// A robot's death sometimes drops a TF2 ammo pack (<see cref="AmmoDrops"/>): the
/// rules' seeded roll, on the floor where it fell, of its size class's size; TF2's
/// own dropped pack (<see cref="EngineAmmoPackWorld"/>), which fills the hero who
/// takes it by its size's share.
/// </summary>
public sealed class AmmoDropsTests
{
    private static readonly TF2DescentRules Rules = new();

    private sealed class RigWorld : IAmmoPackWorld
    {
        public List<(Vector At, string Pack)> Drops { get; } = [];

        public EntityHandle Drop(Vector at, string pack)
        {
            Drops.Add((at, pack));
            return new EntityHandle((uint)(1700 + Drops.Count));
        }
    }

    private static (AmmoDrops Drops, RigWorld World, RigGround Ground) Make(ulong seed = 77)
    {
        var world = new RigWorld();
        var ground = new RigGround();
        return (new AmmoDrops(Rules, new RigLevel(1, seed), ground, world, new RecordingLog()), world, ground);
    }

    [Fact]
    public void A_giants_death_drops_a_medium_pack_on_the_floor_where_it_fell()
    {
        var (drops, world, _) = Make();

        var drop = drops.Drop(3, "giant_soldier", new Vector(100f, -40f, 37f));

        Assert.Equal((new Vector(100f, -40f, 2f), AmmoPacks.Medium), Assert.Single(world.Drops));
        Assert.Equal(AmmoPacks.Medium, drop!.Value.Pack);
    }

    [Fact]
    public void A_death_over_a_pit_drops_the_pack_where_it_died()
    {
        var (drops, world, ground) = Make();
        ground.Pit = true;

        drops.Drop(3, "major_crits", new Vector(5f, 6f, 70f));

        Assert.Equal((new Vector(5f, 6f, 70f), AmmoPacks.Full), Assert.Single(world.Drops));
    }

    [Fact]
    public void A_kill_drops_exactly_when_the_rules_roll_says_so()
    {
        var (drops, world, _) = Make(seed: 91);
        var expected = new List<uint>();
        for (uint k = 0; k < 200; k++)
        {
            if (Rules.Loot.Ammo(91, k, "scout", 1, Rules.Salt) is not null)
                expected.Add(k);
            drops.Drop(k, "scout", default);
        }

        Assert.InRange(expected.Count, 20, 60);
        Assert.Equal(expected.Count, world.Drops.Count);
        Assert.All(world.Drops, d => Assert.Equal(AmmoPacks.Small, d.Pack));
        Assert.Equal(expected.Count, drops.Dropped);
    }

    // ---------------------------------------------------------------------
    // TF2's dropped pack, as the world drops it
    // ---------------------------------------------------------------------

    private sealed class Hero : ITFMapPlayers
    {
        public Dictionary<TFAmmo, int> Ammo { get; } = new() { [TFAmmo.Primary] = 2, [TFAmmo.Secondary] = 4, [TFAmmo.Metal] = 0 };
        public Dictionary<TFAmmo, int> Max { get; } = new() { [TFAmmo.Primary] = 20, [TFAmmo.Secondary] = 32, [TFAmmo.Metal] = 0 };

        public bool IsAlive(EntityHandle player) => true;
        public int Team(EntityHandle player) => 2;
        public TFClass ClassOf(EntityHandle player) => TFClass.Soldier;
        public int Health(EntityHandle player) => 200;
        public int MaxHealth(EntityHandle player) => 200;
        public int TakeHealth(EntityHandle player, float health) => 0;
        public bool InCond(EntityHandle player, TFCond cond) => false;
        public void AddCond(EntityHandle player, TFCond cond, float duration) { }
        public void RemoveCond(EntityHandle player, TFCond cond) { }
        public void RemoveAllCond(EntityHandle player) { }
        public bool IsInvulnerable(EntityHandle player) => false;
        public int MaxAmmo(EntityHandle player, TFAmmo ammo) => Max.GetValueOrDefault(ammo);

        public int GiveAmmo(EntityHandle player, int count, TFAmmo ammo)
        {
            int has = Ammo.GetValueOrDefault(ammo), room = Math.Max(0, MaxAmmo(player, ammo) - has), given = Math.Min(room, count);
            Ammo[ammo] = has + given;
            return given;
        }

        public void Regenerate(EntityHandle player) { }
        public void SelfBurn(EntityHandle player, float duration) { }
        public bool AddToSpyCloakMeter(EntityHandle player, float amount) => false;
        public bool AddToDemoChargeMeterFromAmmo(EntityHandle player, float packRatio) => false;
    }

    private sealed class Notices : ITFPickupNotices
    {
        public void ItemPickup(EntityHandle player, string className) { }
        public void ItemPickupEvent(EntityHandle player, string item) { }
        public void PlayerHealed(EntityHandle patient, EntityHandle healer, int amount) { }
        public void PlayerHealOnHit(EntityHandle player, int amount) { }
        public void CrossbowHeal(EntityHandle healer, EntityHandle healed, int amount) { }
        public void PlayerStealsSandvich(EntityHandle owner, EntityHandle picker) { }
        public void Sound(EntityHandle player, string sound) { }
    }

    private sealed class Middle : IUniformRandom
    {
        public float RandomFloat(float min, float max) => (min + max) / 2;
        public int RandomInt(int min, int max) => min;
    }

    private sealed class NoModels : IBoneModels
    {
        public bool HasStudioModel(int modelIndex) => false;
        public StudioHdr? OpenStudioHdr(int modelIndex) => null;
    }

    private sealed class NoSkins : IServerSkins
    {
        public void SetSkin(EntityHandle entity, int skin) { }
        public void SetBody(EntityHandle entity, int body) { }
    }

    private static readonly EntityHandle HeroHandle = new(3u);

    /// <summary>The world over the real registry and TF2's pack, its pack spawned as the engine would spawn it.</summary>
    private static (EngineAmmoPackWorld World, ScriptedEntityRegistry Registry, Hero Hero, RecordingEngine Engine) PackWorld()
    {
        var engine = RecordingEngine.Default();
        engine.Strict = false;
        engine.Returns("Entities.IsPlayer", true);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TFAmmoPack).Assembly);

        var hero = new Hero();
        var services = new ServiceContainer();
        foreach (var (contract, _) in EngineServices.Areas(new RecordingLog()))
            services.AddInstance(contract, engine);
        services.AddInstance<ILog>(new RecordingLog());
        services.AddInstance<ITFMapPlayers>(hero);
        services.AddInstance<ITFPickupNotices>(new Notices());
        services.AddInstance(DeclaredConVars.Fresh<TFWeaponRagdollVelocityMinConVar>());
        services.AddInstance(DeclaredConVars.Fresh<TFWeaponRagdollVelocityMaxConVar>());
        services.AddInstance(DeclaredConVars.Fresh<TFWeaponRagdollMaxSpeedConVar>());
        services.AddInstance(DeclaredConVars.Fresh<TFObjGibVelocityMinConVar>());
        services.AddInstance(DeclaredConVars.Fresh<TFObjGibVelocityMaxConVar>());
        services.AddInstance(DeclaredConVars.Fresh<TFObjGibMaxSpeedConVar>());
        services.AddInstance<IUniformRandom>(new Middle());
        services.AddInstance<ITFPlayerStats>(new TFPlayerStats());
        var skins = new NoSkins();
        services.AddInstance<IServerSkins>(skins);
        services.AddInstance(new ServerBodygroups(skins, new NoModels(), engine, engine));
        registry.Services = services;

        uint next = 1600;   // an edict slot: the directory holds edicts below 2048
        engine.ReturnsEachCall("Entities.Create", () =>
        {
            var h = new EntityHandle((1u << 16) | next++);
            registry.KeyValue(h, TFAmmoPack.Class, "targetname", "ammo");   // the instance, as the engine's create makes it
            return h;
        });
        engine.WhenCalled("Entities.Spawn", c => registry.Spawn(c.ArgAs<EntityHandle>(0), TFAmmoPack.Class));
        return (new EngineAmmoPackWorld(engine, registry), registry, hero, engine);
    }

    [Theory]
    [InlineData(AmmoPacks.Small, 0.2f, "models/items/ammopack_small.mdl")]
    [InlineData(AmmoPacks.Medium, 0.5f, "models/items/ammopack_medium.mdl")]
    [InlineData(AmmoPacks.Full, 1f, "models/items/ammopack_large.mdl")]
    public void A_dropped_pack_is_tf2s_pack_of_its_sizes_share_and_model(string size, float ratio, string model)
    {
        var (world, registry, _, engine) = PackWorld();

        var handle = world.Drop(new Vector(10f, 20f, 2f), size);

        var pack = Assert.IsType<TFAmmoPack>(registry.Get(handle));
        Assert.Equal(ratio, pack.AmmoRatio);
        Assert.Equal(EntityHandle.None, pack.PackOwner);
        Assert.Equal(new Vector(10f, 20f, 2f), engine.AllOf("Entities.SetOrigin").Last(c => c.ArgAs<EntityHandle>(0) == handle).ArgAs<Vector>(1));
        Assert.Equal(model, engine.AllOf("EntityRender.SetModel").Last(c => c.ArgAs<EntityHandle>(0) == handle).ArgAs<string>(1));
    }

    [Fact]
    public void A_hero_who_takes_a_small_pack_gets_a_fifth_of_his_ammo_back()
    {
        var (world, registry, hero, _) = PackWorld();
        var pack = Assert.IsType<TFAmmoPack>(registry.Get(world.Drop(default, AmmoPacks.Small)));

        pack.OnTouch(HeroHandle);

        Assert.Equal(2 + 4, hero.Ammo[TFAmmo.Primary]);     // ceil(20 * 0.2)
        Assert.Equal(4 + 7, hero.Ammo[TFAmmo.Secondary]);   // ceil(32 * 0.2)
    }

    [Fact]
    public void A_hero_who_takes_a_full_pack_is_filled()
    {
        var (world, registry, hero, _) = PackWorld();
        var pack = Assert.IsType<TFAmmoPack>(registry.Get(world.Drop(default, AmmoPacks.Full)));

        pack.OnTouch(HeroHandle);

        Assert.Equal(20, hero.Ammo[TFAmmo.Primary]);
        Assert.Equal(32, hero.Ammo[TFAmmo.Secondary]);
    }

    [Fact]
    public void A_dropped_pack_goes_after_tf2s_dropped_pack_time()
    {
        var (world, registry, _, _) = PackWorld();
        var handle = world.Drop(default, AmmoPacks.Medium);

        Assert.True(registry.Directory.Think.IsScheduled(handle, "DieContext"));
        Assert.Equal(TFAmmoPack.Lifetime, registry.Directory.Think.NextThinkTime(handle, "DieContext"), 3);
    }
}
