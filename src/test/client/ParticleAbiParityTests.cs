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
        foreach (Match m in Regex.Matches(body, @"\b(float|int32_t|uint32_t)\s+(\w+)\s*(?:\[\s*(\d+)\s*\])?\s*;"))
            fields.Add((m.Groups[2].Value, m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 1));
        return fields;
    }

    private static List<(string Name, int Words)> ManagedFields(Type type)
        => type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
               .OrderBy(f => f.MetadataToken)
               .Select(f => (f.Name, f.FieldType == typeof(Vector) ? 3 : f.FieldType == typeof(Matrix3x4Raw) ? 12 : 1))
               .ToList();

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

    private static void Match(string structName, Type managedType, int managedSize)
    {
        var native = HeaderFields(structName);
        var managed = ManagedFields(managedType);

        Assert.True(native.Count >= 4 && native.Count == managed.Count,
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
}
