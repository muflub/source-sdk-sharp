using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_shim_areas_split phase 10, the second rule for the client's tests: no
/// fact constructs or holds a client seam implementation. A fact reaches one
/// through the client's registration (<c>ClientEntityFetches.AddTo</c>,
/// <c>SideBindings.Of</c>, the root's <c>Services</c>) and holds it as its
/// interface. The rule and its words are <see cref="SeamCensus"/>'s.
/// </summary>
public class SeamCensusClientTests
{
    [Fact]
    public void TheClientsTestsHoldNoSeamImplementation()
    {
        string dir = Path.GetDirectoryName(typeof(SourceSharp.Abi.ILog).Assembly.Location)!;
        Assert.Empty(Listed(SeamCensus.HeldImplementations(dir, SeamCensus.Sides.Client,
                                                    typeof(SeamCensusClientTests).Assembly.Location,
                                                    Path.Combine(dir, "SourceSharp.TestSupport.Client.dll"))));
    }

    /// <summary>The list, or a failure that prints every entry of it.</summary>
    private static List<string> Listed(List<string> found)
    {
        Assert.True(found.Count == 0, $"{found.Count}:\n" + string.Join("\n", found));
        return found;
    }
}
