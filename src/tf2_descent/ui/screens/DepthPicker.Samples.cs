using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui.Screens;

// The church's depth picker from fakes (the ui-screen loop's sample step).
// Leader and member names are ui.Data (game copy); tilesets and classes are
// #Tokens in game/mod_tf2_descent/resource/descent_english.txt.

public static class DepthPickerSamples
{
    public static UiScreen Build(UiSampleServices s, IDescentDescent descent,
                                 IDescentDescentActions actions, IDescentParty party)
    {
        s.Add(descent);
        s.Add(actions);
        s.Add(party);
        return s.Create<DepthPicker>();
    }

    /// <summary>Solo, the account reached depth 7 — four rows sit behind the Lock.</summary>
    [UiSample("solo reached 7")]
    private static UiScreen Solo7(UiSampleServices s)
        => Build(s, new FakeDepthList { Rows = Rows(max: 7, reached: 3, leader: 3, bossAt: 7) },
                 new FakeDepthActions(),
                 new FakePartyCrew
                 {
                     Members = [PartySamples.Member("Red_Mercenary", "#Descent_Class_Soldier", 12, local: true)],
                 });

    /// <summary>A party of four at depth 3: rows 1..3 open, the members ride along.</summary>
    [UiSample("party of 4 at depth 3")]
    private static UiScreen Party4(UiSampleServices s)
        => Build(s, new FakeDepthList { Rows = Rows(max: 3, reached: 3, leader: 3, bossAt: 3) },
                 new FakeDepthActions(),
                 new FakePartyCrew
                 {
                     Members =
                     [
                         PartySamples.Member("Red_Mercenary", "#Descent_Class_Soldier", 12, local: true),
                         PartySamples.Member("Saul_Gentries", "#Descent_Class_Medic", 11),
                         PartySamples.Member("Engineer_88", "#Descent_Class_Engineer", 9),
                         PartySamples.Member("Hidden_Dagger", "#Descent_Class_Spy", 10),
                     ],
                 });

    /// <summary>D14's mapping: catacombs to 4, mines to 8, robot works below.</summary>
    private static DescentDepthRow[] Rows(int max, int reached, int leader, int bossAt)
    {
        var rows = new DescentDepthRow[max];
        for (int d = 1; d <= max; d++)
        {
            string tile = d <= 4 ? "#Descent_Depth_Tile_Catacombs"
                       : d <= 8 ? "#Descent_Depth_Tile_Mines"
                                : "#Descent_Depth_Tile_RobotWorks";
            rows[d - 1] = new DescentDepthRow(d, tile, d <= reached, d <= leader, d > reached, d == bossAt);
        }
        return rows;
    }
}

/// <summary>The church's list (R6): the depths as this character left them.</summary>
public sealed class FakeDepthList : IDescentDescent
{
    public DescentDepthRow[] Rows { get; init; } = [];
    public string PartyLeaderToken => "Red_Mercenary";
    public int RowCount => Rows.Length;
    public DescentDepthRow Row(int i) => Rows[i];
}

/// <summary>Remembers the last descent the picker asked for.</summary>
public sealed class FakeDepthActions : IDescentDescentActions
{
    public int? WentTo { get; private set; }
    public void Descend(int depth) => WentTo = depth;
}
