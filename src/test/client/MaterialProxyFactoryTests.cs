using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client.MaterialProxies;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The proxy names native registers (shell_material_proxy_client.cpp's
/// <c>SS_PROXY_NAMES</c>) are the names managed answers
/// (<see cref="MaterialProxyFactory"/>), and together they are every name the
/// C0 snapshot of client.so's registry held that cluster 1 owns: a name native
/// registers and managed does not answer would refuse every material that asks
/// for it, and a name the engine could ask for that neither has would be a
/// K1 regression.
/// </summary>
public class MaterialProxyFactoryTests
{
    internal static List<string> NativeNames()
    {
        string path = RepoLayout.Repo("src", "client", "native", "shell_material_proxy_client.cpp");
        Assert.True(File.Exists(path), "the trampoline is missing: " + path);
        string text = File.ReadAllText(path);
        int start = text.IndexOf("#define SS_PROXY_NAMES( X )", StringComparison.Ordinal);
        Assert.True(start >= 0, "the trampoline defines SS_PROXY_NAMES");
        int end = text.IndexOf("\n\n", start, StringComparison.Ordinal);
        var names = Regex.Matches(text[start..end], @"X\(\s*(\d+)\s*,\s*(\w+)\s*\)")
                         .Select(m => (Index: int.Parse(m.Groups[1].Value), Name: m.Groups[2].Value))
                         .ToList();
        Assert.Equal(Enumerable.Range(0, names.Count), names.Select(n => n.Index));
        return names.Select(n => n.Name).ToList();
    }

    private static MaterialProxyFactory Factory() => new(new StandardErrorLog(), new StockProxyMath(), () => false);

    [Fact]
    public void NativeRegistersExactlyTheNamesManagedAnswers()
    {
        List<string> native = NativeNames();
        var managed = Factory().Names.ToHashSet(StringComparer.Ordinal);

        // The cluster 1 names, viewpostprocess.cpp's MotionBlur and engine_post (C7), particle_proxies.cpp's ParticleSphereProxy.
        Assert.True(native.Count == 47 && native.Distinct().Count() == 47, $"47 distinct names in the trampoline, not {native.Count}");
        string[] nativeOnly = native.Where(n => !managed.Contains(n)).ToArray();
        string[] managedOnly = managed.Where(n => !native.Contains(n)).ToArray();
        Assert.True(nativeOnly.Length == 0, "registered by native, answered by nobody: " + string.Join(", ", nativeOnly));
        Assert.True(managedOnly.Length == 0, "answered by managed, never registered: " + string.Join(", ", managedOnly));
    }

    [Fact]
    public void EveryClusterOneNameOfTheC0RegistryIsStillAnswered()
    {
        string path = RepoLayout.Repo("docs", "novalve-snapshot", "client-c0", "client-interfaces.txt");
        Assert.True(File.Exists(path), "the C0 registry snapshot is missing: " + path);
        var c0 = File.ReadAllLines(path)
                     .Where(l => l.EndsWith("_IMaterialProxy003", StringComparison.Ordinal))
                     .Select(l => l[..^"_IMaterialProxy003".Length])
                     .Where(n => !MaterialProxyOracleTests.OtherLanes.Contains(n))
                     .ToList();
        List<string> native = NativeNames();

        Assert.True(c0.Count == 44, $"the C0 registry held 44 cluster 1 proxy names, not {c0.Count}");
        string[] lost = c0.Where(n => !native.Contains(n)).ToArray();
        Assert.True(lost.Length == 0, "in the C0 registry and no longer registered: " + string.Join(", ", lost));
    }

    [Fact]
    public void AnUnknownNameMakesNoProxy()
        => Assert.Null(Factory().Create("PlayerProximity"));
}
