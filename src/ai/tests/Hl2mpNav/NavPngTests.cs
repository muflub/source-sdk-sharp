using SkiaSharp;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Replay;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// <c>tools/ai-replay nav</c>'s picture (plan_hl2mp_ai §7.1, §7.3): the
/// course's graph drawn from above matches its committed golden pixel for
/// pixel, and the pixels say what the cells are (a standing floor green, the
/// crawlspace amber). <c>AI_GOLDEN_WRITE=1</c> rewrites the golden.
/// </summary>
public sealed class NavPngTests
{
    private const float Scale = 0.5f;

    private static readonly Lazy<DerivedNav> Course = new(() => NavGraphFromNav3d.Derive(
        new Nav3dNavSource(NavFixtures.Course().Open()), Hl2mpNavNumbers.Stock(56f), NavEdgeCosts.Default, []));

    private static string GoldenPath => RepoLayout.Repo("src", "ai", "tests", "goldens", "course-nav.png");

    /// <summary>The pixel over world point (x, y) in a picture drawn at <see cref="Scale"/> (the course spans 0..768 by 0..512).</summary>
    private static SKColor At(SKBitmap b, float x, float y)
        => b.GetPixel(NavPng.Margin + (int)(x * Scale), b.Height - NavPng.Margin - 1 - (int)(y * Scale));

    [Fact]
    public void TheCoursesPictureIsItsGolden()
    {
        byte[] png = NavPng.Render(Course.Value, Scale);
        if (Environment.GetEnvironmentVariable("AI_GOLDEN_WRITE") == "1")
            File.WriteAllBytes(GoldenPath, png);

        Assert.True(File.Exists(GoldenPath), $"no golden at {GoldenPath}: AI_GOLDEN_WRITE=1 writes it");
        using SKBitmap actual = SKBitmap.Decode(png), golden = SKBitmap.Decode(GoldenPath);
        Assert.Equal((golden.Width, golden.Height), (actual.Width, actual.Height));
        int differ = 0;
        for (int y = 0; y < actual.Height; y++)
        {
            for (int x = 0; x < actual.Width; x++)
            {
                if (actual.GetPixel(x, y) != golden.GetPixel(x, y))
                    differ++;
            }
        }

        Assert.Equal(0, differ);
    }

    [Fact]
    public void ThePixelsSayWhatTheCellsAre()
    {
        using SKBitmap b = NavPng.Draw(Course.Value, Scale);
        SKColor floor = At(b, 40, 40), crawl = At(b, 440, 192);
        Assert.True(floor.Green > floor.Red && floor.Green > floor.Blue, $"the floor is {floor}");
        Assert.True(crawl.Red > crawl.Blue && crawl.Green > crawl.Blue, $"the crawlspace is {crawl}");
        Assert.NotEqual(floor, crawl);
    }
}
