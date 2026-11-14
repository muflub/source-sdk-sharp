using System.Collections.Immutable;
using SourceSharp.Ai;
using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Ai.Senses;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>Bodies' places, set by a fact or the rig's slide.</summary>
internal sealed class FakePoses : IHordeBodyPoses
{
    public Dictionary<int, (AiVector At, float Yaw)> Places { get; } = [];

    public AiVector Position(int body) => Places.TryGetValue(body, out var p) ? p.At : default;

    public float Yaw(int body) => Places.TryGetValue(body, out var p) ? p.Yaw : 0f;

    public void Set(int body, AiVector at, float yaw) => Places[body] = (at, yaw);
}

/// <summary>Every order, hold and action the brains handed out, and each body's order now.</summary>
internal sealed class FakeOrders : IHordeOrders
{
    public List<(int Body, MoveIntent Move, LookIntent Look, long Expires)> Orders { get; } = [];
    public List<int> Holds { get; } = [];
    public List<(int Body, ActionIntent Action)> Acts { get; } = [];
    public Dictionary<int, (MoveIntent Move, LookIntent Look, long Expires, int Corner)> Now { get; } = [];

    public void Order(int body, in MoveIntent move, in LookIntent look, long expiresTick)
    {
        Orders.Add((body, move, look, expiresTick));
        Now[body] = (move, look, expiresTick, 0);
    }

    public void Hold(int body)
    {
        Holds.Add(body);
        Now.Remove(body);
    }

    public void Act(int body, in ActionIntent action) => Acts.Add((body, action));
}

/// <summary>The bodies' guns: each looks along its facing, level, and its line of fire runs its whole reach, unless a fact says otherwise; counts every line traced.</summary>
internal sealed class FakeAim(FakePoses poses) : IHordeBodyAim
{
    public Func<int, AiAngles>? Look { get; set; }
    public Func<int, float, float> Line { get; set; } = (_, reach) => reach;
    /// <summary>Lines traced, by body (an array, so the rig's allocation facts count only the horde's own).</summary>
    public int[] Traced { get; } = new int[64];

    public AiAngles Aim(int body) => Look?.Invoke(body) ?? new AiAngles(0f, poses.Yaw(body), 0f);

    public float LineOfFire(int body, float reach)
    {
        Traced[body]++;
        return Line(body, reach);
    }
}

/// <summary>Lines and the PVS, clear unless a fact says otherwise; counts every line.</summary>
internal sealed class FakeLines : IHordeLines
{
    public Func<AiVector, AiVector, bool> Clear { get; set; } = (_, _) => true;
    public Func<int, bool> Pvs { get; set; } = _ => true;
    public int Lines { get; private set; }

    public bool InPvs(int hero, AiVector mins, AiVector maxs) => Pvs(hero);

    public bool LineClear(AiVector from, AiVector to)
    {
        Lines++;
        return Clear(from, to);
    }
}

/// <summary>
/// The horde's brains with no engine: a level, its roster (only the robots a
/// fact puts on it), the pool, fake places, orders and lines, and a slide that
/// carries each order out (straight to each corner at <see cref="Speed"/>, as the
/// server's stand-in motion does), one server tick a <see cref="Tick"/>.
/// </summary>
internal sealed class BrainRig
{
    public const double Tps = 1 / 0.015;

    public BrainRig(HordeLevel? level = null, IAiTransport? transport = null, IAiRecordSink? sink = null,
                    Action<HordeDeciders>? register = null, int budget = HordeVision.DefaultBudget, IHordePaths? paths = null)
    {
        Level = level ?? Row(4, markers: 0);
        Roster = RobotRoster.Create(TheRules, Tps);
        Roster.Load(Level, 1);
        Pool = BodyPool.For(TheRules.Content.Horde);
        Deciders = HordeDeciders.For(Level, paths);
        register?.Invoke(Deciders);
        Aim = new FakeAim(Poses);
        Brains = HordeBrains.For(Roster, Pool, Level, TheRules, Poses, transport ?? new InlineTransport(), sink ?? new NoRecording(), Deciders, Tps, budget, Aim,
                                 paths);
    }

    public FakeAim Aim { get; }

    public HordeLevel Level { get; }
    public RobotRoster Roster { get; }
    public BodyPool Pool { get; }
    public HordeDeciders Deciders { get; }
    public FakePoses Poses { get; } = new();
    public FakeOrders Orders { get; } = new();
    public FakeLines Lines { get; } = new();
    public HordeBrains Brains { get; }
    public List<HordeSound> Sounds { get; } = [];
    public List<HeroPose> Heroes { get; } = [];
    public long Now { get; set; } = 1000;

    /// <summary>Units a second the slide moves a body.</summary>
    public float Speed { get; set; } = 300f;

    /// <summary>Whether <see cref="Tick"/> slides the bodies along their orders.</summary>
    public bool Slide { get; set; } = true;

