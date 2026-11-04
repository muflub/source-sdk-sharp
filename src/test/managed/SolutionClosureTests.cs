using System.Xml.Linq;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A solution build hands its configuration only to the projects it lists. A
/// project reached only by reference builds as Debug in a Release build
/// (MSBuild's ShouldUnsetParentConfigurationAndPlatform), which is how the two
/// interop assemblies, with every hot path in them, shipped unoptimised.
/// </summary>
public class SolutionClosureTests
{
    private static string Normal(string path) => Path.GetFullPath(path.Replace('\\', '/'));

    private static IEnumerable<string> ReferencesOf(string project) =>
        XDocument.Load(project).Descendants()
                 .Where(e => e.Name.LocalName == "ProjectReference")
                 .Select(e => (string?)e.Attribute("Include") ?? "")
                 .Select(include => Normal(Path.Combine(Path.GetDirectoryName(project)!, include.Replace('\\', '/'))));

    [Fact]
    public void Every_project_a_listed_project_references_is_listed()
    {
        var listed = XDocument.Load(RepoLayout.Repo("SourceSharp.slnx")).Descendants()
                              .Where(e => e.Name.LocalName == "Project")
                              .Select(e => Normal(RepoLayout.Repo((string)e.Attribute("Path")!)))
                              .ToHashSet();
        // The stimulus reaches the subject: an empty walk would pass.
        Assert.True(listed.Count > 40, $"{listed.Count} project(s) were read");
        Assert.Contains(Normal(RepoLayout.Repo("src/client/managed/SourceSharp.Client.csproj")), listed);

        var unlisted = listed.SelectMany(project => ReferencesOf(project).Where(reference => !listed.Contains(reference))
                                                             .Select(reference => $"{reference} <- {project}"))
                             .ToList();

        Assert.Empty(unlisted);
    }
}
