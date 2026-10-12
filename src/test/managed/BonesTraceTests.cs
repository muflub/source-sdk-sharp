using System;
using System.Buffers.Binary;
using SourceSharp.Shared.Bones;
using Xunit;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for the trace cluster (<c>BoneSetupTrace</c>): the stock
/// ClipRayToHitbox (bone_setup.cpp:5153-5253), SweepBoxToStudio (5262-5314)
/// and TraceToStudio (5320-5433), with their private collision mirrors
/// (collisionutils.cpp IntersectRayWithBox / IntersectRayWithOBB /
/// IsRayIntersectingSphere) exercised through them. Expectations are exact:
/// the fixtures keep every coordinate binary-exact, so each compared float
/// is written as the same float-operation chain the port runs (the port
/// mirrors stock's expression order — BoneSetupMath's rule — so a matching
/// expression is bit-identical, not merely close).
/// </summary>
public class BonesTraceTests
{
    private const int ContentsSolid = BoneSetupTrace.ContentsSolid;
    private const int ContentsHitbox = BoneSetupTrace.ContentsHitbox;
    private static readonly Vector Origin10 = new(-10, -10, -10);
    private static readonly Vector Corner10 = new(10, 10, 10);

    // ---- fixtures ----------------------------------------------------------
    //
    // Bones carry contents=CONTENTS_SOLID and physicsbone 3/4 through a
    // direct image patch: MstudioBone exposes neither field yet (requested
    // in docs/plans/needs-trace.md; the port itself reads them via the same record
    // offsets). Hitbox records are re-laid out to the true 68-byte
    // mstudiobbox_t at the image tail: StudioHdrBuilder currently emits them
    // 64-byte (the szhitboxnameindex@32 slot is skipped), which a multi-box
    // set reads shifted by -4. Both gaps are tracked in docs/plans/needs-trace.md.

    private static int ReadI32(byte[] img, int off) =>
        BinaryPrimitives.ReadInt32LittleEndian(img.AsSpan(off, 4));

