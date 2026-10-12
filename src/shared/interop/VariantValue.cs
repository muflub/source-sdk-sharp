//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

namespace SourceSharp.Abi;

/// <summary>
/// The type tag on a <see cref="VariantValue"/>: <c>fieldtype_t</c>'s own
/// numbers, for the subset <c>variant_t</c> can actually hold.
///
/// <para>
/// THE NUMBERS ARE STOCK'S, not a fresh enumeration, because this tag crosses
/// the boundary as an <c>int32</c> beside a value the native side read out of a
/// real <c>variant_t</c>. A renumbering here would be a silent retype of every
/// input in the game — the map still loads, the entity still fires, and a
/// colour arrives as a vector. <c>datamap.h</c>'s <c>enum _fieldtypes</c> is the
/// definition; <c>VariantFieldTypeTests</c> pins each number against it.
/// </para>
///
/// <para>
/// <see cref="Input"/> is in the list because <c>Convert</c> has a rule for it —
/// "FIELD_INPUT accepts the variant type directly" — not because a variant is
/// ever tagged with it. Nothing constructs one.
/// </para>
/// </summary>
public enum VariantFieldType
{
    /// <summary>No value. What an input fired with no parameter carries.</summary>
    Void = 0,

    Float = 1,

    /// <summary>What a map writes. Every parameter override and every <c>ent_fire</c> is one.</summary>
    String = 2,

    Vector = 3,
    Integer = 5,
    Boolean = 6,
    Color32 = 9,
    EHandle = 13,

    /// <summary>A world coordinate. Distinct from <see cref="Vector"/> only in save/restore fixup.</summary>
    PositionVector = 15,

    /// <summary>A conversion TARGET meaning "whatever you already are". Never a tag.</summary>
    Input = 20,
}

/// <summary>
/// The C runtime, as stock's <c>variant_t::Convert</c> and
/// <c>variant_t::ToString</c> use it.
///
/// <para>
/// <b>THIS IS A STAND-IN FOR NATIVE CODE AND STAND-INS DRIFT.</b> Every method
/// here reproduces one libc call that stock makes on a map's value, and the
/// whole point is that it answers what libc answers rather than what C# finds
/// natural — the two disagree about empty strings, malformed numbers, hex, and
/// values that do not fit. So the agreement is ASSERTED rather than assumed:
/// <c>src/sourcesharp/test/variant_convert_test.cpp</c> runs the same table
/// through the real <c>atof</c>, <c>atoi</c>, <c>sscanf</c> and <c>snprintf</c>
/// this game links against and expects the same answers
/// <c>VariantParseTests</c> expects here. A divergence fails one of the two.
/// </para>
///
/// <para>
/// The table itself came from <c>tools/variant-oracle.c</c>, run against this
/// machine's glibc. It is not remembered, and it is not what a C# parser would
/// have produced.
/// </para>
/// </summary>
public static class VariantParse
{
    private static CultureInfo Culture => CultureInfo.InvariantCulture;

    /// <summary>
    /// <c>atof</c>: the longest leading prefix of <paramref name="text"/> that is
    /// a number, and <c>0</c> when there is none.
    ///
    /// <para>
    /// GLIBC'S <c>atof</c> IS <c>strtod</c>, which is a wider grammar than a map
    /// author would guess and wider than <c>float.TryParse</c> by a long way. It
    /// takes an exponent (<c>"1e3"</c> is 1000, and a hand-rolled digit scanner
    /// that stops at the <c>e</c> answers 1); it takes C99 hex floats
    /// (<c>"0x10"</c> is 16, while <c>atoi</c> of the same string is 0, so a
    /// value that is sixteen as a float is zero as an integer and FALSE as a
    /// boolean); it takes <c>"inf"</c> and <c>"nan"</c>; and it OVERFLOWS TO
    /// INFINITY rather than failing. Every one of those reaches a map, because
    /// <c>ent_fire</c> and <c>AddOutput</c> put arbitrary text here.
    /// </para>
    ///
    /// <para>
    /// It also takes a prefix rather than the whole string: <c>atof("2x")</c> is
    /// 2 and <c>atof("0.5 ")</c> is 0.5, which is what makes a mapper's stray
    /// space harmless. <c>TryParse</c> refuses both, and refusing them is how a
    /// delay of 0.5 becomes a delay of zero.
    /// </para>
    /// </summary>
    public static float Atof(string? text)
    {
        if (text is null)
            return 0f;

        int end = FloatPrefix(text, 0);
        if (end < 0)
            return 0f;

        return ParseFloatPrefix(text, 0, end);
    }

    /// <summary>
    /// <c>atoi</c>: the longest leading DECIMAL prefix, with stock's overflow.
    ///
    /// <para>
    /// GLIBC'S <c>atoi</c> IS <c>(int)strtol(s, NULL, 10)</c>, and both halves of
    /// that show. <c>strtol</c> saturates at <c>LONG_MAX</c> — 64 bits here —
    /// and the cast then keeps the low 32, so <c>atoi("2147483648")</c> is
    /// <c>-2147483648</c>, <c>atoi("3000000000")</c> is <c>-1294967296</c>, and
    /// <c>atoi("9999999999999999999999")</c> is <c>-1</c>. A map that writes a
    /// number too big for an integer gets a NEGATIVE one, silently, and that is
    /// the value the entity acts on.
    /// </para>
    ///
    /// <para>
    /// BASE TEN ONLY, unlike <see cref="Atof"/>: no hex, no exponent, no
    /// decimal point. <c>atoi("0x10")</c> is 0, <c>atoi("5.7")</c> is 5,
    /// <c>atoi("1e3")</c> is 1.
    /// </para>
    /// </summary>
    public static int Atoi(string? text)
    {
        if (text is null)
            return 0;

        return ScanLong(text, 0, out _);
    }

