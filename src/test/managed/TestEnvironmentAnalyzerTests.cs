using Microsoft.CodeAnalysis;

using SourceSharp.Analyzers;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>TestEnvironmentAnalyzer (SS0020): no test sets a process environment variable.</summary>
public class TestEnvironmentAnalyzerTests
{
    // The probes write "SetEnvironmentVariable (" with a space: the call the
    // rule is about, never spelled as one in this file's own text.
    private static List<Diagnostic> Analyze(string source, string roles = AnalyzerRoles.Test) =>
        AnalyzerHarness.Run(new TestEnvironmentAnalyzer(), AnalyzerHarness.Compile(source, "SourceSharp.Probe.Tests"), roles).ToList();

    [Fact]
    public void SettingAVariableIsReported() =>
        Assert.Equal(TestEnvironmentAnalyzer.SetsEnvironmentId,
            Assert.Single(Analyze("using System; sealed class T { void F() => Environment.SetEnvironmentVariable (\"A\", \"b\"); }")).Id);

    [Fact]
    public void SettingAVariableForATargetIsReported() =>
        Assert.Single(Analyze("using System; sealed class T { void F() => Environment.SetEnvironmentVariable (\"A\", \"b\", EnvironmentVariableTarget.Process); }"));

    [Fact]
    public void ClearingAVariableIsReported() =>
        Assert.Single(Analyze("using System; sealed class T { void F() => Environment.SetEnvironmentVariable (\"A\", null); }"));

    [Fact]
    public void AMethodGroupIsReported() =>
        Assert.Single(Analyze("using System; sealed class T { Action<string, string?> F() => Environment.SetEnvironmentVariable; }"));

    [Fact]
    public void ASetInsideALambdaIsReported() =>
        Assert.Single(Analyze("using System; sealed class T { Action F() => () => Environment.SetEnvironmentVariable (\"A\", \"b\"); }"));

    [Fact]
    public void TheDiagnosticIsAtTheCall()
    {
        var d = Assert.Single(Analyze("using System;\nsealed class T\n{\n    void F() => Environment.SetEnvironmentVariable (\"A\", \"b\");\n}\n"));
        Assert.Equal(3, d.Location.GetLineSpan().StartLinePosition.Line);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void ReadingAVariableIsSilent() =>
        Assert.Empty(Analyze("using System; sealed class T { string? F() => Environment.GetEnvironmentVariable(\"A\"); }"));

    [Fact]
    public void AChildProcesssEnvironmentIsSilent() =>
        Assert.Empty(Analyze("using System.Diagnostics; sealed class T { void F(ProcessStartInfo p) => p.Environment[\"A\"] = \"b\"; }"));

    [Fact]
    public void AnotherTypesSetEnvironmentVariableIsSilent() =>
        Assert.Empty(Analyze("sealed class Env { public static void SetEnvironmentVariable (string a, string b) { } } sealed class T { void F() => Env.SetEnvironmentVariable (\"A\", \"b\"); }"));

    [Fact]
    public void AProjectNotUnderTheTestRoleIsNotChecked() =>
        Assert.Empty(Analyze("using System; sealed class T { void F() => Environment.SetEnvironmentVariable (\"A\", \"b\"); }", roles: AnalyzerRoles.Mod));
}
