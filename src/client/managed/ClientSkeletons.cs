using System.Diagnostics;
using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared.Bones;

using NVector = System.Numerics.Vector3;

namespace SourceSharp.Client;

/// <summary>
/// THE CLIENT'S SKELETONS, IN MANAGED CODE (plan physics_bones_native_zero
/// steps 1.1-1.2, client half; the server's twin is ServerSkeletons).
///
/// <para>
/// C_StandaloneEntity used to pose itself: EnsureBones, MergeBonesFrom,
/// EnsureAttachments, TestHitboxes, the pose parameters, the layers and the
/// sequence clock were C++ over bone_library.cpp. All of that is here:
/// <see cref="ClientBoneDriver"/> decides, <see cref="BoneEntityStates"/>
/// remembers, <see cref="StudioModels"/> reads the models. Native keeps the
/// IClientRenderable / ICollideable virtuals the engine calls, each a thunk
/// that hands its entity's engine block to <see cref="IClientBoneAnswers"/>
/// through IManagedBonesClient, and a store for the clock its per-frame cycle
/// reads (<see cref="IClientSequenceStore"/>).
/// </para>
///
/// <para>
/// <b>What the engine's asks carry.</b> The block (which model, where it
/// stands and is drawn, whether it is the local viewmodel, its hitbox set),
/// the clock and frame count the engine asked at, and for a ragdoll body the
/// solver's bones as native's ragdoll library wrote them: the solver stays
/// native until the ragdoll lane, so its answer arrives as an input rather
/// than as a call back. A managed ask (an attachment a C# effect wants) reads
/// the block itself, one crossing, at the engine's own clock.
/// </para>
///
/// <para>
/// <b>A merge source is asked here, not through the engine.</b> Every
/// networked entity on this side is a standalone, so the source's skeleton is
/// this class's too: its block is read and it is posed in place, with the
/// driver's re-entry guard breaking a cycle as native's did.
/// </para>
/// </summary>
public sealed class ClientSkeletons : IClientStandaloneSkeletons, IClientBoneAnswers, IReloadParticipant
{
    private const int MaxBones = StudioConstants.MAXSTUDIOBONES;

    private readonly IStandaloneBlocks _blocks;
    private readonly IClientSequenceStore _store;
    private readonly StudioModels _models;
    private readonly Func<int, EntityHandle?> _handleAt;
    private readonly IEngineEnvironment _environment;
    private readonly ILog _log;
    private readonly ClAnimThreeWayBlendConVar? _threeWayBlend;
    private readonly IFwdPhysicsSurfaceProps _surfaceProps;
    private readonly ClientBoneDriver _driver;
    private readonly BoneEntityStates _states = new();
    private readonly ClientBoneBatch _batch = ClientBoneBatch.Allocate();

    // The entities that became ragdolls since the last batch was built: the
    // frame they flipped in and what their entry had answered by then (-1 for
    // no entry), for the flip counters below.
    private readonly Dictionary<EntityHandle, (int Frame, int Answered)> _flips = [];
    private readonly Dictionary<string, int> _surfaceIndices = new(StringComparer.Ordinal);
    private readonly SurfacePropReader _surfaceReader;

    // The ragdoll answer of the ask in flight. A token rather than the native
    // id: a nested ask (a merge source) never carries one, and its state's
    // stale token then names no body.
    private readonly BoneMatrix3x4[] _askRagdollBones = new BoneMatrix3x4[MaxBones];
    private readonly int[] _askRagdollOwned = new int[Ragdolls.RagdollBody.MaxElements];
    private int _askRagdollCount = -1;
    private uint _askToken;

    private readonly BoneMatrix3x4[] _sourceBones = new BoneMatrix3x4[MaxBones];

    // The client's ragdolls (plan_physics_bones_native_zero Phase 3): a body's
    // solver bones are asked of them, not handed in by native. A factory,
    // because the ragdolls are posed from these skeletons and so are built
    // after them.
    private readonly Func<Ragdolls.IClientRagdolls?>? _ragdolls;

    public ClientSkeletons(IStandaloneBlocks blocks, IClientSequenceStore store, StudioModels models,
                           Func<int, EntityHandle?> handleAt, IEngineEnvironment environment, ILog log,
                           IFwdPhysicsSurfaceProps surfaceProps,
                           ClAnimThreeWayBlendConVar? threeWayBlend = null,
                           Func<Ragdolls.IClientRagdolls?>? ragdolls = null)
    {
        _ragdolls = ragdolls;
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _handleAt = handleAt ?? throw new ArgumentNullException(nameof(handleAt));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _threeWayBlend = threeWayBlend;
        _surfaceProps = surfaceProps ?? throw new ArgumentNullException(nameof(surfaceProps));
        _driver = new ClientBoneDriver(new World(this));
        _surfaceReader = SurfaceIndexOf;
    }

    /// <summary>The per-entity states, for the facts and the reports.</summary>
    public BoneEntityStates States => _states;

    /// <summary>The model cache the skeletons read.</summary>
    public StudioModels Models => _models;

    /// <summary>The frame's posed-ahead skeletons (plan_physics_bones_perf A2).</summary>
    public ClientBoneBatch Batch => _batch;

    /// <summary>
    /// Whether the frame's batch is built. Off, BoneBatch answers no table and
    /// every SetupBones ask crosses, as before A2: the A/B the debug-only
    /// cs_cl_bonebatch toggles within one process.
    /// </summary>
    public bool BatchEnabled { get; set; } = true;

    private BoneConVars ConVars => new(_threeWayBlend?.Value ?? true, JiggleBones.DefaultFramerateCutoff, 0);

    private float Now => _environment.Globals.IsAvailable ? _environment.Globals.CurTime : 0f;

    private int FrameCount => _environment.Globals.IsAvailable ? _environment.Globals.FrameCount : 0;

    // ---- the cost report (plan step 1.3) ------------------------------------

    /// <summary>Every ask that could build a skeleton (the engine's and managed's).</summary>
    public long Asks { get; private set; }

