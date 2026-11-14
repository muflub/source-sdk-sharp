using SourceSharp.Ai.Replay;
using SourceSharp.Ai.TF2.Jump;
using Xunit;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The pass's jumps as links a route search takes (lane G5c-2a): a link a jump, named by its place
/// in the set, and on the game's jump map the routes to the ledges no walk reaches go by them.
/// </summary>
public class JumpLinkSetTests
{
    private static readonly NavLinkCosts Costs = new() { RunSpeed = 240f };

    [JumpMapFact]
    public void EachJumpIsALinkNamedByItsPlace()
    {
        JumpPlacesMapTests.Passed p = JumpPlacesMapTests.Shared.Value;
        IReadOnlyList<JumpLink> jumps = p.Result.Links;
        Assert.True(jumps.Count > 1, "the control: the pass found jumps");
        JumpLinkSet set = JumpLinkSet.Of(p.Graph, jumps, Costs);
        Assert.Equal(jumps.Count, set.Links.Count);
        for (int i = 0; i < jumps.Count; i++)
        {
            NavLink l = set.Links[i];
            Assert.Equal((jumps[i].From, jumps[i].To, NavLinkKind.Blast, jumps[i].Seconds, jumps[i].Health, jumps[i].Robustness, i), (l.From, l.To, l.Kind, l.Seconds, l.Health, l.Robustness, l.Id));
            Assert.Equal(jumps[i], set.JumpOf(i));
            Assert.Equal(Costs.Cost(l), set.Links.CostOf(i));
        }

        Assert.True(set.Links.IsOver(p.Graph));
    }

    [JumpMapFact]
    public void OnTheJumpMapALedgeNoWalkReachesIsReachedByItsLinks()
    {
        JumpPlacesMapTests.Passed p = JumpPlacesMapTests.Shared.Value;
        JumpLinkSet set = JumpLinkSet.Of(p.Graph, p.Result.Links, Costs);
        IReadOnlyList<RoutePlace> places = JumpRoutesTool.CtfPlaces(JumpRoutesTool.ReadEntities(File.ReadAllBytes(Path.ChangeExtension(JumpRecording.NavPath, ".bsp"))), p.Graph);
        Assert.NotEmpty(places);
        int ledges = 0;
        foreach (int landing in p.Result.Links.Select(l => l.To).Distinct())
        {
            var at = new RoutePlace("landing", p.Graph.Cell(landing).Centre, landing);
            foreach (RoutePlace from in places)
            {
                RouteChange r = JumpRoutesTool.Route(p.Graph, set, from, at, Costs.RunSpeed);
                if (r.WalkSeconds is not null)
                    continue;
                ledges++;
                Assert.True(r.LinkSeconds is not null, $"from {from.Name} to the landing {landing}: no way even with the links");
                Assert.NotEmpty(r.Links);
                // And the walk goes on from where its last link lands to the landing (it may land on another cell of the ledge).
                var last = new RoutePlace("last", p.Graph.Cell(set.Links[r.Links[^1]].To).Centre, set.Links[r.Links[^1]].To);
                Assert.NotNull(JumpRoutesTool.Route(p.Graph, set, last, at, Costs.RunSpeed).WalkSeconds);
            }
        }

        Assert.True(ledges > 0, "the control: some landing has no way to it on foot");
    }
}
