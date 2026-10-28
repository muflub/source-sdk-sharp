using SourceSharp.StockAb;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// tools/stock-ab's scoring: reading the engine's screenshots, which pixels
/// count, and the entity census.
/// </summary>
public class StockAbTests
{
    private static RgbImage Solid(int width, int height, byte r, byte g, byte b)
    {
        var image = RgbImage.Blank(width, height);
        for (int i = 0; i < image.Pixels.Length; i += 3)
        {
            image.Pixels[i] = r;
            image.Pixels[i + 1] = g;
            image.Pixels[i + 2] = b;
        }

        return image;
    }

    private static void Set(RgbImage image, int x, int y, byte r, byte g, byte b)
    {
        int i = ((y * image.Width) + x) * 3;
        image.Pixels[i] = r;
        image.Pixels[i + 1] = g;
        image.Pixels[i + 2] = b;
    }

    /// <summary>A 2x2 TGA: header, then BGR pixels from the bottom row up unless top-down.</summary>
    private static byte[] Tga(bool topDown)
    {
        var file = new byte[18 + 12];
        file[2] = 2;
        file[12] = 2;
        file[14] = 2;
        file[16] = 24;
        file[17] = (byte)(topDown ? 0x20 : 0);
        byte[] bgr = [3, 2, 1, 6, 5, 4, 9, 8, 7, 12, 11, 10];
        bgr.CopyTo(file, 18);
        return file;
    }

    [Fact]
    public void ABottomUpTgaIsReadTopRowFirstAsRgb()
    {
        var image = RgbImage.ReadTga(Tga(topDown: false));

        Assert.Equal(new byte[] { 7, 8, 9, 10, 11, 12, 1, 2, 3, 4, 5, 6 }, image.Pixels);
    }

