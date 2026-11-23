using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// <c>/ai/jumps/health</c> (lane G9): per bot, the jumps by health its tree began (those the plain floor refused and a heal
/// place by the landing paid for, <see cref="JumpHealthRule"/>), the landings it healed after (health on landing, health
/// after, seconds between), the deaths within <see cref="TFDecider.HealthJumpRiskSeconds"/> of such a landing, the visits
/// that found the pack gone and those not healed in time; and whether its switch is on. Read under each brain's lock, as
/// the other routes read the deciders.
/// </summary>
[SideService(Side.Server)]
public sealed class TFHealthJumpRoute(AiBots bots, ServerAiSession ai) : IDebugRouteProvider, IOrderedHook
{
    public const string Route = "/ai/jumps/health";

    /// <summary>Among the dev API's route providers: after the jumps' own (<see cref="TFJumps.HookOrder"/>).</summary>
    public int HookOrder => ModStages.First + 928;

    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("GET", Route, "the TF2 bots' jumps by health (lane G9: begun under the 90 floor because a pack or cabinet by the landing pays for it): per bot whether its switch (ai_tf_feel_jumphealth) is on, the jumps begun, each heal after one (health on landing, after, seconds), deaths within 5 s of such a landing, packs found gone, visits not healed in time, visits a death or the next jump ended, jumps that came down nowhere near their place, why the last jump the floor refused was not paid for by health (made from its facts when asked); each jump by health's seconds and the walk's it replaced (its plan's); and (lane G9b) the plain jumps that came down by a place worth taking, the heals after them, those missed, deaths within 5 s of them, every death, the times the bot went for a pack by way of a link to it the jumps begun while it did, and how each going ended"),
    ];

    private readonly record struct BotHealthJumps(bool On, int Begun, (int Landed, int After, float Seconds)[] Heals, int Deaths, int Gone, int Unhealed, string LastRefusal,
                                                  (float Seconds, float WalkSeconds)[] Times, int PackJumps, (int Landed, int After, float Seconds)[] PackHeals, int PackMissed,
                                                  int PackDeaths, int AllDeaths, int Seeks, int SeekJumps, int[] SeekEnds, int Lost, int Off, int PackOff);

    private static BotHealthJumps Read(TFDecider d)
        => new(d.Feel.JumpHealth, d.HealthJumps, [.. d.HealthJumpHeals], d.HealthJumpDeaths, d.HealthJumpGone, d.HealthJumpUnhealed, d.LastHealthRefusal,
               [.. d.HealthJumpTimes], d.PackJumps, [.. d.PackJumpHeals], d.PackJumpMissed, d.PackJumpDeaths, d.Deaths, d.PackLinkSeeks, d.PackLinkSeekJumps, [.. d.PackSeekEnds], d.HealthJumpLost, d.HealthJumpsOff, d.PackJumpsOff);

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query)
    {
        var rows = new List<object>();
        foreach (AiBot b in bots.Bots)
        {
            bool read = ai.Session.TryReadBrain(b.Id.Slot, br => br.Decider is TFDecider d ? Read(d) : new BotHealthJumps(false, 0, [], 0, 0, 0, "", [], 0, [], 0, 0, 0, 0, 0, [], 0, 0, 0), out BotHealthJumps e);
            rows.Add(new
            {
                slot = b.Id.Slot, name = b.Name, read, on = e.On, begun = e.Begun,
                heals = (e.Heals ?? []).Select(h => new { landed = h.Landed, after = h.After, seconds = Math.Round(h.Seconds, 2) }).ToArray(),
                deaths = e.Deaths, gone = e.Gone, unhealed = e.Unhealed, lost = e.Lost, off = e.Off, pack_off = e.PackOff, last_refusal = e.LastRefusal ?? "",
                times = (e.Times ?? []).Select(t => new { seconds = Math.Round(t.Seconds, 2), walk_seconds = Math.Round(t.WalkSeconds, 2) }).ToArray(),
                pack_jumps = e.PackJumps,
                pack_heals = (e.PackHeals ?? []).Select(h => new { landed = h.Landed, after = h.After, seconds = Math.Round(h.Seconds, 2) }).ToArray(),
                pack_missed = e.PackMissed, pack_deaths = e.PackDeaths, all_deaths = e.AllDeaths, seeks = e.Seeks, seek_jumps = e.SeekJumps,
                seek_ends = Enum.GetValues<TFDecider.PackSeekEnd>().ToDictionary(w => w.ToString().ToLowerInvariant(), w => (e.SeekEnds ?? []).ElementAtOrDefault((int)w)),
            });
        }

        return new DebugRouteReply(200, AiJson.Write(new { bots = rows }));
    }
}
