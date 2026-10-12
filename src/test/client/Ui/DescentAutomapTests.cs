using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using TF2.Descent.Client.Ui;
using TF2.Descent.Client.Ui.Parts;
using TF2.Descent.Client.Ui.Screens;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The minimap's two named facts (docs/plans/descent_ui.md 2.2): the canvas
/// draws only what the server revealed (R23 / the 2m client contract: nothing
/// "beyond" the sent outlines), and a two-storey map layers the other storey's
/// rooms under the focus storey instead of hiding or fusing them.
/// </summary>
public class DescentAutomapTests
{
    private static readonly UiFontLibrary Fonts = new();

    /// <summary>The room fills as the svg hexes, taken from the paints so a
    /// recolour cannot quietly unpin the assertions.</summary>
    private static string Hex(UiPaint p) => $"#{p.RawColor.R:x2}{p.RawColor.G:x2}{p.RawColor.B:x2}";

    private static (string Svg, UiElementInfo[] Elements) DrawOf(IDescentAutomap map, AutomapProps props = default)
    {
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        var p = props with
        {
            WidthDu = props.WidthDu == 0 ? 480 : props.WidthDu,
            HeightDu = props.HeightDu == 0 ? 480 : props.HeightDu,
        };
        UiSamples.Render(ctx, new UiPanelPreview<AutomapProps>("AutomapCanvas", new AutomapCanvas(map), p),
                         UiSamples.Screen(1920, 1080), 100.0);
        return (UiTestKit.Svg(ctx), UiTestKit.Elements(ctx));
    }

    private static int Fills(string svg, string hex)
        => svg.Split($"\"{hex}\"", StringSplitOptions.None).Length - 1;

    /// <summary>A revealed two-room stretch of a one-storey map; the third
    /// room of the level is unvisited, so the source reports no outline for it
    /// at all — the client never learns where it was.</summary>
    private sealed class FakeRevealedTwo : IDescentAutomap
    {
        public float Heading { get; init; }

        private readonly (float X, float Y, float W, float H)[] Rooms =
        [
            (96, 448, 224, 160),
            (416, 384, 256, 256),
        ];

        public int RoomCount => Rooms.Length;
        public float RoomX(int r) => Rooms[r].X;
        public float RoomY(int r) => Rooms[r].Y;
        public float RoomW(int r) => Rooms[r].W;
        public float RoomH(int r) => Rooms[r].H;
        public int RoomStorey(int r) => 1;
        public bool RoomCurrentStorey(int r) => true;
        public int DoorStubCount => 0;
        public float DoorStubX(int i) => 0;
        public float DoorStubY(int i) => 0;
        public float DoorStubW(int i) => 0;
        public float DoorStubH(int i) => 0;
        public int MarkerCount => 0;
        public DescentMarker Marker(int i) => throw new InvalidOperationException("no markers sent");
        public int StoreyCount => 1;
        public int HeroStorey => 1;
        public float HeroX => 200;
        public float HeroY => 500;
        public float HeroHeadingDeg => Heading;
        public int FocusStorey => 1;
    }

    [Fact]
    public void Unvisited_rooms_are_absent_the_canvas_draws_only_the_sent_outlines()
    {
        // The fake knows two rooms; a third room of the level is unvisited and
        // the source never mentions it.
        var (svg, elements) = DrawOf(new FakeRevealedTwo());
        Assert.Equal(2, Fills(svg, Hex(DescentPaints.MapRoomCurrent)));
        Assert.Equal(0, Fills(svg, Hex(DescentPaints.MapRoom)));
        // Each sent room draws once (a room rect is a sized Spacer), and the
        // only vector glyph is the hero's own dot: nothing beyond the sent
        // outlines ever gets a room or a shape.
        Assert.Equal(2, elements.Count(e => e.Kind == UiElementKind.Spacer && e.Rect.W > 0 && e.Rect.H > 0));
        Assert.Single(elements, e => e.Kind == UiElementKind.Shape);
    }

    [Fact]
    public void Two_storeys_layer_the_other_storeys_rooms_sit_under_the_focus_storey()
    {
        // The samples' two-storey map: three rooms on the hero's storey, one
        // guardroom a storey up (FakeAutomap).
        var (svg, _) = DrawOf(new FakeAutomap());
        Assert.Equal(3, Fills(svg, Hex(DescentPaints.MapRoomCurrent)));   // the hero's storey, bright
        Assert.Equal(1, Fills(svg, Hex(DescentPaints.MapRoom)));          // the other storey, layered under, not hidden
    }

