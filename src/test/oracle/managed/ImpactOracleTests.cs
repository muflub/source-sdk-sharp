extern alias client;
using System.Text.RegularExpressions;

using Xunit;

using BulletImpactEffect = client::SourceSharp.Client.TempEnts.BulletImpactEffect;
using ImpactChoicePolicy = client::SourceSharp.Client.TempEnts.ImpactChoicePolicy;

namespace SourceSharp.OracleTests;

/// <summary>
/// The bullet impact's constants, held to the pristine fx_impact.cpp,
/// fx_impact.h, fx_hl2_impacts.cpp and bspflags.h. The client build includes
/// none of them since lane M1-C made the "Impact" client effect C#
/// (BulletImpactEffect), so they are read here, from the upstream clone.
/// </summary>
public class ImpactOracleTests
{
    private static string Code(string relative)
    {
        string text = ValveSdk.Read(relative);
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\n]*", "");
    }

    private static int Hex(string text, string pattern)
    {
        Match m = Regex.Match(text, pattern);
        Assert.True(m.Success, $"'{pattern}' did not match");
        return Convert.ToInt32(m.Groups[1].Value, 16);
    }

    [Fact]
    public void TheNoFlecksFlagIsStocks()
    {
        // FLAGS_CUSTIOM_EFFECTS_NOFLECKS: stock's spelling of "custom".
        int stock = Hex(Code("src/game/client/fx_impact.h"), @"FLAGS_CUSTIOM_EFFECTS_NOFLECKS\s*=\s*0x([0-9A-Fa-f]+)");
        Assert.True(stock == ImpactChoicePolicy.FlagsNoFlecks,
                    $"stock's no-flecks flag is 0x{stock:x}, the managed one 0x{ImpactChoicePolicy.FlagsNoFlecks:x}");
    }

    [Fact]
    public void TheSurfacesThatTakeNoEffectAreStocks()
    {
        Assert.Contains("if ( tr.surface.flags & (SURF_SKY|SURF_NODRAW|SURF_HINT|SURF_SKIP) )",
                        Code("src/game/client/fx_impact.cpp"));

        string flags = Code("src/public/bspflags.h");
        int expected = 0;
        foreach (string name in new[] { "SURF_SKY", "SURF_NODRAW", "SURF_HINT", "SURF_SKIP" })
            expected |= Hex(flags, @"#define\s+" + name + @"\s+0x([0-9A-Fa-f]+)");

        Assert.True(expected == BulletImpactEffect.NoEffectSurfaces,
                    $"stock's no-effect surfaces are 0x{expected:x}, the managed ones 0x{BulletImpactEffect.NoEffectSurfaces:x}");
    }

    [Fact]
    public void TheTraceRunsAsFarPastTheImpactAsStocks()
    {
        Match m = Regex.Match(Code("src/game/client/fx_impact.cpp"),
                              @"VectorMA\(\s*vecStart,\s*flLength\s*\+\s*([0-9.]+)f,\s*shotDir,\s*traceExt\s*\);");
        Assert.True(m.Success, "fx_impact.cpp's Impact no longer extends the trace past the impact");
        float stock = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(stock == BulletImpactEffect.TraceOvershoot,
                    $"stock extends the trace {stock} past the impact, the managed impact {BulletImpactEffect.TraceOvershoot}");
    }

    [Fact]
    public void TheCallbacksScaleIsStocks()
    {
        Match m = Regex.Match(Code("src/game/client/hl2/fx_hl2_impacts.cpp"),
                              @"void ImpactCallback\(.*?PerformCustomEffects\(\s*vecOrigin,\s*tr,\s*vecShotDir,\s*iMaterial,\s*([0-9.]+)\s*\)",
                              RegexOptions.Singleline);
        Assert.True(m.Success, "fx_hl2_impacts.cpp's ImpactCallback no longer performs its custom effects");
        float stock = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(stock == BulletImpactEffect.Scale,
                    $"stock's callback scale is {stock}, the managed one {BulletImpactEffect.Scale}");
    }
}
