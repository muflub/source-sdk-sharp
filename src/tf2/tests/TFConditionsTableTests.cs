using SourceSharp.Tests;

using TF2.Shared;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// <c>docs/plans/tf2/conditions.tsv</c> (stage1 Phase 1.7): one row per
/// <see cref="TFCond"/>, in order, each classed milestone, later or never.
/// </summary>
public sealed class TFConditionsTableTests
{
    private static string[][] Rows() =>
        File.ReadAllLines(RepoLayout.Repo("docs", "plans", "tf2", "conditions.tsv"))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split('\t'))
            .ToArray();

    [Fact]
    public void Every_condition_has_one_row_in_order()
    {
        string[][] rows = Rows();

        Assert.Equal((int)TFCond.Last, rows.Length);
        for (int id = 0; id < rows.Length; id++)
        {
            Assert.Equal(id.ToString(System.Globalization.CultureInfo.InvariantCulture), rows[id][0]);
            Assert.Equal(((TFCond)id).ToString(), rows[id][1]);
        }
    }

    [Fact]
    public void Every_row_is_classed_and_names_its_arms()
    {
        foreach (string[] row in Rows())
        {
            Assert.Equal(8, row.Length);
            Assert.Contains(row[3], new[] { "milestone", "later", "never" });
            Assert.StartsWith("TF_COND_", row[2]);
        }
    }
}
