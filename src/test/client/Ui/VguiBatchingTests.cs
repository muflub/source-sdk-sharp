using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The live backend's batching: every surface call is a GPU draw of its own, so
/// fills and polygons go down as one mesh, and anything drawn another way
/// flushes the mesh first, so what is drawn over what does not change.
/// </summary>
public class VguiBatchingTests
{
    private static readonly UiColor Red = new(255, 0, 0, 255), Blue = new(0, 0, 255, 128);

    private static UiQuad Fill(float x, float y, float w, float h, UiColor c)
        => new(new UiRect(x, y, w, h), c, 0f, default, 0f, UiFade.None, 0);

    private static UiFidelityTests.RecordingSurface Draw(Action<UiRenderList> build)
    {
        var surface = new UiFidelityTests.RecordingSurface();
        var backend = new VguiBackend(surface, new UiFontLibrary(), new UiFontFiles([]));
        var list = new UiRenderList();
        build(list);
        backend.Draw(list.AsDrawList());
        return surface;
    }

    private static void Text(UiRenderList list)
        => list.GlyphRun(5, 5, list.Font(new UiFontSpec("Inter", 16, 400, UiFontFlags.None)), Red, "hi");

    [Fact]
    public void Fills_of_different_colours_and_a_polygon_are_one_mesh()
    {
        var surface = Draw(l =>
        {
            l.Quad(Fill(0, 0, 10, 10, Red));
            l.Polygon([new UiVertex(0, 0), new UiVertex(9, 0), new UiVertex(9, 9), new UiVertex(0, 9), new UiVertex(-1, 4)], Blue);
            l.Quad(Fill(20, 0, 10, 10, Blue));
        });
        Assert.Equal(["mesh"], surface.Calls);
        var mesh = Assert.Single(surface.Meshes);
        Assert.Equal(13, mesh.Rgba.Length);
        Assert.Equal(6 + 9 + 6, mesh.Indices.Length);
    }

    [Fact]
    public void A_fill_keeps_its_corners_colour_and_the_surfaces_two_triangles()
    {
        var mesh = Assert.Single(Draw(l => l.Quad(Fill(1, 2, 10, 20, Blue))).Meshes);
        Assert.Equal([1f, 2f, 0f, 0f, 11f, 2f, 0f, 0f, 11f, 22f, 0f, 0f, 1f, 22f, 0f, 0f], mesh.Xyuv);
        Assert.Equal([0x0000FF80u, 0x0000FF80u, 0x0000FF80u, 0x0000FF80u], mesh.Rgba);
        Assert.Equal([(ushort)0, (ushort)1, (ushort)2, (ushort)0, (ushort)2, (ushort)3], mesh.Indices);
    }

    [Fact]
    public void A_polygon_is_a_fan_from_its_first_vertex_after_what_went_before()
    {
        var mesh = Assert.Single(Draw(l =>
        {
            l.Quad(Fill(0, 0, 1, 1, Red));
            l.Polygon([new UiVertex(0, 0), new UiVertex(9, 0), new UiVertex(9, 9), new UiVertex(0, 9)], Blue);
        }).Meshes);
        Assert.Equal([(ushort)4, (ushort)5, (ushort)6, (ushort)4, (ushort)6, (ushort)7], mesh.Indices[6..]);
    }

    [Fact]
    public void Text_between_fills_keeps_its_place()
    {
        // The draw order fact: the second fill covers the text, the first lies under it.
        var surface = Draw(l =>
        {
            l.Quad(Fill(0, 0, 10, 10, Red));
            Text(l);
            l.Quad(Fill(0, 0, 10, 10, Blue));
        });
        Assert.Equal(["mesh", "text", "mesh"], surface.Calls);
        Assert.Equal(0xFF0000FFu, surface.Meshes[0].Rgba[0]);
        Assert.Equal(0x0000FF80u, surface.Meshes[1].Rgba[0]);
    }

    [Fact]
    public void A_texture_breaks_the_batch()
    {
        var surface = Draw(l =>
        {
            l.Quad(Fill(0, 0, 10, 10, Red));
            l.TexturedQuad(new UiTexturedQuad(new UiRect(0, 0, 5, 5), 7, new UiRect(0, 0, 1, 1), Red));
            l.Quad(Fill(0, 0, 10, 10, Red));
        });
        Assert.Equal(["mesh", "texture", "mesh"], surface.Calls);
    }

    [Fact]
    public void A_thin_line_breaks_the_batch()
    {
        var surface = Draw(l =>
        {
            l.Quad(Fill(0, 0, 10, 10, Red));
            l.Line(new UiLine(0, 0, 5, 5, 1f, Red));
            l.Quad(Fill(0, 0, 10, 10, Red));
        });
        Assert.Equal(3, surface.Draws);
        Assert.Equal(2, surface.Meshes.Count);
    }

