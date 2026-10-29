using System.Reflection;

namespace SourceSharp.Tests;

/// <summary>
/// Where a test finds files: the one place that knows the repository layout.
///
/// <para><see cref="RepoRoot"/> is this repository: the directory holding
/// <c>SourceSharp.slnx</c>, found by walking up from the test binary
/// (<c>bin/&lt;config&gt;/tests</c>). The walk stops at the first hit and never
/// goes past it, so a test run in one checkout can never answer about another
/// one above it (the "test reads the wrong checkout" failure).</para>
///
/// <para><see cref="SdkTree"/> is the compat farm, <c>farm/&lt;config&gt;</c>:
/// the old single-repo layout rebuilt as links by <c>tools/setup</c>, where
/// <c>src/sourcesharp/...</c>, <c>src/game/...</c> and every Valve file the
/// build uses sit at the paths the tests were written against. A test that
/// reads a source file by its old path reads it here. It is
/// <c>$SOURCESHARP_SDK_TREE</c> when that is set, otherwise the farm of the
/// configuration this test assembly was built in.</para>
///
/// <para>A test asserting that a Valve file is ABSENT (an evicted translation
/// unit) must read the farm, where an evicted file has no link — never the
/// fork, which still holds it.</para>
/// </summary>
internal static class RepoLayout
{
    private const string Marker = "SourceSharp.slnx";

    private static readonly Lazy<string> Root = new(FindRoot);
    private static readonly Lazy<string> SdkLazy = new(FindSdkTree);

    /// <summary>This repository's root (holds SourceSharp.slnx).</summary>
    public static string RepoRoot => Root.Value;

    /// <summary>The compat farm for this configuration: the old layout.</summary>
    public static string SdkTree => SdkLazy.Value;

    /// <summary>Debug or release, as built: picks the farm.</summary>
    public static string Config =>
        typeof(RepoLayout).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
            .Equals("Release", StringComparison.OrdinalIgnoreCase) == true
            ? "release" : "debug";

    /// <summary>A path under the farm, in the old layout.</summary>
    public static string Sdk(params string[] parts) =>
        Path.Combine([SdkTree, .. parts]);

    /// <summary>A path under this repository, in its own layout.</summary>
    public static string Repo(params string[] parts) =>
        Path.Combine([RepoRoot, .. parts]);

    /// <summary>
    /// The source of a type the shared interop declares partial, with each
    /// side's half (plan_shim_areas_split phase 7): <c>src/shared/interop/T.cs</c>,
    /// <c>src/server/interop/T.Server.cs</c> and
    /// <c>src/client/interop/T.Client.cs</c>, joined. What a source-reading fact
    /// said about one file of the type it says about all three.
    /// </summary>
    public static string SharedTypeSource(string type) =>
        string.Join("\n", new[]
        {
            Repo("src", "shared", "interop", type + ".cs"),
            Repo("src", "server", "interop", type + ".Server.cs"),
            Repo("src", "client", "interop", type + ".Client.cs"),
        }.Select(File.ReadAllText));

    /// <summary>
    /// The directories a test used to find a file relative to, deepest first:
    /// the old test project and each of its ancestors, in the farm. The old
    /// tests walked up from their binary and took the first directory holding
    /// the path, so <c>host/sourcesharp.h</c> meant
    /// <c>src/sourcesharp/host/sourcesharp.h</c> and <c>public/const.h</c>
    /// meant <c>src/public/const.h</c>; this is that order, bounded by the
    /// farm root so it can never reach another checkout.
    /// </summary>
    private static IEnumerable<string> OldAnchors()
    {
        yield return Sdk("src", "sourcesharp", "managed", "SourceSharp.Tests");
        yield return Sdk("src", "sourcesharp", "managed");
        yield return Sdk("src", "sourcesharp");
        yield return Sdk("src");
        yield return SdkTree;
    }

    /// <summary>
    /// A file by the relative path an old-layout test walked up to (see
    /// <see cref="OldAnchors"/>), or null when the farm has none.
    /// </summary>
    public static string? FindSdkFile(string relative) =>
        OldAnchors().Select(a => Path.Combine(a, relative)).FirstOrDefault(File.Exists);

    /// <summary>A directory, as <see cref="FindSdkFile"/> finds a file.</summary>
    public static string? FindSdkDirectory(string relative) =>
        OldAnchors().Select(a => Path.Combine(a, relative)).FirstOrDefault(Directory.Exists);

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, Marker)))
                return dir.FullName;
        }

        throw new InvalidOperationException(
            $"no {Marker} above {AppContext.BaseDirectory}: the tests run from bin/<config>/tests of this repo");
    }

    private static string FindSdkTree()
    {
        string? env = Environment.GetEnvironmentVariable("SOURCESHARP_SDK_TREE");
        string tree = string.IsNullOrEmpty(env) ? Path.Combine(RepoRoot, "farm", Config) : env;

        // The build script is the farm's marker: every farm links it at
        // src/buildsourcesharp, exactly where the old checkout had it.
        if (!File.Exists(Path.Combine(tree, "src", "buildsourcesharp")))
        {
            throw new InvalidOperationException(
                $"no farm at {tree} (no src/buildsourcesharp): run `make setup`");
        }

        return tree;
    }
}
