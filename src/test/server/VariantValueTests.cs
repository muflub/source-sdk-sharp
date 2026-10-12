//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>variant_t::Convert</c>'s table, edge by edge — including the holes.
///
/// <para>
/// THE HOLES ARE THE DELICATE PART. Stock's outer switch has four arms and the
/// types with no arm convert to NOTHING, which the caller then reads through an
/// accessor that also refuses, and gets a zero. That is not a crash and it is
/// not a warning; it is a map behaving differently from the way it was authored.
/// Every one of them is a named fact below.
/// </para>
/// </summary>
public class VariantConvertTableTests
{
    //-------------------------------------------------------------------------
    // The three rules before the switch.
    //-------------------------------------------------------------------------

    [Fact]
    public void ConvertingToItsOwnTypeSucceedsAndChangesNothing()
    {
        var value = VariantValue.FromFloat(5.7f);

        Assert.True(value.TryConvert(VariantFieldType.Float, out var converted));
        Assert.Equal(5.7f, converted.Float);
    }

    [Fact]
    public void ConvertingToVoidAlwaysSucceeds()
    {
        Assert.True(VariantValue.FromString("anything").TryConvert(VariantFieldType.Void, out var converted));
        Assert.Equal(VariantFieldType.Void, converted.Type);
    }

    /// <summary>"FIELD_INPUT accepts the variant type directly" — so it succeeds and converts nothing.</summary>
    [Fact]
    public void ConvertingToInputSucceedsAndLeavesTheTypeAlone()
    {
        Assert.True(VariantValue.FromInt(3).TryConvert(VariantFieldType.Input, out var converted));
        Assert.Equal(VariantFieldType.Integer, converted.Type);
        Assert.Equal(3, converted.Int);
    }

    //-------------------------------------------------------------------------
    // FIELD_INTEGER: two targets and no others.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnIntegerConvertsToAFloat()
    {
        Assert.True(VariantValue.FromInt(3).TryConvert(VariantFieldType.Float, out var converted));
        Assert.Equal(3f, converted.Float);
    }

