//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Diagnostics;
using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared.Bones;
using NVector = System.Numerics.Vector3;

namespace SourceSharp.Client.Ragdolls;

/// <summary>Why a body was not made. Most models have no jointed body: a refusal is an ordinary answer.</summary>
public enum RagdollRefusal
{
    None = 0,

    /// <summary>There is no physics environment (before level init, or after shutdown).</summary>
    NoEnvironment,

    /// <summary>No model, or no studio header for it.</summary>
    NoModel,

    /// <summary>The .phy has one solid or none: a rigid prop, not a body.</summary>
    NotJointed,

    /// <summary>A static prop, or a model with no sequences: no pose to collapse from.</summary>
    NotPoseable,

    /// <summary>The pose is shorter than the model's bone count.</summary>
    NoBones,

    /// <summary>RagdollCreate built no element.</summary>
    Solver,
}

/// <summary>The pose a body collapses from: what the entity is wearing and where its bones are.</summary>
/// <param name="ModelIndex">The model the entity wears.</param>
/// <param name="Hdr">Its studio header, or null when it has none.</param>
/// <param name="Bones">Bone-to-world, one per bone, as the skeleton posed them; null when it could not.</param>
/// <param name="EntityToWorld">The entity's own transform (origin and angles, no scale): a bone no mask wrote is placed from it.</param>
public readonly record struct RagdollPose(int ModelIndex, StudioHdr? Hdr, BoneMatrix3x4[]? Bones, BoneMatrix3x4 EntityToWorld);

/// <summary>Where a body's entity is posed from.</summary>
public interface IRagdollPoseSource
{
    RagdollPose Pose(EntityHandle owner);

    /// <summary>
    /// The owner is a body now: its bones are the solver's from this moment,
    /// so a pose made ahead of it from its animation (the frame's bone batch)
    /// answers nothing more.
    /// </summary>
    void BecameRagdoll(EntityHandle owner);
}

/// <summary>
/// The owner's render tail, which the physics step writes (plan_entity 6e: "the
/// pose is written into the render tail by the physics step"), and the native
/// shell's one fact about a body: that the solver owns its pose.
/// </summary>
public interface IRagdollRenderTail
{
    /// <summary>The solver's origin and cull box into the owner's tail, and the render transform from them. False for an owner that is gone.</summary>
    bool Write(EntityHandle owner, Vector origin, Vector mins, Vector maxs);

    /// <summary>The shell stops publishing the networked pose over the solver's, and re-leafs the entity.</summary>
    void SetRagdoll(EntityHandle owner, bool isRagdoll);
}

/// <summary>The engine's clock, as the ragdolls read it.</summary>
public interface IRagdollClock
{
    float CurTime { get; }

    int TickCount { get; }
}

/// <summary>What a body needs from the world: the separation's line test, and the sleep timer's convar.</summary>
public interface IRagdollWorld
{
    /// <summary><c>UTIL_TraceRay( Ray( start, end ), MASK_SOLID, owner, COLLISION_GROUP_NONE ).DidHit()</c>.</summary>
    bool TraceSolidHits(Vector start, Vector end, EntityHandle owner);

    /// <summary><c>ragdoll_sleepaftertime</c>.</summary>
    float SleepAfterTime { get; }

    /// <summary><c>developer</c>.</summary>
    int Developer { get; }
}

/// <summary>Native text vphysics may keep: the model names a body's objects are named by.</summary>
public interface IRagdollNames
{
    /// <summary>The model's name as native text that lives until <see cref="ReleaseAll"/>.</summary>
    nint NameOf(string name);

    /// <summary>Every name the pool holds, by its text, for a reload to carry.</summary>
    IReadOnlyDictionary<string, nint> Held { get; }

    /// <summary>The names a previous build made, taken over as they are.</summary>
    void Adopt(string name, nint pointer);

    /// <summary>Every name freed: no object holds one any more.</summary>
    void ReleaseAll();
}

/// <summary>The client's jointed bodies, by owner.</summary>
public interface IClientRagdolls
{
    /// <summary>How many bodies stand.</summary>
    int Count { get; }

