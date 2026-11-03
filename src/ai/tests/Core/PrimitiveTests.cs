using Xunit;

namespace SourceSharp.Ai.Tests.Core;

public class PrimitiveTests
{
    [Fact]
    public void DefaultActorIsNone()
        => Assert.False(default(ActorId).IsValid);

    [Fact]
    public void TwoLivesInOneSlotAreDifferentActors()
        => Assert.NotEqual(new ActorId(3, 1), new ActorId(3, 2));

    [Fact]
    public void DefaultCellIsNone()
        => Assert.Equal((false, -1), (default(CellId).IsValid, default(CellId).Index));

    [Fact]
    public void CellZeroIsAValidCell()
        => Assert.Equal((true, 0), (new CellId(0).IsValid, new CellId(0).Index));

    [Fact]
    public void ANegativeCellIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new CellId(-1));

    [Fact]
    public void TickArithmeticCountsTicks()
        => Assert.Equal((new Tick(16), 6), (new Tick(10) + 6, new Tick(16) - new Tick(10)));

    [Fact]
    public void VectorLengthIsEuclidean()
        => Assert.Equal(5f, new AiVector(3, 0, 4).Length);

    [Fact]
    public void Length2DIgnoresHeight()
        => Assert.Equal(5f, new AiVector(3, 4, 100).Length2D);

    [Fact]
    public void ZeroNormalisesToZero()
        => Assert.Equal(AiVector.Zero, AiVector.Zero.Normalised());
}
