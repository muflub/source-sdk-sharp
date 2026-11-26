using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared.Bones;

using NVector = System.Numerics.Vector3;

namespace SourceSharp.Server;

/// <summary><see cref="IStandaloneBlocks"/> over the engine: one crossing a read.</summary>
public sealed class EngineStandaloneBlocks(IEngineEntities entities) : IStandaloneBlocks
{
    public Span<byte> Block(EntityHandle entity) => entities.GetEngineBlock(entity);
}

/// <summary>
/// Where an attachment is on a model another entity carries bone-merged, as
/// the SDK's server answers it for a weapon that follows its carrier.
/// </summary>
public interface IMergedAttachments
{
    /// <summary>
    /// <paramref name="follower"/>'s one-based <paramref name="attachment"/>,
    /// its skeleton bone-merged onto <paramref name="source"/>'s. False, with
    /// the follower's own origin, when either has no skeleton or the follower's
    /// model carries no such attachment (CBaseAnimating::GetAttachment's
    /// failure answer).
    /// </summary>
    bool GetMergedAttachment(EntityHandle follower, EntityHandle source, int attachment, out Vector origin);
}

/// <summary>
/// A skeleton's animation as the lag compensation records it and puts it
/// back: the base sequence, its cycle, whether the cycle was held, and the
/// pose parameters. The server's skeletons carry no overlay layers, so there
/// are none here (docs/divergences.md).
/// </summary>
public sealed class SkeletonAnimation
{
    /// <summary><c>GetSequence()</c>.</summary>
    public int Sequence;

    /// <summary><c>GetCycle()</c>: the base sequence's cycle when it was read.</summary>
    public float Cycle;

    /// <summary>A cycle a <c>SetCycle</c> held, or null for one the clock runs.</summary>
    public float? HeldCycle;

    /// <summary><c>GetPoseParameter( i )</c> for every slot.</summary>
    public readonly float[] PoseParameters = new float[BoneEntityState.MaxPoseParameters];
}

/// <summary>
/// What the lag compensation asks of a skeleton: its animation read, set for
/// the rewind and put back, and its bone cache flushed
/// (<c>sv_lagflushbonecache</c>'s <c>InvalidateBoneCache</c>).
/// </summary>
public interface ISkeletonAnimationRewind
{
    /// <summary>Reads <paramref name="entity"/>'s animation into <paramref name="into"/>; false for no skeleton.</summary>
    bool Capture(EntityHandle entity, SkeletonAnimation into);

    /// <summary>
    /// <c>SetSequence</c>, <c>SetCycle</c> and <c>SetPoseParameter</c> for the
    /// rewind: the cycle is held where it is put until the restore.
    /// </summary>
    void Pose(EntityHandle entity, int sequence, float cycle, ReadOnlySpan<float> poseParameters);

    /// <summary>Puts back what <see cref="Capture"/> read, the cycle's clock included.</summary>
    void Restore(EntityHandle entity, SkeletonAnimation saved);

    /// <summary><c>InvalidateBoneCache()</c>.</summary>
    void InvalidateBoneCache(EntityHandle entity);
}

