using System.Text.Json;

namespace SourceSharp.Tests;

/// <summary>
/// What the CMake build compiles and links, for tests that assert it.
///
/// <para>Phase 6b replaced VPC with CMake. A test that used to read
/// <c>game/{server,client}/*_csharp.vpc</c> or VPC's ninja graph asks here:
/// which sources a target compiles comes from <c>compile_commands.json</c>,
/// cross-checked against the target's hand-owned CMakeLists source list; what
/// it links, and which objects are on its link edge, come from
/// <c>build.ninja</c> — never from an <c>ls</c> of the object directory.</para>
///
/// <para>EVERY READER FAILS LOUDLY. A missing, empty, unparsable or stale input
/// throws <see cref="InvalidOperationException"/>; a reader that found nothing
/// must never let an absence assertion pass. Stale means
/// <c>compile_commands.json</c> and the CMakeLists list disagree (a build not
/// re-run after a list edit).</para>
///
/// <para>Paths are farm-relative: <c>game/server/player.cpp</c>,
/// <c>sourcesharp/game/server/shell_gamedll.cpp</c>. The lists name a Valve file
/// through the farm (<c>${SS_SRC}/...</c>) but this repo's files where they live
/// (<c>${CMAKE_CURRENT_SOURCE_DIR}/x.cpp</c>,
/// <c>${PROJECT_SOURCE_DIR}/src/shared/native/x.cpp</c>), and so does
/// <c>compile_commands.json</c>; both map back to the farm spelling through
/// <c>tools/module-map.tsv</c> (<see cref="RepoToFarm"/>), and a local path no
/// row covers throws.
/// The build dir is <c>$SOURCESHARP_BUILD_DIR</c>, else
/// <c>&lt;SdkTree&gt;/build</c> (<c>farm/&lt;config&gt;/build</c>). The C++
/// twin is <c>src/test/native/cmake_graph.h</c>, the Python one
/// <c>tools/cmakegraph.py</c>.</para>
/// </summary>
internal static class CMakeGraph
{
    public const string Server = "server_sourcesharp";
    public const string Client = "client_sourcesharp";
    public const string Host = "sourcesharp";

    /// <summary>
    /// The build this test run reads: <c>$SOURCESHARP_BUILD_DIR</c>, else
    /// <c>&lt;SdkTree&gt;/build</c>. The environment is read ONCE, here; a fact
    /// that needs another build constructs its own <see cref="CMakeBuild"/>
    /// instead of moving the variable under every other reader.
    /// </summary>
    public static CMakeBuild Current { get; } = new(DefaultBuildDir());

    private static string DefaultBuildDir()
    {
        string? env = Environment.GetEnvironmentVariable("SOURCESHARP_BUILD_DIR");
        return string.IsNullOrEmpty(env) ? Path.Combine(RepoLayout.SdkTree, "build") : env;
    }

    public static string BuildDir => Current.BuildDir;

    /// <summary>The repo-relative CMakeLists that owns a target's source list.</summary>
    public static string ListFile(string target) => target switch
    {
        Server => "src/server/native/CMakeLists.txt",
        Client => "src/client/native/CMakeLists.txt",
        Host => "src/host/native/CMakeLists.txt",
        "tier1" or "mathlib" or "raytrace" => $"cmake/sdk/{target}.cmake",
        _ => throw new InvalidOperationException($"cmake_graph: no CMakeLists is known for target '{target}'"),
    };

    public static string ListText(string target) =>
        ReadRequired(RepoLayout.Repo(ListFile(target)), "the target's CMakeLists");

    /// <summary>The target's <c>set(&lt;target&gt;_sources ...)</c> list, in order, farm-relative.</summary>
    public static IReadOnlyList<string> ListedSources(string target) =>
        Block(ListText(target), $"set({target}_sources\n", ListFile(target))
            .Select(t => ListToken(t, target)).ToList();

