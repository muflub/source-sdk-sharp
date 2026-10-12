using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Ropes;

/// <summary>One rope span as the system reports it.</summary>
public readonly record struct RopeSpanReport(
    EntityHandle Owner, int Nodes, bool Initted, Vector Start, Vector End, float StartError, float EndError,
    float Chord, float Sag, float OwnerError, int Thinks, int Draws);

/// <summary>
/// The client's ropes (plan_client_novalve.md §2 cluster 6): one simulated
/// span per managed rope link, stepped once a frame, built and handed to the
/// native executor from inside the owner's draw.
/// </summary>
public interface IClientRopes
{
    /// <summary>Creates or updates the span a rope link owns.</summary>
    void Configure(EntityHandle owner, int ownerIndex, in Vector endPos, float slack, float width, float textureScale,
                   int segments, int subdiv, float scrollSpeed, string material);

    /// <summary>The span goes when its link does.</summary>
    void Remove(EntityHandle owner);

    /// <summary>The owner's draw: builds the span for this view and queues it. Returns whether it drew.</summary>
    bool Draw(EntityHandle owner);

    /// <summary>The "ShakeRopes" effect: an impulse on every rope near a point.</summary>
    void Shake(in Vector center, float radius, float magnitude);

    /// <summary>Spans held.</summary>
    int Count { get; }

    /// <summary>Thinks that stepped the simulation, since the level began.</summary>
    long Steps { get; }

    /// <summary>Strips handed to the executor, since the level began.</summary>
    long Strips { get; }

    /// <summary>Link updates received, since the level began.</summary>
    long Configures { get; }

    /// <summary>What each span is doing, for the dev API and cs_cl_rope_spans.</summary>
    IReadOnlyList<RopeSpanReport> Report();
}

/// <summary>
/// <see cref="IClientRopes"/> over <see cref="RopeSimulation"/> and
/// <see cref="RopeBuilder"/>, drawing through <see cref="IClientRopeExecutor"/>.
/// Carries every span's state across a client reload.
/// </summary>
public sealed class ClientRopeSystem : IClientRopes, IClientLevelHook, IReloadParticipant
{
    // RopeDraw's flags (shell_rope_render.cpp).
    private const int DrawFakeAA = 0x1;
    private const int DrawSolid = 0x2;

    // MASK_SOLID_BRUSHONLY: CONTENTS_SOLID | CONTENTS_MOVEABLE | CONTENTS_WINDOW | CONTENTS_GRATE.
    private const int MaskSolidBrushOnly = 0x1 | 0x4000 | 0x2 | 0x8;

    private sealed class Span
    {
        public EntityHandle Owner;
        public int OwnerIndex;
        public readonly RopeSimulation Sim = new();
        public string Material = string.Empty;
        public int MaterialHandle = -1;
        public bool HasBack;
        public Vector LastMins, LastMaxs;
        public bool BoundsSent;
        public int Thinks, Draws;
        public bool OwnerSeen;
        public Config? Pending;
    }

    // What the link's last OnDataChanged said, applied at the next frame once
    // the owner's origin (the near end, and so the length) can be read.
    private sealed record Config(Vector End, float Slack, float Width, float TextureScale, int Subdiv, float ScrollSpeed);

    private readonly List<Span> _spans = [];
    private readonly IClientRopeExecutor _executor;
    private readonly IEngineEntityRender _render;
    private readonly IEngineEffects _effects;
    private readonly IGameClock _clock;
    private readonly RopeConVars _vars;
    private readonly IEngineConsole _console;
    private readonly IRopeRandom _random;
    private readonly IRopeWorld _world;
    private readonly IRopeEndpoints _endpoints;

    private readonly BeamSeg[] _segs = new BeamSeg[RopeBuilder.MaxSegments];
    private readonly BeamSeg[] _solid = new BeamSeg[RopeBuilder.MaxSegments];
    private readonly float[] _back = new float[RopeBuilder.MaxSegments];
    private readonly BeamSegVertex[] _verts = new BeamSegVertex[RopeBuilder.MaxSegments * 2];
    private readonly BeamSegVertex[] _solidVerts = new BeamSegVertex[RopeBuilder.MaxSegments * 2];
    private readonly Vector[] _predicted = new Vector[RopePhysics.MaxNodes];

