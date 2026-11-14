using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui.Screens;

// The robot bars from fakes (the ui-screen loop's sample step): each stage the
// bestiary reveals (L6, E7: nothing, the count at 1, the life at 15, the
// resistances at 30, the style at 60), a champion with statuses and IMMUNE,
// and bars crowding the screen's edges. Names and styles are ui.Data; every
// #Descent_* token lives in game/mod_tf2_descent/resource/tf2_descent_english.txt.

public static class RobotBarsSamples
{
    public static UiScreen Build(UiSampleServices s, FakeRobotBars bars)
    {
        s.Add<IDescentRobotBars>(bars);
        return s.Create<RobotBars>();
    }

    private static FakeRobotBars.Row Soldier(float x, float y, int kills) => new(
        new DescentRobotBar(x, y, "Soldier Robot", 0.72f, kills >= 1 ? kills : -1,
                            kills >= 15 ? 144 : 0, kills >= 15 ? 200 : 0, false, kills >= 60 ? "Drilled Reckless" : "", false, string.Empty),
        kills >= 30 ? [("#Descent_Type_Fire", 0, false), ("#Descent_Type_Blast", 25, false), ("#Descent_Type_Bullet", 0, false), ("#Descent_Type_Shock", -20, false)] : [],
        [], []);

    /// <summary>The bestiary's stages side by side: no kills, a first kill, fifteen, thirty, sixty.</summary>
    [UiSample("bestiary stages")]
    private static UiScreen Stages(UiSampleServices s) => Build(s, new FakeRobotBars
    {
        Rows =
        [
            Soldier(0.14f, 0.45f, 0),
            Soldier(0.32f, 0.45f, 1),
            Soldier(0.50f, 0.45f, 15),
            Soldier(0.68f, 0.45f, 30),
            Soldier(0.86f, 0.45f, 60),
        ],
    });

    /// <summary>A fireproof champion, stunned and burning, that a flare just hit: IMMUNE over it.</summary>
    [UiSample("champion, statuses, immune")]
    private static UiScreen Champion(UiSampleServices s) => Build(s, new FakeRobotBars
    {
        Rows =
        [
            new(new DescentRobotBar(0.5f, 0.42f, "Giant Soldier Robot", 0.35f, 42, 980, 2800, true, "", true, "#Descent_Type_Fire"),
                [("#Descent_Type_Fire", 100, true), ("#Descent_Type_Blast", 40, false), ("#Descent_Type_Bullet", 10, false), ("#Descent_Type_Shock", -50, false)],
                ["Fireproof", "Extra Fast"],
                ["#Descent_Status_stun", "#Descent_Status_burn"]),
        ],
    });

    /// <summary>One robot under the crosshair, nothing yet known of its kind: the name and the bar.</summary>
    [UiSample("unknown robot")]
    private static UiScreen Unknown(UiSampleServices s) => Build(s, new FakeRobotBars { Rows = [Soldier(0.5f, 0.44f, 0)] });

    /// <summary>Bars at the screen's corners, held inside it.</summary>
    [UiSample("at the edges")]
    private static UiScreen Edges(UiSampleServices s) => Build(s, new FakeRobotBars
    {
        Rows =
        [
            Soldier(0.0f, 0.05f, 30),
            Soldier(1.0f, 0.05f, 60),
            Soldier(0.02f, 1.0f, 15),
            Soldier(0.98f, 1.0f, 1),
        ],
    });
}

/// <summary>Robot bars a sample sets: each bar with its resistance rows, affixes and statuses.</summary>
public sealed class FakeRobotBars : IDescentRobotBars
{
    public sealed record Row(DescentRobotBar Bar, (string Token, int Percent, bool Immune)[] Resists, string[] Affixes, string[] Statuses);

    public Row[] Rows { get; init; } = [];

    public int BarCount => Rows.Length;
    public DescentRobotBar Bar(int i) => Rows[i].Bar;
    public int ResistCount(int bar) => Rows[bar].Resists.Length;
    public string ResistToken(int bar, int r) => Rows[bar].Resists[r].Token;
    public int ResistPercent(int bar, int r) => Rows[bar].Resists[r].Percent;
    public bool ResistImmune(int bar, int r) => Rows[bar].Resists[r].Immune;
    public int AffixCount(int bar) => Rows[bar].Affixes.Length;
    public string AffixName(int bar, int a) => Rows[bar].Affixes[a];
    public int StatusCount(int bar) => Rows[bar].Statuses.Length;
    public string StatusToken(int bar, int s) => Rows[bar].Statuses[s];
}
