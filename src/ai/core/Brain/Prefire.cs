namespace SourceSharp.Ai;

/// <summary>
/// A bot's one channel for asking the game about a line (plan_hl2mp_ai §5.4): the decider names a point in its doing
/// record (<see cref="DoingRecord.LineAsk"/>, <see cref="DoingRecord.LineAt"/>), the server traces from the bot's eye to it
/// through the world and props, and the answer comes back as <see cref="AiEventKind.LineAnswer"/> into
/// <see cref="BotMemory.LastLine"/>. It says nothing of who is behind. Everything a decider asks about a line goes through
/// one of these, so an answer is read once and an ask is never answered for another.
/// </summary>
public sealed class LineAsks
{
    /// <summary>The ask outstanding or last answered (0: never asked), the last answered, and what the last ask was of and from.</summary>
    public int Ask { get; private set; }

    public int Answered { get; private set; }

    public AiVector At { get; private set; }

    public AiVector From { get; private set; }

    /// <summary>Whether <paramref name="last"/> answers the ask outstanding: true once, and the ask is then answered.</summary>
    public bool TryAnswer((int Ask, AiVector Point, bool Clear) last)
    {
        // Nothing asked is nothing answered: an ask of 0 is the one answered before any.
        if (last.Ask != Ask || last.Ask == Answered)
            return false;
        Answered = last.Ask;
        return true;
    }

    /// <summary>A new ask of <paramref name="point"/> from <paramref name="from"/>, when the last was answered or <paramref name="again"/> (it is of another point).</summary>
    public void Reask(AiVector point, AiVector from, bool again)
    {
        if (Ask != Answered && !again)
            return;
        Ask++;
        At = point;
        From = from;
    }
}

/// <summary>
/// Where a bot believes its target is well enough to fire there before seeing it (plan_hl2mp_ai §5.4): just lost sight of,
/// just heard, or at the corner its habit says, on an approach that pre-aims it. Pure over what the bot knows.
/// </summary>
public static class PrefireBelief
{
    /// <summary>How long after losing sight, and after hearing, the bot believes it knows where they are; how sure it must still be, and on a habit.</summary>
    public const float SeenSeconds = 2.5f, HeardSeconds = 1.5f, Confidence = 0.5f, HabitConfidence = 0.25f;

    /// <summary>
    /// Whether the bot believes well enough where <paramref name="a"/> is. <paramref name="at"/> is where it believes,
    /// <paramref name="last"/> the last place it had any evidence of; <paramref name="age"/> how long ago that evidence was and
    /// <paramref name="window"/> how long a belief of its kind lasts (a habit's has no age and no end). <paramref name="habit"/>
    /// is the opponent's usual corner when the bot's approach pre-aims it, else null.
    /// </summary>
    public static bool Of(KnownActor a, Tick now, float tickSeconds, NavGraph? graph, AiVector? habit, out AiVector at, out AiVector last, out float age, out float window)
    {
        ArgumentNullException.ThrowIfNull(a);
        at = last = default;
        age = 0f;
        window = float.MaxValue;
        float seen = a.EverSeen ? (now - a.LastSeenTick) * tickSeconds : float.MaxValue;
        float heard = a.EverHeard ? (now - a.LastHeardTick) * tickSeconds : float.MaxValue;
        if (a.Confidence >= Confidence && seen <= SeenSeconds)
        {
            at = a.BelievedAt(graph);
            last = a.LastSeenPos;
            (age, window) = (seen, SeenSeconds);
        }
        else if (a.Confidence >= Confidence && heard <= HeardSeconds)
        {
            at = a.LastHeardPos;
            last = a.LastHeardPos;
            (age, window) = (heard, HeardSeconds);
        }
        else if (habit is { } spot && a.Confidence >= HabitConfidence)
        {
            at = last = spot;
        }
        else
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// What someone gone round a corner is behind (plan_hl2mp_ai §5.4): the nearest place to where the bot believes they are,
/// along the graph, that the map's sight table says the bot's place sees. With no table, the place itself; the game's
/// answer about the line has the last word. Per bot: its search's scratch is its own.
/// </summary>
public sealed class PrefireOpening(int cells)
{
    /// <summary>How far along the graph from the belief it looks for an opening, and how many cells its queue holds.</summary>
    public const int Cells = 512, Queue = 4096;

    private readonly int[] _seen = new int[cells];
    private readonly int[] _queue = new int[Queue];
    private int _stamp;

    /// <summary>The opening to <paramref name="believed"/> from a bot at <paramref name="self"/>, or <paramref name="believed"/> when none is found.</summary>
    public AiVector Find(NavGraph? graph, ICellSight? sight, AiVector self, AiVector believed)
    {
        if (graph is null || sight is null)
            return believed;
        CellId from = graph.FindCell(self + new AiVector(0f, 0f, 1f)), start = graph.FindCell(believed + new AiVector(0f, 0f, 1f));
        if (!from.IsValid || !start.IsValid || start.Index >= _seen.Length)
            return believed;
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_seen);
            _stamp = 1;
        }

        int head = 0, tail = 0;
        _queue[tail++] = start.Index;
        _seen[start.Index] = _stamp;
        while (head < tail && head < Cells)
        {
            int c = _queue[head++];
            if (sight.Visible(from.Index, c))
                return graph.Cell(c).Centre;
            foreach (NavEdge e in graph.EdgesFrom(c))
            {
                if (_seen[e.To] == _stamp || tail >= _queue.Length)
                    continue;
                _seen[e.To] = _stamp;
                _queue[tail++] = e.To;
            }
        }

        return believed;
    }
}

