using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// THE WEAPON SELECTION (stock HL2's CHudWeaponSelection, hud_fastswitch 0),
/// top centre: the six buckets in a row, each a small numbered box, dim when
/// nothing is carried in it, and the bucket holding the selected weapon open
/// into a column of large boxes, one per position, each with the weapon's
/// icon and the selected one outlined and named. A weapon with nothing to
/// fire is drawn in the danger tone (stock's red). The whole fades with the
/// selection, and the icons not selected sit at half of it (stock's Alpha
/// 128 under SelectionAlpha 255; the boxes stay solid so their numbers read
/// over a bright wall, where stock's 80-alpha boxes did not).
/// </summary>
public sealed class WeaponSelectionPanel(IWeaponSelectionSource selection) : UiPanel
{
    /// <summary>The bucket count (MAX_WEAPON_SLOTS).</summary>
    public const int Slots = 6;

    /// <summary>A closed bucket's box, and an open bucket's, in du (stock's 32 and 112 by 84 at 480 lines, smaller here).</summary>
    public const float SmallDu = 52, LargeWideDu = 192, LargeTallDu = 120, IconDu = 56;

    /// <summary>The same on a narrow screen, where a bucket of three would reach the crosshair.</summary>
    public const float CompactSmallDu = 32, CompactWideDu = 160, CompactTallDu = 100, CompactIconDu = 40;

    /// <summary>MAX_WEAPON_POSITIONS: the keys an open bucket's boxes take.</summary>
    private const int PositionKeys = 20;

    /// <summary>The menu's Alpha (128) against its SelectionAlpha (255).</summary>
    public const float UnselectedOpacity = 128f / 255f;

    public override void Describe(ref UiFrame ui)
    {
        var s = selection;
        if (!s.Showing)
            return;

        int open = s.OpenSlot;
        bool compact = ui.Narrow;
        float small = compact ? CompactSmallDu : SmallDu;

        // Stock: ypos 16 at 480 lines, centred.
        using (ui.Row(Space.S, Align.Start).At(UiAnchor.Top).Inset(Space.None, compact ? Space.XL : Space.Huge).Opacity(s.Opacity))
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                if (slot == open)
                {
                    using (ui.Column(Space.S).Key(slot))
                    {
                        int last = s.LastPosition(slot);
                        for (int position = 0; position <= last; position++)
                            Box(ref ui, slot, position, numbered: position == 0, compact);
                    }
                }
                else
                {
                    bool carried = s.SlotHasWeapon(slot);
                    using (ui.Box().Key(slot).Size(Size.Du(small), Size.Du(small)).Fill(Tone.Window).Round(Radius.M)
                             .Opacity(carried ? 1f : UnselectedOpacity))
                    {
                        if (carried)
                            ui.Number(slot + 1, Text.Label, Tone.Accent).At(UiAnchor.TopLeft).Inset(Space.XS, Space.XS);
                    }
                }
            }
        }
    }

    private void Box(ref UiFrame ui, int slot, int position, bool numbered, bool compact)
    {
        float wide = compact ? CompactWideDu : LargeWideDu;
        float tall = compact ? CompactTallDu : LargeTallDu;
        float icon = compact ? CompactIconDu : IconDu;

        bool has = selection.Weapon(slot, position, out var box);
        bool selected = has && box.Selected;

        // Keyed apart from the buckets' boxes, which share the row's id space.
        int key = Slots + slot * PositionKeys + position;
        using (ui.Box().Key(key).Size(Size.Du(wide), Size.Du(tall)).Fill(Tone.Window).Round(Radius.M)
                 .Border(selected ? Tone.Accent : Tone.None, selected ? 2 : 0)
                 .Opacity(has ? 1f : UnselectedOpacity))
        {
            if (numbered)
                ui.Number(slot + 1, Text.Label, Tone.Accent).At(UiAnchor.TopLeft).Inset(Space.XS, Space.XS);

            if (!has)
                return;

            var tone = !box.Selectable ? Tone.Danger : selected ? Tone.Accent : Tone.Text;
            if (!selected)
            {
                ui.Icon(box.Icon, tone, icon).At(UiAnchor.Centre).Opacity(UnselectedOpacity);
                return;
            }

            // The selected one is named under its icon, from the bottom up so a
            // two-line name (stock's "SMG\n(SUBMACHINE GUN)") clears the number.
            using (ui.Column(Space.XS, Align.Centre).At(UiAnchor.Bottom).Inset(Space.None, Space.S))
            {
                ui.Icon(box.Icon, tone, icon);
                ui.Text(box.Name, Text.Label, tone).Elide().MaxWidth(wide - 16).TextAlign(Align.Centre);
            }
        }
    }
}
