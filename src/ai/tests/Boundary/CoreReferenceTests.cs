using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

namespace SourceSharp.Ai.Tests.Boundary;

/// <summary>
/// The core can name neither the engine nor a mod (plan_hl2mp_ai §1.3, §4.4).
/// tools/ai-mutations adds an ABI reference and an HL2MP string to the core
/// and sees these go red.
/// </summary>
public class CoreReferenceTests
{
    private static readonly Assembly Core = typeof(AiVector).Assembly;

    private static string[] References() => Core.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();

    private static bool IsFramework(string name)
        => name is "netstandard" or "mscorlib" || name.StartsWith("System", StringComparison.Ordinal)
           || name.StartsWith("Microsoft.", StringComparison.Ordinal);

    [Fact]
    public void TheCoreReferencesTheFramework()
        // The positive arm: the read sees references at all.
        => Assert.Contains("System.Runtime", References());

    [Fact]
    public void TheCoreReferencesNothingButTheFramework()
        => Assert.DoesNotContain(References(), n => !IsFramework(n));

    /// <summary>Every type name, string literal and constant in an assembly that names <paramref name="needle"/>.</summary>
    private static List<string> Naming(Assembly assembly, string needle)
    {
        var hits = new List<string>();
        using var pe = new PEReader(File.OpenRead(assembly.Location));
        MetadataReader md = pe.GetMetadataReader();

        foreach (TypeDefinitionHandle h in md.TypeDefinitions)
        {
            TypeDefinition t = md.GetTypeDefinition(h);
            string name = md.GetString(t.Namespace) + "." + md.GetString(t.Name);
            if (name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                hits.Add("type " + name);
        }

        UserStringHandle us = md.GetHeapSize(HeapIndex.UserString) > 1 ? MetadataTokens.UserStringHandle(1) : default;
        while (!us.IsNil)
        {
            string s = md.GetUserString(us);
            if (s.Contains(needle, StringComparison.OrdinalIgnoreCase))
                hits.Add("string " + s);
            us = md.GetNextHandle(us);
        }

        foreach (FieldDefinitionHandle h in md.FieldDefinitions)
        {
            FieldDefinition f = md.GetFieldDefinition(h);
            if (md.GetString(f.Name).Contains(needle, StringComparison.OrdinalIgnoreCase))
                hits.Add("field " + md.GetString(f.Name));
            if (!f.GetDefaultValue().IsNil)
            {
                Constant c = md.GetConstant(f.GetDefaultValue());
                if (c.TypeCode == ConstantTypeCode.String
                    && md.GetBlobReader(c.Value).ReadUTF16(md.GetBlobReader(c.Value).Length).Contains(needle, StringComparison.OrdinalIgnoreCase))
                    hits.Add("constant " + md.GetString(f.Name));
            }
        }

        return hits;
    }

    // The literal the control below finds in this very assembly.
    private const string ModName = "HL2MP";

    [Fact]
    public void TheNameScanFindsALiteral()
        => Assert.NotEmpty(Naming(typeof(CoreReferenceTests).Assembly, ModName));

    [Fact]
    public void TheCoreNamesNoMod()
        => Assert.Empty(Naming(Core, ModName));
}
