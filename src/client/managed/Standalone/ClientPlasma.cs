//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of _plasma: seven sprites that together look like a fire
/// burning with no fuel.
///
/// <para>
/// Where <see cref="ClientFiresmoke"/> starts one particle system and stops,
/// this is a full port of <c>C_Plasma</c> — six flames arranged in a ring whose
/// radius follows the flame's size, a glow lying under them that rises as it
/// grows, all seven animated, and the size itself interpolating towards whatever
/// the fire last asked for.
/// </para>
///
/// <para>
/// NO CLIENT OBJECT IS KEPT BEHIND THIS, which is the <see cref="ClientSprite"/>
/// argument and it holds here for the same reason:
/// <c>C_SpriteRenderer::DrawSprite</c> is a pure function of the values handed
/// to it. Stock held seven <c>C_Sprite</c> members and pushed each into the leaf
/// system by hand; what those objects contributed was a position and a scale
/// apiece, and both are two lines of arithmetic over state this class already
/// holds. Seven client entities per fire, to draw seven quads, is not a trade
/// worth keeping.
/// </para>
///
/// <para>
/// WHAT IS DELIBERATELY NOT PORTED. Stock drops a <c>PlasmaGlowFade</c> decal on
/// the floor twenty times a second while the flame is larger than 0.1. It is not
/// here: a client-side decal shot is an ABI slot of its own, the mark lands
/// under a <c>fire_floor.vmt</c> glow sprite that is already being drawn in the
/// same place, and this port was asked to keep its additions tight. Recorded in
/// docs/divergences.md.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.Plasma)]
public sealed class ClientPlasma : ClientStandaloneEntity
{
    public ClientPlasma(IEngineEntityRender render, IClientSpriteDraw effects,
                        IEngineEntities entities, IGameClock clock)
        : base(render)
    {
        Effects = effects;
        Lookup = entities;
        Clock = clock;
    }

    /// <summary>the sprite draw, planned on this side (ClientSpriteHost).</summary>
    private readonly IClientSpriteDraw Effects;

    /// <summary>
    /// asking this entity for its index, which is what the ring is derived from.
    ///
    /// NOT a random number generator. RandomFloat is a server-only ABI slot and
    /// answers a silent zero on this side, which would have given all six flames
    /// a yaw of zero and a frame rate of zero — one motionless sprite instead of
    /// a fire, with nothing said. See <see cref="PlasmaRing"/>.
    /// </summary>
    private readonly IEngineEntities Lookup;

    /// <summary>what the client thinks the time is.</summary>
    private readonly IGameClock Clock;

    public override string ClassName => EntityNames.Plasma;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public PlasmaNetState State = new();

    /// <summary>Stock's <c>NUM_CHILD_FLAMES</c>.</summary>
    private const int FlameCount = PlasmaRing.FlameCount;

    /// <summary>Stock's <c>CHILD_SPREAD</c>: how far out the ring reaches per unit of scale.</summary>
    private const float ChildSpread = 40f;

    /// <summary>How far a flame rises per unit it grows. Stock's 64.</summary>
    private const float FlameRise = 64f;

    /// <summary>How far the glow rises per unit the flame grows. Stock's 32.</summary>
    private const float GlowRise = 32f;

    /// <summary>
    /// The renderer treats a scale of exactly zero as one, so nothing is ever
    /// handed a bare zero. Stock's, both magnitudes of it.
    /// </summary>
    private const float MinFlameScale = 0.000001f;
    private const float MinGlowScale = 0.0000001f;

    /// <summary>
    /// kRenderFxNone. A plain int here rather than an enum, which is what every
    /// other sprite in this tree networks it as — the fx list is long, mostly
    /// unimplemented, and no map has ever set one on a flame.
    /// </summary>
    private const int RenderFxNone = 0;

    //-------------------------------------------------------------------------
    // The ring, chosen once when the flame arrives and then fixed.
    //-------------------------------------------------------------------------

    /// <summary>Which way out from the centre each flame sits, as a unit vector in XY.</summary>
    private readonly Vector[] _direction = new Vector[FlameCount];

    /// <summary>Where each flame's animation was when it started.</summary>
    private readonly float[] _startFrame = new float[FlameCount];

    /// <summary>How fast each flame animates, so they do not pulse together.</summary>
    private readonly float[] _frameRate = new float[FlameCount];

