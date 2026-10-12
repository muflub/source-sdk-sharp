//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// The managed mdl reader the bone port reads its data through: what a
// CStudioHdr / studiohdr_t pointer pair gives the native bone_setup, replaced
// by record views over one image plus the virtual-model fold.
//
// Every record offset was emitted by a sizeof/offsetof dump compiled with
// bone_setup.cpp's own flags (docs/bones-offsets.txt), not read off the
// declarations: the 64-bit layout, with serializedstudioptr_t fields widened
// and the studiohdr2 indirection. The engine-owned pointer fields
// (virtualModel, animblockModel, physics, keyvalues at their studiohdr2 slots)
// are never dereferenced here.
//
// The fold mirrors virtualmodel_t::AppendModels (studio_virtualmodel.cpp):
//   - group 0 is the base model itself; each include (in qc order) is one
//     group entry, and an included model's own includes append after it;
//   - sequences and anims merge by label/name under stock's stricmp, an
//     ASCII-only case fold (BoneSetupQuery.AsciiEquals): the FIRST
//     entry seen wins, except a sequence whose stored flags still carry
//     STUDIO_OVERRIDE is replaced outright;
//   - each group keeps masterSeq/masterAnim/masterBone/masterPose/
//     masterNode/masterAttachment remaps (group-local -> global) and a
//     boneMap (global -> group-local);
//   - an attachment whose masterBone maps to -1 is skipped; otherwise the
//     whole parent chain of the BASE bone is OR'd with
//     BONE_USED_BY_ATTACHMENT. Stock writes that into the shared cached
//     hdr; we keep the overlay here and BoneFlags() is the only flags read.
//
// Validation: TryCreate checks the id ('IDST'), the version (the engine
// accepts up to STUDIO_VERSION 48, studio.h:70), and that every table the
// reader can hand out fits inside the image. A header whose bones or
// sequences run off the end is rejected rather than half-read -- stock
// trusts the mdl loader, and the loader is not here.
//
//=============================================================================//

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// One element of a compressed animation stream: stock's
/// <c>mstudioanimvalue_t</c> union -- <c>num.valid</c> is the first stored
/// byte, <c>num.total</c> the second (stock compares it as a signed
/// <c>char</c>), and <c>value</c> is the whole short read as SIGNED. The
/// scale (1/255 for the fixed-point streams) is applied by the caller,
/// exactly like <c>ExtractAnimValue</c>'s <c>scale</c> parameter.
/// </summary>
public readonly struct MstudioAnimValue
{
    private readonly short _value;

    public MstudioAnimValue(short value) => _value = value;

    /// <summary><c>num.valid</c> (unsigned byte 0).</summary>
    public int Valid => _value & 0xFF;

    /// <summary><c>num.total</c> (unsigned byte 1; stock's <c>byte total</c>
    /// promotes to <c>int</c> in its comparisons with the frame counter).</summary>
    public int Total => (_value >> 8) & 0xFF;

    /// <summary><c>value * scale</c>.</summary>
    public float Value(float scale) => _value * scale;
}

/// <summary>
/// Base of every record view: the image and the byte address the record's
/// <c>this</c> would have held. Accessors decode on demand -- the hot loops
/// are the performance pass's problem (repo rule: move first, measure after).
/// </summary>
public abstract class StudioRecord
{
    protected readonly byte[] Image;
    protected readonly int Base;

    protected StudioRecord(byte[] image, int @base)
    {
        Image = image;
        Base = @base;
    }

    protected int I32(int off) => BinaryPrimitives.ReadInt32LittleEndian(Image.AsSpan(Base + off, 4));
    protected uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(Image.AsSpan(Base + off, 4));
    protected float F32(int off) => BinaryPrimitives.ReadSingleLittleEndian(Image.AsSpan(Base + off, 4));
    protected ushort U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(Image.AsSpan(Base + off, 2));
    protected short S16(int off) => BinaryPrimitives.ReadInt16LittleEndian(Image.AsSpan(Base + off, 2));
    protected byte U8(int off) => Image[Base + off];

    protected Vector Vec3(int off) => new(F32(off), F32(off + 4), F32(off + 8));

    protected Quaternion Quat(int off) => new(F32(off), F32(off + 4), F32(off + 8), F32(off + 12));

    // matrix3x4_t is three rows of four floats: 48 bytes, not 12. A 12-byte
    // span would make MemoryMarshal.Read throw ("destination is too short").
    protected BoneMatrix3x4 Matrix3x4(int off) =>
        System.Runtime.InteropServices.MemoryMarshal.Read<BoneMatrix3x4>(
            Image.AsSpan(Base + off, 48));
    /// <summary>Stock's <c>(char *)this + index</c>; empty when index 0.</summary>
    protected string CString(int index)
    {
        if (index == 0)
        {
            return string.Empty;
        }

        int start = Base + index;
        int end = start;
        while (Image[end] != 0)
        {
            end++;
        }

        return Encoding.UTF8.GetString(Image, start, end - start);
    }

    internal byte[] RawImage => Image;
    internal int RawAddress => Base;
}

/// <summary>
/// One compressed animation stream: stock's <c>panimvalue</c> — a byte
/// address into the image, with <c>mstudioanimvalue_t</c> records read on
/// demand. A null-image stream is stock's <c>NULL</c> panelvalue;
/// <c>BoneSetupExtract.ExtractAnimValue</c> answers (0, 0) for it. The
/// accessors mirror the union: <c>num.valid</c> is the low byte,
/// <c>num.total</c> the high byte, <c>value</c> the whole short as signed.
/// </summary>
public readonly struct AnimValueStream
{
    /// <summary>The owning image; null is stock's NULL pointer.</summary>
    public readonly byte[]? Image;

    /// <summary>Byte address of record 0 (<c>panimvalue</c>).</summary>
    public readonly int At;

    public AnimValueStream(byte[]? image, int at)
    {
        Image = image;
        At = at;
    }

    /// <summary>Stock's NULL panelvalue.</summary>
    public static AnimValueStream Null => default;

    public bool IsNull => Image is null;

    /// <summary>Stock derefs the image unguarded; a read past its end is
    /// malformed data. Zeros past the end drive stock's own
    /// <c>total == 0</c> terminator arm (the Assert(0) return), so the
    /// port never throws on a bad stream.</summary>
    private byte Byte(int at) =>
        (uint)at < (uint)Image!.Length ? Image[at] : (byte)0;

    /// <summary><c>panimvalue->num.valid</c> (unsigned low byte).</summary>
    public int Valid(int i) => Byte(At + 2 * i);

    /// <summary><c>panimvalue->num.total</c> (high byte; stock's
    /// <c>byte total</c>, promoted to int against the frame counter).</summary>
    public int Total(int i) => Byte(At + 2 * i + 1);

    /// <summary><c>panimvalue[i].value * scale</c> (signed short).</summary>
    public float Value(int i, float scale) =>
        (short)(Byte(At + 2 * i) | (Byte(At + 2 * i + 1) << 8)) * scale;
}

/// <summary><c>mstudiobone_t</c> (216 bytes at the pinned layout).</summary>
public sealed class MstudioBone : StudioRecord
{
    internal MstudioBone(byte[] image, int @base) : base(image, @base) { }

    public string Name => CString(I32(0));
    public int Parent => I32(4);
    public Vector Position => Vec3(32);
    public Quaternion Quaternion => Quat(44);
    public Vector Rotation => Vec3(60);
    public Vector PositionScale => Vec3(72);
    public Vector RotationScale => Vec3(84);
    public BoneMatrix3x4 PoseToBone => Matrix3x4(96);
    public Quaternion QuaternionAlignment => Quat(144);
    public int Flags => I32(160);

    /// <summary><c>proctype</c> (@164): which STUDIO_PROC_* writes this bone, 0 for none.</summary>
    public int ProcType => I32(164);

    /// <summary><c>procindex</c> (@168): the procedural record's offset from
    /// this bone record, 0 when the bone carries none.</summary>
    public int ProcIndex => I32(168);

    public int SurfacePropIndex => I32(176);

    /// <summary><c>pszSurfaceProp()</c>: <c>(char *)this + surfacepropidx</c>,
    /// the name the hitbox trace hands the surface-props table.</summary>
    public string SurfacePropName => CString(SurfacePropIndex);

    public int PhysicsBone => I32(172);
    public int Contents => I32(180);
}

/// <summary><c>mstudiolinearbone_t</c> (64 bytes): the SoA twin.</summary>
public sealed class MstudioLinearBone : StudioRecord
{
    internal MstudioLinearBone(byte[] image, int @base) : base(image, @base) { }

    public int NumBones => I32(0);
    private int FlagsIndex => I32(4);
    private int ParentIndex => I32(8);
    private int PosIndex => I32(12);
    private int QuatIndex => I32(16);
    private int RotIndex => I32(20);
    private int PoseToBoneIndex => I32(24);
    private int PosScaleIndex => I32(28);
    private int RotScaleIndex => I32(32);
    private int QAlignmentIndex => I32(36);

    private int Abs(int table, int stride, int i) => Base + I32(table) + stride * i;

    public int Flags(int i) =>
        BinaryPrimitives.ReadInt32LittleEndian(Image.AsSpan(Abs(4, 4, i), 4));
    public int Parent(int i) =>
        BinaryPrimitives.ReadInt32LittleEndian(Image.AsSpan(Abs(8, 4, i), 4));
    public Vector Position(int i) => ReadVec(Abs(12, 12, i));
    public Quaternion Quaternion(int i) => ReadQuat(Abs(16, 16, i));
    public Vector Rotation(int i) => ReadVec(Abs(20, 12, i));
    public BoneMatrix3x4 PoseToBone(int i) =>
        BoneMatrix3x4.FromElements(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(Image.AsSpan(Abs(24, 48, i), 48)));
    public Vector PositionScale(int i) => ReadVec(Abs(28, 12, i));
    public Vector RotationScale(int i) => ReadVec(Abs(32, 12, i));
    public Quaternion QuaternionAlignment(int i) => ReadQuat(Abs(36, 16, i));

    private Vector ReadVec(int at) => new(ReadF(at), ReadF(at + 4), ReadF(at + 8));
    private Quaternion ReadQuat(int at) => new(ReadF(at), ReadF(at + 4), ReadF(at + 8), ReadF(at + 12));
    private float ReadF(int at) => BinaryPrimitives.ReadSingleLittleEndian(Image.AsSpan(at, 4));
}

/// <summary>
/// <c>mstudioanim_t</c> (bone byte, flags byte, nextoffset short) and its
/// <c>pData</c>. Section offsets are relative to <c>(byte *)this + 4</c>,
/// exactly as stock's <c>pRotV()</c>/<c>pPosV()</c>/<c>pQuat48()</c> cast it:
/// <c>pRotV = pData</c>, <c>pPosV = pData + (ANIMROT ? 1 : 0)</c> in
/// valueptr units, <c>pPos = pData + (RAWROT ? 12 : 0) + (RAWROT2 ? 8 : 0)</c>.
/// </summary>
public sealed class MstudioAnim
{
    private readonly byte[] _image;

