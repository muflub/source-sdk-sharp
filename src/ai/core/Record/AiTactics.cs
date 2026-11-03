using System.Globalization;
using System.Text;

namespace SourceSharp.Ai;

/// <summary>One bot's camping and prefiring in a recording (plan_hl2mp_ai §5.4, §5.5).</summary>
/// <param name="HoldSeconds">Seconds it stood still under the ambush leaf.</param>
/// <param name="HoldNearestPickup">Those seconds' mean distance to the nearest pickup worth holding (health, armour, a charger, a good gun).</param>
/// <param name="HoldNearestChoke">Their mean distance to the nearest choke point; NaN with no chokes given.</param>
/// <param name="HoldByPlace">Seconds held within <see cref="AiTactics.HoldReach"/> of such a pickup or a choke.</param>
/// <param name="Places">Distinct places it held (cells of <see cref="AiTactics.PlaceCell"/> units).</param>
/// <param name="Kills">Its kills.</param>
/// <param name="KillsHolding">Those it made while holding, or within <see cref="AiTactics.HoldGraceTicks"/> of it.</param>
/// <param name="KillsAtPlaces">Those whose victim was within <see cref="AiTactics.HoldReach"/> of a pickup or a choke.</param>
/// <param name="Bursts">Prefire bursts begun.</param>
/// <param name="BurstSeconds">Seconds the bursts lasted, by the intents.</param>
/// <param name="PrefireShots">Trigger pulls under a prefire.</param>
/// <param name="PrefireRounds">Rounds those took.</param>
/// <param name="PrefireHits">Hits on someone under a prefire.</param>
/// <param name="PrefireKills">Kills whose last hit was under a prefire.</param>
/// <param name="Shots">Every trigger pull.</param>
/// <param name="Rounds">Every round.</param>
/// <param name="AliveSeconds">Seconds it had frames for.</param>
/// <param name="BurstDelayMean">Mean seconds from losing sight of someone to the burst that followed, over bursts within <see cref="AiTactics.AfterLostTicks"/> of it; NaN with none.</param>
/// <param name="BurstsAfterLost">Those bursts.</param>
/// <param name="SightEndsWorst">The longest a burst went on after it saw someone, in seconds; 0 with none cut short.</param>
/// <param name="SightEnded">Bursts it saw someone during.</param>
public sealed record TacticsActor(ActorId Actor, double HoldSeconds, double HoldNearestPickup, double HoldNearestChoke, double HoldByPlace, int Places,
                                  int Kills, int KillsHolding, int KillsAtPlaces, int Bursts, double BurstSeconds, int PrefireShots, double PrefireRounds,
                                  int PrefireHits, int PrefireKills, int Shots, double Rounds, double AliveSeconds,
                                  double BurstDelayMean = double.NaN, int BurstsAfterLost = 0, double SightEndsWorst = 0, int SightEnded = 0, IReadOnlyList<int>? BurstTicks = null)
{
    /// <summary>Bursts a minute of its life.</summary>
    public double BurstsPerMinute => AliveSeconds > 0 ? Bursts / (AliveSeconds / 60) : 0;
}

/// <summary>A recording's camps and prefires: per bot, and who died where (a victim at a pickup's spawn was killed picking up).</summary>
public sealed record TacticsReport(IReadOnlyList<TacticsActor> Bots, int Kills, int KilledAtPickups, int KilledAtPickupsByHolders, double SharedHoldSeconds = 0);

/// <summary>
/// Reads camping and prefiring from a recording (<c>ai-replay tactics</c>): pure.
/// A hold is a tick under the "ambush" leaf with the body having moved under
/// <see cref="HoldMoved"/> in the last second. A prefire is an intent looking at a
/// corner (<see cref="LookKind.CornerCheck"/>) with fire on.
/// </summary>
public static class AiTactics
{
    public const float HoldMoved = 48f;

    public const int HoldWindowTicks = 66;

    /// <summary>Near enough to a place to be holding it: about a person's weapon range.</summary>
    public const float HoldReach = 400f;

    /// <summary>A killer that stopped holding this recently (it stepped out to finish it) still counts.</summary>
    public const int HoldGraceTicks = 200;

    public const float PlaceCell = 192f;

    /// <summary>Two bots holding this near each other at one tick share a spot.</summary>
    public const float SharedUnits = 96f;

    /// <summary>A victim this near an item's spawn was at it.</summary>
    public const float AtPickup = 128f;

