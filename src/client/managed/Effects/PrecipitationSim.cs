//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Effects;

/// <summary>A trace's answer, as much of <c>trace_t</c> as the weather reads.</summary>
public struct PrecipitationTrace
{
    public float Fraction;
    public Vector EndPos;
    public bool StartSolid;

    /// <summary><c>m_pEnt</c> is the volume traced against.</summary>
    public bool HitVolume;

    /// <summary><c>surface.flags</c>.</summary>
    public ushort SurfaceFlags;
}

/// <summary>
/// The world as c_effects.cpp's weather asks it: point contents, two kinds of
/// trace, the PVS, the particle wind and the screen.
/// </summary>
public interface IPrecipitationWorld
{
    /// <summary><c>enginetrace-&gt;GetPointContents</c>.</summary>
    int PointContents(in Vector position);

    /// <summary><c>UTIL_TraceLine</c>; <paramref name="ignorePlayer"/> passes the local player as the ignore entity.</summary>
    PrecipitationTrace TraceLine(in Vector start, in Vector end, uint mask, bool ignorePlayer);

    /// <summary><c>UTIL_TraceModel</c> against the volume's own brush.</summary>
    PrecipitationTrace TraceVolume(in Vector start, in Vector end, in Vector hullMin, in Vector hullMax,
                                   PrecipitationVolume volume);

    /// <summary><c>g_pClientLeafSystem-&gt;IsRenderableInPVS</c> for the volume.</summary>
    bool InPvs(PrecipitationVolume volume);

    /// <summary><c>GetWindspeedAtTime</c>.</summary>
    Vector WindspeedAt(float time);

    /// <summary><c>ScreenWidth()</c>.</summary>
    int ScreenWidth { get; }

    /// <summary>The world entity's bounds (<c>r_RainHack</c>'s arm).</summary>
    Vector WorldMins { get; }

    /// <summary>The world entity's bounds (<c>r_RainHack</c>'s arm).</summary>
    Vector WorldMaxs { get; }
}

/// <summary>
/// The weather's draws, by the stream stock drew them from: the client's
/// <c>random-&gt;</c>, vstdlib's free functions, and the C library's
/// <c>rand()</c> that mathlib's <c>RandomVector</c> reads.
/// </summary>
public interface IPrecipitationRandom
{
    float EngineFloat(float min, float max);

    int EngineInt(int min, int max);

    float GlobalFloat(float min, float max);

    int GlobalInt(int min, int max);

    /// <summary>
    /// <c>rand()</c>: 0..0x7fff. RandomVector divides it by VALVE_RAND_MAX
    /// (0x7fff); glibc's own range (to 2^31-1) leaves every ash particle
    /// outside its volume, so the port draws in MSVC's range (integrator's
    /// ruling, docs/client-novalve-rulings.tsv) from a per-system stream.
    /// </summary>
    int CRand();
}

/// <summary>The local player, as the weather reads it each frame.</summary>
public sealed class PrecipitationPlayer
{
    /// <summary>False: stock's <c>C_BasePlayer::GetLocalPlayer()</c> answered NULL.</summary>
    public bool Exists;
    public Vector Origin;
    public Vector Eye;

    /// <summary><c>GetVectors</c>' forward.</summary>
    public Vector Forward;
    public Vector Velocity;
    public Vector Center;
    public float Fov;
    public int DefaultFov;
}

/// <summary>The weather's console variables, at their stock defaults.</summary>
public sealed class PrecipitationSettings
{
    public float WindDir;            // cl_winddir
    public float WindSpeed;          // cl_windspeed
    public int RainSplashPercentage = 20;
    public int RainHack;
    public float RainRadius = 1500;
    public int RainSideVel = 130;
    public int RainSimulate = 1;
    public int DrawRain = 1;
    public float RainWidth = 0.5f;   // r_rainwidth
    public float RainLength = 0.1f;  // r_rainlength
    public float RainSpeed = 600.0f; // r_rainspeed
    public float RainAlpha = 0.4f;
    public float RainAlphaPow = 0.8f;
    public bool SnowEnable = true;
    public int SnowParticles = 500;
    public float SnowInsideRadius = 256;
    public float SnowOutsideRadius = 1024;
    public float SnowSpeedScale = 1;
    public float SnowPosScale = 1;
    public float SnowFallSpeed = 1.5f;
    public float SnowWindScale = 0.0035f;
    public float SnowZoomOffset = 384.0f;
    public float SnowZoomRadius = 512.0f;
    public int SnowStartAlpha = 25;
    public int SnowEndAlpha = 255;
    public int SnowColorRed = 150;
    public int SnowColorGreen = 175;
    public int SnowColorBlue = 200;
    public int SnowStartSize = 1;
    public int SnowEndSize;
    public float SnowRayLength = 8192.0f;
    public float SnowRayRadius = 256;
    public bool SnowRayEnable = true;
}

/// <summary><c>CPrecipitationParticle</c>.</summary>
public struct PrecipitationParticle
{
    public Vector Pos;
    public Vector Velocity;
    public float SpawnTime;
    public float Mass;
    public float Ramp;
}

/// <summary>One rain or snow tracer for the executor: view-space start and delta.</summary>
public readonly record struct PrecipitationTracer(Vector Start, Vector Delta, float Width, float Alpha);

/// <summary>One <c>FX_WaterRipple</c>.</summary>
public readonly record struct PrecipitationSplash(Vector Origin, float Scale, float Lifetime, float Alpha);

/// <summary>What the render reads of the view.</summary>
public sealed class PrecipitationView
{
    /// <summary><c>CurrentWorldToViewMatrix()</c>, row major, 4x4.</summary>
    public readonly float[] WorldToView = new float[16];
    public Vector Forward;
    public Vector Origin;
}

/// <summary>The ash's emitter rules: <c>AshDebrisEffect</c>.</summary>
public sealed class AshDebrisRules : SimpleEmitterRules
{
    /// <summary><c>ASH_PARTICLE_NOISE</c>.</summary>
    public const byte FlagNoise = 0x4;