    /// <summary>
    /// <c>SetFloat((float) iVal)</c>, so an integer past 2^24 loses digits on the
    /// way — 16777217 becomes 16777216 and nothing says so.
    /// </summary>
    [Fact]
    public void AnIntegerPastTwoToTheTwentyFourthRoundsOnTheWayToAFloat()
    {
        Assert.True(VariantValue.FromInt(16777217).TryConvert(VariantFieldType.Float, out var converted));
        Assert.Equal(16777216f, converted.Float);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    public void AnIntegerConvertsToABoolean(int value, bool expected)
    {
        Assert.True(VariantValue.FromInt(value).TryConvert(VariantFieldType.Boolean, out var converted));
        Assert.Equal(expected, converted.Bool);
    }

    [Theory]
    [InlineData(VariantFieldType.String)]
    [InlineData(VariantFieldType.Vector)]
    [InlineData(VariantFieldType.Color32)]
    [InlineData(VariantFieldType.EHandle)]
    public void AnIntegerConvertsToNothingElse(VariantFieldType target)
        => Assert.False(VariantValue.FromInt(3).TryConvert(target, out _));

    //-------------------------------------------------------------------------
    // FIELD_FLOAT: two targets and no others.
    //-------------------------------------------------------------------------

    [Theory]
    [InlineData(5.7f, 5)]
    [InlineData(-5.7f, -5)]
    [InlineData(0.5f, 0)]
    [InlineData(-0.5f, 0)]
    public void AFloatTruncatesTowardsZeroToAnInteger(float value, int expected)
    {
        Assert.True(VariantValue.FromFloat(value).TryConvert(VariantFieldType.Integer, out var converted));
        Assert.Equal(expected, converted.Int);
    }

    /// <summary>
    /// A float too big for an integer is undefined behaviour in C, and what
    /// x86-64 does is <c>cvttss2si</c>'s "integer indefinite" — <c>INT_MIN</c>,
    /// whichever direction it overflowed. So a value of three billion converts
    /// to a large NEGATIVE integer.
    /// </summary>
    [Theory]
    [InlineData(3e9f)]
    [InlineData(-3e9f)]
    [InlineData(1e30f)]
    public void AFloatThatDoesNotFitBecomesIntMin(float value)
    {
        Assert.True(VariantValue.FromFloat(value).TryConvert(VariantFieldType.Integer, out var converted));
        Assert.Equal(int.MinValue, converted.Int);
    }

    [Theory]
    [InlineData(0f, false)]
    [InlineData(0.5f, true)]
    [InlineData(-0.5f, true)]
    public void AFloatConvertsToABoolean(float value, bool expected)
    {
        Assert.True(VariantValue.FromFloat(value).TryConvert(VariantFieldType.Boolean, out var converted));
        Assert.Equal(expected, converted.Bool);
    }

    [Theory]
    [InlineData(VariantFieldType.String)]
    [InlineData(VariantFieldType.Vector)]
    [InlineData(VariantFieldType.Color32)]
    public void AFloatConvertsToNothingElse(VariantFieldType target)
        => Assert.False(VariantValue.FromFloat(1f).TryConvert(target, out _));

    //-------------------------------------------------------------------------
    // FIELD_STRING: the arm that matters, since every map value is one.
    //-------------------------------------------------------------------------

    [Fact]
    public void AStringConvertsToAnInteger()
    {
        Assert.True(VariantValue.FromString("42").TryConvert(VariantFieldType.Integer, out var converted));
        Assert.Equal(42, converted.Int);
    }

    [Fact]
    public void AStringConvertsToAFloat()
    {
        Assert.True(VariantValue.FromString("2.5").TryConvert(VariantFieldType.Float, out var converted));
        Assert.Equal(2.5f, converted.Float);
    }

    /// <summary>
    /// <b>THE UGLY ONE.</b> The boolean target reads with <c>atoi</c> while the
    /// float target reads with <c>atof</c>, so one string is two answers: "0.5"
    /// is <c>false</c> and 0.5, and "0x10" is <c>false</c> and 16.
    /// </summary>
    [Theory]
    [InlineData("0.5")]
    [InlineData("0x10")]
    [InlineData(".9")]
    public void AFractionalStringIsFalseButNotZero(string text)
    {
        var value = VariantValue.FromString(text);

        Assert.True(value.TryConvert(VariantFieldType.Boolean, out var asBool));
        Assert.False(asBool.Bool);

        Assert.True(value.TryConvert(VariantFieldType.Float, out var asFloat));
        Assert.NotEqual(0f, asFloat.Float);
    }

    [Fact]
    public void AStringConvertsToAVector()
    {
        Assert.True(VariantValue.FromString("[1 2 3]").TryConvert(VariantFieldType.Vector, out var converted));
        Assert.Equal(new Vector(1f, 2f, 3f), converted.Vector3D);
    }

    /// <summary>
    /// <b>THE UGLY ONE.</b> <c>FIELD_VECTOR</c> has an arm and
    /// <c>FIELD_POSITION_VECTOR</c> does not, though they hold the same three
    /// floats — so a string asked for a world coordinate is refused.
    /// </summary>
    [Fact]
    public void AStringDoesNotConvertToAPositionVector()
        => Assert.False(VariantValue.FromString("[1 2 3]")
                                    .TryConvert(VariantFieldType.PositionVector, out _));

    [Fact]
    public void AStringConvertsToAColour()
    {
        Assert.True(VariantValue.FromString("255 128 64").TryConvert(VariantFieldType.Color32, out var converted));
        Assert.Equal(new Color32(255, 128, 64, 255), converted.Color);
    }

    /// <summary>
    /// The name lookup arm. Stock is <c>gEntList.FindEntityByName</c>; here it is
    /// passed in, so a test can pin the arm without a world.
    /// </summary>
    [Fact]
    public void AStringConvertsToAnEntityByName()
    {
        var found = new EntityHandle(7);

        Assert.True(VariantValue.FromString("door")
                                .TryConvert(VariantFieldType.EHandle, out var converted,
                                            name => name == "door" ? found : EntityHandle.None));

        Assert.Equal(found, converted.Entity);
    }

    /// <summary>A name that matches nothing is still a SUCCESSFUL conversion, to no entity.</summary>
    [Fact]
    public void AStringNamingNothingConvertsToAnEmptyHandle()
    {
        Assert.True(VariantValue.FromString("nosuchthing")
                                .TryConvert(VariantFieldType.EHandle, out var converted,
                                            _ => EntityHandle.None));

        Assert.Equal(EntityHandle.None, converted.Entity);
    }

    //-------------------------------------------------------------------------
    // FIELD_EHANDLE: one target, and it succeeds even when it does nothing.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnEntityConvertsToItsTargetname()
    {
        var handle = new EntityHandle(11);
        Func<EntityHandle, string> nameOf = h => h == handle ? "lift_door" : string.Empty;

        Assert.True(VariantValue.FromEntity(handle)
                                .TryConvert(VariantFieldType.String, out var converted, nameOf: nameOf));
        Assert.Equal("lift_door", converted.RawString);
    }

    /// <summary>
    /// <b>THE UGLY ONE.</b> Stock returns <c>true</c> from inside the arm having
    /// assigned nothing when the handle is empty, so a "successful" conversion
    /// leaves an ehandle where the caller now believes there is a string. What
    /// it spells as is <c>"&lt;&lt;null entity&gt;&gt;"</c>.
    /// </summary>
    [Fact]
    public void AnEmptyEntityConvertsToAStringSuccessfullyWithoutBecomingOne()
    {
        var value = VariantValue.FromEntity(EntityHandle.None);

        Assert.True(value.TryConvert(VariantFieldType.String, out var converted));
        Assert.Equal(VariantFieldType.EHandle, converted.Type);
        Assert.Equal("<<null entity>>", converted.AsText);
    }

    [Theory]
    [InlineData(VariantFieldType.Float)]
    [InlineData(VariantFieldType.Integer)]
    [InlineData(VariantFieldType.Boolean)]
    public void AnEntityConvertsToNothingElse(VariantFieldType target)
        => Assert.False(VariantValue.FromEntity(new EntityHandle(3)).TryConvert(target, out _));

    //-------------------------------------------------------------------------
    // The types with no arm at all.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <b>THE UGLY ONE, AND THE ONE THAT REACHES FURTHEST.</b> There is no
    /// <c>case FIELD_BOOLEAN</c> in stock's switch, so a boolean converts to
    /// nothing — and an input fired with a boolean <c>true</c> therefore carries
    /// the NUMBER ZERO to the handler, exactly as a boolean <c>false</c> would.
    /// </summary>
    [Theory]
    [InlineData(VariantFieldType.Float)]
    [InlineData(VariantFieldType.Integer)]
    [InlineData(VariantFieldType.String)]
    public void ABooleanConvertsToNothing(VariantFieldType target)
        => Assert.False(VariantValue.FromBool(true).TryConvert(target, out _));

    [Fact]
    public void ABooleanTrueCarriesTheNumberZero()
        => Assert.Equal(0f, VariantValue.FromBool(true).AsNumber);

    [Theory]
    [InlineData(VariantFieldType.Float)]
    [InlineData(VariantFieldType.String)]
    [InlineData(VariantFieldType.PositionVector)]
    public void AVectorConvertsToNothing(VariantFieldType target)
        => Assert.False(VariantValue.FromVector(new Vector(1f, 2f, 3f)).TryConvert(target, out _));

    [Theory]
    [InlineData(VariantFieldType.Float)]
    [InlineData(VariantFieldType.String)]
    [InlineData(VariantFieldType.Vector)]
    public void APositionVectorConvertsToNothing(VariantFieldType target)
        => Assert.False(VariantValue.FromPositionVector(new Vector(1f, 2f, 3f)).TryConvert(target, out _));

    [Theory]
    [InlineData(VariantFieldType.Float)]
    [InlineData(VariantFieldType.String)]
    [InlineData(VariantFieldType.Integer)]
    public void AColourConvertsToNothing(VariantFieldType target)
        => Assert.False(VariantValue.FromColor(new Color32(1, 2, 3)).TryConvert(target, out _));

    [Theory]
    [InlineData(VariantFieldType.Float)]
    [InlineData(VariantFieldType.String)]
    [InlineData(VariantFieldType.Vector)]
    public void AVoidConvertsToNothing(VariantFieldType target)
        => Assert.False(VariantValue.Void.TryConvert(target, out _));

    /// <summary>A refused conversion hands back the value UNCHANGED, as stock leaves it.</summary>
    [Fact]
    public void ARefusedConversionLeavesTheValueAlone()
    {
        var value = VariantValue.FromVector(new Vector(1f, 2f, 3f));

        Assert.False(value.TryConvert(VariantFieldType.Float, out var converted));
        Assert.Equal(VariantFieldType.Vector, converted.Type);
        Assert.Equal(new Vector(1f, 2f, 3f), converted.Vector3D);
    }
}

/// <summary>
/// <c>variant_t</c>'s accessors, each of which refuses a type it does not hold —
/// which is where "a map sends 3 as a string and the counter added nothing"
/// comes from.
/// </summary>
public class VariantAccessorTests
{
    [Fact]
    public void FloatOfAStringIsZeroEvenWhenTheStringIsANumber()
        => Assert.Equal(0f, VariantValue.FromString("3").Float);

