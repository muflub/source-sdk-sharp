using System;
using SourceSharp.Shared.Bones;
using Xunit;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// The query lane's hdr readers: the <c>KEYVALUETEXT</c> blob
/// (studio.h:2308-2310), the flexcontroller table (@268/@272,
/// studio.h:928-937) and the <c>studiohdr2_t</c> bone-flexdriver table
/// (@24/@28, studio.h:2117-2119 over the 24-byte
/// <c>mstudioboneflexdriver_t</c> of studio.h:383-403 and the 16-byte
/// <c>mstudioboneflexdrivercontrol_t</c> of studio.h:364-377) — the tables
/// <c>Studio_GetKeyValueText</c> / <c>Studio_RunBoneFlexDrivers</c> /
/// <c>Studio_FindRandomAttachment</c> answer from. Every expectation is
/// hand-derived from the bytes the fixture declares below, never read back
/// out of the builder.
/// </summary>
public class BonesQueryHdrTests
{
    private static StudioHdr Build(StudioHdrBuilder b) =>
        StudioHdr.TryCreate(b.BuildWithQuery(), null, out StudioHdr? h)!
            ? h!
            : throw new InvalidOperationException("fixture image rejected by TryCreate");

    private static StudioHdrBuilder BaseBuilder()
    {
        var b = new StudioHdrBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0" });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0 });
        // Reuse a queued string as the hdr name: Build() patches @12 from
        // StringAddr(ModelName), which only resolves for a string some
        // record also queues (builder gap reported 2026-10-07).
        b.ModelName = "b0";
        return b;
    }

    // ---- KEYVALUETEXT -------------------------------------------------------

    /// <summary>Round-trip: the exact string the fixture wrote comes back.
    /// qc stores <c>size = strlen + 1</c>; the builder follows that rule, so
    /// the 36-char blob below is size 37 and the NUL sits inside the blob,
    /// not past its end.</summary>
    [Fact]
    public void KeyValueText_RoundTripsTheExactBlob()
    {
        StudioHdrBuilder b = BaseBuilder();
        const string kv = "keyvalues { delivery 1 breakable 1 }";
        b.KeyValueText = kv;
        StudioHdr hdr = Build(b);

        Assert.Equal(kv, hdr.KeyValueText());
        // Hand-derived: strlen(kv) == 36, qc size == 37.
        Assert.Equal(36, kv.Length);
        Assert.Equal(37, hdr.KeyValueSize);
        Assert.True(hdr.KeyValueIndex > 0, "the blob must sit inside the image");
        Assert.True(hdr.KeyValueIndex + hdr.KeyValueSize <= hdr.ImageLength,
            "the whole blob must fit inside the image");
    }

    /// <summary>The absent arm: a model with no blob is stock's NULL
    /// (<c>keyvaluesize == 0</c>), NOT an empty string —
    /// <c>Studio_GetKeyValueText</c> hands that NULL straight to
    /// <c>LoadFromBuffer</c>.</summary>
    [Fact]
    public void KeyValueText_AbsentTableIsNull()
    {
        StudioHdr hdr = Build(BaseBuilder()); // no KeyValueText set
        Assert.Equal(0, hdr.KeyValueIndex);
        Assert.Equal(0, hdr.KeyValueSize);
        Assert.Null(hdr.KeyValueText());
    }

    /// <summary>The empty-string arm is stock's NULL too: qc would store
    /// size 1 for "", but a hand-written size-0 blob must read null, not
    /// "". Guards the reader's size-0 check against a start==end collapse.</summary>
    [Fact]
    public void KeyValueText_ZeroSizeBlobIsNullNotEmpty()
    {
        StudioHdr hdr = Build(BaseBuilder());
        byte[] image = RebuildWithZeroSizeBlob();
        Assert.True(StudioHdr.TryCreate(image, null, out StudioHdr? h) && h is not null);
        Assert.Null(h!.KeyValueText());
        _ = hdr; // the plain fixture is only here to prove the arm differs from the default
    }

