using SourceSharp.Client;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The pickup history's icons: every weapon a player can carry, every stock
/// ammo type and the three items stock gives an icon map to art the UI's icon
/// set has, so no row the history lists draws nothing.
/// </summary>
public class UiPickupHistoryIconTests
{
    private static readonly UiIconSet Set = UiIconSet.Embedded();

    [Fact]
    public void Every_weapon_in_the_catalog_has_art()
    {
        var names = WeaponCatalog.Stock().All.Select(w => PickupIcons.Weapon(w.Name)).ToArray();

        Assert.True(names.Length >= 12, $"only {names.Length} weapons in the catalog");
        Assert.All(names, n => Assert.True(Set.IndexOf(n) >= 0, $"no art for weapon '{n}'"));
    }

    [Fact]
    public void Every_stock_ammo_type_has_art()
    {
        var ammo = AmmoCatalog.Stock();
        var names = ammo.All.Select(a => (a.Name, Icon: PickupIcons.Ammo(a.Name))).ToArray();

        Assert.True(names.Length >= 11, $"only {names.Length} ammo types");
        Assert.All(names, n => Assert.True(n.Icon.Length > 0 && Set.IndexOf(n.Icon) >= 0, $"no art for ammo '{n.Name}' ('{n.Icon}')"));
    }

    [Fact]
    public void The_items_with_an_icon_have_art_and_others_have_none()
    {
        Assert.All(new[] { "item_battery", "item_healthkit", "item_healthvial" },
                   n => Assert.True(PickupIcons.Item(n) is { } icon && Set.IndexOf(icon) >= 0, $"no art for '{n}'"));
        Assert.Null(PickupIcons.Item("item_ammo_crate"));
    }
}
