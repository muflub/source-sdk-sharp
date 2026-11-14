using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The horde's first decider (plan_descent_logic HO4) on the AI core with no
/// engine: one robot in an inline session over a row of rooms, its views built
/// by the fact and its moves carried out by a slide, so each behaviour is the
/// decider's alone. Hold at the post; chase a hero in sight along the room
/// route; stand and face one in range; go to the last known place when sight
/// is lost and search there for Tenacity's time; investigate a sound; walk home.
/// </summary>
public sealed class HuntDeciderTests
{
    private static readonly ActorId Bot = new(1, 1);
    private static readonly ActorId HeroActor = new(HordeSlots.FirstHero, 1);

    /// <summary>One robot's brain, inline, and what the fact feeds it.</summary>
    private sealed class Sim
    {
        private readonly AiSession _s;
        private int _tick = 100;

        public Sim(byte[] levels, AiVector post, HordeLevel? level = null, int weapon = HordeWeapons.RocketLauncher)
        {
            Level = level ?? Row(4, markers: 0);
            Weapon = weapon;
            _s = new AiSession(new InlineTransport(), new NoRecording(), HordeSlots.Slots, HordeSlots.Sighted)
            {
                TeamCallouts = false,
                World = HordeBrains.World(0.015f),
                Curves = HordeSkillMap.Curves(),
                Deciders = HordeDeciders.For(Level).Make,
            };
            _s.AddBot(Bot, 42, levels);
            At = post;
        }

        public HordeLevel Level { get; }
        public int Weapon { get; }
        public AiVector At { get; private set; }
        public IntentCommand Last { get; private set; }
        public List<IntentCommand> Intents { get; } = [];
        public int Tick => _tick;

        /// <summary>A hero in sight at <paramref name="hero"/> this tick; none for no sight.</summary>
        public AiVector? Seen { get; set; }

        /// <summary>The node the last decide ended in; <see cref="HuntDecider.Node.Hunt"/> before the first.</summary>
        public HuntDecider.Node Node => _s.BrainIn(1)!.Decider!.Doing.Leaf is { } leaf
            ? Enum.GetValues<HuntDecider.Node>().First(n => HuntDecider.NameOf(n) == leaf)
            : HuntDecider.Node.Hunt;

        public void Push(AiEvent e) => _s.Push(e);

        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                _tick++;
                AiSnapshot snap = _s.Snapshots.BeginWrite(new Tick(_tick));
                var view = snap.Add(Bot, new SelfState(At, default, default, 100, 0, Weapon, -1, 0, SelfFlags.Alive | SelfFlags.Grounded, Team: HordeBrains.RobotTeam));
                if (Seen is { } h)
                    view.AddSighted(new ActorFacts(HeroActor, h, default, -1, ActorActivity.None, 1f, Team: HordeBrains.HeroTeam));
                _s.Publish(snap);
                if (_s.TryLatestIntent(1, out var intent) && intent.Seq != Last.Seq)
                {
                    Last = intent;
                    Intents.Add(intent);
                }