    /// <summary>The frame's <c>gpGlobals-&gt;curtime</c>, which the noise reads.</summary>
    public float CurTime;

    public override float UpdateAlpha(in SimpleParticle p) => SimpleEmitterMath.SineAlpha(p);

    public override float UpdateRoll(ref SimpleParticle p, float dt)
    {
        float roll = base.UpdateRoll(ref p, dt);

        if ((p.Flags & FlagNoise) != 0)
        {
            Vector vel = p.Velocity;
            float fastFreq = (float)(CurTime * 1.5);   // a double in stock
            float s = MathF.Sin(fastFreq);
            p.Position = p.Position + new Vector(vel.X * dt * s, vel.Y * dt * s, 0);
        }

        return roll;
    }
}

/// <summary>
/// The wind every rain and snow volume blows by: <c>CClient_Precipitation::s_WindVector</c>,
/// one for the whole system, recomputed by each volume's frame.
/// </summary>
public sealed class PrecipitationWind
{
    public Vector Vector;

    /// <summary><c>ComputeWindVector</c>.</summary>
    public void Compute(PrecipitationSettings settings, IPrecipitationRandom random)
    {
        float yaw = settings.WindDir;
        yaw = yaw + random.EngineFloat(-10, 10);
        float windspeed = (float)(settings.WindSpeed * (1.0 + random.EngineFloat(-0.2f, 0.2f)));

        float y = yaw * MoveVec.DegToRad;
        float sy = MathF.Sin(y), cy = MathF.Cos(y);
        // AngleVectors( QAngle( 0, yaw, 0 ) ): pitch 0, so cp 1 and sp 0.
        var forward = new Vector(1.0f * cy, 1.0f * sy, -0.0f);
        Vector = new Vector(forward.X * windspeed, forward.Y * windspeed, forward.Z * windspeed);
    }
}

/// <summary>
/// One func_precipitation: stock's <c>CClient_Precipitation</c> (c_effects.cpp),
/// held bit for bit to it by PrecipitationOracleTests
/// (Fixtures/precipitation-stock.tsv). Rain and snow are tracers the render
/// plans and native draws; ash is a simple emitter; snowfall hands itself to
/// the system's <see cref="SnowFallManager"/>.
/// </summary>
public sealed class PrecipitationVolume
{
    private const float SnowSpeed = 80.0f;
    private const float RainSpeed = 425.0f;
    private const float MinScreenSpaceRainWidth = 1;
    private const float SplashScale = 0.15f;
    private const float SplashLifetime = 0.5f;
    private const float SplashAlpha = 0.3f;
    private const int ContentsSolid = 0x1;
    private const uint MaskWater = 0x4030;
    private const uint MaskSolidBrushOnly = 0x400B;
    private const uint MaskShotHullNoGrate = 0x6004003;
    private const ushort SurfSky = 0x4;

    /// <summary><c>MAX_TRACE_LENGTH</c>: 1.732050807569 * COORD_EXTENT, as the float a Vector multiply takes.</summary>
    public const float MaxTraceLength = (float)(1.732050807569 * 32768);

    /// <summary>The ash's four materials, by the index its particles carry.</summary>
    public static string AshMaterial(int index) => index switch
    {
        0 => "effects/fleck_ash1",
        1 => "effects/fleck_ash2",
        2 => "effects/fleck_ash3",
        _ => "effects/ember_swirling001",
    };

    private readonly PrecipitationSystem _system;
    private readonly List<PrecipitationParticle> _particles = new();
    private readonly List<Vector> _splashes = new();
    private readonly NoDraws _noDraws = new();

    private bool _precached;
    private float _speed;
    private float _initialRamp;
    private float _lifetime;
    private float _remainder;
    private float _halfScreenWidth = 1;

    // The ash.
    private SimpleEmitter? _ash;
    private TimedEvent _ashTimer = TimedEvent.Uninitialised;
    private TimedEvent _ashTraceTimer = TimedEvent.Uninitialised;
    private bool _ashActive;
    private Vector _ashSpawnOrigin;
    private int _ashCount;

    internal PrecipitationVolume(PrecipitationSystem system, PrecipitationType type, int alpha, Vector mins, Vector maxs)
    {
        _system = system;
        Type = type;
        Mins = mins;
        Maxs = maxs;
        Density = FxMath.RemapVal(alpha, 0, 255, 0, 0.001f);
    }

    public PrecipitationType Type { get; }

    /// <summary>The client entity behind it (<see cref="EntityHandle.None"/> for r_RainHack's).</summary>
    public EntityHandle Owner { get; init; } = EntityHandle.None;

    /// <summary><c>WorldAlignMins()</c>.</summary>
    public Vector Mins { get; set; }

    /// <summary><c>WorldAlignMaxs()</c>.</summary>
    public Vector Maxs { get; set; }

    /// <summary><c>m_flDensity</c>: the render alpha, 0..255, as 0..0.001.</summary>
    public float Density { get; set; }

    public IReadOnlyList<PrecipitationParticle> Particles => _particles;

    public float Remainder => _remainder;

    public float Lifetime => _lifetime;

    /// <summary>The ash's emitter, once its first frame has made it.</summary>
    public SimpleEmitter? AshEmitter => _ash;

    /// <summary>The ash emitter's sort origin (<c>SetSortOrigin</c>).</summary>
    public Vector AshSortOrigin { get; private set; }

    /// <summary><c>ClientThink</c>: <c>Simulate( gpGlobals-&gt;frametime )</c>.</summary>
    internal void Think(float curTime, float dt)
    {
        PrecipitationSettings settings = _system.Settings;
        Precache();
        _halfScreenWidth = (float)_system.World.ScreenWidth / 2;

        if (dt != 0)
            _system.Wind.Compute(settings, _system.Random);

        if (Type == PrecipitationType.Ash)
        {
            CreateAshParticle(curTime, dt);
            return;
        }

        if (Type == PrecipitationType.SnowFall)
            return;

        if (settings.RainHack != 0)
            _lifetime = (_system.World.WorldMaxs.Z - _system.World.WorldMins.Z) / _speed;
        else
            _lifetime = (Maxs.Z - Mins.Z) / _speed;

        if (settings.RainSimulate == 0)
            return;

        EmitParticles(curTime, dt);

        for (int i = 0; i < _particles.Count; i++)
        {
            PrecipitationParticle p = _particles[i];
            bool keep = Type == PrecipitationType.Rain ? SimulateRain(ref p, curTime, dt) : SimulateSnow(ref p, dt);
            if (!keep)
            {
                _particles.RemoveAt(i--);
                continue;
            }

            _particles[i] = p;
        }
    }

