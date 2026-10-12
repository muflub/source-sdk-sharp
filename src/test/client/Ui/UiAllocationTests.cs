using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;
using Xunit.Abstractions;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// Zero allocation per frame in describe, layout and emit (plan_client_ui §5.2):
/// the scoreboard and the HUD samples, laid out as the game will (names not
/// recorded), measured by GC.GetAllocatedBytesForCurrentThread over 600 frames
/// after the arena has grown.
/// </summary>
[Collection(ProcessWide.Name)]
public class UiAllocationTests(ITestOutputHelper output)
{
    private static readonly UiFontLibrary Fonts = new();

    public static TheoryData<string, string> Screens() => new()
    {
        { "scoreboard", "12-players-two-teams" },
        { "scoreboard", "full-server-16-players" },
        { "hud", "full-health-smg" },
        { "hud", "low-health-hurt-from-the-left" },
        { "hud", "map-text-hints-empty-clip" },
        { "hud", "smg-2-of-3-grenades" },
        { "hud", "secondary-no-clip" },
        { "teammenu", "joining" },
        { "chat", "busy-chat" },
        { "console", "busy-log" },
        { "console", "autocomplete-open" },
        { "serverbrowser", "2-000-servers-by-ping" },
        { "serverbrowser", "filtered-to-dm-not-empty" },
        { "serverbrowser", "refreshing" },
        { "mouseoptions", "stock-settings" },
        { "mouseoptions", "too-slow-invalid" },
        { "keybindings", "stock-binds" },
        // The descent HUD per frame (docs/plans/descent_ui.md §6 step 4):
        // orbs, belt, party frames, boss bar and cast bar all describe live.
        { "descenthud", "healthy-party-of-two-map-revealed" },
        { "descenthud", "low-life-broken-belt-boss-engaged-casting-gear-broken" },
        { "hud", "kill-feed-busy" },
        { "hud", "kill-feed-long-names-no-teams" },
        { "spectarget", "rebels-full-health-smg" },
    };

    internal const int Frames = 600;

    /// <summary>The most windows measured before giving up on a GC-free one.</summary>
    internal const int MaxWindows = 6;

