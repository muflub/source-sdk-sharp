using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Handing over a declaration must put down exactly the bytes the wire-name
/// calls put down. Anything else is a wire change wearing a refactor's clothes.
///
/// <para>
/// THE BYTES ARE PINNED, NOT JUST COMPARED. Both halves of the round trip below
/// resolve their offsets from the same <see cref="TeClass"/>, so on their own
/// they agree with each other however wrong that class is — this tree's own
/// "do not compare a computation with itself". So each class is also checked
/// against a LITERAL blob captured before the declarations were converted, with
/// the old <c>BaseTable</c>/<c>WireName</c>/<c>TePropKind</c> attributes still
/// on them. That is the assertion that makes the conversion a refactor: the
/// names on the wire moved and not one byte of any blob did.
/// </para>
/// </summary>
public class TempEntDeclarationWriterTests
{
    private static TempEntRegistry Registry()
    {
        var engine = Fake();
        engine.Strict = false;

        int next = 5;
        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        return registry;
    }

    private static TempEntType Registered(string className) => Registry().Find(className)!;

    private static TempEntType Sparks() => Registered("CTESparks");

    /// <summary>The position every golden blob below was captured at.</summary>
    private static readonly Vector Position = new(-2932f, 3218f, 60f);

    private static readonly Vector Up = new(0f, 0f, 1f);

    [Fact]
    public void TheDeclarationPutsDownTheSameBytesTheWireNamesDid()
    {
        TempEntType type = Sparks();

        // The writer-by-name form, at the names this declaration now carries.
        var byWire = new byte[type.BlobBytes];
        var w1 = new TempEntWriter(type, byWire);

        Assert.True(w1.Vector("Origin", Position), "the base's origin is written");
        Assert.True(w1.Int("Magnitude", 6), "and the magnitude");
        Assert.True(w1.Int("TrailLength", 3), "and the trail length");
        Assert.True(w1.Vector("Direction", Up), "and the direction");

        // The declaration form.
        byte[] byDeclaration = new TeSparks
        {
            Origin      = Position,
            Magnitude   = 6,
            TrailLength = 3,
            Direction   = Up,
        }.ToBlob(type);

        Assert.True(byWire.AsSpan().SequenceEqual(byDeclaration),
            "the declaration form writes the same blob, byte for byte, as the wire-name form — "
          + "which is the only thing that makes this a refactor rather than a wire change");
    }

    /// <summary>
    /// <c>DT_TESparks</c>' blob, byte for byte, as it was BEFORE its declaration
    /// dropped <c>BaseTable</c>, <c>WireName</c> and its inferable
    /// <c>TePropKind</c>s.
    ///
    /// <para>
    /// Captured by running the old declaration and the old
    /// <c>writer.Vector("m_vecOrigin", …)</c> call sequence at the values below.
    /// A LITERAL, so that nothing in the conversion can move it: the round-trip
    /// test above resolves both of its sides from the same
    /// <see cref="TeClass"/> and would still pass if every offset in that class
    /// were wrong together, which is exactly what a base table silently
    /// resolving to null does.
    /// </para>
    /// </summary>
    private const string SparksGolden =
        "004037C50020494500007042060000000300000000000000000000000000803F";

    private const string ExplosionGolden =
        "004037C500204945000070424700000000006040" +
        "0F0000000200000000000000000000000000803F43000000C800000064000000";

    [Fact]
    public void TheConvertedSparksBlobIsTheOneTheWireNameFormPacked()
    {
        byte[] blob = new TeSparks
        {
            Origin      = Position,
            Magnitude   = 6,
            TrailLength = 3,
            Direction   = Up,
        }.ToBlob(Sparks());

        Assert.True(Convert.ToHexString(blob) == SparksGolden,
            "CTESparks packs the bytes the wire-name declaration packed before the conversion "
          + $"— got {Convert.ToHexString(blob)}, expected {SparksGolden}");
    }

    /// <summary>
    /// The same for <c>DT_TEExplosion</c>, which is this subtree's extension of
    /// the gate and the one worth extending it to.
    ///
    /// <para>
    /// EIGHT PROPS, A NESTED BASE AND A ONE-BYTE FIELD. Sparks alone would not
    /// have caught a conversion that lost <c>m_chMaterialType</c>'s byte width:
    /// its own three fields are all four bytes wide and all four-byte aligned,
    /// so any consistent mistake about storage width is invisible in it.
    /// Explosion's <see cref="TeExplosion.MaterialType"/> is a <c>byte</c> at
    /// offset 40 with three bytes of padding behind it, and the kind for it is
    /// now INFERRED from the C# type rather than declared — so this blob is
    /// where a wrong inference shows up as bytes.
    /// </para>
    /// </summary>
    [Fact]
    public void TheConvertedExplosionBlobIsTheOneTheWireNameFormPacked()
    {
        TempEntType type = Registry().Find("CTEExplosion")!;

        byte[] blob = new TeExplosion
        {
            Origin       = Position,
            ModelIndex   = 71,
            Scale        = 3.5f,
            FrameRate    = 15,
            Flags        = 2,
            Normal       = Up,
            MaterialType = (byte)'C',
            Radius       = 200,
            Magnitude    = 100,
        }.ToBlob(type);

        Assert.True(Convert.ToHexString(blob) == ExplosionGolden,
            "CTEExplosion packs the bytes the wire-name declaration packed before the "
          + $"conversion — got {Convert.ToHexString(blob)}, expected {ExplosionGolden}");

        // And the same round trip Sparks gets, so a golden that was simply
        // pasted from a broken run would still have to agree with the writer.
        var byWire = new byte[type.BlobBytes];
        var w = new TempEntWriter(type, byWire);

        w.Vector("Origin", Position);
        w.Int("ModelIndex", 71);
        w.Float("Scale", 3.5f);
        w.Int("FrameRate", 15);
        w.Int("Flags", 2);
        w.Vector("Normal", Up);
        w.Int("MaterialType", (byte)'C');
        w.Int("Radius", 200);
        w.Int("Magnitude", 100);

        Assert.True(byWire.AsSpan().SequenceEqual(blob),
            "and the declaration form and the writer-by-name form agree on it");
    }