    private void Precache()
    {
        if (_precached)
            return;

        switch (Type)
        {
            case PrecipitationType.Snow:
                _speed = SnowSpeed;
                _initialRamp = 0.6f;
                _precached = true;
                break;
            case PrecipitationType.Rain:
                _speed = RainSpeed;
                _initialRamp = 1.0f;
                _precached = true;
                break;
            default:
                // Stock finds no material for these, so this runs every frame;
                // it sets nothing they read.
                _initialRamp = 1.0f;
                break;
        }
    }

    private float RemainingLifetime(in PrecipitationParticle p, float curTime) => _lifetime - (curTime - p.SpawnTime);

    private bool SimulateRain(ref PrecipitationParticle p, float curTime, float dt)
    {
        if (RemainingLifetime(p, curTime) < 0.0f)
            return false;

        Vector oldPos = p.Pos;
        p.Pos = FxMath.MA(p.Pos, dt, p.Velocity);
        Blow(ref p, snow: false);

        if (!IsInAir(p.Pos))
        {
            // Possibly a splash, if it went through water.
            if (_splashes.Count < 20 &&
                _system.Random.GlobalInt(0, 100) < _system.Settings.RainSplashPercentage)
            {
                PrecipitationTrace trace = _system.World.TraceLine(oldPos, p.Pos, MaskWater, ignorePlayer: false);
                if (trace.Fraction < 1)
                    _splashes.Add(trace.EndPos);
            }

            return false;
        }

        return true;
    }

    private bool SimulateSnow(ref PrecipitationParticle p, float dt)
    {
        if (!IsInAir(p.Pos))
            return false;

        p.Pos = FxMath.MA(p.Pos, dt, p.Velocity);
        Blow(ref p, snow: true);
        return true;
    }

    // The wind blows the particle towards s_WindVector, 5 / mass a step.
    private void Blow(ref PrecipitationParticle p, bool snow)
    {
        Vector wind = _system.Wind.Vector;
        for (int i = 0; i < 2; i++)
        {
            float v = i == 0 ? p.Velocity.X : p.Velocity.Y;
            float w = i == 0 ? wind.X : wind.Y;
            if (v < w)
            {
                v += 5 / p.Mass;
                if (snow)
                    p.Ramp = 0.5f;
                if (v > w)
                    v = w;
            }
            else if (v > w)
            {
                v -= 5 / p.Mass;
                if (snow)
                    p.Ramp = 0.5f;
                if (v < w)
                    v = w;
            }

            if (i == 0)
                p.Velocity.X = v;
            else
                p.Velocity.Y = v;
        }
    }

    private bool IsInAir(in Vector position) => (_system.World.PointContents(position) & ContentsSolid) == 0;

    private bool ComputeEmissionArea(out Vector origin, out float sizeX, out float sizeY)
    {
        origin = default;
        sizeX = sizeY = 0;
        PrecipitationSettings settings = _system.Settings;
        float emissionSize = settings.RainRadius;

        Vector mins = Mins, maxs = Maxs;
        if (settings.RainHack != 0)
        {
            mins = _system.World.WorldMins;
            maxs = _system.World.WorldMaxs;
        }

        PrecipitationPlayer player = _system.Player;
        if (!player.Exists)
            return false;

        float emissionHeight = FxMath.Min(maxs.Z, player.Origin.Z + 512);
        float distToFall = emissionHeight - player.Origin.Z;
        float fallTime = distToFall / settings.RainSpeed;

        Vector wind = _system.Wind.Vector;
        float centerX = player.Origin.X - fallTime * wind.X;
        float centerY = player.Origin.Y - fallTime * wind.Y;

        float loX = centerX - emissionSize * 0.5f;
        float loY = centerY - emissionSize * 0.5f;
        float hiX = loX + emissionSize;
        float hiY = loY + emissionSize;

        if (maxs.X < loX || maxs.Y < loY || mins.X > hiX || mins.Y > hiY)
            return false;

        origin = new Vector(FxMath.Max(mins.X, loX), FxMath.Max(mins.Y, loY), emissionHeight);
        hiX = FxMath.Min(maxs.X, hiX);
        hiY = FxMath.Min(maxs.Y, hiY);
        sizeX = hiX - origin.X;
        sizeY = hiY - origin.Y;
        return true;
    }

    private void EmitParticles(float curTime, float dt)
    {
        PrecipitationPlayer player = _system.Player;
        if (!player.Exists)
            return;
        Vector playerCenter = player.Center;

        if (!ComputeEmissionArea(out Vector org, out float sizeX, out float sizeY))
            return;

        if (dt > 0.075f)
            dt = 0.075f;

        float density = Density;
        if (density > 0.01f)
            density = 0.01f;

        float fParticles = sizeX * sizeY * density * dt + _remainder;
        int cParticles = (int)fParticles;
        _remainder = fParticles - cParticles;

        Vector wind = _system.Wind.Vector;
        var vel = new Vector(wind.X, wind.Y, wind.Z - _system.Settings.RainSpeed);

        for (int i = 0; i < cParticles; i++)
        {
            Vector pos = org;
            pos.X += sizeX * _system.Random.EngineFloat(0, 1);
            pos.Y += sizeY * _system.Random.EngineFloat(0, 1);

            // Up from the player's height: a brush in the way stops the
            // particle, unless it is sky, where the particle starts.
            Vector playerHeight = pos;
            playerHeight.Z = playerCenter.Z;
            PrecipitationTrace trace = _system.World.TraceLine(playerHeight, pos, MaskSolidBrushOnly, ignorePlayer: false);
            if (trace.Fraction < 1)
            {
                if ((trace.SurfaceFlags & SurfSky) != 0)
                    pos = trace.EndPos;
                else
                    continue;
            }

            CreateRainOrSnowParticle(pos, vel, curTime);
        }
    }

