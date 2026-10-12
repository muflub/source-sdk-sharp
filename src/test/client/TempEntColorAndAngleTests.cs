using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="QAngle"/> and <see cref="Color32"/> as things a temp entity
/// declaration can just SAY, rather than shapes every table spells out again.
///
/// <para>
/// Before this, a facing was a <see cref="Vector"/> with
/// <c>[TeProp(TePropKind.QAngles)]</c> written beside it — the type could not
/// tell an angle from a position, so the table had to — and a colour was four
/// separate <c>int</c> members with four <c>[TeProp(Bits = 8, Unsigned)]</c>
/// attributes. Both are now inferred from the member's type.
/// </para>
///
/// <para>
/// THE COLOUR IS THE INTERESTING ONE, because it is not one prop. There is no
/// <c>SendPropColor</c>: stock sends r, g, b and a as four eight-bit unsigned
/// ints, so one C# member has to become four wire props. That expansion is what
/// most of this file is about — a member that quietly became ONE prop would put
/// three colours' worth of bytes where the next field reads.
/// </para>
/// </summary>
public class TempEntColorAndAngleTests
{
    [TempEntity("CTeColourProbe", "DT_TeColourProbe")]
    private sealed class ColourProbe : TempEntState
    {
        [TeProp(Bits = -1, Flags = TeFlags.Coord)]
        public Vector Origin;

        /// <summary>Inferred: a facing, not a position.</summary>
        [TeProp(Bits = 13)]
        public QAngle Facing;

        /// <summary>Inferred: four eight-bit unsigned props.</summary>
        [TeProp]
        public Color32 Tint;

        [TeProp(Bits = 8, Flags = TeFlags.Unsigned)]
        public int Trailer;
    }

    private static TeClass Probe() => TeClassBuilder.Build(typeof(ColourProbe));

    [Fact]
    public void AFacingInfersItsOwnKindRatherThanAVectors()
    {
        TeField facing = Array.Find(Probe().Fields, f => f.MemberName == "Facing")!;

        Assert.True(facing.Kind == TePropKind.QAngles,
            $"a QAngle member is a SendPropQAngles, which the engine writes through bf_write's "
          + $"angle path and not as three coords; it inferred {facing.Kind}");

        TeField origin = Array.Find(Probe().Fields, f => f.MemberName == "Origin")!;

        Assert.True(origin.Kind == TePropKind.Vector,
            $"and a Vector still infers Vector, so the two are told apart by TYPE rather than by "
          + $"the table saying which; Origin inferred {origin.Kind}");
    }

    /// <summary>
    /// The expansion, asserted as four props and not as "it did something".
    /// </summary>
    [Fact]
    public void AColourBecomesFourUnsignedEightBitPropsNamedForTheMember()
    {
        TeField[] parts = Array.FindAll(Probe().Fields, f => f.Name.StartsWith("Tint"));

        Assert.True(parts.Length == 4,
            $"one Color32 member is FOUR props on the wire — there is no SendPropColor — and "
          + $"this built {parts.Length}");

        Assert.True(string.Join(",", Array.ConvertAll(parts, p => p.Name)) == "TintR,TintG,TintB,TintA",
            "named for the member so both sides derive the same spelling from the same "
          + $"declaration, in R G B A order; they came out [{string.Join(", ", Array.ConvertAll(parts, p => p.Name))}]");

        foreach (TeField p in parts)
        {
            Assert.True(p.Bits == 8 && (p.Flags & TeFlags.Unsigned) != 0,
                $"{p.Name} is eight bits UNSIGNED, like every colour component stock sends; "
              + $"it is {p.Bits} bits, flags {p.Flags}");

            Assert.True(p.Kind == TePropKind.Int, $"{p.Name} is an int prop, not {p.Kind}");
        }
    }

    /// <summary>
    /// THE OFFSETS, which is where a wrong expansion actually hurts. A colour is
    /// FOUR BYTES on the wire — stock's color32, not the sixteen a CLR Color32
    /// occupies — so its components are one byte apart and the field after it
    /// starts four on. A colour that became one prop would leave the next field
    /// reading the green component.
    /// </summary>
    [Fact]
    public void TheColoursFourPropsAreOneByteApartAndTheNextFieldClearsThem()
    {
        TeClass c = Probe();

        TeField r = Array.Find(c.Fields, f => f.Name == "TintR")!;
        TeField g = Array.Find(c.Fields, f => f.Name == "TintG")!;
        TeField b = Array.Find(c.Fields, f => f.Name == "TintB")!;
        TeField a = Array.Find(c.Fields, f => f.Name == "TintA")!;

        Assert.True(g.Offset == r.Offset + 1 && b.Offset == r.Offset + 2 && a.Offset == r.Offset + 3,
            "a colour is four BYTES on the wire, so its components are one byte apart; they are "
          + $"at [{r.Offset}, {g.Offset}, {b.Offset}, {a.Offset}]");

        Assert.True(Array.TrueForAll(new[] { r, g, b, a }, p => p.SizeBytes == 1),
            "and each occupies one byte, which is what makes the unsigned read the one that has "
          + "to run: a signed read of a byte slot sign-extends, and 200 comes back as -56");

        TeField trailer = Array.Find(c.Fields, f => f.MemberName == "Trailer")!;

        Assert.True(trailer.Offset >= a.Offset + 1,
            $"and the field after the colour starts past all four of them — it is at "
          + $"{trailer.Offset}, where the alpha ends at {a.Offset + 1}. A colour that took one "
          + "slot instead of four would put this one on top of the green");

        Assert.True(a.Offset - r.Offset + 1 == 4,
            $"the whole colour is four bytes wide on the wire, and this one spans "
          + $"{a.Offset - r.Offset + 1}");
    }

    /// <summary>
    /// Both types through the writer and back through the reader, which is the
    /// claim that matters: a declaration carrying them is one anybody can send.
    /// </summary>
    [Fact]
    public void BothSurviveTheRoundTripThroughTheDeclarationFormAndKeepTheirValues()
    {
        TeClass c = Probe();
        var type = new TempEntType(c, handle: 1,
                                   fields: BuildLookup(c), new RecordingLog());

        var sent = new ColourProbe
        {
            Origin  = new Vector(1f, 2f, 3f),
            Facing  = new QAngle(10f, 20f, 30f),
            Tint    = new Color32(200, 201, 202, 203),
            Trailer = 7,
        };

        byte[] blob = sent.ToBlob(type);
        var back = TempEntDeclarationReader.Read<ColourProbe>(new TempEntReader(type, blob));

        Assert.True(back.Facing == sent.Facing,
            $"the facing came back {back.Facing}, sent {sent.Facing}");

        Assert.True(back.Tint == sent.Tint,
            $"the colour came back {back.Tint}, sent {sent.Tint}");

        // 200..203 all have the top bit of a byte set, which is the range where
        // a signed read would come back negative -- and the components are
        // declared UNSIGNED, so they must not.
        Assert.True(back.Tint.R == 200 && back.Tint.A == 203,
            $"and every component is above 127, where an accidentally signed read shows up as a "
          + $"negative; R is {back.Tint.R} and A is {back.Tint.A}");

        Assert.True(back.Trailer == 7,
            $"and the field after the colour is untouched at {back.Trailer}, which it would not "
          + "be if the expansion had written the wrong number of bytes");
    }

    private static Dictionary<string, TeField> BuildLookup(TeClass c)
    {
        var into = new Dictionary<string, TeField>();

        foreach (TeField f in c.Fields)
            into[f.Name] = f;

        return into;
    }
}
