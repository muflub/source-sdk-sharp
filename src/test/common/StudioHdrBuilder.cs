using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SourceSharp.Shared.Bones;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// The ONE place the managed bone tests know mdl byte layout. It assembles a
/// <c>studiohdr_t</c> image — header, bones, animdescs with their pNext anim
/// chains and valueptr streams, seqdescs, zeroframe rows, compressed IK
/// errors, linearbones, IK chains/locks, attachments, hitboxes, pose
/// parameters, anim blocks and the include table for the virtual-model fold —
/// exactly as the qc compiler lays it out, so the managed reader in
/// <c>SourceSharp.Shared.Bones</c> and the native stock code see the same
/// bytes.
///
/// <para>
/// Every record offset comes from the compiled sizeof/offsetof dump
/// (<c>docs/bones-offsets.txt</c>, built with bone_setup.cpp's own flags), not
/// from reading declarations. The compressed payloads (half-float rows, the
/// 48/64-bit quaternions) are written as the raw channel forms stock stores:
/// tests hand in channel ints or exactly-representable half-float values, so
/// a decode assertion is bit-exact, never "approximately a float".
/// </para>
///
/// <para>
/// Cluster tests express intent ("two bones, one anim, three frames"), never
/// offsets. A model whose bytes this builder cannot produce is a gap: throw,
/// do not fake. Tables whose records the reader walks in image order
/// (attachments, hitbox sets, ik links…) are laid out contiguously for
/// exactly that reason. Null <see cref="IkRuleDef.CompressedError"/> /
/// <see cref="LocalHierarchyDef.LocalAnim"/> emit offset 0 = stock's NULL
/// pointer; every non-zero sub-record index is relative to its owning
/// record, and valueptr stream offsets are relative to their table, matching
/// <c>pAnimvalue()</c>'s <c>(byte *)this + offset</c> exactly.
/// </para>
/// </summary>
public sealed partial class StudioHdrBuilder
{
    // ---- field defaults ----------------------------------------------------

    /// <summary>One <c>mstudiobone_t</c> (216 bytes).</summary>
    public sealed class BoneDef
    {
        public string Name = "";
        public int Parent = -1;
        public Vector Pos;
        public Quaternion Quat = Quaternion.Identity;
        public Vector Rot;
        public Vector PosScale = Vector.One;
        public Vector RotScale = Vector.One;
        public BoneMatrix3x4 PoseToBone = BoneMatrix3x4.Identity;
        public Quaternion QAlignment;
        public int Flags;
        public int SurfaceProp;
    }

    /// <summary>
    /// One <c>mstudioanim_t</c> in the pNext chain. Stream fields carry the
    /// raw shorts of one <c>mstudioanimvalue_t</c> array — index 0 is the
    /// packed (valid,total) header short, the rest the values — exactly what
    /// <c>pAnimvalue(0)</c> points at. Null = stock's NULL stream.
    /// </summary>
    public sealed class BoneAnimDef
    {
        public int Bone;
        public int Flags; // STUDIO_ANIM_* (StudioConstants)
        public short[]? Rot0;
        public short[]? Rot1;
        public short[]? Rot2;
        public short[]? Pos0;
        public short[]? Pos1;
        public short[]? Pos2;

        /// <summary>RAWROT (Quaternion48 channels) or RAWROT2 (Quaternion64
        /// channels) source; quantized on write.</summary>
        public Quaternion RawQuaternion;
        public Vector RawPosition;
    }

    /// <summary>One <c>mstudiomovement_t</c> (44 bytes).</summary>
    public sealed class MovementDef
    {
        public int EndFrame;
        public int MotionFlags;
        public float V0;
        public float V1;
        public float Angle;
        public Vector Vector;
        public Vector Position;
    }

    /// <summary>One <c>mstudiocompressedikerror_t</c> (36 bytes + streams
    /// after it, offsets relative to the record).</summary>
    public sealed class CompressedIkErrorDef
    {
        public float[] Scale = new float[6];
        public short[]?[] Streams = new short[]?[6]; // raw shorts, [0] = (valid,total)
    }

    /// <summary>One <c>mstudioikerror_t</c> (28 bytes): the uncompressed
    /// per-frame error arm.</summary>
    public sealed class IkErrorFrame
    {
        public Vector Pos;
        public Quaternion Quat = Quaternion.Identity;
    }

    /// <summary>One <c>mstudioikrule_t</c> (152 bytes).</summary>
    public sealed class IkRuleDef
    {
        public int Index;
        public int Type; // IK_SELF..IK_UNLATCH (StudioConstants)
        public int Chain;
        public int Bone;
        public int Slot;
        public float Height;
        public float Radius;
        public float Floor;
        public Vector Pos;
        public Quaternion Q = Quaternion.Identity;
        public int IStart;
        public float Start;
        public float Peak;
        public float Tail = 1.0f;
        public float End = 1.0f;
        public float Unused3; // @92: the stock field the ikrule reader skips
        public float Contact;
        public float Drop;
        public float Top;
        public string AttachmentName = "";
        public CompressedIkErrorDef? CompressedError;
        public IkErrorFrame[]? Errors; // uncompressed arm: one row per frame
    }

    /// <summary>One <c>mstudiolocalhierarchy_t</c> (48 bytes).</summary>
    public sealed class LocalHierarchyDef
    {
        public int IBone;
        public int INewParent = -1;
        public float Start;
        public float Peak;
        public float Tail = 1.0f;
        public float End = 1.0f;
        public int IStart;
        public CompressedIkErrorDef? LocalAnim;
    }

    /// <summary>
    /// One <c>mstudioanimdesc_t</c> (100 bytes) plus its payloads. The anim
    /// chain is placed after the record so <c>animblock == 0</c> resolves as
    /// stock does (<c>this + animindex</c>); <see cref="AnimIndex"/> lets a
    /// test pick the offset.
    /// </summary>
    public sealed class AnimDef
    {
        public string Name = "";
        public float Fps = 30.0f;
        public int Flags; // animdesc flags (StudioConstants.STUDIO_DELTA…)
        public int NumFrames = 1;
        public int AnimBlock; // 0 = inline; >0 = section/block tests
        public int? AnimIndex; // null = auto (chain start right after the record)
        public List<BoneAnimDef> Bones = [];
        public List<MovementDef> Movements = [];
        public List<IkRuleDef> IkRules = [];
        public List<LocalHierarchyDef> Hierarchy = [];
        public List<(int Block, int Index)> Sections = [];
        public int SectionFrames;

        /// <summary>zeroframe: one row list per SAVEFRAME bone, in the bone's
        /// own list order. <see cref="ZeroFrameCount"/> entries each; pos
        /// rows are half-exact floats, rot rows Quaternion64 channel-quats.</summary>
        public short ZeroFrameSpan = 1;
        public short ZeroFrameCount;
        public Dictionary<int, List<Vector>> ZeroFramePos = [];
        public Dictionary<int, List<Quaternion>> ZeroFrameRot = [];
    }

    /// <summary>One <c>mstudioautolayer_t</c> (24 bytes).</summary>
    public sealed class AutoLayerDef
    {
        public ushort ISequence;
        public ushort IPose;
        public int Flags;
        public float Start;
        public float Peak;
        public float Tail = 1.0f;
        public float End = 1.0f;
    }

