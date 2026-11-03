using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The freeze panel from fakes (the ui-screen loop's sample step): every arm
// the death's data can put on screen, built through the same container the
// game uses. Class names are ui.Data (game copy, case kept); every
// #TF2_Freeze_* token lives in game/mod_tf2/resource/tf2_english.txt.

public static class TFFreezePanelSamples
{
    public static UiScreen Build(UiSampleServices s, FakeDeathScreen? death = null,
                                 FakeTextureLibrary? textures = null, SampleKeys? keys = null)
    {
        s.Add<ITFDeathScreen>(death ?? new FakeDeathScreen());
        s.Add<IUiLocalisation>(new FreezeLocalise());
        // The art: a fact's recording fake answers its names; without one the
        // preview run's --content seed stands, and a seedless build draws the
        // fallbacks (the container answers a stateless none).
        s.HudArt = textures ?? s.HudArt;
        s.Add<IUiKeyBindingStore>(keys ?? new SampleKeys().With("F5", "screenshot"));
        return s.Create<TFFreezePanel>();
    }

    // The stock look: Bob the RED soldier, 40 of 125 (187 buffed) left,
    // alive, no flags — freezepanel_basic.res's default painting.
    [UiSample("killed by a living soldier")]
    private static UiScreen Soldier(UiSampleServices s) => Build(s,
        new FakeDeathScreen { HasKiller = true, KillerName = "Bob", KillerTeam = Red,
                              KillerClass = "Soldier", KillerHealth = 40, KillerMaxHealth = 125,
                              KillerMaxBuffedHealth = 187, KillerAlive = true });

    // The team arm (tf_hud_freezepanel.cpp:580-584): the BLU panel bar and
    // the blue class image.
    [UiSample("killed by a blu soldier")]
    private static UiScreen BluSoldier(UiSampleServices s) => Build(s,
        new FakeDeathScreen { HasKiller = true, KillerName = "Sid", KillerTeam = Blue,
                              KillerClass = "Soldier", KillerHealth = 125, KillerMaxHealth = 125,
                              KillerMaxBuffedHealth = 187, KillerAlive = true });

    // The nemesis arm (:234-246, :380-402): a domination names the killer
    // our nemesis — "You were killed again by" and "New Nemesis!".
    [UiSample("your new nemesis")]
    private static UiScreen Nemesis(UiSampleServices s) => Build(s,
        new FakeDeathScreen { HasKiller = true, KillerName = "Bob", KillerTeam = Red,
                              KillerClass = "Soldier", KillerHealth = 60, KillerMaxHealth = 125,
                              KillerMaxBuffedHealth = 187, KillerAlive = true, DeathFlags = Domination });

    // The revenge arm: the killer settled the score — "Revenge!".
    [UiSample("revenge is theirs")]
    private static UiScreen GotRevenge(UiSampleServices s) => Build(s,
        new FakeDeathScreen { HasKiller = true, KillerName = "Bob", KillerTeam = Red,
                              KillerClass = "Soldier", KillerHealth = 12, KillerMaxHealth = 125,
                              KillerMaxBuffedHealth = 187, KillerAlive = true, DeathFlags = Revenge });

    // The _Dead arms (:383-401, :363-366): a dead killer takes "the late",
    // and their cross paints the dead material whole.
    [UiSample("killed by a dead soldier")]
    private static UiScreen DeadKiller(UiSampleServices s) => Build(s,
        new FakeDeathScreen { HasKiller = true, KillerName = "Bob", KillerTeam = Red,
                              KillerClass = "Soldier", KillerHealth = 0, KillerMaxHealth = 125,
                              KillerMaxBuffedHealth = 187, KillerAlive = false });

    // A suicide or a world death: stock's freeze cam never starts (no
    // observer target, tf_player.cpp:12487-12490, :13956-13968), so the
    // panel draws nothing.
    [UiSample("no killer")]
    private static UiScreen NoKiller(UiSampleServices s) => Build(s, new FakeDeathScreen());

    // ShowSnapshotPanel's no-binding arm (:997-1003): nothing bound to
    // screenshot, no hint panel.
    [UiSample("no screenshot key")]
    private static UiScreen NoSnapshotKey(UiSampleServices s) => Build(s,
        new FakeDeathScreen { HasKiller = true, KillerName = "Bob", KillerTeam = Red,
                              KillerClass = "Soldier", KillerHealth = 40, KillerMaxHealth = 125,
                              KillerMaxBuffedHealth = 187, KillerAlive = true },
        keys: new SampleKeys());

    // The raw TF team numbers (tf_shareddefs.h) and the death flags the
    // panel reads (TF_DEATH_DOMINATION/REVENGE, tf_shareddefs.h:931/933).
    private const int Blue = 3;
    private const int Red = 2;
    private const int Domination = 0x0001;
    private const int Revenge = 0x0004;
}

/// <summary>The death the sample arms, as the freeze panel's data source
/// reads it: the killer's row and the death's flags.</summary>
public sealed class FakeDeathScreen : ITFDeathScreen
{
    public bool Showing { get; init; } = true;
    public int DeathFlags { get; init; }
    public bool HasKiller { get; init; }
    public string KillerName { get; init; } = "";
    public int KillerTeam { get; init; }
    public string KillerClass { get; init; } = "";
    public int KillerHealth { get; init; }
    public int KillerMaxHealth { get; init; }
    public int KillerMaxBuffedHealth { get; init; }
    public bool KillerAlive { get; init; }
}

/// <summary>The mod's tokens the hint composes from; the #TF2_Freeze_*
/// labels resolve through the sample context's language file
/// (tf2_english.txt).</summary>
public sealed class FreezeLocalise : IUiLocalisation
{
    public string? Find(string token) => token switch
    {
        "#TF2_Freeze_Snapshot" => "[%s1] Save this moment!",
        _ => null,
    };
}

/// <summary>Bindings in a dictionary: the screenshot key the hint names,
/// and the unbound arm.</summary>
public sealed class SampleKeys : IUiKeyBindingStore
{
    private readonly Dictionary<string, string> _binds = new();

    public SampleKeys With(string key, string command)
    {
        _binds[command] = key;
        return this;
    }

    public string? KeyFor(string command) => _binds.TryGetValue(command, out var key) ? key : null;

    public void Bind(string key, string command) => _binds[command] = key;

    public void Unbind(string key)
    {
        foreach (var (command, bound) in _binds)
            if (bound == key)
                _binds.Remove(command);
    }

    public string? KeyName(int buttonCode) => null;
}
