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

    [Fact]
    public void EveryGatedProjectsCompilerSeesItsRole()
    {
        var missing = new List<string>();
        int checkedProjects = 0;

        foreach (var (role, projects) in GatedLists())
        {
            foreach (string project in projects)
            {
                checkedProjects++;
                string? roles = RolesTheCompilerSees(project);

                if (roles is null || !roles.Split(',').Contains(role))
                    missing.Add($"{project}: the compiler reads roles '{roles}', not '{role}'");
            }
        }

        Assert.True(checkedProjects >= 23, $"the gated lists name the runtime and mod projects ({checkedProjects})");
        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

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
