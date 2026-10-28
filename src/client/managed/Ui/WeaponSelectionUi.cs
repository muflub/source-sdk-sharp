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
    private string[] _icons = [];
    private string[] _names = [];

    public bool Showing => selection.Showing;

    public float Opacity => selection.Opacity;

    public int OpenSlot => selection.Selected >= 0 ? selection.SlotOf(selection.Selected) : -1;

    public bool SlotHasWeapon(int slot) => selection.SlotHasWeapon(slot);

    public int LastPosition(int slot) => selection.LastPositionInSlot(slot);

    public bool Weapon(int slot, int position, out WeaponSelectionBox box)
    {
        int type = selection.WeaponInSlot(slot, position);
        if (type < 0 || !Strings(type))
        {
            box = default;
            return false;
        }

        box = new WeaponSelectionBox(_icons[type], _names[type], selection.CanBeSelected(type), type == selection.Selected);
        return true;
    }

    private bool Strings(int type)
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

        return type < _icons.Length;
    }
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