    private static void WriteI32(byte[] img, int off, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(img.AsSpan(off, 4), value);

    private static void PatchBones(byte[] img, int[] contents, int[] physicsBone)
    {
        int boneBase = ReadI32(img, 160); // studiohdr_t.boneindex (dump: 160)
        for (int i = 0; i < contents.Length; i++)
        {
            WriteI32(img, boneBase + 216 * i + 180, contents[i]); // contents (dump: 180)
            WriteI32(img, boneBase + 216 * i + 172, physicsBone[i]); // physicsbone (dump: 172)
        }
    }

    private static byte[] RelayoutHitboxes(byte[] img)
    {
        int nSets = ReadI32(img, 172); // studiohdr_t.numhitboxsets (dump: 172)
        if (nSets == 0)
        {
            return img;
        }

        int sets = ReadI32(img, 176); // hitboxsetindex (dump: 176)
        int at = img.Length;
        byte[] outImg = new byte[img.Length + 4 * 68 * nSets * 2 + 8];
        img.CopyTo(outImg, 0);
        for (int i = 0; i < nSets; i++)
        {
            int set = sets + 12 * i;
            int n = ReadI32(outImg, set + 4);
            int boxBase = set + ReadI32(outImg, set + 8);
            int newBase = (at + 3) & ~3;
            for (int j = 0; j < n; j++)
            {
                int src = boxBase + 68 * j; // mstudiobbox_t is 68 bytes
                int dst = newBase + 68 * j;
                for (int k = 0; k < 32; k++)
                {
                    outImg[dst + k] = img[src + k]; // bone, group, bbmin, bbmax
                }

                WriteI32(outImg, dst + 32, 0); // szhitboxnameindex = ""
                WriteI32(outImg, dst + 36, ReadI32(img, src + 36)); // builder's unused[0] slot
                for (int k = 40; k < 68; k++)
                {
                    outImg[dst + k] = 0;
                }
            }

            WriteI32(outImg, set + 8, newBase - set); // relative hitboxindex
            at = newBase + 68 * n;
        }

        return outImg[..at];
    }

    // bone0: SurfacePropIndex 101 -> the injected reader answers 209; bone1:
    // 102 -> 211. Distinguishable so a wrong winner is visible in tr.
    private static int Resolver(StudioHdr hdr, int boneIndex) => hdr.Bone(boneIndex).SurfacePropIndex * 2 + 7;

    private static StudioHdrBuilder TwoBoneBase()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1, SurfaceProp = 101 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = -1, SurfaceProp = 102 });
        return b;
    }

    private static StudioHdr MakeHdr(StudioHdrBuilder b, out byte[] image)
    {
        image = b.Build();
        PatchBones(image, [1, 1], [3, 4]);
        image = RelayoutHitboxes(image);
        if (!StudioHdr.TryCreate(image, b.Resolver, out StudioHdr? hdr) || hdr is null)
        {
            throw new InvalidOperationException("fixture hdr rejected");
        }

        return hdr;
    }

    private static StudioHdr OneBoxHdr(Vector min, Vector max, int group, out byte[] image)
    {
        StudioHdrBuilder b = TwoBoneBase();
        b.HitboxSets.Add(new StudioHdrBuilder.HitboxSetDef
        {
            Name = "hb",
            Boxes = { new StudioHdrBuilder.HitboxDef { Bone = 0, Group = group, Min = min, Max = max } },
        });
        return MakeHdr(b, out image);
    }

    private static StudioHdr OneBoxHdr(Vector min, Vector max, int group) =>
        OneBoxHdr(min, max, group, out _);

    private static BoneMatrix3x4[] IdentityMats(int n)
    {
        BoneMatrix3x4[] m = new BoneMatrix3x4[n];
        for (int i = 0; i < n; i++)
        {
            m[i] = BoneMatrix3x4.Identity;
        }

        return m;
    }

    // ---- ClipRayToHitbox (stock 5153-5253) ---------------------------------

    [Fact]
    public void ClipRayToHitbox_SeparatesOnBoxAxis_LeavesTraceUntouched()
    {
        StudioHdr hdr = OneBoxHdr(Origin10, Corner10, 7);
        MstudioBBox box = hdr.HitboxSet(0).Hitbox(0);
        BoneTrace tr = new() { Fraction = 0.5f };
        BoneRay ray = BoneRay.FromPoints(new Vector(500, 0, 500), new Vector(500, -1000, 500));
        Assert.Equal(-1, BoneSetupTrace.ClipRayToHitbox(ray, box, BoneMatrix3x4.Identity, tr));
        // stock: a -1 return happens before tr is touched
        Assert.Equal(0.5f, tr.Fraction);
    }

    [Fact]
    public void ClipRayToHitbox_DeadCentreDownEntersPlusZFace()
    {
        StudioHdr hdr = OneBoxHdr(Origin10, Corner10, 7);
        MstudioBBox box = hdr.HitboxSet(0).Hitbox(0);
        BoneTrace tr = new();
        BoneRay ray = BoneRay.FromPoints(new Vector(0, 0, 200), new Vector(0, 0, -200));
        // slab i=5 (z max): d1 = 200-10 = 190, d2 = 190-400 = -210 -> enter
        // 190/400 (side 5); i=2 leaves at 0.525. PlaneType 2 from the
        // clean-entry arm, normal[2] = +1 >= 0 -> hitside = 2+3.
        int side = BoneSetupTrace.ClipRayToHitbox(ray, box, BoneMatrix3x4.Identity, tr);
        Assert.Equal(5, side);
        Assert.Equal(1.0f * (190f / (190f - (190f - 400f))), tr.Fraction);
        Assert.False(tr.StartSolid);
    }

    [Fact]
    public void ClipRayToHitbox_StartInside_ZerosFractionAndSetsStartSolid()
    {
        StudioHdr hdr = OneBoxHdr(Origin10, Corner10, 7);
        MstudioBBox box = hdr.HitboxSet(0).Hitbox(0);
        BoneTrace tr = new();
        BoneRay ray = BoneRay.FromPoints(Vector.Zero, new Vector(0, 0, 50));
        // all faces inside: t1 stays -1, startsolid holds -> stock's
        // startsolid arm: fraction 0, plane (1,0,0) type 0 -> hitside 3.
        int side = BoneSetupTrace.ClipRayToHitbox(ray, box, BoneMatrix3x4.Identity, tr);
        Assert.Equal(3, side);
        Assert.Equal(0f, tr.Fraction);
        Assert.True(tr.StartSolid);
    }

    [Fact]
    public void ClipRayToHitbox_ZeroExtentRayExactlyTangent_StillHits()
    {
        StudioHdr hdr = OneBoxHdr(Origin10, Corner10, 7);
        MstudioBBox box = hdr.HitboxSet(0).Hitbox(0);
        BoneTrace tr = new();
        // grazes the z = +10 face travelling -X: the cross-axis (OY)
        // separation sits exactly on the boundary (1000 == 1000; stock uses
        // `>`, so equality passes). x-max face: d1 = 190, d2 = -10 ->
        // 190/200, PlaneType 3 -> hitside 3.
        BoneRay ray = BoneRay.FromPoints(new Vector(200, 0, 10), new Vector(0, 0, 10));
        Assert.Equal(3, BoneSetupTrace.ClipRayToHitbox(ray, box, BoneMatrix3x4.Identity, tr));
        Assert.Equal(190f / 200f, tr.Fraction);
    }

    [Fact]
    public void ClipRayToHitbox_OffTheTangentByHalfUnit_Misses()
    {
        // Negative control for the fact above: the same geometry lifted to
        // z = 10.5 separates on box axis Z, so a check-less clip could not
        // have passed the tangent case by accident.
        StudioHdr hdr = OneBoxHdr(Origin10, Corner10, 7);
        MstudioBBox box = hdr.HitboxSet(0).Hitbox(0);
        BoneTrace tr = new();
        BoneRay ray = BoneRay.FromPoints(new Vector(200, 0, 10.5f), new Vector(0, 0, 10.5f));
        Assert.Equal(-1, BoneSetupTrace.ClipRayToHitbox(ray, box, BoneMatrix3x4.Identity, tr));
    }

    // ---- TraceToStudio: ray arm (stock 5320-5433) ---------------------------

    [Fact]
    public void TraceToStudio_DownRay_PublishesFacePlaneAndType3()
    {
        StudioHdr hdr = OneBoxHdr(Origin10, Corner10, 7);
        BoneTrace tr = new();
        BoneRay down = BoneRay.FromPoints(new Vector(0, 0, 200), new Vector(0, 0, -200));
        Assert.True(BoneSetupTrace.TraceToStudio(
            hdr, hdr.HitboxSet(0), IdentityMats(2), down, ContentsSolid, Vector.Zero, 1f, tr, Resolver));
        // wantF = 190/400 = .475 exactly-derived from the fixture: start
        // z=200, the +z face at z=10, ray delta 400. EndPos z = 200 -
        // 400*.475 = 10 (the z = +10 face); PlaneDist = the hit plane's own
        // distance (normal (0,0,1) through z=10) = 10.
        Assert.Equal(190f / (190f - (190f - 400f)), tr.Fraction);
        Assert.Equal(new Vector(0, 0, 1), tr.PlaneNormal); // hitside 5 -> +col2
        Assert.Equal(0f, tr.EndPos.X, 5);
        Assert.Equal(0f, tr.EndPos.Y, 5);
        Assert.Equal(10f, tr.EndPos.Z, 5); // FMA/NonFMA span ~2e-6 at |30|
        Assert.Equal(10f, tr.PlaneDist, 4);
        Assert.Equal(3, tr.PlaneType); // the general-plane stamp
    }

    [Fact]
    public void TraceToStudio_UpRay_PlaneNormalFollowsRayDirection()
    {
        StudioHdr hdr = OneBoxHdr(Origin10, Corner10, 7);
        BoneTrace tr = new();
        BoneRay up = BoneRay.FromPoints(new Vector(0, 0, -200), new Vector(0, 0, 200));
        Assert.True(BoneSetupTrace.TraceToStudio(
            hdr, hdr.HitboxSet(0), IdentityMats(2), up, ContentsSolid, Vector.Zero, 1f, tr, Resolver));
        // enters the -z face at z = -10: EndPos = (0,0,-200) + .475*(0,0,400);
        // the face plane is normal (0,0,-1) through z=-10 -> dist 10.
        Assert.Equal(new Vector(0, 0, -1), tr.PlaneNormal);
        Assert.Equal(190f / (190f - (190f - 400f)), tr.Fraction);
        Assert.Equal(0f, tr.EndPos.X, 5);
        Assert.Equal(0f, tr.EndPos.Y, 5);
        Assert.Equal(-10f, tr.EndPos.Z, 5);
    }

    [Fact]
    public void TraceToStudio_HitStampsComeFromBoxAndBone_WithInjectedSurfaceProps()
    {
        StudioHdr hdr = OneBoxHdr(Origin10, Corner10, 7);
        BoneTrace tr = new();
        BoneRay down = BoneRay.FromPoints(new Vector(0, 0, 200), new Vector(0, 0, -200));
        Assert.True(BoneSetupTrace.TraceToStudio(
            hdr, hdr.HitboxSet(0), IdentityMats(2), down, ContentsSolid, Vector.Zero, 1f, tr, Resolver));
        Assert.Equal(7, tr.HitGroup); // from the box
        Assert.Equal(0, tr.HitBox);
        Assert.Equal(3, tr.PhysicsBone); // from the box's BONE (stock 5407)
        Assert.Equal(ContentsSolid | ContentsHitbox, tr.Contents); // stock 5406
        Assert.Equal("**studio**", tr.SurfaceName);
        Assert.Equal(0x8000, tr.SurfaceFlags); // SURF_HITBOX
        // stock 5409: pProps->GetSurfaceIndex(pBone->pszSurfaceProp()) — the
        // injected delegate answers over bone 0's surfacepropidx (101 -> 209)
        Assert.Equal(209, tr.SurfaceProps);
    }

    [Fact]
    public void TraceToStudio_NonRayRayDelegatesToSweepBoxToStudio()
    {
        // The swept AABB (same half-extents as the box, path offset x=8)
        // hits only through the sweep arm: the ray arm's separation test
        // would reject the identical zero-extent ray (asserted first, so
        // the hit below cannot come from the slab path).
        StudioHdr hdr = OneBoxHdr(new Vector(-6, -6, -6), new Vector(6, 6, 6), 2);
        BoneMatrix3x4[] mats = IdentityMats(2);
        BoneTrace trRay = new();
        BoneRay zeroExt = BoneRay.FromPoints(new Vector(8, 0, -100), new Vector(8, 0, 100));
        Assert.False(BoneSetupTrace.TraceToStudio(
            hdr, hdr.HitboxSet(0), mats, zeroExt, ContentsSolid, Vector.Zero, 1f, trRay, Resolver));

        BoneTrace tr = new() { StartPos = new Vector(999, 999, 999) }; // only the sweep sets StartPos
        BoneRay sweep = BoneRay.FromBox(new Vector(8, 0, -100), new Vector(8, 0, 100), new Vector(-6, -6, -6), new Vector(6, 6, 6));
        Assert.True(BoneSetupTrace.TraceToStudio(
            hdr, hdr.HitboxSet(0), mats, sweep, ContentsSolid, Vector.Zero, 1f, tr, Resolver));
        // GJK entry on the bloated OBB (planes at +/-12): the -z plane is
        // entered at d = 88 of the 200-long delta.
        float wantF = 88f * (1.0f / 200f);
        Assert.Equal(wantF, tr.Fraction);
        Assert.Equal(new Vector(8, 0, -100), tr.StartPos); // ClearTrace, via the obbTrace copy
        // hand-derived: (8,0,-100) + (88/200)*(0,0,200) = (8,0,-12)
        Assert.Equal(8f, tr.EndPos.X, 5);
        Assert.Equal(0f, tr.EndPos.Y, 5);
        Assert.Equal(-12f, tr.EndPos.Z, 5);
        Assert.Equal(12f, tr.PlaneDist); // far-side distance of the hit plane
        Assert.Equal(0, tr.PlaneType); // the GJK arm never stamps type 3
        Assert.False(tr.StartSolid);
        Assert.False(tr.AllSolid);
        Assert.Equal(2, tr.HitGroup);
        Assert.Equal(0, tr.HitBox);
        Assert.Equal(ContentsSolid | ContentsHitbox, tr.Contents);
        Assert.Equal(3, tr.PhysicsBone);
        Assert.Equal("**studio**", tr.SurfaceName);
        Assert.Equal(209, tr.SurfaceProps);
    }

    [Fact]
    public void SweepBoxToStudio_RayFormatRay_TakesCBaseTraceObbArm()
    {
        // Direct call with a zero-extent ray: IntersectRayWithOBB's
        // CBaseTrace arm (collisionutils.cpp:1301-1393), reached from here
        // but NOT through TraceToStudio (which delegates only non-ray rays).
        // Bone-space slab: start (0,0,200), extent*2 = (0,0,-800): d1 = 190,
        // d2 = -610 -> t1 = 190/800 (side 5), leave 0.2625 -> clean arm
        // fraction 190/800; the arm then DOUBLES the fraction (it clipped a
        // half-delta), rebuilds the plane as flSign * col2 and stamps
        // type 3 — each observable here pins one of those fixups.
        StudioHdr hdr = OneBoxHdr(Origin10, Corner10, 7);
        BoneTrace tr = new() { StartPos = new Vector(999, 999, 999) };
        BoneRay ray = BoneRay.FromPoints(new Vector(0, 0, 200), new Vector(0, 0, -200));
        Assert.True(BoneSetupTrace.SweepBoxToStudio(
            hdr, hdr.HitboxSet(0), IdentityMats(2), ray, ContentsSolid, tr, Resolver));
        float wantT = 190f / (190f - (190f + (-800f)));
        Assert.Equal(wantT * 2.0f, tr.Fraction);
        Assert.Equal(new Vector(0, 0, 200), tr.StartPos);
        Assert.Equal(new Vector(0, 0, 10), tr.EndPos); // VectorTransform of the bone-space hit (identity)
        Assert.Equal(new Vector(0, 0, 1), tr.PlaneNormal);
        Assert.Equal(10f, tr.PlaneDist);
        Assert.Equal(3, tr.PlaneType);
        Assert.Equal(7, tr.HitGroup);
        Assert.Equal(ContentsSolid | ContentsHitbox, tr.Contents);
        Assert.Equal(3, tr.PhysicsBone);
        Assert.Equal(209, tr.SurfaceProps);
        Assert.False(tr.StartSolid);
    }

    [Fact]
    public void TraceToStudio_NearestHitboxWins_BothListOrders()
    {
        // A (bone 0, group 5): local z -30..-10, identity matrix.
        // B (bone 1, group 9): local z 30..50 on a matrix at (0,0,-40)
        // -> world z -10..10. Ray up the z axis through both: A is nearer
        // (enters z=-30 at 70/200). Both orders must stamp A — including
        // the surfaceProps/physicsbone, which stock answers from the
        // WINNER's bone (A's bone0 -> 209/3, not B's bone1 -> 211/4).
        StudioHdrBuilder bAB = TwoBoneBase();
        bAB.HitboxSets.Add(new StudioHdrBuilder.HitboxSetDef
        {
            Name = "hb",
            Boxes =
            {
                new StudioHdrBuilder.HitboxDef { Bone = 0, Group = 5, Min = new Vector(-10, -10, -30), Max = new Vector(10, 10, -10) },
                new StudioHdrBuilder.HitboxDef { Bone = 1, Group = 9, Min = new Vector(-10, -10, 30), Max = new Vector(10, 10, 50) },
            },
        });
        StudioHdr hdrAB = MakeHdr(bAB, out _);
        BoneMatrix3x4[] mats = IdentityMats(2);
        mats[1].Position = new Vector(0, 0, -40);
        BoneRay ray = BoneRay.FromPoints(new Vector(0, 0, -100), new Vector(0, 0, 100));

        BoneTrace tr = new();
        Assert.True(BoneSetupTrace.TraceToStudio(
            hdrAB, hdrAB.HitboxSet(0), mats, ray, ContentsSolid, Vector.Zero, 1f, tr, Resolver));
        float wantFrac = 1.0f * (70f / (70f - (70f - 200f)));
        Assert.Equal(0, tr.HitBox); // A, the nearer one, listed first
        Assert.Equal(5, tr.HitGroup);
        Assert.Equal(wantFrac, tr.Fraction);
        // (0,0,-100) + (70/200)*(0,0,200) = (0,0,-30): A's entry face
        Assert.Equal(0f, tr.EndPos.X, 5);
        Assert.Equal(0f, tr.EndPos.Y, 5);
        Assert.Equal(-30f, tr.EndPos.Z, 5);
        Assert.Equal(3, tr.PhysicsBone);
        Assert.Equal(209, tr.SurfaceProps);

        // Same geometry, list order reversed: B clips first (0.45), A then
        // re-clips inside the shortened window (70/90 of it) and wins with
        // fraction (90/200)*(70/90) — and A's stamps.
        StudioHdrBuilder bBA = TwoBoneBase();
        bBA.HitboxSets.Add(new StudioHdrBuilder.HitboxSetDef
        {
            Name = "hb",
            Boxes =
            {
                new StudioHdrBuilder.HitboxDef { Bone = 1, Group = 9, Min = new Vector(-10, -10, 30), Max = new Vector(10, 10, 50) },
                new StudioHdrBuilder.HitboxDef { Bone = 0, Group = 5, Min = new Vector(-10, -10, -30), Max = new Vector(10, 10, -10) },
            },
        });
        StudioHdr hdrBA = MakeHdr(bBA, out _);
        BoneTrace tr2 = new();
        Assert.True(BoneSetupTrace.TraceToStudio(
            hdrBA, hdrBA.HitboxSet(0), mats, ray, ContentsSolid, Vector.Zero, 1f, tr2, Resolver));
        float wantBA = (1.0f * (90f / (90f - (90f - 200f)))) * (70f / (70f - (70f - 90f)));
        Assert.Equal(1, tr2.HitBox); // A, listed second
        Assert.Equal(5, tr2.HitGroup);
        Assert.Equal(wantBA, tr2.Fraction);
        // same hit point through the reversed clip order: (0,0,-30)
        Assert.Equal(0f, tr2.EndPos.X, 5);
        Assert.Equal(0f, tr2.EndPos.Y, 5);
        Assert.Equal(-30f, tr2.EndPos.Z, 5);
        Assert.Equal(3, tr2.PhysicsBone);
        Assert.Equal(209, tr2.SurfaceProps);
    }

    [Fact]
    public void TraceToStudio_ContentsMaskRejection_LeavesFractionOne()
    {
        StudioHdr hdr = OneBoxHdr(Origin10, Corner10, 7);
        BoneMatrix3x4[] mats = IdentityMats(2);
        BoneRay ray = BoneRay.FromPoints(new Vector(0, 0, 200), new Vector(0, 0, -200));

        // The bone's contents is CONTENTS_SOLID; asking only for HITBOX
        // skips the hitbox (stock 5342) -> no hit, fraction stays 1.
        BoneTrace tr = new() { Fraction = 0.25f };
        Assert.False(BoneSetupTrace.TraceToStudio(
            hdr, hdr.HitboxSet(0), mats, ray, ContentsHitbox, Vector.Zero, 1f, tr, Resolver));
        Assert.Equal(1f, tr.Fraction); // re-armed at entry, nothing wrote it

        // Positive control: the same call with the mask the bone passes.
        BoneTrace tr2 = new();
        Assert.True(BoneSetupTrace.TraceToStudio(
            hdr, hdr.HitboxSet(0), mats, ray, ContentsSolid, Vector.Zero, 1f, tr2, Resolver));
        Assert.Equal(190f / (190f - (190f - 400f)), tr2.Fraction);
    }

    [Fact]
    public void TraceToStudio_EntityScaleGoesThroughScaledArm_AndDiffersFromUnscaled()
    {
        // The stock scale arm (5359-5395): basis and ray scaled by
        // 1/flScale about vecOrigin. With flScale = 2 on this geometry the
        // bone-space slab sees start (0,0,50), delta -100 -> enter 40/100,
        // and the hit arm's EndPos uses the UNSCALED ray (the stock quirk):
        // z = 200 - 400*0.4 = 40, not the world surface at 10. The scale-1
        // control on identical geometry must NOT match — a scale arm that
        // ignored flScale would make both arms agree and this fails.
        StudioHdr hdr = OneBoxHdr(Origin10, Corner10, 7);
        BoneMatrix3x4[] mats = IdentityMats(2);
        BoneRay ray = BoneRay.FromPoints(new Vector(0, 0, 200), new Vector(0, 0, -200));

        BoneTrace tr = new();
        Assert.True(BoneSetupTrace.TraceToStudio(
            hdr, hdr.HitboxSet(0), mats, ray, ContentsSolid, Vector.Zero, 2f, tr, Resolver));
        float wantF2 = 40f / (40f - (40f - 100f));
        Assert.Equal(wantF2, tr.Fraction);
        // the stock quirk: EndPos rides the UNSCALED ray: 200 - 400*0.4 = 40
        Assert.Equal(0f, tr.EndPos.X, 5);
        Assert.Equal(0f, tr.EndPos.Y, 5);
        Assert.Equal(40f, tr.EndPos.Z, 5);
        BoneTrace tr1 = new();
        Assert.True(BoneSetupTrace.TraceToStudio(
            hdr, hdr.HitboxSet(0), mats, ray, ContentsSolid, Vector.Zero, 1f, tr1, Resolver));
        Assert.Equal(190f / (190f - (190f - 400f)), tr1.Fraction);
    }

    [Fact]
    public void SweepBoxToStudio_RotatedBone_SphereRejectUsesTranslationColumnNotBasisZ()
    {
        // collisionutils.cpp:1478-1482 builds the bounding-sphere center
        // from matOBBToWorld[i][3] — the translation column. Reading the
        // basis Z element instead (Elem(row,3) falling through to row.Z)
        // puts the sphere at (Row0.Z, Row1.Z, Row2.Z) = (-1, 0, 1) here:
        // the closest point of the sweep path (40,0,1) is 41 away, past
        // the radius (half-diagonal 17.32 + extents 0.017), so the trivial
        // reject returns false and the whole sweep reports a miss for a
        // ray stock hits. Identity-basis fixtures cannot see the bug:
        // their Row.Z is 0/0/1 with zero translation, so basis Z and the
        // translation column agree; this fixture rotates (Z 90 deg about
        // Y: local x -> world -z, local z -> world x) at translation
        // (40,0,0), where the two reads disagree.
        StudioHdr hdr = OneBoxHdr(Origin10, Corner10, 4);
        BoneMatrix3x4[] mats = IdentityMats(2);
        mats[0] = new BoneMatrix3x4
        {
            Row0 = new System.Numerics.Vector4(0f, 0f, -1f, 40f),
            Row1 = new System.Numerics.Vector4(0f, 1f, 0f, 0f),
            Row2 = new System.Numerics.Vector4(1f, 0f, 0f, 0f),
        };
        BoneRay sweep = BoneRay.FromBox(
            new Vector(40, 0, -100), new Vector(40, 0, 100),
            new Vector(-0.01f, -0.01f, -0.01f), new Vector(0.01f, 0.01f, 0.01f));

        BoneTrace tr = new();
        Assert.True(BoneSetupTrace.SweepBoxToStudio(
            hdr, hdr.HitboxSet(0), mats, sweep, ContentsSolid, tr, Resolver));
        // Bone-space ray (VectorITransform at the rotated matrix):
        // origin (-100,0,0), direction (200,0,0). The -x OBB plane
        // (bloated to -10.01) is entered when the box centre has covered
        // 100-10.01 of the 200-long world delta; the expression below
        // mirrors the port's float chain d1 * (1/(d1-d2)).
        float near = -10f - 0.01f;
        float dIn = -(-100f - near);
        float dOut = -(100f - near);
        float wantF = dIn * (1.0f / (dIn - dOut));
        Assert.Equal(wantF, tr.Fraction);
        // EndPos rides the world ray: (40,0,-100) + f*(0,0,200).
        Assert.Equal(40f, tr.EndPos.X);
        Assert.Equal(0f, tr.EndPos.Y);
        Assert.Equal(-100f + wantF * 200f, tr.EndPos.Z);
        Assert.False(tr.StartSolid);
        Assert.False(tr.AllSolid);
        Assert.Equal(4, tr.HitGroup);
        Assert.Equal(0, tr.HitBox);
        Assert.Equal(ContentsSolid | ContentsHitbox, tr.Contents);
        Assert.Equal(3, tr.PhysicsBone);
        Assert.Equal(209, tr.SurfaceProps);
    }
}
