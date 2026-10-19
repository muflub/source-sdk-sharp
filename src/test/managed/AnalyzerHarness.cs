using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using SourceSharp.Analyzers;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Runs one of src/analyzers' analyzers over source text, as the build would
/// for a project under the given roles.
/// </summary>
internal static class AnalyzerHarness
{
    /// <summary>The probe's syntax-tree path: not a file in the tree, so not named like one.</summary>
    public const string ProbePath = "(probe source)";

    // The client's copies of the shared interop and its doubles: each side
    // compiles its own (plan_shim_areas_split phase 3), so with both a probe
    // naming a shared type would find two. The probe sees the server's, as the
    // unaliased names of this suite are.
    private static readonly string[] ClientCopies = ["SourceSharp.Client.Interop", "SourceSharp.TestSupport.Client"];

    private static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => !ClientCopies.Contains(Path.GetFileNameWithoutExtension(p)))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(SourceSharp.Abi.ScriptedVarSet).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(SourceSharp.Abi.EntityHandle).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Xunit.CollectionAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Xunit.Assert).Assembly.Location))
            .Distinct()
            .ToArray();

    private sealed class Text(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    private sealed class Options(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }

    private sealed class Provider(AnalyzerConfigOptions global) : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions None = new Options(ImmutableDictionary<string, string>.Empty);

        public override AnalyzerConfigOptions GlobalOptions => global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => None;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => None;
    }

    /// <summary>The probe compiled as <paramref name="assembly"/>; it must compile, or it tests nothing.</summary>
    public static CSharpCompilation Compile(string source, string assembly = "Probe", params string[] more)
    {
        var trees = new[] { source }.Concat(more)
            .Select((s, i) => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Preview), path: i == 0 ? ProbePath : ProbePath + i));

        var compilation = CSharpCompilation.Create(
            assembly,
            trees,
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, "the probe compiles:\n" + string.Join("\n", errors));
        return compilation;
    }

    /// <summary><paramref name="analyzer"/>'s diagnostics over <paramref name="compilation"/>, for a project under <paramref name="roles"/>.</summary>
    public static ImmutableArray<Diagnostic> Run(
        DiagnosticAnalyzer analyzer, Compilation compilation, string roles, params (string Path, string Text)[] files)
    {
        var global = new Options(ImmutableDictionary<string, string>.Empty.Add(AnalyzerRoles.Property, roles));
        var options = new AnalyzerOptions(
            files.Select(f => (AdditionalText)new Text(f.Path, f.Text)).ToImmutableArray(),
            new Provider(global));

        return compilation
            .WithAnalyzers([analyzer], options)
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }
}
