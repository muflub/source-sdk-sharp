using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

namespace SourceSharp.Ai.Server.Senses;

/// <summary>
/// Which things that are not players each bot sees (plan_bots_teams_tf2 C.3, P6 step 3): the
/// game's things read once a tick (<see cref="IBotGameView.Things"/>), then for a bot each
/// through range, its field of view, the engine's PVS and one sight line from its eye, as an
/// item is (<see cref="VisionPass.RunItems"/>). A sight line's answer holds for
/// <see cref="HoldTicks"/> ticks and at most <see cref="TracesPerTick"/> are traced a tick, so a
/// sky of stickies costs what a few do. What passes is drawn as the bot's client draws it
/// (<see cref="IBotGameView.RenderThing"/>) into its view. One of the perception types: it
/// reads the bot's own eye and view, and of a thing only what the game drew.
/// </summary>
[SideService(Side.Server)]
public sealed class ThingSight(IEnginePlayers players, IEngineEntities entities, ITraceQueries traces, IBotGame game)
{
    /// <summary>Sight lines traced a tick for things, apart from the players' budget.</summary>
    public const int TracesPerTick = 16;

    /// <summary>How long a thing's sight line answer stands before it is traced again.</summary>
    public const int HoldTicks = 6;

    private readonly List<ThingRead> _things = new();
    private readonly Dictionary<long, (bool Clear, int Tick)> _lines = new();
    private readonly List<long> _stale = new();
    private int _tick = -1, _tracedThisTick;

    /// <summary>Sight lines traced in all.</summary>
    public long Traces { get; private set; }

    /// <summary>The things the game drew at the last <see cref="Begin"/>.</summary>
    public int ThingsThisTick => _things.Count;

    /// <summary>Reads the game's things for <paramref name="tick"/>, once: a second call in the same tick reads nothing.</summary>
    public void Begin(Tick tick)
    {
        if (tick.Value == _tick)
            return;
        _tick = tick.Value;
        _tracedThisTick = 0;
        game.View.Things(_things);
        _stale.Clear();
        foreach ((long key, (bool _, int at)) in _lines)
        {
            if (tick.Value - at > HoldTicks || tick.Value < at)
                _stale.Add(key);
        }

        foreach (long key in _stale)
            _lines.Remove(key);
    }

    /// <summary>Adds to <paramref name="view"/> the things <paramref name="bot"/>, whose eye is at <paramref name="eye"/>, sees this tick.</summary>
    public void AddSeen(in SensedBot bot, AiVector eye, PerceivedView view)
    {
        if (_things.Count == 0)
            return;
        Vector ang = entities.GetEyeAngles(bot.Player);
        AiVector forward = VisionMath.Forward(new AiAngles(ang.X, ang.Y, ang.Z));
        foreach (ref readonly ThingRead t in CollectionsMarshal.AsSpan(_things))
        {
            if (view.ThingCount == view.ThingCapacity)
                return;
            AiVector at = Centre(in t);
            if (eye.DistanceTo(at) > bot.Vision.SightRange || !VisionMath.InCone(eye, forward, at, bot.Vision.FovDeg))
                continue;
            float r = t.Reach;
            if (!players.BoxInPvs(bot.Player, new Vector(at.X - r, at.Y - r, at.Z - r), new Vector(at.X + r, at.Y + r, at.Z + r)))
                continue;
            if (!LineClear(in bot, eye, in t))
                continue;
            ThingFacts drawn = t.Facts;
            if (game.View.RenderThing(bot.Player, ref drawn))
                view.AddThing(in drawn);
        }
    }

    /// <summary>Where a sight line ends: half its reach over the origin, the middle of a building on the floor and inside a projectile.</summary>
    public static AiVector Centre(in ThingRead t) => t.Facts.Origin + new AiVector(0f, 0f, t.Reach * 0.5f);

    private bool LineClear(in SensedBot bot, AiVector eye, in ThingRead t)
    {
        long key = ((long)bot.Id.Slot << 32) | (uint)t.Facts.Id;
        if (_lines.TryGetValue(key, out (bool Clear, int Tick) held))
            return held.Clear;
        if (_tracedThisTick >= TracesPerTick)
            return false;   // not yet looked at: seen on a later tick
        _tracedThisTick++;
        Traces++;
        AiVector at = Centre(in t);
        TraceAnswer line = traces.Line(new Vector(eye.X, eye.Y, eye.Z), new Vector(at.X, at.Y, at.Z), VisionPass.LineMask, bot.Player);
        bool clear = line.Fraction >= 0.98f || line.Entity == t.Entity;
        _lines[key] = (clear, _tick);
        return clear;
    }
}
