using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>The SVG backend: a hand-built render list comes out as the golden SVG text.</summary>
public class SvgBackendTests
{
    /// <summary>Fixed metrics: ascent 0.8 em, descent 0.2 em, every glyph half an em.</summary>
    internal sealed class FixedMetrics : IUiFontMetrics
    {
        public UiFontMetrics Metrics(in UiFontSpec font)
            => new(font.PxHeight * 0.8f, font.PxHeight * 0.2f, font.PxHeight);

        public float Advance(in UiFontSpec font, char c) => font.PxHeight * 0.5f;
    }

    internal static UiRenderList HandBuilt()
    {
        var list = new UiRenderList { Screen = new ScreenInfo(640, 360, 360 / 1080f, default) };
        var amber = new UiColor(255, 176, 46, 255);
        list.Quad(new UiQuad(new UiRect(10, 10, 200, 80), new UiColor(12, 14, 18, 180), 6, amber, 2, UiFade.None, 0), 0);
        list.Quad(new UiQuad(new UiRect(10, 100, 200, 20), new UiColor(0, 0, 0, 200), 0, default, 0, UiFade.Vertical, 0), 1);
        list.PushScissor(new UiRect(20, 20, 100, 40), 2);
        int font = list.Font(new UiFontSpec("IBM Plex Sans Condensed", 20, 700, UiFontFlags.DropShadow));
        list.GlyphRun(24, 24, font, amber, "100 <&> \"hp\"", 3);
        list.PopScissor(2);
        list.Line(new UiLine(300, 180, 340, 180, 2, new UiColor(255, 255, 255, 200)), 4);
        list.Polygon([new UiVertex(400, 100), new UiVertex(440, 100), new UiVertex(420, 140)], new UiColor(220, 60, 50, 255), 5);
        list.TexturedQuad(new UiTexturedQuad(new UiRect(500, 20, 32, 32), 7, new UiRect(0, 0, 1, 1), new UiColor(255, 255, 255, 255)), 6);
        return list;
    }

    [Fact]
    public void A_hand_built_list_draws_as_its_golden_svg()
    {
        var svg = new SvgBackend(new FixedMetrics());
        svg.Draw(HandBuilt().AsDrawList());
        UiGolden.Text("handbuilt.svg", svg.Svg);
    }

    [Fact]
    public void Every_primitive_kind_reaches_the_svg()
    {
        var svg = new SvgBackend(new FixedMetrics());
        svg.Draw(HandBuilt().AsDrawList());
        string s = svg.Svg;

        // One of each, anchored on whole constructs.
        Assert.Contains("<rect x=\"10\" y=\"10\" width=\"200\" height=\"80\" rx=\"6\" fill=\"#0c0e12\" fill-opacity=\"0.71\" data-e=\"0\"/>", s);
        Assert.Contains("<stop offset=\"1\" stop-color=\"#000000\" stop-opacity=\"0\"/>", s);
        Assert.Contains("<clipPath id=\"c0\"><rect x=\"20\" y=\"20\" width=\"100\" height=\"40\"/></clipPath>", s);
        // Baseline = top + ascent (24 + 16); the shadow one pixel down and right.
        Assert.Contains("<text x=\"24\" y=\"40\" font-family=\"IBM Plex Sans Condensed\" font-size=\"20\" font-weight=\"700\" fill=\"#ffb02e\" xml:space=\"preserve\" data-e=\"3\">100 &lt;&amp;&gt; &quot;hp&quot;</text>", s);
        Assert.Contains("<text x=\"25\" y=\"41\"", s);
        Assert.Contains("<line x1=\"300\" y1=\"180\" x2=\"340\" y2=\"180\"", s);
        Assert.Contains("<polygon points=\"400,100 440,100 420,140\" fill=\"#dc3c32\" data-e=\"5\"/>", s);
        Assert.Contains("data-texture=\"7\"", s);
        Assert.Equal(1, s.Split("</g>").Length - 1);
    }
}
