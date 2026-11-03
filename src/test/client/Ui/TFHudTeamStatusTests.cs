using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The top team-status bar's stock rules (<c>CTFTeamStatus</c>,
/// tf_teamstatus.cpp): nothing without the local player on a game team
/// (ShouldDraw :520-535), one chip per connected player in entity order
/// with the local player the innermost of their team (PerformLayout
/// :454-457), the res's 19-unit chip at six a side and the shrunk share of
/// max_expand past it (:461), the class portrait grey once dead and the
/// enemy's a silhouette (Update :136-139, :196-227), the health bar and
/// its overheal tail same-team lives only (:157-167) and the respawn count
/// over the dead portrait (:126-133).
/// </summary>
public class TFHudTeamStatusTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void No_team_no_bar()
    {
        // The plain samples carry no teams: not even the backdrop.
        Assert.False(TFHudTests.HasElement("healthy-soldier", "match bg"));
        Assert.False(TFHudTests.HasElement("healthy-soldier", "team chip"));
    }

    [Fact]
    public void The_bar_draws_its_backdrop_art_and_one_chip_a_player()
    {
        var art = new FakeTextureLibrary(Names().ToArray());
        var ctx = Render(TwoSides(), art);
        Assert.NotNull(Find(ctx, "match bg"));       // the hudmatchstatus frame
        Assert.Equal(6, Named(ctx, "team chip").Length);
        // The centre art under the chips (z1) and the portraits over it.
        Assert.Contains("hud/comp_round_timer", art.Asked);
        Assert.Contains("vgui/class_portraits/soldier_alpha", art.Asked);
    }

    [Fact]
    public void The_backdrop_plate_stands_only_while_the_art_misses()
    {
        // Stock's BGFrame is its own line border over the material
        // (hudmatchstatus.res:210-228): once the material binds, the art is
        // the backdrop and the plate — fill and outline both — draws no quad
        // over it (the live shot's thin rectangle); the artless build keeps
        // the quad, Surface and line, as the plate the chips read over.
        var loaded = Render(TwoSides(), new FakeTextureLibrary(Names().ToArray()));
        Assert.False(DrawsQuad(loaded, "match bg"));
        var artless = Render(TwoSides(), new FakeTextureLibrary());
        Assert.True(DrawsQuad(artless, "match bg"));
    }

    /// <summary>Whether the named element emitted a quad: the plate's fill
    /// and outline live and die together in the one op.</summary>
    private static bool DrawsQuad(UiContext ctx, string name)
    {
        int e = IndexOf(ctx, name);
        foreach (var op in ctx.List.AsDrawList().Ops)
            if (op.Element == e && op.Kind == UiPrimitiveKind.Quad)
                return true;
        return false;
    }

    [Fact]
    public void The_local_player_is_the_innermost_of_their_team_and_enemies_lead_outward()
    {
        // Red grows east from c47: the local soldier is slot 0, the
        // team-mates step out; blue grows west, its enemies keyed in the
        // order seen. 19-unit chips, the 6v6 gap 4, at x2.25.
        var ctx = Render(TwoSides(), new FakeTextureLibrary(Names().ToArray()));
        float centre = 1920f / StockRes.Scale / 2f;   // the screen's centre, in units
        float LeftOf(int slot, bool blue) => blue
            ? (centre - 45f - slot * 23f - 19f) * StockRes.Scale
            : (centre + 47f + slot * 23f) * StockRes.Scale;
        var chips = Named(ctx, "team chip").Select(e => e.Rect.X).Order().ToArray();
        float[] expected =
        [
            LeftOf(2, blue: true), LeftOf(1, blue: true), LeftOf(0, blue: true),   // west, outermost first
            LeftOf(0, blue: false), LeftOf(1, blue: false), LeftOf(2, blue: false),
        ];
        // The layout lands on whole du: expect the rounded spot.
        for (int i = 0; i < expected.Length; i++)
            Assert.True(Abs(chips[i] - MathF.Round(expected[i])) < 0.1f,
                $"{i}: {chips[i]} != {MathF.Round(expected[i])} (all chips {string.Join(", ", chips)})");
    }

    [Fact]
    public void A_crowded_room_shrinks_the_chips_to_share_the_row()
    {
        // Seven a side: the gap ramps 4 -> 1 over 6..12 (7 -> 3.5) and the
        // width is (133 - 6 gaps) / 7 = 16 units, 36 du — not the 19-unit
        // rest chip.
        var ctx = Render(Crowd(), new FakeTextureLibrary(Names().ToArray()));
        var chips = Named(ctx, "team chip").Select(e => e.Rect.W).ToArray();
        Assert.Equal(14, chips.Length);
        foreach (float w in chips)
            Assert.True(Abs(w - 16f * StockRes.Scale) < 0.5f, $"{w}: not the shrunk width");
    }

    [Fact]
    public void Dead_teammates_grey_enemies_silhouette_and_the_class_of_the_local_stands()
    {
        var art = new FakeTextureLibrary(Names().ToArray());
        _ = Render(TwoSides(), art);
        Assert.Contains("vgui/class_portraits/soldier_alpha", art.Asked);        // the local red
        Assert.Contains("vgui/class_portraits/silhouette_alpha", art.Asked);     // a living enemy's hidden class
        Assert.Contains("vgui/class_portraits/heavy_grey_alpha", art.Asked);     // the dead team-mate
        Assert.Contains("vgui/class_portraits/spy_blue_grey_alpha", art.Asked);       // the dead enemy
    }

    [Fact]
    public void The_health_bar_rides_the_same_team_lives_with_the_overheal_tail()
    {
        var ctx = Render(TwoSides(), new FakeTextureLibrary(Names()));
        var bars = Named(ctx, "health bar");
        Assert.Equal(2, bars.Length);                // the local red and the living team-mate
        Assert.Single(Named(ctx, "overheal bar"));   // the local's 240 over the 200 ceiling
    }

    [Fact]
    public void The_dead_teammates_respawn_count_replaces_their_bar()
    {
        var ctx = Render(TwoSides(), new FakeTextureLibrary(Names().ToArray()));
        var waits = Named(ctx, "respawn");
        Assert.Single(waits);
        var list = ctx.List.AsDrawList();
        int e = IndexOf(ctx, "respawn");
        string text = default!;
        foreach (var op in list.Ops)
            if (op.Kind == UiPrimitiveKind.GlyphRun && op.Element == e)
            {
                var r = list.GlyphRuns[op.Index];
                text = list.Text.Slice(r.TextStart, r.TextLength).ToString();
            }
        Assert.Equal("20", text);
    }

    // The two samples' rows, built as the tfhud sample "the team bar both
    // sides" carries them: the local red soldier, a dead team-mate counting
    // its respawn, the overhealed blue, the silhouetted enemies.
    private static FakeTeamStatus TwoSides() => new()
    {
        LocalIndex = 2,
        Players =
        [
            new TFTeamStatusPlayer(1, Blue, 1, true, 100, 125, 185, -1),
            new TFTeamStatusPlayer(2, Red, 3, true, 240, 200, 300, -1),
            new TFTeamStatusPlayer(3, Blue, 6, true, 380, 300, 450, -1),
            new TFTeamStatusPlayer(4, Red, 5, true, 130, 150, 300, -1),
            new TFTeamStatusPlayer(5, Red, 6, false, -1, 300, 450, 20),
            new TFTeamStatusPlayer(6, Blue, 8, false, -1, 125, 185, -1),
        ],
    };

    private static FakeTeamStatus Crowd()
    {
        var rows = new TFTeamStatusPlayer[14];
        byte[] classes = [1, 3, 6, 5, 4, 7, 2];
        for (int i = 0; i < 7; i++)
        {
            int blue = 1 + i * 2;
            int red = 2 + i * 2;
            bool dead = i == 6;
            rows[i] = new TFTeamStatusPlayer(blue, Blue, classes[i], !dead, dead ? -1 : 100 + i * 10, 125, 185, dead ? 12 : -1);
            rows[7 + i] = new TFTeamStatusPlayer(red, Red, classes[(i + 2) % 7], true, 150 + i * 10, 200, 300, -1);
        }

        return new FakeTeamStatus { LocalIndex = 2, Players = rows };
    }

    // The names the panel asks over the two rows: the living team-mates'
    // portraits, the dead pair's greys, the enemies' silhouette and the
    // centre art — the art layers all draw.
    private static string[] Names() =>
    [
        "hud/comp_round_timer",
        "vgui/class_portraits/soldier_alpha",
        "vgui/class_portraits/medic_alpha",
        "vgui/class_portraits/heavy_grey_alpha",
        "vgui/class_portraits/spy_blue_grey_alpha",
        "vgui/class_portraits/silhouette_alpha",
    ];

    /// <summary>One frame of the tfhud screen over the given rows.</summary>
    private static UiContext Render(FakeTeamStatus teams, FakeTextureLibrary art)
    {
        var s = new UiSampleServices();
        var screen = TFHudSamples.Build(s, teams: teams, textures: art);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), 100.0);
        return ctx;
    }

    private static UiElementInfo[] Named(UiContext ctx, string name)
        => UiTestKit.Elements(ctx).Where(e => UiTestKit.NameOf(ctx, e) == name).ToArray();

    private static UiElementInfo? Find(UiContext ctx, string name)
    {
        var all = Named(ctx, name);
        return all.Length > 0 ? all[0] : null;
    }

    private static int IndexOf(UiContext ctx, string name)
    {
        var all = Named(ctx, name);
        return all.Length > 0
            ? Array.IndexOf(UiTestKit.Elements(ctx), all[0])
            : throw new InvalidOperationException($"no {name} drew");
    }

    private static float Abs(float x) => MathF.Abs(x);

    // The raw TF team numbers (tf_shareddefs.h): TF_TEAM_BLUE 3, TF_TEAM_RED 2.
    private const int Blue = 3;
    private const int Red = 2;
}
