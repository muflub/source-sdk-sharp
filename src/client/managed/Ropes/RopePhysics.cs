//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Ropes;

/// <summary>One verlet node: where it is, where it was one step ago, and
/// where it is drawn this frame (the two blended by the step remainder).</summary>
public struct RopeNode
{
    public Vector Pos;
    public Vector PrevPos;
    public Vector Predicted;
}

/// <summary>
/// What the spring solver asks of the rope it belongs to: the force on each
/// node, and the rope's own constraints after the springs (collision, locked
/// endpoints).
/// </summary>
public interface IRopePhysicsHelper
{
    Vector GetNodeForces(Span<RopeNode> nodes, int node);

    void ApplyConstraints(Span<RopeNode> nodes);
}

/// <summary>
/// The fixed-step verlet integrator and the distance-spring solver a rope runs
/// on: <c>CSimplePhysics</c> (simple_physics.cpp) and <c>CBaseRopePhysics</c>
/// (rope_physics.cpp), as one class because nothing else uses either.
///
/// <para>
/// Every expression keeps the stock types, because the acceptance is a
/// bit-exact replay (<c>rope-stock.tsv</c>): the predicted time is a DOUBLE
/// and the step a FLOAT, <c>GetCurTime</c> is a float product widened, and the
/// interpolant is computed in double and narrowed once.
/// </para>
/// </summary>
public sealed class RopePhysics
{
    /// <summary><c>ROPE_MAX_SEGMENTS</c> (rope_shared.h): the most nodes a rope has.</summary>
    public const int MaxNodes = 10;

    // static float flEnergy = 0.98 (rope_physics.cpp).
    private const float Damping = 0.98f;

    // static int nIterations = 3 (rope_physics.cpp).
    private const int ConstraintIterations = 3;

    private readonly RopeNode[] _nodes = new RopeNode[MaxNodes];
    private readonly float[] _nodeSpringDistsSqr = new float[MaxNodes - 1];

    private int _nodeCount;
    private float _springDist = 1f;
    private float _springDistSqr = 1f;
    private IRopePhysicsHelper? _delegate;

    // CSimplePhysics.
    private double _predictedTime;
    private int _curTimeStep;
    private float _timeStep;
    private float _timeStepMul;

    public RopePhysics()
    {
        // CSimplePhysics' own constructor, then CBaseRopePhysics': Restart,
        // nodes zeroed, the full node count, no delegate.
        Init(1.0f / 30.0f);
        Restart();
        SetNumNodes(MaxNodes);
    }

    public int NodeCount => _nodeCount;

    public Span<RopeNode> Nodes => _nodes.AsSpan(0, _nodeCount);

    public ref RopeNode Node(int i) => ref _nodes[i];

    public float SpringLength => _springDist;

    public IRopePhysicsHelper? Delegate => _delegate;

    private void Init(float timeStep)
    {
        _predictedTime = 0;
        _curTimeStep = 0;
        _timeStep = timeStep;
        _timeStepMul = _timeStep * _timeStep * 0.5f;
    }

    /// <summary><c>Restart</c>: fifty steps a second, from time zero.</summary>
    public void Restart() => Init((float)(1.0 / 50));

    public void SetNumNodes(int nodes)
    {
        _nodeCount = nodes;

        for (int i = 0; i < NumSprings; i++)
            _nodeSpringDistsSqr[i] = _springDistSqr / NumSprings;
    }

    private int NumSprings => _nodeCount - 1;

    public void ResetSpringLength(float springDist)
    {
        _springDist = MathF.Max(springDist, 0f);
        _springDistSqr = _springDist * _springDist;

        for (int i = 0; i < NumSprings; i++)
            _nodeSpringDistsSqr[i] = _springDistSqr / NumSprings;
    }

    public void ResetNodeSpringLength(int startNode, float springDist)
        => _nodeSpringDistsSqr[startNode] = springDist * springDist;

    public void SetupSimulation(float springDist, IRopePhysicsHelper? helper)
    {
        ResetSpringLength(springDist);
        SetDelegate(helper);
    }

    public void SetDelegate(IRopePhysicsHelper? helper) => _delegate = helper;

