using System;
using System.Collections.Generic;
using System.IO;
using SourceSharp.Shared.Bones;

namespace SourceSharp.Tests;

/// <summary>
/// The query lane's additions to <see cref="StudioHdrBuilder"/>: the
/// <c>KEYVALUETEXT</c> blob, the flexcontroller table, and the
/// <c>studiohdr2_t</c> bone-flexdriver table, so the query-cluster fixtures
/// express intent ("a keyvalue blob, two flex drivers") the same way the
/// main file's tables do.
///
/// <para>
/// These are the ONLY fields the main <c>Build()</c> does not know about, so
/// they are emitted in an explicit tail step: the main tail ends at
/// <c>Patch32(332, Contents)</c> and returns <c>_image.ToArray()</c>, and
/// <see cref="BuildWithQuery"/> calls <c>Build()</c> first, then runs the
/// query emission on the grown image — nothing emitted here shifts an
/// already-placed record, and the header's <c>length</c> @76 is re-patched.
/// </para>
///
/// <para>
/// Offsets are the dump's (<c>docs/bones-offsets.txt</c>): keyvalueindex @312 /
/// keyvaluesize @316; flexcontrollers @268/@272 (20-byte record); the flex-
/// driver pair at studiohdr2 +24/+28 (declaration order, studio.h:2117-2118)
/// over 24-byte driver records with their 16-byte control arrays — record-
/// relative, like stock's <c>pBoneFlexDriverControl</c> inline. Every
/// index/offset here uses the SAME base rule as the reader it feeds (the
/// driver table index is relative to the studiohdr2 record, exactly like
/// <c>linearboneindex</c> and the <c>WithLinearBones</c> precedent).
/// </para>
/// </summary>
public sealed partial class StudioHdrBuilder
{
    /// <summary>One <c>mstudioboneflexdrivercontrol_t</c> (16 bytes,
    /// studio.h:364-377).</summary>
    public sealed class FlexDriverControlDef
    {
        /// <summary>StudioBoneFlexComponent_t: TX/TY/TZ = 0..2.</summary>
        public int BoneComponent;
        public int FlexControllerIndex;
        public float Min;
        public float Max = 1.0f;
    }

    /// <summary>One <c>mstudioboneflexdriver_t</c> (24 bytes,
    /// studio.h:383-403) plus its control array.</summary>
    public sealed class FlexDriverDef
    {
        public int BoneIndex;
        public List<FlexDriverControlDef> Controls = [];
    }

    /// <summary>The raw <c>KEYVALUETEXT</c> blob (keyvalueindex @312 /
    /// keyvaluesize @316). Null (default) emits no blob — stock's NULL
    /// <c>KeyValueText()</c>, size 0. The empty string emits a NUL byte and
    /// is ALSO stock's NULL arm: QC writes <c>size = strlen+1</c>, so an
    /// empty string on disk is size 1 and reads back as "", while a hand-
    /// written size-0 blob reads back as null. Both arms are exercised.</summary>
    public string? KeyValueText { get; set; }

    /// <summary>The raw <c>$surfaceprop</c> C string (surfacepropindex @308,
    /// image-ABSOLUTE like the keyvalue pair — stock's
    /// <c>pszSurfaceProp()</c> is <c>(char *)hdr + surfacepropindex</c>), the
    /// blob <c>Studio_GetDefaultSurfaceProps</c> reads. Null (default) leaves
    /// @308 at the <c>SurfaceProp</c> int's value; index 0 is the reader's
    /// NR-05 empty-string arm.</summary>
    public string? SurfacePropText { get; set; }

    /// <summary>The base-hdr flexcontroller table (numflexcontrollers @268 /
    /// flexcontrollerindex @272): the 20-byte records
    /// <c>Studio_RunBoneFlexDrivers</c> resolves driver indices through.</summary>
    public List<FlexControllerDefEntry> FlexControllers { get; } = [];

    /// <summary>One <c>mstudioflexcontroller_t</c> (20 bytes,
    /// studio.h:928-937).</summary>
    public sealed class FlexControllerDefEntry
    {
        public string Type = "";
        public string Name = "";
        public int LocalToGlobal = -1; // stock skips a controller at < 0
        public float Min;
        public float Max = 1.0f;
    }

    /// <summary>The <c>studiohdr2_t</c> bone-flexdriver table (count @+24 /
    /// index @+28, studio.h:2117-2118): what
    /// <c>Studio_RunBoneFlexDrivers</c> (bone_setup.cpp:6139) walks. Emitting
    /// it creates/links studiohdr2 like <see cref="WithLinearBones"/> does.</summary>
    public List<FlexDriverDef> FlexDrivers { get; } = [];