    /// <summary>
    /// The blob a derived declaration packs starts where its base's ends, and
    /// the base is found through the C# INHERITANCE now that no
    /// <c>BaseTable</c> names it.
    ///
    /// <para>
    /// THE FAILURE THIS EXISTS FOR HAS NO SYMPTOM. If the registry resolved a
    /// converted declaration's base to null it would lay the blob out from
    /// offset zero while the TABLE still nested <c>DT_TEParticleSystem</c> — so
    /// every one of the class's own fields would sit exactly where the base's
    /// three COORD floats read, and every effect would arrive with an origin
    /// made out of a magnitude. It builds, it registers, it sends, and nothing
    /// anywhere says a word.
    /// </para>
    /// </summary>
    [Fact]
    public void ADroppedBaseTableIsStillFoundThroughTheInheritance()
    {
        TempEntRegistry registry = Registry();

        foreach (string className in new[]
                 {
                     "CTESparks", "CTEExplosion", "CTELargeFunnel", "CTEBloodStream",
                     "CTEShowLine", "CTEGaussExplosion", "CTEDust",
                 })
        {
            TempEntType te = registry.Find(className)!;

            Assert.True(te.Class.Declaring.GetCustomAttributes(
                            typeof(TempEntityAttribute), inherit: false)
                            .Cast<TempEntityAttribute>().Single().BaseTable is null,
                $"{className}'s attribute names no base table — the conversion dropped it");
            Assert.True(te.Class.BaseTable == "DT_TEParticleSystem",
                $"and it is resolved to DT_TEParticleSystem from the C# base class anyway");
            Assert.True(te.Class.BaseBytes == 12,
                $"{className}'s own fields start after the base's three COORD floats, not at "
              + "zero — a null base here lays every field over the origin and says nothing");
            Assert.True(te.Fields["Origin"].Offset == 0,
                $"{className} reads the base's origin at the front of its blob");
        }
    }

    [Fact]
    public void ItReportsHowManyMembersItWroteSoASilentZeroCannotPass()
    {
        TempEntType type = Sparks();

        var blob = new byte[type.BlobBytes];
        var writer = new TempEntWriter(type, blob);

        int written = TempEntDeclarationWriter.WriteChain(ref writer, new TeSparks
        {
            Origin = Vector.Zero, Magnitude = 1, TrailLength = 1, Direction = Vector.Zero,
        });

        Assert.True(written == 4,
            $"one inherited field and three of its own, all four written ({written}) — a silent "
          + "zero here is the failure this writer exists to remove, so it is asserted rather "
          + "than assumed");
    }

    /// <summary>
    /// <c>CTEBeamRingPoint</c>'s eighty bytes, captured from the tree BEFORE
    /// the beam declarations were converted (commit <c>b79b0c04</c>) by writing
    /// them through <c>TempEntWriter</c> under stock's own wire names.
    ///
    /// <para>
    /// LITERALS, AND THAT IS THE WHOLE VALUE OF THEM. The obvious gate here is
    /// a round trip — write the blob by name, write it from the declaration,
    /// compare — but both paths take their offsets from the same
    /// <see cref="TeClass"/>, so it agrees with itself however wrong that class
    /// is. That is <c>shell_nettable.cpp</c>'s "do not compare a computation
    /// with itself", and it is not theoretical: the registry read
    /// <c>attr.BaseTable</c> raw, so a converted derived class registered with
    /// <c>BaseBytes = 0</c> and laid its own three props on top of the base's
    /// fifteen — one blob, plausible numbers, no log line — and a round trip
    /// would have compared that layout against itself and passed.
    /// </para>
    ///
    /// <para>
    /// Every value is DISTINCT, because a blob whose fields had swapped in
    /// pairs is still identical to a wire-name form that swapped the same pair.
    /// </para>
    /// </summary>
    private static readonly byte[] BeamRingPointGolden =
    {
        0x47, 0x00, 0x00, 0x00,               //  0  ModelIndex 71
        0x48, 0x00, 0x00, 0x00,               //  4  HaloIndex 72
        0x03, 0x00, 0x00, 0x00,               //  8  StartFrame 3
        0x0A, 0x00, 0x00, 0x00,               // 12  FrameRate 10
        0x00, 0x00, 0x20, 0x40,               // 16  Life 2.5f
        0x00, 0x00, 0x90, 0x40,               // 20  Width 4.5f
        0x00, 0x00, 0xD0, 0x40,               // 24  EndWidth 6.5f
        0x09, 0x00, 0x00, 0x00,               // 28  FadeLength 9
        0x00, 0x00, 0xA0, 0x3F,               // 32  Amplitude 1.25f
        0x0B, 0x00, 0x00, 0x00,               // 36  Speed 11
        // THE COLOUR IS FOUR BYTES NOW, NOT FOUR INTS, and that is the one
        // thing in this file that has deliberately moved. DT_BaseBeam's r g b a
        // became a single Color32 member; TeClassBuilder still expands it into
        // four eight-bit props in the same order and the same positions on the
        // wire, but each occupies ONE byte of the blob rather than four. So the
        // base is 48 bytes where it was 60, and everything after it moves back
        // twelve.
        //
        // Re-pinned rather than relaxed. The point of a literal is that nothing
        // can move it by accident; a change that moves it on purpose has to
        // come here and say so.
        0xF7,                                 // 40  Color.R 247
        0x3F,                                 // 41  Color.G 63
        0x1F,                                 // 42  Color.B 31
        0x96,                                 // 43  Color.A 150
        0x01, 0x00, 0x00, 0x00,               // 44  Flags 1
        0x00, 0x00, 0xC9, 0xC2,               // 48  Center.X -100.5f
        0x00, 0x40, 0x48, 0x43,               // 52  Center.Y 200.25f
        0x00, 0x00, 0x80, 0x42,               // 56  Center.Z 64f
        0x00, 0x00, 0x80, 0x41,               // 60  StartRadius 16f
        0x00, 0x00, 0x40, 0x43,               // 64  EndRadius 192f
    };