    [Fact]
    public void Tabbing_the_focus_storey_dims_every_room_and_hides_the_other_storeys_markers()
    {
        // The samples' two-storey map: all six markers are on storey 2 with
        // the hero. Tab to storey 1 (the map screen's tabs): no room is on the
        // hero's storey any more, every room goes to the muted layer, and the
        // storey-2 glyphs stop drawing — only the hero dot is a shape left.
        var (focused, focusedEls) = DrawOf(new FakeAutomap());
        Assert.Equal(7, focusedEls.Count(e => e.Kind == UiElementKind.Shape));   // 6 markers + hero
        var (tabbed, shapes) = DrawOf(new FakeAutomap(), new AutomapProps(480, 480, FocusStorey: 1));
        Assert.Equal(0, Fills(tabbed, Hex(DescentPaints.MapRoomCurrent)));
        Assert.Equal(4, Fills(tabbed, Hex(DescentPaints.MapRoom)));           // all four, muted
        Assert.Single(shapes, e => e.Kind == UiElementKind.Shape);            // the hero alone
    }

    // ---- plan §2.2: the marker kinds and the hero's triangle ----------------

    /// <summary>The plan's kind→glyph table, and the one place the canvas
    /// answers it. The lock means locked: an open chest is the plain mark.</summary>
    [Fact]
    public void Marker_kinds_map_as_the_plan_draws_them_stairs_to_chevrons_the_lock_only_when_locked()
    {
        Assert.Equal(UiShape.ChevronDown, AutomapCanvas.ShapeOf(DescentMapMarker.Stairs));
        Assert.Equal(UiShape.Cross45, AutomapCanvas.ShapeOf(DescentMapMarker.Corpse));
        Assert.Equal(UiShape.Person, AutomapCanvas.ShapeOf(DescentMapMarker.Party));
        Assert.Equal(UiShape.Star, AutomapCanvas.ShapeOf(DescentMapMarker.ShrineUnused));
        Assert.Equal(UiShape.Check, AutomapCanvas.ShapeOf(DescentMapMarker.Portal));
        Assert.Equal(UiShape.Lock, AutomapCanvas.ShapeOf(DescentMapMarker.ContainerLocked));
        Assert.NotEqual(UiShape.Lock, AutomapCanvas.ShapeOf(DescentMapMarker.Container));
    }

