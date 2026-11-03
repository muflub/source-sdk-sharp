using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// TF2's text window from fakes (the ui-screen loop's sample step): the
// state stock's CTFTextWindow is ever in — the server's message with
// its OK — built through the same container the game uses. The
// #TF2_TextWindow_Title token lives in game/mod_tf2/resource/
// tf2_english.txt.

public static class TFTextWindowSamples
{
    private static UiScreen Build(UiSampleServices s, FakeTFTextWindowSource? source = null,
                                  FakeTFTextWindowActions? actions = null)
    {
        s.Add<IMotdSource>(source ?? new FakeTFTextWindowSource());
        s.Add<ITFTextWindowActions>(actions ?? new FakeTFTextWindowActions());
        return s.Create<TFTextWindow>();
    }

    // The join message: stock's text kind, a title, a few lines and the
    // one button.
    [UiSample("the server's message")]
    private static UiScreen ServerMessage(UiSampleServices s) => Build(s,
        new FakeTFTextWindowSource
        {
            Title = "SERVER MESSAGE",
            Text = "Welcome to 2fort. Read the rules, mind the sewers, "
                 + "and pick a class when the menu opens.\n"
                 + "Have a good round.",
        });
}

/// <summary>The server's title and text as the VGUIMenu "info" carried
/// them (the core window's source shape, the same panel data).
/// A sample's data, fixed for the frame.</summary>
public sealed class FakeTFTextWindowSource : IMotdSource
{
    public string Title { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;
}

/// <summary>What the clicks did — the screen fact's recorder, read after
/// the click's frame.</summary>
public sealed class FakeTFTextWindowActions : ITFTextWindowActions
{
    public int Okays { get; private set; }

    public void Okay() => Okays++;
}