    [Fact]
    public void IntOfAFloatIsZero()
        => Assert.Equal(0, VariantValue.FromFloat(3f).Int);

    [Fact]
    public void BoolOfAnIntegerIsFalse()
        => Assert.False(VariantValue.FromInt(1).Bool);

    [Fact]
    public void Vector3DOfAStringIsTheOrigin()
        => Assert.Equal(new Vector(0f, 0f, 0f), VariantValue.FromString("[1 2 3]").Vector3D);

    /// <summary>A position vector reads through the same accessor as a vector, which stock allows.</summary>
    [Fact]
    public void Vector3DOfAPositionVectorIsTheVector()
        => Assert.Equal(new Vector(1f, 2f, 3f),
                        VariantValue.FromPositionVector(new Vector(1f, 2f, 3f)).Vector3D);

    [Fact]
    public void EntityOfANonEntityIsEmpty()
        => Assert.Equal(EntityHandle.None, VariantValue.FromInt(3).Entity);

    /// <summary>
    /// <b>DIFFERS FROM STOCK, DELIBERATELY.</b> Stock's <c>Color32()</c> has no
    /// type check at all: it returns the union's bytes whatever the variant
    /// holds, so a float variant answers with the four bytes of the float. That
    /// is reachable only from a caller that forgot to convert first, and
    /// reproducing it would mean laying out an explicit union to preserve a
    /// caller's mistake. This refuses in the same shape every other accessor
    /// refuses.
    /// </summary>
    [Fact]
    public void ColorOfANonColourIsTheDefaultRatherThanTheUnionsBytes()
        => Assert.Equal(default, VariantValue.FromFloat(5.7f).Color);
}

/// <summary>
/// <c>variant_t::ToString</c>, which is what a value CROSSES AS. A wrong
/// spelling here is a wrong value delivered to a handler, not a cosmetic
/// difference in a log.
/// </summary>
public class VariantToStringTests
{
    [Fact]
    public void AStringSpellsItself()
        => Assert.Equal("hello", VariantValue.FromString("hello").AsText);