    /// <summary>Force the studiohdr2 record even with no linear bones and no
    /// drivers (the count-0-with-studiohdr2 arm; stock answers count 0 from
    /// <c>studiohdr2index != 0</c> with a zero driver count).</summary>
    public bool WithStudioHdr2 { get; set; }
    /// <summary>The PER-SEQUENCE <c>KEYVALUETEXT</c> blob, index-aligned with
    /// <see cref="Sequences"/>: seqdesc <c>keyvalueindex</c> @172 /
    /// <c>keyvaluesize</c> @176 are RECORD-relative (stock's seqdesc inline
    /// is <c>(char *)this + keyvalueindex</c>), unlike the hdr-level
    /// @312/@316 pair the <see cref="KeyValueText"/> property patches.
    /// Null (or a shorter list) leaves a sequence at size 0 — stock's NULL
    /// <c>KeyValueText()</c> arm. The empty string emits a NUL and size 1,
    /// reading back as "" like qc's <c>size = strlen + 1</c>.</summary>
    public List<string?> SeqKeyValueText { get; } = [];


    /// <summary>Assemble the image exactly like <c>Build()</c>, then emit the
    /// query lane's tables and re-patch <c>length</c>. Call this, not
    /// <c>Build()</c>, whenever <see cref="KeyValueText"/>,
    /// <see cref="SurfacePropText"/>, <see cref="FlexControllers"/> or
    /// <see cref="FlexDrivers"/> carry data; with these empty it is Build()
    /// plus a no-op tail step.</summary>
    public byte[] BuildWithQuery()
    {
        _ = Build(); // validates, lays out every main table, patches length

        // Build()'s tail patches seek back and its ToArray does not move
        // the position; append-only writes must target the end.
        _image.Seek(0, SeekOrigin.End);

        // ---- KEYVALUETEXT blob: header-relative bytes at the tail
        //      (stock's KeyValueText is (char*)hdr + keyvalueindex).
        if (KeyValueText is not null)
        {
            int at = Mark();
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(KeyValueText))
            {
                _image.WriteByte(b);
            }

            _image.WriteByte(0); // NUL-terminated, and qc counts it in the size
            Patch32(312, at);
            Patch32(316, KeyValueText.Length + 1); // keyvaluesize = strlen+1
        }

        // ---- SURFACEPROP blob: an image-absolute C string at the tail
        //      (stock's pszSurfaceProp is (char*)hdr + surfacepropindex; no
        //      size field, NUL-terminated only).
        if (SurfacePropText is not null)
        {
            int at = Mark();
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(SurfacePropText))
            {
                _image.WriteByte(b);
            }

