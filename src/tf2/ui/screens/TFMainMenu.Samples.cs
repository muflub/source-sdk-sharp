using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The TF2 main menu from fakes (the ui-screen loop's sample step): the
// launch menu with and without a holiday, the menu in a level, and the two
// prompts. Every #TF2_MainMenu_* token lives in
// game/mod_tf2/resource/tf2_english.txt.

public static class TFMainMenuSamples
{
    private static TFMainMenu Build(UiSampleServices s, bool inLevel, FakeTFMainMenuArt? art = null)
    {
        s.Add<IMainMenuSource>(new FakeTFMainMenuSource { InLevel = inLevel });
        s.Add<ITFMainMenuArt>(art ?? new FakeTFMainMenuArt());
        s.Add<ITFMainMenuActions>(new FakeTFMainMenuActions());
        return s.Create<TFMainMenu>();
    }

    // The launch menu on an ordinary day: the GameUI's background under a
    // class portrait.
    [UiSample("launch menu")]
    private static UiScreen Launch(UiSampleServices s) => Build(s, inLevel: false);

    // Halloween: the holiday's own backdrop under the portrait.
    [UiSample("launch menu, halloween")]
    private static UiScreen Halloween(UiSampleServices s) => Build(s, inLevel: false, new FakeTFMainMenuArt
    {
        Background = "console/title_team_halloween2015_widescreen",
        Character = "console/characters/heavy",
    });

    // In a level: Disconnect for Quit, Resume, the four in-game buttons; no
    // backdrop or portrait.
    [UiSample("in a level")]
    private static UiScreen InLevel(UiSampleServices s) => Build(s, inLevel: true);

    [UiSample("quit prompt")]
    private static UiScreen QuitPrompt(UiSampleServices s)
    {
        var screen = Build(s, inLevel: false);
        screen.PromptQuit();
        return screen;
    }

    [UiSample("leave prompt")]
    private static UiScreen LeavePrompt(UiSampleServices s)
    {
        var screen = Build(s, inLevel: true);
        screen.PromptLeave();
        return screen;
    }
}

/// <summary>The core's menu answers as the TF2 menu reads them.</summary>
public sealed class FakeTFMainMenuSource : IMainMenuSource
{
    public bool InLevel { get; init; }
    public bool CanPlay => true;
    public int Resumes { get; private set; }
    public int Quits { get; private set; }

    public void Play() { }
    public void Resume() => Resumes++;
    public void OpenOptions() { }
    public void Quit() => Quits++;
}

/// <summary>The art as the adapter picks it; at rest no holiday and the heavy.</summary>
public sealed class FakeTFMainMenuArt : ITFMainMenuArt
{
    public string? Background { get; init; }
    public string? Character { get; init; } = "console/characters/heavy";
}

/// <summary>What the clicks did, by name, in order.</summary>
public sealed class FakeTFMainMenuActions : ITFMainMenuActions
{
    public List<string> Done { get; } = [];

    public void FindAGame() => Done.Add("find");
    public void HostAGame() => Done.Add("host");
    public void Options() => Done.Add("options");
    public void AdvancedOptions() => Done.Add("advanced options");
    public void Items() => Done.Add("items");
    public void Disconnect() => Done.Add("disconnect");
    public void CallVote() => Done.Add("call vote");
    public void MutePlayers() => Done.Add("mute players");
    public void RequestCoach() => Done.Add("request coach");
    public void ReportPlayer() => Done.Add("report player");
}