    [Fact]
    public void AVoidSpellsAsNothing()
        => Assert.Equal(string.Empty, VariantValue.Void.AsText);

    /// <summary>
    /// A boolean spells as a WORD here while a map WRITES booleans as 0 and 1,
    /// so the spelling does not survive a round trip: <c>atoi("true")</c> is 0.
    /// </summary>
    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void ABooleanSpellsAsAWord(bool value, string expected)
        => Assert.Equal(expected, VariantValue.FromBool(value).AsText);

    [Fact]
    public void ABooleanSpellingDoesNotSurviveTheRoundTrip()
    {
        string spelled = VariantValue.FromBool(true).AsText;

        Assert.True(VariantValue.FromString(spelled).TryConvert(VariantFieldType.Boolean, out var back));
        Assert.False(back.Bool);
    }

    [Fact]
    public void AnIntegerSpellsItsDigits()
        => Assert.Equal("-42", VariantValue.FromInt(-42).AsText);

    [Fact]
    public void AFloatSpellsWithSixSignificantDigits()
        => Assert.Equal("1.67772e+07", VariantValue.FromFloat(16777216f).AsText);

    [Fact]
    public void AVectorSpellsInBrackets()
        => Assert.Equal("[1.5 -2.5 3.25]",
                        VariantValue.FromVector(new Vector(1.5f, -2.5f, 3.25f)).AsText);