    /// <summary>
    /// What fraction of the flame's full size each member of the ring is.
    ///
    /// Stock's <c>m_entFlameScales</c>: the first is the full size and the rest
    /// taper, which is what stops the ring reading as six identical sprites.
    /// </summary>
    private readonly float[] _sizeFraction = new float[FlameCount];

    /// <summary>When the ring was built, which is what the animation counts from.</summary>
    private float _startedAt;

    /// <summary>Whether the ring has been chosen at all.</summary>
    private bool _started;

    //-------------------------------------------------------------------------
    // What changes as it burns.
    //-------------------------------------------------------------------------

    /// <summary>Each flame's size last frame, which is what its rise is measured against.</summary>
    private readonly float[] _scale = new float[FlameCount];

    /// <summary>How far each flame has risen from the entity, accumulated.</summary>
    private readonly float[] _rise = new float[FlameCount];

    /// <summary>The glow's size last frame, and how far it has risen.</summary>
    private float _glowScale;
    private float _glowRise;

    /// <summary>
    /// The size the flame is being drawn at — stock's <c>m_flScaleRegister</c>.
    /// </summary>
    private float _register;

    /// <summary>The ramp currently running: from, to, and between when.</summary>
    private float _rampFrom;
    private float _rampTo;
    private float _rampStart;
    private float _rampEnd;

    /// <summary>
    /// The destination last seen on the wire, so that an update which changed
    /// something else does not restart the ramp.
    /// </summary>
    private float _lastScaleSeen = float.NaN;

    /// <summary>
    /// A new size to grow or shrink to, and the box the renderer culls by.
    ///
    /// <para>
    /// This is <c>RecvProxy_PlasmaScale</c> and
    /// <c>RecvProxy_PlasmaScaleTime</c> together. Stock split them across two
    /// proxies and had to guard against each firing without the other; here both
    /// values are decoded before this runs, so the ramp is started once from
    /// both.
    /// </para>
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (!_started || updateType == DataUpdateType.Created)
            BuildRing();

        // Only a CHANGED destination starts a ramp. An update carrying a new
        // model index or a flipped Active bit must not restart one, which is
        // what re-reading Scale unconditionally would do — and a ramp restarted
        // every update never finishes.
        if (State.Scale != _lastScaleSeen)
        {
            _lastScaleSeen = State.Scale;

            _rampFrom  = _register;
            _rampTo    = State.Scale;
            _rampStart = Clock.Now;
            _rampEnd   = Clock.Now + State.ScaleTime;
        }

        // The box the leaf system culls by. Generous on purpose: the ring
        // reaches ChildSpread units out per unit of scale and the flames rise as
        // they grow, and a box that is a little large costs a renderable asked
        // to draw when it need not, where one a little small is a fire that
        // vanishes as it leaves the edge of the screen.
        float reach = MathF.Max(State.Scale, _register);

        if (!float.IsFinite(reach) || reach <= 0f)
            reach = 1f;

        float wide = reach * ChildSpread + 16f;
        float tall = reach * FlameRise + 16f;

