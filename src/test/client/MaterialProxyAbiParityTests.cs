using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The material proxy trampoline's five structs (sourcesharp.h,
/// <c>SourceSharpProxy*_t</c>) against the managed ones the binding reads and
/// writes by layout (lane c5-proxies). Every field is a word, a pointer or
/// three floats, and each has a plausible neighbour of the same width, so a
/// one-sided insertion shifts everything after it and compiles on both sides.
/// The rule is AbiParityTests': names equal, or differing by exactly the XYZ
/// suffix of a three-float; widths equal in words; the managed size is the
/// sum of its fields.
/// </summary>
public class MaterialProxyAbiParityTests
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
        foreach (Match m in Regex.Matches(body, @"\b(?:const\s+)?(float|int32_t|uint32_t|char|void)\s*(\*?)\s*(\w+)\s*(?:\[\s*(\d+)\s*\])?\s*;"))
        {
            int words = m.Groups[2].Value == "*" ? 2 : 1;
            if (m.Groups[4].Success)
                words *= int.Parse(m.Groups[4].Value);
            fields.Add((m.Groups[3].Value, words));
        }
        return fields;
    }

    private static int Words(Type t)
    {
        if (t == typeof(Vector))
            return 3;
        if (t.IsPointer || t == typeof(nint) || t == typeof(nuint))
            return 2;
        return Marshal.SizeOf(t) / 4;
    }

    private static List<(string Name, int Words)> ManagedFields(Type type)
        => type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
               .OrderBy(f => f.MetadataToken)
               .Select(f => (f.Name, Words(f.FieldType)))
               .ToList();

    private static bool SameField((string Name, int Words) native, (string Name, int Words) managed)
    {
        if (native.Words != managed.Words)
            return false;
        if (native.Name == managed.Name)
            return true;
        return native.Words == 3 && native.Name == managed.Name + "XYZ";
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

    [Fact]
    public void TheKeyStructMatchesTheHeader()
        => Match("SourceSharpProxyKey_t", typeof(ProxyKeyRaw), Unsafe.SizeOf<ProxyKeyRaw>());

    [Fact]
    public void TheVarStructMatchesTheHeader()
        => Match("SourceSharpProxyVar_t", typeof(ProxyVar), Unsafe.SizeOf<ProxyVar>());

    [Fact]
    public void TheBindInStructMatchesTheHeader()
        => Match("SourceSharpProxyBindIn_t", typeof(ProxyBindIn), Unsafe.SizeOf<ProxyBindIn>());

    [Fact]
    public void TheOpStructMatchesTheHeader()
        => Match("SourceSharpProxyOp_t", typeof(ProxyOp), Unsafe.SizeOf<ProxyOp>());

    [Fact]
    public void TheEntityStructMatchesTheHeader()
        => Match("SourceSharpProxyEntity_t", typeof(ProxyEntityRaw), Unsafe.SizeOf<ProxyEntityRaw>());

    [Fact]
    public void TheOpKindsAreTheTrampolinesCases()
    {
        // shell_material_proxy_client.cpp's kProxyOp* enum, by name and number.
        string path = RepoLayout.Repo("src", "client", "native", "shell_material_proxy_client.cpp");
        Assert.True(File.Exists(path), "the trampoline is missing: " + path);
        var native = Regex.Matches(File.ReadAllText(path), @"\bkProxyOp(\w+)\s*=\s*(\d+)\s*,")
                          .ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));

        Assert.True(native.Count == Enum.GetValues<ProxyOpKind>().Length && native.Count == 8,
            $"the trampoline names {native.Count} op kinds, ProxyOpKind {Enum.GetValues<ProxyOpKind>().Length}");
        foreach (ProxyOpKind kind in Enum.GetValues<ProxyOpKind>())
        {
            Assert.True(native.TryGetValue(kind.ToString(), out int n) && n == (int)kind,
                $"ProxyOpKind.{kind} = {(int)kind}; the trampoline has {(native.TryGetValue(kind.ToString(), out int m) ? m : -1)}");
        }
    }
}
