using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The static engine's globals pointer is process-wide: a test that bound one
/// (GlobalVarsBindingTests bound a struct whose frame time is 0) and a test
/// that read through it (ShimFrameTests' GetFrameTime) raced in parallel
/// collections -- "Expected 0.015, Actual 0", 1 in 5 full suites under make
/// check. Both were serialised in ProcessWide for it.
///
/// <para>
/// plan_abi_hotreload D8 moved every such test onto an <c>EngineSide</c> of
/// its own, and both left ProcessWide. The fence is now that NO test names the
/// static pointer: a new one would bring the race back in a parallel
/// collection, and the way to bind a pointer is the test's own side.
/// </para>
/// </summary>
public class GlobalVarsSerialFenceTests
{
    private const string StaticGlobals = @"\bEngine\.(GlobalVarsPointer|BindGlobalVars)\b";

    [Fact]
    public void NoTestTouchesTheStaticGlobalsPointer()
    {
        // Every test project's sources (plan_side_isolation P6 split one into four).
        string[] roots = ["managed", "server", "client", "common"];
        foreach (string dir in roots)
            Assert.True(Directory.Exists(RepoLayout.Repo("src", "test", dir)), $"test sources at src/test/{dir}");

        int scanned = 0;
        int instanceBinders = 0;
        var touching = new List<string>();
        foreach (string file in roots.SelectMany(d => Directory.EnumerateFiles(RepoLayout.Repo("src", "test", d), "*.cs")))
        {
            if (Path.GetFileName(file) == "GlobalVarsSerialFenceTests.cs")
                continue;

            scanned++;
            string text = File.ReadAllText(file);
            if (text.Contains(".BindGlobalVars(", StringComparison.Ordinal))
                instanceBinders++;
            if (Regex.IsMatch(text, StaticGlobals))
                touching.Add(Path.GetFileNameWithoutExtension(file));
        }

        // The controls: the scan read the test tree, the needle matches the
        // static's spelling, and the files that do bind a pointer are there to
        // be seen (they bind it on their own side).
        Assert.True(scanned > 100, $"the scan read the test tree: {scanned} files");
        Assert.Matches(StaticGlobals, "Engine" + ".BindGlobalVars(Side.Server, p)");
        Assert.True(instanceBinders > 0, "the tests that bind a pointer bind it on their own side");
        Assert.Empty(touching);
    }
}
