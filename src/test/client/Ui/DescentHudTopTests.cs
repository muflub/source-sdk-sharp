using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Descent.Client.Ui;
using TF2.Descent.Client.Ui.Screens;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The Descent HUD's top and right edge (plan_descent_logic L5, UI2): the XP
/// bar across the top centre at every size, nothing there without a hero,
/// the boss bar under it, no minimap panel until a room is revealed, and the
/// HUD takes TF2's player status corner.
/// </summary>
public class DescentHudTopTests
{
    private static readonly UiFontLibrary Fonts = new();

    private static UiElementInfo[] Render(UiScreen hud, in ScreenInfo screen, out UiContext ctx)
    {
        ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, hud, screen, 10.0);
        return UiTestKit.Elements(ctx);
    }

    private static UiScreen Sample(string slug)
        => Assert.Single(PreviewRun.AllSamples(), s => !s.Gallery && s.Group == "descenthud" && s.Slug == slug).Create();

    [Fact]
    public void The_xp_bar_is_centred_at_the_top_at_every_size()
    {
        foreach (var size in UiSamples.Sizes())
        {
            var els = Render(Sample("healthy-party-of-two-map-revealed"), size.Screen, out var ctx);
            var xp = Assert.Single(els, e => UiTestKit.NameOf(ctx, e) == "xp");
            float w = size.W, h = size.H;
            Assert.True(xp.Rect.Y < 0.05f * h, $"{size.Name}: the xp bar's top is at {xp.Rect.Y} of {h}");
            Assert.True(Math.Abs(xp.Rect.X + xp.Rect.W / 2 - w / 2) < 2f, $"{size.Name}: the xp bar is not centred ({xp.Rect})");
        }
    }

    [Fact]
    public void Without_a_hero_there_is_no_xp_bar()
    {
        var els = Render(Sample("solo-no-hero-yet"), UiSamples.Screen(1920, 1080), out var ctx);

        Assert.DoesNotContain(els, e => UiTestKit.NameOf(ctx, e) == "xp");
        Assert.Contains(els, e => UiTestKit.NameOf(ctx, e) == "belt");   // the HUD drew
    }

    [Fact]
    public void The_boss_bar_hangs_under_the_xp_bar()
    {
        var els = Render(Sample("low-life-broken-belt-boss-engaged-casting-gear-broken"), UiSamples.Screen(1920, 1080), out var ctx);
        var xp = Assert.Single(els, e => UiTestKit.NameOf(ctx, e) == "xp");
        var boss = Assert.Single(els, e => UiTestKit.NameOf(ctx, e) == "boss");

        Assert.True(boss.Rect.Y >= xp.Rect.Y + xp.Rect.H, $"boss {boss.Rect} overlaps xp {xp.Rect}");
    }

    private static UiScreen Hud(int rooms)
    {
        var s = new UiSampleServices();
        s.Add<IDescentVitals>(new FakeVitals());
        s.Add<IDescentBelt>(new FakeBelt());
        s.Add<IDescentXp>(new FakeXp());
        s.Add<IDescentParty>(new FakeParty());
        s.Add<IDescentBoss>(new FakeBoss());
        s.Add<IDescentCastBar>(new FakeCast());
        s.Add<IDescentGearWarning>(new FakeGearWarn());
        s.Add<IDescentAutomap>(rooms > 0 ? new FakeAutomap() : new NoRooms());
        return s.Create<DescentHud>();
    }

    private sealed class NoRooms : IDescentAutomap
    {
        public int RoomCount => 0;
        public float RoomX(int room) => 0f;
        public float RoomY(int room) => 0f;
        public float RoomW(int room) => 0f;
        public float RoomH(int room) => 0f;
        public int RoomStorey(int room) => 0;
        public bool RoomCurrentStorey(int room) => false;
        public int DoorStubCount => 0;
        public float DoorStubX(int i) => 0f;
        public float DoorStubY(int i) => 0f;
        public float DoorStubW(int i) => 0f;
        public float DoorStubH(int i) => 0f;
        public int MarkerCount => 0;
        public DescentMarker Marker(int i) => default;
        public int StoreyCount => 0;
        public int HeroStorey => 0;
        public float HeroX => 0f;
        public float HeroY => 0f;
        public float HeroHeadingDeg => 0f;
        public int FocusStorey => 0;
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1080, 1920)]
    public void No_minimap_is_drawn_until_a_room_is_revealed(int w, int h)
    {
        var shown = Render(Hud(rooms: 3), UiSamples.Screen(w, h), out var withRooms);
        Assert.Contains(shown, e => UiTestKit.NameOf(withRooms, e).StartsWith("AutomapCanvas[minimap]", StringComparison.Ordinal));

        var none = Render(Hud(rooms: 0), UiSamples.Screen(w, h), out var empty);
        Assert.DoesNotContain(none, e => UiTestKit.NameOf(empty, e).StartsWith("AutomapCanvas[minimap]", StringComparison.Ordinal));
    }

    [Fact]
    public void The_hud_takes_tf2s_player_status_corner_and_team_status()
        => Assert.Equal([TF2.Client.Ui.TFHud.PlayerStatusElement, TF2.Client.Ui.TFHud.TeamStatusElement],
                        Sample("healthy-party-of-two-map-revealed").Takes);

    /// <summary>The corner stays under TF2's chat (its box's bottom is 85 of 480 units
    /// up, 191 du): with a party of one the column ends below it at 1920x1080.</summary>
    [Fact]
    public void A_heros_corner_stays_under_tf2s_chat()
    {
        var s = new UiSampleServices();
        s.Add<IDescentVitals>(new FakeVitals());
        s.Add<IDescentBelt>(new FakeBelt());
        s.Add<IDescentXp>(new FakeXp());
        s.Add<IDescentParty>(new FakeParty { Members = [new("Bugha", "#Descent_Class_Soldier", 3, 140, 140, false, true, false, 0, false)] });
        s.Add<IDescentBoss>(new FakeBoss());
        s.Add<IDescentCastBar>(new FakeCast());
        s.Add<IDescentGearWarning>(new FakeGearWarn());
        s.Add<IDescentAutomap>(new NoRooms());
        var els = Render(s.Create<DescentHud>(), UiSamples.Screen(1920, 1080), out var ctx);
        var corner = Assert.Single(els, e => UiTestKit.NameOf(ctx, e) == "corner");

        Assert.True(1080 - corner.Rect.Y <= 85 * 1080 / 480f, $"the corner rises {1080 - corner.Rect.Y} px, into the chat's band");
    }
}
