using System.Reflection;
using System.Xml.Linq;
using SourceSharp.Tests;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// TF2.Descent.Ai is engine-free (plan_descent_logic section 1, 3.10's structure gate): it
/// references the AI core and the rules, and nothing else beyond the framework, in the
/// built assembly and in its project file.
/// </summary>
public sealed class AiReferenceTests
{
    private static readonly string[] Allowed = ["SourceSharp.Ai", "TF2.Descent.Rules"];

    private static bool IsFramework(string name) =>
        name is "netstandard" or "mscorlib"
        || name.StartsWith("System", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.", StringComparison.Ordinal);

    [Fact]
    public void The_horde_assembly_references_exactly_the_ai_core_and_the_rules()
    {
        var references = Assembly.Load(new AssemblyName("TF2.Descent.Ai")).GetReferencedAssemblies()
            .Select(r => r.Name!).Where(r => !IsFramework(r)).Order(StringComparer.Ordinal);

        Assert.Equal(Allowed, references);
    }

    [Fact]
    public void The_horde_project_names_exactly_the_ai_core_and_the_rules()
    {
        // A reference the code does not use yet leaves no mark in the assembly; the project
        // file still carries it into the build, so it is held here too.
        var project = XDocument.Load(RepoLayout.Repo("src", "tf2_descent", "ai", "TF2.Descent.Ai.csproj"));
        var named = project.Descendants()
            .Where(e => e.Name.LocalName is "ProjectReference" or "PackageReference" or "Reference")
            .Select(e => Path.GetFileNameWithoutExtension(((string?)e.Attribute("Include") ?? "").Replace('\\', '/')))
            .Order(StringComparer.Ordinal);

        Assert.Equal(["SourceSharp.Ai", "TF2.Descent.Rules"], named);
    }
}
