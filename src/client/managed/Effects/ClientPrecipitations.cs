using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Effects;

/// <summary>The weather's console variables, read into a <see cref="PrecipitationSettings"/> each frame.</summary>
public sealed class PrecipitationConVars(
    ClWinddirConVar windDir, ClWindspeedConVar windSpeed, RRainSplashPercentageConVar splash, RRainHackConVar hack,
    RRainRadiusConVar radius, RRainSideVelConVar sideVel, RRainSimulateConVar simulate, RDrawRainConVar draw,
    RRainwidthConVar width, RRainlengthConVar length, RRainspeedConVar speed, RRainalphaConVar alpha,
    RRainalphapowConVar alphaPow, RSnowEnableConVar snowEnable, RSnowParticlesConVar snowParticles,
    RSnowInsideRadiusConVar insideRadius, RSnowOutsideRadiusConVar outsideRadius, RSnowSpeedScaleConVar speedScale,
    RSnowPosScaleConVar posScale, RSnowFallSpeedConVar fallSpeed, RSnowWindScaleConVar windScale,
    RSnowZoomOffsetConVar zoomOffset, RSnowZoomRadiusConVar zoomRadius, RSnowStartAlphaConVar startAlpha,
    RSnowEndAlphaConVar endAlpha, RSnowColorRedConVar red, RSnowColorGreenConVar green, RSnowColorBlueConVar blue,
    RSnowStartSizeConVar startSize, RSnowEndSizeConVar endSize, RSnowRayLengthConVar rayLength,
    RSnowRayRadiusConVar rayRadius, RSnowRayEnableConVar rayEnable)
{
    public void Fill(PrecipitationSettings s)
    {
        s.WindDir = windDir.Value;
        s.WindSpeed = windSpeed.Value;
        s.RainSplashPercentage = splash.Value;
        s.RainHack = hack.Value;
        s.RainRadius = radius.Value;
        s.RainSideVel = sideVel.Value;
        s.RainSimulate = simulate.Value;
        s.DrawRain = draw.Value;
        s.RainWidth = width.Value;
        s.RainLength = length.Value;
        s.RainSpeed = speed.Value;
        s.RainAlpha = alpha.Value;
        s.RainAlphaPow = alphaPow.Value;
        s.SnowEnable = snowEnable.Value;
        s.SnowParticles = snowParticles.Value;
        s.SnowInsideRadius = insideRadius.Value;
        s.SnowOutsideRadius = outsideRadius.Value;
        s.SnowSpeedScale = speedScale.Value;
        s.SnowPosScale = posScale.Value;
        s.SnowFallSpeed = fallSpeed.Value;
        s.SnowWindScale = windScale.Value;
        s.SnowZoomOffset = zoomOffset.Value;
        s.SnowZoomRadius = zoomRadius.Value;
        s.SnowStartAlpha = startAlpha.Value;
        s.SnowEndAlpha = endAlpha.Value;
        s.SnowColorRed = red.Value;
        s.SnowColorGreen = green.Value;
        s.SnowColorBlue = blue.Value;
        s.SnowStartSize = startSize.Value;
        s.SnowEndSize = endSize.Value;
        s.SnowRayLength = rayLength.Value;
        s.SnowRayRadius = rayRadius.Value;
        s.SnowRayEnable = rayEnable.Value;
    }
}

/// <summary>
/// The client's weather in the game: the <see cref="PrecipitationSystem"/>,
/// its world over IShimPrecipitationClient and the client winds, its draws
/// from two per-system seeded streams (the integrator's ruling: <c>rand()</c>
/// in MSVC's range, never the process-global C state), its frame, and the
/// render plan DrawPrecipitation asks for.
/// </summary>
public sealed class ClientPrecipitations : IPrecipitationWorld, IPrecipitationRandom, IClientPrecipitationRenderer
{
    private const int ViewMonitor = 2;
    private const int DrawFlagsReflectRefract = 0x1 | 0x2;

