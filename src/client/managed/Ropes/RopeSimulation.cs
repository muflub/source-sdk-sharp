//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Ropes;

/// <summary>The <c>ROPE_*</c> flags (rope_shared.h).</summary>
[Flags]
public enum RopeFlags
{
    None = 0,
    Resize = 1 << 0,
    Barbed = 1 << 1,
    Collide = 1 << 2,
    Simulate = 1 << 3,
    Breakable = 1 << 4,
    NoWind = 1 << 5,
    InitialHang = 1 << 6,
    PlayerWeaponAttach = 1 << 7,
    NoGravity = 1 << 8,
}

/// <summary>Which ends are locked, and whether their direction is (rope_shared.h).</summary>
[Flags]
public enum RopeLock
{
    None = 0,
    StartPoint = 0x1,
    EndPoint = 0x2,
    StartDirection = 0x4,
    EndDirection = 0x8,
}

/// <summary>What a rope reads from the frame it is stepped in.</summary>
public readonly record struct RopeFrame(float FrameTime, float CurTime, Vector MainViewOrigin, Vector Wind);

/// <summary>The rope ConVars the simulation reads (c_rope.cpp's statics).</summary>
public readonly record struct RopeSimSettings(bool DrawRopes, bool Shake, int Collide, float WindDist, bool AverageLight)
{
    /// <summary>Every default as c_rope.cpp declares it.</summary>
    public static readonly RopeSimSettings Defaults = new(true, false, 1, 1000f, true);
}

/// <summary>The random stream a rope draws its gusts from.</summary>
public interface IRopeRandom
{
    /// <summary><c>RandomFloat( lo, hi )</c>.</summary>
    float RandomFloat(float lo, float hi);

    /// <summary><c>RandomVector( lo, hi )</c>: three <c>rand()</c> draws, x first.</summary>
    Vector RandomVector(float lo, float hi);
}

/// <summary>One hull trace's answer: <c>trace_t</c>'s fields the rope reads.</summary>
public readonly record struct RopeTrace(float Fraction, bool AllSolid, bool StartSolid, Vector PlaneNormal, float PlaneDist);

/// <summary>What a rope asks of the world: a hull trace for collision and the
/// lighting at a node.</summary>
public interface IRopeWorld
{
    /// <summary><c>UTIL_TraceHull( start, end, mins, maxs, MASK_SOLID_BRUSHONLY, world only )</c>.</summary>
    RopeTrace TraceHull(in Vector start, in Vector end, in Vector mins, in Vector maxs);

    /// <summary><c>engine-&gt;ComputeLighting( pos, NULL, true, colour, boxColors )</c>.</summary>
    Vector ComputeLighting(in Vector pos, Span<Vector> boxColors);
}

/// <summary>
/// An entity a rope hangs from. The stock rope reads these through its
/// endpoint handles; a rope strung between two positions never asks.
/// </summary>
public interface IRopeEndpoints
{
    /// <summary>Whether the handle resolves to an entity at all.</summary>
    bool Exists(EntityHandle entity);

    /// <summary><c>GetAttachment( attachment, origin, angles )</c>.</summary>
    bool GetAttachment(EntityHandle entity, int attachment, out Vector origin, out Vector angles);

    /// <summary><c>UTIL_CollideableWorldSpaceCenter( GetCollideable() )</c>.</summary>
    Vector WorldSpaceCenter(EntityHandle entity);

    /// <summary><c>GetAbsAngles()</c>.</summary>
    Vector AbsAngles(EntityHandle entity);

    /// <summary><c>IsDormant()</c>; a handle that does not resolve is dormant.</summary>
    bool IsDormant(EntityHandle entity);

    /// <summary>The entity's model index, zero for none.</summary>
    int ModelIndex(EntityHandle entity);
}