    /// <summary>One <c>mstudioiklock_t</c> (32 bytes).</summary>
    public sealed class IkLockDef
    {
        public int Chain;
        public float FlPosWeight = 1.0f;
        public float FlLocalQWeight = 1.0f;
        public int Flags;
    }

    /// <summary>One <c>mstudioevent_t</c> (80 bytes).</summary>
    public sealed class EventDef
    {
        public float Cycle;
        public int Event;
        public int Type;

        /// <summary>The inline <c>options[64]</c>, NUL-terminated.</summary>
        public string Options = "";

        /// <summary><c>szeventindex</c> (@76); empty leaves the index 0.</summary>
        public string Name = "";
    }

    /// <summary>One <c>mstudioseqdesc_t</c> (212 bytes) plus payloads.</summary>
    public sealed class SeqDef
    {
        public string Label = "";

        /// <summary><c>szactivitynameindex</c> (@8); empty leaves the index 0.</summary>
        public string ActivityName = "";
        public int Flags;
        public int Activity;
        public int ActWeight;
        public int NumBlends = 1;

        /// <summary>The event table (<c>numevents</c> @24 / <c>eventindex</c> @28).</summary>
        public List<EventDef> Events = [];

        /// <summary><c>mstudioseqdesc_t.cycleposeindex</c> (@172): pose-parameter
        /// index used as the cycle index under <c>STUDIO_CYCLEPOSE</c>.</summary>
        public int CyclePoseIndex;
        public int GroupSize0 = 1;
        public int GroupSize1 = 1;

        /// <summary>The <c>anim()</c> blend table, row-major (y*GroupSize0+x),
        /// shorts.</summary>
        public short[] AnimTable = [0];

        /// <summary>Per-bone weight list (numbones floats).</summary>
        public float[]? Weights;

        public int ParamIndex0 = -1;
        public int ParamIndex1 = -1;
        public float[] ParamStart = new float[2];
        public float[] ParamEnd = [1.0f, 1.0f];
        public int ParamParent = -1;
        public float FadeInTime;
        public float FadeOutTime;
        public int LocalEntryNode;
        public int LocalExitNode;
        public int NodeFlags;
        public float EntryPhase;
        public float ExitPhase;
        public float LastFrame;
        public int NextSeq = -1;
        public int Pose;
        public List<AutoLayerDef> AutoLayers = [];
        public List<IkLockDef> IkLocks = [];
        public List<IkRuleDef> IkRules = [];
        public List<MovementDef> Movements = [];

        /// <summary>The posekey table as stock lays it: a FLAT float array
        /// walked as <c>iParam * groupsize[0] + iAnim</c> (studio.h:881).</summary>
        public List<float> PoseKeys = [];
    }

    /// <summary>One <c>mstudioikchain_t</c> (16 bytes) + its link table.</summary>
    public sealed class IkChainDef
    {
        public string Name = "";
        public int LinkType;
        public List<(int Bone, Vector KneeDir)> Links = [];
    }

    /// <summary>One <c>mstudioposeparamdesc_t</c> (20 bytes).</summary>
    public sealed class PoseParamDef
    {
        public string Name = "";
        public int Flags;
        public float Start;
        public float End = 1.0f;
        public float Loop;
    }

    /// <summary>One <c>mstudiobonecontroller_t</c> (56 bytes): the CalcBoneAdj
    /// record. The bone record's own <c>bonecontroller[6]</c> stays zero —
    /// stock reads the hdr table, not that field.</summary>
    public sealed class ControllerDef
    {
        public int Bone;
        public int Type; // STUDIO_X..STUDIO_ZR (StudioConstants)
        public float Start;
        public float End = 1.0f;
        public int Rest;
        public int InputField;
    }

    /// <summary>One <c>mstudioattachment_t</c> (92 bytes).</summary>
    public sealed class AttachmentDef
    {
        public string Name = "";
        public int Flags;
        public int LocalBone;
        public BoneMatrix3x4 Local = BoneMatrix3x4.Identity;
    }

    /// <summary>One <c>mstudiohitboxset_t</c> (12 bytes) + its boxes.</summary>
    public sealed class HitboxSetDef
    {
        public string Name = "";
        public List<HitboxDef> Boxes = [];
    }

    /// <summary>One <c>mstudiobbox_t</c> (68 bytes).</summary>
    public sealed class HitboxDef
    {
        public int Bone;
        public int Group;
        public Vector Min;
        public Vector Max;
        public string? Name;
        public int SurfaceProp; // stored in bbox unused[0] (@36): the port
                                // answers physics from the hitbox's own prop
                                // like Studio_GetDefaultSurfaceProps does
    }

    /// <summary>One <c>mstudiomodelgroup_t</c> (8 bytes) — fold include.</summary>
    public sealed class IncludeDef
    {
        public string Label = "";
        public string Name = "";
    }

    // ---- the model being built ---------------------------------------------

    public string ModelName { get; set; } = "test";
    public int HdrFlags { get; set; }
    public float Mass { get; set; }
    public int Contents { get; set; }
    public int SurfaceProp { get; set; }
    public int Version { get; set; } = StudioHdr.StudioVersion;

    public List<BoneDef> Bones { get; } = [];
    public List<AnimDef> Anims { get; } = [];
    public List<SeqDef> Sequences { get; } = [];
    public List<IkChainDef> IkChains { get; } = [];
    public List<PoseParamDef> PoseParams { get; } = [];
    public List<AttachmentDef> Attachments { get; } = [];
    public List<HitboxSetDef> HitboxSets { get; } = [];
    public List<IkLockDef> AutoPlayLocks { get; } = [];
    public List<ControllerDef> Controllers { get; } = [];
    public List<IncludeDef> Includes { get; } = [];
    public List<string> NodeNames { get; } = [];

    /// <summary>Anim blocks for sectioned anims: (datastart, dataend) pairs
    /// filled by <see cref="Build"/> when an anim declares
    /// <c>AnimBlock &gt; 0</c> — the chain bytes then live inside one block.</summary>
    public List<(int DataStart, int DataEnd)> AnimBlocks { get; } = [];

    /// <summary>Emit the SoA <c>mstudiolinearbone_t</c> mirror of
    /// <see cref="Bones"/> and point studiohdr2 at it.</summary>
    public bool WithLinearBones { get; set; }

    private sealed record PendingString(int FieldAddr, int RecordBase, string Value);

    private readonly List<PendingString> _strings = [];
    private readonly Dictionary<string, int> _stringAddrs = [];
    private readonly MemoryStream _image = new();
    private readonly Dictionary<string, byte[]> _resolvedIncludes = new(StringComparer.Ordinal);

    /// <summary>Register the image an include name resolves to (the fold's
    /// <see cref="StudioHdr.IncludeResolver"/> hands these back).</summary>
    public void AddIncludeImage(string name, byte[] image) => _resolvedIncludes[name] = image;

    /// <summary>The resolver that serves <see cref="AddIncludeImage"/> bytes
    /// to <c>StudioHdr.TryCreate</c>.</summary>
    public StudioHdr.IncludeResolver Resolver =>
        name => _resolvedIncludes.TryGetValue(name, out byte[]? b) ? b : null;