    /// <summary>
    /// <c>%i</c> — an integer as <c>Q_snprintf</c> writes it, which is its
    /// digits and nothing else.
    /// </summary>
    public static string FormatI(int value) => value.ToString(Culture);

    /// <summary>
    /// <c>%g</c> with the default precision of six, as <c>ToString</c> spells
    /// every float and every component of every vector.
    ///
    /// <para>
    /// SIX SIGNIFICANT DIGITS, AND THAT IS LOSSY. <c>16777216</c> comes out
    /// <c>"1.67772e+07"</c> and <c>12345.6789</c> comes out <c>"12345.7"</c>.
    /// It matters because the string form is what a value crosses to managed
    /// code AS, so a float that goes out through here and is read back is not
    /// the float that went in — which is exactly why the numeric payload
    /// crosses beside the text rather than being recovered from it.
    /// </para>
    ///
    /// <para>
    /// <c>%g</c> is not any of C#'s standard numeric formats. It picks fixed or
    /// scientific by the decimal exponent — fixed while <c>-4 &lt;= X &lt; 6</c>,
    /// scientific otherwise — writes at least two exponent digits, lower-case
    /// <c>e</c>, and strips trailing zeros from either. <c>"G6"</c> agrees with
    /// none of that: it would give <c>"1.67772E+07"</c>. And the value formatted
    /// is the float PROMOTED TO DOUBLE, because that is what a varargs call
    /// hands <c>printf</c>.
    /// </para>
    /// </summary>
    public static string FormatG(float value)
    {
        if (float.IsNaN(value))
            return "nan";
        if (float.IsPositiveInfinity(value))
            return "inf";
        if (float.IsNegativeInfinity(value))
            return "-inf";

        // The varargs promotion, made explicit: printf never sees the float.
        double d = value;

        if (d == 0.0)
            return double.IsNegative(d) ? "-0" : "0";

        const int Precision = 6;

        // The exponent %g decides on is the one %e would print AFTER rounding to
        // Precision-1 places -- 999999.5 rounds up into a different bracket than
        // its unrounded exponent would suggest. So take it from the rounded form
        // rather than from a logarithm.
        string scientific = d.ToString("E" + (Precision - 1).ToString(Culture), Culture);
        int marker = scientific.IndexOf('E');
        int exponent = int.Parse(scientific.AsSpan(marker + 1), NumberStyles.Integer, Culture);

        if (exponent >= -4 && exponent < Precision)
            return TrimTrailingZeros(d.ToString("F" + (Precision - 1 - exponent).ToString(Culture), Culture));

        string mantissa = TrimTrailingZeros(scientific[..marker]);
        int magnitude = Math.Abs(exponent);

        return mantissa + "e" + (exponent < 0 ? "-" : "+")
             + (magnitude < 10 ? "0" + magnitude.ToString(Culture) : magnitude.ToString(Culture));
    }

    /// <summary>
    /// <c>%d %d %d %d</c> into <c>(0, 0, 0, 255)</c>, then eight bits a channel:
    /// stock's whole string-to-colour conversion.
    ///
    /// <para>
    /// THE RETURN VALUE IS NOT CHECKED, in stock, and that is the behaviour
    /// rather than an oversight to tidy: a channel <c>sscanf</c> never reached
    /// keeps its initialiser, so <c>"255 128"</c> is opaque <c>(255,128,0)</c>
    /// and <c>""</c> is opaque black. There is no such thing as an unreadable
    /// colour here.
    /// </para>
    ///
    /// <para>
    /// AND THE CHANNELS TRUNCATE. <c>sscanf</c> reads into <c>int</c> and
    /// <c>SetColor32</c> assigns into a byte, so <c>"300 -1 0 0"</c> is
    /// <c>(44, 255, 0, 0)</c>. A mapper who writes 300 for "brighter" gets 44.
    /// </para>
    /// </summary>
    public static Color32 ScanColor(string? text)
    {
        int red = 0, green = 0, blue = 0, alpha = 255;

        if (text is not null)
        {
            int pos = 0;

            if (ScanInt(text, ref pos, out int v0))
            {
                red = v0;
                if (ScanInt(text, ref pos, out int v1))
                {
                    green = v1;
                    if (ScanInt(text, ref pos, out int v2))
                    {
                        blue = v2;
                        if (ScanInt(text, ref pos, out int v3))
                            alpha = v3;
                    }
                }
            }
        }

        return new Color32((byte)red, (byte)green, (byte)blue, (byte)alpha);
    }

