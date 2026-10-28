using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The pickup history as the HUD draws it: CHudHistoryResource::Paint's
/// choice of what each row shows. A weapon with nothing to fire and a refused
/// ammo pickup are in caution; an ammo pickup shows its count; a row whose
/// ammo has no icon is not drawn.
/// </summary>
public sealed class PickupHistoryUi(PickupHistory history, LocalPickupHistory local) : IPickupHistorySource
{
    public bool Showing => history.Showing;

    public int SlotCount => history.Slots.Count;

    public bool Row(int slot, out PickupRow row)
    {
        row = default;
        if (slot < 0 || slot >= history.Slots.Count)
            return false;

        PickupHistory.Entry e = history.Slots[slot];
        if (e.Type == PickupHistory.Kind.Empty || e.Icon.Length == 0)
            return false;

        bool caution = e.Type switch
        {
            PickupHistory.Kind.Weapon => e.Id is >= 0 and < 32 && (local.EmptyWeapons & (1u << e.Id)) != 0,
            PickupHistory.Kind.AmmoDenied => true,
            _ => false,
        };

        row = new PickupRow(e.Icon, e.Type == PickupHistory.Kind.Ammo ? e.Count : 0, e.Type == PickupHistory.Kind.AmmoDenied,
                            caution, history.Opacity(slot));
        return true;
    }
}

/// <summary>No pickup history: a composition without the client's (the samples' and tests' hosts).</summary>
public sealed class NoPickupHistory : IPickupHistorySource
{
    public bool Showing => false;
    public int SlotCount => 0;

    public bool Row(int slot, out PickupRow row)
    {
        row = default;
        return false;
    }
}
