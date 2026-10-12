using System;
using System.Buffers.Binary;
using SourceSharp.Shared.Bones;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for the small StudioHdr accessors step 1.0b added so the 1.0 files
/// stop reading raw record offsets: <c>mstudiobone_t::proctype</c> (@164),
/// <c>procindex</c> (@168) and <c>CStudioHdr::CountAutoplaySequences</c>.
/// </summary>
public class BonesStudioHdrAccessorTests
{
    private const int UsedByAnything = 0x0007FF00;

    private static StudioHdr TwoBones(Action<byte[]>? patch = null)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "tail", Parent = 0, Flags = UsedByAnything });
        byte[] image = b.Build();
        patch?.Invoke(image);
        Assert.True(StudioHdr.TryCreate(image, b.Resolver, out StudioHdr? hdr) && hdr is not null);
        return hdr!;
    }

    /// <summary>Writes one int into bone 1's record at <paramref name="offset"/>.</summary>
    private static Action<byte[]> Bone1(int offset, int value) => image =>
    {
        int bone1 = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(160, 4)) + 216;
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(bone1 + offset, 4), value);
    };

    [Fact]
    public void ProcTypeReadsTheRecordAt164()
    {
        Assert.Equal(5, TwoBones(Bone1(164, 5)).Bone(1).ProcType);
    }

    [Fact]
    public void ProcTypeIsZeroOnAnUnpatchedBone()
    {
        Assert.Equal(0, TwoBones(Bone1(164, 5)).Bone(0).ProcType);
    }

    [Fact]
    public void ProcIndexReadsTheRecordAt168()
    {
        Assert.Equal(48, TwoBones(Bone1(168, 48)).Bone(1).ProcIndex);
    }

    private static StudioHdr Sequences(params int[] flags)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = UsedByAnything });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "a" });
        foreach (int f in flags)
        {
            b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", AnimTable = [0], Weights = [1f], Flags = f });
        }

        Assert.True(StudioHdr.TryCreate(b.Build(), b.Resolver, out StudioHdr? hdr) && hdr is not null);
        return hdr!;
    }

    [Fact]
    public void CountAutoplaySequencesCountsOnlyTheFlaggedOnes()
    {
        int autoplay = StudioConstants.STUDIO_AUTOPLAY;
        Assert.Equal(2, Sequences(autoplay, StudioConstants.STUDIO_LOOPING, autoplay | StudioConstants.STUDIO_LOOPING).CountAutoplaySequences());
    }

    [Fact]
    public void CountAutoplaySequencesIsZeroWithNoneFlagged()
    {
        Assert.Equal(0, Sequences(0, StudioConstants.STUDIO_LOOPING).CountAutoplaySequences());
    }
}
