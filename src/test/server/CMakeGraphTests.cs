using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="CMakeGraph"/> against the real build: the readers find the three
/// game targets, and two independent readings (compile_commands.json and
/// build.ninja's link edge) agree. A missing input must throw rather than read
/// as an empty set, which the fixture facts prove through the env override.
/// </summary>
public class CMakeGraphTests
{
    [Fact]
    public void EachGameTargetCompilesItsOwnShimAndNotTheOtherSidesOne()
    {
        Assert.True(CMakeGraph.Compiles(CMakeGraph.Server, "sourcesharp/game/server/shell_gamedll.cpp"));
        Assert.True(CMakeGraph.Compiles(CMakeGraph.Client, "sourcesharp/game/client/shell_clientdll.cpp"));
        Assert.True(CMakeGraph.Compiles(CMakeGraph.Host, "sourcesharp/host/sourcesharp.cpp"));
        Assert.False(CMakeGraph.Compiles(CMakeGraph.Server, "sourcesharp/game/client/shell_clientdll.cpp"));
        Assert.False(CMakeGraph.Compiles(CMakeGraph.Client, "sourcesharp/game/server/shell_gamedll.cpp"));
    }

    [Theory]
    [InlineData(CMakeGraph.Server, "out/server.so")]
    [InlineData(CMakeGraph.Client, "out/client.so")]
    [InlineData(CMakeGraph.Host, "out/libmanaged_host.so")]
    public void TheLinkEdgeNamesOneObjectPerCompiledSource(string target, string output)
    {
        CMakeGraph.LinkEdge edge = CMakeGraph.Link(target);
        Assert.Equal(output, edge.Output);
        Assert.Equal(CMakeGraph.CompiledSources(target).Count, edge.Objects.Count);
        Assert.All(edge.Objects, o => Assert.Contains($"CMakeFiles/{target}.dir/", o, StringComparison.Ordinal));
    }

    [Fact]
    public void TheGameDllsLinkTheHost()
    {
        Assert.Contains("-lmanaged_host", CMakeGraph.LinkLibraries(CMakeGraph.Server));
        Assert.Contains("-lmanaged_host", CMakeGraph.LinkLibraries(CMakeGraph.Client));
        Assert.DoesNotContain("-lmanaged_host", CMakeGraph.LinkLibraries(CMakeGraph.Host));
    }

    [Fact]
    public void TheServerIsCompiledWithItsOwnDefinitions()
    {
        Assert.Contains("GAME_DLL", CMakeGraph.CompileDefinitions(CMakeGraph.Server));
        Assert.DoesNotContain("CLIENT_DLL", CMakeGraph.CompileDefinitions(CMakeGraph.Server));
        Assert.Contains("CLIENT_DLL", CMakeGraph.CompileDefinitions(CMakeGraph.Client));
        Assert.Contains("GAME_DLL", CMakeGraph.ListedDefinitions(CMakeGraph.Server));
    }

    [Fact]
    public void AnUnknownTargetIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => CMakeGraph.ListFile("not_a_target"));
        Assert.Throws<InvalidOperationException>(() => CMakeGraph.Link("not_a_target"));
    }

    [Fact]
    public void AFileOutsideTheFarmIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => CMakeGraph.FarmRelative("/elsewhere/a/one.cpp"));
    }

    // The lists name this repo's files where they live; every caller asks by
    // the farm spelling, so the map back through tools/module-map.tsv has to
    // take the MOST SPECIFIC row (a `file` row over the broad `files` row of
    // src/shared/native) and refuse a path no row links.
    //
    // The repo-side names are built rather than spelled: they are paths of
    // THIS repo, which SourceFileNamesExistTests (a farm scan) cannot resolve.
    [Fact]
    public void ALocalPathMapsBackToItsFarmSpelling()
    {
        static string Local(string module, string file) => $"src/{module}/native/{file}";
        Assert.Equal("sourcesharp/game/server/physics.cpp", CMakeGraph.RepoToFarm(Local("server", "physics.cpp")));
        Assert.Equal("sourcesharp/game/shared/shell_shared.cpp", CMakeGraph.RepoToFarm(Local("shared", "shell_shared.cpp")));
        Assert.Equal("sourcesharp/public/collisionutils.cpp", CMakeGraph.RepoToFarm(Local("shared", "collisionutils.cpp")));
        Assert.Equal("sourcesharp/game/server/shell_gamedll.cpp",
            CMakeGraph.FarmRelative(RepoLayout.Repo("src", "server", "native", "shell_gamedll.cpp")));
        Assert.Throws<InvalidOperationException>(() => CMakeGraph.RepoToFarm(Local("nowhere", "none") + ".cpp"));
        Assert.Throws<InvalidOperationException>(() => CMakeGraph.ListToken("${UNKNOWN}/x" + ".cpp", CMakeGraph.Server));
    }
}

