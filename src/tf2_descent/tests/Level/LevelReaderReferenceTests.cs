using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace TF2.Descent.Tests.Level;

/// <summary>
/// TF2.Descent.Server reads levels with SourceSharp.MapFormats (WO1), as the AI's server does.
/// Both deploy into one bin/managed, where one copy of the package lands, so the version each
/// compiled against must be the same (src/tf2_descent/Directory.Build.props' DescentMapFormatsVersion
/// against src/ai/Directory.Build.props' MapFormatsVersion).
/// </summary>
public sealed class LevelReaderReferenceTests
{
    private static string Deployed => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "bin", "managed"));

    private static Version? MapFormatsVersionOf(string assembly)
    {
        using var stream = File.OpenRead(Path.Combine(Deployed, assembly + ".dll"));
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        foreach (AssemblyReferenceHandle h in metadata.AssemblyReferences)
        {
            AssemblyReference reference = metadata.GetAssemblyReference(h);
            if (metadata.GetString(reference.Name) == "SourceSharp.MapFormats")
                return reference.Version;
        }
        return null;
    }

    [Fact]
    public void The_descent_server_reads_levels_with_the_map_formats_version_the_ai_deploys()
    {
        Version? descent = MapFormatsVersionOf("TF2.Descent.Server");
        Version? ai = MapFormatsVersionOf("SourceSharp.Ai.Server");

        Assert.NotNull(descent);   // the stimulus: the server really references the reader
        Assert.NotNull(ai);
        Assert.Equal(ai, descent);
    }
}