    internal MstudioAnim(byte[] image, int @base)
    {
        _image = image;
        Base = @base;
    }

    internal int Base { get; }
    public int Flags => _image[Base + 1];

    /// <summary><c>pData()</c>: the address the section offsets and raw payloads sit at.</summary>
    private int PData => Base + 4;

    /// <summary><c>bone</c> (byte 0): which base-model bone this record animates.</summary>
    public int Bone => _image[Base];

    /// <summary><c>pNext()</c>: the next record, or null at the terminator.</summary>
    public MstudioAnim? Next()
    {
        short off = BinaryPrimitives.ReadInt16LittleEndian(_image.AsSpan(Base + 2, 2));
        return off != 0 ? new MstudioAnim(_image, Base + off) : null;
    }

    /// <summary>The valueptr table for rotation: <c>pRotV()</c>.</summary>
    private int RotTable => PData;

    /// <summary>The valueptr table for position: <c>pPosV()</c> — studio.h:678
    /// advances one 6-byte valueptr record past it when ANIMROT is set.</summary>
    private int PosTable => PData + ((Flags & StudioConstants.STUDIO_ANIM_ANIMROT) != 0 ? 6 : 0);

    /// <summary><c>pRotV()->pAnimvalue(i)</c> (studio.h:657): the stream at
    /// <c>table + offset[i]</c>; the null stream when the offset is 0.</summary>
    public AnimValueStream RotStream(int i) => StreamAt(RotTable, i);

    /// <summary><c>pPosV()->pAnimvalue(i)</c>.</summary>
    public AnimValueStream PosStream(int i) => StreamAt(PosTable, i);

    private AnimValueStream StreamAt(int table, int i)
    {
        short off = BinaryPrimitives.ReadInt16LittleEndian(_image.AsSpan(table + 2 * i, 2));
        return off > 0 ? new AnimValueStream(_image, table + off) : AnimValueStream.Null;
    }

    /// <summary>Decoded raw rotation (RAWROT -> Quaternion48, RAWROT2 -> Quaternion64).</summary>
    public Quaternion? RawQuaternion
    {
        get
        {
            if ((Flags & StudioConstants.STUDIO_ANIM_RAWROT) != 0)
            {
                return StudioCodec.ReadQuaternion48(_image, PData);
            }

            if ((Flags & StudioConstants.STUDIO_ANIM_RAWROT2) != 0)
            {
                return StudioCodec.ReadQuaternion64(_image, PData);
            }

            return null;
        }
    }

    /// <summary>Decoded raw position (RAWPOS -> Vector48 after the raw rot arms).</summary>
    public Vector? RawPosition
    {
        get
        {
            if ((Flags & StudioConstants.STUDIO_ANIM_RAWPOS) == 0)
            {
                return null;
            }

            int at = PData
                + ((Flags & StudioConstants.STUDIO_ANIM_RAWROT) != 0 ? 6 : 0) // sizeof(Quaternion48) = 6
                + ((Flags & StudioConstants.STUDIO_ANIM_RAWROT2) != 0 ? 8 : 0);
            return StudioCodec.ReadVector48(_image, at);
        }
    }
}

/// <summary><c>mstudioanimdesc_t</c> (100 bytes).</summary>
public sealed class MstudioAnimDesc : StudioRecord
{
    internal MstudioAnimDesc(byte[] image, int @base) : base(image, @base) { }

    public string Name => CString(I32(4));
    public float Fps => F32(8);
    public int Flags => I32(12);
    public int NumFrames => I32(16);
    public int NumMovements => I32(20);
    private int MovementIndex => I32(24);
    public int AnimBlock => I32(52);
    public int AnimIndex => I32(56);
    public int NumIKRules => I32(60);
    private int IkRuleIndex => I32(64);
    public int AnimBlockIkRuleIndex => I32(68);
    public int NumLocalHierarchy => I32(72);
    private int LocalHierarchyIndex => I32(76);
    private int SectionIndex => I32(80);
    public int SectionFrames => I32(84);
    public short ZeroFrameSpan => S16(88);
    public short ZeroFrameCount => S16(90);
    internal int ZeroFrameIndex => I32(92);

