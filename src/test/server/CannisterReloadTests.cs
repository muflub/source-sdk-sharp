using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A lit cannister across a hot reload: the clock, the thrust, the jet, the
/// controller.
///
/// Stock saves every one of these (m_active, m_bSpent?, m_flActivateTime,
/// m_flShutdownTime, m_vLocalThrust, m_hLauncher, the jet and controller
/// handles — physics_cannister.cpp:66-89) and re-wires the pointers in
/// OnRestore. The native motion controller survives the reload (cleared only
/// at LevelShutdown, shell_motioncontrollers.cpp), so before this a reload
/// left a lit cannister amnesiac: no douse clock, a zeroed thrust direction,
/// an orphaned env_steam whose gas sound never stopped, a controller nothing
/// could destroy, and a spent cannister that could be lit a second time on
/// top of the first controller.
/// </summary>
public class CannisterReloadTests
{
    private static readonly EntityHandle Cannister = new(0x0001_0080);

    private const uint JetHandle = 0x0001_0091;
    private const uint ControllerId = 0x0002_0007u;

    private readonly ReloadStore _reload = new(Abi.Side.Server);

    /// <summary>Fuel of 4 s, thrust of 1000; the double answers a 10 kg body.</summary>
    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine)
        Side(float now = 0f)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PhysicsCannister).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Clock.Now", now);
        engine.Returns("Entities.IsValid", true);
        engine.Returns("EntityPhysics.GetMass", 10f);
        engine.ReturnsEachCall("MotionControllers.Create", () => (object?)ControllerId);
        engine.Returns("Entities.Create", new EntityHandle(JetHandle));
        return (registry, engine);
    }

    private static void Light(ScriptedEntityRegistry registry)
        => registry.Input(Cannister, "Activate", "", 0f, EntityHandle.None);

    private static void Spawned(ScriptedEntityRegistry registry)
    {
        registry.KeyValue(Cannister, EntityNames.PhysicsCannister, "targetname", "can");
        registry.KeyValue(Cannister, EntityNames.PhysicsCannister, "fuel", "4");
        registry.KeyValue(Cannister, EntityNames.PhysicsCannister, "thrust", "1000");
        registry.KeyValue(Cannister, EntityNames.PhysicsCannister, "gassound", "weapons/cannister_hiss.wav");
        registry.Spawn(Cannister, EntityNames.PhysicsCannister);
    }

    [Fact]
    public void A_lit_cannister_still_thrusts_after_the_reload()
    {
        var (before, engine) = Side();
        Spawned(before);
        Light(before);
        Assert.True(engine.Called("MotionControllers.Create"), "the control: it lit");

        _reload.Clear();
        Assert.Equal(1, before.SaveForReload(_reload));

        var (after, back) = Side(now: 1f); // one second into its 4 s burn
        Assert.Equal(1, after.RestoreAfterReload(_reload));

        after.Think(Cannister);

        // The controller still attached to the body keeps the thrust computed
        // once at the light (CalcThrust, in the cannister's frame): the
        // rebuilt instance neither stacks a second controller, nor destroys
        // the first, nor overwrites it with a zeroed _localThrust.
        Assert.False(back.Called("MotionControllers.Create"), "no second controller");
        Assert.False(back.Called("MotionControllers.Destroy"), "the burning one is kept");
        Assert.False(back.Called("MotionControllers.SetForceAtPoint"), "and its thrust is not re-issued");

        // The jet, which the think still moves, points along the CARRIED
        // thrust: the zeroed default would aim it nowhere.
        var aim = back.Calls.Last(c => c.Member == "Entities.SetAngles" && c.ArgAs<EntityHandle>(0) == new EntityHandle(JetHandle));
        Assert.NotEqual(new Vector(0f, 0f, 0f), aim.ArgAs<Vector>(1));
    }

    [Fact]
    public void The_douse_clock_survives_and_frees_the_native_state()
    {
        var (before, _) = Side();
        Spawned(before);
        Light(before);

        _reload.Clear();
        before.SaveForReload(_reload);

        // Past the saved shutdown time: the restored clock still runs out, the
        // controller is destroyed, the jet is told to stop, the gas goes quiet.
        var (after, back) = Side(now: 5f);
        Assert.Equal(1, after.RestoreAfterReload(_reload));

        after.Think(Cannister);

        Assert.True(back.Calls.Any(c => c.Member == "MotionControllers.Destroy"
                                        && c.ArgAs<uint>(0) == ControllerId),
                    "the controller that survived the reload is destroyed by the douse");
        Assert.True(back.Called("Sound.EntityStopSound"), "and the gas sound stops");
    }

    [Fact]
    public void A_spent_cannister_is_not_lit_a_second_time()
    {
        var (before, _) = Side();
        Spawned(before);
        Light(before);
        before.Input(Cannister, "Deactivate", "", 0f, EntityHandle.None); // spent for good

        _reload.Clear();
        before.SaveForReload(_reload);

        var (after, back) = Side(now: 1f);
        Assert.Equal(1, after.RestoreAfterReload(_reload));

        // Stock zeroes the fuel on Deactivate; the spent flag is its stand-in,
        // and it is saved state. Without it the rebuilt cannister takes a
        // second controller on top of the world the reload left standing.
        after.Input(Cannister, "Activate", "", 0f, EntityHandle.None);

        Assert.False(back.Called("MotionControllers.Create"),
                    "a spent cannister stays spent across the reload");
    }

    [Fact]
    public void The_jet_handle_survives_so_the_plume_is_removed_not_orphaned()
    {
        var (before, _) = Side();
        Spawned(before);
        Light(before);

        _reload.Clear();
        before.SaveForReload(_reload);

        var (after, back) = Side(now: 6f); // already past the burn when it comes back
        Assert.Equal(1, after.RestoreAfterReload(_reload));

        // The douse (the clock ran out), then the one think a doused
        // cannister still owes: taking the jet away.
        after.Think(Cannister);
        after.Think(Cannister);

        Assert.True(back.Calls.Any(c => c.Member == "Entities.Remove"
                                        && c.ArgAs<EntityHandle>(0) == new EntityHandle(JetHandle)),
                    "the cannister takes its env_steam with it instead of leaving it hanging");
    }
}
