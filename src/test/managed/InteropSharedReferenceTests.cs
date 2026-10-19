extern alias client;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using client::SourceSharp.Client.Interop;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_side_isolation §9.1 (P7a): the Interop assemblies reference no
/// SourceSharp.Shared. Once Shared folds into Server and Client, an Interop
/// assembly naming a shared type would have to reference its own side's game
/// assembly, which already references it: a cycle. So the dependency is held
/// at zero by metadata, over the DLLs the build produced.
/// </summary>
public class InteropSharedReferenceTests
{
    private const string Shared = "SourceSharp.Shared";

    [Fact]
    public void ServerInteropReferencesNoSharedAssembly()
        => Assert.True(References(Built(typeof(UserMessagesBinding))).Count(n => n == Shared) == 0,
            "SourceSharp.Server.Interop references SourceSharp.Shared");

    [Fact]
    public void ClientInteropReferencesNoSharedAssembly()
        => Assert.True(References(Built(typeof(UserMessagesClientBinding))).Count(n => n == Shared) == 0,
            "SourceSharp.Client.Interop references SourceSharp.Shared");

    /// <summary>
    /// The planted control: an assembly that uses one type of an assembly named
    /// SourceSharp.Shared is seen to reference it, and one that does not use it
    /// is not, so a zero above is the instrument's answer and not its blindness.
    /// </summary>
    [Fact]
    public void APlantedUseOfTheSharedAssemblyIsSeen()
    {
        var platform = Platform();
        byte[] shared = Emit(Shared, "namespace P; public sealed class Planted { }", platform);
        var withShared = platform.Append(MetadataReference.CreateFromImage(shared)).ToArray();

        byte[] user = Emit("Synth.Interop", "namespace Q; public static class U { public static object M() => new P.Planted(); }", withShared);
        byte[] bystander = Emit("Synth.Bystander", "namespace Q; public static class U { public static object M() => new object(); }", withShared);

        Assert.True(References(user).Count(n => n == Shared) == 1, "a planted use of the shared assembly is seen");
        Assert.True(References(bystander).Count(n => n == Shared) == 0, "an assembly that uses nothing of it is not blamed");
    }

    private static byte[] Built(Type inAssembly)
    {
        string path = inAssembly.Assembly.Location;
        Assert.True(File.Exists(path), $"{inAssembly.Assembly.GetName().Name} has no file to read ('{path}')");
        return File.ReadAllBytes(path);
    }

    private static List<string> References(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image));
        var md = pe.GetMetadataReader();
        var names = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToList();
        Assert.True(names.Count > 0, "an assembly with no references at all was not read");
        return names;
    }

    private static MetadataReference[] Platform()
        => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0
                        && Path.GetDirectoryName(p) == Path.GetDirectoryName(typeof(object).Assembly.Location))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();

    private static byte[] Emit(string name, string source, MetadataReference[] refs)
    {
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, $"{name} compiles: " + string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return stream.ToArray();
    }
}
