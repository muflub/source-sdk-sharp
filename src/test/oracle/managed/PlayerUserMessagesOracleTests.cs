using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// docs/player-usermessages.tsv against the pristine upstream: every user
/// message stock's player classes send has a row, and every row is one of
/// them. A message added to (or left out of) the audit is a red here.
/// </summary>
public class PlayerUserMessagesOracleTests
{
    private static readonly string[] PlayerClasses =
    [
        "src/game/server/player.cpp",
        "src/game/server/hl2/hl2_player.cpp",
        "src/game/server/hl2mp/hl2mp_player.cpp",
    ];

    private static readonly string[] Rulings = ["ported", "deferred", "ruled-out", "open"];

    /// <summary>Every UserMessageBegin( filter, "Name" ) in the three files, comments removed.</summary>
    internal static HashSet<string> StockSends()
    {
        var sends = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in PlayerClasses)
        {
            string code = Regex.Replace(ValveSdk.Read(file), @"//[^\n]*", "");
            foreach (Match m in Regex.Matches(code, @"UserMessageBegin\(\s*\w+\s*,\s*""(?<name>\w+)""\s*\)"))
                sends.Add(m.Groups["name"].Value);
        }

        return sends;
    }

    /// <summary>The table's rows by message, header and comments skipped.</summary>
    internal static Dictionary<string, string[]> Rows()
    {
        string path = Path.Combine(ValveSdk.RepoRoot, "docs", "player-usermessages.tsv");
        Assert.True(File.Exists(path), path + " is missing");
        var rows = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#').Skip(1))
        {
            string[] cells = line.Split('\t');
            Assert.True(cells.Length == 6, "a row has " + cells.Length + " cells, not 6: " + line);
            Assert.True(rows.TryAdd(cells[0], cells), "two rows for " + cells[0]);
        }

        return rows;
    }

    [Fact]
    public void Every_message_a_stock_player_class_sends_has_a_row()
    {
        var sends = StockSends();
        Assert.True(sends.Count >= 11, "read only " + sends.Count + " sends: the needle stopped matching");

        var missing = sends.Except(Rows().Keys).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0, "no row in docs/player-usermessages.tsv for: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_row_is_a_message_a_stock_player_class_sends_and_is_ruled()
    {
        var sends = StockSends();
        foreach (var (message, cells) in Rows())
        {
            // A row for a message the player classes do not send names the
            // stock file that does, first in its sender column (SayText2:
            // util.cpp's UTIL_SayText2Filter, Host_Say's).
            if (!sends.Contains(message))
            {
                string file = cells[1].Split(' ')[0];
                string code = Regex.Replace(ValveSdk.Read("src/game/server/" + file), @"//[^\n]*", "");
                Assert.True(Regex.IsMatch(code, @"UserMessageBegin\(\s*\w+\s*,\s*""" + Regex.Escape(message) + @"""\s*\)"),
                            message + " has a row, no stock player class sends it, and " + file + " does not either");
            }
            Assert.True(Rulings.Contains(cells[4]), message + " is ruled '" + cells[4] + "'");
            Assert.True(cells[5].Length > 20, message + " has no reason");
        }
    }

    [Fact]
    public void The_pickup_sends_are_stocks()
    {
        string hl2 = Regex.Replace(ValveSdk.Read("src/game/server/hl2/hl2_player.cpp"), @"//[^\n]*", "");
        Assert.Matches(@"UserMessageBegin\(\s*user,\s*""ItemPickup""\s*\);\s*WRITE_STRING\(\s*""item_battery""\s*\);", hl2);
        Assert.Matches(@"if\s*\(\s*nCount\s*>\s*0\s*&&\s*nAdd\s*==\s*0\s*\)\s*\{[^}]*user\.MakeReliable\(\);\s*UserMessageBegin\(\s*user,\s*""AmmoDenied""\s*\);\s*WRITE_SHORT\(\s*nAmmoIndex\s*\);",
                       hl2);

        string kit = ValveSdk.Read("src/game/server/hl2/item_healthkit.cpp");
        Assert.Equal(2, Regex.Matches(kit, @"UserMessageBegin\(\s*user,\s*""ItemPickup""\s*\);\s*WRITE_STRING\(\s*GetClassname\(\)\s*\);").Count);

        // CAmmoDef counts from 1 ("Client assumes zero is an invalid ammo type"):
        // AmmoTypes.WireIndex's base.
        Assert.Matches(@"CAmmoDef::CAmmoDef\(void\)\s*\{[^}]*m_nAmmoIndex\s*=\s*1;", ValveSdk.Read("src/game/shared/ammodef.cpp"));
        Assert.Equal(1, AmmoTypes.WireIndex(AmmoTypes.All[0].Name));
    }
}