    /// <summary><c>pSection(i)</c>: (animblock, animindex).</summary>
    internal (int Block, int Index) Section(int i)
    {
        int at = Base + SectionIndex + 8 * i;
        return (
            BinaryPrimitives.ReadInt32LittleEndian(Image.AsSpan(at, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(Image.AsSpan(at + 4, 4)));
    }

    /// <summary><c>pIKRule(i)</c>: the anim's own IK rules; null table = null.</summary>
    public MstudioIkRule? PIKRule(int i) =>
        IkRuleIndex == 0 ? null : new MstudioIkRule(Image, Base + IkRuleIndex + 152 * i);

    public MstudioLocalHierarchy? PHierarchy(int i) =>
        LocalHierarchyIndex == 0 ? null : new MstudioLocalHierarchy(Image, Base + LocalHierarchyIndex + 48 * i);

    /// <summary><c>pZeroFrameData()</c>: the byte address of the row table;
    /// -1 when zeroframeindex 0 (stock's NULL pData).</summary>
    internal int PZeroFrameData() => ZeroFrameIndex == 0 ? -1 : Base + ZeroFrameIndex;

    public MstudioMovement? PMovement(int i) =>
        MovementIndex == 0 ? null : new MstudioMovement(Image, Base + MovementIndex + 44 * i);
}

/// <summary><c>mstudioseqdesc_t</c> (212 bytes).</summary>
public sealed class MstudioSeqDesc : StudioRecord
{
    internal MstudioSeqDesc(byte[] image, int @base) : base(image, @base) { }

    public string Label => CString(I32(4));
    public int Flags => I32(12);
    public int Activity => I32(16);
    public int ActWeight => I32(20);
    public int NumBlends => I32(56);
    private int AnimIndexIndex => I32(60);
    private int MovementIndex => I32(64);
    public int GroupSize0 => I32(68);
    public int GroupSize1 => I32(72);
    public int ParamIndex0 => I32(76);
    public int ParamIndex1 => I32(80);
    public float ParamStart(int i) => F32(84 + 4 * i);
    public float ParamEnd(int i) => F32(92 + 4 * i);
    // mstudioseqdesc_t.paramparent is a scalar int (@100), not an array.
    public int ParamParent => I32(100);
    public float FadeInTime => F32(104);
    public float FadeOutTime => F32(108);
    public int LocalEntryNode => I32(112);
    public int LocalExitNode => I32(116);
    public int NodeFlags => I32(120);
    public float EntryPhase => F32(124);
    public float ExitPhase => F32(128);
    public float LastFrame => F32(132);
    public int NextSeq => I32(136);
    public int Pose => I32(140);
    public int NumIKRules => I32(144);
    public int NumAutoLayers => I32(148);
    private int AutoLayerIndex => I32(152);
    private int WeightListIndex => I32(156);
    public int PoseKeyIndex => I32(160);
    public int NumIKLocks => I32(164);
    private int IkLockIndex => I32(168);

    /// <summary><c>mstudioseqdesc_t.cycleposeindex</c> (studio.h:893): the
    /// pose-parameter index used as the cycle index when
    /// <c>STUDIO_CYCLEPOSE</c> is set. Raw @180: after iklockindex @168 the
    /// record continues keyvalueindex @172 / keyvaluesize @176, then
    /// cycleposeindex @180.</summary>
    public int CyclePoseIndex => I32(180);

    /// <summary><c>mstudioseqdesc_t.keyvalueindex</c> (@172) /
    /// <c>keyvaluesize</c> (@176): the PER-SEQUENCE keyvalue blob, the
    /// seqdesc fields behind <c>mstudioseqdesc_t::KeyValueText()</c>
    /// (studio.h:889-891). These are NOT the hdr-level pair @312/@316 that
    /// <see cref="StudioHdr.KeyValueText()"/> reads — a model has one
    /// blob-wide keyvalue table at the header and one per sequence.</summary>
    private int KeyValueIndex => I32(172);
    private int KeyValueSize => I32(176);

    /// <summary><c>mstudioseqdesc_t::KeyValueText()</c> (studio.h:891):
    /// <c>keyvaluesize != 0 ? ((char *)this) + keyvalueindex : NULL</c>.
    /// Record-relative like the sibling inlines; NUL-bounded inside
    /// <c>keyvaluesize</c> (qc stores <c>size = strlen + 1</c>). The absent
    /// arm returns null, not empty — what
    /// <c>Studio_GetKeyValueText</c> hands its caller.</summary>
    public string? KeyValueText()
    {
        int size = KeyValueSize;
        if (size == 0)
        {
            return null;
        }

        int start = Base + KeyValueIndex;
        int end = start;
        int limit = start + size;
        while (end < limit && Image[end] != 0)
        {
            end++;
        }

        return Encoding.UTF8.GetString(Image, start, end - start);
    }


    /// <summary>
    /// <c>anim(x, y)</c>: the blend table's (short) animdesc index, clamped to
    /// the group size, exactly as the inline in studio.h.
    /// </summary>
    public int Anim(int x, int y)
    {
        if (x >= GroupSize0)
        {
            x = GroupSize0 - 1;
        }

        if (y >= GroupSize1)
        {
            y = GroupSize1 - 1;
        }

        int offset = y * GroupSize0 + x;
        return BinaryPrimitives.ReadInt16LittleEndian(
            Image.AsSpan(Base + AnimIndexIndex + 2 * offset, 2));
    }

    /// <summary><c>weight(i)</c>: the per-bone blend weight.</summary>
    public float Weight(int i) => F32(WeightListIndex + 4 * i);

    /// <summary><c>pPoseKey(iParam, iAnim)</c> (studio.h:881): the scalar at
    /// flat index <c>iParam * groupsize[0] + iAnim</c> of the posekey table —
    /// a FLAT float array, not a 12-float record (there is no
    /// <c>mstudioposekey_t</c> in studio.h). Stock reads past its end on
    /// malformed data without a thought; so does this.</summary>
    public float PoseKey(int iParam, int iAnim) =>
        F32(PoseKeyIndex + 4 * (iParam * GroupSize0 + iAnim));

    /// <summary><c>pAutoLayer(i)</c> (studio.h inline): autolayerindex is
    /// record-relative and the table walks a 24-byte stride.</summary>
    public MstudioAutoLayer? PAutoLayer(int i) =>
        AutoLayerIndex == 0 ? null : new MstudioAutoLayer(Image, Base + AutoLayerIndex + 24 * i);

    public MstudioIkLock? PIKLock(int i) =>
        IkLockIndex == 0 ? null : new MstudioIkLock(Image, Base + IkLockIndex + 32 * i);

    public MstudioMovement? PMovement(int i) =>
        MovementIndex == 0 ? null : new MstudioMovement(Image, Base + MovementIndex + 44 * i);
}

/// <summary><c>mstudioikrule_t</c> (152 bytes).</summary>
public sealed class MstudioIkRule : StudioRecord
{
    internal MstudioIkRule(byte[] image, int @base) : base(image, @base) { }

    public int Index => I32(0);
    public int Type => I32(4);
    public int Chain => I32(8);
    public int Bone => I32(12);
    public int Slot => I32(16);
    public float Height => F32(20);
    public float Radius => F32(24);
    public float Floor => F32(28);
    public Vector Position => Vec3(32);
    public Quaternion Quaternion => Quat(44);
    private int CompressedIkErrorIndex => I32(60);
    public int IStart => I32(68);
    private int IkErrorIndex => I32(72);
    public float Start => F32(76);
    public float Peak => F32(80);
    public float Tail => F32(84);
    public float End => F32(88);
    public float Contact => F32(96);
    public float Drop => F32(100);
    public float Top => F32(104);
    private int SzAttachmentIndex => I32(120);

    /// <summary><c>szattachmentname</c>: the attachment lookup name for
    /// <c>IK_ATTACHMENT</c> rules; empty when szattachmentindex is 0.</summary>
    public string AttachmentName => CString(SzAttachmentIndex);

    /// <summary><c>pError(iFrame)</c> (studio.h inline): the table base plus
    /// <c>iFrame - iStart</c> — the subtraction belongs to the accessor, as
    /// in stock; callers pass the absolute frame <c>Studio_IKRuleWeight</c>
    /// derived.</summary>
    public MstudioIkError? PError(int iFrame) =>
        IkErrorIndex == 0 ? null : new MstudioIkError(Image, Base + IkErrorIndex + 28 * (iFrame - IStart));

    /// <summary><c>pCompressedError()</c> (studio.h inline): the compressed
    /// error at <c>(byte *)this + compressedikerrorindex</c>; null when the
    /// index is 0.</summary>
    public MstudioCompressedIkError? PCompressedError() =>
        CompressedIkErrorIndex == 0 ? null : new MstudioCompressedIkError(Image, Base + CompressedIkErrorIndex);
}

/// <summary><c>mstudioiklock_t</c> (32 bytes).</summary>
public sealed class MstudioIkLock : StudioRecord
{
    internal MstudioIkLock(byte[] image, int @base) : base(image, @base) { }

    public int Chain => I32(0);
    public float FlPosWeight => F32(4);
    public float FlLocalQWeight => F32(8);
    public int Flags => I32(12);
}

/// <summary><c>mstudiolocalhierarchy_t</c> (48 bytes).</summary>
public sealed class MstudioLocalHierarchy : StudioRecord
{
    internal MstudioLocalHierarchy(byte[] image, int @base) : base(image, @base) { }

    public int IBone => I32(0);
    public int INewParent => I32(4);
    public float Start => F32(8);
    public float Peak => F32(12);
    public float Tail => F32(16);
    public float End => F32(20);
    public int IStart => I32(24);
    private int LocalAnimIndex => I32(28);

    /// <summary><c>pLocalAnim()</c>: the compressed error at
    /// <c>(byte *)this + localanimindex</c>; null when the index is 0.</summary>
    public MstudioCompressedIkError? PLocalAnim() =>
        LocalAnimIndex == 0 ? null : new MstudioCompressedIkError(Image, Base + LocalAnimIndex);
}

/// <summary><c>mstudioautolayer_t</c> (24 bytes).</summary>
public sealed class MstudioAutoLayer : StudioRecord
{
    internal MstudioAutoLayer(byte[] image, int @base) : base(image, @base) { }

    public ushort ISequence => U16(0);
    public ushort IPose => U16(2);
    // mstudioautolayer_t.flags is an int at @4 (dump: sizeof 24, flags @4);
    // the two shorts are only iSequence/iPose.
    public int Flags => I32(4);
    public float Start => F32(8);
    public float Peak => F32(12);
    public float Tail => F32(16);
    public float End => F32(20);
}

/// <summary><c>mstudioposeparamdesc_t</c> (20 bytes).</summary>
public sealed class MstudioPoseParamDesc : StudioRecord
{
    internal MstudioPoseParamDesc(byte[] image, int @base) : base(image, @base) { }

    public string Name => CString(I32(0));
    public int Flags => I32(4);
    public float Start => MutableStartSet ? MutableStart : F32(8);
    public float End => MutableStartSet ? MutableEnd : F32(12);
    public float Loop => F32(16);

    /// <summary>The fold widens duplicate params' start/end (AppendPoseParameters
    /// mutates the winner); the reader keeps the widened values here.</summary>
    internal float MutableStart;
    internal float MutableEnd;
    internal bool MutableStartSet;
}

/// <summary><c>mstudioattachment_t</c> (92 bytes).</summary>
public sealed class MstudioAttachment : StudioRecord
{
    internal MstudioAttachment(byte[] image, int @base) : base(image, @base) { }

    public string Name => CString(I32(0));
    public uint Flags => U32(4);
    public int LocalBone => I32(8);
    public BoneMatrix3x4 Local => Matrix3x4(12);
}

/// <summary><c>mstudiobbox_t</c> (68 bytes).</summary>
public sealed class MstudioBBox : StudioRecord
{
    internal MstudioBBox(byte[] image, int @base) : base(image, @base) { }

    public int Bone => I32(0);
    public int Group => I32(4);
    public Vector BbMin => Vec3(8);
    public Vector BbMax => Vec3(20);
    public string Name => CString(I32(32));
}

/// <summary><c>mstudiohitboxset_t</c> (12 bytes).</summary>
public sealed class MstudioHitboxSet : StudioRecord
{
    internal MstudioHitboxSet(byte[] image, int @base) : base(image, @base) { }

    public string Name => CString(I32(0));
    public int NumHitboxes => I32(4);
    private int HitboxIndex => I32(8);

    public MstudioBBox Hitbox(int i) => new(Image, Base + HitboxIndex + 68 * i);
}

/// <summary><c>mstudioikchain_t</c> (16 bytes).</summary>
public sealed class MstudioIkChain : StudioRecord
{
    internal MstudioIkChain(byte[] image, int @base) : base(image, @base) { }

    public string Name => CString(I32(0));
    public int LinkType => I32(4);
    public int NumLinks => I32(8);
    private int LinkIndex => I32(12);

    // The link views, made once each (see StudioHdr's record views); a chain
    // view carries its knee stamp for life, so its links can too.
    private MstudioIkLink?[]? _linkViews;

    public MstudioIkLink Link(int i)
    {
        bool held = (uint)i < (uint)NumLinks;

        if (held && _linkViews?[i] is { } view)
        {
            return view;
        }

        MstudioIkLink link = new(Image, Base + LinkIndex + 28 * i);
        // The views are fresh per call, so the fold's copied knee lives on
        // the chain and is stamped onto the link view as it leaves.
        if (i == 0 && MutableKneeDirSet)
        {
            link.MutableKneeDir = MutableKneeDir;
            link.MutableKneeDirSet = true;
        }

        if (held)
        {
            (_linkViews ??= new MstudioIkLink?[NumLinks])[i] = link;
        }

        return link;
    }

    /// <summary>The fold copies a knee direction from an include into a
    /// base chain whose own is zero (AppendIKLocks' knee copy,
    /// studio_virtualmodel.cpp:573-592); stock mutates the base image in
    /// place, the reader keeps the copied value here (stamped by
    /// <see cref="StudioHdr.IkChain"/>).</summary>
    internal Vector MutableKneeDir;
    internal bool MutableKneeDirSet;
}

/// <summary><c>mstudioiklink_t</c> (28 bytes).</summary>
public sealed class MstudioIkLink : StudioRecord
{
    internal MstudioIkLink(byte[] image, int @base) : base(image, @base) { }

    public int Bone => I32(0);
    public Vector KneeDir => MutableKneeDirSet ? MutableKneeDir : Vec3(4);

    internal Vector MutableKneeDir;
    internal bool MutableKneeDirSet;
}

/// <summary><c>mstudiocompressedikerror_t</c> (36 bytes).</summary>
public sealed class MstudioCompressedIkError : StudioRecord
{
    internal MstudioCompressedIkError(byte[] image, int @base) : base(image, @base) { }

    /// <summary><c>scale[6]</c>: the six per-component scales (floats at 0..23).</summary>
    public float Scale(int i) => F32(4 * i);

    /// <summary><c>pAnimvalue(i)</c> (studio.h:549): the stream at
    /// <c>this + offset[i]</c>; the null stream when the offset is 0.
    /// <c>short offset[6]</c> sits after the scales at byte 24.</summary>
    public AnimValueStream AnimValueStream(int i)
    {
        short off = S16(24 + 2 * i);
        return off > 0 ? new AnimValueStream(Image, Base + off) : default;
    }
}

/// <summary><c>mstudioikerror_t</c> (28 bytes).</summary>
public sealed class MstudioIkError : StudioRecord
{
    internal MstudioIkError(byte[] image, int @base) : base(image, @base) { }

    public Vector Position => Vec3(0);
    public Quaternion Quaternion => Quat(12);
}

/// <summary><c>mstudiomovement_t</c> (44 bytes).</summary>
public sealed class MstudioMovement : StudioRecord
{
    internal MstudioMovement(byte[] image, int @base) : base(image, @base) { }

    public int EndFrame => I32(0);
    public int MotionFlags => I32(4);
    public float V0 => F32(8);
    public float V1 => F32(12);
    public float Angle => F32(16);
    public Vector Vector => Vec3(20);
    public Vector Position => Vec3(32);
}

/// <summary><c>mstudiobonecontroller_t</c> (56 bytes) — CalcBoneAdj.</summary>
public sealed class MstudioBoneController : StudioRecord
{
    internal MstudioBoneController(byte[] image, int @base) : base(image, @base) { }

    public int Bone => I32(0);
    public int Type => I32(4);
    public float Start => F32(8);
    public float End => F32(12);
    public int Rest => I32(16);
    public int InputField => I32(20);
}

/// <summary><c>mstudioflexcontroller_t</c> (20 bytes — five words by the
/// declaration at studio.h:928-937): <c>sztypeindex</c> @0,
/// <c>sznameindex</c> @4 (both record-relative, stock's
/// <c>(char *)this + index</c> inlines), <c>localToGlobal</c> @8 (the
/// master-list remap; stock skips controllers where it is negative),
/// <c>min</c> @12, <c>max</c> @16.</summary>
public sealed class MstudioFlexController : StudioRecord
{
    internal MstudioFlexController(byte[] image, int @base) : base(image, @base) { }

    public string Type => CString(I32(0));
    public string Name => CString(I32(4));
    public int LocalToGlobal => I32(8);
    public float Min => F32(12);
    public float Max => F32(16);
}

/// <summary><c>mstudioboneflexdrivercontrol_t</c> (16 bytes — four words by
/// the declaration at studio.h:364-377): <c>m_nBoneComponent</c> @0
/// (StudioBoneFlexComponent_t, studio.h:352-358: TX/TY/TZ 0..2),
/// <c>m_nFlexControllerIndex</c> @4, <c>m_flMin</c> @8, <c>m_flMax</c> @12.
/// <c>Studio_RunBoneFlexDrivers</c> (bone_setup.cpp:6139) feeds these to
/// <c>RemapValClamped</c>.</summary>
public sealed class MstudioBoneFlexDriverControl : StudioRecord
{
    internal MstudioBoneFlexDriverControl(byte[] image, int @base) : base(image, @base) { }

    public int BoneComponent => I32(0);
    public int FlexControllerIndex => I32(4);
    public float Min => F32(8);
    public float Max => F32(12);
}

/// <summary><c>mstudioboneflexdriver_t</c> (24 bytes — three ints plus
/// <c>unused[3]</c> by the declaration at studio.h:383-403):
/// <c>m_nBoneIndex</c> @0, <c>m_nControlCount</c> @4,
/// <c>m_nControlIndex</c> @8 (record-relative: stock's
/// <c>pBoneFlexDriverControl</c> at studio.h:391 is
/// <c>(byte *)this + m_nControlIndex</c>), <c>unused[3]</c> @12..23.</summary>
public sealed class MstudioBoneFlexDriver : StudioRecord
{
    internal MstudioBoneFlexDriver(byte[] image, int @base) : base(image, @base) { }

    public int BoneIndex => I32(0);
    public int ControlCount => I32(4);
    private int ControlIndex => I32(8);

    /// <summary><c>pBoneFlexDriverControl(i)</c> (studio.h:391): the 16-byte
    /// stride at <c>this + m_nControlIndex</c>.</summary>
    public MstudioBoneFlexDriverControl Control(int i) =>
        new(Image, Base + ControlIndex + 16 * i);
}

/// <summary>One virtualgroup_t: the resolved hdr plus the remap tables.</summary>
public sealed class VirtualGroupTable
{
    public StudioHdr Hdr = null!;
    internal int[] MasterSeq = Array.Empty<int>();
    internal int[] MasterAnim = Array.Empty<int>();
    internal int[] MasterBone = Array.Empty<int>();
    internal int[] BoneMap = Array.Empty<int>();
    internal int[] MasterPose = Array.Empty<int>();
    internal int[] MasterNode = Array.Empty<int>();
    internal int[] MasterAttachment = Array.Empty<int>();
}

public readonly struct VirtualEntry
{
    public VirtualEntry(int group, int index)
    {
        Group = group;
        Index = index;
    }

    public int Group { get; }
    public int Index { get; }
}

/// <summary>
/// The managed model: a validated mdl image plus the virtual-model fold.
/// Instances are immutable once built (the fold's overlay/remap tables are
/// computed in <see cref="TryCreate"/>); no mutable statics anywhere.
/// </summary>
public sealed class StudioHdr
{
    public const int StudioVersion = 48; // studio.h:70 (STUDIO_VERSION)
    // MAKEID('I','D','S','T') (commonmacros.h:27: (a<<24)|(b<<16)|(c<<8)|d) =
    // 0x54534449; studiomdl writes the LE bytes 'I','D','S','T'.
    public const int Id = 0x54534449;

    private readonly byte[] _image;

    // ---- fold state (virtualmodel_t twin; null = no fold) ------------------
    private VirtualGroupTable[]? _groups;   // group 0 = this hdr
    private VirtualEntry[]? _seq;
    private VirtualEntry[]? _anim;
    private VirtualEntry[]? _attachment;
    private VirtualEntry[]? _pose;
    private VirtualEntry[]? _iklock;
    private int[]? _boneFlagsOverlay;
    // Widened start/end from the pose-parameter fold, keyed by GLOBAL pose
    // index (the _pose list position). MstudioPoseParamDesc is a fresh view
    // over the image per read, so the widening stock performs by mutating
    // the winner's mstudioposeparamdesc_t in place cannot live on the
    // record; the fold records it here and PoseParameter applies it.
    private Dictionary<int, (float Start, float End)>? _poseRangeOverlay;
    // Knee directions copied from includes into zero-knee base chains
    // (AppendIKLocks' knee copy, studio_virtualmodel.cpp:573-592). Same
    // reason as the pose overlay: stock mutates the BASE hdr's image in
    // place; the record views are fresh per read, so the copy lives here
    // keyed by base chain index and IkChain stamps it onto the view.
    private Dictionary<int, Vector>? _kneeDirOverlay;

    /// <summary>Resolves an included-model name to its hdr image; null = not
    /// found. The engine's FindModel goes through mdlcache; managed code
    /// injects the answer (fixture: a name->bytes dictionary; game: mdlcache).</summary>
    public delegate byte[]? IncludeResolver(string name);

    private StudioHdr(byte[] image) => _image = image;

    /// <summary>
    /// Validates, folds. Fails (false, null) on bad id/version, a table that
    /// does not fit, or an unresolvable include — the fold then never exposes
    /// a half-read hdr.
    /// </summary>
    public static bool TryCreate(byte[] image, IncludeResolver? resolver, out StudioHdr? hdr)
    {
        hdr = null;
        if (image is null || image.Length < 408)
        {
            return false;
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0, 4)) != Id)
        {
            return false;
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(4, 4));
        if (version < 1 || version > StudioVersion)
        {
            return false;
        }

        if (!TablesFit(image))
        {
            return false;
        }

        var root = new StudioHdr(image);

        if (root.NumIncludeModels == 0)
        {
            root._sealed = true;
            hdr = root;
            return true;
        }

        if (resolver is null)
        {
            return false;
        }

        // DFS in qc order, matching stock's AppendModels recursion
        // (studio_virtualmodel.cpp:119-137): a group is created and its
        // fold members appended, and only then does the walk descend into
        // that include's own includes — so an included model's whole
        // subtree gets groups (and appends) before its next sibling.
        // Group/append order decides which duplicate sequence, anim,
        // attachment or pose label wins, so the walk order is observable,
        // not cosmetic: the old FIFO queue ordered siblings before
        // descendants and lost to stock for include trees 2+ deep. The
        // dead `frontier` list (an abandoned earlier DFS) is gone.
        // Stock resolves each model's whole include list in one pass
        // before descending (the list[] cache); we resolve per node — a
        // deeper failure fails the whole fold either way, as before.
        var groups = new List<VirtualGroupTable> { new() { Hdr = root } };
        var stack = new Stack<byte[]>();
        for (int j = root.NumIncludeModels - 1; j >= 0; j--)
        {
            // sznameindex is record-relative: stock's pszName() is
            // (char*)this + sznameindex (public/studio.h:485,
            // mstudiomodelgroup_t), so decode from the record base.
            int rec = root.IncludeModelRecordAt(j);
            string name = CStringAt(image, rec, ReadI32(image, rec + 4));
            byte[]? bytes = resolver(name);
            if (bytes is null)
            {
                return false; // engine asserts the hdr; managed refuses
            }

            stack.Push(bytes);
        }

        while (stack.Count != 0)
        {
            // TryCreate validates each include's own tables (bad id,
            // version or an oversized table fail the whole fold) and folds
            // its own sub-tree, as the queue walk did; only the visit
            // order changes. Push children in reverse so the pop order is
            // qc order — the same pre-order the recursion produces.
            byte[] subImage = stack.Pop();
            if (!TryCreate(subImage, resolver, out StudioHdr? sub) || sub is null)
            {
                return false;
            }

            groups.Add(new VirtualGroupTable { Hdr = sub });
            for (int j = sub.NumIncludeModels - 1; j >= 0; j--)
            {
                int srec = sub.IncludeModelRecordAt(j);
                string n = CStringAt(subImage, srec, ReadI32(subImage, srec + 4));
                byte[]? b = resolver(n);
                if (b is null)
                {
                    return false;
                }

                stack.Push(b);
            }
        }
        if (groups.Count == 1)
        {
            return false; // no resolvable includes at all
        }

        root._groups = groups.ToArray();

        // ---- merge (AppendSequences/Animations/Attachments/PoseParameters/IKLocks)
        int baseNumBones = root.NumBonesBase;
        var overlay = new int[baseNumBones];
        root._boneFlagsOverlay = overlay;

        var seqList = new List<VirtualEntry>();
        var animList = new List<VirtualEntry>();
        var attList = new List<VirtualEntry>();
        var poseList = new List<VirtualEntry>();
        var lockList = new List<VirtualEntry>();

        string[] baseBoneNames = root.BoneNameList;

        for (int g = 0; g < groups.Count; g++)
        {
            VirtualGroupTable grp = groups[g];
            StudioHdr h = grp.Hdr;

            grp.MasterSeq = new int[h.NumLocalSeq];
            grp.MasterAnim = new int[h.NumLocalAnim];
            grp.MasterBone = new int[h.NumBonesBase];
            grp.BoneMap = new int[baseNumBones];
            grp.MasterPose = new int[h.NumLocalPoseParams];
            grp.MasterNode = new int[h.NumLocalNodes];
            grp.MasterAttachment = new int[h.NumAttachments];

            // ---- AppendBonemap
            for (int j = 0; j < h.NumBonesBase; j++)
            {
                grp.MasterBone[j] = g == 0 ? j : -1;
            }

            for (int j = 0; j < baseNumBones; j++)
            {
                grp.BoneMap[j] = g == 0 ? j : -1;
            }

            if (g != 0)
            {
                for (int j = 0; j < h.NumBonesBase; j++)
                {
                    int k = Match(baseBoneNames, h.Bone(j).Name);
                    if (k >= 0)
                    {
                        grp.MasterBone[j] = k;
                        grp.BoneMap[k] = j;
                    }
                }
            }

            // ---- AppendSequences (label match; STUDIO_OVERRIDE replaces)
            for (int j = 0; j < h.NumLocalSeq; j++)
            {
                string s1 = h.SeqDescLocal(j).Label;
                int k = seqList.Count;
                for (int t = 0; t < seqList.Count; t++)
                {
                    VirtualEntry e = seqList[t];
                    if (BoneSetupQuery.AsciiEquals(groups[e.Group].Hdr.SeqDescLocal(e.Index).Label, s1))
                    {
                        k = t;
                        break;
                    }
                }

                if (k == seqList.Count)
                {
                    seqList.Add(new VirtualEntry(g, j));
                }
                else
                {
                    VirtualEntry e = seqList[k];
                    if ((groups[e.Group].Hdr.SeqDescLocal(e.Index).Flags
                            & StudioConstants.STUDIO_OVERRIDE) != 0)
                    {
                        seqList[k] = new VirtualEntry(g, j);
                    }
                }

                grp.MasterSeq[j] = k;
            }

            // ---- AppendAnimations (name match)
            for (int j = 0; j < h.NumLocalAnim; j++)
            {
                string s1 = h.LocalAnimdesc(j).Name;
                int k = animList.Count;
                for (int t = 0; t < animList.Count; t++)
                {
                    VirtualEntry e = animList[t];
                    if (BoneSetupQuery.AsciiEquals(groups[e.Group].Hdr.LocalAnimdesc(e.Index).Name, s1))
                    {
                        k = t;
                        break;
                    }
                }

                if (k == animList.Count)
                {
                    animList.Add(new VirtualEntry(g, j));
                }

                grp.MasterAnim[j] = k;
            }

            // ---- AppendAttachments
            for (int j = 0; j < h.NumAttachments; j++)
            {
                int n = grp.MasterBone[h.AttachmentLocal(j).LocalBone];
                if (n == -1)
                {
                    grp.MasterAttachment[j] = -1;
                    continue; // skip: the attachment's bone isn't in the base
                }

                string s1 = h.AttachmentLocal(j).Name;
                int k = attList.Count;
                for (int t = 0; t < attList.Count; t++)
                {
                    VirtualEntry e = attList[t];
                    if (BoneSetupQuery.AsciiEquals(groups[e.Group].Hdr.AttachmentLocal(e.Index).Name, s1))
                    {
                        k = t;
                        break;
                    }
                }

                if (k == attList.Count)
                {
                    attList.Add(new VirtualEntry(g, j));
                    grp.MasterAttachment[j] = k;
                    if ((root.BoneFlagsBase(n) & StudioConstants.BONE_USED_BY_ATTACHMENT) == 0)
                    {
                        int b = n;
                        while (b != -1)
                        {
                            overlay[b] |= StudioConstants.BONE_USED_BY_ATTACHMENT;
                            b = root.BoneParentBase(b);
                        }
                    }
                }
                else
                {
                    grp.MasterAttachment[j] = k;
                }
            }

            // ---- AppendPoseParameters (duplicates widen the winner)
            for (int j = 0; j < h.NumLocalPoseParams; j++)
            {
                string s1 = h.PoseParamLocal(j).Name;
                int k = poseList.Count;
                for (int t = 0; t < poseList.Count; t++)
                {
                    VirtualEntry e = poseList[t];
                    if (BoneSetupQuery.AsciiEquals(groups[e.Group].Hdr.PoseParamLocal(e.Index).Name, s1))
                    {
                        k = t;
                        break;
                    }
                }

                if (k == poseList.Count)
                {
                    poseList.Add(new VirtualEntry(g, j));
                }
                else
                {
                    // Duplicate: reset start/end to fit the full dynamic
                    // range (studio_virtualmodel.cpp:472-486). Stock mutates
                    // the winner's mstudioposeparamdesc_t in place, so a
                    // later include folding into the same winner sees the
                    // already-widened values. Our record is a fresh view over
                    // the image per read, so the widened pair lives in the
                    // overlay keyed by the GLOBAL pose index — read back
                    // here and applied by PoseParameter. Stock's nested
                    // min/max operand order, over the winner's current
                    // values.
                    VirtualEntry e = poseList[k];
                    MstudioPoseParamDesc p1 = h.PoseParamLocal(j);
                    MstudioPoseParamDesc p2 = groups[e.Group].Hdr.PoseParamLocal(e.Index);
                    float p2Start = p2.Start;
                    float p2End = p2.End;
                    if (root._poseRangeOverlay is not null
                        && root._poseRangeOverlay.TryGetValue(k, out (float Start, float End) widened))
                    {
                        p2Start = widened.Start;
                        p2End = widened.End;
                    }

                    float start = Math.Min(p2End, Math.Min(p1.End, Math.Min(p2Start, p1.Start)));
                    float end = Math.Max(p2End, Math.Max(p1.End, Math.Max(p2Start, p1.Start)));
                    (root._poseRangeOverlay ??= new Dictionary<int, (float, float)>())[k] = (start, end);
                }

                grp.MasterPose[j] = k;
            }

            // ---- AppendNodes (name match; bone_setup reads nodes only via
            // EntryNode/ExitNode, which need the global table for group 0 —
            // the base's nodes are its own, so masterNode stays identity here
            // and EntryNode routing is a client concern, waived for the server arm)
            for (int j = 0; j < h.NumLocalNodes; j++)
            {
                grp.MasterNode[j] = j;
            }

            // ---- AppendIKLocks (dup by chain index)
            for (int j = 0; j < h.NumLocalIkAutoPlayLocks; j++)
            {
                int chain1 = h.AutoPlayLockLocal(j).Chain;
                int k = lockList.Count;
                for (int t = 0; t < lockList.Count; t++)
                {
                    VirtualEntry e = lockList[t];
                    if (groups[e.Group].Hdr.AutoPlayLockLocal(e.Index).Chain == chain1)
                    {
                        k = t;
                        break;
                    }
                }

                if (k == lockList.Count)
                {
                    lockList.Add(new VirtualEntry(g, j));
                }
            }

            // ---- knee-direction copy (studio_virtualmodel.cpp:573-592, at
            // the END of AppendIKLocks for group != 0): when the include has
            // the same chain count, a base chain whose link-0 kneeDir is
            // zero adopts the include's non-zero one. Studio_SolveIK
            // (bone_setup.cpp:2690-2705) branches on LengthSqr > 0: without
            // the copy the port always derives the knee, solving $include
            // legs differently from stock. Stock mutates the ONE base image
            // (m_group[0]) in place, so the first include to supply a knee
            // wins — later groups' zero-tests see it filled; the stamped
            // root.IkChain reproduces that. The include side reads its own
            // accessor: the engine reads a group's chains through that
            // hdr's virtualmodel, so a knee its own sub-includes gave it is
            // visible here too (the nested TryCreate recorded it).
            if (g != 0 && h.NumIkChains == root.NumIkChains)
            {
                for (int j = 0; j < h.NumIkChains; j++)
                {
                    Vector includeKnee = h.IkChain(j).Link(0).KneeDir;
                    if (BoneSetupMath.VectorLengthSqr(root.IkChain(j).Link(0).KneeDir) == 0.0f
                        && BoneSetupMath.VectorLengthSqr(includeKnee) > 0.0f)
                    {
                        (root._kneeDirOverlay ??= new Dictionary<int, Vector>())[j] = includeKnee;

                        // A view made before the copy must not outlive it.
                        root._ikChainViews = null;
                    }
                }
            }
        }

        root._seq = seqList.ToArray();
        root._anim = animList.ToArray();
        root._attachment = attList.ToArray();
        root._pose = poseList.ToArray();
        root._iklock = lockList.ToArray();

        root._sealed = true;
        hdr = root;
        return true;
    }

