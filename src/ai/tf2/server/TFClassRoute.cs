using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Player;
using TF2.Shared;

namespace SourceSharp.Ai.TF2;

/// <summary>
/// <c>/ai/classes</c> (plan_bots_teams_tf2 P7 step 5): what the per-class A/B harness (<c>tools/ai-tf-class</c>) reads,
/// one sample a GET. Per bot: its class, subtree and archetype, its pin, its kit (defindex by slot), each ability row's
/// uses, its class's own counters (<see cref="TFDecider.ClassPlay.Count"/>), its stuck lines, the damage it dealt
/// (<see cref="TFDamageTally"/>), and the scoreboard's kills, deaths, captures, defences, healing, backstabs and buildings built (the server's stats, read for the
/// measure only: no bot reads them); and the class clock's changes and the changes it held. A brain busy deciding is
/// <c>read: false</c>.
/// </summary>
[SideService(Side.Server)]
public sealed class TFClassRoute(AiBots bots, ServerAiSession ai, ITFPlayerStats stats, TFBotPins pins, TFBotClassPick pick, TFDamageTally damage)
    : IDebugRouteProvider, IOrderedHook
{
    public const string Route = "/ai/classes";

    /// <summary>Among the dev API's route providers: after <see cref="Knowledge.TFPointsRoute"/> and <see cref="Nav.TFRouteSpreadRoute"/>.</summary>
    public int HookOrder => ModStages.First + 933;

    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("GET", Route, "TF2 bots by class (P7): class, subtree, archetype, pin, kit, ability uses by row, the class's own counters, stuck lines, and the scoreboard's kills, deaths, damage, captures, defences, healing, backstabs and buildings; the class clock's changes"),
    ];

    private readonly record struct BotRead(int Class, string Subtree, string Archetype, int[] Uses, List<KeyValuePair<string, long>> Counters, int[] Kit);

    private static BotRead Read(TFDecider d)
    {
        ReadOnlySpan<int> uses = d.AbilityUses;
        int[] kit = d.LastHud is { } hud ? [.. hud.Kit.Select(k => k.Defindex)] : [];
        return new BotRead(d.LastSelf.Class, d.Subtree, d.Archetype, uses.ToArray(), d.ClassCounters(), kit);
    }

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query)
    {
        AbilityTable abilities = TFAbilityNames.Table;
        var rows = new List<object>();
        foreach (AiBot b in bots.Bots)
        {
            int slot = b.Id.Slot;
            bool read = ai.Session.TryReadBrain(slot, br => br.Decider is TFDecider d ? Read(d) : default, out BotRead r);
            var used = new Dictionary<string, int>();
            for (int i = 0; r.Uses is not null && i < r.Uses.Length; i++)
            {
                if (r.Uses[i] > 0 && abilities.TryGet(i, out AbilityRow row))
                    used[row.Name.Length > 0 ? row.Name : $"row{i}"] = r.Uses[i];
            }

            TFBotPin? pin = pins.Of(b.Id);
            rows.Add(new
            {
                slot, name = b.Name, read, @class = TFClassNames.Raw((TFClass)r.Class), subtree = r.Subtree ?? "", archetype = r.Archetype ?? "",
                pin_class = pin is { Class: not TFClass.Undefined } ? TFClassNames.Raw(pin.Class) : "", pin_archetype = pin?.Archetype ?? "",
                kit = r.Kit ?? [], abilities = used, counters = (r.Counters ?? []).ToDictionary(c => c.Key, c => c.Value),
                stuck_lines = bots.StuckLines(slot),
                kills = stats.Accumulated(slot, TFStat.Kills), deaths = stats.Accumulated(slot, TFStat.Deaths), damage = damage.Dealt(slot),
                captures = stats.Accumulated(slot, TFStat.Captures), defenses = stats.Accumulated(slot, TFStat.Defenses),
                healing = stats.Accumulated(slot, TFStat.Healing), backstabs = stats.Accumulated(slot, TFStat.Backstabs),
                buildings = stats.Accumulated(slot, TFStat.Buildingsbuilt),
            });
        }

        return new DebugRouteReply(200, AiJson.Write(new
        {
            clock = new { changes = pick.Clock.Changes, held = pick.Clock.Held, last_at = float.IsFinite(pick.Clock.LastAt) ? Math.Round(pick.Clock.LastAt, 1) : -1 },
            bots = rows,
        }));
    }
}

/// <summary>The one ability table's row names, for the routes (the same table every decider's map knowledge carries).</summary>
public static class TFAbilityNames
{
    public static AbilityTable Table => Knowledge.TFAbilities.Table();
}
