using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The tooltip part (plan_client_ui §5.3): a short note that appears under an
/// element once the pointer has rested on it for the theme's hover delay
/// (stock vgui's Tooltip waits 500 ms), fades in, and goes when the pointer
/// leaves. It is an overlay, so no clip of the element's parents cuts it.
/// </summary>
public class UiTooltipTests
{
    private const string Note = "TooltipNoteText";

    /// <summary>A button at a known place with a tooltip on it; answers whether the tooltip drew.</summary>
    private static bool Draw(UiContext ctx, double now, float delay = UiWidgets.TooltipDelaySeconds)
    {
        bool shown = false;
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
        {
            using (ui.Column(Space.None, Align.Start).At(UiAnchor.TopLeft).Inset(Raw.Du(100), Raw.Du(100)))
            {
                var box = ui.Box("target").Size(Size.Du(200), Size.Du(40)).Fill(Tone.SurfaceRaised).Name("target");
                var hit = ui.Interact(box);
                using (box) { }
                shown = ui.Tooltip("target-tip", box, hit.Hovered, Note, delay);
            }
        }, now);
        return shown;
    }

    private static void MoveTo(UiContext ctx, float x, float y)
        => ctx.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));

    private static bool NoteDrawn(UiContext ctx) => Svg(ctx).Contains(Note, StringComparison.Ordinal);

    [Fact]
    public void Nothing_shows_while_the_pointer_is_elsewhere()
    {
        var ctx = Context();
        MoveTo(ctx, 1500, 900);
        for (int i = 0; i < 60; i++)
            Assert.False(Draw(ctx, 1.0 + i / 60.0));
        Assert.False(NoteDrawn(ctx));
    }

    [Fact]
    public void A_pointer_resting_less_than_the_delay_shows_nothing()
    {
        var ctx = Context();
        Draw(ctx, 1.0);                       // lay the target out
        MoveTo(ctx, 150, 120);                // on the target
        bool any = false;
        for (int i = 1; i <= 18; i++)         // 0.3 s at 60 Hz, under the 0.5 s delay
            any |= Draw(ctx, 1.0 + i / 60.0);
        Assert.True(Named(ctx, "target").Rect.W > 0);
        Assert.False(any, "the tooltip showed before the hover delay");
        Assert.False(NoteDrawn(ctx));
    }

    [Fact]
    public void A_pointer_resting_past_the_delay_shows_the_note_under_the_element()
    {
        var ctx = Context();
        Draw(ctx, 1.0);
        MoveTo(ctx, 150, 120);
        bool shown = false;
        for (int i = 1; i <= 60; i++)         // a whole second on the target
            shown = Draw(ctx, 1.0 + i / 60.0);
        Assert.True(shown, "the tooltip never showed after a second's hover");
        Assert.True(NoteDrawn(ctx));

        var target = Named(ctx, "target").Rect;
        var tip = Named(ctx, "tooltip").Rect;
        Assert.True(tip.Y >= target.Y + target.H, $"the note is not under its element: {tip} vs {target}");
        Assert.Equal(target.X, tip.X, 1);
    }

    [Fact]
    public void Leaving_the_element_takes_the_note_away()
    {
        var ctx = Context();
        Draw(ctx, 1.0);
        MoveTo(ctx, 150, 120);
        double t = 1.0;
        for (int i = 1; i <= 60; i++)
            Draw(ctx, t += 1 / 60.0);
        Assert.True(NoteDrawn(ctx), "control: the note was up before the pointer left");

        MoveTo(ctx, 1500, 900);
        bool shown = true;
        for (int i = 1; i <= 60; i++)
            shown = Draw(ctx, t += 1 / 60.0);
        Assert.False(shown, "the tooltip stayed after the pointer left");
        Assert.False(NoteDrawn(ctx));
    }

    [Fact]
    public void Coming_back_waits_the_delay_again()
    {
        var ctx = Context();
        Draw(ctx, 1.0);
        MoveTo(ctx, 150, 120);
        double t = 1.0;
        for (int i = 1; i <= 60; i++)
            Draw(ctx, t += 1 / 60.0);
        MoveTo(ctx, 1500, 900);
        for (int i = 1; i <= 60; i++)
            Draw(ctx, t += 1 / 60.0);

        MoveTo(ctx, 150, 120);
        bool any = false;
        for (int i = 1; i <= 12; i++)         // 0.2 s back on it
            any |= Draw(ctx, t += 1 / 60.0);
        Assert.False(any, "the hover clock did not restart when the pointer came back");
    }

    [Fact]
    public void A_zero_delay_shows_at_once_for_previews()
    {
        var ctx = Context();
        Assert.True(Draw(ctx, 1.0, delay: 0f) == false, "control: not hovered, nothing even with no delay");
        MoveTo(ctx, 150, 120);
        Assert.True(Draw(ctx, 1.0 + 1 / 60.0, delay: 0f));
    }
}
