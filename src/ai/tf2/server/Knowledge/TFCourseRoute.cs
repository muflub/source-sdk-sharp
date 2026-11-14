using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.TF2.Knowledge;

/// <summary>
/// <c>/ai/course</c> (lane Ge): the level's course ends as the bots know them (<see cref="TFCourseEnds"/>: each one's
/// title, rank and volume) and, per bot, what its Course branch made of them: the end it goes for, the ends it has
/// stood in this life, its entries over all lives, the lives that forgot them, the ends set aside, its quits, and
/// whether the course is finished. Read under each brain's lock, as the other routes read the deciders.
/// </summary>
[SideService(Side.Server)]
public sealed class TFCourseRoute(AiBots bots, ServerAiSession ai, ITFLevelEntities placed) : IDebugRouteProvider, IOrderedHook
{
    public const string Route = "/ai/course";

    /// <summary>Among the dev API's route providers: after TF2's experience routes.</summary>
    public int HookOrder => ModStages.First + 926;

    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("GET", Route, "the level's course ends (capture areas whose point the level does not place, lane Ge): each one's index, title, rank and volume; and per bot the end it goes for, the ends it stood in this life, its entries over all lives, the lives that forgot them, the ends set aside, its quits and heal decides, and whether the course is finished"),
    ];

    /// <summary>What one bot's decider made of the ends, read under its brain's lock.</summary>
    private readonly record struct BotEnds(int Target, int[] Reached, int[] Aside, int Entries, int Lives, int Quits, int HealDecides, string Verdict);

    private static BotEnds Read(TFDecider d)
    {
        var reached = new List<int>();
        var aside = new List<int>();
        for (int i = 0; i < d.CourseEnds.Length; i++)
        {
            if (d.EndReached(i))
                reached.Add(i);
            else if (d.EndAside(i))
                aside.Add(i);
        }

        return new BotEnds(d.EndTarget, [.. reached], [.. aside], d.CourseEndsReached, d.CourseEndLives, d.CourseQuits, d.CourseHealDecides, d.Doing.Verdict);
    }

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query)
    {
        System.Collections.Immutable.ImmutableArray<TFCourseEnd> ends = TFCourseEnds.Of(placed.Read());
        var rows = new List<object>();
        foreach (AiBot b in bots.Bots)
        {
            bool read = ai.Session.TryReadBrain(b.Id.Slot, br => br.Decider is TFDecider d ? Read(d) : new BotEnds(-1, [], [], 0, 0, 0, 0, ""), out BotEnds e);
            rows.Add(new
            {
                slot = b.Id.Slot, name = b.Name, read, target = e.Target, reached = e.Reached ?? [], aside = e.Aside ?? [],
                done = ends.Length > 0 && (e.Reached?.Length ?? 0) == ends.Length, entries = e.Entries, lives = e.Lives, quits = e.Quits,
                heal_decides = e.HealDecides, verdict = e.Verdict ?? "",
            });
        }

        return new DebugRouteReply(200, AiJson.Write(new
        {
            ends = ends.Select(x => new
            {
                index = x.Index, title = x.Title, rank = x.Rank,
                mins = new[] { Math.Round(x.Mins.X), Math.Round(x.Mins.Y), Math.Round(x.Mins.Z) },
                maxs = new[] { Math.Round(x.Maxs.X), Math.Round(x.Maxs.Y), Math.Round(x.Maxs.Z) },
            }).ToArray(),
            bots = rows,
        }));
    }
}
