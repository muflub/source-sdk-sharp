using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ropes;

namespace SourceSharp.Client.Beams;

/// <summary>What the beam system has done since the level began (cs_cl_beams, the live gate).</summary>
public readonly record struct BeamCounters(
    int Active, long Created, long Refused, long Updates, long Draws, long Strips, long Meshes);

/// <summary>
/// The client's beams (plan_client_novalve.md §2 cluster 5): the temp entity
/// players' creators, env_beam's per-frame beam and env_quadraticbeam's draw.
/// </summary>
public interface IClientBeams
{
    void CreateBeamPoints(in Vector start, in Vector end, int modelIndex, int haloIndex, float haloScale, float life,
                          float width, float endWidth, float fadeLength, float amplitude, float brightness,
                          float speed, int startFrame, float frameRate, float r, float g, float b);

    void CreateBeamEnts(int startEnt, int endEnt, int modelIndex, int haloIndex, float haloScale, float life,
                        float width, float endWidth, float fadeLength, float amplitude, float brightness,
                        float speed, int startFrame, float frameRate, float r, float g, float b, int type);

    void CreateBeamEntPoint(int startEnt, Vector? start, int endEnt, Vector? end, int modelIndex, int haloIndex,
                            float haloScale, float life, float width, float endWidth, float fadeLength,
                            float amplitude, float brightness, float speed, int startFrame, float frameRate,
                            float r, float g, float b);

    void CreateBeamRing(int startEnt, int endEnt, int modelIndex, int haloIndex, float haloScale, float life,
                        float width, float endWidth, float fadeLength, float amplitude, float brightness,
                        float speed, int startFrame, float frameRate, float r, float g, float b, int flags);

    void CreateBeamRingPoint(in Vector center, float startRadius, float endRadius, int modelIndex, int haloIndex,
                             float haloScale, float life, float width, float endWidth, float fadeLength,
                             float amplitude, float brightness, float speed, int startFrame, float frameRate,
                             float r, float g, float b, int flags);

    void CreateBeamFollow(int startEnt, int modelIndex, int haloIndex, float haloScale, float life, float width,
                          float endWidth, float fadeLength, float r, float g, float b, float brightness);

    /// <summary><c>CreateBeamPoints( BeamInfo_t &amp; )</c>.</summary>
    void CreateBeamPoints(BeamInfo info);

    /// <summary>
    /// <c>DrawBeamQuadratic</c> over a sprite bound for a render mode: drawn
    /// now, so only from inside the owner's draw.
    /// </summary>
    void DrawQuadratic(in Vector start, in Vector control, in Vector end, float width, in Vector color,
                       float scrollOffset, int spriteIndex, int renderMode);

    BeamCounters Counters { get; }
}

/// <summary>
/// <see cref="IClientBeams"/> over <see cref="BeamSystem"/> and
/// <see cref="BeamDraw"/>, drawing through <see cref="IClientBeamExecutor"/>;
/// what native's <c>beams</c> forwards (<see cref="IClientBeamCallbacks"/>).
/// Carries every live beam across a client reload.
/// </summary>
public sealed class ClientBeamSystem : IClientBeams, IClientBeamCallbacks, IReloadParticipant
{
    // view_beams.cpp's trail pool: -particles, at least MIN_PARTICLES, else DEFAULT_PARTICLES.
    private const int MinTrails = 512;

    // BeamCreateKind_t (shell_beam_render.cpp).
    private const int KindEnts = 0, KindEntPoint = 1, KindPoints = 2, KindRing = 3, KindRingPoint = 4,
                      KindCirclePoints = 5, KindFollow = 6;

    // BeamLifecycle_t.
    private const int LifecycleInit = 0, LifecycleShutdown = 1, LifecycleClear = 2;

    private readonly IClientBeamExecutor _executor;
    private readonly IPixelVisibility _pixelVis;
    private readonly IGameClock _clock;
    private readonly IEngineConsole _console;
    private readonly ILog _log;
    private readonly World _world;
    private readonly Sink _sink;
    private readonly BeamDraw _draw;
    private readonly float[] _viewFloats = new float[42];
    private readonly float[] _matrix = new float[16];
    private readonly Dictionary<int, Beam> _byRenderable = [];
    private BeamSystem _system;
    private long _created, _refused, _updates, _draws;