    /// <summary>
    /// The target's OPTIONAL <c>set(&lt;target&gt;_debug_sources ...)</c> list:
    /// sources the CMakeLists appends only in a debug build (the diagnostic
    /// console commands, user ruling 2026-10-01). Empty when there is no such
    /// block; a file in both lists is an error.
    /// </summary>
    public static IReadOnlyList<string> DebugListedSources(string target)
    {
        string text = ListText(target);
        string open = $"set({target}_debug_sources\n";
        if (!text.Contains(open, StringComparison.Ordinal))
            return [];
        var debug = Block(text, open, ListFile(target)).Select(t => ListToken(t, target)).ToList();
        var both = debug.Intersect(ListedSources(target), StringComparer.Ordinal).FirstOrDefault();
        if (both is not null)
            throw new InvalidOperationException(
                $"cmake_graph: {ListFile(target)} lists {both} in both {target}_sources and {target}_debug_sources");
        return debug;
    }

    /// <summary>debug or release, read off <c>&lt;farm&gt;/&lt;config&gt;/build</c>.</summary>
    public static string BuildConfig => Current.BuildConfig;

    /// <summary>One source-list entry, farm-relative: <c>${SS_SRC}/x</c> is the
    /// farm's x; <c>${LOCAL}/x</c> (<c>${CMAKE_CURRENT_SOURCE_DIR}/x</c>),
    /// <c>${SHARED}/x</c> (src/shared/native) and <c>${PROJECT_SOURCE_DIR}/x</c>
    /// are this repo's, mapped through the module map; a bare entry is already
    /// farm-relative (the Valve libraries' lists).</summary>
    public static string ListToken(string token, string target)
    {
        const string farm = "${SS_SRC}/", here = "${CMAKE_CURRENT_SOURCE_DIR}/", local = "${LOCAL}/",
            shared = "${SHARED}/", repo = "${PROJECT_SOURCE_DIR}/";
        if (token.StartsWith(farm, StringComparison.Ordinal))
            return token[farm.Length..];
        if (token.StartsWith(here, StringComparison.Ordinal))
            return RepoToFarm(Path.GetDirectoryName(ListFile(target))! + "/" + token[here.Length..]);
        if (token.StartsWith(local, StringComparison.Ordinal))
            return RepoToFarm(Path.GetDirectoryName(ListFile(target))! + "/" + token[local.Length..]);
        if (token.StartsWith(shared, StringComparison.Ordinal))
            return RepoToFarm("src/shared/native/" + token[shared.Length..]);
        if (token.StartsWith(repo, StringComparison.Ordinal))
            return RepoToFarm(token[repo.Length..]);
        if (token.Contains('$'))
            throw new InvalidOperationException($"cmake_graph: {ListFile(target)}: cannot read the source-list entry '{token}'");
        return token;
    }

    /// <summary>This checkout, whose CMakeLists and module map the lists are read from.</summary>
    internal static string RepoOfBuild => RepoLayout.RepoRoot;

