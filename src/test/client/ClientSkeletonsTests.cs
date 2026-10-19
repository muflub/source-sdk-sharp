using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared.Bones;

using Xunit;

using NVector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="ClientSkeletons"/> (plan physics_bones_native_zero
/// steps 1.1-1.2, client half): the engine's skeleton asks and managed's,
/// answered from an entity's engine block, hand-derived from what
/// C_StandaloneEntity answered at the 1.0b base (shell_standalone_client.cpp
/// EnsureBones 1619-1963, SetupBones 2026-2046, MergeBonesFrom 2066-2179,
/// the attachment, pose and sequence members).
/// </summary>
public unsafe class ClientSkeletonsTests
{
    private const int Model = 3;
    private const int UsedByAnything = 0x0007FF00;
    private const int Floats = 12;
    private static readonly EntityHandle Entity = new(0x00010005u);
    private static readonly EntityHandle Source = new(0x00010006u);
    private static readonly EntityHandle Other = new(0x00010007u);

    private sealed class Blocks : IStandaloneBlocks
    {
        public Dictionary<EntityHandle, byte[]> Held { get; } = [];

        public Span<byte> Block(EntityHandle entity) =>
            Held.TryGetValue(entity, out byte[]? b) ? b : Span<byte>.Empty;
    }

    private sealed class Clocks : IClientSequenceStore
    {
        public List<(EntityHandle Entity, int Sequence, float Rate, float Start, float Duration, bool Looping)> Stored { get; } = [];

        public void Store(EntityHandle entity, int sequence, float playbackRate, float animTime, float duration, bool looping)
            => Stored.Add((entity, sequence, playbackRate, animTime, duration, looping));
    }

    private sealed class Memory : IStudioMemory
    {
        public Dictionary<int, byte[]> Models { get; } = [];

        public byte[]? ModelImage(int modelIndex) => Models.TryGetValue(modelIndex, out byte[]? b) ? b : null;
        public byte[]? ModelAnimBlock(int modelIndex, int block) => null;
        public byte[]? IncludeImage(int modelIndex, string includeName) => null;
        public byte[]? IncludeAnimBlock(int modelIndex, string includeName, int block) => null;
    }

    private sealed class Environment : IEngineEnvironment, IDisposable
    {
        private readonly CGlobalVarsRaw* _globals = (CGlobalVarsRaw*)NativeMemory.AllocZeroed((nuint)sizeof(CGlobalVarsRaw));

        public float Now
        {
            set => _globals->curtime = value;
        }

        public int FrameCount
        {
            set => _globals->framecount = value;
        }

        public nint GetInterface(string name) => 0;

        public Globals Globals => new(_globals);

        public void Dispose() => NativeMemory.Free(_globals);
    }

    private sealed class Props : IVtablePhysicsSurfaceProps
    {
        public int ParseSurfaceData(string fileName, string text) => 0;
        public int SurfacePropCount() => 0;
        public string? PropName(int index) => null;
        public int GameMaterial(int surfaceProps) => 0;
        public int SurfaceIndex(string name) => 42;
    }

    /// <summary>root -> arm -> hand, a looping ramp on the root, "muzzle" on
    /// the hand, one solid hitbox on the hand (ServerSkeletonsTests' model).</summary>
    private static byte[] Chain()
    {
        StudioHdrBuilder b = new();
        b.ModelName = "test/chain.mdl";
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "arm", Parent = 0, Flags = UsedByAnything, Pos = new NVector(0f, 0f, 10f) });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "hand", Parent = 1, Flags = UsedByAnything, Pos = new NVector(5f, 0f, 0f) });

        short[] pos0 = [3 | (3 << 8), 0, 10, 20];
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "ramp", NumFrames = 3, Fps = 30f,
            Bones = { new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_ANIMPOS, Pos0 = pos0 } },
        });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "ramp", AnimTable = [0], Flags = StudioConstants.STUDIO_LOOPING, Weights = [1f, 1f, 1f] });

        BoneMatrix3x4 local = BoneMatrix3x4.Identity;
        local.Position = new NVector(1f, 2f, 3f);
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "muzzle", LocalBone = 2, Local = local });
        b.HitboxSets.Add(new StudioHdrBuilder.HitboxSetDef
        {
            Name = "default",
            Boxes = { new StudioHdrBuilder.HitboxDef { Bone = 2, Min = new NVector(-2f, -2f, -2f), Max = new NVector(2f, 2f, 2f) } },
        });

        byte[] image = b.Build();

        byte[] name = "flesh\0"u8.ToArray();
        byte[] grown = new byte[image.Length + name.Length];
        image.CopyTo(grown, 0);
        name.CopyTo(grown, image.Length);
        int bones = BinaryPrimitives.ReadInt32LittleEndian(grown.AsSpan(160, 4));
        for (int i = 0; i < 3; i++)
            BinaryPrimitives.WriteInt32LittleEndian(grown.AsSpan(bones + 216 * i + 180, 4), BoneSetupTrace.ContentsSolid);
        BinaryPrimitives.WriteInt32LittleEndian(grown.AsSpan(bones + 216 * 2 + 176, 4), image.Length - (bones + 216 * 2));
        return grown;
    }

    private static byte[] Block(NVector origin, int model = Model, float worldX = 0f)
    {
        EngineBlock block = default;
        block.Head.ModelIndex = model;
        block.Head.Origin = new Vector(origin.X, origin.Y, origin.Z);
        block.Head.Angles = new QAngle(0f, 0f, 0f);
        block.Head.WorldTransform = new Transform(1f, 0f, 0f, worldX, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f);
        block.Client.RenderOrigin = block.Head.Origin;

        byte[] bytes = new byte[EngineBlock.SizeBytes];
        MemoryMarshal.Write(bytes, in block);
        return bytes;
    }

    /// <summary>
    /// The client's ragdolls as the skeleton asks them: one body's solver bones
    /// (RagdollGetBoneMatrix's answer), or none.
    /// </summary>
    private sealed class SolverBodies : SourceSharp.Client.Ragdolls.IClientRagdolls
    {
        public EntityHandle Owner = EntityHandle.None;
        public BoneMatrix3x4[] Bones = [];
        public int[] Owned = [];

        public int Count => Owner == EntityHandle.None ? 0 : 1;

        public bool Become(EntityHandle owner, Vector force, int forceBone,
                           out SourceSharp.Client.Ragdolls.RagdollRefusal refusal)
        {
            refusal = SourceSharp.Client.Ragdolls.RagdollRefusal.None;
            return false;
        }

        public bool IsRagdoll(EntityHandle owner) => owner == Owner;

        public uint Serial(EntityHandle owner) => owner == Owner ? 1u : 0u;

        public RagdollState State(EntityHandle owner, out Vector origin)
        {
            origin = Vector.Zero;
            return RagdollState.None;
        }

        public bool ApplyForceCenter(EntityHandle owner, Vector force) => false;

        public IReadOnlyCollection<EntityHandle> Owners => [];

        public nint ElementObject(EntityHandle owner, int physicsBone) => 0;

        public int WriteBones(EntityHandle owner, Span<BoneMatrix3x4> bones, Span<int> owned)
        {
            if (owner != Owner)
                return -1;

            for (int i = 0; i < Owned.Length; i++)
            {
                owned[i] = Owned[i];
                bones[Owned[i]] = Bones[Owned[i]];
            }

            return Owned.Length;
        }

        public bool Destroy(EntityHandle owner) => false;

        public int DestroyAll() => 0;

        public int StepPoses() => 0;

        /// <summary>The body's root (bone 0) at <paramref name="root"/>, the solver's only element.</summary>
        public void Root(EntityHandle owner, BoneMatrix3x4 root)
        {
            Owner = owner;
            Bones = new BoneMatrix3x4[Floats * 3];
            Bones[0] = root;
            Owned = [0];
        }
    }

    private sealed class Rig : IDisposable
    {
        public Blocks Blocks { get; } = new();
        public Clocks Store { get; } = new();
        public Memory Memory { get; } = new();
        public Environment Environment { get; } = new();
        public RecordingLog Log { get; } = new();
        public Dictionary<int, EntityHandle> Indices { get; } = [];
        public ClientSkeletons Skeletons { get; }
        public SolverBodies Ragdolls { get; } = new();

        public Rig()
        {
            Memory.Models[Model] = Chain();
            Blocks.Held[Entity] = Block(new NVector(100f, 0f, 0f), worldX: 100f);
            Indices[5] = Entity;
            Skeletons = new ClientSkeletons(Blocks, Store, new StudioModels(Memory),
                                            i => Indices.TryGetValue(i, out EntityHandle h) ? h : null,
                                            Environment, Log, null, new Props(), () => Ragdolls);
        }

        public bool Setup(EntityHandle entity, float[] output, int mask = UsedByAnything, float time = 1f) =>
            Skeletons.SetupBones(entity, Blocks.Held[entity], output, mask, time, 7);

        public void Dispose() => Environment.Dispose();
    }

    private static NVector At(float[] bones, int bone) =>
        new(bones[bone * Floats + 3], bones[bone * Floats + 7], bones[bone * Floats + 11]);

    // ---- SetupBones: the engine's array, written in place ---------------------

    [Fact]
    public void SetupBonesWritesThePosedSkeletonIntoTheEnginesArray()
    {
        using Rig rig = new();
        float[] output = new float[4 * Floats];

        Assert.True(rig.Setup(Entity, output));

        // Cycle 0: the root at the entity origin, the hand +5 x and +10 z off it.
        Assert.Equal(new NVector(105f, 0f, 10f), At(output, 2));
    }

    [Fact]
    public void SetupBonesPadsEverySlotPastTheModelsBonesWithTheEntityTransform()
    {
        using Rig rig = new();
        float[] output = new float[5 * Floats];

        rig.Setup(Entity, output);

        // The block's world transform carries x 100 in its translation column.
        Assert.Equal((100f, 100f), (At(output, 3).X, At(output, 4).X));
    }

    [Fact]
    public void AnEntityWithNoStudioModelGetsItsTransformInEverySlotAndTrue()
    {
        using Rig rig = new();
        rig.Blocks.Held[Other] = Block(NVector.Zero, model: 9, worldX: 7f);
        float[] output = new float[2 * Floats];

        Assert.True(rig.Setup(Other, output));
        Assert.Equal((7f, 7f), (At(output, 0).X, At(output, 1).X));
    }

    [Fact]
    public void AFillOnlyAskAnswersWhetherThereAreBones()
    {
        using Rig rig = new();
        rig.Blocks.Held[Other] = Block(NVector.Zero, model: 9);

        Assert.Equal((true, false), (rig.Setup(Entity, []), rig.Setup(Other, [])));
    }

    [Fact]
    public void ASecondAskAtTheSamePoseIsTheCacheAndBuildsNothing()
    {
        using Rig rig = new();
        float[] output = new float[3 * Floats];

        rig.Setup(Entity, output);
        rig.Setup(Entity, output);

        Assert.Equal((2L, 1L), (rig.Skeletons.Asks, rig.Skeletons.Builds));
    }

    // ---- ragdoll: the solver's bones, asked of the ragdolls ----------------------

    [Fact]
    public void ARagdollBodysOwnedBonesAreTheSolversAndTheRestHangOffThem()
    {
        using Rig rig = new();
        BoneMatrix3x4 root = BoneMatrix3x4.Identity;
        root.Position = new NVector(0f, 50f, 0f);
        rig.Ragdolls.Root(Entity, root);
        float[] output = new float[3 * Floats];

        rig.Skeletons.SetupBones(Entity, rig.Blocks.Held[Entity], output, UsedByAnything, 1f, 7);

        // The root is where the solver put it; the hand follows it.
        Assert.Equal((new NVector(0f, 50f, 0f), new NVector(5f, 50f, 10f)), (At(output, 0), At(output, 2)));
    }

    [Fact]
    public void ARagdollNeverAnswersFromTheCache()
    {
        using Rig rig = new();
        float[] output = new float[3 * Floats];
        BoneMatrix3x4 root = BoneMatrix3x4.Identity;

        foreach (float y in new[] { 50f, 60f })
        {
            root.Position = new NVector(0f, y, 0f);
            rig.Ragdolls.Root(Entity, root);
            rig.Skeletons.SetupBones(Entity, rig.Blocks.Held[Entity], output, UsedByAnything, 1f, 7);
        }

        // Nothing in the key moved; the solver did, and the second ask shows it.
        Assert.Equal(60f, At(output, 0).Y);
    }

    // ---- the merge: the source is this class's own skeleton --------------------

    [Fact]
    public void AMergedFollowerHoldsTheSourcesMatricesForEveryBoneItNames()
    {
        using Rig rig = new();
        rig.Blocks.Held[Source] = Block(new NVector(0f, 300f, 0f));
        rig.Indices[6] = Source;
        rig.Skeletons.SetBoneMergeSource(Entity, 6);
        float[] output = new float[3 * Floats];

        rig.Setup(Entity, output);

        Assert.Equal(new NVector(5f, 300f, 10f), At(output, 2));
    }

    [Fact]
    public void AMergeOntoItselfKeepsTheFollowersOwnPose()
    {
        using Rig rig = new();
        rig.Skeletons.SetBoneMergeSource(Entity, 5);
        float[] output = new float[3 * Floats];

        rig.Setup(Entity, output);

        Assert.Equal(new NVector(105f, 0f, 10f), At(output, 2));
    }

    [Fact]
    public void AMergeSourceNobodyHoldsKeepsTheFollowersOwnPose()
    {
        using Rig rig = new();
        rig.Skeletons.SetBoneMergeSource(Entity, 40);
        float[] output = new float[3 * Floats];

        rig.Setup(Entity, output);

        Assert.Equal(new NVector(105f, 0f, 10f), At(output, 2));
    }

    // ---- the whole-skeleton merge (plan_physics_bones_perf A1) --------------------------
    //
    // A follower the source names every bone of is the source's skeleton and
    // builds no pose of its own; the source answers from its cache when it
    // already stands at the clock. These hold that the follower is never a
    // stale copy: every ask reads the source as it is NOW.

    private static Rig MergedPair()
    {
        Rig rig = new();
        rig.Blocks.Held[Source] = Block(new NVector(0f, 300f, 0f));
        rig.Indices[6] = Source;
        rig.Skeletons.SetBoneMergeSource(Entity, 6);
        return rig;
    }

    [Fact]
    public void AWhollyMergedFollowerIsTheSourcesSkeletonBoneForBone()
    {
        using Rig rig = MergedPair();
        float[] follower = new float[3 * Floats], source = new float[3 * Floats];

        rig.Setup(Entity, follower);
        rig.Setup(Source, source);

        Assert.Equal(source, follower);
        Assert.Equal(new NVector(5f, 300f, 10f), At(follower, 2));
    }

    [Fact]
    public void TheSourceIsPosedOnceWhenItsFollowerAndItAreAskedAtOneClock()
    {
        using Rig rig = MergedPair();
        float[] output = new float[3 * Floats];

        rig.Setup(Entity, output);   // the follower first: the source builds inside it
        rig.Skeletons.States.TryGet(Source, out BoneEntityState? s);
        BoneCacheKey built = s!.CacheKey;
        long builds = rig.Skeletons.Builds;
        rig.Setup(Source, output);   // the source's own ask, the same clock

        Assert.Equal(builds, rig.Skeletons.Builds);
        Assert.True(MemoryMarshal.AsBytes(new ReadOnlySpan<BoneCacheKey>(in built))
            .SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<BoneCacheKey>(in s.CacheKey))));
    }

    [Fact]
    public void AWhollyMergedFollowerFollowsASourceThatTeleportedAtTheSameClock()
    {
        using Rig rig = MergedPair();
        float[] output = new float[3 * Floats];

        rig.Setup(Entity, output);
        rig.Blocks.Held[Source] = Block(new NVector(0f, 900f, 0f));
        rig.Setup(Entity, output);

        Assert.Equal(new NVector(5f, 900f, 10f), At(output, 2));
    }

    [Fact]
    public void AWhollyMergedFollowerFollowsASourceWhoseSequenceRestarted()
    {
        using Rig rig = MergedPair();
        float[] output = new float[3 * Floats];
        rig.Environment.Now = 0f;
        rig.Skeletons.SetSequence(Source, 0, 1f, restart: true);

        rig.Setup(Entity, output, time: 1f / 30f);   // a frame into the ramp: the root at x 10
        float before = At(output, 0).X;
        rig.Environment.Now = 1f / 30f;
        rig.Skeletons.SetSequence(Source, 0, 1f, restart: true);
        rig.Setup(Entity, output, time: 1f / 30f);   // restarted now: cycle 0, the root at x 0

        Assert.Equal((10f, 0f), (before, At(output, 0).X));
    }

    [Fact]
    public void AFollowerWhoseSourceStopsNamingEveryBoneBuildsItsOwnPoseAgain()
    {
        using Rig rig = MergedPair();
        float[] output = new float[3 * Floats];
        rig.Setup(Entity, output);

        // The source now wears a model naming only "root": the arm and the
        // hand are the follower's own again, hanging off the merged root.
        StudioHdrBuilder b = new() { ModelName = "test/rootonly.mdl" };
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "idle", AnimTable = [0], Flags = 0, Weights = [1f] });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "idle", NumFrames = 1, Fps = 30f });
        rig.Memory.Models[8] = b.Build();
        rig.Blocks.Held[Source] = Block(new NVector(0f, 300f, 0f), model: 8);
        rig.Setup(Entity, output, time: 2f);

        Assert.Equal((new NVector(0f, 300f, 0f), new NVector(105f, 0f, 10f)), (At(output, 0), At(output, 2)));
    }

    // ---- the frame's batch (plan_physics_bones_perf A2) -------------------------------

    private static bool Batched(Rig rig, EntityHandle entity, out ClientBoneBatch.Entry entry) =>
        rig.Skeletons.Batch.TryGet(entity, out entry);

    /// <summary>What native does when it answers an ask from an entry.</summary>
    private static void Answer(Rig rig, EntityHandle entity)
    {
        var header = (ClientBoneBatch.Header*)rig.Skeletons.Batch.Address;
        short at = header->SlotToEntry[(int)(entity.Value & (ClientBoneBatch.Slots - 1))];
        header->EntryTable[at].Answered++;
    }

    [Fact]
    public void TheBatchPosesWhatTheEngineDrewTheFrameBefore()
    {
        using Rig rig = new();
        rig.Blocks.Held[Other] = Block(new NVector(0f, 50f, 0f));
        float[] drawn = new float[3 * Floats];
        rig.Skeletons.SetupBones(Entity, rig.Blocks.Held[Entity], drawn, UsedByAnything, 1f, 6);
        rig.Skeletons.SetupBones(Other, rig.Blocks.Held[Other], new float[3 * Floats], UsedByAnything, 1f, 4);

        nint table = rig.Skeletons.BoneBatch(7, 1f);

        Assert.Equal(rig.Skeletons.Batch.Address, table);
        Assert.True(Batched(rig, Entity, out ClientBoneBatch.Entry entry));
        Assert.False(Batched(rig, Other, out _)); // drawn three frames ago, not last frame
        Assert.Equal(drawn, rig.Skeletons.Batch.Matrices(entry).ToArray());
    }

    [Fact]
    public void AnEntryCarriesTheInputsItWasPosedFrom()
    {
        using Rig rig = new();
        rig.Skeletons.SetupBones(Entity, rig.Blocks.Held[Entity], new float[3 * Floats], 0x400, 1f, 6);

        rig.Skeletons.BoneBatch(7, 1.5f);

        Assert.True(Batched(rig, Entity, out ClientBoneBatch.Entry e));
        Assert.Equal((Entity.Value, Model, 100f, 0x400, 3), (e.Entity, e.ModelIndex, e.Origin.X, e.BoneMask, e.Bones));
        var header = (ClientBoneBatch.Header*)rig.Skeletons.Batch.Address;
        Assert.Equal((7, 1.5f), (header->FrameCount, header->CurrentTime));
    }

    [Fact]
    public void AnEntityNativeAnsweredFromTheBatchIsPosedAgainNextFrame()
    {
        using Rig rig = new();
        rig.Blocks.Held[Other] = Block(new NVector(0f, 50f, 0f));
        rig.Skeletons.SetupBones(Entity, rig.Blocks.Held[Entity], new float[3 * Floats], UsedByAnything, 1f, 6);
        rig.Skeletons.SetupBones(Other, rig.Blocks.Held[Other], new float[3 * Floats], UsedByAnything, 1f, 6);
        rig.Skeletons.BoneBatch(7, 1f);
        Answer(rig, Entity); // frame 7 drew Entity from the batch, never crossing; Other was not drawn

        rig.Skeletons.BoneBatch(8, 1.1f);

        Assert.True(Batched(rig, Entity, out _));
        Assert.False(Batched(rig, Other, out _));
        Assert.Equal(1L, rig.Skeletons.BatchAnswered);
    }

    [Fact]
    public void ChangingASkeletonsStateDropsItsEntry()
    {
        using Rig rig = new();
        var changes = new (string What, Action Change)[]
        {
            ("sequence", () => rig.Skeletons.SetSequence(Entity, 0, 2f, restart: true)),
            ("scale", () => rig.Skeletons.SetModelScale(Entity, 2f)),
            ("layer", () => rig.Skeletons.SetAnimLayer(Entity, 0, 0, 1f, 1f, 0, restart: true)),
            ("merge", () => rig.Skeletons.SetBoneMergeSource(Entity, 6)),
            ("forget", () => rig.Skeletons.Forget(Entity)),
            ("model cache", () => rig.Skeletons.InvalidateMdlCache()),
            ("level", () => rig.Skeletons.OnLevelShutdown()),
        };

        foreach ((string what, Action change) in changes)
        {
            rig.Skeletons.SetupBones(Entity, rig.Blocks.Held[Entity], new float[3 * Floats], UsedByAnything, 1f, 6);
            rig.Skeletons.BoneBatch(7, 1f);
            Assert.True(Batched(rig, Entity, out _), what + ": batched before the change");

            change();

            Assert.False(Batched(rig, Entity, out _), what + ": dropped after it");
            rig.Skeletons.SetBoneMergeSource(Entity, -1);
            rig.Skeletons.SetModelScale(Entity, 1f);
        }
    }

    [Fact]
    public void AFollowerAndARagdollBodyAreNotBatched()
    {
        using Rig rig = MergedPair();
        rig.Blocks.Held[Other] = Block(new NVector(0f, 50f, 0f));
        rig.Skeletons.SetupBones(Entity, rig.Blocks.Held[Entity], new float[3 * Floats], UsedByAnything, 1f, 6);
        rig.Skeletons.SetupBones(Other, rig.Blocks.Held[Other], new float[3 * Floats], UsedByAnything, 1f, 6);
        rig.Skeletons.SetupBones(Source, rig.Blocks.Held[Source], new float[3 * Floats], UsedByAnything, 1f, 6);
        rig.Ragdolls.Root(Other, BoneMatrix3x4.Identity);

        rig.Skeletons.BoneBatch(7, 1f);

        Assert.False(Batched(rig, Entity, out _)); // its bones are its source's
        Assert.False(Batched(rig, Other, out _));  // its bones are the solver's
        Assert.True(Batched(rig, Source, out _));  // drawn itself: the source is batched
    }

    /// <summary>
    /// One engine SetupBones ask as the native thunk serves it: from the
    /// frame's batch when an entry was posed for this frame and clock (the
    /// stamps native checks), else across to managed.
    /// </summary>
    private static float[] NativeAsk(Rig rig, EntityHandle entity, int frame, float time)
    {
        float[] output = new float[3 * Floats];
        var header = (ClientBoneBatch.Header*)rig.Skeletons.Batch.Address;

        if (header->FrameCount == frame && header->CurrentTime == time
            && rig.Skeletons.Batch.TryGet(entity, out ClientBoneBatch.Entry entry))
        {
            Answer(rig, entity);
            rig.Skeletons.Batch.Matrices(entry).CopyTo(output);
            return output;
        }

        rig.Skeletons.SetupBones(entity, rig.Blocks.Held[entity], output, UsedByAnything, time, frame);
        return output;
    }

    [Fact]
    public void ARagdollMadeMidFrameDrawsTheSolversPoseForTheRestOfThatFrame()
    {
        using Rig rig = new();
        rig.Skeletons.SetupBones(Entity, rig.Blocks.Held[Entity], new float[3 * Floats], UsedByAnything, 1f, 6);
        rig.Skeletons.BoneBatch(7, 1f);
        Assert.Equal(100f, At(NativeAsk(rig, Entity, 7, 1f), 0).X); // drawn from the batch, animated

        // Frame 7, mid-frame: the entity becomes a body (ClientRagdolls.Become).
        BoneMatrix3x4 root = BoneMatrix3x4.Identity;
        root.Position = new NVector(0f, 50f, 0f);
        rig.Ragdolls.Root(Entity, root);
        new SourceSharp.Client.Ragdolls.ClientRagdollPoses(rig.Skeletons, rig.Blocks).BecameRagdoll(Entity);

        Assert.Equal(new NVector(0f, 50f, 0f), At(NativeAsk(rig, Entity, 7, 1f), 0));
    }

    // ---- the batch table across cs_cl_reload ------------------------------------------

    /// <summary>A client hot reload as the module walks it: the old build's sections saved, a new build restoring them.</summary>
    private static Rig Reload(Rig old)
    {
        var skeletons = new ReloadWriter();
        var carry = new ReloadWriter();
        old.Skeletons.Save(skeletons);
        new ClientBoneBatchCarry(old.Skeletons).Save(carry);

        Rig next = new();
        next.Skeletons.Restore(new ReloadReader(skeletons.ToArray()));
        new ClientBoneBatchCarry(next.Skeletons).Restore(new ReloadReader(carry.ToArray()));
        return next;
    }

    /// <summary>
    /// <paramref name="reloads"/> reloads, a frame drawn on each build (one
    /// ask, then the next frame's batch): every build, its tables and the
    /// addresses it handed native.
    /// </summary>
    private static List<(ClientBoneBatch Batch, nint Handed)> ReloadedFrames(int reloads)
    {
        var builds = new List<(ClientBoneBatch, nint)>();
        Rig rig = new();
        int frame = 6;

        for (int n = 0; n <= reloads; n++)
        {
            if (n > 0)
            {
                Rig next = Reload(rig);
                rig.Dispose();
                rig = next;
            }

            rig.Skeletons.SetupBones(Entity, rig.Blocks.Held[Entity], new float[3 * Floats], UsedByAnything, 1f, frame++);
            nint handed = rig.Skeletons.BoneBatch(frame, 1f);
            Assert.True(Batched(rig, Entity, out _), $"build {n} batched its frame");
            builds.Add((rig.Skeletons.Batch, handed));
        }

        rig.Dispose();
        return builds;
    }

    [Fact]
    public void ReloadsLeaveNoBatchTableBehind()
    {
        List<(ClientBoneBatch Batch, nint Handed)> builds = ReloadedFrames(5);

        int live = builds.Sum(b => b.Batch.TablesAllocated - b.Batch.TablesFreed);

        Assert.Equal((6, 1), (builds.Sum(b => b.Batch.TablesAllocated), live));
        Assert.Single(builds.Select(b => b.Handed).Distinct());
    }

    [Fact]
    public void NoTableNativeWasHandedIsEverFreed()
    {
        List<(ClientBoneBatch Batch, nint Handed)> builds = ReloadedFrames(5);
        var freed = builds.Where(b => b.Batch.FreedTable != 0).Select(b => b.Batch.FreedTable).ToList();

        Assert.Equal(5, freed.Count); // every new build freed its own, unhanded table
        Assert.All(builds, b => Assert.NotEqual(0, b.Handed));
        Assert.Empty(freed.Intersect(builds.Select(b => b.Handed)));
    }

    [Fact]
    public void ABuildWhoseOwnTableWasHandedRefusesToAdoptAnother()
    {
        using Rig old = new();
        using Rig next = new();
        nint own = next.Skeletons.BoneBatch(7, 1f);

        Assert.False(next.Skeletons.Batch.Adopt(old.Skeletons.Batch.Address, 0, 0));
        Assert.Equal((own, 0), (next.Skeletons.Batch.Address, next.Skeletons.Batch.TablesFreed));
    }

    [Fact]
    public void AnAdoptedTableAnswersNothingUntilTheNewBuildsBatch()
    {
        using Rig old = new();
        old.Skeletons.SetupBones(Entity, old.Blocks.Held[Entity], new float[3 * Floats], UsedByAnything, 1f, 6);
        old.Skeletons.BoneBatch(7, 1f);
        Assert.True(Batched(old, Entity, out _));

        using Rig next = Reload(old);

        Assert.False(Batched(next, Entity, out _));
    }

    [Fact]
    public void TheBatchsLayoutIsTheHeadersLayout()
    {
        string path = RepoLayout.Repo("src", "shared", "native", "standalone_bone_batch.h");
        var native = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     System.IO.File.ReadAllText(path), @"\b(SS_BONE_BATCH_(?:ENTRY|HEADER)_\w+)\s*=\s*(\d+)\s*,"))
            native.Add(m.Groups[1].Value, int.Parse(m.Groups[2].Value));

        var managed = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["SS_BONE_BATCH_ENTRY_HENTITY"] = Off<ClientBoneBatch.Entry>(nameof(ClientBoneBatch.Entry.Entity)),
            ["SS_BONE_BATCH_ENTRY_NBONEMASK"] = Off<ClientBoneBatch.Entry>(nameof(ClientBoneBatch.Entry.BoneMask)),
            ["SS_BONE_BATCH_ENTRY_NBONES"] = Off<ClientBoneBatch.Entry>(nameof(ClientBoneBatch.Entry.Bones)),
            ["SS_BONE_BATCH_ENTRY_NFIRSTMATRIX"] = Off<ClientBoneBatch.Entry>(nameof(ClientBoneBatch.Entry.FirstMatrix)),
            ["SS_BONE_BATCH_ENTRY_NANSWERED"] = Off<ClientBoneBatch.Entry>(nameof(ClientBoneBatch.Entry.Answered)),
            ["SS_BONE_BATCH_ENTRY_NMODELINDEX"] = Off<ClientBoneBatch.Entry>(nameof(ClientBoneBatch.Entry.ModelIndex)),
            ["SS_BONE_BATCH_ENTRY_NVIEWMODEL"] = Off<ClientBoneBatch.Entry>(nameof(ClientBoneBatch.Entry.ViewModel)),
            ["SS_BONE_BATCH_ENTRY_FLORIGIN"] = Off<ClientBoneBatch.Entry>(nameof(ClientBoneBatch.Entry.Origin)),
            ["SS_BONE_BATCH_ENTRY_FLANGLES"] = Off<ClientBoneBatch.Entry>(nameof(ClientBoneBatch.Entry.Angles)),
            ["SS_BONE_BATCH_ENTRY_FLRENDERORIGIN"] = Off<ClientBoneBatch.Entry>(nameof(ClientBoneBatch.Entry.RenderOrigin)),
            ["SS_BONE_BATCH_ENTRY_FLRENDERANGLES"] = Off<ClientBoneBatch.Entry>(nameof(ClientBoneBatch.Entry.RenderAngles)),
            ["SS_BONE_BATCH_ENTRY_SIZE"] = sizeof(ClientBoneBatch.Entry),
            ["SS_BONE_BATCH_HEADER_NFRAMECOUNT"] = Off<ClientBoneBatch.Header>(nameof(ClientBoneBatch.Header.FrameCount)),
            ["SS_BONE_BATCH_HEADER_FLCURRENTTIME"] = Off<ClientBoneBatch.Header>(nameof(ClientBoneBatch.Header.CurrentTime)),
            ["SS_BONE_BATCH_HEADER_NENTRIES"] = Off<ClientBoneBatch.Header>(nameof(ClientBoneBatch.Header.Entries)),
            ["SS_BONE_BATCH_HEADER_PSLOTTOENTRY"] = Off<ClientBoneBatch.Header>(nameof(ClientBoneBatch.Header.SlotToEntry)),
            ["SS_BONE_BATCH_HEADER_PENTRIES"] = Off<ClientBoneBatch.Header>(nameof(ClientBoneBatch.Header.EntryTable)),
            ["SS_BONE_BATCH_HEADER_PMATRICES"] = Off<ClientBoneBatch.Header>(nameof(ClientBoneBatch.Header.Matrices)),
            ["SS_BONE_BATCH_HEADER_SIZE"] = sizeof(ClientBoneBatch.Header),
        };

        Assert.Equal(19, native.Count); // the header was read and named every offset
        Assert.Equal(native.OrderBy(p => p.Key), managed.OrderBy(p => p.Key));
    }

    private static int Off<T>(string field) => (int)Marshal.OffsetOf<T>(field);

    // ---- attachments -------------------------------------------------------------

    [Fact]
    public void TheEnginesAttachmentAskWritesTheOneBasedMatrix()
    {
        using Rig rig = new();
        float[] matrix = new float[Floats];

        bool found = rig.Skeletons.GetAttachment(Entity, rig.Blocks.Held[Entity], 1, matrix, 1f, 7);

        Assert.True(found);
        Assert.Equal(new NVector(106f, 2f, 13f), At(matrix, 0));
    }

    [Fact]
    public void AnAttachmentTheModelLacksIsFalseAndWritesNothing()
    {
        using Rig rig = new();
        float[] matrix = new float[Floats];
        matrix[3] = -1f;

        bool found = rig.Skeletons.GetAttachment(Entity, rig.Blocks.Held[Entity], 2, matrix, 1f, 7);

        Assert.Equal((false, -1f), (found, matrix[3]));
    }

    [Fact]
    public void ManagedsAttachmentAskAnswersTheEntitysOwnPlaceOnAMiss()
    {
        using Rig rig = new();

        bool found = rig.Skeletons.GetAttachment(Entity, 2, out Vector origin, out _, out bool standalone);

        Assert.Equal((false, true, 100f), (found, standalone, origin.X));
    }

    [Fact]
    public void AnAttachmentNameIsOneBasedAndZeroForNone()
    {
        using Rig rig = new();
        byte[] block = rig.Blocks.Held[Entity];

        Assert.Equal((1, 0), (rig.Skeletons.LookupAttachment(Entity, block, "muzzle"),
                              rig.Skeletons.LookupAttachment(Entity, block, "gone")));
    }

    // ---- the hitbox trace ----------------------------------------------------------

    [Fact]
    public void AHitboxTraceIsWrittenBackOnlyWhenTested()
    {
        using Rig rig = new();
        rig.Blocks.Held[Other] = Block(NVector.Zero, model: 9);
        float[] ray = new float[BoneTraceFlat.RayFloats];
        ray[BoneTraceFlat.RayStartX] = 105f;
        ray[BoneTraceFlat.RayStartX + 1] = -50f;
        ray[BoneTraceFlat.RayStartX + 2] = 10f;
        ray[BoneTraceFlat.RayDeltaX + 1] = 100f;
        ray[BoneTraceFlat.RayIsRay] = 1f;
        float[] floats = new float[BoneTraceFlat.TraceFloats];
        int[] ints = new int[BoneTraceFlat.TraceInts];
        floats[BoneTraceFlat.TraceFraction] = 1f;
        float[] untouched = new float[BoneTraceFlat.TraceFloats];
        untouched[BoneTraceFlat.TraceFraction] = 1f;

        bool none = rig.Skeletons.TestHitboxes(Other, rig.Blocks.Held[Other], ray, -1, untouched, new int[BoneTraceFlat.TraceInts],
                                               1f, 7);
        bool tested = rig.Skeletons.TestHitboxes(Entity, rig.Blocks.Held[Entity], ray, -1, floats, ints, 1f, 7);

        Assert.Equal((false, 1f), (none, untouched[BoneTraceFlat.TraceFraction]));
        Assert.True(tested);
        Assert.True(floats[BoneTraceFlat.TraceFraction] < 1f);
        Assert.Equal(42, ints[BoneTraceFlat.TraceSurfaceProps]);
    }

    // ---- the sequence clock ------------------------------------------------------------

    [Fact]
    public void SetSequenceTellsNativesCycleTheClockOnlyWhenItMoved()
    {
        using Rig rig = new();
        rig.Environment.Now = 5f;

        rig.Skeletons.SetSequence(Entity, 0, 2f, restart: true);
        rig.Skeletons.SetSequence(Entity, 0, 2f, restart: false);

        (EntityHandle e, int seq, float rate, float start, float duration, bool looping) = Assert.Single(rig.Store.Stored);
        Assert.Equal((Entity, 0, 2f, 5f, true), (e, seq, rate, start, looping));
        Assert.True(duration > 0f);
    }

    [Fact]
    public void SequenceStateCountsStartsAndReadsTheCycleAtTheEnginesClock()
    {
        using Rig rig = new();
        rig.Environment.Now = 5f;
        rig.Skeletons.SetSequence(Entity, 0, 1f, restart: true);
        rig.Skeletons.SetSequence(Entity, 0, 1f, restart: true);
        rig.Skeletons.SequenceInfo(Entity, out float duration, out _);
        rig.Environment.Now = 5f + duration * 0.25f;

        rig.Skeletons.SequenceState(Entity, out int sequence, out float cycle, out _, out bool looping, out int starts);

        Assert.Equal((0, true, 2), (sequence, looping, starts));
        Assert.Equal(0.25f, cycle, 3);
    }

    // ---- pose parameters ------------------------------------------------------------------

    [Fact]
    public void APoseParameterTheModelLacksAnswersZeroAndNothingMoves()
    {
        using Rig rig = new();

        Assert.Equal((0f, -1, 0), (rig.Skeletons.SetPoseParameter(Entity, 0, 45f),
                                   rig.Skeletons.LookupPoseParameter(Entity, "aim_yaw"),
                                   rig.Skeletons.PoseParameterCount(Entity)));
    }

    // ---- scale -------------------------------------------------------------------------

    [Fact]
    public void AScaleOfZeroIsRefusedAndSaidSo()
    {
        using Rig rig = new();

        Assert.Equal((SkeletonScaleChange.Refused, SkeletonScaleChange.Changed, SkeletonScaleChange.Unchanged),
                     (rig.Skeletons.SetModelScale(Entity, 0f), rig.Skeletons.SetModelScale(Entity, 2f),
                      rig.Skeletons.SetModelScale(Entity, 2f)));
        Assert.Contains(rig.Log.Lines, l => l.Warning && l.Text.Contains("refused"));
    }

    [Fact]
    public void AScaledSkeletonIsBuiltAtItsSize()
    {
        using Rig rig = new();
        rig.Skeletons.SetModelScale(Entity, 2f);
        float[] output = new float[3 * Floats];

        rig.Setup(Entity, output);

        Assert.Equal(new NVector(110f, 0f, 20f), At(output, 2));
    }

    // ---- lifetime ------------------------------------------------------------------------

    [Fact]
    public void ARemovedEntityIsForgottenAndALevelShutdownForgetsEveryone()
    {
        using Rig rig = new();
        rig.Blocks.Held[Source] = Block(NVector.Zero);
        rig.Setup(Entity, new float[3 * Floats]);
        rig.Setup(Source, new float[3 * Floats]);

        rig.Skeletons.Forget(Entity);
        int afterForget = rig.Skeletons.States.Count;
        rig.Skeletons.OnLevelShutdown();

        Assert.Equal((1, 0), (afterForget, rig.Skeletons.States.Count));
    }

    [Fact]
    public void AHotReloadCarriesWhatCSharpSet()
    {
        using Rig rig = new();
        rig.Environment.Now = 3f;
        rig.Skeletons.SetBoneMergeSource(Entity, 6);
        rig.Skeletons.SetModelScale(Entity, 2f);
        rig.Skeletons.SetSequence(Entity, 0, 1.5f, restart: true);
        rig.Skeletons.SetAnimLayer(Entity, 1, 0, 0.5f, 1f, 2, restart: true);
        ReloadWriter writer = new();
        rig.Skeletons.Save(writer);

        using Rig next = new();
        next.Skeletons.Restore(new ReloadReader(writer.ToArray()));
        Assert.True(next.Skeletons.States.TryGet(Entity, out BoneEntityState? s) && s is not null);

        Assert.Equal((6, 2f, 1.5f, 3f, 0, 0.5f, 2), (s!.BoneMergeSource, s.ModelScale, s.PlaybackRate, s.AnimTime,
                                                     s.Layers[1].Sequence, s.Layers[1].Weight, s.Layers[1].Order));
    }

    // ---- the flush, the body that goes, the ground speed --------------------------

    [Fact]
    public void AModelCacheFlushInvalidatesEverySkeletonAndDropsTheImages()
    {
        using Rig rig = new();
        rig.Setup(Entity, new float[3 * Floats]);
        long builds = rig.Skeletons.Builds;

        int reached = rig.Skeletons.InvalidateMdlCache();
        int models = rig.Skeletons.Models.Count;
        rig.Setup(Entity, new float[3 * Floats]);

        Assert.Equal((1, 0, builds + 1), (reached, models, rig.Skeletons.Builds));
    }

    [Fact]
    public void ABodyThatGoesDropsTheSkeletonBuiltAroundIt()
    {
        using Rig rig = new();
        BoneMatrix3x4 root = BoneMatrix3x4.Identity;
        root.Position = new NVector(0f, 50f, 0f);
        rig.Ragdolls.Root(Entity, root);
        float[] output = new float[3 * Floats];

        rig.Skeletons.SetupBones(Entity, rig.Blocks.Held[Entity], output, UsedByAnything, 1f, 7);
        rig.Ragdolls.Owner = EntityHandle.None;
        rig.Skeletons.SetupBones(Entity, rig.Blocks.Held[Entity], output, UsedByAnything, 1f, 7);

        // The animated pose again, not the solver's: the key never saw the body.
        Assert.Equal(new NVector(105f, 0f, 10f), At(output, 2));
    }

    [Fact]
    public void ASequenceThatDoesNotMoveTheRootHasNoGroundSpeed()
    {
        using Rig rig = new();

        Assert.Equal((0f, 0f, 0f), (rig.Skeletons.SequenceGroundSpeed(Entity, 0),
                                    rig.Skeletons.SequenceGroundSpeed(Entity, 5),
                                    rig.Skeletons.SequenceGroundSpeed(Other, 0)));
    }

    // ---- a legacy temp entity's pose (PoseModel) ---------------------------------

    private static float[] RenderAt(float x)
    {
        float[] m = new float[Floats];
        m[0] = 1f; m[5] = 1f; m[10] = 1f; m[3] = x;
        return m;
    }

    [Fact]
    public void ATempEntsModelIsPosedAtItsPlaceAndUnwrittenSlotsAreTheRenderTransform()
    {
        using Rig rig = new();
        float[] output = new float[5 * Floats];

        bool answered = rig.Skeletons.PoseModel(Model, [200f, 0f, 0f, 0f, 0f, 0f], RenderAt(-9f), output,
                                                UsedByAnything, 1f, 7);

        Assert.True(answered);
        Assert.Equal(new NVector(205f, 0f, 10f), At(output, 2));
        Assert.Equal((-9f, -9f), (At(output, 3).X, At(output, 4).X));
    }

    [Fact]
    public void AModelWithNoStudioDataIsTheRenderTransformInEverySlot()
    {
        using Rig rig = new();
        float[] output = new float[2 * Floats];

        rig.Skeletons.PoseModel(9, new float[6], RenderAt(4f), output, UsedByAnything, 1f, 7);

        Assert.Equal((4f, 4f), (At(output, 0).X, At(output, 1).X));
    }

    [Fact]
    public void ATempEntsPoseKeepsNoStateBehindIt()
    {
        using Rig rig = new();

        rig.Skeletons.PoseModel(Model, new float[6], RenderAt(0f), new float[3 * Floats], UsedByAnything, 1f, 7);

        Assert.Equal(0, rig.Skeletons.States.Count);
    }

    // ---- cs_cl_boneprobe: the gate's merge fixture ----------------------------------

    private sealed class Probed(IEngineEntityRender render) : ClientStandaloneEntity(render)
    {
        public override string ClassName => "ss_barrel";
    }

    private sealed class LiveList(List<ClientStandaloneEntity> all) : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live() => all;

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => all.Find(e => e.Index == entityIndex);
    }

    private static (ClientBoneProbeCommand Probe, RecordingLog Log, ClientSkeletons Skeletons, Rig Rig) Fixture()
    {
        Rig rig = new();
        var render = (IEngineEntityRender)TestServices.Fake();
        var all = new List<ClientStandaloneEntity>();

        foreach ((EntityHandle handle, int index) in new[] { (Entity, 5), (Source, 6), (Other, 7) })
        {
            rig.Blocks.Held[handle] = Block(new NVector(100f * index, 0f, 0f));
            rig.Indices[index] = handle;
            var e = new Probed(render) { Handle = handle };
            e.BindIndex(index);
            all.Add(e);
        }

        RecordingLog log = new();
        return (new ClientBoneProbeCommand(rig.Skeletons, new LiveList(all), log), log, rig.Skeletons, rig);
    }

    [Fact]
    public void MergeAutoMergesTheLastOfAModelOntoTheOneBeforeIt()
    {
        var (probe, log, skeletons, rig) = Fixture();
        using Rig _ = rig;

        probe.Execute("mergeauto chain chain");

        Assert.True(skeletons.States.TryGet(Other, out BoneEntityState? s) && s is not null);
        Assert.Equal(6, s!.BoneMergeSource);
        Assert.Contains("mergeauto: entity 7 ('chain') merges from 6", log.Text);
    }

    [Fact]
    public void TheReportSaysTheFollowerHoldsTheSourcesMatrixForEveryBone()
    {
        var (probe, log, _, rig) = Fixture();
        using Rig _ = rig;

        probe.Execute("mergeauto chain chain");
        probe.Execute("report");

        Assert.Contains("SS6CGATE\tmerge\tbones\t3\n", log.Text);
        Assert.Contains("SS6CGATE\tmerge\tmatched\t3\n", log.Text);
        Assert.Contains("SS6CGATE\tmerge\tfollows\t3\n", log.Text);
    }

    [Fact]
    public void AReportWithNoMergeSaysBonesZero()
    {
        var (probe, log, _, rig) = Fixture();
        using Rig _ = rig;

        probe.Execute("report");

        Assert.Contains("SS6CGATE\tmerge\tbones\t0\n", log.Text);
    }