    public ClientBeamSystem(IClientBeamExecutor executor, IGameClock clock, IEngineConsole console, ILog log, IPixelVisibility pixelVis)
    {
        _executor = executor;
        _pixelVis = pixelVis;
        _clock = clock;
        _console = console;
        _log = log;
        _world = new World(this);
        _sink = new Sink(executor);
        _draw = new BeamDraw(_sink, _world, default);
        _system = new BeamSystem(_world, _world);
    }

    public BeamCounters Counters
        => new(_system.Active.Count, _created, _refused, _updates, _draws, _sink.Strips, _sink.Meshes);

    /// <summary>The system itself, for the report.</summary>
    public BeamSystem Bookkeeping => _system;

    //-------------------------------------------------------------------------
    // Creation.
    //-------------------------------------------------------------------------

    private void Count(Beam? beam)
    {
        if (beam is null)
            _refused++;
        else
            _created++;
    }

    public void CreateBeamPoints(in Vector start, in Vector end, int modelIndex, int haloIndex, float haloScale,
                                 float life, float width, float endWidth, float fadeLength, float amplitude,
                                 float brightness, float speed, int startFrame, float frameRate, float r, float g,
                                 float b)
        => Count(_system.CreateBeamPoints(start, end, modelIndex, haloIndex, haloScale, life, width, endWidth,
                                          fadeLength, amplitude, brightness, speed, startFrame, frameRate, r, g, b,
                                          _clock.Now));

    public void CreateBeamEnts(int startEnt, int endEnt, int modelIndex, int haloIndex, float haloScale, float life,
                               float width, float endWidth, float fadeLength, float amplitude, float brightness,
                               float speed, int startFrame, float frameRate, float r, float g, float b, int type)
        => Count(_system.CreateBeamEnts(startEnt, endEnt, modelIndex, haloIndex, haloScale, life, width, endWidth,
                                        fadeLength, amplitude, brightness, speed, startFrame, frameRate, r, g, b,
                                        type, _clock.Now));

    public void CreateBeamEntPoint(int startEnt, Vector? start, int endEnt, Vector? end, int modelIndex,
                                   int haloIndex, float haloScale, float life, float width, float endWidth,
                                   float fadeLength, float amplitude, float brightness, float speed, int startFrame,
                                   float frameRate, float r, float g, float b)
        => Count(_system.CreateBeamEntPoint(startEnt, start, endEnt, end, modelIndex, haloIndex, haloScale, life,
                                            width, endWidth, fadeLength, amplitude, brightness, speed, startFrame,
                                            frameRate, r, g, b, _clock.Now));

    public void CreateBeamRing(int startEnt, int endEnt, int modelIndex, int haloIndex, float haloScale, float life,
                               float width, float endWidth, float fadeLength, float amplitude, float brightness,
                               float speed, int startFrame, float frameRate, float r, float g, float b, int flags)
        => Count(_system.CreateBeamRing(startEnt, endEnt, modelIndex, haloIndex, haloScale, life, width, endWidth,
                                        fadeLength, amplitude, brightness, speed, startFrame, frameRate, r, g, b,
                                        flags, _clock.Now));

    public void CreateBeamRingPoint(in Vector center, float startRadius, float endRadius, int modelIndex,
                                    int haloIndex, float haloScale, float life, float width, float endWidth,
                                    float fadeLength, float amplitude, float brightness, float speed,
                                    int startFrame, float frameRate, float r, float g, float b, int flags)
        => Count(_system.CreateBeamRingPoint(center, startRadius, endRadius, modelIndex, haloIndex, haloScale, life,
                                             width, endWidth, fadeLength, amplitude, brightness, speed, startFrame,
                                             frameRate, r, g, b, flags, _clock.Now));

    public void CreateBeamFollow(int startEnt, int modelIndex, int haloIndex, float haloScale, float life,
                                 float width, float endWidth, float fadeLength, float r, float g, float b,
                                 float brightness)
        => Count(_system.CreateBeamFollow(startEnt, modelIndex, haloIndex, haloScale, life, width, endWidth,
                                          fadeLength, r, g, b, brightness, _clock.Now));

    public void CreateBeamPoints(BeamInfo info) => Count(_system.CreateBeamPoints(info, _clock.Now));