    /// <summary>How many of those asks built one.</summary>
    public long Builds { get; private set; }

    /// <summary><see cref="Stopwatch"/> ticks spent inside those asks.</summary>
    public long AskTicks { get; private set; }

    /// <summary>The engine's SetupBones asks alone, and their ticks.</summary>
    public long EngineSetupBones { get; private set; }

    /// <summary><see cref="Stopwatch"/> ticks spent inside the engine's SetupBones asks.</summary>
    public long EngineSetupBonesTicks { get; private set; }

    /// <summary>The engine's SetupBones asks per model index: how many, the ticks inside them, and how many built.</summary>
    public Dictionary<int, (long Asks, long Ticks, long Builds)> SetupBonesByModel { get; } = [];

    /// <summary>The engine's hitbox traces.</summary>
    public long HitboxTraces { get; private set; }

    /// <summary>The frames' batches built, the skeletons they posed, and their ticks.</summary>
    public long Batches { get; private set; }

    /// <summary>The skeletons the batches posed.</summary>
    public long BatchEntries { get; private set; }

    /// <summary><see cref="Stopwatch"/> ticks spent building the batches.</summary>
    public long BatchTicks { get; private set; }

    /// <summary>The SetupBones asks native answered from a batch (no crossing), counted a frame later.</summary>
    public long BatchAnswered { get; private set; }

    /// <summary>Entities that became ragdolls (<see cref="BecameRagdoll"/>).</summary>
    public long RagdollFlips { get; private set; }

    /// <summary>The flips that found a batch entry posed for the entity, and dropped it.</summary>
    public long RagdollFlipEntries { get; private set; }

    /// <summary>Asks native answered from a flipped entity's entry after its flip: the pre-ragdoll pose drawn. Zero.</summary>
    public long RagdollFlipStaleAnswers { get; private set; }

    /// <summary>Asks that crossed for a flipped entity in its flip's frame and were answered the solver's bones.</summary>
    public long RagdollFlipFrameAsks { get; private set; }

    /// <summary>The cost counters back to zero.</summary>
    public void ResetCost()
    {
        Asks = 0;
        Builds = 0;
        AskTicks = 0;
        EngineSetupBones = 0;
        EngineSetupBonesTicks = 0;
        SetupBonesByModel.Clear();
        HitboxTraces = 0;
        Batches = 0;
        BatchEntries = 0;
        BatchTicks = 0;
        BatchAnswered = 0;
        RagdollFlips = 0;
        RagdollFlipEntries = 0;
        RagdollFlipStaleAnswers = 0;
        RagdollFlipFrameAsks = 0;
    }

    private static bool SameKey(in BoneCacheKey a, in BoneCacheKey b) =>
        MemoryMarshal.AsBytes(new ReadOnlySpan<BoneCacheKey>(in a))
            .SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<BoneCacheKey>(in b)));

    private void Count(BoneEntityState state, long started, in BoneCacheKey before)
    {
        AskTicks += Stopwatch.GetTimestamp() - started;
        Asks++;

        if (!SameKey(before, state.CacheKey))
        {
            Builds++;
        }
    }

#if DEBUG
    // ---- the perf instruments (cs_cl_bonebench, cs_cl_bonedump) --------------------

    /// <summary>
    /// The entity's skeleton built again at <paramref name="time"/>, its cache
    /// invalidated first, under every bone's mask: one rebuild for the bench.
    /// A merging entity reads its source's cache, which stands at the same
    /// clock. Answers the bone count built, or 0.
    /// </summary>
    public int Rebuild(EntityHandle entity, float time, int frameCount)
    {
        if (!Find(entity, out EngineBlock block, out BoneEntityState state))
        {
            return 0;
        }

        state.InvalidateBoneCache();
        BeginManagedAsk(state);
        _merging = entity;

        return _driver.EnsureBones(state, PoseOf(block, frameCount), BoneLibrary.BoneUsedByAnything, time, ConVars)
            ? state.CachedBones.Length
            : 0;
    }

    /// <summary>
    /// Every skeleton's history gone (the IK solver's planted feet, the
    /// springs' swing, the sequences a change is fading out, the cache), so a build at a given clock depends on the
    /// animation state alone: the dump's precondition, which two builds of the
    /// code compare under.
    /// </summary>
    public void ForgetHistory()
    {
        foreach ((EntityHandle _, BoneEntityState state) in _states.All())
        {
            state.Ik = null;
            state.Jiggle = null;
            state.Transitions.RemoveAll();
            state.InvalidateBoneCache();
        }
    }

    /// <summary>The entity's cached bones as the last build left them (the dump).</summary>
    public ReadOnlySpan<BoneMatrix3x4> CachedBones(EntityHandle entity) =>
        _states.TryGet(entity, out BoneEntityState? state) && state is not null ? state.CachedBones : [];

    /// <summary>
    /// The inputs the entity's last build was keyed on, as hex (the dump's
    /// fingerprint: two builds compare only where the inputs were the same,
    /// since a view model sways between any two dumps).
    /// </summary>
    public string CacheKeyText(EntityHandle entity) =>
        _states.TryGet(entity, out BoneEntityState? state) && state is not null
            ? Convert.ToHexString(MemoryMarshal.AsBytes(new ReadOnlySpan<BoneCacheKey>(in state.CacheKey)))
            : string.Empty;

    /// <summary>The entity's merge source index, or -1 (the dump orders sources first).</summary>
    public int MergeSourceOf(EntityHandle entity) =>
        _states.TryGet(entity, out BoneEntityState? state) && state is not null ? state.BoneMergeSource : -1;
