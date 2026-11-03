using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The player's field of view: CBasePlayer::SetFOV (baseplayer_shared.cpp:1946)
/// on the record through the movement route, and GetFOV's ramp
/// (player.cpp:8653) as the shared <see cref="PlayerFov"/> asks it. The words
/// are m_iFOV, m_iFOVStart, m_flFOVTime, m_iDefaultFOV, m_hZoomOwner
/// (player.cpp:8247-8251) and m_Local.m_flFOVRate.
/// </summary>
public sealed unsafe class PlayerFovTests : IDisposable
{
    private static readonly EntityHandle Player = new(0x0003_0002u);
    private static readonly EntityHandle Weapon = new(0x0005_0100u);
    private static readonly EntityHandle Other = new(0x0005_0200u);

    private sealed class Env : IEngineEnvironment
    {
        public CGlobalVarsRaw* Raw;
        public nint GetInterface(string name) => 0;
        public Globals Globals => new(Raw);
    }

    private sealed class Published : IPlayerMovePublisher
    {
        public readonly List<EntityHandle> Calls = new();
        public void Publish(EntityHandle player, ServerPlayerMove move) => Calls.Add(player);
    }

    private readonly CGlobalVarsRaw* _globals = (CGlobalVarsRaw*)NativeMemory.AllocZeroed((nuint)sizeof(CGlobalVarsRaw));
    private readonly ServerPlayerMoves _moves = new();
    private readonly Published _published = new();
    private readonly ServerPlayerMovement _route;

    public PlayerFovTests()
    {
        _globals->interval_per_tick = 0.015f;
        _globals->curtime = 12.5f;
        var engine = new ServerModuleEngine(TestSides.Create(Side.Server));
        var commands = ServerPlayerCommands.For(engine, EngineBound.Trace(engine.Api), EngineBound.SurfaceProps(engine.Api),
                                                _moves, new ServerMoveData(), new NoSounds(), null,
                                                TestSides.Movement(engine), Frame, _published,
                                                new Env { Raw = _globals });
        _route = ServerPlayerMovement.For(engine, _moves, commands);
    }

    public void Dispose() => NativeMemory.Free(_globals);

    private sealed class NoSounds : IMoveSoundSink
    {
        public void MoveSound(int entIndex, Vector origin, int channel, string sample, float volume, int soundLevel,
                              int flags, int pitch) { }

        public void MoveSoundScript(int entIndex, Vector origin, string soundName) { }

        public bool Footsteps => false;

        public int MaxClients => 1;

        public void StepSound(int entIndex, Vector origin, float volume, bool left) { }
    }

    private static MoveFrameConstants Frame()
    {
        MovementParams p = Movement.Current;
        var c = new MoveConstants
        {
            MaxVelocity = 3500f, MaxSpeed = 320f, Friction = 4f, StopSpeed = 100f, Accelerate = 10f,
            AirAccelerate = 10f, AirSpeedCap = 30f, DuckSpeedScale = 0.33333333f, Optimizations = true,
            MaxClients = 2, TickInterval = 0.015f, View = p.View, HullMin = p.HullMin, HullMax = p.HullMax,
            DuckHullMin = p.DuckHullMin, DuckHullMax = p.DuckHullMax, DuckView = p.DuckView,
            ObsHullMin = p.ObsHullMin, ObsHullMax = p.ObsHullMax, DeadViewHeight = p.DeadViewHeight,
        };
        return new MoveFrameConstants(c, 21f, 0f, 1f, 600f);
    }

    // ---- the route ---------------------------------------------------------

    [Fact]
    public void TheDefaultFovIsTheBareAssignment()
    {
        _route.SetDefaultFov(Player, 80);

        var m = _moves.For(Player);
        Assert.True(m.FovDefault == 80, $"the record's default; got {m.FovDefault}");
        Assert.Contains(Player, _published.Calls);   // worded out, as SetFov's is
    }