    /// <summary>
    /// The farm-relative spelling of a file of this repo: the most specific
    /// <c>tools/module-map.tsv</c> row that covers it, as <c>tools/setup</c>
    /// links it. Throws when no row does.
    /// </summary>
    public static string RepoToFarm(string repoRelative)
    {
        // A vendored Valve file (vendor/<side>/<path>) is named by its Valve
        // path, as the farm named it.
        string[] vendored = repoRelative.Split('/', 3);
        if (vendored.Length == 3 && vendored[0] == "vendor" && vendored[1] is "shared" or "server" or "client")
            return vendored[2];
        string map = ReadRequired(Path.Combine(RepoOfBuild, "tools", "module-map.tsv"), "the module map");
        string? bestRel = null, bestFarm = null;
        foreach (string raw in map.Split('\n'))
        {
            if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith('#'))
                continue;
            string[] parts = raw.Split('\t');
            if (parts.Length != 3)
                throw new InvalidOperationException($"cmake_graph: tools/module-map.tsv: bad row '{raw}'");
            (string farmPath, string kind, string rel) = (parts[0], parts[1], parts[2]);
            if (rel.Contains("{cfg}", StringComparison.Ordinal))
                continue;
            string? hit = kind == "file"
                ? (repoRelative == rel ? farmPath : null)
                : (repoRelative.StartsWith(rel + "/", StringComparison.Ordinal) ? farmPath + "/" + repoRelative[(rel.Length + 1)..] : null);
            if (hit is not null && (bestRel is null || rel.Length > bestRel.Length))
                (bestRel, bestFarm) = (rel, hit);
        }
        if (bestFarm is null || !bestFarm.StartsWith("src/", StringComparison.Ordinal))
            throw new InvalidOperationException($"cmake_graph: {repoRelative} is a file of this repo that no tools/module-map.tsv row links into the farm");
        return bestFarm["src/".Length..];
    }

    /// <summary>The target's <c>target_compile_definitions</c> list as written.</summary>
    public static IReadOnlyList<string> ListedDefinitions(string target) =>
        Block(ListText(target), $"target_compile_definitions({target} PRIVATE\n", ListFile(target));

    public sealed record Entry(string File, string Output, string Command);

    public static IReadOnlyList<Entry> CompileCommands() => Current.CompileCommands();
    public static IReadOnlyList<Entry> TargetEntries(string target) => Current.TargetEntries(target);
    public static string FarmRelative(string absolute) => Current.FarmRelative(absolute);
    public static IReadOnlySet<string> CompiledSources(string target) => Current.CompiledSources(target);
    public static bool Compiles(string target, string farmRelative) => Current.Compiles(target, farmRelative);
    public static IReadOnlySet<string> GameSources() => Current.GameSources();
    public static string CompileCommand(string target, string farmRelative) => Current.CompileCommand(target, farmRelative);
    public static IReadOnlySet<string> CompileDefinitions(string target) => Current.CompileDefinitions(target);

    public sealed record LinkEdge(string Output, IReadOnlyList<string> Objects, IReadOnlyDictionary<string, string> Vars);

    public static LinkEdge Link(string target) => Current.Link(target);
    public static IReadOnlyList<string> LinkObjects(string target) => Current.LinkObjects(target);
    public static IReadOnlyList<string> LinkLibraries(string target) => Current.LinkLibraries(target);

    internal static List<string> NinjaTokens(string line)
    {
        var toks = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool have = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '$' && i + 1 < line.Length)
            {
                cur.Append(line[++i]);
                have = true;
            }
            else if (c is ' ' or '\t')
            {
                if (have)
                    toks.Add(cur.ToString());
                cur.Clear();
                have = false;
            }
            else
            {
                cur.Append(c);
                have = true;
            }
        }
        if (have)
            toks.Add(cur.ToString());
        return toks;
    }

    private static List<string> Block(string text, string open, string where)
    {
        int at = text.IndexOf(open, StringComparison.Ordinal);
        if (at < 0)
            throw new InvalidOperationException($"cmake_graph: {where} has no `{open.TrimEnd()}` block");
        if (text.IndexOf(open, at + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"cmake_graph: {where} has more than one `{open.TrimEnd()}` block");
        var toks = new List<string>();
        bool closed = false;
        foreach (string raw in text[(at + open.Length)..].Split('\n'))
        {
            string code = raw.Contains('#') ? raw[..raw.IndexOf('#')] : raw;
            int paren = code.IndexOf(')');
            if (paren >= 0)
            {
                code = code[..paren];
                closed = true;
            }
            toks.AddRange(code.Split((char[])[' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries));
            if (closed)
                break;
        }
        if (!closed)
            throw new InvalidOperationException($"cmake_graph: {where}: `{open.TrimEnd()}` is never closed");
        if (toks.Count == 0)
            throw new InvalidOperationException($"cmake_graph: {where}: `{open.TrimEnd()}` lists nothing");
        return toks;
    }

    internal static string ReadRequired(string path, string why)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"cmake_graph: cannot open {path} ({why})");
        string text = File.ReadAllText(path);
        if (text.Length == 0)
            throw new InvalidOperationException($"cmake_graph: {path} is EMPTY ({why})");
        return text;
    }
}

/// <summary>
/// The readers of one CMake build directory (<c>compile_commands.json</c>,
/// <c>build.ninja</c>) -- the half of <see cref="CMakeGraph"/> that depends
/// on which build it reads. <see cref="CMakeGraph.Current"/> is the run's
/// build; a fact that reads a planted build makes its own.
/// </summary>
internal sealed class CMakeBuild(string buildDir)
{
    public string BuildDir { get; } = buildDir;

    private string FarmSrc => Path.Combine(Path.GetDirectoryName(BuildDir.TrimEnd('/'))!, "src");

    /// <summary>debug or release, read off <c>&lt;farm&gt;/&lt;config&gt;/build</c>.</summary>
    public string BuildConfig => Path.GetFileName(Path.GetDirectoryName(BuildDir.TrimEnd('/'))!);

    public IReadOnlyList<CMakeGraph.Entry> CompileCommands()
    {
        string path = Path.Combine(BuildDir, "compile_commands.json");
        string text = CMakeGraph.ReadRequired(path, "CMake's compile database -- run `make debug` (or release)");
        var entries = new List<CMakeGraph.Entry>();
        using JsonDocument doc = JsonDocument.Parse(text);
        foreach (JsonElement e in doc.RootElement.EnumerateArray())
        {
            string? file = e.TryGetProperty("file", out var f) ? f.GetString() : null;
            string? output = e.TryGetProperty("output", out var o) ? o.GetString() : null;
            string? command = e.TryGetProperty("command", out var c) ? c.GetString() : null;
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(output) || string.IsNullOrEmpty(command))
                throw new InvalidOperationException($"cmake_graph: {path}: an entry lacks file, output or command");
            entries.Add(new CMakeGraph.Entry(file, output, command));
        }
        if (entries.Count == 0)
            throw new InvalidOperationException($"cmake_graph: {path} has no entries");
        return entries;
    }

    public IReadOnlyList<CMakeGraph.Entry> TargetEntries(string target)
    {
        string dir = $"/CMakeFiles/{target}.dir/";
        var found = CompileCommands().Where(e => ("/" + e.Output).Contains(dir, StringComparison.Ordinal)).ToList();
        if (found.Count == 0)
            throw new InvalidOperationException($"cmake_graph: compile_commands.json names no object of target '{target}'");
        return found;
    }

    public string FarmRelative(string absolute)
    {
        string prefix = FarmSrc + "/";
        string path = Path.GetFullPath(absolute);
        if (path.StartsWith(prefix, StringComparison.Ordinal))
            return path[prefix.Length..];
        string repo = CMakeGraph.RepoOfBuild + "/";
        if (path.StartsWith(repo, StringComparison.Ordinal) && !path.StartsWith(repo + "farm/", StringComparison.Ordinal))
            return CMakeGraph.RepoToFarm(path[repo.Length..]);
        throw new InvalidOperationException($"cmake_graph: {absolute} is neither under the farm's src/ ({prefix}) nor a file of this repo");
    }

    /// <summary>
    /// THE ANSWER TO "IS X COMPILED INTO T": the sources compile_commands.json
    /// gives T, which must equal the CMakeLists list exactly.
    /// </summary>
    public IReadOnlySet<string> CompiledSources(string target)
    {
        var compiled = TargetEntries(target).Select(e => FarmRelative(e.File)).ToHashSet(StringComparer.Ordinal);
        var listed = CMakeGraph.ListedSources(target).ToHashSet(StringComparer.Ordinal);
        if (BuildConfig == "debug")
            listed.UnionWith(CMakeGraph.DebugListedSources(target));
        if (!listed.SetEquals(compiled))
        {
            var lines = listed.Except(compiled).Take(10).Select(s => "  listed, not compiled: " + s)
                .Concat(compiled.Except(listed).Take(10).Select(s => "  compiled, not listed: " + s));
            throw new InvalidOperationException(
                $"cmake_graph: compile_commands.json disagrees with {CMakeGraph.ListFile(target)} about '{target}' -- " +
                "a stale build (re-run make), or a list the parser misread:\n" + string.Join("\n", lines));
        }
        return compiled;
    }

    public bool Compiles(string target, string farmRelative) =>
        CompiledSources(target).Contains(farmRelative);

    /// <summary>Every source compiled into server.so or client.so.</summary>
    public IReadOnlySet<string> GameSources() =>
        CompiledSources(CMakeGraph.Server).Union(CompiledSources(CMakeGraph.Client)).ToHashSet(StringComparer.Ordinal);

    public string CompileCommand(string target, string farmRelative) =>
        TargetEntries(target).FirstOrDefault(e => FarmRelative(e.File) == farmRelative)?.Command
        ?? throw new InvalidOperationException($"cmake_graph: {farmRelative} is not compiled into '{target}'");

    /// <summary>The -D definitions of the target's compile command (NAME or NAME=VALUE).</summary>
    public IReadOnlySet<string> CompileDefinitions(string target)
    {
        var defs = TargetEntries(target)[0].Command
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.StartsWith("-D", StringComparison.Ordinal) && t.Length > 2)
            .Select(t => t[2..]).ToHashSet(StringComparer.Ordinal);
        if (defs.Count == 0)
            throw new InvalidOperationException($"cmake_graph: the compile command of '{target}' has no -D at all");
        return defs;
    }

    public CMakeGraph.LinkEdge Link(string target)
    {
        string path = Path.Combine(BuildDir, "build.ninja");
        string[] lines = CMakeGraph.ReadRequired(path, "CMake's ninja file -- run `make debug` (or release)")
            .Replace("$\n", "", StringComparison.Ordinal).Split('\n');
        string ruleMark = "_LIBRARY_LINKER__" + target + "_";
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("build ", StringComparison.Ordinal))
                continue;
            List<string> toks = CMakeGraph.NinjaTokens(lines[i][6..]);
            int k = toks.FindIndex(t => t.EndsWith(':'));
            if (k < 0 || k + 1 >= toks.Count)
                continue;
            string rule = toks[k + 1];
            int at = rule.IndexOf(ruleMark, StringComparison.Ordinal);
            if (at < 0 || rule[(at + ruleMark.Length)..].Contains('_'))
                continue;
            var objects = toks.Skip(k + 2).TakeWhile(t => t != "|" && t != "||").ToList();
            var vars = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int j = i + 1; j < lines.Length && lines[j].StartsWith("  ", StringComparison.Ordinal); j++)
            {
                int eq = lines[j].IndexOf(" = ", StringComparison.Ordinal);
                if (eq > 0)
                    vars[lines[j][..eq].Trim()] = lines[j][(eq + 3)..];
            }
            if (objects.Count == 0)
                throw new InvalidOperationException($"cmake_graph: build.ninja's link edge for '{target}' names no object");
            return new CMakeGraph.LinkEdge(toks[k][..^1], objects, vars);
        }
        throw new InvalidOperationException($"cmake_graph: build.ninja has no link edge for target '{target}'");
    }

    public IReadOnlyList<string> LinkObjects(string target) => Link(target).Objects;

    public IReadOnlyList<string> LinkLibraries(string target)
    {
        CMakeGraph.LinkEdge edge = Link(target);
        if (!edge.Vars.TryGetValue("LINK_LIBRARIES", out string? libs) || string.IsNullOrWhiteSpace(libs))
            throw new InvalidOperationException($"cmake_graph: build.ninja's link edge for '{target}' has no LINK_LIBRARIES");
        return CMakeGraph.NinjaTokens(libs);
    }
}
