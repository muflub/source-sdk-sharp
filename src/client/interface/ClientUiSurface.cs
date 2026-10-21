using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The UI's draw primitives (plan_client_ui §8 U2): IShimUiClient's slots, one
/// call each. VguiBackend is the only caller; what to draw is decided there.
/// Colours are packed 0xRRGGBBAA. The draw calls do nothing outside the native
/// paint window.
/// </summary>
public interface IUiSurface
{
    void FilledRect(int x0, int y0, int x1, int y1, uint rgba);

    void FilledRectFade(int x0, int y0, int x1, int y1, uint rgba, int alpha0, int alpha1, bool horizontal);

    void OutlinedRect(int x0, int y0, int x1, int y1, uint rgba);

    void Line(int x0, int y0, int x1, int y1, uint rgba);

    void TexturedRect(int texture, int x0, int y0, int x1, int y1, float s0, float t0, float s1, float t1, uint rgba);

    /// <summary>A polygon of (x, y, u, v) vertices, four floats each.</summary>
    void Polygon(int texture, ReadOnlySpan<float> xyuv, uint rgba);

    void Text(int font, int x, int y, ReadOnlySpan<char> text, uint rgba);

    void MeasureText(int font, ReadOnlySpan<char> text, out int width, out int height);

    void SetClip(bool enabled, int x0, int y0, int x1, int y1);

    int TextureCreate();

    void TextureUpload(int texture, ReadOnlySpan<byte> rgba, int wide, int tall);

    bool FontAddFile(string path);

    /// <summary>A vgui font handle, or 0 when the surface refused it.</summary>
    int FontCreate(string family, int tall, int weight, int vguiFlags);

    void FontMetrics(int font, out int tall, out int ascent);

    /// <summary>Each character's advance (a + b + c) from <paramref name="first"/> on; how many it filled.</summary>
    int FontAdvances(int font, int first, Span<int> advances);

    void PlaySound(string path);

    /// <summary>Whether the menu root takes the mouse and the keyboard (a screen that wants them is open).</summary>
    void SetMenuInput(bool mouse, bool keyboard);

    /// <summary>The menu root's cursor, as a UiCursor value.</summary>
    void SetCursor(int shape);
}
