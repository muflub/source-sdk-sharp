using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The kill feed from fakes (the ui-screen loop's sample step). Names are
// ui.Data (game copy, case kept); the icons are stock's d_images atlas
// names, asked of the seed or the fact's recording fake.

public static class TFKillFeedSamples
{
    public static UiScreen Build(UiSampleServices s, FakeKillFeed? feed = null, FakeTextureLibrary? textures = null)
    {
        s.Add<ITFKillFeed>(feed ?? new FakeKillFeed());
        s.HudArt = textures ?? s.HudArt;
        return s.Create<TFKillFeed>();
    }

    // The plain arms: a rocket kill across teams, then the reply.
    [UiSample("scout rockets a soldier")]
    private static UiScreen Plain(UiSampleServices s) => Build(s,
        feed: new FakeKillFeed
        {
            Lines =
            [
                new("Scout", Red, "Soldier", Blue, "d_tf_projectile_rocket", "", false, false, false),
                new("Soldier", Blue, "Scout", Red, "d_scattergun", "", false, false, false),
            ],
        });

    // The crit glyph rides behind the per-weapon headshot art.
    [UiSample("crit headshot")]
    private static UiScreen Crit(UiSampleServices s) => Build(s,
        feed: new FakeKillFeed
        {
            Lines = [new("Sniper", Red, "Demoman", Blue, "d_headshot", "", true, false, false)],
        });

    [UiSample("backstab")]
    private static UiScreen Backstab(UiSampleServices s) => Build(s,
        feed: new FakeKillFeed
        {
            Lines = [new("Spy", Blue, "Heavy", Red, "d_backstab", "", false, false, false)],
        });

    // The local player's line: the cream bar, the inverted art and the dark
    // names (hudlayout.res LocalBackgroundColor / LocalPlayerColor).
    [UiSample("the local player kills a spy")]
    private static UiScreen Local(UiSampleServices s) => Build(s,
        feed: new FakeKillFeed
        {
            Lines = [new("Engineer", Red, "Spy", Blue, "d_shotgun_primary", "dneg_shotgun_primary", false, false, true)],
        });

    // A suicide: no killer, and no weapon named — the skull fallback
    // (hud_basedeathnotice.cpp:802-807) — and the info phrase after the
    // victim (:303-314).
    [UiSample("suicide falls to the skull")]
    private static UiScreen Suicide(UiSampleServices s) => Build(s,
        feed: new FakeKillFeed
        {
            Lines = [new("", 0, "Pyro", Red, "", "", false, true, false, "#DeathMsg_Suicide")],
        });

    // Stock's cap: four lines, the newest last (RetireExpiredDeathNotices
    // takes the overflow before this draws).
    [UiSample("four capped")]
    private static UiScreen Capped(UiSampleServices s) => Build(s,
        feed: new FakeKillFeed
        {
            Lines =
            [
                new("Scout", Red, "Soldier", Blue, "d_tf_projectile_rocket", "", false, false, false),
                new("Soldier", Blue, "Medic", Red, "d_shotgun_primary", "dneg_shotgun_primary", false, false, true),
                new("Heavy", Blue, "Pyro", Red, "d_minigun", "", true, false, false),
                new("Demoman", Red, "Sniper", Blue, "d_grenadelauncher", "", false, false, false),
            ],
        });

    // The raw TF team numbers (tf_shareddefs.h): TF_TEAM_RED 2, TF_TEAM_BLUE 3.
    private const int Red = 2;
    private const int Blue = 3;
}

/// <summary>The death notices as the screen reads them; the samples fix the
/// lines, so the queue's own expiry lives in the adapter's facts.</summary>
public sealed class FakeKillFeed : ITFKillFeed
{
    public TFKillFeedLine[] Lines { get; init; } = [];

    public int LineCount => Lines.Length;

    public TFKillFeedLine Line(int i) => Lines[i];
}
