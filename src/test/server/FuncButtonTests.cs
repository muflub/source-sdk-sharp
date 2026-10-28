using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// func_button and func_rot_button against stock's CBaseButton and CRotButton
/// (buttons.cpp). Found beside stock (2026-10-27): the button never moved, a
/// +USE pressed it without the use spawnflag, it had no toggle, touch, lock
/// sounds, noise, sparks or PressIn/PressOut, and it was locked by a
/// StartDisabled key stock does not have (stock's is SF_BUTTON_LOCKED).
/// </summary>
public class FuncButtonTests
{
    private const int DontMove = 1, Toggle = 32, TouchActivates = 256, DamageActivates = 512,
                      UseActivates = 1024, Locked = 2048, SparkIfOff = 4096;

    private static readonly EntityHandle Button = new(0x0004_0361u);
    private static readonly EntityHandle Player = new(0x0001_0001u);
    private static readonly EntityHandle Crate = new(0x0004_0362u);

    private static readonly Vector At = new(100f, 0f, 40f);

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public float Now = 10f;
        public readonly ThinkScheduler Thinks = new();
        private readonly List<ScheduledThink> _due = new();

        public BaseButton Entity => (BaseButton)Registry.Get(Button)!;

        public void Think(float at)
        {
            Now = at;
            _due.Clear();
            Thinks.TakeDue(Now, _due);
            foreach (var due in _due)
            {
                if (due.Context is null)
                    Entity.OnThink();
                else
                    Entity.OnThink(due.Context);
            }
        }

        public void Use() => Input("Use");

        public void Input(string name) => Registry.Input(Button, name, "", 0f, Player);

        public void Arrive() => Entity.OnMoveDone();

