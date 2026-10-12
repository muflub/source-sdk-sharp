using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// The descent HUD (plan §Descent.Client, Appendix C.4): the twin orbs, XP
/// bar, potion belt and party frames in the bottom-left corner like Diablo's,
/// the boss bar top-centre, the gear warning and cast bar bottom-centre, and
/// the corner minimap top-right. One screen, one budget: its whole life is
/// the in-game look. The sub-panels draw their content; this screen owns all
/// anchoring, so nothing can drift onto anything else.
/// </summary>
public sealed class DescentHud(
    OrbPair orbs,
    XpBar xpBar,
    BeltStrip belt,
    PartyFramesPanel party,
    BossBarPanel boss,
    CastBarPanel cast,
    GearWarnPanel gear,
    AutomapCanvas minimap) : UiScreen
{
    public override string Name => "descenthud";
    public override UiLayerKind Layer => UiLayerKind.Hud;

    /// <summary>The narrow corner column's width, du (the panel, padding included).</summary>
    public const float NarrowColumnDu = 264;

    /// <summary>What a part in the narrow column may span: the panel inside its padding.</summary>
    public const float NarrowContentDu = NarrowColumnDu - 2 * (float)Space.S;

    public override void Describe(ref UiFrame ui)
    {
        // Bottom-left corner, Diablo's stack on one opaque panel: party frames
        // above the orbs, then XP, then the belt. The panel is what gives every
        // caption in it the theme's contrast over the world.
        bool narrow = ui.Narrow;

        // Bottom-left corner, Diablo's stack on one opaque panel. On a narrow
        // screen the minimap, gear toast and cast bar join the column too —
        // centred or top-right they would collide with the boss bar there.
        using (ui.Box().At(UiAnchor.BottomLeft).Inset(Space.M, Space.M)
                 .Width(narrow ? Size.Du(NarrowColumnDu) : Size.Auto)
                 .Fill(Tone.Surface).Round(Radius.M).Pad(Space.S, Space.S).Key("corner"))
            using (ui.Column(Space.S, Align.Start))
            {
                if (narrow)
                {
                    ui.Sub("minimap", minimap, new AutomapProps(140, 140, HeroHeading: true));
                    ui.Sub("gear", gear);
                    ui.Sub("cast", cast);
                }
                ui.Sub("party", party);
                ui.Sub("orbs", orbs);
                ui.Sub("xp", xpBar);
                ui.Sub("belt", belt);
            }

        ui.Sub("boss", boss);   // boss bar, top-centre when engaged

        if (!narrow)
        {
            // The gear toast over the cast bar, bottom-centre over the belt row.
            using (ui.Column(Space.S, Align.Centre).At(UiAnchor.Bottom).Inset(Space.None, Space.XXL))
            {
                ui.Sub("gear", gear);
                ui.Sub("cast", cast);
            }

            // Corner minimap, top-right: the plan §2.2's 240×180 du
            // panel; the hero reads as an accent triangle (HeroHeading).
            using (ui.Box().At(UiAnchor.TopRight).Inset(Space.M, Space.M)
                     .Fill(Tone.Surface).Border(Tone.Line, 1).Round(Radius.M).Pad(Space.S, Space.S))
                ui.Sub("minimap", minimap, new AutomapProps(240, 180, HeroHeading: true));
        }
    }
}

/// <summary>The party frames: content only; the HUD stacks them in the corner column.</summary>
public sealed class PartyFramesPanel(IDescentParty party, PartyPill pill) : UiPanel
{
    public override void Describe(ref UiFrame ui)
    {
        if (party.MemberCount == 0)
            return;
        using (ui.Column(Space.XS, Align.Start).Fill(Tone.Surface).Round(Radius.M).Pad(Space.S, Space.S))
        {
            for (int i = 0; i < party.MemberCount; i++)
                ui.Sub(i, pill, new PartyPillProps(party.Member(i), false, false));
        }
    }
}

