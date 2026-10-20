using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real surface, over the IShimUiClient api.</summary>
internal sealed unsafe class ClientUiSurface(IUiClientArea uiClient) : IUiSurface
{
    public void FilledRect(int x0, int y0, int x1, int y1, uint rgba)
        => uiClient.UiDrawFilledRect(x0, y0, x1, y1, rgba);

    public void FilledRectFade(int x0, int y0, int x1, int y1, uint rgba, int alpha0, int alpha1, bool horizontal)
        => uiClient.UiDrawFilledRectFade(x0, y0, x1, y1, rgba, alpha0, alpha1, horizontal ? 1 : 0);

    public void OutlinedRect(int x0, int y0, int x1, int y1, uint rgba)
        => uiClient.UiDrawOutlinedRect(x0, y0, x1, y1, rgba);

    public void Line(int x0, int y0, int x1, int y1, uint rgba)
        => uiClient.UiDrawLine(x0, y0, x1, y1, rgba);

    public void TexturedRect(int texture, int x0, int y0, int x1, int y1, float s0, float t0, float s1, float t1, uint rgba)
        => uiClient.UiDrawTexturedRect(texture, x0, y0, x1, y1, s0, t0, s1, t1, rgba);

    public void Polygon(int texture, ReadOnlySpan<float> xyuv, uint rgba)
    {
        if (xyuv.Length < 12)
            return;
        fixed (float* p = xyuv)
            uiClient.UiDrawPolygon(texture, p, xyuv.Length / 4, rgba);
    }

    public void Text(int font, int x, int y, ReadOnlySpan<char> text, uint rgba)
    {
        if (text.IsEmpty)
            return;
        fixed (char* p = text)
            uiClient.UiDrawText(font, x, y, (ushort*)p, text.Length, rgba);
    }

    public void MeasureText(int font, ReadOnlySpan<char> text, out int width, out int height)
    {
        int w = 0, h = 0;
        if (!text.IsEmpty)
        {
            fixed (char* p = text)
                uiClient.UiMeasureText(font, (ushort*)p, text.Length, &w, &h);
        }
        width = w;
        height = h;
    }

    public void SetClip(bool enabled, int x0, int y0, int x1, int y1)
        => uiClient.UiSetClip(enabled ? 1 : 0, x0, y0, x1, y1);

    public int TextureCreate() => uiClient.UiTextureCreate();

    public void TextureUpload(int texture, ReadOnlySpan<byte> rgba, int wide, int tall)
    {
        if (rgba.Length < wide * tall * 4)
            return;
        fixed (byte* p = rgba)
            uiClient.UiTextureUpload(texture, p, wide, tall);
    }

    public bool FontAddFile(string path)
    {
        fixed (byte* p = Utf8(path))
            return uiClient.UiFontAddFile(p) != 0;
    }

    public int FontCreate(string family, int tall, int weight, int vguiFlags)
    {
        fixed (byte* p = Utf8(family))
            return uiClient.UiFontCreate(p, tall, weight, vguiFlags);
    }

    public void FontMetrics(int font, out int tall, out int ascent)
    {
        int t = 0, a = 0;
        uiClient.UiFontMetrics(font, &t, &a);
        tall = t;
        ascent = a;
    }

    public int FontAdvances(int font, int first, Span<int> advances)
    {
        if (advances.IsEmpty)
            return 0;
        fixed (int* p = advances)
            return uiClient.UiFontAdvances(font, first, advances.Length, p);
    }

    public void ScreenSize(out int wide, out int tall)
    {
        int w = 0, t = 0;
        uiClient.UiScreenSize(&w, &t);
        wide = w;
        tall = t;
    }

    public void PlaySound(string path)
    {
        fixed (byte* p = Utf8(path))
            uiClient.UiPlaySound(p);
    }

    public void SetMenuInput(bool mouse, bool keyboard) => uiClient.UiSetMenuInput(mouse ? 1 : 0, keyboard ? 1 : 0);

    public void SetCursor(int shape) => uiClient.UiSetCursor(shape);

    private static byte[] Utf8(string s)
    {
        byte[] bytes = new byte[Encoding.UTF8.GetByteCount(s) + 1];
        Encoding.UTF8.GetBytes(s, bytes);
        return bytes;
    }
}