    // ---- primitive writers --------------------------------------------------

    private int Mark() => (int)_image.Length;

    private void Pad(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _image.WriteByte(0);
        }
    }

    private void PadTo(int align)
    {
        while (_image.Length % align != 0)
        {
            _image.WriteByte(0);
        }
    }

    private void W8(int v) => _image.WriteByte((byte)v);
    private void W16(int v) => SpanWriter.WriteInt16(_image, (short)v);
    private void W32(int v) => SpanWriter.WriteInt32(_image, v);
    private void WF(float v) => SpanWriter.WriteSingle(_image, v);

    private void WVec(Vector v)
    {
        WF(v.X);
        WF(v.Y);
        WF(v.Z);
    }

    private void WVec4(Vector4 v)
    {
        WF(v.X);
        WF(v.Y);
        WF(v.Z);
        WF(v.W);
    }

    private void WQuat(Quaternion q)
    {
        WF(q.X);
        WF(q.Y);
        WF(q.Z);
        WF(q.W);
    }

    private void WMatrix3x4(in BoneMatrix3x4 m)
    {
        // matrix3x4_t is 12 floats: each row is XYZ + W (the translation column).
        WVec4(m.Row0);
        WVec4(m.Row1);
        WVec4(m.Row2);
    }

    private void Patch32(int addr, int value) => SpanWriter.PatchInt32(_image, addr, value);

    // ---- compressed codecs (writer side; the reader side lives in
    //      StudioCodec and is pinned by its own facts) ------------------------

    /// <summary>Exact float -&gt; half bits: throws unless the value round-trips
    /// through <see cref="StudioCodec.HalfToSingle"/>, so an assertion against
    /// a half-stored value is bit-exact or the builder says so.</summary>
    public static ushort HalfBits(float v)
    {
        // IEEE-754 binary16 round-to-nearest-even, finite normals only.
        uint bits = (uint)BitConverter.SingleToInt32Bits(v);
        int sign = (int)(bits >> 31);
        int exp = (int)((bits >> 23) & 0xFF) - 127 + 15;
        uint frac = bits & 0x7FFFFF;

        if ((bits & 0x7FFFFFFF) == 0)
        {
            return (ushort)(sign << 15); // ±0
        }

        ushort half;
        if (exp >= 31)
        {
            throw new ArgumentException($"value {v} is not half-representable (inf/nan range)");
        }

        if (exp <= 0)
        {
            throw new ArgumentException($"value {v} is a denormal/underflow for float16");
        }

        if ((frac & 0x1FFF) != 0)
        {
            throw new ArgumentException($"value {v} is not exactly half-representable");
        }

        half = (ushort)((sign << 15) | (exp << 10) | (int)(frac >> 13));
        if (StudioCodec.HalfToSingle(half) != v)
        {
            throw new ArgumentException($"value {v} does not round-trip through the stock half decode");
        }

        return half;
    }

    /// <summary>Write one Vector48 row (three half words).</summary>
    private void WVector48(Vector v)
    {
        W16(HalfBits(v.X));
        W16(HalfBits(v.Y));
        W16(HalfBits(v.Z));
    }

    /// <summary>Write one Quaternion48 (x,y at 32768, z at 16384, w sign).</summary>
    private void WQuaternion48(Quaternion q)
    {
        int x = Quantize(q.X, 32768, 65535);
        int y = Quantize(q.Y, 32768, 65535);
        int z = Quantize(q.Z, 16384, 32767);
        int wneg = q.W < 0 ? 1 : 0;
        uint lo = (uint)(x | (y << 16));
        SpanWriter.WriteUInt32(_image, lo);
        W16(z | (wneg << 15));
    }

    /// <summary>Write one Quaternion64 (21-bit channels at 1048576).</summary>
    private void WQuaternion64(Quaternion q)
    {
        ulong x = (ulong)Quantize(q.X, 1048576, 2097151);
        ulong y = (ulong)Quantize(q.Y, 1048576, 2097151);
        ulong z = (ulong)Quantize(q.Z, 1048576, 2097151);
        ulong wneg = q.W < 0 ? 1UL << 63 : 0;
        SpanWriter.WriteUInt64(_image, x | (y << 21) | (z << 42) | wneg);
    }

    private static int Quantize(float v, int bias, int max)
    {
        long c = (long)Math.Round(v * bias) + bias;
        if (c < 0 || c > max)
        {
            throw new ArgumentException($"component {v} does not fit the compressed channel [-1,1]");
        }

        return (int)c;
    }

    // ---- assembly ------------------------------------------------------------

    /// <summary>Assemble the image. <see cref="StudioHdr.TryCreate"/> must
    /// accept the result for every declared feature.</summary>
    public byte[] Build()
    {
        _image.SetLength(0);
        _image.Position = 0;
        _strings.Clear();
        _stringAddrs.Clear();
        AnimBlocks.Clear();

        if (Bones.Count == 0)
        {
            throw new InvalidOperationException("a model with no bones is not what the port tests");
        }

        if (Bones.Count > StudioConstants.MAXSTUDIOBONES)
        {
            throw new ArgumentException($"{Bones.Count} bones exceeds MAXSTUDIOBONES");
        }

        for (int i = 0; i < Bones.Count; i++)
        {
            if (Bones[i].Parent >= i)
            {
                throw new ArgumentException(
                    $"bone {i} parent {Bones[i].Parent} is not earlier: the pNext chain and BuildBoneChain assume parents precede");
            }
        }

        // ---- header (408) — fields patched after their tables are placed
        Pad(408);

        // ---- strings live at the tail; every name field is patched from the
        //      PendingString list once both addresses are known.
        // ---- bones
        int boneIndex = Mark();
        foreach (BoneDef b in Bones)
        {
            int at = Mark();
            W32(0);                               // sznameindex @0 — patched from the QueueString list
            QueueString(at, 0, b.Name);
            W32(b.Parent);                        // parent @4
            Pad(24);                              // bonecontroller[6] @8
            WVec(b.Pos);                          // pos @32
            WQuat(b.Quat);                        // quat @44
            WVec(b.Rot);                          // rot @60
            WVec(b.PosScale);                     // posscale @72
            WVec(b.RotScale);                     // rotscale @84
            WMatrix3x4(b.PoseToBone);             // poseToBone @96
            WQuat(b.QAlignment);                  // qAlignment @144
            W32(b.Flags);                         // flags @160
            Pad(4 + 4 + 4);                       // proctype/procindex/physicsbone @164..175
            W32(b.SurfaceProp);                   // surfacepropidx @176
            Pad(4 + 32);                          // contents @180 + unused[8]
            if (Mark() != at + 216)
            {
                throw new InvalidOperationException($"bone record emitted {Mark() - at} bytes, not 216");
            }
        }

        Patch32(156, Bones.Count);
        Patch32(160, boneIndex);

        // ---- animdescs: the PIKAnimDesc table walks a 100-byte stride
        //      (LocalAnimdesc), so ALL records are laid down contiguously in
        //      pass 1; every payload (movements, pNext chain, ikrules,
        //      hierarchy, sections, zeroframe) is emitted in pass 2 and
        //      patched by record-relative index — same rule as ikrules.
        int animIndex = -1;
        if (Anims.Count > 0)
        {
            PadTo(4);
            animIndex = Mark();
            var animBases = new List<int>(Anims.Count);
            foreach (AnimDef a in Anims)
            {
                int at = Mark();
                Pad(100); // fixed record; payload fields patched in pass 2
                QueueString(at, 4, a.Name); // sznameindex @4, record-relative (studio.h pszName)
                SpanWriter.PatchSingle(_image, at + 8, a.Fps);
                SpanWriter.PatchInt32(_image, at + 12, a.Flags);
                SpanWriter.PatchInt32(_image, at + 16, a.NumFrames);
                if (Mark() != at + 100)
                {
                    throw new InvalidOperationException($"animdesc record emitted {Mark() - at} bytes, not 100");
                }

                animBases.Add(at);
            }

            for (int i = 0; i < Anims.Count; i++)
            {
                AnimDef a = Anims[i];
                int at = animBases[i];

                if (a.Movements.Count > 0)
                {
                    PadTo(4);
                    int mov = Mark();
                    foreach (MovementDef m in a.Movements)
                    {
                        W32(m.EndFrame);
                        W32(m.MotionFlags);
                        WF(m.V0);
                        WF(m.V1);
                        WF(m.Angle);
                        WVec(m.Vector);
                        WVec(m.Position);
                    }

                    SpanWriter.PatchInt32(_image, at + 20, a.Movements.Count);
                    SpanWriter.PatchInt32(_image, at + 24, mov - at);
                }

                // pNext chain: animblock == 0 resolves as this + animindex
                // (stock's PAnimBlock), so animindex is chainStart - at.
                if (a.Bones.Count > 0)
                {
                    PadTo(4);
                    int chainStart = Mark();
                    EmitAnimChain(a, chainStart);
                    SpanWriter.PatchInt32(_image, at + 56, a.AnimIndex ?? chainStart - at);
                }

                SpanWriter.PatchInt32(_image, at + 52, a.AnimBlock);

                if (a.IkRules.Count > 0)
                {
                    PadTo(4);
                    int ikr = Mark();
                    foreach (IkRuleDef r in a.IkRules)
                    {
                        EmitIkRule(r, at);
                    }

                    SpanWriter.PatchInt32(_image, at + 60, a.IkRules.Count);
                    SpanWriter.PatchInt32(_image, at + 64, ikr - at);
                }

                if (a.Hierarchy.Count > 0)
                {
                    PadTo(4);
                    int lh = Mark();
                    foreach (LocalHierarchyDef h in a.Hierarchy)
                    {
                        EmitLocalHierarchy(h, at);
                    }

                    SpanWriter.PatchInt32(_image, at + 72, a.Hierarchy.Count);
                    SpanWriter.PatchInt32(_image, at + 76, lh - at);
                }

                if (a.Sections.Count > 0)
                {
                    PadTo(4);
                    int sec = Mark();
                    foreach ((int blk, int idx) in a.Sections)
                    {
                        W32(blk);
                        W32(idx);
                    }

                    SpanWriter.PatchInt32(_image, at + 80, sec - at);
                    SpanWriter.PatchInt32(_image, at + 84, a.SectionFrames);
                }

                if (a.ZeroFrameCount != 0)
                {
                    PadTo(4);
                    EmitZeroframe(a, at);
                }
            }

            Patch32(180, Anims.Count);
            Patch32(184, animIndex);
        }

        // ---- seqdescs: the localseq table walks a 212-byte stride
        //      (LocalSeqdesc), so pass 1 lays the records down contiguously
        //      and pass 2 emits every payload (anim table, weights,
        //      autolayers, iklocks, ikrules, posekeys, movements) and patches
        //      the record-relative indices — same rule as animdescs/ikrules.
        if (Sequences.Count > 0)
        {
            PadTo(4);
            int seqIndex = Mark();
            var seqBases = new List<int>(Sequences.Count);
            foreach (SeqDef s in Sequences)
            {
                int at = Mark();
                Pad(212); // fixed record; payload fields patched in pass 2
                QueueString(at, 4, s.Label); // szlabelindex @4, record-relative
                if (s.ActivityName.Length > 0)
                {
                    QueueString(at, 8, s.ActivityName); // szactivitynameindex @8
                }

                SpanWriter.PatchInt32(_image, at + 12, s.Flags);
                SpanWriter.PatchInt32(_image, at + 16, s.Activity);
                SpanWriter.PatchInt32(_image, at + 20, s.ActWeight);
                SpanWriter.PatchInt32(_image, at + 56, s.NumBlends);
                SpanWriter.PatchInt32(_image, at + 68, s.GroupSize0);
                SpanWriter.PatchInt32(_image, at + 72, s.GroupSize1);
                SpanWriter.PatchInt32(_image, at + 76, s.ParamIndex0);
                SpanWriter.PatchInt32(_image, at + 80, s.ParamIndex1);
                for (int i = 0; i < 2; i++)
                {
                    SpanWriter.PatchSingle(_image, at + 84 + 4 * i, s.ParamStart[i]);
                    SpanWriter.PatchSingle(_image, at + 92 + 4 * i, s.ParamEnd[i]);
                }

                SpanWriter.PatchInt32(_image, at + 100, s.ParamParent); // scalar int
                SpanWriter.PatchSingle(_image, at + 104, s.FadeInTime);
                SpanWriter.PatchSingle(_image, at + 108, s.FadeOutTime);
                SpanWriter.PatchInt32(_image, at + 112, s.LocalEntryNode);
                SpanWriter.PatchInt32(_image, at + 116, s.LocalExitNode);
                SpanWriter.PatchInt32(_image, at + 120, s.NodeFlags);
                SpanWriter.PatchSingle(_image, at + 124, s.EntryPhase);
                SpanWriter.PatchSingle(_image, at + 128, s.ExitPhase);
                SpanWriter.PatchSingle(_image, at + 132, s.LastFrame);
                SpanWriter.PatchInt32(_image, at + 136, s.NextSeq);
                SpanWriter.PatchInt32(_image, at + 140, s.Pose);
                SpanWriter.PatchInt32(_image, at + 180, s.CyclePoseIndex); // @180 (after keyvalueindex/keyvaluesize)
                if (Mark() != at + 212)
                {
                    throw new InvalidOperationException($"seqdesc record emitted {Mark() - at} bytes, not 212");
                }

                seqBases.Add(at);
            }

            for (int i = 0; i < Sequences.Count; i++)
            {
                EmitSeqDescPayload(Sequences[i], seqBases[i]);
            }

            Patch32(188, Sequences.Count);
            Patch32(192, seqIndex);
        }

        // ---- attachments (contiguous; the reader walks them by index)
        if (Attachments.Count > 0)
        {
            PadTo(4);
            int att = Mark();
            foreach (AttachmentDef a in Attachments)
            {
                int at = Mark();
                W32(0);              // sznameindex @0 — patched from the QueueString list
                QueueString(at, 0, a.Name);
                W32(a.Flags);        // flags @4 (unsigned int, dump)
                W32(a.LocalBone);    // localbone @8
                WMatrix3x4(a.Local); // local @12 (48)
                Pad(32);             // unused[8] @60..91
                if (Mark() != at + 92)
                {
                    throw new InvalidOperationException($"attachment record emitted {Mark() - at} bytes, not 92");
                }
            }

            Patch32(240, Attachments.Count);
            Patch32(244, att);
        }

        // ---- nodes: mstudiohdr_t.localnodeindex is the transition table,
        //      numlocalnodes * numlocalnodes BYTES (studio.h:2267
        //      pLocalTransition), followed by the name table (4-byte indices
        //      at localnodenameindex, studio.h:2266) — no PadTo between them.
        if (NodeNames.Count > 0)
        {
            PadTo(4);
            int nodes = Mark();
            Pad(NodeNames.Count * NodeNames.Count);
            Patch32(248, NodeNames.Count);
            Patch32(252, nodes);
            // Name table: header-based indices (NodeName reads
            // ReadI32(image, LocalNodeNameIndex + 4*i), then CStringAt(image,
            // 0, idx)), patched at the tail once the strings are placed.
            int names = Mark();
            foreach (string n in NodeNames)
            {
                QueueString(0, Mark(), n);
                W32(0);
            }

            Patch32(256, names);
        }

        // ---- hitbox sets (contiguous; boxes after the set records — the
        //      reader walks HitboxIndex relative to the set record)
        if (HitboxSets.Count > 0)
        {
            PadTo(4);
            int sets = Mark();
            Pad(12 * HitboxSets.Count);
            var boxAt = new int[HitboxSets.Count];
            for (int i = 0; i < HitboxSets.Count; i++)
            {
                PadTo(4);
                boxAt[i] = Mark();
                foreach (HitboxDef h in HitboxSets[i].Boxes)
                {
                    int at = Mark();
                    W32(h.Bone);
                    W32(h.Group);
                    WVec(h.Min);  // bbmin @8
                    WVec(h.Max);  // bbmax @20
                    W32(0);       // szhitboxnameindex @32 — patched from the QueueString list
                    if (h.Name is not null)
                    {
                        QueueString(at, 32, h.Name);
                    }

                    W32(h.SurfaceProp); // unused[0] @36 — see HitboxDef
                    Pad(4 * 7);         // unused[1..7] @40..67
                }
            }

            for (int i = 0; i < HitboxSets.Count; i++)
            {
                int at = sets + 12 * i;
                QueueString(at, 0, HitboxSets[i].Name); // sznameindex @0
                SpanWriter.PatchInt32(_image, at + 4, HitboxSets[i].Boxes.Count);
                SpanWriter.PatchInt32(_image, at + 8, boxAt[i] - at); // relative
            }

            Patch32(172, HitboxSets.Count);
            Patch32(176, sets);
        }

        // ---- ik chains + link tables
        if (IkChains.Count > 0)
        {
            PadTo(4);
            int chains = Mark();
            Pad(16 * IkChains.Count);
            int links = Mark();
            foreach (IkChainDef c in IkChains)
            {
                foreach ((int bone, Vector kneeDir) in c.Links)
                {
                    W32(bone);       // bone
                    WVec(kneeDir);   // kneeDir @4
                    Pad(12);         // unused0 — mstudioiklink_t is 28 bytes
                }
            }

            for (int i = 0; i < IkChains.Count; i++)
            {
                int at = chains + 16 * i;
                QueueString(at, 0, IkChains[i].Name);
                SpanWriter.PatchInt32(_image, at + 4, IkChains[i].LinkType);
                SpanWriter.PatchInt32(_image, at + 8, IkChains[i].Links.Count);
                SpanWriter.PatchInt32(_image, at + 12, links - at); // linkindex relative
            }

            Patch32(284, IkChains.Count);
            Patch32(288, chains);
        }

        // ---- pose parameters
        if (PoseParams.Count > 0)
        {
            PadTo(4);
            int pp = Mark();
            foreach (PoseParamDef p in PoseParams)
            {
                int at = Mark();
                W32(0);                      // sznameindex @0 — patched from the QueueString list
                QueueString(at, 0, p.Name); // sznameindex @0
                W32(p.Flags);                // flags @4
                WF(p.Start);                 // start @8
                WF(p.End);                   // end @12
                WF(p.Loop);                  // loop @16
            }

            Patch32(300, PoseParams.Count);
            Patch32(304, pp);
        }

        // ---- bone controllers (mstudiobonecontroller_t, 56; the
        //      CalcBoneAdj table at hdr @164/@168)
        if (Controllers.Count > 0)
        {
            PadTo(4);
            int bc = Mark();
            foreach (ControllerDef c in Controllers)
            {
                W32(c.Bone);      // bone @0
                W32(c.Type);      // type @4
                WF(c.Start);      // start @8
                WF(c.End);        // end @12
                W32(c.Rest);      // rest @16
                W32(c.InputField); // inputfield @20
                Pad(56 - 24);     // output @24 + unused[8]
            }

            Patch32(164, Controllers.Count);
            Patch32(168, bc);
        }

        // ---- autoplay locks (mstudioiklock_t, 32)
        if (AutoPlayLocks.Count > 0)
        {
            PadTo(4);
            int locks = Mark();
            foreach (IkLockDef l in AutoPlayLocks)
            {
                W32(l.Chain);
                WF(l.FlPosWeight);
                WF(l.FlLocalQWeight);
                W32(l.Flags);
                Pad(32 - 16);
            }

            Patch32(320, AutoPlayLocks.Count);
            Patch32(324, locks);
        }

        // ---- anim blocks table (mstudioanimblock_t, 8; start/end absolute)
        if (AnimBlocks.Count > 0)
        {
            PadTo(4);
            int blocks = Mark();
            Pad(8 * AnimBlocks.Count);
            for (int i = 0; i < AnimBlocks.Count; i++)
            {
                Patch32(blocks + 8 * i, AnimBlocks[i].DataStart);
                Patch32(blocks + 8 * i + 4, AnimBlocks[i].DataEnd);
            }

            Patch32(352, AnimBlocks.Count);
            Patch32(356, blocks);
        }

        // ---- studiohdr2 (256) + linearbones mirror
        if (WithLinearBones)
        {
            PadTo(4);
            int hdr2 = Mark();
            Pad(256);
            SpanWriter.PatchInt32(_image, hdr2, 256); // numhdr2fields
            int lb = Mark();
            Pad(64);
            int n = Bones.Count;
            void Soa(int field, int stride, Action<BoneDef, int> write)
            {
                PadTo(4);
                SpanWriter.PatchInt32(_image, lb + field, Mark() - lb); // relative to the record
                foreach ((BoneDef b, int i) in EnumerateIndex(Bones))
                {
                    write(b, i);
                }
            }

            Soa(4, 4, (b, _) => W32(b.Flags));
            Soa(8, 4, (b, _) => W32(b.Parent));
            Soa(12, 12, (b, _) => WVec(b.Pos));
            Soa(16, 16, (b, _) => WQuat(b.Quat));
            Soa(20, 12, (b, _) => WVec(b.Rot));
            Soa(24, 48, (b, _) => WMatrix3x4(b.PoseToBone));
            Soa(28, 12, (b, _) => WVec(b.PosScale));
            Soa(32, 12, (b, _) => WVec(b.RotScale));
            Soa(36, 16, (b, _) => WQuat(b.QAlignment));
            _ = stride0; // (stride is documentation here; layouts are fixed above)
            SpanWriter.PatchInt32(_image, lb, n);
            SpanWriter.PatchInt32(_image, hdr2 + 16, lb - hdr2); // linearboneindex RELATIVE to hdr2
            Patch32(400, hdr2);
        }

        // ---- includes (mstudiomodelgroup_t, 8; indices relative to the group record)
        if (Includes.Count > 0)
        {
            PadTo(4);
            int groups = Mark();
            foreach (IncludeDef g in Includes)
            {
                int at = Mark();
                W32(0); // szlabelindex @0 — patched from the QueueString list
                W32(0); // sznameindex @4 — patched from the QueueString list
                QueueString(at, 0, g.Label);
                QueueString(at, 4, g.Name);
                if (Mark() != at + 8)
                {
                    throw new InvalidOperationException($"modelgroup record emitted {Mark() - at} bytes, not 8");
                }
            }

            Patch32(336, Includes.Count);
            Patch32(340, groups);
        }

        // ---- deferred ikrule payloads (compressed errors, ikerror frames):
        //      written after every fixed-size record table so the 152-byte
        //      PIKRule stride stays intact.
        FlushIkRulePayloads();
        FlushHierarchyPayloads();

        // ---- strings at the tail
        PadTo(4);
        foreach (string s in _orderedStrings)
        {
            _stringAddrs[s] = Mark();
            // UTF-8, matching StudioHdr.CString's decoder; byte-identical to
            // qc's output for the ASCII names every other fixture carries.
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(s))
            {
                _image.WriteByte(b);
            }

            _image.WriteByte(0);
        }

        // ---- patch name fields now the string addresses exist
        foreach (PendingString ps in _strings)
        {
            Patch32(ps.FieldAddr, StringAddr(ps.Value) - ps.RecordBase);
        }

        // ---- header constants
        Patch32(0, StudioHdr.Id);
        Patch32(4, Version);
        PatchAscii(12, ModelName, 64); // studiohdr_t.name is an inline char[64], not an index
        Patch32(76, (int)_image.Length);   // length
        Patch32(152, HdrFlags);
        Patch32(308, SurfaceProp);
        SpanWriter.PatchSingle(_image, 328, Mass);
        Patch32(332, Contents);

        return _image.ToArray();
    }

    // ---- record emitters ------------------------------------------------------

    private void EmitAnimChain(AnimDef a, int chainStart)
    {
        // Ascending bone order: the CalcAnimation walk consumes the chain in
        // pNext order against i = 0..numbones.
        int prev = -1;
        int recAt = chainStart;
        var recs = new List<(int At, int Size)>();
        foreach (BoneAnimDef ba in a.Bones)
        {
            if (prev >= 0 && ba.Bone <= prev)
            {
                throw new ArgumentException("BoneAnimDef list must ascend by bone: the pNext walk is ordered");
            }

            prev = ba.Bone;
        }

        foreach ((BoneAnimDef ba, int n) in EnumerateIndex(a.Bones))
        {
            int at = Mark();
            W8(ba.Bone);
            W8(ba.Flags);
            W16(1); // nextoffset placeholder — patched after the layout is final (non-last only)
            int pData = Mark(); // == at + 4

            int rotTable = -1;
            int posTable = -1;

            if ((ba.Flags & StudioConstants.STUDIO_ANIM_ANIMROT) != 0)
            {
                rotTable = Mark();
                Pad(6); // three short offsets, patched below
                EmitStream(ba.Rot0, rotTable);
                EmitStream(ba.Rot1, rotTable);
                EmitStream(ba.Rot2, rotTable);
            }

            if ((ba.Flags & StudioConstants.STUDIO_ANIM_ANIMPOS) != 0)
            {
                posTable = Mark();
                Pad(6);
                EmitStream(ba.Pos0, posTable);
                EmitStream(ba.Pos1, posTable);
                EmitStream(ba.Pos2, posTable);
            }

            if ((ba.Flags & StudioConstants.STUDIO_ANIM_RAWROT) != 0)
            {
                WQuaternion48(ba.RawQuaternion);
            }

            if ((ba.Flags & StudioConstants.STUDIO_ANIM_RAWROT2) != 0)
            {
                WQuaternion64(ba.RawQuaternion);
            }

            if ((ba.Flags & StudioConstants.STUDIO_ANIM_RAWPOS) != 0)
            {
                WVector48(ba.RawPosition);
            }

            if (rotTable >= 0)
            {
                PatchStreamOffsets(ba.Rot0, ba.Rot1, ba.Rot2, rotTable);
            }

            if (posTable >= 0)
            {
                PatchStreamOffsets(ba.Pos0, ba.Pos1, ba.Pos2, posTable);
            }

            _ = pData;
            if (Mark() - at > short.MaxValue)
            {
                throw new InvalidOperationException("anim record payload exceeds the short nextoffset");
            }

            recs.Add((at, Mark() - at));
        }

        // nextoffset: i-th record points at the (i+1)-th; the last is 0.
        for (int i = 0; i < recs.Count - 1; i++)
        {
            SpanWriter.PatchInt16(_image, recs[i].At + 2, (short)(recs[i + 1].At - recs[i].At));
        }

        SpanWriter.PatchInt16(_image, recs[^1].At + 2, 0);
    }

    private void EmitStream(short[]? stream, int tableAt)
    {
        if (stream is null)
        {
            return;
        }

        PadTo(2);
        int at = Mark();
        foreach (short v in stream)
        {
            W16(v);
        }

        if (!_streamEmits.TryGetValue(tableAt, out List<int>? list))
        {
            list = [];
            _streamEmits[tableAt] = list;
        }

        list.Add(at);
    }

    private readonly Dictionary<int, List<(int Idx, int At)>> _pendingStreamOffsets = [];

    private void PatchStreamOffsets(short[]? s0, short[]? s1, short[]? s2, int tableAt)
    {
        // Re-walk in emission order: EmitStream appended exactly the non-null
        // streams; their addresses were captured in _streamEmits keyed by table.
        short[]?[] ss = [s0, s1, s2];
        var ems = _streamEmits[tableAt];
        int e = 0;
        for (int i = 0; i < 3; i++)
        {
            if (ss[i] is null)
            {
                continue;
            }

            SpanWriter.PatchInt16(_image, tableAt + 2 * i, (short)(ems[e++] - tableAt));
        }

        if (e != ems.Count)
        {
            throw new InvalidOperationException("stream emission bookkeeping drifted");
        }
    }

    private readonly Dictionary<int, List<int>> _streamEmits = [];

    private void EmitIkRule(IkRuleDef r, int animBase)
    {
        // The fixed mstudioikrule_t is 152 bytes and the PIKRule table walks a
        // 152-byte stride, so the record is written as pure record bytes and
        // the payloads (compressed error, ikerror frames) are deferred to the
        // Build() tail — inline payloads would shift every field past them.
        int at = Mark();
        W32(r.Index);          // index @0
        W32(r.Type);           // type @4
        W32(r.Chain);          // chain @8
        W32(r.Bone);           // bone @12
        W32(r.Slot);           // slot @16
        WF(r.Height);          // height @20
        WF(r.Radius);          // radius @24
        WF(r.Floor);           // floor @28
        WVec(r.Pos);           // pos @32
        WQuat(r.Q);            // q @44
        W32(0);                // compressedikerrorindex @60 (payload-deferred patch)
        W32(0);                // unused2 @64
        W32(r.IStart);         // iStart @68
        W32(0);                // ikerrorindex @72 (payload-deferred patch)
        WF(r.Start);           // start @76
        WF(r.Peak);            // peak @80
        WF(r.Tail);            // tail @84
        WF(r.End);             // end @88
        WF(r.Unused3);         // unused3 @92
        WF(r.Contact);         // contact @96
        WF(r.Drop);            // drop @100
        WF(r.Top);             // top @104
        Pad(4 + 4 + 4);        // unused6/7/8 @108..119
        W32(0);                // szattachmentindex @120 — patched from the QueueString list
        if (r.AttachmentName.Length > 0)
        {
            QueueString(at, 120, r.AttachmentName);
        }

        Pad(7 * 4);            // unused[7] @124..151
        if (Mark() != at + 152)
        {
            throw new InvalidOperationException($"ikrule record emitted {Mark() - at} bytes, not 152");
        }

        _ = animBase;
        if (r.CompressedError is not null || (r.Errors is not null && r.Errors.Length > 0))
        {
            _deferredIkRulePayloads.Add((at, r));
        }
    }

    private readonly List<(int At, IkRuleDef Def)> _deferredIkRulePayloads = [];

    /// <summary>Writes deferred ikrule payloads and patches the rule's
    /// rule-relative index fields. Called once all record tables are placed.</summary>
    private void FlushIkRulePayloads()
    {
        foreach ((int at, IkRuleDef r) in _deferredIkRulePayloads)
        {
            if (r.CompressedError is not null)
            {
                PadTo(4);
                SpanWriter.PatchInt32(_image, at + 60, EmitCompressedIkError(r.CompressedError, at) - at);
            }

            if (r.Errors is not null && r.Errors.Length > 0)
            {
                PadTo(4);
                int err = Mark();
                foreach (IkErrorFrame f in r.Errors)
                {
                    // mstudioikerror_t is {Vector pos; Quaternion quat;} = 28 bytes
                    WVec(f.Pos);
                    WQuat(f.Quat);
                }

                SpanWriter.PatchInt32(_image, at + 72, err - at);
            }
        }

        _deferredIkRulePayloads.Clear();
    }

    private int EmitCompressedIkError(CompressedIkErrorDef c, int ownerBase)
    {
        int at = Mark();
        foreach (float s in c.Scale)
        {
            WF(s); // scale[6] @0..23
        }

        Pad(12); // short offset[6] @24..35, patched after payloads
        var addrs = new int?[6];
        for (int i = 0; i < 6; i++)
        {
            if (c.Streams[i] is null)
            {
                continue;
            }

            PadTo(2);
            addrs[i] = Mark();
            foreach (short v in c.Streams[i]!)
            {
                W16(v);
            }
        }

        for (int i = 0; i < 6; i++)
        {
            if (addrs[i] is int a)
            {
                SpanWriter.PatchInt16(_image, at + 24 + 2 * i, (short)(a - at)); // relative to the record
            }
        }

        _ = ownerBase;
        return at;
    }

    private void EmitLocalHierarchy(LocalHierarchyDef h, int animBase)
    {
        // pHierarchy walks a 48-byte stride, so the record is pure record
        // bytes; the localanim payload is deferred like the ikrule payloads —
        // inline it would shift every field past it out of its record.
        int at = Mark();
        W32(h.IBone);        // iBone @0
        W32(h.INewParent);   // iNewParent @4
        WF(h.Start);         // start @8
        WF(h.Peak);          // peak @12
        WF(h.Tail);          // tail @16
        WF(h.End);           // end @20
        W32(h.IStart);       // iStart @24
        W32(0);              // localanimindex @28 (payload-deferred patch)
        Pad(16);             // unused[4] @32..47
        if (Mark() != at + 48)
        {
            throw new InvalidOperationException($"localhierarchy record emitted {Mark() - at} bytes, not 48");
        }

        _ = animBase;
        if (h.LocalAnim is not null)
        {
            _deferredHierarchyPayloads.Add((at, h));
        }
    }

    private readonly List<(int At, LocalHierarchyDef Def)> _deferredHierarchyPayloads = [];

    /// <summary>Writes deferred localhierarchy payloads (the compressed error
    /// <c>pLocalAnim</c> points at) and patches the rule-relative index.</summary>
    private void FlushHierarchyPayloads()
    {
        foreach ((int at, LocalHierarchyDef h) in _deferredHierarchyPayloads)
        {
            PadTo(4);
            int err = EmitCompressedIkError(h.LocalAnim!, at);
            SpanWriter.PatchInt32(_image, at + 28, err - at); // record-relative (stock's this + localanimindex)
        }

        _deferredHierarchyPayloads.Clear();
    }

    /// <summary>Pass 2: writes one sequence's payloads after the record
    /// table and patches its record-relative indices. Every payload sits at
    /// least 212*(n-i) bytes past the record start, so consecutive records
    /// never see another sequence's bytes inside their stride.</summary>
    private void EmitSeqDescPayload(SeqDef s, int at)
    {
        void Rel(int field, int payloadAt) => SpanWriter.PatchInt32(_image, at + field, payloadAt - at);

        if (s.AnimTable.Length < s.NumBlends)
        {
            throw new ArgumentException($"anim table of {s.AnimTable.Length} for numblends {s.NumBlends}");
        }

        PadTo(4);
        int tbl = Mark();
        foreach (short a in s.AnimTable)
        {
            W16(a);
        }

        Rel(60, tbl); // animindexindex

        if (s.Events.Count > 0)
        {
            PadTo(4);
            int ev = Mark();
            foreach (EventDef e in s.Events)
            {
                int eat = Mark();
                WF(e.Cycle);
                W32(e.Event);
                W32(e.Type);
                Pad(64);                    // options[64] @12
                PatchAscii(eat + 12, e.Options, 64);
                W32(0);                     // szeventindex @76
                if (e.Name.Length > 0)
                {
                    QueueString(eat, 76, e.Name);
                }
            }

            SpanWriter.PatchInt32(_image, at + 24, s.Events.Count);
            Rel(28, ev); // eventindex
        }

        if (s.Weights is not null)
        {
            if (s.Weights.Length != Bones.Count)
            {
                throw new ArgumentException("seqdesc weight list must cover every bone");
            }

            PadTo(4);
            int wl = Mark();
            foreach (float w in s.Weights)
            {
                WF(w);
            }

            Rel(156, wl); // weightlistindex
        }

        if (s.AutoLayers.Count > 0)
        {
            PadTo(4);
            int al = Mark();
            foreach (AutoLayerDef a in s.AutoLayers)
            {
                W16(a.ISequence);
                W16(a.IPose);
                W32(a.Flags); // flags @4 is an int (dump: autolayer flags @4)
                WF(a.Start);
                WF(a.Peak);
                WF(a.Tail);
                WF(a.End);
            }

            SpanWriter.PatchInt32(_image, at + 148, s.AutoLayers.Count);
            Rel(152, al); // autolayerindex
        }

        if (s.IkLocks.Count > 0)
        {
            PadTo(4);
            int lk = Mark();
            foreach (IkLockDef l in s.IkLocks)
            {
                W32(l.Chain);
                WF(l.FlPosWeight);
                WF(l.FlLocalQWeight);
                W32(l.Flags); // flags @12 is an int, not a float
                Pad(32 - 16);
            }

            SpanWriter.PatchInt32(_image, at + 164, s.IkLocks.Count);
            Rel(168, lk); // iklockindex
        }

        if (s.PoseKeys.Count > 0)
        {
            // Stock lays the table as groupsize[0] * groupsize[1] floats
            // (pPoseKey = (float*)(this + posekeyindex) + iParam*groupsize[0] + iAnim).
            int expected = s.GroupSize0 * s.GroupSize1;
            if (s.PoseKeys.Count != expected)
            {
                throw new ArgumentException($"posekey table of {s.PoseKeys.Count} floats for groupsize {s.GroupSize0}x{s.GroupSize1}");
            }

            PadTo(4);
            int pk = Mark();
            foreach (float f in s.PoseKeys)
            {
                WF(f);
            }

            Rel(160, pk); // posekeyindex
        }

        if (s.IkRules.Count > 0)
        {
            // Stock's mstudioseqdesc_t has no ikruleindex field: the records
            // are placed (and the numikrules count set) exactly as before,
            // but nothing in the compiled reader can reach them.
            PadTo(4);
            foreach (IkRuleDef r in s.IkRules)
            {
                EmitIkRule(r, at);
            }

            SpanWriter.PatchInt32(_image, at + 144, s.IkRules.Count);
        }

        if (s.Movements.Count > 0)
        {
            PadTo(4);
            int mv = Mark();
            foreach (MovementDef m in s.Movements)
            {
                W32(m.EndFrame);
                W32(m.MotionFlags);
                WF(m.V0);
                WF(m.V1);
                WF(m.Angle);
                WVec(m.Vector);
                WVec(m.Position);
            }

            Rel(64, mv); // movementindex
        }
    }

    private void EmitZeroframe(AnimDef a, int animBase)
    {
        if (a.ZeroFrameCount < 1)
        {
            throw new ArgumentException("zeroframecount must be >= 1 when zeroframe rows are declared");
        }

        int zf = Mark();
        for (int b = 0; b < Bones.Count; b++)
        {
            int flags = Bones[b].Flags;
            if ((flags & StudioConstants.BONE_HAS_SAVEFRAME_POS) != 0)
            {
                if (!a.ZeroFramePos.TryGetValue(b, out List<Vector>? rows) || rows.Count != a.ZeroFrameCount)
                {
                    throw new ArgumentException(
                        $"bone {b} carries BONE_HAS_SAVEFRAME_POS: zeroframe needs exactly {a.ZeroFrameCount} pos rows");
                }

                foreach (Vector v in rows)
                {
                    WVector48(v);
                }
            }

            if ((flags & StudioConstants.BONE_HAS_SAVEFRAME_ROT) != 0)
            {
                if (!a.ZeroFrameRot.TryGetValue(b, out List<Quaternion>? rows) || rows.Count != a.ZeroFrameCount)
                {
                    throw new ArgumentException(
                        $"bone {b} carries BONE_HAS_SAVEFRAME_ROT: zeroframe needs exactly {a.ZeroFrameCount} rot rows");
                }

                foreach (Quaternion q in rows)
                {
                    WQuaternion64(q);
                }
            }
        }

        SpanWriter.PatchInt16(_image, animBase + 88, a.ZeroFrameSpan);
        SpanWriter.PatchInt16(_image, animBase + 90, a.ZeroFrameCount);
        SpanWriter.PatchInt32(_image, animBase + 92, zf - animBase); // zeroframeindex relative
    }

    // ---- string plumbing -------------------------------------------------------

    private readonly List<string> _orderedStrings = [];

    private void QueueString(int recordBase, int fieldOffset, string value)
    {
        if (!_stringAddrs.ContainsKey(value) && !_orderedStrings.Contains(value))
        {
            _orderedStrings.Add(value);
        }

        _strings.Add(new PendingString(recordBase + fieldOffset, recordBase, value));
    }

    private int QueueStringRelative(int recordBase, int fieldOffset, string value) => 0; // patched at tail

    private int StringAddr(string value) =>
        _stringAddrs.TryGetValue(value, out int a)
            ? a
            : throw new InvalidOperationException($"string {value} was queued but never placed");

    /// <summary>Patch fixed-width ASCII into already-written record bytes
    /// (<c>studiohdr_t.name</c> is an inline char[64], not a string
    /// index).</summary>
    private void PatchAscii(int at, string value, int max)
    {
        int n = Math.Min(value.Length, max - 1);
        long save = _image.Position;
        for (int i = 0; i < n; i++)
        {
            _image.Position = at + i;
            _image.WriteByte((byte)value[i]);
        }

        _image.Position = at + n;
        _image.WriteByte(0);
        _image.Position = save;
    }

    private static IEnumerable<(T Item, int Index)> EnumerateIndex<T>(List<T> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            yield return (items[i], i);
        }
    }

    private const int stride0 = 0;
}

