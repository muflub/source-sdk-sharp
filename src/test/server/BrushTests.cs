using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The brushes and props that came over with momentary_rot_button.
///
/// Most of what is here is the wheel's arithmetic, because that is where this
/// batch could be wrong without anything complaining: a fraction that comes out
/// backwards still moves the door it drives, just the wrong way, and a fraction
/// off by a degree still reaches the end. The pure helpers exist so the sums can
/// be asked directly — a live brush's angles live in the engine, and a test has
/// no engine.
/// </summary>
public class BrushTests
{

    private static (ScriptedEntityRegistry Registry, EntityHandle Handle, T Entity) Spawn<T>(
        string className, uint index, params (string Key, string Value)[] keys) where T : ScriptedEntity
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncPushable).Assembly);
        registry.Services = Ignoring("Entities.GetBoundsSize", "EntityPhysics.InitPhysics", "EntityPhysics.InitSpherePhysics",
                                     "EntityPhysics.InitStaticPhysics", "EntityPhysics.EnableCollisions",
                                     "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles");

        var handle = new EntityHandle(index);

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, className, key, value);

        registry.Spawn(handle, className);
        return (registry, handle, (T)registry.Get(handle)!);
    }

    //-------------------------------------------------------------------------
    // A blocked door (stock's CBaseDoor::Blocked, doors.cpp:1193-1234)
    //-------------------------------------------------------------------------

    [Fact]
    public void A_door_that_never_closes_by_itself_keeps_going_when_blocked()
    {
        // "if a door has a negative wait, it would never come back if blocked,
        // so let it just squash the object to death real fast": no reversal.
        var (registry, handle, door) = Spawn<FuncDoor>(EntityNames.FuncDoor, 30, ("wait", "-1"), ("speed", "64"));
        registry.Input(handle, "Open", "", 0f, EntityHandle.None);

        // PerformPush: the block's start, then the frame's Blocked.
        door.OnStartBlocked(new EntityHandle(0x0001_0001u));
        door.OnBlocked(new EntityHandle(0x0001_0001u));

        Assert.Equal(1, door.OnBlockedOpening.FireCount);
        Assert.Equal(0, door.OnClose.FireCount);
        Assert.Equal(1, door.OnOpen.FireCount);
    }

    [Fact]
    public void A_door_that_closes_by_itself_reverses_when_blocked()
    {
        // The control: m_flWait >= 0 -> DoorGoDown while going up.
        var (registry, handle, door) = Spawn<FuncDoor>(EntityNames.FuncDoor, 31, ("wait", "4"), ("speed", "64"));
        registry.Input(handle, "Open", "", 0f, EntityHandle.None);

        // PerformPush: the block's start, then the frame's Blocked.
        door.OnStartBlocked(new EntityHandle(0x0001_0001u));
        door.OnBlocked(new EntityHandle(0x0001_0001u));

        Assert.Equal(1, door.OnBlockedOpening.FireCount);
        Assert.Equal(1, door.OnClose.FireCount);
    }

    //-------------------------------------------------------------------------
    // func_pushable
    //-------------------------------------------------------------------------

    [Fact]
    public void PushableTakesDamageOnlyWhenAsked()
    {
        Assert.True(!FuncPushable.IsBreakable(0), "a pushable is furniture by default");
        Assert.True(FuncPushable.IsBreakable(0x0080), "and breakable when the map sets the flag");

        // The flag comes from the ENGINE, which a test does not have, so this
        // instance is the unflagged case — which is the one worth asserting:
        // shooting furniture should shove it, not destroy it.
        var (_, _, crate) = Spawn<FuncPushable>(EntityNames.FuncPushable, 1, ("health", "50"));

        Assert.True(crate.OnDamaged(EntityHandle.None, 40f, DamageType.Bullet) == 0, "an unflagged pushable takes nothing from a bullet");

        Assert.True(crate.OnHealthChanged.FireCount == 0, "and reports no health change, because none happened");
    }

    [Fact]
    public void PushableBreaksOnce()
    {
        var (registry, handle, crate) = Spawn<FuncPushable>(EntityNames.FuncPushable, 2, ("health", "50"));

        // The Break input works on furniture too: the spawnflag decides whether
        // damage can destroy it, not whether a map can.
        registry.Input(handle, "Break", "", 0f, EntityHandle.None);
        Assert.True(crate.OnBreak.FireCount == 1, "a map can break a pushable that damage cannot");

        registry.Input(handle, "Break", "", 0f, EntityHandle.None);
        Assert.True(crate.OnBreak.FireCount == 1, "and breaking it twice still only happened once");
    }

    //-------------------------------------------------------------------------
    // momentary_rot_button
    //-------------------------------------------------------------------------

    [Fact]
    public void WheelAxisIsASpawnflag()
    {
        // The DOOR rotation flags, which is what CBaseToggle::AxisDir reads —
        // not func_rotating's 4 and 8, which are a different set at the same
        // low bits and would silently turn the wheel about the wrong axis.
        // Asserted on the numbers as well as the names, because the numbers are
        // what a map file carries and the names are new.
        Assert.True((int)RotationAxisFlag.Roll == 64 && (int)RotationAxisFlag.Pitch == 128, "the shared axis bits are doors.h's 64 and 128");

        Assert.True(MomentaryRotButton.AxisFor(RotationAxisFlag.None) == MomentaryRotButton.RotationAxis.Yaw, "a wheel turns about yaw by default");
        Assert.True(MomentaryRotButton.AxisFor(RotationAxisFlag.Roll) == MomentaryRotButton.RotationAxis.Roll, "spawnflag 64 turns it about roll");
        Assert.True(MomentaryRotButton.AxisFor(RotationAxisFlag.Pitch) == MomentaryRotButton.RotationAxis.Pitch, "spawnflag 128 turns it about pitch");

        // func_rotating's axis bits read through the wheel's word must not name
        // an axis. This is the mistake the shared type exists to prevent, and
        // it is still worth asserting because the cast at the entity boundary
        // is where a raw word becomes typed.
        Assert.True(MomentaryRotButton.AxisFor((RotationAxisFlag)4) == MomentaryRotButton.RotationAxis.Yaw &&
            MomentaryRotButton.AxisFor((RotationAxisFlag)8) == MomentaryRotButton.RotationAxis.Yaw, "func_rotating's axis bits are not the wheel's");

        var turned = MomentaryRotButton.OffsetAngles(Vector.Zero, RotationAxisFlag.None, 90f);
        Assert.True(turned.Y == 90f && turned.X == 0f && turned.Z == 0f, "turning about yaw only changes yaw");

        var rolled = MomentaryRotButton.OffsetAngles(Vector.Zero, RotationAxisFlag.Roll, 90f);
        Assert.True(rolled.Z == 90f && rolled.X == 0f && rolled.Y == 0f, "turning about roll only changes roll");
    }

    [Fact]
    public void WheelFractionFromAngles()
    {
        var start = Vector.Zero;

        Assert.True(MomentaryRotButton.PositionFor(RotationAxisFlag.None, new Vector(0f, 45f, 0f), start, 90f) == 0.5f, "halfway round a ninety degree travel is a half");

        Assert.True(MomentaryRotButton.PositionFor(RotationAxisFlag.None, new Vector(0f, 90f, 0f), start, 90f) == 1f, "the far end is one");

        // Clamped both ways, because a wheel shoved past its end by the solver
        // must not report more than it has.
        Assert.True(MomentaryRotButton.PositionFor(RotationAxisFlag.None, new Vector(0f, 200f, 0f), start, 90f) == 1f, "past the far end is still one");

        Assert.True(MomentaryRotButton.PositionFor(RotationAxisFlag.None, new Vector(0f, -30f, 0f), start, 90f) == 0f, "back behind the start is nought");

        // A negative distance turns the wheel the other way and still counts UP
        // from nought at the start. Stock arrives here by flipping its axis
        // vector and then multiplying the answer by minus one again.
        Assert.True(MomentaryRotButton.PositionFor(RotationAxisFlag.None, new Vector(0f, -45f, 0f), start, -90f) == 0.5f, "a negative travel counts forward along itself");

        Assert.True(MomentaryRotButton.PositionFor(RotationAxisFlag.None, new Vector(0f, -90f, 0f), start, -90f) == 1f, "and its own far end is one, not minus one");
    }

    [Fact]
    public void WheelReportsWhereItStopped()
    {
        var (registry, handle, wheel) = Spawn<MomentaryRotButton>(
            EntityNames.MomentaryRotButton, 3, ("distance", "90"), ("speed", "100"));

        registry.Input(handle, "SetPosition", "0.5", 0.5f, EntityHandle.None);

        // Nothing yet: the wheel has been sent, not arrived, and the value is
        // read off the angles as it turns.
        Assert.True(wheel.Position.FireCount == 0, "being sent somewhere is not being there");

        registry.MoveDone(handle);

        Assert.True(wheel.Position.FireCount == 1, "arriving reports the fraction");
        Assert.True(wheel.Position.LastValue == "0.5", "and reports the one it was asked for");
        Assert.True(wheel.OnReachedPosition.FireCount == 1, "and says it got there");

        // Halfway is neither end, and firing either would be a lie.
        Assert.True(wheel.OnFullyOpen.FireCount == 0 && wheel.OnFullyClosed.FireCount == 0, "halfway is not an end");

        // Sent to the same place again, the output stays quiet: a map wiring a
        // counter to Position must not see a tick per input.
        registry.Input(handle, "SetPosition", "0.5", 0.5f, EntityHandle.None);
        registry.MoveDone(handle);
        Assert.True(wheel.Position.FireCount == 1, "a fraction that has not changed is not reported again");
    }

    [Fact]
    public void WheelEndsReadBackwards()
    {
        // Stock's own mapping, in OutputMovementComplete and again in
        // UpdateSelf: the wheel wound fully ON is "closed". Reproduced rather
        // than corrected, because every map wired to it was authored this way.
        var (onRegistry, onHandle, one) = Spawn<MomentaryRotButton>(
            EntityNames.MomentaryRotButton, 4, ("distance", "90"));

        onRegistry.Input(onHandle, "SetPosition", "1", 1f, EntityHandle.None);
        onRegistry.MoveDone(onHandle);

        Assert.True(one.OnFullyClosed.FireCount == 1, "fully wound on fires OnFullyClosed");
        Assert.True(one.OnFullyOpen.FireCount == 0, "and not OnFullyOpen");

        var (offRegistry, offHandle, zero) = Spawn<MomentaryRotButton>(
            EntityNames.MomentaryRotButton, 5, ("distance", "90"), ("StartPosition", "1"));

        offRegistry.Input(offHandle, "SetPosition", "0", 0f, EntityHandle.None);
        offRegistry.MoveDone(offHandle);

        Assert.True(zero.OnFullyOpen.FireCount == 1, "fully unwound fires OnFullyOpen");
        Assert.True(zero.OnFullyClosed.FireCount == 0, "and not OnFullyClosed");
    }

    [Fact]
    public void WheelIgnoresAPlayerWhenDisabled()
    {
        // A disabled wheel is not a locked one: it reports nothing at all
        // rather than refusing audibly (MomentaryRotButtonUseTests has the lock).
        var (disabledRegistry, disabledHandle, disabled) = Spawn<MomentaryRotButton>(
            EntityNames.MomentaryRotButton, 9, ("distance", "90"));

        disabledRegistry.Input(disabledHandle, "Disable", "", 0f, EntityHandle.None);
        disabledRegistry.Input(disabledHandle, "Use", "", 0f, EntityHandle.None);

        Assert.True(disabled.OnPressed.FireCount == 0 && disabled.OnUseLocked.FireCount == 0, "a disabled wheel ignores a press silently");
    }

    //-------------------------------------------------------------------------
    // The registrations that ARE the port.
    //-------------------------------------------------------------------------

    [Fact]
    public void WaterIsADoor()
    {
        // Stock points a second classname at CBaseDoor and writes no code;
        // this is the managed side of the same statement, and the assertion is
        // that func_water reaches the door class rather than a class of its own.
        var (_, _, water) = Spawn<FuncDoor>(EntityNames.FuncWater, 10, ("speed", "50"));

        Assert.True(water is FuncDoor, "func_water is a func_door under another name");
        Assert.True(string.Equals(water.ClassName, EntityNames.FuncWater, StringComparison.OrdinalIgnoreCase), "and knows which name it was given");
    }

    [Fact]
    public void TheRestAreRegistered()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncPushable).Assembly);
        registry.Services = Ignoring("Entities.GetBoundsSize", "EntityPhysics.InitPhysics", "EntityPhysics.InitSpherePhysics",
                                     "EntityPhysics.InitStaticPhysics", "EntityPhysics.EnableCollisions",
                                     "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles");

        Assert.True(registry.IsRegistered(EntityNames.FuncPushable), "func_pushable is claimed by C#");
        Assert.True(registry.IsRegistered(EntityNames.FuncWater), "func_water is claimed by C#");
        Assert.True(registry.IsRegistered(EntityNames.MomentaryRotButton), "momentary_rot_button is claimed by C#");
        Assert.True(registry.IsRegistered(EntityNames.PropSphere), "prop_sphere is claimed by C#");

        var (_, _, sphere) = Spawn<PropSphere>(EntityNames.PropSphere, 12,
            ("model", "models/props_junk/watermelon01.mdl"));
        Assert.True(sphere is PropSphere, "a prop_sphere spawns from its model");
    }
}