    private static int Match(string[] list, string s)
    {
        for (int i = 0; i < list.Length; i++)
        {
            if (BoneSetupQuery.AsciiEquals(list[i], s))
            {
                return i;
            }
        }

        return -1;
    }

    private string[]? _boneNameList;
    internal string[] BoneNameList => _boneNameList ??= BuildBoneNames();

    private string[] BuildBoneNames()
    {
        var names = new string[NumBonesBase];
        for (int i = 0; i < NumBonesBase; i++)
        {
            names[i] = Bone(i).Name;
        }

        return names;
    }

    // ---- header table (offsets from the dump) ------------------------------

    /// <summary><c>studiohdr_t::name</c>: an inline <c>char[64]</c> at 12, not a string index.</summary>
    public string Name => InlineString(_image, 12, 64);
    public int ImageLength => _image.Length;
    public int Flags => ReadI32(_image, 152);
    public int NumBonesBase => ReadI32(_image, 156);
    private int BoneIndex => ReadI32(_image, 160);
    public int NumBoneControllers => ReadI32(_image, 164);
    private int BoneControllerIndex => ReadI32(_image, 168);
    public int NumHitboxSets => ReadI32(_image, 172);
    private int HitboxSetIndex => ReadI32(_image, 176);
    public int NumLocalAnim => ReadI32(_image, 180);
    private int LocalAnimIndex => ReadI32(_image, 184);
    public int NumLocalSeq => ReadI32(_image, 188);
    private int LocalSeqIndex => ReadI32(_image, 192);
    public int NumLocalNodes => ReadI32(_image, 248);
    private int LocalNodeNameIndex => ReadI32(_image, 256);
    public int NumIkChains => ReadI32(_image, 284);
    private int IkChainIndex => ReadI32(_image, 288);
    public int NumLocalPoseParams => ReadI32(_image, 300);
    private int LocalPoseParamIndex => ReadI32(_image, 304);
    public int SurfacePropIndex => ReadI32(_image, 308);