    //-------------------------------------------------------------------------
    // Native's `beams`.
    //-------------------------------------------------------------------------

    public void Lifecycle(int op, int arg)
    {
        switch (op)
        {
            case LifecycleInit:
                Clear();
                _system = new BeamSystem(_world, _world,
                                         arg >= 0 ? Math.Max(arg, MinTrails) : BeamSystem.DefaultTrails);
                break;

            case LifecycleShutdown:
            case LifecycleClear:
                Clear();
                break;
        }
    }

    private void Clear()
    {
        _system.Clear();
        _byRenderable.Clear();
    }

    public void Update(float curTime, float frameTime)
    {
        _updates++;
        _drawBeams = DrawBeams();
        _sink.Wireframe = _wireframe = Wireframe();
        _system.UpdateTempEntBeams(curTime, frameTime);
    }

    // r_DrawBeams: 0 off, 1 normal, 2 wireframe. THE ENGINE'S: engine.so defines
    // it, and view_beams.cpp's ConVar of the same name was a child of it, so
    // stock read the engine's too. Read by name once a frame; stock's default
    // when it does not answer.
    private int _drawBeams = 1;

    private int DrawBeams() => _console.FindConVarValue("r_DrawBeams") is { } v ? Int(v) : 1;

    // Draw_SetSpriteTexture's wireframe arm: ShouldDrawInWireFrameMode() (sv_cheats
    // and mat_wireframe) or r_DrawBeams 2. Read once a frame, not per sprite.
    private bool _wireframe;

    private bool Wireframe()
    {
        if (_drawBeams == 2)
            return true;
        return Int(_console.FindConVarValue("sv_cheats")) != 0 && Int(_console.FindConVarValue("mat_wireframe")) != 0;
    }

    private static int Int(string? value)
    {
        if (value is null)
            return 0;
        if (int.TryParse(value, out int i))
            return i;
        return float.TryParse(value, System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out float f) ? (int)f : 0;
    }

    public void KillDead(EntityHandle entity, float curTime) => _system.KillDeadBeams(entity, curTime);

    public bool Create(int kind, ReadOnlySpan<int> ints, ReadOnlySpan<float> floats, string? model, string? halo,
                       float curTime)
    {
        var info = new BeamInfo
        {
            Type = (BeamType)ints[0],
            StartEnt = new EntityHandle((uint)ints[1]),
            StartAttachment = ints[2],
            EndEnt = new EntityHandle((uint)ints[3]),
            EndAttachment = ints[4],
            ModelIndex = ints[5],
            HaloIndex = ints[6],
            StartFrame = ints[7],
            Renderable = ints[8] != 0,
            Segments = ints[9],
            Flags = (BeamFlags)ints[10],
            Start = new Vector(floats[0], floats[1], floats[2]),
            End = new Vector(floats[3], floats[4], floats[5]),
            Center = new Vector(floats[6], floats[7], floats[8]),
            HaloScale = floats[9],
            Life = floats[10],
            Width = floats[11],
            EndWidth = floats[12],
            FadeLength = floats[13],
            Amplitude = floats[14],
            Brightness = floats[15],
            Speed = floats[16],
            FrameRate = floats[17],
            Red = floats[18],
            Green = floats[19],
            Blue = floats[20],
            StartRadius = floats[21],
            EndRadius = floats[22],
            ModelName = model,
            HaloName = halo,
        };

        Beam? beam = kind switch
        {
            KindEnts => _system.CreateBeamEnts(info, curTime),
            KindEntPoint => _system.CreateBeamEntPoint(info, curTime),
            KindPoints => _system.CreateBeamPoints(info, curTime),
            KindRing => _system.CreateBeamRing(info, curTime),
            KindRingPoint => _system.CreateBeamRingPoint(info, curTime),
            KindCirclePoints => _system.CreateBeamCirclePoints(info, curTime),
            KindFollow => _system.CreateBeamFollow(info, curTime),
            _ => null,
        };

        Count(beam);
        return beam is not null;
    }

    public void Draw(int renderable)
    {
        if (!_byRenderable.TryGetValue(renderable, out Beam? beam))
            return;

        _draws++;
        _draw.View = View(insideView: true);
        _system.DrawBeam(beam, _draw, _world);
    }

