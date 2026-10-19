using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_shim_areas_split phase 10: the rule the split built, held over IL in
/// the style of <see cref="StaticStateCensusTests"/>. What counts as the
/// engine's wrapper, and as a reference to it, is <see cref="SeamCensus"/>'s.
///
/// <para>
/// <b>Logic reaches the engine only through seams.</b> No logic assembly (the
/// server's and the client's game code, the UI and the mods) references
/// <c>EngineSide</c>, <c>ModuleEngine</c>, an area class, a forwarder struct or
/// a seam implementation, except the two composition roots, which build the
/// implementations over their own engine and register them.
/// </para>
///
/// <para>
/// <b>Tests hold implementations as their interfaces.</b> A test of an
/// implementation reaches it through its side's registration
/// (<c>SideBindings.Of</c>, a root's <c>Services</c>); a test of logic takes a
/// fake of the seam. The second rule is held per test project, beside each
/// project's own facts.
/// </para>
/// </summary>
public class SeamCensusTests
{
    /// <summary>The logic assemblies, by file, beside this test's own binaries.</summary>
    private static readonly string[] Logic =
    [
        "SourceSharp.Server", "SourceSharp.Client", "SourceSharp.Client.Ui",
        "SourceSharp.Client.Ui.Toolkit", "SourceSharp.Mods",
    ];

    /// <summary>The composition roots, the only logic types that may name the engine.</summary>
    private static readonly string[] Roots = ["SourceSharp.Server.ServerModule", "SourceSharp.Client.ClientModule"];

    private static string Dir => Path.GetDirectoryName(typeof(SourceSharp.Abi.ILog).Assembly.Location)!;

    private static string PathOf(string assembly)
    {
        string path = Path.Combine(Dir, assembly + ".dll");
        Assert.True(File.Exists(path), $"{assembly}.dll is beside the tests: a census of a missing assembly proves nothing");
        return path;
    }

    [Fact]
    public void TheCensusFindsEachKindOfTheEnginesWrapper()
    {
        var wrapper = SeamCensus.WrapperTypes(Dir);
        string KindOf(string asm, string name) => wrapper.TryGetValue((asm, name), out string? k) ? k : "(none)";

        Assert.Equal("wrapper", KindOf("SourceSharp.Server.Interop", "SourceSharp.Abi.EngineSide"));
        Assert.Equal("wrapper", KindOf("SourceSharp.Client.Interop", "SourceSharp.Abi.ModuleEngine"));
        Assert.Equal("area", KindOf("SourceSharp.Abi", "SourceSharp.Abi.UiClientArea"));
        Assert.Equal("forwarder", KindOf("SourceSharp.Abi", "SourceSharp.Abi.ShimUiClientFwd"));
        Assert.StartsWith("implementation of SourceSharp.Abi.IEngineEntities",
                          KindOf("SourceSharp.Server.Interop", "SourceSharp.Abi.EngineEntities"), StringComparison.Ordinal);
        Assert.Matches(@"^implementation of SourceSharp\.[\w.]+\.IUiSurface$",
                       KindOf("SourceSharp.Client.Interop", "SourceSharp.Client.Interop.ClientUiSurface"));

        // A stateless null object reaches no engine: it is no implementation.
        Assert.Equal("(none)", KindOf("SourceSharp.Abi", "SourceSharp.Abi.StandardErrorLog"));
        Assert.Equal("(none)", KindOf("SourceSharp.Server.Interop", "SourceSharp.Abi.NoRenderPolicyBlocks"));

        Assert.True(wrapper.Values.Count(k => k == "area") >= 50, "every generated area is found");
        Assert.True(wrapper.Values.Count(k => k.StartsWith("implementation", StringComparison.Ordinal)) >= 60,
                    "the implementations of both sides are found");
    }

    [Fact]
    public void NoLogicAssemblyNamesTheEnginesWrapperOutsideTheRoots()
    {
        var wrapper = SeamCensus.WrapperTypes(Dir);
        var hits = Logic.SelectMany(a => SeamCensus.References(PathOf(a), wrapper).Select(h => (Asm: a, Hit: h))).ToList();

        // The positive arm: the walk reaches the roots' own references, so an
        // empty list below is the rule holding, not the walk seeing nothing.
        foreach (string root in Roots)
            Assert.True(hits.Any(x => x.Hit.Owner == root && x.Hit.Type == "SourceSharp.Abi.ModuleEngine"),
                        $"the census sees {root} name ModuleEngine");

        var stray = hits.Where(x => !Roots.Contains(x.Hit.Owner))
                        .Select(x => $"{x.Asm}: {x.Hit.Owner} -> {x.Hit.Type} ({x.Hit.Kind}, {x.Hit.Via})")
                        .Distinct()
                        .OrderBy(x => x, StringComparer.Ordinal)
                        .ToList();
        Assert.Empty(Listed(stray));
    }

    [Fact]
    public void TheCrossSideTestsHoldNoSeamImplementation()
        => Assert.Empty(Listed(SeamCensus.HeldImplementations(Dir, SeamCensus.Sides.Both, typeof(SeamCensusTests).Assembly.Location,
                                                       PathOf("SourceSharp.TestSupport.Server"))));

    /// <summary>The list, or a failure that prints every entry of it.</summary>
    private static List<string> Listed(List<string> found)
    {
        Assert.True(found.Count == 0, $"{found.Count}:\n" + string.Join("\n", found));
        return found;
    }
}
