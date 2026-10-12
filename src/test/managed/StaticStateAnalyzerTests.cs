using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using SourceSharp.Analyzers;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// StaticStateAnalyzer (SS0001/SS0002/SS0003), driven over source text: each
/// case of the census's definition of mutable static state, the cases it calls
/// constant staying silent, and the waiver list's two failure modes.
/// </summary>
public class StaticStateAnalyzerTests
{
    private const string Asm = "Probe";

    private const string ProbePath = AnalyzerHarness.ProbePath;

    private static CSharpCompilation Compile(string source) => AnalyzerHarness.Compile(source, Asm);

    /// <summary>The analyzer's diagnostics over <paramref name="source"/>, with <paramref name="waivers"/> as the list (null: no list at all).</summary>
    private static ImmutableArray<Diagnostic> Analyze(string source, string? waivers = "# no rows\n", string roles = AnalyzerRoles.StaticState) =>
        AnalyzerHarness.Run(new StaticStateAnalyzer(), Compile(source), roles,
            waivers is null ? [] : [("/repo/src/analyzers/" + StaticStateWaiverFile.FileName, waivers)]);

    private static List<string> Flagged(string source, string? waivers = "# no rows\n") =>
        Analyze(source, waivers)
            .Where(d => d.Id == StaticStateAnalyzer.MutableStaticId)
            .Select(d => d.Properties[StaticStateAnalyzer.KeyName]!)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static void Silent(string source) =>
        Assert.Empty(Analyze(source));

    [Fact]
    public void AProjectNotUnderTheStaticStateRoleIsNotGated() =>
        Assert.Empty(Analyze("static class C { static int Counter; }", waivers: null, roles: AnalyzerRoles.Mod));

    [Fact]
    public void AnAssignableStaticFieldIsFlagged() =>
        Assert.Equal(["Probe: N.C.Counter"], Flagged("namespace N; static class C { static int Counter; }"));

