using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// What a UI-only reload carries (plan_client_ui U5): a layer's open screens
/// and its context's focus and per-id state, as bytes, into a new layer built
/// from the same screens' code.
/// </summary>
public class UiCarryTests
{
    private static readonly TestScreen.Describer List = (ref UiFrame ui) =>
    {
        using (ui.Scroll("s").Size(Size.Du(200), Size.Du(100)).Name("s"))
            for (int i = 0; i < 6; i++)
                ui.Rect(Tone.Accent, Size.Fill, Size.Du(50)).Name($"row{i}");
    };

    private static readonly TestScreen.Describer Other = (ref UiFrame ui) => ui.Rect(Tone.Accent, Size.Du(10), Size.Du(10));

    private static UiLayer Layer(UiContext ctx, params TestScreen[] screens)
        => new(screens, ctx, new SvgBackend(new SvgBackendTests.FixedMetrics()));

    private static void Run(UiLayer layer, ref double now, int frames = 90)
    {
        for (int i = 0; i < frames; i++)
            layer.Frame(UiLayerKind.Menu, Screen(1920, 1080), now += 1 / 60.0);
    }

    private static Type? Resolve(string name)
        => typeof(UiContext).Assembly.GetType(name) ?? Type.GetType(name);

    [Fact]
    public void A_scrolled_list_is_still_scrolled_after_the_carry()
    {
        var oldCtx = Context();
        var old = Layer(oldCtx, new TestScreen("list", List));
        old.Open("list");
        double now = 1;
        Run(old, ref now, 1);
        old.Input(new UiInputEvent(UiInputKind.Wheel, 10, 10, 0, '\0', -2));
        Run(old, ref now);
        float scrolled = Named(oldCtx, "row0").Rect.Y;
        Assert.True(scrolled < 0, $"the stimulus scrolled the list ({scrolled})");

        var newCtx = Context();
        var fresh = Layer(newCtx, new TestScreen("list", List));
        Assert.Equal(1, fresh.Restore(old.Carry(), Resolve));
        Run(fresh, ref now, 1);
        Assert.Equal(scrolled, Named(newCtx, "row0").Rect.Y);
    }

    [Fact]
    public void Without_the_carry_a_new_layer_starts_at_the_top()
    {
        var ctx = Context();
        var fresh = Layer(ctx, new TestScreen("list", List));
        fresh.Open("list");
        double now = 1;
        Run(fresh, ref now, 1);
        Assert.Equal(Named(ctx, "s").Rect.Y, Named(ctx, "row0").Rect.Y);
    }

    [Fact]
    public void Open_screens_come_back_and_one_the_new_build_dropped_is_skipped()
    {
        var old = Layer(Context(), new TestScreen("list", List), new TestScreen("other", Other));
        old.Open("list");
        old.Open("other");
        double now = 1;
        Run(old, ref now, 1);

        var fresh = Layer(Context(), new TestScreen("list", List));
        Assert.Equal(1, fresh.Restore(old.Carry(), Resolve));
        Assert.Equal(["list"], fresh.OpenScreens());
    }

    [Fact]
    public void A_state_type_that_changed_size_starts_fresh()
    {
        var oldCtx = Context();
        var old = Layer(oldCtx, new TestScreen("list", List));
        old.Open("list");
        double now = 1;
        Run(old, ref now, 1);
        old.Input(new UiInputEvent(UiInputKind.Wheel, 10, 10, 0, '\0', -2));
        Run(old, ref now);

        // The new build's ScrollState is some other shape: its name resolves to a type of another size.
        var newCtx = Context();
        var fresh = Layer(newCtx, new TestScreen("list", List));
        var (restored, skipped) = newCtx.Restore(oldCtx.Carry(), _ => typeof(Guid));
        Assert.True(skipped > 0 && restored == 0, $"{restored} restored, {skipped} skipped");
        fresh.Open("list");
        Run(fresh, ref now, 1);
        Assert.Equal(Named(newCtx, "s").Rect.Y, Named(newCtx, "row0").Rect.Y);
    }

    [Fact]
    public void The_focus_is_carried()
    {
        var ctx = Context();
        var layer = Layer(ctx, new TestScreen("list", List));
        layer.Open("list");
        double now = 1;
        Run(layer, ref now, 1);
        ctx.Input(new UiInputEvent(UiInputKind.MouseDown, 10, 10, (int)UiKey.MouseLeft, '\0', 0));
        ctx.Input(new UiInputEvent(UiInputKind.MouseUp, 10, 10, (int)UiKey.MouseLeft, '\0', 0));
        Run(layer, ref now, 2);
        Assert.NotEqual(0u, ctx.Focused);

        var next = Context();
        next.Restore(ctx.Carry(), Resolve);
        Assert.Equal(ctx.Focused, next.Focused);
    }
}