    public ClientRopeSystem(IClientRopeExecutor executor, IEngineEntityRender render, IEngineEffects effects,
                            IEngineWorldQuery world, IEngineHost host, IGameClock clock, RopeConVars vars,
                            IEngineConsole console)
    {
        _executor = executor;
        _render = render;
        _effects = effects;
        _clock = clock;
        _vars = vars;
        _console = console;
        _random = new EngineRopeRandom(host);
        _world = new EngineRopeWorld(executor);
        _endpoints = new EngineRopeEndpoints(effects, world);
    }

    public int Count => _spans.Count;

    public long Steps { get; private set; }

    public long Strips { get; private set; }

    private RopeSimSettings Settings() => _vars.Sim;

    //-------------------------------------------------------------------------
    // Spans.
    //-------------------------------------------------------------------------

    private Span? Find(EntityHandle owner)
    {
        foreach (Span s in _spans)
        {
            if (s.Owner == owner)
                return s;
        }

        return null;
    }

    private bool OwnerOrigin(Span span, out Vector origin)
        => _effects.ClientEntityOrigin(span.OwnerIndex, out origin);

    /// <summary>Configures and Removes seen, for the report.</summary>
    public long Configures { get; private set; }

    public void Configure(EntityHandle owner, int ownerIndex, in Vector endPos, float slack, float width,
                          float textureScale, int segments, int subdiv, float scrollSpeed, string material)
    {
        Configures++;
        Span? span = Find(owner);

        if (span is null)
        {
            span = new Span { Owner = owner, OwnerIndex = ownerIndex, Material = material };
            span.MaterialHandle = _executor.Material(material, out int textureHeight, out span.HasBack);

            // Map ropes simulate, and hang for a moment on creation so the
            // cable starts sagged rather than snapping taut from a straight
            // line. Both ends are positions: the near one is swept to the
            // owner once a frame, the far one is written on every update.
            span.Sim.Create(width, Math.Clamp(segments, 2, RopePhysics.MaxNodes),
                            RopeFlags.Simulate | RopeFlags.InitialHang, textureHeight);

            _spans.Add(span);
        }

        span.OwnerIndex = ownerIndex;
        span.Pending = new Config(endPos, slack, width, textureScale, subdiv, scrollSpeed);
    }

    private static void Apply(Span span, in Vector start, Config c)
    {
        span.Sim.SetEndPos(c.End);

        // Stock computes the length server-side (RecalculateLength: endpoint
        // distance, without the slack); the client holds both endpoints, so it
        // is computed here, as shell_rope_render.cpp did.
        span.Sim.RopeLength = (int)(start - c.End).Length;
        span.Sim.Slack = (int)c.Slack;
        span.Sim.Width = c.Width;
        span.Sim.TextureScale = c.TextureScale;
        span.Sim.Subdiv = Math.Clamp(c.Subdiv, 1, RopeBuilder.MaxSubdivs);
        span.Sim.ScrollSpeed = c.ScrollSpeed;
        span.Sim.RecomputeSprings();
    }

    public void Remove(EntityHandle owner)
    {
        Span? span = Find(owner);
        if (span is not null)
            _spans.Remove(span);
    }

    //-------------------------------------------------------------------------
    // The frame: sweep every near end to its owner, then step every rope.
    //-------------------------------------------------------------------------

