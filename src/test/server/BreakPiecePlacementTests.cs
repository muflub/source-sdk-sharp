using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Where a break piece sits on the prop it came from: BreakModels.LocalOffset
/// and PlacementOrigin, the arithmetic break_piece_offset.h held natively
/// (these facts are its break_piece_offset_test, moved with it).
/// </summary>
public class BreakPiecePlacementTests
{
    private static readonly Vector BlockOffset = new(10f, 20f, 30f);
    private static readonly Vector ParentPlacement = new(1f, 2f, 3f);
    private static readonly Vector PiecePlacement = new(100f, 200f, 300f);

    [Fact]
    public void NeitherModelHasAPlacementAttachment()
        => Assert.Equal(new Vector(10f, 20f, 30f),
                        BreakModels.LocalOffset(BlockOffset, false, ParentPlacement, false, PiecePlacement));

    [Fact]
    public void OnlyTheParentRecentres()
        => Assert.Equal(new Vector(9f, 18f, 27f),
                        BreakModels.LocalOffset(BlockOffset, true, ParentPlacement, false, PiecePlacement));

    [Fact]
    public void OnlyThePieceRecentres()
        => Assert.Equal(new Vector(-90f, -180f, -270f),
                        BreakModels.LocalOffset(BlockOffset, false, ParentPlacement, true, PiecePlacement));

    [Fact]
    public void BothRecentreAndTheParentsShiftIsAddedBack()
        => Assert.Equal(new Vector(-89f, -178f, -267f),
                        BreakModels.LocalOffset(BlockOffset, true, ParentPlacement, true, PiecePlacement));

    [Fact]
    public void AZeroAttachmentIsNotTheSameAsNoAttachment()
    {
        Vector none = BreakModels.LocalOffset(BlockOffset, true, ParentPlacement, false, Vector.Zero);
        Vector zero = BreakModels.LocalOffset(BlockOffset, true, ParentPlacement, true, Vector.Zero);

        Assert.Equal(9f, none.X);
        Assert.Equal(11f, zero.X);
        Assert.NotEqual(none, zero);
    }

    [Fact]
    public void ThePlacementOriginIsTheDifferenceWhenBothHaveOne()
        => Assert.Equal(new Vector(99f, 198f, 297f),
                        BreakModels.PlacementOrigin(true, ParentPlacement, true, PiecePlacement));
}
