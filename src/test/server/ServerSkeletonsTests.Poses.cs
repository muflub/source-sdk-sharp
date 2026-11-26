using System;
using System.Buffers.Binary;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared.Bones;

using Xunit;

using NVector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// The server's pose parameters (plan_descent_logic CR11): Lookup, name,
/// range, count, set and get answer on the server's skeletons with stock's
/// semantics (CBaseAnimating::SetPoseParameter / GetPoseParameter over
/// Studio_SetPoseParameter), and the server's bone setup, so its hitboxes,
/// read the stored poses.
/// </summary>
public partial class ServerSkeletonsTests
{
    private const int BodyModel = 6;
    private const int BareModel = 7;

    /// <summary>
    /// A body: a root, and a head fifty above it that "body_pitch" (-45..45)
    /// leans: the "aim" sequence blends an anim with the head twenty forward
    /// at -45 and one with it twenty back at 45, so the default (0, the
    /// range's middle) stands it straight. One solid hitbox, four each way,
    /// on the head.
    /// </summary>
    private static byte[] Body(float loop = 0f, float start = -45f, float end = 45f)
    {
        StudioHdrBuilder b = new();
        b.ModelName = "test/body.mdl";
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "head", Parent = 0, Flags = UsedByAnything, Pos = new NVector(0f, 0f, 50f) });

        foreach ((string name, float x) in new[] { ("lean_forward", 20f), ("lean_back", -20f) })
        {
            b.Anims.Add(new StudioHdrBuilder.AnimDef
            {
                Name = name,
                NumFrames = 1,
                Bones = { new StudioHdrBuilder.BoneAnimDef { Bone = 1, Flags = StudioConstants.STUDIO_ANIM_RAWPOS, RawPosition = new NVector(x, 0f, 50f) } },
            });
        }

        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "body_pitch", Start = start, End = end, Loop = loop });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "aim",
            NumBlends = 2,
            GroupSize0 = 2,
            GroupSize1 = 1,
            AnimTable = [0, 1],
            Weights = [1f, 1f],
            ParamIndex0 = 0,
            ParamStart = [start, 0f],
            ParamEnd = [end, 1f],
        });
        b.HitboxSets.Add(new StudioHdrBuilder.HitboxSetDef
        {
            Name = "default",
            Boxes = { new StudioHdrBuilder.HitboxDef { Bone = 1, Min = new NVector(-4f, -4f, -4f), Max = new NVector(4f, 4f, 4f) } },
        });

        return SolidBones(b.Build(), 2);
    }

    /// <summary>A model with a skeleton and no pose parameters at all.</summary>
    private static byte[] Bare()
    {
        StudioHdrBuilder b = new();
        b.ModelName = "test/bare.mdl";
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "idle", NumFrames = 1 });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "idle", AnimTable = [0], Weights = [1f] });
        return b.Build();
    }

    /// <summary>Every bone solid (the contents a hitbox trace asks for), as <see cref="Chain"/> makes them.</summary>
    private static byte[] SolidBones(byte[] image, int count)
    {
        int bones = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(160, 4));
        for (int i = 0; i < count; i++)
            BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(bones + 216 * i + 180, 4), BoneSetupTrace.ContentsSolid);
        return image;
    }

    private static Rig BodyRig(byte[]? model = null)
    {
        Rig rig = new();
        rig.Memory.Models[BodyModel] = model ?? Body();
        rig.Memory.Models[BareModel] = Bare();
        rig.Blocks.Held[Entity] = Block(new NVector(100f, 0f, 0f), model: BodyModel);
        return rig;
    }

    /// <summary>Whether a ray across the body at <paramref name="x"/>, head high, hits a hitbox.</summary>
    private static bool HitsHeadAt(Rig rig, float x)
    {
        var ray = BoneRay.FromPoints(new NVector(x, -50f, 50f), new NVector(x, 50f, 50f));
        var tr = new BoneTrace();
        bool tested = rig.Skeletons.TestHitboxes(Entity, rig.Blocks.Held[Entity], ray, BoneSetupTrace.ContentsSolid, tr);
        return tested && tr.Fraction < 1f;
    }

    private static IEngineEntityRender ServerRender(Rig rig) =>
        SideBindings.Of<IEngineEntityRender>(TestSides.Create(Side.Server), new StandaloneMirror(),
                                             new StandaloneSkeletonRoute { Skeletons = rig.Skeletons });

    // ---- the premise: the server's render binding reaches the skeleton ------

    /// <summary>
    /// The server's IEngineEntityRender asks the server's skeletons: a model
    /// that declares "body_pitch" answers its index there, as stock's
    /// LookupPoseParameter does (baseanimating.cpp 1393).
    /// </summary>
    [Fact]
    public void TheServersRenderBindingLooksUpAPoseParameterOnTheSkeleton()
    {
        Rig rig = BodyRig();

        Assert.Equal(0, ServerRender(rig).LookupPoseParameter(Entity, "body_pitch"));
    }

    // ---- the gate: the posed head is where the pose puts it ----------------

    /// <summary>
    /// Posed with body_pitch -45, the head leans twenty forward: a trace
    /// across the unposed head hits before and misses after.
    /// </summary>
    [Fact]
    public void ATraceAtTheUnposedHeadHitsBeforeThePoseAndMissesAfter()
    {
        Rig rig = BodyRig();
        bool before = HitsHeadAt(rig, 100f);

        rig.Skeletons.SetPoseParameter(Entity, 0, -45f);

        Assert.Equal((true, false), (before, HitsHeadAt(rig, 100f)));
    }

    /// <summary>
    /// The mirror, so the stimulus is shown to reach the subject: a trace
    /// across the place the pose puts the head misses before and hits after.
    /// </summary>
    [Fact]
    public void ATraceAtThePosedHeadMissesBeforeThePoseAndHitsAfter()
    {
        Rig rig = BodyRig();
        bool before = HitsHeadAt(rig, 120f);

        rig.Skeletons.SetPoseParameter(Entity, 0, -45f);

        Assert.Equal((false, true), (before, HitsHeadAt(rig, 120f)));
    }

    /// <summary>The render binding's set reaches the server's hitboxes (the path game code takes).</summary>
    [Fact]
    public void APoseSetThroughTheServersRenderBindingMovesTheHitboxes()
    {
        Rig rig = BodyRig();
        IEngineEntityRender render = ServerRender(rig);

        render.SetPoseParameter(Entity, render.LookupPoseParameter(Entity, "body_pitch"), -45f);

        Assert.True(HitsHeadAt(rig, 120f));
    }

    /// <summary>The bone position a server ask reads (stock's GetBonePosition) is the posed one.</summary>
    [Fact]
    public void ThePosedHeadBoneIsWhereThePosePutsIt()
    {
        Rig rig = BodyRig();
        rig.Skeletons.SetPoseParameter(Entity, 0, 22.5f);

        Assert.True(rig.Skeletons.GetBonePosition(Entity, 1, out Vector head));
        Assert.Equal((90f, 0f, 50f), (MathF.Round(head.X, 3), MathF.Round(head.Y, 3), MathF.Round(head.Z, 3)));
    }

    // ---- the ground speed the server's animation state scales its legs by ----

    /// <summary>A walk whose root travels a hundred units over its one second (eleven frames at ten a second).</summary>
    private static byte[] Walk()
    {
        StudioHdrBuilder b = new();
        b.ModelName = "test/walk.mdl";
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        var walk = new StudioHdrBuilder.AnimDef { Name = "walk", NumFrames = 11, Fps = 10f };
        walk.Movements.Add(new StudioHdrBuilder.MovementDef
        {
            EndFrame = 10, V0 = 100f, V1 = 100f, Vector = new NVector(1f, 0f, 0f), Position = new NVector(100f, 0f, 0f),
        });
        b.Anims.Add(walk);
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "walk", AnimTable = [0], Weights = [1f] });
        return b.Build();
    }

    /// <summary>
    /// The server's skeleton measures a sequence's authored speed as the
    /// client's does (stock's GetSequenceGroundSpeed: the root's travel over
    /// the duration), so the server's move blend is scaled as stock's server
    /// scales it; a sequence the model does not have measures nothing.
    /// </summary>
    [Fact]
    public void TheServersSkeletonMeasuresASequencesGroundSpeed()
    {
        Rig rig = BodyRig(Walk());

        Assert.Equal((100f, 0f, 0f),
                     (MathF.Round(rig.Skeletons.SequenceGroundSpeed(Entity, 0), 3), rig.Skeletons.SequenceGroundSpeed(Entity, 1),
                      rig.Skeletons.SequenceGroundSpeed(Entity, -1)));
    }

    /// <summary>The server's render binding answers it from the server's skeleton (the path the animation state takes).</summary>
    [Fact]
    public void TheServersRenderBindingAnswersTheGroundSpeedFromItsSkeleton()
    {
        Rig rig = BodyRig(Walk());

        Assert.Equal(100f, MathF.Round(ServerRender(rig).SequenceGroundSpeed(Entity, 0), 3));
    }

    /// <summary>The cost report counts each pose ask (cs_sv_bonecost's poseasks), and its reset zeroes them.</summary>
    [Fact]
    public void ThePoseAsksAreCountedForTheCostReport()
    {
        Rig rig = BodyRig(Walk());
        int dial = rig.Skeletons.LookupPoseParameter(Entity, "body_pitch");
        rig.Skeletons.SetPoseParameter(Entity, 0, 1f);
        rig.Skeletons.GetPoseParameter(Entity, 0);
        rig.Skeletons.SequenceGroundSpeed(Entity, 0);
        long counted = rig.Skeletons.PoseAsks;
        rig.Skeletons.ResetCost();

        Assert.Equal((-1, 4L, 0L), (dial, counted, rig.Skeletons.PoseAsks));
    }

    // ---- what the model declares ---------------------------------------------

    [Fact]
    public void TheCountNameAndRangeAreTheModels()
    {
        Rig rig = BodyRig();

        Assert.True(rig.Skeletons.PoseParameterRange(Entity, 0, out float start, out float end));
        Assert.Equal((1, "body_pitch", -45f, 45f), (rig.Skeletons.PoseParameterCount(Entity), rig.Skeletons.PoseParameterName(Entity, 0), start, end));
    }

    [Fact]
    public void ALookUpIsCaseInsensitive()
    {
        Assert.Equal(0, BodyRig().Skeletons.LookupPoseParameter(Entity, "BODY_Pitch"));
    }

    [Fact]
    public void AnUnknownNameIsMinusOne()
    {
        Rig rig = BodyRig();

        Assert.Equal((-1, -1), (rig.Skeletons.LookupPoseParameter(Entity, "aim_pitch"), rig.Skeletons.LookupPoseParameter(Entity, "")));
    }

    /// <summary>An index the model does not declare stores nothing and answers 0, and the head stays put.</summary>
    [Fact]
    public void AnUndeclaredIndexStoresNothing()
    {
        Rig rig = BodyRig();

        Assert.Equal((0f, 0f, 0f), (rig.Skeletons.SetPoseParameter(Entity, -1, -45f), rig.Skeletons.SetPoseParameter(Entity, 1, -45f),
                                    rig.Skeletons.GetPoseParameter(Entity, 1)));
        Assert.True(HitsHeadAt(rig, 100f));
    }

    /// <summary>A model with no pose parameters: none counted, none found, none named, none ranged.</summary>
    [Fact]
    public void AModelWithNoPoseParametersAnswersNone()
    {
        Rig rig = BodyRig();
        rig.Blocks.Held[Entity] = Block(new NVector(100f, 0f, 0f), model: BareModel);

        Assert.Equal((0, -1, "", false, 0f),
                     (rig.Skeletons.PoseParameterCount(Entity), rig.Skeletons.LookupPoseParameter(Entity, "body_pitch"),
                      rig.Skeletons.PoseParameterName(Entity, 0), rig.Skeletons.PoseParameterRange(Entity, 0, out _, out _),
                      rig.Skeletons.SetPoseParameter(Entity, 0, 10f)));
    }

    [Fact]
    public void AHandleWithNoBlockHasNoPoseParameters()
    {
        Rig rig = BodyRig();

        Assert.Equal((0, -1, 0f, 0f), (rig.Skeletons.PoseParameterCount(Other), rig.Skeletons.LookupPoseParameter(Other, "body_pitch"),
                                       rig.Skeletons.SetPoseParameter(Other, 0, 10f), rig.Skeletons.GetPoseParameter(Other, 0)));
        Assert.Equal(0, rig.Skeletons.States.Count);
    }

    // ---- stock's set: clamp, wrap, the 0..1 store ----------------------------

    [Fact]
    public void ASetBeyondTheRangeIsClampedToIt()
    {
        Rig rig = BodyRig();

        Assert.Equal((-45f, -45f), (rig.Skeletons.SetPoseParameter(Entity, 0, -90f), rig.Skeletons.GetPoseParameter(Entity, 0)));
    }

    /// <summary>The value is stored as stock stores it: Studio_SetPoseParameter's 0..1 encoding.</summary>
    [Fact]
    public void TheStoredValueIsTheZeroToOneEncoding()
    {
        Rig rig = BodyRig();

        rig.Skeletons.SetPoseParameter(Entity, 0, 22.5f);

        Assert.Equal(0.75f, rig.Skeletons.States.GetOrCreate(Entity).PoseParameter[0]);
    }

    /// <summary>A looping parameter wraps into its window rather than clamping (Studio_SetPoseParameter's loop arm).</summary>
    [Fact]
    public void ALoopingParameterWrapsIntoItsRange()
    {
        Rig rig = BodyRig(Body(loop: 360f, start: -180f, end: 180f));

        Assert.Equal((-90f, -90f), (rig.Skeletons.SetPoseParameter(Entity, 0, 270f), rig.Skeletons.GetPoseParameter(Entity, 0)));
    }

    [Fact]
    public void AnUnsetParameterReadsTheModelsDefault()
    {
        Assert.Equal(0f, BodyRig().Skeletons.GetPoseParameter(Entity, 0));
    }

    // ---- what keeps a pose and what resets it ---------------------------------

    /// <summary>The engine's model cache flushed: the images are re-read, the pose stays, and so does the posed head.</summary>
    [Fact]
    public void APoseSurvivesAModelCacheFlush()
    {
        Rig rig = BodyRig();
        rig.Skeletons.SetPoseParameter(Entity, 0, -45f);
        Assert.True(HitsHeadAt(rig, 120f), "the control: posed before the flush");

        rig.Skeletons.InvalidateMdlCache();

        Assert.Equal((-45f, true), (rig.Skeletons.GetPoseParameter(Entity, 0), HitsHeadAt(rig, 120f)));
    }

    /// <summary>A new model's defaults replace the old one's poses (stock's SetModel: InitBoneControllers).</summary>
    [Fact]
    public void AModelChangeResetsThePoseToTheNewModelsDefault()
    {
        Rig rig = BodyRig();
        rig.Memory.Models[BodyModel + 10] = Body();
        rig.Skeletons.SetPoseParameter(Entity, 0, -45f);

        rig.Blocks.Held[Entity] = Block(new NVector(100f, 0f, 0f), model: BodyModel + 10);

        Assert.Equal((0f, true), (rig.Skeletons.GetPoseParameter(Entity, 0), HitsHeadAt(rig, 100f)));
    }

    /// <summary>A hot reload carries a set pose (stock's DATADESC saves m_flPoseParameter), and the posed head with it.</summary>
    [Fact]
    public void APoseIsCarriedAcrossAHotReload()
    {
        Rig before = BodyRig();
        before.Skeletons.SetPoseParameter(Entity, 0, -45f);
        ReloadRecord record = Saved(before.Skeletons);

        Rig after = BodyRig();
        Restore(after.Skeletons, record);

        Assert.Equal((-45f, true), (after.Skeletons.GetPoseParameter(Entity, 0), HitsHeadAt(after, 120f)));
    }

    /// <summary>A pose carried for a model the entity no longer wears is the new model's default.</summary>
    [Fact]
    public void ACarriedPoseOnAnotherModelIsDropped()
    {
        Rig before = BodyRig();
        before.Skeletons.SetPoseParameter(Entity, 0, -45f);
        ReloadRecord record = Saved(before.Skeletons);

        Rig after = BodyRig();
        after.Memory.Models[BodyModel + 10] = Body();
        after.Blocks.Held[Entity] = Block(new NVector(100f, 0f, 0f), model: BodyModel + 10);
        Restore(after.Skeletons, record);

        Assert.Equal(0f, after.Skeletons.GetPoseParameter(Entity, 0));
    }

    /// <summary>A skeleton at its model's default pose and scale carries nothing.</summary>
    [Fact]
    public void ADefaultPoseCarriesNothing()
    {
        Rig before = BodyRig();
        before.Skeletons.SetPoseParameter(Entity, 0, 0f);
        Assert.True(HitsHeadAt(before, 100f));

        Assert.Empty(Saved(before.Skeletons).Rows);
    }

    // ---- the cost: a set builds nothing, the same value is no new key ---------

    /// <summary>A set only stores: no skeleton is built until an ask wants one.</summary>
    [Fact]
    public void ASetBuildsNoSkeleton()
    {
        Rig rig = BodyRig();

        rig.Skeletons.SetPoseParameter(Entity, 0, -45f);

        Assert.Equal((0L, 0), (rig.Skeletons.Builds, rig.Skeletons.CachedBoneCount(Entity)));
    }

    /// <summary>Setting the value already held moves nothing: the next trace answers from the cache.</summary>
    [Fact]
    public void TheSameValueSetAgainKeepsTheCache()
    {
        Rig rig = BodyRig();
        rig.Skeletons.SetPoseParameter(Entity, 0, -45f);
        HitsHeadAt(rig, 120f);

        rig.Skeletons.SetPoseParameter(Entity, 0, -45f);
        HitsHeadAt(rig, 120f);

        Assert.Equal(1L, rig.Skeletons.Builds);
    }

    /// <summary>
    /// A steady set (a turret aiming every think) allocates nothing: the
    /// quietest of three windows, so a one-off re-jit in one window is not
    /// counted.
    /// </summary>
    [Fact]
    public void ASteadySetAllocatesNothing()
    {
        Rig rig = BodyRig();
        HitsHeadAt(rig, 100f);
        long quietest = long.MaxValue;

        for (int window = 0; window < 3; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 1000; i++)
            {
                rig.Skeletons.SetPoseParameter(Entity, 0, (i % 90) - 45f);
                rig.Skeletons.GetPoseParameter(Entity, 0);
            }

            quietest = Math.Min(quietest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0L, quietest);
    }

    // ---- the lag compensation's rewind moves the hitboxes -------------------

    /// <summary>
    /// A rewind that changes only the pose parameters moves the hitboxes: the
    /// skeleton built before it is not the one traced after it.
    /// </summary>
    [Fact]
    public void ARewindPoseOfThePoseParametersAloneMovesTheHitboxes()
    {
        Rig rig = BodyRig();
        Assert.True(HitsHeadAt(rig, 100f), "the control: the unposed head is hit");

        float[] leaned = new float[BoneEntityState.MaxPoseParameters];
        leaned[0] = 0f; // -45 in the 0..1 encoding
        rig.Skeletons.Pose(Entity, 0, 0f, leaned);

        Assert.False(HitsHeadAt(rig, 100f));
    }
}
