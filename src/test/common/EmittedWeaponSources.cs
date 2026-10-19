using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SourceSharp.Tests;

/// <summary>
/// A MOD ASSEMBLY, BUILT BY THE FACT (Phase 0.4).
///
/// <para>
/// The catalogues order their sources by simple assembly name, ties kept in
/// registration order. A fact that declares its sources in the test assembly
/// tests only the tie-break: every source there has the same assembly name, so
/// the sort can neither take effect nor be seen to fail. This compiles a
/// source under an assembly NAME the fact chooses, loads it and hands back an
/// instance, so "WepOrd.Alpha's rows come before WepOrd.Zulu's whatever order
/// they were registered in" is something the catalogue can get wrong.
/// </para>
///
/// <para>
/// The source is compiled against ONE side's assembly (the caller passes
/// <c>typeof(WeaponType).Assembly</c> from the side it means), which is how the
/// same text builds the server's copy and the client's copy of one mod, the way
/// a mod's shared source compiles into both of its side assemblies. Only the
/// framework, that side and the assemblies it references are referenced, so
/// <c>SourceSharp.Shared.IWeaponTypeSource</c> names that side's copy even in a
/// test project that loads both sides.
/// </para>
/// </summary>
public static class EmittedWeaponSources
{
    /// <summary>
    /// Compile <paramref name="source"/> as assembly <paramref name="assemblyName"/>
    /// against <paramref name="sideAssembly"/>, and return a new instance of its
    /// type <paramref name="typeName"/> (namespace-qualified).
    /// </summary>
    public static T Instantiate<T>(string assemblyName, string typeName, string source, Assembly sideAssembly)
        where T : class
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            References(sideAssembly),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);

        if (!emitted.Success)
        {
            string errors = string.Join("\n", emitted.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException($"the emitted mod assembly '{assemblyName}' did not compile:\n{errors}");
        }

        // From BYTES: the server's and the client's copy of one mod carry the
        // same assembly name, and loading by name would hand back whichever
        // copy was loaded first.
        var assembly = Assembly.Load(stream.ToArray());
        var type = assembly.GetType(typeName)
            ?? throw new InvalidOperationException($"'{assemblyName}' has no type '{typeName}'");

        return (T)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"'{typeName}' could not be constructed"));
    }

    private static List<MetadataReference> References(Assembly sideAssembly)
    {
        // The shared framework: every trusted assembly that lives beside
        // System.Private.CoreLib. The test's own dependencies are also trusted,
        // and in a project that loads both sides they include the OTHER side's
        // copy of every shared type.
        string framework = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0 && Path.GetDirectoryName(path) == framework)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(sideAssembly.Location));

        // What the side's public types are declared with (SourceSharp.Abi's
        // DamageType and Vector in a WeaponType's constructor, the interop
        // assembly's ILog).
        foreach (var name in sideAssembly.GetReferencedAssemblies())
        {
            string location = Assembly.Load(name).Location;

            if (Path.GetDirectoryName(location) != framework)
                references.Add(MetadataReference.CreateFromFile(location));
        }

        return references;
    }
}
