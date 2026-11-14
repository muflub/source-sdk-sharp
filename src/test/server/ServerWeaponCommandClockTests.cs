using System;
using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The clock a queued burst of commands runs its weapon frame on
/// (plan_server_native_v2 T4g's tick base, stock's CPlayerMove::RunCommand at
/// player_command.cpp:336): every command of one frame carries its own tick —
/// the tracked tick base times the interval — so the timers the machine stamps
/// inside the command (the meter pair, the deploy and refire waits) land where
/// the predicting client puts them, which derives the very same clock from the
/// command's tick base (ClientPlayerPrediction's RunOne). The command's claimed
/// tick count is not that clock: a client could send one tick ahead and fire
/// before its refire time. The weapon's own press, through the server's
/// Arsenal, is the player-visible half.
/// </summary>
public class ServerWeaponCommandClockTests
{
    private const uint PlayerHandle = 0x0001_0001;

    private static EntityHandle Player => new(PlayerHandle);

    private const int Attack = (int)InputButtons.Attack;

    /// <summary>The frame's sampled clock the fakes hold still for the whole burst.</summary>
    private const float FrameClock = 10f;

    /// <summary>The test's tick interval.</summary>
    private const float Tick = 0.5f;

    private sealed class Rig
    {
        public required Arsenal Arsenal;
        public required RecordingEngine Engine;
        public required WeaponSwitchTests.SpawningEntities World;
        public required ScriptedEntityRegistry Registry;

        public int Pool => Arsenal.Pools(Player).TryGetValue("slam", out int n) ? n : 0;

        public WeaponNetState WeaponState =>
            ((SsWeapon)Registry.Get(Arsenal.CarriedPair(Player).Weapon)!).State;

        /// <summary>
        /// One queued command of the burst, at the tick stock's RunCommand
        /// would run it on: the tracked tick base times the interval — the
        /// same arithmetic ServerModule.CommandClock does.
        /// </summary>
        public void Command(int tick, int buttons, int tickCount = 0)
        {
            var cmd = new UserCmd { CommandNumber = tick, TickCount = tickCount, Buttons = buttons };
            Arsenal.OnPlayerCommand(Player, in cmd, firstTime: true, commandTime: tick * Tick);
        }
    }

    /// <summary>
    /// The slam in hands, the deploy stamped at the fixed frame clock: the
    /// burst's command clocks therefore cross it tick by tick, and only the
    /// command whose own tick has passed it can fire.
    /// </summary>
    private static Rig Armed()
    {
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", 1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f))
            .Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f))
            .Returns("Entities.GetWorldSpaceCenter", new Vector(0f, 0f, 36f))
            .Returns("Entities.GetVelocity", new Vector(0f, 0f, 0f))
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            .Returns("WorldQuery.TraceLine",
                     new TraceResult(false, 1f, new Vector(20f, 0f, 64f),
                                     new Vector(-1f, 0f, 0f), EntityHandle.None))
            .Returns("Clock.Now", FrameClock);

        engine.ReturnsEachCall("EntityRender.LookupSequenceByActivity",
            () => Array.FindIndex(SlamMachineTests.VSlam, s => s.Activity == engine.Calls[^1].ArgAs<string>(1)));
        // One answer for every anim: the deploy stamp then sits exactly a
        // tenth past the frame clock, so the command clock alone can cross it.
        engine.Returns("EntityRender.SequenceDuration", 0.1f);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out RecordingEngine entityEngine);

        entityEngine.Strict = false;
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");

        var world = new WeaponSwitchTests.SpawningEntities(engine, registry);

        var arsenal = new Arsenal(new RecordingLog(), world, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups());

        Assert.True(arsenal.GiveWeapon(Player, EntityNames.WeaponSlam), "armed with the SLAM");

        // The carried pair exists from here on: the viewmodel answers its sequences.
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;

        return new Rig { Arsenal = arsenal, Engine = engine, World = world, Registry = registry };
    }

    /// <summary>
    /// The first command whose tick has passed the deploy's stamp fires, and
    /// the ones before it do not: with a burst held to one frame the machine
    /// moves on each command's own tick time, not on the frame's one sampled
    /// clock every command of the burst would share.
    /// </summary>
    [Fact]
    public void EachQueuedCommandOfABurstRunsOnItsOwnTickTime()
    {
        var rig = Armed();

        // The deploy stamped itself at the frame clock plus the draw anim; the
        // eligible command is the first tick whose time reaches that stamp.
        float stamp = rig.WeaponState.NextPrimaryAttack;
        int firstEligibleTick = (int)Math.Ceiling(stamp / Tick);

        // Command one, at the tick before: the stamp still holds the press.
        rig.Command(firstEligibleTick - 1, Attack);
        Assert.Equal(3, rig.Pool);

        // Command two carries its own tick, past the stamp: the press goes in
        // there, the throw itself completing on the next command's Think (the
        // slam machine's own order, the one the existing slam facts follow).
        rig.Command(firstEligibleTick, Attack);

        // Command three, no button held: the tick past the stamp lets the
        // queued throw off, and nothing else comes with it.
        rig.Command(firstEligibleTick + 2, buttons: 0);
        Assert.Equal(2, rig.Pool);
        Assert.Single(rig.Registry.OfClass(EntityNames.NpcSatchel).OfType<Satchel>());
    }

    /// <summary>
    /// A command claiming a far-future tick count still runs on the tracked
    /// tick base's tick: the claim is the client's word about the wire, not a
    /// clock the server's weapon frame reads, so the burst crosses the deploy's
    /// stamp at the same tick it always does.
    /// </summary>
    [Fact]
    public void ACommandClaimingAFutureTickCountRunsOnTheTickBaseAnyway()
    {
        var rig = Armed();

        float stamp = rig.WeaponState.NextPrimaryAttack;
        int firstEligibleTick = (int)Math.Ceiling(stamp / Tick);

        rig.Command(firstEligibleTick - 1, Attack, tickCount: 40_000);
        Assert.Equal(3, rig.Pool);

        rig.Command(firstEligibleTick, Attack, tickCount: 40_000);
        rig.Command(firstEligibleTick + 2, buttons: 0, tickCount: 40_000);
        Assert.Equal(2, rig.Pool);
        Assert.Single(rig.Registry.OfClass(EntityNames.NpcSatchel).OfType<Satchel>());
    }
}