    private readonly IClientPrecipitationEngine _engine;
    private readonly IClientWinds _winds;
    private readonly IGameClock _clock;
    private readonly PrecipitationConVars _convars;
    private readonly IEngineHud _hud;
    private readonly IClientStandalones _standalones;
    private readonly UniformStream _stream;
    private readonly UniformStream _cRand;
    private readonly PrecipitationView _view = new();
    private readonly List<PrecipitationTracer> _tracers = new();
    private readonly List<PrecipitationSplash> _splashes = new();
    private readonly List<int> _tracerVolume = new();
    private PrecipitationVolume? _hack;

    public ClientPrecipitations(IClientPrecipitationEngine engine, IClientWinds winds, IGameClock clock,
                                PrecipitationConVars convars, IEngineHud hud, IClientStandalones standalones,
                                IRandomSeeds seeds)
    {
        _engine = engine;
        _winds = winds;
        _clock = clock;
        _convars = convars;
        _hud = hud;
        _standalones = standalones;
        _stream = new UniformStream(seeds.Next());
        _cRand = new UniformStream(seeds.Next());
        System = new PrecipitationSystem(this, this);
    }

    public PrecipitationSystem System { get; }

    /// <summary>How many render plans have been asked for.</summary>
    public int Renders { get; private set; }

    /// <summary>Tracers in the last plan.</summary>
    public int LastTracers { get; private set; }

    /// <summary>
    /// The frame: the settings, the player, <c>r_RainHack</c>'s volume
    /// (stock's CPrecipHack), then every think.
    /// </summary>
    public void Frame(float frameTime)
    {
        _convars.Fill(System.Settings);
        ReadPlayer();

        if (System.Settings.RainHack != 0 && _hack is null)
            _hack = System.Create(PrecipitationType.Rain, 255, default, default);
        else if (System.Settings.RainHack == 0 && _hack is not null)
        {
            System.Remove(_hack);
            _hack = null;
        }

        if (System.Volumes.Count > 0)
            System.Think(_clock.Now, frameTime);
    }

    /// <summary>The level goes: every volume with it.</summary>
    public void LevelShutdown()
    {
        System.Clear();
        _hack = null;
    }

    private void ReadPlayer()
    {
        PrecipitationPlayer p = System.Player;
        PrecipitationPlayerRead? read = _engine.Player();
        p.Exists = read is not null;
        if (read is not { } r)
            return;

        p.Origin = r.Origin;
        p.Eye = r.Eye;
        p.Center = r.Center;
        p.Fov = r.Fov;
        p.DefaultFov = r.DefaultFov;
        p.Forward = MoveVec.AngleForward(new Vector(0, r.Yaw, 0));
        p.Velocity = _standalones.GetByIndex(_hud.LocalPlayerIndex) is Standalone.ClientSsPlayer local
            ? local.State.Velocity
            : default;
    }

    public int Render(ReadOnlySpan<float> view, Span<float> tracers, Span<float> splashes, out int splashCount)
    {
        Renders++;
        splashCount = 0;
        LastTracers = 0;

        // Not in monitors, reflections or refractions (stock's Render).
        if ((int)view[22] == ViewMonitor || ((int)view[23] & DrawFlagsReflectRefract) != 0)
            return 0;

        view[..16].CopyTo(_view.WorldToView);
        _view.Forward = new Vector(view[16], view[17], view[18]);
        _view.Origin = new Vector(view[19], view[20], view[21]);

        _tracers.Clear();
        _splashes.Clear();
        _tracerVolume.Clear();
        int ordinal = 0;
        foreach (PrecipitationVolume v in System.Volumes)
        {
            int before = _tracers.Count;
            v.Render(_view, _clock.Now, _tracers, _splashes);
            for (int i = before; i < _tracers.Count; i++)
                _tracerVolume.Add(ordinal);
            ordinal++;
        }

        int t = 0;
        for (; t < _tracers.Count && (t + 1) * 10 <= tracers.Length; t++)
        {
            PrecipitationTracer tr = _tracers[t];
            Span<float> o = tracers.Slice(t * 10, 10);
            o[0] = tr.Start.X; o[1] = tr.Start.Y; o[2] = tr.Start.Z;
            o[3] = tr.Delta.X; o[4] = tr.Delta.Y; o[5] = tr.Delta.Z;
            o[6] = tr.Width; o[7] = tr.Alpha;
            o[8] = System.Volumes[_tracerVolume[t]].Type == PrecipitationType.Snow ? 1 : 0;
            o[9] = _tracerVolume[t];
        }

        int s = 0;
        for (; s < _splashes.Count && (s + 1) * 6 <= splashes.Length; s++)
        {
            PrecipitationSplash sp = _splashes[s];
            Span<float> o = splashes.Slice(s * 6, 6);
            o[0] = sp.Origin.X; o[1] = sp.Origin.Y; o[2] = sp.Origin.Z;
            o[3] = sp.Scale; o[4] = sp.Lifetime; o[5] = sp.Alpha;
        }

        splashCount = s;
        LastTracers = t;
        return t;
    }