    /// <summary>Once per frame, never per view: a verlet step is not idempotent.</summary>
    public void Frame(float frameTime)
    {
        if (_spans.Count == 0)
            return;

        for (int i = _spans.Count - 1; i >= 0; i--)
        {
            Span span = _spans[i];

            // An owner the client had and no longer has takes its span with
            // it (OnRemoved is the ordinary route; this is the backstop). One
            // it has not resolved yet is waited for.
            if (!OwnerOrigin(span, out Vector origin))
            {
                if (span.OwnerSeen)
                    _spans.RemoveAt(i);
                continue;
            }

            span.OwnerSeen = true;
            span.Sim.SetStartPos(origin);

            if (span.Pending is { } config)
            {
                Apply(span, origin, config);
                span.Pending = null;
            }
        }

        float now = _clock.Now;
        ClientViewState view = _executor.View(insideView: false);
        var frame = new RopeFrame(frameTime, now, view.MainOrigin, _effects.ClientWindSpeed(now));
        RopeSimSettings settings = Settings();

        foreach (Span span in _spans)
        {
            // Not until the owner has been seen and the link configured.
            if (!span.OwnerSeen)
                continue;

            OwnerOrigin(span, out Vector origin);
            span.Sim.BoundsOrigin = origin;

            span.Sim.Think(frame, settings, _random, _world, _endpoints);
            span.Thinks++;
            Steps++;

            if (!span.BoundsSent || !Same(span.Sim.RenderMins, span.LastMins) || !Same(span.Sim.RenderMaxs, span.LastMaxs))
            {
                span.LastMins = span.Sim.RenderMins;
                span.LastMaxs = span.Sim.RenderMaxs;
                span.BoundsSent = true;
                _render.SetCustomDraw(span.Owner, span.LastMins, span.LastMaxs);
            }
        }
    }

