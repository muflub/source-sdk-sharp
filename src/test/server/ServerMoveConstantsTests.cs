using System.Globalization;
using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's movement constants, built in managed from what this side
/// declares (plan_server_native_v2 T4g). Each fact is one word of what
/// shell_movement.cpp's FillConstants wrote: a convar into its constant, the
/// registered params into the hulls and the jump, the globals, the clock.
/// </summary>
public sealed unsafe class ServerMoveConstantsTests : IDisposable
{
    private sealed class Backend : IConVarBackend
    {
        private readonly Dictionary<int, string> _held = new();
        private int _next;

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            _held[_next] = defaultValue;
            return _next++;
        }

        public void Set(int id, string value) => _held[id] = value;
        public string Get(int id) => _held.TryGetValue(id, out string? v) ? v : "";
        public string? ValueOf(string name) => null;
    }

    private sealed class Environment : IEngineEnvironment
    {
        public CGlobalVarsRaw* Raw;
        public nint GetInterface(string name) => 0;
        public Globals Globals => new(Raw);
    }

    private static readonly Type[] Declared =
    [
        typeof(SvFrictionConVar), typeof(SvAccelerateConVar), typeof(SvAirAccelerateConVar),
        typeof(SvStopSpeedConVar), typeof(SvMaxVelocityConVar), typeof(SvMaxSpeedConVar),
        typeof(SvBounceConVar), typeof(SvRollAngleConVar), typeof(SvRollSpeedConVar),
        typeof(SvNoclipSpeedConVar), typeof(SvNoclipAccelerateConVar), typeof(SvSpecSpeedConVar),
        typeof(SvSpecAccelerateConVar), typeof(SvSpecNoclipConVar), typeof(SvGravityConVar),
    ];

    private readonly CGlobalVarsRaw* _globals = (CGlobalVarsRaw*)NativeMemory.AllocZeroed((nuint)sizeof(CGlobalVarsRaw));
    private readonly Environment _environment;
    private readonly ConVarRegistry _convars;
    private MovementParams _params = SourceSharp.Shared.Movement.Current;
    private float _clock;

    public ServerMoveConstantsTests()
    {
        _environment = new Environment { Raw = _globals };
        _convars = new ConVarRegistry(new Backend());
        _convars.RegisterTypes(Declared);
    }

    public void Dispose() => NativeMemory.Free(_globals);

    private ServerMoveConstants Subject() => new(_convars, () => _params, _environment, () => _clock);

    private void Set(Type convar, string value)
    {
        foreach (ConVarBase cv in _convars.All)
        {
            if (cv.GetType() != convar)
                continue;

            if (cv is BoolConVar b)
                b.SetValue(value != "0");
            else
                ((FloatConVar)cv).SetValue(float.Parse(value, CultureInfo.InvariantCulture));
            return;
        }

        throw new InvalidOperationException($"{convar.Name} is not registered");
    }

    [Theory]
    [InlineData(typeof(SvFrictionConVar), nameof(MoveConstants.Friction))]
    [InlineData(typeof(SvAccelerateConVar), nameof(MoveConstants.Accelerate))]
    [InlineData(typeof(SvAirAccelerateConVar), nameof(MoveConstants.AirAccelerate))]
    [InlineData(typeof(SvStopSpeedConVar), nameof(MoveConstants.StopSpeed))]
    [InlineData(typeof(SvMaxVelocityConVar), nameof(MoveConstants.MaxVelocity))]
    [InlineData(typeof(SvMaxSpeedConVar), nameof(MoveConstants.MaxSpeed))]
    [InlineData(typeof(SvBounceConVar), nameof(MoveConstants.Bounce))]
    [InlineData(typeof(SvRollAngleConVar), nameof(MoveConstants.RollAngle))]
    [InlineData(typeof(SvRollSpeedConVar), nameof(MoveConstants.RollSpeed))]
    [InlineData(typeof(SvNoclipSpeedConVar), nameof(MoveConstants.NoClipSpeed))]
    [InlineData(typeof(SvNoclipAccelerateConVar), nameof(MoveConstants.NoClipAccelerate))]
    [InlineData(typeof(SvSpecSpeedConVar), nameof(MoveConstants.SpecSpeed))]
    [InlineData(typeof(SvSpecAccelerateConVar), nameof(MoveConstants.SpecAccelerate))]
    public void EachMovementConVarIsItsConstant(Type convar, string field)
    {
        Set(convar, "1234.5");

        MoveConstants c = Subject().Read().Constants;
        object? read = typeof(MoveConstants).GetField(field)!.GetValue(c);

        Assert.True(read is float f && f == 1234.5f, $"{convar.Name} set to 1234.5, {field} read {read}");
    }

    [Fact]
    public void TheCurrentGravityIsSvGravity()
    {
        Set(typeof(SvGravityConVar), "432.25");

        Assert.Equal(432.25f, Subject().Read().CurrentGravity);
    }

    [Fact]
    public void SpecNoClipIsItsBoolConVar()
    {
        Set(typeof(SvSpecNoclipConVar), "1");
        bool on = Subject().Read().Constants.SpecNoClip;
        Set(typeof(SvSpecNoclipConVar), "0");
        bool off = Subject().Read().Constants.SpecNoClip;

        Assert.True(on && !off, $"sv_specnoclip 1 read {on}, 0 read {off}");
    }

    [Fact]
    public void TheJumpAirCapDuckScaleAndHullsAreTheRegisteredParams()
    {
        MovementParams p = _params;
        p.JumpHeight = 33f;
        p.AirSpeedCap = 45f;
        p.DuckSpeedScale = 0.5f;
        p.View = new Vector(0f, 0f, 70f);
        p.HullMin = new Vector(-20f, -20f, 0f);
        p.HullMax = new Vector(20f, 20f, 80f);
        p.DuckHullMin = new Vector(-20f, -20f, 1f);
        p.DuckHullMax = new Vector(20f, 20f, 40f);
        p.DuckView = new Vector(0f, 0f, 30f);
        p.ObsHullMin = new Vector(-11f, -11f, -11f);
        p.ObsHullMax = new Vector(11f, 11f, 11f);
        p.DeadViewHeight = new Vector(0f, 0f, 15f);
        _params = p;

        MoveFrameConstants f = Subject().Read();
        MoveConstants c = f.Constants;

        Assert.True(f.JumpHeight == 33f && c.AirSpeedCap == 45f && c.DuckSpeedScale == 0.5f
                    && c.View.Equals(p.View) && c.HullMin.Equals(p.HullMin) && c.HullMax.Equals(p.HullMax)
                    && c.DuckHullMin.Equals(p.DuckHullMin) && c.DuckHullMax.Equals(p.DuckHullMax)
                    && c.DuckView.Equals(p.DuckView) && c.ObsHullMin.Equals(p.ObsHullMin)
                    && c.ObsHullMax.Equals(p.ObsHullMax) && c.DeadViewHeight.Equals(p.DeadViewHeight),
                    "every registered param reaches its constant");
    }

    [Fact]
    public void MaxClientsAndTheTickAreTheGlobals()
    {
        _globals->maxClients = 7;
        _globals->interval_per_tick = 0.03125f;

        MoveConstants c = Subject().Read().Constants;

        Assert.True(c.MaxClients == 7 && c.TickInterval == 0.03125f,
                    $"maxClients {c.MaxClients}, tick {c.TickInterval}");
    }

    [Fact]
    public void TheEngineTimeIsTheEnginesClock()
    {
        _clock = 9876.5f;

        Assert.Equal(9876.5f, Subject().Read().EngineTime);
    }

    [Fact]
    public void TheOptimizationsAreOnAsShellMovementsConstantIs()
        => Assert.True(Subject().Read().Constants.Optimizations);

    [Fact]
    public void AConVarThisBuildDoesNotDeclareReadsZero()
    {
        var empty = new ConVarRegistry(new Backend());

        MoveFrameConstants f = new ServerMoveConstants(empty, () => _params, _environment, () => 0f).Read();

        Assert.True(f.Constants.Friction == 0f && f.CurrentGravity == 0f && !f.Constants.SpecNoClip,
                    "an unresolved CManagedConVarRef answered 0");
    }
}
