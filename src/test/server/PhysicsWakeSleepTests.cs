using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Wake and Sleep are SIMULATION STATE, and the port had been spelling them
/// with motion enable.
///
/// <para>
/// The five stock handlers are each exactly one call on the physics object and
/// not one of them touches motion enable:
/// </para>
///
/// <code>
///   props.cpp:2707            CPhysicsProp::InputWake      -&gt; pPhysicsObject-&gt;Wake()
///   props.cpp:2719            CPhysicsProp::InputSleep     -&gt; pPhysicsObject-&gt;Sleep()
///   physobj.cpp:639           CPhysBox::InputWake          -&gt; VPhysicsGetObject()-&gt;Wake()
///   physobj.cpp:648           CPhysBox::InputSleep         -&gt; VPhysicsGetObject()-&gt;Sleep()
///   physics_cannister.cpp:319 CPhysicsCannister::InputWake -&gt; pPhys-&gt;Wake()
/// </code>
///
/// <para>
/// The two are not interchangeable in either direction.
/// <c>EnableMotion(false)</c> pins the object in vphysics, so
/// <c>IsMotionEnabled()</c> and with it <c>IsMoveable()</c> go false. That has
/// two consequences a slept object does not have: nothing can shove it awake
/// again, and the player's physics controller treats a contact that is not
/// moveable (or is heavier than its pushable mass limit) as immovable and
/// clamps the player's own shadow velocity to zero along the contact
/// normal — a crate sent to Sleep became a wall. In the other direction
/// <c>EnableMotion(true)</c> UNPINS a prop the map deliberately disabled,
/// which stock's Wake leaves pinned.
/// </para>
///
/// <para>
/// These are call-trace facts because the pin lives in vphysics and there is
/// nothing to read back without a game. What is checkable here is precisely
/// what went wrong: WHICH of the two calls each input makes. The live gate in
/// <c>status/phys-wakesleep.md</c> is what shows the wall is gone.
/// </para>
/// </summary>
public class PhysicsWakeSleepTests
{
    /// <summary>The prop_physics MotionDisabled spawnflag — PropPhysics's own constant.</summary>
    private const int MotionDisabled = 8;

    /// <summary>An entity under test, with the log and the way to fire an input at it.</summary>
    private sealed record Subject(ScriptedEntity Entity, ScriptedEntityRegistry Registry,
                                  EntityHandle Handle, RecordingEngine Engine)
    {
        /// <summary>
        /// Through the REGISTRY, which is how a map's input reaches a bound
        /// handler. Calling OnInput on the instance would reach the virtual
        /// fall-back and miss every [ScriptedEvent] this lane is about.
        /// </summary>
        public void Fire(string name) => Registry.Input(Handle, name, string.Empty, 0f, EntityHandle.None);
    }

    private static Subject Prop(uint index, out RecordingEngine engine, int spawnFlags = 0)
    {
        var services = Services(out engine);

        engine.Returns("Entities.GetSpawnFlags", spawnFlags);
        engine.Ignores("Entities.GetHealth",
                       "EntityRender.GetModelName",
                       "EntityRender.GetModelKeyValueText",
                       "EntityPhysics.InitPhysics",
                       "EntityPhysics.GetMass",
                       "Precache.BreakPieceCount",
                       "Scripted.SetNextThink");

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        registry.Services = services;

        var handle = new EntityHandle(index);
        registry.Spawn(handle, EntityNames.PropPhysics);

        return new Subject(registry.Get(handle)!, registry, handle, engine);
    }

    private static Subject Physbox(uint index, out RecordingEngine engine)
    {
        var services = Services(out engine);

        engine.Ignores("Entities.GetHealth", "EntityPhysics.InitPhysics");

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(FuncPhysbox).Assembly);
        registry.Services = services;

        var handle = new EntityHandle(index);
        registry.Spawn(handle, EntityNames.FuncPhysbox);

