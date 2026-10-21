using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared.Bones;

using Xunit;

using NVector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="ServerSkeletons"/> (plan physics_bones_native_zero
/// steps 1.1-1.2): the server's skeleton questions answered in managed code
/// from an entity's engine block, hand-derived from what CStandaloneEntity
/// answered at the 1.0b base (shell_standalone_hierarchy.cpp 173-586,
/// shell_scripted.cpp 1199-1376).
/// </summary>
public class ServerSkeletonsTests
{
    private const int Model = 3;
    private const int UsedByAnything = 0x0007FF00;
    private static readonly EntityHandle Entity = new(0x00010005u);
    private static readonly EntityHandle Other = new(0x00010006u);

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 1.0f;
    }

    private sealed class Blocks : IStandaloneBlocks
    {
        public Dictionary<EntityHandle, byte[]> Held { get; } = [];
        public int Reads { get; private set; }

        public Span<byte> Block(EntityHandle entity)
        {
            Reads++;
            return Held.TryGetValue(entity, out byte[]? b) ? b : Span<byte>.Empty;
        }
    }

    private sealed class Memory : IStudioMemory
    {
        public Dictionary<int, byte[]> Models { get; } = [];
        public int Reads { get; private set; }

        public byte[]? ModelImage(int modelIndex)
        {
            Reads++;
            return Models.TryGetValue(modelIndex, out byte[]? b) ? b : null;
        }

        public byte[]? ModelAnimBlock(int modelIndex, int block) => null;
        public byte[]? IncludeImage(int modelIndex, string includeName) => null;
        public byte[]? IncludeAnimBlock(int modelIndex, string includeName, int block) => null;
    }

    private sealed class Props : IFwdPhysicsSurfaceProps
    {
        public List<string> Asked { get; } = [];
        public int ParseSurfaceData(string fileName, string text) => 0;
        public int SurfacePropCount() => 0;
        public string? PropName(int index) => null;

        public int GameMaterial(int surfaceProps) => 0;
        public MoveSurface Surface(int surfaceProps) => default;
        public string? ScrapeSound(int surfaceProps, bool smooth) => null;
        public int SurfaceIndex(string name)
        {
            Asked.Add(name);
            return 42;
        }
    }

    /// <summary>root -> arm -> hand, a looping ramp on the root, "muzzle" on
    /// the hand, one solid hitbox on the hand.</summary>
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

        // Solid bones, and the hand's surfaceprop named "flesh" at the end.
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

    private static byte[] Block(NVector origin, float yaw = 0f, int sequence = 0, float rate = 1f, float animTime = 0f, int hitboxSet = 0)
    {
        EngineBlock block = default;
        block.Head.ModelIndex = Model;
        block.Head.Origin = new Vector(origin.X, origin.Y, origin.Z);
        block.Head.Angles = new QAngle(0f, yaw, 0f);
        block.Client.Sequence = sequence;
        block.Client.PlaybackRate = rate;
        block.Client.AnimTime = animTime;
        block.Client.HitboxSet = hitboxSet;

        byte[] bytes = new byte[EngineBlock.SizeBytes];
        MemoryMarshal.Write(bytes, in block);
        return bytes;
    }

    private sealed class Rig
    {
        public Blocks Blocks { get; } = new();
        public Memory Memory { get; } = new();
        public Clock Clock { get; } = new();
        public RecordingLog Log { get; } = new();
        public Props Props { get; } = new();
        public ServerSkeletons Skeletons { get; }

        public Rig()
        {
            Memory.Models[Model] = Chain();
            Blocks.Held[Entity] = Block(new NVector(100f, 0f, 0f));
            Skeletons = new ServerSkeletons(Blocks, new StudioModels(Memory), Clock, Log, Props);
        }

        public EngineBlock Read(EntityHandle entity) => MemoryMarshal.Read<EngineBlock>(Blocks.Held[entity]);
    }

    // ---- not a standalone entity -------------------------------------------

    [Fact]
    public void AHandleWithNoBlockIsNotAStandaloneAndNothingAnswers()
    {
        Rig rig = new();
        ServerSkeletons s = rig.Skeletons;

        bool found = s.GetAttachment(Other, 1, out _, out _, out bool standalone);

        Assert.Equal((false, false), (found, standalone));
        Assert.Equal(0, s.LookupAttachment(Other, "muzzle"));
        Assert.Null(s.BoneMatrices(Other));
        Assert.False(s.SetSequence(Other, 0, 1f, true));
        Assert.Equal(SkeletonScaleChange.NotStandalone, s.SetModelScale(Other, 2f));
        Assert.Equal(0, s.States.Count);
    }

    // ---- attachments ---------------------------------------------------------

    [Fact]
    public void AnAttachmentIsPlacedOffTheBlocksPose()
    {
        Rig rig = new();

        bool found = rig.Skeletons.GetAttachment(Entity, 1, out Vector origin, out _, out bool standalone);

        // At cycle 0 the root sits at the entity origin: the hand at +5 x and
        // +10 z, the muzzle +1,+2,+3 off it.
        Assert.True(found && standalone);
        Assert.Equal((106f, 2f, 13f), (origin.X, origin.Y, origin.Z));
    }

    [Fact]
    public void AMissingAttachmentAnswersTheEntitysOwnPlace()
    {
        Rig rig = new();

        bool found = rig.Skeletons.GetAttachment(Entity, 2, out Vector origin, out _, out bool standalone);

        Assert.Equal((false, true), (found, standalone));
        Assert.Equal(100f, origin.X);
    }

    [Fact]
    public void AnAttachmentNameIsOneBasedAndZeroForNone()
    {
        Rig rig = new();

        Assert.Equal((1, 0, 0), (rig.Skeletons.LookupAttachment(Entity, "muzzle"),
                                 rig.Skeletons.LookupAttachment(Entity, "gone"),
                                 rig.Skeletons.LookupAttachment(Entity, "")));
    }

    // ---- the sequence clock --------------------------------------------------

    [Fact]
    public void SetSequenceWritesTheClockIntoTheBlock()
    {
        Rig rig = new();
        rig.Clock.Now = 5f;

        Assert.True(rig.Skeletons.SetSequence(Entity, 0, 2f, restart: true));

        EngineBlock block = rig.Read(Entity);
        Assert.Equal((0, 2f, 5f), (block.Client.Sequence, block.Client.PlaybackRate, block.Client.AnimTime));
    }

    [Fact]
    public void AFreshSkeletonTakesItsClockFromTheBlock()
    {
        // What a hot reload looks like: the block says the sequence started at
        // t=1 at rate 1; this composition has never seen the entity. At t=1.05
        // the ramp is at cycle 0.75 (duration 2/30), so the root has moved.
        Rig rig = new();
        rig.Blocks.Held[Entity] = Block(new NVector(100f, 0f, 0f), animTime: 1f);
        rig.Clock.Now = 1.05f;

        Assert.True(rig.Skeletons.GetAttachment(Entity, 1, out Vector moved, out _, out _));
        Assert.True(rig.Skeletons.States.TryGet(Entity, out BoneEntityState? state));

        Assert.True(state!.SeqDuration > 0f && state.SeqLooping);
        Assert.Equal(1f, state.AnimTime);
        Assert.NotEqual(106f, moved.X);
    }

    [Fact]
    public void AFreshSkeletonWithTheConstructorsClockReadsNoDuration()
    {
        Rig rig = new();
        rig.Skeletons.GetAttachment(Entity, 1, out _, out _, out _);

        Assert.True(rig.Skeletons.States.TryGet(Entity, out BoneEntityState? state));
        Assert.Equal(0f, state!.SeqDuration);
    }

    // ---- the model scale -----------------------------------------------------

    [Fact]
    public void AScaleOfZeroIsRefusedOutLoud()
    {
        Rig rig = new();

        Assert.Equal(SkeletonScaleChange.Refused, rig.Skeletons.SetModelScale(Entity, 0f));
        Assert.Contains(rig.Log.Lines, l => l.Warning && l.Text.Contains("model scale 0 refused"));
    }

    [Fact]
    public void AScaleIsChangedOnceAndThenUnchanged()
    {
        Rig rig = new();

        Assert.Equal(SkeletonScaleChange.Changed, rig.Skeletons.SetModelScale(Entity, 2f));
        Assert.Equal(SkeletonScaleChange.Unchanged, rig.Skeletons.SetModelScale(Entity, 2f));

        rig.Skeletons.GetAttachment(Entity, 1, out Vector origin, out _, out _);
        Assert.Equal(112f, origin.X); // the bones carry the scale: hand at +10, muzzle +2
    }

    // ---- the model scale across a hot reload ---------------------------------
    //
    // The scale has no engine-block word, so a fresh composition would build
    // a scaled entity's skeleton at 1 until the scale was set again. It rides
    // the ReloadStore (ISaveCustom) instead.

    private static ReloadRecord Saved(ServerSkeletons skeletons)
    {
        var record = new ReloadRecord(typeof(ServerSkeletons).FullName!);
        Assert.IsAssignableFrom<ISaveCustom>(skeletons).OnSave(new SaveRecordAccess(record, record.Owner, new RecordingLog()));
        return record;
    }

    private static void Restore(ServerSkeletons skeletons, ReloadRecord record)
    {
        // An engine that is up and knows every handle: the restoring side's check.
        var engine = new RecordingEngine { Strict = false };
        engine.Returns("Host.IsInitialized", true);
        engine.Returns("Entities.IsValid", true);
        Assert.IsAssignableFrom<ISaveCustom>(skeletons).OnRestore(new SaveRecordAccess(record, record.Owner, new RecordingLog(),
                                                                                     new SavedHandles(engine, engine)));
    }

    [Fact]
    public void AModelScaleIsCarriedAcrossAHotReload()
    {
        Rig before = new();
        Assert.Equal(SkeletonScaleChange.Changed, before.Skeletons.SetModelScale(Entity, 2f));
        ReloadRecord record = Saved(before.Skeletons);

        Rig after = new();
        Restore(after.Skeletons, record);

        after.Skeletons.GetAttachment(Entity, 1, out Vector origin, out _, out _);
        Assert.Equal(112f, origin.X); // scaled 2, as before the reload, not the 106 of scale 1
        Assert.Equal(SkeletonScaleChange.Unchanged, after.Skeletons.SetModelScale(Entity, 2f));
    }

    [Fact]
    public void AnUnscaledSkeletonCarriesNothing()
    {
        Rig before = new();
        before.Skeletons.GetAttachment(Entity, 1, out _, out _, out _);
        before.Skeletons.SetModelScale(Other, 3f); // not standalone: refused before any state

        Assert.Empty(Saved(before.Skeletons).Rows);
    }

    [Fact]
    public void ACarriedScaleLeavesTheClockToTheBlock()
    {
        Rig before = new();
        before.Skeletons.SetModelScale(Entity, 2f);
        ReloadRecord record = Saved(before.Skeletons);

        Rig after = new();
        after.Blocks.Held[Entity] = Block(new NVector(100f, 0f, 0f), animTime: 1f);
        Restore(after.Skeletons, record);
        after.Skeletons.GetAttachment(Entity, 1, out _, out _, out _);

        Assert.True(after.Skeletons.States.TryGet(Entity, out BoneEntityState? state));
        Assert.Equal((2f, 1f), (state!.ModelScale, state.AnimTime));
        Assert.True(state.SeqDuration > 0f, "the duration was read from the model, as a fresh skeleton's is");
    }

    // ---- the skeleton for a renderer ----------------------------------------

    [Fact]
    public void BoneMatricesCarryTheSkeletonTheWrittenBitsAndTheEntityTransform()
    {
        Rig rig = new();

        StandaloneBones? bones = rig.Skeletons.BoneMatrices(Entity);

        Assert.NotNull(bones);
        Assert.Equal(3, bones!.Count);
        Assert.Equal(0b111u, bones.WrittenBits[0]);
        Assert.Equal(100f, bones.EntityToWorld[3]);
        Assert.Equal(105f, bones.BoneToWorld[2 * StandaloneBones.FloatsPerMatrix + 3]);
        Assert.Equal(3, rig.Skeletons.CachedBoneCount(Entity));
    }

    [Fact]
    public void TheStudioNameAndPoseabilityComeFromTheModel()
    {
        Rig rig = new();

        Assert.Equal(("test/chain.mdl", true), (rig.Skeletons.StudioName(Entity), rig.Skeletons.IsPoseable(Entity)));
    }

    // ---- the engine's hitbox trace -------------------------------------------

    [Fact]
    public void TheHitboxTraceHitsTheHandAndNamesItsSurface()
    {
        Rig rig = new();
        var ray = BoneRay.FromPoints(new NVector(105f, -50f, 10f), new NVector(105f, 50f, 10f));
        var tr = new BoneTrace();

        bool tested = rig.Skeletons.TestHitboxes(Entity, rig.Blocks.Held[Entity], ray, BoneSetupTrace.ContentsSolid, tr);

        Assert.True(tested);
        Assert.True(tr.Fraction < 1f);
        Assert.Equal((0, 42, BoneSetupTrace.StudioSurfaceName), (tr.HitBox, tr.SurfaceProps, tr.SurfaceName));
        Assert.Equal(new[] { "flesh" }, rig.Props.Asked);
        Assert.Equal(1, rig.Skeletons.HitboxTraces);
    }

    [Fact]
    public void TheHitboxTraceReadsTheBlockItIsHandedNotAnother()
    {
        Rig rig = new();
        int before = rig.Blocks.Reads;
        var ray = BoneRay.FromPoints(new NVector(205f, -50f, 10f), new NVector(205f, 50f, 10f));
        var tr = new BoneTrace();

        // Moved 100 units in the handed block only: the hit follows it, and
        // the engine block was never read through the route.
        byte[] moved = Block(new NVector(200f, 0f, 0f));
        rig.Skeletons.TestHitboxes(Entity, moved, ray, BoneSetupTrace.ContentsSolid, tr);

        Assert.True(tr.Fraction < 1f);
        Assert.Equal(before, rig.Blocks.Reads);
    }

    [Fact]
    public void AHitboxSetTheModelLacksTestsNothing()
    {
        Rig rig = new();
        var ray = BoneRay.FromPoints(new NVector(105f, -50f, 10f), new NVector(105f, 50f, 10f));

        Assert.False(rig.Skeletons.TestHitboxes(Entity, Block(new NVector(100f, 0f, 0f), hitboxSet: 4), ray,
                                                BoneSetupTrace.ContentsSolid, new BoneTrace()));
    }

    // ---- the cost counters ---------------------------------------------------

    [Fact]
    public void ASecondAskOnTheSameKeyIsAnAskAndNotABuild()
    {
        Rig rig = new();

        rig.Skeletons.GetAttachment(Entity, 1, out _, out _, out _);
        rig.Skeletons.GetAttachment(Entity, 1, out _, out _, out _);

        Assert.Equal((2L, 1L), (rig.Skeletons.Asks, rig.Skeletons.Builds));
    }

    // ---- lifetime ------------------------------------------------------------

    [Fact]
    public void ForgetDropsTheEntitysSkeleton()
    {
        Rig rig = new();
        rig.Skeletons.BoneMatrices(Entity);

        rig.Skeletons.Forget(Entity);

        Assert.Equal((0, 0), (rig.Skeletons.States.Count, rig.Skeletons.CachedBoneCount(Entity)));
    }

    [Fact]
    public void LevelShutdownDropsEverySkeletonAndEveryModel()
    {
        Rig rig = new();
        rig.Skeletons.BoneMatrices(Entity);

        rig.Skeletons.OnLevelShutdown();

        Assert.Equal((0, 0), (rig.Skeletons.States.Count, rig.Skeletons.Models.Count));
    }

    [Fact]
    public void AModelCacheFlushInvalidatesEverySkeletonAndRereadsTheModel()
    {
        Rig rig = new();
        rig.Skeletons.BoneMatrices(Entity);
        int reads = rig.Memory.Reads;

        Assert.Equal(1, rig.Skeletons.InvalidateMdlCache());
        rig.Skeletons.BoneMatrices(Entity);

        Assert.Equal((reads + 1, 2L), (rig.Memory.Reads, rig.Skeletons.Builds));
    }

    [Fact]
    public void TheSkeletonsClearAfterTheHierarchysTeardown()
    {
        Assert.True((int)LifecycleStage.Skeletons > (int)LifecycleStage.Standalone);
        Assert.Equal((int)LifecycleStage.Skeletons, new Rig().Skeletons.LifecycleOrder);
    }
}
