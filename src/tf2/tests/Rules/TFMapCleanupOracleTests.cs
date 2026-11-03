using SourceSharp.OracleTests;

using TF2.Server.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// TF2's map cleanup filter (<see cref="TFMapCleanupFilter"/>) held against
/// the PRISTINE <c>tf_gamerules.cpp</c> and <c>teamplayroundbased_gamerules.cpp</c>:
/// each of its two stock lists is its file's <c>s_PreserveEnts</c>, row for
/// row and in order (TF's with its <c>#ifdef TF_RAID_MODE</c> row out, as no
/// build defines it).
/// </summary>
public sealed class TFMapCleanupOracleTests
{
    [Fact]
    public void TFsPreserveListIsTFGameRulesRowForRow()
        => Assert.Equal(StockStringArrays.Rows(ValveSdk.Read("src/game/shared/tf/tf_gamerules.cpp"), "s_PreserveEnts"),
                        new TFMapCleanupFilter().StockTFPreserved);

    [Fact]
    public void TheTeamplayPreserveListIsTheBaseRulesRowForRow()
        => Assert.Equal(StockStringArrays.Rows(ValveSdk.Read("src/game/shared/teamplayroundbased_gamerules.cpp"), "s_PreserveEnts"),
                        new TFMapCleanupFilter().StockTeamplayPreserved);

    [Fact]
    public void TheRaidRowIsInTheStockTableBehindItsMacro()
    {
        // The control for the arm the reader drops: the row exists in the file
        // and is read when its macro is defined.
        string source = ValveSdk.Read("src/game/shared/tf/tf_gamerules.cpp");

        Assert.Contains("tf_logic_raid", StockStringArrays.Rows(source, "s_PreserveEnts", "TF_RAID_MODE"));
        Assert.DoesNotContain("tf_logic_raid", new TFMapCleanupFilter().StockTFPreserved);
    }
}
