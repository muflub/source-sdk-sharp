using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What every trigger takes from stock's CBaseTrigger (triggers.cpp): the
/// TouchTest, StartTouch, EndTouch and DisableAndEndTouch inputs, the
/// OnStartTouchAll, OnTouching and OnNotTouching outputs, the toucher as the
/// touch outputs' activator, and the list cleaned of the gone and the dead
/// when someone leaves. Found in the contract diff (2026-10-28): 19 trigger
/// classes answered none of the four inputs or three outputs, and ours fired
/// OnStartTouch, OnEndTouch and OnEndTouchAll naming the trigger as their
/// activator where stock names the toucher.
/// </summary>
public class BaseTriggerTests
{
    private static readonly EntityHandle Trigger = new(0x000B_0101u);
    private static readonly EntityHandle Sink = new(0x000B_0102u);
    private static readonly EntityHandle Player = new(0x0001_0001u);
    private static readonly EntityHandle Other = new(0x0002_0001u);

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public TestIoRelay Sink = null!;
        public bool PlayerAlive = true;
        public float Now;
        public required string ClassName;

        public TriggerMultiple Entity => (TriggerMultiple)Registry.Get(Trigger)!;

        public void Wire(string output) => Registry.KeyValue(Trigger, ClassName, output, $"sink,{output},,0,-1");

        public void Input(string name, EntityHandle caller = default)
            => Registry.Input(Registry.Resolve(Trigger), name, string.Empty, 0f, EntityHandle.None, caller);

