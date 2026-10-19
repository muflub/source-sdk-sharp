using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The TF2 mod's assemblies stand on the core alone: neither on TF2Descent
/// (which builds on TF2, never the other way) nor on the host SDK (TF2 ships
/// no hosting code, and the host packages are the very thing that keeps
/// TF2.Descent's tests in their own project). Read from the deployed dll
/// metadata, as <c>DeployedReferencesTests</c> does for Descent: the reference
/// list of the built assembly is what bootstrap and the game would see.
/// </summary>
public sealed class Tf2AssemblyStructureTests
{
    private static string Deployed =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "bin", "managed"));

    private static string[] ReferencesOf(string assembly)
    {
        string file = Path.Combine(Deployed, assembly + ".dll");
        Assert.True(File.Exists(file), $"{assembly} is not deployed in {Deployed}");

        using var stream = File.OpenRead(file);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return metadata.AssemblyReferences
            .Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name))
            .ToArray();
    }

    [Theory]
    [InlineData("TF2.Server")]
    [InlineData("TF2.Client")]
    public void Tf2_builds_on_the_core_alone(string assembly)
    {
        var refs = ReferencesOf(assembly);

        // An assembly with no code yet lists only the runtime: the compiler
        // trims an assembly reference nothing uses. So the positive arm here
        // is that the deployed dll exists and is read (ReferencesOf refuses
        // it missing), and the fail-first arm for the negative assertions was
        // shown by mutation (stage 0.1 commit message): TF2.Server referencing
        // and *using* TF2.Descent.Rules turns the first fact red.
        Assert.DoesNotContain(refs, r => r.StartsWith("TF2.Descent", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("SourceSharp.Host", StringComparison.Ordinal));
    }
}
