using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>Where a side's players held (lane G4b, <see cref="ExperienceHolds"/>): the hold rows of one kind and role summed by bin and yaw, each worth what holding there earned.</summary>
public sealed class ExperienceHoldsTests
{
    private const int Hold = 7, Red = 2, Blue = 3;
    private static readonly ExperienceKey Key = new("tf_holds", "0123456789abcdef", "ctf");
    private static readonly WorldBin Corner = new(10, 2, 0);

    private static MapExperience Rows(Action<ExperienceDelta> add)
    {
        var rows = new ExperienceDelta();
        add(rows);
        return new MapExperience(Key, 1, rows);
    }

    private static void Held(ExperienceDelta rows, WorldBin bin, SpotStat stat, int role = Red, int kind = Hold, int yaw = 4, int cls = 3)
        => rows.Add(new SpotKey(new ExperienceContext(0, role, cls), kind, bin, yaw), stat);

    [Fact]
    public void ASpotsRowsOfEveryClassAddUpAndItIsWorthItsSecondsByItsKillsOverItsDeaths()
    {
        MapExperience m = Rows(r =>
        {
            Held(r, Corner, new SpotStat(1d, 10d, 1d, 0d));
            Held(r, Corner, new SpotStat(1d, 20d, 2d, 2d), cls: 7);
        });
        HoldSpot spot = Assert.Single(ExperienceHolds.Of(m, Red, Hold).All);
        Assert.Equal(new SpotStat(2d, 30d, 3d, 2d), spot.Stat);
        Assert.Equal(30d * 4d / 3d, spot.Worth, 6);
        Assert.Equal(90f, spot.Yaw);
        Assert.Equal(Corner.Centre, spot.At);
    }

    [Fact]
    public void AnotherSidesOrAnotherKindsRowsAreNotRead()
    {
        MapExperience m = Rows(r =>
        {
            Held(r, Corner, new SpotStat(1d, 10d, 0d, 0d), role: Blue);
            Held(r, Corner, new SpotStat(1d, 10d, 0d, 0d), kind: 1);
        });
        Assert.True(ExperienceHolds.Of(m, Red, Hold).IsEmpty);
        Assert.Single(ExperienceHolds.Of(m, Blue, Hold).All);
    }

    [Fact]
    public void NearIsWithinTheRadius()
    {
        MapExperience m = Rows(r =>
        {
            Held(r, Corner, new SpotStat(1d, 10d, 0d, 0d));
            Held(r, new WorldBin(40, 2, 0), new SpotStat(1d, 10d, 0d, 0d));
        });
        ExperienceHolds h = ExperienceHolds.Of(m, Red, Hold);
        Assert.Equal(Corner, Assert.Single(h.Near(new AiVector(256f, 256f, 0f), 1024f)).Bin);
        Assert.Equal(2, h.Near(new AiVector(256f, 256f, 0f), 3000f).Count);
    }

    /// <summary>A spot worth nine times another is drawn about nine times in ten.</summary>
    [Fact]
    public void ASpotIsDrawnByItsWorth()
    {
        ExperienceHolds h = ExperienceHolds.Of(Rows(r =>
        {
            Held(r, Corner, new SpotStat(1d, 90d, 0d, 0d));
            Held(r, new WorldBin(12, 2, 0), new SpotStat(1d, 10d, 0d, 0d));
        }), Red, Hold);
        var random = new AiRandom(5);
        int corner = Enumerable.Range(0, 1000).Count(_ => ExperienceHolds.Draw(h.All, random)!.Value.Bin == Corner);
        Assert.InRange(corner, 860, 940);
    }

    [Fact]
    public void NothingIsDrawnFromNoSpot() => Assert.Null(ExperienceHolds.Draw([], new AiRandom(1)));
}
