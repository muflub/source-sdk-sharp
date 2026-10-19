using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The particle structs of sourcesharp.h against their managed mirrors: the
/// native executor and the managed manager read each other's by layout. Every field is one word or three, and each has a
/// plausible neighbour of the same width, so a one-sided insertion would shift
/// everything after it and compile on both sides. The rule is AbiParityTests':
/// names equal, or differing by exactly the XYZ / PYR suffix of a three-float.
/// </summary>
public class ParticleAbiParityTests
{
    private static List<(string Name, int Words)> HeaderFields(string structName)
    {
        string path = RepoLayout.Repo("src", "host", "native", "sourcesharp.h");
        Assert.True(File.Exists(path), "the ABI header is missing: " + path);
        string text = File.ReadAllText(path);

        int end = text.IndexOf($"}} {structName};", StringComparison.Ordinal);
        Assert.True(end > 0, $"the header declares {structName}");
        int start = text.LastIndexOf("typedef struct", end, StringComparison.Ordinal);
        string body = Regex.Replace(text[start..end], @"//[^\n]*", string.Empty);

        var fields = new List<(string, int)>();
        foreach (Match m in Regex.Matches(body, @"\b(float|int32_t|uint32_t|uint64_t)\s+(\w+)\s*(?:\[\s*(\d+)\s*\])?\s*;"))
        {
            int words = m.Groups[1].Value == "uint64_t" ? 2 : 1;
            fields.Add((m.Groups[2].Value, words * (m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 1)));
        }
        return fields;
    }

