using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// ROBOT HEALTH BARS (plan_descent_logic UI6; plan_tf2_rpg R25, ruling L6): a small plate over
/// each robot the hero has in his sights or has seen hurt, anchored at its head. What anyone may
/// see always: the name, the life as a bar, a champion's mark and the statuses on it now. What
/// the bestiary has revealed to him, as it is revealed: his kill count, then the life numbers,
/// then the resistances (an immunity named), the champion's affixes, then the temperament style.
/// IMMUNE stands over the plate when his hit drew it. The source places and chooses; this only
/// draws, each plate a fixed width with its foot centred on the point the source gives.
/// </summary>
public sealed class RobotBars(IDescentRobotBars bars) : UiScreen
{
    public override string Name => "robotbars";
    public override UiLayerKind Layer => UiLayerKind.Hud;

    /// <summary>A plate's width, du: four resistance chips two to a row fit it at every scale.</summary>
    public const float PlateDu = 224;

    /// <summary>How far a plate may reach above its foot: the tallest one (every row shown) fits.</summary>
    private const float TallestDu = 168;

    // The resistances two to a row: four in one row overran the plate.
    private readonly UiSize[] _resistColumns = [Size.Fr(), Size.Fr()];

    public override void Describe(ref UiFrame ui)
    {
        int n = bars.BarCount;
        if (n == 0)
            return;

        float plate = PlateDu;
        float w = ui.DesignWidth, h = ui.DesignHeight;
        using (ui.Box().Size(Size.Fill, Size.Fill).Layered().Key("robot-bars"))
            for (int i = 0; i < n; i++)
            {
                var bar = bars.Bar(i);
                float x = Math.Clamp(bar.X * w - plate / 2, (float)Space.S, Math.Max((float)Space.S, w - plate - (float)Space.S));
                float up = Math.Clamp((1f - bar.Y) * h, (float)Space.S, Math.Max((float)Space.S, h - TallestDu));
                using (ui.Column(Space.XS, Align.Start).Key(i).Width(Size.Du(plate)).At(UiAnchor.BottomLeft)
                         .Inset(new UiLength(x, false), new UiLength(up, false)).Layered()
                         .Fill(Tone.Surface).Border(bar.Champion ? Tone.Warning : Tone.Line, 1).Round(Radius.M).Pad(Space.S, Space.XS))
                    Plate(ref ui, i, in bar);
            }
    }

    private void Plate(ref UiFrame ui, int i, in DescentRobotBar bar)
    {
        if (bar.Immune)
            using (ui.Row(Space.XS, Align.Centre))
            {
                ui.Text("#Descent_Bar_Immune", Text.Label, Tone.Danger);
                if (bar.ImmuneToken.Length > 0)
                    ui.Text(bar.ImmuneToken, Text.Caption, Tone.Danger);
            }

        using (ui.Row(Space.XS, Align.Centre).Width(Size.Fill))
        {
            if (bar.Champion)
                ui.Icon(UiShape.Star, Tone.Warning, 12);
            ui.Data(bar.Name, Text.Caption, Tone.Text).Elide().Width(Size.Fill);
            if (bar.Kills >= 0)
            {
                ui.Text("#Descent_Bar_Kills", Text.Caption, Tone.TextMuted);
                ui.Number(bar.Kills, Text.Caption, Tone.Accent);
            }
        }

        ui.Meter(bar.LifeFraction, Tone.Danger, Size.Fill, 8);

        if (bar.LifeMax > 0 || bar.Style.Length > 0)
            using (ui.Row(Space.S, Align.Centre).Width(Size.Fill))
            {
                if (bar.LifeMax > 0)
                    ui.Text($"{bar.Life} / {bar.LifeMax}", Text.Caption, Tone.Text);
                ui.Flex();
                if (bar.Style.Length > 0)
                    ui.Data(bar.Style, Text.Caption, Tone.TextMuted).Elide();
            }

        int resists = bars.ResistCount(i);
        if (resists > 0)
            using (ui.Grid(_resistColumns, Space.XS, Space.XS).Width(Size.Fill))
                for (int r = 0; r < resists; r++)
                {
                    bool immune = bars.ResistImmune(i, r);
                    using (ui.Row(Space.XS, Align.Centre).Key(1000 + i * 16 + r).Width(Size.Fill).Pad(Space.XS, Space.None).Round(Radius.S)
                             .Border(immune || bars.ResistPercent(i, r) < 0 ? Tone.Danger : Tone.Line, 1))
                    {
                        ui.Text(bars.ResistToken(i, r), Text.Caption, immune ? Tone.Danger : Tone.TextMuted);
                        ui.Flex();
                        if (immune)
                            ui.Icon(UiShape.Cross45, Tone.Danger, 12);
                        else
                        {
                            int pct = bars.ResistPercent(i, r);
                            ui.Text($"{pct}%", Text.Caption, Tone.Text);
                        }
                    }
                }

        int affixes = bars.AffixCount(i);
        if (affixes > 0)
            using (ui.Row(Space.XS, Align.Centre).Width(Size.Fill))
                for (int a = 0; a < affixes; a++)
                    ui.Data(bars.AffixName(i, a), Text.Caption, Tone.Warning).Key(2000 + i * 16 + a).Elide();

        int statuses = bars.StatusCount(i);
        if (statuses > 0)
            using (ui.Row(Space.XS, Align.Centre).Width(Size.Fill))
                for (int s = 0; s < statuses; s++)
                    using (ui.Row(Space.None).Key(3000 + i * 16 + s).Fill(((UiPaint)Tone.Warning).WithOpacity(0.18f)).Round(Radius.S).Pad(Space.XS, Space.None))
                        ui.Text(bars.StatusToken(i, s), Text.Caption, Tone.Warning);
    }
}