    /// <summary>Build a fixture whose blob bytes exist but whose size field
    /// says 0 (hand-patched): stock's inline returns NULL for size 0 even
    /// though the bytes are there.</summary>
    private static byte[] RebuildWithZeroSizeBlob()
    {
        StudioHdrBuilder b = BaseBuilder();
        b.KeyValueText = "ignored";
        byte[] image = b.BuildWithQuery();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(316, 4), 0);
        return image;
    }

    // ---- flexdrivers --------------------------------------------------------

    /// <summary>Two drivers over three controls: count/index wiring, the
    /// exact per-entry fields, and the record-relative control index landing
    /// on the control array BEYOND the uniform 24-byte record stride (stock's
    /// typed-pointer walk forbids inline arrays).</summary>
    [Fact]
    public void BoneFlexDriver_EntriesMatchTheFixtureBytes()
    {
        StudioHdrBuilder b = BaseBuilder();
        b.FlexControllers.Add(new StudioHdrBuilder.FlexControllerDefEntry
        {
            Type = "eyes", Name = "eyeLeft", LocalToGlobal = 0, Min = -1.0f, Max = 2.0f,
        });
        b.FlexControllers.Add(new StudioHdrBuilder.FlexControllerDefEntry
        {
            Type = "eyes", Name = "eyeRight", LocalToGlobal = 1,
        });
        // driver 0: bone 1, two controls (TX over [-4,6] -> fc 0, TZ over [0,1] -> fc 1)
        b.FlexDrivers.Add(new StudioHdrBuilder.FlexDriverDef
        {
            BoneIndex = 1,
            Controls =
            {
                new() { BoneComponent = 0, FlexControllerIndex = 0, Min = -4.0f, Max = 6.0f },
                new() { BoneComponent = 2, FlexControllerIndex = 1, Min = 0.0f, Max = 1.0f },
            },
        });
        // driver 1: bone 0, one control (TY over [0.5,0.5] -> fc 0)
        b.FlexDrivers.Add(new StudioHdrBuilder.FlexDriverDef
        {
            BoneIndex = 0,
            Controls = { new() { BoneComponent = 1, FlexControllerIndex = 0, Min = 0.5f, Max = 0.5f } },
        });
        StudioHdr hdr = Build(b);

        // ---- flexcontroller table
        Assert.Equal(2, hdr.NumFlexControllers);
        MstudioFlexController c0 = hdr.FlexController(0);
        Assert.Equal("eyes", c0.Type);       // shared string: dedup'd, both index the same bytes
        Assert.Equal("eyeLeft", c0.Name);
        Assert.Equal(0, c0.LocalToGlobal);
        Assert.Equal(-1.0f, c0.Min);
        Assert.Equal(2.0f, c0.Max);
        MstudioFlexController c1 = hdr.FlexController(1);
        Assert.Equal("eyes", c1.Type);
        Assert.Equal("eyeRight", c1.Name);
        Assert.Equal(1, c1.LocalToGlobal);
        Assert.Equal(0.0f, c1.Min);           // builder default
        Assert.Equal(1.0f, c1.Max);           // builder default

        // ---- driver count comes from studiohdr2 @24
        Assert.Equal(2, hdr.BoneFlexDriverCount);

        MstudioBoneFlexDriver d0 = hdr.BoneFlexDriver(0)!;
        Assert.NotNull(d0);
        Assert.Equal(1, d0.BoneIndex);
        Assert.Equal(2, d0.ControlCount);
        Assert.Equal(0, d0.Control(0).BoneComponent);
        Assert.Equal(0, d0.Control(0).FlexControllerIndex);
        Assert.Equal(-4.0f, d0.Control(0).Min);
        Assert.Equal(6.0f, d0.Control(0).Max);
        Assert.Equal(2, d0.Control(1).BoneComponent);
        Assert.Equal(1, d0.Control(1).FlexControllerIndex);
        Assert.Equal(0.0f, d0.Control(1).Min);
        Assert.Equal(1.0f, d0.Control(1).Max);

        MstudioBoneFlexDriver d1 = hdr.BoneFlexDriver(1)!;
        Assert.NotNull(d1);
        Assert.Equal(0, d1.BoneIndex);
        Assert.Equal(1, d1.ControlCount);
        Assert.Equal(1, d1.Control(0).BoneComponent);
        Assert.Equal(0, d1.Control(0).FlexControllerIndex);
        Assert.Equal(0.5f, d1.Control(0).Min);
        Assert.Equal(0.5f, d1.Control(0).Max);

        // The stride proof, hand-derived: driver 1's record begins 24 bytes
        // after driver 0's (typed-pointer +1), NOT after driver 0's inline
        // control payload (which would be 24 + 2*16 = 56). If the builder
        // inlined controls, d1's fields above would read driver 0's control
        // bytes: BoneIndex would be 0 (control[0].m_nBoneComponent == 0
        // collides!) and ControlCount would be 0 (FlexControllerIndex 0) —
        // so pin the raw bytes the reader must see.
        int d0At = d0.RawAddress;
        Assert.Equal(d0At + 24, d1.RawAddress);
        // d0's control table lives past BOTH records: with the uniform
        // 24-byte stride the controls start at table + 48, so d0's
        // record-relative index is exactly 48. An inlined layout (qc never
        // emits one, but a broken builder might) would say 24.
        int ctlIdx = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
            d0.RawImage.AsSpan(d0At + 8, 4));
        Assert.Equal(48, ctlIdx);
    }

    /// <summary>The studiohdr2-absent arm (<c>studiohdr2index == 0</c>):
    /// stock's <c>BoneFlexDriverCount()</c> answers 0 and
    /// <c>BoneFlexDriver(i)</c> is reached only behind that count — the
    /// reader's null arm is the same NULL the inline's Assert would hit.</summary>
    [Fact]
    public void BoneFlexDriver_NoStudioHdr2AnswersZeroAndNull()
    {
        StudioHdrBuilder b = BaseBuilder(); // no linear bones, no drivers
        StudioHdr hdr = Build(b);
        Assert.Equal(0, hdr.BoneFlexDriverCount);
        Assert.Null(hdr.BoneFlexDriver(0));
    }

    /// <summary>The zero-count arm WITH a studiohdr2 record: count @24 is 0
    /// (and @28 stays 0), so the loop in
    /// <c>Studio_RunBoneFlexDrivers</c> never turns.</summary>
    [Fact]
    public void BoneFlexDriver_StudioHdr2WithZeroCount()
    {
        StudioHdrBuilder b = BaseBuilder();
        b.WithStudioHdr2 = true;
        StudioHdr hdr = Build(b);
        Assert.Equal(0, hdr.BoneFlexDriverCount);
        Assert.Null(hdr.BoneFlexDriver(0));
    }

    /// <summary>Studiohdr2 coexistence: drivers alongside linear bones keep
    /// both tables readable at their own indices (count @24 must not collide
    /// with <c>linearboneindex</c> @16).</summary>
    [Fact]
    public void BoneFlexDriver_CoexistsWithLinearBones()
    {
        StudioHdrBuilder b = BaseBuilder();
        b.WithLinearBones = true;
        b.FlexDrivers.Add(new StudioHdrBuilder.FlexDriverDef
        {
            BoneIndex = 1,
            Controls = { new() { BoneComponent = 1, FlexControllerIndex = 0, Min = 2.25f, Max = 3.5f } },
        });
        StudioHdr hdr = Build(b);

        Assert.NotNull(hdr.LinearBones());
        Assert.Equal(2, hdr.LinearBones()!.NumBones);
        Assert.Equal(1, hdr.BoneFlexDriverCount);
        MstudioBoneFlexDriver d = hdr.BoneFlexDriver(0)!;
        Assert.NotNull(d);
        Assert.Equal(1, d.BoneIndex);
        Assert.Equal(1, d.ControlCount);
        Assert.Equal(1, d.Control(0).BoneComponent);
        Assert.Equal(0, d.Control(0).FlexControllerIndex);
        Assert.Equal(2.25f, d.Control(0).Min);
        Assert.Equal(3.5f, d.Control(0).Max);
    }
}
