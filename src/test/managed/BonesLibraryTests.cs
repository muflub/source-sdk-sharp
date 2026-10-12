using System;
using System.Buffers.Binary;
using System.Numerics;
using SourceSharp.Shared.Bones;
using Xunit;
using Quaternion = System.Numerics.Quaternion;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="BoneLibrary"/>, the managed twin of bone_library.cpp
/// layer 2 (ModelFacts, IsPoseable, AccumulatePose, PoseToWorld,
/// BoneIsWritten, BuildBones, BuildAttachments, SequenceFacts, the lookups,
/// HitboxCentre, TraceHitboxes). Each expectation is derived by hand from the
/// native body at bone_library.cpp over a StudioHdrBuilder model whose
/// numbers are binary-exact. The native layer 2 has no gtest (it needs an
/// engine); bone_library_test.cpp's TheFallbackTransformIsUNSCALED source
/// check becomes the behavioural fact AnUnevaluatedBoneCollapsesOntoTheUnscaledEntity.
/// </summary>
public class BonesLibraryTests
{
    private const int UsedByHitbox = 0x100;          // BONE_USED_BY_HITBOX
    private const int UsedByAttachment = 0x200;      // BONE_USED_BY_ATTACHMENT
    private const int UsedByAnything = 0x0007FF00;   // BONE_USED_BY_ANYTHING
    private const int AllBits = unchecked((int)0xFFFFFFFF);
    private const int StudioLooping = 0x0001;
    private const int StudioAutoplay = 0x0008;
    private const int ContentsSolid = BoneSetupTrace.ContentsSolid;

    // ---- fixtures ----------------------------------------------------------

    private static StudioHdr Make(StudioHdrBuilder b, Action<byte[]>? patch = null)
    {
        // Every bone weighs 1 in every sequence: the builder wants the weight
        // list to cover the bones the fixture ended up with.
        foreach (StudioHdrBuilder.SeqDef seq in b.Sequences)
        {
            seq.Weights = new float[b.Bones.Count];
            Array.Fill(seq.Weights, 1f);
        }

        byte[] image = b.Build();
        patch?.Invoke(image);
        if (!StudioHdr.TryCreate(image, b.Resolver, out StudioHdr? hdr) || hdr is null)
        {
            throw new InvalidOperationException("fixture hdr rejected");
        }

        return hdr;
    }

