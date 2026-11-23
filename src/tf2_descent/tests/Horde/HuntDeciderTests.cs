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

        public Sim(byte[] levels, AiVector post, HordeLevel? level = null, int weapon = HordeWeapons.RocketLauncher, IHordePaths? paths = null)
        {
            Level = level ?? Row(4, markers: 0);
            Weapon = weapon;
            _s = new AiSession(new InlineTransport(), new NoRecording(), HordeSlots.Slots, HordeSlots.Sighted)
            {
                TeamCallouts = false,
                World = HordeBrains.World(0.015f),
                Curves = HordeSkillMap.Curves(),
                Deciders = HordeDeciders.For(Level, paths).Make,
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

        /// <summary>The level graph's epoch the robot's view carries (<see cref="HordeView.NavEpoch"/>); 0 for no graph, so the room route walks.</summary>
        public int NavEpoch { get; set; }

        /// <summary>Whether the slide keeps the robot on its floor (as a body does), going to each corner on the floor plane only.</summary>
        public bool OnFloor { get; set; }

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
                var view = snap.Add(Bot, new SelfState(At, default, default, 100, 0, Weapon, -1, 0, SelfFlags.Alive | SelfFlags.Grounded, Team: HordeBrains.RobotTeam, AltAmmo: NavEpoch));
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
                    var corner = OnFloor ? Last.Move.Corner0 with { Z = At.Z } : Last.Move.Corner0;
                    var d = corner - At;
                    float step = 300f * 0.015f;
                    At = d.Length <= step ? corner : At + d * (step / d.Length);
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

    /// <summary>
    /// A hero last seen in the air (a jump, a fall, a rocket jump: live, a hero put down 64 units
    /// over a door on depth 1 seed 1): the robot walks under the place on its floor and is there,
    /// so it searches; it never stands under it following for good, a stalled follower.
    /// </summary>
    [Fact]
    public void A_hero_lost_in_the_air_is_searched_for_from_the_floor_under_him()
    {
        var sim = new Sim(Levels.Eleven(), Post) { OnFloor = true };
        var lastSeen = Centre(1) + new AiVector(0f, 0f, 64f);
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
        Assert.Equal(Post.Z, sim.At.Z);
        Assert.True((sim.At - lastSeen).Length2D <= HuntDecider.ArriveUnits, $"it searches from {sim.At}, not under {lastSeen}");
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

    /// <summary>
    /// A hit from out of sight is looked for at the family's range along the hit, from where the
    /// robot stood when it took the hit as its goal. The place was worked out again from where the
    /// robot stood at every decide, so it kept its distance ahead of him: he walked the hit's way to
    /// the end of the level and never searched (seen live: a pack hurt once walked 5,000 units).
    /// </summary>
    [Fact]
    public void A_hit_from_out_of_sight_is_looked_for_at_one_place_and_then_it_goes_home()
    {
        var sim = new Sim(Levels.Eleven(5, ("tenacity", 0)), Post);
        sim.Push(AiEvent.Damaged(Bot, new Tick(sim.Tick + 1), HeroActor, 5f, new AiVector(1f, 0f, 0f), 0));
        var seen = new List<HuntDecider.Node>();
        var places = new List<AiVector>();
        for (int t = 0; t < 4000 && !(seen.Contains(HuntDecider.Node.Home) && sim.Node == HuntDecider.Node.Hold); t++)
        {
            sim.Step();
            if (seen.Count == 0 || seen[^1] != sim.Node)
                seen.Add(sim.Node);
            if (sim.Node == HuntDecider.Node.Investigate && (places.Count == 0 || places[^1] != sim.Last.Look.Point))
                places.Add(sim.Last.Look.Point);
        }

        // The stimulus reached the decider: it went to look, along the hit and away from its post.
        Assert.Contains(HuntDecider.Node.Investigate, seen);
        Assert.True(places[0].X > Post.X + HuntDecider.ArriveUnits && places[0].Y == Post.Y, $"it looked at {places[0]} from {Post}");

        Assert.Single(places);
        Assert.Equal([HuntDecider.Node.Investigate, HuntDecider.Node.Search, HuntDecider.Node.Home, HuntDecider.Node.Hold], seen.Skip(seen.IndexOf(HuntDecider.Node.Investigate)));
    }

    /// <summary>
    /// A place it cannot stand at (a sound through a wall, a hit's line ending off its floor): the
    /// route leads to the nearest place of its floor and holds there, and the search starts from
    /// there. It held there for good, going to look and never searching, so it never went home.
    /// </summary>
    [Fact]
    public void A_place_off_its_floor_is_searched_from_the_nearest_place_it_can_stand()
    {
        var nearest = Centre(1);
        var paths = new FakePaths { NearestTo = nearest };
        var sim = new Sim(Levels.Eleven(5, ("tenacity", 0)), Post, paths: paths) { NavEpoch = paths.Epoch };
        var noise = Centre(2);
        sim.Push(AiEvent.SoundHeard(Bot, new Tick(sim.Tick + 1), SoundKind.Gunfire, noise, 0.8f, ActorId.None, -1));

        int ticks = 0;
        while (sim.Node != HuntDecider.Node.Search && ticks++ < 4000)
            sim.Step();

        // The stimulus: the paths were asked for the noise and answered the nearest place.
        Assert.Contains(paths.Asked, a => a.To == noise);
        Assert.True(sim.At.DistanceTo(nearest) < sim.At.DistanceTo(noise), $"it stands at {sim.At}, nearer the noise {noise} than the nearest place {nearest}");
        Assert.True(sim.At.DistanceTo(noise) > HuntDecider.ArriveUnits);

        Assert.Equal(HuntDecider.Node.Search, sim.Node);
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
