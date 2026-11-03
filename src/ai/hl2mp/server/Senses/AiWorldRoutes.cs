using System.Diagnostics;
using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>A player as it truly is: where it stands (its feet), null when dead.</summary>
public sealed record WorldActor(int Slot, string Name, bool IsBot, bool Alive, AiVector? At);

/// <summary>Another player as the bot's eyes dealt with it this tick: where it truly is, the stage it reached, the share visible.</summary>
public sealed record WorldCandidate(int Slot, AiVector At, string Stage, float Fraction, bool Sees);

/// <summary>
/// The truth about a bot's eyes: the eye, the view (Source's angles: yaw about
/// z from +x, pitch positive down), the field, and how far along the view the
/// first thing a bullet would hit is (-1: nothing within reach).
/// </summary>
public sealed record WorldTruth(AiVector Eye, float Yaw, float Pitch, float Fov, float Range, float AimDistance, WorldCandidate[] Candidates);

/// <summary>What the bot's tree chose: its branch and leaf, its goal, the slot of its target (-1 none).</summary>
public sealed record WorldGoing(string Branch, string Leaf, AiVector? Goal, int TargetSlot);

/// <summary>The path the bot's follower is on: whose (<c>brain</c>, or <c>goto</c> for a scripted walk), where to, its points.</summary>
public sealed record WorldRoute(string Source, AiVector Target, AiVector[] Points);

/// <summary>A sound the bot heard: the place it believes (with the hearing's error), when, what, how loud, whose (-1 unknown).</summary>
public sealed record WorldHeard(int Source, AiVector At, int Tick, string Kind, float Loudness);

/// <summary>An opponent as the bot remembers it: where it last saw it, when, how sure it still is, whether it is in sight now.</summary>
public sealed record WorldRemembered(int Slot, AiVector At, int Tick, float Confidence, bool InSight);

/// <summary>What the bot believes, which may be wrong. Remembered is null while its brain is stepping on a worker.</summary>
public sealed record WorldBeliefs(WorldHeard[] Heard, WorldRemembered[]? Remembered);

/// <summary>One bot. Truth is null while it is dead; Going is null with no tree, or while its brain is stepping (<see cref="Stepping"/>).</summary>
public sealed record WorldBot(int Slot, string Name, bool Stepping, WorldTruth? Truth, WorldGoing? Going, WorldRoute? Route, WorldBeliefs Beliefs);

/// <summary>
/// <c>/ai/world</c>'s answer: every player's live data in one call. What the
/// actors and each bot's truth say is the debugger's knowledge; only
/// <see cref="WorldBot.Beliefs"/> is what a bot knows.
/// </summary>
public sealed record AiWorld(int Tick, float TickSeconds, string Map, string? NavRefusal, float CostUs, WorldActor[] Actors, WorldBot[] Bots);

