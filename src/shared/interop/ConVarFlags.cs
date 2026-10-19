//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>Renders a raw flag word as names.</summary>
public static class ConVarFlagNames
{
    /// <summary>
    /// Spells out the bits that are set, in bit order.
    ///
    /// Written by hand rather than through Enum.ToString, because a word with a
    /// bit nobody named comes back from that as a decimal number spliced into a
    /// list of names — and three of the 32 bits are unassigned in this SDK and
    /// free to appear in a convar registered by something newer. An unnamed bit
    /// is reported by its position instead, which at least says what it is.
    ///
    /// Bit order rather than declaration order, so the same set of flags always
    /// renders the same way and two rows of a listing can be compared.
    /// </summary>
    public static string Describe(int flags)
    {
        if (flags == 0)
            return "None";

        var names = new List<string>();

        for (int bit = 0; bit < 32; bit++)
        {
            uint mask = 1u << bit;

            if (((uint)flags & mask) == 0)
                continue;

            names.Add(Enum.IsDefined(typeof(ConVarFlags), mask)
                ? ((ConVarFlags)mask).ToString()
                : $"Bit{bit}");
        }

        return string.Join(", ", names);
    }

    /// <summary>The same, for a flag word already typed.</summary>
    public static string Describe(ConVarFlags flags) => Describe(unchecked((int)flags));
}