    /// <summary>
    /// The byte-identity gate over <c>CTEBeamRingPoint</c> — the beam lane's
    /// hardest case and the one worth spending it on.
    ///
    /// <para>
    /// WHY THIS ONE OF THE EIGHT. It is the only beam whose own props are not
    /// all one kind — a COORD vector and two <c>SPROP_ROUNDUP</c> floats — and
    /// it is a DERIVED class, so the write has to walk the inheritance chain
    /// ROOT FIRST: fifteen inherited fields into bytes 0..47 and three of its
    /// own after them. A chain walked the other way round writes eighteen
    /// perfectly valid fields into the wrong eighteen slots, which is a beam
    /// with a plausible colour drawn somewhere else, and no error anywhere.
    /// </para>
    ///
    /// <para>
    /// And it pins the two hazards the conversion carries. <c>Speed</c> goes
    /// down FIFTH FROM LAST of the base's fifteen, at byte 36, where stock's
    /// table puts <c>m_nSpeed</c> and where <c>te_basebeam.h</c> does not — so
    /// a declaration reordered to match the header writes speed where red
    /// belongs and the bytes say so. And <c>BaseBytes</c> is asserted at 48,
    /// which is the null-base failure named on
    /// <see cref="BeamRingPointGolden"/> caught by its cause rather than only
    /// by its symptom.
    /// </para>
    /// </summary>
    [Fact]
    public void ABeamsDeclarationPutsDownTheBytesItPutDownBeforeTheConversion()
    {
        TempEntType type = Registered("CTEBeamRingPoint");

        Assert.True(type.BlobBytes == 68,
            $"CTEBeamRingPoint's blob is sixty-eight bytes, not {type.BlobBytes}");
        Assert.True(type.Class.BaseBytes == 48,
            $"and its own three props start at 48, where DT_BaseBeam's fifteen end, not at "
          + $"{type.Class.BaseBytes} — a zero here is the base table not being resolved, which "
          + "lays this class's props on top of its base's in the same sixty-eight bytes");

        // The whole declaration in ONE object initialiser across both levels of
        // the chain, which is what the inheritance buys.
        byte[] byDeclaration = new TeBeamRingPoint
        {
            ModelIndex  = 71,
            HaloIndex   = 72,
            StartFrame  = 3,
            FrameRate   = 10,
            Life        = 2.5f,
            Width       = 4.5f,
            EndWidth    = 6.5f,
            FadeLength  = 9,
            Amplitude   = 1.25f,
            Speed       = 11,
            Color       = new Color32(247, 63, 31, 150),
            Center      = new Vector(-100.5f, 200.25f, 64f),
            StartRadius = 16f,
            EndRadius   = 192f,
            Flags       = 1,
        }.ToBlob(type);

        Assert.True(BeamRingPointGolden.AsSpan().SequenceEqual(byDeclaration),
            "CTEBeamRingPoint's declaration writes the same sixty-eight bytes it wrote before the "
          + "conversion, base chain and all — which is what makes lane RA's work a refactor "
          + "rather than a wire-format accident. Got: "
          + string.Join(",", byDeclaration.Select(b => "0x" + b.ToString("X2"))));
    }

    /// <summary>
    /// And the count, for the same reason the Sparks one is asserted: fifteen
    /// inherited plus three of its own. A chain that stopped at the derived
    /// level would write three fields, leave the other fifteen at zero, and
    /// draw a ring of no width in no colour at the world origin.
    /// </summary>
    [Fact]
    public void ABeamsChainWritesTheBasesFifteenAsWellAsItsOwnThree()
    {
        TempEntType type = Registered("CTEBeamRingPoint");

        var blob = new byte[type.BlobBytes];
        var writer = new TempEntWriter(type, blob);

        int written = TempEntDeclarationWriter.WriteChain(ref writer, new TeBeamRingPoint
        {
            ModelIndex = 71, Life = 2f, Speed = 3, Color = new Color32(0, 0, 0, 150),
            Center = Vector.Zero, StartRadius = 1f, EndRadius = 2f,
        });

        Assert.True(written == 15,
            $"fifteen inherited and three of its own, all eighteen written ({written})");
    }

    [Fact]
    public void AMemberNameThatIsNotOnTheSchemaIsRefusedRatherThanIgnored()
    {
        TempEntType type = Sparks();

        var blob = new byte[type.BlobBytes];
        var writer = new TempEntWriter(type, blob);

        Assert.True(!writer.Int("NoSuchField", 7),
            "a field the schema does not declare is refused, and says so");

        Assert.True(blob.All(b => b == 0), "and nothing was written for it");
    }

    // =====================================================================
    // The same gate on the widest declaration in the DT_BaseTempEntity
    // subtree, and on the inventory's only nested table.
    // =====================================================================

    /// <summary>
    /// <c>DT_TEShatterSurface</c>, fourteen props, through both forms.
    ///
    /// <para>
    /// IT IS THE ONE CHOSEN BECAUSE IT IS THE WIDEST AND THE LEAST REGULAR:
    /// four vectors, three <c>SPROP_NOSCALE</c> floats, an <c>enum</c>-typed
    /// int, and SIX one-byte colours that stock declares with
    /// <c>SENDINFO_ARRAYELEM</c> — six names that all changed at once when the
    /// declaration dropped <c>WireName</c>. A byte-identity check is exactly
    /// the shape of test that catches a colour landing one byte out, because
    /// six consecutive bytes are six places a wrong offset still writes
    /// something plausible.
    /// </para>
    ///
    /// <para>
    /// Every value below is DISTINCT and none is zero, which is the other half
    /// of it: a blob is zeroed before it is written, so a field the declaration
    /// form silently skipped would match a wire-name form that had written a
    /// zero there.
    /// </para>
    /// </summary>
    [Fact]
    public void TheShatterSurfaceDeclarationPutsDownTheSameBytesTheWireNamesDid()
    {
        TempEntType type = Registry().Find("CTEShatterSurface")!;

        var origin   = new Vector(-512f, 256f, 64f);
        var angles   = new Vector(-30f, 90f, 12.5f);
        var force    = new Vector(0f, -300f, 25f);
        var forcePos = new Vector(-512f, 260f, 70f);

        // The old form, by name, verbatim.
        var byWire = new byte[type.BlobBytes];
        var w = new TempEntWriter(type, byWire);

        w.Vector("Origin", origin);
        w.Vector("Angles", angles);
        w.Vector("Force", force);
        w.Vector("ForcePos", forcePos);
        w.Float("Width", 33.5f);
        w.Float("Height", 48.25f);
        w.Float("ShardSize", 4.75f);
        w.Int("SurfaceType", (int)ShatterSurfaceType.Tile);
        w.Int("FrontColorR", 201);
        w.Int("FrontColorG", 202);
        w.Int("FrontColorB", 203);
        w.Int("BackColorR", 11);
        w.Int("BackColorG", 12);
        w.Int("BackColorB", 13);

        // The new one.
        byte[] byDeclaration = new TeShatterSurface
        {
            Origin      = origin,
            Angles      = angles,
            Force       = force,
            ForcePos    = forcePos,
            Width       = 33.5f,
            Height      = 48.25f,
            ShardSize   = 4.75f,
            SurfaceType = ShatterSurfaceType.Tile,
            FrontColorR = 201,
            FrontColorG = 202,
            FrontColorB = 203,
            BackColorR  = 11,
            BackColorG  = 12,
            BackColorB  = 13,
        }.ToBlob(type);

        Assert.True(byWire.AsSpan().SequenceEqual(byDeclaration),
            "DT_TEShatterSurface's declaration form writes the same blob, byte for byte, as the "
          + "wire-name form — fourteen props, six of them one-byte colours at consecutive "
          + "offsets");

        // AND AGAINST LITERALS, which is the half the comparison above cannot
        // do. Both forms take their offsets from the same TeClass, so they
        // agree with each other however wrong that class is — the null-base bug
        // in RegisterAll was exactly that shape. These bytes are hand-derived
        // from stock's table order and the storage widths: four Vectors at
        // 0/12/24/36, three floats at 48/52/56, the two-bit surface type as a
        // whole int at 60, then SIX consecutive BYTES at 64..69 and two of
        // padding. Nothing here is read back out of the declaration.
        Assert.True(Convert.ToHexString(byDeclaration) == ShatterSurfaceGolden,
            $"and it is the blob it has always been: {Convert.ToHexString(byDeclaration)}");

        Assert.True(byDeclaration.Length == 72,
            "seventy-two bytes: 48 of vectors, 12 of floats, 4 of surface type, 6 of colour, "
          + "2 of padding");

        Assert.True(byDeclaration[64] == 201 && byDeclaration[65] == 202 && byDeclaration[66] == 203
                 && byDeclaration[67] == 11  && byDeclaration[68] == 12  && byDeclaration[69] == 13,
            "with the six colours at SIX CONSECUTIVE literal offsets, in the order stock's "
          + "m_uchFrontColor[0..2] and m_uchBackColor[0..2] have them — six is enough places "
          + "for a one-byte slip to still write something plausible");
    }

