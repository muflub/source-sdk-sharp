extern alias client;

using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What <c>docs/plans/tf2/seams.tsv</c> cost to read once: the source and
/// test trees and the core assemblies, shared by the facts below.
/// </summary>
public sealed class SeamsLedgerIndex
{
    public const string Header = "interface\towner_rule\tside\tdispatch\tno_owner_fact\towner_fact\tnotes";

    public static readonly string[] OwnerRules =
        ["single", "chain", "single-per-key", "single-per-mode", "set-unique-id", "per-entity"];

    private readonly Assembly _server = typeof(SourceSharp.Server.ServerModule).Assembly;
    private readonly Assembly _client = typeof(client::SourceSharp.Client.ClientModule).Assembly;
    private readonly Assembly _ui = typeof(SourceSharp.Client.Ui.Toolkit.UiScreen).Assembly;

    private readonly Dictionary<string, SyntaxNode> _trees = new(StringComparer.Ordinal);
    private (string Path, string Text)[]? _tests;

    /// <summary>Every .cs file under src/test, read once (bin and obj are build output, not tests).</summary>
    public IReadOnlyList<(string Path, string Text)> TestSources
    {
        get
        {
            if (_tests is null)
            {
                string root = RepoLayout.Repo("src", "test");
                _tests = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                    .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(d => d is "bin" or "obj"))
                    .Select(p => (p, File.ReadAllText(p)))
                    .ToArray();
            }
            return _tests;
        }
    }

    private SyntaxNode Tree(string path)
    {
        if (!_trees.TryGetValue(path, out var tree))
            _trees[path] = tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
        return tree;
    }

    /// <summary>The problems with the ledger's lines; empty means every row is true.</summary>
    public List<string> Check(IReadOnlyList<string> lines)
    {
        var problems = new List<string>();
        var rows = lines.Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

        if (rows.Count == 0 || rows[0] != Header)
        {
            problems.Add($"the first row is not the header '{Header}'");
            return problems;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string row in rows.Skip(1))
        {
            string[] f = row.Split('\t');
            if (f.Length != 7)
            {
                problems.Add($"{row}: {f.Length} columns, want 7");
                continue;
            }

            string name = f[0];
            if (f.Take(6).Any(c => c.Trim().Length == 0))
                problems.Add($"{name}: an empty column (every seam needs an owner rule, a side, a dispatch and both facts)");
            if (!seen.Add(name))
                problems.Add($"{name}: listed twice");
            if (!OwnerRules.Contains(f[1]))
                problems.Add($"{name}: owner rule '{f[1]}' is not one of {string.Join("/", OwnerRules)}");
            if (f[2] is not ("server" or "client" or "shared"))
                problems.Add($"{name}: side '{f[2]}' is not server, client or shared");

            CheckInterface(name, f[2], problems);

            foreach (string site in Items(f[3]))
                CheckDispatch(name, site, problems);

            foreach (string fact in Items(f[4]).Concat(Items(f[5])))
                CheckFact(name, fact, problems);
        }

        return problems;
    }

    private static IEnumerable<string> Items(string column)
        => column.Split(", ", StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0);

    private void CheckInterface(string name, string side, List<string> problems)
    {
        if (name.StartsWith("non-type:", StringComparison.Ordinal))
        {
            if (name.Length == "non-type:".Length)
                problems.Add("a non-type row has no name");
            return;
        }

        Assembly[] where = side switch
        {
            "shared" => [_server, _client],
            "server" => [_server],
            _ => [_client, _ui],
        };

        // "shared" means BOTH sides compile it; the others need it on one.
        var found = where.Select(a => Resolve(a, name)).ToArray();
        bool ok = side == "shared" ? found.All(x => x) : found.Any(x => x);
        if (!ok)
            problems.Add($"{name}: no such type or member in the {side} core assemblies");
    }

    /// <summary>A type by full name, or a type's member as <c>Namespace.Type.Member</c>.</summary>
    private static bool Resolve(Assembly assembly, string name)
    {
        if (assembly.GetType(name) is not null)
            return true;

        int dot = name.LastIndexOf('.');
        if (dot <= 0 || assembly.GetType(name[..dot]) is not { } type)
            return false;

        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        return type.GetMember(name[(dot + 1)..], all).Length > 0;
    }

    private void CheckDispatch(string name, string site, List<string> problems)
    {
        int sep = site.IndexOf("::", StringComparison.Ordinal);
        if (sep <= 0)
        {
            problems.Add($"{name}: dispatch '{site}' is not path::Symbol");
            return;
        }

        string path = site[..sep], symbol = site[(sep + 2)..];
        string full = RepoLayout.Repo(path.Split('/'));
        if (path.Contains("..") || Path.IsPathRooted(path) || !File.Exists(full))
        {
            problems.Add($"{name}: dispatch file '{path}' does not exist in this repository");
            return;
        }

        string[] parts = symbol.Split('.');
        if (parts.Length is < 1 or > 2 || parts.Any(p => p.Length == 0))
        {
            problems.Add($"{name}: dispatch symbol '{symbol}' is not Type or Type.Member");
            return;
        }

        var types = Tree(full).DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                              .Where(t => t.Identifier.Text == parts[0]).ToList();
        if (types.Count == 0)
        {
            problems.Add($"{name}: {path} declares no type '{parts[0]}'");
            return;
        }

        if (parts.Length == 2 && !types.Any(t => Declares(t, parts[1])))
            problems.Add($"{name}: {parts[0]} in {path} declares no member '{parts[1]}'");
    }

    private static bool Declares(BaseTypeDeclarationSyntax type, string member)
    {
        if (type is not TypeDeclarationSyntax declared)
            return false;

        return declared.Members.Any(m => m switch
        {
            MethodDeclarationSyntax x => x.Identifier.Text == member,
            ConstructorDeclarationSyntax x => x.Identifier.Text == member,
            PropertyDeclarationSyntax x => x.Identifier.Text == member,
            EventDeclarationSyntax x => x.Identifier.Text == member,
            BaseFieldDeclarationSyntax x => x.Declaration.Variables.Any(v => v.Identifier.Text == member),
            BaseTypeDeclarationSyntax x => x.Identifier.Text == member,
            _ => false,
        });
    }

    private void CheckFact(string name, string fact, List<string> problems)
    {
        string[] parts = fact.Split('.');
        if (parts.Length != 2 || parts.Any(p => p.Length == 0))
        {
            problems.Add($"{name}: fact '{fact}' is not TestClass.Method");
            return;
        }

        string needle = "class " + parts[0];
        bool declared = TestSources
            .Where(s => s.Text.Contains(needle, StringComparison.Ordinal))
            .SelectMany(s => Tree(s.Path).DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(c => c.Identifier.Text == parts[0])
            .SelectMany(c => c.Members.OfType<MethodDeclarationSyntax>())
            .Any(m => m.Identifier.Text == parts[1] && IsFact(m));

        if (!declared)
            problems.Add($"{name}: no [Fact] or [Theory] {fact} is declared under src/test");
    }

    /// <summary>[Fact], [Theory], or a project attribute deriving their names (e.g. ClientShellFact).</summary>
    private static bool IsFact(MethodDeclarationSyntax method)
        => method.AttributeLists.SelectMany(l => l.Attributes)
                 .Select(a => a.Name.ToString().Split('.')[^1])
                 .Select(n => n.EndsWith("Attribute", StringComparison.Ordinal) ? n[..^"Attribute".Length] : n)
                 .Any(n => n.EndsWith("Fact", StringComparison.Ordinal) || n.EndsWith("Theory", StringComparison.Ordinal));
}

