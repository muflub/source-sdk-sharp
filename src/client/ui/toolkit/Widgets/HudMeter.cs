using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>What a <see cref="HudMeter"/> shows.</summary>
/// <param name="LabelToken">The small label, e.g. #SS_UI_Health.</param>
/// <param name="Icon">The icon left of the number.</param>
/// <param name="Value">The number.</param>
/// <param name="Max">Full, for the bar.</param>
/// <param name="LowAt">At or below this the meter turns to the danger tone.</param>
/// <param name="Tone">The tone when not low.</param>
public readonly record struct HudMeterProps(string LabelToken, UiShape Icon, int Value, int Max, int LowAt, Tone Tone);

/// <summary>
/// A HUD meter (plan_client_ui §5.8.2): icon, big number and label on a panel,
/// with a bar under them that eases to the value (health draining reads as a
/// drain, not a jump). Turns to the danger tone at or below
/// <see cref="HudMeterProps.LowAt"/>. Use for health, armour, any 0..max value
/// the player watches.
/// </summary>
public sealed class HudMeter : UiPanel<HudMeterProps>
{
    public override void Describe(ref UiFrame ui, in HudMeterProps p)
    {
        bool low = p.Value <= p.LowAt;
        var tone = low ? Tone.Danger : p.Tone;
        float target = p.Max > 0 ? Math.Clamp(p.Value / (float)p.Max, 0f, 1f) : 0f;
        float shown = ui.Animate("bar", target, Motion.Standard);

        // Compact on a narrow screen (ui.Narrow): a smaller panel, the number in
        // the Title role instead of Number.
        bool compact = ui.Narrow;
        using (ui.Panel(gap: Space.XS).Width(Size.Du(compact ? 176 : 232))
                 .Pad(compact ? Space.M : Space.L, compact ? Space.S : Space.M))
        {
            using (ui.Row(compact ? Space.S : Space.M, Align.Centre))
            {
                ui.Icon(p.Icon, tone, compact ? 16 : 24);
                ui.Number(p.Value, compact ? Text.Title : Text.Number, tone);
                ui.Flex();
                ui.Label(p.LabelToken);
            }
            ui.Meter(shown, tone, Size.Fill, 4);
        }
    }
}