    /// <summary>
    /// <c>DT_TEShatterSurface</c>'s blob for the values above, as bytes.
    /// Hand-checked against the layout, not captured from a run and trusted.
    /// </summary>
    private const string ShatterSurfaceGolden =
        "000000C400008043000080420000F0C10000B4420000484100000000000096C3"
      + "0000C841000000C40000824300008C4200000642000041420000984001000000"
      + "C9CACB0B0C0D0000";

    /// <summary>
    /// <c>DT_TEEffectDispatch</c>: the inventory's ONLY table nested as a FIELD,
    /// and the one shape the declaration writer could not reach until now.
    ///
    /// <para>
    /// WHAT IT PINS IS THE NESTING, AND NOT DT_EffectData'S PROP NAMES. Those
    /// belong to another declaration and another lane; naming them here would
    /// make this check a copy of their spelling rather than a check of this
    /// one's structure. What this asserts is that handing over the outer
    /// declaration puts the nested table's bytes down IDENTICALLY to writing
    /// that same payload into a bare <c>DT_EffectData</c> blob — shifted by
    /// FOUR, which is <c>TeClassBuilder.NestedTablePad</c>'s deliberate
    /// non-zero offset and the difference between an effect and a NULL virtual
    /// call inside <c>engine-&gt;FireEvents()</c>.
    /// </para>
    ///
    /// <para>
    /// Before the writer learned to recurse, the outer declaration wrote NO
    /// fields at all and said so in a warning — which is a whole effect
    /// disappearing, and exactly the failure <c>ToBlob</c>'s zero-count warning
    /// exists to catch.
    /// </para>
    /// </summary>
    [Fact]
    public void TheEffectDispatchDeclarationReachesItsNestedTableAtThePaddedOffset()
    {
        TempEntRegistry registry = Registry();

        TempEntType dispatch = registry.Find("CTEEffectDispatch")!;
        TempEntType payload  = registry.Find("CEffectData")
                            ?? registry.All.Single(t => t.Class.TableName == "DT_EffectData");

        var data = new TeEffectData
        {
            Origin          = new Vector(1f, 2f, 3f),
            Start           = new Vector(4f, 5f, 6f),
            Angles          = new Vector(7f, 8f, 9f),
            Normal          = new Vector(0f, 0f, 1f),
            Flags           = 5,
            Magnitude       = 2.5f,
            Scale           = 1.25f,
            AttachmentIndex = 3,
            SurfaceProp     = 7,
            EffectName      = 11,
            Material        = 13,
            DamageType      = 17,
            HitBox          = 19,
            EntIndex        = 23,
            Color           = 29,
            Radius          = 31.5f,

            // The tail, which is where the padding is and where a hand-written
            // offset goes wrong: a bool, two vectors, another bool, an int and
            // a third vector.
            CustomColors       = true,
            Color1             = new Vector(0.25f, 0.5f, 0.75f),
            Color2             = new Vector(1f, 0.125f, 0.375f),
            ControlPoint1      = true,
            ParticleAttachment = 41,
            ControlPointOffset = new Vector(43f, 47f, 53f),
        };

        // The nested table on its own, through its own declaration.
        var inner = new byte[payload.BlobBytes];
        var innerWriter = new TempEntWriter(payload, inner);

        int innerWritten = TempEntDeclarationWriter.Write(ref innerWriter, in data);

        Assert.True(innerWritten > 0, "DT_EffectData's own declaration writes its own fields");

        // And nested inside the dispatch, which is the thing under test.
        byte[] outer = new TeEffectDispatch { EffectData = data }.ToBlob(dispatch);

        Assert.True(dispatch.Class.Fields[0].Offset == 4,
            "the nested datatable prop sits at FOUR — at zero the engine marks it "
          + "SPROP_COLLAPSIBLE and the receiving side applies it at the object base");

        Assert.True(outer.Length >= 4 + inner.Length,
            $"the outer blob ({outer.Length}) holds the pad and the whole nested table "
          + $"({inner.Length})");

        Assert.True(outer.AsSpan(4, inner.Length).SequenceEqual(inner),
            "and the outer declaration put the nested table's bytes down exactly as the nested "
          + "declaration does, four bytes in");

        Assert.True(outer[0] == 0 && outer[1] == 0 && outer[2] == 0 && outer[3] == 0,
            "with the pad itself never written — it exists to make one comparison in the engine "
          + "come out false, not to carry anything");

        Assert.True(inner.Any(b => b != 0), "and none of this is two empty blobs agreeing");

        // AND THE LITERAL BYTES. Everything above compares one path against
        // another and both take their offsets from the same TeClass, so a wrong
        // TeClass agrees with itself — which is how RegisterAll laid a derived
        // blob out from zero and logged nothing. This is hand-derived instead:
        // four bytes of pad, then DT_EffectData's twenty-two fields at
        // 0/12/24/36 for the vectors, 48/52/56/60 for the flags and three
        // floats, a SHORT at 64 with two bytes of padding after it, five ints
        // at 68..84, a BYTE at 88, a float at 92, a BYTE at 96, two vectors at
        // 100 and 112, a BYTE at 124, an int at 128 and a vector at 132 — 144
        // in all, 148 with the pad.
        Assert.True(Convert.ToHexString(outer) == EffectDispatchGolden,
            $"and it is the blob it has always been: {Convert.ToHexString(outer)}");

        Assert.True(outer.Length == 148, "four bytes of pad and DT_EffectData's hundred and "
                                       + "forty-four");

        // Three literal offsets out of the middle of it, so a failure above
        // says WHERE rather than only THAT. The short and the two bytes are the
        // three that padding decides, which is where a hand-written offset goes
        // wrong.
        Assert.True(BitConverter.ToInt16(outer, 4 + 64) == 7,
            "m_nSurfaceProp is a SHORT at 64, two bytes and not four");
        Assert.True(outer[4 + 88] == 29, "m_nColor is one byte at 88");
        Assert.True(BitConverter.ToSingle(outer, 4 + 92) == 31.5f,
            "and m_flRadius is realigned to 92 after it");
    }

