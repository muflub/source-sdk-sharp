using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// <c>DT_TEEffectDispatch</c> against the stock table it was transcribed from,
/// <c>game/server/te_effect_dispatch.cpp</c>: one nested
/// <c>SendPropDataTable</c> over <c>DT_EffectData</c>, which the managed
/// declaration must say too.
///
/// <para>
/// <b>AN ORACLE, MOVED OUT OF SourceSharp.Tests.</b> Neither DLL compiles
/// te_effect_dispatch.cpp any more, so the farm no longer links it; this fact
/// reads the pristine upstream clone through <see cref="ValveSdk"/>, where a
/// missing file is a failure rather than a skip. The rest of
/// TempEntEffectDataTests (the <c>DT_EffectData</c> parity over
/// effect_dispatch_data.cpp, which the build still compiles) stays where it was.
/// </para>
/// </summary>
public class TempEntEffectDataOracleTests
{
    [Fact]
    public void TheDispatchTableMatchesStocksSingleNestedProp()
    {
        string text = ValveSdk.Read("src/game/server/te_effect_dispatch.cpp");

        var block = Regex.Match(text,
            @"IMPLEMENT_SERVERCLASS_ST(_NOBASE)?\s*\(\s*CTEEffectDispatch\s*,\s*"
          + @"DT_TEEffectDispatch\s*\)(.*?)END_SEND_TABLE", RegexOptions.Singleline);

        Assert.True(block.Success, "the send table block was found in te_effect_dispatch.cpp");
        Assert.True(!block.Groups[1].Success,
            "declared with IMPLEMENT_SERVERCLASS_ST and NOT _NOBASE, so it nests a base — which "
          + "is why the managed declaration names DT_BaseTempEntity");

        var props = Regex.Matches(block.Groups[2].Value, @"SendProp(\w+)\s*\(");

        Assert.True(props.Count == 1, "one prop and one only");
        Assert.True(props[0].Groups[1].Value == "DataTable",
            "and it is a SendPropDataTable — the table is nested, not flattened");

        Assert.True(Regex.IsMatch(block.Groups[2].Value,
            @"SENDINFO_DT\(\s*m_EffectData\s*\)"),
            "under the member name m_EffectData, which SENDINFO_DT stringifies as the prop name");
        Assert.True(Regex.IsMatch(block.Groups[2].Value,
            @"REFERENCE_SEND_TABLE\(\s*DT_EffectData\s*\)"),
            "pointing at DT_EffectData");

        TeField mine = Dispatch().Fields[0];

        Assert.True(mine.Name == "EffectData" && mine.SubTable == "DT_EffectData"
                 && mine.Kind == TePropKind.DataTable,
            "and the managed declaration says the same three things — under the member's own "
          + "name, which is what this build's client pairs on");
    }

    /// <summary>
    /// DT_TEEffectDispatch built over its base's size, looked up rather than
    /// typed — DT_BaseTempEntity is empty today and the zero is a fact about it,
    /// not about this table.
    /// </summary>
    private static TeClass Dispatch()
        => TeClassBuilder.Build(typeof(TeEffectDispatch),
                                TeClassBuilder.Build(typeof(TeBaseTempEntity)).BlobBytes);
}