    [Fact]
    public void ATopDownTgaKeepsItsRowOrder()
    {
        var image = RgbImage.ReadTga(Tga(topDown: true));

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, image.Pixels);
    }

    [Fact]
    public void ARunLengthTgaReadsTheSameAsAnUncompressedOne()
    {
        var file = new byte[18 + 1 + 3 + 1 + 6];
        file[2] = 10;
        file[12] = 3;
        file[14] = 1;
        file[16] = 24;
        file[17] = 0x20;
        // A run of two of one pixel, then a raw packet of one.
        byte[] packets = [0x81, 3, 2, 1, 0x00, 6, 5, 4];
        packets.CopyTo(file, 18);

        var image = RgbImage.ReadTga(file[..(18 + packets.Length)]);

        Assert.Equal(new byte[] { 1, 2, 3, 1, 2, 3, 4, 5, 6 }, image.Pixels);
    }

    [Fact]
    public void ATruncatedTgaIsRefused()
    {
        byte[] file = Tga(topDown: false);

        Assert.Throws<InvalidDataException>(() => RgbImage.ReadTga(file[..^1]));
    }

    [Fact]
    public void APngStartsWithItsSignatureAndEndsWithIend()
    {
        byte[] png = Solid(3, 2, 10, 20, 30).ToPng();

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        Assert.Equal("IEND"u8.ToArray(), png[^8..^4]);
    }

    [Fact]
    public void IdenticalShotsPassWithNothingDiffering()
    {
        var image = Solid(16, 16, 100, 100, 100);

        var score = ShotComparison.Score(image, image, image, image, new ComparisonSettings(), out _);

        Assert.Equal(Verdict.Pass, score.Verdict);
        Assert.Equal(0, score.DifferingPercent);
        Assert.Equal(100, score.StillPercent);
        Assert.Equal(1.0, score.LumaRatio, 6);
    }

    [Fact]
    public void AFrameTwiceAsBrightFailsWithItsLumaRatio()
    {
        var stock = Solid(16, 16, 50, 50, 50);
        var sharp = Solid(16, 16, 100, 100, 100);

        var score = ShotComparison.Score(stock, stock, sharp, sharp, new ComparisonSettings(), out _);

        Assert.Equal(Verdict.Fail, score.Verdict);
        Assert.Equal(100, score.DifferingPercent);
        Assert.Equal(2.0, score.LumaRatio, 6);
    }

    [Fact]
    public void ADifferenceWithinTheThresholdDoesNotCount()
    {
        var stock = Solid(16, 16, 100, 100, 100);
        var sharp = Solid(16, 16, 120, 100, 100);

        var score = ShotComparison.Score(stock, stock, sharp, sharp, new ComparisonSettings(Threshold: 24), out _);

        Assert.Equal(0, score.DifferingPercent);
    }

    [Fact]
    public void APixelThatMovedBetweenAGamesOwnShotsIsLeftOut()
    {
        var stockA = Solid(32, 32, 100, 100, 100);
        var stockB = Solid(32, 32, 100, 100, 100);
        var sharp = Solid(32, 32, 100, 100, 100);
        Set(stockB, 16, 16, 255, 0, 0);
        Set(sharp, 16, 16, 0, 255, 0);

        var score = ShotComparison.Score(stockA, stockB, sharp, sharp, new ComparisonSettings(DynamicDilation: 0), out _);

        Assert.Equal(0, score.DifferingPercent);
        Assert.Equal(100.0 * 1023 / 1024, score.StillPercent, 6);
    }

    [Fact]
    public void TheMovingMaskGrowsByTheDilation()
    {
        var still = Solid(32, 32, 100, 100, 100);
        var moved = Solid(32, 32, 100, 100, 100);
        Set(moved, 16, 16, 255, 0, 0);

        var score = ShotComparison.Score(still, moved, still, still, new ComparisonSettings(DynamicDilation: 2), out _);

        // A 5x5 square around the one pixel that moved.
        Assert.Equal(100.0 * (1024 - 25) / 1024, score.StillPercent, 6);
    }

    [Fact]
    public void AFrameThatMostlyMovedIsInconclusive()
    {
        var a = Solid(8, 8, 0, 0, 0);
        var b = Solid(8, 8, 255, 255, 255);

        var score = ShotComparison.Score(a, b, a, a, new ComparisonSettings(), out _);

        Assert.Equal(Verdict.Inconclusive, score.Verdict);
    }

    [Fact]
    public void TheHeatmapMarksADifferingPixelRed()
    {
        var stock = Solid(4, 4, 100, 100, 100);
        var sharp = Solid(4, 4, 100, 100, 100);
        Set(sharp, 1, 1, 250, 100, 100);

        ShotComparison.Score(stock, stock, sharp, sharp, new ComparisonSettings(DynamicDilation: 0), out var heatmap);

        int i = ((1 * 4) + 1) * 3;
        Assert.Equal(255, heatmap.Pixels[i]);
        Assert.True(heatmap.Pixels[i + 1] < 255, "a differing pixel is red, not white");
        Assert.Equal(heatmap.Pixels[0], heatmap.Pixels[1]);
    }

    [Fact]
    public void ScreenshotsOfDifferentSizesAreRefused()
    {
        var a = Solid(4, 4, 0, 0, 0);
        var b = Solid(4, 5, 0, 0, 0);

        Assert.Throws<ArgumentException>(() => ShotComparison.Score(a, a, b, b, new ComparisonSettings(), out _));
    }

    private const string StockDump =
        "0\tworldspawn\t\t0.000 0.000 0.000\t0 0 0\t0\t*0\t0\n"
        + "5\tprop_physics\tbarrel\t10.000 20.000 30.000\t0 0 0\t0\tx.mdl\t0\n"
        + "6\tlight\t\t1.000 1.000 1.000\t0 0 0\t0\t\t0\n"
        + "#end\tt1\n";

    private const string SharpAnswer =
        "{\"class\":\"*\",\"entities\":["
        + "{\"handle\":1,\"index\":0,\"classname\":\"worldspawn\",\"health\":0,\"origin\":[0,0,0],\"angles\":[0,0,0]},"
        + "{\"handle\":2,\"index\":5,\"classname\":\"ss_barrel\",\"health\":0,\"origin\":[10,20,30.5],\"angles\":[0,0,0]},"
        + "{\"handle\":3,\"index\":7,\"classname\":\"info_null\",\"health\":0,\"origin\":[0,0,0],\"angles\":[0,0,0]}"
        + "],\"count\":3}";

    [Fact]
    public void AStockDumpWithoutItsEndLineIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => EntityCensus.ReadStock(StockDump.Replace("#end\tt1\n", "")));
    }

    [Fact]
    public void ARenamedClassIsCountedAgainstWhatStockSpawnedInItsPlace()
    {
        var census = EntityCensus.Compare(
            EntityCensus.ReadStock(StockDump), EntityCensus.ReadSharp(SharpAnswer),
            new Dictionary<string, string> { ["ss_barrel"] = "prop_physics" });

        var props = Assert.Single(census, c => c.ClassName == "prop_physics");
        Assert.Equal(1, props.Stock);
        Assert.Equal(1, props.Sharp);
        Assert.Equal(0, props.Moved);
        Assert.DoesNotContain(census, c => c.ClassName == "ss_barrel");
    }

    [Fact]
    public void AClassOnlyOneGameHoldsIsListedWithZeroForTheOther()
    {
        var census = EntityCensus.Compare(
            EntityCensus.ReadStock(StockDump), EntityCensus.ReadSharp(SharpAnswer), new Dictionary<string, string>());

        Assert.Contains(census, c => c.ClassName == "light" && c.Stock == 1 && c.Sharp == 0);
        Assert.Contains(census, c => c.ClassName == "info_null" && c.Stock == 0 && c.Sharp == 1);
    }

    [Fact]
    public void AnEntityFurtherThanTheToleranceFromItsCounterpartHasMoved()
    {
        var stock = new List<LiveEntity> { new("prop_physics", 0, 0, 0), new("prop_physics", 100, 0, 0) };
        var sharp = new List<LiveEntity> { new("prop_physics", 0.5, 0, 0), new("prop_physics", 200, 0, 0) };

        var census = EntityCensus.Compare(stock, sharp, new Dictionary<string, string>(), tolerance: 1.0);

        Assert.Equal(1, Assert.Single(census).Moved);
    }

    [Fact]
    public void EachCounterpartIsMatchedOnce()
    {
        var stock = new List<LiveEntity> { new("light", 0, 0, 0), new("light", 0.2, 0, 0) };
        var sharp = new List<LiveEntity> { new("light", 0.1, 0, 0), new("light", 50, 0, 0) };

        var census = EntityCensus.Compare(stock, sharp, new Dictionary<string, string>(), tolerance: 1.0);

        Assert.Equal(1, Assert.Single(census).Moved);
    }
}
