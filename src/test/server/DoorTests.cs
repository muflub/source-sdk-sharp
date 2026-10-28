using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// func_door and func_door_rotating against stock's CBaseDoor and CRotDoor
/// (doors.cpp): the axis and the facings, passable and passable-to-player
/// doors, +USE and touch, locks, the wait and no-auto-return, the default
/// noises, the crush and the block's outputs. Found beside stock (dm_lockdown
/// solidity census, 2026-10-27): tport_switch_cover_1, a passable one-way
/// door turning about pitch, was solid here, held a shadow and turned about
/// yaw; ours was a simplified door that read none of its spawnflags.
/// </summary>
public class DoorTests
{
    private static readonly EntityHandle Door = new(0x0005_0101u);
    private static readonly EntityHandle Player = new(0x0001_0001u);

    private const int Passable = 8, OneWay = 16, NoAutoReturn = 32, Roll = 64, Pitch = 128, UseOpens = 256,
                      TouchOpens = 1024, Locked = 2048, Backwards = 2, NonSolidToPlayer = 4;

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public readonly ThinkScheduler Thinks = new();
        public SolidFlags Flags = SolidFlags.None;
        public float Now = 10f;

        public MovingBrush Entity => (MovingBrush)Registry.Get(Door)!;

        public void Input(string name, string value = "") => Registry.Input(Door, name, value, 0f, EntityHandle.None);

        public void Use(EntityHandle activator) => Registry.Input(Door, "Use", "", 0f, activator);

        public Vector LastTurn => Engine.Last("BrushMovement.MoveToAngles")!.Value.ArgAs<Vector>(1);