/// <summary>
/// The fixture half: a CMakeBuild over a temp build dir of its own. The
/// readers take the directory as an input, so these facts run beside every
/// other CMakeGraph reader; they used to move SOURCESHARP_BUILD_DIR, which
/// every reader in the process read, and so ran alone in ProcessWide.
/// </summary>
public class CMakeGraphFixtureTests
{
    private static void WithBuildDir(Action<string, CMakeBuild> body)
    {
        string dir = Path.Combine(Path.GetTempPath(), "cmake_graph_" + Guid.NewGuid().ToString("N"), "build");
        Directory.CreateDirectory(dir);
        try
        {
            body(dir, new CMakeBuild(dir));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public void AMissingDatabaseIsAnErrorNotAnEmptySet() =>
        WithBuildDir((dir, build) =>
        {
            var e = Assert.Throws<InvalidOperationException>(() => build.Compiles(CMakeGraph.Server, "x.cpp"));
            Assert.Contains("compile_commands.json", e.Message, StringComparison.Ordinal);
            // It read the planted build, not the run's: the path is the temp dir's.
            Assert.Contains(dir, e.Message, StringComparison.Ordinal);
        });

    [Fact]
    public void AnEmptyDatabaseIsAnError() =>
        WithBuildDir((dir, build) =>
        {
            File.WriteAllText(Path.Combine(dir, "compile_commands.json"), "[]");
            Assert.Throws<InvalidOperationException>(() => build.CompileCommands());
            File.WriteAllText(Path.Combine(dir, "compile_commands.json"), "");
            Assert.Throws<InvalidOperationException>(() => build.CompileCommands());
        });

    [Fact]
    public void AStaleDatabaseIsAnErrorNotAnAnswer() =>
        WithBuildDir((dir, build) =>
        {
            // The server's real list, minus one file the database does not name.
            string farmSrc = Path.Combine(Path.GetDirectoryName(dir)!, "src");
            var entries = CMakeGraph.ListedSources(CMakeGraph.Server).Skip(1).Select(s => new
            {
                directory = dir,
                command = "c++ -DGAME_DLL -c " + farmSrc + "/" + s,
                file = farmSrc + "/" + s,
                output = "src/server/native/CMakeFiles/server_sourcesharp.dir/" + s + ".o",
            });
            File.WriteAllText(Path.Combine(dir, "compile_commands.json"), System.Text.Json.JsonSerializer.Serialize(entries));
            var e = Assert.Throws<InvalidOperationException>(() => build.CompiledSources(CMakeGraph.Server));
            Assert.Contains("listed, not compiled: " + CMakeGraph.ListedSources(CMakeGraph.Server)[0], e.Message, StringComparison.Ordinal);
        });

    [Fact]
    public void AMissingNinjaFileIsAnError() =>
        WithBuildDir((_, build) => Assert.Throws<InvalidOperationException>(() => build.LinkLibraries(CMakeGraph.Server)));

    /// <summary>
    /// The debug-only sources (server_sourcesharp_debug_sources, user ruling
    /// 2026-10-01): every entry is a <c>*_debug.cpp</c>, and the build this run
    /// reads compiles all of them when it is a debug build and none of them
    /// when it is a release one.
    /// </summary>
    [Fact]
    public void TheDebugOnlySourcesAreCompiledInExactlyTheDebugBuild()
    {
        var debug = CMakeGraph.DebugListedSources(CMakeGraph.Server);
        Assert.NotEmpty(debug);
        Assert.All(debug, s => Assert.EndsWith("_debug.cpp", s));

        var compiled = CMakeGraph.CompiledSources(CMakeGraph.Server);
        bool isDebug = CMakeGraph.BuildConfig == "debug";
        Assert.True(isDebug || CMakeGraph.BuildConfig == "release", "config read as " + CMakeGraph.BuildConfig);
        Assert.All(debug, s => Assert.Equal(isDebug, compiled.Contains(s)));
    }
}