    public void DrawQuadratic(in Vector start, in Vector control, in Vector end, float width, in Vector color,
                              float scrollOffset, int spriteIndex, int renderMode)
    {
        if (spriteIndex <= 0)
            return;
        if (!_executor.BindSprite(spriteIndex, 0, renderMode, _wireframe))
            return;

        _draws++;
        _draw.View = View(insideView: true);
        _draw.DrawBeamQuadratic(start, control, end, width, color, scrollOffset);
    }

    private BeamView View(bool insideView)
    {
        float[] f = _viewFloats;
        _executor.View(insideView, f);
        Array.Copy(f, 21, _matrix, 0, 16);
        _sink.Camera = new Vector(f[39], f[40], f[41]);

        return new BeamView(
            new Vector(f[0], f[1], f[2]), new Vector(f[3], f[4], f[5]), new Vector(f[6], f[7], f[8]),
            new Vector(f[9], f[10], f[11]), new Vector(f[12], f[13], f[14]),
            new Vector(f[15], f[16], f[17]), new Vector(f[18], f[19], f[20]),
            new BeamMatrix(_matrix), f[37], f[38], _drawBeams);
    }

    //-------------------------------------------------------------------------
    // Entity endpoints (BeamEndpoint's plan over native's facts).
    //-------------------------------------------------------------------------

    private readonly float[] _facts = new float[9];
    private readonly HashSet<int> _saidHitboxes = [];
    private readonly HashSet<int> _saidInertCentre = [];

    /// <summary>
    /// An endpoint's position, as stock's ComputeBeamEntPosition answered it: a
    /// false answer leaves <paramref name="position"/> as it was.
    /// </summary>
    public bool EntityPosition(EntityHandle entity, int attachment, bool hitboxes, ref Vector position)
    {
        var facts = (BeamEntityFacts)_executor.EntityFacts(entity, attachment, tryAttachment: !hitboxes, _facts);
        BeamEndpointPlan plan = BeamEndpoint.PlanFor(
            (facts & BeamEntityFacts.Resolved) != 0, (facts & BeamEntityFacts.Sdk) != 0,
            (facts & BeamEntityFacts.Renderable) != 0, hitboxes, (facts & BeamEntityFacts.Player) != 0);

        if (plan.Route != BeamEndpointRoute.ManagedRenderable)
            return false;

        float[] f = _facts;
        if (plan.TryAttachment && (facts & BeamEntityFacts.Attachment) != 0)
        {
            position = new Vector(f[0], f[1], f[2]);
            return true;
        }

        if (plan.HitboxesRefused && _saidHitboxes.Add(entity.EntryIndex))
        {
            _log.Warning($"[SourceSharp] beam endpoint: entity {entity.EntryIndex} is a managed entity and the " +
                         "beam asked for a HITBOX index (FBEAM_USE_HITBOXES). The managed hierarchy has no " +
                         "hitbox set to close on, so the beam falls back to the attachment-less arm exactly as " +
                         "stock does for an endpoint with no C_BaseAnimating. Behaviour ledger row C8.\n");
        }

        var origin = new Vector(f[6], f[7], f[8]);

        if (plan.Fallback == BeamEndpointFallback.WorldSpaceCenter && (facts & BeamEntityFacts.Collideable) != 0)
        {
            var centre = new Vector(f[3], f[4], f[5]);
            if (centre.X == origin.X && centre.Y == origin.Y && centre.Z == origin.Z
                && _saidInertCentre.Add(entity.EntryIndex))
            {
                _log.Warning($"[SourceSharp] beam endpoint: entity {entity.EntryIndex} is a managed PLAYER, and " +
                             "stock's \"player origins are at their feet\" correction moved the point by nothing: " +
                             "its collision centre IS its origin. The beam attaches at the feet where stock " +
                             "attached at the chest.\n");
            }
            position = centre;
            return true;
        }

        position = origin;
        return true;
    }

    //-------------------------------------------------------------------------
    // Reload.
    //-------------------------------------------------------------------------

    public void Save(ReloadWriter writer)
    {
        writer.Write((int)_created);
        writer.Write((int)_refused);
        writer.Write((int)_updates);
        writer.Write((int)_draws);
        writer.Write((int)_sink.Strips);
        writer.Write((int)_sink.Meshes);
        _system.Save(writer);
    }