    /// <summary>
    /// <c>sscanf(s, "[%f %f %f]")</c>, and only if that assigned NOTHING,
    /// <c>sscanf(s, "%f %f %f")</c>: stock's whole string-to-vector conversion,
    /// including the retry.
    ///
    /// <para>
    /// THE TEST IS <c>== 0</c>, NOT <c>== 3</c>, and the difference is a wrong
    /// answer rather than a refusal. <c>"[1 2"</c> assigns two floats, so the
    /// retry never happens and the vector is <c>(1, 2, 0)</c> — a truncated
    /// vector that reads as a deliberate one. Only a string the bracketed form
    /// could not start on at all falls through to the bare form, which is how
    /// <c>"1 2 3"</c> works.
    /// </para>
    ///
    /// <para>
    /// A component nothing was scanned into keeps <c>vec3_origin</c>'s zero, so
    /// this cannot fail: every string is a vector, and a nonsense one is
    /// <c>(0,0,0)</c>.
    /// </para>
    ///
    /// <para>
    /// The closing <c>]</c> is not checked here because checking it would change
    /// nothing: it is matched AFTER the three assignments, so a string that
    /// reached it has already made them and <c>sscanf</c> returns 3 whether or
    /// not it is there. <c>"[  4   5   6  ]"</c> is <c>(4,5,6)</c> exactly
    /// because of that.
    /// </para>
    ///
    /// <para>
    /// <b>A LIMIT WORTH KNOWING.</b> <c>scanf</c>'s <c>%f</c> is not
    /// <c>strtod</c>: it is a separate scanner that cannot push back more than
    /// one character, and the two libcs on this machine disagree about
    /// <c>"1e"</c> because of it — glibc 2.31 in the sniper container assigns 1,
    /// a modern host one assigns nothing. The container is what the game links
    /// against, and there <c>%f</c> and <c>strtod</c> agree over the whole case
    /// table, so this uses the <c>strtod</c> prefix rules for both. If a row
    /// ever appears where they part company,
    /// <c>variant_convert_test.cpp</c> is what says so.
    /// </para>
    ///
    /// <para>
    /// Nor is <c>EOF</c> told apart from <c>0</c>, for the same kind of reason.
    /// <c>sscanf</c> answers <c>EOF</c> instead of <c>0</c> when the input ran
    /// out before the first conversion — <c>""</c>, <c>"["</c>, <c>"[   "</c> —
    /// and stock's <c>== 0</c> would then skip the retry. But the bare form
    /// assigns nothing to any of those three either, so the two paths agree on
    /// every string that can reach the difference.
    /// </para>
    /// </summary>
    public static Vector ScanVector(string? text)
    {
        var result = new Vector(0f, 0f, 0f);

        if (text is null)
            return result;

        if (ScanFloatTriple(text, bracketed: true, ref result) == 0)
            ScanFloatTriple(text, bracketed: false, ref result);

        return result;
    }

    //-------------------------------------------------------------------------
    // The scanners. Each one consumes what its libc counterpart consumes.
    //-------------------------------------------------------------------------

    /// <summary>How many of the three components were assigned. Components not reached keep their value.</summary>
    private static int ScanFloatTriple(string text, bool bracketed, ref Vector into)
    {
        int pos = 0;

        if (bracketed)
        {
            // A literal that is not whitespace matches the next input character
            // exactly -- no skipping -- so a string that does not START with the
            // bracket assigns nothing.
            if (pos >= text.Length || text[pos] != '[')
                return 0;
            pos++;
        }

        int assigned = 0;

        for (int component = 0; component < 3; component++)
        {
            SkipWhitespace(text, ref pos);

            int end = FloatPrefix(text, pos);
            if (end < 0)
                return assigned;

            float value = ParseFloatPrefix(text, pos, end);
            pos = end;

            switch (component)
            {
                case 0: into.X = value; break;
                case 1: into.Y = value; break;
                default: into.Z = value; break;
            }

            assigned++;
        }

        return assigned;
    }

    /// <summary>One <c>%d</c>: whitespace, then a decimal integer, or no assignment.</summary>
    private static bool ScanInt(string text, ref int pos, out int value)
    {
        SkipWhitespace(text, ref pos);

        int start = pos;
        value = ScanLong(text, start, out int end);

        if (end == start)
            return false;

        pos = end;
        return true;
    }

    /// <summary>
    /// <c>strtol</c> base ten, saturating, truncated to 32 bits — and where it
    /// stopped, so a scanner can carry on from there. <paramref name="end"/>
    /// equals the start when nothing was read.
    /// </summary>
    private static int ScanLong(string text, int start, out int end)
    {
        int pos = start;
        SkipWhitespace(text, ref pos);

        bool negative = false;
        if (pos < text.Length && (text[pos] == '+' || text[pos] == '-'))
        {
            negative = text[pos] == '-';
            pos++;
        }

        int firstDigit = pos;
        long accumulated = 0;
        bool saturated = false;

        while (pos < text.Length && text[pos] >= '0' && text[pos] <= '9')
        {
            if (!saturated)
            {
                long digit = text[pos] - '0';

                // strtol clamps at the ends of long rather than wrapping, and
                // the wrap that a caller sees is the CAST that follows.
                if (accumulated > (long.MaxValue - digit) / 10)
                    saturated = true;
                else
                    accumulated = accumulated * 10 + digit;
            }

            pos++;
        }

        if (pos == firstDigit)
        {
            // No digits: strtol consumes nothing at all, sign included.
            end = start;
            return 0;
        }

        end = pos;

        if (saturated)
            return unchecked((int)(negative ? long.MinValue : long.MaxValue));

        return unchecked((int)(negative ? -accumulated : accumulated));
    }

    private static void SkipWhitespace(string text, ref int pos)
    {
        while (pos < text.Length && IsCWhitespace(text[pos]))
            pos++;
    }

