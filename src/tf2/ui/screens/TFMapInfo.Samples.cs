using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The TF2 map info menu from fakes (the ui-screen loop's sample step):
// the states stock's CTFMapInfoMenu is ever in, built through the same
// container the game uses. Every #TF2_MapInfo_* token lives in
// game/mod_tf2/resource/tf2_english.txt.

public static class TFMapInfoSamples
{
    private static UiScreen Build(UiSampleServices s, FakeTFMapInfoSource? source = null,
                                  FakeTFMapInfoActions? actions = null)
    {
        s.Add<ITFMapInfoSource>(source ?? new FakeTFMapInfoSource());
        s.Add<ITFMapInfoActions>(actions ?? new FakeTFMapInfoActions());
        return s.Create<TFMapInfo>();
    }

    // The text window's OK raised it: no team yet, so BACK shows and the
    // gate reads CONTINUE (tf_mapinfomenu.cpp:230-240).
    [UiSample("the briefing")]
    private static UiScreen Briefing(UiSampleServices s) => Build(s, new FakeTFMapInfoSource
    {
        MapName = "2FORT",
        GameMode = "Capture the Flag",
        Briefing = "The two bases face each other across a covered bridge.\n"
                 + "Take the enemy intelligence back to your base's capture\n"
                 + "point while defending your own.",
        Photo = "vgui/maps/menu_photos_ctf_2fort",
    });

    // On a team (the lobby-assigned case): BACK is gone and the gate
    // reads CLOSE (:241-250).
    [UiSample("on a team")]
    private static UiScreen OnTeam(UiSampleServices s) => Build(s, new FakeTFMapInfoSource
    {
        MapName = "2FORT",
        GameMode = "Capture the Flag",
        Team = 2,
    });

    // A map with no photo and no briefing: stock hides both
    // (:344-346, :371-377).
    [UiSample("an unknown map")]
    private static UiScreen UnknownMap(UiSampleServices s) => Build(s, new FakeTFMapInfoSource
    {
        MapName = "SS_SANDBOX",
    });
}

/// <summary>The map the menu reads; at rest an unknown map with nothing
/// but its name (stock's blank page).</summary>
public sealed class FakeTFMapInfoSource : ITFMapInfoSource
{
    public string MapName { get; init; } = "";
    public string GameMode { get; init; } = "";
    public string Briefing { get; init; } = "";
    public string? Photo { get; init; }
    public int Team { get; init; }
}

/// <summary>What the clicks did — the screen fact's recorder, read after
/// the click's frame.</summary>
public sealed class FakeTFMapInfoActions : ITFMapInfoActions
{
    public int Continues { get; private set; }
    public int Backs { get; private set; }

    public void Continue() => Continues++;

    public void Back() => Backs++;
}