/// <summary>
/// The TF2 Stage 0 exit ledger (<c>docs/plans/tf2/seams.tsv</c>) stays true:
/// every row names a type the core compiles (or says it is not a type), a
/// dispatch symbol its file declares, and facts that are declared tests. Stage
/// 1+ reads the ledger instead of the core, so a row that rots is a wrong
/// answer handed on. The corrupted-copy facts show each check can fail.
/// </summary>
public class SeamsLedgerTests(SeamsLedgerIndex index) : IClassFixture<SeamsLedgerIndex>
{
    private static readonly string LedgerPath = RepoLayout.Repo("docs", "plans", "tf2", "seams.tsv");

    private static List<string> Ledger()
    {
        Assert.True(File.Exists(LedgerPath), $"the ledger {LedgerPath} is missing");
        return File.ReadAllLines(LedgerPath).ToList();
    }

    private static int RowCount(IEnumerable<string> lines)
        => lines.Count(l => l.Length > 0 && !l.StartsWith('#')) - 1;

    /// <summary>The ledger with one row's column replaced, for the corrupted-copy facts.</summary>
    private static List<string> Corrupted(string interfaceName, int column, Func<string, string> change)
    {
        var lines = Ledger();
        int at = lines.FindIndex(l => l.StartsWith(interfaceName + "\t", StringComparison.Ordinal));
        Assert.True(at >= 0, $"the ledger has no row for {interfaceName} to corrupt");

        string[] f = lines[at].Split('\t');
        string before = f[column];
        f[column] = change(before);
        Assert.True(f[column] != before, "the corruption changed the row");
        lines[at] = string.Join('\t', f);
        return lines;
    }

