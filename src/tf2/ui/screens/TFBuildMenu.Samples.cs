using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The engineer's build menu from fakes (the ui-screen loop's sample
// step): the states stock's CHudMenuEngyBuild is ever in, built through
// the same container the game uses. Every #TF2_BuildMenu_* token lives
// in game/mod_tf2/resource/tf2_english.txt.

public static class TFBuildMenuSamples
{
    private static UiScreen Build(UiSampleServices s, FakeTFBuildMenuSource? source = null,
                                  FakeTFBuildMenuActions? actions = null)
    {
        s.Add<ITFBuildMenuSource>(source ?? new FakeTFBuildMenuSource());
        s.Add<ITFBuildMenuActions>(actions ?? new FakeTFBuildMenuActions());
        return s.Create<TFBuildMenu>();
    }

    // The reference picture: metal enough for everything, nothing built —
    // every slot in base_active.res.
    [UiSample("all available")]
    private static UiScreen AllAvailable(UiSampleServices s) => Build(s,
        new FakeTFBuildMenuSource { Metal = 400 });

    // The sentry is up: its slot draws base_already_built.res, the
    // outlined background and the dimmed cost, and the teleporter exits
    // are past the metal (tf_hud_menu_engy_build.cpp:562-575).
    [UiSample("one built, one unaffordable")]
    private static UiScreen BuiltAndShort(UiSampleServices s) => Build(s,
        new FakeTFBuildMenuSource
        {
            Metal = 30,
            BuiltSlots = [0],
        });
}

/// <summary>The build PDA's world as the menu reads it: the metal
/// account, and per slot whether the building exists and CanBuild says
/// no. At rest the PDA is out, everything is buildable and nothing is
/// built (the in-game adapter's defaults until the client reads land,
/// docs/plans/tf2/deferred.tsv).</summary>
public sealed class FakeTFBuildMenuSource : ITFBuildMenuSource
{
    /// <summary>Slots (0-based, stock's order) whose object exists and is
    /// not placing.</summary>
    public int[] BuiltSlots { get; init; } = [];

    public bool BuilderEquipped { get; init; } = true;

    public int Metal { get; init; }

    public string CancelLabel { get; init; } = "Hit 'q' to Cancel";

    /// <summary>Prices the facts can move (by slot, stock's order); unset
    /// is CalculateObjectCost's fresh-engineer answer
    /// (tf_hud_menu_engy_build.cpp:558): the sentry 125, the dispenser
    /// 100, each teleporter end 50.</summary>
    public int[]? Prices { get; init; }

    public int Cost(int building, int mode)
    {
        if (Prices is { } prices)
            return prices[SlotOf(building, mode)];
        return building switch
        {
            2 => 125,
            0 => 100,
            _ => 50,
        };
    }

    /// <summary>The player's can-build-another answer (stock's
    /// <c>pLocalPlayer->CanBuild(...) == CB_CAN_BUILD</c>, the gate the
    /// built arm and the send arm share, tf_hud_menu_engy_build.cpp:562,
    /// :472): one of a kind, so an existing object of the kind says no.</summary>
    public bool CanBuild(int building, int mode) => !Built(building, mode);

    public bool Built(int building, int mode) => Array.IndexOf(BuiltSlots, SlotOf(building, mode)) >= 0;

    /// <summary>The slot an OBJ_*/mode pair rides in (stock's
    /// g_kEngyBuildings order, tf_hud_menu_engy_build.cpp:35-84).</summary>
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
public sealed class FakeTFBuildMenuActions : ITFBuildMenuActions
{
    public List<int> Builds { get; } = [];

    public int Cancels { get; private set; }

    public void Build(int building, int mode) => Builds.Add(building * 10 + mode);

    public void Cancel() => Cancels++;
}