    /// <summary><c>studiohdr_t::pszSurfaceProp()</c> (studio.h:2305): the C
    /// string at image + surfacepropindex @308 — image-ABSOLUTE, like the
    /// @312/@316 keyvalue pair, because <c>this</c> is the image base. Index
    /// 0 (a QC with no <c>$surfaceprop</c>) is stock's UB arm — it reads the
    /// image header bytes as a string; the reader's standing
    /// <c>CString(0)</c> convention answers the empty string (NR-05).</summary>
    public string SurfacePropText()
    {
        int index = SurfacePropIndex;
        if (index == 0)
        {
            return string.Empty;
        }

        int end = index;
        while (_image[end] != 0)
        {
            end++;
        }

        return System.Text.Encoding.UTF8.GetString(_image, index, end - index);
    }

    public int KeyValueIndex => ReadI32(_image, 312);
    public int KeyValueSize => ReadI32(_image, 316);
    public int NumLocalIkAutoPlayLocks => ReadI32(_image, 320);
    private int LocalIkAutoPlayLockIndex => ReadI32(_image, 324);
    public float Mass => ReadF32(_image, 328);
    public int Contents => ReadI32(_image, 332);
    public int NumIncludeModels => ReadI32(_image, 336);
    internal int IncludeModelRecordAt(int i) => ReadI32(_image, 340) + 8 * i; // mstudiomodelgroup_t
    private int AnimBlockNameIndex => ReadI32(_image, 348);
    public int NumAnimBlocks => ReadI32(_image, 352);
    private int AnimBlockIndex => ReadI32(_image, 356);
    private int StudioHdr2Index => ReadI32(_image, 400);

    // ---- the query cluster's tables (KEYVALUETEXT, flexcontrollers,
    //      bone flex drivers) ---------------------------------------------

    private int FlexControllerTableIndex => ReadI32(_image, 272);

    /// <summary>Base-hdr flex controllers (<c>numflexcontrollers</c> @268 /
    /// <c>flexcontrollerindex</c> @272). <c>Studio_RunBoneFlexDrivers</c>
    /// resolves each driver control's index here, then skips the control when
    /// <c>localToGlobal &lt; 0</c> (bone_setup.cpp:6181, 6201).</summary>
    public int NumFlexControllers => ReadI32(_image, 268);

    /// <summary><c>pFlexcontroller(i)</c>: the 20-byte stride. Stock indexes
    /// unguarded (the assert is compiled out); so does this.</summary>
    public MstudioFlexController FlexController(int i) =>
        new(_image, FlexControllerTableIndex + 20 * i);

    /// <summary>The <c>studiohdr2_t</c> flex-driver pair (@24/@28 by the
    /// declaration order at studio.h:2117-2118 — <c>linearboneindex</c> @16
    /// and <c>sznameindex</c> @20 are the dump's pinned fields, and the
    /// flexdriver count/index follow in declaration order).
    /// <c>BoneFlexDriverCount</c> (studio.h:2413) answers 0 without a
    /// studiohdr2.</summary>
    public int BoneFlexDriverCount =>
        StudioHdr2Index == 0 ? 0 : ReadI32(_image, StudioHdr2Index + 24);

    private int BoneFlexDriverTableIndex =>
        StudioHdr2Index == 0 ? 0 : ReadI32(_image, StudioHdr2Index + 28);

