using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>A side's looks by bin (lane G4a): the most looked-along yaw over the pitches, its seconds and share, only of the rows the context takes.</summary>
public sealed class ExperienceLooksTests
{
    private static readonly ExperienceKey Key = new("tf_looks", "0123456789abcdef", "ctf");
    private static readonly WorldBin Bin = new(3, 4, 0);

    private static MapExperience Rows(Action<ExperienceDelta> add)
    {
        var rows = new ExperienceDelta();
        add(rows);
        return new MapExperience(Key, 1, rows);
    }

    [Fact]
    public void ABinsYawIsTheOneMostLookedAlongOverEveryPitch()
    {
        MapExperience m = Rows(r =>
        {
            r.Add(new LookKey(new ExperienceContext(0, 2, 3), Bin, 4, 0), new Tally(3d));
            r.Add(new LookKey(new ExperienceContext(0, 2, 3), Bin, 4, 2), new Tally(3d));
            r.Add(new LookKey(new ExperienceContext(0, 2, 5), Bin, 8, 1), new Tally(5d));
        });
        ExperienceLook look = ExperienceLooks.Of(m, new ExperienceContext(ExperienceContext.Any, 2, ExperienceContext.Any)).At(Bin)!.Value;
        Assert.Equal(90f, look.Yaw);
        Assert.Equal(11d, look.Seconds, 6);
        Assert.Equal(6d / 11d, look.Share, 6);
    }

    [Fact]
    public void AnotherSidesLooksAreNotRead()
    {
        MapExperience m = Rows(r => r.Add(new LookKey(new ExperienceContext(0, 3, 3), Bin, 4, 1), new Tally(9d)));
        ExperienceLooks red = ExperienceLooks.Of(m, new ExperienceContext(ExperienceContext.Any, 2, ExperienceContext.Any));
        Assert.True(red.IsEmpty);
        Assert.Null(red.At(Bin));
        Assert.Equal(1, ExperienceLooks.Of(m, new ExperienceContext(ExperienceContext.Any, 3, ExperienceContext.Any)).Count);
    }
}
