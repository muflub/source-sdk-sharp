using System.Text;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceSharp.Tests;

/// <summary>
/// plan_abi_hotreload §2 (rewritten at §5 row 12): which files of
/// SourceSharp.Abi are binding plumbing and stay, and where the rest go.
///
/// <para>
/// SourceSharp.Abi loads once and never reloads, so it should hold only what
/// binds native and managed code: the generated boundary and the value
/// contracts it names, state that must outlive a collectible context, the
/// loading contract and binding machinery, and the Log sink. Everything else
/// is game or engine-facing code that should reload with its side. Rulings:
/// </para>
///
/// <list type="bullet">
/// <item><b>plumbing</b> -- a seed: a generated boundary file (rule a), a row of
/// docs/abi-plumbing.tsv (rules b-e), or a file the generated boundary names
/// (rule a's closure: the boundary's field and argument types).</item>
/// <item><b>stays</b> -- not plumbing itself, but a plumbing file names it, so it
/// cannot leave until that reference goes. Each one is a blocker.</item>
/// <item><b>stuck</b> -- not plumbing, but named by an assembly no
/// destination serves, or by such a file, so it cannot leave until that
/// assembly references a destination. (The DebugApi assemblies were the ones
/// until plan_devapi_interfaces took them off the Abi; none is today.)</item>
/// <item><b>delete</b> -- a row ruled x: a delete target, neither a seed nor
/// counted as a reference that keeps anything.</item>
/// <item><b>moves</b> -- everything else, to the most specific assembly every
/// consumer can see (pointer code only to an Interop assembly), or
/// "unused" when nothing outside the tests names it.</item>
/// </list>
/// </summary>
internal static class AbiResidency
{
    public sealed record Ruling(string File, string Kind, string Why, string Destination);

    /// <summary>The generated boundary: rule a's seeds.</summary>
    private static readonly string[] GeneratedBoundary = { "ShimAreas.g.cs", "SharedForwarders.g.cs" };

    /// <summary>
    /// The destinations, most specific first, each with the assemblies that can
    /// see it (itself and what references it, per the csproj graph).
    /// </summary>
    private static readonly (string Name, string[] SeenBy, bool Interop)[] Destinations =
    {
        ("Server", new[] { "Server" }, false),
        ("Client", new[] { "Client" }, false),
        ("Server.Interop", new[] { "Server.Interop", "Server" }, true),
        ("Client.Interop", new[] { "Client.Interop", "Client" }, true),
        ("Shared", new[] { "Shared", "Server.Interop", "Client.Interop", "Server", "Client" }, false),
        ("Shared.Interop", new[] { "Shared.Interop", "Shared", "Server.Interop", "Client.Interop", "Server", "Client" }, true),
    };

    /// <summary>
    /// Every source tree outside Abi that can name an Abi type, as the
    /// destination-tier it sits in. A tree below every destination (one that
    /// references Abi but none of them) is its own tier, which only Abi serves.
    /// </summary>
    private static readonly (string Tier, string[] Dirs)[] Consumers =
    {
        ("Shared.Interop", new[] { "src/shared/interop" }),
        ("Shared", new[] { "src/shared/managed" }),
        ("Server.Interop", new[] { "src/server/interop" }),
        ("Client.Interop", new[] { "src/client/interop" }),
        ("Server", new[] { "src/server/managed", "src/mapgen", "src/tf2/server", "src/tf2_descent/server" }),
        ("Client", new[] { "src/client/managed", "src/tf2/client", "src/tf2_descent/client" }),
        // No DebugApi tier (plan_devapi_interfaces P5): both halves of the dev
        // API reference only System.*, their side's Interface and
        // Debug.Api.Shared, so they can name no Abi type (DebugApiClosureTests
        // holds that from metadata and from their projects).
    };

    public static string PlumbingPath => RepoLayout.Repo("docs", "abi-plumbing.tsv");

    public static string EdgesPath => RepoLayout.Repo("docs", "abi-edges.tsv");