    [Fact]
    public void EveryRowNamesARealTypeADeclaredDispatchAndDeclaredFacts()
    {
        var lines = Ledger();

        Assert.True(index.TestSources.Count > 100, $"the test tree was read ({index.TestSources.Count} files)");
        Assert.True(RowCount(lines) >= 24, $"the ledger lists every Stage 0 seam ({RowCount(lines)} rows)");

        var problems = index.Check(lines);
        Assert.True(problems.Count == 0, "seams.tsv is wrong:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void ARowNamingAFactThatDoesNotExistIsCaught()
    {
        var lines = Corrupted("SourceSharp.Server.IClientCommandHook", 4,
            c => c.Replace("WithNoHookRegisteredSpectateReachesTheStockObserversArm", "WithNoHookRegisteredSpectateReachesNothing"));

        var problems = index.Check(lines);

        Assert.True(problems.Count == 1 && problems[0].Contains("ClientCommandHookTests.WithNoHookRegisteredSpectateReachesNothing"),
            "the renamed fact is the one problem: " + string.Join(" | ", problems));
    }

    [Fact]
    public void ARowNamingAHelperRatherThanAFactIsCaught()
    {
        // GameModesTests.Rig's Register is a method of a nested class, not a
        // test: a fact must be a [Fact]/[Theory] of the named test class.
        var lines = Corrupted("SourceSharp.Server.IGameMode", 4, _ => "GameModesTests.Register");

        var problems = index.Check(lines);

        Assert.True(problems.Count == 1 && problems[0].Contains("GameModesTests.Register"),
            "a non-test method is refused: " + string.Join(" | ", problems));
    }

    [Fact]
    public void ARowWhoseDispatchFileDoesNotExistIsCaught()
    {
        // The row's own path with one letter dropped (spelled as a row edit,
        // not as a file name: no file is looked up by this text).
        var lines = Corrupted("SourceSharp.Server.IPlayerDeathHook", 3,
            c => c.Replace("/PlayerDeaths.cs::", "/PlayerDeath.cs::"));

        var problems = index.Check(lines);

        Assert.True(problems.Count == 1 && problems[0].Contains("/PlayerDeath.cs' does not exist"),
            "the wrong file is the one problem: " + string.Join(" | ", problems));
    }

    [Fact]
    public void ARowWhoseDispatchSymbolTheFileDoesNotDeclareIsCaught()
    {
        var lines = Corrupted("SourceSharp.Server.IPlayerDeathHook", 3,
            c => c.Replace("PlayerDeaths.OnKilled", "PlayerDeaths.OnDied"));

        var problems = index.Check(lines);

        Assert.True(problems.Count == 1 && problems[0].Contains("declares no member 'OnDied'"),
            "the missing member is the one problem: " + string.Join(" | ", problems));
    }

    [Fact]
    public void ARowNamingATypeTheCoreDoesNotCompileIsCaught()
    {
        var lines = Corrupted("SourceSharp.Server.IRespawnGate", 0, _ => "SourceSharp.Server.IRespawnGates");

        var problems = index.Check(lines);

        Assert.True(problems.Count == 1 && problems[0].Contains("IRespawnGates: no such type"),
            "the unknown type is the one problem: " + string.Join(" | ", problems));
    }

    [Fact]
    public void ASharedRowNeedsTheTypeOnBothSides()
    {
        // IClientCommandHook is server-only: claiming "shared" must fail on
        // the client half, not pass on the server's copy.
        var lines = Corrupted("SourceSharp.Server.IClientCommandHook", 2, _ => "shared");

        var problems = index.Check(lines);

        Assert.True(problems.Count == 1 && problems[0].Contains("in the shared core assemblies"),
            "a one-sided type is not shared: " + string.Join(" | ", problems));
    }

    [Fact]
    public void ARowWithAnEmptyFactColumnIsCaught()
    {
        var lines = Corrupted("SourceSharp.Server.IRespawnGate", 4, _ => " ");

        var problems = index.Check(lines);

        Assert.True(problems.Count == 1 && problems[0].Contains("an empty column"),
            "a seam without its no-owner fact is refused: " + string.Join(" | ", problems));
    }
}