    /// <summary>A pickup a person goes for; a battery or kit, a charger, a gun of at least this rank (the caller's table).</summary>
    public const int GoodRank = 5;

    /// <summary>A burst this soon after losing sight of someone was about them.</summary>
    public const int AfterLostTicks = 200;

    private sealed class Bot(ActorId id)
    {
        public readonly ActorId Id = id;
        public readonly SortedList<int, AiActorFrame> Frames = [];
        public readonly List<(int Tick, int Node, bool Prefire)> Intents = [];
        public readonly List<AiEvent> Shots = [];
        public readonly List<int> Lost = [];
        public readonly List<int> Gained = [];
        public readonly List<int> BurstTicks = [];
        public double DelaySum, SightWorst;
        public int AfterLost, SightEnded;
        public double HoldSeconds, NearestPickup, NearestChoke, ByPlace, BurstSeconds, PrefireRounds, Rounds;
        public int Bursts, PrefireShots, PrefireHits, PrefireKills, Kills, KillsHolding, KillsAtPlaces, ShotCount;
        public readonly HashSet<(int, int)> Places = [];
        public readonly Dictionary<int, AiVector> HoldAt = [];
    }

    public static TacticsReport Read(Stream recording, IReadOnlyList<AiVector> chokes, Func<string, int> weaponRank)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(chokes);
        ArgumentNullException.ThrowIfNull(weaponRank);
        AiRecordFormat.ReadFileHeader(recording);
        var bots = new Dictionary<ActorId, Bot>();
        var frames = new Dictionary<ActorId, SortedList<int, AiActorFrame>>();
        var kills = new HashSet<(ActorId Killer, ActorId Victim, int Tick)>();
        var hits = new List<AiEvent>();
        var good = new List<AiVector>();
        var pickups = new List<AiVector>();
        AiDecisionTree? tree = null;
        while (AiRecordFormat.TryReadRecord(recording, out AiRecordType type, out byte[] payload))
        {
            using var r = new BinaryReader(new MemoryStream(payload, writable: false));
            switch (type)
            {
                case AiRecordType.BotAdded:
                    ActorId added = AiRecordFormat.ReadActor(r);
                    bots[added] = new Bot(added);
                    break;
                case AiRecordType.DecisionTree:
                    tree = AiRecordFormat.ReadDecisionTree(r);
                    break;
                case AiRecordType.World:
                    foreach (ItemSpawn item in AiRecordFormat.ReadWorld(r).Map.Items)
                    {
                        pickups.Add(item.Position);
                        if (item.Kind is ItemKind.Health or ItemKind.Armour or ItemKind.Charger || (item.Kind == ItemKind.Weapon && weaponRank(item.ClassName) >= GoodRank))
                            good.Add(item.Position);
                    }

                    break;
                case AiRecordType.Frame:
                    AiActorFrame f = AiRecordFormat.ReadFrame(r);
                    if (!frames.TryGetValue(f.Actor, out SortedList<int, AiActorFrame>? at))
                        frames[f.Actor] = at = [];
                    at[f.Tick.Value] = f;
                    break;
                case AiRecordType.Intent:
                    IntentCommand i = AiRecordFormat.ReadIntent(r);
                    if (bots.TryGetValue(i.Bot, out Bot? bot))
                        bot.Intents.Add((i.FromTick.Value, i.Reason.NodeId, i.Look.Kind == LookKind.CornerCheck && i.Action.Kind == ActionKind.Fire));
                    break;
                case AiRecordType.Event:
                case AiRecordType.Observed:
                    AiEvent e = AiRecordFormat.ReadEvent(r);
                    if (e.Kind == AiEventKind.ShotFired && bots.TryGetValue(e.Bot, out Bot? shooter))
                        shooter.Shots.Add(e);
                    else if (e.Kind == AiEventKind.HitDealt)
                        hits.Add(e);
                    else if (e.Kind == AiEventKind.SightLost && bots.TryGetValue(e.Bot, out Bot? lost))
                        lost.Lost.Add(e.Tick.Value);
                    else if (e.Kind == AiEventKind.SightGained && bots.TryGetValue(e.Bot, out Bot? gained))
                        gained.Gained.Add(e.Tick.Value);
                    else if (e.Kind == AiEventKind.KillFeed)
                        kills.Add((new ActorId(e.Code, e.Code2), e.Other, e.Tick.Value));
                    break;
            }
        }

        foreach (Bot b in bots.Values)
        {
            if (frames.TryGetValue(b.Id, out SortedList<int, AiActorFrame>? own))
                foreach ((int tick, AiActorFrame frame) in own)
                    b.Frames[tick] = frame;
            Sweep(b, tree, good, chokes, hits);
        }

