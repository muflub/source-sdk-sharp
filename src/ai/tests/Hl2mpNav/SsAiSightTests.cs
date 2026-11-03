using SourceSharp.Ai.Hl2mp.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// ss_ai_sight (plan_hl2mp_ai §6.6): the per-tree map loads by its level id;
/// its door and crates are the graph's obstacles; the sight table sees
/// through the window and over the half cover and nowhere else through the
/// window wall. Skipped, visibly, in a tree that has not built it.
/// </summary>
public sealed class SsAiSightTests
{
    private const string Map = "ss_ai_sight";

    private static readonly Lazy<(LoadedNav Nav, NavSight Sight)> Loaded = new(() =>
    {
        NavLoadResult r = NavLoader.Load(Map, p => File.Exists(Path.Combine(PerTreeNav.MapsDir, Path.GetFileName(p)))
                                              ? File.ReadAllBytes(Path.Combine(PerTreeNav.MapsDir, Path.GetFileName(p))) : null, null);
        Assert.True(r.Nav is not null, r.Refusal);
        return (r.Nav!, NavSight.Build(r.Nav!.Nav.Graph, new Nav3dVoxelSpace(r.Nav.Reader)));
    });

    [PerTreeNavFact(Map)]
    public void TheDoorAndTheCratesAreObstacles()
        => Assert.Equal(["sight_crate_0 Physics", "sight_crate_1 Physics", "sight_door Door"],
                        Loaded.Value.Nav.Nav.Obstacles.Select(o => $"{o.Name} {o.Kind}").Order(StringComparer.Ordinal));

    [PerTreeNavFact(Map)]
    public void EverySightThroughTheWindowWallIsThroughTheWindow()
    {
        NavSight sight = Loaded.Value.Sight;
        DerivedNav nav = Loaded.Value.Nav.Nav;
        var cellOfNode = new int[sight.NodeCount];
        for (int c = 0; c < nav.Graph.CellCount; c++)
            cellOfNode[sight.NodeOf(c)] = c;
        int through = 0;
        for (int a = 0; a < sight.NodeCount; a++)
        {
            for (int b = 0; b < sight.NodeCount; b++)
            {
                AiVector ea = sight.Eye(a), eb = sight.Eye(b);
                // The window wall runs x 256..272 in the first room (x < 512).
                if (ea.X >= 248f || eb.X <= 280f || eb.X >= 500f || !sight.Visible(cellOfNode[a], cellOfNode[b]))
                    continue;
                float t = (264f - ea.X) / (eb.X - ea.X);
                float y = ea.Y + (eb.Y - ea.Y) * t, z = ea.Z + (eb.Z - ea.Z) * t;
                Assert.InRange(y, 192f, 256f);
                Assert.InRange(z, 56f, 104f);
                through++;
            }
        }

        Assert.True(through > 0, "nothing is seen through the window");
    }

    [PerTreeNavFact(Map)]
    public void HalfCoverDoesNotHideAStandingEye()
    {
        (LoadedNav n, NavSight sight) = Loaded.Value;
        NavGraph g = n.Nav.Graph;
        int west = g.FindCell(new AiVector(304, 416, 17)).Index, east = g.FindCell(new AiVector(456, 416, 17)).Index;
        Assert.True(west >= 0 && east >= 0);
        Assert.True(sight.Visible(west, east), $"eyes {sight.Eye(sight.NodeOf(west))} {sight.Eye(sight.NodeOf(east))}");
    }
}