    /// <summary>
    /// The entity becomes a jointed body: <c>C_StandaloneEntity::BecomeRagdoll</c>,
    /// and stock's <c>CreateRagdoll</c>. True when it is one (including when it
    /// already was).
    /// </summary>
    bool Become(EntityHandle owner, Vector force, int forceBone, out RagdollRefusal refusal);

    bool IsRagdoll(EntityHandle owner);

    /// <summary>The body's serial (the skeleton's cache key for it), or 0 for none.</summary>
    uint Serial(EntityHandle owner);

    /// <summary>Whether a body stands, where the solver has it, and whether it was made this tick.</summary>
    RagdollState State(EntityHandle owner, out Vector origin);

    /// <summary><c>C_ClientRagdoll::ImpactTrace</c>'s blast arm: element zero pushed, the sleep timer reset.</summary>
    bool ApplyForceCenter(EntityHandle owner, Vector force);

    /// <summary>The owners of every body that stands.</summary>
    IReadOnlyCollection<EntityHandle> Owners { get; }

    /// <summary>
    /// <c>ragdoll_t::list[ physicsBone ].pObject</c>, the element a trace's
    /// <c>physicsbone</c> names (c_stickybolt.cpp's lookup): 0 for an owner
    /// with no body, or a bone outside <c>0 .. listCount - 1</c>.
    /// </summary>
    nint ElementObject(EntityHandle owner, int physicsBone);

    /// <summary>
    /// The solver's bones into <paramref name="bones"/> (<c>RagdollGetBoneMatrix</c>
    /// per element) and the model bones written into <paramref name="owned"/>;
    /// answers how many, or -1 for an owner with no body.
    /// </summary>
    int WriteBones(EntityHandle owner, Span<BoneMatrix3x4> bones, Span<int> owned);

    /// <summary>The body taken apart: the entity went.</summary>
    bool Destroy(EntityHandle owner);

    /// <summary>Every body taken apart, before the environment goes (level shutdown). Answers how many.</summary>
    int DestroyAll();

    /// <summary>The physics step: each moved body's update, then every body's pose into its tail. Answers how many were written.</summary>
    int StepPoses();
}

