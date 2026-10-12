using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The shared source (src/shared/managed) compiled ALONE, the way the
/// SourceSharp.Shared assembly was before plan_side_isolation P7 folded it into
/// each side: against SourceSharp.Abi and SourceSharp.Shared.Interop only, with
/// the generators. What a fact needs to say about "the code both sides compile"
/// it says about this assembly, because inside SourceSharp.Server or
/// SourceSharp.Client that code is mixed with one side's own.
/// </summary>
internal static class SharedSourceBuild
{
    /// <summary>
    /// The assembly's name. Shared.Interop grants it its internals, as it
    /// granted SourceSharp.Shared, because the shared source reads them.
    /// </summary>
    public const string Name = "SourceSharp.SharedSource";

    /// <summary>
    /// Compiles the shared source into a fresh directory, beside copies of the
    /// two assemblies it references, and answers the built file's path. A
    /// compile error is a failure: an audit of an assembly that did not build
    /// would read nothing.
    /// </summary>
    public static string Build()
    {
        string source = RepoLayout.Repo("src", "shared", "managed");
        string[] files = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
                                  .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                                  .OrderBy(f => f, StringComparer.Ordinal)
                                  .ToArray();
        Assert.True(files.Length > 50, $"the shared source at {source} ({files.Length} files)");

        var options = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), options, f))
                         .Append(CSharpSyntaxTree.ParseText(ImplicitUsings, options, "<implicit usings>"))
                         .ToList();

        Assembly abi = typeof(SourceSharp.Abi.ILog).Assembly;
        Assembly interop = typeof(SourceSharp.Shared.Interop.SideLoad).Assembly;
        var references = Platform().Append(MetadataReference.CreateFromFile(abi.Location))
                                   .Append(MetadataReference.CreateFromFile(interop.Location))
                                   .ToArray();

        var compilation = CSharpCompilation.Create(Name, trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                                         nullableContextOptions: NullableContextOptions.Enable));

        // Every generator the side projects run over the same source.
        var generators = typeof(SourceSharp.Generators.NetStateGenerator).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<Microsoft.CodeAnalysis.GeneratorAttribute>() is not null
                     && typeof(IIncrementalGenerator).IsAssignableFrom(t))
            .Select(t => ((IIncrementalGenerator)Activator.CreateInstance(t)!).AsSourceGenerator())
            .ToArray();
        Assert.True(generators.Length > 0, "the generators were found");

        CSharpGeneratorDriver.Create(generators, parseOptions: options)
                             .RunGeneratorsAndUpdateCompilation(compilation, out Compilation generated, out _);

        string dir = Path.Combine(Path.GetTempPath(), "ss-shared-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.Copy(abi.Location, Path.Combine(dir, Path.GetFileName(abi.Location)));
        File.Copy(interop.Location, Path.Combine(dir, Path.GetFileName(interop.Location)));

        string path = Path.Combine(dir, Name + ".dll");
        using (var stream = File.Create(path))
        {
            var result = generated.Emit(stream);
            Assert.True(result.Success, "the shared source compiles alone: "
                + string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(20)));
        }

        return path;
    }

    // What <ImplicitUsings>enable</ImplicitUsings> gives the side projects.
    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    private static IEnumerable<MetadataReference> Platform()
        => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0
                        && Path.GetDirectoryName(p) == Path.GetDirectoryName(typeof(object).Assembly.Location))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
}
