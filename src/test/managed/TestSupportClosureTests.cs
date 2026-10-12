using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// SourceSharp.TestSupport holds the test doubles (RecordingEngine) the test
/// assemblies share, and no product assembly may reference it: a fake engine
/// reachable from the game is a fake engine the game can be handed. Read from
/// the project files and from the deployed metadata, as DebugApiClosureTests
/// reads its rules.
/// </summary>
public class TestSupportClosureTests
{
    private const string TestSupport = "SourceSharp.TestSupport";

    /// <summary>The projects that are tests, and so may reference it.</summary>
    private static readonly string[] TestProjects =
    [
        "src/test/managed/SourceSharp.Shared.Tests.csproj",
        "src/test/server/SourceSharp.Server.Tests.csproj",
        "src/test/client/SourceSharp.Client.Tests.csproj",
        "src/test/oracle/managed/SourceSharp.OracleTests.csproj",
        "src/tf2_descent/tests/TF2.Descent.Tests.csproj",
    ];

    private static string Relative(string path) =>
        Path.GetRelativePath(RepoLayout.RepoRoot, path).Replace('\\', '/');

    [Fact]
    public void No_product_project_references_it()
    {
        var projects = Directory.EnumerateFiles(RepoLayout.Repo("src"), "*.csproj", SearchOption.AllDirectories)
                                .Select(Relative)
                                .Where(p => !TestProjects.Contains(p) && p != "src/test/support/SourceSharp.TestSupport.csproj")
                                .ToList();
        // The stimulus reaches the subject: the game, its mods and its tools
        // are well over a dozen projects, and an empty walk would pass.
        Assert.True(projects.Count > 15, $"{projects.Count} product project(s) were read");
        Assert.Contains("src/server/managed/SourceSharp.Server.csproj", projects);

        var offenders = projects.Where(p => DebugApiClosureTests.ProjectReferencesOf(File.ReadAllText(RepoLayout.Repo(p.Split('/'))))
                                                              .Contains(TestSupport))
                                .ToList();
        Assert.True(offenders.Count == 0, "product projects reference " + TestSupport + ": " + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_test_project_references_it()
    {
        // The positive arm: the reader sees the reference where it is, so the
        // walk above would see it where it is not allowed.
        foreach (string project in TestProjects)
            Assert.Contains(TestSupport, DebugApiClosureTests.ProjectReferencesOf(File.ReadAllText(RepoLayout.Repo(project.Split('/')))));
    }

    [Fact]
    public void No_deployed_assembly_references_it_and_it_is_not_deployed()
    {
        string managed = RepoLayout.Repo("bin", RepoLayout.Config, "bin", "managed");
        var dlls = Directory.Exists(managed) ? Directory.GetFiles(managed, "*.dll") : [];
        Assert.True(dlls.Any(d => Path.GetFileName(d) == "SourceSharp.Server.dll"),
                    $"{managed} holds the deployed game ({dlls.Length} dll(s)); a check that read nothing would pass");
        Assert.DoesNotContain(dlls, d => Path.GetFileNameWithoutExtension(d) == TestSupport);

        var offenders = dlls.Where(d => DebugApiClosureTests.ReferencesOf(d).AssemblyRefs.Contains(TestSupport))
                            .Select(Path.GetFileName).ToList();
        Assert.True(offenders.Count == 0, "deployed assemblies reference " + TestSupport + ": " + string.Join(", ", offenders));

        // The positive arm, from metadata: the test assembly does.
        Assert.Contains(TestSupport, DebugApiClosureTests.ReferencesOf(typeof(TestSupportClosureTests).Assembly.Location).AssemblyRefs);
    }
}
