using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

using SourceSharp.Analyzers;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// ModCoreAccessAnalyzer (SS0010-SS0013): a mod reaches the core only by
/// constructor injection and hook interfaces. One case of CoreAccessTests'
/// rule set per fact, and the shapes it allowed staying silent.
/// </summary>
public class ModCoreAccessAnalyzerTests
{
    /// <summary>Stand-ins for the core's statics, named as the rule names them.</summary>
    private const string Core = """
        namespace SourceSharp.Abi
        {
            public static class Engine { public static int Ticks => 0; public static void Msg(string s) { } }
            public static class EngineServices { public static object? Find(string name) => null; }
        }
        """;

    private static ImmutableArray<Diagnostic> Analyze(string source, string roles = AnalyzerRoles.Mod) =>
        AnalyzerHarness.Run(new ModCoreAccessAnalyzer(), AnalyzerHarness.Compile(source, "TF2.Descent.Server", Core), roles);

    /// <summary>The diagnostics raised in the probe itself (not the stand-ins), as "id message".</summary>
    private static List<string> Found(string source, string roles = AnalyzerRoles.Mod) =>
        Analyze(source, roles)
            .Where(d => d.Location.GetLineSpan().Path == AnalyzerHarness.ProbePath)
            .Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

    private static void Reports(string id, string needle, string source) =>
        Assert.Contains(Found(source), f => f.StartsWith(id + " ", StringComparison.Ordinal) && f.Contains(needle, StringComparison.Ordinal));

    private static void Silent(string source) =>
        Assert.Empty(Found(source));

    [Fact]
    public void AStaticFieldIsReported() =>
        Reports(ModCoreAccessAnalyzer.StaticFieldId, "'M.C.Counter'", "namespace M; static class C { static int Counter; }");

    [Fact]
    public void AStaticReadonlyStringIsReportedToo() =>
        Reports(ModCoreAccessAnalyzer.StaticFieldId, "'M.C.Name'", "namespace M; static class C { static readonly string Name = \"n\"; }");

    [Fact]
    public void AConstIsSilent() =>
        Silent("namespace M; static class C { const int Max = 4; }");

    [Fact]
    public void AStaticAutoPropertyIsTheCompilersFieldAndSilent() =>
        Silent("namespace M; static class C { public static int P { get; set; } }");

    [Fact]
    public void AnInstanceFieldIsSilent() =>
        Silent("namespace M; sealed class C { int _n; public int Next() => ++_n; }");

    [Fact]
    public void TheVarsTablesOfAScriptVarsClassAreSilent() =>
        Silent("namespace M; [SourceSharp.Abi.ScriptVars] sealed class V { static readonly int[] s_Descriptors = []; static readonly int[] s_Defaults = []; }");

    [Fact]
    public void AVarsTableNameOutsideAScriptVarsClassIsReported() =>
        Reports(ModCoreAccessAnalyzer.StaticFieldId, "s_Descriptors", "namespace M; sealed class V { static readonly int[] s_Descriptors = []; }");

    [Fact]
    public void AnEventWithAccessorsIsReported() =>
        Reports(ModCoreAccessAnalyzer.DeclaresEventId, "'M.C.Changed'", "using System; namespace M; sealed class C { public event Action? Changed { add { } remove { } } }");

    [Fact]
    public void AFieldLikeEventIsReportedOnceAsAnEvent()
    {
        var found = Found("using System; namespace M; sealed class C { public static event Action? Changed; void Raise() => Changed?.Invoke(); }");
        Assert.Equal([ModCoreAccessAnalyzer.DeclaresEventId], found.Select(f => f[..6]));
    }

    [Fact]
    public void GettingAnEnvironmentVariableIsReported() =>
        Reports(ModCoreAccessAnalyzer.ForbiddenMemberId, "System.Environment.GetEnvironmentVariable", "using System; namespace M; sealed class C { string? Run() => Environment.GetEnvironmentVariable(\"HOME\"); }");

    [Fact]
    public void GettingAllEnvironmentVariablesIsReported() =>
        Reports(ModCoreAccessAnalyzer.ForbiddenMemberId, "System.Environment.GetEnvironmentVariables", "using System; namespace M; sealed class C { object Run() => Environment.GetEnvironmentVariables(); }");

    [Fact]
    public void SettingAnEnvironmentVariableIsReported() =>
        Reports(ModCoreAccessAnalyzer.ForbiddenMemberId, "System.Environment.SetEnvironmentVariable", "using System; namespace M; sealed class C { void Run() => Environment.SetEnvironmentVariable (\"A\", \"b\"); }");

