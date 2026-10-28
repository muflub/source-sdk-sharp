using System;
using SourceSharp.Shared.Bones;
using Xunit;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// The hdr-read cluster (<see cref="BoneSetupHdrRead"/>): pose-parameter
/// resolution, the bilinear four-anim blend, CPS/duration, the attachment
/// scan, and the default pose-parameter seed. Fixtures are built with
/// <see cref="StudioHdrBuilder"/>; expectations are hand-computed from the
/// stock formulas at the cited oracle ranges (1682-1768, 5441-5471,
/// 5532-5549, 5555-5564, 5842-5857, 5070-5091).
/// </summary>
public class BonesHdrReadTests
{
    // ---- fixtures ----------------------------------------------------------

    private static StudioHdr Build(StudioHdrBuilder b) =>
        StudioHdr.TryCreate(b.Build(), null, out StudioHdr? h) !
            ? h!
            : throw new InvalidOperationException("fixture image rejected by TryCreate");

    /// <summary>LocalPoseParameter over a one-sequence model whose seqdesc
    /// uses local pose axis 0 (the linear arm: no posekeys emitted).</summary>
    private static void Local0(StudioHdr hdr, float[] pose, out float setting, out int index) =>
        BoneSetupHdrRead.LocalPoseParameter(hdr, pose, hdr.SeqDesc(0), 0, 0, out setting, out index);

    private static StudioHdrBuilder BaseBuilder()
    {
        var b = new StudioHdrBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0" });

        // Reuse a queued string as the hdr name: Build() patches @12 from
        // StringAddr(ModelName), which only resolves for a string some
        // record also queues (builder gap reported 2026-10-07).
        b.ModelName = "b0";
        return b;
    }