#endif

    // ---- the block --------------------------------------------------------------

    private static bool Read(ReadOnlySpan<byte> block, out EngineBlock read)
    {
        if (block.Length < EngineBlock.SizeBytes)
        {
            read = default;
            return false;
        }

        read = MemoryMarshal.Read<EngineBlock>(block);
        return true;
    }

    private bool Find(EntityHandle entity, out EngineBlock block, out BoneEntityState state)
    {
        if (!Read(_blocks.Block(entity), out block))
        {
            state = null!;
            return false;
        }

        state = _states.GetOrCreate(entity);
        return true;
    }

    private static ClientBonePose PoseOf(in EngineBlock block, int frameCount) => new()
    {
        ModelIndex = block.Head.ModelIndex,
        ViewModel = block.Client.ViewModel != 0,
        NetOrigin = new NVector(block.Head.Origin.X, block.Head.Origin.Y, block.Head.Origin.Z),
        NetAngles = new BoneAngle(block.Head.Angles.Pitch, block.Head.Angles.Yaw, block.Head.Angles.Roll),
        RenderOrigin = new NVector(block.Client.RenderOrigin.X, block.Client.RenderOrigin.Y, block.Client.RenderOrigin.Z),
        RenderAngles = new BoneAngle(block.Client.RenderAngles.Pitch, block.Client.RenderAngles.Yaw,
                                     block.Client.RenderAngles.Roll),
        FrameCount = frameCount,
        InterpolationFrame = block.Head.InterpolationFrame,
    };

    /// <summary>The model index a block names, as GetModel() reads it: none at 0 or below.</summary>
    private static int ModelOf(in EngineBlock block) => block.Head.ModelIndex > 0 ? block.Head.ModelIndex : 0;

    private StudioHdr? Hdr(in EngineBlock block)
    {
        int model = ModelOf(block);
        return model > 0 ? _models.OpenStudioHdr(model) : null;
    }

    private static BoneMatrix3x4 MatrixOf(in Transform t) => new()
    {
        Row0 = new System.Numerics.Vector4(t.M00, t.M01, t.M02, t.M03),
        Row1 = new System.Numerics.Vector4(t.M10, t.M11, t.M12, t.M13),
        Row2 = new System.Numerics.Vector4(t.M20, t.M21, t.M22, t.M23),
    };

    // ---- the ragdoll answer of one ask ----------------------------------------

    private void BeginAsk(EntityHandle entity, BoneEntityState state)
    {
        unchecked { _askToken = _askToken == uint.MaxValue ? 1u : _askToken + 1u; }

        // THE SOLVER'S BONES, FROM THE RAGDOLLS THEMSELVES: RagdollGetBoneMatrix
        // per element, straight into the ask's buffer (plan
        // physics_bones_native_zero Phase 3; native held them until then).
        int written = _ragdolls?.Invoke() is { } ragdolls
            ? ragdolls.WriteBones(entity, _askRagdollBones, _askRagdollOwned)
            : -1;

        if (written >= 0)
        {
            _askRagdollCount = written;
            state.Ragdoll = _askToken;
            return;
        }

        _askRagdollCount = -1;

        // The body went (its entity's removal, or a level change took every
        // body apart): the skeleton built around the solver must not answer
        // from the cache, whose key never saw the solver.
        if (state.Ragdoll != BoneEntityState.RagdollNone)
        {
            state.Ragdoll = BoneEntityState.RagdollNone;
            state.InvalidateBoneCache();
        }
    }

    private void BeginManagedAsk(BoneEntityState state)
    {
        // A managed ask carries no solver answer: an entity that is a body
        // answers from the bones its last engine ask built (the solver's
        // marks never let a body's key be rebuilt from its frozen animation
        // without the solver), and its body token is left for the next engine
        // ask to compare. No ask is in flight, so no token names a body now.
        _askRagdollCount = -1;
        _ = state;
    }

    // ---- what the driver asks the engine ----------------------------------------

    private sealed class World(ClientSkeletons owner) : IClientBoneWorld
    {
        public bool HasStudioModel(int modelIndex) => modelIndex > 0 && owner._models.HasStudioModel(modelIndex);

        public StudioHdr? OpenStudioHdr(int modelIndex) => modelIndex > 0 ? owner._models.OpenStudioHdr(modelIndex) : null;

        public bool IsRagdollBody(uint ragdoll) =>
            ragdoll != BoneEntityState.RagdollNone && ragdoll == owner._askToken && owner._askRagdollCount >= 0;

        public int WriteRagdollBones(uint ragdoll, BoneMatrix3x4[] bones, Span<int> ownedBones)
        {
            if (!IsRagdollBody(ragdoll))
            {
                return 0;
            }

            int count = owner._askRagdollCount;
            int listed = Math.Min(count, Math.Min(ownedBones.Length, owner._askRagdollOwned.Length));

            for (int i = 0; i < listed; i++)
            {
                int bone = owner._askRagdollOwned[i];
                ownedBones[i] = bone;

                if ((uint)bone < (uint)bones.Length)
                {
                    bones[bone] = owner._askRagdollBones[bone];
                }
            }

            return count;
        }

        public BoneMergeSourceView ResolveMergeSource(int entIndex) => owner.ResolveMergeSource(entIndex);

        public StudioHdr? OpenStudioHdrForModel(nint model) => model > 0 ? owner._models.OpenStudioHdr((int)model) : null;

        public bool SourceSetupBones(int entIndex, BoneMatrix3x4[] bones, int boneMask, float currentTime) =>
            owner.SourceSetupBones(entIndex, bones, boneMask, currentTime);

        public void Say(string message) => owner._log.Msg(message + "\n");
    }

    private EntityHandle? HandleAt(int entIndex) => _handleAt(entIndex);

    private BoneMergeSourceView ResolveMergeSource(int entIndex)
    {
        if (HandleAt(entIndex) is not { } source || !Read(_blocks.Block(source), out EngineBlock block))
        {
            return new BoneMergeSourceView(false, 0);
        }

        return new BoneMergeSourceView(source == _merging, ModelOf(block));
    }

    // The follower whose merge is being resolved, for the self test.
    private EntityHandle _merging;

    private bool SourceSetupBones(int entIndex, BoneMatrix3x4[] bones, int boneMask, float currentTime)
    {
        if (HandleAt(entIndex) is not { } source || !Find(source, out EngineBlock block, out BoneEntityState state))
        {
            return false;
        }

        // The source answers as IClientRenderable::SetupBones would: its own
        // skeleton, or its world transform in every slot.
        bool haveBones;
        int saved = _askRagdollCount;
        EntityHandle merging = _merging;

        try
        {
            BeginManagedAsk(state);
            _merging = source;
            haveBones = _driver.EnsureBones(state, PoseOf(block, FrameCount), boneMask, currentTime, ConVars);
        }
        finally
        {
            _askRagdollCount = saved;
            _merging = merging;
        }

        int copy = haveBones ? Math.Min(state.CachedBones.Length, bones.Length) : 0;
        state.CachedBones.AsSpan(0, copy).CopyTo(bones);

        BoneMatrix3x4 world = MatrixOf(block.Head.WorldTransform);

        for (int i = copy; i < bones.Length; i++)
        {
            bones[i] = world;
        }

        return true;
    }

    // ---- IClientBoneAnswers: the engine's asks ---------------------------------------

    /// <inheritdoc/>
    public bool SetupBones(EntityHandle entity, ReadOnlySpan<byte> block, Span<float> boneToWorld, int boneMask,
                           float currentTime, int frameCount)
    {
        long started = Stopwatch.GetTimestamp();

        if (!Read(block, out EngineBlock read))
        {
            return false;
        }

        BoneEntityState state = _states.GetOrCreate(entity);
        BoneCacheKey before = state.CacheKey;
        state.EngineAskFrame = frameCount;

        BeginAsk(entity, state);

        if (_askRagdollCount >= 0 && _flips.TryGetValue(entity, out (int Frame, int Answered) flip)
            && flip.Frame == frameCount)
        {
            RagdollFlipFrameAsks++;
        }

        _merging = entity;
        bool haveBones = _driver.EnsureBones(state, PoseOf(read, frameCount), boneMask, currentTime, ConVars);

        Count(state, started, before);

        bool answer = haveBones;

        if (!boneToWorld.IsEmpty)
        {
            Span<BoneMatrix3x4> output = MemoryMarshal.Cast<float, BoneMatrix3x4>(boneToWorld);
            int copy = haveBones ? Math.Min(state.CachedBones.Length, output.Length) : 0;

            state.CachedBones.AsSpan(0, copy).CopyTo(output);

            // Every slot past the model's bones -- every slot, with no
            // skeleton -- is the entity's own transform, not stack fungus.
            if (copy < output.Length)
            {
                output[copy..].Fill(MatrixOf(read.Head.WorldTransform));
            }

            answer = true;
        }

        long spent = Stopwatch.GetTimestamp() - started;
        EngineSetupBones++;
        EngineSetupBonesTicks += spent;

        SetupBonesByModel.TryGetValue(read.Head.ModelIndex, out (long Asks, long Ticks, long Builds) model);
        SetupBonesByModel[read.Head.ModelIndex] =
            (model.Asks + 1, model.Ticks + spent, model.Builds + (SameKey(before, state.CacheKey) ? 0 : 1));
        return answer;
    }

    /// <inheritdoc/>
    public bool TestHitboxes(EntityHandle entity, ReadOnlySpan<byte> block, ReadOnlySpan<float> ray, int contentsMask,
                             Span<float> traceFloats, Span<int> traceInts, float currentTime, int frameCount)
    {
        if (!Read(block, out EngineBlock read))
        {
            return false;
        }

        BoneTrace tr = BoneTraceCodec.ReadTrace(traceFloats, traceInts);
        bool tested = TestHitboxes(entity, read, BoneTraceCodec.ReadRay(ray), contentsMask, tr, currentTime, frameCount);

        if (tested)
        {
            BoneTraceCodec.WriteTrace(tr, traceFloats, traceInts);
        }

        return tested;
    }

    /// <summary>
    /// The same hitbox trace, asked by managed code of an entity by its handle
    /// (stock's <c>enginetrace-&gt;ClipRayToEntity</c> with a mask that carries
    /// <c>CONTENTS_HITBOX</c>, which reaches the entity's <c>TestHitboxes</c>):
    /// a line from <paramref name="start"/> to <paramref name="end"/>, posed
    /// as the engine's ask poses it, so a body answers from its solver's bones.
    /// False for a handle that names no standalone entity or a model with no
    /// hitboxes; otherwise <paramref name="tr"/> holds the answer (a fraction
    /// under 1 is a hit, with its hitbox and physics bone).
    /// </summary>
    public bool TestHitboxes(EntityHandle entity, NVector start, NVector end, int contentsMask, BoneTrace tr)
    {
        if (!Read(_blocks.Block(entity), out EngineBlock read))
        {
            return false;
        }

        tr.StartPos = start;
        tr.EndPos = end;
        tr.Fraction = 1f;
        return TestHitboxes(entity, read, BoneRay.FromPoints(start, end), contentsMask, tr, Now, FrameCount);
    }

    private bool TestHitboxes(EntityHandle entity, in EngineBlock read, in BoneRay ray, int contentsMask, BoneTrace tr,
                              float currentTime, int frameCount)
    {
        HitboxTraces++;
        BoneEntityState state = _states.GetOrCreate(entity);
        BoneCacheKey before = state.CacheKey;
        long started = Stopwatch.GetTimestamp();

        BeginAsk(entity, state);
        _merging = entity;

        NVector collisionOrigin = new(read.Head.Origin.X, read.Head.Origin.Y, read.Head.Origin.Z);
        bool tested = _driver.TestHitboxes(state, PoseOf(read, frameCount), read.Client.HitboxSet,
                                           ray, contentsMask, collisionOrigin, tr,
                                           _surfaceReader, currentTime, ConVars);
        Count(state, started, before);
        return tested;
    }

    /// <inheritdoc/>
    public bool GetAttachment(EntityHandle entity, ReadOnlySpan<byte> block, int attachment,
                              Span<float> attachmentToWorld, float currentTime, int frameCount)
    {
        if (!Read(block, out EngineBlock read))
        {
            return false;
        }

        BoneEntityState state = _states.GetOrCreate(entity);
        BeginAsk(entity, state);
        _merging = entity;

        if (!Attachment(state, read, attachment, currentTime, frameCount, out BoneMatrix3x4 matrix))
        {
            return false;
        }

        MemoryMarshal.Write(MemoryMarshal.AsBytes(attachmentToWorld), in matrix);
        return true;
    }

    /// <inheritdoc/>
    public int LookupAttachment(EntityHandle entity, ReadOnlySpan<byte> block, string name)
    {
        if (string.IsNullOrEmpty(name) || !Read(block, out EngineBlock read))
        {
            return 0;
        }

        return Math.Max(0, BoneLibrary.LookupAttachment(Hdr(read), name));
    }

    /// <inheritdoc/>
    public bool PoseModel(int modelIndex, ReadOnlySpan<float> originAngles, ReadOnlySpan<float> renderToWorld,
                          Span<float> boneToWorld, int boneMask, float currentTime, int frameCount)
    {
        if (originAngles.Length < 6 || renderToWorld.Length < Floats || boneToWorld.Length < Floats)
        {
            return false;
        }

        Span<BoneMatrix3x4> output = MemoryMarshal.Cast<float, BoneMatrix3x4>(boneToWorld);
        BoneMatrix3x4 render = MemoryMarshal.Read<BoneMatrix3x4>(MemoryMarshal.AsBytes(renderToWorld));
        StudioHdr? hdr = modelIndex > 0 ? _models.OpenStudioHdr(modelIndex) : null;
        int posed = 0;

        if (hdr is not null && BoneLibrary.IsPoseable(hdr))
        {
            BoneBuildInputs inputs = default;
            inputs.Reset();
            inputs.Placement.Origin = new NVector(originAngles[0], originAngles[1], originAngles[2]);
            inputs.Placement.Angles = new BoneAngle(originAngles[3], originAngles[4], originAngles[5]);
            inputs.BoneMask = boneMask;
            inputs.CurTime = currentTime;
            inputs.AutoplayIkAlways = true;
            inputs.FrameCount = frameCount;
            inputs.PoseParameter = _zeroPose;
            inputs.AnimThreeWayBlend = ConVars.AnimThreeWayBlend;
            BoneLibrary.ModelFacts(hdr, out inputs.HasAutoplay, null);

            posed = Math.Min(BoneLibrary.BuildBones(hdr, inputs, output), output.Length);
        }

        for (int i = 0; i < output.Length; i++)
        {
            if (i < posed && BoneLibrary.BoneIsWritten(hdr, i, boneMask))
            {
                continue;
            }

            output[i] = render;
        }

        return true;
    }

    private const int Floats = 12;

    /// <inheritdoc/>
    /// <remarks>
    /// The frame's batch poses every skeleton the engine drew the frame
    /// before (asked through SetupBones, or answered from that frame's batch),
    /// at this clock, under the mask its asks have widened to. Left out, and
    /// so still crossing per ask: a merging follower (its bones are its
    /// source's, which may change within the frame) and a ragdoll body (the
    /// solver's bones step within the frame).
    /// </remarks>
    public nint BoneBatch(int frameCount, float currentTime)
    {
        if (!BatchEnabled)
        {
            return 0;
        }

        long started = Stopwatch.GetTimestamp();

        // What native answered from the last batch was drawn in that frame.
        for (int i = 0; i < _batch.Count; i++)
        {
            ClientBoneBatch.Entry entry = _batch.EntryAt(i);

            if (entry.Answered > 0)
            {
                BatchAnswered += entry.Answered;

                if (_states.TryGet(new EntityHandle(entry.Entity), out BoneEntityState? drawn) && drawn is not null)
                {
                    drawn.EngineAskFrame = Math.Max(drawn.EngineAskFrame, _batch.FrameCount);
                }
            }

            if (_flips.TryGetValue(new EntityHandle(entry.Entity), out (int Frame, int Answered) flip)
                && flip.Answered >= 0 && entry.Answered > flip.Answered)
            {
                RagdollFlipStaleAnswers += entry.Answered - flip.Answered;
            }
        }

        // A flip in an earlier frame has been counted; one this frame, before
        // its first ask, is still counted against this frame's asks.
        foreach (KeyValuePair<EntityHandle, (int Frame, int Answered)> flip in _flips)
        {
            if (flip.Value.Frame < frameCount)
            {
                _flips.Remove(flip.Key);
            }
        }

        _batch.Begin(frameCount, currentTime);
        Ragdolls.IClientRagdolls? ragdolls = _ragdolls?.Invoke();

        foreach (KeyValuePair<EntityHandle, BoneEntityState> held in _states)
        {
            EntityHandle entity = held.Key;
            BoneEntityState state = held.Value;

            if (state.EngineAskFrame != frameCount - 1 || state.BoneMergeSource >= 0
                || state.Ragdoll != BoneEntityState.RagdollNone || (ragdolls?.IsRagdoll(entity) ?? false)
                || !Read(_blocks.Block(entity), out EngineBlock block))
            {
                continue;
            }

            BeginManagedAsk(state);
            _merging = entity;

            if (_driver.EnsureBones(state, PoseOf(block, frameCount), state.BoneMaskAsked, currentTime, ConVars))
            {
                _batch.Add(entity, block, state.CacheKey.BoneMask,
                           MemoryMarshal.Cast<BoneMatrix3x4, float>(state.CachedBones.AsSpan()));
            }
        }

        Batches++;
        BatchEntries += _batch.Count;
        BatchTicks += Stopwatch.GetTimestamp() - started;
        return _batch.Hand();
    }

    // A temp entity never sets a pose parameter: stock's tempent poses at zero.
    private readonly float[] _zeroPose = new float[BoneEntityState.MaxPoseParameters];

    /// <summary>
    /// <c>GetAttachment( number, matrix )</c> over <c>EnsureAttachments</c>:
    /// ONE-based, false for an attachment the model lacks.
    /// </summary>
    private bool Attachment(BoneEntityState state, in EngineBlock block, int attachment, float currentTime,
                            int frameCount, out BoneMatrix3x4 matrix)
    {
        matrix = default;
        BoneCacheKey before = state.CacheKey;
        long started = Stopwatch.GetTimestamp();

        bool have = _driver.EnsureAttachments(state, PoseOf(block, frameCount), currentTime, ConVars);
        Count(state, started, before);

        if (!have || attachment < 1 || attachment > state.Attachments.Length)
        {
            return false;
        }

        matrix = state.Attachments[attachment - 1];
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

    // ---- IStandaloneSkeletons: managed's asks -------------------------------------------

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
        BeginManagedAsk(state);
        _merging = entity;

        if (!Attachment(state, block, attachment, Now, FrameCount, out BoneMatrix3x4 matrix))
        {
            // The entity's own place rather than stack fungus (stock's answer).
            origin = block.Head.Origin;
            angles = block.Head.Angles;
            return false;
        }

        BoneAngle a = BoneSetupMath.MatrixAngles(matrix);
        NVector o = matrix.Position;
        origin = new Vector(o.X, o.Y, o.Z);
        angles = new QAngle(a.Pitch, a.Yaw, a.Roll);
        return true;
    }

    /// <inheritdoc/>
    public int LookupAttachment(EntityHandle entity, string name)
    {
        if (string.IsNullOrEmpty(name) || !Read(_blocks.Block(entity), out EngineBlock block))
        {
            return 0;
        }

        return Math.Max(0, BoneLibrary.LookupAttachment(Hdr(block), name));
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

        StudioHdr? hdr = Hdr(block);

        if (hdr is null)
        {
            return null;
        }

        BoneCacheKey before = state.CacheKey;
        long started = Stopwatch.GetTimestamp();

        BeginManagedAsk(state);
        _merging = entity;
        bool built = _driver.EnsureBones(state, PoseOf(block, FrameCount), BoneLibrary.BoneUsedByAnything, Now,
                                         ConVars);
        Count(state, started, before);

        int bones = state.CachedBones.Length;

        if (!built || bones <= 0)
        {
            return null;
        }

        BonePlacement placement = default;
        placement.Reset();
        placement.Origin = new NVector(block.Head.Origin.X, block.Head.Origin.Y, block.Head.Origin.Z);
        placement.Angles = new BoneAngle(block.Head.Angles.Pitch, block.Head.Angles.Yaw, block.Head.Angles.Roll);
        placement.ModelScale = state.ModelScale;

        float[] entityToWorld = new float[StandaloneBones.FloatsPerMatrix];
        Write(BoneLibraryMath.PlacementToWorld(placement), entityToWorld, 0);

        float[] matrices = new float[bones * StandaloneBones.FloatsPerMatrix];
        uint[] written = new uint[(bones + 31) / 32];

        for (int i = 0; i < bones; i++)
        {
            Write(state.CachedBones[i], matrices, i * StandaloneBones.FloatsPerMatrix);

            if (BoneLibrary.BoneIsWritten(hdr, i, state.BoneMaskAsked))
            {
                written[i / 32] |= 1u << (i % 32);
            }
        }

        return new StandaloneBones(bones, matrices, written, entityToWorld);
    }

    private static void Write(in BoneMatrix3x4 m, float[] into, int at)
    {
        MemoryMarshal.Write(MemoryMarshal.AsBytes(into.AsSpan(at, StandaloneBones.FloatsPerMatrix)), in m);
    }

    /// <summary>A bone's name in the model the entity wears, or empty (the reports).</summary>
    public string BoneName(EntityHandle entity, int bone) =>
        Read(_blocks.Block(entity), out EngineBlock block) && Hdr(block) is { } hdr && bone >= 0 && bone < hdr.NumBones
            ? hdr.Bone(bone).Name
            : string.Empty;

    /// <summary>Whether a bone is placed by the model's IK chains or is procedural (the reports).</summary>
    public string BoneRole(EntityHandle entity, int bone)
    {
        if (!Read(_blocks.Block(entity), out EngineBlock block) || Hdr(block) is not { } hdr || bone < 0 || bone >= hdr.NumBones)
            return string.Empty;

        for (int c = 0; c < hdr.NumIkChains; c++)
        {
            MstudioIkChain chain = hdr.IkChain(c);
            for (int l = 0; l < chain.NumLinks; l++)
                if (chain.Link(l).Bone == bone)
                    return "ik";
        }

        return ProceduralBoneRule.Kind(hdr.BoneFlags(bone), hdr.Bone(bone).ProcType).ToString();
    }

    /// <inheritdoc/>
    public bool IsPoseable(EntityHandle entity) =>
        Read(_blocks.Block(entity), out EngineBlock block) && BoneLibrary.IsPoseable(Hdr(block));

    /// <inheritdoc/>
    public string StudioName(EntityHandle entity) =>
        Read(_blocks.Block(entity), out EngineBlock block) && ModelOf(block) > 0
            ? _models.StudioName(ModelOf(block))
            : string.Empty;

    /// <inheritdoc/>
    public bool SetSequence(EntityHandle entity, int sequence, float playbackRate, bool restart)
    {
        if (!Find(entity, out EngineBlock block, out BoneEntityState state))
        {
            return false;
        }

        int was = state.Sequence;
        float rate = state.PlaybackRate, start = state.AnimTime, duration = state.SeqDuration;
        bool looping = state.SeqLooping;

        _driver.SetPlayingSequence(state, ModelOf(block), sequence, playbackRate, restart, Now);
        _batch.Drop(entity);

        // Native's per-frame cycle reads the clock; it is told only when it moved.
        if ((was != state.Sequence || rate != state.PlaybackRate || start != state.AnimTime
            || duration != state.SeqDuration || looping != state.SeqLooping))
        {
            _store.Store(entity, state.Sequence, state.PlaybackRate, state.AnimTime, state.SeqDuration,
                         state.SeqLooping);
        }

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

        _batch.Drop(entity);

        switch (state.SetModelScale(scale))
        {
            case ModelScaleChange.Refused:
                _log.Warning($"[SourceSharp] model scale {scale} refused on entity {block.Head.Handle & 0xFFF}: a scale " +
                             "of zero or less is a model drawn as nothing and picked by nothing\n");
                return SkeletonScaleChange.Refused;

            case ModelScaleChange.Unchanged:
                return SkeletonScaleChange.Unchanged;

            default:
                return SkeletonScaleChange.Changed;
        }
    }

    // ---- IClientStandaloneSkeletons ------------------------------------------------------

    /// <inheritdoc/>
    public void SetAnimLayer(EntityHandle entity, int slot, int sequence, float weight, float playbackRate, int order,
                             bool restart)
    {
        if (Find(entity, out EngineBlock block, out BoneEntityState state))
        {
            _driver.SetAnimLayer(state, ModelOf(block), slot, sequence, weight, playbackRate, order, restart, Now);
            _batch.Drop(entity);
        }
    }

    /// <inheritdoc/>
    public int AnimLayer(EntityHandle entity, int slot, out int sequence, out float weight, out int order,
                         out float cycle)
    {
        sequence = -1;
        weight = 0f;
        order = 0;
        cycle = 0f;

        if (!Find(entity, out _, out BoneEntityState state))
        {
            return 0;
        }

        if (slot >= 0 && slot < AnimLayerRule.MaxLayers)
        {
            sequence = state.Layers[slot].Sequence;
            weight = state.Layers[slot].Weight;
            order = state.Layers[slot].Order;
            cycle = state.AnimLayerCycle(slot, Now);
        }

        return state.ActiveAnimLayers();
    }

    /// <inheritdoc/>
    public bool SequenceState(EntityHandle entity, out int sequence, out float cycle, out float duration,
                              out bool looping, out int starts)
    {
        sequence = 0;
        cycle = 0f;
        duration = 0f;
        looping = false;
        starts = 0;

        if (!Find(entity, out _, out BoneEntityState state))
        {
            return false;
        }

        sequence = state.Sequence;
        cycle = state.SequenceCycle(Now);
        duration = state.SeqDuration;
        looping = state.SeqLooping;
        starts = state.SequenceStarts;
        return true;
    }

    /// <inheritdoc/>
    public bool SequenceInfo(EntityHandle entity, out float duration, out bool looping)
    {
        duration = 0f;
        looping = false;

        if (!Find(entity, out _, out BoneEntityState state))
        {
            return false;
        }

        duration = state.SeqDuration;
        looping = state.SeqLooping;
        return true;
    }

    /// <inheritdoc/>
    public float SequenceGroundSpeed(EntityHandle entity, int sequence)
    {
        if (sequence < 0 || !Find(entity, out EngineBlock block, out BoneEntityState state))
        {
            return 0f;
        }

        StudioHdr? hdr = Hdr(block);

        if (hdr is null || sequence >= hdr.NumSequences)
        {
            return 0f;
        }

        // A real array for the movement walk: the entity's pose once its
        // defaults were read for this model, the model's defaults otherwise.
        float[]? pose = state.PoseParametersForDuration(ModelOf(block));

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

    /// <inheritdoc/>
    public int PoseParameterCount(EntityHandle entity) =>
        Read(_blocks.Block(entity), out EngineBlock block) && Hdr(block) is { } hdr ? hdr.NumPoseParameters : 0;

    /// <inheritdoc/>
    public int LookupPoseParameter(EntityHandle entity, string name)
    {
        if (string.IsNullOrEmpty(name) || !Read(_blocks.Block(entity), out EngineBlock block) || Hdr(block) is not { } hdr)
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
        Read(_blocks.Block(entity), out EngineBlock block) && Hdr(block) is { } hdr
        && parameter >= 0 && parameter < hdr.NumPoseParameters
            ? hdr.PoseParameter(parameter).Name
            : string.Empty;

    /// <inheritdoc/>
    public bool PoseParameterRange(EntityHandle entity, int parameter, out float start, out float end)
    {
        start = 0f;
        end = 0f;

        if (!Read(_blocks.Block(entity), out EngineBlock block) || Hdr(block) is not { } hdr
            || parameter < 0 || parameter >= hdr.NumPoseParameters)
        {
            return false;
        }

        MstudioPoseParamDesc desc = hdr.PoseParameter(parameter);
        start = desc.Start;
        end = desc.End;
        return true;
    }

    /// <inheritdoc/>
    public float SetPoseParameter(EntityHandle entity, int parameter, float value)
    {
        if (parameter < 0 || parameter >= BoneEntityState.MaxPoseParameters
            || !Find(entity, out EngineBlock block, out BoneEntityState state)
            || Hdr(block) is not { } hdr || parameter >= hdr.NumPoseParameters)
        {
            return 0f;
        }

        // The defaults first, or a set that lands before the first build is
        // overwritten by them a frame later.
        _driver.RefreshModelPoseState(state, ModelOf(block));

        float normalised = 0f;
        float stored = BoneSetupQuery.SetPoseParameter(hdr, parameter, value, ref normalised);

        if (state.PoseParameter[parameter] != normalised)
        {
            state.PoseParameter[parameter] = normalised;
            state.PoseGeneration++;
            _batch.Drop(entity);
        }

        return stored;
    }

    /// <inheritdoc/>
    public float GetPoseParameter(EntityHandle entity, int parameter)
    {
        if (parameter < 0 || parameter >= BoneEntityState.MaxPoseParameters
            || !Find(entity, out EngineBlock block, out BoneEntityState state)
            || Hdr(block) is not { } hdr || parameter >= hdr.NumPoseParameters)
        {
            return 0f;
        }

        return BoneSetupQuery.GetPoseParameter(hdr, parameter, state.PoseParameter[parameter]);
    }

    /// <inheritdoc/>
    public void SetBoneMergeSource(EntityHandle entity, int sourceEntityIndex)
    {
        if (Find(entity, out _, out BoneEntityState state))
        {
            state.SetBoneMergeSource(sourceEntityIndex);
            _batch.Drop(entity);
        }
    }

    /// <inheritdoc/>
    public int ProceduralBones(EntityHandle entity, out int computed, out int jiggle, out int ikChains)
    {
        computed = 0;
        jiggle = 0;
        ikChains = 0;

        if (!Read(_blocks.Block(entity), out EngineBlock block) || Hdr(block) is not { } hdr || hdr.NumBones <= 0)
        {
            return 0;
        }

        ikChains = hdr.NumIkChains;

        for (int i = 0; i < hdr.NumBones; i++)
        {
            switch (ProceduralBoneRule.Kind(hdr.BoneFlags(i), hdr.Bone(i).ProcType))
            {
                case ProceduralBoneKind.Computed:
                    computed++;
                    break;

                case ProceduralBoneKind.Jiggle:
                    jiggle++;
                    break;
            }
        }

        return hdr.NumBones;
    }

    // ---- lifetime ---------------------------------------------------------------------

    /// <summary>
    /// The entity became a jointed body (ClientRagdolls.Become): from now its
    /// bones are the solver's, so an entry the frame's batch posed ahead from
    /// its animation answers nothing more. Stock's BecomeRagdollOnClient draws
    /// the ragdoll from the moment it exists, within the frame too.
    /// </summary>
    public void BecameRagdoll(EntityHandle entity)
    {
        bool held = _batch.TryGet(entity, out ClientBoneBatch.Entry entry);
        _batch.Drop(entity);
        RagdollFlips++;
        RagdollFlipEntries += held ? 1 : 0;
        _flips[entity] = (_environment.Globals.IsAvailable ? _environment.Globals.FrameCount : _batch.FrameCount,
                          held ? entry.Answered : -1);
    }

    /// <summary>The entity is gone: its skeleton goes with it.</summary>
    public void Forget(EntityHandle entity)
    {
        _batch.Drop(entity);
        _states.Remove(entity);
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
        _batch.DropAll();
        return _states.InvalidateAll();
    }

    /// <summary>Level shutdown: every skeleton and every model image goes.</summary>
    public void OnLevelShutdown()
    {
        _batch.DropAll();
        _states.Clear();
        _flips.Clear();
        _models.Clear();
        _surfaceIndices.Clear();
    }

    // ---- across a client hot reload ---------------------------------------------------

    /// <summary>
    /// What the next build cannot recompute: each entity's merge source,
    /// scale, pose, layer table and clocks. The bones themselves are rebuilt
    /// at the next ask; an IK solver's planted feet and a jiggle spring's
    /// swing start again (the native members did not survive either: the
    /// carried state is what C# set).
    /// </summary>
    public void Save(ReloadWriter writer)
    {
        List<(EntityHandle Handle, BoneEntityState State)> held = _states.All();
        writer.Write(held.Count);

        foreach ((EntityHandle handle, BoneEntityState s) in held)
        {
            writer.Write((int)handle.Value);
            writer.Write(s.BoneMergeSource);
            writer.Write(s.ModelScale);
            writer.Write(s.PoseDefaultsFor);
            writer.Write(s.ModelHasAutoplay);
            writer.Write(s.ModelHasJiggleBones);
            writer.Write(s.ModelIkChains);
            writer.Write(s.PoseGeneration);

            for (int i = 0; i < BoneEntityState.MaxPoseParameters; i++)
            {
                writer.Write(s.PoseParameter[i]);
            }

            writer.Write(s.Sequence);
            writer.Write(s.PlaybackRate);
            writer.Write(s.AnimTime);
            writer.Write(s.SeqDuration);
            writer.Write(s.SeqLooping);
            writer.Write(s.SequenceStarts);
            writer.Write(s.HeldCycle.HasValue);
            writer.Write(s.HeldCycle ?? 0f);
            writer.Write(s.LayerGeneration);

            for (int i = 0; i < AnimLayerRule.MaxLayers; i++)
            {
                writer.Write(s.Layers[i].Sequence);
                writer.Write(s.Layers[i].Weight);
                writer.Write(s.Layers[i].Order);
                writer.Write(s.LayerStart[i]);
                writer.Write(s.LayerRate[i]);
                writer.Write(s.LayerDuration[i]);
                writer.Write(s.LayerLooping[i]);
            }
        }
    }

    /// <inheritdoc/>
    public void Restore(ReloadReader reader)
    {
        _batch.DropAll();
        int count = reader.ReadInt();

        for (int n = 0; n < count; n++)
        {
            BoneEntityState s = _states.GetOrCreate(new EntityHandle(unchecked((uint)reader.ReadInt())));
            s.BoneMergeSource = reader.ReadInt();
            s.ModelScale = reader.ReadFloat();
            s.PoseDefaultsFor = reader.ReadInt();
            s.ModelHasAutoplay = reader.ReadBool();
            s.ModelHasJiggleBones = reader.ReadBool();
            s.ModelIkChains = reader.ReadInt();
            s.PoseGeneration = reader.ReadInt();

            for (int i = 0; i < BoneEntityState.MaxPoseParameters; i++)
            {
                s.PoseParameter[i] = reader.ReadFloat();
            }

            s.Sequence = reader.ReadInt();
            s.PlaybackRate = reader.ReadFloat();
            s.AnimTime = reader.ReadFloat();
            s.SeqDuration = reader.ReadFloat();
            s.SeqLooping = reader.ReadBool();
            s.SequenceStarts = reader.ReadInt();
            bool held = reader.ReadBool();
            float heldCycle = reader.ReadFloat();
            s.HeldCycle = held ? heldCycle : null;
            s.LayerGeneration = reader.ReadInt();

            for (int i = 0; i < AnimLayerRule.MaxLayers; i++)
            {
                s.Layers[i].Sequence = reader.ReadInt();
                s.Layers[i].Weight = reader.ReadFloat();
                s.Layers[i].Order = reader.ReadInt();
                s.LayerStart[i] = reader.ReadFloat();
                s.LayerRate[i] = reader.ReadFloat();
                s.LayerDuration[i] = reader.ReadFloat();
                s.LayerLooping[i] = reader.ReadBool();
            }

            // The facts were carried with the model they were read for, so
            // the next ask does not reset the pose to the model's defaults.
            s.InvalidateBoneCache();
        }
    }
}
