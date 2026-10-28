using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>The weapon selection as the HUD panel reads it: the open bucket, each weapon's icon, name and state.</summary>
public class WeaponSelectionUiTests
{
    private static readonly WeaponCatalog Weapons = WeaponCatalog.Stock();
    private static readonly int Smg = Weapons.IndexOf(EntityNames.WeaponSmg1), Ar2 = Weapons.IndexOf(EntityNames.WeaponAr2);

    private sealed class World : IWeaponSelectionPlayer, IWeaponSelectionActions, IWeaponSlots, IGameClock
    {
        public bool Exists => true;
        public bool HudAllowed => true;
        public bool FrozenOrDead => false;
        public uint Carried => (1u << Smg) | (1u << Ar2);
        public uint Empty { get; set; }
        public int Active => -1;
        public float Now => 10f;
        public void Select(int weaponType) { }
        public void LastWeapon() { }
        public void Sound(string soundName) { }
        public int Count => Weapons.Count;
        public int Slot(int t) => t == Smg || t == Ar2 ? 2 : -1;
        public int Position(int t) => t == Ar2 ? 1 : 0;
    }

    private static (WeaponSelectionUi Ui, World World) Open()
    {
        var world = new World();
        var hud = new WeaponSelectionHud(world, world, world, world, () => 0);
        hud.SlotCommand(3);
        var names = new WeaponNames(c => c == EntityNames.WeaponSmg1 ? "#HL2_SMG1" : null, t => t == "#HL2_SMG1" ? "SMG" : null);
        return (new WeaponSelectionUi(hud, Weapons, names), world);
    }

    [Fact]
    public void The_open_bucket_is_the_selected_weapons()
    {
        var (ui, _) = Open();

        Assert.True(ui.Showing);
        Assert.Equal(2, ui.OpenSlot);
        Assert.Equal(1, ui.LastPosition(2));
        Assert.True(ui.SlotHasWeapon(2) && !ui.SlotHasWeapon(0));
    }

    [Fact]
    public void A_weapon_is_its_icon_its_name_and_whether_it_is_on_the_selection()
    {
        var (ui, _) = Open();

        Assert.True(ui.Weapon(2, 0, out var smg));
        Assert.Equal(new WeaponSelectionBox("smg1", "SMG", true, true), smg);
        Assert.True(ui.Weapon(2, 1, out var ar2));
        Assert.Equal(("ar2", false), (ar2.Icon, ar2.Selected));
        Assert.False(ui.Weapon(2, 2, out _));
    }

    [Fact]
    public void An_empty_weapon_is_not_selectable()
    {
        var (ui, world) = Open();
        world.Empty = 1u << Ar2;

        ui.Weapon(2, 1, out var ar2);

        Assert.False(ar2.Selectable);
    }
}