    /// <summary>docs/abi-edges.tsv: the references source cannot show.</summary>
    public static IReadOnlyList<(string From, string To, string How)> Edges()
    {
        var rows = new List<(string, string, string)>();
        foreach (string line in File.ReadAllLines(EdgesPath))
        {
            if (line.StartsWith('#') || line.StartsWith("from\t", StringComparison.Ordinal) || line.Length == 0)
                continue;
            string[] cells = line.Split('\t');
            if (cells.Length != 3)
                throw new InvalidDataException($"docs/abi-edges.tsv: '{line}' is not from<TAB>to<TAB>how");
            rows.Add((cells[0], cells[1], cells[2]));
        }
        return rows;
    }

    /// <summary>docs/abi-plumbing.tsv: file -> (rule, reason).</summary>
    public static IReadOnlyDictionary<string, (string Rule, string Reason)> Plumbing()
    {
        var rows = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(PlumbingPath))
        {
            if (line.StartsWith('#') || line.StartsWith("file\t", StringComparison.Ordinal) || line.Length == 0)
                continue;
            string[] cells = line.Split('\t');
            if (cells.Length != 3)
                throw new InvalidDataException($"docs/abi-plumbing.tsv: '{line}' is not file<TAB>rule<TAB>reason");
            rows.Add(cells[0], (cells[1], cells[2]));
        }
        return rows;
    }

    /// <summary>The rulings, in file order, from the tree on disk.</summary>
    public static IReadOnlyList<Ruling> Measure() => Measure(out _, out _);

    /// <summary>
    /// The rulings, plus the graph they were ruled over: file -> the Abi files
    /// it names (with partial parts and docs/abi-edges.tsv), and the groups of
    /// files that share a partial type.
    /// </summary>
    public static IReadOnlyList<Ruling> Measure(out IReadOnlyDictionary<string, HashSet<string>> graph,
                                                out IReadOnlyList<IReadOnlySet<string>> partials)
        => Measure(out graph, out partials, []);

    /// <summary>
    /// The census with extra consumer tiers planted beside the real ones: a
    /// test's way to exercise the stuck arms now that no real consumer is
    /// one no destination serves (plan_devapi_interfaces P5). A planted dir
    /// may be absolute. <paramref name="plantedRows"/> are docs/abi-plumbing.tsv
    /// rows planted the same way, over the real ones: how the delete arm is
    /// exercised now that no real row is a delete target (Engine.cs, the last,
    /// was deleted in Phase 1).
    /// </summary>
    internal static IReadOnlyList<Ruling> Measure(out IReadOnlyDictionary<string, HashSet<string>> graph,
                                                  out IReadOnlyList<IReadOnlySet<string>> partials,
                                                  (string Tier, string[] Dirs)[] planted,
                                                  (string File, string Rule, string Reason)[]? plantedRows = null,
                                                  string? plantedAbi = null,
                                                  IReadOnlyList<(string Tier, HashSet<string> Words)>? consumers = null)
    {
        var (files, trees, compilation) = Compile(plantedAbi);
        var names = files.Select(f => Path.GetFileName(f)!).ToList();

        // Which file declares each named type (nested types belong to their file).
        // A partial type is declared in every file that has a part of it.
        var declaredIn = new Dictionary<INamedTypeSymbol, HashSet<string>>(SymbolEqualityComparer.Default);
        var typeNames = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            string file = Path.GetFileName(tree.FilePath);
            typeNames[file] = new HashSet<string>(StringComparer.Ordinal);
            foreach (var decl in tree.GetRoot().DescendantNodes().OfType<MemberDeclarationSyntax>()
                         .Where(d => d is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
            {
                if (model.GetDeclaredSymbol(decl) is INamedTypeSymbol t)
                {
                    if (!declaredIn.TryGetValue(t.OriginalDefinition, out var parts))
                        declaredIn[t.OriginalDefinition] = parts = new HashSet<string>(StringComparer.Ordinal);
                    parts.Add(file);
                    typeNames[file].Add(t.Name);
                }
            }
        }

        // File -> the files it names a type of.
        var uses = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            string file = Path.GetFileName(tree.FilePath);
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var symbol = model.GetSymbolInfo(name).Symbol;
                var type = symbol as INamedTypeSymbol ?? symbol?.ContainingType;
                for (; type is not null; type = type.ContainingType)
                    if (declaredIn.TryGetValue(type.OriginalDefinition, out var targets))
                        used.UnionWith(targets.Where(target => target != file));
            }
            uses[file] = used;
        }

        // The references source cannot show (reflection, generator output).
        var edges = Edges();
        foreach (var (from, to, _) in edges.Where(e => e.From.EndsWith(".cs", StringComparison.Ordinal)))
            if (uses.TryGetValue(from, out var set) && names.Contains(to))
                set.Add(to);

        graph = uses;
        partials = declaredIn.Values.Where(p => p.Count > 1).ToList();

        var listed = new Dictionary<string, (string Rule, string Reason)>(Plumbing(), StringComparer.Ordinal);
        foreach (var (file, rule, reason) in plantedRows ?? [])
            listed[file] = (rule, reason);
        var deleted = new HashSet<string>(listed.Where(r => r.Value.Rule == "x").Select(r => r.Key), StringComparer.Ordinal);

        // Rule a: the generated boundary and, to a fixpoint, what it names.
        var kind = new Dictionary<string, (string Kind, string Why)>(StringComparer.Ordinal);
        var boundary = new Queue<string>(GeneratedBoundary.Where(names.Contains));
        foreach (string f in boundary)
            kind[f] = ("plumbing", "a: the generated boundary");
        while (boundary.Count > 0)
        {
            string f = boundary.Dequeue();
            foreach (string dep in uses[f].Where(d => !kind.ContainsKey(d) && !deleted.Contains(d)).OrderBy(d => d, StringComparer.Ordinal))
            {
                kind[dep] = ("plumbing", "a: named by " + f);
                boundary.Enqueue(dep);
            }
        }

        // Rules b-e: the listed seeds.
        foreach (var (file, row) in listed)
            if (row.Rule != "x" && names.Contains(file) && !kind.ContainsKey(file))
                kind[file] = ("plumbing", row.Rule + ": " + row.Reason);
        foreach (string file in deleted.Where(names.Contains))
            kind[file] = ("delete", "x: " + listed[file].Reason);

        // Whatever plumbing names stays with it: Abi cannot reference out.
        var pulled = new Queue<string>(kind.Where(k => k.Value.Kind == "plumbing").Select(k => k.Key).OrderBy(f => f, StringComparer.Ordinal));
        while (pulled.Count > 0)
        {
            string f = pulled.Dequeue();
            foreach (string dep in uses[f].Where(d => !kind.ContainsKey(d)).OrderBy(d => d, StringComparer.Ordinal))
            {
                kind[dep] = ("stays", "named by plumbing " + f);
                pulled.Enqueue(dep);
            }
        }

        // Destinations: who outside Abi names each moving file's types, and
        // which moving Abi files name it, to a fixpoint.
        var tokens = (consumers ?? ReadConsumers()).Concat(planted.Select(c => (c.Tier, Words: Identifiers(c.Dirs)))).ToList();
        var moving = names.Where(f => !kind.ContainsKey(f)).ToList();
        var external = moving.ToDictionary(f => f,
            f => tokens.Where(c => typeNames[f].Any(c.Words.Contains)).Select(c => c.Tier)
                       .Concat(edges.Where(e => e.To == f && !e.From.EndsWith(".cs", StringComparison.Ordinal)).Select(e => e.From))
                       .ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var pointer = moving.ToDictionary(f => f,
            f => Regex.IsMatch(CodeOnly(File.ReadAllText(files[names.IndexOf(f)])), @"\bunsafe\b|delegate\s*\*|\w\*\s*\w"),
            StringComparer.Ordinal);
        // A file that holds the engine side itself goes to an Interop assembly
        // too (P1 run 3): no game assembly may reach EngineSide
        // (EngineAreaTests.NothingOutsideTheAbiCanReachTheEngine), and no game
        // assembly sees the internals it is reached through.
        var surface = moving.ToDictionary(f => f,
            f => f != "EngineSide.cs" && Regex.IsMatch(CodeOnly(File.ReadAllText(files[names.IndexOf(f)])), @"\bEngineSide\b"),
            StringComparer.Ordinal);
        // Stuck: a mover whose outside consumers no destination serves cannot
        // leave, and neither can anything it names: Abi cannot reference out.
        // (The DebugApi assemblies were such consumers until P5; none is today.)
        var stuck = new Queue<string>();
        foreach (string f in moving)
            if (external[f].Count > 0 && Pick(external[f], pointer[f] || surface[f]) is { } none && none.StartsWith("none", StringComparison.Ordinal))
            {
                kind[f] = ("stuck", "named by " + string.Join(", ", external[f].Where(t => !Destinations.Any(d => d.SeenBy.Contains(t)))
                                                                                .DefaultIfEmpty(none[6..])
                                                                                .OrderBy(t => t, StringComparer.Ordinal)));
                stuck.Enqueue(f);
            }
        while (stuck.Count > 0)
        {
            string f = stuck.Dequeue();
            foreach (string dep in uses[f].Where(d => !kind.ContainsKey(d)).OrderBy(d => d, StringComparer.Ordinal))
            {
                kind[dep] = ("stuck", "named by stuck " + f);
                stuck.Enqueue(dep);
            }
        }
        moving = moving.Where(f => !kind.ContainsKey(f)).ToList();

        var dest = new Dictionary<string, string>(StringComparer.Ordinal);
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (string f in moving)
            {
                var tiers = new HashSet<string>(external[f], StringComparer.Ordinal);
                foreach (string g in moving)
                    if (g != f && uses[g].Contains(f) && dest.TryGetValue(g, out var d) && d != "unused")
                        tiers.Add(d.StartsWith("none", StringComparison.Ordinal) ? "Abi" : d);
                string next = tiers.Count == 0 ? "unused" : Pick(tiers, pointer[f] || surface[f]);
                if (!dest.TryGetValue(f, out var was) || was != next)
                {
                    dest[f] = next;
                    changed = true;
                }
            }
        }

        return names.Select(file =>
        {
            if (kind.TryGetValue(file, out var k))
                return new Ruling(file, k.Kind, k.Why, "-");
            var seen = external[file].OrderBy(t => t, StringComparer.Ordinal).ToList();
            string why = (seen.Count == 0 ? "named outside Abi by nothing" : "named by " + string.Join(", ", seen))
                       + (pointer[file] ? "; pointer code" : string.Empty)
                       + (surface[file] && !pointer[file] ? "; holds the engine side" : string.Empty);
            return new Ruling(file, "moves", why, dest[file]);
        }).ToList();
    }

    /// <summary>The most specific destination every tier can see, or why none can.</summary>
    internal static string Pick(HashSet<string> tiers, bool pointerCode)
    {
        foreach (var (name, seenBy, interop) in Destinations)
            if ((interop || !pointerCode) && tiers.All(seenBy.Contains))
                return name;
        return "none: " + string.Join("+", tiers.OrderBy(t => t, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every name these trees use that could be a type: each simple name except
    /// the member half of <c>x.Name</c> or <c>x?.Name</c> and argument labels,
    /// so a property called Angles is not read as the type Angles. Declared
    /// names are tokens, not names, so a member declared with a type's name is
    /// not one either. A direct call names a method, and a name the same file
    /// declares as a type means that type (see <see cref="NamesIn"/>).
    /// </summary>
    private static HashSet<string> Identifiers(string[] dirs)
    {
        var files = new List<string>();
        foreach (string rel in dirs)
        {
            string dir = Path.IsPathRooted(rel) ? rel : RepoLayout.Repo(rel.Split('/'));
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException($"consumer tree {rel} is missing; the census would under-count its names");
            files.AddRange(Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
        }
        // Each file parses on its own (1.3 s one after another over the real
        // tiers); the union is taken after.
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var names in files.AsParallel().Select(f => NamesIn(File.ReadAllText(f))).ToList())
            words.UnionWith(names);
        return words;
    }

    /// <summary>
    /// The names every real consumer tier uses (see <see cref="Identifiers"/>):
    /// the census's most expensive input, and the same for every planted
    /// measurement, so a caller that measures several times reads it once and
    /// passes it to <c>Measure</c>'s <c>consumers</c>.
    /// </summary>
    internal static IReadOnlyList<(string Tier, HashSet<string> Words)> ReadConsumers()
        => Consumers.Select(c => (c.Tier, Identifiers(c.Dirs))).ToList();

    /// <summary>The names one source file uses that could be types (see <see cref="Identifiers"/>).</summary>
    internal static HashSet<string> NamesIn(string code)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var root = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest)).GetRoot();
        foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            bool member = name.Parent switch
            {
                // The member half of x.Name, unless x is the namespace:
                // global::SourceSharp.Abi.Foo.Bar names the type Foo.
                MemberAccessExpressionSyntax access => access.Name == name
                    && access.Expression.ToString() is not ("SourceSharp.Abi" or "global::SourceSharp.Abi"),
                MemberBindingExpressionSyntax => true,
                NameColonSyntax => true,
                // A direct call, Name(...) or Name<T>(...): a method, never a
                // type (plan_devapi_interfaces P5; DebugApiServer's ConVar and
                // NetTable read as Abi types).
                InvocationExpressionSyntax call => call.Expression == name,
                _ => false,
            };
            if (!member)
                words.Add(name.Identifier.ValueText);
        }

        // A type this file declares is the one its uses mean (P5;
        // DebugChannelListener's own Sink read as the Abi's).
        foreach (var declared in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            words.Remove(declared.Identifier.ValueText);
        foreach (var declared in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
            words.Remove(declared.Identifier.ValueText);

        return words;
    }

    /// <summary>The rulings as the committed table's text.</summary>
    public static string Table(IReadOnlyList<Ruling> rulings)
    {
        var s = new StringBuilder();
        s.Append("# plan_abi_hotreload §2: written by tools/abi-residency (AbiResidency.Measure) from docs/abi-plumbing.tsv.\n");
        s.Append("# DO NOT EDIT: a changed ruling is a changed tree; rerun the tool and read the diff.\n");
        s.Append("file\truling\twhy\tdestination\n");
        foreach (var r in rulings)
            s.Append(r.File).Append('\t').Append(r.Kind).Append('\t').Append(r.Why).Append('\t').Append(r.Destination).Append('\n');
        return s.ToString();
    }

    private static string CodeOnly(string text)
    {
        text = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\n]*", string.Empty);
    }

    /// <summary>The framework only: Abi references nothing else.</summary>
    private static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal)
                     || Path.GetFileName(p) is "System.dll" or "mscorlib.dll" or "netstandard.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();

    /// <summary>The errors binding src/abi on its own, for the vacuity fact, first few spelled out.</summary>
    public static (int Count, string First) CompileErrors()
    {
        var errors = Compile().Compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        return (errors.Count, string.Join("\n", errors.Take(5)));
    }

    /// <summary>
    /// src/abi as the SDK compiles it: Directory.Build.props sets LangVersion
    /// latest and ImplicitUsings, so the implicit global usings are a tree of
    /// their own here.
    /// </summary>
    /// <remarks>
    /// <paramref name="plantedAbi"/> is a directory whose .cs files are read as
    /// Abi files beside the real ones: how a fact exercises the moving arms
    /// once the drain has left no real mover (P1 run 3).
    /// </remarks>
    private static (List<string> Files, List<SyntaxTree> Trees, CSharpCompilation Compilation) Compile(string? plantedAbi = null)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        string abiDir = RepoLayout.Repo("src", "abi");
        var files = Directory.GetFiles(abiDir, "*.cs")
            .Concat(plantedAbi is null ? [] : Directory.GetFiles(plantedAbi, "*.cs"))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        var trees = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, path: f)).ToList();
        var usings = CSharpSyntaxTree.ParseText(
            "global using System; global using System.Collections.Generic; global using System.IO; " +
            "global using System.Linq; global using System.Net.Http; global using System.Threading; " +
            "global using System.Threading.Tasks;", parse, path: "(the SDK's implicit usings)");

        var compilation = CSharpCompilation.Create(
            "AbiResidency", trees.Append(usings), References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        return (files, trees, compilation);
    }
}
