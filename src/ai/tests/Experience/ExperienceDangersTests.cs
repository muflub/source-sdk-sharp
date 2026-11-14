using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// Where a side keeps being killed from (lane G4b, <see cref="ExperienceDangers"/>): for each bin its players died in, the place
/// most of the deaths around it came from, when one place made at least two of them and two fifths, and stands off.
/// </summary>
public sealed class ExperienceDangersTests
{
    private const int Red = 2, Blue = 3;
    private static readonly ExperienceKey Key = new("tf_dangers", "0123456789abcdef", "ctf");
    private static readonly WorldBin Door = new(10, 10, 0);
    private static readonly WorldBin Window = new(10, 20, 1);

    private static MapExperience Rows(Action<ExperienceDelta> add)
    {
        var rows = new ExperienceDelta();
        add(rows);
        return new MapExperience(Key, 1, rows);
    }

    private static void Death(ExperienceDelta rows, WorldBin victim, WorldBin killer, double count = 1d, int role = Red, int weapon = 2, int cls = 3)
        => rows.Add(new SightKey(new ExperienceContext(0, role, cls), victim, killer, weapon), new Tally(count));

    [Fact]
    public void ThePlaceMostDeathsCameFromIsTheBinsDangerPlace()
    {
        MapExperience m = Rows(r =>
        {
            Death(r, Door, Window, 2d);
            Death(r, Door, Window, 1d, weapon: 5, cls: 7);
            Death(r, Door, new WorldBin(30, 10, 0), 1d);
        });
        DangerPlace place = ExperienceDangers.Of(m, Red).At(Door)!.Value;
        Assert.Equal(Window, place.Killer);
        Assert.Equal(3d, place.Deaths, 6);
        Assert.Equal(0.75d, place.Share, 6);
        Assert.Equal(Window.Centre, place.At);
    }

    [Fact]
    public void OneDeathIsNoPlace()
    {
        ExperienceDangers d = ExperienceDangers.Of(Rows(r => Death(r, Door, Window, 1.5d)), Red);
        Assert.Null(d.At(Door));
        // The control: two are.
        Assert.NotNull(ExperienceDangers.Of(Rows(r => Death(r, Door, Window, 2d)), Red).At(Door));
    }

    [Fact]
    public void DeathFromAllRoundIsNoPlace()
    {
        // Three places, two deaths each: none has two fifths.
        MapExperience m = Rows(r =>
        {
            Death(r, Door, Window, 2d);
            Death(r, Door, new WorldBin(30, 10, 0), 2d);
            Death(r, Door, new WorldBin(10, -10, 0), 2d);
        });
        Assert.Null(ExperienceDangers.Of(m, Red).At(Door));
        // The control: two of them leave one with half.
        Assert.NotNull(ExperienceDangers.Of(Rows(r =>
        {
            Death(r, Door, Window, 2d);
            Death(r, Door, new WorldBin(30, 10, 0), 2d);
        }), Red).At(Door));
    }

    [Fact]
    public void AKillerAtArmsLengthIsNoPlace()
    {
        Assert.Null(ExperienceDangers.Of(Rows(r => Death(r, Door, new WorldBin(12, 10, 0), 5d)), Red).At(Door));
        // The control: five bins off is.
        Assert.NotNull(ExperienceDangers.Of(Rows(r => Death(r, Door, new WorldBin(15, 10, 0), 5d)), Red).At(Door));
    }

    [Fact]
    public void AnotherSidesDeathsAreNotRead()
    {
        MapExperience m = Rows(r => Death(r, Door, Window, 4d, role: Blue));
        Assert.True(ExperienceDangers.Of(m, Red).IsEmpty);
        Assert.Equal(1, ExperienceDangers.Of(m, Blue).Count);
    }

    /// <summary>A doorway's deaths spread over the bins around it, and the shots that made them over the bins of one window: they add up.</summary>
    [Fact]
    public void DeathsAndShotsSpreadOverNeighbouringBinsAddUp()
    {
        MapExperience m = Rows(r =>
        {
            Death(r, Door, Window, 1d);
            Death(r, new WorldBin(11, 10, 0), new WorldBin(11, 20, 1), 1d);
        });
        DangerPlace place = ExperienceDangers.Of(m, Red).At(Door)!.Value;
        Assert.Equal(2d, place.Deaths, 6);
        // The point is between the two killer bins.
        Assert.Equal((Window.Centre.X + new WorldBin(11, 20, 1).Centre.X) / 2f, place.At.X, 3);
        // A bin two away is not the same area: each alone has one.
        MapExperience apart = Rows(r =>
        {
            Death(r, Door, Window, 1d);
            Death(r, new WorldBin(12, 10, 0), new WorldBin(13, 20, 1), 1d);
        });
        Assert.True(ExperienceDangers.Of(apart, Red).IsEmpty);
    }

    [Fact]
    public void AnEmptySnapshotHasNoPlace() => Assert.True(ExperienceDangers.Of(MapExperience.Empty, Red).IsEmpty);
}