    private static bool Same(in Vector a, in Vector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    //-------------------------------------------------------------------------
    // The draw.
    //-------------------------------------------------------------------------

    public bool Draw(EntityHandle owner)
    {
        Span? span = Find(owner);

        if (span is null || span.MaterialHandle < 0 || !span.OwnerSeen)
            return false;

        if (!span.Sim.ShouldDraw(_vars.Translucent))
            return false;

        ClientViewState view = _executor.View(insideView: true);
        float now = _clock.Now;
        var frame = new RopeFrame(0, now, view.MainOrigin, default);

        if (!span.Sim.PrepareDraw(frame, Settings(), _random, _world, _endpoints))
            return false;

        // CRopeManager::DrawRenderCache's own early-out.
        if (!_vars.DrawRopes)
            return true;

        int nodes = span.Sim.Physics.NodeCount;
        for (int i = 0; i < nodes; i++)
            _predicted[i] = span.Sim.Physics.Node(i).Predicted;

        bool fakeAA = span.HasBack && _vars.Smooth && view.DxLevel > 70 && !view.AAEnabled;

        var buildView = new RopeBuildView(
            view.Forward, view.Origin, view.ScreenWidth, fakeAA,
            FullBright: (int.TryParse(_console.FindConVarValue("mat_fullbright"), out int fb) ? fb : 0) == 1,
            SubdivConVar: _vars.Subdiv,
            SmoothEnlarge: _vars.SmoothEnlarge,
            SmoothMinAlpha: _vars.SmoothMinAlpha,
            SmoothMaxAlpha: _vars.SmoothMaxAlpha,
            SmoothMinWidth: _vars.SmoothMinWidth,
            SmoothMaxAlphaWidth: _vars.SmoothMaxAlphaWidth);

        var input = new RopeBuildInput
        {
            Predicted = _predicted.AsSpan(0, nodes),
            LightValues = span.Sim.LightValues,
            ColorMod = span.Sim.ColorMod,
            RopeLength = span.Sim.RopeLength,
            Slack = span.Sim.Slack,
            CurScroll = span.Sim.CurScroll,
            Width = span.Sim.Width,
            TextureScale = span.Sim.TextureScale,
            TextureHeight = span.Sim.TextureHeight,
            Subdiv = span.Sim.Subdiv,
            Flags = span.Sim.Flags,
        };

        int count = RopeBuilder.Build(input, buildView, _segs, _back, out float maxBack);

        int flags = _vars.RenderSolid ? DrawSolid : 0;
        int solidCount = 0;

        if (fakeAA)
        {
            flags |= DrawFakeAA;
            if (RopeBuilder.SolidPass(_segs.AsSpan(0, count), _back.AsSpan(0, count), maxBack,
                                      _vars.SolidMinWidth, _vars.SolidMaxWidth,
                                      _vars.SolidMinAlpha, _vars.SolidMaxAlpha,
                                      _solid))
                solidCount = count;
        }

        // CRopeManager::DrawRenderCache's CBeamSegDraw, facing the camera the
        // view draws from; native writes the vertices.
        int verts = BeamSegDraw.Expand(_segs.AsSpan(0, count), view.Camera, _verts);
        int solidVerts = BeamSegDraw.Expand(_solid.AsSpan(0, solidCount), view.Camera, _solidVerts);

        _executor.Draw(span.MaterialHandle,
                       MemoryMarshal.Cast<BeamSegVertex, float>(_verts.AsSpan(0, verts)),
                       MemoryMarshal.Cast<BeamSegVertex, float>(_solidVerts.AsSpan(0, solidVerts)),
                       flags);

        span.Draws++;
        Strips++;
        return true;
    }

    public void Shake(in Vector center, float radius, float magnitude)
    {
        foreach (Span span in _spans)
            span.Sim.ShakeRope(center, radius, magnitude);
    }

    //-------------------------------------------------------------------------
    // Reports.
    //-------------------------------------------------------------------------

    public IReadOnlyList<RopeSpanReport> Report()
    {
        var rows = new List<RopeSpanReport>(_spans.Count);

        foreach (Span span in _spans)
        {
            RopeSimulation sim = span.Sim;
            int n = sim.Physics.NodeCount;
            Vector start = sim.FixedEndPointPos(0), end = sim.FixedEndPointPos(1);

            float sag = 0;
            for (int i = 1; i < n - 1; i++)
            {
                float t = (float)i / (n - 1);
                float drop = RopeMath.Lerp(start, end, t).Z - sim.Physics.Node(i).Pos.Z;
                if (drop > sag)
                    sag = drop;
            }

            // Node 0 against the owner's origin THIS frame: the per-frame sweep,
            // measured (-1 when the owner does not resolve).
            float ownerError = OwnerOrigin(span, out Vector owner) ? (sim.Physics.Node(0).Pos - owner).Length : -1f;

            rows.Add(new RopeSpanReport(
                span.Owner, n, sim.PhysicsInitted, start, end,
                (sim.Physics.Node(0).Pos - start).Length, (sim.Physics.Node(n - 1).Pos - end).Length,
                (end - start).Length, sag, ownerError, span.Thinks, span.Draws));
        }

        return rows;
    }

    //-------------------------------------------------------------------------
    // Level and reload.
    //-------------------------------------------------------------------------

    // NOT a clear: the level's rope links can be created and configured before
    // the managed level init runs, and their spans are theirs. The counters
    // restart; the spans went at the last level's shutdown.
    void IClientLevelHook.OnLevelInit(string mapName)
    {
        Steps = 0;
        Strips = 0;
        Configures = 0;
    }

    void IClientLevelHook.OnLevelShutdown() => _spans.Clear();

    /// <summary>
    /// Every span's ring and the state its next think reads, so a reload
    /// neither re-hangs the cables nor resets their gusts. The material handle
    /// is not carried: native keeps it and the name re-derives it.
    /// </summary>
    public void Save(ReloadWriter writer)
    {
        // The counters too: a reload continues them rather than restarting them.
        writer.Write((int)Steps);
        writer.Write((int)Strips);
        writer.Write((int)Configures);
        writer.Write(_spans.Count);

        foreach (Span span in _spans)
        {
            writer.Write((int)span.Owner.Value);
            writer.Write(span.OwnerIndex);
            writer.Write(span.Material);
            writer.Write(span.OwnerSeen);
            writer.Write(span.Thinks);
            writer.Write(span.Draws);
            span.Sim.Save(writer);
        }
    }

    public void Restore(ReloadReader reader)
    {
        _spans.Clear();
        Steps = reader.ReadInt();
        Strips = reader.ReadInt();
        Configures = reader.ReadInt();
        int count = reader.ReadInt();

        for (int i = 0; i < count; i++)
        {
            var span = new Span
            {
                Owner = new EntityHandle((uint)reader.ReadInt()),
                OwnerIndex = reader.ReadInt(),
                Material = reader.ReadString(),
                OwnerSeen = reader.ReadBool(),
                Thinks = reader.ReadInt(),
                Draws = reader.ReadInt(),
            };

            span.Sim.Restore(reader);
            span.Sim.Physics.SetDelegate(span.Sim);
            span.MaterialHandle = _executor.Material(span.Material, out _, out span.HasBack);

            _spans.Add(span);
        }
    }
}

/// <summary>The rope's random draws from the engine's stream.</summary>
internal sealed class EngineRopeRandom(IEngineHost host) : IRopeRandom
{
    public float RandomFloat(float lo, float hi) => host.RandomFloat(lo, hi);

