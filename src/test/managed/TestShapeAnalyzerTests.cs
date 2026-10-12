using Microsoft.CodeAnalysis;

using SourceSharp.Analyzers;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>TestShapeAnalyzer (SS0050, SS0051): no test is written in a shape xUnit never runs.</summary>
public class TestShapeAnalyzerTests
{
    private static List<Diagnostic> Analyze(string source, string roles = AnalyzerRoles.Test) =>
        AnalyzerHarness.Run(new TestShapeAnalyzer(), AnalyzerHarness.Compile("using System; using System.Collections.Generic; using Xunit;\n" + source, "SourceSharp.Probe.Tests"), roles).ToList();

    private static void Reports(string id, string member, string source)
    {
        var d = Assert.Single(Analyze(source));
        Assert.Equal(id, d.Id);
        Assert.Contains($"'{member}'", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRunnerShapeIsReported() =>
        Reports(TestShapeAnalyzer.RunnerShapeId, "OldTests.Run(Action<string, bool>)",
            "public static class OldTests { public static void Run(Action<string, bool> check) => check(\"x\", true); }");

    [Fact]
    public void TheRunnerShapeUnderAnotherNameIsReported() =>
        Reports(TestShapeAnalyzer.RunnerShapeId, "OldTests.Check(Action<string, bool>)",
            "static class OldTests { static void Check(Action<string, bool> check) { } }");

    [Fact]
    public void ACallbackBesideOtherParametersIsSilent() =>
        Assert.Empty(Analyze("static class Helpers { static void WithSink(Action<string, bool> sink, Action body) { } }"));

    [Fact]
    public void AnotherCallbackShapeIsSilent() =>
        Assert.Empty(Analyze("static class Helpers { static void Run(Action<string, int> check) { } }"));

    [Fact]
    public void APublicHelperOnATestClassIsReported() =>
        Reports(TestShapeAnalyzer.UnmarkedPublicId, "SomeTests.Helper()",
            "public class SomeTests { [Fact] public void A() { } public void Helper() { } }");

    [Fact]
    public void APublicStaticHelperOnATestClassIsReported() =>
        Reports(TestShapeAnalyzer.UnmarkedPublicId, "SomeTests.Strip(string)",
            "public class SomeTests { [Fact] public void A() { } public static string Strip(string s) => s; }");

    [Fact]
    public void AForgottenFactIsReported() =>
        Reports(TestShapeAnalyzer.UnmarkedPublicId, "SomeTests.B()",
            "public class SomeTests { [Fact] public void A() { } public void B() { Assert.True(false); } }");

    [Fact]
    public void FactsAndTheoriesAreSilent() =>
        Assert.Empty(Analyze("public class SomeTests { [Fact] public void A() { } [Theory, InlineData(1)] public void B(int x) { } }"));

    [Fact]
    public void ADerivedFactAttributeMarksATest() =>
        Assert.Empty(Analyze("public sealed class SlowFactAttribute : FactAttribute { } public class SomeTests { [SlowFact] public void A() { } }"));

    [Fact]
    public void APrivateOrInternalHelperIsSilent() =>
        Assert.Empty(Analyze("public class SomeTests { [Fact] public void A() { } private void P() { } internal static int I() => 1; }"));

    [Fact]
    public void AMemberDataSourceIsSilent() =>
        Assert.Empty(Analyze("public class SomeTests { public static IEnumerable<object[]> Cases() { yield return new object[] { 1 }; } [Theory, MemberData(nameof(Cases))] public void A(int x) { } }"));

    [Fact]
    public void DisposeIsSilent() =>
        Assert.Empty(Analyze("public class SomeTests : IDisposable { [Fact] public void A() { } public void Dispose() { } }"));

    [Fact]
    public void AnOverrideIsSilent() =>
        Assert.Empty(Analyze("public class SomeTests { [Fact] public void A() { } public override string ToString() => \"t\"; }"));

    [Fact]
    public void APublicMethodOnAClassWithNoTestsIsSilent() =>
        Assert.Empty(Analyze("public class Fixture { public void Helper() { } }"));

    [Fact]
    public void AProjectNotUnderTheTestRoleIsNotChecked() =>
        Assert.Empty(Analyze("public class SomeTests { [Fact] public void A() { } public void Helper() { } }", roles: AnalyzerRoles.Mod));
}
