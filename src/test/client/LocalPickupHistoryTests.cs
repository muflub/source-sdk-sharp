using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// What feeds the pickup history, as stock's client feeds CHudHistoryResource:
/// a weapon the local player now carries, a pool that rose, and the server's
/// ItemPickup and AmmoDenied.
/// </summary>
public class LocalPickupHistoryTests
{
    private sealed class OnePlayer(ClientStandaloneEntity player) : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live() => [player];
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => entityIndex == 1 ? player : null;
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    private sealed class Event<T> : IUserMessageEvent<T>
    {
        public event Action<T>? Fired;
        public void Raise(T value) => Fired?.Invoke(value);
    }

    private static readonly WeaponCatalog Weapons = WeaponCatalog.Stock();
    private static readonly AmmoCatalog Ammo = AmmoCatalog.Stock();

    private static int Type(string weapon) => Weapons.IndexOf(weapon);

    private sealed record Rig(LocalPickupHistory Feed, PickupHistory History, ClientSsPlayer Player,
                              Event<string> Items, Event<int> Denied, Clock Clock);

    private static Rig Made()
    {
        var engine = Fake().Returns("Hud.LocalPlayerIndex", 1);
        var player = new ClientSsPlayer(engine) { Handle = new EntityHandle(0x0001_0001u) };
        player.State.BindLocal();
        var scripts = new WeaponScriptDatabase(new RecordingLog(), path => path switch
        {
            "scripts/weapon_frag.txt" => "WeaponData\n{\n\"item_flags\" \"16\"\n\"primary_ammo\" \"grenade\"\n}\n",
            "scripts/weapon_pistol.txt" => "WeaponData\n{\n\"item_flags\" \"64\"\n\"primary_ammo\" \"Pistol\"\n}\n",
            "scripts/weapon_smg1.txt" => "WeaponData\n{\n\"primary_ammo\" \"SMG1\"\n\"secondary_ammo\" \"SMG1_Grenade\"\n}\n",
            _ => null,
        });
        var clock = new Clock();
        var history = new PickupHistory(clock, () => 5f);
        var feed = new LocalPickupHistory(history, engine, new OnePlayer(player), Weapons, Ammo, scripts);
        var items = new Event<string>();
        var denied = new Event<int>();
        feed.Subscribe(items, denied);
        return new Rig(feed, history, player, items, denied, clock);
    }

    private static PickupHistory.Entry[] Rows(PickupHistory h)
        => h.Slots.Where(e => e.Type != PickupHistory.Kind.Empty).ToArray();

    [Fact]
    public void A_weapon_newly_carried_is_listed_by_its_icon_and_an_exhaustible_one_is_not()
    {
        var rig = Made();
        rig.Player.State.CarriedWeapons = (1u << Type(EntityNames.WeaponSmg1)) | (1u << Type(EntityNames.WeaponFrag));

        rig.Feed.Think();
        rig.Feed.Think();

        var row = Assert.Single(Rows(rig.History));
        Assert.Equal((PickupHistory.Kind.Weapon, "smg1"), (row.Type, row.Icon));
    }

    [Fact]
    public void A_weapon_still_carried_after_its_row_expires_is_not_listed_again()
    {
        var rig = Made();
        rig.Player.State.CarriedWeapons = 1u << Type(EntityNames.WeaponSmg1);
        rig.Feed.Think();

        rig.Clock.Now = 20f;
        rig.Feed.Think();
        rig.Feed.Think();

        Assert.Empty(Rows(rig.History));
    }

    [Fact]
    public void A_pool_that_rose_is_listed_with_the_rise_and_one_that_fell_is_not()
    {
        var rig = Made();
        int smg = Ammo.WireIndex("SMG1");
        rig.Player.State.Ammo[smg] = 45;
        rig.Feed.Think();

        rig.Player.State.Ammo[smg] = 30;
        rig.Feed.Think();
        rig.Player.State.Ammo[smg] = 75;
        rig.Feed.Think();

        Assert.Equal([(PickupHistory.Kind.Ammo, 45, "ammo_smg1"), (PickupHistory.Kind.Ammo, 45, "ammo_smg1")],
                     Rows(rig.History).Select(e => (e.Type, e.Count, e.Icon)));
    }