/// <summary>Little-endian writers/patches over a <see cref="MemoryStream"/>
/// (backed list grows; patches rewrite already-written bytes).</summary>
internal static class SpanWriter
{
    public static void WriteInt16(MemoryStream s, short v)
    {
        Span<byte> b = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(b, v);
        s.Write(b);
    }

    public static void WriteUInt16(MemoryStream s, ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        s.Write(b);
    }

    public static void WriteInt32(MemoryStream s, int v)
    {
        Span<byte> b = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b, v);
        s.Write(b);
    }

    public static void WriteUInt32(MemoryStream s, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        s.Write(b);
    }

    public static void WriteUInt64(MemoryStream s, ulong v)
    {
        Span<byte> b = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(b, v);
        s.Write(b);
    }

    public static void WriteSingle(MemoryStream s, float v)
    {
        Span<byte> b = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(b, v);
        s.Write(b);
    }

    public static void PatchInt16(MemoryStream s, int at, short v)
    {
        Span<byte> b = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(b, v);
        long save = s.Position;
        s.Seek(at, SeekOrigin.Begin);
        s.Write(b);
        s.Seek(save, SeekOrigin.Begin);
    }

    public static void PatchInt32(MemoryStream s, int at, int v)
    {
        Span<byte> b = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b, v);
        long save = s.Position;
        s.Seek(at, SeekOrigin.Begin);
        s.Write(b);
        s.Seek(save, SeekOrigin.Begin);
    }

    public static void PatchSingle(MemoryStream s, int at, float v)
    {
        Span<byte> b = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(b, v);
        long save = s.Position;
        s.Seek(at, SeekOrigin.Begin);
        s.Write(b);
        s.Seek(save, SeekOrigin.Begin);
    }

    public static void PatchUInt32(MemoryStream s, int at, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        long save = s.Position;
        s.Seek(at, SeekOrigin.Begin);
        s.Write(b);
        s.Seek(save, SeekOrigin.Begin);
    }
}
