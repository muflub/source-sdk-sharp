using System.Collections.Immutable;
using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Families;
using TF2.Descent.Ai.Level;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// A small engine-free arena for the family deciders (HO6's inline simulation): robots as brains
/// of an inline session over one big hall, with the families registered as the game registers
/// them; each robot's view built here as the horde's brains build it (its gun's look, its life
/// and most, its line of fire, the heroes it sees, its pack-mates with their family and life:
/// <see cref="HordeView"/>), and its moves carried out by a slide: a follow to its first corner,
/// a strafe or a retreat along its direction, at <see cref="Speed"/>. A robot's gun turns at
/// once to its look. Every action is kept with the look it was asked with.
/// </summary>
internal sealed class FamilySim
{
    public const float Tick = 0.015f;

    private readonly AiSession _s;
    private int _tick = 100;

    public FamilySim(HordeLevel? level = null)
    {
        Level = level ?? Hall();
        _s = new AiSession(new InlineTransport(), new NoRecording(), HordeSlots.Slots, HordeSlots.Sighted)
        {
            TeamCallouts = false,
            World = HordeBrains.World(Tick),
            Curves = HordeSkillMap.Curves(),
            Deciders = HordeFamilies.Deciders(Level, TheRules).Make,
        };
    }

    /// <summary>One room 4096 square: an arena with no doors.</summary>
    public static HordeLevel Hall() =>
        HordeLevel.Create(7, 1, [new HordeRoom(0, At(0, 0, -64), At(4096, 4096, 1024), ImmutableArray<PackMarker>.Empty)], []);

    public HordeLevel Level { get; }
    public float Speed { get; set; } = 300f;
    public int Now => _tick;

    /// <summary>The session's faulted steps, and the last fault's text (a decider that threw).</summary>
    public long Faulted => _s.Counters.StepsFaulted;

    public string? LastFault => _s.Counters.LastFault;
    public List<Bot> Bots { get; } = [];
    public List<HeroIn> Heroes { get; } = [];

    /// <summary>How far a robot's line of fire runs from its eye along its gun's look; the whole reach by default.</summary>
    public Func<Bot, float> LineOfFire { get; set; } = _ => HordeView.FireLineReach;

    /// <summary>
    /// The beam the hands would run (<c>RobotAttacks</c>, whose own facts hold its numbers): an ask for
    /// the heal on a pack-mate the look names within the rules' reach holds the beam for the rules'
    /// hold, and the patient gains the rules' rate (at level 1) each tick, to its most.
    /// </summary>
    public bool Beams { get; set; } = true;

    public static float HealRate => (float)TheRules.Attacks.HealPerSecond(1);

    public static int HoldTicks => (int)TF2.Descent.Rules.Robots.RobotAttackRules.Ticks(TheRules.Attacks.Settings.Heal.HoldSeconds, 1 / Tick, 1);

    /// <summary>Every action asked, with the robot, the tick, and the look of the intent that asked it.</summary>
    public List<(Bot Bot, int Tick, ActionIntent Action, LookIntent Look)> Acts { get; } = [];

    public sealed class Bot
    {
        public required ActorId Id { get; init; }
        public required int Family { get; init; }
        public int Pack { get; set; }
        public AiVector At { get; set; }
        public AiVector Velocity { get; set; }
        public float Life { get; set; } = 200f;
        public float Most { get; set; } = 200f;
        public AiAngles Eye { get; set; }

        /// <summary>The robot's scale: its eye stands this many times the brains' eye height (a giant's look turns from there, as the server's motion turns it).</summary>
        public float Scale { get; set; } = 1f;

        /// <summary>A robot that does not move (its brain still decides).</summary>
        public bool Still { get; set; }

        /// <summary>The pack-mate this robot's beam is on, and the last tick it lasts (the hands' hold, as the sink keeps it).</summary>
        public Bot? Beaming { get; set; }
        public int BeamUntil { get; set; }
        public IntentCommand Last { get; set; }
        public List<IntentCommand> Intents { get; } = [];

        public FamilyNode? Node(FamilySim sim) => sim._s.BrainIn(Id.Slot)?.Decider?.Doing.Leaf is { Length: > 0 } leaf
            ? Enum.GetValues<FamilyNode>().First(n => FamilyDecider.NameOf(n) == leaf)
            : null;

        public IDecider? Decider(FamilySim sim) => sim._s.BrainIn(Id.Slot)?.Decider;

        /// <summary>The family's decider the brain runs (HO13: a boss's and the Tank's own, out of the horde's wrapper).</summary>
        public IDecider? Fights(FamilySim sim) => Decider(sim) is { } d ? HordeDeciders.Unwrap(d) : null;
    }

    public sealed class HeroIn
    {
        public required ActorId Id { get; init; }
        public AiVector At { get; set; }
        public AiVector Velocity { get; set; }
        public bool Seen { get; set; } = true;

        /// <summary>The yaw the hero's eyes face, degrees (the view carries it: <see cref="HordeView.Hero"/>).</summary>
        public float Yaw { get; set; }
    }

    /// <summary>A robot of family <paramref name="family"/> at <paramref name="at"/> with the core's eleven <paramref name="levels"/>; its home at <paramref name="home"/> (a roster post), where it stands first by default.</summary>
    public Bot Robot(int family, AiVector at, byte[]? levels = null, int pack = 0, ulong seed = 42, AiVector? home = null)
    {
        var id = new ActorId(Bots.Count + 1, 1);
        _s.AddBot(id, seed + (ulong)Bots.Count, levels ?? Levels.Eleven(), home: home);
        var bot = new Bot { Id = id, Family = family, Pack = pack, At = at };
        Bots.Add(bot);
        return bot;
    }

