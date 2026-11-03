using System.Reflection;
using System.Text.RegularExpressions;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Boundary;

/// <summary>
/// No stock nav-mesh logic (plan_hl2mp_ai §6, ruling 2026-11-01): the AI's
/// assemblies name no NavMesh or NavArea type, and no file under src/ai/
/// carries a Valve-header row citing a stock nav or NextBot path source.
/// tools/ai-mutations adds a NavMesh-named type to the core and sees red.
/// </summary>
public class NoStockNavTests
{
    private static readonly Regex StockNavName = new("NavMesh|NavArea", RegexOptions.CultureInvariant);

    private static readonly Regex StockNavSource = new(@"\bnav_\w+\.(cpp|h)\b|NextBotPath", RegexOptions.CultureInvariant);

    private static string[] StockNavTypes(Assembly a)
        => a.GetTypes().Select(t => t.FullName ?? t.Name).Where(n => StockNavName.IsMatch(n)).ToArray();

    // The control's planted name: the scan finds it here, so its empty answers below are the scan's.
    private sealed class NavMeshNamedControl;

    [Fact]
    public void TheNameScanFindsAPlantedType()
        => Assert.Contains(StockNavTypes(typeof(NoStockNavTests).Assembly), n => n.EndsWith(nameof(NavMeshNamedControl), StringComparison.Ordinal));

    [Fact]
    public void TheCoreNamesNoStockNavType()
        => Assert.Empty(StockNavTypes(typeof(NavGraph).Assembly));

    [Fact]
    public void TheHl2mpBindingNamesNoStockNavType()
        => Assert.Empty(StockNavTypes(typeof(SourceSharp.Ai.Hl2mp.Hl2mpAiService).Assembly));

    [Fact]
    public void TheSourceRuleSeesAStockNavCitation()
        // The control: the rule matches what a ported file's row would say.
        => Assert.Matches(StockNavSource, "ports CNavMesh::Load (nav_file.cpp)");

    [Fact]
    public void NoAiFileCitesAStockNavSource()
    {
        string[] rows = File.ReadAllLines(RepoLayout.Repo("docs", "valve-header-new.tsv"));
        Assert.True(rows.Length > 10, "the header ledger was read");
        Assert.DoesNotContain(rows, r => r.StartsWith("src/ai/", StringComparison.Ordinal) && StockNavSource.IsMatch(r));
    }
}