        public EngineCall[] Moves => Engine.AllOf("BrushMovement.MoveToPosition").ToArray();
        public EngineCall[] Turns => Engine.AllOf("BrushMovement.MoveToAngles").ToArray();
        public string[] Sounds => Engine.AllOf("Sound.EntityEmitSound").Select(c => (string)c.Args[1]!).ToArray();
    }

    private static Rig Spawned(string className, int flags, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncButton).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine };
        engine.ReturnsEachCall("Entities.GetSpawnFlags", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Button ? flags : 0)
              .ReturnsEachCall("Clock.Now", () => rig.Now)
              .Returns("Entities.GetSpawnOrigin", At)
              .Returns("Entities.GetSpawnAngles", new Vector(0f, 90f, 0f))
              .Returns("Entities.GetBoundsSize", new Vector(10f, 34f, 26f))
              .Returns("Entities.GetWorldSpaceCenter", At)
              .Returns("Host.RandomFloat", 0.5f)
              .ReturnsEachCall("Entities.IsPlayer", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Player);

        foreach (var (key, value) in keys)
            registry.KeyValue(Button, className, key, value);
        registry.KeyValue(Button, className, "model", "*7");
        rig.Entity.BindThinks(rig.Thinks);
        registry.Spawn(Button, className);
        return rig;
    }

    private static Rig Spawned(int flags, params (string Key, string Value)[] keys)
        => Spawned(EntityNames.FuncButton, flags, keys);

    //-------------------------------------------------------------------------
    // Travel
    //-------------------------------------------------------------------------

    [Fact]
    public void A_press_travels_its_depth_along_movedir_less_the_lip()
    {
        // movedir 0 90 0 is +y; the brush is 34 deep there, less the engine's 2, less a lip of 4.
        var rig = Spawned(UseActivates, ("movedir", "0 90 0"));

        rig.Use();

        var move = Assert.Single(rig.Moves);
        Assert.Equal(At + new Vector(0f, 28f, 0f), move.ArgAs<Vector>(1));
        Assert.Equal(40f, move.ArgAs<float>(2));
    }

    [Fact]
    public void The_lip_key_sets_how_much_stays_out()
    {
        var rig = Spawned(UseActivates, ("movedir", "0 90 0"), ("lip", "10"));

        rig.Use();

        Assert.Equal(At + new Vector(0f, 22f, 0f), rig.Moves[0].ArgAs<Vector>(1));
    }

    [Fact]
    public void A_dont_move_button_goes_nowhere()
    {
        var rig = Spawned(UseActivates | DontMove, ("movedir", "0 90 0"));

        rig.Use();

        Assert.Equal(At, rig.Moves[0].ArgAs<Vector>(1));
    }

    [Fact]
    public void In_it_fires_OnIn_then_waits_and_comes_out_firing_OnOut()
    {
        var rig = Spawned(UseActivates, ("wait", "2"));

        rig.Use();
        Assert.Equal(1, rig.Entity.OnPressed.FireCount);
        Assert.Equal(0, rig.Entity.OnIn.FireCount);

        rig.Arrive();
        Assert.Equal(1, rig.Entity.OnIn.FireCount);

        rig.Think(11.9f);
        Assert.Single(rig.Moves);
        rig.Think(12.0f);
        Assert.Equal(At, rig.Moves[^1].ArgAs<Vector>(1));

        rig.Arrive();
        Assert.Equal(1, rig.Entity.OnOut.FireCount);
    }

    [Fact]
    public void A_use_while_it_travels_or_waits_in_does_nothing()
    {
        var rig = Spawned(UseActivates);

        rig.Use();
        rig.Use();
        rig.Arrive();
        rig.Use();

        Assert.Equal(1, rig.Entity.OnPressed.FireCount);
        Assert.Single(rig.Moves);
    }

    [Fact]
    public void A_toggle_stays_in_until_used_again_and_returns_with_its_noise()
    {
        var rig = Spawned(UseActivates | Toggle, ("sounds", "14"));
        rig.Use();
        rig.Arrive();
        rig.Think(30f);
        Assert.Single(rig.Moves);

        rig.Use();

        Assert.Equal(2, rig.Entity.OnPressed.FireCount);
        Assert.Equal(At, rig.Moves[^1].ArgAs<Vector>(1));
        Assert.Equal(["Buttons.snd14", "Buttons.snd14"], rig.Sounds);
    }

    [Fact]
    public void A_wait_of_minus_one_stays_in_for_good()
    {
        var rig = Spawned(UseActivates, ("wait", "-1"));
        rig.Use();
        rig.Arrive();

        rig.Think(100f);
        rig.Use();

        Assert.Single(rig.Moves);
        Assert.Equal(1, rig.Entity.OnPressed.FireCount);
    }

    //-------------------------------------------------------------------------
    // What presses it
    //-------------------------------------------------------------------------

    [Fact]
    public void Without_the_use_flag_a_use_does_nothing()
    {
        var rig = Spawned(0);

        rig.Use();

        Assert.Empty(rig.Moves);
        Assert.Equal(0, rig.Entity.OnPressed.FireCount);
    }

    [Fact]
    public void A_player_s_touch_presses_a_touch_button_and_a_crate_s_does_not()
    {
        var rig = Spawned(TouchActivates);

        rig.Entity.OnTouch(Crate);
        Assert.Empty(rig.Moves);

        rig.Entity.OnTouch(Player);
        Assert.Single(rig.Moves);
    }

    [Fact]
    public void Damage_reports_first_and_presses_only_with_the_flag()
    {
        var rig = Spawned(0);
        Assert.Equal(0, rig.Entity.OnDamaged(Player, 10f, DamageType.Bullet));
        Assert.Equal(1, rig.Entity.Damaged.FireCount);
        Assert.Empty(rig.Moves);

        var flagged = Spawned(DamageActivates);
        flagged.Entity.OnDamaged(Player, 10f, DamageType.Bullet);
        Assert.Single(flagged.Moves);
    }

    [Fact]
    public void The_outputs_bind_under_the_names_a_map_writes()
    {
        var button = Spawned(0).Entity;

        Assert.Equal(("OnDamaged", "OnPressed", "OnUseLocked", "OnIn", "OnOut"),
                     (button.Damaged.Name, button.OnPressed.Name, button.OnUseLocked.Name, button.OnIn.Name, button.OnOut.Name));
    }

    [Fact]
    public void Press_returns_one_that_is_in_and_PressIn_and_PressOut_only_go_their_way()
    {
        var rig = Spawned(0, ("wait", "-1"));

        rig.Input("PressOut");
        Assert.Empty(rig.Moves);

        rig.Input("PressIn");
        rig.Arrive();
        rig.Input("PressIn");
        Assert.Single(rig.Moves);

        rig.Input("Press");
        Assert.Equal(At, rig.Moves[^1].ArgAs<Vector>(1));
    }

    //-------------------------------------------------------------------------
    // Locks and sounds
    //-------------------------------------------------------------------------

    [Fact]
    public void A_locked_use_plays_the_locked_sound_and_reports_at_most_twice_a_second()
    {
        var rig = Spawned(UseActivates | Locked, ("locked_sound", "10"));

        rig.Use();
        rig.Now = 10.3f;
        rig.Use();
        rig.Now = 10.6f;
        rig.Use();

        Assert.Empty(rig.Moves);
        Assert.Equal(2, rig.Entity.OnUseLocked.FireCount);
        Assert.Equal(["Buttons.snd10", "Buttons.snd10"], rig.Sounds);
    }

    [Fact]
    public void Unlock_lets_it_press_with_the_unlocked_sound()
    {
        var rig = Spawned(UseActivates | Locked, ("unlocked_sound", "3"), ("sounds", "1"));

        rig.Input("Unlock");
        rig.Use();

        Assert.Single(rig.Moves);
        Assert.Equal(["Buttons.snd1", "Buttons.snd3"], rig.Sounds);
    }

    [Fact]
    public void PressIn_on_a_button_already_in_asks_nothing_of_the_lock()
    {
        var rig = Spawned(0, ("wait", "-1"), ("locked_sound", "10"));
        rig.Input("PressIn");
        rig.Arrive();
        rig.Input("Lock");

        rig.Input("PressIn");
        Assert.Empty(rig.Sounds);

        rig.Input("PressOut");
        Assert.Equal(["Buttons.snd10"], rig.Sounds);
    }

    [Fact]
    public void The_sounds_are_precached_by_their_script_names()
    {
        var rig = Spawned(0, ("sounds", "14"), ("locked_sound", "2"), ("unlocked_sound", "3"));

        var precached = rig.Engine.AllOf("Precache.PrecacheSound").Select(c => (string)c.Args[0]!).ToArray();
        Assert.Equal(["Buttons.snd14", "Buttons.snd2", "Buttons.snd3"], precached);
    }

    [Fact]
    public void A_sparking_button_sparks_while_out_and_plays_no_lock_sounds()
    {
        var rig = Spawned(UseActivates | SparkIfOff | Locked, ("locked_sound", "10"));

        rig.Think(10.5f);
        var spark = Assert.Single(rig.Engine.AllOf("Effects.SparkEffect"));
        Assert.Equal(At, spark.ArgAs<Vector>(0));
        Assert.Equal(0.6f, rig.Entity.NextThinkDelay("Spark"), 3);

        rig.Use();
        Assert.Empty(rig.Sounds);
    }

    //-------------------------------------------------------------------------
    // func_rot_button
    //-------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0f, 135f, 0f)]
    [InlineData(64, 0f, 90f, 45f)]
    [InlineData(128, 45f, 90f, 0f)]
    [InlineData(2, 0f, 45f, 0f)]
    public void A_lever_turns_its_distance_about_the_door_axis(int flags, float pitch, float yaw, float roll)
    {
        var rig = Spawned(EntityNames.FuncRotButton, flags, ("distance", "45"));

        rig.Use();

        Assert.Equal(new Vector(pitch, yaw, roll), Assert.Single(rig.Turns).ArgAs<Vector>(1));
    }

    [Fact]
    public void A_lever_is_used_without_the_use_flag_and_takes_damage_only_with_health()
    {
        var rig = Spawned(EntityNames.FuncRotButton, 0);
        Assert.False(rig.Entity.TakesDamage);
        rig.Use();
        Assert.Single(rig.Turns);

        Assert.True(Spawned(EntityNames.FuncRotButton, 0, ("health", "5")).Entity.TakesDamage);
    }
}