    /// <summary>C's <c>isspace</c> in the "C" locale, which is the six characters and no more.</summary>
    private static bool IsCWhitespace(char c)
        => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    /// <summary>
    /// Where <c>strtod</c> would stop, starting at <paramref name="start"/>, or
    /// <c>-1</c> if it would consume nothing.
    /// </summary>
    private static int FloatPrefix(string text, int start)
    {
        int pos = start;
        SkipWhitespace(text, ref pos);

        if (pos < text.Length && (text[pos] == '+' || text[pos] == '-'))
            pos++;

        if (Matches(text, pos, "infinity"))
            return pos + 8;
        if (Matches(text, pos, "inf"))
            return pos + 3;

        if (Matches(text, pos, "nan"))
        {
            int after = pos + 3;

            // strtod takes an optional parenthesised n-char-sequence after NAN,
            // and takes it only if the closing bracket is there.
            if (after < text.Length && text[after] == '(')
            {
                int close = after + 1;
                while (close < text.Length && text[close] != ')')
                    close++;
                if (close < text.Length)
                    return close + 1;
            }

            return after;
        }

        if (Matches(text, pos, "0x"))
        {
            int hex = pos + 2;
            int digits = 0;

            while (hex < text.Length && IsHexDigit(text[hex])) { hex++; digits++; }

            if (hex < text.Length && text[hex] == '.')
            {
                hex++;
                while (hex < text.Length && IsHexDigit(text[hex])) { hex++; digits++; }
            }

            // Without a hex digit "0x" is not a hex float at all; strtod falls
            // back to reading the plain "0" and leaving the "x".
            if (digits == 0)
                return pos + 1;

            return ExponentEnd(text, hex, 'p');
        }

        int decimals = 0;
        int scan = pos;

        while (scan < text.Length && text[scan] >= '0' && text[scan] <= '9') { scan++; decimals++; }

        if (scan < text.Length && text[scan] == '.')
        {
            scan++;
            while (scan < text.Length && text[scan] >= '0' && text[scan] <= '9') { scan++; decimals++; }
        }

        if (decimals == 0)
            return -1;

        return ExponentEnd(text, scan, 'e');
    }

    /// <summary>
    /// Past an exponent if there is a complete one. An exponent marker with no
    /// digits behind it is not part of the number — <c>atof("1e")</c> is 1, not
    /// a failure — so the marker is given back.
    /// </summary>
    private static int ExponentEnd(string text, int pos, char marker)
    {
        if (pos >= text.Length || char.ToLowerInvariant(text[pos]) != marker)
            return pos;

        int scan = pos + 1;

        if (scan < text.Length && (text[scan] == '+' || text[scan] == '-'))
            scan++;

        int digits = 0;
        while (scan < text.Length && text[scan] >= '0' && text[scan] <= '9') { scan++; digits++; }

        return digits == 0 ? pos : scan;
    }

    /// <summary>The value of a prefix <see cref="FloatPrefix"/> measured out.</summary>
    private static float ParseFloatPrefix(string text, int start, int end)
    {
        int pos = start;
        SkipWhitespace(text, ref pos);

        bool negative = false;
        if (pos < text.Length && (text[pos] == '+' || text[pos] == '-'))
        {
            negative = text[pos] == '-';
            pos++;
        }

        if (Matches(text, pos, "inf"))
            return negative ? float.NegativeInfinity : float.PositiveInfinity;

        if (Matches(text, pos, "nan"))
            return float.NaN;

        if (Matches(text, pos, "0x") && pos + 2 < end)
            return HexFloat(text, pos + 2, end, negative);

        // Everything else is a form double.Parse reads the same way, and letting
        // it do so is what keeps the ROUNDING right: a hand-rolled accumulator
        // would be a second, worse implementation of decimal-to-binary.
        double value = double.Parse(text.AsSpan(pos, end - pos), NumberStyles.Float, Culture);

        return (float)(negative ? -value : value);
    }

    /// <summary>
    /// C99's hex float: a hexadecimal mantissa, optionally with a point, scaled
    /// by a power of TWO. <c>0x1.8p1</c> is 3.
    ///
    /// <para>
    /// Here because <c>strtod</c> takes it and .NET does not, and because
    /// <c>atof("0x10")</c> being 16 while <c>atoi("0x10")</c> is 0 is precisely
    /// the sort of disagreement a map value can land on.
    /// </para>
    /// </summary>
    private static float HexFloat(string text, int pos, int end, bool negative)
    {
        double mantissa = 0;
        int fractionDigits = 0;
        bool afterPoint = false;

        while (pos < end)
        {
            char c = text[pos];

            if (c == '.')
            {
                afterPoint = true;
                pos++;
                continue;
            }

            if (!IsHexDigit(c))
                break;

            mantissa = mantissa * 16 + HexDigit(c);
            if (afterPoint)
                fractionDigits++;

            pos++;
        }

        int exponent = 0;

        if (pos < end && char.ToLowerInvariant(text[pos]) == 'p')
        {
            pos++;
            bool exponentNegative = false;

            if (pos < end && (text[pos] == '+' || text[pos] == '-'))
            {
                exponentNegative = text[pos] == '-';
                pos++;
            }

            while (pos < end && text[pos] >= '0' && text[pos] <= '9')
            {
                exponent = exponent * 10 + (text[pos] - '0');
                pos++;
            }

            if (exponentNegative)
                exponent = -exponent;
        }

        double value = mantissa * Math.Pow(2, exponent - 4 * fractionDigits);

        return (float)(negative ? -value : value);
    }

