using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>The console's suggestions as drawn: a click on a row takes it, and the raised row stays in view.</summary>
public class ConsoleCompletionsPanelTests
{
    private sealed class Source(int count) : IConsoleCompletionSource, IConsoleActions
    {
        public readonly List<int> Taken = [];

        /// <summary>What the list holds now; taking a row may shrink it, as the real source's rebuild does.</summary>
        public int Count { get; set; } = count;

        /// <summary>The count a take leaves (the real source rebuilds the list from the line it wrote).</summary>
        public int CountAfterTake { get; init; } = count;

        public int CompletionCount => Count;

        // Past the end throws, as the real source's list does.
        public ConsoleCompletion Completion(int i)
            => i < Count ? new($"cvar_{i}", "1", "", false) : throw new ArgumentOutOfRangeException(nameof(i));

        public int SelectedCompletion { get; set; }

        public void Submit() { }

        public void TakeCompletion(int i)
        {
            Taken.Add(i);
            Count = CountAfterTake;
        }
    }

    private sealed class Rig
    {
        private readonly ConsoleCompletionsPanel _panel;
        public readonly UiContext Ctx = UiTestKit.Context();

        public Rig(Source source)
        {
            _panel = new ConsoleCompletionsPanel(source, source);
            Frame();
        }

        public void Frame()
            => UiTestKit.Frame(Ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) =>
            {
                var field = ui.Box().Size(Size.Du(400), Size.Du(40)).At(UiAnchor.BottomLeft);
                using (field) { }
                ui.Sub("completions", _panel, new ConsoleCompletionsProps(field, 1200, false));
            });

        public bool Drew(string name) => Find(name) >= 0;

        public int Find(string name)
        {
            var els = UiTestKit.Elements(Ctx);
            return Array.FindIndex(els, e => UiTestKit.RunText(Ctx, Array.IndexOf(els, e)) == name);
        }

        public void Click(string name)
        {
            int i = Find(name);
            Assert.True(i >= 0, $"no row drew {name}");
            var r = UiTestKit.Elements(Ctx)[i].Rect;
            float x = r.X + r.W / 2, y = r.Y + r.H / 2;
            Ctx.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0f));
            Frame();
            Ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, 0, '\0', 0f));
            Frame();
            Ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, 0, '\0', 0f));
            Frame();
            Frame();
        }
    }

    [Fact]
    public void A_click_on_a_row_takes_that_suggestion()
    {
        var source = new Source(4);
        var rig = new Rig(source);
        rig.Click("cvar_2");
        Assert.Equal([2], source.Taken);
    }

    [Fact]
    public void A_click_takes_the_row_shown_when_the_list_has_walked_on()
    {
        var source = new Source(12) { SelectedCompletion = 10 };
        var rig = new Rig(source);
        rig.Click("cvar_9");
        Assert.Equal([9], source.Taken);
    }

    /// <summary>
    /// A take rebuilds the real list (MenuSources' TakeCompletion), and the panel
    /// took it from inside the loop that was drawing the rows: the next row read
    /// past the shrunk list's end and the console threw IndexOutOfRange (user's
    /// report, 2026-11-08). The take waits until the rows are drawn.
    /// </summary>
    [Fact]
    public void A_click_that_shrinks_the_list_draws_no_row_past_its_end()
    {
        var source = new Source(4) { CountAfterTake = 1 };
        var rig = new Rig(source);

        rig.Click("cvar_0");

        Assert.Equal([0], source.Taken);
        Assert.Equal(1, source.CompletionCount);
    }

    [Fact]
    public void The_raised_row_past_the_first_eight_is_drawn()
    {
        var rig = new Rig(new Source(12));
        Assert.True(rig.Drew("cvar_7"));
        Assert.False(rig.Drew("cvar_11"));

        var walked = new Rig(new Source(12) { SelectedCompletion = 11 });
        Assert.True(walked.Drew("cvar_11"));
        Assert.False(walked.Drew("cvar_3"));
    }
}