    [Fact]
    public void AColourSpellsFourChannels()
        => Assert.Equal("255 128 64 32", VariantValue.FromColor(new Color32(255, 128, 64, 32)).AsText);

    /// <summary>
    /// <b>THE UGLY ONE.</b> <c>ToString</c>'s switch has a <c>FIELD_VECTOR</c>
    /// case and no <c>FIELD_POSITION_VECTOR</c> case, so a position vector falls
    /// off the end and comes back as the literal words "No conversion to string"
    /// — which is then the VALUE the input carries. It reads like an error
    /// message because it is one, and it is delivered as data.
    /// </summary>
    [Fact]
    public void APositionVectorSpellsAsAnErrorMessage()
        => Assert.Equal("No conversion to string",
                        VariantValue.FromPositionVector(new Vector(1f, 2f, 3f)).AsText);

    [Fact]
    public void AnEmptyEntitySpellsAsNullEntity()
        => Assert.Equal("<<null entity>>", VariantValue.FromEntity(EntityHandle.None).AsText);

    [Fact]
    public void AnEntitySpellsAsItsTargetname()
    {
        var handle = new EntityHandle(4);

        Assert.Equal("elevator", VariantValue.FromEntity(handle).AsTextWith(_ => "elevator"));
    }

    /// <summary>
    /// The equivalence the shim's <c>Convert(FIELD_STRING)</c> was relying on
    /// without saying so: <c>String()</c> falls back to <c>ToString()</c>, and
    /// <c>ToString</c>'s ehandle case spells the same targetname the conversion
    /// would have assigned. So converting first changed the TYPE and never the
    /// characters — which is why the call could be dropped.
    /// </summary>
    [Fact]
    public void ConvertingToAStringFirstNeverChangesTheCharacters()
    {
        var handle = new EntityHandle(9);
        Func<EntityHandle, string> nameOf = _ => "gate";

        VariantValue[] values =
        {
            VariantValue.Void,
            VariantValue.FromString("plain"),
            VariantValue.FromBool(true),
            VariantValue.FromInt(-7),
            VariantValue.FromFloat(5.7f),
            VariantValue.FromVector(new Vector(1f, 2f, 3f)),
            VariantValue.FromPositionVector(new Vector(1f, 2f, 3f)),
            VariantValue.FromColor(new Color32(1, 2, 3, 4)),
            VariantValue.FromEntity(handle),
            VariantValue.FromEntity(EntityHandle.None),
        };

        foreach (var value in values)
        {
            value.TryConvert(VariantFieldType.String, out var converted, nameOf: nameOf);
            Assert.Equal(value.AsTextWith(nameOf), converted.AsTextWith(nameOf));
        }
    }
}

/// <summary>
/// That <see cref="VariantFieldType"/> still carries <c>fieldtype_t</c>'s own
/// numbers, read out of the header rather than remembered.
///
/// <para>
/// The tag crosses the boundary as an <c>int32</c> beside a value the native
/// side read out of a real <c>variant_t</c>. A renumbering would retype every
/// input in the game silently — the map still loads, the entity still fires,
/// and a colour arrives as a vector.
/// </para>
/// </summary>
public class VariantFieldTypeTests
{
    [Theory]
    [InlineData(VariantFieldType.Void, "FIELD_VOID")]
    [InlineData(VariantFieldType.Float, "FIELD_FLOAT")]
    [InlineData(VariantFieldType.String, "FIELD_STRING")]
    [InlineData(VariantFieldType.Vector, "FIELD_VECTOR")]
    [InlineData(VariantFieldType.Integer, "FIELD_INTEGER")]
    [InlineData(VariantFieldType.Boolean, "FIELD_BOOLEAN")]
    [InlineData(VariantFieldType.Color32, "FIELD_COLOR32")]
    [InlineData(VariantFieldType.EHandle, "FIELD_EHANDLE")]
    [InlineData(VariantFieldType.PositionVector, "FIELD_POSITION_VECTOR")]
    [InlineData(VariantFieldType.Input, "FIELD_INPUT")]
    public void EveryTagHasTheNumberDatamapGivesIt(VariantFieldType tag, string nativeName)
        => Assert.Equal(FieldTypes.NumberOf(nativeName), (int)tag);

