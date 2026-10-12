using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="VCollideLayout"/> is read in place over the engine's
/// <c>vcollide_t</c>. engine_vcollide_layout_oracle_test pins the pristine
/// header's <c>pKeyValues</c> at offset 16 and the size at 24; these hold the
/// managed struct to the same two numbers.
/// </summary>
public class VCollideLayoutTests
{
    [Fact]
    public void TheKeyValueTextPointerIsWhereTheHeaderPutsIt()
        => Assert.Equal(VCollideLayout.KeyValuesOffset,
                        (int)Marshal.OffsetOf<VCollideLayout>(nameof(VCollideLayout.KeyValues)));

    [Fact]
    public void TheStructIsTheHeadersSize()
        => Assert.Equal(24, Unsafe.SizeOf<VCollideLayout>());

    [Fact]
    public void TheSolidsPointerFollowsTheTwoShortsAfterPadding()
        => Assert.Equal(8, (int)Marshal.OffsetOf<VCollideLayout>(nameof(VCollideLayout.Solids)));
}