                // The slide: 300 units a second to the first corner.
                if (Last.Move.Kind == MoveKind.Follow && !Last.ExpiredAt(new Tick(_tick)))
                {
                    var d = Last.Move.Corner0 - At;
                    float step = 300f * 0.015f;
                    At = d.Length <= step ? Last.Move.Corner0 : At + d * (step / d.Length);
                }
            }
        }
    }

    private static readonly AiVector Post = Centre(0);

    [Fact]
    public void With_nothing_seen_or_heard_it_holds_at_its_post()
    {
        var sim = new Sim(Levels.Eleven(), Post);
        sim.Step(30);
        Assert.Equal(HuntDecider.Node.Hold, sim.Node);
        Assert.All(sim.Intents, i => Assert.Equal(MoveKind.Hold, i.Move.Kind));
        Assert.Equal(Post, sim.At);
    }

    [Fact]
    public void A_hero_in_sight_beyond_its_range_is_chased_through_the_doors_and_faced()
    {
        var sim = new Sim(Levels.Eleven(), Post);
        var hero = Centre(3);
        sim.Seen = hero;
        sim.Step(12);
        Assert.Equal(HuntDecider.Node.Chase, sim.Node);
        Assert.Equal(MoveKind.Follow, sim.Last.Move.Kind);
        Assert.Equal(RowDoor(0).Position, sim.Last.Move.Corner0);
        Assert.Equal(LookKind.Actor, sim.Last.Look.Kind);
        Assert.Equal(HeroActor, sim.Last.Look.Actor);
        Assert.True(sim.At.DistanceTo(hero) < Post.DistanceTo(hero), "it came towards the hero");
    }

    [Fact]
    public void A_hero_within_its_range_is_faced_from_where_it_stands()
    {
        var sim = new Sim(Levels.Eleven(), Post, weapon: HordeWeapons.SniperRifle);
        sim.Seen = Post + new AiVector(200f, 0f, 0f);
        sim.Step(12);
        Assert.Equal(HuntDecider.Node.Engage, sim.Node);
        Assert.Equal(MoveKind.Hold, sim.Last.Move.Kind);
        Assert.Equal(LookKind.Actor, sim.Last.Look.Kind);
    }

    [Fact]
    public void Sight_lost_sends_it_to_the_last_known_place_to_search()
    {
        var sim = new Sim(Levels.Eleven(), Post);
        var lastSeen = Centre(1);
        sim.Seen = lastSeen;
        sim.Push(AiEvent.SightGained(Bot, new Tick(sim.Tick + 1), HeroActor, lastSeen, default, 1f));
        sim.Step(6);
        sim.Seen = null;
        sim.Push(AiEvent.SightLost(Bot, new Tick(sim.Tick + 1), HeroActor, lastSeen, default));
        sim.Step(6);
        Assert.Equal(HuntDecider.Node.LastKnown, sim.Node);
        int ticks = 0;
        while (sim.Node != HuntDecider.Node.Search && ticks++ < 2000)
            sim.Step();
        Assert.Equal(HuntDecider.Node.Search, sim.Node);
        Assert.True(sim.At.DistanceTo(lastSeen) <= HuntDecider.ArriveUnits, $"it searches {sim.At.DistanceTo(lastSeen):0} from the place");
    }

    /// <summary>Ticks spent searching the last known place at Tenacity <paramref name="tenacity"/>.</summary>
    private static int SearchTicks(int tenacity)
    {
        var sim = new Sim(Levels.Eleven(5, ("tenacity", tenacity)), Post);
        var lastSeen = Post + new AiVector(100f, 0f, 0f);
        sim.Seen = lastSeen;
        sim.Step(6);
        sim.Seen = null;
        sim.Push(AiEvent.SightLost(Bot, new Tick(sim.Tick + 1), HeroActor, lastSeen, default));
        int searching = 0;
        for (int t = 0; t < 6000; t++)
        {
            sim.Step();
            if (sim.Node == HuntDecider.Node.Search)
                searching++;
            else if (searching > 0)
                break;
        }

        return searching;
    }

    [Fact]
    public void Search_time_rises_with_Tenacity()
    {
        int low = SearchTicks(0), mid = SearchTicks(5), high = SearchTicks(10);
        Assert.True(low > 0, "the control: a robot of Tenacity 0 still searches");
        Assert.True(mid > low, $"tenacity 5 searched {mid} ticks, tenacity 0 {low}");
        Assert.True(high > mid, $"tenacity 10 searched {high} ticks, tenacity 5 {mid}");
    }

    [Fact]
    public void A_heard_sound_is_investigated_at_a_walk()
    {
        var sim = new Sim(Levels.Eleven(), Post);
        var noise = Centre(2);
        sim.Push(AiEvent.SoundHeard(Bot, new Tick(sim.Tick + 1), SoundKind.Gunfire, noise, 0.8f, HeroActor, -1));
        sim.Step(12);
        Assert.Equal(HuntDecider.Node.Investigate, sim.Node);
        Assert.Equal(MoveKind.Follow, sim.Last.Move.Kind);
        Assert.Equal(MoveSpeed.Walk, sim.Last.Move.Speed);
        Assert.Equal(noise, sim.Last.Move.Corner(sim.Last.Move.CornerCount - 1));
    }

    [Fact]
    public void After_its_search_it_walks_home_and_holds_there()
    {
        var sim = new Sim(Levels.Eleven(5, ("tenacity", 0)), Post);
        var noise = Centre(1);
        sim.Push(AiEvent.SoundHeard(Bot, new Tick(sim.Tick + 1), SoundKind.Gunfire, noise, 0.8f, ActorId.None, -1));
        var seen = new List<HuntDecider.Node>();
        for (int t = 0; t < 4000 && !(seen.Contains(HuntDecider.Node.Home) && sim.Node == HuntDecider.Node.Hold); t++)
        {
            sim.Step();
            if (seen.Count == 0 || seen[^1] != sim.Node)
                seen.Add(sim.Node);
        }

        Assert.Equal([HuntDecider.Node.Investigate, HuntDecider.Node.Search, HuntDecider.Node.Home, HuntDecider.Node.Hold], seen.Skip(seen.IndexOf(HuntDecider.Node.Investigate)));
        Assert.True(sim.At.DistanceTo(Post) <= HuntDecider.ArriveUnits, $"it holds {sim.At.DistanceTo(Post):0} from its post");
    }

    [Fact]
    public void It_never_says_or_fires_anything()
    {
        var sim = new Sim(Levels.Eleven(), Post);
        sim.Seen = Centre(2);
        sim.Step(60);
        sim.Seen = null;
        sim.Push(AiEvent.SightLost(Bot, new Tick(sim.Tick + 1), HeroActor, Centre(2), default));
        sim.Step(600);
        Assert.True(sim.Intents.Count > 50, $"the control: {sim.Intents.Count} intents");
        Assert.All(sim.Intents, i =>
        {
            Assert.False(i.Say.Says);
            Assert.Equal(ActionKind.None, i.Action.Kind);
        });
    }
}
