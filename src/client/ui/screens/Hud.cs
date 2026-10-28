using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The HUD (plan_client_ui §7: today's elements re-done on the toolkit), as
/// one sub-panel per element (§5.7a), each in its own full-screen layer:
/// vitals (health and suit meters) bottom left, ammo bottom right, crosshair,
/// damage vignette, a map's text, key hints, the weapon selection (top
/// centre) and the build stamp.
/// Slots: <c>hud.top-left</c>, <c>hud.top-right</c>, <c>hud.bottom-left</c>
/// (above the vitals), <c>hud.bottom-right</c> (above the ammo).
/// </summary>
public sealed class Hud : UiScreen
{
    private readonly IPlayerLife _life;
    private readonly VitalsPanel _vitals;
    private readonly AmmoPanel _ammo;
    private readonly CrosshairPanel _crosshair;
    private readonly DamagePanel _damage;
    private readonly MapTextPanel _mapText;
    private readonly HintsPanel _hints;
    private readonly BuildStampPanel _build;
    private readonly KillFeedPanel _kills;
    private readonly VoicePanel _voice;
    private readonly WeaponSelectionPanel _selection;
    private readonly WeaponPlusPanel _plus;
    private readonly WeaponCarouselPanel _carousel;
    private readonly PickupHistoryPanel _history;

    /// <summary>The HUD reads only whether there is a player; each element reads its own sources.</summary>
    public Hud(IPlayerLife life, VitalsPanel vitals, AmmoPanel ammo, CrosshairPanel crosshair, DamagePanel damage,
               MapTextPanel mapText, HintsPanel hints, BuildStampPanel build, KillFeedPanel kills, VoicePanel voice,
               WeaponSelectionPanel selection, WeaponPlusPanel plus, WeaponCarouselPanel carousel, PickupHistoryPanel history)
    {
        _selection = selection;
        _plus = plus;
        _carousel = carousel;
        _history = history;
        _voice = voice;
        _kills = kills;
        _life = life;
        _vitals = vitals;
        _ammo = ammo;
        _crosshair = crosshair;
        _damage = damage;
        _mapText = mapText;
        _hints = hints;
        _build = build;
    }

    public override string Name => "hud";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    public override void Describe(ref UiFrame ui)
    {
        // The stamp first and always: "which build is this" is asked as often
        // in the menu as in play (today's HUD keeps it the same way).
        ui.Sub("build", _build);

        // The kill feed too: stock's shows while dead and between rounds.
        ui.Sub("kills", _kills);
        ui.Sub("voice", _voice);

        // No local player (the menu, between levels): nothing else, rather than
        // a zeroed HUD flashing on every level change.
        if (!_life.Alive)
            return;

        ui.Sub("damage", _damage);
        ui.Sub("crosshair", _crosshair, 5f);

        // The bottom line: vitals left, hints centre, ammo right. On a narrow
        // screen (ui.Narrow) the panels are compact and the hints move above the
        // line, centred, so the line keeps to the corners.
        using (ui.Box().Size(Size.Fill, Size.Fill).Layered())
        {
            if (!ui.Narrow)
            {
                // Huge from the bottom: the build stamp is under the line's middle (the hints).
                using (ui.Row(Space.L, Align.End).At(UiAnchor.Bottom).Inset(Space.None, Space.Huge).Width(Size.Fill).Pad(Space.XXL, Space.None))
                {
                    ui.Sub("vitals", _vitals);
                    ui.Flex();
                    ui.Sub("hints", _hints);
                    ui.Flex();
                    ui.Sub("ammo", _ammo);
                }
            }
            else
            {
                // Huge, not XXL, from the bottom: the line spans the screen here,
                // so the build stamp under it needs the room to stay clear.
                using (ui.Column(Space.L, Align.Centre).At(UiAnchor.Bottom).Inset(Space.None, Space.Huge).Width(Size.Fill).Pad(Space.XL, Space.None))
                {
                    ui.Sub("hints", _hints);
                    using (ui.Row(Space.M, Align.End).Width(Size.Fill))
                    {
                        ui.Sub("vitals", _vitals);
                        ui.Flex();
                        ui.Sub("ammo", _ammo);
                    }
                }
            }
        }

        ui.Sub("maptext", _mapText);
        ui.Sub("weaponselection", _selection);
        ui.Sub("weaponplus", _plus);
        ui.Sub("weaponcarousel", _carousel);
        ui.Sub("history", _history);

        // The top corners' slots; the bottom ones sit above the vitals and the ammo, in their panels.
        using (ui.Box().Size(Size.Fill, Size.Fill).Layered())
        {
            ui.Slot("hud.top-left", Space.S).At(UiAnchor.TopLeft).Inset(Space.XXL);
            ui.Slot("hud.top-right", Space.S).At(UiAnchor.TopRight).Inset(Space.XXL);
        }
    }
}

/// <summary>
/// Health and suit, bottom left: two <see cref="HudMeter"/>s side by side, the
/// <see cref="AuxPowerPanel"/> above them while it shows.
/// </summary>
public sealed class VitalsPanel : UiPanel
{
    private readonly IPlayerHealth _healthSource;
    private readonly IPlayerArmour _armourSource;
    private readonly AuxPowerPanel _aux;

    public VitalsPanel(IPlayerHealth health, IPlayerArmour armour, AuxPowerPanel aux)
    {
        _healthSource = health;
        _armourSource = armour;
        _aux = aux;
    }

    private readonly HudMeter _health = new();
    private readonly HudMeter _suit = new();

    /// <summary>The meters' height, for what is placed above them; compact on a narrow screen.</summary>
    public const float HeightDu = 84, CompactHeightDu = 64;

    public override void Describe(ref UiFrame ui)
    {
        var h = _healthSource;
        var a = _armourSource;
        using (ui.Column(Space.L, Align.Start))
        {
            ui.Slot("hud.bottom-left", Space.S);
            ui.Sub("aux", _aux);
            using (ui.Row(Space.M, Align.End))
            {
                ui.Sub("health", _health, new HudMeterProps("#SS_UI_Health", UiShape.Cross, h.Health, h.MaxHealth, h.MaxHealth / 4, Tone.Accent));
                if (a.Armour > 0)
                    ui.Sub("suit", _suit, new HudMeterProps("#SS_UI_Suit", UiShape.Shield, a.Armour, a.MaxArmour, 0, Tone.Accent));
            }
        }
    }
}

/// <summary>
/// The suit's AUX POWER (stock hud_suitpower.cpp), above the vitals: a bar of
/// ten chunks, the ones the power has left lit and the rest dim, in the danger
/// tone at a quarter or less, and a line for each device that draws on it as
/// stock lists them (OXYGEN under water, FLASHLIGHT; stock's SPRINT line asks
/// IsSprinting, which nothing sets, so it never shows). It fades in when the
/// power drops below full and out when it is full again
/// (SuitAuxPowerNotMax / SuitAuxPowerMax).
/// </summary>
public sealed class AuxPowerPanel : UiPanel
{
    private readonly IPlayerSuitPower _suit;

    public AuxPowerPanel(IPlayerSuitPower suit)
    {
        _suit = suit;
    }

    /// <summary>BarWidth / (BarChunkWidth + BarChunkGap): 92 / 9.</summary>
    public const int Chunks = 10;

    /// <summary>A chunk and the bar, at 1080 lines (stock's 6 and 4 at 480, on the 4 du grid).</summary>
    public const float ChunkDu = 12, BarDu = 8;

    /// <summary>AuxPowerDisabledAlpha against the colour's own: 70 of 220.</summary>
    public const float DimOpacity = 70f / 220f;

    /// <summary>bits_SUIT_DEVICE_FLASHLIGHT and bits_SUIT_DEVICE_BREATHER.</summary>
    public const int Flashlight = 0x2, Breather = 0x4;

    /// <summary>The chunks lit at <paramref name="power"/>: stock's rounding.</summary>
    public static int Lit(float power) => (int)(Chunks * (power / 100f) + 0.5f);

    public override void Describe(ref UiFrame ui)
    {
        float power = _suit.SuitPower;
        float shown = ui.Animate("fade", power < 100f ? 1f : 0f, Motion.Standard);
        if (shown <= 0.001f)
            return;

        int lit = Lit(power);
        var tone = lit <= Chunks / 4 ? Tone.Danger : Tone.Accent;
        UiPaint on = tone;
        UiPaint dim = ((UiPaint)tone).WithOpacity(DimOpacity);
        int devices = _suit.SuitDevices;

        using (ui.Panel(gap: Space.XS).Pad(Space.M, Space.S).Opacity(shown))
        {
            ui.Label("#SS_UI_AuxPower", tone);
            using (ui.Row(Space.XS, Align.Centre))
            {
                for (int i = 0; i < Chunks; i++)
                    ui.Rect(i < lit ? on : dim, Size.Du(ChunkDu), Size.Du(BarDu));
            }
            if ((devices & Breather) != 0)
                ui.Label("#SS_UI_Oxygen", tone);
            if ((devices & Flashlight) != 0)
                ui.Label("#SS_UI_Flashlight", tone);
        }
    }
}

/// <summary>
/// The weapon in hand, bottom right, built like a <see cref="HudMeter"/> so the
/// two sit level: the clip big with the reserve after it, the weapon's name as
/// the label, and, when the weapon has one, the secondary ammunition beside the
/// primary block as (current, max): one pip per round up to five, "2 / 3" past
/// that, the danger tone at none. A weapon with no secondary draws nothing
/// there and reserves no room. A weapon with no ammunition (the crowbar) shows
/// its name only; an empty clip turns to the danger tone.
/// </summary>
public sealed class AmmoPanel : UiPanel
{
    private readonly IPlayerWeapon _weapon;
    private readonly IPlayerAmmo _ammo;

    public AmmoPanel(IPlayerWeapon weapon, IPlayerAmmo ammo)
    {
        _weapon = weapon;
        _ammo = ammo;
    }

    /// <summary>Up to this many, the secondary is drawn as pips; more, as numbers.</summary>
    public const int MaxPips = 5;

    public override void Describe(ref UiFrame ui)
    {
        if (!_weapon.HasWeapon)
            return;
        var p = _ammo;

        bool hasAmmo = p.Clip >= 0 || p.Reserve >= 0;
        var tone = p.Clip == 0 ? Tone.Danger : Tone.Accent;

        using (ui.Column(Space.L, Align.End))
        {
            ui.Slot("hud.bottom-right", Space.S);
            bool compact = ui.Narrow;   // as the HudMeters beside it
            // A weapon with nothing to count (the crowbar) has a panel its
            // name's size; the ammo panel's width would push the name to its far side.
            bool counts = hasAmmo || p.Secondary >= 0;
            using (ui.Panel(gap: Space.XS).MinWidth(counts ? (compact ? 176 : 232) : 0)
                     .Pad(compact ? Space.M : Space.L, compact ? Space.S : Space.M))
            {
                using (ui.Row(compact ? Space.S : Space.M, Align.Centre).Width(Size.Fill))
                {
                    if (hasAmmo)
                    {
                        ui.Icon(UiShape.Bullet, tone, compact ? 16 : 24);
                        ui.Number(p.Clip >= 0 ? p.Clip : p.Reserve, compact ? Text.Title : Text.Number, tone);
                        if (p.Clip >= 0)
                            ui.Text($"/ {p.Reserve}", Text.Body, Tone.TextMuted);
                    }
                    if (p.Secondary >= 0)
                        Secondary(ref ui, p.Secondary, p.SecondaryMax);
                    if (counts)
                        ui.Flex();
                    ui.Text(_weapon.WeaponName, Text.Label, Tone.TextMuted);
                }

                // Where a meter has its bar: kept, so the panel sits level with the vitals.
                ui.Spacer(Size.Du(1), Size.Du(4));
            }
        }
    }

    /// <summary>The secondary block: a divider, then pips (or "n / max") over an ALT label.</summary>
    private static void Secondary(ref UiFrame ui, int current, int max)
    {
        var tone = current == 0 ? Tone.Danger : Tone.Accent;
        ui.Rect(Tone.Line, Size.Du(1), Size.Du(ui.Narrow ? 32 : 40)).Name("secondary divider");
        using (ui.Column(Space.XS, Align.Start).Name("secondary"))
        {
            if (max > 0 && max <= MaxPips)
            {
                using (ui.Row(Space.XS).Height(Size.Du(16)))
                {
                    for (int i = 0; i < max; i++)
                    {
                        bool full = i < current;
                        ui.Rect(full ? (UiPaint)tone : Tone.SurfaceRaised, Size.Du(12), Size.Fill).Round(Radius.S)
                          .Border(full ? Tone.None : tone, 1).Key(i).Name(full ? "pip full" : "pip empty");
                    }
                }
            }
            else if (max > 0)
            {
                ui.Text($"{current} / {max}", Text.Label, tone).Name("secondary count");
            }
            else
            {
                ui.Number(current, Text.Label, tone).Name("secondary count");
            }
            ui.Text("#SS_UI_Alt", Text.Label, current == 0 ? Tone.Danger : Tone.TextMuted);
        }
    }
}

/// <summary>The crosshair: four ticks around a gap (props: the gap in du), outlined to read over a bright wall.</summary>
public sealed class CrosshairPanel : UiPanel<float>
{
    public override void Describe(ref UiFrame ui, in float gapDu)
        => ui.Shape(UiShape.Crosshair, ((UiPaint)Tone.Text).WithOpacity(0.85f), 32, gapDu).At(UiAnchor.Centre);
}

/// <summary>
/// The damage vignette: the danger tone glowing in from the edge the damage
/// came from (and its neighbours for a hit from a diagonal), fading over a
/// second; every edge evenly when the game does not say where from.
/// </summary>
public sealed class DamagePanel : UiPanel
{
    private readonly IPlayerDamage _damage;

    public DamagePanel(IPlayerDamage damage) => _damage = damage;

    public const double FadeSeconds = 1.0;

    public override void Describe(ref UiFrame ui)
    {
        var p = _damage;
        double since = ui.Now - p.LastDamageAt;
        if (double.IsInfinity(since) || since < 0 || since >= FadeSeconds)
            return;

        float fresh = (float)(1 - since / FadeSeconds);
        float yaw = p.LastDamageYaw;
        // Per edge: full on the side it came from, falling to nothing 90 degrees
        // away, so the opposite edge stays clear and the glow points. (It was a
        // 0.35 floor on every edge, which read as no direction: UiDamageTests.)
        float left = Edge(yaw, -90), right = Edge(yaw, 90), front = Edge(yaw, 0), back = Edge(yaw, 180);

        using (ui.Box().Size(Size.Fill, Size.Fill).Layered().Opacity(fresh))
        {
            if (front > 0f) ui.Rect(((UiPaint)Tone.Danger).WithOpacity(0.55f * front), Size.Fill, Size.Du(140)).At(UiAnchor.Top).Fade(UiFade.Vertical).Layered();
            if (back > 0f) ui.Rect(((UiPaint)Tone.Danger).WithOpacity(0.55f * back), Size.Fill, Size.Du(140)).At(UiAnchor.Bottom).Fade(UiFade.Vertical, 1f, 0f).Layered();
            if (left > 0f) ui.Rect(((UiPaint)Tone.Danger).WithOpacity(0.55f * left), Size.Du(180), Size.Fill).At(UiAnchor.Left).Fade(UiFade.Horizontal).Layered();
            if (right > 0f) ui.Rect(((UiPaint)Tone.Danger).WithOpacity(0.55f * right), Size.Du(180), Size.Fill).At(UiAnchor.Right).Fade(UiFade.Horizontal, 1f, 0f).Layered();
        }
    }

    private static float Edge(float yaw, float at)
    {
        if (float.IsNaN(yaw))
            return 0.6f;
        float d = MathF.Abs(((yaw - at) % 360 + 540) % 360 - 180);
        return MathF.Max(0f, 1f - d / 90f);
    }
}

/// <summary>A map's text (game_text, env_message) where the map placed it, each line on a pill so it reads over anything.</summary>
public sealed class MapTextPanel : UiPanel
{
    private readonly IMapTextSource _text;

    public MapTextPanel(IMapTextSource text) => _text = text;

    public override void Describe(ref UiFrame ui)
    {
        var t = _text;
        for (int i = 0; i < t.MessageCount; i++)
        {
            var m = t.Message(i);
            using (ui.Box().Size(Size.Fill, Size.Fill).Pad(Space.Huge).Layered())
            using (ui.Panel(Space.M, Space.None, Elevation.Flat).PlaceFraction(m.X, m.Y).Opacity(m.Alpha).Key(i).MaxWidth(900))
                ui.Data(m.Text, Text.Body).Color(UiPaint.FromGame(m.Color)).Wrap(860);
        }
    }
}

/// <summary>Key hints (env_hudhint) bottom centre, above the vitals' line, each with its key as a keycap.</summary>
public sealed class HintsPanel : UiPanel
{
    private readonly IKeyHintSource _hints;

    public HintsPanel(IKeyHintSource hints) => _hints = hints;

    public override void Describe(ref UiFrame ui)
    {
        var t = _hints;
        if (t.HintCount == 0)
            return;
        using (ui.Panel(Space.M, Space.S).Pad(ui.Narrow ? Space.M : Space.L, ui.Narrow ? Space.S : Space.L))   // tighter when narrow
            for (int i = 0; i < t.HintCount; i++)
                ui.KeyHintData(t.HintKey(i), t.Hint(i)).Key(i);
    }
}

/// <summary>Which build this is, on a small pill at the bottom; the debug line (when and where) at the top right.</summary>
public sealed class BuildStampPanel : UiPanel
{
    private readonly IBuildStampSource _stamp;

    public BuildStampPanel(IBuildStampSource stamp) => _stamp = stamp;

    public override void Describe(ref UiFrame ui)
    {
        var s = _stamp;
        if (!s.Visible)
            return;
        using (ui.Box().At(UiAnchor.Bottom).Inset(Space.None, Space.XS).Fill(Tone.Surface).Round(Radius.M).Pad(Space.S, Space.XS))
            ui.Data(s.Line, Text.Caption, s.Stale ? Tone.Accent : Tone.TextMuted);
        if (s.DebugLine.Length > 0)
            using (ui.Box().At(UiAnchor.TopRight).Inset(Space.S).Fill(Tone.Surface).Round(Radius.M).Pad(Space.S, Space.XS))
                ui.Data(s.DebugLine, Text.Caption, Tone.TextMuted);
    }
}
