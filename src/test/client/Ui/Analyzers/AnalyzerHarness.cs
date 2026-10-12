using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using Xunit;

namespace SourceSharp.Tests.Ui.Analyzers;

/// <summary>
/// Compiles planted UI code in memory against the real toolkit and contract
/// assemblies and runs one analyzer over it: the equivalent of
/// Microsoft.CodeAnalysis.Testing, with no new package.
/// </summary>
internal static class AnalyzerHarness
{
    private static readonly Lazy<MetadataReference[]> References = new(() =>
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).ToList();
        paths.Add(typeof(SourceSharp.Client.Ui.Toolkit.UiFrame).Assembly.Location);
        paths.Add(typeof(SourceSharp.Client.Ui.UiColor).Assembly.Location);
        return paths.Distinct().Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
    });

    public const string Usings = """
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using SourceSharp.Client.Ui;
        using SourceSharp.Client.Ui.Toolkit;
        """;

    public static CSharpCompilation Compile(string source, string assembly = "Planted")
    {
        var tree = CSharpSyntaxTree.ParseText(Usings + "\n" + source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(assembly, [tree], References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, "the planted code does not compile:\n" + string.Join("\n", errors));
        return compilation;
    }

    /// <summary>The analyzer's diagnostics on <paramref name="source"/>, as "id line: message".</summary>
    public static List<Diagnostic> Run(DiagnosticAnalyzer analyzer, string source)
    {
        var compilation = Compile(source);
        var with = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        return with.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult().OrderBy(d => d.Location.SourceSpan.Start).ToList();
    }

    /// <summary>The text each diagnostic points at.</summary>
    public static List<string> Spans(IEnumerable<Diagnostic> diagnostics)
        => diagnostics.Select(d => d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan)).ToList();
}
