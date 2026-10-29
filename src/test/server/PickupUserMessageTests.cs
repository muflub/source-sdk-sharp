using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The HUD's pickup history: ItemPickup when a battery, health kit or vial is
/// taken (CHL2_Player::ApplyBattery, CHealthKit::MyTouch, CHealthVial::MyTouch)
/// and AmmoDenied when ammunition is offered and none fits
/// (CHL2_Player::GiveAmmo). Reliable, to the player alone.
/// </summary>
public class PickupUserMessageTests
{
    private const uint PlayerHandle = 0x010005;
    private const int Slot = 5;
    private static EntityHandle Player => new(PlayerHandle);

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine) World(int health)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsPlayer).Assembly);
        var services = Services(out var engine);
        engine.Ignores("Entities.SetHealth", "Entities.SetModel", "Entities.GetOrigin",
                       "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles")
              .Returns("Entities.IsValid", true)
              .Returns("Entities.GetIndex", Slot)
              .Returns("Entities.GetHealth", health)
              .Returns("Entities.GetClassName", EntityNames.Player)
              .Returns("Players.IsAlive", true)
              .Returns("Clock.Now", 10f)
              .Returns("Entities.IsPlayer", true)
              .Returns("WorldQuery.EntityTraceHull", new EntityHullTrace(Vector.Zero, false, false, Fraction: 1f))
              .Ignores("Entities.GetSolidType", "Entities.GetWorldSpaceCenter", "Entities.GetEyePosition",
                       "Entities.UseTriggerBounds");
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");
        return (registry, engine);
    }

    private static PickupItem Item(ScriptedEntityRegistry registry, string className, uint handle = 0x020001)
    {
        registry.KeyValue(new EntityHandle(handle), className, "targetname", "fixture_item");
        return (PickupItem)registry.Get(new EntityHandle(handle))!;
    }

    private static List<EngineCall> Sends(RecordingEngine engine, string key)
        => engine.AllOf("UserMessages.Send").Where(c => (string)c.Args[0]! == key).ToList();

    private static string PickedUp(EngineCall send)
    {
        var reader = new UserMessageReader((byte[])send.Args[4]!);
        return ItemPickup.Read(ref reader).Item;
    }

    [Theory]
    [InlineData(EntityNames.ItemHealthkit)]
    [InlineData(EntityNames.ItemHealthvial)]
    public void Taking_health_reports_the_item_by_its_classname(string className)
    {
        var (registry, engine) = World(health: 50);

        Item(registry, className).OnTouch(Player);

        var send = Assert.Single(Sends(engine, "ItemPickup"));
        Assert.Equal((className, Slot, true), (PickedUp(send), (int)send.Args[2]!, (bool)send.Args[3]!));
    }

    [Fact]
    public void A_full_player_takes_no_health_and_hears_nothing()
    {
        var (registry, engine) = World(health: 100);

        Item(registry, EntityNames.ItemHealthkit).OnTouch(Player);

        Assert.Empty(Sends(engine, "ItemPickup"));
    }

    [Fact]
    public void Taking_a_battery_reports_item_battery()
    {
        var (registry, engine) = World(health: 100);

        Item(registry, EntityNames.ItemBattery).OnTouch(Player);

        Assert.Equal("item_battery", PickedUp(Assert.Single(Sends(engine, "ItemPickup"))));
    }

    [Fact]
    public void A_full_suit_takes_no_battery_and_hears_nothing()
    {
        var (registry, engine) = World(health: 100);
        ((SsPlayer)registry.Get(Player)!).State.BindLocal();
        ((SsPlayer)registry.Get(Player)!).Armour = SsPlayer.MaxArmour;

        Item(registry, EntityNames.ItemBattery).OnTouch(Player);

        Assert.Empty(Sends(engine, "ItemPickup"));
    }

    private static Arsenal Arsenal(ScriptedEntityRegistry registry, RecordingEngine engine)
        => new(new RecordingLog(), engine, engine, engine, engine, engine, engine, engine, engine, registry, engine,
               new DamageHitGroups(),
               messages: engine);

    [Fact]
    public void Ammo_that_does_not_fit_is_denied_by_its_wire_index()
    {
        var (registry, engine) = World(health: 100);
        var arsenal = Arsenal(registry, engine);
        var pistol = AmmoCatalog.Stock().For("Pistol")!.Value;

        Assert.Equal(pistol.MaxCarry, arsenal.GiveAmmo(Player, pistol.MaxCarry, "Pistol"));
        Assert.Empty(Sends(engine, "AmmoDenied"));   // what fitted is not denied

        Assert.Equal(0, arsenal.GiveAmmo(Player, 20, "Pistol"));

        var send = Assert.Single(Sends(engine, "AmmoDenied"));
        var reader = new UserMessageReader((byte[])send.Args[4]!);
        Assert.Equal((AmmoCatalog.Stock().WireIndex("Pistol"), Slot, true),
                     ((int)AmmoDenied.Read(ref reader).AmmoType, (int)send.Args[2]!, (bool)send.Args[3]!));
    }

    [Fact]
    public void Ammo_wire_indexes_count_from_one_as_stocks_ammo_table()
    {
        Assert.Equal(1, AmmoCatalog.Stock().WireIndex(AmmoTypes.All[0].Name));
        Assert.Equal(AmmoTypes.All.Length, AmmoCatalog.Stock().WireIndex(AmmoTypes.All[^1].Name));
        Assert.Equal(-1, AmmoCatalog.Stock().WireIndex("NoSuchAmmo"));
    }
}