    /// <summary>
    /// <c>DT_TEEffectDispatch</c>'s blob for the payload above, as bytes —
    /// the pad included, and hand-checked against the layout.
    /// </summary>
    private const string EffectDispatchGolden =
        "000000000000803F0000004000004040000080400000A0400000C0400000E040"
      + "000000410000104100000000000000000000803F05000000000020400000A03F"
      + "03000000070000000B0000000D0000001100000013000000170000001D000000"
      + "0000FC41010000000000803E0000003F0000403F0000803F0000003E0000C03E"
      + "010000002900000000002C4200003C4200005442";

    // =====================================================================
    // The native half, which changed names with the declarations.
    // =====================================================================

    // =====================================================================
    // The base a converted declaration no longer names.
    // =====================================================================

    /// <summary>
    /// Every registered table's own fields start exactly where its base's blob
    /// ends — the invariant a converted declaration puts at risk.
    ///
    /// <para>
    /// THE BUG THIS IS FOR LOGS NOTHING AND LOOKS LIKE NOTHING.
    /// <c>RegisterAll</c> read <c>attr.BaseTable</c> raw to decide where a blob
    /// starts. A converted declaration drops that attribute and lets the C#
    /// base class supply the table, so the attribute answers null — read as
    /// NOBASE, laid out from offset zero — while <c>TeClassBuilder</c> resolves
    /// the base from the inheritance and STILL NESTS IT. Every derived field
    /// then sits exactly where the base's props read. It compiles, registers,
    /// sends, and says nothing anywhere. Found by lane RP.
    /// </para>
    ///
    /// <para>
    /// It is INVISIBLE while the base is <c>DT_BaseTempEntity</c>, which has no
    /// fields and no bytes — which is every one of the twenty-two in this lane,
    /// and exactly why the check has to be an invariant over the whole registry
    /// rather than an assertion in one of them. <c>DT_TEParticleSystem</c>'s
    /// twelve bytes are what it costs, and its subtree converts next.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryTableOwnFieldsStartWhereItsBaseBlobEnds()
    {
        TempEntRegistry registry = Registry();

        var byTable = registry.All.ToDictionary(t => t.Class.TableName, StringComparer.Ordinal);
        int checkedBases = 0;

        foreach (TempEntType te in registry.All)
        {
            if (te.Class.BaseTable is null)
            {
                Assert.True(te.Class.BaseBytes == 0,
                    $"{te.Class.TableName} nests nothing, so its own fields start at zero");
                continue;
            }

            Assert.True(byTable.TryGetValue(te.Class.BaseTable, out TempEntType? baseType),
                $"{te.Class.TableName} nests {te.Class.BaseTable}, which is registered here — a "
              + "base this side does not own is a blob laid out against offsets it cannot "
              + "compute");

            checkedBases++;

            // BaseBytes may carry TeClassBuilder's four-byte pad in front of a
            // nested datatable, which is not the base's and is deliberate.
            int pad = te.Class.Fields.Length > 0 && te.Class.Fields[0].Kind == TePropKind.DataTable
                    ? te.Class.BaseBytes - baseType!.Class.BlobBytes
                    : 0;

            Assert.True(te.Class.BaseBytes - pad == baseType!.Class.BlobBytes,
                $"{te.Class.TableName}'s own fields begin at {te.Class.BaseBytes - pad}, where "
              + $"{te.Class.BaseTable}'s {baseType.Class.BlobBytes}-byte blob ends — not at zero, "
              + "on top of it");
        }

        Assert.True(checkedBases >= 20,
            $"and it checked {checkedBases} nested bases, so the registry was read rather than "
          + "found empty");
    }