/// <summary>
/// The simulation half of <c>C_RopeKeyframe</c> (c_rope.cpp): the verlet ring,
/// its forces (gravity, wind, gusts, shakes) and constraints (world collision,
/// locked ends), the resting test that lets a still rope skip its step, and the
/// render bounds. What the stock class did as an engine renderable (the leaf
/// system, the render cache, the mesh) is not here; <see cref="RopeBuilder"/>
/// turns this state into the segments the native executor draws.
/// </summary>
public sealed class RopeSimulation : IRopePhysicsHelper
{
    // c_rope.cpp's file constants.
    private const float ImpulseScale = 20;               // ROPE_IMPULSE_SCALE
    private const float ImpulseDecay = (float)0.95;      // ROPE_IMPULSE_DECAY, through operator*=( float )
    private const float WindForceFactor = 10;            // WIND_FORCE_FACTOR
    private const float ShakeScale = 15000;              // GetNodeForces' static scale
    private const float GustScale = 50;                  // RopeThink's static basicScale
    private const float LockAmount = 0.1f;               // g_flLockAmount
    private const float LockFalloff = 0.3f;              // g_flLockFalloff
    private const float CollideSlowFactor = 0.3f;        // ApplyConstraints' static flSlowFactor
    private const int CollideIterations = 10;
    private const int SlackFudge = -100;                 // ROPESLACK_FUDGEFACTOR
    private static Vector Gravity => new(0, 0, -1500);           // ROPE_GRAVITY

    private readonly RopePhysics _physics = new();
    private readonly bool[] _linksTouching = new bool[RopePhysics.MaxNodes];
    private readonly Vector[] _lightValues = new Vector[RopePhysics.MaxNodes];

    private readonly Vector[] _cachedEndPos = new Vector[2];
    private readonly Vector[] _cachedEndAngles = new Vector[2];
    private readonly bool[] _prevEndPointValid = new bool[2];
    private readonly Vector[] _prevEndPointPos = new Vector[2];
    private readonly bool[] _endPointFixed = new bool[2];
    private readonly Vector[] _fixedEndPointPos = new Vector[2];
    private readonly EntityHandle[] _endEntity = [EntityHandle.None, EntityHandle.None];
    private readonly int[] _endAttachment = new int[2];

    private bool _endPositionsDirty = true;
    private bool _endAnglesDirty = true;

    // The frame being stepped, for the physics callbacks.
    private RopeFrame _frame;
    private RopeSimSettings _settings = RopeSimSettings.Defaults;
    private IRopeRandom? _random;
    private IRopeWorld? _world;
    private IRopeEndpoints? _endpoints;

    public RopeSimulation()
    {
        // C_RopeKeyframe's constructor. The members it leaves alone are zero
        // here; rope-stock.tsv's RC rows recorded them zero on both ropes.
        Subdiv = 255;
        TextureScale = 4;
        TextureHeight = 1;
        ColorMod = new Vector(1, 1, 1);
    }

    //-------------------------------------------------------------------------
    // State, named after the members.
    //-------------------------------------------------------------------------

    public RopePhysics Physics => _physics;
    public RopeFlags Flags { get; set; }
    public RopeLock LockedPoints { get; set; }
    public RopeLock PrevLockedPoints { get; private set; }
    public int Segments { get; private set; }
    public int RopeLength { get; set; }
    public int Slack { get; set; }
    public float Width { get; set; }
    public float TextureScale { get; set; }
    public int TextureHeight { get; set; }
    public int Subdiv { get; set; }
    public float ScrollSpeed { get; set; }
    public float CurScroll { get; set; }
    public Vector ColorMod { get; set; }
    public bool ConstrainBetweenEndpoints { get; set; }
    public bool NewDataThisFrame { get; set; }
    public bool PhysicsInitted { get; private set; }
    public bool ApplyWind { get; private set; }
    public int ForcePointMoveCounter { get; private set; }
    public int LinksTouchingSomething { get; private set; }
    public Vector Impulse { get; set; }
    public Vector PreviousImpulse { get; set; }
    public float CurrentGustTimer { get; set; }
    public float CurrentGustLifetime { get; set; }
    public float TimeToNextGust { get; set; }
    public Vector WindDir { get; set; }
    public Vector RenderMins { get; private set; }
    public Vector RenderMaxs { get; private set; }
    public ReadOnlySpan<Vector> LightValues => _lightValues.AsSpan(0, _physics.NodeCount);

    /// <summary>The origin <c>UpdateBBox</c> takes the bounds relative to
    /// (<c>GetAbsOrigin()</c>; zero for a rope that is not an entity).</summary>
    public Vector BoundsOrigin { get; set; }