    [Fact]
    public void TheHeaderWasActuallyRead()
        => Assert.True(FieldTypes.Count >= 29,
                       $"only {FieldTypes.Count} field types were read from {FieldTypes.Path}");
}

/// <summary>
/// <c>enum _fieldtypes</c> from <c>src/public/datamap.h</c>, numbered by
/// position exactly as C does it. There are no explicit values in that enum
/// after <c>FIELD_VOID = 0</c>, so position IS the number.
/// </summary>
internal static class FieldTypes
{
    private static readonly Dictionary<string, int> s_numbers = Read();

    internal static string? Path { get; private set; }

    internal static int Count => s_numbers.Count;

    internal static int NumberOf(string name)
    {
        Assert.True(s_numbers.TryGetValue(name, out int number),
                    $"{name} is not in datamap.h's enum _fieldtypes ({Path})");
        return number;
    }

    private static Dictionary<string, int> Read()
    {
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);

        string candidate = RepoLayout.Sdk("src", "public", "datamap.h");

        if (File.Exists(candidate))
        {
            Path = candidate;

        bool inside = false;
        int next = 0;

        foreach (string raw in File.ReadAllLines(candidate))
        {
            string line = raw.Trim();

            if (line.StartsWith("typedef enum _fieldtypes", StringComparison.Ordinal))
            {
                inside = true;
                continue;
            }

            if (!inside)
                continue;

            if (line.StartsWith("} fieldtype_t", StringComparison.Ordinal))
                break;

            // A member line is "FIELD_NAME," or "FIELD_NAME = 0," possibly
            // with a trailing comment. Anything else in the block -- a
            // comment line, a blank -- is not a member and does not consume
            // a number.
            int comma = line.IndexOf(',');
            if (comma < 0 || !line.StartsWith("FIELD_", StringComparison.Ordinal))
                continue;

            string member = line[..comma];
            int equals = member.IndexOf('=');

            if (equals >= 0)
            {
                next = int.Parse(member[(equals + 1)..].Trim());
                member = member[..equals].Trim();
            }

            numbers[member.Trim()] = next;
            next++;
        }
        }

