using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The engineer's destroy menu from fakes (the ui-screen loop's sample
// step): the states stock's CHudMenuEngyDestroy is ever in, built
// through the same container the game uses. Every #TF2_DestroyMenu_*
// token lives in game/mod_tf2/resource/tf2_english.txt.

public static class TFDestroyMenuSamples
{
    private static UiScreen Build(UiSampleServices s, FakeTFDestroyMenuSource? source = null,
                                  FakeTFDestroyMenuActions? actions = null)
    {
        s.Add<ITFDestroyMenuSource>(source ?? new FakeTFDestroyMenuSource());
        s.Add<ITFDestroyMenuActions>(actions ?? new FakeTFDestroyMenuActions());
        return s.Create<TFDestroyMenu>();
    }

    // Nothing stands: every slot draws base_inactive.res, the transparent
    // background and the #TF_NotBuilt reason (OnTick's :327-339 arm).
    [UiSample("nothing-built")]
    private static UiScreen NothingBuilt(UiSampleServices s) => Build(s);

    // The sentry is up: its slot draws base_active.res, the demolish
    // cross over the half-alpha sentry art, and the others stay in the
    // not-built arm (tf_hud_menu_engy_destroy.cpp:316-326).
    [UiSample("sentry-built")]
    private static UiScreen SentryBuilt(UiSampleServices s) => Build(s,
        new FakeTFDestroyMenuSource
        {
            BuiltSlots = [0],
        });
}

/// <summary>The destroy PDA's world as the menu reads it: which of the
/// player's own objects stand and are not placing. At rest the PDA is
/// out and nothing is built (the in-game adapter's default until the
/// building mirror lands, docs/plans/tf2/deferred.tsv).</summary>
public sealed class FakeTFDestroyMenuSource : ITFDestroyMenuSource
{
    /// <summary>Slots (0-based, stock's order) whose object exists and is
    /// not placing.</summary>
    public int[] BuiltSlots { get; init; } = [];

    public bool DestroyerEquipped { get; init; } = true;

    public string CancelLabel { get; init; } = "Hit 'q' to Cancel";

    public bool Built(int building, int mode)
        => Array.IndexOf(BuiltSlots, SlotOf(building, mode)) >= 0;

    /// <summary>The slot an OBJ_*/mode pair rides in (stock's
    /// g_kEngyBuildings order, tf_hud_menu_engy_destroy.cpp:35-84).</summary>
    private static int SlotOf(int building, int mode) => (building, mode) switch
    {
        (2, _) => 0,
        (0, _) => 1,
        (1, 0) => 2,
        _ => 3,
    };
}

/// <summary>What the clicks did — the screen fact's recorder, read after
/// the click's frame.</summary>
public sealed class FakeTFDestroyMenuActions : ITFDestroyMenuActions
{
    public List<int> Destroys { get; } = [];

    public void Destroy(int building, int mode) => Destroys.Add(building * 10 + mode);

    public void Cancel()
    { }
}
