using Xunit;

namespace TF2.Tests;

/// <summary>
/// The tf2 leg runs: a trivial fact so the leg is non-empty from the first
/// commit and `make test` can give it a declared floor.
/// </summary>
public sealed class Tf2TestLegRuns
{
    [Fact]
    public void TheLegRuns() => Assert.True(true);
}
