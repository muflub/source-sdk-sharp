using System.Numerics;
using Vector = System.Numerics.Vector3;
using SourceSharp.Shared.Bones;

namespace SourceSharp.Tests;

/// <summary>
/// Procedural-bone fixtures for <see cref="BoneSetupProcedural"/>.
/// <see cref="BuildWithProcedural"/> runs the main builder, then appends each
/// procedure payload image-absolute and patches the owning bone record's
/// <c>proctype</c>/<c>procindex</c> cells (<c>mstudiobone_t</c> 216-byte
/// record at <c>boneindex</c> + 216·i; proctype at +164, procindex at +168,
/// both record-relative like every Source <c>Base() + index</c>).
/// </summary>
public sealed partial class StudioHdrBuilder
{
    /// <summary>One procedural bone: the payload kind must match <see cref="ProcType"/>.</summary>
    public sealed class ProceduralDef
    {
        public int Bone;
        public int ProcType; // STUDIO_PROC_* (studio.h:103-107)
        public AxisInterpDef? AxisInterp;
        public QuatInterpDef? QuatInterp;
        public AimAtDef? AimAt;
    }

    /// <summary>mstudioaxisinterpbone_t: { control I32(0), axis I32(4), pos[6] 8+12i, quat[6] 80+16i }.</summary>
    public sealed class AxisInterpDef
    {
        public int Control;
        public int Axis;
        public Vector[] Pos = [new(1, 1, 1), new(2, 2, 2), new(3, 3, 3), new(4, 4, 4), new(5, 5, 5), new(6, 6, 6)];
        public Quaternion[] Quat = [Default, Default, Default, Default, Default, Default];

        private static Quaternion Default => new(0f, 0f, 0f, 0f); // stock default: not Identity, tests set explicitly
    }

    /// <summary>mstudioquatinterpbone_t: { control I32(0), numtriggers I32(4), triggerindex I32(8) }.</summary>
    public sealed class QuatInterpDef
    {
        public int Control;
        public List<QuatTriggerDef> Triggers = [];
        /// <summary>Stock's numtriggers cell; -1 = Triggers.Count. Set 0 to keep
        /// the record (and the trigger list) present while the loop reads none.</summary>
        public int NumTriggers = -1;
    }

    /// <summary>mstudioquatinterpinfo_t: { inv_tolerance F32(0), trigger Quat(4), pos Vec3(20), quat Quat(32) }.</summary>
    public sealed class QuatTriggerDef
    {
        public float InvTolerance;
        public Quaternion Trigger = Quaternion.Identity;
        public Vector Pos;
        public Quaternion Quat = Quaternion.Identity;
    }

    /// <summary>mstudioaimatbone_t: { parent I32(0), aim I32(4), aimvector Vec3(8), upvector Vec3(20), basepos Vec3(32) }.</summary>
    public sealed class AimAtDef
    {
        public int Parent;
        public int Aim;
        public Vector AimVector = new(1f, 0f, 0f);
        public Vector UpVector = new(0f, 0f, 1f);
        public Vector BasePos;
    }

    public List<ProceduralDef> Procedurals { get; } = [];

    /// <summary>
    /// Assembles the header, then lays the procedural payloads after the main
    /// image and wires the bone records. The image grows past its
    /// header-declared length — the readers take the full array
    /// (<see cref="StudioHdr.RawImageForRows"/>) and only table bounds are
    /// length-checked, so the payloads stay addressable.
    /// </summary>
    public byte[] BuildWithProcedural()
    {
        Build();
        _image.Position = _image.Length;
        int boneIndex = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
            _image.GetBuffer().AsSpan(160, 4));

        foreach (ProceduralDef p in Procedurals)
        {
            int recordBase = boneIndex + 216 * p.Bone;
            PadTo(4);
            int at = Mark();
            SpanWriter.PatchInt32(_image, recordBase + 164, p.ProcType);
            SpanWriter.PatchInt32(_image, recordBase + 168, at - recordBase); // record-relative, like every Source index

            switch (p.ProcType)
            {
                case 1: // STUDIO_PROC_AXISINTERPBONE
                    Require(p.QuatInterp is null && p.AimAt is null,
                        $"bone {p.Bone}: proctype 1 needs AxisInterp only");
                    WriteAxisInterp(p.AxisInterp ?? throw PayloadMissing(p, "AxisInterp"));
                    break;
                case 2: // STUDIO_PROC_QUATINTERPBONE
                    Require(p.AxisInterp is null && p.AimAt is null,
                        $"bone {p.Bone}: proctype 2 needs QuatInterp only");
                    WriteQuatInterp(p.QuatInterp ?? throw PayloadMissing(p, "QuatInterp"));
                    break;
                case 3: // STUDIO_PROC_AIMATBONE
                case 4: // STUDIO_PROC_AIMATATTACH
                    Require(p.AxisInterp is null && p.QuatInterp is null,
                        $"bone {p.Bone}: proctype {p.ProcType} needs AimAt only");
                    WriteAimAt(p.AimAt ?? throw PayloadMissing(p, "AimAt"));
                    break;
                case 5: // STUDIO_PROC_JIGGLEBONE: no Do-function in the stock
                        // switch — the dispatch's default arm never reads the
                        // procedure, so the patched cells point at an empty
                        // tail and no payload is written.
                    Require(p.AxisInterp is null && p.QuatInterp is null && p.AimAt is null,
                        $"bone {p.Bone}: proctype 5 has no Do-function; give it no payload");
                    break;
                default:
                    throw new InvalidOperationException($"bone {p.Bone}: proctype {p.ProcType} has no payload");
            }
        }
        return _image.ToArray();
    }

    private static void Require(bool ok, string message)
    {
        if (!ok)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static InvalidOperationException PayloadMissing(ProceduralDef p, string name) =>
        new($"bone {p.Bone}: proctype {p.ProcType} needs {name}");

    private void WriteAxisInterp(AxisInterpDef a)
    {
        W32(a.Control);
        W32(a.Axis);
        for (int i = 0; i < 6; i++)
        {
            WVec(a.Pos[i]);
        }
        for (int i = 0; i < 6; i++)
        {
            WQuat(a.Quat[i]);
        }
    }

    private void WriteQuatInterp(QuatInterpDef q)
    {
        int triggerBase = Mark() + 12; // right after the 12-byte record header, 48-byte-aligned
        W32(q.Control);
        W32(q.NumTriggers < 0 ? q.Triggers.Count : q.NumTriggers);
        W32(triggerBase - (triggerBase - 12)); // record-relative triggerindex == 12
        foreach (QuatTriggerDef t in q.Triggers)
        {
            WF(t.InvTolerance);
            WQuat(t.Trigger);
            WVec(t.Pos);
            WQuat(t.Quat);
        }
    }

    private void WriteAimAt(AimAtDef a)
    {
        W32(a.Parent);
        W32(a.Aim);
        WVec(a.AimVector);
        WVec(a.UpVector);
        WVec(a.BasePos);
    }
}