    private void CreateRainOrSnowParticle(Vector spawn, Vector velocity, float curTime)
    {
        int side = _system.Settings.RainSideVel;
        var p = new PrecipitationParticle
        {
            SpawnTime = curTime,
            Ramp = _initialRamp,
            Velocity = velocity,
            Pos = spawn,
        };
        p.Velocity.X += _system.Random.EngineFloat(-side, side);
        p.Velocity.Y += _system.Random.EngineFloat(-side, side);
        p.Mass = _system.Random.EngineFloat(0.5f, 1.5f);
        _particles.Add(p);
    }

    //-------------------------------------------------------------------------
    // The ash
    //-------------------------------------------------------------------------

    private Vector RandomVector(float min, float max)
    {
        IPrecipitationRandom r = _system.Random;
        float x = min + ((float)r.CRand() / ParticleRandomExtensions.ValveRandMax) * (max - min);
        float y = min + ((float)r.CRand() / ParticleRandomExtensions.ValveRandMax) * (max - min);
        float z = min + ((float)r.CRand() / ParticleRandomExtensions.ValveRandMax) * (max - min);
        return new Vector(x, y, z);
    }

    private void CreateAshParticle(float curTime, float frameTime)
    {
        if (_ash is null)
        {
            _ash = new SimpleEmitter(new AshDebrisRules());
            _ashTimer.Init(192);
            _ashTraceTimer.Init(15);
            _ashActive = false;
            _ashCount = 0;
        }

        PrecipitationPlayer player = _system.Player;
        if (!player.Exists)
            return;

        Vector forward = player.Forward;
        forward.Z = 0.0f;

        float delta = frameTime;
        Vector absMins = Mins, absMaxs = Maxs;

        // 15 traces a second.
        while (_ashTraceTimer.Next(ref delta))
        {
            Vector traceStart = player.Eye;
            Vector traceEnd = player.Eye + forward * MaxTraceLength;

            PrecipitationTrace tr = _system.World.TraceLine(traceStart, traceEnd, MaskShotHullNoGrate, ignorePlayer: true);
            if (tr.Fraction != 1.0f)
            {
                PrecipitationTrace tr2 = _system.World.TraceVolume(traceStart, tr.EndPos,
                    new Vector(-1, -1, -1), new Vector(1, 1, 1), this);

                if (tr2.HitVolume)
                {
                    _ashActive = true;
                    _ashSpawnOrigin = !tr2.StartSolid ? tr2.EndPos + forward * 256 : traceStart;
                }
                else
                {
                    _ashActive = false;
                }
            }
        }

        if (!_ashActive)
            return;

        Vector velocity = player.Velocity;
        float speed = FxMath.Normalize(ref velocity);
        AshSortOrigin = _ashSpawnOrigin;

        Vector spawnOffset = default;
        if (speed > 0)
            spawnOffset = (forward * 256) + (velocity * (speed * 2));

        while (_ashTimer.Next(ref delta))
        {
            int randomAltitude = _system.Random.GlobalInt(0, 128);

            Vector offset = _ashSpawnOrigin + spawnOffset + RandomVector(-256, 256);
            offset.Z = _ashSpawnOrigin.Z + randomAltitude;

            if (offset.X > absMaxs.X || offset.Y > absMaxs.Y || offset.Z > absMaxs.Z ||
                offset.X < absMins.X || offset.Y < absMins.Y || offset.Z < absMins.Z)
                continue;

            _ashCount++;

            bool emberTime = false;
            if (_ashCount >= 250)
            {
                emberTime = true;
                _ashCount = 0;
            }

            int material = _system.Random.EngineInt(0, 2);

            if (emberTime)
            {
                offset = _ashSpawnOrigin + (forward * 256) + RandomVector(-128, 128);
                offset.Z = player.Eye.Z + _system.Random.GlobalFloat(-16, 64);
                material = 3;
            }

            var p = new SimpleParticle
            {
                Id = _system.NextParticleId(),
                Material = material,
                Position = offset,
                Lifetime = 0.0f,
                DieTime = FxMath.RemapVal(randomAltitude, 0, 128, 4, 8),
            };

            if (emberTime)
            {
                Vector goal = player.Eye + RandomVector(-64, 64);
                Vector dir = goal - offset;
                FxMath.Normalize(ref dir);
                p.Velocity = dir * 75;
                p.DieTime = 2.5f;
            }
            else
            {
                float vx = _system.Random.GlobalFloat(-20.0f, 20.0f);
                float vy = _system.Random.GlobalFloat(-20.0f, 20.0f);
                float vz = _system.Random.GlobalFloat(-10, -15);
                p.Velocity = new Vector(vx, vy, vz);
            }

            float color = _system.Random.EngineInt(125, 225);
            p.R = p.G = p.B = (byte)color;
            p.StartSize = 1;
            p.EndSize = 1;
            p.StartAlpha = 255;
            p.Roll = _system.Random.EngineInt(0, 360);
            p.RollDelta = _system.Random.EngineFloat(-0.15f, 0.15f);
            p.Flags = SimpleEmitterRules.FlagWindBlown;

            if (_system.Random.EngineInt(0, 10) <= 1)
                p.Flags |= AshDebrisRules.FlagNoise;

            _ash.Add(p);
        }
    }

    /// <summary>The ash emitter's particle step (the particle manager's, in stock).</summary>
    internal void SimulateAsh(float curTime, float dt)
    {
        if (_ash is null)
            return;

        var rules = (AshDebrisRules)_ash.Rules;
        rules.CurTime = curTime;
        rules.Wind = _system.World.WindspeedAt(curTime);
        _ash.Simulate(dt, _noDraws);
    }

