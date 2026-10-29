using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// THE CAROUSEL WEAPON SELECTION (stock HL2's CHudWeaponSelection,
/// hud_fastswitch 3), top centre: every weapon carried in a strip, the one the
/// wheel switched to in the middle, taller, outlined and named, its icon
/// glowing once the strip has slid to it. Five or more scroll in a window
/// five boxes wide (three on a narrow screen) and wrap round; fewer sit in a
/// row, one of n. The others
/// are shorter, at half the selection's fade; while the strip slides none is
/// drawn as selected.
/// </summary>
public sealed class WeaponCarouselPanel(IWeaponCarouselSource carousel) : UiPanel
{
    /// <summary>MAX_CAROUSEL_SLOTS: the window's width in boxes, and the count from which the strip scrolls.</summary>
    public const int Window = 5;

    /// <summary>The window on a narrow screen, where five and the two half-shown at its edges would not fit.</summary>
    public const int NarrowWindow = 3;

    /// <summary>The selected box and its icon, in du (stock's LargeBox 108 by 72 at 480 lines, smaller here).</summary>
    public const float WideDu = 168, TallDu = 112, IconDu = 52;

    /// <summary>The room kept either side of the window when it is shrunk to fit a narrow screen.</summary>
    private const Space Margin = Space.XL;

    /// <summary>The others are stock's tall / 1.5.</summary>
    public const float UnselectedTallShare = 1f / 1.5f;

    /// <summary>The glow's strongest wash over the selected box.</summary>
    public const float GlowOpacity = 0.35f;

    private const Space Gap = Space.M;

    public override void Describe(ref UiFrame ui)
    {
        var c = carousel;
        if (!c.Showing)
            return;

        // The window's boxes and the two half-shown at its edges as they slide,
        // shrunk to fit a screen too narrow for them.
        int shown = ui.Narrow ? NarrowWindow : Window;
        float fit = (ui.DesignWidth - 2 * (float)Margin - (shown + 1) * (float)Gap) / (shown + 2);
        float scale = Math.Min(1f, fit / WideDu);
        float wide = WideDu * scale, tall = TallDu * scale, icon = IconDu * scale;
        var top = ui.Narrow ? Space.XL : Space.Huge;
        int count = c.Count, selected = c.Selected;

        if (count < Window)
        {
            // draw the selected weapon as a 1 of n style
            using (ui.Row(Gap, Align.Start).At(UiAnchor.Top).Inset(Space.None, top).Opacity(c.Opacity))
            {
                for (int i = 0; i < count; i++)
                    Box(ref ui, i, i, i == selected, wide, tall, icon);
            }

            return;
        }

        // The strip in the window holds the boxes that reach into it, wrapping
        // round: box i's middle sits (offset + i) pitches right of the window's,
        // so it shows while that is under (shown + 1) / 2 pitches less a gap.
        // The strip is placed by its share of the room it leaves: its left edge
        // is ((shown - 1) / 2 + offset + first) pitches from the window's.
        float offset = c.Offset;
        float pitch = wide + (float)Gap;
        float window = shown * pitch - (float)Gap;
        float edge = (shown + 1) / 2f - (float)Gap / pitch;
        int first = (int)MathF.Floor(-edge - offset) + 1, last = (int)MathF.Ceiling(edge - offset) - 1;
        int boxes = last - first + 1;
        float room = (shown - boxes) * pitch;
        float place = room == 0f ? 0f : ((shown - 1) / 2f + offset + first) * pitch / room;

        using (ui.Box().At(UiAnchor.Top).Inset(Space.None, top).Size(Size.Du(window), Size.Du(tall)).Clip().Opacity(c.Opacity))
        {
            using (ui.Row(Gap, Align.Start).Width(Size.Du(boxes * pitch - (float)Gap)).PlaceFraction(place, 0f))
            {
                for (int i = first; i <= last; i++)
                {
                    int box = ((selected + i) % count + count) % count;
                    Box(ref ui, box, i - first, i == 0 && offset == 0f, wide, tall, icon);
                }
            }
        }
    }

    private void Box(ref UiFrame ui, int index, int key, bool selected, float wide, float tall, float icon)
    {
        bool has = carousel.Weapon(index, out var box);
        float height = selected ? tall : tall * UnselectedTallShare;
        using (ui.Box().Key(key).Size(Size.Du(wide), Size.Du(height)).Fill(Tone.Window).Round(Radius.M)
                 .Border(selected ? Tone.Accent : Tone.None, selected ? 2 : 0)
                 .Opacity(selected ? 1f : WeaponSelectionPanel.UnselectedOpacity))
        {
            if (!has)
                return;

            var tone = !box.Selectable ? Tone.Danger : selected ? Tone.Accent : Tone.Text;
            if (!selected)
            {
                ui.Icon(box.Icon, tone, icon).At(UiAnchor.Centre);
                return;
            }

            // The glow washes the icon's part of the box, not the name's.
            float glow = carousel.Glow;
            if (glow > 0f)
                ui.Spacer(Size.Fill, Size.Du(tall * UnselectedTallShare)).At(UiAnchor.Top).Fill(Tone.Accent).Round(Radius.M)
                  .Opacity(glow * GlowOpacity).Layered();

            // The icon where the others' sit, the name under it.
            using (ui.Column(Space.XS, Align.Centre).At(UiAnchor.Top).Inset(Space.None, Space.M))
            {
                ui.Icon(box.Icon, tone, icon);
                ui.Text(box.Name, Text.Label, tone).Elide().MaxWidth(wide - 16).TextAlign(Align.Centre);
            }
        }
    }
}
