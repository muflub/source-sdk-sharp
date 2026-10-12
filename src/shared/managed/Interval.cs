//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;

namespace SourceSharp.Shared;

/// <summary>
/// A RANGE OF NUMBERS WRITTEN AS TEXT — <c>"3"</c> or <c>"3,5"</c> — which is
/// how the game's script files spell "a delay", "a lifetime", "how long to wait
/// before the next one".
///
/// <para>
/// This is the rule <c>game/shared/interval.cpp</c> held. That file has left
/// both <c>_csharp.vpc</c> file lists (plan_shared.md Phase 3): the two
/// functions its 59 lines contained are inline in <c>interval.h</c> now,
/// because three surviving native translation units call them on paths where a
/// boundary crossing would buy nothing. What lives HERE is the rule itself,
/// where it can be read, argued about and tested — <c>IntervalTests</c> checks
/// this class against that header's text, so the two cannot drift apart.
/// </para>
///
/// <para>
/// <b>THE SECOND NUMBER IS AN END, AND WHAT IS STORED IS A RANGE.</b>
/// <c>"3,5"</c> means "between 3 and 5", and stock stores it as
/// <c>start = 3, range = 2</c>. Reading the stored pair as (start, end) gives a
/// draw between 3 and 3+5, which is right for a fixed value and wrong for every
/// interval anybody actually wrote — the kind of error that looks like a
/// balance problem for a year.
/// </para>
///
/// <para>
/// <b>A MISSING COMMA IS NOT A PARSE FAILURE.</b> <c>"3"</c> is a fixed 3, and
/// a great many intervals in the scripts are written that way. So is a string
/// this cannot make any sense of at all: stock's <c>atof</c> answers 0 for
/// unparseable text and the interval becomes a fixed 0, silently. That is
/// reproduced rather than improved on — see <see cref="Parse"/>.
/// </para>
/// </summary>
public readonly record struct Interval(float Start, float Range)
{
    /// <summary>An interval that is always exactly this number.</summary>
    public static Interval Fixed(float value) => new(value, 0f);

    /// <summary>
    /// Parse <c>"start"</c> or <c>"start,end"</c>, exactly as
    /// <c>ReadInterval</c> does.
    ///
    /// <para>
    /// EVERY MALFORMED INPUT ANSWERS AN INTERVAL rather than throwing or
    /// answering null, because that is what the native side does and what its
    /// callers are written against: a script with a typo in it gives a fixed
    /// zero, and the sound or the delay it described simply does not happen.
    /// An exception here would be a different behaviour wearing the same name.
    /// </para>
    ///
    /// <para>
    /// Only the FIRST TWO fields are read. <c>"1,2,3"</c> is 1 to 2 and the 3
    /// is dropped — stock's second <c>strtok</c> stops at the second comma and
    /// never asks for a third.
    /// </para>
    /// </summary>
    public static Interval Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return default;
        }

        // 128 bytes, as stock's stack buffer is: a longer string is TRUNCATED
        // rather than rejected, and truncating in the middle of a number
        // changes it. Reproduced because a caller feeding a 200-character
        // interval gets the same answer from both sides or from neither.
        if (text.Length >= 128)
        {
            text = text[..127];
        }

        // strtok( ..., "," ): empty fields are skipped, so ",5" starts at 5
        // and "3,,5" is 3 to 5.
        string[] fields = text.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 0)
        {
            return default;
        }

        // tmp.start = atof( token ); tmp.range = atof( token ) - tmp.start:
        // atof answers a double, so the range is a double minus the float
        // start, then narrowed. "0.5,0.9" is 0.4f, not 0.9f - 0.5f.
        float start = (float)Number(fields[0]);
        float range = fields.Length > 1 ? (float)(Number(fields[1]) - start) : 0f;

        return new Interval(start, range);
    }

    /// <summary>
    /// <c>atof</c>'s answer: the leading numeric prefix, or 0 for text that has
    /// none. Never throws — that is the whole point of it being here rather
    /// than <c>float.Parse</c>.
    /// </summary>
    private static double Number(string field)
    {
        // atof skips leading whitespace: the shipped scripts write "2, 7".
        int begin = 0;
        while (begin < field.Length && char.IsWhiteSpace(field[begin]))
        {
            begin++;
        }

        field = field[begin..];

        int end = 0;
        bool seenDigit = false, seenDot = false;

        if (end < field.Length && (field[end] == '+' || field[end] == '-'))
        {
            end++;
        }

        while (end < field.Length)
        {
            if (char.IsAsciiDigit(field[end]))
            {
                seenDigit = true;
            }
            else if (field[end] == '.' && !seenDot)
            {
                seenDot = true;
            }
            else
            {
                break;
            }

            end++;
        }

        // An exponent, when digits follow it.
        if (seenDigit && end < field.Length && (field[end] == 'e' || field[end] == 'E'))
        {
            int exp = end + 1;
            if (exp < field.Length && (field[exp] == '+' || field[exp] == '-'))
            {
                exp++;
            }

            if (exp < field.Length && char.IsAsciiDigit(field[exp]))
            {
                while (exp < field.Length && char.IsAsciiDigit(field[exp]))
                {
                    exp++;
                }

                end = exp;
            }
        }

        return seenDigit
            && double.TryParse(field[..end], System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out double v)
            ? v
            : 0.0;
    }

    /// <summary>
    /// A draw from the interval, given a source of randomness.
    ///
    /// <para>
    /// A ZERO RANGE DOES NOT DRAW, and that is not an optimisation. Native's
    /// <c>RandomInterval</c> skips the call for the same reason: taking a
    /// number out of the engine's RNG stream moves every draw after it, so a
    /// fixed interval that consulted the stream would change what an unrelated
    /// system rolled next. Any managed implementation has to have the same
    /// shape or the two sides desynchronise on the first fixed interval.
    /// </para>
    /// </summary>
    public float Draw(Func<float, float, float> randomFloat)
        => Range != 0f ? Start + randomFloat(0f, Range) : Start;

    /// <summary>The far end of the interval — <c>Start + Range</c>.</summary>
    public float End => Start + Range;
}
