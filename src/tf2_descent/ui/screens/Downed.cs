using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// The downed overlay (R26, plan §2.12): the local hero is down, so the
/// centre-bottom of the screen becomes the bleed-out clock — a Display
/// number in danger ink — with the revive meter when a member is actually
/// on you (name, progress, the drag-to-safety caption) or the nearest
/// member's name and distance when nobody is close yet. The party's pills
/// ride along so the answer to "who can reach me" is on the dying screen,
/// not on the HUD's frames. The HUD's own corner is bottom-left; this panel
/// is centre-bottom and width-clamped, so the two never share a column even
/// at the narrow sizes. It draws only while downed.
/// </summary>
public sealed class Downed(
    IDescentDowned downed,
    IDescentParty party,
    PartyPill pill) : UiScreen
{
    public override string Name => "downed";
    public override UiLayerKind Layer => UiLayerKind.Hud;

    public override void Describe(ref UiFrame ui)
    {
        if (!downed.Downed)
            return;
        bool narrow = ui.Narrow;

        // Centre-bottom, clear of the HUD's bottom-left corner (its column is
        // a ~264-du panel at a 12 du inset, so nothing centred reaches it at
        // the regular sizes). On a narrow screen — where the design width can
        // get down to ~730 du — the panel takes the bottom-right instead:
        // centred there it would sit on that corner column.
        var box = ui.Box().Fill(Tone.Surface).Border(Tone.Danger, 1).Round(Radius.M)
            .Pad(Space.S, Space.M).Key("downed");
        if (narrow)
            box.At(UiAnchor.BottomRight).Inset(Space.M, Space.M);
        else
            box.At(UiAnchor.Bottom).Inset(Space.None, Space.Huge);
        using (box)
            using (ui.Column(Space.XS, Align.Centre))
            {
                using (ui.Row(Space.S, Align.Centre))
                {
                    ui.Icon(UiShape.Cross45, Tone.Danger, 16);
                    ui.Text("#Descent_Downed_BleedingOut", Text.Caption, Tone.Warning);
                }

                // The clock also drains as a hairline: total → zero, so the
                // eye reads the slope without re-reading the digits.
                float bleed = downed.BleedOutTotal > 0f
                    ? System.Math.Clamp(downed.BleedOutSecondsLeft / downed.BleedOutTotal, 0f, 1f) : 1f;
                ui.Meter(ui.Animate("bleed", bleed, Motion.Standard), Tone.Danger, Size.Fill, 4);
                int seconds = (int)System.Math.Ceiling(System.Math.Max(0f, downed.BleedOutSecondsLeft));
                using (ui.Row(Space.XS, Align.End))
                {
                    ui.Number(seconds, Text.Display, Tone.Danger);
                    ui.Text("#Descent_Downed_Seconds", Text.Caption, Tone.Text);
                }

                if (downed.BeingRevived)
                {
                    // Someone is on you: who, how far along, and the drag caption.
                    using (ui.Row(Space.XS, Align.Centre))
                    {
                        ui.Data(downed.ReviverName, Text.Caption, Tone.Accent).Elide();
                        ui.Text("#Descent_Downed_Reviving", Text.Caption, Tone.Text);
                    }
                    float f = System.Math.Clamp(downed.ReviveFraction, 0f, 1f);
                    ui.Meter(ui.Animate("revive", f, Motion.Standard), Tone.Accent, Size.Fill, 4);
                    ui.Text("#Descent_Downed_DragToSafety", Text.Caption, Tone.Warning);
                }
                else if (downed.NearestMemberName.Length > 0)
                {
                    // Nobody close yet: name the closest throat to yell at.
                    using (ui.Row(Space.XS, Align.Centre))
                    {
                        ui.Text("#Descent_Downed_Nearest", Text.Caption, Tone.Text);
                        ui.Data(downed.NearestMemberName, Text.Caption, Tone.Text).Elide();
                        ui.Text($"{downed.NearestMemberDistance}u", Text.Caption, Tone.Warning);
                    }
                }

                // Who can revive: the party's frames (the local hero is the
                // clock, not a pill). The reviver wears the Focus frame and
                // the reviving check; narrow, every pill goes compact. One
                // left-aligned block, centred as a whole: the reviver's check
                // makes its pill wider, and centred row by row the icons and
                // bars staggered by half of it.
                using (ui.Column(Space.XS, Align.Start))
                    for (int i = 0; i < party.MemberCount; i++)
                    {
                        var m = party.Member(i);
                        if (m.Local)
                            continue;
                        bool reviving = downed.BeingRevived && m.Name == downed.ReviverName;
                        ui.Sub(i, pill, new PartyPillProps(m, reviving && !narrow, reviving));
                    }
            }
    }
}