    /// <summary>
    /// <c>Vector::Random</c>: three <c>rand()</c> draws (x, y, z), each
    /// <c>lo + ((float)rand() / VALVE_RAND_MAX) * (hi - lo)</c>. The engine's
    /// <c>rand()</c> is <c>RandomInt( 0, VALVE_RAND_MAX )</c>; managed code has
    /// the float stream only, so the integer is the floor of a draw over
    /// [0, VALVE_RAND_MAX + 1), the same distribution.
    /// </summary>
    public Vector RandomVector(float lo, float hi)
    {
        const int RandMax = 0x7fff;

        float Component()
        {
            int r = Math.Min((int)host.RandomFloat(0f, RandMax + 1), RandMax);
            return lo + ((float)r / RandMax) * (hi - lo);
        }

        float x = Component();
        float y = Component();
        float z = Component();
        return new Vector(x, y, z);
    }
}

/// <summary>The world a rope collides with and is lit by.</summary>
internal sealed class EngineRopeWorld(IClientRopeExecutor executor) : IRopeWorld
{
    // MASK_SOLID_BRUSHONLY.
    private const int Mask = 0x1 | 0x2 | 0x8 | 0x4000;

    public RopeTrace TraceHull(in Vector start, in Vector end, in Vector mins, in Vector maxs)
    {
        ClientHullTrace t = executor.TraceHullWorld(start, end, mins, maxs, Mask);
        return new RopeTrace(t.Fraction, t.AllSolid, t.StartSolid, t.PlaneNormal, t.PlaneDist);
    }

    public Vector ComputeLighting(in Vector pos, Span<Vector> boxColors) => executor.ComputeLighting(pos, boxColors);
}

/// <summary>
/// The entities a rope can hang from. Every span this client makes hangs
/// between two positions, so this is the stock entity arm kept reachable: the
/// origin and the model's bounds by index. Attachments have no client read
/// yet, so an attachment endpoint falls back to the entity's centre, which is
/// stock's own fallback for an attachment that does not resolve.
/// </summary>
internal sealed class EngineRopeEndpoints(IEngineEffects effects, IEngineWorldQuery world) : IRopeEndpoints
{
    public bool Exists(EntityHandle entity) => effects.ClientEntityOrigin(entity.EntryIndex, out _);

    public bool GetAttachment(EntityHandle entity, int attachment, out Vector origin, out Vector angles)
    {
        origin = default;
        angles = default;
        return false;
    }

    public Vector WorldSpaceCenter(EntityHandle entity)
    {
        if (!world.ClientEntityModelBounds(entity.EntryIndex, out Vector mins, out Vector maxs, out _))
            return effects.ClientEntityOrigin(entity.EntryIndex, out Vector o) ? o : default;

        effects.ClientEntityOrigin(entity.EntryIndex, out Vector origin);
        return origin + (mins + maxs) * 0.5f;
    }

    public Vector AbsAngles(EntityHandle entity)
        => world.ClientEntityModelBounds(entity.EntryIndex, out _, out _, out Vector angles) ? angles : default;

    public bool IsDormant(EntityHandle entity) => !Exists(entity);

    public int ModelIndex(EntityHandle entity) => 0;
}