/// <summary>
/// THE SERVER'S SKELETONS, IN MANAGED CODE (plan physics_bones_native_zero
/// steps 1.1-1.2).
///
/// <para>
/// CStandaloneEntity used to pose itself: EnsureBones, the attachment reads,
/// SetPoseSequence, PoseCycle and TestHitboxes were C++ over bone_library.cpp,
/// with the bone cache, the pose memory and the sequence clock as native
/// members. All of that is here now -- <see cref="ServerBoneDriver"/> decides,
/// <see cref="BoneEntityStates"/> remembers, <see cref="StudioModels"/> reads
/// the models -- and native keeps only the ICollideable virtual the engine's
/// traces call, which hands its ray and its trace to <see cref="TestHitboxes"/>
/// through IManagedBones.
/// </para>
///
/// <para>
/// <b>Every managed bone question costs one crossing</b>: the engine-block read
/// that says which model the entity wears and where it stands (and which is
/// also how "not a standalone entity" is told). The skeleton itself is built
/// and kept on this side. The engine's own hitbox trace costs one crossing too,
/// the other way, and reads the block it is handed.
/// </para>
///
/// <para>
/// <b>The sequence clock lives in the block as well as here.</b> The block's
/// Client.Sequence / PlaybackRate / AnimTime were the native skeleton's clock
/// (shell_standalone.h: m_nPoseSequence and its two partners moved there in
/// step 6d), so every write lands in both, and an entity this composition has
/// not seen yet -- the first ask after a hot reload -- takes its clock from the
/// block and re-reads its duration from the model, as SetPoseSequence did.
/// </para>
///
/// <para>
/// <b>The model scale has no block word</b>, so it rides the ReloadStore
/// instead (<see cref="ISaveCustom"/>, 2026-10-12): each skeleton whose scale
/// is not 1 is written out, and the first ask after the reload takes it with
/// the clock from the block. Stock saves m_flModelScale (baseanimating.cpp's
/// DATADESC); before this a scaled entity came back built at 1 until its
/// scale was set again. The pose parameters ride with it (stock saves
/// m_flPoseParameter too): each that is not its model's default.
/// </para>
///
/// <para>
/// <b>The pose parameters are the server's own</b> (plan_descent_logic CR11):
/// what game code sets through IEngineEntityRender is stored here and the
/// hitbox trace poses from it, as stock's SetupBones reads
/// GetPoseParameterArray. A set only stores; the bones are built when an ask
/// wants them.
/// </para>
/// </summary>
public sealed class ServerSkeletons : IServerStandaloneSkeletons, IStandaloneHitboxes, IGameLifecycle, ISaveCustom, IMergedAttachments,
                                      ISkeletonAnimationRewind
{
    private readonly IStandaloneBlocks _blocks;
    private readonly StudioModels _models;
    private readonly IGameClock _clock;
    private readonly ILog _log;
    private readonly AnimThreeWayBlendConVar? _threeWayBlend;
    private readonly IFwdPhysicsSurfaceProps _surfaceProps;
    private readonly ServerBoneDriver _driver;
    private readonly BoneEntityStates _states = new();
    private readonly Dictionary<string, int> _surfaceIndices = new(StringComparer.Ordinal);
    private readonly SurfacePropReader _surfaceReader;

    /// <summary>What a reload carried, for skeletons not yet asked about in this composition.</summary>
    private readonly Dictionary<EntityHandle, CarriedSkeleton> _carried = [];

    /// <summary>
    /// One skeleton's carry across a hot reload: its scale, and the poses that
    /// differ from its model's defaults (<see cref="float.NaN"/> for one that
    /// does not), with the model they were set on.
    /// </summary>
    private sealed class CarriedSkeleton
    {
        public float Scale = 1.0f;
        public int PoseModel = -1;
        public float[]? Poses;
    }

    public ServerSkeletons(IStandaloneBlocks blocks, StudioModels models, IGameClock clock, ILog log,
                           IFwdPhysicsSurfaceProps surfaceProps, AnimThreeWayBlendConVar? threeWayBlend = null,
                           Func<EntityHandle, ICustomTraceTarget?>? customTargets = null)
    {
        _customTargets = customTargets;
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _threeWayBlend = threeWayBlend;
        _surfaceProps = surfaceProps ?? throw new ArgumentNullException(nameof(surfaceProps));
        _driver = new ServerBoneDriver(models);
        _surfaceReader = SurfaceIndexOf;
    }

    /// <inheritdoc/>
    public int LifecycleOrder => (int)LifecycleStage.Skeletons;

    /// <summary>The per-entity states, for the facts and the report.</summary>
    public BoneEntityStates States => _states;

    /// <summary>The model cache the skeletons read.</summary>
    public StudioModels Models => _models;

    // ---- the cost report (plan step 1.3) ------------------------------------

    /// <summary>Every ask that could build a skeleton.</summary>
    public long Asks { get; private set; }

    /// <summary>How many of those asks built one.</summary>
    public long Builds { get; private set; }

    /// <summary><see cref="Stopwatch"/> ticks spent inside those asks.</summary>
    public long AskTicks { get; private set; }

    /// <summary>The engine's hitbox traces (IManagedBones::TestHitboxes).</summary>
    public long HitboxTraces { get; private set; }

    /// <summary>The engine's traces an entity answered itself (<see cref="ICustomTraceTarget"/>).</summary>
    public long CustomTraces { get; private set; }

    /// <summary>The pose asks (look up, set, get, ground speed): the dials the players' animation state turns.</summary>
    public long PoseAsks { get; private set; }

    /// <summary><see cref="Stopwatch"/> ticks spent inside those pose asks.</summary>
    public long PoseTicks { get; private set; }

    private readonly Func<EntityHandle, ICustomTraceTarget?>? _customTargets;

    /// <summary>The cost counters back to zero.</summary>
    public void ResetCost()
    {
        Asks = 0;
        Builds = 0;
        AskTicks = 0;
        HitboxTraces = 0;
        PoseAsks = 0;
        PoseTicks = 0;
    }

    private BoneConVars ConVars => new(_threeWayBlend?.Value ?? true, JiggleBones.DefaultFramerateCutoff, 0);

    // ---- the block ------------------------------------------------------------

    private static bool Read(Span<byte> block, out EngineBlock read)
    {
        if (block.Length < EngineBlock.SizeBytes)
        {
            read = default;
            return false;
        }

        read = MemoryMarshal.Read<EngineBlock>(block);
        return true;
    }

    private static ServerBonePose PoseOf(in EngineBlock block) => new()
    {
        ModelIndex = block.Head.ModelIndex,
        AbsOrigin = new NVector(block.Head.Origin.X, block.Head.Origin.Y, block.Head.Origin.Z),
        AbsAngles = new BoneAngle(block.Head.Angles.Pitch, block.Head.Angles.Yaw, block.Head.Angles.Roll),
    };

    /// <summary>
    /// The entity's state, made on first ask with its clock taken from the
    /// block (see the class summary).
    /// </summary>
    private BoneEntityState StateOf(EntityHandle entity, in EngineBlock block)
    {
        if (_states.TryGet(entity, out BoneEntityState? held) && held is not null)
        {
            return held;
        }

        BoneEntityState state = _states.GetOrCreate(entity);

        if (_carried.Remove(entity, out CarriedSkeleton? carried))
        {
            state.SetModelScale(carried.Scale);

            // Poses set on the model the entity still wears; a model changed
            // since keeps the new one's defaults, as a SetModel would.
            if (carried.Poses is { } poses && carried.PoseModel == block.Head.ModelIndex)
            {
                _driver.RefreshModelPoseState(state, carried.PoseModel);

                for (int i = 0; i < poses.Length; i++)
                {
                    if (!float.IsNaN(poses[i]))
                    {
                        state.PoseParameter[i] = poses[i];
                    }
                }

                PoseMoved(state);
            }
        }

        state.Sequence = block.Client.Sequence;
        state.PlaybackRate = block.Client.PlaybackRate;
        state.AnimTime = block.Client.AnimTime;

        // A clock that is not the constructor's (sequence 0, rate 1, never
        // started) was set by SetPoseSequence, which read the duration and
        // the loop flag at the model's own defaults: so does this.
        if (state.ClockStarted)
        {
            StudioHdr? hdr = _models.OpenStudioHdr(block.Head.ModelIndex);

            if (hdr is not null)
            {
                BoneLibrary.SequenceFacts(hdr, state.Sequence, null, out state.SeqDuration, out state.SeqLooping);
            }
        }

        return state;
    }

    private bool Find(EntityHandle entity, out EngineBlock block, out BoneEntityState state)
    {
        if (!Read(_blocks.Block(entity), out block))
        {
            state = null!;
            return false;
        }

        state = StateOf(entity, block);
        return true;
    }

    private static void WriteClock(Span<byte> block, BoneEntityState state)
    {
        if (block.Length < EngineBlock.SizeBytes)
        {
            return;
        }

        ref EngineBlock target = ref MemoryMarshal.AsRef<EngineBlock>(block);
        target.Client.Sequence = state.Sequence;
        target.Client.PlaybackRate = state.PlaybackRate;
        target.Client.AnimTime = state.ClockStart;
    }

    // ---- the cost wrapper -----------------------------------------------------

    private static bool SameKey(in BoneCacheKey a, in BoneCacheKey b) =>
        MemoryMarshal.AsBytes(new ReadOnlySpan<BoneCacheKey>(in a))
            .SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<BoneCacheKey>(in b)));

    private long Begin(BoneEntityState state, out BoneCacheKey before)
    {
        before = state.CacheKey;
        return Stopwatch.GetTimestamp();
    }

    private void End(BoneEntityState state, long started, in BoneCacheKey before)
    {
        AskTicks += Stopwatch.GetTimestamp() - started;
        Asks++;

        if (!SameKey(before, state.CacheKey))
        {
            Builds++;
        }
    }

    // ---- IStandaloneSkeletons -------------------------------------------------

    /// <inheritdoc/>
    public bool GetAttachment(EntityHandle entity, int attachment, out Vector origin, out QAngle angles,
                              out bool standalone)
    {
        origin = default;
        angles = default;

        if (!Find(entity, out EngineBlock block, out BoneEntityState state))
        {
            standalone = false;
            return false;
        }

        standalone = true;
        ServerBonePose pose = PoseOf(block);

        long started = Begin(state, out BoneCacheKey before);
        bool found = _driver.GetAttachment(state, pose, attachment, _clock.Now, ConVars,
                                           out NVector o, out BoneAngle a);
        End(state, started, before);

        origin = new Vector(o.X, o.Y, o.Z);
        angles = new QAngle(a.Pitch, a.Yaw, a.Roll);
        return found;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// SetupBones' BuildMatricesWithBoneMerge arm (baseanimating.cpp): the
    /// follower is placed where FollowEntity puts it, on the source; each of
    /// its bones the source's model names takes the source's matrix, and any
    /// other is its own pose under its parent as merged.
    /// </remarks>
    public bool GetMergedAttachment(EntityHandle follower, EntityHandle source, int attachment, out Vector origin)
    {
        origin = default;

        if (!Find(follower, out EngineBlock followerBlock, out BoneEntityState own))
        {
            return false;
        }

        ServerBonePose ownPose = PoseOf(followerBlock);
        origin = new Vector(ownPose.AbsOrigin.X, ownPose.AbsOrigin.Y, ownPose.AbsOrigin.Z);

        if (!Find(source, out EngineBlock sourceBlock, out BoneEntityState carrier))
        {
            return false;
        }

        ServerBonePose sourcePose = PoseOf(sourceBlock);
        StudioHdr? ownHdr = _models.OpenStudioHdr(ownPose.ModelIndex);
        StudioHdr? sourceHdr = _models.OpenStudioHdr(sourcePose.ModelIndex);

        if (ownHdr is null || sourceHdr is null || attachment < 1 || attachment > ownHdr.NumGlobalAttachments)
        {
            return false;
        }

        ownPose.AbsOrigin = sourcePose.AbsOrigin;
        ownPose.AbsAngles = sourcePose.AbsAngles;

        long started = Begin(own, out BoneCacheKey before);
        bool built = _driver.EnsureBones(carrier, sourcePose, BoneLibrary.BoneUsedByAnything, _clock.Now, ConVars)
                   & _driver.EnsureBones(own, ownPose, BoneLibrary.BoneUsedByAnything, _clock.Now, ConVars);
        End(own, started, before);

        int bones = own.CachedBones.Length;

        if (!built || bones <= 0)
        {
            return false;
        }

        Span<short> map = stackalloc short[bones];
        BoneLibrary.BuildMergeMap(ownHdr, sourceHdr, map);

        var merged = (BoneMatrix3x4[])own.CachedBones.Clone();
        BoneLibraryMath.ApplyMergedBones(map, carrier.CachedBones, merged);
        BoneLibraryMath.ReparentUnmergedBones(ownHdr, map, own.CachedBones, merged);

        int bone = ownHdr.GetAttachmentBone(attachment - 1);

        if (bone < 0 || bone >= bones)
        {
            return false;
        }

        MstudioAttachment local1 = ownHdr.Attachment(attachment - 1);
        BoneMatrix3x4 world = BoneLibraryMath.ComposeAttachment(
            merged[bone], local1.Local, (local1.Flags & BoneLibrary.AttachmentFlagWorldAlign) != 0);

        origin = new Vector(world.Position.X, world.Position.Y, world.Position.Z);
        return true;
    }

    /// <inheritdoc/>
    public int LookupAttachment(EntityHandle entity, string name)
    {
        if (string.IsNullOrEmpty(name) || !Read(_blocks.Block(entity), out EngineBlock block))
        {
            return 0;
        }

        return Math.Max(0, BoneLibrary.LookupAttachment(_models.OpenStudioHdr(block.Head.ModelIndex), name));
    }

    /// <inheritdoc/>
    public int LookupBone(EntityHandle entity, string name)
    {
        if (string.IsNullOrEmpty(name) || !Read(_blocks.Block(entity), out EngineBlock block))
        {
            return -1;
        }

        // Stock's LookupBone spelling: the ZERO-based index, or the -1 of a
        // name the model does not carry.
        return BoneSetupQuery.BoneIndexByName(_models.OpenStudioHdr(block.Head.ModelIndex), name);
    }

    /// <inheritdoc/>
    public bool GetBonePosition(EntityHandle entity, int bone, out Vector origin)
    {
        origin = default;

        if (!Find(entity, out EngineBlock block, out BoneEntityState state))
        {
            return false;
        }

        ServerBonePose pose = PoseOf(block);
        StudioHdr? hdr = _models.OpenStudioHdr(pose.ModelIndex);

        if (hdr is null)
        {
            return false;
        }

        // The same build BoneMatrices makes and GetAttachment reads through:
        // everything a renderer would ask, at the clock's now.
        long started = Begin(state, out BoneCacheKey before);
        bool built = _driver.EnsureBones(state, pose, BoneLibrary.BoneUsedByAnything, _clock.Now, ConVars);
        End(state, started, before);

        if (!built || bone < 0 || bone >= state.CachedBones.Length)
        {
            return false;
        }

        // Stock's GetBonePosition: the bone-to-world matrix's origin.
        NVector position = state.CachedBones[bone].Position;
        origin = new Vector(position.X, position.Y, position.Z);
        return true;
    }

    /// <inheritdoc/>
    public int CachedBoneCount(EntityHandle entity) =>
        _states.TryGet(entity, out BoneEntityState? state) && state is not null ? state.CachedBones.Length : 0;

    /// <inheritdoc/>
    public StandaloneBones? BoneMatrices(EntityHandle entity)
    {
        if (!Find(entity, out EngineBlock block, out BoneEntityState state))
        {
            return null;
        }

        ServerBonePose pose = PoseOf(block);
        StudioHdr? hdr = _models.OpenStudioHdr(pose.ModelIndex);

        if (hdr is null)
        {
            return null;
        }

        // The renderer's mask, as SourceSharp_StandaloneBoneMatrices forced it:
        // the cache is whatever the last ask left, and empty for an entity
        // nothing has asked about.
        long started = Begin(state, out BoneCacheKey before);
        bool built = _driver.EnsureBones(state, pose, BoneLibrary.BoneUsedByAnything, _clock.Now, ConVars);
        End(state, started, before);

        int bones = state.CachedBones.Length;

        if (!built || bones <= 0)
        {
            return null;
        }

        // The placement carries the scale here: it IS the entity's transform,
        // which the static-prop branch writes into every bone, and so what a
        // bone must differ from to count as posed.
        BonePlacement placement = default;
        placement.Reset();
        placement.Origin = pose.AbsOrigin;
        placement.Angles = pose.AbsAngles;
        placement.ModelScale = state.ModelScale;

        float[] entityToWorld = new float[StandaloneBones.FloatsPerMatrix];
        WriteMatrix(BoneLibraryMath.PlacementToWorld(placement), entityToWorld, 0);

        float[] matrices = new float[bones * StandaloneBones.FloatsPerMatrix];
        uint[] written = new uint[(bones + 31) / 32];

        for (int i = 0; i < bones; i++)
        {
            WriteMatrix(state.CachedBones[i], matrices, i * StandaloneBones.FloatsPerMatrix);

            if (BoneLibrary.BoneIsWritten(hdr, i, state.BoneMaskAsked))
            {
                written[i / 32] |= 1u << (i % 32);
            }
        }

        return new StandaloneBones(bones, matrices, written, entityToWorld);
    }

    private static void WriteMatrix(in BoneMatrix3x4 m, float[] into, int at)
    {
        into[at + 0] = m.Row0.X; into[at + 1] = m.Row0.Y; into[at + 2] = m.Row0.Z; into[at + 3] = m.Row0.W;
        into[at + 4] = m.Row1.X; into[at + 5] = m.Row1.Y; into[at + 6] = m.Row1.Z; into[at + 7] = m.Row1.W;
        into[at + 8] = m.Row2.X; into[at + 9] = m.Row2.Y; into[at + 10] = m.Row2.Z; into[at + 11] = m.Row2.W;
    }

    /// <inheritdoc/>
    public bool IsPoseable(EntityHandle entity) =>
        Read(_blocks.Block(entity), out EngineBlock block)
        && BoneLibrary.IsPoseable(_models.OpenStudioHdr(block.Head.ModelIndex));

    /// <inheritdoc/>
    public string StudioName(EntityHandle entity) =>
        Read(_blocks.Block(entity), out EngineBlock block) ? _models.StudioName(block.Head.ModelIndex) : string.Empty;

    /// <inheritdoc/>
    public bool SetSequence(EntityHandle entity, int sequence, float playbackRate, bool restart, float startCycle = 0.0f)
    {
        Span<byte> span = _blocks.Block(entity);

        if (!Read(span, out EngineBlock block))
        {
            return false;
        }

        BoneEntityState state = StateOf(entity, block);
        _driver.SetPoseSequence(state, block.Head.ModelIndex, sequence, playbackRate, restart, _clock.Now, startCycle);
        WriteClock(span, state);
        return true;
    }

    /// <inheritdoc/>
    public bool ModelChanged(EntityHandle entity)
    {
        Span<byte> span = _blocks.Block(entity);

        if (!Read(span, out EngineBlock block))
        {
            return false;
        }

        BoneEntityState state = StateOf(entity, block);
        _driver.ResetSequenceForModel(state, block.Head.ModelIndex, _clock.Now);
        WriteClock(span, state);
        return true;
    }

    /// <inheritdoc/>
    public bool SequenceState(EntityHandle entity, out int sequence, out float cycle, out float duration,
                              out bool looping, out int starts)
    {
        sequence = 0;
        cycle = 0.0f;
        duration = 0.0f;
        looping = false;
        starts = 0;

        if (!Find(entity, out _, out BoneEntityState state))
        {
            return false;
        }

        sequence = state.Sequence;
        cycle = state.SequenceCycle(_clock.Now);
        duration = state.SeqDuration;
        looping = state.SeqLooping;
        starts = state.SequenceStarts;
        return true;
    }

    /// <inheritdoc/>
    public bool SetCycle(EntityHandle entity, float cycle)
    {
        if (!Find(entity, out _, out BoneEntityState state))
        {
            return false;
        }

        // The cache key carries the cycle, so a held one is a new key.
        state.HeldCycle = Math.Clamp(cycle, 0.0f, 1.0f);
        return true;
    }

    /// <inheritdoc/>
    public SkeletonScaleChange SetModelScale(EntityHandle entity, float scale)
    {
        if (!Find(entity, out EngineBlock block, out BoneEntityState state))
        {
            return SkeletonScaleChange.NotStandalone;
        }

        switch (state.SetModelScale(scale))
        {
            case ModelScaleChange.Refused:
                // CStandaloneEntity::SetModelScale's warning, kept: a refused
                // call that said nothing is the defect it guards against.
                _log.Warning($"[SourceSharp] model scale {scale} refused on '{_models.StudioName(block.Head.ModelIndex)}' " +
                             "(entity " + entity + "): a scale of zero or less is an entity that draws as nothing " +
                             "and traces as nothing\n");
                return SkeletonScaleChange.Refused;

            case ModelScaleChange.Unchanged:
                return SkeletonScaleChange.Unchanged;

            default:
                return SkeletonScaleChange.Changed;
        }
    }

    // ---- the pose parameters (plan_descent_logic CR11) ----------------------
    //
    // CBaseAnimating's (baseanimating.cpp 1318-1410): the value is stored as
    // Studio_SetPoseParameter's 0..1 encoding and read back in the
    // parameter's own units. A set builds nothing: the next ask that wants
    // bones (a hitbox trace, an attachment) sees the new generation and
    // rebuilds, so an entity nobody traces costs no bone work.

    private StudioHdr? HdrOf(EntityHandle entity, out EngineBlock block) =>
        Read(_blocks.Block(entity), out block) ? _models.OpenStudioHdr(block.Head.ModelIndex) : null;

    /// <inheritdoc/>
    public int PoseParameterCount(EntityHandle entity) => HdrOf(entity, out _)?.NumPoseParameters ?? 0;

    /// <inheritdoc/>
    public int LookupPoseParameter(EntityHandle entity, string name)
    {
        long started = Stopwatch.GetTimestamp();
        int index = LookupPoseParameterCore(entity, name);
        PoseCost(started);
        return index;
    }

    private int LookupPoseParameterCore(EntityHandle entity, string name)
    {
        if (string.IsNullOrEmpty(name) || HdrOf(entity, out _) is not { } hdr)
        {
            return -1;
        }

        for (int i = 0; i < hdr.NumPoseParameters; i++)
        {
            if (string.Equals(hdr.PoseParameter(i).Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <inheritdoc/>
    public string PoseParameterName(EntityHandle entity, int parameter) =>
        HdrOf(entity, out _) is { } hdr && parameter >= 0 && parameter < hdr.NumPoseParameters
            ? hdr.PoseParameter(parameter).Name
            : string.Empty;

    /// <inheritdoc/>
    public bool PoseParameterRange(EntityHandle entity, int parameter, out float start, out float end)
    {
        start = 0f;
        end = 0f;

        if (HdrOf(entity, out _) is not { } hdr || parameter < 0 || parameter >= hdr.NumPoseParameters)
        {
            return false;
        }

        MstudioPoseParamDesc desc = hdr.PoseParameter(parameter);
        start = desc.Start;
        end = desc.End;
        return true;
    }

    /// <summary>The entity's state with the model's defaults read, for a parameter the model declares.</summary>
    private bool PoseState(EntityHandle entity, int parameter, out StudioHdr hdr, out BoneEntityState state)
    {
        hdr = null!;
        state = null!;

        if (parameter < 0 || parameter >= BoneEntityState.MaxPoseParameters
            || !Find(entity, out EngineBlock block, out state)
            || _models.OpenStudioHdr(block.Head.ModelIndex) is not { } opened
            || parameter >= opened.NumPoseParameters)
        {
            return false;
        }

        // The defaults first: a set (or a read) after a model change is about
        // the new model, whose defaults replace the old one's poses.
        _driver.RefreshModelPoseState(state, block.Head.ModelIndex);
        hdr = opened;
        return true;
    }

    /// <inheritdoc/>
    public float SetPoseParameter(EntityHandle entity, int parameter, float value)
    {
        long started = Stopwatch.GetTimestamp();
        float stored = SetPoseParameterCore(entity, parameter, value);
        PoseCost(started);
        return stored;
    }

    private float SetPoseParameterCore(EntityHandle entity, int parameter, float value)
    {
        if (!PoseState(entity, parameter, out StudioHdr hdr, out BoneEntityState state))
        {
            return 0f;
        }

        float normalised = state.PoseParameter[parameter];
        float stored = BoneSetupQuery.SetPoseParameter(hdr, parameter, value, ref normalised);

        if (state.PoseParameter[parameter] != normalised)
        {
            state.PoseParameter[parameter] = normalised;
            PoseMoved(state);
        }

        return stored;
    }

    /// <inheritdoc/>
    public float GetPoseParameter(EntityHandle entity, int parameter)
    {
        long started = Stopwatch.GetTimestamp();
        float value = PoseState(entity, parameter, out StudioHdr hdr, out BoneEntityState state)
            ? BoneSetupQuery.GetPoseParameter(hdr, parameter, state.PoseParameter[parameter])
            : 0f;
        PoseCost(started);
        return value;
    }

    /// <summary>One pose ask counted (the dials the players' animation state turns each frame).</summary>
    private void PoseCost(long started)
    {
        PoseAsks++;
        PoseTicks += Stopwatch.GetTimestamp() - started;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Stock's <c>GetSequenceGroundSpeed</c> over the server's own pose (the client's twin is
    /// ClientSkeletons'): the root's travel over the sequence's duration, read at the pose the entity
    /// holds once its model's defaults were read, the model's defaults before that.
    /// </remarks>
    public float SequenceGroundSpeed(EntityHandle entity, int sequence)
    {
        long started = Stopwatch.GetTimestamp();
        float speed = SequenceGroundSpeedCore(entity, sequence);
        PoseCost(started);
        return speed;
    }

    private float SequenceGroundSpeedCore(EntityHandle entity, int sequence)
    {
        if (sequence < 0 || !Find(entity, out EngineBlock block, out BoneEntityState state))
        {
            return 0f;
        }

        StudioHdr? hdr = _models.OpenStudioHdr(block.Head.ModelIndex);

        if (hdr is null || sequence >= hdr.NumSequences)
        {
            return 0f;
        }

        float[]? pose = state.PoseParametersForDuration(block.Head.ModelIndex);

        if (pose is null)
        {
            pose = new float[BoneEntityState.MaxPoseParameters];
            BoneSetupHdrRead.CalcDefaultPoseParameters(hdr, pose, BoneEntityState.MaxPoseParameters);
        }

        if (!BoneLibrary.SequenceFacts(hdr, sequence, pose, out float duration, out _) || duration <= 0f)
        {
            return 0f;
        }

        if (!BoneSetupQuery.SeqMovement(hdr, sequence, 0f, 1f, pose, out NVector movement, out _))
        {
            return 0f;
        }

        return movement.Length() / duration;
    }

    /// <summary>
    /// A stored pose changed: the generation moves (the bone cache's key), and
    /// the attachments, which are read without asking whether the bones still
    /// stand, go stale.
    /// </summary>
    private static void PoseMoved(BoneEntityState state)
    {
        state.PoseGeneration++;
        state.AttachmentsValid = false;
    }

    // ---- the engine's hitbox trace --------------------------------------------

    /// <summary>
    /// <c>CStandaloneEntity::TestHitboxes</c>: a ray against the posed
    /// hitboxes of the entity whose <paramref name="block"/> native handed
    /// over. True means "tested", not "hit" (the trace's fraction is the hit).
    /// </summary>
    public bool TestHitboxes(EntityHandle entity, Span<byte> block, in BoneRay ray, int contentsMask, BoneTrace tr)
    {
        // An entity that answers its traces itself (its custom ray or box test) answers before its hitboxes.
        if (_customTargets?.Invoke(entity) is { } custom)
        {
            CustomTraces++;
            return custom.TestTrace(ray, contentsMask, tr);
        }

        if (!Read(block, out EngineBlock read))
        {
            return false;
        }

        HitboxTraces++;
        BoneEntityState state = StateOf(entity, read);
        ServerBonePose pose = PoseOf(read);

        long started = Begin(state, out BoneCacheKey before);
        bool tested = _driver.TestHitboxes(state, pose, read.Client.HitboxSet, ray, contentsMask, tr,
                                           _surfaceReader, _clock.Now, ConVars);
        End(state, started, before);
        return tested;
    }

    /// <inheritdoc/>
    public bool TestHitboxes(EntityHandle entity, Span<byte> block, ReadOnlySpan<float> ray, int contentsMask,
                             Span<float> traceFloats, Span<int> traceInts)
    {
        BoneTrace tr = BoneTraceCodec.ReadTrace(traceFloats, traceInts);

        if (!TestHitboxes(entity, block, BoneTraceCodec.ReadRay(ray), contentsMask, tr))
        {
            return false;
        }

        BoneTraceCodec.WriteTrace(tr, traceFloats, traceInts);
        return true;
    }

    private int SurfaceIndexOf(StudioHdr hdr, int bone)
    {
        string name = hdr.Bone(bone).SurfacePropName;

        if (!_surfaceIndices.TryGetValue(name, out int index))
        {
            index = _surfaceProps.SurfaceIndex(name);
            _surfaceIndices.Add(name, index);
        }

        return index;
    }

    // ---- the lag compensation's rewind -------------------------------------------

    /// <inheritdoc/>
    public bool Capture(EntityHandle entity, SkeletonAnimation into)
    {
        if (!Find(entity, out _, out BoneEntityState state))
        {
            return false;
        }

        into.Sequence = state.Sequence;
        into.Cycle = state.SequenceCycle(_clock.Now);
        into.HeldCycle = state.HeldCycle;
        state.PoseParameter.AsSpan().CopyTo(into.PoseParameters);
        return true;
    }

    /// <inheritdoc/>
    public void Pose(EntityHandle entity, int sequence, float cycle, ReadOnlySpan<float> poseParameters)
    {
        if (!Find(entity, out _, out BoneEntityState state))
        {
            return;
        }

        // The block's clock is the client's and is not written: the rewind
        // lasts one attack and is put back before anything is sent.
        state.Sequence = sequence;
        state.HeldCycle = cycle;
        poseParameters[..Math.Min(poseParameters.Length, state.PoseParameter.Length)].CopyTo(state.PoseParameter);

        // The cache key carries the sequence and the cycle but not the poses:
        // their generation moves, and the attachments go stale with it.
        PoseMoved(state);
    }

    /// <inheritdoc/>
    public void Restore(EntityHandle entity, SkeletonAnimation saved)
    {
        if (!Find(entity, out _, out BoneEntityState state))
        {
            return;
        }

        state.Sequence = saved.Sequence;
        state.HeldCycle = saved.HeldCycle;
        saved.PoseParameters.AsSpan().CopyTo(state.PoseParameter);
        PoseMoved(state);
    }

    /// <inheritdoc/>
    public void InvalidateBoneCache(EntityHandle entity)
    {
        if (_states.TryGet(entity, out BoneEntityState? state) && state is not null)
        {
            state.InvalidateBoneCache();
        }
    }

    // ---- lifetime -------------------------------------------------------------

    /// <summary>The entity is gone: its skeleton goes with it.</summary>
    public void Forget(EntityHandle entity)
    {
        _states.Remove(entity);
        _carried.Remove(entity);
    }

    /// <summary>
    /// The engine flushed its model cache: every image this cache copied is
    /// stale, and so is every skeleton built from one. Answers how many
    /// skeletons were invalidated.
    /// </summary>
    public int InvalidateMdlCache()
    {
        _models.Clear();
        _surfaceIndices.Clear();
        return _states.InvalidateAll();
    }

    /// <summary>Level shutdown: every skeleton and every model image goes.</summary>
    public void OnLevelShutdown()
    {
        _states.Clear();
        _carried.Clear();
        _models.Clear();
        _surfaceIndices.Clear();
    }

    // ---- across a hot reload ----------------------------------------------------

    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        float[] defaults = new float[BoneEntityState.MaxPoseParameters];

        foreach ((EntityHandle entity, BoneEntityState state) in _states.All())
        {
            // Stock's DATADESC saves m_flPoseParameter: each pose that is not
            // its model's default rides with the model it was set on.
            StudioHdr? hdr = state.PoseDefaultsFor >= 0 ? _models.OpenStudioHdr(state.PoseDefaultsFor) : null;
            int declared = 0;
            int moved = 0;

            if (hdr is not null)
            {
                BoneLibrary.ModelFacts(hdr, out _, defaults);
                declared = Math.Min(hdr.NumPoseParameters, BoneEntityState.MaxPoseParameters);

                for (int i = 0; i < declared; i++)
                {
                    moved += state.PoseParameter[i] != defaults[i] ? 1 : 0;
                }
            }

            if (state.ModelScale == 1.0f && moved == 0)
            {
                continue;
            }

            ReloadRow row = writer.AddRow().Set("entity", entity).Set("scale", state.ModelScale);

            if (moved > 0)
            {
                row.Set("posemodel", state.PoseDefaultsFor);

                for (int i = 0; i < declared; i++)
                {
                    if (state.PoseParameter[i] != defaults[i])
                    {
                        row.Set(PoseKey(i), state.PoseParameter[i]);
                    }
                }
            }
        }

        // One carried but not yet asked about is still owed.
        foreach ((EntityHandle entity, CarriedSkeleton carried) in _carried)
        {
            ReloadRow row = writer.AddRow().Set("entity", entity).Set("scale", carried.Scale);

            if (carried.Poses is { } poses)
            {
                row.Set("posemodel", carried.PoseModel);

                for (int i = 0; i < poses.Length; i++)
                {
                    if (!float.IsNaN(poses[i]))
                    {
                        row.Set(PoseKey(i), poses[i]);
                    }
                }
            }
        }
    }

    private static string PoseKey(int parameter) => "pose" + parameter.ToString(System.Globalization.CultureInfo.InvariantCulture);

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        _carried.Clear();

        foreach (ReloadRow row in reader.Rows)
        {
            EntityHandle entity = reader.Resolve(row.GetHandle("entity"));
            float scale = row.GetFloat("scale", 1.0f);

            if (entity == EntityHandle.None || scale <= 0.0f)
            {
                continue;
            }

            var carried = new CarriedSkeleton { Scale = scale, PoseModel = row.GetInt("posemodel", -1) };

            if (carried.PoseModel >= 0)
            {
                carried.Poses = new float[BoneEntityState.MaxPoseParameters];

                for (int i = 0; i < carried.Poses.Length; i++)
                {
                    carried.Poses[i] = row.GetFloat(PoseKey(i), float.NaN);
                }
            }

            if (scale != 1.0f || carried.Poses is not null)
            {
                _carried[entity] = carried;
            }
        }
    }
}
