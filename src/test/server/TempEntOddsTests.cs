using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Lane F of the temp entity port — the odd ones, the restored pair, and Dust:
/// <c>DT_TEBeamSpline</c>, <c>DT_TEClientProjectile</c>,
/// <c>DT_TEMuzzleFlash</c>, <c>DT_TEImpact</c>, <c>DT_TEGaussExplosion</c> and
/// <c>DT_TEDust</c>, against plan_tmp.md Part 6 and against the stock tables
/// they were transcribed from.
///
/// <para>
/// THE PARITY HALF IS THE ONE THAT MATTERS, for the reason
/// <see cref="TempEntSchemaTests"/> gives at length and lanes A and C both
/// proved: <c>cs_sv_te_dump</c> cannot catch a wrong-but-consistent width, flag,
/// sign or range, because both its columns come from the same C# declaration.
/// So every one of these six tables is ALSO read off disk and compared prop for
/// prop — name, kind, width, flags and the [low, high] range.
/// </para>
///
/// <para>
/// THE PARITY SOURCES ARE NOT ALL <c>te_*.cpp</c>, AND THAT IS THIS LANE:
/// </para>
/// <list type="bullet">
///   <item><c>te_beamspline.cpp</c>, <c>te_clientprojectile.cpp</c> and
///     <c>te_muzzleflash.cpp</c> — evicted by this lane, still in the tree.</item>
///   <item><c>te_impact.cpp</c> and <c>hl2/te_gaussexplosion.cpp</c> — excluded
///     from this build long BEFORE the port (ledger rows S2 and S3), never
///     evicted by anyone, and still in the tree for the same reason.</item>
///   <item><c>func_dust.cpp</c> for <c>DT_TEDust</c>. Its table had been moved
///     VERBATIM into <c>shell_entity_orphans.cpp</c>, which is where this lane
///     evicted it from — so the orphans copy no longer exists and
///     <c>func_dust.cpp</c> is the surviving original. The two were identical
///     text; the commit that moved it says so, and this file is the one still
///     on disk to read.</item>
/// </list>
///
/// <para>
/// This lane's risk is concentrated in three encodings that no byte total and
/// no prop count would catch, and each has assertions of its own: the ARRAY's
/// element count and stride (16 and 12, read off stock's own
/// <c>CNetworkArray</c> and <c>#define</c>), the EHANDLE's symbolic width and
/// the composition <see cref="TempEntWriter"/> writes into the blob, and DUST's
/// four-bit direction over an explicit [-1, 1] with NO flags — which is one
/// <c>SPROP_NORMAL</c> away from a completely different bitstream.
/// </para>
/// </summary>
public class TempEntOddsTests
{
    // =====================================================================
    // DT_TEBeamSpline — the only array in all forty-four tables
    // =====================================================================

    [Fact]
    public void TheBeamSplineTableIsACountAndSixteenPointsInOneField()
    {
        TeClass te = Build<TeBeamSpline>();

        Assert.True(te.ClassName == "CTEBeamSpline", "the two sides pair on CTEBeamSpline");
        Assert.True(te.TableName == "DT_TEBeamSpline", "over DT_TEBeamSpline");
        Assert.True(te.BaseTable is null,
            "and it nests NOTHING — IMPLEMENT_SERVERCLASS_ST_NOBASE, though the C++ class "
          + "derives from CBaseTempEntity");
        Assert.True(te.RegistersClass,
            "it claims a ServerClass: te_beamspline.cpp is evicted from the build");

        Assert.True(te.Fields.Length == 2,
            "TWO description fields for three props: the array is one field and two props");

        Assert.True(te.Fields[0].Name == "Points", "the count goes first — stock's m_nPoints");
        Assert.True(te.Fields[1].Name == "Spline", "then the points — stock's m_vecPoints");

        AssertUnsignedInt(te.Fields[0], 5);

        // The element encoding, which is what SENDINFO_ARRAY's template prop
        // carries.
        // THE KIND IS THE ONE THING ON THIS FIELD THAT IS STILL WRITTEN OUT.
        // Everything else in the declaration takes its kind from the member's
        // type; this cannot, because the member is a TeSplinePoints and nothing
        // about sixteen inline Vectors says whether they are one array prop of
        // sixteen elements or a vector-elem field of three. Left to inference it
        // throws at registration rather than guessing, which is the sharpest
        // mutation available against this table and the one this line catches.
        Assert.True(te.Fields[1].Kind == TePropKind.Array,
            "Spline is an ARRAY field and not a vector — TePropKind.Array is stated explicitly "
          + "in the declaration, because a struct of sixteen Vectors cannot say which it is");
        Assert.True(te.Fields[1].Bits == -1, "its elements' width is -1, so SPROP_COORD decides it");
        Assert.True(te.Fields[1].Flags == TeFlags.Coord,
            "and they are world coordinates and nothing else");

        // The DPT_Array prop's own two numbers.
        Assert.True(te.Fields[1].Elems == 16, "sixteen elements, which is MAX_SPLINE_POINTS");
        Assert.True(te.Fields[1].ElemStride == 12,
            "twelve bytes apart, which is sizeof(Vector) and what m_ElementStride addresses");

        Assert.True(te.BaseBytes == 0, "NOBASE, so this class's own fields start the blob");
        Assert.True(te.Fields[1].Offset == 4, "the points begin after the four-byte count");
        Assert.True(te.BlobBytes == 196,
            "196 bytes: four of count and 16 x 12 of points, computed by EntityLayout rather "
          + "than written down");
    }

