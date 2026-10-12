using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Shared.Bones;
using Xunit;
using static SourceSharp.Tests.BonesDriverModels;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for the studio header handoff (plan physics_bones_native_zero step
/// 1.2): <see cref="StudioImageFold"/>, which puts a model's .ani blocks where
/// StudioHdr reads them, and <see cref="StudioModels"/>, the per-level cache
/// of folded headers over a side's model cache.
/// </summary>
public class BonesStudioModelsTests
{
    private const int Model = 7;

    // Far past any fixture's length, so an UNFOLDED read of the block misses
    // the image altogether rather than landing on header bytes.
    private const int AniStart = 1 << 20;

    /// <summary>A fake model cache: images by index and include name, blocks
    /// by (model or include, block), and a count of every ask.</summary>
    private sealed class Memory : IStudioMemory
    {
        public Dictionary<int, byte[]> Models { get; } = [];
        public Dictionary<int, Dictionary<int, byte[]>> Blocks { get; } = [];
        public Dictionary<string, byte[]> Includes { get; } = [];
        public int ModelAsks { get; private set; }
        public int IncludeAsks { get; private set; }

        public byte[]? ModelImage(int modelIndex)
        {
            ModelAsks++;
            return Models.TryGetValue(modelIndex, out byte[]? b) ? b : null;
        }

        public byte[]? ModelAnimBlock(int modelIndex, int block) =>
            Blocks.TryGetValue(modelIndex, out var blocks) && blocks.TryGetValue(block, out byte[]? b) ? b : null;

        public byte[]? IncludeImage(int modelIndex, string includeName)
        {
            IncludeAsks++;
            return Includes.TryGetValue(includeName, out byte[]? b) ? b : null;
        }

        public byte[]? IncludeAnimBlock(int modelIndex, string includeName, int block) => null;
    }

    /// <summary>
    /// The chain fixture with its ramp moved into animation block 1 of an
    /// .ani whose data starts at <see cref="AniStart"/>: the animdesc names
    /// block 1 at index 0, and the block's bytes are the chain the builder
    /// placed inline. Answers the header and the block.
    /// </summary>
    private static (byte[] Mdl, byte[] Block) Split()
    {
        StudioHdrBuilder b = ChainBuilder();
        b.Anims[0].AnimBlock = 1;
        byte[] built = BuildImage(b);

        int length = ReadI32(built, 76);
        int desc = ReadI32(built, 184);
        int chain = desc + ReadI32(built, desc + 56);
        byte[] block = built.AsSpan(chain, length - chain).ToArray();

        // The block table, two mstudioanimblock_t (block 0 unused), appended
        // and counted in the header's length as studiomdl would lay it out.
        int table = (length + 3) & ~3;
        byte[] mdl = new byte[table + 16];
        built.AsSpan(0, length).CopyTo(mdl);
        WriteI32(mdl, 76, mdl.Length);
        WriteI32(mdl, 352, 2);
        WriteI32(mdl, 356, table);
        WriteI32(mdl, table + 8, AniStart);
        WriteI32(mdl, table + 12, AniStart + block.Length);
        WriteI32(mdl, desc + 56, 0);
        return (mdl, block);
    }

    private static byte[] BuildImage(StudioHdrBuilder b)
    {
        foreach (StudioHdrBuilder.SeqDef seq in b.Sequences)
        {
            seq.Weights ??= [1f, 1f, 1f];
        }

        return b.Build();
    }

    private static int ReadI32(byte[] image, int at) => BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at, 4));

    private static void WriteI32(byte[] image, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(at, 4), value);

    private static StudioHdr Hdr(byte[] image)
    {
        Assert.True(StudioHdr.TryCreate(image, null, out StudioHdr? hdr) && hdr is not null, "image rejected");
        return hdr!;
    }

    /// <summary>The root bone's x at a cycle of 0.75 (frame 1.5 of the ramp).</summary>
    private static float RootX(StudioHdr hdr)
    {
        FakeBoneWorld w = new();
        w.Models[Model] = hdr;
        ServerBoneDriver d = new(w);
        BoneEntityState s = new();
        d.SetPoseSequence(s, Model, 0, 1f, restart: true, now: 0f);

        ServerBonePose pose = new() { ModelIndex = Model, AbsOrigin = Vector.Zero, AbsAngles = default };
        Assert.True(d.EnsureBones(s, pose, UsedByAnything, 0.05f, BoneConVars.Stock));
        return s.CachedBones[0].Position.X;
    }

    // ---- the fold --------------------------------------------------------

    [Fact]
    public void FoldOfAModelWithNoBlocksIsTheSameImage()
    {
        byte[] mdl = BuildImage(ChainBuilder());
        Assert.Same(mdl, StudioImageFold.Fold(mdl, _ => throw new InvalidOperationException("asked")));
    }

    [Fact]
    public void ABlockedAnimationFoldedPosesAsTheInlineOneDoes()
    {
        float inline = RootX(Make(ChainBuilder()));
        (byte[] mdl, byte[] block) = Split();

        float folded = RootX(Hdr(StudioImageFold.Fold(mdl, b => b == 1 ? block : null)));

        Assert.NotEqual(0f, inline);
        Assert.Equal(inline, folded);
    }

    [Fact]
    public void ABlockedAnimationUnfoldedDoesNotPose()
    {
        // The negative control for the fact above: the same header, read as
        // StudioHdr reads it without the fold, does not find the ramp.
        float inline = RootX(Make(ChainBuilder()));
        (byte[] mdl, _) = Split();

        Assert.NotEqual(inline, RootX(Hdr(mdl)));
    }

    [Fact]
    public void FoldRebasesTheBlockTableOntoTheAppendedBytes()
    {
        (byte[] mdl, byte[] block) = Split();
        byte[] image = StudioImageFold.Fold(mdl, b => b == 1 ? block : null);

        int length = ReadI32(mdl, 76);
        int table = ReadI32(image, 356);

        Assert.Equal((length + AniStart, length + AniStart + block.Length, length + AniStart + block.Length),
                     (ReadI32(image, table + 8), ReadI32(image, table + 12), image.Length));
        Assert.True(image.AsSpan(length + AniStart, block.Length).SequenceEqual(block));
        Assert.Equal(AniStart, ReadI32(mdl, table + 8)); // the source was not written
    }

    [Fact]
    public void AMissingBlockIsZeroedSoTheReaderAnswersNone()
    {
        (byte[] mdl, _) = Split();
        byte[] image = StudioImageFold.Fold(mdl, _ => null);
        int table = ReadI32(image, 356);

        Assert.Equal((0, 0), (ReadI32(image, table + 8), ReadI32(image, table + 12)));
    }

    [Fact]
    public void ABlockOfTheWrongLengthIsMissing()
    {
        (byte[] mdl, byte[] block) = Split();
        byte[] image = StudioImageFold.Fold(mdl, _ => block[..^1]);
        int table = ReadI32(image, 356);

        Assert.Equal((0, 0), (ReadI32(image, table + 8), ReadI32(image, table + 12)));
    }

    // ---- the cache -------------------------------------------------------

    [Fact]
    public void AModelIsReadFromTheEngineOncePerLevel()
    {
        Memory m = new();
        m.Models[Model] = BuildImage(ChainBuilder());
        StudioModels models = new(m);

        StudioHdr? first = models.OpenStudioHdr(Model);
        StudioHdr? second = models.OpenStudioHdr(Model);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal((1, 1), (m.ModelAsks, models.ImagesRead));
    }

    [Fact]
    public void AModelThatAnswersNoneIsAskedAgain()
    {
        Memory m = new();
        StudioModels models = new(m);

        Assert.Null(models.OpenStudioHdr(Model));
        m.Models[Model] = BuildImage(ChainBuilder());

        Assert.NotNull(models.OpenStudioHdr(Model));
        Assert.Equal(2, m.ModelAsks);
    }

    [Fact]
    public void ClearDropsEveryModel()
    {
        Memory m = new();
        m.Models[Model] = BuildImage(ChainBuilder());
        StudioModels models = new(m);
        StudioHdr? before = models.OpenStudioHdr(Model);

        models.Clear();

        Assert.Equal(0, models.Count);
        Assert.NotSame(before, models.OpenStudioHdr(Model));
        Assert.Equal(2, m.ModelAsks);
    }

    [Fact]
    public void TheModelsBlocksAreFoldedOnTheWayIn()
    {
        (byte[] mdl, byte[] block) = Split();
        Memory m = new();
        m.Models[Model] = mdl;
        m.Blocks[Model] = new Dictionary<int, byte[]> { [1] = block };

        StudioHdr? hdr = new StudioModels(m).OpenStudioHdr(Model);

        Assert.NotNull(hdr);
        Assert.Equal(RootX(Make(ChainBuilder())), RootX(hdr!));
    }

    [Fact]
    public void AnIncludeIsReadByNameOncePerLevel()
    {
        StudioHdrBuilder g = new();
        g.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        byte[] groupBytes = g.Build();

        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "g", Name = "models/grp.mdl" });

        Memory m = new();
        m.Models[Model] = b.Build();
        m.Models[Model + 1] = b.Build();
        m.Includes["models/grp.mdl"] = groupBytes;
        StudioModels models = new(m);

        StudioHdr? one = models.OpenStudioHdr(Model);
        StudioHdr? two = models.OpenStudioHdr(Model + 1);

        Assert.True(one is { IsVirtual: true } && two is { IsVirtual: true });
        Assert.Equal(1, m.IncludeAsks);
    }

    [Fact]
    public void AModelWhoseIncludeIsMissingIsRefused()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Includes.Add(new StudioHdrBuilder.IncludeDef { Label = "g", Name = "models/gone.mdl" });

        Memory m = new();
        m.Models[Model] = b.Build();

        Assert.Null(new StudioModels(m).OpenStudioHdr(Model));
    }

    [Fact]
    public void TheStudioNameIsTheHeadersOwn()
    {
        StudioHdrBuilder b = ChainBuilder();
        b.ModelName = @"Humans\Group03\Male_01.mdl";
        Memory m = new();
        m.Models[Model] = BuildImage(b);

        StudioModels models = new(m);

        Assert.Equal(@"Humans\Group03\Male_01.mdl", models.StudioName(Model));
        Assert.Equal(string.Empty, models.StudioName(Model + 1));
    }

    // ---- the bone record's surfaceprop -----------------------------------

    [Fact]
    public void ABonesSurfacePropNameIsReadOffTheBoneRecord()
    {
        byte[] mdl = BuildImage(ChainBuilder());
        byte[] name = Encoding.ASCII.GetBytes("flesh\0");
        byte[] image = new byte[mdl.Length + name.Length];
        mdl.CopyTo(image, 0);
        name.CopyTo(image, mdl.Length);

        int bone1 = ReadI32(image, 160) + 216;
        WriteI32(image, bone1 + 176, mdl.Length - bone1);

        StudioHdr hdr = Hdr(image);

        Assert.Equal(("", "flesh"), (hdr.Bone(0).SurfacePropName, hdr.Bone(1).SurfacePropName));
    }
}
