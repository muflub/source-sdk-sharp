namespace SourceSharp.Client.Ui;

// THE RENDER LIST: what a UI frame draws, as plain data (plan_client_ui §2, §4).
//
// A frame of UI is described by screens, laid out by the toolkit and emitted as
// the primitives below; a backend (vgui in game, SVG in tests and mock-ups)
// draws them. Everything here is in PIXELS of the screen it was laid out for:
// design units exist only above the render list. The list is the contract that
// /ui serialises, goldens compare and the fidelity fact feeds to both backends,
// so it holds data only (interface-assemblies-data-only).

/// <summary>A colour, straight (not premultiplied) alpha.</summary>
public readonly record struct UiColor(byte R, byte G, byte B, byte A);

/// <summary>An axis-aligned rectangle in pixels: left, top, width, height.</summary>
public readonly record struct UiRect(float X, float Y, float W, float H);

/// <summary>Distances in from each edge.</summary>
public readonly record struct UiInsets(float Left, float Top, float Right, float Bottom);

/// <summary>A point in pixels.</summary>
public readonly record struct UiVertex(float X, float Y);

/// <summary>Which primitive a <see cref="UiDrawOp"/> names.</summary>
public enum UiPrimitiveKind : byte
{
    Quad,
    TexturedQuad,
    GlyphRun,
    Line,
    Polygon,
    ScissorPush,
    ScissorPop,
}

/// <summary>
/// How a quad's alpha changes across it: vgui's <c>DrawFilledRectFade</c>, the
/// one gradient the surface has, so the only one the list offers.
/// </summary>
public enum UiFade : byte
{
    None,
    Vertical,
    Horizontal,
}

/// <summary>
/// One draw, in order. <see cref="Index"/> points into the array of its kind;
/// <see cref="Element"/> is the element (in <see cref="UiDrawList.Elements"/>)
/// that emitted it, or -1.
/// </summary>
public readonly record struct UiDrawOp(UiPrimitiveKind Kind, int Index, int Element);

/// <summary>
/// A filled rectangle. <see cref="Radius"/> rounds the corners (vgui draws it as
/// a 9-slice texture); a <see cref="BorderWidth"/> above zero strokes the edge
/// inside the rect in <see cref="Border"/>. With a <see cref="Fade"/>, the
/// alpha runs from <see cref="Color"/>'s to <see cref="AlphaEnd"/>.
/// </summary>
public readonly record struct UiQuad(
    UiRect Rect, UiColor Color, float Radius, UiColor Border, float BorderWidth,
    UiFade Fade, byte AlphaEnd);

/// <summary>A texture (by backend id) drawn into a rectangle, tinted.</summary>
public readonly record struct UiTexturedQuad(UiRect Rect, int Texture, UiRect Uv, UiColor Tint);

/// <summary>
/// One line of text. (<see cref="X"/>, <see cref="Y"/>) is the top left of the
/// line box; the backend places the baseline at Y + the font's ascent. The text
/// is <see cref="UiDrawList.Text"/>[TextStart..TextStart+TextLength], and the
/// font is <see cref="UiDrawList.Fonts"/>[Font].
/// </summary>
public readonly record struct UiGlyphRun(
    float X, float Y, int Font, UiColor Color, int TextStart, int TextLength);

/// <summary>A straight line.</summary>
public readonly record struct UiLine(float X0, float Y0, float X1, float Y1, float Width, UiColor Color);

/// <summary>A filled convex polygon: <see cref="UiDrawList.Vertices"/>[VertexStart..+VertexCount].</summary>
public readonly record struct UiPolygon(int VertexStart, int VertexCount, UiColor Color);

/// <summary>Clips everything until the matching pop to <see cref="Rect"/> (intersected with any outer clip).</summary>
public readonly record struct UiScissor(UiRect Rect);

/// <summary>
/// A font as the list asks for it: a family, a pixel height (the backend's
/// fonts are rasterised per pixel height), a weight (400 regular, 700 bold) and
/// flags. The backend maps it to whatever it really has.
/// </summary>
public readonly record struct UiFontSpec(string Family, int PxHeight, int Weight, UiFontFlags Flags);