        Render.SetCustomDraw(Handle,
                             new Vector(-wide, -wide, -16f),
                             new Vector(wide, wide, tall));
    }

    /// <summary>
    /// Chooses the ring: which way each flame sits, how fast it animates, and
    /// how large it is relative to its siblings.
    ///
    /// <para>
    /// Stock's <c>Start()</c>, and random for the reason stock is: six flames
    /// that agreed on their angle would draw as one sprite six times over.
    /// </para>
    /// </summary>
    private void BuildRing()
    {
        int index = Lookup.GetIndex(Handle);

        for (int i = 0; i < FlameCount; i++)
        {
            float yaw = PlasmaRing.Yaw(index, i);

            // AngleVectors( 0, yaw, 0 ) — pitch zero, so the direction is flat
            // and the ring lies around the vertical. The flames rise on their
            // own; nothing tilts them.
            _direction[i] = new Vector(MathF.Cos(yaw), MathF.Sin(yaw), 0f);

            _startFrame[i]   = PlasmaRing.StartFrame(index, i, State.MaxFrames);
            _frameRate[i]    = PlasmaRing.FrameRate(index, i);
            _sizeFraction[i] = PlasmaRing.SizeFraction(i);

            _scale[i] = State.StartScale;
            _rise[i] = 0f;
        }

        _register  = State.StartScale;
        _glowScale = State.StartScale;
        _glowRise  = 0f;
        _startedAt = Clock.Now;
        _started   = true;
    }

    /// <summary>
    /// A flicker, so the glow is never quite steady. Stock's three sines, and
    /// its frequencies.
    /// </summary>
    private float FlickerScale()
    {
        float t = Clock.Now;

        float result = MathF.Sin(t * 10000f);
        result += 0.5f * MathF.Sin(t * 2000f);
        result -= 0.5f * MathF.Cos(t * 8000f);

        return result * 0.1f;
    }

    /// <summary>
    /// Seven sprites, this frame.
    ///
    /// <para>
    /// Stock's <c>AddEntity</c>, <c>Update</c>, <c>UpdateScale</c>,
    /// <c>UpdateAnimation</c>, <c>UpdateFlames</c> and <c>AddFlames</c> in the
    /// order it ran them, with the two interpolations lifted into
    /// <see cref="PlasmaScaleRamp"/> and <see cref="SpriteFrameCursor"/> where a
    /// test can reach them.
    /// </para>
    ///
    /// <para>
    /// WHERE the sprites are is an offset from the entity rather than a world
    /// position, because native holds the interpolated place for the frame being
    /// drawn and this side holds the last snapshot. Same reason
    /// <see cref="ClientSprite"/> passes none at all.
    /// </para>
    /// </summary>
    public override int OnDraw()
    {
        // Stock's first line: an unlit plasma flame draws nothing AND updates
        // nothing, so it resumes where it left off rather than where it would
        // have got to.
        if (!State.Active || !_started)
            return 0;

        // A dormant entity answers its last known values without complaint, and
        // those values are a position the flame has since left. Drawing from
        // them puts a fire where the fire is not.
        if (Render.IsDormant(Handle))
            return 0;

        if (State.PlasmaModelIndex <= 0 || State.PlasmaModelIndex2 <= 0)
            return 0;

        // --- the size, interpolated ---------------------------------------
        _register = PlasmaScaleRamp.Evaluate(Clock.Now, _rampStart, _rampEnd, _rampFrom, _rampTo);

        float elapsed = Clock.Now - _startedAt;
        int drawn = 0;

        // --- the ring ------------------------------------------------------
        for (int i = 0; i < FlameCount; i++)
        {
            float wanted = _register * _sizeFraction[i];
            float scale = MathF.Max(MinFlameScale, wanted);

            // Stock measures the rise against the CLAMPED previous size, not
            // the wanted one, so the floor participates in the sum.
            _rise[i] += (wanted - _scale[i]) * FlameRise;
            _scale[i] = scale;

            // The centre flame stays on the axis; the rest stand off by their
            // own size, so the ring opens out as the fire grows.
            Vector offset = i == 0
                ? new Vector(0f, 0f, _rise[i])
                : new Vector(_direction[i].X * scale * ChildSpread,
                             _direction[i].Y * scale * ChildSpread,
                             _rise[i]);

            int model = (i % 2) != 0 ? State.PlasmaModelIndex : State.PlasmaModelIndex2;

            float frame = SpriteFrameCursor.Advance(_startFrame[i], _frameRate[i],
                                                    elapsed, State.MaxFrames);

            drawn += Effects.DrawSpriteOffset(Handle, offset, model,
                                              frame,
                                              (int)RenderMode.TransAddFrameBlend, RenderFxNone,
                                              255, 255, 255, 255,
                                              scale, worldSpaceScale: false,
                                              hdrColorScale: 1f, glowProxySize: 0f);
        }

        // --- the glow beneath ----------------------------------------------
        if (State.GlowModelIndex > 0)
        {
            _glowRise += (_register - _glowScale) * GlowRise;
            _glowScale = _register;

            float glowScale = MathF.Max(MinGlowScale, _register * 1.5f + FlickerScale());

            drawn += Effects.DrawSpriteOffset(Handle, new Vector(0f, 0f, _glowRise),
                                              State.GlowModelIndex,
                                              frame: 0f,
                                              (int)RenderMode.TransAdd, RenderFxNone,
                                              255, 255, 255, 255,
                                              glowScale, worldSpaceScale: false,
                                              hdrColorScale: 1f, glowProxySize: 0f);
        }

        return drawn > 0 ? 1 : 0;
    }
}
