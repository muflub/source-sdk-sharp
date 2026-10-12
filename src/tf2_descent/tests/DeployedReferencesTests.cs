using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// Bootstrap loads assemblies by simple name from bin/&lt;config&gt;/bin/managed,
/// so everything a deployed TF2Descent assembly references, beyond the
/// framework, has to be a file there. A class library does not copy its
/// packages by default, and a missing one fails only when the code that needs
/// it first runs in the game: the client's bzip2 library was missing, which
/// left the prefetch failing on its background task. The tests' own output
/// directory has every package, so this reads the deployed directory.
/// </summary>
public sealed class DeployedReferencesTests
{
    private static string Deployed =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "bin", "managed"));

    private static string[] ReferencesOf(string file)
    {
        using var stream = File.OpenRead(file);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return metadata.AssemblyReferences
            .Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name))
            .ToArray();
    }

    private static bool IsFramework(string name) =>
        name is "netstandard" or "mscorlib"
        || name.StartsWith("System", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.", StringComparison.Ordinal);

    [Theory]
    [InlineData("TF2.Descent.Client", "ICSharpCode.SharpZipLib")]
    [InlineData("TF2.Descent.Server", "SourceSharp.Host.Sdk")]
    public void Every_reference_of_a_deployed_mod_assembly_is_deployed_beside_it(string assembly, string package)
    {
        string file = Path.Combine(Deployed, assembly + ".dll");
        Assert.True(File.Exists(file), $"{assembly} is not deployed in {Deployed}");

        string[] references = ReferencesOf(file);
        Assert.Contains(package, references);   // the stimulus: a package it really uses

        var missing = references
            .Where(r => !IsFramework(r) && !File.Exists(Path.Combine(Deployed, r + ".dll")))
            .ToArray();
        Assert.Empty(missing);
    }
}
