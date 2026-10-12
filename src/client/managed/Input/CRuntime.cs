using System.Globalization;

namespace SourceSharp.Client.Input;

/// <summary>
/// The two C library conversions stock input reads its text through, with C's
/// rules rather than .NET's: <c>atoi</c> for a key command's key code and
/// <c>atof</c> for a convar's float. Both take the longest numeric prefix and
/// answer 0 for none, which is what makes <c>"384.0f"</c> read 384 and a
/// console <c>+forward</c> with no code read -1 (the caller's, before atoi).
/// </summary>
public static class CRuntime
{
    /// <summary>C's <c>atoi</c>: leading space, a sign, decimal digits; 0 for none. Wraps as int does.</summary>
    public static int Atoi(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int i = 0;
        while (i < text.Length && IsSpace(text[i]))
            i++;

        bool negative = false;
        if (i < text.Length && (text[i] == '+' || text[i] == '-'))
        {
            negative = text[i] == '-';
            i++;
        }

        long value = 0;
        while (i < text.Length && text[i] >= '0' && text[i] <= '9')
        {
            value = value * 10 + (text[i] - '0');
            if (value > int.MaxValue + 1L)
                value = int.MaxValue + 1L;
            i++;
        }

        return unchecked((int)(negative ? -value : value));
    }

    /// <summary>
    /// C's <c>atof</c>: the longest prefix strtod accepts (decimal with an
    /// optional exponent, or inf/nan), as a double; 0 for none.
    /// </summary>
    public static double Atof(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0.0;

        int i = 0;
        while (i < text.Length && IsSpace(text[i]))
            i++;

        int start = i;
        if (i < text.Length && (text[i] == '+' || text[i] == '-'))
            i++;

        string rest = text[i..];
        if (rest.StartsWith("inf", StringComparison.OrdinalIgnoreCase))
            return text[start] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
        if (rest.StartsWith("nan", StringComparison.OrdinalIgnoreCase))
            return double.NaN;

        int digits = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
        {
            i++;
            digits++;
        }

        if (i < text.Length && text[i] == '.')
        {
            i++;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
                digits++;
            }
        }

        if (digits == 0)
            return 0.0;

        int mantissaEnd = i;
        if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
        {
            int j = i + 1;
            if (j < text.Length && (text[j] == '+' || text[j] == '-'))
                j++;
            int expDigits = 0;
            while (j < text.Length && char.IsAsciiDigit(text[j]))
            {
                j++;
                expDigits++;
            }
            if (expDigits > 0)
                mantissaEnd = j;
        }

        return double.Parse(text.AsSpan(start, mantissaEnd - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static bool IsSpace(char c) => c == ' ' || (c >= '\t' && c <= '\r');
}