    /// <summary>
    /// The twenty-two take <c>DT_BaseTempEntity</c> from the C# base class and
    /// not from a second copy on the attribute.
    ///
    /// <para>
    /// <see cref="TeBaseTempEntity"/> IS A CLASS OTHERS DERIVE FROM NOW, which
    /// is what makes that possible — a declaration whose C# base carries no
    /// <c>[TempEntity]</c> resolves to null, which is NOBASE, and its table
    /// would silently stop nesting anything. So a root that has nothing to
    /// derive from must keep the name written out; these have something to
    /// derive from and must not.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTwentyTwoTakeTheirBaseFromTheClassTheyDeriveFrom()
    {
        Type[] mine =
        [
            typeof(TeBspDecal), typeof(TeDecal), typeof(TeWorldDecal), typeof(TePlayerDecal),
            typeof(TeProjectedDecal), typeof(TeFootprintDecal), typeof(TeSprite),
            typeof(TeGlowSprite), typeof(TeSpriteSpray), typeof(TeSmoke), typeof(TeDynamicLight),
            typeof(TeBreakModel), typeof(TePhysicsProp), typeof(TeShatterSurface),
            typeof(TeMuzzleFlash), typeof(TeImpact), typeof(TeFizz), typeof(TeBubbles),
            typeof(TeBubbleTrail), typeof(TeClientProjectile), typeof(TeEffectDispatch),
        ];

        Assert.True(mine.Length == 21, "twenty-one derive from it, and it makes twenty-two");

        foreach (Type type in mine)
        {
            var attr = type.GetCustomAttribute<TempEntityAttribute>()!;

            Assert.True(attr.BaseTable is null,
                $"{type.Name} does not name its base table twice");
            Assert.True(type.BaseType == typeof(TeBaseTempEntity),
                $"{type.Name} derives from TeBaseTempEntity, which is what says it");
            Assert.True(TeClassBuilder.BaseTableOf(type) == "DT_BaseTempEntity",
                $"{type.Name} resolves DT_BaseTempEntity from that");
            Assert.True(TeClassBuilder.Build(type).BaseTable == "DT_BaseTempEntity",
                $"{type.Name}'s built table nests DT_BaseTempEntity, as stock's "
              + "IMPLEMENT_SERVERCLASS_ST does");
        }

        Assert.True(typeof(TeBaseTempEntity).BaseType == typeof(TempEntState),
            "and the root itself is a TempEntState, so ToBlob can be called on the whole chain");
        Assert.True(TeClassBuilder.Build(typeof(TeBaseTempEntity)).BlobBytes == 0,
            "carrying no fields and no bytes, exactly as stock's empty DT_BaseTempEntity does");
    }

    // =====================================================================
    // The declaration AS IT WAS, verbatim out of b79b0c04.
    // =====================================================================

    /// <summary>
    /// <c>DT_TEShatterSurface</c>'s declaration BEFORE the conversion, copied
    /// out of <c>b79b0c04</c> with nothing changed but the type's name.
    ///
    /// <para>
    /// IT IS HERE SO THE GOLDEN BYTES ARE A CAPTURE AND NOT A CLAIM. Comparing
    /// the declaration form against the wire-name form compares two paths that
    /// both take their offsets from the same <see cref="TeClass"/>, so both
    /// agree however wrong that class is — lane RA measured exactly that,
    /// reinstating the registry's null-base bug and watching the round trip
    /// still answer "agrees". This type's attributes were written before any of
    /// it, so a field that moved, changed storage width or lost its flags shows
    /// up as a difference from something that predates the change.
    /// </para>
    ///
    /// <para>
    /// It is discovered by nothing: the registry and the layout fingerprint both
    /// scan the SHARED assembly, and this is in the test one. The class and
    /// table names are deliberately not <c>DT_TEShatterSurface</c>'s.
    /// </para>
    /// </summary>
    // Never assigned in C#: every field here is read through reflection by
    // TeClassBuilder, which is the whole of what this type is for.
#pragma warning disable CS0649
    [TempEntity("CTEShatterSurfaceBefore", "DT_TEShatterSurfaceBefore",
                BaseTable = "DT_BaseTempEntity")]
    private struct ShatterSurfaceBeforeConversion
    {
        [TeProp(TePropKind.Vector, WireName = "m_vecOrigin", Bits = -1, Flags = TeFlags.Coord)]
        public Vector Origin;

        [TeProp(TePropKind.Vector, WireName = "m_vecAngles", Bits = -1, Flags = TeFlags.Coord)]
        public Vector Angles;

        [TeProp(TePropKind.Vector, WireName = "m_vecForce", Bits = -1, Flags = TeFlags.Coord)]
        public Vector Force;

        [TeProp(TePropKind.Vector, WireName = "m_vecForcePos", Bits = -1, Flags = TeFlags.Coord)]
        public Vector ForcePos;

        [TeProp(TePropKind.Float, WireName = "m_flWidth", Bits = 0, Flags = TeFlags.NoScale)]
        public float Width;

        [TeProp(TePropKind.Float, WireName = "m_flHeight", Bits = 0, Flags = TeFlags.NoScale)]
        public float Height;

        [TeProp(TePropKind.Float, WireName = "m_flShardSize", Bits = 0, Flags = TeFlags.NoScale)]
        public float ShardSize;

        [TeProp(TePropKind.Int, WireName = "m_nSurfaceType", Bits = 2, Flags = TeFlags.Unsigned)]
        public ShatterSurfaceType SurfaceType;

        [TeProp(TePropKind.Int, WireName = "m_uchFrontColor[0]", Bits = 8, Flags = TeFlags.Unsigned)]
        public byte FrontColorR;

        [TeProp(TePropKind.Int, WireName = "m_uchFrontColor[1]", Bits = 8, Flags = TeFlags.Unsigned)]
        public byte FrontColorG;

        [TeProp(TePropKind.Int, WireName = "m_uchFrontColor[2]", Bits = 8, Flags = TeFlags.Unsigned)]
        public byte FrontColorB;

        [TeProp(TePropKind.Int, WireName = "m_uchBackColor[0]", Bits = 8, Flags = TeFlags.Unsigned)]
        public byte BackColorR;

        [TeProp(TePropKind.Int, WireName = "m_uchBackColor[1]", Bits = 8, Flags = TeFlags.Unsigned)]
        public byte BackColorG;

        [TeProp(TePropKind.Int, WireName = "m_uchBackColor[2]", Bits = 8, Flags = TeFlags.Unsigned)]
        public byte BackColorB;
    }

    /// <summary><c>DT_TEEffectDispatch</c>'s, the same way and for the same reason.</summary>
    [TempEntity("CTEEffectDispatchBefore", "DT_TEEffectDispatchBefore",
                BaseTable = "DT_BaseTempEntity")]
    private struct EffectDispatchBeforeConversion
    {
        [TeProp(TePropKind.DataTable, WireName = "m_EffectData", SubTable = "DT_EffectData")]
        public TeEffectData EffectData;
    }
#pragma warning restore CS0649

    /// <summary>
    /// The converted declarations lay out exactly as the ones they replaced —
    /// every offset, storage width, kind, encoding and range, and only the
    /// names differ.
    /// </summary>
    [Fact]
    public void TheConvertedDeclarationsLayOutAsTheOnesTheyReplaced()
    {
        AssertSameLayout(TeClassBuilder.Build(typeof(ShatterSurfaceBeforeConversion)),
                         TeClassBuilder.Build(typeof(TeShatterSurface)),
                         "DT_TEShatterSurface");

        AssertSameLayout(TeClassBuilder.Build(typeof(EffectDispatchBeforeConversion)),
                         TeClassBuilder.Build(typeof(TeEffectDispatch)),
                         "DT_TEEffectDispatch");
    }