/// <summary>
/// The client's ragdolls (plan_physics_bones_native_zero Phase 3): what
/// ragdoll_library.cpp held natively -- the bodies, their owners, their
/// creation ticks -- and stock's <c>CRagdoll</c> lifecycle over
/// <see cref="RagdollSolver"/>. An injected service; a reload participant,
/// because the bodies' vphysics objects outlive a <c>cs_cl_reload</c>.
/// </summary>
public sealed class ClientRagdolls(
    IRagdollVPhysics vphysics,
    RagdollSolver solver,
    IRagdollPoseSource poses,
    IRagdollRenderTail tails,
    IRagdollClock clock,
    IRagdollWorld world,
    IRagdollNames names,
    ILog log) : IClientRagdolls, IClientRagdollStep, IReloadParticipant
{
    private readonly Dictionary<EntityHandle, RagdollBody> _bodies = [];
    private uint _serial;

    public int Count => _bodies.Count;

    /// <summary>Bodies made, destroyed and refused since the module loaded, for the reports.</summary>
    public int Created { get; private set; }

    public int Destroyed { get; private set; }

    public int Refused { get; private set; }

    /// <summary>The stepped and the updated, as the last step counted them.</summary>
    public int LastStepped { get; private set; }

    public int LastUpdated { get; private set; }

    public IEnumerable<RagdollBody> Bodies => _bodies.Values;

    /// <summary>Whether every element of <paramref name="body"/> sleeps now, asked of vphysics (the report's).</summary>
    public bool AsleepNow(RagdollBody body) => solver.IsAsleep(body);

    public bool IsRagdoll(EntityHandle owner) => _bodies.ContainsKey(owner);

    public uint Serial(EntityHandle owner) => _bodies.TryGetValue(owner, out RagdollBody? body) ? body.Serial : 0;

    private bool Refuse(RagdollRefusal why, out RagdollRefusal refusal)
    {
        refusal = why;
        Refused++;
        return false;
    }

    public bool Become(EntityHandle owner, Vector force, int forceBone, out RagdollRefusal refusal)
    {
        refusal = RagdollRefusal.None;

        // ALREADY ONE: a second RagdollCreate over a live body leaks every
        // object and constraint in the first.
        if (_bodies.ContainsKey(owner))
            return true;

        nint environment = vphysics.Environment();
        if (environment == 0)
            return Refuse(RagdollRefusal.NoEnvironment, out refusal);

        RagdollPose pose = poses.Pose(owner);
        if (pose.Hdr is not { } hdr || pose.ModelIndex <= 0)
            return Refuse(RagdollRefusal.NoModel, out refusal);

        // A .phy with one solid is a rigid prop: a "ragdoll" of it is a lump.
        RagdollCollide collide = vphysics.Collide(pose.ModelIndex);
        if (collide.IsNone || collide.SolidCount <= 1)
            return Refuse(RagdollRefusal.NotJointed, out refusal);

        // A static prop or a model with no sequences has every bone at one
        // point, and the first constraint solve would fling them apart.
        if (!BoneLibrary.IsPoseable(hdr))
            return Refuse(RagdollRefusal.NotPoseable, out refusal);

        // A .phy may name any bone, and each element is placed straight from it.
        if (pose.Bones is not { } bones || bones.Length < hdr.NumBones || hdr.NumBones <= 0)
            return Refuse(RagdollRefusal.NoBones, out refusal);

        int filled = FillUnwrittenBones(hdr, pose.EntityToWorld, bones);
        if (filled > 0 && world.Developer > 0)
            log.Msg($"[SourceSharp/Client] ragdoll: filled {filled} unmasked bone(s) the .phy may name\n");

        var p = new RagdollParams
        {
            GameData = PhysicsGameData.FromHandle(owner),
            Collide = collide,
            StudioHdr = hdr,
            Name = names.NameOf(hdr.Name),
            ModelIndex = pose.ModelIndex,
            ForceVector = force,
            ForceBoneIndex = forceBone,
            CurrentBones = bones,
            JointFrictionScale = 1f,
        };

        // CRagdoll::Init. No RagdollApplyAnimationAsVelocity: this hierarchy
        // keeps no bone history to take two poses a tick apart from, so the
        // force is the whole of the motion (CRagdollProp::Spawn's choice too).
        var body = new RagdollBody();
        solver.Create(body, p, environment);

        if (!body.IsValid)
        {
            solver.Destroy(body, environment);
            log.Msg($"Bad ragdoll for {hdr.Name}\n");
            return Refuse(RagdollRefusal.Solver, out refusal);
        }

        solver.Activate(body, collide, pose.ModelIndex);

        // It's moving now...
        body.LastOriginChangeTime = clock.CurTime;

        if (vphysics.ModelBounds(pose.ModelIndex, out Vector mins, out Vector maxs))
            solver.BuildBounds(body, mins, maxs);

        body.Owner = owner;
        body.CreateTick = clock.TickCount;
        body.Serial = NextSerial();
        _bodies[owner] = body;
        Created++;

        // The skeleton's pose made ahead from the animation goes now, so the
        // rest of this frame draws the body, as stock's does from the moment
        // BecomeRagdollOnClient made it.
        poses.BecameRagdoll(owner);

        // The first pose now, so a body made between two steps does not draw a
        // frame at the place it was thrown from; then the shell hands its pose
        // to the solver.
        StepPose(body);
        tails.SetRagdoll(owner, true);
        return true;
    }

    private uint NextSerial()
    {
        unchecked { _serial = _serial == uint.MaxValue ? 1u : _serial + 1u; }
        return _serial;
    }

    /// <summary>
    /// ragdoll_library.cpp's <c>RagdollLibrary_FillUnwrittenBones</c>: a bone
    /// whose flags meet no mask is never posed, and a .phy may name it, so it
    /// is placed from its bind pose under its parent (bones come parent first).
    /// </summary>
    public static int FillUnwrittenBones(StudioHdr hdr, in BoneMatrix3x4 entityToWorld, Span<BoneMatrix3x4> bones)
    {
        int count = Math.Min(hdr.NumBones, bones.Length);
        int filled = 0;

        for (int i = 0; i < count; i++)
        {
            if ((hdr.BoneFlags(i) & BoneLibrary.BoneUsedByAnything) != 0)
                continue;

            MstudioBone bone = hdr.Bone(i);
            BoneMatrix3x4 local = BoneSetupMath.QuaternionMatrix(bone.Quaternion, bone.Position);
            int parent = bone.Parent;

            bones[i] = parent >= 0 && parent < count
                ? BoneSetupMath.ConcatTransforms(bones[parent], local)
                : BoneSetupMath.ConcatTransforms(entityToWorld, local);
            filled++;
        }

        return filled;
    }

    public RagdollState State(EntityHandle owner, out Vector origin)
    {
        origin = Vector.Zero;

        if (!_bodies.TryGetValue(owner, out RagdollBody? body))
            return RagdollState.None;

        origin = solver.Origin(body);

        // WasRagdollCreatedOnCurrentTick: a body made this tick already has
        // its creation force in it.
        return body.CreateTick == clock.TickCount ? RagdollState.Present | RagdollState.MadeThisTick : RagdollState.Present;
    }

    public bool ApplyForceCenter(EntityHandle owner, Vector force)
    {
        if (!_bodies.TryGetValue(owner, out RagdollBody? body) || body.ListCount <= 0 || body.List[0].Object == 0)
            return false;

        // apply force at object mass center (VPhysicsGetObject: element zero)
        vphysics.ApplyForceCenter(body.List[0].Object, force);
        RagdollSolver.ResetSleepAfterTime(body, clock.CurTime);
        return true;
    }

    public IReadOnlyCollection<EntityHandle> Owners => _bodies.Keys;

    public nint ElementObject(EntityHandle owner, int physicsBone)
    {
        if (!_bodies.TryGetValue(owner, out RagdollBody? body) || physicsBone < 0 || physicsBone >= body.ListCount)
            return 0;

        return body.List[physicsBone].Object;
    }

    public int WriteBones(EntityHandle owner, Span<BoneMatrix3x4> bones, Span<int> owned)
    {
        if (!_bodies.TryGetValue(owner, out RagdollBody? body))
            return -1;

        long start = Stopwatch.GetTimestamp();
        int written = WriteBones(body, bones, owned);
        WriteTicks += Stopwatch.GetTimestamp() - start;
        Writes++;
        return written;
    }

    private int WriteBones(RagdollBody body, Span<BoneMatrix3x4> bones, Span<int> owned)
    {
        int written = 0;

        for (int i = 0; i < body.ListCount; i++)
        {
            if (body.BoneIndex[i] >= bones.Length || !solver.GetBoneMatrix(body, bones, i))
                continue;

            if (written < owned.Length)
                owned[written] = body.BoneIndex[i];

            written++;
        }

        return written;
    }

    public bool Destroy(EntityHandle owner)
    {
        if (!_bodies.Remove(owner, out RagdollBody? body))
            return false;

        TakeApart(body);
        return true;
    }

    private void TakeApart(RagdollBody body)
    {
        nint environment = vphysics.Environment();

        // ~CRagdoll: collisions off first (a touch callback on a half-deleted
        // body is very bad), then RagdollDestroy. With no environment the
        // objects already went with it.
        if (environment != 0)
        {
            solver.PrepareForDestroy(body);
            solver.Destroy(body, environment);
        }

        Destroyed++;
    }

    public int DestroyAll()
    {
        int n = _bodies.Count;

        foreach (RagdollBody body in _bodies.Values)
            TakeApart(body);

        _bodies.Clear();

        // No object names anything any more.
        names.ReleaseAll();
        return n;
    }

    public int StepPoses()
    {
        LastStepped = LastUpdated = 0;

        if (_bodies.Count == 0)
            return 0;

        long start = Stopwatch.GetTimestamp();
        int stepped = Step();
        StepTicks += Stopwatch.GetTimestamp() - start;
        StepBodyFrames += _bodies.Count;
        return stepped;
    }

    //-------------------------------------------------------------------------
    // The cost meter (plan_physics_bones_perf lane B): the step's and the bone
    // write's time, summed, so cs_cl_ragdolls can say what a body costs a
    // frame. Two timestamps a frame and two a write; never reset by the game.
    //-------------------------------------------------------------------------

    /// <summary>Stopwatch ticks spent in <see cref="StepPoses"/> with at least one body.</summary>
    public long StepTicks { get; private set; }

    /// <summary>Bodies those steps covered, summed: the per-body-frame divisor.</summary>
    public long StepBodyFrames { get; private set; }

    /// <summary>Stopwatch ticks spent in <see cref="WriteBones"/> for a body this table holds.</summary>
    public long WriteTicks { get; private set; }

    /// <summary>Those writes.</summary>
    public long Writes { get; private set; }

    /// <summary>Zeroes the meter (cs_cl_ragdolls reset).</summary>
    public void ResetCost() => StepTicks = StepBodyFrames = WriteTicks = Writes = 0;

    /// <summary>The bodies the last <see cref="ScanMoved"/> found moved, in table order.</summary>
    private readonly List<RagdollBody> _moved = [];

    /// <summary>
    /// game/client/physics.cpp's active-object walk: the bodies an object of
    /// which the last step moved (stock asks the owner per object; the
    /// per-curtime guard makes that once per body). How many; the step and
    /// the cost bench (<c>cs_cl_ragdoll_bench</c>) share it.
    /// </summary>
    public int ScanMoved()
    {
        _moved.Clear();

        nint environment = vphysics.Environment();
        if (environment == 0 || _bodies.Count == 0)
            return 0;

        // The step's own buffer and set, kept: a frame allocates nothing
        // (plan_physics_bones_perf B3).
        int count = vphysics.ActiveObjects(environment, ref _activeBuffer);
        _active.Clear();
        for (int i = 0; i < count; i++)
            _active.Add(_activeBuffer[i]);

        foreach (RagdollBody body in _bodies.Values)
        {
            if (Moved(body, _active))
                _moved.Add(body);
        }

        return _moved.Count;
    }

    private nint[] _activeBuffer = new nint[64];
    private readonly HashSet<nint> _active = [];

    /// <summary>The solver the bodies are stepped by, for the cost bench.</summary>
    public RagdollSolver Solver => solver;

    // The separation pass's trace, made once: it traces for the body being
    // updated, which the step names before each update (one body at a time).
    private EntityHandle _tracing;
    private Func<Vector, Vector, bool>? _trace;

    private bool TraceForUpdatedBody(Vector start, Vector end) => world.TraceSolidHits(start, end, _tracing);

    private int Step()
    {
        nint environment = vphysics.Environment();
        if (environment == 0)
            return 0;

        ScanMoved();

        _trace ??= TraceForUpdatedBody;
        foreach (RagdollBody body in _moved)
        {
            _tracing = body.Owner;
            solver.VPhysicsUpdate(body, clock.CurTime, world.SleepAfterTime, _trace, clock.TickCount);
            LastUpdated++;
        }

        foreach (RagdollBody body in _bodies.Values)
        {
            if (StepPose(body))
                LastStepped++;
        }

        return LastStepped;
    }

    private static bool Moved(RagdollBody body, HashSet<nint> active)
    {
        for (int i = 0; i < body.ListCount; i++)
        {
            if (active.Contains(body.List[i].Object))
                return true;
        }

        return false;
    }

    private bool StepPose(RagdollBody body)
    {
        if (body.ListCount <= 0 || body.List[0].Object == 0)
            return false;

        return tails.Write(body.Owner, solver.Origin(body), body.Mins, body.Maxs);
    }

    //-------------------------------------------------------------------------
    // The reload: the bodies' objects stay in the environment, so the table
    // goes across, pointer for pointer.
    //-------------------------------------------------------------------------

    private static void Write(ReloadWriter w, nint pointer)
    {
        long value = pointer;
        w.Write(unchecked((int)value));
        w.Write(unchecked((int)(value >> 32)));
    }

    private static nint ReadPointer(ReloadReader r)
    {
        uint lo = unchecked((uint)r.ReadInt());
        long hi = r.ReadInt();
        return (nint)((hi << 32) | lo);
    }

    private static void Write(ReloadWriter w, Vector v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    private static Vector ReadVector(ReloadReader r) => new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());

    public void Save(ReloadWriter writer)
    {
        writer.Write(unchecked((int)_serial));
        writer.Write(names.Held.Count);

        foreach ((string name, nint pointer) in names.Held)
        {
            writer.Write(name);
            Write(writer, pointer);
        }

        writer.Write(_bodies.Count);

        foreach (RagdollBody body in _bodies.Values)
        {
            writer.Write(unchecked((int)body.Owner.Value));
            writer.Write(unchecked((int)body.Serial));
            writer.Write(body.CreateTick);
            writer.Write(body.ListCount);
            writer.Write(body.AllowStretch);
            Write(writer, body.Group);

            for (int i = 0; i < body.ListCount; i++)
            {
                RagdollElement e = body.List[i];
                Write(writer, e.OriginParentSpace);
                Write(writer, e.Object);
                Write(writer, e.Constraint);
                writer.Write(e.ParentIndex);
                writer.Write(body.BoneIndex[i]);
            }

            writer.Write(body.AnimFriction.FrictionTimeIn);
            writer.Write(body.AnimFriction.FrictionTimeOut);
            writer.Write(body.AnimFriction.FrictionTimeHold);
            writer.Write(body.AnimFriction.MinAnimatedFriction);
            writer.Write(body.AnimFriction.MaxAnimatedFriction);
            Write(writer, body.Mins);
            Write(writer, body.Maxs);
            writer.Write(body.Radius);
            writer.Write(body.LastUpdate);
            writer.Write(body.AllAsleep);
            Write(writer, body.LastOrigin);
            writer.Write(body.LastOriginChangeTime);
        }
    }

    public void Restore(ReloadReader reader)
    {
        _bodies.Clear();
        _serial = unchecked((uint)reader.ReadInt());

        int nameCount = reader.ReadInt();
        for (int i = 0; i < nameCount; i++)
        {
            string name = reader.ReadString();
            names.Adopt(name, ReadPointer(reader));
        }

        int count = reader.ReadInt();
        for (int b = 0; b < count; b++)
        {
            var body = new RagdollBody
            {
                Owner = new EntityHandle(unchecked((uint)reader.ReadInt())),
                Serial = unchecked((uint)reader.ReadInt()),
                CreateTick = reader.ReadInt(),
                ListCount = reader.ReadInt(),
                AllowStretch = reader.ReadBool(),
                Group = ReadPointer(reader),
            };

            for (int i = 0; i < body.ListCount; i++)
            {
                RagdollElement e = body.List[i];
                e.OriginParentSpace = ReadVector(reader);
                e.Object = ReadPointer(reader);
                e.Constraint = ReadPointer(reader);
                e.ParentIndex = reader.ReadInt();
                body.BoneIndex[i] = reader.ReadInt();
            }

            body.AnimFriction.FrictionTimeIn = reader.ReadFloat();
            body.AnimFriction.FrictionTimeOut = reader.ReadFloat();
            body.AnimFriction.FrictionTimeHold = reader.ReadFloat();
            body.AnimFriction.MinAnimatedFriction = reader.ReadInt();
            body.AnimFriction.MaxAnimatedFriction = reader.ReadInt();
            body.Mins = ReadVector(reader);
            body.Maxs = ReadVector(reader);
            body.Radius = reader.ReadFloat();
            body.LastUpdate = reader.ReadFloat();
            body.AllAsleep = reader.ReadBool();
            body.LastOrigin = ReadVector(reader);
            body.LastOriginChangeTime = reader.ReadFloat();

            _bodies[body.Owner] = body;
        }
    }
}

/// <summary>
/// The ragdolls when vphysics did not answer: no entity becomes a body, and
/// every question about one answers "none".
/// </summary>
public sealed class NoClientRagdolls : IClientRagdolls
{
    public int Count => 0;

    public bool Become(EntityHandle owner, Vector force, int forceBone, out RagdollRefusal refusal)
    {
        refusal = RagdollRefusal.NoEnvironment;
        return false;
    }

    public bool IsRagdoll(EntityHandle owner) => false;

    public uint Serial(EntityHandle owner) => 0;

    public RagdollState State(EntityHandle owner, out Vector origin)
    {
        origin = Vector.Zero;
        return RagdollState.None;
    }

    public bool ApplyForceCenter(EntityHandle owner, Vector force) => false;

    public IReadOnlyCollection<EntityHandle> Owners => [];

    public nint ElementObject(EntityHandle owner, int physicsBone) => 0;

    public int WriteBones(EntityHandle owner, Span<BoneMatrix3x4> bones, Span<int> owned) => -1;

    public bool Destroy(EntityHandle owner) => false;

    public int DestroyAll() => 0;

    public int StepPoses() => 0;
}