    [Fact]
    public void Ammo_whose_weapon_takes_no_ammo_pickups_is_not_listed()
    {
        var rig = Made();
        rig.Player.State.Ammo[Ammo.WireIndex("Pistol")] = 18;

        rig.Feed.Think();

        Assert.Empty(Rows(rig.History));
    }

    [Fact]
    public void An_item_pickup_with_an_icon_is_listed_and_one_without_is_not()
    {
        var rig = Made();

        rig.Items.Raise("item_ammo_crate");
        rig.Items.Raise("item_battery");

        var row = Assert.Single(Rows(rig.History));
        Assert.Equal((PickupHistory.Kind.Item, "item_battery"), (row.Type, row.Icon));
    }

    [Fact]
    public void A_refusal_is_listed_with_its_ammo_s_icon()
    {
        var rig = Made();

        rig.Denied.Raise(Ammo.WireIndex("Buckshot"));

        var row = Assert.Single(Rows(rig.History));
        Assert.Equal((PickupHistory.Kind.AmmoDenied, "ammo_buckshot"), (row.Type, row.Icon));
    }

    [Fact]
    public void A_carried_weapon_with_nothing_to_fire_is_drawn_in_caution_and_one_with_ammo_is_not()
    {
        var rig = Made();
        rig.Player.State.CarriedWeapons = (1u << Type(EntityNames.WeaponSmg1)) | (1u << Type(EntityNames.WeaponPistol));
        rig.Player.State.EmptyWeapons = 1u << Type(EntityNames.WeaponSmg1);
        rig.Feed.Think();
        var ui = new PickupHistoryUi(rig.History, rig.Feed);

        var rows = Enumerable.Range(0, ui.SlotCount).Select(s => ui.Row(s, out var r) ? (r.Icon, r.Caution) : default).ToArray();

        Assert.Equal([("pistol", false), ("smg1", true)], rows.OrderBy(r => r.Icon, StringComparer.Ordinal));
    }

    [Fact]
    public void A_refusal_is_drawn_full_in_caution_and_a_pickup_with_its_count()
    {
        var rig = Made();
        rig.Denied.Raise(Ammo.WireIndex("Buckshot"));
        rig.Player.State.Ammo[Ammo.WireIndex("SMG1")] = 45;
        rig.Feed.Think();
        var ui = new PickupHistoryUi(rig.History, rig.Feed);

        Assert.True(ui.Row(0, out var refused));
        Assert.True(ui.Row(1, out var picked));

        Assert.Equal(("ammo_buckshot", 0, true, true), (refused.Icon, refused.Count, refused.Full, refused.Caution));
        Assert.Equal(("ammo_smg1", 45, false, false), (picked.Icon, picked.Count, picked.Full, picked.Caution));
        Assert.Equal(1f, picked.Opacity);
    }

    [Fact]
    public void A_row_whose_ammo_has_no_icon_is_not_drawn()
    {
        var rig = Made();
        rig.Denied.Raise(Ammo.Count + 1);
        var ui = new PickupHistoryUi(rig.History, rig.Feed);

        Assert.True(ui.Showing);
        Assert.False(ui.Row(0, out _));
    }

    [Fact]
    public void A_new_level_empties_the_history_and_lists_what_the_player_spawns_with_again()
    {
        var rig = Made();
        int smg = Ammo.WireIndex("SMG1");
        rig.Player.State.CarriedWeapons = 1u << Type(EntityNames.WeaponSmg1);
        rig.Player.State.Ammo[smg] = 45;
        rig.Feed.Think();
        rig.History.AddItem("item_battery");

        ((IClientLevelHook)rig.Feed).OnLevelInit("next");
        Assert.Empty(Rows(rig.History));
        rig.Feed.Think();

        Assert.Equal([(PickupHistory.Kind.Weapon, "smg1"), (PickupHistory.Kind.Ammo, "ammo_smg1")],
                     Rows(rig.History).Select(e => (e.Type, e.Icon)));
    }
}