            _image.WriteByte(0);
            Patch32(308, at); // overwrites Build()'s SurfaceProp-int patch
        }
        // ---- PER-SEQUENCE KEYVALUETEXT blobs: bytes at the tail, the
        //      seqdesc fields patched RECORD-relatively (the seq table's
        //      absolute base is header @192, 212-byte stride).
        if (SeqKeyValueText.Count > 0)
        {
            int seqTable = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                _image.GetBuffer().AsSpan(192, 4));
            if (seqTable == 0)
            {
                throw new InvalidOperationException(
                    "SeqKeyValueText needs at least one Sequences entry to patch");
            }

            for (int i = 0; i < SeqKeyValueText.Count; i++)
            {
                string? text = SeqKeyValueText[i];
                if (text is null)
                {
                    continue; // leave the record's pair at 0/0: stock's NULL
                }

                int at = Mark();
                foreach (byte b in System.Text.Encoding.UTF8.GetBytes(text))
                {
                    _image.WriteByte(b);
                }

                _image.WriteByte(0); // qc counts the NUL in the size
                int rec = seqTable + 212 * i;
                Patch32(rec + 172, at - rec); // record-relative
                Patch32(rec + 176, text.Length + 1);
            }
        }


        // ---- flexcontrollers (20-byte records). The names cannot ride the
        //      main tail — Build() has already flushed and patched it — so
        //      this table carries its own string block after the records and
        //      patches its own name fields (record-relative, stock's
        //      pszName/pszType inlines).
        if (FlexControllers.Count > 0)
        {
            PadTo(4);
            int fc = Mark();
            // Materialise every record BEFORE patching the name indices
            // (see the flexdriver table's comment: patches restore the
            // position, so writes must never land past the written end).
            foreach (FlexControllerDefEntry c in FlexControllers)
            {
                int at = Mark();
                W32(0);               // sztypeindex @0 — patched below
                W32(0);               // sznameindex @4
                W32(c.LocalToGlobal); // localToGlobal @8
                WF(c.Min);            // min @12
                WF(c.Max);            // max @16
                if (Mark() != at + 20)
                {
                    throw new InvalidOperationException(
                        $"flexcontroller record emitted {Mark() - at} bytes, not 20");
                }
            }

            var nameFields = new List<(int FieldAddr, int RecordBase, int FieldOffset, string Value)>();
            for (int i = 0; i < FlexControllers.Count; i++)
            {
                nameFields.Add((fc + 20 * i, fc + 20 * i, 0, FlexControllers[i].Type));
                nameFields.Add((fc + 20 * i + 4, fc + 20 * i, 4, FlexControllers[i].Name));
            }

            Patch32(268, FlexControllers.Count);
            Patch32(272, fc);

            // Names immediately after the records: dedup by content so two
            // controllers sharing a type share the bytes.
            PadTo(4);
            var addrs = new Dictionary<string, int>(System.StringComparer.Ordinal);
            foreach ((int fieldAddr, int recordBase, int fieldOffset, string value) in nameFields)
            {
                if (!addrs.TryGetValue(value, out int addr))
                {
                    addr = Mark();
                    foreach (byte b in System.Text.Encoding.UTF8.GetBytes(value))
                    {
                        _image.WriteByte(b);
                    }

                    _image.WriteByte(0);
                    addrs[value] = addr;
                }

                Patch32(fieldAddr, addr - recordBase); // record-relative index
                _ = fieldOffset; // (field offset is documentation; fieldAddr is absolute)
            }
        }

        // ---- the bone-flexdriver table. Stock walks it on a UNIFORM
        //      24-byte stride (pBoneFlexDriver is a typed-pointer +i, so
        //      sizeof mstudioboneflexdriver_t = 3 ints + unused[3] = 24),
        //      exactly like qc lays it out: the driver records first, every
        //      driver's control array AFTER the table, each
        //      m_nControlIndex record-relative (pBoneFlexDriverControl is
        //      (byte*)this + index, so a value past 24 is normal).
        if (FlexDrivers.Count > 0 || WithStudioHdr2)
        {
            PadTo(4);
            int hdr2 = StudioHdr2Abs;
            if (hdr2 == 0)
            {
                hdr2 = Mark();
                Pad(256);
                SpanWriter.PatchInt32(_image, hdr2, 256); // numhdr2fields, as WithLinearBones writes
                Patch32(400, hdr2);
            }

            if (FlexDrivers.Count > 0)
            {
                PadTo(4);
                int table = Mark();
                // Materialise the whole record table BEFORE patching: the
                // SpanWriter patches seek away and restore the position, and
                // Pad/Mark append at the position, so patching past the
                // written end and padding afterwards writes zeros back over
                // the patched fields. Unused[3] @12..23 comes out zero here.
                Pad(24 * FlexDrivers.Count);
                for (int i = 0; i < FlexDrivers.Count; i++)
                {
                    int rec = table + 24 * i;
                    SpanWriter.PatchInt32(_image, rec, FlexDrivers[i].BoneIndex); // m_nBoneIndex @0
                    SpanWriter.PatchInt32(_image, rec + 4, FlexDrivers[i].Controls.Count); // m_nControlCount @4
                }

                for (int i = 0; i < FlexDrivers.Count; i++)
                {
                    int ctlAbs = Mark();
                    foreach (FlexDriverControlDef c in FlexDrivers[i].Controls)
                    {
                        W32(c.BoneComponent);       // m_nBoneComponent @0
                        W32(c.FlexControllerIndex); // m_nFlexControllerIndex @4
                        WF(c.Min);                  // m_flMin @8
                        WF(c.Max);                  // m_flMax @12
                    }

                    SpanWriter.PatchInt32(_image, table + 24 * i + 8, ctlAbs - (table + 24 * i)); // record-relative
                }

                SpanWriter.PatchInt32(_image, hdr2 + 24, FlexDrivers.Count);
                SpanWriter.PatchInt32(_image, hdr2 + 28, table - hdr2); // RELATIVE to hdr2
            }
        }

        // ---- length @76: the blob grew past the main tail's patch
        Patch32(76, (int)_image.Length);
        return _image.ToArray();
    }

    /// <summary>studiohdr2_t record address from the header, 0 when absent
    /// (the reader's <c>StudioHdr2Index</c> is private).</summary>
    private int StudioHdr2Abs =>
        System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
            _image.GetBuffer().AsSpan(400, 4));
}