    /// <summary>A far robot of <paramref name="template"/> at <paramref name="at"/> in its room: a pack of its own, or the newest pack's with <paramref name="joinLastPack"/>.</summary>
    public RobotId Robot(AiVector at, string template = "soldier", ulong seed = 42, bool joinLastPack = false)
    {
        int room = Level.RoomAt(at);
        var id = Roster.Add(template, 1.0, room, new PackMarker(at, 0f), seed, joinLastPack: joinLastPack);
        Assert.True(id.IsValid, "the roster is full");
        return id;
    }

    /// <summary>Lends <paramref name="id"/> the first free body, facing <paramref name="yaw"/>; answers the body.</summary>
    public int Lend(RobotId id, float yaw = 0f)
    {
        int body = 1;
        while (!Pool.IsFree(body))
            body++;
        var lease = Pool.Lend(body, id);
        var start = Roster.Promote(id.Index, lease, Now);
        Poses.Set(body, start.Position, yaw);
        return body;
    }

    /// <summary>Takes <paramref name="id"/>'s body back where it stands.</summary>
    public void Return(RobotId id)
    {
        int body = Roster.Body(id.Index);
        var state = Roster.BodyState(id.Index) with { Position = Poses.Position(body) };
        Pool.Return(Roster.Demote(id.Index, state, Now));
        Poses.Places.Remove(body);
    }

    /// <summary>A hero in seat <paramref name="seat"/> standing at <paramref name="feet"/>.</summary>
    public void Hero(int seat, AiVector feet)
    {
        Heroes.RemoveAll(h => h.Seat == seat);
        Heroes.Add(new HeroPose(seat, feet, feet + new AiVector(0f, 0f, 64f), Level.RoomAt(feet)));
    }

    /// <summary>One server tick: heroes, apply, sync, sight, hearing, the slide, publish.</summary>
    public void Tick(int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
        {
            Now++;
            Brains.Heroes(Now, Heroes.ToArray());
            Brains.Apply(Now, Orders);
            Brains.Sync(Now, Orders);
            if (Slide)
                SlideAll();
            Brains.See(Now, Lines);
            Brains.Hear(Now, Sounds.ToArray());
            Sounds.Clear();
            Brains.Publish(Now);
        }
    }

    /// <summary>Ticks until <paramref name="done"/> or <paramref name="limit"/> ticks; answers the ticks run.</summary>
    public int TickUntil(Func<bool> done, int limit = 5000)
    {
        int n = 0;
        while (!done() && n < limit)
        {
            Tick();
            n++;
        }

        return n;
    }

    /// <summary>The decider of the brain at <paramref name="body"/>'s last node (inline only).</summary>
    public HuntDecider.Node? NodeOf(int body)
    {
        var brain = Brains.Session.BrainIn(body);
        return brain?.Decider?.Doing.Leaf is { Length: > 0 } leaf
            ? Enum.GetValues<HuntDecider.Node>().First(n => HuntDecider.NameOf(n) == leaf)
            : null;
    }

    private void SlideAll()
    {
        foreach (var body in Orders.Now.Keys.ToArray())
        {
            var (move, look, expires, corner) = Orders.Now[body];
            if (!Poses.Places.TryGetValue(body, out var place))
                continue;
            if (Now >= expires || move.Kind != MoveKind.Follow || corner >= move.CornerCount)
            {
                Orders.Now.Remove(body);
                continue;
            }

            AiVector at = place.At, to = move.Corner(corner);
            float step = Speed / (float)Tps;
            AiVector d = new(to.X - at.X, to.Y - at.Y, 0f);
            float yaw = place.Yaw;
            if (d.Length <= step)
            {
                at = new AiVector(to.X, to.Y, at.Z);
                Orders.Now[body] = (move, look, expires, corner + 1);
            }
            else
            {
                at += d * (step / d.Length);
            }

            AiVector face = look.Kind is LookKind.Point or LookKind.Actor ? look.Point - at : d;
            if (look.Kind == LookKind.Actor && Brains.TryHeroEye(look.Actor, out AiVector eye))
                face = eye - at;
            if (face.Length2D > 1f)
                yaw = MathF.Atan2(face.Y, face.X) * (180f / MathF.PI);
            Poses.Set(body, at, yaw);
        }
    }
}

/// <summary>Skill levels over the core's eleven, every one at <paramref name="level"/> but those named.</summary>
internal static class Levels
{
    public static byte[] Eleven(int level = 5, params (string Key, int Level)[] set)
    {
        var l = Enumerable.Repeat((byte)level, StandardSkills.Eleven.Length).ToArray();
        foreach (var (key, v) in set)
            l[StandardSkills.Eleven.IndexOf(StandardSkills.Eleven.First(s => s.Key == key))] = (byte)v;
        return l;
    }

    public static SkillProfile Profile(byte[] eleven) =>
        new("fact", StandardSkills.Eleven, [.. eleven.Select(b => (int)b)]);

    public static ImmutableArray<int> Descent(int level = 4, params (string Key, int Level)[] set)
    {
        var skills = TheRules.Temperaments.Skills;
        var l = Enumerable.Repeat(level, skills.Count).ToArray();
        foreach (var (key, v) in set)
            l[TheRules.Temperaments.Index(key)] = v;
        return [.. l];
    }
}
