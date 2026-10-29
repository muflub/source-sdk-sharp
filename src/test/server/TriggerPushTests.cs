using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// trigger_push's arms, read off stock's <c>CTriggerPush::Touch</c>
/// (triggers.cpp), which runs every frame a toucher is inside (a player: after
/// each command, from ProcessImpacts): nothing for a
/// toucher that is not solid, is a pusher or a fixture, or is parented; a
/// push-once pad impulses and removes itself; nothing for noclip; a VPHYSICS
/// object gets <c>ApplyForceCenter(speed*dir*100*frametime)</c>; a player gets
/// speed*dir as base velocity. The player arm is the bug fixed here: the
/// impulse that stood in for it landed on the player's shadow and lifted
/// nobody.
/// </summary>
public sealed unsafe class TriggerPushTests : IDisposable
{
    private static readonly EntityHandle Ent = new(0x0011_0011u);
    private static readonly EntityHandle Toucher = new(0x0012_0012u);

    /// <summary>A tick that is not the default 0.015, so a push that ignored the globals is a different number.</summary>
    private const float Tick = 0.03f;

    /// <summary>The engine's frame time, distinct from the tick so the force reads the right one.</summary>
    private const float FrameTime = 0.02f;

    /// <summary>Push once (128), with clients (1): a map's pad names who it launches, or nobody passes.</summary>
    private const int PushOnceClients = 128 | 1;

    private const EntityState PlayerState = EntityState.Solid | EntityState.Player;
    private const EntityState PropState = EntityState.Solid | EntityState.Physics;

    private readonly CGlobalVarsRaw* _globals = (CGlobalVarsRaw*)NativeMemory.AllocZeroed((nuint)sizeof(CGlobalVarsRaw));

    public TriggerPushTests() => _globals->interval_per_tick = Tick;

    public void Dispose() => NativeMemory.Free(_globals);

    /// <summary>An upward push of 200 (the sandbox's `forces` station), touched by something in this state.</summary>
    private (ScriptedEntityRegistry Registry, TriggerPush Push, RecordingEngine Engine) Spawn(EntityState state, MoveType moveType,
                                                                                           int spawnflags = 0)
        => Spawn(state, moveType, out _, spawnflags);