        public (string Input, EntityHandle Activator)[] Heard()
        {
            Registry.Directory.ServiceIo(Registry.Now);
            return Sink.Inputs.Zip(Sink.Activators).ToArray();
        }
    }

    private static Rig Spawned(string className = EntityNames.TriggerMultiple, string? wait = "0")
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TriggerMultiple).Assembly);
        registry.RegisterAll(typeof(BaseTriggerTests).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        engine.Returns("Entities.IsValid", true)
              .ReturnsEachCall("Entities.IsPlayer", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Player);

        var rig = new Rig { Registry = registry, Engine = engine, Sink = null!, ClassName = className };
        engine.ReturnsEachCall("Players.IsAlive", () => rig.PlayerAlive)
              .ReturnsEachCall("Clock.Now", () => rig.Now)
              .ReturnsEachCall("Players.GetTeam", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Player ? 2 : -1);

        registry.KeyValue(Sink, "test_io_relay", "targetname", "sink");
        registry.Spawn(Sink, "test_io_relay");
        rig.Sink = (TestIoRelay)registry.Get(Sink)!;

        registry.KeyValue(Trigger, className, "origin", "0 0 0");
        if (wait is not null)
            registry.KeyValue(Trigger, className, "wait", wait);
        registry.Spawn(Trigger, className);
        return rig;
    }

    [Fact]
    public void TouchTest_says_whether_anything_is_inside_from_the_trigger()
    {
        var rig = Spawned();
        rig.Wire("OnTouching");
        rig.Wire("OnNotTouching");

        rig.Input("TouchTest");
        rig.Registry.Touch(Trigger, Player);
        rig.Input("TouchTest");

        Assert.Equal(new[] { ("OnNotTouching", Trigger), ("OnTouching", Trigger) }, rig.Heard());
    }

    [Fact]
    public void A_disabled_trigger_answers_no_TouchTest()
    {
        var rig = Spawned();
        rig.Wire("OnTouching");
        rig.Wire("OnNotTouching");

        rig.Input("Disable");
        rig.Input("TouchTest");

        Assert.Empty(rig.Heard());
    }

    /// <summary>OnStartTouch for each arrival, OnStartTouchAll for the first; both name the toucher.</summary>
    [Fact]
    public void OnStartTouchAll_is_the_first_arrival_and_the_toucher_is_the_activator()
    {
        var rig = Spawned();
        rig.Wire("OnStartTouch");
        rig.Wire("OnStartTouchAll");

        rig.Registry.Touch(Trigger, Player);
        rig.Registry.Touch(Trigger, Other);

        Assert.Equal(new[] { ("OnStartTouch", Player), ("OnStartTouchAll", Player), ("OnStartTouch", Other) }, rig.Heard());
    }

    [Fact]
    public void The_leave_outputs_name_who_left()
    {
        var rig = Spawned();
        rig.Registry.Touch(Trigger, Player);
        rig.Wire("OnEndTouch");
        rig.Wire("OnEndTouchAll");

        rig.Registry.EndTouch(Trigger, Player);

        Assert.Equal(new[] { ("OnEndTouch", Player), ("OnEndTouchAll", Player) }, rig.Heard());
    }

    /// <summary>EndTouch drops a dead player from the list, so the last live one leaving empties it.</summary>
    [Fact]
    public void A_dead_player_inside_does_not_keep_it_occupied()
    {
        var rig = Spawned();
        rig.Registry.Touch(Trigger, Player);
        rig.Registry.Touch(Trigger, Other);
        rig.PlayerAlive = false;
        rig.Wire("OnEndTouchAll");

        rig.Registry.EndTouch(Trigger, Other);

        Assert.Equal(new[] { ("OnEndTouchAll", Other) }, rig.Heard());
    }

    [Fact]
    public void The_StartTouch_and_EndTouch_inputs_touch_as_their_caller()
    {
        var rig = Spawned();
        rig.Wire("OnStartTouch");
        rig.Wire("OnEndTouch");

        rig.Input("StartTouch", caller: Other);
        Assert.Equal(1, rig.Entity.OnTrigger.FireCount);
        rig.Input("EndTouch", caller: Other);
        rig.Input("StartTouch");

        Assert.Equal(new[] { ("OnStartTouch", Other), ("OnEndTouch", Other) }, rig.Heard());
    }

    [Fact]
    public void DisableAndEndTouch_sends_everyone_out_then_turns_it_off()
    {
        var rig = Spawned();
        rig.Registry.Touch(Trigger, Player);
        rig.Registry.Touch(Trigger, Other);
        rig.Wire("OnEndTouch");
        rig.Wire("OnEndTouchAll");

        rig.Input("DisableAndEndTouch");

        var heard = rig.Heard();
        Assert.Equal(2, heard.Count(h => h.Input == "OnEndTouch"));
        Assert.Single(heard, h => h.Input == "OnEndTouchAll");

        int fired = rig.Entity.OnTrigger.FireCount;
        rig.Registry.Touch(Trigger, Player);
        Assert.Equal(fired, rig.Entity.OnTrigger.FireCount);
        Assert.Empty(rig.Heard().Skip(heard.Length));
    }

    /// <summary>The caller an output's delivery carries reaches an input that takes InputData.</summary>
    [Fact]
    public void An_outputs_delivery_carries_its_caller()
    {
        var rig = Spawned();
        rig.Registry.KeyValue(Sink, "test_io_relay", "OnTrigger", $"{EntityNames.TriggerMultiple},StartTouch,,0,-1");

        rig.Sink.OnTrigger.Fire(Player);
        rig.Registry.Directory.ServiceIo(rig.Registry.Now);

        Assert.Equal(1, rig.Entity.OnTrigger.FireCount);
        rig.Wire("OnEndTouch");
        rig.Input("EndTouch", caller: Sink);
        Assert.Equal(new[] { ("OnEndTouch", Sink) }, rig.Heard());
    }

    /// <summary>
    /// CTriggerMultiple's MultiTouch runs every frame of a touch, so whoever
    /// stays inside sets it off again once the wait is over, as the activator.
    /// </summary>
    [Fact]
    public void A_trigger_multiple_fires_again_for_whoever_stays_once_its_wait_is_over()
    {
        var rig = Spawned(wait: "1");
        rig.Wire("OnTrigger");
        Assert.True(rig.Entity.TouchEveryTick);

        rig.Registry.Touch(Trigger, Player);
        rig.Now = 0.5f;
        rig.Registry.Touch(Trigger, Player);
        rig.Now = 1.25f;
        rig.Registry.Touch(Trigger, Player);

        Assert.Equal(new[] { ("OnTrigger", Player), ("OnTrigger", Player) }, rig.Heard());
    }

    [Fact]
    public void A_trigger_multiple_with_no_wait_waits_a_fifth_of_a_second()
    {
        var rig = Spawned();

        rig.Registry.Touch(Trigger, Player);
        rig.Now = 0.15f;
        rig.Registry.Touch(Trigger, Player);
        Assert.Equal(1, rig.Entity.OnTrigger.FireCount);

        rig.Now = 0.25f;
        rig.Registry.Touch(Trigger, Player);
        Assert.Equal(2, rig.Entity.OnTrigger.FireCount);
    }

    [Fact]
    public void A_trigger_multiple_with_a_negative_wait_fires_once_and_goes()
    {
        var rig = Spawned(wait: "-1");
        int pending = rig.Registry.Directory.PendingRemovalCount;

        rig.Registry.Touch(Trigger, Player);
        rig.Now = 5f;
        rig.Registry.Touch(Trigger, Player);

        Assert.Equal(1, rig.Entity.OnTrigger.FireCount);
        Assert.Equal(pending + 1, rig.Registry.Directory.PendingRemovalCount);
    }

    /// <summary>
    /// CBaseTrigger has no wait: two arrivals at the same moment are both
    /// acted on. The base's fifth of a second dropped the second of two
    /// crates in a trigger_remove, and of two players in a teleport.
    /// </summary>
    [Fact]
    public void A_trigger_without_a_wait_of_its_own_acts_on_every_arrival()
    {
        var rig = Spawned(EntityNames.TriggerRemove, wait: null);
        rig.Wire("OnRemove");

        rig.Registry.Touch(Trigger, Other);
        rig.Registry.Touch(Trigger, Sink);

        Assert.Equal(new[] { ("OnRemove", Other), ("OnRemove", Sink) }, rig.Heard());
    }
}
