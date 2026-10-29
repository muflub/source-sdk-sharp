using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// THE PLUS WEAPON SELECTION (stock HL2's CHudWeaponSelection, hud_fastswitch
/// 2), round the crosshair: each bucket an arm of boxes out from the centre,
/// 1 up, 2 right, 3 down, 4 left, 5 and 6 the lower diagonals, a box per
/// position up to the furthest carried, an empty place drawn empty. The
/// selected box is outlined and its icon glows as it is reached; the others
/// sit at half the selection's fade. No names: stock's plus draws none.
/// </summary>
public sealed class WeaponPlusPanel(IWeaponPlusSource plus) : UiPanel
{
    /// <summary>The arms (MAX_WEAPON_SLOTS).</summary>
    public const int Arms = 6;

    /// <summary>A box and its icon, in du (stock's MediumBox 75 by 50 at 480 lines, smaller here).</summary>
    public const float WideDu = 128, TallDu = 84, IconDu = 48;

    /// <summary>The room kept round the grid when it is shrunk to fit a small screen.</summary>
    private const Space Margin = Space.XL;

    /// <summary>The glow's strongest wash over the selected box.</summary>
    public const float GlowOpacity = 0.35f;

    /// <summary>Paint's xModifiers: which way along x each arm runs.</summary>
    public static int Dx(int slot) => slot switch { 1 or 5 => 1, 3 or 4 => -1, _ => 0 };

    /// <summary>Paint's yModifiers: which way along y each arm runs (down is positive).</summary>
    public static int Dy(int slot) => slot switch { 0 => -1, 2 or 4 or 5 => 1, _ => 0 };

    public override void Describe(ref UiFrame ui)
    {
        var p = plus;
        if (!p.Showing)
            return;

        Span<int> last = stackalloc int[Arms];
        int reachX = 0, reachY = 0;
        for (int slot = 0; slot < Arms; slot++)
        {
            last[slot] = p.LastPosition(slot);
            int reach = last[slot] + 1;
            if (Dx(slot) != 0)
                reachX = Math.Max(reachX, reach);
            if (Dy(slot) != 0)
                reachY = Math.Max(reachY, reach);
        }

        if (reachX == 0 && reachY == 0)
            return;

        // The grid's boxes and gaps, shrunk to fit a screen too small for them.
        int columns = 2 * reachX + 1, rows = 2 * reachY + 1;
        float gap = (float)Space.S, room = 2 * (float)Margin;
        float fitWide = (ui.DesignWidth - room - (columns - 1) * gap) / columns;
        float fitTall = (ui.DesignHeight - room - (rows - 1) * gap) / rows;
        float scale = Math.Min(1f, Math.Min(fitWide / WideDu, fitTall / TallDu));
        float wide = WideDu * scale, tall = TallDu * scale, icon = IconDu * scale;

        // A grid of one pitch, as wide each side of the crosshair's cell as the
        // furthest arm reaches, so that cell is the screen's middle. It is drawn
        // round the crosshair, so it is a layer over it. Rows and cells share
        // the panel's key space: the rows take the first keys, the cells the
        // ones after.
        using (ui.Column(Space.S, Align.Centre).At(UiAnchor.Centre).Opacity(p.Opacity).Layered())
        {
            for (int y = -reachY; y <= reachY; y++)
            {
                using (ui.Row(Space.S, Align.Centre).Key(y + reachY))
                {
                    for (int x = -reachX; x <= reachX; x++)
                    {
                        int key = rows + (y + reachY) * columns + x + reachX;
                        int slot = ArmAt(x, y);
                        int position = Math.Max(Math.Abs(x), Math.Abs(y)) - 1;
                        if (slot < 0 || position > last[slot])
                        {
                            ui.Spacer(Size.Du(wide), Size.Du(tall)).Key(key);
                            continue;
                        }

                        Box(ref ui, slot, position, key, wide, tall, icon);
                    }
                }
            }
        }
    }

    /// <summary>The arm a grid cell lies on (its bucket); -1 for the centre or a cell off every arm.</summary>
    public static int ArmAt(int x, int y)
    {
        if ((x == 0 && y == 0) || (x != 0 && y != 0 && Math.Abs(x) != Math.Abs(y)))
            return -1;

        int sx = Math.Sign(x), sy = Math.Sign(y);
        for (int slot = 0; slot < Arms; slot++)
        {
            if (Dx(slot) == sx && Dy(slot) == sy)
                return slot;
        }

        return -1;
    }

    private void Box(ref UiFrame ui, int slot, int position, int key, float wide, float tall, float icon)
    {
        bool selected = slot == plus.SelectedSlot && position == plus.SelectedPosition;
        bool has = plus.Weapon(slot, position, out var box);

        // An empty place selected is drawn in the danger tone (stock's red bubble).
        var edge = !selected ? Tone.None : has ? Tone.Accent : Tone.Danger;
        using (ui.Box().Key(key).Size(Size.Du(wide), Size.Du(tall)).Fill(Tone.Window).Round(Radius.M)
                 .Border(edge, selected ? 2 : 0)
                 .Opacity(selected ? 1f : WeaponSelectionPanel.UnselectedOpacity))
        {
            if (!has)
                return;

            float glow = selected ? plus.Glow : 0f;
            if (glow > 0f)
                ui.Spacer(Size.Fill, Size.Fill).Fill(Tone.Accent).Round(Radius.M).Opacity(glow * GlowOpacity).Layered();

            var tone = !box.Selectable ? Tone.Danger : selected ? Tone.Accent : Tone.Text;
            ui.Icon(box.Icon, tone, icon).At(UiAnchor.Centre);
        }
    }
}
