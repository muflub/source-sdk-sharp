using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The preview's HUD art store and the Skia backend drawing from it.
/// </summary>
public sealed class UiPreviewHudArtTests
{
    [Fact]
    public void A_handle_the_store_never_decoded_is_not_art()
    {
        // A sample's own texture handle (a fake portrait's 7) is not HUD art:
        // the store answered a default entry with no pixels for it.
        var store = new PreviewTextures();
        int handle = store.Add([1, 2, 3, 4], 1, 1);

        Assert.Null(store.Find(7));
        Assert.NotNull(store.Find(handle));
    }

    [Fact]
    public void A_texture_drawn_again_draws_the_same_art()
    {
        // The backend caches each texture's image; the first draw used to
        // dispose the cached one, and the next frame's draw of it threw.
        var store = new PreviewTextures();
        byte[] red = [.. Enumerable.Repeat<byte[]>([0xff, 0x00, 0x00, 0xff], 4).SelectMany(b => b)];
        int handle = store.Add(red, 2, 2);
        var ctx = Context();
        Frame(ctx, Screen(640, 360), (ref UiFrame ui) => ui.Texture(handle, Size.Du(100), Size.Du(100)).Name("art"));
        var rect = Named(ctx, "art").Rect;
        using var skia = new SkiaBackend(new UiFontLibrary()) { HudArt = store };

        skia.Draw(ctx.List.AsDrawList());
        skia.Draw(ctx.List.AsDrawList());

        var pixel = skia.Bitmap.GetPixel((int)(rect.X + rect.W / 2), (int)(rect.Y + rect.H / 2));
        Assert.Equal((0xff, 0x00, 0x00), (pixel.Red, pixel.Green, pixel.Blue));
    }

    [Fact]
    public void A_uv_window_samples_its_part_of_the_art()
    {
        // Four columns; the window names the last one, so the quad's centre
        // is white — the whole image drawn into the quad would land blue.
        var store = new PreviewTextures();
        byte[] columns = [0xff, 0, 0, 0xff, 0, 0xff, 0, 0xff, 0, 0, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff];
        int handle = store.Add(columns, 4, 1);
        var ctx = Context();
        Frame(ctx, Screen(640, 360),
              (ref UiFrame ui) => ui.Texture(handle, Size.Du(80), Size.Du(20), new UiRect(0.75f, 0f, 0.25f, 1f)).Name("art"));
        var rect = Named(ctx, "art").Rect;
        using var skia = new SkiaBackend(new UiFontLibrary()) { HudArt = store };

        skia.Draw(ctx.List.AsDrawList());

        var pixel = skia.Bitmap.GetPixel((int)(rect.X + rect.W / 2), (int)(rect.Y + rect.H / 2));
        Assert.Equal((0xff, 0xff, 0xff), (pixel.Red, pixel.Green, pixel.Blue));
    }

    [Fact]
    public void A_window_reaching_past_the_art_draws_only_the_on_surface_fraction()
    {
        // The cap swipe's window starts past the art's right edge: the part
        // of the quad that samples nothing draws nothing (the clear), and
        // the rest the art's last column. Drawn whole, the quad would be the
        // red and blue columns it does not name.
        var store = new PreviewTextures();
        byte[] columns = [0xff, 0, 0, 0xff, 0, 0, 0xff, 0xff];
        int handle = store.Add(columns, 2, 1);
        var ctx = Context();
        Frame(ctx, Screen(640, 360),
              (ref UiFrame ui) => ui.Texture(handle, Size.Du(80), Size.Du(20), new UiRect(0.5f, 0f, 1f, 1f)).Name("art"));
        var rect = Named(ctx, "art").Rect;
        using var skia = new SkiaBackend(new UiFontLibrary()) { HudArt = store };

        skia.Draw(ctx.List.AsDrawList());

        var left = skia.Bitmap.GetPixel((int)(rect.X + 0.3f * rect.W), (int)(rect.Y + rect.H / 2));
        var right = skia.Bitmap.GetPixel((int)(rect.X + 3 * rect.W / 4), (int)(rect.Y + rect.H / 2));
        Assert.Equal((0, 0, 0xff), (left.Red, left.Green, left.Blue));
        Assert.Equal((0x3a, 0x3e, 0x42), (right.Red, right.Green, right.Blue));   // the clear
    }

    [Fact]
    public void A_rotated_texture_draws_about_its_centre()
    {
        // A 4:1 bar turned a quarter turn stands vertical: a point below the
        // unrotated bar is the art now, and a point the bar covered is the
        // clear — rotation about the centre, as stock's ring turns to the
        // hit's bearing. Offsets scale with the bar, the du scale varies.
        var store = new PreviewTextures();
        byte[] red = [.. Enumerable.Repeat<byte[]>([0xff, 0x00, 0x00, 0xff], 4).SelectMany(b => b)];
        int handle = store.Add(red, 2, 2);
        var ctx = Context();
        Frame(ctx, Screen(640, 360), (ref UiFrame ui) =>
            ui.Texture(handle, Size.Du(80), Size.Du(20)).Rotate(MathF.PI / 2f).Name("bar"));
        var rect = Named(ctx, "bar").Rect;
        using var skia = new SkiaBackend(new UiFontLibrary()) { HudArt = store };

        skia.Draw(ctx.List.AsDrawList());

        var nowArt = skia.Bitmap.GetPixel((int)(rect.X + rect.W / 2), (int)(rect.Y + rect.H / 2 + 0.3f * rect.W));
        var wasArt = skia.Bitmap.GetPixel((int)(rect.X + rect.W / 2 + 0.3f * rect.W), (int)(rect.Y + rect.H / 2));
        Assert.Equal((0xff, 0x00, 0x00), (nowArt.Red, nowArt.Green, nowArt.Blue));
        Assert.Equal((0x3a, 0x3e, 0x42), (wasArt.Red, wasArt.Green, wasArt.Blue));   // the clear
    }

    [Fact]
    public void An_additive_texture_adds_to_what_is_behind()
    {
        // Red over blue additively is magenta; painted over, it would be red.
        var store = new PreviewTextures();
        byte[] red = [.. Enumerable.Repeat<byte[]>([0xff, 0x00, 0x00, 0xff], 4).SelectMany(b => b)];
        int handle = store.Add(red, 2, 2);
        var ctx = Context();
        Frame(ctx, Screen(640, 360), (ref UiFrame ui) =>
        {
            ui.Rect(UiPaint.FromGame(new UiColor(0, 0, 0xff, 0xff)), Size.Du(80), Size.Du(20)).Name("behind");
            ui.Texture(handle, Size.Du(80), Size.Du(20)).Blend(UiBlend.Additive).Name("glow");
        });
        var behind = Named(ctx, "behind").Rect;
        var glow = Named(ctx, "glow").Rect;
        Assert.Equal((behind.X, behind.Y, behind.W, behind.H), (glow.X, glow.Y, glow.W, glow.H));
        using var skia = new SkiaBackend(new UiFontLibrary()) { HudArt = store };

        skia.Draw(ctx.List.AsDrawList());

        var pixel = skia.Bitmap.GetPixel((int)(glow.X + glow.W / 2), (int)(glow.Y + glow.H / 2));
        Assert.Equal((0xff, 0x00, 0xff), (pixel.Red, pixel.Green, pixel.Blue));
    }
}
