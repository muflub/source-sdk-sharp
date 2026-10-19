//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;

using SourceSharp.Abi;
using SourceSharp.Shared;
using Vector = SourceSharp.Abi.Vector;

namespace SourceSharp.Shared.Anim;

/// <summary>
/// The <see cref="IAnimTarget"/> both sides run: everything the animation
/// state machine asks, answered through engine arms that already exist —
/// the two-sided reads directly (<c>IEngineEntities.GetAngles</c>,
/// <c>IEngineEntityRender.GetModelName/LookupSequence/
/// LookupSequenceByActivity</c>, <c>ModelDescriptor</c> for the per-sequence
/// activity and loop flag), the networked <see cref="PlayerNetState"/> for
/// the movement flags, and everything the <c>SideAuditTests</c> rule keeps
/// out of shared code — the CLIENT-only arms (pose parameters, dormancy,
/// <c>SetAnimLayer</c>) and the write the recording branch reaches — as
/// <see cref="Arms"/> the host passes at construction. <b>No new ABI
/// surface</b> (phase 0.10's rule): where a side has no arm for something,
/// the delegate is null and the answer is the honest no-answer the interface
/// documents — which for the server is exactly what its side's silent
/// defaults already answered (no dials, never dormant, memory-only layers),
/// now without the shared assembly leaning on a slot its side never fills.
///
/// <para>
/// <b>Why a delegate box rather than two classes.</b> The sides differ in
/// which answers are theirs at all — which eyes to trust (the viewer's own
/// view angles versus the server's replicated eye angles), whether sequence
/// ground speed can be measured (client-only), where the base sequence's
/// running cycle comes from (the client's shell clock), how the main
/// sequence reaches the skeleton (<c>PlaySequence</c> with the shell's
/// rate/restart on the client; the server's own <c>EntitySetSequence</c>
/// write, edge-triggered by its host, over there), whether gesture-layer pushes
/// reach a shell or only this table, and the pose dials and dormancy, which
/// have no server arm at all (the divergence rows name each). Each is one
/// nullable function the host passes; branching on a side flag inside the
/// machine would be the two-machines bug the class comment of
/// <see cref="MultiplayerAnimState"/> warns about, and two near-copy
/// classes would drift exactly where the facts are.
/// </para>
///
/// <para>
/// <b>The gesture table lives HERE</b> (the target, not only the machine's
/// scratch): the machine drives it and this class mirrors it so
/// <see cref="GestureLayerState"/> can read it back per slot — the client's
/// <see cref="Arms.GestureLayer"/> also pushes each change to the shell, and
/// the server's stays server-memory only because there is no server layer
/// arm. Mirroring rather than asking the shell: the shell reads back the
/// CLIENT's last push, and asking it what a push did is its own
/// verification, not this one's.
/// </para>
public sealed class EngineAnimTarget : IAnimTarget
{
    /// <summary>What the base sequence's shell hand-over actually is right now (the host's own record is what CurrentSequence answers; this is the shell-side read of the cycle).</summary>
    public delegate float SequenceCycleReader(int sequence);

    /// <summary>
    /// The side's six arms this shared class may not reach itself, because
    /// <c>SideAuditTests</c> forbids the shared assembly leaning on a slot
    /// the other side leaves null: the three pose-parameter crossings, the
    /// dormancy read, the gesture-layer push, and the main sequence's
    /// hand-over to the skeleton (CLIENT_DLL-backed <c>PlaySequence</c> on
    /// the client, the server's own <c>EntitySetSequence</c> write, which
    /// always restarts and so is edge-triggered by the server's host, over
    /// there). A null means the side has no such arm, and every answer the
    /// nulls give is the answer the side's silent engine defaults gave
    /// before the box existed — the divergence rows on file name each.
    /// </summary>
    public sealed record Arms(Func<string, int>? LookupPoseParameter = null,
                              Func<int, float, float>? SetPoseParameter = null,
                              Func<int, float>? GetPoseParameter = null,
                              Func<bool>? IsDormant = null,
                              Action<int, int, float, float, int, bool>? SetAnimLayer = null,
                              Action<int, float, bool>? PlaySequence = null);

    private readonly IEngineEntities _entities;
    private readonly IEngineEntityRender _render;
    private readonly ModelDescriptors? _models;
    private readonly Func<PlayerNetState> _netState;
    private readonly Func<Vector> _eyeAngles;
    private readonly Func<int, float>? _sequenceGroundSpeed;
    private readonly SequenceCycleReader? _sequenceCycle;
    private readonly Arms _arms;

    private readonly GestureMirror[] _gesture = new GestureMirror[GestureSlot.Count];

    private EntityHandle _handle = EntityHandle.None;
    private int _lastPlayed = -1;
    private string _model = string.Empty;