    // IPrecipitationWorld, over the engine and the client winds.
    public int PointContents(in Vector position) => _engine.PointContents(position);

    public PrecipitationTrace TraceLine(in Vector start, in Vector end, uint mask, bool ignorePlayer)
    {
        _engine.TraceLine(start, end, mask, ignorePlayer, out float fraction, out Vector endPos, out ushort flags);
        return new PrecipitationTrace { Fraction = fraction, EndPos = endPos, SurfaceFlags = flags };
    }

    public PrecipitationTrace TraceVolume(in Vector start, in Vector end, in Vector hullMin, in Vector hullMax, PrecipitationVolume volume)
    {
        if (volume.Owner == EntityHandle.None)
            return BoxTrace(start, end, hullMin, hullMax, volume);

        _engine.TraceVolume(volume.Owner, start, end, hullMin, hullMax, out bool hit, out float fraction, out bool startSolid, out Vector endPos);
        return new PrecipitationTrace { HitVolume = hit, Fraction = fraction, StartSolid = startSolid, EndPos = endPos };
    }

    public bool InPvs(PrecipitationVolume volume) => volume.Owner == EntityHandle.None || _engine.InPvs(volume.Owner);

    // A volume no entity stands behind (r_RainHack's, a test volume): the
    // trace is against its own box, grown by the hull.
    private static PrecipitationTrace BoxTrace(in Vector start, in Vector end, in Vector hullMin, in Vector hullMax, PrecipitationVolume volume)
    {
        Vector lo = volume.Mins - hullMax, hi = volume.Maxs - hullMin;
        bool inside = start.X >= lo.X && start.X <= hi.X && start.Y >= lo.Y && start.Y <= hi.Y && start.Z >= lo.Z && start.Z <= hi.Z;
        float fraction = CollisionMath.IntersectRayWithBoxFraction(start, end - start, lo, hi, 0.0f);
        bool hit = inside || fraction < 1.0f;
        return new PrecipitationTrace
        {
            HitVolume = hit,
            StartSolid = inside,
            Fraction = hit ? fraction : 1.0f,
            EndPos = start + (end - start) * (hit ? fraction : 1.0f),
        };
    }

    private readonly List<PrecipitationVolume> _tests = new();

    /// <summary>
    /// <c>cs_cl_weather_test</c>'s volume: one of <paramref name="type"/> around
    /// the local player, a box <paramref name="halfSize"/> out from them and
    /// <paramref name="height"/> up. A stimulus for maps without a
    /// func_precipitation; nothing else makes one.
    /// </summary>
    public PrecipitationVolume? AddTestVolume(PrecipitationType type, int alpha, float halfSize, float height)
    {
        ReadPlayer();
        if (!System.Player.Exists)
            return null;
        Vector o = System.Player.Origin;
        var v = System.Create(type, alpha, new Vector(o.X - halfSize, o.Y - halfSize, o.Z - 16),
                              new Vector(o.X + halfSize, o.Y + halfSize, o.Z + height));
        _tests.Add(v);
        return v;
    }

