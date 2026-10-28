using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The pickup history (stock CHudHistoryResource): what was just picked up,
/// a row per pickup up the right of the screen from its middle, each in its
/// slot so a row does not move when one under it goes. A row is the item's
/// icon with the count an ammo pickup gave, or FULL for a refused one; a
/// weapon with nothing to fire and a refusal are in the danger tone. Each row
/// fades on its own as its time runs out.
/// </summary>
public sealed class PickupHistoryPanel(IPickupHistorySource history) : UiPanel
{
    /// <summary>A row's height, and its icon's, regular and narrow.</summary>
    public const float RowDu = 56, IconDu = 40, CompactRowDu = 44, CompactIconDu = 28;

    public override void Describe(ref UiFrame ui)
    {
        var h = history;
        if (!h.Showing)
            return;

        bool compact = ui.Narrow;
        float row = compact ? CompactRowDu : RowDu;
        float icon = compact ? CompactIconDu : IconDu;

        // From the bottom: the column's foot on the right edge's middle, the
        // first slot lowest; its right edge the ammo panel's (the HUD line's
        // pad, XL when narrow).
        using (ui.Column(Space.S, Align.End).At(UiAnchor.Right, UiAnchor.BottomRight)
                 .Inset(compact ? Space.XL : Space.XXL, Space.None))
        {
            for (int slot = h.SlotCount - 1; slot >= 0; slot--)
            {
                if (!h.Row(slot, out var r))
                {
                    ui.Spacer(Size.Du(icon), Size.Du(row)).Key(slot);
                    continue;
                }

                var tone = r.Caution ? Tone.Danger : Tone.Accent;
                using (ui.Row(Space.M, Align.Centre).Key(slot).Height(Size.Du(row)).Fill(Tone.Window).Round(Radius.M)
                         .Pad(Space.M, Space.None).Opacity(r.Opacity))
                {
                    ui.Icon(r.Icon, tone, icon);
                    if (r.Count > 0)
                        ui.Number(r.Count, Text.Body, tone);
                    else if (r.Full)
                        ui.Text("#SS_UI_AmmoFull", Text.Label, tone);
                }
            }
        }
    }
}