    //-------------------------------------------------------------------------
    // The render plan
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>Render</c>: the queued splashes, then a tracer per particle. Native
    /// draws both. A snow particle's trail is shortened here, in the render,
    /// as stock's is (<c>m_Ramp = 0.3</c>), and the next frame's simulation
    /// sees it.
    /// </summary>
    internal void Render(PrecipitationView view, float curTime, List<PrecipitationTracer> tracers, List<PrecipitationSplash> splashes)
    {
        PrecipitationSettings settings = _system.Settings;
        if (settings.DrawRain == 0)
            return;
        if (Type is PrecipitationType.Ash or PrecipitationType.SnowFall)
            return;

        foreach (Vector splash in _splashes)
        {
            if (FxMath.Dot(view.Forward, splash - view.Origin) > 1)
                splashes.Add(new PrecipitationSplash(splash, SplashScale, SplashLifetime, SplashAlpha));
        }

        _splashes.Clear();

        for (int i = 0; i < _particles.Count; i++)
        {
            PrecipitationParticle p = _particles[i];
            if (RenderParticle(ref p, view, curTime, out PrecipitationTracer tracer))
                tracers.Add(tracer);
            _particles[i] = p;
        }
    }

    private bool RenderParticle(ref PrecipitationParticle p, PrecipitationView view, float curTime, out PrecipitationTracer tracer)
    {
        tracer = default;
        PrecipitationSettings settings = _system.Settings;

        float lifetimeRemaining = RemainingLifetime(p, curTime);
        float scale = lifetimeRemaining >= settings.RainLength
            ? settings.RainLength * p.Ramp
            : lifetimeRemaining * p.Ramp;

        float[] m = view.WorldToView;
        Vector start = MultiplyPosition(m, p.Pos);
        if (start.Z > -1)
            return false;

        Vector delta = Multiply(m, p.Velocity);

        if (Type == PrecipitationType.Snow)
        {
            if (p.Mass > 1.0f)
            {
                float a = (float)(curTime * Math.PI * (1 + p.Mass * 0.1f) + p.Mass * 5.0f);
                float s = MathF.Sin(a), c = MathF.Cos(a);
                var spiral = new Vector(28 * c, 28 * s, 0.0f);
                Vector camSpiral = Multiply(m, spiral);
                start = start + camSpiral;
                delta = delta + camSpiral;
            }

            p.Ramp = 0.3f;
        }

        delta = new Vector(delta.X * scale, delta.Y * scale, delta.Z * scale);

        float alpha = settings.RainAlpha;
        float width = settings.RainWidth;
        float screenWidth = width * _halfScreenWidth / -start.Z;
        if (screenWidth < MinScreenSpaceRainWidth)
        {
            alpha *= screenWidth / MinScreenSpaceRainWidth;
            width = MinScreenSpaceRainWidth * -start.Z / _halfScreenWidth;
        }

        alpha = MathF.Pow(alpha, settings.RainAlphaPow);
        tracer = new PrecipitationTracer(start, delta, width, alpha);
        return true;
    }

    // Vector3DMultiplyPosition and Vector3DMultiply (mathlib/vmatrix.h).
    private static Vector MultiplyPosition(float[] m, in Vector v)
        => new(m[0] * v.X + m[1] * v.Y + m[2] * v.Z + m[3],
               m[4] * v.X + m[5] * v.Y + m[6] * v.Z + m[7],
               m[8] * v.X + m[9] * v.Y + m[10] * v.Z + m[11]);

    private static Vector Multiply(float[] m, in Vector v)
        => new(m[0] * v.X + m[1] * v.Y + m[2] * v.Z,
               m[4] * v.X + m[5] * v.Y + m[6] * v.Z,
               m[8] * v.X + m[9] * v.Y + m[10] * v.Z);

    private sealed class NoDraws : IParticleRandom
    {
        public float RandomFloat(float min, float max) => throw new InvalidOperationException("the ash's step draws nothing");

        public int RandomInt(int min, int max) => throw new InvalidOperationException("the ash's step draws nothing");
    }
}

/// <summary>
/// Stock's <c>CSnowFallManager</c> and its <c>SnowFallEffect</c>: the snow that
/// falls around the player inside (or within sight of) a snowfall volume. One
/// per system, made by the first snowfall volume and, as in stock, destroyed
/// by the destruction of ANY precipitation volume.
/// </summary>
public sealed class SnowFallManager
{
    private const int MaxSnowList = 32;
    private const int AreaNone = 0;
    private const int AreaAroundPlayer = 1;
    private const int AreaInEntity = 2;
    private const int ContentsSolid = 0x1;

    private readonly PrecipitationSystem _system;
    private readonly List<(PrecipitationVolume Volume, Vector Min, Vector Max)> _snow = new();
    private readonly List<SimpleParticle> _particles = new();
    private readonly int[] _active = new int[MaxSnowList];

    private TimedEvent _particleTimer = TimedEvent.Uninitialised;
    private TimedEvent _traceTimer = TimedEvent.Uninitialised;
    private int _area;
    private Vector _emitOrigin;
    private float _radius;
    private Vector _min = new(float.MaxValue, float.MaxValue, float.MaxValue);
    private Vector _max = new(1.17549435E-38f, 1.17549435E-38f, 1.17549435E-38f);   // FLT_MIN, as stock
    private int _activeCount;
    private bool _rayParticles;

    internal SnowFallManager(PrecipitationSystem system)
    {
        _system = system;
        // SpawnClientEntity.
        _particleTimer.Init(500);
        _traceTimer.Init(6);
        _area = AreaNone;
    }

    /// <summary>The emitter's particles, newest first.</summary>
    public IReadOnlyList<SimpleParticle> Particles => _particles;

    /// <summary>The emitter's bounding box (<c>SetBounds</c>).</summary>
    public (Vector Min, Vector Max) Bounds => (_min, _max);

    /// <summary>The emitter's sort origin.</summary>
    public Vector SortOrigin { get; private set; }

