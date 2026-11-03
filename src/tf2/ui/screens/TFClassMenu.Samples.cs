using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The TF2 class menu from fakes (the ui-screen loop's sample step): the
// states stock's CTFClassMenu is ever in, built through the same
// container the game uses. Every #TF2_ClassMenu_* token lives in
// game/mod_tf2/resource/tf2_english.txt.

public static class TFClassMenuSamples
{
    private static UiScreen Build(UiSampleServices s, FakeTFClassMenuSource? source = null,
                                  FakeTFClassMenuActions? actions = null)
    {
        s.Add<ITFClassMenuSource>(source ?? new FakeTFClassMenuSource());
        s.Add<ITFClassMenuActions>(actions ?? new FakeTFClassMenuActions());
        return s.Create<TFClassMenu>();
    }

    // The forced first pick on RED: nobody has a class, so no tile
    // carries the mark and Cancel stays hidden (tf_classmenu.cpp:1044-1081),
    // and the counts differ so the binding of each tile's number is visible.
    [UiSample("choose a class")]
    private static UiScreen FirstPick(UiSampleServices s) => Build(s,
        new FakeTFClassMenuSource
        {
            Team = 2,
            Players = [2, 1, 0, 1, 2, 3, 1, 0, 2],
        });

    // The class already chosen on RED: Soldier carries the mark, Cancel
    // shows, and the medic tile is at its limit — the state with every
    // label live, the lane's reference picture.
    [UiSample("on red as soldier")]
    private static UiScreen OnRedAsSoldier(UiSampleServices s) => Build(s,
        new FakeTFClassMenuSource
        {
            Team = 2,
            Current = 3,
            Full = 5,
            Players = [2, 1, 3, 1, 1, 2, 1, 0, 2],
        });

    // A spectator: stock draws no counts (:1446-1449) and there is no
    // class to cancel out of.
    [UiSample("spectating")]
    private static UiScreen Spectating(UiSampleServices s) => Build(s,
        new FakeTFClassMenuSource
        {
            Team = 1,
            Players = [2, 1, 3, 1, 1, 2, 1, 0, 2],
        });
}

/// <summary>The team, the nine tiles and the local player's state as the
/// menu reads them; at rest nobody is on a team, nobody has a class and
/// no class is full.</summary>
public sealed class FakeTFClassMenuSource : ITFClassMenuSource
{
    /// <summary>The display names, in TF_CLASS order (stock's
    /// <c>g_aPlayerClassNames_NonLocalized</c>).</summary>
    private readonly string[] Names =
        ["Scout", "Sniper", "Soldier", "Demoman", "Medic", "Heavy", "Pyro", "Spy", "Engineer"];

    public int Team { get; init; }

    /// <summary>The raw class number the local player is on (0: none).</summary>
    public int Current { get; init; }

    /// <summary>The raw class number at its limit (0: none).</summary>
    public int Full { get; init; }

    /// <summary>The teammates on each class, in TF_CLASS order (the
    /// local player not counted, as stock's counts work).</summary>
    public int[] Players { get; init; } = [0, 0, 0, 0, 0, 0, 0, 0, 0];

    public TFClassChoice Choice(int classNumber) =>
        new(classNumber, Names[classNumber - 1], Players[classNumber - 1],
            classNumber == Current, classNumber == Full);

    public bool HasClass => Current > 0;
}

/// <summary>What the clicks did — the screen fact's recorder, read after
/// the click's frame.</summary>
public sealed class FakeTFClassMenuActions : ITFClassMenuActions
{
    public List<int> Choices { get; } = [];
    public int Randoms { get; private set; }
    public int Cancels { get; private set; }
    public int EditLoadouts { get; private set; }
    public int Resets { get; private set; }

    /// <summary>The shows the panel raised (stock's menuopen arm).</summary>
    public int Opens { get; private set; }

    /// <summary>The closes the host told (stock's menuclosed arm).</summary>
    public int Closes { get; private set; }

    public void Opened() => Opens++;

    public void Closed() => Closes++;

    public void Choose(int classNumber) => Choices.Add(classNumber);

    public void ChooseRandom() => Randoms++;

    public void Cancel() => Cancels++;

    public void EditLoadout() => EditLoadouts++;

    public void ResetLoadout() => Resets++;
}
