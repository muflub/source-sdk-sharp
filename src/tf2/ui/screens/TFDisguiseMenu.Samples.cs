using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The spy's disguise menu from fakes (the ui-screen loop's sample step):
// the states stock's CHudMenuSpyDisguise is ever in, built through the
// same container the game uses. Every #TF2_DisguiseMenu_* token lives
// in game/mod_tf2/resource/tf2_english.txt.

public static class TFDisguiseMenuSamples
{
    private static UiScreen Build(UiSampleServices s, FakeTFDisguiseMenuSource? source = null,
                                  FakeTFDisguiseMenuActions? actions = null)
    {
        s.Add<ITFDisguiseMenuSource>(source ?? new FakeTFDisguiseMenuSource());
        s.Add<ITFDisguiseMenuActions>(actions ?? new FakeTFDisguiseMenuActions());
        return s.Create<TFDisguiseMenu>();
    }

    // The default arm (the spy_pda_reset event, :613-624): a RED spy
    // sees the blue team's tiles.
    [UiSample("showing-blue")]
    private static UiScreen ShowingBlue(UiSampleServices s) => Build(s,
        new FakeTFDisguiseMenuSource { Team = 2 });

    // Toggled (or a BLU spy's own default): the red team's tiles.
    [UiSample("showing-red")]
    private static UiScreen ShowingRed(UiSampleServices s) => Build(s,
        new FakeTFDisguiseMenuSource { Team = 3 });
}

/// <summary>The disguise kit's world as the menu reads it: the local
/// player's team and whether the kit is out (the in-game adapter's team
/// read is the player manager's).</summary>
public sealed class FakeTFDisguiseMenuSource : ITFDisguiseMenuSource
{
    public bool DisguiseKitEquipped { get; init; } = true;

    /// <summary>The local player's team, the raw TF number (2 RED, 3 BLU).</summary>
    public int Team { get; init; }

    public string ToggleLabel { get; init; } = "Hit 'x' or 'r' to Toggle Team";

    public string CancelLabel { get; init; } = "Hit 'q' to Cancel";
}

/// <summary>What the clicks did — the screen fact's recorder, read after
/// the click's frame.</summary>
public sealed class FakeTFDisguiseMenuActions : ITFDisguiseMenuActions
{
    public List<(int Class, int Team)> Disguises { get; } = [];

    public void Disguise(int classNumber, int team) => Disguises.Add((classNumber, team));

    public void Cancel()
    { }
}