    private static bool IsHexDigit(char c)
        => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static int HexDigit(char c)
        => c <= '9' ? c - '0' : (char.ToLowerInvariant(c) - 'a' + 10);

    /// <summary>Case-insensitive literal match at a position, as <c>strtod</c> compares its words.</summary>
    private static bool Matches(string text, int pos, string word)
        => pos + word.Length <= text.Length
        && string.Compare(text, pos, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0;

    private static string TrimTrailingZeros(string formatted)
    {
        if (!formatted.Contains('.'))
            return formatted;

        formatted = formatted.TrimEnd('0');

        return formatted.EndsWith('.') ? formatted[..^1] : formatted;
    }
}

/// <summary>
/// <c>variant_t</c> in managed code: the value an entity input carries, and the
/// coercion every map value passes through on its way to a handler.
///
/// <para>
/// <b>THE ANSWERS ARE STOCK'S, NOT C#'s.</b> A map was authored against what
/// <c>variant_t::Convert</c> does, so that is what this does — including where
/// it is ugly. A conversion that quietly returns a BETTER answer than stock is
/// not an improvement here; it is a map that behaves differently in this build
/// than in the one it was built for, with no crash to find it by. Every
/// deliberate difference is marked <b>DIFFERS FROM STOCK</b> below and there
/// are two of them.
/// </para>
///
/// <para>
/// IMMUTABLE, where stock's mutates in place. <c>Convert</c> is a question
/// asked of a copy in every caller that matters — <c>shell_dispatch.cpp</c>
/// makes two copies precisely so the original survives — so making the copy the
/// only shape removes the one way to get it wrong. <see cref="TryConvert"/>
/// returns stock's <c>bool</c> and, on failure, the value unchanged, which is
/// exactly what stock leaves behind in the variant it refused to convert.
/// </para>
/// </summary>
public readonly struct VariantValue
{
    private readonly string? _text;
    private readonly float _x, _y, _z;
    private readonly int _integer;
    private readonly Color32 _color;
    private readonly EntityHandle _handle;

    private VariantValue(VariantFieldType type, string? text, float x, float y, float z,
                         int integer, Color32 color, EntityHandle handle)
    {
        Type = type;
        _text = text;
        _x = x; _y = y; _z = z;
        _integer = integer;
        _color = color;
        _handle = handle;
    }

    /// <summary>What this variant is holding.</summary>
    public VariantFieldType Type { get; }

    /// <summary>A variant holding nothing, which is what an input with no parameter carries.</summary>
    public static VariantValue Void
        => new(VariantFieldType.Void, null, 0, 0, 0, 0, default, EntityHandle.None);

    public static VariantValue FromString(string? text)
        => new(VariantFieldType.String, text ?? string.Empty, 0, 0, 0, 0, default, EntityHandle.None);

    public static VariantValue FromFloat(float value)
        => new(VariantFieldType.Float, null, value, 0, 0, 0, default, EntityHandle.None);

    public static VariantValue FromInt(int value)
        => new(VariantFieldType.Integer, null, 0, 0, 0, value, default, EntityHandle.None);

    public static VariantValue FromBool(bool value)
        => new(VariantFieldType.Boolean, null, 0, 0, 0, value ? 1 : 0, default, EntityHandle.None);

    public static VariantValue FromVector(Vector value)
        => new(VariantFieldType.Vector, null, value.X, value.Y, value.Z, 0, default, EntityHandle.None);

    public static VariantValue FromPositionVector(Vector value)
        => new(VariantFieldType.PositionVector, null, value.X, value.Y, value.Z, 0, default, EntityHandle.None);

    public static VariantValue FromColor(Color32 value)
        => new(VariantFieldType.Color32, null, 0, 0, 0, 0, value, EntityHandle.None);

    public static VariantValue FromEntity(EntityHandle value)
        => new(VariantFieldType.EHandle, null, 0, 0, 0, 0, default, value);

    /// <summary>
    /// An entity variant that arrived as a NAME, with no handle behind it —
    /// which is how one crosses the boundary, because <c>ToString</c> spells an
    /// ehandle as its targetname and the handle itself is not on the wire.
    ///
    /// <para>
    /// Its type is still <see cref="VariantFieldType.EHandle"/>, and that is the
    /// point rather than a detail: stock's <c>Convert</c> has no ehandle-to-float
    /// arm, so an ehandle-valued input carries the number ZERO however its
    /// targetname reads. Storing it as a string instead would put an entity
    /// called "3door" through <c>atof</c> and deliver the number three.
    /// </para>
    /// </summary>
    public static VariantValue FromEntityName(string? name)
        => new(VariantFieldType.EHandle, name ?? string.Empty, 0, 0, 0, 0, default, EntityHandle.None);

    //-------------------------------------------------------------------------
    // The accessors, each with stock's own refusal built in.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>variant_t::Float()</c>: the float, and ZERO for anything that is not
    /// one — a string of digits included. That refusal is the bug
    /// <c>shell_dispatch.cpp</c>'s note records: a map sends "3" as a STRING, so
    /// a counter told to add three added nothing until somebody converted first.
    /// </summary>
    public float Float => Type == VariantFieldType.Float ? _x : 0f;

    /// <summary><c>variant_t::Int()</c>: the integer, and zero for every other type.</summary>
    public int Int => Type == VariantFieldType.Integer ? _integer : 0;

    /// <summary><c>variant_t::Bool()</c>: the boolean, and false for every other type.</summary>
    public bool Bool => Type == VariantFieldType.Boolean && _integer != 0;

    /// <summary>
    /// <c>variant_t::Vector3D()</c>: the vector, for either vector type, and the
    /// origin for anything else.
    /// </summary>
    public Vector Vector3D
        => Type is VariantFieldType.Vector or VariantFieldType.PositionVector
           ? new Vector(_x, _y, _z)
           : new Vector(0f, 0f, 0f);

    /// <summary>
    /// The colour, for a colour.
    ///
    /// <para>
    /// <b>DIFFERS FROM STOCK (1 of 2).</b> Stock's <c>Color32()</c> has NO type
    /// check: it returns <c>rgbaVal</c> whatever the variant holds, so asking a
    /// float variant for a colour hands back the four bytes of the float
    /// reinterpreted. That is union punning, and reproducing it would mean
    /// laying out an explicit union here for the sole purpose of preserving a
    /// caller's mistake — every reachable caller converts to
    /// <see cref="VariantFieldType.Color32"/> first, and one that did not was
    /// reading noise. So this refuses, in the same shape every other accessor
    /// refuses, and answers the default colour instead.
    /// </para>
    /// </summary>
    public Color32 Color => Type == VariantFieldType.Color32 ? _color : default;

    /// <summary>
    /// <c>variant_t::Entity()</c>: the handle, and an empty one for every other
    /// type.
    /// </summary>
    public EntityHandle Entity
        => Type == VariantFieldType.EHandle ? _handle : EntityHandle.None;

    /// <summary>The string a string variant holds. Empty for every other type; see <see cref="AsText"/>.</summary>
    public string RawString => Type == VariantFieldType.String ? _text ?? string.Empty : string.Empty;

    //-------------------------------------------------------------------------
    // Convert and ToString.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>variant_t::Convert</c>: the whole table of which I/O types coerce into
    /// which, and stock's <c>bool</c> for whether this one does.
    ///
    /// <para>
    /// THE TABLE IS SMALL AND FULL OF HOLES, and the holes are the part to know.
    /// A source type with no arm returns <c>false</c> and leaves the value
    /// alone; the caller then reads it with an accessor that refuses too, and
    /// gets a zero. So:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><b>A BOOLEAN CONVERTS TO NOTHING.</b> There is no
    /// <c>case FIELD_BOOLEAN</c> in stock's outer switch at all, so a boolean
    /// asked for a float answers <c>false</c> and the caller's number is
    /// <c>0</c> — for <c>true</c> as much as for <c>false</c>. An input fired
    /// with a boolean <c>true</c> arrives carrying the number zero.</item>
    /// <item><b>A VECTOR, A POSITION VECTOR AND A COLOUR CONVERT TO NOTHING
    /// EITHER</b>, for the same reason: no arm.</item>
    /// <item><b>A STRING DOES NOT CONVERT TO A POSITION VECTOR.</b>
    /// <c>FIELD_VECTOR</c> has an arm and <c>FIELD_POSITION_VECTOR</c> does not,
    /// though they hold the same three floats.</item>
    /// <item><b>A STRING TO A BOOLEAN GOES THROUGH <c>atoi</c>, NOT
    /// <c>atof</c></b>, so <c>"0.5"</c> is <c>false</c> while the same string to
    /// a float is <c>0.5</c>, and <c>"0x10"</c> is <c>false</c> while its float
    /// is 16.</item>
    /// <item><b>AN ENTITY TO A STRING SUCCEEDS WITHOUT CONVERTING</b> when the
    /// handle is empty: stock returns <c>true</c> from inside the arm having
    /// changed nothing, so the value is still an ehandle afterwards. The
    /// <see cref="AsText"/> of it is <c>"&lt;&lt;null entity&gt;&gt;"</c>.</item>
    /// </list>
    ///
    /// <para>
    /// <paramref name="findByName"/> is stock's
    /// <c>gEntList.FindEntityByName</c>, needed for the one arm that turns a
    /// name into an entity. Passed in rather than reached for, so this type
    /// stays a value with no directory behind it and a test can pin the arm
    /// without a world. Absent, the arm behaves as it does when the name
    /// matches nothing — which stock also treats as success, assigning a null
    /// entity.
    /// </para>
    ///
    /// <para>
    /// <paramref name="nameOf"/> is the other direction, for the one arm that
    /// turns an entity into its targetname, passed in for the same reason.
    /// Absent, that arm succeeds without converting, as it does for an empty
    /// handle.
    /// </para>
    /// </summary>
    public bool TryConvert(VariantFieldType newType, out VariantValue result,
                           Func<string, EntityHandle>? findByName = null,
                           Func<EntityHandle, string>? nameOf = null)
    {
        result = this;

        if (newType == Type)
            return true;

        // Converting to a null value is easy.
        if (newType == VariantFieldType.Void)
        {
            result = Void;
            return true;
        }

        // FIELD_INPUT accepts the variant type directly.
        if (newType == VariantFieldType.Input)
            return true;

        switch (Type)
        {
            case VariantFieldType.Integer:
                switch (newType)
                {
                    case VariantFieldType.Float:
                        result = FromFloat(_integer);
                        return true;
                    case VariantFieldType.Boolean:
                        result = FromBool(_integer != 0);
                        return true;
                }
                break;

            case VariantFieldType.Float:
                switch (newType)
                {
                    case VariantFieldType.Integer:
                        result = FromInt(TruncateToInt(_x));
                        return true;
                    case VariantFieldType.Boolean:
                        result = FromBool(_x != 0f);
                        return true;
                }
                break;

            // Everyone must convert from FIELD_STRING if possible, since
            // parameter overrides are always passed as strings.
            case VariantFieldType.String:
                switch (newType)
                {
                    case VariantFieldType.Integer:
                        result = FromInt(IsNullString ? 0 : VariantParse.Atoi(_text));
                        return true;

                    case VariantFieldType.Float:
                        result = FromFloat(IsNullString ? 0f : VariantParse.Atof(_text));
                        return true;

                    case VariantFieldType.Boolean:
                        result = FromBool(!IsNullString && VariantParse.Atoi(_text) != 0);
                        return true;

                    case VariantFieldType.Vector:
                        result = FromVector(VariantParse.ScanVector(_text));
                        return true;

                    case VariantFieldType.Color32:
                        result = FromColor(VariantParse.ScanColor(_text));
                        return true;

                    case VariantFieldType.EHandle:
                        result = FromEntity(IsNullString || findByName is null
                                            ? EntityHandle.None
                                            : findByName(_text ?? string.Empty));
                        return true;
                }
                break;

            case VariantFieldType.EHandle:
                if (newType == VariantFieldType.String)
                {
                    // The entity's targetname becomes the string -- but only if
                    // there IS an entity. Stock returns true either way, leaving
                    // an empty handle as an ehandle.
                    if (_handle != EntityHandle.None && nameOf is not null)
                        result = FromString(nameOf(_handle));

                    return true;
                }
                break;
        }

        // invalid conversion
        return false;
    }

    /// <summary>
    /// <c>NULL_STRING</c>, which stock checks for before every string parse and
    /// answers zero to. A string variant that was never assigned holds it, and
    /// it is not the same thing as the empty string in stock — though both
    /// convert to the same numbers, since <c>atoi("")</c> and <c>atof("")</c>
    /// are zero as well.
    /// </summary>
    private bool IsNullString => _text is null;

    /// <summary>
    /// <c>variant_t::String()</c>: the string a string variant holds, and
    /// <see cref="AsDebugText"/> for everything else — which is stock's own
    /// inline, and the reason the <c>Convert(FIELD_STRING)</c> that used to
    /// precede it in <c>shell_dispatch.cpp</c> changed nothing.
    /// </summary>
    public string AsText => AsTextWith(null);

    /// <summary>
    /// <see cref="AsText"/>, with an ehandle's name looked up through
    /// <paramref name="nameOf"/> — the caller's directory, passed in the way
    /// <see cref="TryConvert"/>'s <c>findByName</c> is, so this type stays a
    /// value with no world behind it. Null spells an ehandle as
    /// <c>"&lt;&lt;null entity&gt;&gt;"</c>.
    /// </summary>
    public string AsTextWith(Func<EntityHandle, string>? nameOf)
        => Type == VariantFieldType.String ? _text ?? string.Empty : AsDebugTextWith(nameOf);

    /// <summary>
    /// <c>variant_t::ToString()</c>: every type as text, for debugging and for
    /// the value an input carries across to managed code.
    ///
    /// <para>
    /// A POSITION VECTOR HAS NO SPELLING. <c>ToString</c>'s switch has a
    /// <c>FIELD_VECTOR</c> case and no <c>FIELD_POSITION_VECTOR</c> case, so one
    /// falls off the end and comes back as the literal words
    /// <c>"No conversion to string"</c> — which is then the value the input
    /// carries. It reads like an error message because it is one, and it is
    /// delivered as data.
    /// </para>
    ///
    /// <para>
    /// A boolean spells <c>"true"</c>/<c>"false"</c> here while a map WRITES
    /// booleans as <c>0</c>/<c>1</c>, so this spelling does not survive a round
    /// trip through <see cref="TryConvert"/> back to a boolean:
    /// <c>atoi("true")</c> is 0. Stock's asymmetry, kept.
    /// </para>
    /// </summary>
    public string AsDebugText => AsDebugTextWith(null);

    /// <summary>
    /// <see cref="AsDebugText"/>, with an ehandle's name looked up through
    /// <paramref name="nameOf"/>. See <see cref="AsTextWith"/>.
    /// </summary>
    public string AsDebugTextWith(Func<EntityHandle, string>? nameOf) => Type switch
    {
        VariantFieldType.String => _text ?? string.Empty,
        VariantFieldType.Boolean => _integer != 0 ? "true" : "false",
        VariantFieldType.Integer => VariantParse.FormatI(_integer),
        VariantFieldType.Float => VariantParse.FormatG(_x),
        VariantFieldType.Color32
            => $"{_color.R} {_color.G} {_color.B} {_color.A}",
        VariantFieldType.Vector
            => $"[{VariantParse.FormatG(_x)} {VariantParse.FormatG(_y)} {VariantParse.FormatG(_z)}]",
        VariantFieldType.Void => string.Empty,

        // The name it crossed with, when it crossed with one, and otherwise the
        // one the directory has for the handle.
        VariantFieldType.EHandle => _text ?? EntityName(_handle, nameOf),
        _ => "No conversion to string",
    };

    private static string EntityName(EntityHandle handle, Func<EntityHandle, string>? nameOf)
    {
        if (handle == EntityHandle.None || nameOf is null)
            return "<<null entity>>";

        return nameOf(handle) ?? "<<null entity>>";
    }

    /// <summary>
    /// The value as a number, the way an input's <c>flFloat0</c> is derived:
    /// <c>Convert(FIELD_FLOAT)</c>, then <c>Float()</c>, and zero if the
    /// conversion was refused.
    ///
    /// <para>
    /// One method because it is one question and it was being asked in two
    /// places in two ways — <c>shell_dispatch.cpp</c> through the native
    /// <c>Convert</c>, and <c>EventAction</c> through a hand-rolled digit
    /// scanner that stopped at an exponent. They disagreed about <c>"1e3"</c>:
    /// 1000 down one path and 1 down the other, for the same map value.
    /// </para>
    /// </summary>
    public float AsNumber => TryConvert(VariantFieldType.Float, out var converted)
                             ? converted.Float
                             : 0f;

    /// <summary>
    /// The value an input crossed with, rebuilt from what the shim put on the
    /// wire — the shim's <c>nInt0</c> (the variant's own field type, plus one),
    /// its <c>pText2</c> (the value as text, which is
    /// <c>variant_t::String()</c>) and its <c>flFloat0</c>.
    ///
    /// <para>
    /// <b>WHY THE TYPE HAS TO CROSS.</b> The text alone is not enough to
    /// recover the number, and the two types it gets wrong get it wrong
    /// SILENTLY. A colour spells itself "255 128 64 32", and
    /// <c>atof</c> of that is 255 — while stock's <c>Convert</c> has no colour
    /// arm at all and yields <c>0</c>. A boolean spells itself "true", and
    /// stock yields <c>0</c> for that too, from a hole rather than from a
    /// parse. Deriving the number from the text would make a colour-valued
    /// input arrive carrying 255 where the map expects nothing, with no crash
    /// and no message.
    /// </para>
    ///
    /// <para>
    /// <c>fieldType</c> of ZERO means the shim did not say — an older shim, or
    /// a path that fills the args block itself — and the value is then taken as
    /// a string, which is what every map value and every <c>ent_fire</c>
    /// actually is. <see cref="VariantFieldType.Void"/> is <c>1</c> here, not
    /// <c>0</c>, precisely so that "nothing was written" cannot be read as a
    /// real type: a one-sided slot answering a plausible zero has cost this port
    /// two lanes.
    /// </para>
    ///
    /// <para>
    /// <c>number</c> is what the shim's remaining native <c>Convert</c>
    /// produced. It is taken on trust for the numeric types and RECOMPUTED for
    /// the rest, so this method already answers correctly for every type; when
    /// the Entry.cs line lands and the shim stops converting, the parameter
    /// carries the raw payload instead and only the three numeric arms change.
    /// </para>
    /// </summary>
    public static VariantValue FromCrossing(int fieldType, string? text, float number)
    {
        // Not a type this build knows, and "did not say", are the same answer:
        // treat it as the string it arrived as.
        if (fieldType <= 0 || !System.Enum.IsDefined(typeof(VariantFieldType), fieldType - 1))
            return FromString(text);

        var type = (VariantFieldType)(fieldType - 1);

        return type switch
        {
            VariantFieldType.Void => Void,
            VariantFieldType.String => FromString(text),
            VariantFieldType.Float => FromFloat(number),

            // The integer and the boolean cannot be recovered from `number`:
            // an integer past 2^24 has already lost digits in the float, and a
            // boolean's number is zero from a HOLE in Convert rather than from
            // its value. Both are exact in the text -- "%i" and "true"/"false"
            // are lossless spellings -- so both come from there.
            VariantFieldType.Integer => FromInt(VariantParse.Atoi(text)),
            VariantFieldType.Boolean => FromBool(text == "true"),

            VariantFieldType.Vector => FromVector(VariantParse.ScanVector(text)),

            // A POSITION VECTOR LOSES ITS COMPONENTS, and they were already lost
            // before this method: stock's ToString has no FIELD_POSITION_VECTOR
            // case, so what crossed is the literal words "No conversion to
            // string" and there is nothing in them to rebuild from. Kept as a
            // position vector at the origin, which spells itself back as those
            // same words and yields the same number -- zero -- that stock's
            // Convert yields for one. Only Vector3D is poorer than stock's, and
            // it is poorer because the wire is.
            VariantFieldType.PositionVector => FromPositionVector(new Vector(0f, 0f, 0f)),

            VariantFieldType.Color32 => FromColor(VariantParse.ScanColor(text)),

            // An entity crossed as its targetname and the handle did not come
            // with it. Kept AS an ehandle rather than as a string, so that the
            // number it yields stays stock's zero: a string called "3door" put
            // through atof is three.
            VariantFieldType.EHandle => FromEntityName(text),

            _ => FromString(text),
        };
    }

    /// <summary>
    /// <c>(int)</c> of a float, as C does it and as this machine does it.
    ///
    /// <para>
    /// Truncation towards zero, which C# matches — but a value that does not FIT
    /// is undefined behaviour in C, and what x86-64 actually does is
    /// <c>cvttss2si</c>'s "integer indefinite": <c>INT_MIN</c>, for a
    /// magnitude too large in EITHER direction. C# defines the same conversion
    /// as unspecified-but-not-trapping and .NET gives <c>INT_MIN</c> too, but
    /// only because it lowers to the same instruction — so it is written out
    /// rather than left to agree by accident.
    /// </para>
    /// </summary>
    private static int TruncateToInt(float value)
    {
        if (float.IsNaN(value) || value >= 2147483648f || value < -2147483648f)
            return int.MinValue;

        return (int)value;
    }

    public override string ToString() => $"{Type}:{AsDebugText}";
}