    [Fact]
    public void ASetFovSetsTheFovAndItsOwner()
    {
        _route.SetFov(Player, Weapon, 20, 0.1f);

        ServerPlayerMove m = _moves.For(Player);
        Assert.True(m.Fov == 20 && m.ZoomOwner == Weapon && m.FovRate == 0.1f,
                    $"the zoom's fov, owner and rate; got {m.Fov}, {m.ZoomOwner.Value}, {m.FovRate}");
        // The ramp starts at the value the player saw — unzoomed, the default
        // (stock's m_iFOVStart = GetFOV()); the clock is the globals'.
        Assert.True(m.FovStart == 90 && m.FovDefault == 90, $"the default start; got {m.FovStart}");
        Assert.Equal(12.5f, m.FovTime);
        Assert.Equal([Player], _published.Calls);
    }

    [Fact]
    public void ASecondRequesterIsRefusedWhileOneHolds()
    {
        _route.SetFov(Player, Weapon, 20, 0.1f);

        _route.SetFov(Player, Other, 40, 0.1f);

        ServerPlayerMove m = _moves.For(Player);
        Assert.True(m.Fov == 20 && m.ZoomOwner == Weapon, $"the first zoom held; got {m.Fov}, {m.ZoomOwner.Value}");
    }

    [Fact]
    public void TheHolderMayChangeItsOwnFov()
    {
        // Stock's owner==requester arm: the holder re-zooms freely
        // (tf_weaponbase_gun.cpp's ZoomIn over ZoomOut).
        _route.SetFov(Player, Weapon, 20, 0.1f);

        _route.SetFov(Player, Weapon, 40, 0.1f);

        ServerPlayerMove m = _moves.For(Player);
        Assert.True(m.Fov == 40 && m.ZoomOwner == Weapon, $"the holder's new zoom; got {m.Fov}");
    }

    [Fact]
    public void AZeroFovReleasesTheOwner()
    {
        _route.SetFov(Player, Weapon, 20, 0.1f);

        _route.SetFov(Player, Weapon, 0, 0.1f);

        ServerPlayerMove m = _moves.For(Player);
        Assert.True(m.Fov == 0 && m.ZoomOwner == EntityHandle.None, "the zoom released");
    }

    [Fact]
    public void AZoomDuringARampStartsFromTheCurrentValue()
    {
        // m_iFOVStart = GetFOV(): the interpolated value, not the last target.
        _route.SetFov(Player, Weapon, 20, 1f);
        ServerPlayerMove m = _moves.For(Player);
        m.FovTime = 12f;    // half a second into the ramp

        _route.SetFov(Player, Weapon, 40, 1f);

        // GetFOV at half the ramp: SimpleSpline(0.5) = 0.5, 90 -> 20 halfway is 55.
        Assert.Equal(55, m.FovStart);
    }

    [Fact]
    public void ANullRequesterIsRefused()
    {
        // Stock's assert( pRequester != NULL ) and return false.
        _route.SetFov(Player, EntityHandle.None, 20, 0.1f);

        ServerPlayerMove m = _moves.For(Player);
        Assert.True(m.Fov == 0 && m.ZoomOwner == EntityHandle.None && m.FovRate == 0f,
                    "nothing recorded for a null requester");
    }

    // ---- the ramp ----------------------------------------------------------

    [Fact]
    public void AnUnzoomedFovIsTheDefault()
        => Assert.Equal(90, PlayerFov.Current(0, 90, 0f, 0f, 90, now: 1f));

    [Fact]
    public void AnImmediateZoomHasNoRamp()
        => Assert.Equal(20, PlayerFov.Current(20, 90, 0f, 0f, 90, now: 0f));

    [Fact]
    public void TheRampInterpolatesFromTheStart()
        // Halfway through a 90 -> 20 zoom over one second: SimpleSpline(0.5) = 0.5.
        => Assert.Equal(55, PlayerFov.Current(20, 90, 0f, 1f, 90, now: 0.5f));

    [Fact]
    public void PastTheZoomTimeTheFovIsTheTarget()
        => Assert.Equal(20, PlayerFov.Current(20, 90, 0f, 1f, 90, now: 2f));
}
