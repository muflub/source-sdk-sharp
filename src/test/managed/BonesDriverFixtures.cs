using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using SourceSharp.Shared.Bones;
using Xunit;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>Writes a ragdoll's bones; answers how many it owns.</summary>
internal delegate int RagdollWriter(BoneMatrix3x4[] bones, Span<int> owned);

/// <summary>
/// A fake engine for the bone drivers: a model table (a present key with a
/// null header is a model whose studio data is momentarily unreadable), a
/// ragdoll table, a merge-source table, and counters for every ask, so a
/// fact can say "the cache answered" by "the model was not opened".
/// </summary>
internal sealed class FakeBoneWorld : IClientBoneWorld
{
    public Dictionary<int, StudioHdr?> Models { get; } = [];
    public int Opens { get; private set; }

    public HashSet<uint> Bodies { get; } = [];
    public RagdollWriter? Ragdoll { get; set; }
    public int RagdollWrites { get; private set; }

    public Dictionary<int, BoneMergeSourceView> Sources { get; } = [];
    public Dictionary<nint, StudioHdr?> SourceModels { get; } = [];
    public int SourceModelOpens { get; private set; }
    public Func<int, BoneMatrix3x4[], int, float, bool>? SourceBones { get; set; }
    public int SourceSetupBonesCalls { get; private set; }
    public int LastSourceMask { get; private set; }

    public List<string> Said { get; } = [];

    public bool HasStudioModel(int modelIndex) => Models.ContainsKey(modelIndex);

    public StudioHdr? OpenStudioHdr(int modelIndex)
    {
        Opens++;
        return Models.TryGetValue(modelIndex, out StudioHdr? hdr) ? hdr : null;
    }

    public bool IsRagdollBody(uint ragdoll) => Bodies.Contains(ragdoll);

    public int WriteRagdollBones(uint ragdoll, BoneMatrix3x4[] bones, Span<int> ownedBones)
    {
        RagdollWrites++;
        return Ragdoll is null ? 0 : Ragdoll(bones, ownedBones);
    }

    public BoneMergeSourceView ResolveMergeSource(int entIndex) =>
        Sources.TryGetValue(entIndex, out BoneMergeSourceView view) ? view : default;

    public StudioHdr? OpenStudioHdrForModel(nint model)
    {
        SourceModelOpens++;
        return SourceModels.TryGetValue(model, out StudioHdr? hdr) ? hdr : null;
    }

    public bool SourceSetupBones(int entIndex, BoneMatrix3x4[] bones, int boneMask, float currentTime)
    {
        SourceSetupBonesCalls++;
        LastSourceMask = boneMask;
        return SourceBones is not null && SourceBones(entIndex, bones, boneMask, currentTime);
    }

    public void Say(string message) => Said.Add(message);
}

/// <summary>Model fixtures for the driver facts, over StudioHdrBuilder.</summary>
internal static class BonesDriverModels
{
    public const int UsedByAnything = 0x0007FF00;
    public const int UsedByHitbox = 0x100;
    public const int UsedByAttachment = 0x200;
    public const int UsedByVertexLod0 = 0x400;
    public const int AllBits = unchecked((int)0xFFFFFFFF);

    public static StudioHdr Make(StudioHdrBuilder b, Action<byte[]>? patch = null)
    {
        foreach (StudioHdrBuilder.SeqDef seq in b.Sequences)
        {
            if (seq.Weights is null || seq.Weights.Length != b.Bones.Count)
            {
                seq.Weights = new float[b.Bones.Count];
                Array.Fill(seq.Weights, 1f);
            }
        }

        byte[] image = b.Build();
        patch?.Invoke(image);
        Assert.True(StudioHdr.TryCreate(image, b.Resolver, out StudioHdr? hdr) && hdr is not null, "fixture rejected");
        return hdr!;
    }

