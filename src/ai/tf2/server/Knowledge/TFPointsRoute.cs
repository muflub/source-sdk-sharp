using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Entities;
using TF2.Server.Rules;

namespace SourceSharp.Ai.TF2.Knowledge;

/// <summary>
/// <c>/ai/points</c> (lane Gc): a control-point level as a measurement reads it, one sample a GET. The objectives as
/// the bots' HUD feed has them (<see cref="TFObjectiveFeed"/>: owner, state, the teams that may capture now, the
/// setup), the capture areas' volumes with the point each names (the placements), and per bot its team, whether it is
/// alive, where it stands, the branch and verdict of its last decide, its stance on such a level and how often that
/// changed, and (the second pass) what it does with its team: gathering or pushing, the rally it holds (its tick, caller
/// and place), the push it is in (the call's tick and the objective), and its counts of rallies and pushes. Read under each brain's lock, as the other routes read the deciders; a brain busy deciding is
/// <c>read: false</c>. For the measure of how fast bots reach the objective (lane Gobj), the round machine's state and
/// setup and the server's tick: the server's own, read for the measurement only (no bot reads them).
/// </summary>
[SideService(Side.Server)]
public sealed class TFPointsRoute(AiBots bots, ServerAiSession ai, ITFLevelEntities placed, TFObjectiveFeed feed, ITFRoundFacts round, IEngineFrame frame,
                                  AiNav nav) : IDebugRouteProvider, IOrderedHook
{
    public const string Route = "/ai/points";

    /// <summary>Among the dev API's route providers: after <see cref="TFCourseRoute"/>.</summary>
    public int HookOrder => ModStages.First + 931;

    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("GET", Route, "a control-point level now (lane Gc): the objectives as the HUD feed has them (owner, state, teams that may capture, setup), the capture areas' volumes and points, and per bot its team, life, place, last branch and verdict, stance and stance changes, its rally and push (second pass) and their counts, with the third pass's (alone to the points, the points after a push, pushes rejoined from the dead, jumps in a push); the round machine's state and setup and the tick, for the measure (lane Gobj)"),
    ];

    private readonly record struct BotNow(int Team, bool Alive, AiVector At, string Branch, string Verdict, string Stance, int Changes, int PointsDecides, int TeamDecides,
                                         int Pulls, int PointsJumps, int TeamJumps, string Play, int RallyTick, int RallyCaller, int[] Rally, int PushTick, int PushTarget,
                                         int[] Counts, bool HasGoal = false, AiVector Goal = default);

    private static BotNow Read(TFDecider d)
    {
        SelfState self = d.LastSelf;
        (int rallyTick, int caller) = d.RallyId;
        AiVector r = d.RallyPlace ?? default;
        return new BotNow(self.Team, self.Alive, self.Origin, d.Doing.Branch ?? "", d.Doing.Verdict ?? "", d.StanceName, d.StanceChanges, d.PointsStanceDecides,
                           d.TeamStanceDecides, d.StancePulls, d.PointsStanceJumps, d.TeamStanceJumps, d.TeamPlay.ToString().ToLowerInvariant(), rallyTick, caller,
                           [(int)MathF.Round(r.X), (int)MathF.Round(r.Y), (int)MathF.Round(r.Z)], d.PushCallTick, d.PushTarget,
                           [d.RalliesCalled, d.RallyJoins, d.PushesCalled, d.PushesBegun, d.PushesTaken, d.PushesLost, d.PushesTimedOut,
                            d.AloneToPoints, d.PointsAfterPush, d.PushRejoins, d.PushJumps], d.Doing.HasGoal, d.Doing.Goal);
    }

    /// <summary>The corners of a route this route reports: one cell in this many.</summary>
    public const int RouteEvery = 8;

    /// <summary>
    /// <c>?routes=1</c> (lane Gobj, measurement only): the way from <paramref name="at"/> to <paramref name="goal"/> by a field
    /// built on the bot's own obstacles (its team's closed cells, its reach, links and hazard: what its searches see), and by
    /// one on the level's shared obstacles (the shortest walk, as <c>/ai/flow</c> has it): each one's cost, walked length,
    /// cells and corners. Null with no navigation, or either end off it.
    /// </summary>
    private object? RouteOf(EntityHandle player, AiVector at, AiVector goal)
    {
        if (nav.Current is not { } loaded || nav.Obstacles is not { } shared)
            return null;
        NavGraph g = loaded.Nav.Graph;
        CellId from = NavPoints.CellNear(g, at, ClearanceClass.Stand), to = NavPoints.CellNear(g, goal, ClearanceClass.Stand);
        if (!from.IsValid || !to.IsValid)
            return null;
        NavObstacleState? own = nav.ObstaclesFor(player)?.Clone();
        return new { own = own is null ? null : Way(g, from, to, own), shared = Way(g, from, to, shared.Clone()) };
    }

    private static object Way(NavGraph g, CellId from, CellId to, NavObstacleState obstacles)
    {
        var field = new FlowField();
        field.Build(g, to, ClearanceClass.Stand, obstacles);
        float cost = field.CostAt(from.Index), length = 0f;
        var corners = new List<int[]>();
        int cells = 0;
        CellId c = from;
        AiVector last = g.Cell(c).Centre;
        while (c.IsValid && c != to && cells < 20000)
        {
            AiVector here = g.Cell(c).Centre;
            length += here.DistanceTo(last);
            last = here;
            if (cells % RouteEvery == 0)
                corners.Add([(int)MathF.Round(here.X), (int)MathF.Round(here.Y), (int)MathF.Round(here.Z)]);
            c = field.NextFrom(c.Index);
            cells++;
        }

        return new { cost = float.IsFinite(cost) ? Math.Round(cost) : -1, length = Math.Round(length), cells, corners };
    }

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query)
    {
        var rows = new List<object>();
        foreach (AiBot b in bots.Bots)
        {
            bool read = ai.Session.TryReadBrain(b.Id.Slot, br => br.Decider is TFDecider d ? Read(d) : default, out BotNow n);
            rows.Add(new
            {
                slot = b.Id.Slot, name = b.Name, read, team = n.Team, alive = n.Alive,
                at = new[] { Math.Round(n.At.X), Math.Round(n.At.Y), Math.Round(n.At.Z) },
                branch = n.Branch ?? "", verdict = n.Verdict ?? "", stance = n.Stance ?? "", stance_changes = n.Changes,
                points_decides = n.PointsDecides, team_decides = n.TeamDecides, pulls = n.Pulls, points_jumps = n.PointsJumps, team_jumps = n.TeamJumps,
                play = n.Play ?? "", rally_tick = n.RallyTick, rally_caller = n.RallyCaller, rally = n.Rally ?? [], push_tick = n.PushTick, push_target = n.PushTarget,
                rallies_called = n.Counts?[0] ?? 0, rally_joins = n.Counts?[1] ?? 0, pushes_called = n.Counts?[2] ?? 0, pushes_begun = n.Counts?[3] ?? 0,
                pushes_taken = n.Counts?[4] ?? 0, pushes_lost = n.Counts?[5] ?? 0, pushes_timed_out = n.Counts?[6] ?? 0,
                alone_to_points = n.Counts?[7] ?? 0, points_after_push = n.Counts?[8] ?? 0, push_rejoins = n.Counts?[9] ?? 0, push_jumps = n.Counts?[10] ?? 0,
                goal = n.HasGoal ? new[] { Math.Round(n.Goal.X), Math.Round(n.Goal.Y), Math.Round(n.Goal.Z) } : null,
                route = query.GetValueOrDefault("routes") == "1" && read && n.Alive && n.HasGoal ? RouteOf(bots.Applier.PlayerOf(b.Id), n.At, n.Goal) : null,
            });
        }

        var areas = new List<object>();
        foreach (MapEntity e in placed.Read().All)
        {
            if (e.ClassName == TriggerAreaCapture.MapClass && e.Mins is { } mins && e.Maxs is { } maxs)
            {
                areas.Add(new
                {
                    point = e.Get(TFCourseEnds.PointKey) ?? "",
                    mins = new[] { Math.Round(mins.X), Math.Round(mins.Y), Math.Round(mins.Z) },
                    maxs = new[] { Math.Round(maxs.X), Math.Round(maxs.Y), Math.Round(maxs.Z) },
                });
            }
        }

        var objectives = feed.Current.All.Select(o => new
        {
            id = o.Id, kind = o.Kind.ToString(), owner = o.OwnerTeam, state = o.State.ToString(), may = o.MayAct, setup = o.Setup,
            progress = Math.Round(o.Progress, 3), at = new[] { Math.Round(o.Position.X), Math.Round(o.Position.Y), Math.Round(o.Position.Z) },
        }).ToArray();
        var roundNow = new { state = round.State.ToString(), setup = round.InSetup, setup_left = Math.Round(round.SetupSecondsLeft, 1), tick = frame.TickCount() };
        return new DebugRouteReply(200, AiJson.Write(new { round = roundNow, objectives, areas, bots = rows }));
    }
}