    public HeroIn Hero(AiVector at)
    {
        var h = new HeroIn { Id = new ActorId(HordeSlots.FirstHero + Heroes.Count, 1), At = at };
        Heroes.Add(h);
        return h;
    }

    /// <summary>The eye of a robot (the brains' eye height at its scale) and of a hero.</summary>
    public static AiVector EyeOf(Bot b) => b.At + new AiVector(0f, 0f, HordeBrains.EyeHeight * b.Scale);

    public static AiVector EyeOf(HeroIn h) => h.At + new AiVector(0f, 0f, 64f);

    /// <summary>The engine angles of a look from <paramref name="eye"/> at <paramref name="point"/>.</summary>
    public static AiAngles Angles(AiVector eye, AiVector point)
    {
        var d = point - eye;
        float yaw = MathF.Atan2(d.Y, d.X) * (180f / MathF.PI);
        float pitch = -MathF.Atan2(d.Z, d.Length2D) * (180f / MathF.PI);
        return new AiAngles(pitch, yaw, 0f);
    }

    /// <summary>One server tick: the views published, the newest intents taken, the actions kept, the slide.</summary>
    public void Step(int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
        {
            _tick++;
            var tick = new Tick(_tick);
            AiSnapshot snap = _s.Snapshots.BeginWrite(tick);
            foreach (var b in Bots)
            {
                int line = DecideSchedule.IsDecideTick(tick, b.Id.Slot) ? (int)MathF.Min(HordeView.FireLineReach, LineOfFire(b)) : HordeView.NotTraced;
                var self = new SelfState(b.At, b.Velocity, b.Eye, (int)MathF.Ceiling(b.Life), (int)MathF.Ceiling(b.Most), b.Family, -1, line,
                                         SelfFlags.Alive | SelfFlags.Grounded, Team: HordeBrains.RobotTeam, Metal: HordeView.Scaled(b.Scale));
                var view = snap.Add(b.Id, self);
                foreach (var h in Heroes)
                    if (h.Seen)
                        view.AddSighted(new ActorFacts(h.Id, h.At, h.Velocity, HordeView.Hero(h.Yaw), ActorActivity.None, 1f, Team: HordeBrains.HeroTeam));
                foreach (var m in Bots.Where(m => m != b && m.Pack == b.Pack).OrderBy(m => m.At.DistanceTo(b.At)).ThenBy(m => m.Id.Slot).Take(HordeView.PackMates))
                    view.AddSighted(new ActorFacts(m.Id, m.At, m.Velocity, HordeView.Mate(m.Family, m.Life / m.Most), ActorActivity.None, 1f, Team: HordeBrains.RobotTeam));
            }

            _s.Publish(snap);
            foreach (var b in Bots)
            {
                if (_s.TryLatestIntent(b.Id.Slot, out var intent) && (intent.Seq != b.Last.Seq || b.Last.Bot != intent.Bot))
                {
                    b.Last = intent;
                    b.Intents.Add(intent);
                    if (intent.Action.Kind != ActionKind.None)
                    {
                        Acts.Add((b, _tick, intent.Action, intent.Look));
                        Beam(b, intent);
                    }
                }

                SlideAndTurn(b, tick);
                if (b.Beaming is { } patient && _tick <= b.BeamUntil)
                    patient.Life = MathF.Min(patient.Most, patient.Life + HealRate * Tick);
            }
        }
    }

    private void Beam(Bot b, in IntentCommand intent)
    {
        if (!Beams || intent.Action.Kind != ActionKind.Ability || intent.Action.WeaponSlot != HordeAbilities.Heal || intent.Look.Kind != LookKind.Actor)
            return;
        var id = intent.Look.Actor;
        var mate = Bots.FirstOrDefault(m => m.Id == id && m != b && m.Pack == b.Pack);
        if (mate is null || mate.At.DistanceTo(b.At) > TheRules.Attacks.Settings.Heal.Range)
            return;
        b.Beaming = mate;
        b.BeamUntil = _tick + HoldTicks;
    }

    /// <summary>Moves each hero by its velocity for a tick.</summary>
    public void MoveHeroes()
    {
        foreach (var h in Heroes)
            h.At += h.Velocity * Tick;
    }

    public void Push(AiEvent e) => _s.Push(e);

    private void SlideAndTurn(Bot b, Tick tick)
    {
        var last = b.Last;
        AiVector before = b.At;
        if (!last.ExpiredAt(tick) && last.Bot == b.Id)
        {
            float step = b.Still ? 0f : Speed * Tick;
            switch (last.Move.Kind)
            {
                case MoveKind.Follow when last.Move.CornerCount > 0:
                    var d = last.Move.Corner0 - b.At;
                    d = new AiVector(d.X, d.Y, 0f);
                    b.At = d.Length <= step ? new AiVector(last.Move.Corner0.X, last.Move.Corner0.Y, b.At.Z) : b.At + d * (step / d.Length);
                    break;
                case MoveKind.Strafe or MoveKind.Retreat when last.Move.Direction.Length2D > 0f:
                    b.At += last.Move.Direction * step;
                    break;
            }

            AiVector? point = last.Look.Kind switch
            {
                LookKind.Point => last.Look.Point,
                LookKind.Actor => Heroes.FirstOrDefault(h => h.Id == last.Look.Actor) is { } hero ? EyeOf(hero)
                    : Bots.FirstOrDefault(m => m.Id == last.Look.Actor) is { } mate ? EyeOf(mate) : last.Look.Point,
                _ => null,
            };
            if (point is { } p)
                b.Eye = Angles(EyeOf(b), p);
        }

        b.Velocity = (b.At - before) * (1f / Tick);
    }
}