    /// <summary>
    /// The 192-byte ceiling, priced from the declaration with
    /// <c>tempent_schema.h</c>'s own arithmetic.
    ///
    /// <para>
    /// The engine's per-event data cap is 192 bytes and overflow is a
    /// <c>Host_Error</c> rather than a dropped effect (plan 2F), so this table
    /// is priced AT REGISTRATION — native does it in
    /// <c>CSourceSharpTeRegistry::AddInternal</c> and refuses with
    /// <c>REFUSED_TOO_BIG</c>. THIS is the same sum done from the C# side, and
    /// it is here to fail when the DECLARATION moves rather than to second-guess
    /// native: the authority is the registry's own number, checked at gate 3 by
    /// <c>cs_sv_te_dump</c>, and <c>tempent_schema_test.cpp</c>'s
    /// <c>TheSplineFitsPackedAndWouldNotAsPlainFloats</c> asserts 1112 for the
    /// same description.
    /// </para>
    ///
    /// <para>
    /// The second half is the one that makes it a measurement rather than an
    /// assertion of comfort: the SAME sixteen vectors as plain floats — which is
    /// what a generic payload would have made of them (plan 2B-bis) — do not
    /// fit. That is why <c>SPROP_COORD</c> is load-bearing here and not
    /// decoration.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSplineFitsUnderMaxEventDataAndWouldNotWithoutTheCoordFlag()
    {
        TeClass te = Build<TeBeamSpline>();

        const int Framing   = 32;   // SOURCESHARP_TE_MESSAGE_OVERHEAD_BITS
        const int PropIndex = 7;    // SOURCESHARP_TE_PROP_INDEX_BITS
        const int Coord     = 22;   // 3 + COORD_INTEGER_BITS(14) + COORD_FRACTIONAL_BITS(5)
        const int NoScale   = 32;
        const int Ceiling   = 192 * 8;

        int packed = Framing
                   + (PropIndex + te.Fields[0].Bits)                       // m_nPoints
                   + (PropIndex + te.Fields[1].Elems * 3 * Coord + 5);     // + Q_log2(16) length

        Assert.True(packed == 1112,
            $"the spline prices at 1112 bits and this declaration gives {packed} — the same "
          + "number tempent_schema_test.cpp predicts for the same description");
        Assert.True(packed <= Ceiling,
            $"{packed} bits is {(packed + 7) / 8} of the 192 bytes the engine's event cap allows");

        int unpacked = Framing
                     + (PropIndex + te.Fields[0].Bits)
                     + (PropIndex + te.Fields[1].Elems * 3 * NoScale + 5);

        Assert.True(unpacked > Ceiling,
            $"and the same sixteen vectors as whole floats are {unpacked} bits, "
          + $"{(unpacked + 7) / 8} bytes — over the cap before a single prop index, which is "
          + "why the generic payload cannot carry this one");

        Assert.True(te.Fields[1].Flags.HasFlag(TeFlags.Coord),
            "so SPROP_COORD on the element is what makes this message legal at all");
    }

    // =====================================================================
    // DT_TEClientProjectile — the only EHandle
    // =====================================================================

    [Fact]
    public void TheClientProjectileTableEndsInAnEHandleOfASymbolicWidth()
    {
        TeClass te = Build<TeClientProjectile>();

        Assert.True(te.ClassName == "CTEClientProjectile", "the two sides pair on CTEClientProjectile");
        Assert.True(te.TableName == "DT_TEClientProjectile", "over DT_TEClientProjectile");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "which nests DT_BaseTempEntity");