/// <summary>
/// The web UI's live 3D view of the bots (plan_hl2mp_ai §7.1): <c>GET
/// /ai/world</c>, asked about five times a second while the AI tab shows, read
/// on the game thread. A debug view of the truth, so it may read what no bot
/// may know; a route is outside the bots' view and no brain reads it
/// (<c>PerceptionCensusTests</c>). Written through <see cref="AiJson"/>.
/// </summary>
[SideService(Side.Server)]
public sealed class AiWorldRoutes(Hl2mpBots bots, VisionPass vision, Hl2mpAiSession ai, HeardLog heard, IEngineFrame frame, IEngineEntities entities,
                                  ITraceQueries traces, PlayerNames names, IHl2mpNavigation nav)
{
    /// <summary>bspflags.h's <c>MASK_SHOT</c>: the aim line stops where a bullet would.</summary>
    public const uint ShotMask = 0x1 | 0x4000 | 0x2000000 | 0x2 | 0x4000000 | 0x40000000;

    /// <summary>How far along its view a bot's aim is traced (stock's MAX_TRACE_LENGTH is far past any map's room).</summary>
    public const float AimReach = 8192f;

    /// <summary>How long a heard sound stays in the answer (the view fades it over this).</summary>
    public const float HeardSeconds = 5f;

    /// <summary>
    /// How far the frame's tick may be from the vision pass's for its reads to
    /// be of now (half a second). Past it nothing has been sensed on this level
    /// yet, and the reads are where players stood on the last one.
    /// </summary>
    public const int FreshTicks = 33;

    private readonly List<AiVector> _route = new();
    private float _lastCostUs;
    private Func<double>? _cpu;

    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("GET", "/ai/world", "every player's live data in one call, for the AI tab's 3D view: truth (actors, each bot's eye, view, aim, candidates, route) and beliefs (heard, remembered)"),
    ];

    /// <summary>Whether <paramref name="path"/> is this route.</summary>
    public static bool Serves(string path) => path == "/ai/world";

    public DebugRouteReply Handle()
    {
        // The thread's own CPU where the clock has one: what a busy box's other work took is not this route's.
        Func<double>? cpu = _cpu ??= ThreadCpuClock.Micros();
        double before = cpu?.Invoke() ?? 0d;
        long start = Stopwatch.GetTimestamp();
        string json = Json(Build());
        _lastCostUs = (float)(cpu is null ? Stopwatch.GetElapsedTime(start).TotalMicroseconds : cpu() - before);
        return new DebugRouteReply(200, json);
    }

    /// <summary>The answer as JSON: only ever through the one writer that keeps nothing of the types it is handed.</summary>
    public static string Json(AiWorld world) => AiJson.Write(world);

    /// <summary>The players now. <see cref="AiWorld.CostUs"/> is the last answer's cost in this thread's CPU time, the JSON included; 0 on the first.</summary>
    public AiWorld Build()
    {
        var tick = new Tick(frame.TickCount());
        var actors = new List<WorldActor>();
        for (int slot = 1; slot < VisionPass.Slots; slot++)
        {
            string? bot = BotName(slot);
            if (ReadOf(slot, tick) is { } read)
                actors.Add(new WorldActor(slot, bot ?? HumanName(slot), bot is not null, true, read.Origin));
            else if (bot is not null)
                actors.Add(new WorldActor(slot, bot, true, false, null));
        }

        var list = new WorldBot[bots.Bots.Count];
        for (int i = 0; i < list.Length; i++)
            list[i] = Bot(bots.Bots[i], tick);
        Array.Sort(list, (a, b) => a.Slot.CompareTo(b.Slot));
        return new AiWorld(tick.Value, Hl2mpAiSession.TickSeconds, nav.Current?.Map ?? "", nav.Refusal, Round(_lastCostUs), [.. actors], list);
    }

    /// <summary>The vision pass's read of the slot, while it is of this level.</summary>
    private ActorRead? ReadOf(int slot, Tick now)
        => Math.Abs(now.Value - vision.LastRun.Value) <= FreshTicks ? vision.ReadOf(slot) : null;

    private string? BotName(int slot)
    {
        foreach (Hl2mpBot b in bots.Bots)
        {
            if (b.Id.Slot == slot)
                return b.Name;
        }

        return null;
    }

    private string HumanName(int slot) => names.NameOf(slot) is { Length: > 0 } name ? name : $"player {slot}";

    private WorldBot Bot(Hl2mpBot bot, Tick tick)
    {
        int slot = bot.Id.Slot;
        WorldTruth? truth = Truth(bot, tick);
        bool read = ai.Session.TryReadBrain(slot, b => (Going(b), Remembered(b)), out (WorldGoing? Going, WorldRemembered[]? Remembered) mind);
        return new WorldBot(slot, bot.Name, !read && ai.Session.BrainIn(slot) is not null, truth, mind.Going, truth is null ? null : Route(slot),
                            new WorldBeliefs(Heard(bot, tick), mind.Remembered));
    }

    private WorldTruth? Truth(Hl2mpBot bot, Tick tick)
    {
        EntityHandle player = bots.Applier.PlayerOf(bot.Id);
        if (player == EntityHandle.None || ReadOf(bot.Id.Slot, tick) is not { } me)
            return null;

        Vector ang = entities.GetEyeAngles(player);
        AiVector forward = VisionMath.Forward(new AiAngles(ang.X, ang.Y, ang.Z));
        AiVector end = me.Eye + forward * AimReach;
        TraceAnswer aim = traces.Line(new Vector(me.Eye.X, me.Eye.Y, me.Eye.Z), new Vector(end.X, end.Y, end.Z), ShotMask, player);
        VisionParams field = VisionCurves.For(bot.Skills);
        return new WorldTruth(me.Eye, Round(ang.Y), Round(ang.X), field.FovDeg, field.SightRange, aim.Hit ? Round(aim.Fraction * AimReach) : -1f, Candidates(bot.Id.Slot));
    }

    private WorldCandidate[] Candidates(int slot)
    {
        var list = new List<WorldCandidate>();
        SightTracker? sight = vision.SightOf(slot);
        for (int t = 1; t < VisionPass.Slots; t++)
        {
            if (t == slot || vision.ReadOf(t) is not { } them)
                continue;
            list.Add(new WorldCandidate(t, VisionPass.Centre(them), vision.StageOf(slot, t).ToString().ToLowerInvariant(),
                                        Round(sight?.FractionOf(t) ?? 0f), sight?.Sees(t) == true));
        }

        return [.. list];
    }

    /// <summary>The bot's actual follower: a scripted walk's, else the one carrying out its brain's goal; null when nothing is walking it.</summary>
    private WorldRoute? Route(int slot)
    {
        NavFollower? scripted = bots.ScriptedFollowerOf(slot);
        if ((scripted ?? bots.Driver.FollowerOf(slot)) is not { } follower)
            return null;
        follower.Route(_route);
        return _route.Count == 0 ? null : new WorldRoute(scripted is null ? "brain" : "goto", follower.Target, [.. _route]);
    }

    private WorldHeard[] Heard(Hl2mpBot bot, Tick tick)
    {
        LoggedSound[] sounds = heard.Recent(bot.Id, tick, (int)(HeardSeconds / Hl2mpAiSession.TickSeconds));
        var list = new WorldHeard[sounds.Length];
        for (int i = 0; i < list.Length; i++)
            list[i] = new WorldHeard(sounds[i].Source, sounds[i].Place, sounds[i].Tick.Value, sounds[i].Kind.ToString().ToLowerInvariant(), Round(sounds[i].Loudness));
        return list;
    }

    private static WorldGoing? Going(Brain brain)
    {
        if (brain.Decider is not { } decider)
            return null;
        DoingRecord d = decider.Doing;
        return new WorldGoing(d.Branch, d.Leaf, d.HasGoal ? d.Goal : null, d.Target.IsValid ? d.Target.Slot : -1);
    }

    private static WorldRemembered[]? Remembered(Brain brain)
    {
        if (brain.Memory is not { } m)
            return [];
        var list = new List<WorldRemembered>();
        for (int s = 0; s < m.Board.Slots; s++)
        {
            KnownActor a = m.Board.Actor(s);
            if (a.Known && a.EverSeen)
                list.Add(new WorldRemembered(s, a.LastSeenPos, a.LastSeenTick.Value, Round(a.Confidence), a.InSight));
        }

        return [.. list];
    }

    private static float Round(float v) => MathF.Round(v, 2);
}