    /// <summary><c>AddSnowFallEntity</c>.</summary>
    internal void Add(PrecipitationVolume volume)
    {
        foreach (var s in _snow)
        {
            if (s.Volume == volume)
                return;
        }

        _snow.Add((volume, volume.Mins, volume.Maxs));

        // UpdateBounds.
        float[] min = [_min.X, _min.Y, _min.Z], max = [_max.X, _max.Y, _max.Z];
        float[] smin = [volume.Mins.X, volume.Mins.Y, volume.Mins.Z], smax = [volume.Maxs.X, volume.Maxs.Y, volume.Maxs.Z];
        for (int axis = 0; axis < 3; axis++)
        {
            if (smin[axis] < min[axis])
                min[axis] = smin[axis];
            if (smax[axis] > max[axis])
                max[axis] = smax[axis];
        }

        _min = new Vector(min[0], min[1], min[2]);
        _max = new Vector(max[0], max[1], max[2]);
    }

    /// <summary><c>ClientThink</c>.</summary>
    internal void Think(float frameTime)
    {
        if (!_system.Settings.SnowEnable)
            return;
        CreateSnowFall(frameTime);
    }

    private int StandingInSnowVolume(in Vector point)
    {
        for (int i = 0; i < _snow.Count; i++)
        {
            PrecipitationTrace trace = _system.World.TraceVolume(point, point, default, default, _snow[i].Volume);
            if (trace.StartSolid)
                return i;
        }

        return -1;
    }

    private void FindSnowVolumes(in Vector center, float radius, in Vector eyePos, in Vector forward)
    {
        _activeCount = 0;
        _rayParticles = false;

        for (int i = 0; i < _snow.Count; i++)
        {
            if (!_system.World.InPvs(_snow[i].Volume))
                continue;

            if (CollisionMath.IsBoxIntersectingSphere(_snow[i].Min, _snow[i].Max, center, radius))
            {
                _active[_activeCount++] = i;
                if (_activeCount >= MaxSnowList)
                    break;
            }
            else
            {
                Vector newForward = forward * _system.Settings.SnowRayLength;
                newForward.Z = 0.0f;
                if (CollisionMath.IntersectRayWithBoxFraction(eyePos, newForward, _snow[i].Min, _snow[i].Max, 0.325f) < 1.0f)
                {
                    _active[_activeCount++] = i;
                    if (_activeCount >= MaxSnowList)
                        break;
                    _rayParticles = true;
                }
            }
        }
    }

    private void CreateSnowFall(float frameTime)
    {
        PrecipitationPlayer player = _system.Player;
        if (!player.Exists)
            return;

        PrecipitationSettings settings = _system.Settings;
        float currentTime = frameTime;

        _emitOrigin = player.Eye;
        Vector forward = player.Forward;
        forward.Z = 0.0f;
        Vector velocity = player.Velocity;
        float speed = FxMath.Normalize(ref velocity);
        _emitOrigin = _emitOrigin + (forward * (64.0f + (speed * 0.4f * settings.SnowPosScale)));
        _emitOrigin = _emitOrigin + (velocity * (speed * 1.25f * settings.SnowSpeedScale));

        bool zoomed = player.Fov != player.DefaultFov;
        float zoomScale = 1.0f;
        if (zoomed)
        {
            zoomScale = player.DefaultFov / player.Fov;
            zoomScale *= 0.5f;
        }

        // Six times a second: which volumes are near or in sight.
        if (_traceTimer.Next(ref currentTime))
        {
            _area = AreaNone;
            Vector traceStart = player.Eye;

            int volume = StandingInSnowVolume(traceStart);
            if (volume != -1)
            {
                _radius = settings.SnowInsideRadius + (speed * 0.5f);
                _area = AreaAroundPlayer;
            }
            else
            {
                _radius = settings.SnowOutsideRadius;
            }

            float radius = _radius;
            if (zoomed)
            {
                if (_area == AreaAroundPlayer)
                    radius = settings.SnowOutsideRadius * zoomScale;
                else
                    radius *= zoomScale;
            }

            FindSnowVolumes(_emitOrigin, radius, player.Eye, forward);
            if (_activeCount != 0 && _area != AreaAroundPlayer)
                _area = AreaInEntity;
        }

        if (_area == AreaNone)
            return;

        SortOrigin = _emitOrigin;

        if (_area == AreaInEntity)
            CreateOutsideVolumeSnowParticles(currentTime, _radius, zoomScale);
        else
            CreateInsideVolumeSnowParticles(currentTime, _radius, player.Eye, forward, zoomScale);
    }

    private void CreateOutsideVolumeSnowParticles(float currentTime, float radius, float zoomScale)
    {
        int snow = 0;
        float radiusScaled = radius * zoomScale;
        float radius2 = radiusScaled * radiusScaled;

        while (_particleTimer.Next(ref currentTime))
        {
            if (_particles.Count >= _system.Settings.SnowParticles)
                continue;

            var s = _snow[_active[snow]];
            var spawn = new Vector(_system.Random.GlobalFloat(s.Min.X, s.Max.X), 0, 0);
            spawn.Y = _system.Random.GlobalFloat(s.Min.Y, s.Max.Y);
            spawn.Z = _system.Random.GlobalFloat(s.Min.Z, s.Max.Z);

            Vector d = _emitOrigin - spawn;
            float distance2 = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
            if (distance2 < radius2)
                CreateSnowFallParticle(spawn, _active[snow]);

            snow = (snow + 1) % _activeCount;
        }
    }

    private void CreateInsideVolumeSnowParticles(float currentTime, float radius, in Vector eyePos, in Vector forward, float zoomScale)
    {
        bool zoomed = zoomScale > 1.0f;
        float zoomRadius = 0.0f;
        if (zoomed)
            zoomRadius = radius * zoomScale;

        int index = 0;
        while (_particleTimer.Next(ref currentTime))
        {
            if (_particles.Count >= _system.Settings.SnowParticles)
                continue;

            if (index > 0)
            {
                CreateSnowParticlesSphere(zoomRadius);
                CreateSnowParticlesRay(zoomRadius, eyePos, forward);
            }
            else
            {
                CreateSnowParticlesSphere(radius);
                CreateSnowParticlesRay(radius, eyePos, forward);
            }

            if (zoomed)
                index = (index + 1) % 3;
        }
    }

