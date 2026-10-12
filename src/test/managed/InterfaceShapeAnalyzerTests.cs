using Microsoft.CodeAnalysis;

using SourceSharp.Analyzers;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>InterfaceShapeAnalyzer (SS0030): an interface assembly holds interfaces, enums and pure data, never a body.</summary>
public class InterfaceShapeAnalyzerTests
{
    private static List<Diagnostic> Analyze(string source, string roles = AnalyzerRoles.Interface) =>
        AnalyzerHarness.Run(new InterfaceShapeAnalyzer(), AnalyzerHarness.Compile(source, "SourceSharp.Probe.Interface"), roles).ToList();

    private static void Reports(string member, string source)
    {
        var d = Assert.Single(Analyze(source));
        Assert.Equal(InterfaceShapeAnalyzer.BodyId, d.Id);
        Assert.Contains($"'{member}'", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void AnInterfaceIsSilent() =>
        Assert.Empty(Analyze("public interface IHook { void Fire(int x); int Count { get; } event System.Action Changed; }"));

    [Fact]
    public void AnEnumIsSilent() =>
        Assert.Empty(Analyze("public enum Kind { A = 1, B = 2 }"));

    [Fact]
    public void AClassOfFieldsAndAutoPropertiesIsSilent() =>
        Assert.Empty(Analyze("public sealed class Facts { public int A; public const int Max = 4; public int B { get; init; } public string C { get; set; } = \"\"; }"));

    [Fact]
    public void APositionalRecordIsSilent() =>
        Assert.Empty(Analyze("public sealed record Facts(bool Armed, int NowMs); public readonly record struct Pair(int A, int B);"));

    [Fact]
    public void AMethodWithABlockBodyIsReported() =>
        Reports("Codec.Write(int)", "public static class Codec { public static int Write(int x) { return x; } }");

    [Fact]
    public void AnExpressionBodiedMethodIsReported() =>
        Reports("Codec.Twice(int)", "public static class Codec { public static int Twice(int x) => x * 2; }");

    [Fact]
    public void AComputedPropertyIsReported() =>
        Reports("Facts.None", "public sealed record Facts(int A) { public static Facts None => new(0); }");

    [Fact]
    public void AnAccessorWithABodyIsReported() =>
        Reports("Box.Value.get", "public sealed class Box { private int _v; public int Value { get { return _v; } } }");

    [Fact]
    public void AConstructorWithABodyIsReported() =>
        Reports("Box.Box(int)", "public sealed class Box { public int V; public Box(int v) { V = v; } }");

    [Fact]
    public void AnOperatorIsReported() =>
        Reports("Pair.operator +(Pair, Pair)", "public readonly struct Pair { public readonly int A; public static Pair operator +(Pair x, Pair y) => x; }");

    [Fact]
    public void ADefaultInterfaceMethodIsReported() =>
        Reports("IHook.Twice(int)", "public interface IHook { int Twice(int x) => x * 2; }");

    [Fact]
    public void TheDiagnosticIsAtTheMembersName()
    {
        var d = Assert.Single(Analyze("public static class Codec\n{\n    public static int Twice(int x) => x * 2;\n}\n"));
        Assert.Equal(2, d.Location.GetLineSpan().StartLinePosition.Line);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void AProjectNotUnderTheInterfaceRoleIsNotChecked() =>
        Assert.Empty(Analyze("public static class Codec { public static int Twice(int x) => x * 2; }", roles: AnalyzerRoles.StaticState));
}
