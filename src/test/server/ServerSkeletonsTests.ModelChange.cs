using System;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared.Bones;

using Xunit;

using NVector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// A model change resets the sequence, as stock's CBaseAnimating::SetModel
/// does (baseanimating.cpp 2676-2687: UTIL_SetModel, InitBoneControllers,
/// SetSequence( 0 )). SetSequence writes m_nSequence alone, so the cycle,
/// the playback rate and the loop flag are what the old sequence left. Found
/// 2026-11-14 on ctf_2fort: a built sentry wore sentry1.mdl on the build
/// sequence's index, and its turret hitboxes did not follow its aim.
/// </summary>
public partial class ServerSkeletonsTests
{
    private const int ClockedModel = 8;
    private const int OtherClockedModel = 9;
    private const int TurretBuildModel = 10;
    private const int TurretModel = 11;

    /// <summary>
    /// A root that slides along x: sequence 0 a looping slide of 0.4 s (five
    /// frames at 10 fps), sequence 1 a looping slide of 0.2 s (three frames).
    /// </summary>
    private static byte[] Clocked(string name = "test/clocked.mdl")
    {
        StudioHdrBuilder b = new();
        b.ModelName = name;
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });

        foreach ((string anim, int frames) in new[] { ("slow", 5), ("quick", 3) })
        {
            short[] pos0 = [(short)(frames | (frames << 8)), 0, 10, 20, 30, 40];
            b.Anims.Add(new StudioHdrBuilder.AnimDef
            {
                Name = anim, NumFrames = frames, Fps = 10f,
                Bones = { new StudioHdrBuilder.BoneAnimDef { Bone = 0, Flags = StudioConstants.STUDIO_ANIM_ANIMPOS, Pos0 = pos0[..(frames + 1)] } },
            });
        }

        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "slow", AnimTable = [0], Flags = StudioConstants.STUDIO_LOOPING, Weights = [1f] });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "quick", AnimTable = [1], Flags = StudioConstants.STUDIO_LOOPING, Weights = [1f] });
        return b.Build();
    }

    private static Rig ClockedRig(float now = 1f)
    {
        Rig rig = new();
        rig.Memory.Models[ClockedModel] = Clocked();
        rig.Memory.Models[OtherClockedModel] = Clocked("test/clocked2.mdl");
        rig.Blocks.Held[Entity] = Block(new NVector(100f, 0f, 0f), model: ClockedModel);
        rig.Clock.Now = now;
        return rig;
    }

    /// <summary>What the native half of a SetModel does first: the block wears the new model.</summary>
    private static void Wear(Rig rig, int model)
    {
        EngineBlock block = rig.Read(Entity);
        block.Head.ModelIndex = model;
        System.Runtime.InteropServices.MemoryMarshal.Write(rig.Blocks.Held[Entity], in block);
    }

    private static (int Sequence, float Cycle, float Duration) Playing(Rig rig)
    {
        Assert.True(rig.Skeletons.SequenceState(Entity, out int sequence, out float cycle, out float duration, out _, out _));
        return (sequence, cycle, duration);
    }

    [Fact]
    public void AModelChangePlaysSequenceZeroAfterIt()
    {
        Rig rig = ClockedRig();
        rig.Skeletons.SetSequence(Entity, 1, 1f, restart: true);
        rig.Clock.Now = 1.05f;
        Assert.Equal(1, Playing(rig).Sequence);

        Wear(rig, OtherClockedModel);
        rig.Skeletons.ModelChanged(Entity);

        Assert.Equal(0, Playing(rig).Sequence);
    }

    [Fact]
    public void AModelChangeLeavesTheCycleWhereTheOldSequenceHadIt()
    {
        // Sequence 1 (0.2 s) started at t=1: at t=1.05 it is a quarter through.
        Rig rig = ClockedRig();
        rig.Skeletons.SetSequence(Entity, 1, 1f, restart: true);
        rig.Clock.Now = 1.05f;
        float before = Playing(rig).Cycle;

        Wear(rig, OtherClockedModel);
        rig.Skeletons.ModelChanged(Entity);

        Assert.Equal(0.25f, before, 4);
        Assert.Equal(before, Playing(rig).Cycle, 4);
    }

    [Fact]
    public void AfterAModelChangeTheCycleAdvancesAtSequenceZerosRate()
    {
        // From a quarter at t=1.05, a tenth of a second on: sequence 0 runs
        // 0.4 s, so a quarter more (stock's StudioFrameAdvance reads the rate
        // of the sequence now set); sequence 1's rate would have made it 0.75.
        Rig rig = ClockedRig();
        rig.Skeletons.SetSequence(Entity, 1, 1f, restart: true);
        rig.Clock.Now = 1.05f;

        Wear(rig, OtherClockedModel);
        rig.Skeletons.ModelChanged(Entity);
        rig.Clock.Now = 1.15f;

        (int sequence, float cycle, float duration) = Playing(rig);
        Assert.Equal((0, 0.4f), (sequence, MathF.Round(duration, 4)));
        Assert.Equal(0.5f, cycle, 4);
    }

    [Fact]
    public void TheSameModelSetAgainStillPlaysSequenceZero()
    {
        // Stock's SetModel has no early out for the model already worn.
        Rig rig = ClockedRig();
        rig.Skeletons.SetSequence(Entity, 1, 1f, restart: true);
        rig.Clock.Now = 1.05f;

        rig.Skeletons.ModelChanged(Entity);

        Assert.Equal(0, Playing(rig).Sequence);
    }

    [Fact]
    public void AModelChangeOnASkeletonNoSequenceWasPlayedOnStartsNoClock()
    {
        // An entity nothing animates keeps its first frame, as a stock entity
        // that never calls StudioFrameAdvance keeps m_flCycle 0.
        Rig rig = ClockedRig();

        Wear(rig, OtherClockedModel);
        rig.Skeletons.ModelChanged(Entity);
        rig.Clock.Now = 2.1f;

        Assert.Equal((0, 0f), (Playing(rig).Sequence, Playing(rig).Cycle));
    }

    [Fact]
    public void AModelChangeWritesTheResetClockIntoTheBlockForAReload()
    {
        Rig rig = ClockedRig();
        rig.Skeletons.SetSequence(Entity, 1, 1f, restart: true);
        rig.Clock.Now = 1.05f;

        Wear(rig, OtherClockedModel);
        rig.Skeletons.ModelChanged(Entity);

        // A fresh composition over the same blocks: the hot reload's first ask.
        var reloaded = new ServerSkeletons(rig.Blocks, new StudioModels(rig.Memory), rig.Clock, rig.Log, rig.Props);
        Assert.True(reloaded.SequenceState(Entity, out int sequence, out float cycle, out _, out _, out _));
        Assert.Equal(0, sequence);
        Assert.Equal(0.25f, cycle, 4);
    }

    [Fact]
    public void AHandleWithNoBlockHasNoModelToChange()
    {
        Rig rig = new();

        Assert.False(rig.Skeletons.ModelChanged(Other));
        Assert.Equal(0, rig.Skeletons.States.Count);
    }

    // ---- the sentry's shape --------------------------------------------------

    /// <summary>
    /// <see cref="Body"/> with a second sequence, "still", that holds the head
    /// straight whatever the pose: sequence 0 aims by body_pitch, sequence 1
    /// does not, as sentry1.mdl's aim and its building model's build do.
    /// </summary>
    private static byte[] Turret(string name)
    {
        StudioHdrBuilder b = new();
        b.ModelName = name;
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "head", Parent = 0, Flags = UsedByAnything, Pos = new NVector(0f, 0f, 50f) });

        foreach ((string anim, float x) in new[] { ("lean_forward", 20f), ("lean_back", -20f), ("straight", 0f) })
        {
            b.Anims.Add(new StudioHdrBuilder.AnimDef
            {
                Name = anim,
                NumFrames = 1,
                Bones = { new StudioHdrBuilder.BoneAnimDef { Bone = 1, Flags = StudioConstants.STUDIO_ANIM_RAWPOS, RawPosition = new NVector(x, 0f, 50f) } },
            });
        }

        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "body_pitch", Start = -45f, End = 45f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "aim",
            NumBlends = 2,
            GroupSize0 = 2,
            GroupSize1 = 1,
            AnimTable = [0, 1],
            Weights = [1f, 1f],
            ParamIndex0 = 0,
            ParamStart = [-45f, 0f],
            ParamEnd = [45f, 1f],
        });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "still", AnimTable = [2], Weights = [1f, 1f] });
        b.HitboxSets.Add(new StudioHdrBuilder.HitboxSetDef
        {
            Name = "default",
            Boxes = { new StudioHdrBuilder.HitboxDef { Bone = 1, Min = new NVector(-4f, -4f, -4f), Max = new NVector(4f, 4f, 4f) } },
        });

        return SolidBones(b.Build(), 2);
    }

    /// <summary>
    /// The building model plays "still" (sequence 1); the finished model is
    /// worn and aimed at body_pitch -45, which leans the head twenty forward
    /// in "aim" (sequence 0). Answers whether a trace finds the head where the
    /// pose puts it (x 120) and where the straight head stood (x 100).
    /// </summary>
    private static (bool AtPose, bool AtStraight) BuiltTurretAimed(bool modelChangeReaches)
    {
        Rig rig = new();
        rig.Memory.Models[TurretBuildModel] = Turret("test/turret_build.mdl");
        rig.Memory.Models[TurretModel] = Turret("test/turret.mdl");
        rig.Blocks.Held[Entity] = Block(new NVector(100f, 0f, 0f), model: TurretBuildModel);
        rig.Skeletons.SetSequence(Entity, 1, 1f, restart: true);

        Wear(rig, TurretModel);
        if (modelChangeReaches)
            rig.Skeletons.ModelChanged(Entity);

        rig.Skeletons.SetPoseParameter(Entity, 0, -45f);
        return (HitsHeadAt(rig, 120f), HitsHeadAt(rig, 100f));
    }

    [Fact]
    public void ABuiltTurretsHitboxFollowsItsAimAfterTheModelChange()
    {
        // The control: with nothing told of the model change, the head stays
        // straight on "still" whatever the aim, which was the sentry's fault.
        Assert.Equal((false, true), BuiltTurretAimed(modelChangeReaches: false));
        Assert.Equal((true, false), BuiltTurretAimed(modelChangeReaches: true));
    }

    // ---- the server's render binding ----------------------------------------

    [Fact]
    public void TheServersSetModelResetsTheSkeletonsSequence()
    {
        Rig rig = ClockedRig();
        rig.Skeletons.SetSequence(Entity, 1, 1f, restart: true);
        rig.Clock.Now = 1.05f;

        // The test side's native area is a default that writes nothing, so
        // the block is dressed first, as the native half does in a game.
        Wear(rig, OtherClockedModel);
        ServerRender(rig).SetModel(Entity, "test/clocked2.mdl");

        Assert.Equal(0, Playing(rig).Sequence);
    }
}