        int atPickups = 0, byHolders = 0;
        foreach ((ActorId killer, ActorId victim, int tick) in kills)
        {
            bots.TryGetValue(killer, out Bot? by);
            AiVector? fell = frames.TryGetValue(victim, out SortedList<int, AiActorFrame>? theirs) ? Last(theirs, tick) : null;
            bool holding = by is not null && HoldingNear(by, tree, tick);
            if (by is not null)
            {
                by.Kills++;
                by.KillsHolding += holding ? 1 : 0;
                if (fell is { } v && Nearest(v, good) <= HoldReach || fell is { } w && Nearest(w, chokes) <= HoldReach)
                    by.KillsAtPlaces++;
                if (HitUnderPrefire(by, victim, tick, hits))
                    by.PrefireKills++;
            }

            if (fell is { } place && Nearest(place, pickups) <= AtPickup)
            {
                atPickups++;
                byHolders += holding ? 1 : 0;
            }
        }

        double shared = 0d;
        Bot[] holders = [.. bots.Values];
        for (int i = 0; i < holders.Length; i++)
        {
            for (int j = i + 1; j < holders.Length; j++)
            {
                foreach ((int tick, AiVector at) in holders[i].HoldAt)
                    shared += holders[j].HoldAt.TryGetValue(tick, out AiVector other) && other.DistanceTo(at) < SharedUnits ? AiMetrics.TickSeconds : 0d;
            }
        }