    //-------------------------------------------------------------------------
    // Creation: Create + FinishInit, minus the materials (the executor's).
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>C_RopeKeyframe::Create</c> without the endpoint entities (a rope hung
    /// between two positions, as every managed span is) and without the
    /// material lookup, which is a handle and stays native: the caller hands
    /// the material's mapping height.
    /// </summary>
    public void Create(float width, int segments, RopeFlags flags, int textureHeight)
    {
        Width = width;
        Segments = Math.Clamp(segments, 2, RopePhysics.MaxNodes);
        Flags = flags;
        TextureHeight = textureHeight;

        // FinishInit.
        Segments = Math.Clamp(Segments, 2, RopePhysics.MaxNodes);
        _physics.SetNumNodes(Segments);
        RenderMins = new Vector(-10, -10, -10);
        RenderMaxs = new Vector(10, 10, 10);
    }

    /// <summary>
    /// The entity arm of <c>Create</c>: a rope locked to an entity (or an
    /// attachment on one) at either end.
    /// </summary>
    public void SetEndEntities(EntityHandle start, int startAttachment, EntityHandle end, int endAttachment)
    {
        if (start != EntityHandle.None)
        {
            _endEntity[0] = start;
            LockedPoints |= RopeLock.StartPoint;
        }

        if (end != EntityHandle.None)
        {
            _endEntity[1] = end;
            LockedPoints |= RopeLock.EndPoint;
        }

        _endAttachment[0] = startAttachment;
        _endAttachment[1] = endAttachment;
    }

    /// <summary>
    /// <c>C_RopeKeyframe::CreateFromKeyValues</c>: a model's "Cables" entry,
    /// strung between two attachments of the same entity.
    /// </summary>
    public void CreateFromKeyValues(EntityHandle entity, int startAttachment, int endAttachment,
                                    float width, int segments, bool gravity, int length,
                                    float? textureScale, int textureHeight)
    {
        Create(width, segments, RopeFlags.None, textureHeight);
        SetEndEntities(entity, startAttachment, entity, endAttachment);

        if (!gravity)
            Flags |= RopeFlags.NoGravity;

        RopeLength = length;
        if (textureScale is float scale)
            TextureScale = scale;
        Slack = 0;
        Flags |= RopeFlags.Simulate;
    }

    //-------------------------------------------------------------------------
    // Configuration.
    //-------------------------------------------------------------------------

    public void SetStartPos(in Vector pos) => SetFixedEnd(0, pos, RopeLock.StartPoint);

    public void SetEndPos(in Vector pos) => SetFixedEnd(1, pos, RopeLock.EndPoint);

    private void SetFixedEnd(int pt, in Vector pos, RopeLock lockFlag)
    {
        _endPointFixed[pt] = true;
        _fixedEndPointPos[pt] = pos;
        LockedPoints |= lockFlag;

        _endPositionsDirty = true;
        _endAnglesDirty = true;
    }

    public bool IsEndPointFixed(int pt) => _endPointFixed[pt];

    public Vector FixedEndPointPos(int pt) => _fixedEndPointPos[pt];

    public void SetSlack(int slack)
    {
        Slack = slack;
        RecomputeSprings();
    }

    /// <summary><c>RecomputeSprings</c>: an INTEGER division, as stock's members are ints.</summary>
    public void RecomputeSprings()
        => _physics.ResetSpringLength((RopeLength + Slack + SlackFudge) / (_physics.NodeCount - 1));

    /// <summary><c>ShakeRope</c>: the "ShakeRopes" effect's impulse.</summary>
    public void ShakeRope(in Vector center, float radius, float magnitude)
    {
        Vector impulse = Impulse;

        for (int i = 0; i < Segments; i++)
        {
            float dist = (_physics.Node(i).Pos - center).Length;
            float shake = 1.0f - dist / radius;

            if (shake >= 0)
                impulse.Z += shake * magnitude;
        }

        Impulse = impulse;
    }

    //-------------------------------------------------------------------------
    // The frame.
    //-------------------------------------------------------------------------

    private void Bind(in RopeFrame frame, in RopeSimSettings settings, IRopeRandom random,
                      IRopeWorld world, IRopeEndpoints? endpoints)
    {
        _frame = frame;
        _settings = settings;
        _random = random;
        _world = world;
        _endpoints = endpoints;
    }

    /// <summary><c>RopeThink</c>: once per frame, never per view.</summary>
    public void Think(in RopeFrame frame, in RopeSimSettings settings, IRopeRandom random,
                      IRopeWorld world, IRopeEndpoints? endpoints = null)
    {
        Bind(frame, settings, random, world, endpoints);

        _endPositionsDirty = true;
        _endAnglesDirty = true;

        if (!settings.DrawRopes)
            return;

        if (!InitRopePhysics())
            return;

        if (DetectRestingState(out bool applyWind))
        {
            ApplyWind = applyWind;
            return;
        }

        ApplyWind = applyWind;

        RunRopeSimulation(frame.FrameTime);

        NewDataThisFrame = false;

        CurrentGustTimer += frame.FrameTime;
        TimeToNextGust -= frame.FrameTime;

        if (TimeToNextGust <= 0)
        {
            Vector dir = random.RandomVector(-1, 1);
            RopeMath.Normalize(ref dir);

            dir *= GustScale;
            dir *= random.RandomFloat(-1.0f, 1.0f);
            WindDir = dir;

            CurrentGustTimer = 0;
            CurrentGustLifetime = random.RandomFloat(2.0f, 3.0f);

            TimeToNextGust = random.RandomFloat(3.0f, 4.0f);
        }

        UpdateBBox();
    }

    /// <summary>
    /// What <c>DrawModel</c> does to the rope before it is queued: initialise,
    /// resize, refuse a rope whose two endpoint entities are both dormant
    /// modelled entities, and pull the nodes inside the endpoints if asked.
    /// Returns whether the rope is drawn.
    /// </summary>
    public bool PrepareDraw(in RopeFrame frame, in RopeSimSettings settings, IRopeRandom random,
                            IRopeWorld world, IRopeEndpoints? endpoints = null)
    {
        Bind(frame, settings, random, world, endpoints);

        if (!InitRopePhysics())
            return false;

        if ((Flags & RopeFlags.Resize) != 0)
            RecomputeSprings();

        if (_endEntity[0] != EntityHandle.None && EndpointDormant(_endEntity[0]) &&
            _endEntity[1] != EntityHandle.None && EndpointDormant(_endEntity[1]))
        {
            if (endpoints!.ModelIndex(_endEntity[0]) != 0 && endpoints.ModelIndex(_endEntity[1]) != 0)
                return false;
        }

        ConstrainNodesBetweenEndpoints();
        return true;
    }

    /// <summary><c>ShouldDraw</c>, less the ConVar the caller reads.</summary>
    public bool ShouldDraw(bool ropeTranslucent)
        => ropeTranslucent && (Flags & RopeFlags.Simulate) != 0;

    /// <summary>
    /// <c>RopeEndpointIsDormant</c>: an endpoint the client cannot resolve
    /// counts as dormant ("there is nothing here to draw or follow"), so a rope
    /// whose endpoints have both gone stops drawing.
    /// </summary>
    private bool EndpointDormant(EntityHandle h)
    {
        return _endpoints is null || !_endpoints.Exists(h) || _endpoints.IsDormant(h);
    }

    private void RunRopeSimulation(float seconds)
    {
        for (int i = 0; i < Segments; i++)
            _linksTouching[i] = false;

        _physics.Simulate(seconds);

        LinksTouchingSomething = 0;
        for (int i = 0; i < Segments; i++)
        {
            if (_linksTouching[i])
                LinksTouchingSomething++;
        }
    }

    private bool InitRopePhysics()
    {
        if ((Flags & RopeFlags.Simulate) == 0)
            return false;

        if (PhysicsInitted)
            return true;

        _prevEndPointValid[0] = GetEndPointPos(0, out _prevEndPointPos[0]);
        if (!_prevEndPointValid[0])
            return false;

        _prevEndPointValid[1] = GetEndPointPos(1, out _prevEndPointPos[1]);
        if (!_prevEndPointValid[1])
            _prevEndPointPos[1] = _prevEndPointPos[0];

        Vector start = _prevEndPointPos[0];
        Vector attached = _prevEndPointPos[1];

        _physics.SetupSimulation(0, this);
        RecomputeSprings();
        _physics.Restart();

        for (int i = 0; i < _physics.NodeCount; i++)
        {
            ref RopeNode node = ref _physics.Node(i);
            float t = (float)i / (_physics.NodeCount - 1);

            node.Pos = RopeMath.Lerp(start, attached, t);
            node.PrevPos = node.Pos;
        }

        if ((Flags & RopeFlags.InitialHang) != 0)
            RunRopeSimulation(5);

        CalcLightValues();

        UpdateBBox();

        TimeToNextGust = _random!.RandomFloat(1.0f, 3.0f);
        PhysicsInitted = true;

        return true;
    }

    private void CalcLightValues()
    {
        Span<Vector> box = stackalloc Vector[6];

        for (int i = 0; i < _physics.NodeCount; i++)
        {
            Vector pos = _physics.Node(i).Predicted;
            Vector light = _world!.ComputeLighting(pos, box);

            if (!_settings.AverageLight)
            {
                float maxIntensity = 0;
                for (int side = 0; side < 6; side++)
                {
                    float len = box[side].Length;
                    maxIntensity = maxIntensity > len ? maxIntensity : len;
                }

                RopeMath.Normalize(ref light);
                light *= maxIntensity;

                float max = light.X > (light.Y > light.Z ? light.Y : light.Z)
                    ? light.X : (light.Y > light.Z ? light.Y : light.Z);
                if (max > 1)
                    light = RopeMath.Div(light, max);
            }

            _lightValues[i] = light;
        }
    }

    private void UpdateBBox()
    {
        Vector start = _physics.Node(0).Pos;
        Vector end = _physics.Node(_physics.NodeCount - 1).Pos;

        Vector mins = RopeMath.Min(start, end);
        Vector maxs = RopeMath.Max(start, end);

        for (int i = 1; i < _physics.NodeCount - 1; i++)
            RopeMath.AddPointToBounds(_physics.Node(i).Pos, ref mins, ref maxs);

        RenderMins = mins - BoundsOrigin;
        RenderMaxs = maxs - BoundsOrigin;
    }

    private bool AnyPointsMoved()
    {
        for (int i = 0; i < _physics.NodeCount; i++)
        {
            ref RopeNode node = ref _physics.Node(i);
            if ((node.Pos - node.PrevPos).LengthSquared > 0.03f)
                return true;
        }

        return --ForcePointMoveCounter > 0;
    }

    private bool DidEndPointMove(int pt)
    {
        if (((int)LockedPoints & (1 << pt)) == 0)
            return false;

        bool oldValid = _prevEndPointValid[pt];
        Vector old = _prevEndPointPos[pt];
        _prevEndPointValid[pt] = GetEndPointPos(pt, out _prevEndPointPos[pt]);

        if (!oldValid && !_prevEndPointValid[pt])
            return true;

        return !RopeMath.AreEqual(old, _prevEndPointPos[pt], 0.1f);
    }

    private bool DetectRestingState(out bool applyWind)
    {
        applyWind = false;

        if (PrevLockedPoints != LockedPoints)
        {
            ForcePointMoveCounter = 10;
            PrevLockedPoints = LockedPoints;
            return false;
        }

        if (NewDataThisFrame)
            return false;

        if (DidEndPointMove(0) || DidEndPointMove(1))
            return false;

        Vector end1 = _physics.Node(0).Pos;
        Vector end2 = _physics.Node(_physics.NodeCount - 1).Pos;

        if ((Flags & RopeFlags.NoWind) == 0)
        {
            float dist = RopeMath.DistanceToLineSegment(_frame.MainViewOrigin, end1, end2);
            if (LinksTouchingSomething < (_physics.NodeCount >> 1))
                applyWind = dist < _settings.WindDist;
        }

        if (!VectorEquals(PreviousImpulse, Impulse))
        {
            PreviousImpulse = Impulse;
            return false;
        }

        return !AnyPointsMoved() && !applyWind && !_settings.Shake;
    }

    private static bool VectorEquals(in Vector a, in Vector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    //-------------------------------------------------------------------------
    // Endpoints.
    //-------------------------------------------------------------------------

    private bool CalculateEndPointAttachment(EntityHandle entity, int attachment, out Vector pos, out Vector angles)
    {
        pos = default;
        angles = default;

        if (_endpoints is null || !_endpoints.Exists(entity))
            return false;

        if (attachment > 0 && _endpoints.GetAttachment(entity, attachment, out pos, out angles))
            return true;

        pos = _endpoints.WorldSpaceCenter(entity);
        angles = _endpoints.AbsAngles(entity);
        return true;
    }

    private void ComputeEndPoint(int pt, bool wantAngles)
    {
        if (_endPointFixed[pt])
        {
            _cachedEndPos[pt] = _fixedEndPointPos[pt];
            if (wantAngles)
                _cachedEndAngles[pt] = Vector.Zero;
            return;
        }

        // A handle that does not resolve leaves the cache as it was, exactly
        // as stock's CalculateEndPointAttachment returning false does.
        if (CalculateEndPointAttachment(_endEntity[pt], _endAttachment[pt], out Vector pos, out Vector angles))
        {
            _cachedEndPos[pt] = pos;
            if (wantAngles)
                _cachedEndAngles[pt] = angles;
        }
    }

    /// <summary><c>GetEndPointPos</c>: cached once per frame; always true.</summary>
    public bool GetEndPointPos(int pt, out Vector pos)
    {
        if (_endPositionsDirty)
        {
            ComputeEndPoint(0, false);
            ComputeEndPoint(1, false);
            _endPositionsDirty = false;
        }

        pos = _cachedEndPos[pt];
        return true;
    }

    private void GetEndPointAttachment(int pt, out Vector pos, out Vector angles)
    {
        if (_endPositionsDirty || _endAnglesDirty)
        {
            ComputeEndPoint(0, true);
            ComputeEndPoint(1, true);
            _endPositionsDirty = false;
            _endAnglesDirty = false;
        }

        pos = _cachedEndPos[pt];
        angles = _cachedEndAngles[pt];
    }

    //-------------------------------------------------------------------------
    // The physics delegate (CPhysicsDelegate).
    //-------------------------------------------------------------------------

    Vector IRopePhysicsHelper.GetNodeForces(Span<RopeNode> nodes, int node)
    {
        Vector accel = default;

        if ((Flags & RopeFlags.NoGravity) == 0)
            accel = Gravity;

        if (!_linksTouching[node] && ApplyWind)
        {
            Vector wind = _frame.Wind;

            if (wind.LengthSquared > 0)
            {
                // VectorMA( *pAccel, WIND_FORCE_FACTOR, vecWindVel, *pAccel ).
                accel = new Vector(accel.X + wind.X * WindForceFactor,
                                   accel.Y + wind.Y * WindForceFactor,
                                   accel.Z + wind.Z * WindForceFactor);
            }
            else if (CurrentGustTimer < CurrentGustLifetime)
            {
                float div = CurrentGustTimer / CurrentGustLifetime;
                float scale = (float)(1 - Math.Cos(div * Math.PI));
                accel += WindDir * scale;
            }
        }

        if (_settings.Shake)
            accel += _random!.RandomVector(-ShakeScale, ShakeScale);

        accel += ImpulseScale * Impulse;
        Impulse *= ImpulseDecay;

        return accel;
    }

    void IRopePhysicsHelper.ApplyConstraints(Span<RopeNode> nodes)
    {
        if (((Flags & RopeFlags.Collide) != 0 && _settings.Collide != 0) || _settings.Collide == 2)
        {
            var hullMin = new Vector(-2, -2, -2);
            var hullMax = new Vector(2, 2, 2);

            for (int i = 0; i < nodes.Length; i++)
            {
                ref RopeNode node = ref nodes[i];

                int iteration;
                for (iteration = 0; iteration < CollideIterations; iteration++)
                {
                    RopeTrace trace = _world!.TraceHull(node.PrevPos, node.Pos, hullMin, hullMax);

                    if (trace.Fraction == 1)
                        break;

                    if (trace.Fraction == 0 || trace.AllSolid || trace.StartSolid)
                    {
                        _linksTouching[i] = true;
                        node.Pos = node.PrevPos;
                        break;
                    }

                    node.Pos -= (node.Pos - node.PrevPos) * CollideSlowFactor;

                    float distBehind = trace.PlaneNormal.Dot(node.Pos) - trace.PlaneDist;
                    // -distBehind + 2.2 is a DOUBLE, multiplied into the normal
                    // through operator*( float ).
                    node.Pos += trace.PlaneNormal * (float)(-distBehind + 2.2);

                    _linksTouching[i] = true;
                }

                if (iteration == CollideIterations)
                    node.Pos = node.PrevPos;
            }
        }

        if ((LockedPoints & RopeLock.StartPoint) != 0)
        {
            GetEndPointAttachment(0, out nodes[0].Pos, out Vector angles);

            if ((LockedPoints & RopeLock.StartDirection) != 0 && nodes.Length > 3)
            {
                Vector forward = MoveVec.AngleForward(angles);
                int falloff = Math.Min(2, nodes.Length - 2);
                LockNodeDirection(nodes, 0, 1, falloff, forward);
            }
        }

        if ((LockedPoints & RopeLock.EndPoint) != 0)
        {
            GetEndPointAttachment(1, out nodes[^1].Pos, out Vector angles);

            if ((LockedPoints & RopeLock.EndDirection) != 0 && nodes.Length > 3)
            {
                Vector forward = MoveVec.AngleForward(angles);
                int falloff = Math.Min(2, nodes.Length - 2);
                LockNodeDirection(nodes, nodes.Length - 1, -1, falloff, forward);
            }
        }
    }

    /// <summary><c>LockNodeDirection</c>, walking from <paramref name="first"/> by <paramref name="parity"/>.</summary>
    private static void LockNodeDirection(Span<RopeNode> nodes, int first, int parity, int falloffNodes,
                                          in Vector idealDir)
    {
        float lockAmount = LockAmount;

        for (int i = 0; i < falloffNodes; i++)
        {
            ref Vector v0 = ref nodes[first + i * parity].Pos;
            ref Vector v1 = ref nodes[first + (i + 1) * parity].Pos;

            Vector dir = v1 - v0;
            float len = dir.Length;

            if (len > 0.0001f)
            {
                dir = RopeMath.Div(dir, len);

                Vector actual = RopeMath.Lerp(dir, idealDir, lockAmount);
                v1 = v0 + actual * len;

                lockAmount *= LockFalloff;
            }
        }
    }

    /// <summary><c>ConstrainNodesBetweenEndpoints</c>.</summary>
    private void ConstrainNodesBetweenEndpoints()
    {
        if (!ConstrainBetweenEndpoints)
            return;

        Vector mid = RopeMath.Div(_cachedEndPos[0] + _cachedEndPos[1], 2.0f);
        Vector normal = mid - _cachedEndPos[0];
        float normalLength = RopeMath.Normalize(ref normal);

        for (int i = 1; i < _physics.NodeCount - 1; i++)
        {
            ref RopeNode node = ref _physics.Node(i);
            node.Pos = ConstrainNode(normal, node.Pos, mid, normalLength);
            node.Predicted = ConstrainNode(normal, node.Predicted, mid, normalLength);
        }
    }

    private static Vector ConstrainNode(in Vector normal, in Vector nodePos, in Vector mid, float normalLength)
    {
        Vector toNode = nodePos - mid;
        Vector projected = toNode.Dot(normal) * normal;

        float toNodeLength = RopeMath.Normalize(ref toNode);
        float projectedLength = RopeMath.Normalize(ref projected);

        if (projectedLength < normalLength + 1.0f)
            return nodePos;

        return mid + toNode * toNodeLength * (normalLength / projectedLength);
    }

    //-------------------------------------------------------------------------
    // Hot reload: the whole rope, so the new build's next think is the one the
    // old build would have run.
    //-------------------------------------------------------------------------

    public void Save(ReloadWriter w)
    {
        _physics.Save(w);
        w.Write((int)Flags); w.Write((int)LockedPoints); w.Write((int)PrevLockedPoints);
        w.Write(Segments); w.Write(RopeLength); w.Write(Slack); w.Write(Width); w.Write(TextureScale);
        w.Write(TextureHeight); w.Write(Subdiv); w.Write(ScrollSpeed); w.Write(CurScroll);
        RopeCarry.Write(w, ColorMod);
        w.Write(ConstrainBetweenEndpoints); w.Write(NewDataThisFrame); w.Write(PhysicsInitted); w.Write(ApplyWind);
        w.Write(ForcePointMoveCounter); w.Write(LinksTouchingSomething);
        RopeCarry.Write(w, Impulse); RopeCarry.Write(w, PreviousImpulse);
        w.Write(CurrentGustTimer); w.Write(CurrentGustLifetime); w.Write(TimeToNextGust);
        RopeCarry.Write(w, WindDir); RopeCarry.Write(w, RenderMins); RopeCarry.Write(w, RenderMaxs);
        RopeCarry.Write(w, BoundsOrigin);
        for (int i = 0; i < RopePhysics.MaxNodes; i++)
        {
            w.Write(_linksTouching[i]);
            RopeCarry.Write(w, _lightValues[i]);
        }
        for (int pt = 0; pt < 2; pt++)
        {
            RopeCarry.Write(w, _cachedEndPos[pt]); RopeCarry.Write(w, _cachedEndAngles[pt]);
            w.Write(_prevEndPointValid[pt]); RopeCarry.Write(w, _prevEndPointPos[pt]);
            w.Write(_endPointFixed[pt]); RopeCarry.Write(w, _fixedEndPointPos[pt]);
            w.Write((int)_endEntity[pt].Value); w.Write(_endAttachment[pt]);
        }
        w.Write(_endPositionsDirty); w.Write(_endAnglesDirty);
    }

    public void Restore(ReloadReader r)
    {
        _physics.Restore(r);
        Flags = (RopeFlags)r.ReadInt(); LockedPoints = (RopeLock)r.ReadInt(); PrevLockedPoints = (RopeLock)r.ReadInt();
        Segments = r.ReadInt(); RopeLength = r.ReadInt(); Slack = r.ReadInt(); Width = r.ReadFloat();
        TextureScale = r.ReadFloat(); TextureHeight = r.ReadInt(); Subdiv = r.ReadInt(); ScrollSpeed = r.ReadFloat();
        CurScroll = r.ReadFloat();
        ColorMod = RopeCarry.Read(r);
        ConstrainBetweenEndpoints = r.ReadBool(); NewDataThisFrame = r.ReadBool(); PhysicsInitted = r.ReadBool();
        ApplyWind = r.ReadBool();
        ForcePointMoveCounter = r.ReadInt(); LinksTouchingSomething = r.ReadInt();
        Impulse = RopeCarry.Read(r); PreviousImpulse = RopeCarry.Read(r);
        CurrentGustTimer = r.ReadFloat(); CurrentGustLifetime = r.ReadFloat(); TimeToNextGust = r.ReadFloat();
        WindDir = RopeCarry.Read(r); RenderMins = RopeCarry.Read(r); RenderMaxs = RopeCarry.Read(r);
        BoundsOrigin = RopeCarry.Read(r);
        for (int i = 0; i < RopePhysics.MaxNodes; i++)
        {
            _linksTouching[i] = r.ReadBool();
            _lightValues[i] = RopeCarry.Read(r);
        }
        for (int pt = 0; pt < 2; pt++)
        {
            _cachedEndPos[pt] = RopeCarry.Read(r); _cachedEndAngles[pt] = RopeCarry.Read(r);
            _prevEndPointValid[pt] = r.ReadBool(); _prevEndPointPos[pt] = RopeCarry.Read(r);
            _endPointFixed[pt] = r.ReadBool(); _fixedEndPointPos[pt] = RopeCarry.Read(r);
            _endEntity[pt] = new EntityHandle((uint)r.ReadInt()); _endAttachment[pt] = r.ReadInt();
        }
        _endPositionsDirty = r.ReadBool(); _endAnglesDirty = r.ReadBool();
    }

    //-------------------------------------------------------------------------
    // GetAttachment (ROPE_ATTACHMENT_START_POINT / END_POINT): the end node and
    // the direction of the segment beside it.
    //-------------------------------------------------------------------------

    /// <summary>The start (1) or end (2) node and the direction it points.</summary>
    public bool GetAttachment(int number, out Vector origin, out Vector forward)
    {
        int n = _physics.NodeCount;
        origin = default;
        forward = default;

        if ((number != 1 && number != 2) || n < 2)
            return false;

        if (number == 1)
        {
            origin = _physics.Node(0).Predicted;
            forward = _physics.Node(0).Predicted - _physics.Node(1).Predicted;
        }
        else
        {
            origin = _physics.Node(n - 1).Predicted;
            forward = _physics.Node(n - 1).Predicted - _physics.Node(n - 2).Predicted;
        }

        return true;
    }
}
