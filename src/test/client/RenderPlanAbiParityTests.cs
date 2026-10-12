using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The render plan's ABI (plan_client_novalve C7): the structs in sourcesharp.h
/// (<c>SourceSharpRender*_t</c>, <c>SourceSharpViewSetup_t</c>,
/// <c>SourceSharpFogParams_t</c>) against the managed ones read and written by
/// layout, field for field, nested structs and fixed arrays flattened; and the
/// three lists both sides number -- the op kinds, the render targets and the
/// convar slots -- against shell_view_executor_client.cpp's. Every field is a
/// word, so a one-sided insertion shifts everything after it and still compiles.
/// </summary>
public class RenderPlanAbiParityTests
{
    private static string Header()
    {
        string path = RepoLayout.Repo("src", "host", "native", "sourcesharp.h");
        Assert.True(File.Exists(path), "the ABI header is missing: " + path);
        return File.ReadAllText(path);
    }

    private static string Executor()
    {
        string path = RepoLayout.Repo("src", "client", "native", "shell_view_executor_client.cpp");
        Assert.True(File.Exists(path), "the executor is missing: " + path);
        return File.ReadAllText(path);
    }

    private static List<(string Name, int Words)> HeaderFields(string text, string structName, string prefix = "")
    {
        int end = text.IndexOf($"}} {structName};", StringComparison.Ordinal);
        Assert.True(end > 0, $"the header declares {structName}");
        int start = text.LastIndexOf("typedef struct", end, StringComparison.Ordinal);
        string body = Regex.Replace(text[start..end], @"//[^\n]*", string.Empty);
        var fields = new List<(string, int)>();
        foreach (Match m in Regex.Matches(body, @"\b(float|int32_t|uint32_t|SourceSharp\w+_t)\s+(\w+)\s*(?:\[\s*(\d+)\s*\])?\s*;"))
        {
            string type = m.Groups[1].Value, name = prefix + m.Groups[2].Value;
            if (type.StartsWith("SourceSharp", StringComparison.Ordinal))
                fields.AddRange(HeaderFields(text, type, name + "."));
            else
                fields.Add((name, m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 1));
        }
        return fields;
    }