    /// <summary>Everything the next step reads, for a client reload.</summary>
    public void Save(ReloadWriter w)
    {
        w.Write(_nodeCount);
        for (int i = 0; i < MaxNodes; i++)
        {
            RopeCarry.Write(w, _nodes[i].Pos);
            RopeCarry.Write(w, _nodes[i].PrevPos);
            RopeCarry.Write(w, _nodes[i].Predicted);
        }
        for (int i = 0; i < MaxNodes - 1; i++)
            w.Write(_nodeSpringDistsSqr[i]);
        w.Write(_springDist);
        w.Write(_springDistSqr);
        long bits = BitConverter.DoubleToInt64Bits(_predictedTime);
        w.Write((int)bits);
        w.Write((int)(bits >> 32));
        w.Write(_curTimeStep);
        w.Write(_timeStep);
        w.Write(_timeStepMul);
    }

    /// <summary>Reads <see cref="Save"/> back; the delegate is the caller's to set.</summary>
    public void Restore(ReloadReader r)
    {
        _nodeCount = r.ReadInt();
        for (int i = 0; i < MaxNodes; i++)
        {
            _nodes[i].Pos = RopeCarry.Read(r);
            _nodes[i].PrevPos = RopeCarry.Read(r);
            _nodes[i].Predicted = RopeCarry.Read(r);
        }
        for (int i = 0; i < MaxNodes - 1; i++)
            _nodeSpringDistsSqr[i] = r.ReadFloat();
        _springDist = r.ReadFloat();
        _springDistSqr = r.ReadFloat();
        long lo = (uint)r.ReadInt();
        long hi = r.ReadInt();
        _predictedTime = BitConverter.Int64BitsToDouble(lo | (hi << 32));
        _curTimeStep = r.ReadInt();
        _timeStep = r.ReadFloat();
        _timeStepMul = r.ReadFloat();
    }

    /// <summary><c>CBaseRopePhysics::Simulate</c> into <c>CSimplePhysics::Simulate</c>.</summary>
    public void Simulate(float dt)
    {
        Span<RopeNode> nodes = Nodes;

        _predictedTime += dt;
        int newTimeStep = (int)Math.Ceiling(_predictedTime / _timeStep);
        int timeSteps = newTimeStep - _curTimeStep;

        for (int step = 0; step < timeSteps; step++)
        {
            for (int i = 0; i < nodes.Length; i++)
            {
                Vector accel = _delegate is null ? Vector.Zero : _delegate.GetNodeForces(nodes, i);

                ref RopeNode node = ref nodes[i];
                Vector prev = node.Pos;
                node.Pos = node.Pos + (node.Pos - node.PrevPos) * Damping + accel * _timeStepMul;
                node.PrevPos = prev;
            }

            ApplyConstraints(nodes);
        }

        _curTimeStep = newTimeStep;

        // GetCurTime() is m_flTimeStep * m_iCurTimeStep: a FLOAT product,
        // widened on return.
        double curTime = _timeStep * _curTimeStep;
        float interpolant = (float)((_predictedTime - (curTime - _timeStep)) / _timeStep);

        for (int i = 0; i < nodes.Length; i++)
        {
            ref RopeNode node = ref nodes[i];
            node.Predicted = RopeMath.Lerp(node.PrevPos, node.Pos, interpolant);
        }
    }

    private void ApplyConstraints(Span<RopeNode> nodes)
    {
        for (int iteration = 0; iteration < ConstraintIterations; iteration++)
        {
            for (int i = 0; i < NumSprings; i++)
            {
                Vector to = nodes[i].Pos - nodes[i + 1].Pos;
                float distSqr = to.LengthSquared;

                float springDist = _springDistSqr;
                if (springDist == 0f)
                    springDist = _nodeSpringDistsSqr[i];

                if (distSqr > springDist)
                {
                    float dist = MathF.Sqrt(distSqr);
                    to *= 1 - (_springDist / dist);
                    nodes[i].Pos -= to * 0.5f;
                    nodes[i + 1].Pos += to * 0.5f;
                }
            }

            _delegate?.ApplyConstraints(nodes);
        }
    }
}
