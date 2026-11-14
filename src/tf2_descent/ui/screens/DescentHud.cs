using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// The descent HUD (plan §Descent.Client, Appendix C.4): the twin orbs and
/// potion belt in the bottom-left corner like Diablo's (the belt beside the
/// orbs, so the corner stays under TF2's chat), the party frames top-left
/// (Diablo II's place for them: in the corner column a party of two reached
/// a row into TF2's chat), the XP bar across the top (plan_descent_logic L5)
/// with the boss bar under it, the six skill slots bottom-centre with the
/// gear warning and cast bar above them, and the corner minimap top-right
/// under TF2's kill feed once the automap has a room to show. One screen,
/// one budget: its whole life is the in-game look. The sub-panels draw their
/// content; this screen owns all anchoring, so nothing can drift onto
/// anything else.
/// </summary>
public sealed class DescentHud(
    OrbPair orbs,
    XpBar xpBar,
    BeltStrip belt,
    PartyFramesPanel party,
    BossBarPanel boss,
    CastBarPanel cast,
    GearWarnPanel gear,
    AutomapCanvas minimap,
    IDescentAutomap map,
    SkillSlotsStrip skills) : UiScreen
{
    public override string Name => "descenthud";
    public override UiLayerKind Layer => UiLayerKind.Hud;

    /// <summary>
    /// TF2's player status corner (its class portrait and health cross, the
    /// element TF2's HUD names <c>tf.playerstatus</c>): the life orb is the
    /// hero's health, and the corner column stands where that corner was.
    /// TF2's ammo, crosshair, chat and kill feed stay.
    /// </summary>
    public const string TFPlayerStatus = "tf.playerstatus";

    /// <summary>TF2's team status across the top centre (<c>tf.teamstatus</c>):
    /// the XP bar stands there, and a hero's party is the party frames.</summary>
    public const string TFTeamStatus = "tf.teamstatus";

    private readonly string[] _takes = [TFPlayerStatus, TFTeamStatus];
    public override IReadOnlyList<string> Takes => _takes;

    /// <summary>The narrow corner column's width, du (the panel, padding included).</summary>
    public const float NarrowColumnDu = 264;

    /// <summary>What a part in the narrow column may span: the panel inside its padding.</summary>
    public const float NarrowContentDu = NarrowColumnDu - 2 * (float)Space.S;

    /// <summary>
    /// How far down TF2's kill feed reaches at the top right, du (TFKillFeed's stock
    /// numbers: its first bar at (18 + 16 + 1) x 2.25, four bars of 14 x 2.25 with the
    /// column's 12-du gaps, hudlayout.res's MaxDeathNotices 4): about 241 du, and the
    /// corner minimap hangs a gap under it so both stay readable.
    /// </summary>
    public const float KillFeedBandDu = 256;

    /// <summary>The gear warning and cast bar ride this far up, above the skill strip's two rows.</summary>
    public const float OverSkillsDu = 2 * SkillSlotsStrip.CellDu + 3 * (float)Space.S + (float)Space.M;

    public override void Describe(ref UiFrame ui)
    {
        // Bottom-left corner, Diablo's stack on one opaque panel: party frames
        // above the orbs, the belt beside them (under them when narrow). The
        // panel is what gives every caption in it the theme's contrast over
        // the world. Kept low: TF2's chat sits above this corner.
        bool narrow = ui.Narrow;

        // Bottom-left corner, Diablo's stack on one opaque panel. On a narrow
        // screen the minimap, gear toast and cast bar join the column too —
        // centred or top-right they would collide with the boss bar there.
        using (ui.Box().At(UiAnchor.BottomLeft).Inset(Space.M, Space.S)
                 .Width(narrow ? Size.Du(NarrowColumnDu) : Size.Auto)
                 .Fill(Tone.Surface).Round(Radius.M).Pad(Space.S, Space.S).Key("corner"))
            using (ui.Column(Space.S, Align.Start))
            {
                if (narrow)
                {
                    if (map.RoomCount > 0)
                        ui.Sub("minimap", minimap, new AutomapProps(140, 140, HeroHeading: true));
                    ui.Sub("gear", gear);
                    ui.Sub("cast", cast);
                    ui.Sub("party", party);
                    ui.Sub("orbs", orbs);
                    ui.Sub("belt", belt);
                    ui.Sub("skills", skills, new SkillSlotsProps(Compact: true));
                }
                else
                {
                    using (ui.Row(Space.S, Align.End))
                    {
                        ui.Sub("orbs", orbs);
                        ui.Sub("belt", belt);
                    }
                }
            }

        // Top-centre, the XP bar across the top of the screen (L5); the boss
        // bar hangs under it when one is engaged.
        using (ui.Column(Space.None, Align.Centre).At(UiAnchor.Top).Inset(Space.None, Space.S).Key("top"))
            ui.Sub("xp", xpBar);
        ui.Sub("boss", boss);

        if (!narrow)
        {
            // The six skill slots bottom-centre on their own panel.
            using (ui.Box().At(UiAnchor.Bottom).Inset(Space.None, Space.S)
                     .Fill(Tone.Surface).Round(Radius.M).Pad(Space.S, Space.S).Key("skillcorner"))
                ui.Sub("skills", skills, default(SkillSlotsProps));

            // The party frames top-left, Diablo II's place: the corner column
            // stays as low as TF2's chat allows.
            if (party.Showing)
                using (ui.Box().At(UiAnchor.TopLeft).Inset(Space.M, Space.M).Key("partycorner"))
                    ui.Sub("party", party);

            // The gear toast over the cast bar, above the skill slots.
            using (ui.Column(Space.S, Align.Centre).At(UiAnchor.Bottom).Inset(Space.None, new UiLength(OverSkillsDu, false)))
            {
                ui.Sub("gear", gear);
                ui.Sub("cast", cast);
            }

            // Corner minimap, top-right under TF2's kill feed (KillFeedBandDu):
            // the plan §2.2's 240×180 du panel; the hero reads as an accent
            // triangle (HeroHeading). Nothing revealed, no panel.
            if (map.RoomCount > 0)
                using (ui.Box().At(UiAnchor.TopRight).Inset(Space.M, new UiLength(KillFeedBandDu, false))
                         .Fill(Tone.Surface).Border(Tone.Line, 1).Round(Radius.M).Pad(Space.S, Space.S))
                    ui.Sub("minimap", minimap, new AutomapProps(240, 180, HeroHeading: true));
        }
    }
}

/// <summary>The party frames: content only; the HUD stacks them in the corner column.</summary>
public sealed class PartyFramesPanel(IDescentParty party, PartyPill pill) : UiPanel
{
    /// <summary>Whether there are frames to show (the HUD gives them a corner only then).</summary>
    public bool Showing => party.MemberCount > 0;

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
        // Under the XP bar across the top (L5).
        using (ui.Box().At(UiAnchor.Top).Inset(Space.None, Space.Huge).Size(Size.Du(ui.Narrow ? 320 : 400), Size.Du(56))
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
