namespace SourceSharp.Abi;

/// <summary>
/// An RGBA colour, eight bits a channel. Source's <c>color32</c>, and named for
/// it.
///
/// <para>
/// Its own type rather than a tuple so the channel order cannot be transposed at
/// a call site — the HUD drawing API takes eight loose ints across the ABI, and
/// (r,g,b,a) versus (a,r,g,b) is exactly the sort of mistake that produces a
/// plausible-looking wrong colour.
/// </para>
///
/// <para>
/// It was HudColor until a map keyvalue wanted one too. A colour a mapper types
/// is the same thing as a colour the HUD draws, and a second RGBA record beside
/// this one would have been two descriptions of one idea for no reason but the
/// old name.
/// </para>
/// </summary>
public readonly record struct Color32(int R, int G, int B, int A = 255)
{
    /// <summary>Returns the same colour at a different opacity.</summary>
    public Color32 WithAlpha(int alpha) => this with { A = Math.Clamp(alpha, 0, 255) };

    /// <summary>Scales opacity by a 0..1 fraction, clamped.</summary>
    public Color32 Fade(float fraction)
        => WithAlpha((int)(A * Math.Clamp(fraction, 0f, 1f)));

    public static readonly Color32 White  = new(255, 255, 255);
    public static readonly Color32 Red    = new(255,  64,  64);
    public static readonly Color32 Amber  = new(255, 176,   0);
    public static readonly Color32 Green  = new(160, 255, 160);
}
