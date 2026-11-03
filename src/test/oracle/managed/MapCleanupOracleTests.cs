using SourceSharp.Server;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// The core's map cleanup filter (<see cref="HL2MPMapCleanupFilter"/>) held
/// against the PRISTINE <c>hl2mp_gamerules.cpp</c>: its preserve list is the
/// file's <c>s_PreserveEnts</c>, row for row and in order, so a row added,
/// dropped or misspelt on either side alone fails.
/// </summary>
public sealed class MapCleanupOracleTests
{
    private const string HL2MP = "src/game/shared/hl2mp/hl2mp_gamerules.cpp";

    [Fact]
    public void TheCoresPreserveListIsHL2MPsRowForRow()
        => Assert.Equal(StockStringArrays.Rows(ValveSdk.Read(HL2MP), "s_PreserveEnts"),
                        HL2MPMapCleanupFilter.StockPreserved);

    [Fact]
    public void TheReaderDropsTheEndMarkerAndAnArmWhoseMacroIsNotDefined()
    {
        const string source = "static const char *s_Rows[] =\n{\n\t\"a\",\n#ifdef RAID\n\t\"b\",\n#endif // RAID\n\t\"c\", // comment \"x\"\n\t\"\",\n};\n";

        Assert.Equal(["a", "c"], StockStringArrays.Rows(source, "s_Rows"));
        Assert.Equal(["a", "b", "c"], StockStringArrays.Rows(source, "s_Rows", "RAID"));   // the arm is read when defined
    }

    [Fact]
    public void ASourceWithoutTheTableThrowsRatherThanYieldingNothing()
    {
        Assert.NotEmpty(StockStringArrays.Rows(ValveSdk.Read(HL2MP), "s_PreserveEnts"));
        Assert.Throws<InvalidDataException>(() => StockStringArrays.Rows("// nothing here\n", "s_PreserveEnts"));
    }
}