    /// <summary>
    /// <c>CStudioHdr::pszName()</c> is <c>studiohdr_t::name</c>, an inline
    /// <c>char[64]</c> at offset 12. The reader took those bytes for a string
    /// index and threw (or read garbage) for any real name; the ragdoll's
    /// "Bogus constraint on ragdoll %s" was its first caller.
    /// </summary>
    [Fact]
    public void TheHeadersNameIsItsInlineCharArray()
    {
        StudioHdrBuilder b = new() { ModelName = "characters/a_much_longer_model_name.mdl" };
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });

        Assert.True(StudioHdr.TryCreate(b.Build(), null, out StudioHdr? hdr) && hdr is not null);
        Assert.Equal("characters/a_much_longer_model_name.mdl", hdr!.Name);
    }

    // ---- the builder proves itself (port contract's MUTATION RULE) --------

    /// <summary>
    /// The fixture helper is real: a hand-built two-bone model reads back
    /// through <see cref="StudioHdr"/> as the same numbers, and a
    /// deliberately corrupted blob is REFUSED by <c>TryCreate</c> — never
    /// half-read. If the builder emitted records at the wrong offsets, the
    /// read-back arm goes red; if the reader stopped validating, the
    /// corruption arms go red.
    /// </summary>
    [Fact]
    public void Builder_RoundTripsKnownValues_AndRefusesCorruption()
    {
        StudioHdrBuilder b = new() { ModelName = "b0" }; // queued as a bone name (see BaseBuilder)
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0, Pos = new Vector(10f, 0f, 0f) });
        byte[] image = b.Build();

        Assert.True(StudioHdr.TryCreate(image, null, out StudioHdr? hdr) && hdr is not null,
            "known-good 2-bone image rejected");
        Assert.Equal(2, hdr!.NumBones);
        Assert.Equal("b0", hdr.Bone(0).Name);
        Assert.Equal(-1, hdr.Bone(0).Parent);
        Assert.Equal("b1", hdr.Bone(1).Name);
        Assert.Equal(0, hdr.Bone(1).Parent);
        Vector b1Pos = hdr.Bone(1).Position;
        Assert.Equal(10f, b1Pos.X);
        Assert.Equal(0f, b1Pos.Y);
        Assert.Equal(0f, b1Pos.Z);

        // corrupted id: never a half-read hdr
        byte[] bad = (byte[])image.Clone();
        bad[0] ^= 0x40;
        Assert.False(StudioHdr.TryCreate(bad, null, out StudioHdr? half));
        Assert.Null(half);

        // out-of-range version
        bad = (byte[])image.Clone();
        bad[4] = 99; // version byte 0; version = 99 > STUDIO_HEADER_VERSION
        Assert.False(StudioHdr.TryCreate(bad, null, out half));
        Assert.Null(half);

        // table counts beyond the image (TablesFit arm): numbones @156
        bad = (byte[])image.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(156, 4), 1 << 20);
        Assert.False(StudioHdr.TryCreate(bad, null, out half));
        Assert.Null(half);
    }

    /// <summary>studio.h:2263-2267: the localnodes block is the transition
    /// matrix (numlocalnodes² BYTES at localnodeindex @252) plus a separate
    /// name table (numlocalnodes ints at localnodenameindex @256). A model
    /// whose 3x3 transition matrix is the last 9 bytes of the image is well
    /// formed; a 16-byte record stride at @252 would ask for 48.</summary>
    [Fact]
    public void TablesFit_LocalNodes_TransitionMatrixIsNumSquaredBytes()
    {
        StudioHdrBuilder b = BaseBuilder();
        b.NodeNames.AddRange(["n0", "n1", "n2"]);
        byte[] built = b.Build();
        byte[] image = new byte[built.Length + 9];
        built.CopyTo(image, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(76, 4), image.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(252, 4), built.Length);

        Assert.True(StudioHdr.TryCreate(image, null, out StudioHdr? hdr) && hdr is not null,
            "tail-placed transition matrix rejected");
        Assert.Equal(3, hdr!.NumLocalNodes);
        Assert.Equal("n2", hdr.NodeName(2));
    }

    /// <summary>The name table is what <see cref="StudioHdr.NodeName"/>
    /// reads, so it is the one validated: a localnodenameindex @256 leaving
    /// room for one of three ints is refused (the untouched image loads, so
    /// the refusal is the patch's doing). A long bone name pads the string
    /// tail so even the old 16-byte stride at @252 fit this image: the old
    /// check accepted the patched one and NodeName read past the end.</summary>
    [Fact]
    public void TablesFit_LocalNodes_NameTableMustFit()
    {
        StudioHdrBuilder b = BaseBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = new string('x', 96), Parent = 0 });
        b.NodeNames.AddRange(["n0", "n1", "n2"]);
        byte[] image = b.Build();
        Assert.True(StudioHdr.TryCreate(image, null, out _));

        byte[] bad = (byte[])image.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(256, 4), bad.Length - 4);
        Assert.False(StudioHdr.TryCreate(bad, null, out StudioHdr? half));
        Assert.Null(half);
    }

    /// <summary>CStudioHdr::EntryNode and ExitNode: a sequence's localentrynode
    /// @112 and localexitnode @116, nodes counted from 1, 0 for none.</summary>
    [Fact]
    public void EntryNode_and_ExitNode_are_the_sequences_nodes()
    {
        StudioHdrBuilder b = BaseBuilder();
        b.NodeNames.AddRange(["closed", "open"]);
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "idle", AnimTable = [0] });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "opening", AnimTable = [0], LocalEntryNode = 1, LocalExitNode = 2 });
        StudioHdr hdr = Build(b);

        Assert.Equal((0, 0), (hdr.EntryNode(0), hdr.ExitNode(0)));
        Assert.Equal((1, 2), (hdr.EntryNode(1), hdr.ExitNode(1)));
    }

    /// <summary>CStudioHdr::GetTransition: the byte at
    /// (from - 1) * numlocalnodes + (to - 1) of the table at @252.</summary>
    [Fact]
    public void GetTransition_reads_the_table_by_from_and_to()
    {
        StudioHdrBuilder b = BaseBuilder();
        b.NodeNames.AddRange(["n1", "n2", "n3"]);
        b.Transitions[(1, 3)] = 2;
        b.Transitions[(3, 1)] = 2;
        b.Transitions[(2, 3)] = 3;
        StudioHdr hdr = Build(b);

        Assert.Equal([2, 2, 3, 0], new[] { hdr.GetTransition(1, 3), hdr.GetTransition(3, 1), hdr.GetTransition(2, 3), hdr.GetTransition(1, 2) });
    }

    // ---- LocalPoseParameter (oracle 1682-1768) ------------------------------

    [Fact]
    public void LocalPoseParameter_null_hdr_answers_zero_zero()
    {
        var b = BaseBuilder();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "p0" });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", AnimTable = new short[] { 0 } });
        StudioHdr hdr = Build(b);

        BoneSetupHdrRead.LocalPoseParameter(null!, new float[1], hdr.SeqDesc(0), 0, 0,
            out float setting, out int index);
        Assert.Equal(0f, setting); // stock's !pStudioHdr arm, oracle 1684-1689
        Assert.Equal(0, index);
    }

    [Fact]
    public void LocalPoseParameter_unmapped_local_pose_answers_zero_zero()
    {
        // paramindex[0] = -1 -> GetSharedPoseParameter answers -1: stock's
        // iPose == -1 arm (oracle 1691-1696) fires whatever the pose value says.
        var b = BaseBuilder();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "p0", Start = -10f, End = 10f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "s",
            ParamIndex0 = -1,
            ParamIndex1 = -1,
            AnimTable = new short[] { 0 },
        });
        StudioHdr hdr = Build(b);

        Local0(hdr, new[] { 0.9f }, out float setting, out int index);
        Assert.Equal(0f, setting);
        Assert.Equal(0, index);
    }

    [Fact]
    public void LocalPoseParameter_linear_arm_maps_the_param_window_and_clamps()
    {
        // poseparam 0..10; seq window paramstart 2 / paramend 7, which the
        // port normalises to flLocalStart 0.2 / flLocalEnd 0.7 (oracle
        // 1712-1713). pose 0.5 -> (0.5-0.2)/0.5 = 0.6.
        var b = BaseBuilder();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "p0", Start = 0f, End = 10f });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "a0", NumFrames = 2, Fps = 30f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "s",
            ParamIndex0 = 0,
            ParamStart = new float[] { 2f, 0f },
            ParamEnd = new float[] { 7f, 1f },
            AnimTable = new short[] { 0 },
        });
        StudioHdr hdr = Build(b);

        Local0(hdr, new[] { 0.5f }, out float setting, out int index);
        Assert.Equal(0.6f, setting, 6);
        Assert.Equal(0, index);

        // Below the window: (0 - 0.2)/0.5 = -0.4 -> clamp to 0 (oracle 1719).
        Local0(hdr, new[] { 0.0f }, out setting, out index);
        Assert.Equal(0f, setting);
        Assert.Equal(0, index);

        // Above the window: (0.95 - 0.2)/0.5 = 1.5 -> clamp to 1 (oracle 1721).
        Local0(hdr, new[] { 0.95f }, out setting, out _);
        Assert.Equal(1f, setting);
    }

    [Fact]
    public void LocalPoseParameter_groupsize_above_two_estimates_index_and_fraction()
    {
        // Identity window, groupsize 4: index = (int)(t*3), fraction = t*3 -
        // index (oracle 1724-1733). t = 0.5 -> index 1, fraction 0.5.
        var b = BaseBuilder();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "p0", Start = 0f, End = 1f });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "a0", NumFrames = 2, Fps = 30f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "s",
            ParamIndex0 = 0,
            GroupSize0 = 4,
            ParamStart = new float[] { 0f, 0f },
            ParamEnd = new float[] { 1f, 1f },
            AnimTable = new short[] { 0, 0, 0, 0 },
        });
        StudioHdr hdr = Build(b);

        Local0(hdr, new[] { 0.5f }, out float setting, out int index);
        Assert.Equal(0.5f, setting);
        Assert.Equal(1, index);

        // t = 1: index would be 3 == groupsize-1 -> pulled back to 2 with the
        // fraction reaching the next key (stock's last-cell arm).
        Local0(hdr, new[] { 1.0f }, out setting, out index);
        Assert.Equal(2, index);
        Assert.Equal(1f, setting);

        // t = 0.1 -> index 0, fraction 0.3: proves the fraction is per-cell.
        Local0(hdr, new[] { 0.1f }, out setting, out index);
        Assert.Equal(0, index);
        Assert.Equal(0.3f, setting, 6);
    }

    [Fact]
    public void LocalPoseParameter_loop_wraps_by_the_poseparam_period_including_negatives()
    {
        // poseparam [-2, 2] loop 4: stock (oracle 1703-1708) computes
        // wrap = 0 + 2 = 2, shift = 4 - 2 = 2, then
        // flValue -= 4 * floor((flValue + 2) / 4) -> the [-2, 2) window.
        // The seq window paramstart -1 / paramend 1 normalises to 0.25..0.75,
        // so a pose wrapped to 0.5 answers (0.5-0.25)/0.5 = 0.5 exactly.
        var b = BaseBuilder();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef
        {
            Name = "turn",
            Start = -2f,
            End = 2f,
            Loop = 4f,
        });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "s",
            ParamIndex0 = 0,
            ParamStart = new float[] { -1f, 0f },
            ParamEnd = new float[] { 1f, 1f },
            AnimTable = new short[] { 0 },
        });
        StudioHdr hdr = Build(b);

        // 4.5 wraps down by one period to 0.5
        Local0(hdr, new[] { 4.5f }, out float setting, out _);
        Assert.Equal(0.5f, setting);

        // -3.5 wraps UP by one period to 0.5: the negative arm of floor.
        Local0(hdr, new[] { -3.5f }, out setting, out _);
        Assert.Equal(0.5f, setting);
    }

    [Fact]
    public void LocalPoseParameter_loop_zero_leaves_the_pose_unwrapped()
    {
        // Same model without the loop: 4.5 is far above the window and
        // clamps to 1 — the wrap above is the only thing that could land it
        // at 0.5. In-range values pass through untouched either way.
        var b = BaseBuilder();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef
        {
            Name = "turn",
            Start = -2f,
            End = 2f,
            Loop = 0f,
        });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "s",
            ParamIndex0 = 0,
            ParamStart = new float[] { -1f, 0f },
            ParamEnd = new float[] { 1f, 1f },
            AnimTable = new short[] { 0 },
        });
        StudioHdr hdr = Build(b);

        Local0(hdr, new[] { 4.5f }, out float setting, out _);
        Assert.Equal(1f, setting); // no wrap: (4.5-0.25)/0.5 clamps

        Local0(hdr, new[] { 0.5f }, out setting, out _);
        Assert.Equal(0.5f, setting); // in range: identical answer with or without loop
    }

    [Fact]
    public void LocalPoseParameter_posekey_arm_walks_segments_and_clamps()
    {
        // Posekey arm (posekeyindex != 0, oracle 1734-1767): axis-0 keys
        // [0, 2.5, 5, 10] over groupsize 4; the poseparam is 0..1 so
        // flValue = pose * 1 + 0 = pose. p = 0.5 sits in segment 0; p = 3 in
        // segment 1; p = 9.5 in the last reachable segment (groupsize-2 = 2);
        // a pose below the first key clamps to 0.
        var b = BaseBuilder();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "p", Start = 0f, End = 1f });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "a0", NumFrames = 2, Fps = 30f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "s",
            ParamIndex0 = 0,
            GroupSize0 = 4,
            GroupSize1 = 3, // the posekey table is groupsize0 * groupsize1 floats
            AnimTable = new short[] { 0, 0, 0, 0 },
            // The table is the FLAT float array studio.h:881 walks (iParam *
            // groupsize[0] + iAnim): axis 0 is the first 4 entries.
            PoseKeys =
            {
                0f, 2.5f, 5f, 10f, 0f, 1f, 0f, 1f, 0f, 0f, 0f, 0f,
            },
        });
        StudioHdr hdr = Build(b);

        Local0(hdr, new[] { 0.5f }, out float setting, out int index);
        Assert.Equal(0.2f, setting, 6); // (0.5-0)/(2.5-0)
        Assert.Equal(0, index);

        Local0(hdr, new[] { 3f }, out setting, out index);
        Assert.Equal(0.2f, setting, 6); // (3-2.5)/(5-2.5) after the walk
        Assert.Equal(1, index);

        Local0(hdr, new[] { 9.5f }, out setting, out index);
        Assert.Equal(0.9f, setting, 6); // (9.5-5)/(10-5)
        Assert.Equal(2, index);

        // Below the first key: the post-walk clamp to 0 (oracle 1762).
        Local0(hdr, new[] { -2f }, out setting, out _);
        Assert.Equal(0f, setting);
    }

    // ---- SeqAnims (oracle 5441-5471) ---------------------------------------

    /// <summary>2x2 blend, identity windows, so the weights are exactly the
    /// bilinear weights of (pose0, pose1).</summary>
    private static StudioHdr Hdr2x2()
    {
        var b = BaseBuilder();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "x", Start = 0f, End = 1f });
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "y", Start = 0f, End = 1f });
        for (int i = 0; i < 4; i++)
        {
            b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "a" + i, NumFrames = 2, Fps = 30f });
        }

        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "run",
            ParamIndex0 = 0,
            ParamIndex1 = 1,
            GroupSize0 = 2,
            GroupSize1 = 2,
            ParamStart = new float[] { 0f, 0f },
            ParamEnd = new float[] { 1f, 1f },
            AnimTable = new short[] { 0, 1, 2, 3 },
        });
        return Build(b);
    }

    [Fact]
    public void SeqAnims_2x2_blend_returns_bilinear_weights_and_cell_anims()
    {
        StudioHdr hdr = Hdr2x2();
        BoneSetupHdrRead.SeqAnims(hdr, hdr.SeqDesc(0), 0, new[] { 0.25f, 0.5f },
            out MstudioAnimDesc[] panim, out float[] weight);

        Assert.Equal(0.75 * 0.5, weight[0], 6); // (1-s0)(1-s1)
        Assert.Equal(0.25 * 0.5, weight[1], 6); // s0(1-s1)
        Assert.Equal(0.75 * 0.5, weight[2], 6); // (1-s0)s1
        Assert.Equal(0.25 * 0.5, weight[3], 6); // s0 s1

        // anim(x, y) table [0,1,2,3] maps the cells to anims 0..3
        Assert.Equal("a0", panim[0]!.Name);
        Assert.Equal("a1", panim[1]!.Name);
        Assert.Equal("a2", panim[2]!.Name);
        Assert.Equal("a3", panim[3]!.Name);
    }

    [Fact]
    public void SeqAnims_out_of_range_sequence_zeroes_all_four_weights()
    {
        StudioHdr hdr = Hdr2x2();
        BoneSetupHdrRead.SeqAnims(hdr, hdr.SeqDesc(0), hdr.NumSequences, new[] { 0.25f, 0.5f },
            out MstudioAnimDesc[] panim, out float[] weight);

        Assert.Equal(new[] { 0f, 0f, 0f, 0f }, weight);
        foreach (MstudioAnimDesc? a in panim)
        {
            Assert.Null(a);
        }
    }

    [Fact]
    public void SeqAnims_null_hdr_zeroes_all_four_weights()
    {
        StudioHdr hdr = Hdr2x2();
        BoneSetupHdrRead.SeqAnims(null!, hdr.SeqDesc(0), 0, new[] { 0.25f, 0.5f },
            out MstudioAnimDesc[] panim, out float[] weight);
        Assert.Equal(new[] { 0f, 0f, 0f, 0f }, weight);
    }

    [Fact]
    public void SeqAnims_maps_each_cell_through_the_relative_anim_lookup()
    {
        // Plain (non-folded) hdr: IRelativeAnim is identity, so each entry
        // is the blend table's own animdesc. A table that does NOT run
        // 0,1,2,3 proves the panim[i] = AnimDesc(IRelativeAnim(...)) chain.
        var b = BaseBuilder();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "x", Start = 0f, End = 1f });
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "y", Start = 0f, End = 1f });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "low", NumFrames = 2, Fps = 30f });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "high", NumFrames = 2, Fps = 30f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "blend",
            ParamIndex0 = 0,
            ParamIndex1 = 1,
            GroupSize0 = 2,
            GroupSize1 = 2,
            ParamStart = new float[] { 0f, 0f },
            ParamEnd = new float[] { 1f, 1f },
            AnimTable = new short[] { 1, 0, 1, 0 }, // (0,0)->high (1,0)->low (0,1)->high (1,1)->low
        });
        StudioHdr hdr = Build(b);

        BoneSetupHdrRead.SeqAnims(hdr, hdr.SeqDesc(0), 0, new[] { 0f, 0f },
            out MstudioAnimDesc[] panim, out float[] weight);
        Assert.Equal("high", panim[0]!.Name);
        Assert.Equal("low", panim[1]!.Name);
        Assert.Equal("high", panim[2]!.Name);
        Assert.Equal("low", panim[3]!.Name);
        Assert.Equal(1f, weight[0], 6); // s0 = s1 = 0 puts all weight on cell 0
    }

    // ---- CPS / Duration (oracle 5532-5564) ----------------------------------

    [Fact]
    public void Cps_excludes_weighted_anims_with_one_frame()
    {
        // s0 = s1 = 0 leaves weight[0] = 1 on the numframes-1 anim: the
        // numframes > 1 gate (oracle 5541) zeroes the sum despite its fps.
        var b = BaseBuilder();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "x", Start = 0f, End = 1f });
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "y", Start = 0f, End = 1f });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "single", NumFrames = 1, Fps = 1000f });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "fast", NumFrames = 24, Fps = 24f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "s",
            ParamIndex0 = 0,
            ParamIndex1 = 1,
            GroupSize0 = 2,
            GroupSize1 = 2,
            ParamStart = new float[] { 0f, 0f },
            ParamEnd = new float[] { 1f, 1f },
            AnimTable = new short[] { 0, 1, 1, 1 },
        });
        StudioHdr hdr = Build(b);

        // weight[0] = 1 on the single-frame anim: excluded -> 0
        Assert.Equal(0f, BoneSetupHdrRead.Cps(hdr, hdr.SeqDesc(0), 0, new[] { 0f, 0f }));

        // s0 = 1 moves ALL weight to cell 1 ("fast"): 24/(24-1)
        Assert.Equal(24f / 23f, BoneSetupHdrRead.Cps(hdr, hdr.SeqDesc(0), 0, new[] { 1f, 0f }), 6);
    }

    [Fact]
    public void Cps_sums_weighted_cells_by_the_bilinear_weights()
    {
        StudioHdr hdr = Hdr2x2(); // four anims, fps 30, numframes 2
        // every cell answers 30/(2-1) = 30; the bilinear weights sum to 1.
        Assert.Equal(30f, BoneSetupHdrRead.Cps(hdr, hdr.SeqDesc(0), 0, new[] { 0.25f, 0.75f }), 5);
    }

    [Fact]
    public void Duration_is_inverse_cps()
    {
        var b = BaseBuilder();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "x", Start = 0f, End = 1f });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "walk", NumFrames = 31, Fps = 30f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "walk",
            ParamIndex0 = 0,
            GroupSize0 = 1,
            ParamStart = new float[] { 0f, 0f },
            ParamEnd = new float[] { 1f, 1f },
            AnimTable = new short[] { 0 },
        });
        StudioHdr hdr = Build(b);

        // cps = 30/(31-1) = 1 -> duration 1.0
        Assert.Equal(1.0f, BoneSetupHdrRead.Duration(hdr, 0, new[] { 0f }), 6);
    }

    [Fact]
    public void Duration_is_zero_when_cps_is_zero()
    {
        var b = BaseBuilder();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "x", Start = 0f, End = 1f });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "single", NumFrames = 1, Fps = 40f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "hold",
            ParamIndex0 = 0,
            GroupSize0 = 1,
            ParamStart = new float[] { 0f, 0f },
            ParamEnd = new float[] { 1f, 1f },
            AnimTable = new short[] { 0 },
        });
        StudioHdr hdr = Build(b);

        // The only weighted cell is numframes 1: cps 0 -> 0, not infinity
        // (stock's `if (cps == 0) return 0` arm, oracle 5560-5563).
        Assert.Equal(0f, BoneSetupHdrRead.Duration(hdr, 0, new[] { 0f }));
    }

    // ---- FindAttachment (oracle 5842-5857) ----------------------------------

    private static StudioHdr HdrAttachments()
    {
        var b = BaseBuilder();
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "muzzle", LocalBone = 0 });
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "Chest", LocalBone = 0 });
        return Build(b);
    }

    [Fact]
    public void FindAttachment_matches_case_insensitively()
    {
        StudioHdr hdr = HdrAttachments();
        Assert.Equal(0, BoneSetupHdrRead.FindAttachment(hdr, "MUZZLE"));
        Assert.Equal(1, BoneSetupHdrRead.FindAttachment(hdr, "chest"));
        Assert.Equal(1, BoneSetupHdrRead.FindAttachment(hdr, "CHEST"));
    }

    [Fact]
    public void FindAttachment_miss_returns_minus_one()
    {
        StudioHdr hdr = HdrAttachments();
        Assert.Equal(-1, BoneSetupHdrRead.FindAttachment(hdr, "back"));
        Assert.Equal(-1, BoneSetupHdrRead.FindAttachment(hdr, "muzzlez")); // one char off
    }

    [Fact]
    public void FindAttachment_empty_name_hits_only_an_empty_attachment()
    {
        // Stock's ss::StrICmp loop matches "" only against an attachment
        // whose pszName() is also "". None here: every lookup misses.
        StudioHdr hdr = HdrAttachments();
        Assert.Equal(-1, BoneSetupHdrRead.FindAttachment(hdr, string.Empty));

        var b = BaseBuilder();
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "root", LocalBone = 0 });
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "", LocalBone = 0 });
        StudioHdr withEmpty = Build(b);
        Assert.Equal(1, BoneSetupHdrRead.FindAttachment(withEmpty, ""));
    }

    [Fact]
    public void FindAttachment_only_ascii_letters_fold_case()
    {
        // ss::StrICmp folds A-Z by | 0x20 and nothing else; OrdinalIgnoreCase
        // would also fold the non-ASCII letters here. Sibling control:
        // EffectDispatchTableTests.OnlyAsciiLettersFoldCase.
        var b = BaseBuilder();
        // U+00E9 precomposed on both sides — a decomposed e+accent would
        // mismatch on normalization, not on the fold.
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "éclat", LocalBone = 0 });
        b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = "Tracer", LocalBone = 0 });
        StudioHdr hdr = Build(b);

        Assert.Equal(-1, BoneSetupHdrRead.FindAttachment(hdr, "ÉCLAT"));
        Assert.Equal(0, BoneSetupHdrRead.FindAttachment(hdr, "éCLAT"));
        Assert.Equal(1, BoneSetupHdrRead.FindAttachment(hdr, "tRaCeR"));
    }

    [Fact]
    public void FindAttachment_null_hdr_returns_minus_one()
    {
        Assert.Equal(-1, BoneSetupHdrRead.FindAttachment(null!, "muzzle"));
    }

    // ---- CalcDefaultPoseParameters (oracle 5070-5091) -----------------------

    [Fact]
    public void CalcDefaultPoseParameters_prefers_zero_state_then_half()
    {
        var b = BaseBuilder();
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "straddle", Start = -10f, End = 30f }); // -(-10)/40 = 0.25
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "lean", Start = 0f, End = 2f });        // no zero state -> 0.5
        StudioHdr hdr = Build(b);

        var pose = new float[] { 9f, 9f, 9f };
        BoneSetupHdrRead.CalcDefaultPoseParameters(hdr, pose, 2); // nCount bounds the fill

        Assert.Equal(0.25f, pose[0], 6); // zero state inside [-10, 30]
        Assert.Equal(0.5f, pose[1]);     // start !< 0 -> the half default
        Assert.Equal(9f, pose[2]);       // past nCount: untouched
    }

    [Fact]
    public void CalcDefaultPoseParameters_fills_past_pose_count_with_half_then_stops_at_maxstudioposeparam()
    {
        var b = BaseBuilder();
        for (int i = 0; i < 30; i++)
        {
            b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "p" + i, Start = -1f, End = 1f });
        }
        StudioHdr hdr = Build(b);

        var pose = new float[30];
        Array.Fill(pose, 9f);
        BoneSetupHdrRead.CalcDefaultPoseParameters(hdr, pose, 30);

        Assert.Equal(0.5f, pose[0], 6); // -(-1)/2 = 0.5
        Assert.Equal(0.5f, pose[23], 6); // slot 23 is the last the 24 bound writes
        Assert.Equal(9f, pose[24]);      // stock's MIN(nCount, MAXSTUDIOPOSEPARAM) bound

        // The 0.5 default also covers indices past the model's own count:
        var few = BaseBuilder();
        few.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "only", Start = -1f, End = 1f });
        StudioHdr fewHdr = Build(few);
        var fewPose = new float[] { 9f, 9f };
        BoneSetupHdrRead.CalcDefaultPoseParameters(fewHdr, fewPose, 2);
        Assert.Equal(0.5f, fewPose[0], 6); // the one param's zero state
        Assert.Equal(0.5f, fewPose[1]);    // i >= nPoseCount -> still the 0.5 default
    }

    // ---- Studio_GetKeyValueText (ours 6095-6107) ----------------------------

    private static StudioHdr BuildQuery(StudioHdrBuilder b) =>
        StudioHdr.TryCreate(b.BuildWithQuery(), null, out StudioHdr? h) !
            ? h!
            : throw new InvalidOperationException("fixture image rejected by TryCreate");

    private static void AddTwoSequences(StudioHdrBuilder b)
    {
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s0", AnimTable = new short[] { 0 } });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s1", AnimTable = new short[] { 0 } });
    }

    /// <summary>Two sequences, two blobs: each sequence reads its OWN
    /// keyvalue text (seqdesc @172/@176, record-relative) — and the empty
    /// string reads back as "", distinct from the size-0 NULL arm (qc
    /// stores size = strlen + 1, so "" is size 1).</summary>
    [Fact]
    public void GetKeyValueText_PerSequenceBlob_RoundTrips()
    {
        StudioHdrBuilder b = BaseBuilder();
        AddTwoSequences(b);
        b.SeqKeyValueText.Add("model { act idle }");
        b.SeqKeyValueText.Add("");
        StudioHdr hdr = BuildQuery(b);

        Assert.Equal("model { act idle }", BoneSetupHdrRead.GetKeyValueText(hdr, 0));
        Assert.Equal(string.Empty, BoneSetupHdrRead.GetKeyValueText(hdr, 1));
    }

    /// <summary>The absent arms: sequence -1, sequence == NumSequences, and
    /// a sequence whose pair was never patched (size 0) are all null;
    /// a null hdr is stock's own guarded arm, not an exception.</summary>
    [Fact]
    public void GetKeyValueText_OutOfRange_And_NullHdr_AreNull()
    {
        StudioHdrBuilder b = BaseBuilder();
        AddTwoSequences(b);
        b.SeqKeyValueText.Add("present");
        b.SeqKeyValueText.Add(null); // patched list hole: seq 1 stays size 0
        StudioHdr hdr = BuildQuery(b);

        Assert.Equal("present", BoneSetupHdrRead.GetKeyValueText(hdr, 0));
        Assert.Null(BoneSetupHdrRead.GetKeyValueText(hdr, 1));   // size-0 NULL arm
        Assert.Null(BoneSetupHdrRead.GetKeyValueText(hdr, -1));
        Assert.Null(BoneSetupHdrRead.GetKeyValueText(hdr, 2));   // == NumSequences
        Assert.Null(BoneSetupHdrRead.GetKeyValueText(null, 0));
    }

    /// <summary>The hdr-level blob (@312/@316) is a DIFFERENT table: a model
    /// carrying both answers each reader from its own bytes, proving the
    /// port does not confuse the seqdesc inline (studio.h:891) with the
    /// studiohdr_t one (studio.h:2310).</summary>
    [Fact]
    public void GetKeyValueText_HdrLevelBlobIsNotTheSeqBlob()
    {
        StudioHdrBuilder b = BaseBuilder();
        AddTwoSequences(b);
        b.KeyValueText = "hdr level";
        b.SeqKeyValueText.Add("seq level");
        b.SeqKeyValueText.Add(null);
        StudioHdr hdr = BuildQuery(b);

        Assert.Equal("hdr level", hdr.KeyValueText());
        Assert.Equal("seq level", BoneSetupHdrRead.GetKeyValueText(hdr, 0));
    }

    // ---- Studio_RunBoneFlexDrivers (ours 6139-6211) -------------------------

    private static void AddEyeDrivers(StudioHdrBuilder b)
    {
        b.FlexControllers.Add(new StudioHdrBuilder.FlexControllerDefEntry
        {
            Type = "eyes", Name = "eyeLeft", LocalToGlobal = 0, Min = 0f, Max = 1f,
        });
        b.FlexControllers.Add(new StudioHdrBuilder.FlexControllerDefEntry
        {
            Type = "eyes", Name = "eyeRight", LocalToGlobal = 1, Min = 0f, Max = 1f,
        });
        b.FlexControllers.Add(new StudioHdrBuilder.FlexControllerDefEntry
        {
            Type = "eyes", Name = "hidden", LocalToGlobal = -1, Min = 0f, Max = 1f,
        });
    }

    /// <summary>Non-merge arm: the component comes from
    /// <c>pvPositions[bone]</c>, remapped over the control's [min,max] into
    /// the controller's <c>localToGlobal</c> slot. Hand-derived: TX 4 over
    /// [-4,6] is (4+4)/10 = 0.8; TZ 3 over [0,6] is 0.5; TZ 12 clamps to 1;
    /// TX -9 clamps to 0; and a control on the hidden controller
    /// (localToGlobal -1) must NOT write slot 0's neighbour — slot 0 keeps
    /// its 0.8 while a control that also targets slot 2 stays unwritten.</summary>
    [Fact]
    public void RunBoneFlexDrivers_NonMerge_RemapsPositionComponents()
    {
        StudioHdrBuilder b = BaseBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0 });
        AddEyeDrivers(b);
        b.FlexDrivers.Add(new StudioHdrBuilder.FlexDriverDef
        {
            BoneIndex = 1,
            Controls =
            {
                new() { BoneComponent = 0, FlexControllerIndex = 0, Min = -4f, Max = 6f },   // TX -> slot 0
                new() { BoneComponent = 2, FlexControllerIndex = 1, Min = 0f, Max = 6f },    // TZ -> slot 1
                new() { BoneComponent = 0, FlexControllerIndex = 2, Min = 0f, Max = 1f },    // hidden: no write
            },
        });
        StudioHdr hdr = BuildQuery(b);

        var positions = new[] { Vector.Zero, new Vector(4f, 0f, 3f) };
        var boneToWorld = new[] { BoneMatrix3x4.Identity, BoneMatrix3x4.Identity };
        var weights = new float[2];
        BoneSetupHdrRead.RunBoneFlexDrivers(weights, hdr, positions, boneToWorld, BoneMatrix3x4.Identity);

        Assert.Equal(0.8f, weights[0], 6);
        Assert.Equal(0.5f, weights[1], 6);

        // clamp arms: same driver, positions past both ends
        positions[1] = new Vector(-9f, 0f, 12f);
        weights = new float[2];
        BoneSetupHdrRead.RunBoneFlexDrivers(weights, hdr, positions, boneToWorld, BoneMatrix3x4.Identity);
        Assert.Equal(0f, weights[0]); // ( -9 + 4 ) / 10 = -0.5 -> clamp 0
        Assert.Equal(1f, weights[1]); // 12 / 6 = 2 -> clamp 1
    }

    /// <summary>Merge arm (parent >= 0): the bone's local matrix is
    /// <c>MatrixInvert(boneToWorld[parent]) * boneToWorld[bone]</c>, and the
    /// POSITION ARRAY IS NOT CONSULTED. Hand-derived: parent at translation
    /// (1,1,1), bone at (4,2,3): local translation (3,1,2); TX over [0,10]
    /// is 0.3, TZ over [0,4] is 0.5. The positions array carries poison
    /// values: any result other than the matrix-derived one (or a throw)
    /// means the wrong arm ran.</summary>
    [Fact]
    public void RunBoneFlexDrivers_MergeChild_UsesInvertedParentMatrix()
    {
        const int merge = StudioConstants.BONE_USED_BY_BONE_MERGE;
        StudioHdrBuilder b = BaseBuilder();
        b.Bones[0].Flags = merge; // root b0 also merged: drives the lazy root arm below
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0, Flags = merge });
        AddEyeDrivers(b);
        b.FlexDrivers.Add(new StudioHdrBuilder.FlexDriverDef
        {
            BoneIndex = 1,
            Controls =
            {
                new() { BoneComponent = 0, FlexControllerIndex = 0, Min = 0f, Max = 10f }, // TX -> slot 0
                new() { BoneComponent = 2, FlexControllerIndex = 1, Min = 0f, Max = 4f },  // TZ -> slot 1
            },
        });
        StudioHdr hdr = BuildQuery(b);

        var positions = new[] { new Vector(1e6f, 1e6f, 1e6f), new Vector(1e6f, 1e6f, 1e6f) };
        var boneToWorld = new[]
        {
            BoneMatrix3x4.Identity with { Row0 = new System.Numerics.Vector4(1, 0, 0, 1), Row1 = new System.Numerics.Vector4(0, 1, 0, 1), Row2 = new System.Numerics.Vector4(0, 0, 1, 1) },
            BoneMatrix3x4.Identity with { Row0 = new System.Numerics.Vector4(1, 0, 0, 4), Row1 = new System.Numerics.Vector4(0, 1, 0, 2), Row2 = new System.Numerics.Vector4(0, 0, 1, 3) },
        };
        var weights = new float[2];
        BoneSetupHdrRead.RunBoneFlexDrivers(weights, hdr, positions, boneToWorld, BoneMatrix3x4.Identity);

        Assert.Equal(0.3f, weights[0], 6); // (4-1)/10
        Assert.Equal(0.5f, weights[1], 6); // (3-1)/4
    }

    /// <summary>Merge arm (parent &lt; 0): the ROOT uses the lazily inverted
    /// <c>mRootToWorld</c> — once per call even with two root-merged driver
    /// bones. Hand-derived: rootToWorld translation (10,0,0) inverts to
    /// (-10,0,0); boneToWorld[b1] translation (14,2,3) gives local (4,2,3),
    /// and boneToWorld[b2] translation (10,1,1) gives (0,1,1). TX over
    /// [0,10] -> 0.4 / 0; TZ over [0,4] -> 0.75 / 0.25.</summary>
    [Fact]
    public void RunBoneFlexDrivers_MergeRoot_UsesInvertedRootToWorld()
    {
        const int merge = StudioConstants.BONE_USED_BY_BONE_MERGE;
        StudioHdrBuilder b = BaseBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = -1, Flags = merge });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b2", Parent = -1, Flags = merge });
        AddEyeDrivers(b);
        b.FlexDrivers.Add(new StudioHdrBuilder.FlexDriverDef
        {
            BoneIndex = 1,
            Controls =
            {
                new() { BoneComponent = 0, FlexControllerIndex = 0, Min = 0f, Max = 10f },
                new() { BoneComponent = 2, FlexControllerIndex = 1, Min = 0f, Max = 4f },
            },
        });
        b.FlexDrivers.Add(new StudioHdrBuilder.FlexDriverDef
        {
            BoneIndex = 2,
            Controls =
            {
                new() { BoneComponent = 0, FlexControllerIndex = 0, Min = 0f, Max = 10f },
                new() { BoneComponent = 2, FlexControllerIndex = 1, Min = 0f, Max = 4f },
            },
        });
        StudioHdr hdr = BuildQuery(b);

        var positions = new[] { new Vector(-7f, -7f, -7f), new Vector(-7f, -7f, -7f), new Vector(-7f, -7f, -7f) };
        static BoneMatrix3x4 At(float x, float y, float z) =>
            BoneMatrix3x4.Identity with
            {
                Row0 = new System.Numerics.Vector4(1, 0, 0, x),
                Row1 = new System.Numerics.Vector4(0, 1, 0, y),
                Row2 = new System.Numerics.Vector4(0, 0, 1, z),
            };
        var boneToWorld = new[] { At(0, 0, 0), At(14, 2, 3), At(10, 1, 1) };
        var weights = new float[2];
        BoneSetupHdrRead.RunBoneFlexDrivers(weights, hdr, positions, boneToWorld, At(10, 0, 0));

        // driver for bone 2 ran LAST: slot values are the bone-2 results.
        Assert.Equal(0f, weights[0], 6);   // (10-10)/10
        Assert.Equal(0.25f, weights[1], 6); // (1-0)/4
    }

    /// <summary>Stock's <c>RemapValClamped</c> degenerate arm: min == max
    /// answers <c>val &gt;= B ? 1 : 0</c>, NOT a division (and not
    /// <c>Math.Clamp</c>'s NaN behaviour). TX 0.6 over [0.5,0.5] is 1;
    /// TX 0.4 is 0.</summary>
    [Fact]
    public void RunBoneFlexDrivers_DegenerateRange_IsStockStepNotDivide()
    {
        StudioHdrBuilder b = BaseBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0 });
        AddEyeDrivers(b);
        b.FlexDrivers.Add(new StudioHdrBuilder.FlexDriverDef
        {
            BoneIndex = 1,
            Controls = { new() { BoneComponent = 0, FlexControllerIndex = 0, Min = 0.5f, Max = 0.5f } },
        });
        StudioHdr hdr = BuildQuery(b);

        var weights = new float[2];
        BoneSetupHdrRead.RunBoneFlexDrivers(
            weights, hdr, new[] { Vector.Zero, new Vector(0.6f, 0f, 0f) },
            new[] { BoneMatrix3x4.Identity, BoneMatrix3x4.Identity }, BoneMatrix3x4.Identity);
        Assert.Equal(1f, weights[0]);

        BoneSetupHdrRead.RunBoneFlexDrivers(
            weights, hdr, new[] { Vector.Zero, new Vector(0.4f, 0f, 0f) },
            new[] { BoneMatrix3x4.Identity, BoneMatrix3x4.Identity }, BoneMatrix3x4.Identity);
        Assert.Equal(0f, weights[0]);
    }

    /// <summary>Malformed-data arms (NR-02, stock UB with assert-only
    /// guards): a NULL driver record (count > 0 with a zero table index) is
    /// SKIPPED — weights untouched — and a bone component outside {0,1,2}
    /// throws instead of reading the neighbour matrix/position.</summary>
    [Fact]
    public void RunBoneFlexDrivers_Malformed_SkipsNullDriver_ThrowsBadComponent()
    {
        // ---- null driver: hand-patch the studiohdr2 table index to 0
        StudioHdrBuilder b = BaseBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0 });
        AddEyeDrivers(b);
        b.FlexDrivers.Add(new StudioHdrBuilder.FlexDriverDef
        {
            BoneIndex = 1,
            Controls = { new() { BoneComponent = 0, FlexControllerIndex = 0, Min = 0f, Max = 10f } },
        });
        byte[] image = b.BuildWithQuery();
        int hdr2 = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(400, 4));
        Assert.True(hdr2 != 0 && System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(hdr2 + 24, 4)) == 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(hdr2 + 28, 4), 0); // table index -> 0
        Assert.True(StudioHdr.TryCreate(image, null, out StudioHdr? nullDrvHdr) && nullDrvHdr is not null);

        var weights = new float[2];
        BoneSetupHdrRead.RunBoneFlexDrivers(
            weights, nullDrvHdr!, new[] { Vector.Zero, new Vector(5f, 0f, 0f) },
            new[] { BoneMatrix3x4.Identity, BoneMatrix3x4.Identity }, BoneMatrix3x4.Identity);
        Assert.Equal(0f, weights[0]); // skipped, not 0.5
        Assert.Equal(1, nullDrvHdr!.BoneFlexDriverCount);

        // ---- bad component: patch control 0's m_nBoneComponent to 5
        StudioHdrBuilder c = BaseBuilder();
        c.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0 });
        AddEyeDrivers(c);
        c.FlexDrivers.Add(new StudioHdrBuilder.FlexDriverDef
        {
            BoneIndex = 1,
            Controls = { new() { BoneComponent = 0, FlexControllerIndex = 0, Min = 0f, Max = 10f } },
        });
        byte[] image2 = c.BuildWithQuery();
        Assert.True(StudioHdr.TryCreate(image2, null, out StudioHdr? badHdr) && badHdr is not null);
        int ctlAt = badHdr!.BoneFlexDriver(0)!.Control(0).RawAddress;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(image2.AsSpan(ctlAt, 4), 5);
        Assert.True(StudioHdr.TryCreate(image2, null, out StudioHdr? badHdr2) && badHdr2 is not null);

        Assert.Throws<InvalidOperationException>(() => BoneSetupHdrRead.RunBoneFlexDrivers(
            new float[2], badHdr2!, new[] { Vector.Zero, new Vector(5f, 0f, 0f) },
            new[] { BoneMatrix3x4.Identity, BoneMatrix3x4.Identity }, BoneMatrix3x4.Identity));
    }

    /// <summary>No drivers at all (no studiohdr2): the walk never turns and
    /// the weights array is untouched — stock's count-0 early-out.</summary>
    [Fact]
    public void RunBoneFlexDrivers_NoDrivers_LeavesWeightsUntouched()
    {
        StudioHdrBuilder b = BaseBuilder();
        StudioHdr hdr = BuildQuery(b);
        Assert.Equal(0, hdr.BoneFlexDriverCount);
        var weights = new float[2];
        BoneSetupHdrRead.RunBoneFlexDrivers(weights, hdr, [], [], BoneMatrix3x4.Identity);
        Assert.Equal(0f, weights[0]);
        Assert.Equal(0f, weights[1]);
    }

    // ---- Studio_GetDefaultSurfaceProps / Studio_GetMass (ours 6077-6089) --

    /// <summary>The tail-appended <c>$surfaceprop</c> blob at @308 reads
    /// back verbatim through the reader's image-absolute C-string walk —
    /// the multi-char decode is the point; the index-0 empty arm is the
    /// reader's own standing NR-05 convention, tested in StudioHdr tests.</summary>
    [Fact]
    public void GetDefaultSurfaceProps_ReadsBlobAtSurfacePropIndex()
    {
        StudioHdrBuilder b = BaseBuilder();
        b.SurfacePropText = "flesh";
        StudioHdr hdr = BuildQuery(b);

        Assert.Equal("flesh", BoneSetupHdrRead.GetDefaultSurfaceProps(hdr));
    }

    /// <summary>One line over the studiohdr float at @328: the value the
    /// builder patched is the value physics_shared sees (distinctive
    /// constant so a wrong field offset cannot alias).</summary>
    [Fact]
    public void GetMass_ReadsStudioHdrFloatAt328()
    {
        StudioHdrBuilder b = BaseBuilder();
        b.Mass = 85.5f;
        StudioHdr hdr = Build(b);

        Assert.Equal(85.5f, BoneSetupHdrRead.GetMass(hdr));
    }

    // ---- Studio_PrefetchSequence (ours 6109-6133) --------------------------

    private static void AddBlendPair(StudioHdrBuilder b, int animBlock0, int animBlock1)
    {
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "a0", NumFrames = 2, Fps = 30f, AnimBlock = animBlock0 });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "a1", NumFrames = 2, Fps = 30f, AnimBlock = animBlock1 });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "blend",
            GroupSize0 = 2,
            GroupSize1 = 2,
            NumBlends = 4,
            AnimTable = new short[] { 0, 1, 0, 1 },
        });
    }

    /// <summary>Two blend cells over two inline anims (block 0): every
    /// cell's pAnim resolves, so the sequence is fully resident. The
    /// negative control below flips one anim's block to -1.</summary>
    [Fact]
    public void PrefetchSequence_AllCellsInline_Resident()
    {
        StudioHdrBuilder b = BaseBuilder();
        AddBlendPair(b, 0, 0);
        StudioHdr hdr = Build(b);

        Assert.True(BoneSetupHdrRead.PrefetchSequence(hdr, 0));
    }

    /// <summary>An anim whose block is -1 ("model must be recompiled")
    /// makes every cell that references it pending — cells 1 and 3 of the
    /// 2x2 table. Same fixture as the resident control with one field
    /// flipped: before != after. Zeroing groupsize[0] then makes the walk
    /// never turn, so even that anim counts as resident (stock's loop is
    /// driven by the group sizes, not the anim table).</summary>
    [Fact]
    public void PrefetchSequence_MissingBlock_Pending_And_ZeroGroup_Resident()
    {
        StudioHdrBuilder b = BaseBuilder();
        AddBlendPair(b, 0, -1);
        StudioHdr hdr = Build(b);

        Assert.False(BoneSetupHdrRead.PrefetchSequence(hdr, 0));

        StudioHdrBuilder z = BaseBuilder();
        AddBlendPair(z, 0, -1);
        z.Sequences[0].GroupSize0 = 0;
        StudioHdr zeroHdr = Build(z);
        Assert.True(BoneSetupHdrRead.PrefetchSequence(zeroHdr, 0));
    }
}
