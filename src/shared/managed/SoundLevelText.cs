//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;

namespace SourceSharp.Shared;

/// <summary>
/// Stock's <c>TextToSoundLevel</c> (<c>public/SoundParametersInternal.cpp</c>):
/// a <c>SNDLVL_*</c> name to its decibel level.
///
/// <para>
/// The lookup is the named table first (case-insensitively), then
/// <c>SNDLVL_&lt;n&gt;</c> by <c>atoi</c> of what follows the prefix when n is
/// in 1..180, and otherwise <c>SNDLVL_NORM</c> (75). The table holds names
/// the prefix rule would answer differently: <c>SNDLVL_IDLE</c> is 60,
/// <c>SNDLVL_TALKING</c> 80, <c>SNDLVL_STATIC</c> 66, <c>SNDLVL_NORM</c> 75,
/// <c>SNDLVL_GUNFIRE</c> 140, and <c>SNDLVL_NONE</c> 0.
/// </para>
/// </summary>
public static class SoundLevelText
{
    /// <summary><c>SNDLVL_NORM</c>.</summary>
    public const int Normal = 75;

    private const string Prefix = "SNDLVL_";

    /// <summary>
    /// The level <paramref name="text"/> names. <paramref name="unknown"/>
    /// hears stock's <c>DevMsg</c> when nothing matches.
    /// </summary>
    public static int Parse(string text, Action<string>? unknown = null)
    {
        // g_pSoundLevels, matched case-insensitively (Q_strcasecmp).
        int named = text.ToUpperInvariant() switch
        {
            "SNDLVL_NONE" => 0,
            "SNDLVL_20DB" => 20,
            "SNDLVL_25DB" => 25,
            "SNDLVL_30DB" => 30,
            "SNDLVL_35DB" => 35,
            "SNDLVL_40DB" => 40,
            "SNDLVL_45DB" => 45,
            "SNDLVL_50DB" => 50,
            "SNDLVL_55DB" => 55,
            "SNDLVL_IDLE" => 60,
            "SNDLVL_TALKING" => 80,
            "SNDLVL_60DB" => 60,
            "SNDLVL_65DB" => 65,
            "SNDLVL_STATIC" => 66,
            "SNDLVL_70DB" => 70,
            "SNDLVL_NORM" => 75,
            "SNDLVL_75DB" => 75,
            "SNDLVL_80DB" => 80,
            "SNDLVL_85DB" => 85,
            "SNDLVL_90DB" => 90,
            "SNDLVL_95DB" => 95,
            "SNDLVL_100DB" => 100,
            "SNDLVL_105DB" => 105,
            "SNDLVL_110DB" => 110,
            "SNDLVL_120DB" => 120,
            "SNDLVL_130DB" => 130,
            "SNDLVL_GUNFIRE" => 140,
            "SNDLVL_140DB" => 140,
            "SNDLVL_150DB" => 150,
            "SNDLVL_180DB" => 180,
            _ => -1,
        };
        if (named >= 0)
            return named;

        if (text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            int level = Atoi(text.AsSpan(Prefix.Length));
            if (level > 0 && level <= 180)
                return level;
        }

        unknown?.Invoke($"CSoundEmitterSystem:  Unknown sound level {text}\n");
        return Normal;
    }

    // atoi: leading whitespace, a sign, then digits up to the first non-digit.
    private static int Atoi(ReadOnlySpan<char> s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;

        bool negative = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
            negative = s[i++] == '-';

        long value = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i]) && value <= int.MaxValue)
            value = (value * 10) + (s[i++] - '0');

        return (int)(negative ? -value : value);
    }
}
