//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.Effects;

/// <summary>
/// The client's effect dispatch-by-name table: which registered effect a
/// dispatched name runs. The decision of <c>c_te_effect_dispatch.cpp</c>'s
/// <c>DispatchEffectToCallback</c>, which walks the
/// <c>CClientEffectRegistration</c> list from its head and runs the FIRST
/// registration whose name matches with <c>Q_stricmp</c>.
///
/// <para>
/// The entries are in stock's walk order. Natively that order is the reverse of
/// static construction (each registration pushes itself on the head), so on a
/// duplicate name the LAST registration constructed wins. Stock's build here has
/// no duplicate (the recorded table, <c>Fixtures/effect-dispatch-stock.tsv</c>,
/// is 26 distinct names), and <see cref="Add"/> keeps the rule anyway rather
/// than inventing a second one.
/// </para>
/// </summary>
public sealed class EffectDispatchTable
{
    private readonly List<string> _names = [];

    /// <summary>The names, in walk order.</summary>
    public IReadOnlyList<string> Names => _names;

    /// <summary>How many registrations the table holds.</summary>
    public int Count => _names.Count;

    /// <summary>
    /// Appends a registration at the END of the walk, so a table filled in
    /// stock's walk order (head first) walks as stock's did. Answers its index.
    /// </summary>
    public int Add(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _names.Add(name);
        return _names.Count - 1;
    }

    /// <summary>Forgets every registration.</summary>
    public void Clear() => _names.Clear();

    /// <summary>
    /// The walk index of the first registration whose name matches
    /// <paramref name="name"/> as <c>Q_stricmp</c> compares, or -1 for none
    /// (stock's <c>"DispatchEffect: effect '%s' not found on client"</c>).
    /// </summary>
    public int Lookup(string name)
    {
        for (int i = 0; i < _names.Count; i++)
        {
            if (StockNameEquals(_names[i], name))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// <c>Q_stricmp(a, b) == 0</c>: byte by byte, folding only ASCII
    /// <c>A</c>-<c>Z</c> (tier1's <c>FastASCIIToLower</c>). Not
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>, which folds every
    /// Unicode letter and would match names stock does not.
    /// </summary>
    public static bool StockNameEquals(string a, string b)
    {
        if (a.Length != b.Length)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (FoldAscii(a[i]) != FoldAscii(b[i]))
                return false;
        }

        return true;
    }

    private static char FoldAscii(char c) => c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
}
