using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>The chat as drawn: clear of the aux power at its tallest, a name in its own colour and a colon after it.</summary>
public class ChatPlacementTests
{
    private static readonly UiFontLibrary Fonts = new();

    private static UiContext Render(UiScreen screen, int w = 1920, int h = 1080)
    {
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        // A second frame, late: the aux power fades in.
        UiSamples.Render(ctx, screen, UiSamples.Screen(w, h), 100.0);
        UiSamples.Render(ctx, screen, UiSamples.Screen(w, h), 110.0);
        return ctx;
    }

    private static UiScreen Sample(string group, string slug)
        => PreviewRun.AllSamples().Single(s => !s.Gallery && s.Group == group && s.Slug == slug).Create();

    private static int At(UiContext ctx, string text)
    {
        var els = UiTestKit.Elements(ctx);
        for (int i = 0; i < els.Length; i++)
            if (string.Equals(UiTestKit.RunText(ctx, i), text, StringComparison.OrdinalIgnoreCase))
                return i;
        throw new Xunit.Sdk.XunitException($"nothing drew {text}");
    }

    private static UiElementInfo Drew(UiContext ctx, string text) => UiTestKit.Elements(ctx)[At(ctx, text)];

    private static UiScreen Lines(params ChatLine[] lines)
    {
        var s = new UiSampleServices();
        s.Add<IChatSource>(new FakeChat { Lines = lines });
        return s.Create<Chat>();
    }

    [Fact]
    public void The_lines_and_the_input_clear_the_aux_power_at_its_tallest()
    {
        float auxTop = Drew(Render(Sample("hud", "aux-power-flashlight-and-oxygen")), "Aux power").Rect.Y;
        var line = Drew(Render(Lines(new ChatLine("Alyx", 3, "hello", false, 1f))), "hello").Rect;
        var input = Drew(Render(Sample("chatinput", "say-typing")), "Say").Rect;

        // Texts measured, so the two panels' padding between them: their edges must not meet.
        Assert.True(input.Y + input.H + 32 <= auxTop, $"the input's foot {input.Y + input.H} is within 32 of the aux power's top {auxTop}");
        Assert.True(line.Y + line.H <= input.Y, $"a line's foot {line.Y + line.H} is under the input's top {input.Y}");
    }

    [Fact]
    public void A_name_with_no_team_is_not_the_colour_of_what_it_said()
    {
        var ctx = Render(Lines(new ChatLine("lodle", 0, "hello", false, 1f)));
        Assert.Equal(Tone.Warning, ctx.Report.TextToneOf(At(ctx, "lodle")));
        Assert.NotEqual(Tone.Warning, ctx.Report.TextToneOf(At(ctx, "hello")));
    }

    [Fact]
    public void A_colon_follows_the_name_before_what_it_said()
    {
        var ctx = Render(Lines(new ChatLine("lodle", 0, "hello", false, 1f)));
        var name = Drew(ctx, "lodle").Rect;
        var colon = Drew(ctx, " :").Rect;
        var said = Drew(ctx, "hello").Rect;
        Assert.True(name.X < colon.X && colon.X < said.X, $"{name.X} {colon.X} {said.X}");
    }
}