    public void Restore(ReloadReader reader)
    {
        _created = reader.ReadInt();
        _refused = reader.ReadInt();
        _updates = reader.ReadInt();
        _draws = reader.ReadInt();
        _sink.Strips = reader.ReadInt();
        _sink.Meshes = reader.ReadInt();
        _system = BeamSystem.Restore(reader, _world, _world);

        _byRenderable.Clear();
        foreach (Beam beam in _system.Active)
        {
            if (beam.Renderable >= 0)
                _byRenderable[beam.Renderable] = beam;
        }
    }

    //-------------------------------------------------------------------------
    // The engine, as the beam code asks it.
    //-------------------------------------------------------------------------

    private sealed class World(ClientBeamSystem owner) : IBeamWorld, IBeamDrawWorld, IBeamRenderables
    {
        private IClientBeamExecutor X => owner._executor;

        public bool ModelExists(int modelIndex) => X.ModelInfo(modelIndex, out _);

        public int ModelFrameCount(int modelIndex) => X.ModelInfo(modelIndex, out int n) ? n : 0;

        public int ModelIndexForName(string name) => X.ModelIndex(name);

        public EntityHandle EntityAt(int entry) => X.EntityAt(entry);

        public bool EntityHasModel(EntityHandle entity) => X.EntityHasModel(entity);

        public bool EntityPosition(EntityHandle entity, int attachment, bool useHitboxes, ref Vector position)
            => owner.EntityPosition(entity, attachment, useHitboxes, ref position);

        public bool IsBoxVisible(in Vector mins, in Vector maxs)
        {
            X.BoxVisibility(mins, maxs, out bool visible, out _);
            return visible;
        }

        public bool CullBox(in Vector mins, in Vector maxs)
        {
            X.BoxVisibility(mins, maxs, out _, out bool culled);
            return culled;
        }

        // Stock's beam never set its query handle: the handle-less question, a sight line.
        public float HaloVisibility(Beam beam, in Vector origin, float proxySize) => owner._pixelVis.FractionVisibleNoHandle(origin);

        public Vector ScreenPosition(in Vector point) => X.ScreenPosition(point);

        public int Add(Beam beam)
        {
            int index = X.RenderableAdd(beam.RenderOrigin, beam.Mins, beam.Maxs);
            if (index >= 0)
                owner._byRenderable[index] = beam;
            return index;
        }

        public void Changed(Beam beam) => X.RenderableChanged(beam.Renderable, beam.RenderOrigin, beam.Mins, beam.Maxs);

        public void Remove(int handle)
        {
            owner._byRenderable.Remove(handle);
            X.RenderableRemove(handle);
        }
    }

    private sealed class Sink(IClientBeamExecutor executor) : IBeamDrawSink
    {
        public long Strips;
        public long Meshes;

        // Set by the system once a frame (the wireframe arm).
        public bool Wireframe;

        public bool BindSprite(int modelIndex, int frame, int renderMode)
            => executor.BindSprite(modelIndex, frame, renderMode, Wireframe);

        public void SetHdrColorScale(int modelIndex, int renderMode, float scale)
            => executor.SetHdrColorScale(modelIndex, renderMode, scale);

        public bool HasGlow(int modelIndex) => executor.Glow(modelIndex, bind: false);

        public void BindGlow(int modelIndex) => executor.Glow(modelIndex, bind: true);

        // The render context's camera for the view being drawn (View sets it).
        public Vector Camera;

        private BeamSegVertex[] _verts = new BeamSegVertex[64];

        public void Strip(ReadOnlySpan<BeamSeg> segments)
        {
            Strips++;
            if (_verts.Length < segments.Length * 2)
                _verts = new BeamSegVertex[segments.Length * 2];

            // CBeamSegDraw's maths here; native writes the vertices.
            int n = BeamSegDraw.Expand(segments, Camera, _verts);
            executor.Strip(MemoryMarshal.Cast<BeamSegVertex, float>(_verts.AsSpan(0, n)));
        }

        public void Mesh(BeamPrimitive primitive, int primitives, BeamColorKind colorKind,
                         ReadOnlySpan<BeamVertex> vertices)
        {
            Meshes++;
            executor.Mesh((int)primitive, primitives, (int)colorKind, MemoryMarshal.Cast<BeamVertex, float>(vertices));
        }
    }
}
