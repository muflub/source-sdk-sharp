using Microsoft.CodeAnalysis;

using SourceSharp.Analyzers;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>CrossingsThunkAnalyzer (SS0070, SS0071): each ABI thunk in a side entry counts its own crossing, first.</summary>
public class CrossingsThunkAnalyzerTests
{
    /// <summary>Stand-ins for the counters, the op list and SideLoad, named as the rule names them.</summary>
    private const string Core = """
        namespace SourceSharp.Abi
        {
            public enum Side { Server, Client }
            public enum CrossingOp { LoadServer, UnloadServer, Tick, CrossingsReport }
            public sealed class Crossings { public void Count(CrossingOp op) { } }
        }
        namespace SourceSharp.Shared.Interop
        {
            public static class SideLoad
            {
                public static int Load(SourceSharp.Abi.Side side, SourceSharp.Abi.CrossingOp op, nint a) => 0;
                public static int Unload(SourceSharp.Abi.CrossingOp op, nint session) => 0;
            }
        }
        """;

    private static List<Diagnostic> Analyze(string entryBody, string type = "ServerSideEntry", string roles = AnalyzerRoles.StaticState) =>
        AnalyzerHarness.Run(new CrossingsThunkAnalyzer(), AnalyzerHarness.Compile($$"""
            using System.Runtime.InteropServices;
            using SourceSharp.Abi;
            using SourceSharp.Shared.Interop;
            public static unsafe class {{type}}
            {
                private static readonly Crossings crossings = new();
                {{entryBody}}
            }
            """, "SourceSharp.Server.Interop", Core), roles)
        .Where(d => d.Location.GetLineSpan().Path == AnalyzerHarness.ProbePath)
        .ToList();

    [Fact]
    public void ASideLoadThunkHandingItsOwnOpIsSilent() =>
        Assert.Empty(Analyze("[UnmanagedCallersOnly] public static int LoadServer(nint a) { return SideLoad.Load(Side.Server, CrossingOp.LoadServer, a); }"));

    [Fact]
    public void ASideLoadThunkWithoutASideIsSilent() =>
        Assert.Empty(Analyze("[UnmanagedCallersOnly] public static int UnloadServer(nint s) => SideLoad.Unload(CrossingOp.UnloadServer, s);"));

    [Fact]
    public void AThunkCountingItselfFirstIsSilent() =>
        Assert.Empty(Analyze("[UnmanagedCallersOnly] public static void Tick() { crossings.Count(CrossingOp.Tick); int x = 1; _ = x; }"));

    [Fact]
    public void AThunkThatDoesNotCountIsReported() =>
        Assert.Equal(CrossingsThunkAnalyzer.NotCountedFirstId,
            Assert.Single(Analyze("[UnmanagedCallersOnly] public static void Tick() { int x = 1; _ = x; }")).Id);

    [Fact]
    public void AThunkThatCountsSecondIsReported() =>
        Assert.Equal(CrossingsThunkAnalyzer.NotCountedFirstId,
            Assert.Single(Analyze("[UnmanagedCallersOnly] public static void Tick() { int x = 1; _ = x; crossings.Count(CrossingOp.Tick); }")).Id);

    [Fact]
    public void AThunkCountingAnotherOpIsReportedTwice()
    {
        var found = Analyze("[UnmanagedCallersOnly] public static void Tick() { crossings.Count(CrossingOp.LoadServer); }");
        Assert.Equal([CrossingsThunkAnalyzer.NotCountedFirstId, CrossingsThunkAnalyzer.CountsAnotherId], found.Select(d => d.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AThunkNamingAnotherOpLaterIsReported() =>
        Assert.Equal(CrossingsThunkAnalyzer.CountsAnotherId,
            Assert.Single(Analyze("[UnmanagedCallersOnly] public static void Tick() { crossings.Count(CrossingOp.Tick); crossings.Count(CrossingOp.UnloadServer); }")).Id);

    [Fact]
    public void ASideLoadThunkHandingAnotherOpIsReported() =>
        Assert.Contains(Analyze("[UnmanagedCallersOnly] public static int LoadServer(nint a) => SideLoad.Load(Side.Server, CrossingOp.UnloadServer, a);"),
            d => d.Id == CrossingsThunkAnalyzer.NotCountedFirstId);

    [Fact]
    public void CrossingsReportIsExempt() =>
        Assert.Empty(Analyze("[UnmanagedCallersOnly] public static int CrossingsReport() => 0;"));

    [Fact]
    public void AMethodWithoutTheAttributeIsNotAThunk() =>
        Assert.Empty(Analyze("public static void Tick() { }"));

    [Fact]
    public void AThunkOutsideASideEntryIsNotChecked() =>
        Assert.Empty(Analyze("[UnmanagedCallersOnly] public static void Tick() { }", type: "ManagedVtableTraps"));

    [Fact]
    public void TheClientEntryIsChecked() =>
        Assert.Single(Analyze("[UnmanagedCallersOnly] public static void Tick() { }", type: "ClientSideEntry"));

    [Fact]
    public void AProjectNotUnderTheRoleIsNotChecked() =>
        Assert.Empty(Analyze("[UnmanagedCallersOnly] public static void Tick() { }", roles: AnalyzerRoles.Test));
}