    public EngineAnimTarget(IEngineEntities entities, IEngineEntityRender render,
                            ModelDescriptors? models,
                            Func<PlayerNetState> netState,
                            Func<Vector> eyeAngles,
                            Func<int, float>? sequenceGroundSpeed = null,
                            SequenceCycleReader? sequenceCycle = null,
                            Arms? arms = null)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _render = render ?? throw new ArgumentNullException(nameof(render));
        _models = models;
        _netState = netState ?? throw new ArgumentNullException(nameof(netState));
        _eyeAngles = eyeAngles ?? throw new ArgumentNullException(nameof(eyeAngles));
        _sequenceGroundSpeed = sequenceGroundSpeed;
        _sequenceCycle = sequenceCycle;
        _arms = arms ?? new Arms();
    }

    /// <summary>Point this target at its player. The host sets it when it is bound.</summary>
    public EntityHandle Handle
    {
        get => _handle;
        set
        {
            if (_handle == value)
                return;

            _handle = value;
            _lastPlayed = -1;
            _model = string.Empty;
            ClearPoseParameterCache();
        }
    }

    public void SetHandle(EntityHandle handle) => Handle = handle;

    //-------------------------------------------------------------------------
    // Movement and state — all of it from the net state the side already
    // has, because that is what both sides' copies of the machine must agree
    // on, and what the client's animator has always read.
    //-------------------------------------------------------------------------

    private PlayerNetState State => _netState();

    /// <summary><c>FL_ONGROUND</c> (const.h) — the same bit the client's animator read off the wire.</summary>
    public const uint OnGroundFlag = 1u << 0;

    /// <summary><c>FL_DUCKING</c> is spelled in the net state as <c>Ducked</c>; the raw flag stays here for the report.</summary>
    public const uint DuckingFlag = 1u << 1;

    /// <summary>Stock's WL_Waist.</summary>
    public const int WaterLevelWaist = 3;

    public Vector Velocity => State.Velocity;

    /// <summary>The body yaw the shell holds — <c>IEngineEntities.GetAngles</c>, the wire's own answer.</summary>
    public float BodyYaw => _entities.GetAngles(_handle).Y;

    /// <summary>
    /// False on both sides: neither has a render-angle arm, so the model's
    /// root is drawn at <see cref="BodyYaw"/> and the torso dial is posed
    /// against that (the divergence row on <c>ComputePoseParam_AimYaw</c>).
    /// </summary>
    public bool RootFollowsFeet => false;

    public float MaxSpeed => State.MaxSpeed;

    public bool OnGround => (State.Flags & OnGroundFlag) != 0;

    public bool Ducked => State.Ducked;

    public int WaterLevel => State.WaterLevel;

    public bool Alive => State.Alive;

    /// <summary>
    /// The visibility half of stock's <c>ShouldUpdateAnimState</c>: the
    /// client knows a dormant entity; the server's answer is true (it has no
    /// dormant/NODRAW arm — the divergence the IAnimTarget row names).
    /// </summary>
    public bool ShouldUpdateAnimState => _arms.IsDormant is null || !_arms.IsDormant();

    /// <summary>
    /// The blend the legs run, MEASURED against the model: the 9-way box
    /// when it declares both <c>move_x</c> and <c>move_y</c>, the 8-way
    /// polar blend otherwise. The client can look both names up; the server
    /// has no pose-parameter-name arm (divergence row: the server answers
    /// 8-way, and its pose writes reach no shell anyway until a pose driver
    /// exists).
    /// </summary>
    public LegAnimType LegAnimType =>
        PoseParameterIndex("move_x") >= 0 && PoseParameterIndex("move_y") >= 0
            ? LegAnimType.NineWay
            : LegAnimType.EightWay;

    public bool HasModel => _render.GetModelName(_handle).Length > 0;

    public PlayerPosture? Posture { get; set; }

    //-------------------------------------------------------------------------
    // Sequences.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The last sequence this target was told to play — the machine's own
    /// hand-over is the record of what the body plays, because on both sides
    /// only the machine plays the player's body. -1 before the first.
    /// </summary>
    public int CurrentSequence => _lastPlayed;

    public void PlaySequence(int sequence, float playbackRate, bool restart)
    {
        _arms.PlaySequence?.Invoke(sequence, playbackRate, restart);
        _lastPlayed = sequence;
    }

    /// <summary>
    /// The per-sequence answers come from the model table when the side has
    /// one (both do in the game; tests may pass null): activity by index,
    /// looping by the STUDIO flags, duration as the descriptor carries it.
    /// A sequence out of range reads empty/false/0 — the table's own honest
    /// no-answers.
    /// </summary>
    public string SequenceActivity(int sequence)
    {
        if (_models is null)
        {
            // The fallback for a target without a model table: equality by
            // index, which is what the client's animator compared before the
            // machine existed (an edge-trigger on SEQUENCE). Same sequence →
            // same string, different → different: strictly weaker than the
            // activity compare, never stronger.
            return sequence >= 0 && sequence == _lastPlayed ? "<same>" : $"<seq:{sequence}>";
        }

        var d = _models.For(_handle);

        if (sequence < 0 || sequence >= d.Sequences.Count)
            return string.Empty;

        return d.Sequences[sequence].ActivityName;
    }

    public int SelectWeightedSequence(string activity)
    {
        if (_models is not null)
            return _models.For(_handle).LookupSequenceByActivity(activity);

        return _render.LookupSequenceByActivity(_handle, activity);
    }

    public int LookupSequence(string label) => _render.LookupSequence(_handle, label);

    public float SequenceGroundSpeed(int sequence) =>
        _sequenceGroundSpeed is null ? float.NaN : _sequenceGroundSpeed(sequence);

    public float SequenceDuration(int sequence)
    {
        if (_models is null)
            return 0f;

        var d = _models.For(_handle);

        if (sequence < 0 || sequence >= d.Sequences.Count)
            return 0f;

        return d.Sequences[sequence].DefaultDuration;
    }

    public bool SequenceLooping(int sequence)
    {
        if (_models is null)
            return false;

        var d = _models.For(_handle);

        if (sequence < 0 || sequence >= d.Sequences.Count)
            return false;

        return (d.Sequences[sequence].Flags & Bones.StudioConstants.STUDIO_LOOPING) != 0;
    }

    public float SequenceCycle(int sequence) =>
        _sequenceCycle is null || sequence != _lastPlayed
            ? float.NaN
            : _sequenceCycle(sequence);

    //-------------------------------------------------------------------------
    // Pose parameters. The index cache belongs to the target, keyed by model
    // name — the model can change under the entity without the handle doing
    // so, and an index from the old model is worse than -1.
    //-------------------------------------------------------------------------

    private readonly Dictionary<string, int> _poseIndex = new(StringComparer.Ordinal);
    private string _poseIndexFor = string.Empty;

    private void EnsurePoseParameterCache()
    {
        string model = _render.GetModelName(_handle);

        if (model == _poseIndexFor && model.Length > 0)
            return;

        ClearPoseParameterCache();
        _poseIndexFor = model;
        _model = model;
    }

    /// <summary>Forget the pose-parameter name cache (a new model, or a test re-pointing the handle).</summary>
    public void ClearPoseParameterCache()
    {
        _poseIndex.Clear();
        _poseIndexFor = string.Empty;
    }

    private int PoseParameterIndex(string name)
    {
        EnsurePoseParameterCache();

        if (_poseIndex.TryGetValue(name, out int known))
            return known;

        int index = _arms.LookupPoseParameter is { } ask ? ask(name) : -1;
        _poseIndex[name] = index;
        return index;
    }

    public int LookupPoseParameter(string name) => PoseParameterIndex(name);

    public float SetPoseParameter(int index, float value)
    {
        if (index < 0)
            return 0f;

        return _arms.SetPoseParameter is { } write && index >= 0 ? write(index, value) : 0f;
    }

    public float GetPoseParameter(int index) =>
        index >= 0 && _arms.GetPoseParameter is { } read ? read(index) : 0f;

    //-------------------------------------------------------------------------
    // Gesture slots — the mirrored table (see the class comment).
    //-------------------------------------------------------------------------

    private struct GestureMirror
    {
        public int Sequence;
        public float Weight;
        public float Cycle;
        public float PlaybackRate;
        public int Order;
        public bool Filled;
    }

    public void SetGestureLayer(int slot, int sequence, float weight, float playbackRate,
                                float cycle, int order, bool restart)
    {
        if (slot < 0 || slot >= GestureSlot.Count)
            return;

        _gesture[slot] = new GestureMirror
        {
            Sequence = sequence,
            Weight = weight,
            Cycle = cycle,
            PlaybackRate = playbackRate,
            Order = order,
            Filled = true,
        };

        // The push is the host's (CLIENT-armed on the client; the server's
        // Arms leave it null, which is exactly the divergence the class
        // comment records — its mirror above is the whole answer there).
        // cycle rides along in the mirror: the machine's bookkeeping clock,
        // seeded on restart, is what the shell starts its own clock from.
        _arms.SetAnimLayer?.Invoke(slot, sequence, weight, playbackRate, order, restart);
    }

    public void ClearGestureLayer(int slot)
    {
        if (slot < 0 || slot >= GestureSlot.Count)
            return;

        _gesture[slot] = default;
        _arms.SetAnimLayer?.Invoke(slot, -1, 0f, 1f, slot, true);
    }

    /// <summary>What the target's mirror holds for one slot (the read-back; the shell's answer is a different check, with its own commands).</summary>
    public (int Sequence, float Weight, float Cycle, bool Filled) GestureLayerState(int slot)
    {
        if (slot < 0 || slot >= GestureSlot.Count)
            return (-1, 0f, 0f, false);

        var g = _gesture[slot];
        return (g.Sequence, g.Weight, g.Cycle, g.Filled);
    }

    /// <summary>The eyes this side trusts: the host's delegate — the viewer's own view angles for the local player, the server's replicated eye angles on the server, the body yaw on a remote client entity until the eye angles ride the wire.</summary>
    public Vector EyeAngles => _eyeAngles();

    /// <summary>The model name this target last saw (reports).</summary>
    public string ModelName => _model.Length > 0 ? _model : _render.GetModelName(_handle);
}
