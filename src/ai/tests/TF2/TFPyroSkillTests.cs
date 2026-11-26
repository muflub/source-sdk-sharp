using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>The Pyro's own skill curve in TF2's curves file: the reflect chance, stock's ladder by difficulty.</summary>
public sealed class TFPyroSkillTests
{
    /// <summary>The reflect chance at the presets' base levels is stock's ladder: Easy 0, Normal 0.5, Hard 0.9, Expert 1 (tf_bot.cpp:4081-4105).</summary>
    [Fact]
    public void TheReflectChanceIsStocksLadder()
    {
        SkillCurves curves = TFSkillCurvesTests.ShippedCurves();
        float At(string preset) => curves.For(SkillPresets.TryGet(preset, out SkillProfile? p) ? p! : throw new ArgumentException(preset)).ReflectChance;
        Assert.Equal((0f, 0.5f, 0.9f, 1f), (At("easy"), At("normal"), At("hard"), At("expert")));
    }
}
