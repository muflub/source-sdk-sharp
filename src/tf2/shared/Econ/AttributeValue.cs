//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

namespace TF2.Shared.Econ;

/// <summary>
/// One attribute on an item: <c>static_attrib_t</c> (a definition and the
/// 32-bit <c>attribute_data_union_t</c>), plus the text for the types whose
/// value is not 32 bits (<c>string</c>, <c>uint64</c>, the recipe types).
/// </summary>
public readonly struct AttributeValue
{
    public AttributeValue(AttributeDefinition definition, uint bits, string? text = null)
    {
        Definition = definition;
        Bits = bits;
        Text = text;
    }

    public AttributeDefinition Definition { get; }

    /// <summary>The union's 32 bits: a float's pattern, or an integer for a <c>stored_as_integer</c> <c>static_attrs</c> value.</summary>
    public uint Bits { get; }

    /// <summary>The value as written, for a type that is not 32 bits; null for <see cref="AttributeValueType.Default"/>/<see cref="AttributeValueType.Float"/>.</summary>
    public string? Text { get; }

    /// <summary>The bits read as a float: what the hooks apply (<c>*reinterpret_cast&lt;float *&gt;( &amp;value )</c>, attribute_manager.cpp:708).</summary>
    public float AsFloat => BitConverter.UInt32BitsToSingle(Bits);

    /// <summary>A float value.</summary>
    public static AttributeValue OfFloat(AttributeDefinition definition, float value) =>
        new(definition, BitConverter.SingleToUInt32Bits(value));

    /// <summary>
    /// <c>BConvertStringToEconAttributeValue</c> (econ_item_schema.cpp:1891-2016)
    /// for one written value. <paramref name="multiLine"/> is the <c>attributes</c>
    /// block's path (<c>BInitFromKV_MultiLine</c>, :3434), which passes
    /// <c>bEnableTerribleBackwardsCompatibilitySchemaParsingCode = true</c>: a
    /// default-type value is then ALWAYS a float (<c>atof</c>), even for a
    /// <c>stored_as_integer</c> definition. Only <c>static_attrs</c>
    /// (<c>BInitFromKV_SingleLine</c> with false, :3337) stores integer bits.
    /// </summary>
    public static AttributeValue Parse(AttributeDefinition definition, string? text, bool multiLine)
    {
        switch (definition.ValueType)
        {
            case AttributeValueType.Default:
                if (multiLine || !definition.StoredAsInteger)
                    return OfFloat(definition, text is null ? 0f : CNumbers.Atof(text));
                return new AttributeValue(definition, unchecked((uint)CNumbers.Atoui64(text ?? string.Empty)));

            case AttributeValueType.Float:
                return OfFloat(definition, CNumbers.Atof(text ?? string.Empty));

            default:
                return new AttributeValue(definition, 0, text ?? string.Empty);
        }
    }

    public override string ToString() =>
        Text ?? (Definition.StoredAsInteger
            ? Bits.ToString(CultureInfo.InvariantCulture)
            : AsFloat.ToString("R", CultureInfo.InvariantCulture));
}

/// <summary>The C library's number readers the schema uses, over a managed string.</summary>
public static class CNumbers
{
    /// <summary>
    /// C's <c>atof</c>: the longest decimal prefix (leading space, sign,
    /// digits, one point, an exponent), 0 when there is none.
    /// </summary>
    public static float Atof(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;

        int start = i;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
            i++;

        int digits = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; digits++; }
        if (i < s.Length && s[i] == '.')
        {
            i++;
            while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; digits++; }
        }

        if (digits == 0)
            return 0f;

        int mantissaEnd = i;
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            int j = i + 1;
            if (j < s.Length && (s[j] == '+' || s[j] == '-'))
                j++;
            int expDigits = 0;
            while (j < s.Length && char.IsAsciiDigit(s[j])) { j++; expDigits++; }
            if (expDigits > 0)
                mantissaEnd = j;
        }

        return (float)double.Parse(s.AsSpan(start, mantissaEnd - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>tier1's <c>V_atoui64</c> (strtools.cpp:545-595): hex after <c>0x</c>, a quoted character, else decimal digits.</summary>
    public static ulong Atoui64(string s)
    {
        ulong value = 0;
        if (s.Length >= 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X'))
        {
            for (int i = 2; i < s.Length; i++)
            {
                int d = s[i] switch
                {
                    >= '0' and <= '9' => s[i] - '0',
                    >= 'a' and <= 'f' => s[i] - 'a' + 10,
                    >= 'A' and <= 'F' => s[i] - 'A' + 10,
                    _ => -1,
                };
                if (d < 0)
                    break;
                value = unchecked((value << 4) + (ulong)d);
            }
            return value;
        }

        if (s.Length >= 2 && s[0] == '\'')
            return s[1];

        foreach (char c in s)
        {
            if (c < '0' || c > '9')
                break;
            value = unchecked(value * 10 + (ulong)(c - '0'));
        }
        return value;
    }

    /// <summary>C's <c>atoi</c>: leading space, a sign, decimal digits; 0 when there are none.</summary>
    public static int Atoi(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;

        bool negative = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
            negative = s[i++] == '-';

        long value = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
        {
            value = value * 10 + (s[i++] - '0');
            if (value > uint.MaxValue)
                break;
        }
        return unchecked((int)(negative ? -value : value));
    }
}
