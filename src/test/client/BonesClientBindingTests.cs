using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="BonesClientBinding"/> (plan physics_bones_native_zero 1.1,
/// client half): what IManagedBonesClient hands managed, and what managed's
/// answer becomes on the way back.
/// </summary>
public sealed unsafe class BonesClientBindingTests
{
    private sealed class Answers : IClientBoneAnswers
    {
        public int Calls;
        public int OutputFloats = -1;
        public int Mask;
        public float Time;
        public int Frame;
        public uint Entity;
        public string? Name;
        public bool Throw;

        public bool SetupBones(EntityHandle entity, ReadOnlySpan<byte> block, Span<float> boneToWorld, int boneMask,
                               float currentTime, int frameCount)
        {
            Calls++;
            if (Throw)
                throw new InvalidOperationException("planted");
            Entity = entity.Value;
            OutputFloats = boneToWorld.Length;
            Mask = boneMask;
            Time = currentTime;
            Frame = frameCount;
            if (!boneToWorld.IsEmpty)
                boneToWorld[0] = 9f;
            return true;
        }

        public bool TestHitboxes(EntityHandle entity, ReadOnlySpan<byte> block, ReadOnlySpan<float> ray, int contentsMask,
                                 Span<float> traceFloats, Span<int> traceInts, float currentTime, int frameCount)
        {
            Calls++;
            traceFloats[BoneTraceFlat.TraceFraction] = 0.5f;
            return true;
        }

        public bool GetAttachment(EntityHandle entity, ReadOnlySpan<byte> block, int attachment, Span<float> attachmentToWorld,
                                  float currentTime, int frameCount)
        {
            Calls++;
            attachmentToWorld[3] = attachment;
            return attachment == 1;
        }

        public int LookupAttachment(EntityHandle entity, ReadOnlySpan<byte> block, string name)
        {
            Calls++;
            Name = name;
            return 4;
        }

        public int PosedModel = -1;
        public float[] Place = [];

        public bool PoseModel(int modelIndex, ReadOnlySpan<float> originAngles, ReadOnlySpan<float> renderToWorld,
                              Span<float> boneToWorld, int boneMask, float currentTime, int frameCount)
        {
            Calls++;
            PosedModel = modelIndex;
            Place = originAngles.ToArray();
            OutputFloats = boneToWorld.Length;
            boneToWorld[0] = renderToWorld[3];
            return true;
        }

        public (int Frame, float Time) BatchAsked = (-1, 0f);
        public nint Batch;
        public bool ThrowOnBatch;

        public nint BoneBatch(int frameCount, float currentTime)
        {
            Calls++;
            BatchAsked = (frameCount, currentTime);
            if (ThrowOnBatch)
                throw new InvalidOperationException("batch");
            return Batch;
        }
    }

    private static readonly byte[] Block = new byte[EngineBlock.SizeBytes];

    [Fact]
    public void SetupBonesHandsTheEnginesArrayOverAtTwelveFloatsABone()
    {
        Answers answers = new();
        Crossings crossings = new();
        BonesClientBinding binding = new(crossings, new RecordingLog(), answers);
        float[] bones = new float[3 * 12];

        fixed (byte* block = Block)
        fixed (float* p = bones)
        {
            int answer = binding.SetupBones(0x10005u, block, p, 3, 0x100, 2.5f, 11);

            Assert.Equal(1, answer);
        }

        Assert.Equal((0x10005u, 36, 0x100, 2.5f, 11), (answers.Entity, answers.OutputFloats, answers.Mask,
                                                       answers.Time, answers.Frame));
        Assert.Equal(9f, bones[0]);
        Assert.Equal(1, crossings.Read(CrossingOp.ClientBonesSetupBones));
    }

    [Fact]
    public void ANullArrayIsAFillOnlyAsk()
    {
        Answers answers = new();
        BonesClientBinding binding = new(new Crossings(), new RecordingLog(), answers);

        fixed (byte* block = Block)
            binding.SetupBones(1u, block, null, 0, 0x100, 0f, 0);

        Assert.Equal(0, answers.OutputFloats);
    }

    [Fact]
    public void ANullBlockIsNoSkeletonAndManagedIsNotAsked()
    {
        Answers answers = new();
        Crossings crossings = new();
        BonesClientBinding binding = new(crossings, new RecordingLog(), answers);

        int answer = binding.SetupBones(1u, null, null, 0, 0x100, 0f, 0);

        Assert.Equal((0, 0, 1L), (answer, answers.Calls, crossings.Read(CrossingOp.ClientBonesSetupBones)));
    }

    [Fact]
    public void AThrowIsLoggedAndAnswersNoSkeleton()
    {
        Answers answers = new() { Throw = true };
        RecordingLog log = new();
        BonesClientBinding binding = new(new Crossings(), log, answers);

        int answer;
        fixed (byte* block = Block)
            answer = binding.SetupBones(1u, block, null, 0, 0x100, 0f, 0);

        Assert.Equal(0, answer);
        Assert.Contains("planted", log.Text);
    }

    [Fact]
    public void TheHitboxTraceIsWrittenBackFlat()
    {
        Answers answers = new();
        Crossings crossings = new();
        BonesClientBinding binding = new(crossings, new RecordingLog(), answers);
        float[] ray = new float[BoneTraceFlat.RayFloats];
        float[] floats = new float[BoneTraceFlat.TraceFloats];
        int[] ints = new int[BoneTraceFlat.TraceInts];

        int answer;
        fixed (byte* block = Block)
        fixed (float* r = ray)
        fixed (float* f = floats)
        fixed (int* i = ints)
            answer = binding.TestHitboxes(1u, block, r, -1, f, i, 0f, 0);

        Assert.Equal((1, 0.5f, 1L), (answer, floats[BoneTraceFlat.TraceFraction],
                                     crossings.Read(CrossingOp.ClientBonesTestHitboxes)));
    }

    [Fact]
    public void TheAttachmentAskWritesOneMatrixAndAnswersTheLookup()
    {
        Answers answers = new();
        Crossings crossings = new();
        BonesClientBinding binding = new(crossings, new RecordingLog(), answers);
        float[] matrix = new float[12];
        byte[] name = "muzzle\0"u8.ToArray();

        int found, looked;
        fixed (byte* block = Block)
        fixed (float* m = matrix)
        fixed (byte* n = name)
        {
            found = binding.GetAttachment(1u, block, 1, m, 0f, 0);
            looked = binding.LookupAttachment(1u, block, n);
        }

        Assert.Equal((1, 1f, 4, "muzzle"), (found, matrix[3], looked, answers.Name));
        Assert.Equal((1L, 1L), (crossings.Read(CrossingOp.ClientBonesGetAttachment),
                                crossings.Read(CrossingOp.ClientBonesLookupAttachment)));
    }

    [Fact]
    public void PoseModelHandsOverTheModelThePlaceAndTheEnginesArray()
    {
        Answers answers = new();
        Crossings crossings = new();
        BonesClientBinding binding = new(crossings, new RecordingLog(), answers);
        float[] place = [1f, 2f, 3f, 10f, 20f, 30f];
        float[] render = new float[12];
        render[3] = 7f;
        float[] bones = new float[4 * 12];

        int answer;
        fixed (float* p = place)
        fixed (float* r = render)
        fixed (float* b = bones)
            answer = binding.PoseModel(42, p, r, b, 4, 0x100, 1f, 3);

        Assert.Equal((1, 42, 48, 7f, 30f), (answer, answers.PosedModel, answers.OutputFloats, bones[0], answers.Place[5]));
        Assert.Equal(1L, crossings.Read(CrossingOp.ClientBonesPoseModel));
    }

    [Fact]
    public void PoseModelWithNoArrayAsksNothing()
    {
        Answers answers = new();
        BonesClientBinding binding = new(new Crossings(), new RecordingLog(), answers);
        float[] place = new float[6];
        float[] render = new float[12];

        int answer;
        fixed (float* p = place)
        fixed (float* r = render)
            answer = binding.PoseModel(42, p, r, null, 4, 0x100, 1f, 3);

        Assert.Equal((0, 0), (answer, answers.Calls));
    }

    [Fact]
    public void TheFramesBatchIsAskedWithItsFrameAndClockAndCounted()
    {
        Answers answers = new() { Batch = 0x1234 };
        Crossings crossings = new();
        BonesClientBinding binding = new(crossings, new RecordingLog(), answers);

        void* table = binding.BoneBatch(77, 2.5f);

        Assert.Equal(((nint)0x1234, 77, 2.5f), ((nint)table, answers.BatchAsked.Frame, answers.BatchAsked.Time));
        Assert.Equal(1L, crossings.Read(CrossingOp.ClientBonesBatch));
    }

    [Fact]
    public void ABatchThatThrowsIsNoTable()
    {
        Answers answers = new() { Batch = 0x1234, ThrowOnBatch = true };
        BonesClientBinding binding = new(new Crossings(), new RecordingLog(), answers);

        Assert.True(binding.BoneBatch(1, 0f) == null);
    }
}