    // A C++ struct of iparticlelib.h (`struct X { ... };`): a pointer is two
    // words, and the Hungarian prefix (p, fl, b, n, v) is not part of the name.
    private static List<(string Name, int Words)> LibraryFields(string structName)
    {
        string path = RepoLayout.Repo("src", "client", "native", "particles_lib", "include", "ss_particles", "iparticlelib.h");
        Assert.True(File.Exists(path), "the library header is missing: " + path);
        string text = File.ReadAllText(path);

        int start = text.IndexOf($"struct {structName}\n{{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"iparticlelib.h declares {structName}");
        int end = text.IndexOf("};", start, StringComparison.Ordinal);
        string body = Regex.Replace(text[start..end], @"//[^\n]*", string.Empty);

        var fields = new List<(string, int)>();
        foreach (Match m in Regex.Matches(body, @"\b(float|int32_t|\w+\s*\*)\s*(\w+)\s*(?:\[\s*(\d+)\s*\])?\s*;"))
        {
            int words = m.Groups[1].Value.EndsWith('*') ? 2 : 1;
            string name = Regex.Replace(m.Groups[2].Value, "^(p|fl|b|n|v)(?=[A-Z])", string.Empty);
            fields.Add((name, words * (m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 1)));
        }
        return fields;
    }

    private static List<(string Name, int Words)> ManagedFields(Type type)
        => type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
               .OrderBy(f => f.MetadataToken)
               .Select(f => (f.Name, Words(f.FieldType)))
               .ToList();

    private static int Words(Type t)
        => t == typeof(Vector) ? 3
           : t == typeof(Matrix3x4Raw) ? 12
           : t == typeof(Matrix4x4Raw) ? 16
           : t == typeof(ulong) || t == typeof(long) ? 2
           : 1;

    private static bool SameField((string Name, int Words) native, (string Name, int Words) managed)
    {
        if (native.Words != managed.Words)
            return false;
        if (native.Name == managed.Name)
            return true;
        if (native.Words != 3 || !native.Name.StartsWith(managed.Name, StringComparison.Ordinal))
            return false;
        return native.Name[managed.Name.Length..] is "XYZ" or "PYR";
    }

    private static void Match(string structName, Type managedType, int managedSize, int minFields = 4)
        => Match(structName, HeaderFields(structName), managedType, managedSize, minFields);

    private static void Match(string structName, List<(string Name, int Words)> native, Type managedType, int managedSize, int minFields)
    {
        var managed = ManagedFields(managedType);

        Assert.True(native.Count >= minFields && native.Count == managed.Count,
            $"{structName}: field counts (native {native.Count}, managed {managed.Count})");
        for (int i = 0; i < native.Count; i++)
        {
            Assert.True(SameField(native[i], managed[i]),
                $"{structName} field {i}: native '{native[i].Name}' x{native[i].Words}, managed '{managed[i].Name}' x{managed[i].Words}");
        }

        int words = managed.Sum(f => f.Words);
        Assert.True(managedSize == words * 4, $"{managedType.Name} is {managedSize} bytes; its fields are {words * 4}");
    }

    // C7 particles phase 2.3: the old-style manager's buffers.
    [Fact]
    public void TheFrameHeaderMatchesTheHeader()
        => Match("SourceSharpParticleFrameHeader_t", typeof(ParticleFrameHeader), Unsafe.SizeOf<ParticleFrameHeader>());

    [Fact]
    public void TheFrameInputMatchesTheHeader()
        => Match("SourceSharpParticleFrameIn_t", typeof(ParticleFrameIn), Unsafe.SizeOf<ParticleFrameIn>());

    [Fact]
    public void TheCommitMatchesTheHeader()
        => Match("SourceSharpParticleCommit_t", typeof(ParticleCommit), Unsafe.SizeOf<ParticleCommit>());

    [Fact]
    public void TheDrawInputMatchesTheHeader()
        => Match("SourceSharpParticleDrawIn_t", typeof(ParticleDrawIn), Unsafe.SizeOf<ParticleDrawIn>());

    [Fact]
    public void TheDrawOutputMatchesTheHeader()
        => Match("SourceSharpParticleDrawOut_t", typeof(ParticleDrawOut), Unsafe.SizeOf<ParticleDrawOut>());

    [Fact]
    public void TheFollowMatchesTheHeader()
        => Match("SourceSharpParticleFollow_t", typeof(ParticleFollow), Unsafe.SizeOf<ParticleFollow>());

    [Fact]
    public void TheQuadStructMatchesTheHeader()
        => Match("SourceSharpParticleQuad_t", typeof(ParticleQuadRaw), Unsafe.SizeOf<ParticleQuadRaw>());

    [Fact]
    public void TheSystemInfoStructMatchesTheHeader()
        => Match("SourceSharpParticleSystemInfo_t", typeof(ParticleSystemInfo), Unsafe.SizeOf<ParticleSystemInfo>());

    [Fact]
    public void TheWorldTextViewMatchesTheHeader()
        => Match("SourceSharpWorldTextView_t", typeof(WorldTextView), Unsafe.SizeOf<WorldTextView>());

    [Fact]
    public void TheWorldTextVertexMatchesTheHeader()
        => Match("SourceSharpWorldTextVertex_t", typeof(WorldTextVertexRaw), Unsafe.SizeOf<WorldTextVertexRaw>());

    // C7 particles phase 3: the new-style effects' buffers, the managed-built
    // area's (IManagedParticlesClient::NewEffectsFrame and NewEffectDraw).
    [Fact]
    public void TheNewEffectsFrameHeaderMatchesTheHeader()
        => Match("SourceSharpNewParticleFrameHeader_t", typeof(NewParticleFrameHeader), Unsafe.SizeOf<NewParticleFrameHeader>());

    [Fact]
    public void TheNewEffectInputMatchesTheHeader()
        => Match("SourceSharpNewParticleIn_t", typeof(NewParticleIn), Unsafe.SizeOf<NewParticleIn>());

    [Fact]
    public void TheNewEffectCommitMatchesTheHeader()
        => Match("SourceSharpNewParticleCommit_t", typeof(NewParticleCommit), Unsafe.SizeOf<NewParticleCommit>(), minFields: 2);

    [Fact]
    public void TheNewEffectDrawInputMatchesTheHeader()
        => Match("SourceSharpNewParticleDrawIn_t", typeof(NewParticleDrawInRaw), Unsafe.SizeOf<NewParticleDrawInRaw>());

    [Fact]
    public void TheNewEffectDrawOutputMatchesTheHeader()
        => Match("SourceSharpNewParticleDrawOut_t", typeof(NewParticleDrawOutRaw), Unsafe.SizeOf<NewParticleDrawOutRaw>());

    // And the library's batch (iparticlelib.h), which managed builds.
    [Fact]
    public void TheSimEntryMatchesTheLibraryHeader()
        => Match("SSParticleSimEntry_t", LibraryFields("SSParticleSimEntry_t"), typeof(ParticleSimEntry), Unsafe.SizeOf<ParticleSimEntry>(), 3);

    [Fact]
    public void TheSimResultMatchesTheLibraryHeader()
        => Match("SSParticleSimResult_t", LibraryFields("SSParticleSimResult_t"), typeof(ParticleSimResult), Unsafe.SizeOf<ParticleSimResult>(), 4);

    // The width rules the facts above rest on, shown on the two wide types.
    [Fact]
    public void AUint64IsTwoWordsAndAFloatSixteenIsAMatrix()
    {
        var header = HeaderFields("SourceSharpNewParticleFrameHeader_t");
        Assert.Contains(("WorldToScreen", 16), header);
        var input = HeaderFields("SourceSharpNewParticleIn_t");
        Assert.Contains(("Collection", 2), input);
        Assert.Equal(16, Words(typeof(Matrix4x4Raw)));
    }
}