    private static void AssertSameLayout(TeClass before, TeClass after, string what)
    {
        Assert.True(before.Fields.Length == after.Fields.Length,
            $"{what} has {before.Fields.Length} field(s) as it always did, not {after.Fields.Length}");
        Assert.True(before.BlobBytes == after.BlobBytes,
            $"{what}'s blob is {before.BlobBytes} bytes as it always was, not {after.BlobBytes}");
        Assert.True(before.BaseTable == after.BaseTable,
            $"{what} nests {before.BaseTable} as it always did — the C# base class says it now, "
          + "and the attribute said it then");

        for (int i = 0; i < before.Fields.Length; i++)
        {
            TeField b = before.Fields[i];
            TeField a = after.Fields[i];

            Assert.True(b.Offset == a.Offset,
                $"{what} field {i} ({b.Name} -> {a.Name}) is still at byte {b.Offset}");
            Assert.True(b.SizeBytes == a.SizeBytes,
                $"{what} field {i} still reads {b.SizeBytes} byte(s) — the width SendPropInt "
              + "picks its proxy from");
            Assert.True(b.Kind == a.Kind && b.Bits == a.Bits && b.BitsFrom == a.BitsFrom,
                $"{what} field {i} is the same kind and width it was");
            Assert.True(b.Flags == a.Flags && b.Low == a.Low && b.High == a.High,
                $"{what} field {i} carries the same flags and range");
            Assert.True(b.Elems == a.Elems && b.ElemStride == a.ElemStride
                     && b.SubTable == a.SubTable,
                $"{what} field {i} expands into the same props and nests the same table");
            // THE NAME IS THE ONE THING THAT DID CHANGE, and asserting it is
            // what stops this becoming a comparison of a declaration with
            // itself: every prop in both tables was renamed, so a replica that
            // had quietly been kept in step with the conversion fails here.
            Assert.True(b.Name != a.Name,
                $"{what} field {i} went from '{b.Name}' to '{a.Name}' — the name is the only "
              + "thing the conversion changes, and it changed");
        }
    }

    /// <summary>
    /// And the golden bytes are the ones the PRE-CONVERSION declaration would
    /// have written, laid down through its own offsets rather than through
    /// anything the conversion touched.
    /// </summary>
    [Fact]
    public void TheGoldenBytesAreWhatThePreConversionDeclarationWrote()
    {
        TeClass before = TeClassBuilder.Build(typeof(ShatterSurfaceBeforeConversion));
        var blob = new byte[before.BlobBytes];

        // A HAND-ROLLED WRITER over the OLD declaration's own offsets. Not
        // TempEntWriter and not TempEntDeclarationWriter: both of those are on
        // the path under test, and the whole point of this is a value laid down
        // somewhere nothing in that path chose.
        void Vec(string wire, Vector v)
        {
            int at = before.Fields.Single(f => f.Name == wire).Offset;
            BitConverter.TryWriteBytes(blob.AsSpan(at), v.X);
            BitConverter.TryWriteBytes(blob.AsSpan(at + 4), v.Y);
            BitConverter.TryWriteBytes(blob.AsSpan(at + 8), v.Z);
        }

        void Flt(string wire, float f)
            => BitConverter.TryWriteBytes(
                   blob.AsSpan(before.Fields.Single(x => x.Name == wire).Offset), f);

        void Num(string wire, int n)
        {
            TeField f = before.Fields.Single(x => x.Name == wire);

            if (f.SizeBytes == 1)
                blob[f.Offset] = (byte)n;
            else
                BitConverter.TryWriteBytes(blob.AsSpan(f.Offset), n);
        }

        Vec("m_vecOrigin",   new Vector(-512f, 256f, 64f));
        Vec("m_vecAngles",   new Vector(-30f, 90f, 12.5f));
        Vec("m_vecForce",    new Vector(0f, -300f, 25f));
        Vec("m_vecForcePos", new Vector(-512f, 260f, 70f));
        Flt("m_flWidth", 33.5f);
        Flt("m_flHeight", 48.25f);
        Flt("m_flShardSize", 4.75f);
        Num("m_nSurfaceType", (int)ShatterSurfaceType.Tile);
        Num("m_uchFrontColor[0]", 201);
        Num("m_uchFrontColor[1]", 202);
        Num("m_uchFrontColor[2]", 203);
        Num("m_uchBackColor[0]", 11);
        Num("m_uchBackColor[1]", 12);
        Num("m_uchBackColor[2]", 13);

        Assert.True(Convert.ToHexString(blob) == ShatterSurfaceGolden,
            "the pinned bytes are the ones the declaration wrote before it converted: "
          + $"{Convert.ToHexString(blob)}");
    }

    /// <summary>
    /// THE SAME GATE ON THE ARRAY, which is the shape most likely to shift under
    /// a conversion and the only one in all forty-four tables.
    ///
    /// <para>
    /// Every other declaration is scalars and vectors at one offset each, and a
    /// wire-name write and a declaration write reach them the same way. The
    /// spline is ONE field holding sixteen <see cref="Vector"/>s at
    /// <c>offset + i * 12</c>: the wire-name form addresses it with
    /// <c>VectorAt(name, i, v)</c> sixteen times, and the declaration form has
    /// to reproduce that out of an <c>[InlineArray]</c> whose elements have no
    /// <c>FieldInfo</c> past the first. Getting THAT wrong writes element zero
    /// and leaves fifteen at the world origin — a spline whose far end is
    /// nowhere, with nothing in any log — so the two forms are compared byte for
    /// byte over all 196 of them.
    /// </para>
    ///
    /// <para>
    /// EVERY ELEMENT IS GIVEN A VALUE DERIVED FROM ITS INDEX, so an off-by-one
    /// stride writes a neighbour's bytes rather than a plausible zero and the
    /// comparison sees it. The tail past <see cref="TeBeamSpline.MaxSplinePoints"/>
    /// is not special-cased: with a NULL length proxy all sixteen go whatever
    /// the count says (plan 3.3), which is why both forms write all sixteen.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSplineDeclarationPutsDownTheSameBytesTheWireNamesDid()
    {
        TempEntType type = Registered("CTEBeamSpline");

        Assert.True(type.BlobBytes == 196,
            $"the spline's blob is 196 bytes — four of count and sixteen twelve-byte points — "
          + $"and this is comparing {type.BlobBytes}");

        static Vector Point(int i) => new(i * 1f, i * 10f, i * 100f);

        // The old form, verbatim: a name and an index, sixteen times.
        var byWire = new byte[type.BlobBytes];
        var w1 = new TempEntWriter(type, byWire);

        w1.Int("Points", 6);

        for (int i = 0; i < TeBeamSpline.MaxSplinePoints; i++)
            w1.VectorAt("Spline", i, Point(i));

        // The new one: one object, one hand-over.
        var state = new TeBeamSpline { Points = 6 };

        for (int i = 0; i < TeBeamSpline.MaxSplinePoints; i++)
            state.Spline[i] = Point(i);

        byte[] byDeclaration = state.ToBlob(type);

        Assert.True(byWire.AsSpan().SequenceEqual(byDeclaration),
            "the declaration form writes the same 196 bytes, element for element, as sixteen "
          + "VectorAt calls — which is what makes the array a refactor and not a wire change");

        // AND THE FAR END IS REALLY THERE. Two all-zero blobs would compare
        // equal and prove nothing, so the last element is read back by hand.
        Assert.True(BitConverter.ToSingle(byDeclaration, 4 + 15 * 12 + 4) == 150f,
            "point fifteen's Y is at the far end of the run, twelve bytes per element past the "
          + "four-byte count — a comparison of two empty blobs would have passed without it");
    }