        Assert.True(te.Fields.Length == 5, "five props of its own");

        Assert.True(te.Fields[0].Name == "Origin", "the origin goes first");
        Assert.True(te.Fields[1].Name == "Velocity", "then the velocity");
        Assert.True(te.Fields[2].Name == "ModelIndex", "then the model index");
        Assert.True(te.Fields[3].Name == "LifeTime", "then the lifetime");
        Assert.True(te.Fields[4].Name == "Owner", "then the owner");

        AssertCoordVector(te.Fields[0], "Origin");
        AssertCoordVector(te.Fields[1], "Velocity");
        AssertModelIndex(te.Fields[2], "ModelIndex");
        AssertUnsignedInt(te.Fields[3], 6);

        // THE EHANDLE. An int on the wire, of a width nobody restates here.
        Assert.True(te.Fields[4].Kind == TePropKind.Int,
            "SendPropEHandle IS a SendPropInt (sendproxy.cpp:70) — the proxy is the only "
          + "difference and a send proxy never reaches the client");
        Assert.True(te.Fields[4].BitsFrom == TeBits.NetworkedEHandle,
            "of NUM_NETWORKED_EHANDLE_BITS, resolved natively rather than restated as 21");
        Assert.True(te.Fields[4].Bits == -1,
            "with no literal width of its own, so a stale number cannot be read instead of the symbol");
        Assert.True(te.Fields[4].Flags == TeFlags.Unsigned,
            "and SPROP_UNSIGNED, which sendproxy.cpp:70 ORs in unconditionally");
        Assert.True(te.Fields[4].SizeBytes == 4,
            "reading four bytes of blob: the packed index-and-serial, not a pointer");