#if DEBUG
    // ---- the perf instruments: cs_cl_bonebench and cs_cl_bonedump -------------------

    [Fact]
    public void ARebuildBuildsAgainWhatTheCacheWouldHaveAnswered()
    {
        using Rig rig = new();
        rig.Setup(Entity, new float[3 * Floats]);
        rig.Skeletons.States.TryGet(Entity, out BoneEntityState? s);
        s!.CachedBones[2] = default;

        Assert.Equal(3, rig.Skeletons.Rebuild(Entity, 1f, 7));
        Assert.Equal(new NVector(105f, 0f, 10f), rig.Skeletons.CachedBones(Entity)[2].Position);
    }

    [Fact]
    public void ForgettingHistoryDropsTheSolverTheSpringsAndTheCache()
    {
        using Rig rig = new();
        rig.Setup(Entity, new float[3 * Floats]);
        rig.Skeletons.States.TryGet(Entity, out BoneEntityState? s);
        s!.Ik = new CIKContext();
        s.Jiggle = new JiggleBones();

        rig.Skeletons.ForgetHistory();

        Assert.Equal((true, true, -1), (s.Ik is null, s.Jiggle is null, s.CacheKey.ModelIndex));
    }

    [Fact]
    public void ForgettingHistoryDropsTheTransitionQueue()
    {
        // A build at the dump's clock leaves its clock in the queue; a change
        // seen next would fade from there (weight 1 until the game reaches it).
        using Rig rig = new();
        rig.Setup(Entity, new float[3 * Floats]);
        rig.Skeletons.States.TryGet(Entity, out BoneEntityState? s);
        Assert.Equal(3, rig.Skeletons.Rebuild(Entity, 1000f, 7));
        Assert.Equal((1, 1000f), (s!.Transitions.Count, s.Transitions[0].LayerAnimtime));

        rig.Skeletons.ForgetHistory();

        Assert.Equal(0, s.Transitions.Count);
    }

    [Fact]
    public void TheDumpOrdersAMergeSourceBeforeItsFollowerWhateverTheirHandles()
    {
        var (_, _, skeletons, rig) = Fixture();
        using Rig _ = rig;

        // Entity (the lowest handle) follows Other (the highest); every one
        // of the three holds a skeleton.
        foreach (EntityHandle held in new[] { Entity, Source, Other })
            rig.Setup(held, new float[3 * Floats]);
        skeletons.SetBoneMergeSource(Entity, 7);

        List<EntityHandle> order = ClientSkeletonDumpCommand.SourcesFirst(skeletons, new LiveList([]));

        Assert.Equal([Source, Other, Entity], order);
    }
#endif
}