    /// <summary>
    /// THE SPLINE'S BYTES, PINNED AS LITERALS — because the round trip above
    /// cannot see the failure that matters most here.
    ///
    /// <para>
    /// <b>A ROUND TRIP AGREES WITH ITSELF HOWEVER WRONG THE TABLE IS.</b> Both
    /// halves of it — <c>VectorAt(name, i, v)</c> and <c>ToBlob</c> — take their
    /// offsets from the SAME <see cref="TeClass"/>, so a class that placed the
    /// run at the wrong offset, or gave it the wrong stride, or was built with
    /// <c>baseBytes</c> it should not have, produces two identical wrong blobs
    /// and the comparison passes. That is <c>shell_nettable.cpp</c>'s "do not
    /// compare a computation with itself", and it is exactly the shape of the
    /// null-base bug this conversion found in <c>TempEntRegistry</c>.
    /// </para>
    ///
    /// <para>
    /// So EVERY NUMBER BELOW IS TYPED HERE, and none of them is asked of the
    /// declaration:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>4</c> — where the run starts, which is one
    /// four-byte <c>m_nPoints</c> and nothing else in front of it, because
    /// <c>DT_TEBeamSpline</c> is <c>_NOBASE</c> and nests nothing.</description></item>
    /// <item><description><c>12</c> — the stride, which is
    /// <c>sizeof(Vector)</c> and what <c>SendPropArray</c> puts in the
    /// <c>DPT_Array</c> prop's <c>m_ElementStride</c>.</description></item>
    /// <item><description><c>16</c> — <c>MAX_SPLINE_POINTS</c>,
    /// <c>te_beamspline.cpp:20</c>.</description></item>
    /// <item><description><c>196</c> — 4 + 16 x 12, the whole blob.</description></item>
    /// </list>
    ///
    /// <para>
    /// A layout change that moved the run, a stride that stopped being twelve,
    /// or a base that appeared where there is none all move real bytes against
    /// these fixed ones and fail here — where the round trip would follow them
    /// and agree.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSplineLandsOnTheBytesStocksOwnTableSaysItShould()
    {
        const int CountBytes = 4;    // one int, and DT_TEBeamSpline nests nothing
        const int RunAt      = 4;    // so the sixteen points start right after it
        const int Stride     = 12;   // sizeof(Vector), the DPT_Array's m_ElementStride
        const int Points     = 16;   // MAX_SPLINE_POINTS, te_beamspline.cpp:20
        const int BlobBytes  = CountBytes + Points * Stride;   // 196

        static Vector Point(int i) => new(i * 1f, i * 10f, i * 100f);

        // THE GOLDEN BLOB, assembled from the constants above and nothing else.
        var golden = new byte[BlobBytes];

        BitConverter.GetBytes(6).CopyTo(golden, 0);

        for (int i = 0; i < Points; i++)
        {
            Vector p = Point(i);

            BitConverter.GetBytes(p.X).CopyTo(golden, RunAt + i * Stride);
            BitConverter.GetBytes(p.Y).CopyTo(golden, RunAt + i * Stride + 4);
            BitConverter.GetBytes(p.Z).CopyTo(golden, RunAt + i * Stride + 8);
        }

        // WHAT THE DECLARATION ACTUALLY PUTS DOWN.
        TempEntType type = Registered("CTEBeamSpline");

        var state = new TeBeamSpline { Points = 6 };

        for (int i = 0; i < Points; i++)
            state.Spline[i] = Point(i);

        byte[] mine = state.ToBlob(type);

        Assert.True(mine.Length == BlobBytes,
            $"the blob is {BlobBytes} bytes — four of count and sixteen twelve-byte points — "
          + $"and the declaration produced {mine.Length}");

        // Compared BYTE BY BYTE with the first difference named, because
        // "196 bytes differ" says nothing about whether the run moved by four or
        // the stride is off by one, and those are different bugs.
        int at = Enumerable.Range(0, BlobBytes).FirstOrDefault(i => mine[i] != golden[i], -1);

        Assert.True(at < 0,
            at < 0 ? "" :
            $"byte {at} is {mine[at]} where stock's layout says {golden[at]} — "
          + $"which is point {(at - RunAt) / Stride} of the run if the run starts at {RunAt}, "
          + "and a different point entirely if it does not");

        // AND THE TABLE ITSELF AGREES WITH THE SAME FOUR NUMBERS, so a drift is
        // reported as a drift rather than only as a byte mismatch.
        TeField run = type.Fields["Spline"];

        Assert.True(type.BlobBytes == BlobBytes, $"the table says {type.BlobBytes} bytes");
        Assert.True(run.Offset == RunAt, $"and puts the run at {run.Offset}, not {RunAt}");
        Assert.True(run.ElemStride == Stride, $"at a stride of {run.ElemStride}, not {Stride}");
        Assert.True(run.Elems == Points, $"over {run.Elems} elements, not {Points}");
        Assert.True(type.Class.BaseBytes == 0,
            $"and nothing in front of it: DT_TEBeamSpline is NOBASE, and BaseBytes is "
          + $"{type.Class.BaseBytes}");
    }

}
