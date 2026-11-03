using System.Reflection;

using SourceSharp.Abi;

using TF2.Shared.Buildings;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// Every networked state the TF2 mod declares fits the largest standard
/// entity tier: native's tier list stops at 2048 bytes (generic_nettable.h,
/// SourceSharp_Tiers, "2048 is the largest that fits MAX_DATATABLE_PROPS"),
/// and a state past it is refused at bind on a live map
/// (BlobTooShortException), never at build or test time.
/// </summary>
public sealed class TFNetStateTierTests
{
    private const int LargestTierBytes = 2048;

    [Fact]
    public void Every_TF2_standalone_state_fits_the_largest_tier()
    {
        var sizes = typeof(TFBuildingResourceNetState).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<StandaloneNetStateAttribute>() is not null)
            .Select(t => (t.Name, Size: ((StandaloneNetState)t.GetField("NetLayout", BindingFlags.Public | BindingFlags.Static)!
                                                              .GetValue(null)!).Size))
            .ToArray();

        // The stimulus: the player and building resources at least are read.
        Assert.True(sizes.Length >= 2, $"found {sizes.Length} standalone state(s)");
        var over = sizes.Where(s => s.Size > LargestTierBytes).Select(s => $"{s.Name} {s.Size}b").ToArray();
        Assert.True(over.Length == 0, "past the 2048-byte tier: " + string.Join(", ", over));
    }
}
