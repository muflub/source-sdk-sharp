using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// The surface-prop field TempEntEffectDataTests describes (eight unsigned bits
/// over a short, plus one on the way out) is read out of stock's own lines, not
/// remembered. Until the server gc cull (2026-10-01) server.so carried copies of
/// SendPropIntWithMinusOneFlag and SendProxy_ShortAddOne and the managed test
/// read those; nothing called them and they went, so the evidence is stock's.
/// </summary>
public class TempEntSurfacePropOracleTests
{
    [Fact]
    public void StocksMinusOneFlagIsAPlainUnsignedSendPropInt()
    {
        string proxy = ValveSdk.Read("src/game/server/sendproxy.cpp");

        Assert.Matches(new Regex(
            @"SendProp\s+SendPropIntWithMinusOneFlag\([^)]*\)\s*\{\s*return\s+SendPropInt\("
          + @"\s*pVarName\s*,\s*offset\s*,\s*sizeofVar\s*,\s*nBits\s*,\s*SPROP_UNSIGNED\s*,\s*proxyFn\s*\)\s*;"),
            proxy);
    }

    [Fact]
    public void StocksShortAddOneIsAPlusOne()
    {
        string proxy = ValveSdk.Read("src/game/server/sendproxy.cpp");

        Assert.Matches(new Regex(@"pOut->m_Int\s*=\s*\(\*pInt\)\s*\+\s*1\s*;"), proxy);
    }

    [Fact]
    public void StocksEffectDataSendsTheSurfacePropThroughBoth()
    {
        string data = ValveSdk.Read("src/game/shared/effect_dispatch_data.cpp");

        Assert.Matches(new Regex(
            @"SendPropIntWithMinusOneFlag\(\s*SENDINFO_NOCHECK\(\s*m_nSurfaceProp\s*\)\s*,\s*8\s*,\s*SendProxy_ShortAddOne\s*\)"),
            data);
    }
}
