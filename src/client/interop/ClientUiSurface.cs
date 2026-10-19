using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real surface, over the IShimUiClient api.</summary>
internal sealed unsafe class ClientUiSurface(UiClientArea? uiClient) : IUiSurface
{
    // The shim areas this calls, taken by constructor (plan_shim_areas_split
    // phase 5). Null on a client that never attached (an unattached engine
    // double): a guarded call answers its default and any other throws, as
    // they did through the side's attach guard.
    private UiClientArea UiClient => uiClient ?? throw ShimAreaGuard.NotAttached(nameof(UiClient));

    public void FilledRect(int x0, int y0, int x1, int y1, uint rgba)
        => UiClient.UiDrawFilledRect(x0, y0, x1, y1, rgba);

    public void FilledRectFade(int x0, int y0, int x1, int y1, uint rgba, int alpha0, int alpha1, bool horizontal)
        => UiClient.UiDrawFilledRectFade(x0, y0, x1, y1, rgba, alpha0, alpha1, horizontal ? 1 : 0);

    public void OutlinedRect(int x0, int y0, int x1, int y1, uint rgba)
        => UiClient.UiDrawOutlinedRect(x0, y0, x1, y1, rgba);

    public void Line(int x0, int y0, int x1, int y1, uint rgba)
        => UiClient.UiDrawLine(x0, y0, x1, y1, rgba);

    public void TexturedRect(int texture, int x0, int y0, int x1, int y1, float s0, float t0, float s1, float t1, uint rgba)
        => UiClient.UiDrawTexturedRect(texture, x0, y0, x1, y1, s0, t0, s1, t1, rgba);

    public void Polygon(int texture, ReadOnlySpan<float> xyuv, uint rgba)
    {
        if (xyuv.Length < 12)
            return;
        fixed (float* p = xyuv)
            UiClient.UiDrawPolygon(texture, p, xyuv.Length / 4, rgba);
    }

    public void Text(int font, int x, int y, ReadOnlySpan<char> text, uint rgba)
    {
        if (text.IsEmpty)
            return;
        fixed (char* p = text)
            UiClient.UiDrawText(font, x, y, (ushort*)p, text.Length, rgba);
    }

    public void MeasureText(int font, ReadOnlySpan<char> text, out int width, out int height)
    {
        int w = 0, h = 0;
        if (!text.IsEmpty)
        {
            fixed (char* p = text)
                UiClient.UiMeasureText(font, (ushort*)p, text.Length, &w, &h);
        }
        width = w;
        height = h;
    }

    public void SetClip(bool enabled, int x0, int y0, int x1, int y1)
        => UiClient.UiSetClip(enabled ? 1 : 0, x0, y0, x1, y1);

    public int TextureCreate() => UiClient.UiTextureCreate();

    public void TextureUpload(int texture, ReadOnlySpan<byte> rgba, int wide, int tall)
    {
        if (rgba.Length < wide * tall * 4)
            return;
        fixed (byte* p = rgba)
            UiClient.UiTextureUpload(texture, p, wide, tall);
    }

    public bool FontAddFile(string path)
    {
        fixed (byte* p = Utf8(path))
            return UiClient.UiFontAddFile(p) != 0;
    }

    public int FontCreate(string family, int tall, int weight, int vguiFlags)
    {
        fixed (byte* p = Utf8(family))
            return UiClient.UiFontCreate(p, tall, weight, vguiFlags);
    }

    public void FontMetrics(int font, out int tall, out int ascent)
    {
        int t = 0, a = 0;
        UiClient.UiFontMetrics(font, &t, &a);
        tall = t;
        ascent = a;
    }

    public int FontAdvances(int font, int first, Span<int> advances)
    {
        if (advances.IsEmpty)
            return 0;
        fixed (int* p = advances)
            return UiClient.UiFontAdvances(font, first, advances.Length, p);
    }

    public void ScreenSize(out int wide, out int tall)
    {
        int w = 0, t = 0;
        UiClient.UiScreenSize(&w, &t);
        wide = w;
        tall = t;
    }

    public void PlaySound(string path)
    {
        fixed (byte* p = Utf8(path))
            UiClient.UiPlaySound(p);
    }

    public void SetMenuInput(bool mouse, bool keyboard) => UiClient.UiSetMenuInput(mouse ? 1 : 0, keyboard ? 1 : 0);

    public void SetCursor(int shape) => UiClient.UiSetCursor(shape);

    private static byte[] Utf8(string s)
    {
        byte[] bytes = new byte[Encoding.UTF8.GetByteCount(s) + 1];
        Encoding.UTF8.GetBytes(s, bytes);
        return bytes;
    }
}
