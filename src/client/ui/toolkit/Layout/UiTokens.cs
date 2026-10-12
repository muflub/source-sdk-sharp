using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

// DESIGN TOKENS (plan_client_ui §5.8.1). The public API takes these, not
// numbers and colours: a screen says "Space.M" and "Tone.Accent", the theme
// says what they are. Raw values exist (Raw.*) and the lint lists every use.

/// <summary>
/// Spacing on a 4 du scale. The value IS the size in design units, so
/// <c>(float)Space.L</c> is 16.
/// </summary>
public enum Space
{
    None = 0,
    XS = 4,
    S = 8,
    M = 12,
    L = 16,
    XL = 24,
    XXL = 32,
    Huge = 48,
}

/// <summary>Corner radius, in design units.</summary>
public enum Radius
{
    None = 0,
    S = 2,
    M = 4,
    L = 8,
}

/// <summary>
/// Type roles. A screen uses at most four (the lint counts). Sizes, weights and
/// families are the theme's.
/// </summary>
public enum Text : byte
{
    /// <summary>The one huge thing: a round timer, a winner banner.</summary>
    Display,
    /// <summary>A screen's title.</summary>
    Title,
    /// <summary>A section or group heading: a team name.</summary>
    Heading,
    /// <summary>Running text: names, messages, chat.</summary>
    Body,
    /// <summary>Small upper-case labels: column headers, "HEALTH".</summary>
    Label,
    /// <summary>Big numerals: health, ammo, score.</summary>
    Number,
    /// <summary>Fine print: build stamp, ping, hints' second line.</summary>
    Caption,
    /// <summary>Monospaced: console output and input, convar names and values.</summary>
    Code,
}

/// <summary>Colour roles. A screen uses at most six (the lint counts).</summary>
public enum Tone : byte
{
    /// <summary>No colour: nothing is drawn.</summary>
    None,
    /// <summary>The translucent dark base of a panel over the world.</summary>
    Surface,
    /// <summary>A panel on a panel: header rows, the local player's row, buttons.</summary>
    SurfaceRaised,
    /// <summary>A hairline: dividers and outlines.</summary>
    Line,
    /// <summary>Primary text.</summary>
    Text,
    /// <summary>Secondary text: labels, captions, zero values.</summary>
    TextMuted,
    /// <summary>The house colour: the amber of the Source HUD.</summary>
    Accent,
    Positive,
    Warning,
    Danger,
    TeamA,
    TeamB,
    /// <summary>The dimming behind a modal.</summary>
    Scrim,
    /// <summary>Text on an accent fill.</summary>
    OnAccent,
    /// <summary>
    /// A window's surface: opaque, so a window fully covers the windows beneath
    /// it (a translucent Surface let the one below show through).
    /// </summary>
    Window,
}

/// <summary>How far a surface stands off what is under it.</summary>
public enum Elevation : byte
{
    Flat,
    /// <summary>A soft drop shadow: panels over the world.</summary>
    Raised,
    /// <summary>A heavier one: dialogs and overlays.</summary>
    Overlay,
}

/// <summary>Easing durations for <see cref="UiFrame.Animate"/>.</summary>
public enum Motion : byte
{
    Instant,
    /// <summary>Hover fades, small value changes.</summary>
    Quick,
    /// <summary>Panels in and out, meters draining.</summary>
    Standard,
    /// <summary>Slow drift: damage fade.</summary>
    Slow,
}

/// <summary>A main- or cross-axis alignment.</summary>
public enum Align : byte
{
    Start,
    Centre,
    End,
    /// <summary>Cross axis only: take the whole width (height) of the container.</summary>
    Stretch,
    /// <summary>Main axis only: first and last at the ends, the rest spread between.</summary>
    SpaceBetween,
}

/// <summary>A stack's direction.</summary>
public enum Axis : byte
{
    Vertical,
    Horizontal,
}

/// <summary>A paint: a tone (with an opacity) or, through <see cref="Raw"/>, a literal colour.</summary>
public readonly record struct UiPaint(Tone Tone, float Opacity, UiColor RawColor, bool IsRaw, bool IsGame = false)
{
    public static implicit operator UiPaint(Tone tone) => new(tone, 1f, default, false);

    /// <summary>
    /// A colour the game supplied (a map's game_text colour, a team's colour
    /// from the server): drawn as given, not a design escape, so the lint's
    /// raw rule does not list it (its contrast is still checked).
    /// </summary>
    public static UiPaint FromGame(UiColor color) => new(Tone.None, 1f, color, true, true);

    /// <summary>The same paint at <paramref name="opacity"/> of its alpha.</summary>
    public UiPaint WithOpacity(float opacity) => this with { Opacity = Opacity * opacity };

    public bool IsNone => !IsRaw && Tone == Tone.None;
}

/// <summary>A length in design units: a <see cref="Space"/> token or, through <see cref="Raw"/>, a number.</summary>
public readonly record struct UiLength(float Du, bool IsRaw)
{
    public static implicit operator UiLength(Space space) => new((float)space, false);
}

/// <summary>A size on one axis.</summary>
/// <remarks><c>default</c> is <see cref="Size.Auto"/>.</remarks>
public readonly record struct UiSize(UiUnit Unit, float Value);

/// <summary>Sizes. Widths and heights are layout, not style, so du are allowed here.</summary>
public static class Size
{
    public static UiSize Auto => new(UiUnit.Auto, 0);
    public static UiSize Fill => new(UiUnit.Fill, 1);
    public static UiSize Du(float du) => new(UiUnit.Du, du);
    public static UiSize Percent(float percent) => new(UiUnit.Percent, percent);
    public static UiSize Fr(float share = 1) => new(UiUnit.Fraction, share);
}

/// <summary>
/// The escape hatch: a literal colour or length outside the tokens. Every use
/// is listed by the lint (rule <c>raw</c>), so a reviewer sees each one.
/// </summary>
public static class Raw
{
    public static UiPaint Color(byte r, byte g, byte b, byte a = 255) => new(Tone.None, 1f, new UiColor(r, g, b, a), true);

    public static UiLength Du(float du) => new(du, true);
}

/// <summary>Which way a scroll region scrolls.</summary>
public enum ScrollAxes : byte
{
    Vertical,
    Horizontal,
    Both,
}
