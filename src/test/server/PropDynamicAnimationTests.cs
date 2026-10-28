using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// prop_dynamic's animation against stock's CDynamicProp (props.cpp): a named
/// animation reached through the model's graph, transitions played forwards
/// and backwards, OnAnimationBegun and OnAnimationDone, the return to the
/// default animation, and the random animator. Found beside stock
/// (2026-10-27): a door model told to open snapped to its open idle, and no
/// prop's sequence reached the client at all.
/// </summary>
public class PropDynamicAnimationTests
{
    private static readonly EntityHandle Prop = new(0x0004_0371u);

    private const int IdleClosed = 0, IdleOpen = 1, Opening = 2, Free = 3, Fidget = 4, IdleLocked = 5, Locking = 6;
    private const int Looping = 0x0001;

    /// <summary>How long every sequence runs, in the rig's clock.</summary>
    private const float Duration = 1f;

    private static ModelSequence Seq(int index, string label, string activity, int weight, int flags,
                                     int entry = 0, int exit = 0, int nodeFlags = 0)
        => new(index, label, activity, flags, Duration, Array.Empty<ModelSequenceEvent>(), weight, entry, exit, nodeFlags);

    /// <summary>
    /// A door: closed, open and locked nodes, a looping idle on each, the
    /// opening (reversible) and the locking between them, a one-shot on no
    /// node, and a fidget idle. Locked is reached from closed through open.
    /// </summary>
    private static ModelDescriptor Door() => ModelDescriptor.Create(1, [], [],
        [
            Seq(IdleClosed, "idle_closed", "ACT_IDLE", 1, Looping, 1, 1),
            Seq(IdleOpen, "idle_open", "", 0, Looping, 2, 2),
            Seq(Opening, "opening", "", 0, 0, 1, 2, nodeFlags: 1),
            Seq(Free, "wave", "", 0, 0),
            Seq(Fidget, "fidget", "ACT_IDLE", 3, Looping, 1, 1),
            Seq(IdleLocked, "idle_locked", "", 0, Looping, 3, 3),
            Seq(Locking, "locking", "", 0, 0, 2, 3),
        ],
        nodeCount: 3, transitions: [0, 2, 2, 1, 0, 3, 2, 2, 0]);

    private sealed class Source(ModelDescriptor model) : IModelDescriptorSource
    {
        public ModelDescriptor? Describe(int modelIndex) => modelIndex == 1 ? model : null;
    }

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public float Now = 10f;
        public readonly ThinkScheduler Thinks = new();
        private readonly List<ScheduledThink> _due = new();

        public PropDynamic Entity => (PropDynamic)Registry.Get(Prop)!;

        /// <summary>The cycle the client would run from the prop's own net state.</summary>
        public float Cycle
        {
            get
            {
                var s = Entity.State;
                if (s.Starts == 0)
                    return 0f;
                float c = s.StartCycle + (Now - s.StartTime) * s.PlaybackRate / Duration;
                return (Door().Sequences[s.Sequence].Flags & Looping) != 0 ? c - MathF.Floor(c) : Math.Clamp(c, 0f, 0.999f);
            }
        }

        public void Think(float at)
        {
            Now = at;
            _due.Clear();
            Thinks.TakeDue(Now, _due);
            foreach (var due in _due)
                Entity.OnThink();
        }

        public (int Sequence, float Rate, float Cycle)[] Started
            => Engine.AllOf("EntityRender.PlaySequenceAt")
                     .Select(c => (c.ArgAs<int>(1), c.ArgAs<float>(2), c.ArgAs<float>(3))).ToArray();