/// <summary>What a decide of a prefire came to: ended, the view on the place with the line not yet clear, or the trigger on.</summary>
public enum PrefireStep : byte
{
    Ended = 0,
    Aiming,
    Firing,
}

/// <summary>
/// A prefire's state (plan_hl2mp_ai §5.4, user request 2026-11-02: "Make bots prefire spots it thinks players are at"):
/// fire at the place the bot believes someone is before it sees them, only down a line the game has answered clear
/// (<see cref="LineAsks"/>). A chance at most every <see cref="CheckSeconds"/>, drawn from the stream it is given; the view
/// goes to the place at once and the trigger follows the answer; a blocked line tries the last place seen once, then gives
/// up; a line found blocked while firing ends it. The game says when one is allowed, where its place is, whether a place is
/// in reach and how long the trigger is held; a decide calls <see cref="Gate"/>, then (not <see cref="Aiming"/> and
/// <see cref="Next"/> due) <see cref="Draw"/>, then <see cref="Line"/>, in that order.
/// </summary>
public sealed class PrefireRun(AiRandom random, LineAsks lines)
{
    /// <summary>How long between one chance and the next: a person sprays, then checks.</summary>
    public const float CheckSeconds = 2f;

    /// <summary>A line's answer stands while the bot has moved less than this.</summary>
    public const float LineMoved = 48f;

    private readonly AiRandom _random = random;
    private readonly LineAsks _lines = lines;

    /// <summary>The view is on the place: a chance was taken and has not ended.</summary>
    public bool Aiming { get; private set; }

    /// <summary>The trigger is on, until <see cref="Until"/>.</summary>
    public bool Firing { get; private set; }

    /// <summary>The game's last answer about the line to <see cref="Spot"/> was clear.</summary>
    public bool LineClear { get; private set; }

    public bool TriedFallback { get; private set; }

    /// <summary>Where it aims, and the place it tries when the line there is blocked.</summary>
    public AiVector Spot { get; private set; }

    public AiVector Fallback { get; private set; }

    public Tick Until { get; private set; }

    /// <summary>The next chance is not before this.</summary>
    public Tick Next { get; private set; }

    /// <summary>Bursts begun.</summary>
    public int Begun { get; private set; }

    public void End()
    {
        Firing = Aiming = LineClear = false;
    }

    /// <summary>Ends it and waits <see cref="CheckSeconds"/> for the next chance.</summary>
    public void EndAndWait(Tick now, float tickSeconds)
    {
        End();
        Next = now + (int)(CheckSeconds / tickSeconds);
    }

    /// <summary>The decide's first step: a prefire not <paramref name="allowed"/> ends, and so does a burst whose time is up (then it waits). True to go on.</summary>
    public bool Gate(bool allowed, Tick now, float tickSeconds)
    {
        if (!allowed)
        {
            End();
            return false;
        }

        if (Firing && now >= Until)
        {
            EndAndWait(now, tickSeconds);
            return false;
        }

        return true;
    }

    /// <summary>A chance at <paramref name="spot"/> (with <paramref name="fallback"/> behind it) once its place is in reach: one draw against <paramref name="chance"/>, then the wait.</summary>
    public bool Draw(Tick now, float tickSeconds, float chance, AiVector spot, AiVector fallback)
    {
        Next = now + (int)(CheckSeconds / tickSeconds);
        if (_random.NextFloat() >= chance)
            return false;
        Spot = spot;
        Fallback = fallback;
        TriedFallback = false;
        LineClear = false;
        Aiming = true;
        return true;
    }

    /// <summary>
    /// The line, with the view already on <see cref="Spot"/>: <paramref name="last"/> (the memory's last answer) read if it
    /// answers this ask, the line asked again from <paramref name="origin"/>, and the trigger begun for
    /// <paramref name="burstTicks"/> on a clear answer. <paramref name="fallbackInReach"/> is whether
    /// <see cref="Fallback"/> is in the game's reach now.
    /// </summary>
    public PrefireStep Line((int Ask, AiVector Point, bool Clear) last, Tick now, AiVector origin, bool fallbackInReach, int burstTicks)
    {
        if (_lines.TryAnswer(last))
        {
            LineClear = last.Clear && last.Point.DistanceTo(Spot) < 1f && origin.DistanceTo(_lines.From) < LineMoved;
            if (!LineClear && !Firing)
            {
                if (TriedFallback || Fallback.DistanceTo(Spot) < 1f || !fallbackInReach)
                {
                    End();
                    return PrefireStep.Ended;
                }

                // Behind what it can see: the place it last saw them is the opening.
                TriedFallback = true;
                Spot = Fallback;
            }
            else if (!LineClear)
            {
                End();
                return PrefireStep.Ended;
            }
        }

        // Each decide it looks again: it moves, and a door may shut.
        _lines.Reask(Spot, origin, _lines.At.DistanceTo(Spot) >= 1f);
        if (!LineClear)
            return PrefireStep.Aiming;
        if (!Firing)
        {
            Firing = true;
            Until = now + Math.Max(1, burstTicks);
            Begun++;
        }

        return PrefireStep.Firing;
    }
}