    /// <summary><c>pBoneFlexDriver(i)</c> (studio.h:2119): the 24-byte stride
    /// based at the studiohdr2 record, since <c>m_nBoneFlexDriverIndex</c> is
    /// relative to that record — stock's <c>((byte *)this) + index</c>.
    /// Null on the studiohdr2-absent arm (<c>studiohdr2index == 0</c>) or a
    /// zero table index, matching stock's NULL pointer.</summary>
    public MstudioBoneFlexDriver? BoneFlexDriver(int i)
    {
        int table = BoneFlexDriverTableIndex;
        int at = table == 0 ? 0 : StudioHdr2Index + table + 24 * i;
        return at == 0 ? null : new MstudioBoneFlexDriver(_image, at);
    }

    /// <summary><c>KeyValueText()</c> (studio.h:2308-2310): the raw
    /// <c>KEYVALUETEXT</c> C string at <c>this + keyvalueindex</c>, bounded
    /// by <c>keyvaluesize</c> and terminated by its NUL (qc stores
    /// <c>size = strlen + 1</c>). Stock answers NULL when the size is 0 —
    /// the absent arm here returns null, not empty, so the port tells "no
    /// blob" from "empty blob" exactly like
    /// <c>Studio_GetKeyValueText</c> (bone_setup.cpp:6095) hands NULL to
    /// <c>KeyValues::LoadFromBuffer</c>.</summary>
    public string? KeyValueText()
    {
        int size = KeyValueSize;
        if (size == 0)
        {
            return null;
        }

        int start = KeyValueIndex;
        int end = start;
        int limit = start + size;
        while (end < limit && _image[end] != 0)
        {
            end++;
        }

        return Encoding.UTF8.GetString(_image, start, end - start);
    }

    private int LinearBoneTableIndex =>
        StudioHdr2Index == 0 ? 0 : ReadI32(_image, StudioHdr2Index + 16);

    /// <summary><c>pStudioHdr2()->pLinearBones()</c> (studio.h:2112):
    /// <c>linearboneindex</c> is relative to the studiohdr2 record, not the
    /// image base — stock's <c>((byte *)this) + linearboneindex</c>.</summary>
    private int LinearBoneAddress =>
        LinearBoneTableIndex == 0 ? 0 : StudioHdr2Index + LinearBoneTableIndex;

    /// <summary>True when this model folds included models in.</summary>
    public bool IsVirtual => _groups is not null && _groups.Length > 1;

    /// <summary>The number of fold groups (1 = plain hdr).</summary>
    public int NumGroups => _groups?.Length ?? 1;

    /// <summary>Linear bones when studiohdr2 carries them; the extract's
    /// <c>linearbones ? linearbones : pBone</c> dispatch reads this.</summary>
    public MstudioLinearBone? LinearBones()
    {
        int at = LinearBoneAddress;

        if (at == 0)
        {
            return null;
        }

        return _linearBonesView ??= new MstudioLinearBone(_image, at);
    }

    private MstudioLinearBone? _linearBonesView;

    private QuatInterpTable?[]? _quatInterpTables;

    /// <summary>Bone <paramref name="bone"/>'s quat-interp record (at
    /// <paramref name="procBase"/>), decoded once: the image never changes.</summary>
    internal QuatInterpTable QuatInterpTable(int bone, int procBase)
    {
        if ((uint)bone >= (uint)NumBonesBase)
        {
            return new QuatInterpTable(new MstudioQuatInterpBone(_image, procBase));
        }

        QuatInterpTable?[] tables = _quatInterpTables ??= new QuatInterpTable?[NumBonesBase];
        return tables[bone] ??= new QuatInterpTable(new MstudioQuatInterpBone(_image, procBase));
    }

    // ---- the per-bone tables a build reads every pass (plan_physics_bones_perf
    // A3: "no repeated StudioHdr reads"). Decoded once, and only from a
    // SEALED header: the fold ORs include flags into the overlay while it
    // runs, so a table read before the fold finished would be stale. Until
    // then every read decodes, as before.
    private bool _sealed;
    private int[]? _boneFlagsTable;
    private int[]? _boneParentTable;
    private Vector[]? _initPosTable;
    private Quaternion[]? _initQuatTable;

    private void BuildBoneTables()
    {
        int n = NumBonesBase;
        var flags = new int[n];
        var parents = new int[n];
        var pos = new Vector[n];
        var quat = new Quaternion[n];
        MstudioLinearBone? lb = LinearBones();

        for (int i = 0; i < n; i++)
        {
            flags[i] = ReadBoneFlags(i);
            parents[i] = ReadBoneParent(i);

            // InitPose's dispatch: the linear table when there is one, the
            // bone record otherwise (both index every bone).
            if (lb is not null)
            {
                pos[i] = lb.Position(i);
                quat[i] = lb.Quaternion(i);
            }
            else
            {
                MstudioBone bone = Bone(i);
                pos[i] = bone.Position;
                quat[i] = bone.Quaternion;
            }
        }

        _boneParentTable = parents;
        _initPosTable = pos;
        _initQuatTable = quat;
        _boneFlagsTable = flags;
    }

    /// <summary>
    /// <c>InitPose</c>'s source for bone <paramref name="i"/>: its rest
    /// position and rotation (the linear table's when the model has one),
    /// decoded once a sealed header is asked.
    /// </summary>
    internal bool TryRestPose(out ReadOnlySpan<Vector> pos, out ReadOnlySpan<Quaternion> quat)
    {
        if (!_sealed)
        {
            pos = default;
            quat = default;
            return false;
        }

        if (_boneFlagsTable is null)
        {
            BuildBoneTables();
        }

        pos = _initPosTable;
        quat = _initQuatTable;
        return true;
    }

    // ---- CStudioHdr-routed accessors ---------------------------------------

    /// <summary>Bone count: the base hdr's own (the fold never adds bones).</summary>
    public int NumBones => NumBonesBase;

    /// <summary>Base bone parent (no remap).</summary>
    internal int BoneParentBase(int i) => ReadI32(_image, BoneIndex + 216 * i + 4);

    internal int BoneFlagsBase(int i) => ReadI32(_image, BoneIndex + 216 * i + 160);

    public int BoneFlags(int i)
    {
        if (_sealed && (uint)i < (uint)NumBonesBase)
        {
            if (_boneFlagsTable is null)
            {
                BuildBoneTables();
            }

            return _boneFlagsTable![i];
        }

        return ReadBoneFlags(i);
    }

    private int ReadBoneFlags(int i)
    {
        MstudioLinearBone? lb = LinearBones();
        int flags = lb is not null && i < lb.NumBones ? lb.Flags(i) : BoneFlagsBase(i);
        return _boneFlagsOverlay is null ? flags : flags | _boneFlagsOverlay[i];
    }

    public int BoneParent(int i)
    {
        if (_sealed && (uint)i < (uint)NumBonesBase)
        {
            if (_boneFlagsTable is null)
            {
                BuildBoneTables();
            }

            return _boneParentTable![i];
        }

        return ReadBoneParent(i);
    }

    private int ReadBoneParent(int i)
    {
        MstudioLinearBone? lb = LinearBones();
        return lb is not null && i < lb.NumBones ? lb.Parent(i) : BoneParentBase(i);
    }

    // The record views a build reads per bone, per pass, made once each: a
    // view is the image and an address, and neither changes under a
    // constructed hdr (plan_physics_bones_perf A3; they were the build's
    // commonest garbage after the pose tables).
    private MstudioBone?[]? _boneViews;
    private MstudioIkChain?[]? _ikChainViews;

    public MstudioBone Bone(int i)
    {
        if ((uint)i >= (uint)NumBonesBase)
        {
            return new(_image, BoneIndex + 216 * i);
        }

        MstudioBone?[] views = _boneViews ??= new MstudioBone?[NumBonesBase];
        return views[i] ??= new(_image, BoneIndex + 216 * i);
    }

    /// <summary>Chain view; link 0's kneeDir carries the fold's copy (see
    /// <see cref="_kneeDirOverlay"/>) when this is the base hdr. The stamp
    /// sits on the returned chain and <c>Link(0)</c> propagates it onto
    /// every fresh link view (records are re-nosed per call).</summary>
    public MstudioIkChain IkChain(int i)
    {
        bool held = (uint)i < (uint)NumIkChains;

        if (held && _ikChainViews?[i] is { } view)
        {
            return view;
        }

        MstudioIkChain chain = new(_image, IkChainIndex + 16 * i);
        if (_kneeDirOverlay is not null
            && _kneeDirOverlay.TryGetValue(i, out Vector copied))
        {
            chain.MutableKneeDir = copied;
            chain.MutableKneeDirSet = true;
        }

        if (held)
        {
            (_ikChainViews ??= new MstudioIkChain?[NumIkChains])[i] = chain;
        }

        return chain;
    }

    public int NumIKChains => NumIkChains;

    public MstudioBoneController BoneController(int i) => new(_image, BoneControllerIndex + 56 * i);

    public MstudioHitboxSet HitboxSet(int i) => new(_image, HitboxSetIndex + 12 * i);

    private int LocalAttachmentIndex => ReadI32(_image, 244);
    public int NumAttachments => ReadI32(_image, 240);

    /// <summary>Local (hdr-relative) attachment.</summary>
    public MstudioAttachment AttachmentLocal(int i) => new(_image, LocalAttachmentIndex + 92 * i);

    /// <summary>Global attachment count (fold-routed).</summary>
    public int NumGlobalAttachments => _attachment?.Length ?? NumAttachments;

    /// <summary>Global attachment (fold routes to the owning group's local table).</summary>
    public MstudioAttachment Attachment(int i)
    {
        if (_attachment is null)
        {
            return AttachmentLocal(i);
        }

        VirtualEntry e = _attachment[i];
        return _groups![e.Group].Hdr.AttachmentLocal(e.Index);
    }

    /// <summary><c>GetAttachmentBone</c>: masterBone remap for virtual, raw localbone otherwise.</summary>
    public int GetAttachmentBone(int i)
    {
        if (_attachment is null)
        {
            return AttachmentLocal(i).LocalBone;
        }

        VirtualEntry e = _attachment[i];
        int n = _groups![e.Group].MasterBone[AttachmentLocal(e.Index).LocalBone];
        return n == -1 ? 0 : n;
    }

    public MstudioPoseParamDesc PoseParamLocal(int i) => new(_image, LocalPoseParamIndex + 20 * i);

    /// <summary>Global poseparam count (fold-routed).</summary>
    public int NumPoseParameters => _pose?.Length ?? NumLocalPoseParams;

    /// <summary>Global poseparam (fold-routed; widened start/end apply).
    /// The fold's duplicate-widening (AppendPoseParameters) lives in
    /// <see cref="_poseRangeOverlay"/>, keyed by this global index; the
    /// record view itself stays an unmutated image read.</summary>
    public MstudioPoseParamDesc PoseParameter(int i)
    {
        if (_sealed && (uint)i < (uint)NumPoseParameters)
        {
            MstudioPoseParamDesc?[] views = _poseParamViews ??= new MstudioPoseParamDesc?[NumPoseParameters];
            return views[i] ??= ReadPoseParameter(i);
        }

        return ReadPoseParameter(i);
    }

