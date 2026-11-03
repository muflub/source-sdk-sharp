namespace SourceSharp.Ai;

/// <summary>A bot's ammo estimate of one opponent, as the inspector draws it (plan_hl2mp_ai §7.2): the bar's width is the range's share of the clip.</summary>
public sealed record InspectAmmo(int Weapon, int Clip, int Min, int Max, float[] Histogram, bool Reloading, int ReloadEndTick, float PEmpty,
                                 float PReloading, bool Push, float WindowSeconds)
{
    /// <summary>The uncertainty bar's width, 0..1: the range over the clip.</summary>
    public float BarWidth => Clip <= 0 ? 0f : (Max - Min) / (float)Clip;
}

/// <summary>Where the bot believes an unseen opponent is (§5.3.1): the arg-max, the entropy, and the mass by cell (the heatmap).</summary>
public sealed record InspectBelief(int ArgMaxCell, AiVector ArgMax, float Entropy, int[] Cells, float[] Mass);

/// <summary>An opponent's learned habits (§5.1).</summary>
public sealed record InspectHabits(float RetreatTendency, float Aggression, float ReloadStyle, float[] EngagementRange, int[] CampCells,
                                   float[] CampCounts, int[] PickupItems, float[] ReloadBehaviours, int[] RouteCells, int[] RouteCounts);

/// <summary>One opponent as the bot remembers it (§7.2, memory).</summary>
public sealed record InspectActor(int Slot, int Serial, bool InSight, float Confidence, AiVector LastSeen, int LastSeenTick, AiVector LastHeard,
                                  int LastHeardTick, string LastHeardKind, int HealthEstimate, int WeaponSeen, int Rank, int Frags, int Deaths,
                                  InspectAmmo Ammo, InspectBelief? Belief, string Intent, float IntentScore, InspectHabits? Habits);

/// <summary>An item spawn as the bot believes it (§5.1).</summary>
public sealed record InspectItem(int Index, string ClassName, AiVector Position, string Present, int SeenTick, int ExpectedBack, float RespawnSeconds);

public sealed record InspectInvestigation(AiVector Position, float Loudness, int Tick, string Kind, float Worth);

public sealed record InspectMemory(InspectActor[] Actors, InspectInvestigation[] Investigations, InspectItem[] Items);

/// <summary>One skill's points (§7.2, skill allocation).</summary>
public sealed record InspectSkill(string Key, string Name, int Level);

/// <summary>The allocation and what it drives (§7.2, skill allocation).</summary>
public sealed record InspectSkills(string Profile, InspectSkill[] Skills, int Spent, int Pool, int Cap, Dictionary<string, float> Derived);

/// <summary>
/// What the inspector shows of one bot at one tick (plan_hl2mp_ai §7.2): its
/// memory and its skill allocation as data; the "doing and planning" part
/// is the behaviour tree's, Phase 5's. Built from the brain on the main
/// thread when a dev-API route asks; nothing is networked to a client.
/// </summary>
public sealed record BotInspectRecord(int Slot, int Serial, int Tick, InspectMemory? Memory, InspectSkills Skills, InspectDoing? Doing);

/// <summary>
/// The doing-and-planning part (§7.2): the chosen branch and leaf, the goal,
/// the opponent, the push / hold / retreat verdict with its window
/// arithmetic, the reason terms, the intent's expiry, and the last decides'
/// tree traces (newest first).
/// </summary>
public sealed record InspectDoing(string Branch, string Leaf, AiVector? Goal, int TargetSlot, string Verdict, float PEmpty, float PReloading,
                                  float WindowSeconds, float ArriveSeconds, float ScoreboardTerm, float AmmoTerm, int Expires,
                                  IReadOnlyList<InspectDecide> Decides);

/// <summary>One decide's trace: its tick, the reason, and each node visited with its answer and value.</summary>
public sealed record InspectDecide(int Tick, string Chosen, float Utility, float ScoreboardTerm, float AmmoTerm, IReadOnlyList<InspectNode> Nodes);

public sealed record InspectNode(int Id, string Name, string Status, float? Value);

/// <summary>Builds a <see cref="BotInspectRecord"/> from a brain and its skills.</summary>
public static class BotInspect
{
    /// <summary>The route prior's cells shown: the most visited.</summary>
    public const int RouteCellsShown = 256;