        return new TacticsReport(
            [.. bots.Values.OrderBy(b => b.Id.Slot).ThenBy(b => b.Id.Serial).Select(b => new TacticsActor(
                b.Id, b.HoldSeconds, b.HoldSeconds > 0 ? b.NearestPickup / b.HoldSeconds : double.NaN,
                b.HoldSeconds > 0 && chokes.Count > 0 ? b.NearestChoke / b.HoldSeconds : double.NaN, b.ByPlace, b.Places.Count,
                b.Kills, b.KillsHolding, b.KillsAtPlaces, b.Bursts, b.BurstSeconds, b.PrefireShots, b.PrefireRounds, b.PrefireHits, b.PrefireKills,
                b.ShotCount, b.Rounds, b.Frames.Count * AiMetrics.TickSeconds,
                b.AfterLost > 0 ? b.DelaySum / b.AfterLost : double.NaN, b.AfterLost, b.SightWorst, b.SightEnded, b.BurstTicks))],
            kills.Count, atPickups, byHolders, shared);
    }

    private static void Sweep(Bot b, AiDecisionTree? tree, List<AiVector> good, IReadOnlyList<AiVector> chokes, List<AiEvent> hits)
    {
        IList<int> ticks = b.Frames.Keys;
        int decision = 0, back = 0;
        bool wasPrefire = false;
        foreach (int tick in ticks)
        {
            while (decision + 1 < b.Intents.Count && b.Intents[decision + 1].Tick <= tick)
                decision++;
            if (b.Intents.Count == 0 || b.Intents[decision].Tick > tick)
                continue;
            (_, int node, bool prefire) = b.Intents[decision];
            if (prefire)
            {
                b.BurstSeconds += AiMetrics.TickSeconds;
                if (!wasPrefire)
                {
                    b.Bursts++;
                    b.BurstTicks.Add(tick);
                    int lost = b.Lost.FindLastIndex(t => t <= tick);
                    if (lost >= 0 && tick - b.Lost[lost] <= AfterLostTicks)
                    {
                        b.AfterLost++;
                        b.DelaySum += (tick - b.Lost[lost]) * AiMetrics.TickSeconds;
                    }
                }
            }

            wasPrefire = prefire;
            while (back < ticks.Count && ticks[back] < tick - HoldWindowTicks)
                back++;
            AiVector here = b.Frames[tick].Origin;
            if (tree?.Name(node) == "ambush" && here.DistanceTo(b.Frames[ticks[back]].Origin) < HoldMoved)
            {
                float pickup = Nearest(here, good), choke = Nearest(here, chokes);
                b.HoldSeconds += AiMetrics.TickSeconds;
                b.HoldAt[tick] = here;
                b.NearestPickup += pickup * AiMetrics.TickSeconds;
                b.NearestChoke += choke * AiMetrics.TickSeconds;
                if (pickup <= HoldReach || choke <= HoldReach)
                    b.ByPlace += AiMetrics.TickSeconds;
                b.Places.Add(((int)MathF.Floor(here.X / PlaceCell), (int)MathF.Floor(here.Y / PlaceCell)));
            }
        }

        // How long a burst went on once it saw someone: the intent after a sight stops it.
        foreach (int seen in b.Gained)
        {
            int i = IntentAt(b, seen);
            if (i < 0 || !b.Intents[i].Prefire)
                continue;
            while (i < b.Intents.Count && b.Intents[i].Prefire)
                i++;
            if (i < b.Intents.Count)
            {
                b.SightEnded++;
                b.SightWorst = Math.Max(b.SightWorst, (b.Intents[i].Tick - seen) * AiMetrics.TickSeconds);
            }
        }

        foreach (AiEvent shot in b.Shots)
        {
            b.ShotCount++;
            b.Rounds += shot.Amount;
            if (UnderPrefire(b, shot.Tick.Value))
            {
                b.PrefireShots++;
                b.PrefireRounds += shot.Amount;
            }
        }

        foreach (AiEvent hit in hits)
        {
            if (hit.Bot == b.Id && UnderPrefire(b, hit.Tick.Value))
                b.PrefireHits++;
        }
    }

    private static bool UnderPrefire(Bot b, int tick)
    {
        int i = IntentAt(b, tick);
        return i >= 0 && b.Intents[i].Prefire;
    }

    private static int IntentAt(Bot b, int tick)
    {
        int lo = 0, hi = b.Intents.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (b.Intents[mid].Tick <= tick)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found;
    }

    private static bool HoldingNear(Bot b, AiDecisionTree? tree, int tick)
    {
        for (int i = IntentAt(b, tick); i >= 0 && b.Intents[i].Tick >= tick - HoldGraceTicks; i--)
        {
            if (tree?.Name(b.Intents[i].Node) == "ambush")
                return true;
        }

        return false;
    }

    private static bool HitUnderPrefire(Bot by, ActorId victim, int tick, List<AiEvent> hits)
    {
        for (int i = hits.Count - 1; i >= 0; i--)
        {
            AiEvent h = hits[i];
            if (h.Bot == by.Id && h.Other == victim && h.Tick.Value <= tick)
                return tick - h.Tick.Value <= 2 && UnderPrefire(by, h.Tick.Value);
        }

        return false;
    }

    private static AiVector? Last(SortedList<int, AiActorFrame> frames, int tick)
    {
        AiVector? last = null;
        foreach ((int at, AiActorFrame f) in frames)
        {
            if (at > tick)
                break;
            last = f.Origin;
        }

        return last;
    }

    private static float Nearest(AiVector from, IReadOnlyList<AiVector> places)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < places.Count; i++)
            best = MathF.Min(best, from.DistanceTo(places[i]));
        return best;
    }

    /// <summary>What <c>ai-replay tactics</c> prints.</summary>
    public static string Table(TacticsReport r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var sb = new StringBuilder();
        sb.AppendLine("bot                 hold s  to pickup  to choke  by place  places  kills  holding  at places | bursts/min  shots  rounds  hits  kills  delay  (of)  sight worst (of) | all shots rounds alive s");
        foreach (TacticsActor a in r.Bots)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{a.Actor.Slot,2}#{a.Actor.Serial,-16} {a.HoldSeconds,6:F0} {a.HoldNearestPickup,10:F0} {a.HoldNearestChoke,9:F0} {a.HoldByPlace,9:F0} {a.Places,7} {a.Kills,6} {a.KillsHolding,8} {a.KillsAtPlaces,10} | {a.BurstsPerMinute,10:F2} {a.PrefireShots,6} {a.PrefireRounds,7:F0} {a.PrefireHits,5} {a.PrefireKills,5} {a.BurstDelayMean,6:F2} {a.BurstsAfterLost,5} {a.SightEndsWorst,12:F2} {a.SightEnded,4} | {a.Shots,9} {a.Rounds,6:F0} {a.AliveSeconds,7:F0}"));
        }

        foreach (TacticsActor a in r.Bots)
        {
            if (a.BurstTicks is { Count: > 0 } at)
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"bursts of {a.Actor.Slot}: {string.Join(' ', at)}"));
        }

        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"kills {r.Kills}, of them at a pickup's spawn {r.KilledAtPickups}, by someone holding {r.KilledAtPickupsByHolders}; two holders within {SharedUnits:F0} u of each other for {r.SharedHoldSeconds:F1} s"));
        return sb.ToString();
    }
}