        public string[] Sounds => Engine.AllOf("Sound.EntityEmitSound").Select(c => c.ArgAs<string>(1)).ToArray();
    }

    private static Rig Spawned(string className, int spawnFlags, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(MovingBrush).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine };
        engine.ReturnsEachCall("Clock.Now", () => rig.Now)
              .Returns("Entities.GetSpawnAngles", new Vector(0f, 30f, 0f))
              .Returns("Entities.GetSpawnOrigin", new Vector(100f, 0f, 0f))
              .Returns("Entities.GetSpawnFlags", spawnFlags)
              .Returns("Entities.GetBoundsSize", new Vector(66f, 10f, 66f))
              .Returns("Entities.FindAllByName", new List<EntityHandle>())
              .Returns("Entities.FindAllByClassname", Array.Empty<EntityHandle>())
              .ReturnsEachCall("Entities.GetSolidFlags", () => engine.Last("Entities.SetSolidity")?.ArgAs<SolidFlags>(2) ?? SolidFlags.None)
              .ReturnsEachCall("Entities.IsPlayer", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Player);

        // Keys before the spawn, as Source hands them; the first one makes the entity.
        registry.KeyValue(Door, className, "origin", "100 0 0");
        foreach (var (key, value) in keys)
            registry.KeyValue(Door, className, key, value);
        rig.Entity.BindThinks(rig.Thinks);
        registry.Spawn(Door, className);
        return rig;
    }

    [Theory]
    [InlineData(0, 0f, 90f, 0f)]
    [InlineData(Pitch, 90f, 0f, 0f)]
    [InlineData(Roll, 0f, 0f, 90f)]
    [InlineData(Roll | Pitch, 0f, 0f, 90f)]
    [InlineData(Pitch | Backwards, -90f, 0f, 0f)]
    public void A_rotating_door_turns_its_distance_about_the_axis_its_flags_name(int flags, float pitch, float yaw, float roll)
    {
        var rig = Spawned(EntityNames.FuncDoorRotating, flags | OneWay, ("distance", "90"));

        rig.Input("Open");

        Assert.Equal(new Vector(pitch, 30f + yaw, roll), rig.LastTurn);
    }

    /// <summary>tport_switch_cover_1's shape: passable, so NOT_SOLID, and CRotDoor makes no shadow for it.</summary>
    [Fact]
    public void A_passable_rotating_door_is_not_solid_and_makes_no_shadow()
    {
        var rig = Spawned(EntityNames.FuncDoorRotating, Passable | OneWay | NoAutoReturn | Pitch, ("distance", "160"));

        Assert.True((rig.Engine.Last("Entities.SetSolidity")!.Value.ArgAs<SolidFlags>(2) & SolidFlags.NotSolid) != 0);
        Assert.False(rig.Engine.Called("EntityPhysics.InitShadowPhysics"));
    }

    [Fact]
    public void A_passable_sliding_door_still_makes_its_shadow()
    {
        var rig = Spawned(EntityNames.FuncDoor, Passable);

        Assert.True((rig.Engine.Last("Entities.SetSolidity")!.Value.ArgAs<SolidFlags>(2) & SolidFlags.NotSolid) != 0);
        Assert.True(rig.Engine.Called("EntityPhysics.InitShadowPhysics"));
    }

    [Fact]
    public void A_door_players_pass_through_is_in_the_passable_door_group_and_unblockable_by_them()
    {
        var rig = Spawned(EntityNames.FuncDoor, NonSolidToPlayer);

        Assert.Equal(CollisionGroup.PassableDoor, rig.Engine.Last("EntityPhysics.SetCollisionGroup")!.Value.ArgAs<CollisionGroup>(1));
        Assert.True(rig.Entity.UnblockableByPlayer);
    }

    [Fact]
    public void A_players_use_opens_only_a_door_that_says_so()
    {
        var refuses = Spawned(EntityNames.FuncDoor, 0);
        refuses.Use(Player);
        Assert.Equal(0, refuses.Entity.OnOpen.FireCount);
        Assert.Contains("DoorSound.DefaultLocked", refuses.Sounds);

        var opens = Spawned(EntityNames.FuncDoor, UseOpens);
        opens.Use(Player);
        Assert.Equal(1, opens.Entity.OnOpen.FireCount);
    }

    [Fact]
    public void A_touch_opens_a_touch_door_once_until_it_is_back_at_the_bottom()
    {
        var rig = Spawned(EntityNames.FuncDoor, TouchOpens);

        rig.Entity.OnTouch(Player);
        rig.Entity.OnMoveDone();
        rig.Entity.OnTouch(Player);
        Assert.Equal(1, rig.Entity.OnOpen.FireCount);

        rig.Input("Close");
        rig.Entity.OnMoveDone();
        rig.Entity.OnTouch(Player);
        Assert.Equal(2, rig.Entity.OnOpen.FireCount);
    }

    /// <summary>SetTouch( NULL ): a door a touch opened hears no touch until it is back down, not even its unlocked sound.</summary>
    [Fact]
    public void A_door_a_touch_opened_hears_no_more_touches_on_its_way()
    {
        var rig = Spawned(EntityNames.FuncDoor, TouchOpens, ("unlocked_sound", "Doors.Unlocked"));

        rig.Entity.OnTouch(Player);
        rig.Now = 12f;
        rig.Entity.OnTouch(Player);

        Assert.Single(rig.Sounds, s => s == "Doors.Unlocked");
    }

    [Fact]
    public void A_touch_leaves_a_door_without_the_touch_flag_alone()
    {
        var rig = Spawned(EntityNames.FuncDoor, UseOpens);

        rig.Entity.OnTouch(Player);

        Assert.Equal(0, rig.Entity.OnOpen.FireCount);
    }

    [Fact]
    public void A_locked_door_says_so_to_a_use_and_opens_once_unlocked()
    {
        var rig = Spawned(EntityNames.FuncDoor, UseOpens | Locked);

        rig.Use(Player);
        rig.Input("Open");
        Assert.Equal((1, 0), (rig.Entity.OnLockedUse.FireCount, rig.Entity.OnOpen.FireCount));

        rig.Input("Unlock");
        rig.Use(Player);
        Assert.Equal(1, rig.Entity.OnOpen.FireCount);
    }

    [Fact]
    public void An_open_door_comes_back_after_its_wait()
    {
        var rig = Spawned(EntityNames.FuncDoor, 0, ("wait", "3"));
        rig.Input("Open");
        rig.Entity.OnMoveDone();

        Assert.Equal(1, rig.Entity.OnFullyOpen.FireCount);
        Assert.True(rig.Thinks.IsScheduled(Door, "DoorReturn"));

        rig.Entity.OnThink("DoorReturn");
        Assert.Equal(1, rig.Entity.OnClose.FireCount);
    }

    [Fact]
    public void A_door_that_does_not_return_waits_open_and_a_use_closes_it()
    {
        var rig = Spawned(EntityNames.FuncDoor, NoAutoReturn | UseOpens, ("wait", "3"));
        rig.Use(Player);
        rig.Entity.OnMoveDone();
        Assert.False(rig.Thinks.IsScheduled(Door, "DoorReturn"));

        rig.Use(Player);
        Assert.Equal(1, rig.Entity.OnClose.FireCount);
    }

    [Fact]
    public void A_door_the_map_names_no_noises_for_plays_stocks()
    {
        var sliding = Spawned(EntityNames.FuncDoor, 0);
        sliding.Input("Open");
        sliding.Entity.OnMoveDone();
        // The Open input plays the unlocked sound first, which is DoorSound.Null when the map names none.
        Assert.Equal(new[] { "DoorSound.Null", "DoorSound.DefaultMove", "DoorSound.DefaultArrive" }, sliding.Sounds);

        var rotating = Spawned(EntityNames.FuncDoorRotating, OneWay, ("distance", "90"));
        rotating.Input("Open");
        Assert.Equal(new[] { "DoorSound.Null", "RotDoorSound.DefaultMove" }, rotating.Sounds);
    }

    [Fact]
    public void A_closing_door_plays_its_close_noises_when_the_map_names_them()
    {
        var rig = Spawned(EntityNames.FuncDoor, 0, ("startclosesound", "Doors.CloseStart"), ("closesound", "Doors.Shut"));
        rig.Input("Open");
        rig.Entity.OnMoveDone();
        rig.Input("Close");
        rig.Entity.OnMoveDone();

        Assert.Equal(new[] { "DoorSound.Null", "DoorSound.DefaultMove", "DoorSound.DefaultArrive", "Doors.CloseStart", "Doors.Shut" },
                     rig.Sounds);
    }

    [Fact]
    public void A_blocked_door_with_damage_crushes_what_is_in_the_way()
    {
        var rig = Spawned(EntityNames.FuncDoor, 0, ("dmg", "15"));
        rig.Input("Open");

        rig.Entity.OnBlocked(Player);

        var hurt = rig.Engine.Last("Damage.Apply")!.Value;
        Assert.Equal((Player, Door, 15f, DamageType.Crush),
                     (hurt.ArgAs<EntityHandle>(0), hurt.ArgAs<EntityHandle>(1), hurt.ArgAs<float>(2), hurt.ArgAs<DamageType>(3)));
    }

    [Fact]
    public void A_forced_closed_door_does_not_back_away()
    {
        var rig = Spawned(EntityNames.FuncDoor, 0, ("forceclosed", "1"));
        rig.Input("Open");

        rig.Entity.OnBlocked(Player);

        Assert.Equal(0, rig.Entity.OnClose.FireCount);
    }

    [Fact]
    public void The_end_of_a_block_says_which_way_the_door_was_going()
    {
        var rig = Spawned(EntityNames.FuncDoor, 0);
        rig.Input("Open");
        rig.Entity.OnEndBlocked();
        Assert.Equal((1, 0), (rig.Entity.OnUnblockedOpening.FireCount, rig.Entity.OnUnblockedClosing.FireCount));

        rig.Entity.OnMoveDone();
        rig.Input("Close");
        rig.Entity.OnEndBlocked();
        Assert.Equal((1, 1), (rig.Entity.OnUnblockedOpening.FireCount, rig.Entity.OnUnblockedClosing.FireCount));
    }

    [Fact]
    public void A_rotating_door_that_spawns_open_starts_at_its_open_facing()
    {
        var rig = Spawned(EntityNames.FuncDoorRotating, OneWay, ("distance", "90"), ("spawnpos", "1"));
        Assert.Equal(new Vector(0f, 120f, 0f), rig.Engine.Last("Entities.SetAngles")!.Value.ArgAs<Vector>(1));

        rig.Input("Close");
        Assert.Equal(new Vector(0f, 30f, 0f), rig.LastTurn);
    }

    [Fact]
    public void SetSpeed_is_the_speed_of_the_next_travel()
    {
        var rig = Spawned(EntityNames.FuncDoor, 0, ("speed", "50"));
        rig.Registry.Input(Door, "SetSpeed", "200", 200f, EntityHandle.None);
        rig.Input("Open");

        Assert.Equal(200f, rig.Engine.Last("BrushMovement.MoveToPosition")!.Value.ArgAs<float>(2));
    }

    [Fact]
    public void SetToggleState_puts_a_sliding_door_at_an_end()
    {
        var rig = Spawned(EntityNames.FuncDoor, 0);
        rig.Registry.Input(Door, "SetToggleState", "0", 0f, EntityHandle.None);

        Assert.Equal(new Vector(164f, 0f, 0f), rig.Engine.Last("Entities.SetOrigin")!.Value.ArgAs<Vector>(1));
    }
}
