extern alias client;

using System.Text.RegularExpressions;

using Xunit;

using KeyActions = client::SourceSharp.Client.Input.KeyActions;

namespace SourceSharp.OracleTests;

/// <summary>
/// The core's key action classes against what stock HL2MP ships them from:
/// scripts/kb_act.lst, the keyboard page's rows, and scripts/kb_def.lst, a fresh
/// player's keys.
/// </summary>
public class KeyActionsOracleTests
{
    private static readonly System.Reflection.Assembly Core = typeof(KeyActions).Assembly;

    /// <summary>Ours and not stock's: stock's vote panel answered F1 and F2, ours is a radial menu on a key of its own, and the admin menu is one more.</summary>
    private static readonly string[] Ours = ["callvote", "adminmenu"];

    private static List<(string, string)> Pairs(string file)
        => Regex.Matches(File.ReadAllText(file), "\"([^\"]*)\"\\s+\"([^\"]*)\"").Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();

    [Fact]
    public void The_rows_are_stock_s_in_stock_s_order_under_its_headings()
    {
        var rows = new List<(string, string, string?)>();
        string? section = null;
        foreach (var (command, description) in Pairs(ValveSdk.Find("game/mod_hl2mp/scripts/kb_act.lst")))
        {
            if (description.StartsWith('='))
                continue;
            if (command == "blank")
                section = description;
            else
                rows.Add((command, description, section));
        }
        Assert.True(rows.Count > 50, $"{rows.Count} rows in stock's list");
        Assert.Equal(rows, KeyActions.Of(Core.GetTypes(), Core).Actions().Where(r => !Ours.Contains(r.Command)).Select(r => (r.Command, r.Label, r.Section)));
    }

    /// <summary>Compared as what the file leaves bound: it binds z twice, and the later holds.</summary>
    [Fact]
    public void The_default_keys_are_what_stock_s_list_leaves_bound()
    {
        var stock = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, command) in Pairs(ValveSdk.Find("game/mod_hl2mp/scripts/kb_def.lst")))
            stock[key] = command;
        Assert.True(stock.Count > 50, $"{stock.Count} keys in stock's list");
        var ours = KeyActions.Of(Core.GetTypes(), Core).DefaultBinds.Where(b => !Ours.Contains(b.Command)).ToList();
        Assert.Equal(stock.OrderBy(b => b.Key, StringComparer.OrdinalIgnoreCase).Select(b => (b.Key.ToLowerInvariant(), b.Value)),
                     ours.OrderBy(b => b.Key, StringComparer.OrdinalIgnoreCase).Select(b => (b.Key.ToLowerInvariant(), b.Command)));
    }
}