    private int ContainingActive(in Vector spawn)
    {
        for (int i = 0; i < _activeCount; i++)
        {
            var s = _snow[_active[i]];
            if (spawn.X < s.Min.X || spawn.X > s.Max.X)
                continue;
            if (spawn.Y < s.Min.Y || spawn.Y > s.Max.Y)
                continue;
            if (spawn.Z < s.Min.Z || spawn.Z > s.Max.Z)
                continue;
            return i;
        }

        return _activeCount;
    }

    private void CreateSnowParticlesSphere(float radius)
    {
        var spawn = new Vector(_emitOrigin.X + _system.Random.GlobalFloat(-radius, radius), 0, 0);
        spawn.Y = _emitOrigin.Y + _system.Random.GlobalFloat(-radius, radius);
        spawn.Z = _emitOrigin.Z + _system.Random.GlobalFloat(-radius, radius);

        int i = ContainingActive(spawn);
        if (i == _activeCount)
            return;

        CreateSnowFallParticle(spawn, _active[i]);
    }

    private void CreateSnowParticlesRay(float radius, in Vector eyePos, in Vector forward)
    {
        PrecipitationSettings settings = _system.Settings;

        // Stock's test, kept: ray particles are made when the ray FOUND a
        // volume OR r_SnowRayEnable is off.
        if (!_rayParticles && settings.SnowRayEnable)
            return;

        float rayRadius = settings.SnowRayRadius;
        Vector newForward = forward * _system.Random.GlobalFloat(radius, settings.SnowRayLength);

        var spawn = new Vector(eyePos.X + newForward.X, eyePos.Y + newForward.Y, 0);
        spawn.Z = eyePos.Z + _system.Random.GlobalFloat(72, rayRadius);
        spawn.X += _system.Random.GlobalFloat(-rayRadius, rayRadius);
        spawn.Y += _system.Random.GlobalFloat(-rayRadius, rayRadius);

        int i = ContainingActive(spawn);
        if (i == _activeCount)
            return;

        CreateSnowFallParticle(spawn, _active[i]);
    }

    private void CreateSnowFallParticle(in Vector spawn, int snow)
    {
        PrecipitationSettings settings = _system.Settings;
        var p = new SimpleParticle
        {
            Id = _system.NextParticleId(),
            Position = spawn,
            Lifetime = 0.0f,
        };

        float vx = _system.Random.GlobalFloat(-5.0f, 5.0f);
        float vy = _system.Random.GlobalFloat(-5.0f, 5.0f);
        float vz = _system.Random.GlobalFloat(-25, -35) * settings.SnowFallSpeed;
        p.Velocity = new Vector(vx, vy, vz);
        p.DieTime = (float)Math.Abs((spawn.Z - _snow[snow].Min.Z) / (p.Velocity.Z - 0.1));

        p.R = (byte)settings.SnowColorRed;
        p.G = (byte)settings.SnowColorGreen;
        p.B = (byte)settings.SnowColorBlue;
        p.StartSize = (byte)settings.SnowStartSize;
        p.EndSize = (byte)settings.SnowEndSize;
        p.StartAlpha = (byte)settings.SnowStartAlpha;
        p.EndAlpha = (byte)settings.SnowEndAlpha;
        p.Roll = _system.Random.EngineInt(0, 360);
        p.RollDelta = _system.Random.EngineFloat(-0.15f, 0.15f);
        p.Flags = SimpleEmitterRules.FlagWindBlown;

        _particles.Insert(0, p);
    }

    /// <summary>
    /// <c>SnowFallEffect::SimulateParticles</c>: each flake drifts a little,
    /// slows, takes the wind, falls, and dies at its time or in anything
    /// solid.
    /// </summary>
    internal void Simulate(float curTime, float dt, List<int> died)
    {
        Vector wind = _system.World.WindspeedAt(curTime);
        float windScale = _system.Settings.SnowWindScale;

        for (int i = 0; i < _particles.Count; i++)
        {
            SimpleParticle p = _particles[i];

            // UpdateVelocity.
            Vector v = p.Velocity;
            float speed = FxMath.Normalize(ref v);
            speed -= dt;
            v.X += _system.Random.GlobalFloat(-0.025f, 0.025f);
            v.Y += _system.Random.GlobalFloat(-0.025f, 0.025f);
            FxMath.Normalize(ref v);
            v = v * speed;
            v = v + (wind * windScale);
            p.Velocity = v;

            p.Position = p.Position + p.Velocity * dt;
            p.Lifetime += dt;
            p.Roll += p.RollDelta * dt;

            if (p.Lifetime >= p.DieTime ||
                (_system.World.PointContents(p.Position) & ContentsSolid) != 0)
            {
                died.Add(p.Id);
                _particles.RemoveAt(i--);
                continue;
            }

            _particles[i] = p;
        }
    }
}

/// <summary>
/// The client's weather: every precipitation volume, the wind they share, the
/// snowfall manager, and the frame in stock's order (each volume's think, the
/// snowfall manager's after the volume that made it, then the particle
/// steps, then the render).
/// </summary>
public sealed class PrecipitationSystem(IPrecipitationWorld world, IPrecipitationRandom random)
{
    private readonly List<PrecipitationVolume> _volumes = new();
    private readonly List<object> _thinkers = new();
    private int _nextParticleId = 1;

    public IPrecipitationWorld World { get; } = world;

    public IPrecipitationRandom Random { get; } = random;

    public PrecipitationSettings Settings { get; set; } = new();

    public PrecipitationPlayer Player { get; } = new();

    public PrecipitationWind Wind { get; } = new();

    public SnowFallManager? SnowFall { get; private set; }

