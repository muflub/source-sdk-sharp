using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The weapon selection as the HUD panel reads it: <see cref="WeaponSelectionHud"/>'s
/// state, each weapon's icon (its classname without <c>weapon_</c>, the kill
/// feed's icon set) and its display name, both made once per type.
/// </summary>
public sealed class WeaponSelectionUi(WeaponSelectionHud selection, WeaponCatalog weapons, WeaponNames names)
    : IWeaponSelectionSource
{
    private readonly WeaponStrings _strings = new(weapons, names);

    public bool Showing => selection.Showing;

    public float Opacity => selection.Opacity;

    public int OpenSlot => selection.Selected >= 0 ? selection.SlotOf(selection.Selected) : -1;

    public bool SlotHasWeapon(int slot) => selection.SlotHasWeapon(slot);

    public int LastPosition(int slot) => selection.LastPositionInSlot(slot);

    public bool Weapon(int slot, int position, out WeaponSelectionBox box)
    {
        int type = selection.WeaponInSlot(slot, position);
        if (!_strings.Has(type))
        {
            box = default;
            return false;
        }

        box = new WeaponSelectionBox(_strings.Icon(type), _strings.Name(type), selection.CanBeSelected(type), type == selection.Selected);
        return true;
    }
}

/// <summary>The plus selection as its panel reads it: the arms are the buckets, the selection its arm and place.</summary>
public sealed class WeaponPlusUi(WeaponSelectionHud selection, WeaponCatalog weapons, WeaponNames names) : IWeaponPlusSource
{
    private readonly WeaponStrings _strings = new(weapons, names);

    public bool Showing => selection.PlusShowing;

    public float Opacity => selection.Opacity;

    public float Glow => selection.Glow;

    public int SelectedSlot => selection.PlusSlot;

    public int SelectedPosition => selection.PlusPosition;

    public int LastPosition(int slot) => selection.LastPositionInSlot(slot);

    public bool Weapon(int slot, int position, out WeaponSelectionBox box)
    {
        int type = selection.WeaponInSlot(slot, position);
        if (!_strings.Has(type))
        {
            box = default;
            return false;
        }

        bool selected = slot == selection.PlusSlot && position == selection.PlusPosition;
        box = new WeaponSelectionBox(_strings.Icon(type), _strings.Name(type), selection.CanBeSelected(type), selected);
        return true;
    }
}

/// <summary>The carousel as its panel reads it: the boxes the last wheel step laid out, each the weapon carried there now.</summary>
public sealed class WeaponCarouselUi(WeaponSelectionHud selection, WeaponCatalog weapons, WeaponNames names) : IWeaponCarouselSource
{
    private readonly WeaponStrings _strings = new(weapons, names);

    public bool Showing => selection.CarouselShowing;

    public float Opacity => selection.Opacity;

    public float Glow => selection.Glow;

    public int Count => selection.CarouselCount;

    public int Selected => selection.CarouselSelected;

    public float Offset => selection.CarouselOffset;

    public bool Weapon(int box, out WeaponSelectionBox weapon)
    {
        int type = selection.CarouselWeapon(box);
        if (!_strings.Has(type))
        {
            weapon = default;
            return false;
        }

        weapon = new WeaponSelectionBox(_strings.Icon(type), _strings.Name(type), selection.CanBeSelected(type),
                                        box == selection.CarouselSelected);
        return true;
    }
}

/// <summary>
/// Each weapon type's icon (its classname without <c>weapon_</c>, the kill
/// feed's icon set) and display name, made once per type.
/// </summary>
public sealed class WeaponStrings(WeaponCatalog weapons, WeaponNames names)
{
    private string[] _icons = [];
    private string[] _names = [];

    /// <summary>Whether the type is one the catalog names.</summary>
    public bool Has(int type)
    {
        if (_icons.Length != weapons.Count)
        {
            _icons = new string[weapons.Count];
            _names = new string[weapons.Count];
            for (int t = 0; t < _icons.Length; t++)
            {
                string name = weapons.ByIndex(t)?.Name ?? string.Empty;
                _icons[t] = name.StartsWith("weapon_", StringComparison.Ordinal) ? name["weapon_".Length..] : name;
                _names[t] = names.Display(name);
            }
        }

        return type >= 0 && type < _icons.Length;
    }

    public string Icon(int type) => _icons[type];

    public string Name(int type) => _names[type];
}

/// <summary>No weapon selection (a host composed without the client's): never shown.</summary>
public sealed class NoWeaponSelection : IWeaponSelectionSource
{
    public bool Showing => false;
    public float Opacity => 0f;
    public int OpenSlot => -1;
    public bool SlotHasWeapon(int slot) => false;
    public int LastPosition(int slot) => -1;

    public bool Weapon(int slot, int position, out WeaponSelectionBox box)
    {
        box = default;
        return false;
    }
}

/// <summary>No plus selection: never shown.</summary>
public sealed class NoWeaponPlus : IWeaponPlusSource
{
    public bool Showing => false;
    public float Opacity => 0f;
    public float Glow => 0f;
    public int SelectedSlot => -1;
    public int SelectedPosition => 0;
    public int LastPosition(int slot) => -1;

    public bool Weapon(int slot, int position, out WeaponSelectionBox box)
    {
        box = default;
        return false;
    }
}

/// <summary>No carousel: never shown.</summary>
public sealed class NoWeaponCarousel : IWeaponCarouselSource
{
    public bool Showing => false;
    public float Opacity => 0f;
    public float Glow => 0f;
    public int Count => 0;
    public int Selected => -1;
    public float Offset => 0f;

    public bool Weapon(int box, out WeaponSelectionBox weapon)
    {
        weapon = default;
        return false;
    }
}