    [Fact]
    public void TheDiagnosticIsAtTheFieldsDeclaration()
    {
        var d = Assert.Single(Analyze("namespace N;\nstatic class C\n{\n    static int Counter;\n}\n"));
        var span = d.Location.GetLineSpan();
        Assert.Equal(ProbePath, span.Path);
        Assert.Equal(3, span.StartLinePosition.Line);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void TheMessageNamesTheRule() =>
        Assert.Contains("inject it as a service",
            Assert.Single(Analyze("static class C { static int Counter; }")).GetMessage(System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void AStaticAutoPropertyWithASetterIsFlaggedByItsBackingField() =>
        Assert.Equal(["Probe: N.C.<P>k__BackingField"], Flagged("namespace N; static class C { public static int P { get; set; } }"));

    [Fact]
    public void AGetOnlyStaticAutoPropertyOfAMutableTypeIsFlagged() =>
        Assert.Equal(["Probe: C.<P>k__BackingField"], Flagged("using System.Collections.Generic; static class C { public static List<int> P { get; } = new(); }"));

    [Fact]
    public void AGetOnlyStaticAutoPropertyOfAStringIsSilent() =>
        Silent("static class C { public static string P { get; } = \"x\"; }");

    [Fact]
    public void AStaticFieldLikeEventIsFlagged() =>
        Assert.Equal(["Probe: C.Changed"], Flagged("using System; static class C { public static event Action? Changed; static void Raise() => Changed?.Invoke(); }"));

    [Fact]
    public void AStaticEventWithAccessorsIsSilent() =>
        Silent("using System; static class C { public static event Action Changed { add { } remove { } } }");

    [Fact]
    public void AReadonlyListIsFlagged() =>
        Assert.Equal(["Probe: C.Items"], Flagged("using System.Collections.Generic; static class C { static readonly List<int> Items = new(); }"));

    [Fact]
    public void AReadonlyDictionaryIsFlagged() =>
        Assert.Equal(["Probe: C.Map"], Flagged("using System.Collections.Generic; static class C { static readonly Dictionary<int, int> Map = new(); }"));

    [Fact]
    public void AReadonlyArrayIsFlagged() =>
        Assert.Equal(["Probe: C.Table"], Flagged("static class C { static readonly int[] Table = { 1, 2 }; }"));

    [Fact]
    public void AReadonlyArrayOfReadOnlyListsIsFlagged() =>
        Assert.Equal(["Probe: C.Rows"], Flagged("using System.Collections.Generic; static class C { static readonly IReadOnlyList<int>[] Rows = []; }"));

    [Fact]
    public void AReadonlyLazyIsFlagged() =>
        Assert.Equal(["Probe: C.Cache"], Flagged("using System; static class C { static readonly Lazy<string> Cache = new(() => \"x\"); }"));

    [Fact]
    public void AReadonlyThreadLocalIsFlagged() =>
        Assert.Equal(["Probe: C.Slot"], Flagged("using System.Threading; static class C { static readonly ThreadLocal<int> Slot = new(); }"));

    [Fact]
    public void AReadonlyRandomIsFlagged() =>
        Assert.Equal(["Probe: C.Rng"], Flagged("using System; static class C { static readonly Random Rng = new(); }"));

    [Fact]
    public void AReadonlyInstanceOfAClassIsFlagged() =>
        Assert.Equal(["Probe: C.Instance"], Flagged("sealed class C { public static readonly C Instance = new(); }"));

    [Fact]
    public void AReadonlyFieldOfATypeParameterIsFlagged() =>
        Assert.Equal(["Probe: G`1.Value"], Flagged("sealed class G<T> where T : new() { static readonly T Value = new(); }"));

    [Fact]
    public void AStaticInAGenericNestedTypeIsNamedAsTheClrNamesIt() =>
        Assert.Equal(["Probe: N.Outer`1.Inner.Count"], Flagged("namespace N; class Outer<T> { class Inner { static int Count; } }"));

    [Fact]
    public void AStaticInAFileLocalTypeIsFlagged() =>
        Assert.Single(Flagged("file static class Hidden { static int Count; }"));

    [Fact]
    public void AConstIsSilent() =>
        Silent("static class C { const int Max = 4; const string Name = \"n\"; }");

    [Fact]
    public void AStaticReadonlyStringIsSilent() =>
        Silent("static class C { static readonly string Name = \"n\"; }");

    [Fact]
    public void AStaticReadonlyImmutableArrayIsSilent() =>
        Silent("using System.Collections.Immutable; static class C { static readonly ImmutableArray<int> Table = ImmutableArray.Create(1, 2); }");

    [Fact]
    public void AStaticReadonlyReadOnlyListIsSilent() =>
        Silent("using System.Collections.Generic; static class C { static readonly IReadOnlyList<int> Table = new[] { 1 }; }");

    [Fact]
    public void AStaticReadonlyFrozenDictionaryIsSilent() =>
        Silent("using System.Collections.Frozen; using System.Collections.Generic; static class C { static readonly FrozenDictionary<int, int> Map = new Dictionary<int, int>().ToFrozenDictionary(); }");

    [Fact]
    public void AStaticReadonlyValueTypeIsSilent() =>
        Silent("using System; static class C { static readonly TimeSpan Wait = TimeSpan.Zero; static readonly int Size = 4; }");

    [Fact]
    public void AStaticReadonlyDelegateIsSilent() =>
        Silent("using System; static class C { static readonly Func<int, int> Twice = x => x * 2; }");

    [Fact]
    public void AStaticReadonlyLockIsSilent() =>
        Silent("using System.Threading; static class C { static readonly Lock Gate = new(); static readonly object Sync = new(); }");

    [Fact]
    public void AStaticMethodIsSilent() =>
        Silent("static class C { static int Twice(int x) => x * 2; }");

    [Fact]
    public void AnInstanceFieldIsSilent() =>
        Silent("sealed class C { int _count; public int Next() => ++_count; }");

    [Fact]
    public void AnEnumIsSilent() =>
        Silent("enum E { A, B }");

    [Fact]
    public void ACompilerGeneratedTypeIsSkipped() =>
        Silent("[System.Runtime.CompilerServices.CompilerGenerated] static class C { static int Counter; }");

    [Fact]
    public void AStandaloneNetStatesLayoutIsAGeneratorConstant() =>
        Silent("[SourceSharp.Abi.StandaloneNetState(\"CNet\")] sealed class S { static readonly int[] NetLayout = []; }");

    [Fact]
    public void AnotherReadonlyArrayOnAStandaloneNetStateIsFlagged() =>
        Assert.Equal(["Probe: S.Other"], Flagged("[SourceSharp.Abi.StandaloneNetState(\"CNet\")] sealed class S { static readonly int[] Other = []; }"));

    [Fact]
    public void AWaivedStaticIsSilent() =>
        Assert.Empty(Analyze("namespace N; static class C { static readonly int[] Table = { 1 }; }", "Probe: N.C.Table\tTABLE\n"));

    [Fact]
    public void AWaiverForAnotherAssemblyDoesNotWaiveThisOne() =>
        Assert.Equal(["Probe: C.Counter"], Flagged("static class C { static int Counter; }", "Other: C.Counter\tTABLE\n"));

    [Fact]
    public void AWaiverForAnotherAssemblyIsNotStaleHere() =>
        Assert.Empty(Analyze("static class C { }", "Other: C.Counter\tTABLE\n"));

    [Fact]
    public void AWaiverWhoseStaticIsGoneIsReportedAtItsLine()
    {
        var d = Assert.Single(Analyze("static class C { }", "# header\nProbe: C.Counter\tTABLE\n"));
        Assert.Equal(StaticStateAnalyzer.StaleWaiverId, d.Id);
        Assert.Equal("Probe: C.Counter", d.Properties[StaticStateAnalyzer.KeyName]);
        Assert.Equal(1, d.Location.GetLineSpan().StartLinePosition.Line);
        Assert.EndsWith(StaticStateWaiverFile.FileName, d.Location.GetLineSpan().Path, StringComparison.Ordinal);
    }

    [Fact]
    public void AWaiverWhoseStaticIsNoLongerMutableIsStale() =>
        Assert.Equal(StaticStateAnalyzer.StaleWaiverId,
            Assert.Single(Analyze("static class C { static readonly string Name = \"n\"; }", "Probe: C.Name\tTABLE\n")).Id);

    [Fact]
    public void AMissingWaiverFileFailsTheBuild() =>
        Assert.Equal(StaticStateAnalyzer.BadWaiverFileId, Assert.Single(Analyze("static class C { }", waivers: null)).Id);

    [Fact]
    public void AWaiverWithAnotherReasonFailsTheBuild() =>
        Assert.Equal(StaticStateAnalyzer.BadWaiverFileId,
            Assert.Single(Analyze("static class C { static int Counter; }", "Probe: C.Counter\tit is fine\n")
                .Where(d => d.Id != StaticStateAnalyzer.MutableStaticId)).Id);

    [Fact]
    public void AWaiverListedTwiceFailsTheBuild() =>
        Assert.Contains(Analyze("static class C { static readonly int[] T = []; }", "Probe: C.T\tTABLE\nProbe: C.T\tTABLE\n"),
            d => d.Id == StaticStateAnalyzer.BadWaiverFileId);

    /// <summary>
    /// The analyzer's rule and the census's rule agree, case for case: one
    /// corpus with every shape, compiled, its symbols put through the analyzer
    /// and its IL through StaticStateCensusTests.MutableStaticsOf. The
    /// positive arm is non-empty, so agreement is not two empty sets.
    /// </summary>
    [Fact]
    public void TheAnalyzerAndTheCensusFlagTheSameFields()
    {
        const string corpus = """
            using System;
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Threading;
            namespace N;
            public static class Mixed
            {
                const int K = 1;
                static int Counter;
                static readonly int[] Table = { 1 };
                static readonly IReadOnlyList<int>[] Rows = [];
                static readonly List<int> Items = new();
                static readonly Lazy<string> Cache = new(() => "x");
                static readonly ThreadLocal<int> Slot = new();
                static readonly string Name = "n";
                static readonly ImmutableArray<int> Frozen = ImmutableArray<int>.Empty;
                static readonly IReadOnlyDictionary<int, int> Map = new Dictionary<int, int>();
                static readonly Func<int, int> Twice = x => x * 2;
                static readonly Lock Gate = new();
                public static int Prop { get; set; }
                public static List<int> GetOnly { get; } = new();
                public static string GetOnlyString { get; } = "s";
                public static int FieldKeyword { get => field; set => field = value; }
                public static event Action? Changed;
                public static event Action Explicit { add { } remove { } }
                static void Raise() => Changed?.Invoke();
                static IEnumerable<int> Iterate() { yield return Counter; }
                static Func<int> Capture(int x) => () => x + Counter;
            }
            public class Outer<T> where T : new() { static readonly T Value = new(); static int Shared; class Inner { static int Count; } }
            public enum E { A }
            public struct S { static S Default; static readonly S Empty; }
            [System.Runtime.CompilerServices.CompilerGenerated] static class Generated { static int Hidden; }
            [SourceSharp.Abi.StandaloneNetState("CNet")] public sealed class Net { static readonly int[] NetLayout = []; static readonly int[] Other = []; }
            file static class Local { static int Count; }
            """;

        var compilation = Compile(corpus);
        var analyzer = Flagged(corpus);

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        image.Position = 0;

        var context = new AssemblyLoadContext("static-state-parity", isCollectible: true);

        try
        {
            Assembly loaded = context.LoadFromStream(image);
            var census = StaticStateCensusTests.MutableStaticsOf(Asm, loaded.GetTypes()).ToList();

            Assert.True(census.Count >= 15, "the corpus's positive arm reaches the census: " + census.Count);
            Assert.Equal(census, analyzer);
        }
        finally
        {
            context.Unload();
        }
    }
}