    [Fact]
    public void A_shape_inside_the_clip_stays_in_the_batch_across_the_clip_change()
    {
        var surface = Draw(l =>
        {
            l.Quad(Fill(0, 0, 10, 10, Red));
            l.PushScissor(new UiRect(0, 0, 100, 100));
            l.Quad(Fill(10, 10, 10, 10, Red));
            l.PopScissor();
        });
        Assert.Equal(["clip", "unclip", "mesh"], surface.Calls);
        Assert.Equal(8, surface.Meshes[0].Rgba.Length);
    }

    [Fact]
    public void A_fill_crossing_the_clip_is_the_surfaces_to_clip_in_its_place()
    {
        var surface = Draw(l =>
        {
            l.Quad(Fill(0, 0, 10, 10, Red));
            l.PushScissor(new UiRect(0, 0, 100, 100));
            l.Quad(Fill(90, 90, 20, 20, Red));
            l.Quad(Fill(10, 10, 10, 10, Red));
            l.PopScissor();
        });
        Assert.Equal(["clip", "mesh", "rect", "unclip", "mesh"], surface.Calls);
    }

    [Fact]
    public void A_polygon_crossing_the_clip_is_the_surfaces()
    {
        var surface = Draw(l =>
        {
            l.PushScissor(new UiRect(0, 0, 10, 10));
            l.Polygon([new UiVertex(5, 5), new UiVertex(15, 5), new UiVertex(15, 15)], Red);
            l.PopScissor();
        });
        Assert.Equal(["clip", "polygon", "unclip"], surface.Calls);
    }

    [Fact]
    public void A_fade_runs_its_alpha_as_the_surface_scales_it()
    {
        // The surface scales both ends by the draw colour's alpha: 128 becomes
        // 64 and 200 becomes 100, truncated.
        var fade = new UiQuad(new UiRect(0, 0, 10, 10), new UiColor(1, 2, 3, 128), 0f, default, 0f, UiFade.Horizontal, 200);
        var mesh = Assert.Single(Draw(l => l.Quad(fade)).Meshes);
        Assert.Equal([0x01020340u, 0x01020364u, 0x01020364u, 0x01020340u], mesh.Rgba);

        var down = fade with { Fade = UiFade.Vertical };
        mesh = Assert.Single(Draw(l => l.Quad(down)).Meshes);
        Assert.Equal([0x01020340u, 0x01020340u, 0x01020364u, 0x01020364u], mesh.Rgba);
    }

    [Fact]
    public void A_one_pixel_border_is_the_surfaces_four_edges()
    {
        var q = new UiQuad(new UiRect(0, 0, 10, 8), default, 0f, Red, 1f, UiFade.None, 0);
        var mesh = Assert.Single(Draw(l => l.Quad(q)).Meshes);
        Assert.Equal(16, mesh.Rgba.Length);
        // top, bottom, left, right: the left and right run between the other two.
        Assert.Equal([0f, 0f, 10f, 1f], new[] { mesh.Xyuv[0], mesh.Xyuv[1], mesh.Xyuv[8], mesh.Xyuv[9] });
        Assert.Equal([0f, 7f, 10f, 8f], new[] { mesh.Xyuv[16], mesh.Xyuv[17], mesh.Xyuv[24], mesh.Xyuv[25] });
        Assert.Equal([0f, 1f, 1f, 7f], new[] { mesh.Xyuv[32], mesh.Xyuv[33], mesh.Xyuv[40], mesh.Xyuv[41] });
        Assert.Equal([9f, 1f, 10f, 7f], new[] { mesh.Xyuv[48], mesh.Xyuv[49], mesh.Xyuv[56], mesh.Xyuv[57] });
    }

    [Fact]
    public void A_transparent_fill_draws_nothing()
    {
        Assert.Empty(Draw(l => l.Polygon([new UiVertex(0, 0), new UiVertex(9, 0), new UiVertex(9, 9)], new UiColor(1, 2, 3, 0))).Calls);
    }

    [Fact]
    public void A_full_mesh_goes_down_and_the_next_begins()
    {
        int rects = UiMeshBatch.MaxVertices / 4 + 1;
        var surface = Draw(l =>
        {
            for (int i = 0; i < rects; i++)
                l.Quad(Fill(i % 100, 0, 1, 1, Red));
        });
        Assert.Equal(2, surface.Meshes.Count);
        Assert.Equal(UiMeshBatch.MaxVertices, surface.Meshes[0].Rgba.Length);
        Assert.Equal(4, surface.Meshes[1].Rgba.Length);
    }

    [Fact]
    public void Ten_fills_are_one_draw()
    {
        var surface = Draw(l =>
        {
            for (int i = 0; i < 10; i++)
                l.Quad(Fill(i, 0, 1, 1, Red));
        });
        Assert.Equal(1, surface.Draws);
    }
}