        return new Subject(registry.Get(handle)!, registry, handle, engine);
    }

    private static Subject Cannister(uint index, out RecordingEngine engine)
    {
        var services = Services(out engine);

        engine.Ignores("Entities.GetHealth",
                       "EntityPhysics.InitPhysics",
                       "Precache.Model",
                       "Scripted.SetNextThink");

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PhysicsCannister).Assembly);
        registry.Services = services;

        var handle = new EntityHandle(index);
        registry.Spawn(handle, EntityNames.PhysicsCannister);

        return new Subject(registry.Get(handle)!, registry, handle, engine);
    }

    /// <summary>Everything the entity said to the engine after the spawn settled.</summary>
    private static int Since(RecordingEngine engine) => engine.Calls.Count;

    private static int CountAfter(RecordingEngine engine, int from, string member)
        => engine.Calls.Skip(from).Count(c => c.Member == member);

    //-------------------------------------------------------------------------
    // prop_physics — the class the divergence was found on.
    //-------------------------------------------------------------------------

    [Fact]
    public void SleepingAPropReachesTheSolversSleep()
    {
        var prop = Prop(1, out var engine);
        int from = Since(engine);

        prop.Fire("Sleep");

        Assert.True(CountAfter(engine, from, "EntityPhysics.Sleep") == 1,
                    "the Sleep input is IPhysicsObject::Sleep, exactly once");
    }

    [Fact]
    public void ASleptPropIsStillMotionEnabled()
    {
        // The defect, stated as the thing it broke: a slept crate stays
        // MOVEABLE, so a shove revives it and the player's shadow controller
        // still pushes it instead of clamping to zero against it.
        var prop = Prop(2, out var engine);
        int from = Since(engine);

        prop.Fire("Sleep");

        Assert.True(CountAfter(engine, from, "EntityPhysics.EnableMotion") == 0,
                    "sleeping a prop does not disable its motion, so it stays moveable and is not a wall");
    }

    [Fact]
    public void WakingAPropReachesTheSolversWake()
    {
        var prop = Prop(3, out var engine);
        int from = Since(engine);

        prop.Fire("Wake");

        Assert.True(CountAfter(engine, from, "EntityPhysics.Wake") == 1,
                    "the Wake input is IPhysicsObject::Wake, exactly once");
    }

    [Fact]
    public void WakingAMotionDisabledPropLeavesItDisabled()
    {
        // The other direction, and the one nothing would have noticed: a crate
        // the map pinned with the MotionDisabled spawnflag must stay pinned
        // through a Wake input, because stock's InputWake never enables motion.
        var pinned = Prop(4, out var engine, spawnFlags: MotionDisabled);
        int from = Since(engine);

        pinned.Fire("Wake");

        Assert.True(CountAfter(engine, from, "EntityPhysics.EnableMotion") == 0,
                    "waking a deliberately motion-disabled prop does not un-disable it");
    }

    [Fact]
    public void WakeAndSleepAreDistinguishableFromEachOther()
    {
        // A check that cannot pass by both inputs doing the same thing, which
        // is exactly the state the port was in.
        var prop = Prop(5, out var engine);
        int from = Since(engine);

        prop.Fire("Wake");
        prop.Fire("Sleep");

        Assert.True(CountAfter(engine, from, "EntityPhysics.Wake") == 1
                 && CountAfter(engine, from, "EntityPhysics.Sleep") == 1,
                    "Wake and Sleep are two different calls, not one call with a flag");
    }

    //-------------------------------------------------------------------------
    // func_physbox — CPhysBox::InputWake/InputSleep, physobj.cpp:639 and :648.
    //-------------------------------------------------------------------------

    [Fact]
    public void SleepingAPhysboxDoesNotDisableItsMotion()
    {
        var box = Physbox(11, out var engine);
        int from = Since(engine);

        box.Fire("Sleep");

        Assert.True(CountAfter(engine, from, "EntityPhysics.Sleep") == 1
                 && CountAfter(engine, from, "EntityPhysics.EnableMotion") == 0,
                    "a physbox sent to Sleep is slept, not pinned");
    }

    [Fact]
    public void WakingAPhysboxDoesNotEnableItsMotion()
    {
        var box = Physbox(12, out var engine);
        int from = Since(engine);

        box.Fire("Wake");

        Assert.True(CountAfter(engine, from, "EntityPhysics.Wake") == 1
                 && CountAfter(engine, from, "EntityPhysics.EnableMotion") == 0,
                    "a physbox woken is woken, not un-pinned");
    }

    [Fact]
    public void WakingAPhysboxDoesNotFireOnMotionEnabled()
    {
        // A visible consequence of the old spelling that had nothing to do
        // with the solver: Wake went through SetMotion, which fires the
        // OnMotionEnabled output. Stock's InputWake fires nothing.
        var box = Physbox(13, out _);
        var output = ((FuncPhysbox)box.Entity).OnMotionEnabled;

        // DisableMotion first, and that is not scene-setting: FuncPhysbox
        // starts _motionEnabled = true and SetMotion fires the output only on
        // the TRANSITION, so a Wake on a freshly spawned box fires nothing
        // whichever way Wake is spelled. Without this line the assertion below
        // is a check that cannot fail.
        box.Fire("DisableMotion");
        int before = output.FireCount;

        box.Fire("Wake");

        Assert.True(output.FireCount == before,
                    "waking a physbox is not a motion-enable, so it fires no OnMotionEnabled");
    }

    //-------------------------------------------------------------------------
    // physics_cannister — CPhysicsCannister::InputWake, physics_cannister.cpp:319.
    //-------------------------------------------------------------------------

    [Fact]
    public void WakingACannisterDoesNotEnableItsMotion()
    {
        var can = Cannister(21, out var engine);
        int from = Since(engine);

        can.Fire("Wake");

        Assert.True(CountAfter(engine, from, "EntityPhysics.Wake") == 1
                 && CountAfter(engine, from, "EntityPhysics.EnableMotion") == 0,
                    "a cannister woken is woken, not un-pinned");
    }
}