    /// <summary>The snowfall volume that made the manager; its entity draws the manager's flakes.</summary>
    public PrecipitationVolume? SnowFallHolder { get; private set; }

    /// <summary>Every volume, in creation order (stock's g_Precipitations).</summary>
    public IReadOnlyList<PrecipitationVolume> Volumes => _volumes;

    internal int NextParticleId() => _nextParticleId++;

    /// <summary>A volume's first data update (<c>OnDataChanged( DATA_UPDATE_CREATED )</c>).</summary>
    public PrecipitationVolume Create(PrecipitationType type, int alpha, Vector mins, Vector maxs)
        => Create(type, alpha, mins, maxs, EntityHandle.None);

    /// <summary><see cref="Create(PrecipitationType, int, Vector, Vector)"/>, for a client entity.</summary>
    public PrecipitationVolume Create(PrecipitationType type, int alpha, Vector mins, Vector maxs, EntityHandle owner)
    {
        var volume = new PrecipitationVolume(this, type, alpha, mins, maxs) { Owner = owner };
        _volumes.Add(volume);
        _thinkers.Add(volume);

        if (type == PrecipitationType.SnowFall)
        {
            if (SnowFall is null)
            {
                SnowFall = new SnowFallManager(this);
                SnowFallHolder = volume;
                _thinkers.Add(SnowFall);
            }

            SnowFall.Add(volume);
        }

        return volume;
    }

    /// <summary>
    /// A volume goes. As in stock, the snowfall manager goes with it, whatever
    /// the volume's kind (<c>~CClient_Precipitation</c> calls
    /// <c>SnowFallManagerDestroy</c>).
    /// </summary>
    public void Remove(PrecipitationVolume volume)
    {
        _volumes.Remove(volume);
        _thinkers.Remove(volume);
        if (SnowFall is not null)
        {
            _thinkers.Remove(SnowFall);
            SnowFall = null;
            SnowFallHolder = null;
        }
    }

    /// <summary>Everything gone at once (the level ends).</summary>
    public void Clear()
    {
        _volumes.Clear();
        _thinkers.Clear();
        SnowFall = null;
        SnowFallHolder = null;
    }

    /// <summary>The thinks, in registration order.</summary>
    public void Think(float curTime, float frameTime)
    {
        foreach (object thinker in _thinkers.ToArray())
        {
            if (thinker is PrecipitationVolume v)
                v.Think(curTime, frameTime);
            else if (thinker is SnowFallManager s)
                s.Think(frameTime);
        }
    }

    /// <summary>The particle manager's steps of the simple emitters: the snowfall's, then each ash volume's.</summary>
    public void SimulateParticles(float curTime, float dt, List<int> died)
    {
        SnowFall?.Simulate(curTime, dt, died);
        foreach (PrecipitationVolume v in _volumes)
            v.SimulateAsh(curTime, dt);
    }

    /// <summary><c>DrawPrecipitation</c>: each volume's render plan.</summary>
    public void Render(PrecipitationView view, float curTime, List<PrecipitationTracer> tracers, List<PrecipitationSplash> splashes)
    {
        foreach (PrecipitationVolume v in _volumes)
            v.Render(view, curTime, tracers, splashes);
    }
}

/// <summary>The two box tests the snowfall makes (public/collisionutils.cpp).</summary>
internal static class CollisionMath
{
    /// <summary><c>IsBoxIntersectingSphere</c>.</summary>
    public static bool IsBoxIntersectingSphere(in Vector boxMin, in Vector boxMax, in Vector center, float radius)
    {
        float dmin = 0.0f;
        float delta;

        if (center.X < boxMin.X)
        {
            delta = center.X - boxMin.X;
            dmin += delta * delta;
        }
        else if (center.X > boxMax.X)
        {
            delta = boxMax.X - center.X;
            dmin += delta * delta;
        }

        if (center.Y < boxMin.Y)
        {
            delta = center.Y - boxMin.Y;
            dmin += delta * delta;
        }
        else if (center.Y > boxMax.Y)
        {
            delta = boxMax.Y - center.Y;
            dmin += delta * delta;
        }

        if (center.Z < boxMin.Z)
        {
            delta = center.Z - boxMin.Z;
            dmin += delta * delta;
        }
        else if (center.Z > boxMax.Z)
        {
            delta = boxMax.Z - center.Z;
            dmin += delta * delta;
        }

        return dmin < radius * radius;
    }

    /// <summary>
    /// <c>IntersectRayWithBox( start, delta, mins, maxs, tolerance, CBaseTrace * )</c>,
    /// as much of it as the snowfall reads: the trace's fraction. 1 is a miss;
    /// a start inside the box is 0.
    /// </summary>
    public static float IntersectRayWithBoxFraction(in Vector start, in Vector delta, in Vector boxMins, in Vector boxMaxs, float tolerance)
    {
        float t1 = -1.0f, t2 = 1.0f;
        bool startSolid = true;

        for (int i = 0; i < 6; ++i)
        {
            float d1, d2;
            if (i >= 3)
            {
                d1 = Axis(start, i - 3) - Axis(boxMaxs, i - 3);
                d2 = d1 + Axis(delta, i - 3);
            }
            else
            {
                d1 = -Axis(start, i) + Axis(boxMins, i);
                d2 = d1 - Axis(delta, i);
            }

            if (d1 > 0 && d2 > 0)
                return 1.0f;

            if (d1 <= 0 && d2 <= 0)
                continue;

            if (d1 > 0)
                startSolid = false;

            if (d1 > d2)
            {
                float f = d1 - tolerance;
                if (f < 0)
                    f = 0;
                f = f / (d1 - d2);
                if (f > t1)
                    t1 = f;
            }
            else
            {
                float f = (d1 + tolerance) / (d1 - d2);
                if (f < t2)
                    t2 = f;
            }
        }

        if (!(startSolid || (t1 < t2 && t1 >= 0.0f)))
            return 1.0f;
        if (t1 < t2 && t1 >= 0.0f)
            return t1;
        return 0.0f;   // start solid
    }

    private static float Axis(in Vector v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;
}