        return numbers;
    }
}

/// <summary>
/// That a value survives the crossing: what the shim puts on the wire is enough
/// to rebuild what a handler is owed.
///
/// <para>
/// The wire is three fields — the variant's type (as <c>nInt0</c>, plus one),
/// its text (<c>pText2</c>, which is <c>variant_t::String()</c>) and its number
/// (<c>flFloat0</c>). <see cref="VariantValue.FromCrossing"/> is the far end.
/// What a handler is owed is the TEXT and the NUMBER stock would have given it,
/// so those are what the round trip asserts.
/// </para>
/// </summary>
public class VariantCrossingTests
{
    private static VariantValue Cross(VariantValue value, Func<EntityHandle, string>? nameOf = null)
        => VariantValue.FromCrossing((int)value.Type + 1, value.AsTextWith(nameOf), value.AsNumber);

    [Fact]
    public void AStringSurvives()
    {
        var crossed = Cross(VariantValue.FromString("3"));

        Assert.Equal("3", crossed.AsText);
        Assert.Equal(3f, crossed.AsNumber);
    }

    [Fact]
    public void AVoidSurvivesAsAVoid()
    {
        var crossed = Cross(VariantValue.Void);

        Assert.Equal(VariantFieldType.Void, crossed.Type);
        Assert.Equal(string.Empty, crossed.AsText);
        Assert.Equal(0f, crossed.AsNumber);
    }

    /// <summary>
    /// An integer past 2^24 comes back EXACT, because it is rebuilt from the
    /// text and not from the number — the number lost digits on the way out,
    /// exactly as stock's <c>SetFloat((float) iVal)</c> loses them.
    /// </summary>
    [Fact]
    public void AnIntegerSurvivesExactlyThoughItsNumberDoesNot()
    {
        var crossed = Cross(VariantValue.FromInt(16777217));

        Assert.Equal(16777217, crossed.Int);
        Assert.Equal("16777217", crossed.AsText);
        Assert.Equal(16777216f, crossed.AsNumber);
    }

    /// <summary>
    /// A boolean's number is zero from a HOLE in <c>Convert</c> rather than from
    /// its value, so <c>true</c> and <c>false</c> cross indistinguishable as
    /// numbers and are told apart only by their text.
    /// </summary>
    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void ABooleanSurvivesThroughItsWordAndNotItsNumber(bool value, string expected)
    {
        var crossed = Cross(VariantValue.FromBool(value));

        Assert.Equal(VariantFieldType.Boolean, crossed.Type);
        Assert.Equal(value, crossed.Bool);
        Assert.Equal(expected, crossed.AsText);
        Assert.Equal(0f, crossed.AsNumber);
    }

    [Fact]
    public void AFloatSurvivesExactlyThroughTheNumberEvenWhereItsTextIsLossy()
    {
        var crossed = Cross(VariantValue.FromFloat(12345.6789f));

        Assert.Equal(12345.6789f, crossed.Float);
        Assert.Equal("12345.7", crossed.AsText);
    }

    [Fact]
    public void AVectorSurvives()
    {
        var crossed = Cross(VariantValue.FromVector(new Vector(1.5f, -2.5f, 3.25f)));

        Assert.Equal(new Vector(1.5f, -2.5f, 3.25f), crossed.Vector3D);
        Assert.Equal("[1.5 -2.5 3.25]", crossed.AsText);
    }

    /// <summary>
    /// <b>A COST, RECORDED.</b> A vector crosses as its <c>%g</c> spelling, so a
    /// component needing more than six significant digits comes back rounded.
    /// That is stock's loss and not this port's — the text IS what stock's
    /// ToString produces — but it is a real one and it belongs written down.
    /// </summary>
    [Fact]
    public void AVectorComponentPastSixDigitsIsRoundedByTheCrossing()
    {
        var crossed = Cross(VariantValue.FromVector(new Vector(12345.6789f, 0f, 0f)));

        Assert.Equal(12345.7f, crossed.Vector3D.X);
    }