        Assert.True(te.BlobBytes == 36, "thirty-six bytes: two vectors and three four-byte fields");
    }

    /// <summary>
    /// The EHandle's COMPOSITION, which is the half no width check can see.
    ///
    /// <para>
    /// <c>SendProxy_EHandleToInt</c> (<c>sendproxy.cpp:24</c>) packs
    /// <c>GetEntryIndex() | (serial &amp; 0x3FF) &lt;&lt; MAX_EDICT_BITS</c>, and
    /// the proxy is gone, so <see cref="TempEntWriter.EHandle"/> writes what it
    /// would have produced. A shift of 12 instead of 11 is a handle that still
    /// decodes, still names an entity, and names the WRONG one — which is why
    /// this asserts the number and not just the width.
    /// </para>
    /// </summary>
    [Fact]
    public void TheEHandleIsPackedTheWayTheVanishedProxyPackedIt()
    {
        TempEntType te = Registered("CTEClientProjectile");

        // CBaseHandle's own storage: the low sixteen bits are the entry index
        // and the high sixteen the serial (public/basehandle.h:137).
        const uint Entry  = 37;
        const uint Serial = 5;

        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        Assert.True(writer.EHandle("Owner", new EntityHandle(Entry | (Serial << 16))),
            "the owner is written");

        int packed = BitConverter.ToInt32(blob, te.Fields["Owner"].Offset);

        Assert.True(packed == (int)(Entry | (Serial << 11)),
            $"entry 37 with serial 5 packs to {Entry | (Serial << 11)} — index in the low "
          + $"MAX_EDICT_BITS, serial above it — and the blob holds {packed}");

        Assert.True((packed & ((1 << 11) - 1)) == (int)Entry,
            "so the low eleven bits are the entity index the client will resolve");
        Assert.True((packed >> 11) == (int)Serial, "and the rest is the serial number");

        // The other arm of the proxy, which is a value and not an absence.
        var none = new byte[te.BlobBytes];
        var noneWriter = new TempEntWriter(te, none);

        noneWriter.EHandle("Owner", EntityHandle.None);

        Assert.True(BitConverter.ToInt32(none, te.Fields["Owner"].Offset) == (1 << 21) - 1,
            "and a handle resolving to nothing is INVALID_NETWORKED_EHANDLE_VALUE — all "
          + "twenty-one bits set, which is what stock's else arm writes. A zero here would be "
          + "entity 0 with serial 0, which is the world and a plausible owner");
    }

    // =====================================================================
    // DT_TEMuzzleFlash — a QAngle member sent as a plain vector
    // =====================================================================

    [Fact]
    public void TheMuzzleFlashTableSendsItsAnglesAsACoordVectorAndNotAsQAngles()
    {
        TeClass te = Build<TeMuzzleFlash>();

        Assert.True(te.ClassName == "CTEMuzzleFlash", "the two sides pair on CTEMuzzleFlash");
        Assert.True(te.TableName == "DT_TEMuzzleFlash", "over DT_TEMuzzleFlash");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "which nests DT_BaseTempEntity");

        Assert.True(te.Fields.Length == 4, "four props of its own");

        Assert.True(te.Fields[0].Name == "Origin", "the origin goes first");
        Assert.True(te.Fields[1].Name == "Angles", "then the angles");
        Assert.True(te.Fields[2].Name == "Scale", "then the scale");
        Assert.True(te.Fields[3].Name == "Type", "then the type");

        AssertCoordVector(te.Fields[0], "Origin");

        // THE ONE THAT COULD HAVE BEEN GOT NEARLY RIGHT. The member is a
        // CNetworkQAngle and the prop is SendPropVector, so there is no
        // anglemod and no [0,360]: a negative pitch stays negative.
        Assert.True(te.Fields[1].Kind == TePropKind.Vector,
            "m_vecAngles is a SendPropVector, though its member is a CNetworkQAngle");
        Assert.True(te.Fields[1].Kind != TePropKind.QAngles,
            "NOT SendPropQAngles, which would install SendProxy_QAngles and anglemod every "
          + "component — turning -45 into 315, which points the flash the other way and is a "
          + "plausible number");
        Assert.True(te.Fields[1].Flags == TeFlags.Coord && te.Fields[1].Bits == -1,
            "a COORD vector of the width the flag decides");

        Assert.True(te.Fields[2].Kind == TePropKind.Float
                 && te.Fields[2].Flags == TeFlags.NoScale
                 && te.Fields[2].Bits == -1,
            "m_flScale is SPROP_NOSCALE: the whole float, no range");

        AssertUnsignedInt(te.Fields[3], 32);

        Assert.True(te.BlobBytes == 32, "thirty-two bytes: two vectors, a float and an int");
    }

    // =====================================================================
    // DT_TEImpact — ledger row S2
    // =====================================================================

    [Fact]
    public void TheImpactTableIsAPointANormalAndAType()
    {
        TeClass te = Build<TeImpact>();

        Assert.True(te.ClassName == "CTEImpact", "the two sides pair on CTEImpact");
        Assert.True(te.TableName == "DT_TEImpact", "over DT_TEImpact");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "which nests DT_BaseTempEntity");
        Assert.True(te.RegistersClass,
            "and it claims a ServerClass — ledger row S2, which is the whole point: nothing "
          + "has been able to send DT_TEImpact in this build");

        Assert.True(te.Fields.Length == 3, "three props");

        Assert.True(te.Fields[0].Name == "Origin", "the origin goes first");
        Assert.True(te.Fields[1].Name == "Normal", "then the normal");
        Assert.True(te.Fields[2].Name == "Type", "then the type");

        AssertCoordVector(te.Fields[0], "Origin");

        // A NORMAL SENT AS A COORD VECTOR. SPROP_NORMAL would be 23 bits
        // instead of 66 and a different bitstream from the one c_te_impact.cpp
        // decodes.
        AssertCoordVector(te.Fields[1], "Normal");
        Assert.True(!te.Fields[1].Flags.HasFlag(TeFlags.Normal),
            "m_vecNormal carries a normal and is NOT SPROP_NORMAL — stock's own comment is "
          + "about the bandwidth it would like to save, not about what it sends");

        AssertUnsignedInt(te.Fields[2], 32);

        Assert.True(te.BlobBytes == 28, "twenty-eight bytes: two vectors and an int");

        // m_ucFlags is the client's fourth recv prop and has never had a sender.
        Assert.True(!te.Fields.Any(f => f.Name == "m_ucFlags"),
            "and m_ucFlags is NOT here: c_te_impact.cpp declares a fourth recv prop of that "
          + "name that stock's send table has never had, so it keeps the client "
          + "constructor's zero. Adding it would make this build's wire differ from stock's");
    }

    // =====================================================================
    // DT_TEGaussExplosion — ledger row S3
    // =====================================================================

    [Fact]
    public void TheGaussExplosionTableIsATypeThenADirectionOnTheParticleSystemsOrigin()
    {
        TeClass te = GaussExplosion();

        Assert.True(te.ClassName == "CTEGaussExplosion", "the two sides pair on CTEGaussExplosion");
        Assert.True(te.TableName == "DT_TEGaussExplosion", "over DT_TEGaussExplosion");
        Assert.True(te.BaseTable == "DT_TEParticleSystem",
            "and it nests DT_TEParticleSystem, not DT_BaseTempEntity");
        Assert.True(te.RegistersClass,
            "and it claims a ServerClass UNCONDITIONALLY — ledger row S3. A managed "
          + "declaration has no #ifdef HL2_DLL to be switched off by");

        Assert.True(te.Fields.Length == 2, "two props of its own");

        // The TYPE goes first though TE_GaussExplosion assigns the direction
        // before it. The table is the wire; the assignment order is not.
        Assert.True(te.Fields[0].Name == "Type", "the type goes first (stock: m_nType)");
        Assert.True(te.Fields[1].Name == "Direction", "then the direction (m_vecDirection)");

        AssertUnsignedInt(te.Fields[0], 2);
        AssertCoordVector(te.Fields[1], "Direction");

        Assert.True(te.BaseBytes == 12,
            "DT_TEParticleSystem's origin occupies the front of the blob");
        Assert.True(te.Fields[0].Offset == 12, "so Type begins after it");
        Assert.True(te.Fields[1].Offset == 16, "and the direction after that");
        Assert.True(te.BlobBytes == 28, "twenty-eight bytes in all");
    }

    // =====================================================================
    // DT_TEDust — ledger row S6, and the inventory's oddest vector
    // =====================================================================

    [Fact]
    public void TheDustTableCarriesTheCheapNormalWhichIsFourBitsAndNoFlags()
    {
        TeClass te = Dust();

        Assert.True(te.ClassName == "CTEDust", "the two sides pair on CTEDust");
        Assert.True(te.TableName == "DT_TEDust", "over DT_TEDust");
        Assert.True(te.BaseTable == "DT_TEParticleSystem", "which nests DT_TEParticleSystem");
        Assert.True(te.RegistersClass,
            "and it claims a ServerClass now that shell_entity_orphans.cpp's copy is gone");

        Assert.True(te.Fields.Length == 3, "three props of its own");

        Assert.True(te.Fields[0].Name == "Size", "the size goes first (stock: m_flSize)");
        Assert.True(te.Fields[1].Name == "Speed", "then the speed (m_flSpeed)");
        Assert.True(te.Fields[2].Name == "Direction", "then the direction (m_vecDirection)");

        // Two COORD floats: the flag decides the width for a size and a speed,
        // which is stock's choice and not a natural one.
        foreach (int i in new[] { 0, 1 })
        {
            Assert.True(te.Fields[i].Kind == TePropKind.Float, $"{te.Fields[i].Name} is a float");
            Assert.True(te.Fields[i].Bits == -1, $"{te.Fields[i].Name}'s width is -1");
            Assert.True(te.Fields[i].Flags == TeFlags.Coord,
                $"{te.Fields[i].Name} is SPROP_COORD and nothing else");
        }

        // THE CHEAP NORMAL. Four bits, flags ZERO, an explicit range — the only
        // vector in the whole inventory declared this way.
        TeField dir = te.Fields[2];

        Assert.True(dir.Kind == TePropKind.Vector, "the direction is one SendPropVector");
        Assert.True(dir.Bits == 4,
            "of FOUR bits per component — sixteen steps, coarse on purpose");
        Assert.True(dir.Flags == TeFlags.None,
            "with NO flags at all: not SPROP_COORD, and emphatically not SPROP_NORMAL, which "
          + "would send x and y and a sign bit for z — a different encoding, a different bit "
          + "count and a different bitstream from the one c_func_dust.cpp decodes");
        Assert.True(dir.Low == -1f && dir.High == 1f,
            "over an EXPLICIT [-1, 1], which is what makes four bits enough for a unit vector");

        Assert.True(te.BaseBytes == 12, "the base's origin is at the front of the blob");
        Assert.True(te.Fields[0].Offset == 12, "so Size begins after it");
        Assert.True(te.BlobBytes == 32, "thirty-two bytes in all");
    }

    // =====================================================================
    // The lane as a whole.
    // =====================================================================

    [Fact]
    public void EveryClassInTheLaneIsDiscoveredByAttributeAndRegistered()
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 0);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        foreach (string name in LaneClassNames)
        {
            Assert.True(registry.Find(name) is { Registered: true },
                $"{name} was discovered by [TempEntity] and accepted — there is no list file");
        }

        // Base before derived, and it is load-bearing rather than tidy: both of
        // this lane's DT_TEParticleSystem children have blobs that start where
        // the base's ends.
        var order = registry.All.Select(t => t.Class.TableName).ToList();

        Assert.True(order.IndexOf("DT_TEParticleSystem") < order.IndexOf("DT_TEGaussExplosion"),
            "DT_TEParticleSystem is published before DT_TEGaussExplosion, which nests it");
        Assert.True(order.IndexOf("DT_TEParticleSystem") < order.IndexOf("DT_TEDust"),
            "and before DT_TEDust");
    }

    /// <summary>
    /// The census that says the two hazards this lane owns are the only ones of
    /// their kind, over EVERY declaration in the shared assembly — not just this
    /// lane's.
    ///
    /// <para>
    /// plan 2B counts one array and one EHandle in all forty-four tables, and
    /// both are here. Written as a census rather than as two assertions about
    /// two classes so that a second array or a second EHandle appearing in
    /// somebody else's lane is a test that changed rather than a discovery made
    /// at a join.
    /// </para>
    /// </summary>
    [Fact]
    public void TheOnlyArrayAndTheOnlyEHandleInTheWholeInventoryAreThisLanes()
    {
        var arrays   = new List<string>();
        var handles  = new List<string>();

        foreach (TeClass te in AllDeclared())
        {
            foreach (TeField f in te.Fields)
            {
                if (f.Kind == TePropKind.Array)
                    arrays.Add($"{te.TableName}.{f.Name}");

                if (f.BitsFrom == TeBits.NetworkedEHandle)
                    handles.Add($"{te.TableName}.{f.Name}");
            }
        }

        Assert.True(arrays.Count == 1 && arrays[0] == "DT_TEBeamSpline.Spline",
            $"DT_TEBeamSpline's Spline — stock's m_vecPoints — is the only SendPropArray in the "
          + $"inventory; found [{string.Join(", ", arrays)}]");

        Assert.True(handles.Count == 1 && handles[0] == "DT_TEClientProjectile.Owner",
            "DT_TEClientProjectile's Owner — stock's m_hOwner — is the only SendPropEHandle — "
          + $"found [{string.Join(", ", handles)}]");
    }

    /// <summary>
    /// Every vector in the lane, and what decides its width — the census that
    /// stops Dust's four-bit range being copied onto a neighbour or a
    /// neighbour's <c>SPROP_COORD</c> being copied onto Dust.
    /// </summary>
    [Fact]
    public void ExactlyOneVectorInTheLaneIsNeitherCoordNorNormal()
    {
        var ranged = new List<string>();
        var coords = new List<string>();

        foreach (TeField f in LaneFields())
        {
            if (f.Kind != TePropKind.Vector)
                continue;

            if (f.Flags == TeFlags.Coord)
                coords.Add(f.Name);
            else
                ranged.Add($"{f.Name} {f.Bits} bits [{f.Low}, {f.High}]");
        }

        Assert.True(ranged.Count == 1 && ranged[0] == "Direction 4 bits [-1, 1]",
            $"DT_TEDust's cheap normal is the lane's only vector that is not a plain "
          + $"SPROP_COORD — found [{string.Join(", ", ranged)}]");

        Assert.True(coords.Count == 7,
            "seven COORD vectors: the projectile's origin and velocity, the flash's origin and "
          + "angles, the impact's origin and normal, and the gauss direction — "
          + $"found {coords.Count} [{string.Join(", ", coords)}]");

        Assert.True(!LaneFields().Any(f => f.Flags.HasFlag(TeFlags.Normal)),
            "and NOTHING in this lane is SPROP_NORMAL, though two of its vectors carry "
          + "normals");
    }

    /// <summary>
    /// The lane's declarations are entity layouts too, so the fingerprint the
    /// two sides already exchange at join covers their blobs — including the
    /// 192-byte inline array, which is the one this lane could have got wrong in
    /// a way that reads as a working table.
    /// </summary>
    [Fact]
    public void TheLanesBlobsAreInTheLayoutFingerprintAndTheArrayIsAllOfItsBytes()
    {
        var layouts = new EntityLayoutRegistry(new RecordingLog());
        layouts.DiscoverAll(TestServices.ServerGame);

        var names = layouts.Layouts.Select(l => l.ClassName).ToHashSet(StringComparer.Ordinal);

        // By the C# TYPE name: DiscoverAll builds each layout without the
        // attribute, so it is a fingerprint over the SHAPE.
        foreach (string t in new[]
        {
            "TeBeamSpline", "TeClientProjectile", "TeMuzzleFlash",
            "TeImpact", "TeGaussExplosion", "TeDust",
        })
        {
            Assert.True(names.Contains(t), $"{t}'s blob is in the layout fingerprint the two sides compare");
        }

        EntityLayout spline = layouts.Layouts.Single(l => l.ClassName == "TeBeamSpline");

        Assert.True(spline.Size == 196,
            $"CTEBeamSpline's blob is 196 bytes and the fingerprint has {spline.Size}");
        Assert.True(spline.Fields[1].Size == 192,
            "the inline array is all sixteen elements of storage, not a reference to them — "
          + $"the layout gives it {spline.Fields[1].Size} bytes");
    }

    /// <summary>
    /// A writer for either <c>DT_TEParticleSystem</c> child can reach the base's
    /// <c>m_vecOrigin</c>, which neither table declares and every caller has to
    /// set.
    ///
    /// <para>
    /// Get this wrong and every gauss burst and every puff of dust happens at
    /// the world origin — which is an effect that fired, in the wrong place,
    /// rather than one that did not.
    /// </para>
    /// </summary>
    [Fact]
    public void BothParticleSystemChildrenReachTheOriginTheirOwnTablesDoNotDeclare()
    {
        foreach (string className in new[] { "CTEGaussExplosion", "CTEDust" })
        {
            TempEntType te = Registered(className);

            Assert.True(te.Fields.ContainsKey("Origin"),
                $"Origin is addressable from {className}, though its table does not declare it");

            var blob = new byte[te.BlobBytes];
            var writer = new TempEntWriter(te, blob);

            Assert.True(writer.Vector("Origin", new Vector(1f, 2f, 3f)),
                $"{className}'s origin is written");

            Assert.True(BitConverter.ToSingle(blob, 0) == 1f,
                $"{className} writes it at byte 0, where DT_TEParticleSystem's first prop reads");
            Assert.True(BitConverter.ToSingle(blob, 8) == 3f, "and its z at byte 8");
        }
    }

    // =====================================================================
    // The three ledger rows, as behaviour rather than as tables.
    // =====================================================================

    /// <summary>
    /// <see cref="ITempEnts"/> has a method for each of this lane's three ledger
    /// rows, and for two of them that method is the ONLY way to fire the effect.
    ///
    /// <para>
    /// <c>ITempEntsSystem</c> has no <c>Impact</c> member at all and its
    /// <c>GaussExplosion</c> body is <c>#ifdef HL2_DLL</c>, which this build
    /// does not define — so a managed method with a body on every target is what
    /// "restored" means for S2 and S3. Asserted by REFLECTION over the interface
    /// rather than by calling them, because what is being checked is that the
    /// promise exists at all.
    /// </para>
    /// </summary>
    [Fact]
    public void TheInterfaceCarriesTheThreeRestoredEffects()
    {
        var members = typeof(ITempEnts).GetMethods().Select(m => m.Name).ToHashSet(StringComparer.Ordinal);

        Assert.True(members.Contains("Impact"),
            "ITempEnts.Impact exists — ledger row S2, and the only way to fire DT_TEImpact "
          + "in a build whose te_impact.cpp is excluded and whose ITempEntsSystem never had "
          + "an Impact member");
        Assert.True(members.Contains("GaussExplosion"),
            "ITempEnts.GaussExplosion exists — ledger row S3, unconditionally, where stock's "
          + "is #ifdef HL2_DLL and this build defines no such thing");
        Assert.True(members.Contains("Dust"),
            "ITempEnts.Dust exists — ledger row S6 and plan Part 8's named destination for "
          + "DustEffect: a real DT_TEDust, not a DispatchEffect(\"dust\"), because C_TEDust "
          + "is registered and waiting");
    }

    /// <summary>
    /// <c>TempEnts.Dust</c> NORMALISES, which is <c>TE_Dust</c>'s own
    /// <c>Assert( dir.Length() &lt; 1.01 )</c> turned into the thing it was
    /// asserting.
    ///
    /// <para>
    /// Four bits per component over [-1, 1] means a longer vector is CLAMPED by
    /// the encoder rather than refused — it arrives pointing somewhere else —
    /// and an assert is not a check in a release build.
    /// </para>
    /// </summary>
    [Fact]
    public void TheDustWriterNormalisesTheDirectionItsRangeCannotCarry()
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 0);
        engine.Ignores("Effects.TempEntPlayback");

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        var tempEnts = new SourceSharp.Server.TempEnts(new RecordingLog(), registry, engine);

        tempEnts.Dust(TeFilters.Pvs(Vector.Zero), 0f, Vector.Zero, new Vector(0f, 0f, 8f), 16f, 32f);

        var sent = engine.Calls.Single(c => c.Member == "Effects.TempEntPlayback");
        byte[] blob = sent.ArgAs<byte[]>(4);

        TeField dir = Dust().Fields[2];

        Assert.True(sent.ArgAs<int>(1) == blob.Length && blob.Length == Dust().BlobBytes,
            "a whole DT_TEDust blob went, and the bytes recorded are the bytes sent");

        Assert.True(BitConverter.ToSingle(blob, dir.Offset + 8) == 1f,
            "a direction of (0 0 8) is sent as (0 0 1): the encoder would have clamped it to "
          + "the declared [-1, 1] and the client would have drawn dust going somewhere else");
    }

    // =====================================================================
    // Fixtures.
    // =====================================================================

    private static readonly string[] LaneClassNames =
    {
        "CTEBeamSpline", "CTEClientProjectile", "CTEMuzzleFlash",
        "CTEImpact", "CTEGaussExplosion", "CTEDust",
    };

    private static int ParticleSystemBytes
        => TeClassBuilder.Build(typeof(TeParticleSystem)).BlobBytes;

    private static TeClass Build<T>() => TeClassBuilder.Build(typeof(T));

    private static TeClass GaussExplosion()
        => TeClassBuilder.Build(typeof(TeGaussExplosion), ParticleSystemBytes);

    private static TeClass Dust()
        => TeClassBuilder.Build(typeof(TeDust), ParticleSystemBytes);

    /// <summary>Every field this lane declares, its own only — not the bases'.</summary>
    private static IEnumerable<TeField> LaneFields()
        => Build<TeBeamSpline>().Fields
            .Concat(Build<TeClientProjectile>().Fields)
            .Concat(Build<TeMuzzleFlash>().Fields)
            .Concat(Build<TeImpact>().Fields)
            .Concat(GaussExplosion().Fields)
            .Concat(Dust().Fields);

    /// <summary>
    /// Every temp entity declared in the shared assembly, whoever's lane it is.
    /// Built without base bytes, which does not move a field's KIND or WIDTH —
    /// only its offset — and this is used for censuses over encodings.
    /// </summary>
    private static IEnumerable<TeClass> AllDeclared()
        => TestServices.ServerGame.GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(TempEntityAttribute), false).Length > 0)
            .Select(t => TeClassBuilder.Build(t));

    private static TempEntType Registered(string className)
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 0);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        return registry.Find(className)
            ?? throw new InvalidOperationException($"{className} was not discovered");
    }

    private static void AssertCoordVector(TeField f, string name)
    {
        Assert.True(f.Name == name, $"{name} is spelled stock's way");
        Assert.True(f.Kind == TePropKind.Vector, $"{name} is one SendPropVector, not three elements");
        Assert.True(f.Bits == -1, $"{name}'s width is -1, so SPROP_COORD decides it");
        Assert.True(f.Flags == TeFlags.Coord, $"{name} is a world coordinate and nothing else");
        Assert.True(f.SizeBytes == 12, $"{name} reads a whole Vector out of the blob");
    }

    /// <param name="name">
    /// What the prop is called on the wire. <c>m_nModelIndex</c> until a
    /// declaration converts, then the C# member's name.
    /// </param>
    private static void AssertModelIndex(TeField f, string name = "m_nModelIndex")
    {
        Assert.True(f.Name == name, $"the model index is spelled {name}");
        Assert.True(f.Kind == TePropKind.Int, "SendPropModelIndex IS a SendPropInt");
        Assert.True(f.BitsFrom == TeBits.ModelIndex,
            "of SP_MODEL_INDEX_BITS, resolved natively rather than restated as 13");
        Assert.True(f.Flags == TeFlags.None,
            "with flags of ZERO — dt_send.h:750 does not pass SPROP_UNSIGNED");
        Assert.True(f.SizeBytes == 4, "reading a four-byte int, which is what CNetworkVar(int) is");
    }

    private static void AssertUnsignedInt(TeField f, int bits)
    {
        Assert.True(f.Kind == TePropKind.Int, $"{f.Name} is a SendPropInt");
        Assert.True(f.Bits == bits, $"{f.Name} is {bits} bits");
        Assert.True(f.BitsFrom == TeBits.Literal, $"{f.Name}'s width is a literal in stock's own table");
        Assert.True(f.Flags == TeFlags.Unsigned, $"{f.Name} is SPROP_UNSIGNED");
    }
}
