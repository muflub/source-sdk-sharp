//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// THE ONE NAME-MATCHING RULE, on the managed side.
///
/// <para>
/// A transliteration of <c>EntityNames_Match</c> in
/// <c>game/shared/entity_name_match.h</c> — which itself exists because the
/// rule had been written twice in C++ and the two copies were a standing
/// hazard. This is a third copy, in another language, and the same hazard: a
/// managed queue that matched names differently from the native walk would be
/// a map that works for one kind of target and not the other.
/// </para>
///
/// <para>
/// So it is pinned against the C++ one by shared fixture rather than by eye:
/// <c>EntityIoTests.EveryNameCaseTheNativeRuleFixes</c> here and
/// <c>entity_name_match_test.cpp</c> there walk the same table of cases,
/// including the two stock's own comment gets wrong — a '*' in the MIDDLE of a
/// query works, and an empty name matches an empty or '*' query.
/// </para>
/// </summary>
public static class EntityNameMatch
{
    /// <summary>
    /// Whether <paramref name="name"/> answers to <paramref name="query"/>.
    /// </summary>
    /// <param name="query">A name, a classname, or a prefix ending in '*'.</param>
    /// <param name="name">What the entity is called; null and "" both mean unnamed.</param>
    public static bool Match(string? query, string? name)
    {
        // The unnamed arm, first, exactly as the header spells it: a query that
        // asks for nothing, or for anything, matches an unnamed entity.
        if (string.IsNullOrEmpty(name))
            return string.IsNullOrEmpty(query) || query[0] == '*';

        if (query is null)
            return false;

        int n = 0;
        int q = 0;

        while (n < name.Length && q < query.Length)
        {
            char cName = name[n];
            char cQuery = query[q];

            if (cName == cQuery)
            {
                // equal
            }
            else if (cName is >= 'A' and <= 'Z' && (char)(cName - 'A' + 'a') == cQuery)
            {
                // equal but for case
            }
            else if (cName is >= 'a' and <= 'z' && (char)(cName - 'a' + 'A') == cQuery)
            {
                // equal but for case
            }
            else
            {
                break;
            }

            n++;
            q++;
        }

        if (q == query.Length && n == name.Length)
            return true;

        // A '*' WHEREVER THE WALK STOPPED, which is what the C code does and
        // not what the comment beside it says: "ga*e_north" matches
        // "gate_north". Reproduced rather than corrected, for the reason the
        // header gives — correcting it would change what an existing map does.
        return q < query.Length && query[q] == '*';
    }
}
