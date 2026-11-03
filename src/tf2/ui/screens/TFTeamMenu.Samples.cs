using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The TF2 team menu from fakes (the ui-screen loop's sample step): the
// states stock's CTeamMenu is ever in, built through the same container
// the game uses. Every #TF2_TeamMenu_* token lives in
// game/mod_tf2/resource/tf2_english.txt.

public static class TFTeamMenuSamples
{
    private static UiScreen Build(UiSampleServices s, FakeTFTeamMenuSource? source = null,
                                  FakeTFTeamMenuActions? actions = null)
    {
        s.Add<ITFTeamMenuSource>(source ?? new FakeTFTeamMenuSource());
        s.Add<ITFTeamMenuActions>(actions ?? new FakeTFTeamMenuActions());
        return s.Create<TFTeamMenu>();
    }

    // The forced first pick: nobody is on a team, so no card carries the
    // mark and Cancel stays hidden (tf_teammenu.cpp:165), and the counts
    // differ so the binding of each card's number is visible.
    [UiSample("no team yet")]
    private static UiScreen FirstPick(UiSampleServices s) => Build(s,
        new FakeTFTeamMenuSource { RedPlayers = 2, BluePlayers = 1 });

    // The team already chosen: its card carries the mark and Cancel shows.
    [UiSample("on red")]
    private static UiScreen OnRed(UiSampleServices s) => Build(s,
        new FakeTFTeamMenuSource { RedPlayers = 5, BluePlayers = 1, CurrentTeam = 2 });

    // mp_allowspectators off: stock never adds the Spectate button (:127-130).
    [UiSample("spectators not allowed")]
    private static UiScreen NoSpectate(UiSampleServices s) => Build(s,
        new FakeTFTeamMenuSource { AllowsSpectators = false });
}

/// <summary>The two teams and the local player's state as the menu reads
/// them; at rest nobody is on a team and spectating is open (stock's
/// <c>mp_allowspectators 1</c>).</summary>
public sealed class FakeTFTeamMenuSource : ITFTeamMenuSource
{
    public int RedPlayers { get; init; }
    public int BluePlayers { get; init; }
    public int CurrentTeam { get; init; }
    public bool AllowsSpectators { get; init; } = true;
}

/// <summary>What the clicks did — the screen fact's recorder, read after
/// the click's frame.</summary>
public sealed class FakeTFTeamMenuActions : ITFTeamMenuActions
{
    public List<TFTeamChoice> Choices { get; } = [];
    public int Cancels { get; private set; }

    public void Choose(TFTeamChoice choice) => Choices.Add(choice);

    public void Cancel() => Cancels++;
}
