using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// The static sight table (plan_hl2mp_ai §6.2): a voxel ray through the
/// file's free space between standing eyes, no engine trace. On the course,
/// the wall at x 608-624 hides one side from the other except through its
/// door gap, open floor sees open floor, and nothing beyond 1,024 units is
/// seen.
/// </summary>
public sealed class NavSightTests
{
    private static readonly Lazy<(DerivedNav Nav, NavSight Sight)> Built = new(() =>
    {
        Nav3dReader reader = NavFixtures.Course().Open();
        DerivedNav nav = NavGraphFromNav3d.Derive(new Nav3dNavSource(reader), Hl2mpNavNumbers.Stock(56f), NavEdgeCosts.Default, []);
        return (nav, NavSight.Build(nav.Graph, new Nav3dVoxelSpace(reader)));
    });

    private static int Cell(float x, float y, float z) => Built.Value.Nav.Graph.FindCell(new AiVector(x, y, z)).Index;

    private static bool Sees(int a, int b) => Built.Value.Sight.Visible(a, b);

    [Fact]
    public void OpenFloorSeesOpenFloor() => Assert.True(Sees(Cell(40, 320, 16), Cell(560, 320, 16)));

    [Fact]
    public void TheWallHidesOneSideFromTheOther() => Assert.False(Sees(Cell(560, 40, 16), Cell(700, 40, 16)));

    [Fact]
    public void EverySightAcrossTheWallLooksThroughTheDoorGap()
    {
        NavSight sight = Built.Value.Sight;
        int across = 0;
        for (int a = 0; a < sight.NodeCount; a++)
        {
            for (int b = 0; b < sight.NodeCount; b++)
            {
                AiVector ea = sight.Eye(a), eb = sight.Eye(b);
                if (ea.X >= 600f || eb.X <= 632f || !SeesNodes(sight, a, b))
                    continue;
                float t = (616f - ea.X) / (eb.X - ea.X), y = ea.Y + (eb.Y - ea.Y) * t;
                Assert.InRange(y, 224f, 288f);
                across++;
            }
        }

        Assert.True(across > 0, "nothing is seen through the door gap");
    }

    private static bool SeesNodes(NavSight sight, int a, int b)
    {
        DerivedNav nav = Built.Value.Nav;
        int ca = Enumerable.Range(0, nav.Graph.CellCount).First(c => sight.NodeOf(c) == a);
        int cb = Enumerable.Range(0, nav.Graph.CellCount).First(c => sight.NodeOf(c) == b);
        return sight.Visible(ca, cb);
    }

    [Fact]
    public void SightIsTheSameBothWays()
    {
        (DerivedNav nav, NavSight sight) = Built.Value;
        for (int a = 0; a < nav.Graph.CellCount; a += 97)
        {
            for (int b = 0; b < nav.Graph.CellCount; b += 89)
                Assert.Equal(sight.Visible(a, b), sight.Visible(b, a));
        }
    }

    [Fact]
    public void TheTableHoldsPairsAndSeesSomeAndNotOthers()
    {
        (DerivedNav nav, NavSight sight) = Built.Value;
        int seen = 0, hidden = 0;
        for (int a = 0; a < nav.Graph.CellCount; a += 7)
        {
            for (int b = 0; b < nav.Graph.CellCount; b += 11)
            {
                if (sight.Visible(a, b))
                    seen++;
                else
                    hidden++;
            }
        }

        Assert.True(sight.Pairs > 100 && seen > 0 && hidden > 0, $"{sight.Pairs} pairs, {seen} seen, {hidden} hidden");
    }
}
