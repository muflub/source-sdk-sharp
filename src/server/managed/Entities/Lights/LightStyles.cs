//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;

namespace SourceSharp.Server;

/// <summary>
/// The engine's lightstyle animation strings.
///
/// <para>
/// A lightstyle is a string of letters, one consumed every tenth of a second and
/// then repeated: 'a' is black, 'm' is the brightness vrad baked, and 'z' is
/// about double. The engine holds 64 slots and scales the matching lightmap
/// layer by whatever letter is current, which is the whole mechanism behind
/// every flickering lamp in a Source map.
/// </para>
///
/// <para>
/// These thirteen came over from <c>world.cpp</c> rather than being invented
/// here, and they are copied rather than called for a reason worth stating: the
/// native table is a file-static in a file whose only other job is spawning the
/// world, and reaching it would have meant an ABI slot to fetch a constant
/// string. They are also not engine data — the engine has no opinion about what
/// style 5 looks like, it just plays what it was handed at level start. So this
/// is the mod's table now, and a mod that wants a different candle changes it
/// here.
/// </para>
///
/// <para>
/// Styles 0..12 are these. 13..31 are unused, 32..62 are what vbsp assigns to
/// switchable lights, and 63 is a test slot the world sets to black.
/// </para>
/// </summary>
public static class LightStyles
{
    /// <summary>Steady, at the brightness vrad baked. What an unpatterned light shows.</summary>
    public const string Normal = "m";

    /// <summary>Black. Not removal — the lightmap layer is still there, scaled to nothing.</summary>
    public const string Off = "a";

    private static readonly ImmutableArray<string> Presets =
    [
        /* 0  normal                         */ "m",
        /* 1  flicker (first variety)        */ "mmnmmommommnonmmonqnmmo",
        /* 2  slow strong pulse              */ "abcdefghijklmnopqrstuvwxyzyxwvutsrqponmlkjihgfedcba",
        /* 3  candle (first variety)         */ "mmmmmaaaaammmmmaaaaaabcdefgabcdefg",
        /* 4  fast strobe                    */ "mamamamamama",
        /* 5  gentle pulse                   */ "jklmnopqrstuvwxyzyxwvutsrqponmlkj",
        /* 6  flicker (second variety)       */ "nmonqnmomnmomomno",
        /* 7  candle (second variety)        */ "mmmaaaabcdefgmmmmaaaammmaamm",
        /* 8  candle (third variety)         */ "mmmaaammmaaammmabcdefaaaammmmabcdefmmmaaaa",
        /* 9  slow strobe                    */ "aaaaaaaazzzzzzzz",
        /* 10 fluorescent flicker            */ "mmamammmmammamamaaamammma",
        /* 11 slow pulse, not to black       */ "abcdefghijklmnopqrrqponmlkjihgfedcba",
        /* 12 underwater light mutation      */ "mmnnmmnnnmmnn",
    ];

    /// <summary>How many presets there are, for tests.</summary>
    public static int PresetCount => Presets.Length;

    /// <summary>
    /// One of the engine's preset animations by number, or steady normal for a
    /// number outside the table.
    ///
    /// <para>
    /// Falling back rather than throwing, because the number comes from a map's
    /// "defaultstyle" key and a mapper's typo should give a lit room rather than
    /// a failed spawn. Stock answers "m" here for the same reason.
    /// </para>
    /// </summary>
    public static string Default(int styleIndex)
        => (uint)styleIndex < (uint)Presets.Length ? Presets[styleIndex] : Normal;
}
