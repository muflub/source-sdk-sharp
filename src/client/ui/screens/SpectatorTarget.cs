using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// Who a spectator is watching, bottom centre: a stripe in the target's team
/// colour, the name biggest, then health (icon, number and bar, the danger tone
/// when low or dead) and the weapon in hand. Nothing when there is no target.
/// Compact on a narrow screen, as the HUD's meters are.
/// </summary>
public sealed class SpectatorTarget(ISpectatorTargetSource target) : UiScreen
{
    public override string Name => "spectarget";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    public override void Describe(ref UiFrame ui)
    {
        var t = target;
        if (!t.HasTarget)
            return;

        bool compact = ui.Narrow;
        bool dead = t.Health <= 0;
        var healthTone = dead || t.Health <= t.MaxHealth / 4 ? Tone.Danger : Tone.Accent;
        float fraction = t.MaxHealth > 0 ? Math.Clamp(t.Health / (float)t.MaxHealth, 0f, 1f) : 0f;
        float shown = ui.Animate("health", fraction, Motion.Standard);

        using (ui.Box().Size(Size.Fill, Size.Fill).Layered())
        using (ui.Panel(compact ? Space.M : Space.L).At(UiAnchor.Bottom).Inset(Space.None, Space.Huge)
                 .Width(Size.Du(compact ? 320 : 400)).Name("spectator target"))
        using (ui.Row(Space.M, Align.Stretch).Width(Size.Fill))
        {
            ui.Rect(TeamTone(t.Team), Size.Du(4), Size.Fill).Round(Radius.S).Name("team stripe");
            using (ui.Column(Space.S, Align.Start).Width(Size.Fill))
            {
                ui.Label("#SS_UI_Spec_Watching");
                ui.Data(t.Name, Text.Title).Elide().Width(Size.Fill);
                // A fixed height, so the panel keeps its size when the target dies.
                using (ui.Row(Space.S, Align.Centre).Width(Size.Fill).Height(Size.Du(28)))
                {
                    ui.Icon(UiShape.Cross, healthTone, 16);
                    if (dead)
                    {
                        // No number and no empty bar: the word says it.
                        ui.Label("#SS_UI_Dead", Tone.Danger);
                    }
                    else
                    {
                        ui.Number(t.Health, Text.Heading, healthTone);
                        ui.Meter(shown, healthTone, Size.Fr(), 4);
                        if (t.WeaponName.Length > 0)
                            ui.Text(t.WeaponName, Text.Label, Tone.TextMuted).Pad(Space.S, Space.None);
                    }
                }
            }
        }
    }

    /// <summary>The team's colour: HL2DM's Combine and Rebels; a plain line for no team.</summary>
    private static Tone TeamTone(int team) => team switch
    {
        2 => Tone.TeamA,
        3 => Tone.TeamB,
        _ => Tone.Line,
    };
}