    [Fact]
    public void AColourSurvives()
    {
        var crossed = Cross(VariantValue.FromColor(new Color32(255, 128, 64, 32)));

        Assert.Equal(new Color32(255, 128, 64, 32), crossed.Color);
        Assert.Equal("255 128 64 32", crossed.AsText);
    }

    /// <summary>
    /// A position vector loses its components and keeps its number, because
    /// stock's <c>ToString</c> never spelled them: what crossed is the words
    /// "No conversion to string".
    /// </summary>
    [Fact]
    public void APositionVectorCrossesAsStocksErrorMessageAndTheOrigin()
    {
        var crossed = Cross(VariantValue.FromPositionVector(new Vector(1f, 2f, 3f)));

        Assert.Equal(VariantFieldType.PositionVector, crossed.Type);
        Assert.Equal("No conversion to string", crossed.AsText);
        Assert.Equal(new Vector(0f, 0f, 0f), crossed.Vector3D);
        Assert.Equal(0f, crossed.AsNumber);
    }

    /// <summary>
    /// An entity crosses as its targetname, and stays an EHANDLE at the far end.
    /// Storing it as a string instead would put a name through <c>atof</c>, and
    /// an entity called "3door" would deliver the number three where stock
    /// delivers nothing.
    /// </summary>
    [Fact]
    public void AnEntityCrossesAsItsNameAndStillCarriesNoNumber()
    {
        var crossed = Cross(VariantValue.FromEntity(new EntityHandle(5)), _ => "3door");

        Assert.Equal(VariantFieldType.EHandle, crossed.Type);
        Assert.Equal("3door", crossed.AsText);
        Assert.Equal(0f, crossed.AsNumber);
    }

    [Fact]
    public void ANameLookupReachesOnlyTheCallThatWasGivenIt()
    {
        // The property the static hook lacked: one caller's directory is not
        // the name every other caller's ehandle spells as.
        var handle = new EntityHandle(6);

        Assert.Equal("mine", VariantValue.FromEntity(handle).AsTextWith(_ => "mine"));
        Assert.Equal("<<null entity>>", VariantValue.FromEntity(handle).AsTextWith(null));
    }

    /// <summary>
    /// A shim that did not write the type writes zero, and zero is read as "did
    /// not say" rather than as <see cref="VariantFieldType.Void"/> — which is
    /// why the wire carries the type PLUS ONE. A one-sided slot answering a
    /// plausible zero has cost this port two lanes.
    /// </summary>
    [Fact]
    public void ATypeOfZeroIsTakenAsAStringRatherThanAsVoid()
    {
        var value = VariantValue.FromCrossing(0, "3", 3f);

        Assert.Equal(VariantFieldType.String, value.Type);
        Assert.Equal(3f, value.AsNumber);
    }

    [Fact]
    public void ATypeThisBuildDoesNotKnowIsTakenAsAString()
    {
        var value = VariantValue.FromCrossing(999, "7", 7f);

        Assert.Equal(VariantFieldType.String, value.Type);
        Assert.Equal(7f, value.AsNumber);
    }

    /// <summary>
    /// The whole reason the type is on the wire: two types whose TEXT parses as
    /// a number yield ZERO in stock, and deriving the number from the text alone
    /// would deliver 255 and 0 where the map expects nothing.
    /// </summary>
    [Fact]
    public void AColoursTextParsesAsANumberAndItsValueIsStillZero()
    {
        var colour = VariantValue.FromColor(new Color32(255, 128, 64, 32));

        Assert.Equal(255f, VariantParse.Atof(colour.AsText));
        Assert.Equal(0f, Cross(colour).AsNumber);
    }
}