    public static BotInspectRecord Build(Brain brain, SkillProfile skills, Tick now, int pool = StandardSkills.Pool, int cap = StandardSkills.Cap)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(skills);
        return new BotInspectRecord(brain.Bot.Slot, brain.Bot.Serial, now.Value, brain.Memory is { } m ? Memory(m, now) : null,
                                    Skills(skills, pool, cap, brain.Decisions), brain.Decider is { } d ? Doing(d) : null);
    }

    /// <summary>The last decides shown (the trace ring holds 64).</summary>
    public const int DecidesShown = 16;

    public static InspectDoing Doing(IDecider decider, int decides = DecidesShown)
    {
        ArgumentNullException.ThrowIfNull(decider);
        DoingRecord d = decider.Doing;
        BtTrace trace = decider.Trace;
        IReadOnlyList<string> names = decider.NodeNames;
        var list = new List<InspectDecide>();
        for (int back = 0; back < Math.Min(decides, trace.Count); back++)
        {
            DecisionReason r = trace.ReasonAt(back);
            var nodes = new List<InspectNode>();
            foreach (BtTraceEntry e in trace.EntriesAt(back))
                nodes.Add(new InspectNode(e.NodeId, (uint)e.NodeId < (uint)names.Count ? names[e.NodeId] : "?", e.Status.ToString().ToLowerInvariant(),
                                          float.IsNaN(e.Value) ? null : e.Value));
            string chosen = (uint)r.NodeId < (uint)names.Count ? names[r.NodeId] : "";
            list.Add(new InspectDecide(trace.TickAt(back).Value, chosen, r.Utility, r.ScoreboardTerm, r.AmmoTerm, nodes));
        }

        return new InspectDoing(d.Branch, d.Leaf, d.HasGoal ? d.Goal : null, d.Target.IsValid ? d.Target.Slot : -1, d.Verdict, d.PEmpty, d.PReloading,
                                d.WindowSeconds, d.ArriveSeconds, d.ScoreboardTerm, d.AmmoTerm, d.Expires.Value, list);
    }

    public static InspectSkills Skills(SkillProfile p, int pool, int cap, DecisionParams? decisions = null)
    {
        VisionParams v = VisionCurves.For(p);
        MemoryParams m = MemoryParams.For(p);
        MotionParams motion = MotionCurves.For(p);
        var derived = new Dictionary<string, float>
        {
            ["recognize_s"] = v.RecognizeSeconds,
            ["sight_range"] = v.SightRange,
            ["fov_deg"] = v.FovDeg,
            ["memory_half_life_s"] = m.MemoryHalfLifeSeconds,
            ["habit_half_life_s"] = m.HabitHalfLifeSeconds,
            ["reaction_ms"] = motion.ReactionMs,
            ["turn_peak_deg_s"] = motion.TurnPeakDegPerSec,
        };
        if (decisions is { } d)
        {
            // Every curve's value (§10), by the parameter's own name.
            foreach (System.Reflection.PropertyInfo prop in typeof(DecisionParams).GetProperties())
                derived[prop.Name] = (float)prop.GetValue(d)!;
        }

        InspectSkill[] list = [.. p.Skills.Select((s, i) => new InspectSkill(s.Key, s.Name, p.Levels[i]))];
        return new InspectSkills(p.Name, list, p.Spent, pool, cap, derived);
    }

    private static int Level(SkillProfile p, string key)
    {
        int i = p.IndexOf(key);
        return i < 0 ? 0 : p.Levels[i];
    }

    public static InspectMemory Memory(BotMemory m, Tick now)
    {
        float dt = m.World.TickSeconds;
        var actors = new List<InspectActor>();
        for (int s = 0; s < m.Board.Slots; s++)
        {
            KnownActor a = m.Board.Actor(s);
            if (!a.Known)
                continue;
            m.TryHabits(a.Id, out OpponentHabits? h);
            InspectAmmo ammo = Ammo(m, a, h, now);
            InspectBelief? belief = a.Where is { Active: true } w ? Belief(m, w) : null;
            var inputs = new IntentInputs(h?.RetreatTendency ?? 0.5f, h?.Aggression ?? 0.5f, 0f, a.HealthEstimate,
                                          m.Self.DistanceTo(a.LastSeenPos), ammo.PEmpty, ammo.Reloading, false);
            (OpponentIntent intent, float score) = IntentClassifier.Top(inputs);
            actors.Add(new InspectActor(a.Id.Slot, a.Id.Serial, a.InSight, a.Confidence, a.LastSeenPos, a.LastSeenTick.Value, a.LastHeardPos,
                                        a.LastHeardTick.Value, a.LastHeardKind.ToString(), a.HealthEstimate, a.WeaponSeen, a.Rank, a.Frags, a.Deaths,
                                        ammo, belief, intent.ToString(), score, h is null ? null : Habits(h)));
        }

        InspectInvestigation[] inv = [.. m.Board.Investigations.ToArray()
            .Select(i => new InspectInvestigation(i.Position, i.Loudness, i.Tick.Value, i.Kind.ToString(), Blackboard.Worth(i, now, dt)))];
        var items = new List<InspectItem>();
        for (int i = 0; i < m.Board.Items.Length; i++)
        {
            ItemBelief b = m.Board.Items[i];
            ItemSpawn spawn = m.World.Map.Items[i];
            items.Add(new InspectItem(i, spawn.ClassName, spawn.Position, b.Present switch { true => "present", false => "gone", _ => "unknown" },
                                      b.SeenTick.Value, b.ExpectedBack.Value, b.RespawnSeconds));
        }

        return new InspectMemory([.. actors], inv, [.. items]);
    }

    private static InspectAmmo Ammo(BotMemory m, KnownActor a, OpponentHabits? h, Tick now)
    {
        AmmoEstimate e = a.Ammo;
        float[] hist = new float[Math.Max(0, e.Clip + 1)];
        for (int k = 0; k < hist.Length; k++)
            hist[k] = e.P(k);
        float pReload = 0f, window = 0f;
        bool push = false;
        if (e.Known && m.World.Map.Weapons.TryGet(e.Weapon, out WeaponFacts w))
        {
            float dist = m.Self.DistanceTo(a.LastSeenPos);
            AmmoWindow.Verdict v = AmmoWindow.Judge(e, w, now, m.World.TickSeconds, h?.ReloadStyle ?? float.NaN, dist, 190f, 0.6f, 0.2f);
            (pReload, window, push) = (v.PReloading, v.SecondsLeft, v.Push);
        }

        return new InspectAmmo(e.Weapon, e.Clip, e.Min, e.Max, hist, e.ReloadInProgress, e.ReloadEndTick.Value, e.PEmpty, pReload, push, window);
    }

    private static InspectBelief Belief(BotMemory m, PositionPredictor w)
    {
        NavGraph g = m.World.Graph!;
        var cells = new List<int>();
        var mass = new List<float>();
        for (int c = 0; c < g.CellCount; c++)
        {
            float p = w.P(c);
            if (p > 0f)
            {
                cells.Add(c);
                mass.Add(p);
            }
        }

        CellId arg = w.ArgMax;
        return new InspectBelief(arg.Index, arg.IsValid ? g.Cell(arg).Centre : default, w.Entropy, [.. cells], [.. mass]);
    }

    private static InspectHabits Habits(OpponentHabits h)
    {
        ReadOnlySpan<byte> route = h.Route;
        var top = new List<(int Cell, int Count)>();
        for (int c = 0; c < route.Length; c++)
        {
            if (route[c] > 0)
                top.Add((c, route[c]));
        }

        top.Sort((x, y) => y.Count != x.Count ? y.Count.CompareTo(x.Count) : x.Cell.CompareTo(y.Cell));
        if (top.Count > RouteCellsShown)
            top.RemoveRange(RouteCellsShown, top.Count - RouteCellsShown);
        var camps = h.Camps.ToArray();
        return new InspectHabits(h.RetreatTendency, h.Aggression, h.ReloadStyle, h.EngagementRange.ToArray(),
                                 [.. camps.Where(c => c.Cell.IsValid).Select(c => c.Cell.Index)], [.. camps.Where(c => c.Cell.IsValid).Select(c => c.Count)],
                                 [.. h.FavouritePickups.ToArray().Where(p => p.Item >= 0).Select(p => p.Item)], h.ReloadBehaviours.ToArray(),
                                 [.. top.Select(t => t.Cell)], [.. top.Select(t => t.Count)]);
    }
}