    /// <summary>
    /// THE MEASURE: this thread's allocated bytes over <paramref name="run"/>, in
    /// a window in which no collection ran anywhere. A collection on ANY thread
    /// (a fact elsewhere calling GC.Collect, the loader facts' leak arm) retires
    /// this thread's allocation context and moves its count by what was left
    /// in it: measured 2026-10-05 on df8808cc, every one of four failing runs of
    /// the loader and allocation facts together had gen0/gen2 collections inside
    /// the failing window on the measuring thread (the same thread id start to
    /// end), 3,120 or 98,352 bytes, and no clean window allocated. So a window
    /// with a collection is not a measurement: it is run again, up to
    /// <see cref="MaxWindows"/>; and a clean window must be zero.
    /// </summary>
    internal static long CleanWindow(Action run)
    {
        for (int w = 0; w < MaxWindows; w++)
        {
            int gc = GC.CollectionCount(0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            run();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            if (GC.CollectionCount(0) == gc)
                return bytes;
        }
        Assert.Fail($"every one of {MaxWindows} windows had a collection in it: no clean measurement");
        return -1;
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void A_frame_allocates_nothing(string group, string slug)
    {
        var sample = PreviewRun.AllSamples().Single(s => !s.Gallery && s.Group == group && s.Slug == slug);
        var screen = sample.Create();
        var ctx = new UiContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language(), []);
        Assert.False(ctx.RecordNames);
        var info = UiSamples.Screen(1920, 1080);

        void Frame(double now)
        {
            ctx.BeginFrame(info, now);
            var ui = ctx.BeginScreen(screen.Name, screen.Layer);
            screen.Describe(ref ui);
            ctx.EndScreen();
            ctx.EndFrame();
        }

        // Warm-up: the arena, the render list, the state pools grow to this screen.
        for (int i = 0; i < 60; i++)
            Frame(sample.Now - 1 + i / 60.0);

        long bytes = CleanWindow(() =>
        {
            for (int i = 0; i < Frames; i++)
                Frame(sample.Now + i / 60.0);
        });

        int ops = ctx.List.OpCount;
        output.WriteLine($"{group}/{slug}: {bytes} bytes over {Frames} frames ({bytes / (double)Frames:0.##} per frame), {ops} draw ops a frame");
        Assert.True(ops > 10, $"{ops} draw ops: the screen drew (almost) nothing, so zero bytes would prove nothing");
        Assert.Equal(0, bytes);
    }

    /// <summary>
    /// The game's path (plan_client_ui U2): the context measures through
    /// VguiBackend and the backend draws the list through the surface, every
    /// frame, with nothing allocated once the fonts are made.
    /// </summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public void A_frame_through_vgui_allocates_nothing(string group, string slug)
    {
        var sample = PreviewRun.AllSamples().Single(s => !s.Gallery && s.Group == group && s.Slug == slug);
        var screen = sample.Create();
        var surface = new CountingSurface();
        var backend = new SourceSharp.Client.Vgui.VguiBackend(surface, Fonts, new SourceSharp.Client.Vgui.UiFontFiles([]));
        var ctx = new UiContext(SourceSharpTheme.Create(), backend, UiSamplesTests.Language(), []);
        var info = UiSamples.Screen(1920, 1080);

        void Frame(double now)
        {
            ctx.BeginFrame(info, now);
            var ui = ctx.BeginScreen(screen.Name, screen.Layer);
            screen.Describe(ref ui);
            ctx.EndScreen();
            ctx.EndFrame();
            backend.Draw(ctx.List.AsDrawList());
        }

        for (int i = 0; i < 60; i++)
            Frame(sample.Now - 1 + i / 60.0);

        long draws = surface.Draws;
        int windows = 0;
        long bytes = CleanWindow(() =>
        {
            windows++;
            for (int i = 0; i < Frames; i++)
                Frame(sample.Now + i / 60.0);
        });

        long perFrame = (surface.Draws - draws) / Frames / windows;
        output.WriteLine($"{group}/{slug}: {bytes} bytes over {Frames} frames, {perFrame} surface calls a frame");
        Assert.True(perFrame > 10, $"{perFrame} surface calls a frame: the backend drew (almost) nothing");
        Assert.Equal(0, bytes);
    }

    /// <summary>A surface that counts and keeps nothing (so it allocates nothing itself).</summary>
    private sealed class CountingSurface : SourceSharp.Client.Interop.IUiSurface
    {
        public long Draws;
        private int _handles;

        public void FilledRect(int x0, int y0, int x1, int y1, uint rgba) => Draws++;
        public void FilledRectFade(int x0, int y0, int x1, int y1, uint rgba, int alpha0, int alpha1, bool horizontal) => Draws++;
        public void OutlinedRect(int x0, int y0, int x1, int y1, uint rgba) => Draws++;
        public void Line(int x0, int y0, int x1, int y1, uint rgba) => Draws++;
        public void TexturedRect(int texture, int x0, int y0, int x1, int y1, float s0, float t0, float s1, float t1, uint rgba) => Draws++;
        public void Polygon(int texture, ReadOnlySpan<float> xyuv, uint rgba) => Draws++;
        public void Text(int font, int x, int y, ReadOnlySpan<char> text, uint rgba) => Draws++;
        public void MeasureText(int font, ReadOnlySpan<char> text, out int width, out int height) => (width, height) = (0, 0);
        public void SetClip(bool enabled, int x0, int y0, int x1, int y1) => Draws++;
        public int TextureCreate() => ++_handles;
        public void TextureUpload(int texture, ReadOnlySpan<byte> rgba, int wide, int tall) { }
        public bool FontAddFile(string path) => true;
        public int FontCreate(string family, int tall, int weight, int vguiFlags) => ++_handles;
        public void FontMetrics(int font, out int tall, out int ascent) => (tall, ascent) = (20, 15);
        public int FontAdvances(int font, int first, Span<int> advances)
        {
            advances.Fill(9);
            return advances.Length;
        }
        public void ScreenSize(out int wide, out int tall) => (wide, tall) = (1920, 1080);
        public void PlaySound(string path) { }
        public void SetMenuInput(bool mouse, bool keyboard) { }
        public void SetCursor(int shape) { }
    }

    [Fact]
    public void The_measure_sees_an_allocation_in_describe()
    {
        // The negative control: the same loop with a screen that makes a string a frame.
        var ctx = new UiContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language(), []);
        int n = 0;
        var screen = new TestScreen("alloc", (ref UiFrame ui) => { ui.Data((n++).ToString()); });
        for (int i = 0; i < 60; i++)
            UiTestKit.Frame(ctx, UiSamples.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui), now: i / 60.0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Frames; i++)
        {
            ctx.BeginFrame(UiSamples.Screen(1920, 1080), 1 + i / 60.0);
            var ui = ctx.BeginScreen("alloc");
            screen.Describe(ref ui);
            ctx.EndScreen();
            ctx.EndFrame();
        }
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before >= Frames * 20);
    }

    /// <summary>
    /// The measure itself: a background GC run by another thread must not move
    /// this thread's allocated-bytes count. It does under concurrent GC (the
    /// runtime counts the unused rest of this thread's allocation context, a few
    /// kB, as allocated when a background GC retires it: 25-33 of ~80 windows
    /// in a 3 s run, while blocking GCs moved 0 of ~30). That was the flake
    /// (2026-10-05: 10 odd windows in 15 full-suite runs, 0 in 15 with
    /// DOTNET_gcConcurrent=0). The client test project turns concurrent GC off
    /// (SourceSharp.Client.Tests.csproj), which this fact holds.
    /// </summary>
    // Measured 2026-10-05 beside the other two managed legs, 20 runs: 111-218
    // gen2 collections and 12-20 windows in 1.5 s, none moved.
    private const int MinCollections = 100, MinWindows = 10;

    [Fact]
    public void A_background_gc_on_another_thread_does_not_move_the_count()
    {
        var sample = PreviewRun.AllSamples().Single(s => !s.Gallery && s.Group == "hud" && s.Slug == "smg-2-of-3-grenades");
        var screen = sample.Create();
        var ctx = new UiContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language(), []);
        var info = UiSamples.Screen(1920, 1080);
        double t = sample.Now;
        void Frame()
        {
            ctx.BeginFrame(info, t += 1 / 60.0);
            var ui = ctx.BeginScreen(screen.Name, screen.Layer);
            screen.Describe(ref ui);
            ctx.EndScreen();
            ctx.EndFrame();
        }
        for (int i = 0; i < 60; i++) Frame();

        bool stop = false;
        var junk = new List<object>();
        var collector = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                for (int i = 0; i < 2000; i++) junk.Add(new byte[64]);
                if (junk.Count > 100000) junk.Clear();
                GC.Collect(2, GCCollectionMode.Forced, blocking: false);   // a background GC, where concurrent GC is on
                Thread.Sleep(1);
            }
        }) { IsBackground = true };
        collector.Start();

        // Until enough of both has happened, not for a time: under a loaded box
        // (every leg of make test at once) a fixed 1.5 s saw as few gen2 GCs as
        // the old floor. The cap only bounds a collector that never runs.
        int windows = 0, moved = 0;
        long[] room = [];
        long cap = Environment.TickCount64 + 60_000;
        int gc2 = GC.CollectionCount(2);
        while ((windows < MinWindows || GC.CollectionCount(2) - gc2 < MinCollections) && Environment.TickCount64 < cap)
        {
            room = new long[600];   // leave room in this thread's allocation context, as each fact does
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) Frame();
            if (GC.GetAllocatedBytesForCurrentThread() != before) moved++;
            windows++;
        }
        Volatile.Write(ref stop, true);
        collector.Join();
        GC.KeepAlive(room);

        Assert.True(GC.CollectionCount(2) - gc2 >= MinCollections && windows >= MinWindows,
                    $"the other thread ran {GC.CollectionCount(2) - gc2} gen2 collections over {windows} windows: the stimulus reached the subject");
        Assert.Equal(0, moved);
        output.WriteLine($"{windows} windows of 200 frames, {GC.CollectionCount(2) - gc2} gen2 GCs elsewhere, {moved} moved");
    }
}