    private MstudioPoseParamDesc ReadPoseParameter(int i)
    {
        if (_pose is null)
        {
            return PoseParamLocal(i);
        }

        VirtualEntry e = _pose[i];
        MstudioPoseParamDesc p = _groups![e.Group].Hdr.PoseParamLocal(e.Index);
        if (_poseRangeOverlay is not null
            && _poseRangeOverlay.TryGetValue(i, out (float Start, float End) widened))
        {
            p.MutableStart = widened.Start;
            p.MutableEnd = widened.End;
            p.MutableStartSet = true;
        }

        return p;
    }

    /// <summary><c>GetSharedPoseParameter</c>: group-local pose -> global, -1 when out of range.</summary>
    public int GetSharedPoseParameter(int iSequence, int iLocalPose)
    {
        if (_pose is null)
        {
            return iLocalPose == -1 ? -1 : iLocalPose;
        }

        if (iLocalPose == -1)
        {
            return -1;
        }

        int group = _seq![iSequence].Group;
        if (group < 0 || group >= _groups!.Length)
        {
            return -1;
        }

        if (iLocalPose >= _groups[group].MasterPose.Length)
        {
            return -1;
        }

        return _groups[group].MasterPose[iLocalPose];
    }

    /// <summary><c>iRelativeAnim</c>: the sequence's group masterAnim, else identity.</summary>
    public int IRelativeAnim(int baseSeq, int relAnim)
    {
        if (_seq is null || _groups is null)
        {
            return relAnim;
        }

        return _groups[_seq[baseSeq].Group].MasterAnim[relAnim];
    }

    /// <summary><c>iRelativeSeq</c>: the sequence's group masterSeq.</summary>
    public int IRelativeSeq(int baseSeq, int relSeq)
    {
        if (_seq is null || _groups is null)
        {
            return relSeq;
        }

        return _groups[_seq[baseSeq].Group].MasterSeq[relSeq];
    }

    public string NodeName(int i) => CStringAt(_image, 0, ReadI32(_image, LocalNodeNameIndex + 4 * i));

    public MstudioIkLock AutoPlayLockLocal(int i) => new(_image, LocalIkAutoPlayLockIndex + 32 * i);
    public int NumLocalAutoPlayLocks => NumLocalIkAutoPlayLocks;

    /// <summary>Global autoplay lock count (fold-routed).</summary>
    public int NumAutoPlayLocks => _iklock?.Length ?? NumLocalIkAutoPlayLocks;

    public MstudioIkLock AutoPlayLock(int i)
    {
        if (_iklock is null)
        {
            return AutoPlayLockLocal(i);
        }

        VirtualEntry e = _iklock[i];
        return _groups![e.Group].Hdr.AutoPlayLockLocal(e.Index);
    }

    /// <summary>Sequences: fold count, else the base's local count.</summary>
    public int NumSequences => _seq?.Length ?? NumLocalSeq;

    /// <summary><c>CStudioHdr::CountAutoplaySequences</c>: the sequences
    /// flagged <c>STUDIO_AUTOPLAY</c> over the (fold-routed) table.</summary>
    public int CountAutoplaySequences() => AutoplayList().Length;

    // GetAutoplayList's answer, read once: the image never changes under a
    // constructed hdr, and a walk of every sequence per build was the build's
    // largest garbage (Male_01's fold, every frame; plan_physics_bones_perf A3).
    private ushort[]? _autoplayList;

    /// <summary>
    /// <c>CStudioHdr::GetAutoplayList</c> (studio_generic_io.cpp:110, the
    /// virtual model's <c>m_autoplaySequences</c>): the global indices of the
    /// sequences flagged <c>STUDIO_AUTOPLAY</c>, in table order. Stock caches
    /// it per header in a file-global tree; this header holds its own.
    /// </summary>
    public ReadOnlySpan<ushort> AutoplayList()
    {
        if (_autoplayList is null)
        {
            var list = new List<ushort>();

            for (int i = 0; i < NumSequences; i++)
            {
                if ((SeqDesc(i).Flags & StudioConstants.STUDIO_AUTOPLAY) != 0)
                {
                    list.Add((ushort)i);
                }
            }

            _autoplayList = [.. list];
        }

        return _autoplayList;
    }

    /// <summary>Global anims (fold count), else base locals.</summary>
    public int NumAnims => _anim?.Length ?? NumLocalAnim;

    /// <summary><c>pSeqdesc(i)</c> folded.</summary>
    public MstudioSeqDesc SeqDesc(int i)
    {
        if (_sealed && (uint)i < (uint)NumSequences)
        {
            MstudioSeqDesc?[] views = _seqDescViews ??= new MstudioSeqDesc?[NumSequences];
            return views[i] ??= ReadSeqDesc(i);
        }

        return ReadSeqDesc(i);
    }

    // The fold-routed views of a sealed header, made once each (see the
    // per-bone tables): a build reads its sequence, its anims and its pose
    // parameters' views on every pass.
    private MstudioSeqDesc?[]? _seqDescViews;
    private MstudioAnimDesc?[]? _animDescViews;
    private MstudioPoseParamDesc?[]? _poseParamViews;

    private MstudioSeqDesc ReadSeqDesc(int i)
    {
        if (_seq is null)
        {
            return SeqDescLocal(i);
        }

        VirtualEntry e = _seq[i];
        return _groups![e.Group].Hdr.SeqDescLocal(e.Index);
    }

    public MstudioSeqDesc SeqDescLocal(int i) => new(_image, LocalSeqIndex + 212 * i);

    /// <summary><c>pAnimdesc(i)</c> folded.</summary>
    public MstudioAnimDesc AnimDesc(int i)
    {
        if (_sealed && (uint)i < (uint)NumAnims)
        {
            MstudioAnimDesc?[] views = _animDescViews ??= new MstudioAnimDesc?[NumAnims];
            return views[i] ??= ReadAnimDesc(i);
        }

        return ReadAnimDesc(i);
    }

    private MstudioAnimDesc ReadAnimDesc(int i)
    {
        if (_anim is null)
        {
            return LocalAnimdesc(i);
        }

        VirtualEntry e = _anim[i];
        return _groups![e.Group].Hdr.LocalAnimdesc(e.Index);
    }

    public MstudioAnimDesc LocalAnimdesc(int i) => new(_image, LocalAnimIndex + 100 * i);

    internal MstudioAnimDesc? TryLocalAnimdesc(int i) =>
        i >= 0 && i < NumLocalAnim ? LocalAnimdesc(i) : null;

    /// <summary>The fold group of a global sequence.</summary>
    public int SeqGroup(int i) => _seq?[i].Group ?? 0;

    /// <summary>The fold group of a global animdesc.</summary>
    public int AnimGroup(int i) => _anim?[i].Group ?? 0;

    /// <summary><c>virtualmodel_t::pGroupHdr(g)</c>: the group's own hdr
    /// (group 0 is this one).</summary>
    public StudioHdr GroupHdr(int g) => _groups is null ? this : _groups[g].Hdr;

    /// <summary><c>virtualgroup_t::boneMap</c>: base-hdr bone -> this group's
    /// local bone, -1 when the group lacks the bone.</summary>
    public int[] GroupBoneMap(int g) => _groups is null ? IdentityBoneMap(NumBonesBase) : _groups[g].BoneMap;

    /// <summary><c>virtualgroup_t::masterBone</c>: this group's local bone ->
    /// base-hdr bone, -1 when the base lacks it.</summary>
    public int[] GroupMasterBone(int g) =>
        _groups is null ? IdentityBoneMap(NumBonesBase) : _groups[g].MasterBone;

    private static int[] IdentityBoneMap(int n)
    {
        int[] m = new int[n];
        for (int i = 0; i < n; i++)
        {
            m[i] = i;
        }

        return m;
    }

    /// <summary>The raw image this hdr was built from (zeroframe rows read
    /// it by byte address, like stock's <c>pData</c> walk).</summary>
    internal byte[] RawImageForRows => _image;

    /// <summary>
    /// <c>mstudioanimdesc_t::pAnimBlock(block, index)</c>: block -1 (model
    /// needs recompile) and a missing block answer null; block 0 is
    /// header-relative. Blocks &gt; 0 read the animblock image the engine
    /// would have paged in separately — this port's models are single-file
    /// mdl, so an out-of-image block is "missing", the same answer stock gets
    /// when GetAnimBlock returns NULL.
    /// </summary>
    internal MstudioAnim? PAnimBlock(MstudioAnimDesc anim, int block, int index)
    {
        if (block == -1)
        {
            return null;
        }

        int at;
        // Stock's pAnimBlock is an mstudioanimdesc_t member (pristine
        // studio.cpp:44-62): block 0 is (byte *)this + index, and block != 0
        // reads pStudiohdr()->GetAnimBlock(), where pStudiohdr() follows the
        // animdesc's own baseptr (studio.h:727). For a fold the owner is the
        // GROUP's image, not the base hdr's.
        byte[] img = anim.RawImage;
        if (block == 0)
        {
            at = anim.RawAddress + index;
        }
        else
        {
            // The animblock table lives in the hdr that owns the animdesc
            // (pristine studio.cpp:76 reads pHdr from `this`), so it is read
            // from img, not from the base hdr's image.
            int numAnimBlocks = ReadI32(img, 352);
            int table = ReadI32(img, 356) + 8 * block;
            if (block < 0 || block >= numAnimBlocks || table + 8 > img.Length)
            {
                return null;
            }

            int start = ReadI32(img, table);
            int end = ReadI32(img, table + 4);
            if (start == 0 || end <= start)
            {
                return null;
            }

            long addr = (long)start + index;
            // Stock does no bounds check here; sizeof(mstudioanim_t) is 4 and
            // a flags-0 record is legal, so only the header must fit. Section
            // payloads are checked where they are read.
            if (addr < 4 || addr + 4 > img.Length)
            {
                return null;
            }

            at = (int)addr;
        }

        if (at < 4 || at + 4 > img.Length)
        {
            return null;
        }

        return new MstudioAnim(img, at);
    }

    /// <summary>
    /// <c>mstudioanimdesc_t::pAnim</c> (pristine studio.cpp:76). The
    /// section-windowing arms are kept; the block-preload loop and the
    /// <c>mod_load_showstall</c> telemetry are IO-only and absent (no block
    /// can page here: every answer is local or missing), and with them the
    /// stall blend — <c>flStall</c> would stay 0 in this regime anyway.
    /// </summary>
    public MstudioAnim? PAnim(MstudioAnimDesc anim, ref int iFrame)
    {
        int block = anim.AnimBlock;
        int index = anim.AnimIndex;
        int section = 0;
        int sectionFrames = anim.SectionFrames;
        int numFrames = anim.NumFrames;

        if (sectionFrames != 0)
        {
            if (numFrames > sectionFrames && iFrame == numFrames - 1)
            {
                // last frame on long anims is stored separately
                iFrame = 0;
                section = (numFrames / sectionFrames) + 1;
            }
            else
            {
                section = iFrame / sectionFrames;
                iFrame -= section * sectionFrames;
            }

            (block, index) = anim.Section(section);
        }

        if (block == -1)
        {
            return null; // model needs to be recompiled
        }

        MstudioAnim? panim = PAnimBlock(anim, block, index);

        if (panim is null && section > 0)
        {
            // stock backs up to the last section whose block answered, and
            // lands the frame on that section's last frame
            while (--section >= 0)
            {
                (block, index) = anim.Section(section);
                panim = PAnimBlock(anim, block, index);
                if (panim is not null)
                {
                    iFrame = sectionFrames - 1;
                    break;
                }
            }
        }

        return panim;
    }

