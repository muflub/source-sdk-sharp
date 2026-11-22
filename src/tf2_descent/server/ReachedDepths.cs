using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using SourceSharp.Server;

using TF2.Descent.Server.Hero;

namespace TF2.Descent.Server;

/// <summary>
/// The deepest level each hero has reached (plan_tf2_rpg R6, D8: the town offers the depths
/// reached, narrowed by mine plan M10 to the cage's 1 to 5 and the camps), recorded as he
/// ARRIVES, whichever way he came (the town's cage, a level's way down or up, the portal back, a
/// changelevel): while this map is a Descent level of depth N, each hero on it has N written to
/// his sheet's <see cref="Rules.Hero.HeroSheet.ReachedDepth"/> once, never lowered, through the
/// sheet's audited change. Unhosted the sheet is held for the process (<see cref="HeroSheets"/>),
/// so the record outlives the level change and the return to town; hosted the next checkpoint
/// carries it, beside the host's own record, which the join raises
/// (<c>HostLink.Join</c>'s <c>SetReachedDepth</c>) and the join's entry reads.
/// <see cref="Of"/> is what the town's depth picker and the travel service ask: the deeper of the
/// two.
/// </summary>
[SideService(Side.Server)]
public sealed class ReachedDepths(Level.ILevelData level, Heroes heroes, HeroSheets sheets, PlayerService players, HostService host, ILog log)
    : IGameLifecycle, IFrameLifecycle
{
    /// <summary>Beside the level's own services (the level is read at +50, arrivals placed at +55).</summary>
    public const int Order = ModStages.First + 60;

    public int LifecycleOrder => Order;

    public int FrameOrder => Order;

    // The heroes whose arrival on this level is recorded, by slot: the hero, so a new player in the slot is recorded too.
    private readonly Dictionary<int, HeroComponent> _recorded = [];

    /// <summary>Arrivals recorded as a deeper depth on a sheet, all levels.</summary>
    public long Recorded { get; private set; }

    /// <summary>
    /// The deepest level the hero in <paramref name="player"/> has reached: his sheet's record, or
    /// hosted the join's (the host's ledger) when that is deeper; 0 for none.
    /// </summary>
    public int Of(int player)
    {
        int sheet = sheets.Of(player)?.ReachedDepth ?? 0;
        int joined = host.Mode == HostingMode.Hosted ? players.ByEntity(player)?.ReachedDepth ?? 0 : 0;
        return Math.Max(0, Math.Max(sheet, joined));
    }

    public void OnLevelInit(string mapName) => _recorded.Clear();

    public void OnThink(float frameTime)
    {
        if (level.Current is not { Depth: > 0 } here || _recorded.Count == heroes.All.Count)
            return;

        foreach (var hero in heroes.All)
        {
            int index = hero.PlayerIndex;
            if (_recorded.TryGetValue(index, out var had) && ReferenceEquals(had, hero))
                continue;
            if (Record(index, here.Depth, here.Map))
                _recorded[index] = hero;
        }

        // A slot whose hero left: forget it, so the count holds the heroes still here.
        if (_recorded.Count != heroes.All.Count)
            foreach (int gone in _recorded.Where(kv => !ReferenceEquals(heroes.Of(kv.Key), kv.Value)).Select(kv => kv.Key).ToList())
                _recorded.Remove(gone);
    }

    /// <summary>Writes <paramref name="depth"/> to the slot's sheet when it is deeper; false while the slot has no sheet (a hosted join not yet answered).</summary>
    private bool Record(int player, int depth, string map)
    {
        if (sheets.Of(player) is not { } sheet)
            return false;
        if (sheet.ReachedDepth >= depth)
            return true;

        int was = sheet.ReachedDepth;
        var audit = sheets.Apply(player, s => s with { ReachedDepth = Math.Max(s.ReachedDepth, depth) });
        if (!audit.Ok)
            return true;   // refused and logged by the sheets; asking again each frame changes nothing

        Recorded++;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Travel] player {player} reached depth {depth} ({map}): his record was {was}\n"));
        return true;
    }
}
