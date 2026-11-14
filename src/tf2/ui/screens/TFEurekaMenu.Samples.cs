using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The Eureka Effect's teleport menu from fakes (the ui-screen loop's sample
// step): the states stock's CHudEurekaEffectTeleportMenu is ever in, built
// through the same container the game uses. Every #TF2_EurekaMenu_* token
// lives in game/mod_tf2/resource/tf2_english.txt.

public static class TFEurekaMenuSamples
{
    private static UiScreen Build(UiSampleServices s, FakeTFEurekaMenuSource? source = null,
                                  FakeTFEurekaMenuActions? actions = null)
    {
        s.Add<ITFEurekaMenuSource>(source ?? new FakeTFEurekaMenuSource());
        s.Add<ITFEurekaMenuActions>(actions ?? new FakeTFEurekaMenuActions());
        return s.Create<TFEurekaMenu>();
    }

    // The reference picture: the want laid, the wrench held, both choices
    // live — the exit's teleporter standing (tf_hud_menu_eureka_teleport.cpp
    // :397-405).
    [UiSample("both choices")]
    private static UiScreen BothChoices(UiSampleServices s) => Build(s,
        new FakeTFEurekaMenuSource { ExitStanding = true });

    // The exit's row dimmed to the unavailable words: no teleporter of the
    // builder's stands, the choice still shows and still denies
    // (:200-204).
    [UiSample("exit unavailable")]
    private static UiScreen ExitMissing(UiSampleServices s) => Build(s);
}

/// <summary>The eureka menu's world as the screen reads it: the want and
/// the wrench, and whether the builder's exit stands. Both live by
/// default, the in-game adapter's answered state.</summary>
public sealed class FakeTFEurekaMenuSource : ITFEurekaMenuSource
{
    public bool WantsTeleport { get; init; } = true;

    public bool WrenchActive { get; init; } = true;

    public bool ExitStanding { get; init; }

    public string CancelLabel { get; init; } = "Hit 'q' to Cancel";
}

/// <summary>What the choices did — the screen fact's recorder, read after
/// the click's frame.</summary>
public sealed class FakeTFEurekaMenuActions : ITFEurekaMenuActions
{
    public List<int> Teleports { get; } = [];

    public int Cancels { get; private set; }

    public void Teleport(int target) => Teleports.Add(target);

    public void Cancel() => Cancels++;
}