/// <summary>Rasterisation hints a font is asked for with.</summary>
[Flags]
public enum UiFontFlags : byte
{
    None = 0,
    Antialias = 1,
    DropShadow = 2,
    Outline = 4,
    Additive = 8,
    /// <summary>Digits of equal width: numbers that change do not jitter.</summary>
    Tabular = 16,
}

/// <summary>A font's vertical metrics at its pixel height.</summary>
public readonly record struct UiFontMetrics(float Ascent, float Descent, float LineHeight);

/// <summary>
/// One laid-out element: what /ui reports, what input is hit-tested against,
/// what the lint names. <see cref="Name"/> is
/// <see cref="UiDrawList.Text"/>[NameStart..+NameLength] (empty unless names
/// were recorded, which only previews and tests ask for).
/// </summary>
public readonly record struct UiElementInfo(
    uint Id, int Parent, UiRect Rect, UiElementKind Kind, int NameStart, int NameLength,
    UiElementFlags Flags);

/// <summary>The pointer shapes the UI asks the backend for.</summary>
public enum UiCursor : byte
{
    Default,
    /// <summary>Over a window's title bar: it moves.</summary>
    Move,
    /// <summary>Over a left or right edge.</summary>
    ResizeEW,
    /// <summary>Over a top or bottom edge.</summary>
    ResizeNS,
    /// <summary>Over the top-left or bottom-right corner (the grip).</summary>
    ResizeNWSE,
    /// <summary>Over the top-right or bottom-left corner.</summary>
    ResizeNESW,
}

/// <summary>What an element is, for the lint and the inspector.</summary>
public enum UiElementKind : byte
{
    Box,
    Stack,
    Grid,
    Text,
    Shape,
    Image,
    Spacer,
}

/// <summary>Facts about an element the lint reads.</summary>
[Flags]
public enum UiElementFlags : ushort
{
    None = 0,
    /// <summary>Text that did not fit and was cut with an ellipsis, as asked.</summary>
    Elided = 1,
    /// <summary>Text that did not fit and was NOT allowed to elide.</summary>
    Overflowed = 2,
    /// <summary>Takes clicks: the 32 du target rule applies.</summary>
    Interactive = 4,
    /// <summary>Its paint or size came through <c>Raw.*</c>, outside the tokens.</summary>
    Raw = 8,
    /// <summary>Its text is not a #Token and not marked as data (a name, a number).</summary>
    Unlocalised = 16,
    /// <summary>Interpolated data text carrying a compile-time <c>#</c>: a token
    /// written inside an interpolation, which the localiser can never see.</summary>
    StrandedToken = 256,
    /// <summary>Allowed to overlap its siblings (a badge over a corner, a layer).</summary>
    Overlaps = 32,
    /// <summary>Clips its children.</summary>
    Clips = 64,
    /// <summary>Drawn over the world with nothing opaque behind it: contrast is judged against the backdrops.</summary>
    OverWorld = 128,
}

/// <summary>
/// A whole frame's draw, as spans over the builder's arrays: valid only for the
/// duration of the <see cref="IUiBackend.Draw"/> call that receives it.
/// </summary>
public ref struct UiDrawList
{
    public ScreenInfo Screen;

    /// <summary>The pointer shape this frame asks for (a window being moved or resized); the backend sets it.</summary>
    public UiCursor Cursor;
    public ReadOnlySpan<UiDrawOp> Ops;
    public ReadOnlySpan<UiQuad> Quads;
    public ReadOnlySpan<UiTexturedQuad> TexturedQuads;
    public ReadOnlySpan<UiGlyphRun> GlyphRuns;
    public ReadOnlySpan<UiLine> Lines;
    public ReadOnlySpan<UiPolygon> Polygons;
    public ReadOnlySpan<UiVertex> Vertices;
    public ReadOnlySpan<UiScissor> Scissors;
    public ReadOnlySpan<UiFontSpec> Fonts;
    public ReadOnlySpan<char> Text;
    public ReadOnlySpan<UiElementInfo> Elements;
}
