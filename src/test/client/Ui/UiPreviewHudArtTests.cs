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
}