    [Fact]
    public void ExitIsReported() =>
        Reports(ModCoreAccessAnalyzer.ForbiddenMemberId, "System.Environment.Exit", "using System; namespace M; sealed class C { void Run() => Environment.Exit(1); }");

    [Fact]
    public void FailFastIsReported() =>
        Reports(ModCoreAccessAnalyzer.ForbiddenMemberId, "System.Environment.FailFast", "using System; namespace M; sealed class C { void Run() => Environment.FailFast(\"x\"); }");

    [Fact]
    public void AnotherEnvironmentMemberIsSilent() =>
        Silent("using System; namespace M; sealed class C { int Run() => Environment.ProcessorCount; }");

    [Fact]
    public void AnyMemberOfEngineIsReported() =>
        Reports(ModCoreAccessAnalyzer.ForbiddenMemberId, "SourceSharp.Abi.Engine.Ticks", "namespace M; sealed class C { int Run() => SourceSharp.Abi.Engine.Ticks; }");

    [Fact]
    public void AMethodGroupOfEngineIsReported() =>
        Reports(ModCoreAccessAnalyzer.ForbiddenMemberId, "SourceSharp.Abi.Engine.Msg", "using System; namespace M; sealed class C { Action<string> Run() => SourceSharp.Abi.Engine.Msg; }");

    [Fact]
    public void AnyMemberOfEngineServicesIsReported() =>
        Reports(ModCoreAccessAnalyzer.ForbiddenMemberId, "SourceSharp.Abi.EngineServices.Find", "namespace M; sealed class C { object? Run() => SourceSharp.Abi.EngineServices.Find(\"x\"); }");

    [Fact]
    public void NameofAnEngineMemberIsSilent() =>
        Silent("namespace M; sealed class C { string Run() => nameof(SourceSharp.Abi.Engine.Ticks); }");

    [Fact]
    public void AUseInsideALambdaIsChargedToItsClass() =>
        Reports(ModCoreAccessAnalyzer.ForbiddenMemberId, "'M.InLambda' uses System.Environment.GetEnvironmentVariable",
            "using System; namespace M; sealed class InLambda { public Func<string?> Run() => () => Environment.GetEnvironmentVariable(\"HOME\"); }");

    [Fact]
    public void AUseInsideALocalFunctionIsChargedToItsClass() =>
        Reports(ModCoreAccessAnalyzer.ForbiddenMemberId, "'M.Local' uses System.Environment.Exit",
            "using System; namespace M; sealed class Local { public void Run() { Stop(); static void Stop() => Environment.Exit(0); } }");

    [Fact]
    public void SubscribingToAnEventIsReported() =>
        Reports(ModCoreAccessAnalyzer.SubscribesId, "subscribes to System.AppDomain.ProcessExit",
            "using System; namespace M; sealed class S { public void Run(AppDomain d) => d.ProcessExit += (_, _) => { }; }");

    [Fact]
    public void UnsubscribingIsSilent() =>
        Silent("using System; namespace M; sealed class S { public void Run(AppDomain d, EventHandler h) => d.ProcessExit -= h; }");

    [Fact]
    public void TheEventAdapterMaySubscribe() =>
        Silent("using System; namespace TF2.Descent.Server; public sealed class HostLink { public void Run(AppDomain d) => d.ProcessExit += (_, _) => { }; }");

    [Fact]
    public void AClassNestedInTheEventAdapterMayNot() =>
        Reports(ModCoreAccessAnalyzer.SubscribesId, "TF2.Descent.Server.HostLink+Inner",
            "using System; namespace TF2.Descent.Server; public sealed class HostLink { sealed class Inner { public void Run(AppDomain d) => d.ProcessExit += (_, _) => { }; } }");

    [Fact]
    public void InjectedCoreAccessIsSilent() =>
        Silent("namespace M; public interface ILog { void Msg(string s); } public sealed class Clean(ILog log) { public void Run() => log.Msg(\"x\"); }");

    [Fact]
    public void AProjectNotUnderTheModRoleIsNotChecked() =>
        Assert.Empty(Found("namespace M; static class C { static int Counter; }", roles: AnalyzerRoles.StaticState));

    [Fact]
    public void TheDiagnosticIsAtTheUse()
    {
        var d = Assert.Single(Analyze("using System;\nnamespace M;\nsealed class C\n{\n    void Run() => Environment.Exit(1);\n}\n"),
            x => x.Location.GetLineSpan().Path == AnalyzerHarness.ProbePath);
        Assert.Equal(4, d.Location.GetLineSpan().StartLinePosition.Line);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }
}
