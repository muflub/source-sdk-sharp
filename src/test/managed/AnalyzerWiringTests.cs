using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;

using SourceSharp.Analyzers;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The analyzers' rules reach the projects Directory.Build.props puts under
/// them. An analyzer that cannot read its project's roles is silently off:
/// the roles travel to the compiler through the generated .editorconfig, and
/// the first version wrote them ';'-separated, which an .editorconfig reads as
/// a comment, so every rule went quiet with the build still green.
/// </summary>
public class AnalyzerWiringTests
{
    /// <summary>Each gated list in Directory.Build.props, as (role, project names).</summary>
    private static List<(string Role, string[] Projects)> GatedLists()
    {
        string props = File.ReadAllText(RepoLayout.Repo("Directory.Build.props"));
        var lists = new List<(string, string[])>();

        foreach (var (role, property) in new[] { (AnalyzerRoles.StaticState, "SourceSharpStaticStateGated"), (AnalyzerRoles.Mod, "SourceSharpModGated"), (AnalyzerRoles.Test, "SourceSharpTestGated"), (AnalyzerRoles.Interface, "SourceSharpInterfaceGated") })
        {
            var match = Regex.Match(props, $@"<{property}>;([^<]*);</{property}>");
            Assert.True(match.Success, $"Directory.Build.props declares {property}");
            lists.Add((role, match.Groups[1].Value.Split(';')));
        }

        return lists;
    }

    /// <summary>What the compiler reads as the project's roles, parsed by Roslyn's own .editorconfig reader.</summary>
    private static string? RolesTheCompilerSees(string project)
    {
        string config = RepoLayout.Config == "release" ? "Release" : "Debug";
        string path = RepoLayout.Repo("bin", "obj", project, config, $"{project}.GeneratedMSBuildEditorConfig.editorconfig");
        Assert.True(File.Exists(path), $"{path} was not found: build the solution first; a check that read nothing would pass");

        var parsed = AnalyzerConfig.Parse(File.ReadAllText(path), path);
        var set = AnalyzerConfigSet.Create(new[] { parsed });
        set.GlobalConfigOptions.AnalyzerOptions.TryGetValue(AnalyzerRoles.Property, out string? roles);
        return roles;
    }

    /// <summary>
    /// The gated projects that were not built, on purpose: under
    /// <c>make ... HOST_SDK=0</c> (SS_HOST_SDK=0 in the environment, exported by
    /// the Makefile) the build is <c>SourceSharp.NoHost.slnf</c>, and the
    /// projects it leaves out of <c>SourceSharp.slnx</c> have no editorconfig to
    /// read. Computed from the two files, not listed here, so the filter stays
    /// the one record of what is left out; empty under any other value, so a
    /// full build is still held to every project.
    /// </summary>
    internal static HashSet<string> NotBuiltWithoutHostSdk(string? hostSdk)
    {
        if (hostSdk != "0")
            return [];

        var all = Regex.Matches(File.ReadAllText(RepoLayout.Repo("SourceSharp.slnx")), @"<Project Path=""([^""]+)""")
            .Select(m => m.Groups[1].Value).ToList();
        string filter = File.ReadAllText(RepoLayout.Repo("SourceSharp.NoHost.slnf"));
        var leftOut = all.Where(p => !filter.Contains($"\"{p}\"")).Select(Path.GetFileNameWithoutExtension).ToHashSet();
        Assert.True(leftOut.Count > 0, "SourceSharp.NoHost.slnf leaves nothing out of SourceSharp.slnx; HOST_SDK=0 would then build everything");
        return leftOut!;
    }

    [Fact]
    public void EveryGatedProjectsCompilerSeesItsRole()
    {
        var missing = new List<string>();
        var notBuilt = NotBuiltWithoutHostSdk(Environment.GetEnvironmentVariable("SS_HOST_SDK"));
        int checkedProjects = 0;
        int leftOut = 0;

        foreach (var (role, projects) in GatedLists())
        {
            foreach (string project in projects)
            {
                if (notBuilt.Contains(project))
                {
                    leftOut++;
                    continue;
                }

                checkedProjects++;
                string? roles = RolesTheCompilerSees(project);

                if (roles is null || !roles.Split(',').Contains(role))
                    missing.Add($"{project}: the compiler reads roles '{roles}', not '{role}'");
            }
        }

        Assert.True(checkedProjects + leftOut >= 23, $"the gated lists name the runtime and mod projects ({checkedProjects + leftOut})");
        Assert.True(leftOut <= notBuilt.Count, $"{leftOut} gated projects were left out, more than the {notBuilt.Count} the filter leaves out");
        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

    [Fact]
    public void WithTheHostSdkNothingIsLeftOut() =>
        Assert.Empty(NotBuiltWithoutHostSdk(null));

    [Fact]
    public void WithoutTheHostSdkExactlyTheFiltersProjectsAreLeftOut() =>
        Assert.Equal(["TF2.Descent.Server", "TF2.Descent.Tests"], NotBuiltWithoutHostSdk("0").Order());

    [Fact]
    public void AnUngatedProjectHasNoRoles() =>
        Assert.Null(RolesTheCompilerSees("SourceSharp.Mods"));

    [Fact]
    public void ASemicolonInAnEditorConfigValueEmptiesIt()
    {
        // The control for the fact above: the shape that went silent.
        var parsed = AnalyzerConfig.Parse("is_global = true\nbuild_property.SourceSharpAnalyzerRoles = ;static-state\n", "/x/.editorconfig");
        AnalyzerConfigSet.Create(new[] { parsed }).GlobalConfigOptions.AnalyzerOptions.TryGetValue(AnalyzerRoles.Property, out string? roles);
        Assert.True(string.IsNullOrEmpty(roles), $"read as '{roles}'");
    }
}