    /// <summary>Patches mstudiobone_t::contents (@180): the builder writes 0.</summary>
    private static Action<byte[]> Contents(params int[] contents) => image =>
    {
        int boneIndex = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(160, 4));
        for (int i = 0; i < contents.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(boneIndex + 216 * i + 180, 4), contents[i]);
        }
    };

    /// <summary>An ANIMPOS X ramp on one bone: frame i is x = step * i.</summary>
    private static StudioHdrBuilder.AnimDef Ramp(int bone, int frames, float fps, short step = 10)
    {
        short[] pos0 = new short[1 + frames];
        pos0[0] = (short)(frames | (frames << 8));
        for (int i = 0; i < frames; i++)
        {
            pos0[1 + i] = (short)(step * i);
        }

        return new StudioHdrBuilder.AnimDef
        {
            Name = "ramp" + bone,
            NumFrames = frames,
            Fps = fps,
            Bones = { new StudioHdrBuilder.BoneAnimDef { Bone = bone, Flags = StudioConstants.STUDIO_ANIM_ANIMPOS, Pos0 = pos0 } },
        };
    }

    /// <summary>One bone, one sequence over a 3-frame ramp at 30 fps:
    /// x = 0, 10, 20 at cycles 0, 0.5, 1; CPS 15.</summary>
    private static StudioHdrBuilder RampModel(int seqFlags = 0, int boneFlags = UsedByAnything)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = boneFlags });
        b.Anims.Add(Ramp(0, 3, 30f));
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "ramp", AnimTable = [0], Weights = [1f], Flags = seqFlags });
        return b;
    }

    private static BoneBuildInputs Inputs(int sequence = 0, float cycle = 0f, int mask = UsedByAnything)
    {
        BoneBuildInputs inputs = default;
        inputs.Reset();
        inputs.BoneMask = mask;
        inputs.Sequence = sequence;
        inputs.Cycle = cycle;
        return inputs;
    }

    private static (Vector[] Pos, Quaternion[] Q) Pose()
    {
        Vector[] pos = new Vector[StudioConstants.MAXSTUDIOBONES];
        Quaternion[] q = new Quaternion[StudioConstants.MAXSTUDIOBONES];
        return (pos, q);
    }

    private static float RootX(StudioHdr hdr, in BoneBuildInputs inputs, CIKContext? ik = null)
    {
        (Vector[] pos, Quaternion[] q) = Pose();
        Assert.True(BoneLibrary.AccumulatePose(hdr, inputs, pos, q, ik));
        return pos[0].X;
    }

    private static BoneMatrix3x4 Sentinel => new()
    {
        Row0 = new Vector4(7f, 7f, 7f, 7f),
        Row1 = new Vector4(7f, 7f, 7f, 7f),
        Row2 = new Vector4(7f, 7f, 7f, 7f),
    };

    private static BoneMatrix3x4[] Sentinels(int n)
    {
        BoneMatrix3x4[] m = new BoneMatrix3x4[n];
        Array.Fill(m, Sentinel);
        return m;
    }

    private static BoneMatrix3x4 Translation(Vector v)
    {
        BoneMatrix3x4 m = BoneMatrix3x4.Identity;
        m.Position = v;
        return m;
    }

    // ---- ModelFacts --------------------------------------------------------

    [Fact]
    public void ModelFactsWithNoModelAnswersFalseWithNeutralOutputs()
    {
        float[] defaults = new float[BoneLibrary.MaxStudioPoseParam];
        Array.Fill(defaults, 9f);
        bool ok = BoneLibrary.ModelFacts(null, out bool hasAutoplay, defaults);
        Assert.Equal((false, false, 0f, 0f), (ok, hasAutoplay, defaults[0], defaults[23]));
    }

    [Fact]
    public void ModelFactsSeesAnAutoplaySequence()
    {
        Assert.True(BoneLibrary.ModelFacts(Make(RampModel(StudioAutoplay)), out bool hasAutoplay, null) && hasAutoplay);
    }

    [Fact]
    public void ModelFactsSeesNoAutoplayOnAPlainModel()
    {
        Assert.True(BoneLibrary.ModelFacts(Make(RampModel()), out bool hasAutoplay, null) && !hasAutoplay);
    }

    [Fact]
    public void ModelFactsFillsTheModelsOwnPoseDefaults()
    {
        StudioHdrBuilder b = RampModel();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "aim", Start = -90f, End = 270f });
        float[] defaults = new float[BoneLibrary.MaxStudioPoseParam];
        BoneLibrary.ModelFacts(Make(b), out _, defaults);
        // -start / (end - start) = 90 / 360 for the straddling parameter; the
        // rest keep Studio_CalcDefaultPoseParameters' 0.5.
        Assert.Equal((0.25f, 0.5f, 0.5f), (defaults[0], defaults[1], defaults[23]));
    }

    // ---- IsPoseable / BoneIsWritten ----------------------------------------

    [Fact]
    public void AModelWithSequencesIsPoseable()
    {
        Assert.True(BoneLibrary.IsPoseable(Make(RampModel())));
    }

    [Fact]
    public void AStaticPropIsNotPoseable()
    {
        StudioHdrBuilder b = RampModel();
        b.HdrFlags = BoneLibrary.StudioHdrFlagsStaticProp;
        Assert.False(BoneLibrary.IsPoseable(Make(b)));
    }

    [Fact]
    public void AModelWithNoSequencesIsNotPoseable()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        Assert.False(BoneLibrary.IsPoseable(Make(b)));
    }

    [Fact]
    public void NoModelIsNotPoseable()
    {
        Assert.False(BoneLibrary.IsPoseable(null));
    }

    [Fact]
    public void ABoneOutsideTheMaskIsNotWritten()
    {
        Assert.False(BoneLibrary.BoneIsWritten(Make(RampModel(boneFlags: UsedByHitbox)), 0, UsedByAttachment));
    }

    [Fact]
    public void ABoneInsideTheMaskIsWritten()
    {
        Assert.True(BoneLibrary.BoneIsWritten(Make(RampModel(boneFlags: UsedByHitbox)), 0, UsedByAnything));
    }

    [Fact]
    public void EveryBoneOfAStaticPropIsWrittenWhateverTheMask()
    {
        StudioHdrBuilder b = RampModel(boneFlags: UsedByHitbox);
        b.HdrFlags = BoneLibrary.StudioHdrFlagsStaticProp;
        Assert.True(BoneLibrary.BoneIsWritten(Make(b), 0, UsedByAttachment));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void ABoneTheModelDoesNotHaveIsNotWritten(int bone)
    {
        Assert.False(BoneLibrary.BoneIsWritten(Make(RampModel()), bone, UsedByAnything));
    }

    // ---- AccumulatePose ----------------------------------------------------

    [Fact]
    public void AccumulatePoseRefusesNoModel()
    {
        (Vector[] pos, Quaternion[] q) = Pose();
        Assert.False(BoneLibrary.AccumulatePose(null, Inputs(), pos, q));
    }

    [Fact]
    public void AccumulatePoseRefusesNoArrays()
    {
        Assert.False(BoneLibrary.AccumulatePose(Make(RampModel()), Inputs(), null, null));
    }

    [Fact]
    public void AccumulatePoseRefusesAStaticProp()
    {
        StudioHdrBuilder b = RampModel();
        b.HdrFlags = BoneLibrary.StudioHdrFlagsStaticProp;
        (Vector[] pos, Quaternion[] q) = Pose();
        Assert.False(BoneLibrary.AccumulatePose(Make(b), Inputs(), pos, q));
    }

    [Fact]
    public void TheBaseSequenceIsPosedAtTheCycle()
    {
        Assert.Equal(10f, RootX(Make(RampModel()), Inputs(cycle: 0.5f)));
    }

    [Fact]
    public void ASequenceTheModelDoesNotHaveIsTheReferenceSequence()
    {
        Assert.Equal(10f, RootX(Make(RampModel()), Inputs(sequence: 7, cycle: 0.5f)));
    }

    [Fact]
    public void ALayerBlendsOnAtItsWeight()
    {
        BoneBuildInputs inputs = Inputs(cycle: 0f);
        inputs.Layers = [new BoneLayerPose(0, 0.5f, 0.5f)];
        inputs.LayerCount = 1;
        Assert.Equal(5f, RootX(Make(RampModel()), inputs)); // 0 * 0.5 + 10 * 0.5
    }

    [Fact]
    public void TheLayersBlendInArrayOrder()
    {
        BoneBuildInputs inputs = Inputs(cycle: 0f);
        // full layer to 10, then a half layer of 5 over it: 10 * 0.5 + 5 * 0.5
        inputs.Layers = [new BoneLayerPose(0, 0.5f, 1f), new BoneLayerPose(0, 0.25f, 0.5f)];
        inputs.LayerCount = 2;
        Assert.Equal(7.5f, RootX(Make(RampModel()), inputs));
    }

    [Fact]
    public void TheReversedLayerOrderIsADifferentPose()
    {
        BoneBuildInputs inputs = Inputs(cycle: 0f);
        inputs.Layers = [new BoneLayerPose(0, 0.25f, 0.5f), new BoneLayerPose(0, 0.5f, 1f)];
        inputs.LayerCount = 2;
        Assert.Equal(10f, RootX(Make(RampModel()), inputs));
    }

    [Fact]
    public void ALayerSequenceTheModelDoesNotHaveIsTheReferenceSequence()
    {
        BoneBuildInputs inputs = Inputs(cycle: 0f);
        inputs.Layers = [new BoneLayerPose(99, 0.5f, 1f)];
        inputs.LayerCount = 1;
        Assert.Equal(10f, RootX(Make(RampModel()), inputs));
    }

    [Fact]
    public void OnlyLayerCountLayersAreBlended()
    {
        BoneBuildInputs inputs = Inputs(cycle: 0f);
        inputs.Layers = [new BoneLayerPose(0, 0.5f, 1f)];
        inputs.LayerCount = 0;
        Assert.Equal(0f, RootX(Make(RampModel()), inputs));
    }

    [Fact]
    public void AnAutoplayLayerRunsOnTheClock()
    {
        BoneBuildInputs inputs = Inputs(cycle: 0f);
        inputs.HasAutoplay = true;
        inputs.CurTime = 0.1f; // CPS 15: frac(1.5) = 0.5 -> x 10, at weight 1
        Assert.Equal(10f, RootX(Make(RampModel(StudioAutoplay)), inputs));
    }

    [Fact]
    public void AutoplayIsSkippedWhenTheCallerSaysTheModelHasNone()
    {
        BoneBuildInputs inputs = Inputs(cycle: 0f);
        inputs.HasAutoplay = false;
        inputs.CurTime = 0.1f;
        Assert.Equal(0f, RootX(Make(RampModel(StudioAutoplay)), inputs));
    }

    /// <summary>A pose-parameter blend: anim 0 at x 0, anim 1 at x 100,
    /// parameter range -1..1 (its default 0.5 is the value 0).</summary>
    private static StudioHdr BlendModel()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        foreach (float x in new[] { 0f, 100f })
        {
            b.Anims.Add(new StudioHdrBuilder.AnimDef
            {
                Name = "a" + x,
                NumFrames = 1,
                Bones = { new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_RAWPOS, RawPosition = new Vector(x, 0f, 0f) } },
            });
        }

        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "p", Start = -1f, End = 1f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "blend",
            NumBlends = 2,
            GroupSize0 = 2,
            GroupSize1 = 1,
            AnimTable = [0, 1],
            Weights = [1f],
            ParamIndex0 = 0,
            ParamStart = [-1f, 0f],
            ParamEnd = [1f, 1f],
        });
        return Make(b);
    }

    [Fact]
    public void NoPoseParametersMeansTheModelsDefaults()
    {
        Assert.Equal(50f, RootX(BlendModel(), Inputs()));
    }

    [Fact]
    public void ZeroedPoseParametersAreTheStartOfTheRange()
    {
        BoneBuildInputs inputs = Inputs();
        inputs.PoseParameter = new float[BoneLibrary.MaxStudioPoseParam];
        Assert.Equal(0f, RootX(BlendModel(), inputs));
    }

    /// <summary>The 2x2 cell of BonesPoseTests: bilinear 1498, triangle 3100.</summary>
    private static StudioHdr TwoByTwo()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        foreach ((string name, float x) in new[] { ("A00", 0f), ("A10", 100f), ("A01", 1000f), ("A11", 10000f) })
        {
            b.Anims.Add(new StudioHdrBuilder.AnimDef
            {
                Name = name,
                NumFrames = 1,
                Bones = { new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_RAWPOS, RawPosition = new Vector(x, 0f, 0f) } },
            });
        }

        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "p0", Start = 0f, End = 1f });
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "p1", Start = 0f, End = 1f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "g",
            NumBlends = 4,
            GroupSize0 = 2,
            GroupSize1 = 2,
            AnimTable = [0, 1, 2, 3],
            Weights = [1f],
            ParamIndex0 = 0,
            ParamIndex1 = 1,
            ParamStart = [0f, 0f],
            ParamEnd = [1f, 1f],
        });
        return Make(b);
    }

    [Theory]
    [InlineData(false, 1498f)]
    [InlineData(true, 3100f)]
    public void TheThreeWayBlendConVarReachesTheBlend(bool threeWay, float expected)
    {
        BoneBuildInputs inputs = Inputs();
        float[] pose = new float[BoneLibrary.MaxStudioPoseParam];
        pose[0] = 0.3f;
        pose[1] = 0.4f;
        inputs.PoseParameter = pose;
        inputs.AnimThreeWayBlend = threeWay;
        Assert.Equal(expected, RootX(TwoByTwo(), inputs), 2);
    }

    // ---- the autoplay IK context (client vs server shape) -----------------

    /// <summary>
    /// A leg (root, thigh at z 50, knee, foot) with one IK chain and one
    /// autoplay lock on it, and ONE autoplay sequence that slides the foot
    /// 10 along x at cycle 0.5. Posed at cycle 0 then autoplayed at CPS 15 /
    /// t 0.1 (cycle 0.5): a lock holds the foot where the base pose put it,
    /// world (0, 0, 35); without one the foot lands at (10, 0, 35).
    /// </summary>
    private static StudioHdr LegModel()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = AllBits });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "thigh", Parent = 0, Flags = AllBits, Pos = new Vector(0f, 0f, 50f) });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "knee", Parent = 1, Flags = AllBits, Pos = new Vector(0f, 12f, -15f) });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "foot", Parent = 2, Flags = AllBits, Pos = new Vector(0f, -12f, 0f) });
        b.Anims.Add(Ramp(3, 3, 30f));
        b.IkChains.Add(new StudioHdrBuilder.IkChainDef { Name = "leg", Links = { (1, Vector.Zero), (2, Vector.Zero), (3, Vector.Zero) } });
        b.AutoPlayLocks.Add(new StudioHdrBuilder.IkLockDef { Chain = 0, FlPosWeight = 1.0f, FlLocalQWeight = 1.0f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "idle", AnimTable = [0], Weights = [1f], Flags = StudioAutoplay });
        return Make(b);
    }

    private static Vector LegFoot(bool autoplayIkAlways, CIKContext? ik)
    {
        StudioHdr hdr = LegModel();
        BoneBuildInputs inputs = Inputs(cycle: 0f, mask: AllBits);
        inputs.HasAutoplay = true;
        inputs.CurTime = 0.1f;
        inputs.AutoplayIkAlways = autoplayIkAlways;
        BoneMatrix3x4[] world = new BoneMatrix3x4[4];
        Assert.Equal(4, BoneLibrary.BuildBones(hdr, inputs, world, ik));
        return world[3].Position;
    }

    private static void AssertNear(Vector expected, Vector actual, float tol)
    {
        Assert.True(Vector.Distance(expected, actual) <= tol, $"expected {expected}, got {actual}");
    }

    [Fact]
    public void TheServerShapeRunsAutoplayWithNoIkContext()
    {
        AssertNear(new Vector(10f, 0f, 35f), LegFoot(autoplayIkAlways: false, ik: null), 1e-3f);
    }

    [Fact]
    public void TheClientShapeRunsAutoplayUnderALocalIkContext()
    {
        AssertNear(new Vector(0f, 0f, 35f), LegFoot(autoplayIkAlways: true, ik: null), 0.05f);
    }

    [Fact]
    public void ACallerWithAnIkContextGetsALocalOneForAutoplay()
    {
        StudioHdr hdr = LegModel();
        CIKContext ik = new();
        ik.Init(hdr, BoneMatrix3x4.Identity, AllBits);
        AssertNear(new Vector(0f, 0f, 35f), LegFoot(autoplayIkAlways: false, ik), 0.05f);
    }

    // ---- PoseToWorld / BuildBones ------------------------------------------

    [Fact]
    public void BuildBonesRefusesNoModel()
    {
        Assert.Equal(0, BoneLibrary.BuildBones(null, Inputs(), new BoneMatrix3x4[4]));
    }

    [Fact]
    public void BuildBonesRefusesABufferTooSmallRatherThanTruncating()
    {
        BoneMatrix3x4[] world = Sentinels(3);
        int n = BoneLibrary.BuildBones(LegModel(), Inputs(mask: AllBits), world);
        Assert.Equal((0, Sentinel), (n, world[0]));
    }

    [Fact]
    public void APoseableBoneIsPlacedAndScaled()
    {
        BoneBuildInputs inputs = Inputs(cycle: 0.5f);
        inputs.Placement.Origin = new Vector(100f, 0f, 0f);
        inputs.Placement.ModelScale = 2f;
        BoneMatrix3x4[] world = new BoneMatrix3x4[1];
        BoneLibrary.BuildBones(Make(RampModel()), inputs, world);
        Assert.Equal(new Vector(120f, 0f, 0f), world[0].Position); // 100 + 2 * 10
    }

    [Fact]
    public void BuildBonesAnswersTheModelsBoneCount()
    {
        Assert.Equal(1, BoneLibrary.BuildBones(Make(RampModel()), Inputs(), new BoneMatrix3x4[8]));
    }

    [Fact]
    public void ABoneOutsideTheMaskKeepsWhatTheBufferHeld()
    {
        BoneMatrix3x4[] world = Sentinels(1);
        int n = BoneLibrary.BuildBones(Make(RampModel(boneFlags: UsedByHitbox)), Inputs(mask: UsedByAttachment), world);
        Assert.Equal((1, Sentinel), (n, world[0]));
    }

    [Fact]
    public void AStaticPropWritesTheScaledPlacementIntoEveryBone()
    {
        StudioHdrBuilder b = RampModel(boneFlags: UsedByHitbox);
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "child", Parent = 0, Flags = UsedByHitbox, Pos = new Vector(5f, 0f, 0f) });
        b.HdrFlags = BoneLibrary.StudioHdrFlagsStaticProp;
        BoneBuildInputs inputs = Inputs(mask: UsedByAttachment);
        inputs.Placement.Origin = new Vector(1f, 2f, 3f);
        inputs.Placement.Angles = new BoneAngle(0f, 90f, 0f);
        inputs.Placement.ModelScale = 3f;
        BoneMatrix3x4 expected = BoneLibraryMath.PlacementToWorld(inputs.Placement);
        BoneMatrix3x4[] world = Sentinels(2);
        int n = BoneLibrary.BuildBones(Make(b), inputs, world);
        Assert.Equal((2, expected, expected), (n, world[0], world[1]));
    }

    [Fact]
    public void AModelWithNoSequencesTakesTheStaticBranch()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByHitbox });
        BoneBuildInputs inputs = Inputs();
        inputs.Placement.Origin = new Vector(4f, 5f, 6f);
        BoneMatrix3x4[] world = Sentinels(1);
        BoneLibrary.BuildBones(Make(b), inputs, world);
        Assert.Equal(Translation(new Vector(4f, 5f, 6f)), world[0]);
    }

    [Fact]
    public void PoseToWorldConcatenatesEachBoneOntoItsParent()
    {
        StudioHdr hdr = LegModel();
        (Vector[] pos, Quaternion[] q) = Pose();
        Array.Fill(q, Quaternion.Identity);
        pos[1] = new Vector(0f, 0f, 50f);
        pos[2] = new Vector(0f, 12f, -15f);
        pos[3] = new Vector(1f, -12f, 0f);
        BonePlacement placement = default;
        placement.Reset();
        BoneMatrix3x4[] world = new BoneMatrix3x4[4];
        int n = BoneLibrary.PoseToWorld(hdr, placement, pos, q, AllBits, world);
        Assert.Equal((4, new Vector(1f, 0f, 35f)), (n, world[3].Position));
    }

    [Fact]
    public void PoseToWorldRefusesABufferTooSmall()
    {
        (Vector[] pos, Quaternion[] q) = Pose();
        BonePlacement placement = default;
        placement.Reset();
        Assert.Equal(0, BoneLibrary.PoseToWorld(LegModel(), placement, pos, q, AllBits, new BoneMatrix3x4[3]));
    }

    // ---- BuildAttachments --------------------------------------------------

    private static StudioHdr AttachmentModel(uint flags = 0)
    {
        StudioHdrBuilder b = RampModel();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "gun", Parent = 0, Flags = UsedByAnything, Pos = new Vector(0f, 0f, 10f) });
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "muzzle", LocalBone = 1, Flags = (int)flags, Local = BoneSetupMath.AngleMatrix(new BoneAngle(0f, 90f, 0f), new Vector(4f, 0f, 0f)) });
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "eyes", LocalBone = 0, Local = Translation(new Vector(0f, 0f, 2f)) });
        return Make(b);
    }

    [Fact]
    public void AnAttachmentHangsOffItsBone()
    {
        StudioHdr hdr = AttachmentModel();
        BoneMatrix3x4[] bones = [BoneMatrix3x4.Identity, BoneSetupMath.AngleMatrix(new BoneAngle(0f, 45f, 0f), new Vector(7f, 8f, 9f))];
        BoneMatrix3x4[] att = new BoneMatrix3x4[2];
        BonePlacement placement = default;
        placement.Reset();
        int n = BoneLibrary.BuildAttachments(hdr, bones, placement, att);
        Assert.Equal((2, BoneSetupMath.ConcatTransforms(bones[1], hdr.Attachment(0).Local)), (n, att[0]));
    }

    [Fact]
    public void AWorldAlignedAttachmentTakesTheAlignedBranch()
    {
        StudioHdr hdr = AttachmentModel(BoneLibrary.AttachmentFlagWorldAlign);
        BoneMatrix3x4[] bones = [BoneMatrix3x4.Identity, BoneSetupMath.AngleMatrix(new BoneAngle(0f, 45f, 0f), new Vector(7f, 8f, 9f))];
        BoneMatrix3x4[] att = new BoneMatrix3x4[2];
        BonePlacement placement = default;
        placement.Reset();
        BoneLibrary.BuildAttachments(hdr, bones, placement, att);
        Assert.Equal(BoneLibraryMath.ComposeAttachment(bones[1], hdr.Attachment(0).Local, worldAlign: true), att[0]);
    }

    [Fact]
    public void AnUnevaluatedBoneCollapsesOntoTheUnscaledEntity()
    {
        StudioHdr hdr = AttachmentModel();
        BoneMatrix3x4[] bones = [BoneMatrix3x4.Identity]; // bone 1 not supplied
        BoneMatrix3x4[] att = new BoneMatrix3x4[2];
        BonePlacement placement = new() { Origin = new Vector(1f, 2f, 3f), Angles = new BoneAngle(0f, 30f, 0f), ModelScale = 2f };
        BoneLibrary.BuildAttachments(hdr, bones, placement, att);
        Assert.Equal(BoneSetupMath.AngleMatrix(placement.Angles, placement.Origin), att[0]);
    }

    [Fact]
    public void BuildAttachmentsRefusesABufferTooSmall()
    {
        BonePlacement placement = default;
        placement.Reset();
        Assert.Equal(0, BoneLibrary.BuildAttachments(AttachmentModel(), [BoneMatrix3x4.Identity], placement, new BoneMatrix3x4[1]));
    }

    [Fact]
    public void AModelWithNoAttachmentsBuildsNone()
    {
        BonePlacement placement = default;
        placement.Reset();
        Assert.Equal(0, BoneLibrary.BuildAttachments(Make(RampModel()), [BoneMatrix3x4.Identity], placement, new BoneMatrix3x4[4]));
    }

    // ---- SequenceFacts -----------------------------------------------------

    [Fact]
    public void SequenceFactsReadsTheDuration()
    {
        Assert.True(BoneLibrary.SequenceFacts(Make(RampModel()), 0, null, out float duration, out _));
        Assert.Equal(1.0f / 15.0f, duration);
    }

    [Theory]
    [InlineData(StudioLooping, true)]
    [InlineData(0, false)]
    public void SequenceFactsReadsTheLoopFlag(int flags, bool expected)
    {
        BoneLibrary.SequenceFacts(Make(RampModel(flags)), 0, null, out _, out bool looping);
        Assert.Equal(expected, looping);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void SequenceFactsRefusesASequenceTheModelDoesNotHave(int sequence)
    {
        bool ok = BoneLibrary.SequenceFacts(Make(RampModel(StudioLooping)), sequence, null, out float duration, out bool looping);
        Assert.Equal((false, 0f, false), (ok, duration, looping));
    }

    /// <summary>A blend whose two anims run at CPS 15 and CPS 5.</summary>
    private static StudioHdr TwoSpeedModel()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        b.Anims.Add(Ramp(0, 3, 30f));
        b.Anims.Add(Ramp(0, 3, 10f));
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "p", Start = -1f, End = 1f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "walkrun",
            NumBlends = 2,
            GroupSize0 = 2,
            GroupSize1 = 1,
            AnimTable = [0, 1],
            Weights = [1f],
            ParamIndex0 = 0,
            ParamStart = [-1f, 0f],
            ParamEnd = [1f, 1f],
        });
        return Make(b);
    }

    [Fact]
    public void SequenceFactsWithNoPoseReadsTheDurationAtTheDefaults()
    {
        BoneLibrary.SequenceFacts(TwoSpeedModel(), 0, null, out float duration, out _);
        Assert.Equal(1.0f / 10.0f, duration); // CPS 15 * 0.5 + 5 * 0.5
    }

    [Fact]
    public void SequenceFactsReadsTheDurationAtTheGivenPose()
    {
        BoneLibrary.SequenceFacts(TwoSpeedModel(), 0, new float[BoneLibrary.MaxStudioPoseParam], out float duration, out _);
        Assert.Equal(1.0f / 15.0f, duration);
    }

    // ---- lookups -----------------------------------------------------------

    [Fact]
    public void CountsAnswerZeroForNoModel()
    {
        Assert.Equal((0, 0), (BoneLibrary.BoneCount(null), BoneLibrary.AttachmentCount(null)));
    }

    [Fact]
    public void CountsAnswerTheModels()
    {
        StudioHdr hdr = AttachmentModel();
        Assert.Equal((2, 2), (BoneLibrary.BoneCount(hdr), BoneLibrary.AttachmentCount(hdr)));
    }

    [Fact]
    public void LookupAttachmentIsOneBased()
    {
        Assert.Equal(2, BoneLibrary.LookupAttachment(AttachmentModel(), "eyes"));
    }

    [Theory]
    [InlineData("nosuch")]
    [InlineData("")]
    [InlineData(null)]
    public void LookupAttachmentAnswersZeroForNone(string? name)
    {
        Assert.Equal(0, BoneLibrary.LookupAttachment(AttachmentModel(), name));
    }

    [Fact]
    public void AttachmentNameIsOneBased()
    {
        Assert.True(BoneLibrary.AttachmentName(AttachmentModel(), 1, out string name));
        Assert.Equal("muzzle", name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void AttachmentNameOutOfRangeIsEmpty(int oneBased)
    {
        bool ok = BoneLibrary.AttachmentName(AttachmentModel(), oneBased, out string name);
        Assert.Equal((false, ""), (ok, name));
    }

    // ---- HitboxCentre / TraceHitboxes --------------------------------------

    /// <summary>Two bones (contents SOLID); one box on bone 1 from
    /// (-1,-1,-1) to (3,3,3) and, in set 1, one box on bone 0.</summary>
    private static StudioHdr HitboxModel(int bone1Flags = UsedByHitbox)
    {
        StudioHdrBuilder b = RampModel(boneFlags: UsedByHitbox);
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "head", Parent = 0, Flags = bone1Flags });
        b.HitboxSets.Add(new StudioHdrBuilder.HitboxSetDef { Name = "default", Boxes = { new StudioHdrBuilder.HitboxDef { Bone = 1, Min = new Vector(-1f, -1f, -1f), Max = new Vector(3f, 3f, 3f) } } });
        b.HitboxSets.Add(new StudioHdrBuilder.HitboxSetDef { Name = "other", Boxes = { new StudioHdrBuilder.HitboxDef { Bone = 0, Min = new Vector(-10f), Max = new Vector(10f) } } });
        return Make(b, Contents(ContentsSolid, ContentsSolid | 0x8));
    }

    [Fact]
    public void AHitboxCentreIsTheBoxMiddleThroughItsBone()
    {
        BoneMatrix3x4 headWorld = BoneSetupMath.AngleMatrix(new BoneAngle(0f, 90f, 0f), new Vector(10f, 20f, 30f));
        BoneMatrix3x4[] bones = [BoneMatrix3x4.Identity, headWorld];
        Assert.True(BoneLibrary.HitboxCentre(HitboxModel(), 0, 0, bones, UsedByAnything, out Vector centre, out int bone, out int contents));
        Assert.Equal((BoneSetupMath.VectorTransform(new Vector(1f, 1f, 1f), headWorld), 1, ContentsSolid | 0x8),
                     (centre, bone, contents));
    }

    [Fact]
    public void AHitboxCentreDoesNotApplyTheScaleAgain()
    {
        BoneMatrix3x4 headWorld = BoneMatrix3x4.Identity;
        BoneLibraryMath.ScaleRotation(ref headWorld, 2f); // the build put the scale here already
        BoneMatrix3x4[] bones = [BoneMatrix3x4.Identity, headWorld];
        BoneLibrary.HitboxCentre(HitboxModel(), 0, 0, bones, UsedByAnything, out Vector centre, out _, out _);
        Assert.Equal(new Vector(2f, 2f, 2f), centre);
    }

    [Fact]
    public void AHitboxOnABoneTheBuildDidNotWriteHasNoCentre()
    {
        BoneMatrix3x4[] bones = [BoneMatrix3x4.Identity, BoneMatrix3x4.Identity];
        bool ok = BoneLibrary.HitboxCentre(HitboxModel(bone1Flags: UsedByAttachment), 0, 0, bones, UsedByHitbox, out Vector centre, out int bone, out int contents);
        Assert.Equal((false, Vector.Zero, -1, 0), (ok, centre, bone, contents));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(0, -1)]
    public void AHitboxTheModelDoesNotHaveHasNoCentre(int set, int hitbox)
    {
        BoneMatrix3x4[] bones = [BoneMatrix3x4.Identity, BoneMatrix3x4.Identity];
        Assert.False(BoneLibrary.HitboxCentre(HitboxModel(), set, hitbox, bones, UsedByAnything, out _, out _, out _));
    }

    [Fact]
    public void AHitboxWhoseBoneIsPastTheSuppliedBonesHasNoCentre()
    {
        Assert.False(BoneLibrary.HitboxCentre(HitboxModel(), 0, 0, [BoneMatrix3x4.Identity], UsedByAnything, out _, out _, out _));
    }

    private static int SurfaceOfBone(StudioHdr hdr, int bone) => 40 + bone;

    [Fact]
    public void AModelWithNoHitboxesAnswersFalse()
    {
        BoneTrace tr = new();
        BoneRay ray = BoneRay.FromPoints(new Vector(0f, 0f, 100f), new Vector(0f, 0f, -100f));
        Assert.False(BoneLibrary.TraceHitboxes(SurfaceOfBone, Make(RampModel()), 0, ray, ContentsSolid, [BoneMatrix3x4.Identity], Vector.Zero, 1f, tr));
    }

    [Fact]
    public void ARayBetweenTheHitboxesAnswersTrueWithNoHit()
    {
        BoneTrace tr = new();
        BoneRay ray = BoneRay.FromPoints(new Vector(50f, 50f, 100f), new Vector(50f, 50f, -100f));
        bool ok = BoneLibrary.TraceHitboxes(SurfaceOfBone, HitboxModel(), 1, ray, ContentsSolid, [BoneMatrix3x4.Identity, BoneMatrix3x4.Identity], Vector.Zero, 1f, tr);
        Assert.Equal((true, 1.0f), (ok, tr.Fraction));
    }

    [Fact]
    public void AHitNamesTheHitboxAndItsBonesSurface()
    {
        BoneTrace tr = new();
        BoneRay ray = BoneRay.FromPoints(new Vector(0f, 0f, 100f), new Vector(0f, 0f, -100f));
        bool ok = BoneLibrary.TraceHitboxes(SurfaceOfBone, HitboxModel(), 1, ray, ContentsSolid, [BoneMatrix3x4.Identity, BoneMatrix3x4.Identity], Vector.Zero, 1f, tr);
        Assert.Equal((true, 0, "**studio**", 0x8000, 40),
                     (ok, tr.HitBox, tr.SurfaceName, tr.SurfaceFlags, tr.SurfaceProps));
        Assert.Equal(0.45f, tr.Fraction, 1e-6f); // (100 - 10) / 200
    }

    [Fact]
    public void ABoneTheBuildDidNotSupplyIsTestedAtBoneZero()
    {
        // set 0's box hangs off bone 1, which is not supplied: it is tested
        // at bone 0's transform, here moved to x 500.
        BoneTrace tr = new();
        BoneRay ray = BoneRay.FromPoints(new Vector(501f, 1f, 100f), new Vector(501f, 1f, -100f));
        bool ok = BoneLibrary.TraceHitboxes(SurfaceOfBone, HitboxModel(), 0, ray, ContentsSolid, [Translation(new Vector(500f, 0f, 0f))], Vector.Zero, 1f, tr);
        Assert.Equal((true, 0, 41), (ok, tr.HitBox, tr.SurfaceProps));
    }

    [Fact]
    public void TraceHitboxesRefusesNoBones()
    {
        BoneTrace tr = new();
        BoneRay ray = BoneRay.FromPoints(new Vector(0f, 0f, 100f), new Vector(0f, 0f, -100f));
        Assert.False(BoneLibrary.TraceHitboxes(SurfaceOfBone, HitboxModel(), 1, ray, ContentsSolid, [], Vector.Zero, 1f, tr));
    }
}