    /// <summary>An ANIMPOS X ramp on one bone: frame i is x = step * i.</summary>
    public static StudioHdrBuilder.AnimDef Ramp(int bone, int frames, float fps, short step = 10, string name = "ramp")
    {
        short[] pos0 = new short[1 + frames];
        pos0[0] = (short)(frames | (frames << 8));
        for (int i = 0; i < frames; i++)
        {
            pos0[1 + i] = (short)(step * i);
        }

        return new StudioHdrBuilder.AnimDef
        {
            Name = name,
            NumFrames = frames,
            Fps = fps,
            Bones = { new StudioHdrBuilder.BoneAnimDef { Bone = bone, Flags = StudioConstants.STUDIO_ANIM_ANIMPOS, Pos0 = pos0 } },
        };
    }

    /// <summary>root -> arm -> hand, every bone used by anything, one looping
    /// 3-frame ramp at 30 fps on the root (duration 2/30).</summary>
    public static StudioHdrBuilder ChainBuilder(int seqFlags = StudioConstants.STUDIO_LOOPING)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "arm", Parent = 0, Flags = UsedByAnything, Pos = new Vector(0f, 0f, 10f) });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "hand", Parent = 1, Flags = UsedByAnything, Pos = new Vector(5f, 0f, 0f) });
        b.Anims.Add(Ramp(0, 3, 30f));
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "ramp", AnimTable = [0], Flags = seqFlags });
        return b;
    }

    public static StudioHdr Chain(int seqFlags = StudioConstants.STUDIO_LOOPING) => Make(ChainBuilder(seqFlags));

    /// <summary>The chain with a second and third sequence (ramps of
    /// different slopes on the root) for layers.</summary>
    public static StudioHdr LayeredChain()
    {
        StudioHdrBuilder b = ChainBuilder();
        b.Anims.Add(Ramp(0, 3, 30f, 40, "steep"));
        b.Anims.Add(Ramp(0, 3, 30f, -30, "back"));
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "steep", AnimTable = [1], Flags = StudioConstants.STUDIO_LOOPING });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "back", AnimTable = [2], Flags = StudioConstants.STUDIO_LOOPING });
        return Make(b);
    }

    /// <summary>The chain whose one sequence autoplays.</summary>
    public static StudioHdr AutoplayChain() => Chain(StudioConstants.STUDIO_AUTOPLAY | StudioConstants.STUDIO_LOOPING);

    /// <summary>The chain flagged a static prop.</summary>
    public static StudioHdr StaticProp()
    {
        StudioHdrBuilder b = ChainBuilder();
        b.HdrFlags = BoneLibrary.StudioHdrFlagsStaticProp;
        return Make(b);
    }

    /// <summary>The chain with a pose parameter of -90..270 (default 0.25).</summary>
    public static StudioHdr PosedChain()
    {
        StudioHdrBuilder b = ChainBuilder();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "aim_yaw", Start = -90f, End = 270f });
        return Make(b);
    }

    /// <summary>The chain with attachments: "muzzle" on the hand, "base" on the root.</summary>
    public static StudioHdr AttachedChain()
    {
        StudioHdrBuilder b = ChainBuilder();
        BoneMatrix3x4 local = BoneMatrix3x4.Identity;
        local.Position = new Vector(1f, 2f, 3f);
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "muzzle", LocalBone = 2, Local = local });
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "base", LocalBone = 0 });
        return Make(b);
    }

    /// <summary>The chain with one hitbox set: a box on the hand.</summary>
    public static StudioHdr HitboxChain()
    {
        StudioHdrBuilder b = ChainBuilder();
        b.HitboxSets.Add(new StudioHdrBuilder.HitboxSetDef
        {
            Name = "default",
            Boxes = { new StudioHdrBuilder.HitboxDef { Bone = 2, Min = new Vector(-2f, -2f, -2f), Max = new Vector(2f, 2f, 2f) } },
        });
        return Make(b, image =>
        {
            int boneIndex = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(160, 4));
            for (int i = 0; i < 3; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(boneIndex + 216 * i + 180, 4), BoneSetupTrace.ContentsSolid);
            }
        });
    }

    /// <summary>root -> tail; the tail a jiggle bone with a boing record
    /// (procType 5), or another proctype.</summary>
    public static StudioHdr JiggleTail(int procType = ProceduralBoneRule.ProcJiggle)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "tail", Parent = 0, Flags = UsedByAnything | ProceduralBoneRule.BoneAlwaysProcedural, Pos = new Vector(0f, 0f, 4f) });
        b.Anims.Add(Ramp(0, 3, 30f));
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "ramp", AnimTable = [0], Flags = StudioConstants.STUDIO_LOOPING, Weights = [1f, 1f] });
        byte[] built = b.Build();
        float[] record = new float[35];
        record[0] = BitConverter.Int32BitsToSingle(JiggleBoneParams.IsBoing);
        record[1] = 10f;
        record[34] = 0.5f;
        byte[] image = new byte[built.Length + 4 * record.Length + 4];
        built.CopyTo(image, 0);
        int at = (built.Length + 3) & ~3;
        for (int i = 0; i < record.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(image.AsSpan(at + 4 * i, 4), record[i]);
        }

        int bone1 = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(160, 4)) + 216;
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(bone1 + 164, 4), procType);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(bone1 + 168, 4), at - bone1);
        Assert.True(StudioHdr.TryCreate(image, b.Resolver, out StudioHdr? hdr) && hdr is not null);
        return hdr!;
    }

    private static StudioHdrBuilder.IkErrorFrame[] FlatFrames(Vector pos)
    {
        StudioHdrBuilder.IkErrorFrame[] f = new StudioHdrBuilder.IkErrorFrame[101];
        for (int i = 0; i < f.Length; i++)
        {
            f[i] = new StudioHdrBuilder.IkErrorFrame { Pos = pos, Quat = Quaternion.Identity };
        }

        return f;
    }

    /// <summary>
    /// A leg (BonesIkClientTests' ground fixture): four bones, one chain
    /// over bones 1-3, one IK_GROUND rule on bone 2 whose error pulls the
    /// foot 45 down, live over cycles 0.25-0.75 of a looping 101-frame
    /// sequence (duration 100/30).
    /// </summary>
    public static StudioHdr Leg()
    {
        StudioHdrBuilder b = new();
        for (int i = 0; i < 4; i++)
        {
            b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b" + i, Parent = i - 1, Flags = AllBits });
        }

        b.Bones[1].Pos = new Vector(0f, 0f, 50f);
        b.Bones[2].Pos = new Vector(0f, 15f, -60f);
        b.Bones[3].Pos = new Vector(0f, -30f, 0f);
        b.ModelName = "b0";
        StudioHdrBuilder.IkRuleDef rule = new()
        {
            Type = StudioConstants.IK_GROUND, Chain = 0, Bone = 2, Slot = 2,
            Start = 0.25f, Peak = 0.4375f, Tail = 0.625f, End = 0.75f,
            Height = 5f, Floor = 8f, Radius = 3f,
            Errors = FlatFrames(new Vector(0f, 0f, -45f)),
        };
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "a0", NumFrames = 101, Fps = 30, IkRules = { rule } });
        StudioHdrBuilder.SeqDef seq = new() { Label = "s0", AnimTable = [0], Flags = StudioConstants.STUDIO_LOOPING };
        seq.IkRules.Add(rule);
        b.Sequences.Add(seq);
        b.IkChains.Add(new StudioHdrBuilder.IkChainDef { Name = "leg", Links = { (1, Vector.Zero), (2, Vector.Zero), (3, Vector.Zero) } });
        return Make(b);
    }

    /// <summary>Bones named <paramref name="names"/>, chained, static prop (bone i = placement).</summary>
    public static StudioHdr NamedStatic(params string[] names)
    {
        StudioHdrBuilder b = new();
        for (int i = 0; i < names.Length; i++)
        {
            b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = names[i], Parent = i - 1, Flags = UsedByAnything });
        }

        b.HdrFlags = BoneLibrary.StudioHdrFlagsStaticProp;
        return Make(b);
    }

    public static BoneMatrix3x4 Translation(float x, float y, float z)
    {
        BoneMatrix3x4 m = BoneMatrix3x4.Identity;
        m.Position = new Vector(x, y, z);
        return m;
    }

    public static readonly BoneMatrix3x4 Sentinel = new()
    {
        Row0 = new Vector4(7f, 7f, 7f, 7f),
        Row1 = new Vector4(7f, 7f, 7f, 7f),
        Row2 = new Vector4(7f, 7f, 7f, 7f),
    };
}