    // ---- static helpers -----------------------------------------------------

    private static int ReadI32(byte[] image, int at) =>
        BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at, 4));

    private static float ReadF32(byte[] image, int at) =>
        BinaryPrimitives.ReadSingleLittleEndian(image.AsSpan(at, 4));

    private static string InlineString(byte[] image, int at, int capacity)
    {
        int end = at;
        while (end < at + capacity && end < image.Length && image[end] != 0)
        {
            end++;
        }

        return Encoding.UTF8.GetString(image, at, end - at);
    }

    private static string CStringAt(byte[] image, int @base, int index)
    {
        if (index == 0)
        {
            return string.Empty;
        }

        int start = @base + index;
        int end = start;
        while (end < image.Length && image[end] != 0)
        {
            end++;
        }

        return Encoding.UTF8.GetString(image, start, end - start);
    }

    private static bool TablesFit(byte[] image)
    {
        int length = ReadI32(image, 76);
        if (length > image.Length)
        {
            return false;
        }

        (int num, int index, int size)[] tables =
        {
            (ReadI32(image, 156), ReadI32(image, 160), 216), // bones
            (ReadI32(image, 164), ReadI32(image, 168), 56),  // bonecontrollers
            (ReadI32(image, 172), ReadI32(image, 176), 12),  // hitboxsets
            (ReadI32(image, 180), ReadI32(image, 184), 100), // localanim
            (ReadI32(image, 188), ReadI32(image, 192), 212), // localseq
            (ReadI32(image, 240), ReadI32(image, 244), 92),  // attachments
            (ReadI32(image, 248), ReadI32(image, 256), 4),   // localnode name table (studio.h:2266, what NodeName reads; the numlocalnodes² transition bytes at @252 are never read)
            (ReadI32(image, 284), ReadI32(image, 288), 16),  // ikchains
            (ReadI32(image, 300), ReadI32(image, 304), 20),  // poseparams
            (ReadI32(image, 320), ReadI32(image, 324), 32),  // ikautoplaylocks (mstudioiklock_t)
        };
        foreach ((int num, int index, int size) in tables)
        {
            if (num != 0 && (index == 0 || (long)index + (long)num * size > image.Length))
            {
                return false;
            }
        }

        int hdr2 = ReadI32(image, 400);
        if (hdr2 != 0)
        {
            if (hdr2 + 256 > image.Length)
            {
                return false;
            }

            int lbi = ReadI32(image, hdr2 + 16);
            if (lbi != 0)
            {
                // pLinearBones bases the index at the studiohdr2 record
                int at = hdr2 + lbi;
                if (at + 64 > image.Length)
                {
                    return false;
                }

                int linearbones = ReadI32(image, at);
                if (linearbones < 0 || linearbones > StudioConstants.MAXSTUDIOBONES)
                {
                    return false;
                }
            }
        }

        return true;
    }
}

/// <summary>Stock's studio.h flags/constants the port needs (one table so each
/// is cited where used).</summary>
public static class StudioConstants
{
    /// <summary>studio.h:82 — total bones actually used.</summary>
    public const int MAXSTUDIOBONES = 128;
    // mstudioanim_t flags (studio.h:660-665)
    public const int STUDIO_ANIM_RAWPOS = 0x01;
    public const int STUDIO_ANIM_RAWROT = 0x02;
    public const int STUDIO_ANIM_ANIMPOS = 0x04;
    public const int STUDIO_ANIM_ANIMROT = 0x08;
    public const int STUDIO_ANIM_DELTA = 0x10;
    public const int STUDIO_ANIM_RAWROT2 = 0x20;

    // seqdesc flags
    public const int STUDIO_LOOPING = 0x0001;
    public const int STUDIO_SNAP = 0x0002;
    public const int STUDIO_DELTA = 0x0004;
    public const int STUDIO_AUTOPLAY = 0x0008;
    public const int STUDIO_POST = 0x0010;
    public const int STUDIO_ALLZEROS = 0x0020; // studio.h:3083
    public const int STUDIO_CYCLEPOSE = 0x0080;
    public const int STUDIO_REALTIME = 0x0100;
    public const int STUDIO_LOCAL = 0x0200;
    public const int STUDIO_OVERRIDE = 0x0800;
    public const int STUDIO_ACTIVITY = 0x1000;

    // animdesc flags
    public const int STUDIO_ANIM_REALTIME_FPS = 0x0002;

    // bone flags (studio.h:405ff)
    public const int BONE_FIXED_ALIGNMENT = 0x00100000;
    // studio.h:426 — the flag Studio_RunBoneFlexDrivers branches on: a
    // bone-merged bone has no local space, so its matrix is recomputed.
    public const int BONE_USED_BY_BONE_MERGE = 0x00040000;
    public const int BONE_HAS_SAVEFRAME_POS = 0x00200000; // studio.h:436
    public const int BONE_HAS_SAVEFRAME_ROT = 0x00400000; // studio.h:437
    public const int BONE_USED_BY_HITBOX = 0x00000100;
    public const int BONE_USED_BY_ATTACHMENT = 0x00000200;
    public const int BONE_USED_BY_VERTEX_MASK = 0x0003FC00;
    public const int BONE_USED_MASK = 0x0007FF00;

    // bonecontroller types (studio.h:3058-3074) — CalcBoneAdj's switch.
    // The mask keeps LX..LINEAR inside too; CalcBoneAdj's switch has no arm
    // for them, so they take stock's silent default (no case, no default:
    // the bone is untouched).
    public const int STUDIO_X = 0x00000001;
    public const int STUDIO_Y = 0x00000002;
    public const int STUDIO_Z = 0x00000004;
    public const int STUDIO_XR = 0x00000008;
    public const int STUDIO_YR = 0x00000010;
    public const int STUDIO_ZR = 0x00000020;
    public const int STUDIO_LX = 0x00000040;
    public const int STUDIO_TYPES = 0x0003FFFF;

    // ik rule types (studio.h:522ff)
    public const int IK_SELF = 1;
    public const int IK_WORLD = 2;
    public const int IK_GROUND = 3;
    public const int IK_RELEASE = 4;
    public const int IK_ATTACHMENT = 5;
    public const int IK_UNLATCH = 6;

    // animvalue fixed-point scale: the stored short is value*255
    public const float AnimValueScale = 1.0f / 255.0f;
}

/// <summary>
/// The compressed-value codecs, line-by-line from compressed_vector.h: the
/// half-float decode (denormal and NaN/Inf arms included because stock data
/// carries them and goldens must not silently differ), and the 48/64-bit
/// quaternions' exact scaling constants with w from the unit constraint.
/// </summary>
public static class StudioCodec
{
    private const float Float16Max = 65504.0f;

    /// <summary>compressed_vector.h:461 <c>Convert16bitFloatTo32bits</c>.</summary>
    public static float HalfToSingle(ushort input)
    {
        int sign = (input >> 15) & 1;
        int exponent = (input >> 10) & 0x1F;
        int mantissa = input & 0x3FF;

        if (exponent == 31)
        {
            return mantissa == 0
                ? (sign == 1 ? -Float16Max : Float16Max) // IsInfinity
                : 0.0f;                                  // IsNaN
        }

        if (exponent == 0 && mantissa != 0)
        {
            // denorm: sgn * (mantissa/1024) * 2^-14
            const float halfDenorm = 1.0f / 16384.0f;
            float m = mantissa / 1024.0f;
            float sgn = sign != 0 ? -1.0f : 1.0f;
            return sgn * m * halfDenorm;
        }

        // regular: rebuilt bits; the exponent rebias is multiplied by
        // (biased_exponent != 0), stock's exact expression.
        int newExp = (exponent - 15 + 127) * (exponent != 0 ? 1 : 0);
        int bits = (mantissa << 13) | (newExp << 23) | (sign << 31);
        return BitConverter.Int32BitsToSingle(bits);
    }

    /// <summary>Vector48: three half-floats (compressed_vector.h:323).</summary>
    public static Vector ReadVector48(byte[] image, int at)
    {
        ushort x = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(at, 2));
        ushort y = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(at + 2, 2));
        ushort z = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(at + 4, 2));
        return new Vector(HalfToSingle(x), HalfToSingle(y), HalfToSingle(z));
    }

    /// <summary>
    /// Quaternion48::operator Quaternion: x,y 16-bit biased at 32768, z 15-bit
    /// at 16384, w = sqrt(1 - x^2 - y^2 - z^2) with the stored sign. The
    /// little-endian bitfields pack x[0:16) y[16:32) z[32:47) wneg[47].
    /// </summary>
    public static Quaternion ReadQuaternion48(byte[] image, int at)
    {
        uint lo = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at, 4));
        ushort zW = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(at + 4, 2));
        int x = (int)(lo & 0xFFFF);
        int y = (int)(lo >> 16);
        int z = zW & 0x7FFF;
        int wneg = (zW >> 15) & 1;

        float fx = (x - 32768) * (1.0f / 32768.0f);
        float fy = (y - 32768) * (1.0f / 32768.0f);
        float fz = (z - 16384) * (1.0f / 16384.0f);
        float w = MathF.Sqrt(1.0f - (fx * fx + fy * fy + fz * fz));
        if (wneg != 0)
        {
            w = -w;
        }

        return new Quaternion(fx, fy, fz, w);
    }

    /// <summary>
    /// Quaternion64::operator Quaternion: 21-bit channels biased at 1048576,
    /// scaled by 1/1048576.5f (stock's literal), w from the unit constraint.
    /// Bitfields pack x[0:21) y[21:42) z[42:63) wneg[63].
    /// </summary>
    public static Quaternion ReadQuaternion64(byte[] image, int at)
    {
        ulong raw = BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(at, 8));
        uint x = (uint)(raw & 0x1FFFFF);
        uint y = (uint)((raw >> 21) & 0x1FFFFF);
        uint z = (uint)((raw >> 42) & 0x1FFFFF);
        int wneg = (int)(raw >> 63);

        const float scale = 1.0f / 1048576.5f;
        float fx = ((int)x - 1048576) * scale;
        float fy = ((int)y - 1048576) * scale;
        float fz = ((int)z - 1048576) * scale;
        float w = MathF.Sqrt(1.0f - (fx * fx + fy * fy + fz * fz));
        if (wneg != 0)
        {
            w = -w;
        }

        return new Quaternion(fx, fy, fz, w);
    }
}