    /// <summary>A player reads a marker by its shape first (tone alone fails
    /// colour-blind eyes): no two kinds share one, but the shrine's two
    /// states, which are one thing. Boss was drawn as stairs and a trap as a
    /// corpse (RevD).</summary>
    [Fact]
    public void Every_marker_kind_but_the_shrines_two_states_has_its_own_shape()
    {
        var shared = Enum.GetValues<DescentMapMarker>()
            .GroupBy(AutomapCanvas.ShapeOf)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join("+", g))
            .ToArray();
        Assert.Equal(new[] { "ShrineUnused+ShrineUsed" }, shared);
    }

    /// <summary>The hero is an accent triangle pointing along the heading
    /// (plan §2.2's 'hero as an accent triangle', which the HUD asks for with
    /// <c>HeroHeading</c>): the arrow is the only accent fill around the hero
    /// and its bounding box is long along the heading — east at 90 degrees,
    /// north at 0 (flipping the heading's sin/cos turns one arm red).</summary>
    [Fact]
    public void The_hero_draws_as_an_accent_triangle_pointing_along_the_heading()
    {
        // Without the heading prop the hero stays the plain dot: two room
        // fills and one glyph, no arrow rows.
        var (_, flat) = DrawOf(new FakeRevealedTwo());
        Assert.Single(flat, e => e.Kind == UiElementKind.Shape);
        Assert.Equal(2, flat.Count(e => e.Kind == UiElementKind.Spacer && e.Rect.W > 0 && e.Rect.H > 0));
    }

    [Fact]
    public void The_hero_triangle_points_east_and_north_with_the_heading()
    {
        foreach (var (heading, pointsEast) in new[] { (90f, true), (0f, false) })
        {
            var (ctx, els) = DrawOfHero(new FakeRevealedTwo { Heading = heading });
            Assert.DoesNotContain(els, e => e.Kind == UiElementKind.Shape);   // the dot is gone

            // The arrow: the accent-filled draws around the hero (a room fill
            // or door stub is a raw game paint, never a Tone). The fake's
            // widest room is 256 of the 1024 grid = 120 canvas du, which
            // calibrates px per canvas du; the hero sits at grid (200, 500),
            // that room's corner at (416, 384).
            var rooms = els.Where(e => e.Kind == UiElementKind.Spacer && e.Rect.W > 0 && e.Rect.H > 0
                && ctx.Report.FillToneOf(Array.IndexOf(els, e)) != Tone.Accent).ToArray();
            Assert.Equal(2, rooms.Length);
            var big = rooms.OrderByDescending(e => e.Rect.W).First();
            float scale = big.Rect.W / 120f;
            float hx = big.Rect.X + (200 - 416) * 480f / 1024f * scale;
            float hy = big.Rect.Y + (500 - 384) * 480f / 1024f * scale;
            var rows = els.Where((e, i) => e.Kind == UiElementKind.Spacer && e.Rect.W > 0 && e.Rect.H > 0
                && ctx.Report.FillToneOf(i) == Tone.Accent
                && System.Math.Abs(e.Rect.X + e.Rect.W / 2 - hx) < 40 * scale
                && System.Math.Abs(e.Rect.Y + e.Rect.H / 2 - hy) < 40 * scale).ToArray();
            Assert.True(rows.Length >= 3, $"{rows.Length} arrow rows at heading {heading}");

            float x0 = rows.Min(e => e.Rect.X), x1 = rows.Max(e => e.Rect.X + e.Rect.W);
            float y0 = rows.Min(e => e.Rect.Y), y1 = rows.Max(e => e.Rect.Y + e.Rect.H);
            if (pointsEast)
            {
                Assert.True(x1 - x0 >= 1.15f * (y1 - y0), "the east arrow is not long along x");
                Assert.True((x0 + x1) / 2 > hx, "the east arrow leans backwards");
            }
            else
            {
                Assert.True(y1 - y0 >= 1.15f * (x1 - x0), "the north arrow is not long along y");
                Assert.True((y0 + y1) / 2 < hy, "the north arrow leans backwards");
            }
        }
    }

    private static (UiContext Ctx, UiElementInfo[] Elements) DrawOfHero(FakeRevealedTwo map)
    {
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, new UiPanelPreview<AutomapProps>("AutomapCanvas", new AutomapCanvas(map),
                         new AutomapProps(480, 480, HeroHeading: true)), UiSamples.Screen(1920, 1080), 100.0);
        return (ctx, UiTestKit.Elements(ctx));
    }

    [Fact]
    public void The_downed_overlay_draws_nothing_while_the_hero_is_not_downed()
    {
        // A verification fact for plan §2.12's 'draws over the HUD only while
        // downed' — Downed's early return predates it. The positive arm first:
        // the same party and state with Downed = true does draw the panel
        // keyed "downed", so the negative arm's silence is the gate and not a
        // broken stimulus (a check that cannot fail is not evidence). The
        // screen's own root element is always named after the screen, so the
        // panel is found as a named element that has a parent.
        static (UiContext Ctx, UiElementInfo[] Elements) Draw(bool downed)
        {
            var state = new FakeDownedState
            {
                Downed = downed, BleedOutSecondsLeft = 14.4f, BleedOutTotal = 30f,
                NearestMemberName = "Merasmus", NearestMemberDistance = 620,
            };
            var party = new FakeParty
            {
                Members =
                [
                    new("Bugha", "#Descent_Class_Soldier", 18, 0, 140, true, true, false, 0, false),
                    new("Merasmus", "#Descent_Class_Spy", 17, 66, 110, false, false, false, 620, false),
                ],
            };
            var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
            UiSamples.Render(ctx, DownedSamples.Build(new UiSampleServices(), state, party),
                             UiSamples.Screen(1920, 1080), 100.0);
            return (ctx, UiTestKit.Elements(ctx));
        }
        static bool IsPanel(UiContext ctx, UiElementInfo e)
            => e.Parent >= 0 && UiTestKit.NameOf(ctx, e) == "downed";

        var (onCtx, on) = Draw(downed: true);
        Assert.Contains(on, e => IsPanel(onCtx, e));
        var (offCtx, off) = Draw(downed: false);
        Assert.DoesNotContain(off, e => IsPanel(offCtx, e));
    }

    [Fact]
    public void The_room_labels_control_looks_different_on_and_off()
    {
        // The overlay owns the labels toggle; a plain button drew the same
        // pixels in both states, so nothing said whether labels were on
        // (RevD: revealed and labelless pixel-identical there). The control's
        // own subtree must change, not just the map under it.
        static string Look(string slug)
        {
            var sample = Assert.Single(SourceSharp.UiPreview.PreviewRun.AllSamples(),
                                       s => !s.Gallery && s.Group == "automap" && s.Slug == slug);
            var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
            UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
            var els = UiTestKit.Elements(ctx);
            int root = Array.FindIndex(els, e => UiTestKit.NameOf(ctx, e) == "labels");
            Assert.True(root >= 0, $"{slug}: no labels control");
            var tones = new List<string>();
            for (int i = root; i < els.Length; i++)
            {
                bool under = i == root;
                for (int p = els[i].Parent; p >= 0 && !under; p = els[p].Parent)
                    under = p == root;
                if (under)
                    tones.Add($"{els[i].Kind}:{ctx.Report.FillToneOf(i)}");
            }
            return string.Join(",", tones);
        }
        Assert.NotEqual(Look("revealed"), Look("labelless"));
    }

    [Fact]
    public void Room_labels_never_hide_a_marker_or_the_hero()
    {
        // The plates were centred in their rooms, where the markers and the
        // hero sit too: drawn last, a plate hid its room's marker (the shrine
        // star), and the hero arrow cut into "Entry Hall" (RevD). A plate now
        // hangs from its room's clearer edge, but the server puts markers
        // anywhere and a small room cannot hold both apart (the window's
        // minimum), so the guarantee is the order: a mark that meets a plate
        // is drawn after it, on top.
        int plates = 0, marksSeen = 0, meetings = 0;
        var problems = new List<string>();
        foreach (var sample in SourceSharp.UiPreview.PreviewRun.AllSamples().Where(s => !s.Gallery && s.Group == "automap"))
            foreach (var size in UiSamples.Sizes())
            {
                var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
                UiSamples.Render(ctx, sample.Create(), size.Screen, sample.Now);
                var els = UiTestKit.Elements(ctx);
                int canvas = Array.FindIndex(els, e => UiTestKit.NameOf(ctx, e).StartsWith("AutomapCanvas[", StringComparison.Ordinal));
                if (canvas < 0)
                    continue;   // the empty state draws no canvas
                bool Under(int i) { for (int q = els[i].Parent; q >= 0; q = els[q].Parent) if (q == canvas) return true; return false; }
                // Marks: the marker glyphs and the hero (a dot, or the arrow's accent rows).
                var marks = Enumerable.Range(canvas + 1, els.Length - canvas - 1)
                    .Where(i => Under(i) && els[i].Rect.W > 0f && els[i].Rect.H > 0f
                                && (els[i].Kind == UiElementKind.Shape
                                    || (els[i].Kind == UiElementKind.Box && ctx.Report.FillToneOf(i) == Tone.Accent)))
                    .ToArray();
                marksSeen += marks.Length;
                foreach (int t in Enumerable.Range(canvas + 1, els.Length - canvas - 1).Where(i => Under(i) && els[i].Kind == UiElementKind.Text))
                {
                    int plateIx = els[t].Parent;
                    var plate = els[plateIx].Rect;
                    plates++;
                    foreach (int mi in marks)
                    {
                        var m = els[mi].Rect;
                        if (!(m.X < plate.X + plate.W - 0.5f && plate.X < m.X + m.W - 0.5f && m.Y < plate.Y + plate.H - 0.5f && plate.Y < m.Y + m.H - 0.5f))
                            continue;
                        meetings++;
                        if (mi < plateIx)
                            problems.Add($"{sample.Slug}@{size.Name}: {UiTestKit.RunText(ctx, t)} plate {plate} drawn over mark {m}");
                    }
                }
            }
        // The stimulus: plates, marks, and pairs that meet (the order is only
        // tested where they do).
        Assert.True(plates > 0 && marksSeen > 0 && meetings > 0, $"{plates} plates, {marksSeen} marks, {meetings} meetings");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct().Take(12)));
    }
}