    /// <summary>Every test volume goes.</summary>
    public void ClearTestVolumes()
    {
        foreach (PrecipitationVolume v in _tests)
            System.Remove(v);
        _tests.Clear();
    }

    public Vector WindspeedAt(float time) => _winds.Windspeed;

    public int ScreenWidth => _engine.ScreenWidth();

    public Vector WorldMins => _engine.WorldBounds().Mins;

    public Vector WorldMaxs => _engine.WorldBounds().Maxs;

    // IPrecipitationRandom: the per-system streams.
    public float EngineFloat(float min, float max) => _stream.RandomFloat(min, max);

    public int EngineInt(int min, int max) => _stream.RandomInt(min, max);

    public float GlobalFloat(float min, float max) => _stream.RandomFloat(min, max);

    public int GlobalInt(int min, int max) => _stream.RandomInt(min, max);

    public int CRand() => _cRand.RandomInt(0, ParticleRandomExtensions.ValveRandMax);
}

/// <summary>
/// <c>cs_cl_weather_report</c>: each precipitation volume (kind, density,
/// particles), the snowfall flakes, and the render plans asked for. Prints
/// and changes nothing.
/// </summary>
[ConCommand("cs_cl_weather_report", Help = "SourceSharp: the client weather's volumes, particles and render plans")]
public sealed class WeatherReportCommand(ILog log, ClientPrecipitations weather) : IConCommand
{
    public void Execute(string args)
    {
        PrecipitationSystem s = weather.System;
        log.Msg($"[SourceSharp/Weather] {s.Volumes.Count} volume(s), renders {weather.Renders}, last tracers {weather.LastTracers}, " +
                $"snowfall {(s.SnowFall is null ? "none" : s.SnowFall.Particles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))}, " +
                $"player {(s.Player.Exists ? "yes" : "no")}, wind {s.Wind.Vector.X:0.##} {s.Wind.Vector.Y:0.##}\n");
        foreach (PrecipitationVolume v in s.Volumes)
        {
            log.Msg($"[SourceSharp/Weather]   {v.Type} density {v.Density:0.######} particles {v.Particles.Count} " +
                    $"ash {v.AshEmitter?.Particles.Count ?? 0} mins {v.Mins.X} {v.Mins.Y} {v.Mins.Z} maxs {v.Maxs.X} {v.Maxs.Y} {v.Maxs.Z}\n");
        }
    }
}

/// <summary>
/// <c>cs_cl_weather_test &lt;type&gt; [alpha] [halfsize] [height]</c>: a weather
/// volume of kind 0 rain, 1 snow, 2 ash or 3 snowfall around the local player,
/// for a live check on a map with no func_precipitation;
/// <c>cs_cl_weather_test clear</c> removes them. The volume is drawn and
/// simulated as any other; its traces against itself are a box test.
/// </summary>
[ConCommand("cs_cl_weather_test", Help = "SourceSharp: a test weather volume around you: <type 0-3> [alpha] [halfsize] [height], or clear",
            Flags = ConVarFlags.Cheat)]
public sealed class WeatherTestCommand(ILog log, ClientPrecipitations weather) : IConCommand
{
    public void Execute(string args)
    {
        string[] a = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0 || a[0] == "clear")
        {
            weather.ClearTestVolumes();
            log.Msg("[SourceSharp/Weather] test volumes cleared\n");
            return;
        }

        static float Arg(string[] a, int i, float fallback)
            => a.Length > i && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;

        var type = (PrecipitationType)(int)Arg(a, 0, 0);
        PrecipitationVolume? v = weather.AddTestVolume(type, (int)Arg(a, 1, 160), Arg(a, 2, 1024), Arg(a, 3, 600));
        log.Msg(v is null
            ? "[SourceSharp/Weather] no local player; no test volume\n"
            : $"[SourceSharp/Weather] test {type} volume {v.Mins.X} {v.Mins.Y} {v.Mins.Z} .. {v.Maxs.X} {v.Maxs.Y} {v.Maxs.Z}\n");
    }
}
