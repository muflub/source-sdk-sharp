using System.Globalization;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// Stock .res coordinates into toolkit placements. A vgui element's
/// <c>xpos</c>/<c>ypos</c> is a prefix plus a number in the 640x480 space the
/// .res files are written in: plain counts from the parent's left/top, "r"
/// from the parent's RIGHT or BOTTOM edge to the element's LEFT or TOP edge
/// (vgui Panel.cpp ComputePos: <c>nNewPos = nParentSize - nPosDelta</c>), "c"
/// about the parent's centre. The toolkit's <c>AtRes</c> offsets point inward
/// from an anchor, in du, so a resolved spot answers both shapes.
/// </summary>
public static class StockRes
{
    /// <summary>The du per stock unit: the 480-tall canvas into 1080.</summary>
    public const float Scale = 2.25f;
}

/// <summary>A resolved rect of the stock space, in units, measured up from
/// the screen's bottom-left — a parent panel's own box for nesting.</summary>
/// <param name="Left">The left edge, units from the screen's left.</param>
/// <param name="Bottom">The bottom edge, units above the screen's bottom.</param>
/// <param name="Wide">The width, units.</param>
/// <param name="Tall">The height, units.</param>
public readonly record struct StockRect(float Left, float Bottom, float Wide, float Tall)
{
    /// <summary>The canvas the root .res panels fill.</summary>
    public static readonly StockRect Screen = new(0f, 0f, 640f, 480f);
}

/// <summary>Where an element landed: its edges in du from its parent's
/// (left, right, bottom, top), the offset of its centre from the parent's
/// centre, and its size — the AtRes arguments each corner takes.</summary>
public readonly record struct StockSpot(
    float Left,
    float Right,
    float Bottom,
    float Top,
    float CentreX,
    float Wide,
    float Tall);

/// <summary>One element's box as a .res writes it. Resolve it against the
/// parent panel's own <see cref="StockRect"/>.</summary>
public readonly record struct StockBox
{
    private const byte Plain = 0;
    private const byte Reverse = 1; // "r": from the right / bottom edge
    private const byte Centre = 2;  // "c": about the parent's centre

    private readonly float _x;
    private readonly float _y;
    private readonly byte _xKind;
    private readonly byte _yKind;
    private readonly float _wide;
    private readonly float _tall;

    /// <param name="xpos">The .res xpos: "25", "r95", "c-140".</param>
    /// <param name="ypos">The .res ypos: "35", "r88", "c10".</param>
    /// <param name="wide">The .res wide, units.</param>
    /// <param name="tall">The .res tall, units.</param>
    public StockBox(string xpos, string ypos, float wide, float tall)
    {
        // The .res mixes prefixes across the pair freely (xpos 25, ypos r88).
        (_x, _xKind) = Parse(xpos);
        (_y, _yKind) = Parse(ypos);
        _wide = wide;
        _tall = tall;
    }

    /// <summary>The rect in screen units (for nesting children inside).</summary>
    public StockRect Rect(StockRect parent)
    {
        float left = _xKind switch
        {
            Centre => parent.Left + parent.Wide / 2f + _x,
            Reverse => parent.Left + parent.Wide - _x,
            _ => parent.Left + _x,
        };
        // Up-positive: "r N" puts the TOP N above the parent's bottom.
        float top = _yKind switch
        {
            Centre => parent.Bottom + parent.Tall / 2f - _y,
            Reverse => parent.Bottom + _y,
            _ => parent.Bottom + parent.Tall - _y,
        };
        return new StockRect(left, top - _tall, _wide, _tall);
    }

    /// <summary>The spot in du, relative to the parent's edges and centre.</summary>
    public StockSpot In(StockRect parent)
    {
        StockRect r = Rect(parent);
        float s = StockRes.Scale;
        return new StockSpot(
            (r.Left - parent.Left) * s,
            (parent.Left + parent.Wide - (r.Left + _wide)) * s,
            (r.Bottom - parent.Bottom) * s,
            (parent.Bottom + parent.Tall - (r.Bottom + _tall)) * s,
            (r.Left + _wide / 2f - (parent.Left + parent.Wide / 2f)) * s,
            _wide * s,
            _tall * s);
    }

    // Allocation-free: the screens resolve their boxes every frame, and a
    // substring per prefix would show up as bytes a frame (UiAllocationTests).
    private static (float Value, byte Kind) Parse(string spec)
    {
        byte kind = Plain;
        if (spec.StartsWith('r') || spec.StartsWith('R'))
            kind = Reverse;
        else if (spec.StartsWith('c') || spec.StartsWith('C'))
            kind = Centre;
        float value = kind == Plain
            ? float.Parse(spec, CultureInfo.InvariantCulture)
            : float.Parse(spec.AsSpan(1), CultureInfo.InvariantCulture);
        return (value, kind);
    }
}
