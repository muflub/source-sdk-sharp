using Microsoft.CodeAnalysis;

using SourceSharp.Analyzers;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>ProcessWideAnalyzer (SS0060-SS0062): the serial collection's attributes and its list agree, and it is defined once.</summary>
public class ProcessWideAnalyzerTests
{
    /// <summary>A test project's ProcessWide, as src/test/common/ProcessWide.cs declares it, with these rows.</summary>
    private static string Definition(string rows) => $$"""
        using System;
        using System.Collections.Generic;
        using Xunit;
        namespace SourceSharp.Tests;
        [CollectionDefinition(Name, DisableParallelization = true)]
        public sealed partial class ProcessWide
        {
            public const string Name = "ProcessWide";
            public static readonly IReadOnlyDictionary<Type, string> Members = Rows();
            private static partial Dictionary<Type, string> Rows();
        }
        public sealed partial class ProcessWide
        {
            private static partial Dictionary<Type, string> Rows() => new()
            {
                {{rows}}
            };
        }
        """;

    private static List<Diagnostic> Analyze(string rows, string tests, string roles = AnalyzerRoles.Test) =>
        AnalyzerHarness.Run(new ProcessWideAnalyzer(), AnalyzerHarness.Compile(tests, "SourceSharp.Probe.Tests", Definition(rows)), roles).ToList();

    private const string Member = """
        using Xunit;
        namespace SourceSharp.Tests;
        [Collection(ProcessWide.Name)]
        public class UnloadProbeTests { [Fact] public void F() { } }
        """;

    [Fact]
    public void AListedMemberIsSilent() =>
        Assert.Empty(Analyze("[typeof(UnloadProbeTests)] = \"unloads\",", Member));

    [Fact]
    public void AnEmptyCollectionWithNoRowsIsSilent() =>
        Assert.Empty(Analyze("", "namespace SourceSharp.Tests; public class Plain { }"));

    [Fact]
    public void AMemberWithNoRowIsReported()
    {
        var d = Assert.Single(Analyze("", Member));
        Assert.Equal(ProcessWideAnalyzer.UnlistedId, d.Id);
        Assert.Contains("SourceSharp.Tests.UnloadProbeTests", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal(AnalyzerHarness.ProbePath, d.Location.GetLineSpan().Path);
        Assert.Equal(2, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void AMemberNamedByTheLiteralIsAMemberToo() =>
        Assert.Equal(ProcessWideAnalyzer.UnlistedId,
            Assert.Single(Analyze("", "using Xunit; namespace SourceSharp.Tests; [Collection(\"ProcessWide\")] public class Literal { }")).Id);

    [Fact]
    public void AnotherCollectionsMemberIsNotThisOnes() =>
        Assert.Empty(Analyze("", "using Xunit; namespace SourceSharp.Tests; [Collection(\"Other\")] public class Elsewhere { }"));

    [Fact]
    public void ARowForAClassNotInTheCollectionIsReported()
    {
        var d = Assert.Single(Analyze("[typeof(Stray)] = \"left\",", "namespace SourceSharp.Tests; public class Stray { }"));
        Assert.Equal(ProcessWideAnalyzer.StrayRowId, d.Id);
        Assert.Contains("SourceSharp.Tests.Stray", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondCollectionDefinitionIsReported() =>
        Assert.Equal(ProcessWideAnalyzer.SecondDefinitionId,
            Assert.Single(Analyze("", "using Xunit; namespace SourceSharp.Tests; [CollectionDefinition(\"Mine\")] public class Mine { }")).Id);

    [Fact]
    public void ATypeofOutsideRowsIsNotARow() =>
        Assert.Empty(Analyze("", "namespace SourceSharp.Tests; public class Plain { public System.Type T = typeof(Plain); }"));

    [Fact]
    public void AProjectNotUnderTheTestRoleIsNotChecked() =>
        Assert.Empty(Analyze("", Member, roles: AnalyzerRoles.Mod));
}
