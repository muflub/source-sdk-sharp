using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>What a node lays out as.</summary>
internal enum NodeKind : byte
{
    /// <summary>Children placed by anchor and offset, each independently.</summary>
    Box,
    /// <summary>Children one after another along an axis.</summary>
    Stack,
    /// <summary>Children flowing into columns, row by row.</summary>
    Grid,
    Text,
    Shape,
    Spacer,
    /// <summary>A texture leaf: the render list's textured quad (ui.Texture).</summary>
    Texture,
}

/// <summary>A vector shape a Shape node draws into its rect.</summary>
public enum UiShape : byte
{
    None,
    /// <summary>An icon of the icon set (src/client/ui/icons), from its atlas; the node's ShapeValue is its index.</summary>
    Art,
    /// <summary>A plus: the health cross.</summary>
    Cross,
    /// <summary>A shield: armour.</summary>
    Shield,
    /// <summary>A cartridge: ammunition.</summary>
    Bullet,
    /// <summary>Four rising bars: connection quality.</summary>
    Signal,
    /// <summary>A filled circle (as an octagon-ish polygon).</summary>
    Dot,
    /// <summary>A right-pointing chevron.</summary>
    Chevron,
    /// <summary>A skull-free "dead" mark: an X.</summary>
    Cross45,
    /// <summary>The crosshair: four ticks around a gap.</summary>
    Crosshair,
    /// <summary>A person: head and shoulders (players, spectators).</summary>
    Person,
    /// <summary>A tick: a checked box.</summary>
    Check,
    /// <summary>A downward chevron: a dropdown, a sort order.</summary>
    ChevronDown,
    /// <summary>An upward chevron: a sort order.</summary>
    ChevronUp,
    /// <summary>A padlock: a passworded server.</summary>
    Lock,
    /// <summary>A star: a favourite.</summary>
    Star,
    /// <summary>A resize grip: three steps of dots, bottom right.</summary>
    Grip,
}

/// <summary>
/// One element of a frame's tree, in the context's arena. A plain struct reset
/// every frame: no objects per element, so describing a screen allocates
/// nothing once the arena has grown to it.
/// </summary>
internal struct UiNode
{
    // Tree.
    public int Parent, FirstChild, LastChild, Next, ChildCount;
    public NodeKind Kind;
    public uint Id;
    public int Depth;

    // Placement in the parent (du).
    public UiSize W, H;
    public UiAnchor ParentAnchor, SelfAnchor;
    public float OffX, OffY;
    public float MinW, MaxW;
    public float PlaceX, PlaceY;        // NaN, or a fraction of the free space (game_text's x/y); -1 centres

    // As a container (du).
    public float PadL, PadT, PadR, PadB;
    public float Gap, RowGap;
    public Axis Axis;
    public Align MainAlign, CrossAlign;
    public int ColumnStart, ColumnCount;
    public float ScrollOffset, ScrollOffsetX;
    public ScrollAxes ScrollAxes;
    public bool Follow;
    public bool Handle;                 // a drag handle (window edge, title, thumb): thin by design
    public bool PopsDesign;
    public bool Movable;                // a window's frame: the player places it, partly off screen if they like             // closing it ends a design-size context (a window's body)                 // a log: stays at the end while it is there
    public int AnchorTarget;            // -1, or the node whose rect an overlay is placed against
    public UiAnchor TargetAnchor;

    // Paint.
    public UiPaint Fill;
    public UiPaint Border;
    public float BorderDu;
    public float RadiusDu;
    public UiFade Fade;
    public float FadeStart, FadeEnd;    // 0..1 of the fill's alpha at the near (top/left) and far end
    public Elevation Elevation;
    public float Opacity;               // multiplies this subtree's alpha
    public bool Clip;
    public bool Scroll;
    public byte ClosesOuter;            // a composite's inner box: closing it closes this many containers around it too

    // Content.
    public int TextStart, TextLength;   // into the context's describe-time text arena
    public Text Role;
    public UiPaint TextPaint;
    public Align TextAlign;
    public bool Elide, IsData, Interpolated, KeepCase;
    public float WrapDu;
    public int Caret, SelStart, SelLength;   // an edited line: caret index (-1: none), selection                // > 0: break lines at this width
    public UiShape Shape;
    public float ShapeValue;            // a shape's parameter: signal bars lit, crosshair gap
    public int Texture;                 // a Texture node's backend texture id
    public UiRect TextureUv;            // and the part of it the leaf samples

    // Facts for the report.
    public UiElementFlags Flags;
    public int KeyNumber;               // -1, or the integer key (a row number)

    // Results, pixels.
    public float MeasuredW, MeasuredH;
    public float X, Y, Wpx, Hpx;
    public int Font;                    // index in the render list's font table, after emit
}
