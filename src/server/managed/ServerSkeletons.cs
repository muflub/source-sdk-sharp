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
/// scale was set again.
/// </para>
/// </summary>
public sealed class ServerSkeletons : IStandaloneSkeletons, IStandaloneHitboxes, IGameLifecycle, ISaveCustom
{
    private readonly IStandaloneBlocks _blocks;
    private readonly StudioModels _models;
    private readonly IGameClock _clock;
    private readonly ILog _log;
    private readonly AnimThreeWayBlendConVar? _threeWayBlend;
    private readonly IFwdPhysicsSurfaceProps? _surfaceProps;
    private readonly ServerBoneDriver _driver;
    private readonly BoneEntityStates _states = new();
    private readonly Dictionary<string, int> _surfaceIndices = new(StringComparer.Ordinal);
    private readonly SurfacePropReader? _surfaceReader;

    /// <summary>Scales a reload carried, for skeletons not yet asked about in this composition.</summary>
    private readonly Dictionary<EntityHandle, float> _carriedScales = [];

    public ServerSkeletons(IStandaloneBlocks blocks, StudioModels models, IGameClock clock, ILog log,
                           AnimThreeWayBlendConVar? threeWayBlend = null,
                           IFwdPhysicsSurfaceProps? surfaceProps = null)
    {
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _threeWayBlend = threeWayBlend;
        _surfaceProps = surfaceProps;
        _driver = new ServerBoneDriver(models);
        _surfaceReader = surfaceProps is null ? null : new SurfacePropReader(SurfaceIndexOf);
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

    /// <summary>The cost counters back to zero.</summary>
    public void ResetCost()
    {
        Asks = 0;
        Builds = 0;
        AskTicks = 0;
        HitboxTraces = 0;
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

        if (_carriedScales.Remove(entity, out float scale))
        {
            state.SetModelScale(scale);
        }

        state.Sequence = block.Client.Sequence;
        state.PlaybackRate = block.Client.PlaybackRate;
        state.AnimTime = block.Client.AnimTime;

        // A clock that is not the constructor's (sequence 0, rate 1, never
        // started) was set by SetPoseSequence, which read the duration and
        // the loop flag at the model's own defaults: so does this.
        if (state.Sequence != 0 || state.PlaybackRate != 1.0f || state.AnimTime != 0.0f)
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
        target.Client.AnimTime = state.AnimTime;
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
    public int LookupAttachment(EntityHandle entity, string name)
    {
        if (string.IsNullOrEmpty(name) || !Read(_blocks.Block(entity), out EngineBlock block))
        {
            return 0;
        }

        return Math.Max(0, BoneLibrary.LookupAttachment(_models.OpenStudioHdr(block.Head.ModelIndex), name));
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
    public bool SetSequence(EntityHandle entity, int sequence, float playbackRate, bool restart)
    {
        Span<byte> span = _blocks.Block(entity);

        if (!Read(span, out EngineBlock block))
        {
            return false;
        }

        BoneEntityState state = StateOf(entity, block);
        _driver.SetPoseSequence(state, block.Head.ModelIndex, sequence, playbackRate, restart, _clock.Now);
        WriteClock(span, state);
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

    // ---- the engine's hitbox trace --------------------------------------------

    /// <summary>
    /// <c>CStandaloneEntity::TestHitboxes</c>: a ray against the posed
    /// hitboxes of the entity whose <paramref name="block"/> native handed
    /// over. True means "tested", not "hit" (the trace's fraction is the hit).
    /// </summary>
    public bool TestHitboxes(EntityHandle entity, Span<byte> block, in BoneRay ray, int contentsMask, BoneTrace tr)
    {
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
            index = _surfaceProps!.SurfaceIndex(name);
            _surfaceIndices.Add(name, index);
        }

        return index;
    }

    // ---- lifetime -------------------------------------------------------------

    /// <summary>The entity is gone: its skeleton goes with it.</summary>
    public void Forget(EntityHandle entity)
    {
        _states.Remove(entity);
        _carriedScales.Remove(entity);
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
        _carriedScales.Clear();
        _models.Clear();
        _surfaceIndices.Clear();
    }

    // ---- across a hot reload ----------------------------------------------------

    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        foreach ((EntityHandle entity, BoneEntityState state) in _states.All())
        {
            if (state.ModelScale != 1.0f)
            {
                writer.AddRow().Set("entity", entity).Set("scale", state.ModelScale);
            }
        }

        // One carried but not yet asked about is still owed.
        foreach ((EntityHandle entity, float scale) in _carriedScales)
        {
            writer.AddRow().Set("entity", entity).Set("scale", scale);
        }
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        _carriedScales.Clear();

        foreach (ReloadRow row in reader.Rows)
        {
            EntityHandle entity = reader.Resolve(row.GetHandle("entity"));
            float scale = row.GetFloat("scale", 1.0f);

            if (entity != EntityHandle.None && scale > 0.0f && scale != 1.0f)
            {
                _carriedScales[entity] = scale;
            }
        }
    }
}