        public int Begun => Entity.OnAnimationBegun.FireCount;
        public int Done => Entity.OnAnimationDone.FireCount;
    }

    private static Rig Spawned(params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropDynamic).Assembly);
        var models = new ModelDescriptors(_ => 1, _ => "models/door.mdl", new RecordingLog());
        models.AttachSource(new Source(Door()));
        registry.Services = Services(out var engine, models);
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine };
        engine.ReturnsEachCall("Clock.Now", () => rig.Now)
              .Returns("Precache.PrecacheModel", 3)
              .ReturnsEachCall("EntityRender.LookupSequence", () => engine.Calls.Last().ArgAs<string>(1) switch
              {
                  "idle_closed" => IdleClosed, "idle_open" => IdleOpen, "opening" => Opening, "wave" => Free, "fidget" => Fidget,
                  "idle_locked" => IdleLocked, "locking" => Locking,
                  _ => -1,
              })
              .Returns("EntityRender.SequenceState", true)
              .ReturnsEachCall("EntityRender.SequenceState.sequence", () => rig.Entity.State.Starts == 0 ? IdleClosed : rig.Entity.State.Sequence)
              .ReturnsEachCall("EntityRender.SequenceState.cycle", () => rig.Cycle)
              .ReturnsEachCall("EntityRender.SequenceState.looping", () =>
                  (Door().Sequences[rig.Entity.State.Starts == 0 ? IdleClosed : rig.Entity.State.Sequence].Flags & Looping) != 0)
              .Returns("Host.RandomFloat", 2f)
              .Returns("Host.RandomInt", 3);

        registry.KeyValue(Prop, EntityNames.PropDynamic, "model", "models/door.mdl");
        foreach (var (key, value) in keys)
            registry.KeyValue(Prop, EntityNames.PropDynamic, key, value);
        rig.Entity.BindThinks(rig.Thinks);
        registry.Spawn(Prop, EntityNames.PropDynamic);
        return rig;
    }

    [Fact]
    public void A_default_animation_across_the_graph_plays_the_transition_first()
    {
        var rig = Spawned(("DefaultAnim", "idle_open"));

        Assert.Equal([(Opening, 1f, 0f)], rig.Started);
        Assert.Equal(1, rig.Begun);
        Assert.Equal(10.1f, rig.Thinks.NextThinkTime(Prop, null), 4);
    }

    [Fact]
    public void A_transition_that_has_played_out_moves_on_to_the_goal()
    {
        var rig = Spawned(("DefaultAnim", "idle_open"));

        rig.Think(10.5f);
        Assert.Single(rig.Started);

        rig.Think(11.1f);
        Assert.Equal([(Opening, 1f, 0f), (IdleOpen, 1f, 0f)], rig.Started);
        Assert.Equal(0, rig.Done);
    }

    [Fact]
    public void Going_back_plays_the_transition_backwards_from_its_end()
    {
        var rig = Spawned(("DefaultAnim", "idle_open"));
        rig.Think(10.5f);
        rig.Think(11.1f);

        rig.Registry.Input(Prop, "SetAnimation", "idle_closed", 0f, EntityHandle.None);
        Assert.Equal((Opening, -1f, 0.999f), rig.Started[^1]);

        rig.Think(11.2f);
        rig.Think(12.2f);
        Assert.Equal((IdleClosed, 1f, 0f), rig.Started[^1]);
    }

    [Fact]
    public void A_one_shot_played_out_fires_done_and_returns_to_the_default_animation()
    {
        var rig = Spawned(("DefaultAnim", "idle_closed"));
        Assert.Equal([(IdleClosed, 1f, 0f)], rig.Started);

        rig.Registry.Input(Prop, "SetAnimation", "wave", 0f, EntityHandle.None);
        Assert.Equal((Free, 1f, 0f), rig.Started[^1]);

        rig.Think(10.1f);
        rig.Think(11.1f);

        Assert.Equal(1, rig.Done);
        Assert.Equal((IdleClosed, 1f, 0f), rig.Started[^1]);
        Assert.Equal(3, rig.Begun);
    }

    [Fact]
    public void A_name_the_model_lacks_leaves_the_first_sequence_and_begins_nothing()
    {
        var rig = Spawned(("DefaultAnim", "salute"));

        Assert.Equal([(IdleClosed, 1f, 0f)], rig.Started);
        Assert.Equal(0, rig.Begun);
    }

    [Fact]
    public void A_random_animator_waits_its_time_counted_from_a_whole_game_time_as_stocks_does()
    {
        var rig = Spawned(("RandomAnimation", "1"), ("MinAnimTime", "1"), ("MaxAnimTime", "3"));

        Assert.Empty(rig.Started);
        Assert.Equal(10f + (10f + 2f) + 0.1f, rig.Thinks.NextThinkTime(Prop, null), 4);
    }

    [Fact]
    public void A_random_animator_picks_a_weighted_idle_when_its_time_comes()
    {
        var rig = Spawned(("RandomAnimation", "1"), ("MinAnimTime", "1"), ("MaxAnimTime", "3"));

        rig.Think(22.1f);

        // ACT_IDLE: idle_closed (weight 1) and fidget (3); the roll of 3 lands on the fidget.
        Assert.Equal([(Fidget, 1f, 0f)], rig.Started);
        Assert.Equal(1, rig.Begun);
    }

    [Fact]
    public void What_the_client_runs_is_the_sequence_its_rate_and_cycle_and_when_it_started()
    {
        var rig = Spawned(("DefaultAnim", "idle_open"));
        rig.Think(10.5f);
        rig.Think(11.1f);
        rig.Registry.Input(Prop, "SetAnimation", "idle_closed", 0f, EntityHandle.None);

        var s = rig.Entity.State;
        Assert.Equal((Opening, -1f, 0.999f, 11.1f, 3), (s.Sequence, s.PlaybackRate, s.StartCycle, s.StartTime, s.Starts));
    }

    [Fact]
    public void A_goal_two_nodes_away_is_walked_to_one_transition_at_a_time()
    {
        var rig = Spawned(("DefaultAnim", "idle_locked"));
        Assert.Equal([(Opening, 1f, 0f)], rig.Started);

        rig.Think(11.1f);
        Assert.Equal((Locking, 1f, 0f), rig.Started[^1]);

        rig.Think(11.2f);
        rig.Think(12.2f);
        Assert.Equal((IdleLocked, 1f, 0f), rig.Started[^1]);
    }

    [Fact]
    public void A_looping_goal_never_finishes()
    {
        var rig = Spawned(("DefaultAnim", "idle_closed"));

        rig.Think(10.9995f);

        Assert.Equal(0, rig.Done);
        Assert.Single(rig.Started);
    }
}
