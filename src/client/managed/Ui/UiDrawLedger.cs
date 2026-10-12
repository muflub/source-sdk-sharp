using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>What one ledger entry drew; one per element the SVG backend writes for the same list.</summary>
public enum UiLedgerKind : byte
{
    /// <summary>A quad's solid fill (square: a filled rect; rounded: a polygon).</summary>
    Fill,

    /// <summary>A quad's faded fill (a filled rect fade).</summary>
    Fade,

    /// <summary>A quad's border, inside its rect.</summary>
    Border,

    Texture,

    /// <summary>A glyph run; a drop shadow is its own entry, before the run.</summary>
    Text,

    Line,

    Polygon,

    ClipPush,

    ClipPop,
}

/// <summary>
/// One primitive <see cref="VguiBackend"/> drew, in the screen's pixels as they
/// went to the surface: bounds as integer edges, the vgui texture or font
/// handle, the text and the colour. <see cref="Element"/> is the render list's
/// element, so a /ui reader can say which widget drew it.
/// </summary>
public readonly record struct UiLedgerEntry(
    UiLedgerKind Kind, int X0, int Y0, int X1, int Y1, int Texture, int Font,
    int TextStart, int TextLength, UiColor Color, int Element);

/// <summary>
/// THE DRAW LEDGER (plan_client_ui §8 U2): what the last frame drew through the
/// surface, kept so the fidelity fact can hold it against the SVG of the same
/// list and the /ui route can show it. Reused frame to frame: it grows to the
/// largest frame and then allocates nothing.
/// </summary>
public sealed class UiDrawLedger
{
    private UiLedgerEntry[] _entries = new UiLedgerEntry[256];
    private char[] _text = new char[2048];
    private int _textLength;

    public int Count { get; private set; }

    public UiLedgerEntry this[int i] => _entries[i];

    /// <summary>The text an entry drew (empty for anything but text).</summary>
    public ReadOnlySpan<char> TextOf(in UiLedgerEntry e) => _text.AsSpan(e.TextStart, e.TextLength);

    public void Clear()
    {
        Count = 0;
        _textLength = 0;
    }

    public void Add(UiLedgerKind kind, int x0, int y0, int x1, int y1, UiColor color, int element,
                    int texture = 0, int font = 0)
        => Push(new UiLedgerEntry(kind, x0, y0, x1, y1, texture, font, 0, 0, color, element));

    public void AddText(int x, int y, int font, ReadOnlySpan<char> text, UiColor color, int element)
    {
        if (_textLength + text.Length > _text.Length)
            Array.Resize(ref _text, Math.Max(_text.Length * 2, _textLength + text.Length));
        text.CopyTo(_text.AsSpan(_textLength));
        Push(new UiLedgerEntry(UiLedgerKind.Text, x, y, x, y, 0, font, _textLength, text.Length, color, element));
        _textLength += text.Length;
    }

    private void Push(in UiLedgerEntry e)
    {
        if (Count == _entries.Length)
            Array.Resize(ref _entries, _entries.Length * 2);
        _entries[Count++] = e;
    }
}