/// <summary>The boss bar: the engaged boss's name, life meter and phase caption, top-centre.</summary>
public sealed class BossBarPanel(IDescentBoss boss) : UiPanel
{
    public override void Describe(ref UiFrame ui)
    {
        if (!boss.Present || boss.Name.Length == 0)
            return;
        using (ui.Box().At(UiAnchor.Top).Inset(Space.None, Space.XXL).Size(Size.Du(ui.Narrow ? 320 : 400), Size.Du(56))
                 .Fill(Tone.Surface).Border(Tone.Line, 1).Round(Radius.M).Pad(Space.S, Space.M).Key("boss"))
        {
            using (ui.Column(Space.XS, Align.Start))
            {
                using (ui.Row(Space.S, Align.Start))
                {
                    ui.Icon(UiShape.Shield, Tone.Danger, 16);
                    // Caption (not Caption + Heading + Body): the HUD's type budget is 4 roles.
                    ui.Data(boss.Name, Text.Caption, Tone.Text).Elide();
                }
                float f = boss.LifeMax > 0 ? System.Math.Clamp(boss.Life / (float)boss.LifeMax, 0f, 1f) : 1f;
                ui.Meter(ui.Animate("boss", f, Motion.Standard), Tone.Danger, Size.Fill, 12);
                // The plan's phase ticks: one chip per phase, the current one
                // Danger, the rest Line (the belt keycaps' chip family).
                if (boss.PhaseCount > 1)
                    using (ui.Row(Space.XS, Align.Start))
                        for (int t = 0; t < boss.PhaseCount; t++)
                            ui.Rect(t == boss.Phase ? (UiPaint)Tone.Danger : Tone.Line, Size.Du(12), Size.Du(4));
            }
        }
    }
}

/// <summary>The spell name and its channel meter while a cast is in flight (2o): content only.
/// Narrow, it rides the HUD's corner column, so it spans that column's content width.</summary>
public sealed class CastBarPanel(IDescentCastBar casting) : UiPanel
{
    public override void Describe(ref UiFrame ui)
    {
        if (!casting.Casting)
            return;
        using (ui.Box().Size(Size.Du(ui.Narrow ? DescentHud.NarrowContentDu : 320), Size.Du(48))
                 .Fill(Tone.Surface).Border(Tone.Line, 1).Round(Radius.M).Pad(Space.S, Space.M).Key("cast"))
        {
            // Fill: an auto column is as wide as the spell's name, and the
            // meter's Fill track with it (97 of 320 du for "Chain Lightning").
            using (ui.Column(Space.XS, Align.Start).Width(Size.Fill))
            {
                ui.Data(casting.SpellName, Text.Caption, Tone.Text).Elide();
                float f = System.Math.Clamp(casting.Fraction, 0f, 1f);
                ui.Meter(f, casting.Interrupted ? Tone.Danger : Tone.Accent, Size.Fill, 4);
            }
        }
    }
}

/// <summary>The gear warning: a toast while worn gear runs low on durability (R10),
/// with the item's wear as a meter — the fraction that put it on the toast.
/// Narrow, it rides the corner column: it spans that column and the item's
/// name elides in what the caption and meter leave.</summary>
public sealed class GearWarnPanel(IDescentGearWarning warn) : UiPanel
{
    public override void Describe(ref UiFrame ui)
    {
        if (!warn.Present)
            return;
        bool narrow = ui.Narrow;
        using (ui.Box().Width(narrow ? Size.Du(DescentHud.NarrowContentDu) : Size.Auto)
                 .Fill(Tone.Surface).Border(Tone.Warning, 1).Round(Radius.M).Pad(Space.S, Space.M).Key("gear"))
            using (ui.Row(Space.S, Align.Centre).Width(narrow ? Size.Fill : Size.Auto))
            {
                ui.Icon(UiShape.Cross45, Tone.Warning, 16);
                ui.Data(warn.ItemName, Text.Caption).Elide().Width(narrow ? Size.Fill : Size.Auto);
                ui.Text("#Descent_Hud_GearWear", Text.Caption, Tone.Warning);
                float wear = System.Math.Clamp(warn.Fraction, 0f, 1f);
                ui.Meter(wear, Tone.Warning, Size.Du(48), 4);
            }
    }
}
