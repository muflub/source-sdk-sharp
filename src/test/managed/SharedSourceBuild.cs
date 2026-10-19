using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The code both sides compile, compiled ALONE: the shared seams
/// (src/shared/interface, plan_shim_areas_split phase 4), the shared interop
/// (src/shared/interop) as the SourceSharp.Shared.Interop assembly was before
/// plan_shim_areas_split phase 3 compiled it into each side's interop, and the
/// shared source (src/shared/managed) as the SourceSharp.Shared assembly was
/// before plan_side_isolation P7 folded it into each side, against the Abi and
/// that interop only, with the generators. What a fact needs to say about "the
/// code both sides compile" it says about these, because inside a side's
/// assemblies that code is mixed with the side's own.
/// </summary>
internal static class SharedSourceBuild
{
    /// <summary>
    /// The shared source's assembly name. The interop below grants it its
    /// internals, as Shared.Interop granted SourceSharp.Shared, because the
    /// shared source reads them.
    /// </summary>
    public const string Name = "SourceSharp.SharedSource";

    /// <summary>
    /// The shared interop's assembly name. The Abi grants it its internals, as
    /// it granted SourceSharp.Shared.Interop.
    /// </summary>
    public const string InteropName = "SourceSharp.SharedInteropSource";

    /// <summary>
    /// The shared seams' assembly name. They are compiled into each side's
    /// interface assembly (plan_shim_areas_split phase 4), and alone here for
    /// the interop and the shared source to build against. It grants both its
    /// internals, as the side's interface assemblies grant theirs, for the one
    /// internal seam (IEngineStringTables).
    /// </summary>
    public const string InterfaceName = "SourceSharp.SharedInterfaceSource";

    /// <summary>A fresh directory for one build; the caller deletes it.</summary>
    public static string NewDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ss-shared-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Compiles the shared source into a fresh directory, beside copies of the
    /// Abi and the shared interop it references (built alone, as
    /// <see cref="BuildInterop"/>), and answers the built file's path. A
    /// compile error is a failure: an audit of an assembly that did not build
    /// would read nothing.
    /// </summary>
    public static string Build()
    {
        string dir = NewDirectory();
        string interop = BuildInterop(dir);
        string path = Compile(dir, Name, Sources(50, "src", "shared", "managed"),
                              [Abi().Location, Path.Combine(dir, InterfaceName + ".dll"), interop],
                              allowUnsafe: false, extra: []);
        return path;
    }

    /// <summary>
    /// Compiles the shared interop alone into <paramref name="dir"/>, beside a
    /// copy of the Abi and the shared seams compiled alone
    /// (<see cref="InterfaceName"/>), and answers the built file's path.
    /// </summary>
    public static string BuildInterop(string dir)
    {
        const string interfaceGrants = """
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SourceSharp.SharedInteropSource")]
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SourceSharp.SharedSource")]
            """;
        string seams = Compile(dir, InterfaceName, Sources(20, "src", "shared", "interface"),
                               [Abi().Location], allowUnsafe: false, extra: [interfaceGrants]);

        // What the interop granted as an assembly of its own and what the code
        // compiled alone needs: the shared source reads its internals.
        const string grants = """
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SourceSharp.SharedSource")]
            """;

        // EngineSide's per-side half (plan_shim_areas_split phase 7): the shared
        // source calls a slot both sides serve on OwnShims, which each side's
        // file names. The server's stands in here: both containers carry those
        // slots under the same names, so the audit reads the same slots.
        string serverHalf = File.ReadAllText(RepoLayout.Repo("src", "server", "interop", "EngineSide.Server.cs"));

        return Compile(dir, InteropName, Sources(50, "src", "shared", "interop"),
                       [Abi().Location, seams], allowUnsafe: true, extra: [grants, serverHalf]);
    }

    private static Assembly Abi() => typeof(SourceSharp.Abi.ILog).Assembly;

    private static string[] Sources(int atLeast, params string[] relative)
    {
        string source = RepoLayout.Repo(relative);
        string sep = Path.DirectorySeparatorChar.ToString();
        string[] files = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
                                  .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
                                           && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
                                  .OrderBy(f => f, StringComparer.Ordinal)
                                  .ToArray();
        Assert.True(files.Length >= atLeast, $"the shared code at {source} ({files.Length} files, at least {atLeast})");
        return files;
    }

    private static string Compile(string dir, string name, string[] files, string[] references,
                                  bool allowUnsafe, string[] extra)
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), options, f))
                         .Append(CSharpSyntaxTree.ParseText(ImplicitUsings, options, "<implicit usings>"))
                         .Concat(extra.Select((t, k) => CSharpSyntaxTree.ParseText(t, options, $"<extra {k}>")))
                         .ToList();

        var compilation = CSharpCompilation.Create(name, trees,
            Platform().Concat(references.Select(r => (MetadataReference)MetadataReference.CreateFromFile(r))).ToArray(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                                         nullableContextOptions: NullableContextOptions.Enable,
                                         allowUnsafe: allowUnsafe));

        // Every generator the side projects run over the same source.
        var generators = typeof(SourceSharp.Generators.NetStateGenerator).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<Microsoft.CodeAnalysis.GeneratorAttribute>() is not null
                     && typeof(IIncrementalGenerator).IsAssignableFrom(t))
            .Select(t => ((IIncrementalGenerator)Activator.CreateInstance(t)!).AsSourceGenerator())
            .ToArray();
        Assert.True(generators.Length > 0, "the generators were found");

        CSharpGeneratorDriver.Create(generators, parseOptions: options)
                             .RunGeneratorsAndUpdateCompilation(compilation, out Compilation generated, out _);

        foreach (string reference in references)
        {
            string copy = Path.Combine(dir, Path.GetFileName(reference));
            if (!File.Exists(copy))
                File.Copy(reference, copy);
        }

        string path = Path.Combine(dir, name + ".dll");
        using (var stream = File.Create(path))
        {
            var result = generated.Emit(stream);
            Assert.True(result.Success, $"{name} compiles alone: "
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