    /// <summary>The same, handing back the per-command touches the trigger registers players with.</summary>
    private (ScriptedEntityRegistry Registry, TriggerPush Push, RecordingEngine Engine) Spawn(EntityState state, MoveType moveType,
                                                                                           out PlayerCommandTouches touches,
                                                                                           int spawnflags = 0)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TriggerPush).Assembly);

        var services = TestServices.Services(out RecordingEngine engine);
        engine.Ignores("Clock.Now", "Host.RandomFloat", "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles",
                       "Entities.GetSolidFlags", "Entities.GetSolidType");
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.GetSpawnFlags", spawnflags)
              .Returns("Networking.EntityGetState", state)
              .Returns("Networking.EntityGetState.moveType", moveType)
              .Returns("Frame.GetFrameTime", FrameTime)
              .Returns("Environment.Globals", new Globals(_globals));
        services.AddInstance<IEntityDirectory>(registry);
        touches = new PlayerCommandTouches(registry.Get);
        services.AddInstance(touches);
        registry.Services = services;

        registry.KeyValue(Ent, EntityNames.TriggerPush, "wait", "0");
        registry.KeyValue(Ent, EntityNames.TriggerPush, "pushdir", "-90 0 0");
        registry.KeyValue(Ent, EntityNames.TriggerPush, "speed", "200");
        registry.Spawn(Ent, EntityNames.TriggerPush);

        return (registry, (TriggerPush)registry.Get(Ent)!, engine);
    }

    private static bool Up(Vector v, float z) => MathF.Abs(v.X) < 0.001f && MathF.Abs(v.Y) < 0.001f && MathF.Abs(v.Z - z) < 0.001f;

    /// <summary>Every push of any kind the trigger made.</summary>
    private static int Pushes(RecordingEngine engine)
        => engine.CountOf("Entities.AddBaseVelocity") + engine.CountOf("Entities.ApplyVelocityImpulse")
           + engine.CountOf("EntityPhysics.ApplyForceCenter");

    [Fact]
    public void APlayerEnteringIsPushedThroughItsBaseVelocityAndHeldForItsCommands()
    {
        var (registry, _, engine) = Spawn(PlayerState, MoveType.Walk, out PlayerCommandTouches touches);

        registry.Touch(Ent, Toucher);

        EngineCall? push = engine.Last("Entities.AddBaseVelocity");
        Assert.True(push is { } p && p.ArgAs<EntityHandle>(0) == Toucher && Up(p.ArgAs<Vector>(1), 200f) && Pushes(engine) == 1
                    && touches.Holds(Toucher, Ent),
                    $"base velocity {push?.ArgAs<Vector>(1)}, pushes {Pushes(engine)}, held {touches.Holds(Toucher, Ent)}");
    }

    /// <summary>
    /// Stock's Touch reaches a player from ProcessImpacts after each command,
    /// and the next command consumes the base velocity it set. Pushed per tick
    /// instead, the ticks between one batch's commands added it up (two or
    /// three times over) and the commands not renewed folded it into the
    /// velocity: dm_lockdown's fan_wind flung the live gate's player.
    /// </summary>
    [Fact]
    public void APlayerIsPushedOncePerCommandAndNotPerTick()
    {
        var (registry, push, engine) = Spawn(PlayerState, MoveType.Walk, out PlayerCommandTouches touches);
        registry.Touch(Ent, Toucher);

        push.OnThink();
        push.OnThink();
        int afterTicks = engine.CountOf("Entities.AddBaseVelocity");
        touches.ProcessImpacts(Toucher);
        touches.ProcessImpacts(Toucher);

        Assert.True(afterTicks == 1 && engine.CountOf("Entities.AddBaseVelocity") == 3
                    && Up(engine.Last("Entities.AddBaseVelocity")!.Value.ArgAs<Vector>(1), 200f),
                    $"pushes after two ticks {afterTicks}, after two commands {engine.CountOf("Entities.AddBaseVelocity")}");
    }

    [Fact]
    public void APlayerWhoLeftIsNotPushedByTheirNextCommandAndIsLetGo()
    {
        var (registry, _, engine) = Spawn(PlayerState, MoveType.Walk, out PlayerCommandTouches touches);
        registry.Touch(Ent, Toucher);
        registry.EndTouch(Ent, Toucher);

        touches.ProcessImpacts(Toucher);

        Assert.True(engine.CountOf("Entities.AddBaseVelocity") == 1 && !touches.Holds(Toucher, Ent),
                    $"pushes {engine.CountOf("Entities.AddBaseVelocity")}, held {touches.Holds(Toucher, Ent)}");
    }

    [Fact]
    public void ADisabledPushLetsGoAndEnablingItHoldsThePlayerAgain()
    {
        var (registry, push, engine) = Spawn(PlayerState, MoveType.Walk, out PlayerCommandTouches touches);
        registry.Touch(Ent, Toucher);

        registry.Input(Ent, "Disable", "", 0f, EntityHandle.None);
        touches.ProcessImpacts(Toucher);
        bool heldWhileOff = touches.Holds(Toucher, Ent);
        registry.Directory.RunFrame(1.0f);   // the booked think, which a disabled volume does not renew
        float whileOff = push.NextThinkDelay();
        registry.Input(Ent, "Enable", "", 0f, EntityHandle.None);
        float resumesIn = push.NextThinkDelay();
        push.OnThink();
        touches.ProcessImpacts(Toucher);

        Assert.True(!heldWhileOff && whileOff < 0f && MathF.Abs(resumesIn - Tick) < 1e-6f && touches.Holds(Toucher, Ent)
                    && engine.CountOf("Entities.AddBaseVelocity") == 2,
                    $"held while off {heldWhileOff}, next while off {whileOff}, resumes in {resumesIn}, " +
                    $"pushes {engine.CountOf("Entities.AddBaseVelocity")}");
    }

    [Fact]
    public void APushOncePadImpulsesAPlayerLetsGoOfTheGroundAndGoes()
    {
        var (registry, _, engine) = Spawn(PlayerState, MoveType.Walk, spawnflags: PushOnceClients);

        registry.Touch(Ent, Toucher);

        EngineCall? kick = engine.Last("Entities.ApplyVelocityImpulse");
        Assert.True(kick is { } k && k.ArgAs<EntityHandle>(0) == Toucher && Up(k.ArgAs<Vector>(1), 200f) && k.ArgAs<bool>(2)
                    && Pushes(engine) == 1 && registry.Directory.IsRemoved(Ent),
                    $"impulse {kick?.ArgAs<Vector>(1)}, pushes {Pushes(engine)}, removed {registry.Directory.IsRemoved(Ent)}");
    }

    [Fact]
    public void APushOncePadStaysForAToucherItSkips()
    {
        var (registry, _, engine) = Spawn(EntityState.Player, MoveType.Walk, spawnflags: PushOnceClients);

        registry.Touch(Ent, Toucher);

        Assert.True(Pushes(engine) == 0 && !registry.Directory.IsRemoved(Ent),
                    $"pushes {Pushes(engine)}, removed {registry.Directory.IsRemoved(Ent)}");
    }

    [Fact]
    public void ASimulatedObjectIsPushedByAForceForAHundredKilograms()
    {
        // m_flPushSpeed * vecAbsDir * 100.0f * gpGlobals->frametime, every frame.
        var (registry, push, engine) = Spawn(PropState, MoveType.VPhysics);

        registry.Touch(Ent, Toucher);
        push.OnThink();
        push.TouchFromCommand(Toucher);

        EngineCall? force = engine.Last("EntityPhysics.ApplyForceCenter");
        Assert.True(force is { } f && f.ArgAs<EntityHandle>(0) == Toucher && Up(f.ArgAs<Vector>(1), 200f * 100f * FrameTime)
                    && engine.CountOf("EntityPhysics.ApplyForceCenter") == 2 && Pushes(engine) == 2
                    && MathF.Abs(push.NextThinkDelay() - Tick) < 1e-6f,
                    $"force {force?.ArgAs<Vector>(1)}, pushes {Pushes(engine)}, next {push.NextThinkDelay()}");
    }

    /// <summary>
    /// Stock's refusals: not solid; MOVETYPE_PUSH or NONE; parented; noclip.
    /// And a non-player that is not simulated: stock gives it base velocity,
    /// which only a player has here, so it is not pushed (docs/todo.md).
    /// The positive arm is the player's fact above, with the same stubs.
    /// </summary>
    [Theory]
    [InlineData(EntityState.Player, MoveType.Walk)]
    [InlineData(EntityState.Solid | EntityState.Player, MoveType.Push)]
    [InlineData(EntityState.Solid | EntityState.Player, MoveType.None)]
    [InlineData(EntityState.Solid | EntityState.Physics | EntityState.Parented, MoveType.VPhysics)]
    [InlineData(EntityState.Solid | EntityState.Player, MoveType.NoClip)]
    [InlineData(EntityState.Solid, MoveType.FlyGravity)]
    public void NothingStockSkipsIsPushed(EntityState state, MoveType moveType)
    {
        var (registry, push, engine) = Spawn(state, moveType, out PlayerCommandTouches touches);

        registry.Touch(Ent, Toucher);
        push.OnThink();
        push.TouchFromCommand(Toucher);
        touches.ProcessImpacts(Toucher);

        Assert.Equal(0, Pushes(engine));
    }
}