    private static List<(string Name, int Words)> ManagedFields(Type type, string prefix = "")
    {
        var fields = new List<(string, int)>();
        foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.MetadataToken))
        {
            string name = prefix + f.Name;
            if (f.GetCustomAttribute<FixedBufferAttribute>() is { } fixedBuffer)
                fields.Add((name, fixedBuffer.Length));
            else if (f.FieldType == typeof(int) || f.FieldType == typeof(uint) || f.FieldType == typeof(float))
                fields.Add((name, 1));
            else
                fields.AddRange(ManagedFields(f.FieldType, name + "."));
        }
        return fields;
    }

    private static void Match(string structName, Type managed, int size)
    {
        var native = HeaderFields(Header(), structName);
        var fields = ManagedFields(managed);
        Assert.True(native.Count >= 4 && native.Count == fields.Count,
            $"{structName}: field counts (native {native.Count}, managed {fields.Count})");
        for (int i = 0; i < native.Count; i++)
            Assert.True(native[i] == fields[i], $"{structName} field {i}: native {native[i]}, managed {fields[i]}");
        Assert.Equal(native.Sum(f => f.Words) * 4, size);
    }

    [Fact]
    public void TheFrameInStructMatchesTheHeader()
        => Match("SourceSharpRenderFrameIn_t", typeof(RenderFrameIn), Unsafe.SizeOf<RenderFrameIn>());

    [Fact]
    public void TheFrameOutStructMatchesTheHeader()
        => Match("SourceSharpRenderFrameOut_t", typeof(RenderFrameOut), Unsafe.SizeOf<RenderFrameOut>());

    [Fact]
    public void TheSetUpInStructMatchesTheHeader()
        => Match("SourceSharpViewSetUpIn_t", typeof(ViewSetUpIn), Unsafe.SizeOf<ViewSetUpIn>());

    [Fact]
    public void TheSetUpOutStructMatchesTheHeader()
        => Match("SourceSharpViewSetUpOut_t", typeof(ViewSetUpOut), Unsafe.SizeOf<ViewSetUpOut>());

    [Fact]
    public void TheViewSetupStructMatchesTheHeader()
        => Match("SourceSharpViewSetup_t", typeof(ViewSetup), Unsafe.SizeOf<ViewSetup>());

    [Fact]
    public void TheFogStructMatchesTheHeader()
        => Match("SourceSharpFogParams_t", typeof(RenderFogParams), Unsafe.SizeOf<RenderFogParams>());

    [Fact]
    public void TheInputsStructMatchesTheHeader()
        => Match("SourceSharpRenderInputs_t", typeof(RenderInputs), Unsafe.SizeOf<RenderInputs>());

    [Fact]
    public void ThePostInputsStructMatchesTheHeader()
        => Match("SourceSharpPostInputs_t", typeof(PostProcessInputs), Unsafe.SizeOf<PostProcessInputs>());

    [Fact]
    public void TheOpStructMatchesTheHeader()
        => Match("SourceSharpRenderOp_t", typeof(RenderOp), Unsafe.SizeOf<RenderOp>());

    [Fact]
    public void TheSceneStructMatchesTheHeader()
        => Match("SourceSharpViewScene_t", typeof(ViewScene), Unsafe.SizeOf<ViewScene>());

    [Fact]
    public void TheSceneLeafStructMatchesTheHeader()
        => Match("SourceSharpViewSceneLeaf_t", typeof(ViewSceneLeaf), Unsafe.SizeOf<ViewSceneLeaf>());

    [Fact]
    public void TheSceneEntryStructMatchesTheHeader()
        => Match("SourceSharpViewSceneEntry_t", typeof(ViewSceneEntry), Unsafe.SizeOf<ViewSceneEntry>());

    private static Dictionary<string, int> NativeEnum(string prefix)
    {
        var values = Regex.Matches(Executor(), $@"\b{prefix}(\w+)\s*=\s*(\d+)\s*,")
                          .ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
        Assert.True(values.Count > 0, $"the executor names no {prefix} values");
        return values;
    }

    private static void SameEnum<T>(string prefix) where T : struct, Enum
    {
        var native = NativeEnum(prefix);
        Assert.Equal(Enum.GetValues<T>().Length, native.Count);
        foreach (T v in Enum.GetValues<T>())
        {
            Assert.True(native.TryGetValue(v.ToString(), out int n) && n == Convert.ToInt32(v),
                $"{typeof(T).Name}.{v} = {Convert.ToInt32(v)}; the executor has {(native.TryGetValue(v.ToString(), out int m) ? m : -1)}");
        }
    }

    [Fact]
    public void TheOpKindsAreTheExecutorsCases() => SameEnum<RenderOpKind>("RENDER_OP_");

    [Fact]
    public void TheRenderTargetsAreTheExecutors() => SameEnum<RenderTarget>("RENDER_TARGET_");

    [Fact]
    public void EveryOpKindHasACaseInTheExecutor()
    {
        string text = Executor();
        foreach (RenderOpKind kind in Enum.GetValues<RenderOpKind>().Where(k => k != RenderOpKind.None))
            Assert.True(text.Contains($"case RENDER_OP_{kind}:", StringComparison.Ordinal), $"the executor has no case for {kind}");
    }

    [Fact]
    public void TheConvarSlotsAreTheExecutorsTable()
    {
        string text = Executor();
        int start = text.IndexOf("const char *const kRenderCvars[] =", StringComparison.Ordinal);
        Assert.True(start > 0, "the executor has no kRenderCvars table");
        int end = text.IndexOf("};", start, StringComparison.Ordinal);
        var names = Regex.Matches(text[start..end], "\"(\\w+)\"").Select(m => m.Groups[1].Value).ToList();

        var managed = Enum.GetNames<RenderCvar>().Where(n => n != nameof(RenderCvar.Count)).ToList();
        // The two fog colours are three managed slots each, after the table.
        int colours = managed.Count(n => n.StartsWith("fog_color", StringComparison.Ordinal));
        Assert.Equal(6, colours);
        Assert.Equal(managed.Take(managed.Count - colours), names);
        Assert.True((int)RenderCvar.Count <= 64, "RenderInputs.Cvars holds 64");
    }

    [Fact]
    public void ThePostConvarSlotsAreTheExecutorsTable()
    {
        string text = Executor();
        int start = text.IndexOf("const char *const kPostCvars[] =", StringComparison.Ordinal);
        Assert.True(start > 0, "the executor has no kPostCvars table");
        int end = text.IndexOf("};", start, StringComparison.Ordinal);
        var names = Regex.Matches(text[start..end], "\"(\\w+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(Enum.GetNames<PostCvar>().Where(n => n != nameof(PostCvar.Count)), names);
        Assert.True((int)PostCvar.Count <= 64, "PostProcessInputs.Cvars holds 64");
    }
}
