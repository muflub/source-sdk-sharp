//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// c_te_legacytempents.cpp's <c>CBreakableHelper</c>: the breakmodel gibs the
/// server sends fifteen at a time share one lighting origin, the first
/// master's of their group, so the light cache is built once rather than
/// fifteen times. A master starts a context; its slaves join it; when a
/// context's head goes, the next of its members leads. Keyed by tempent slot.
/// Replayed against the recorded helper (TempEntRenderTests).
/// </summary>
public sealed class TempEntBreakGroups
{
    private readonly List<(uint Context, long Member)> _list = [];
    private uint _context;

    /// <summary>A breakmodel joins: a master opens a new context, a slave joins the current one.</summary>
    public void Insert(long member, bool slave)
    {
        if (!slave)
            ++_context;
        _list.Add((_context, member));
    }

    /// <summary>A tempent went (every entry of it).</summary>
    public void Remove(long member) => _list.RemoveAll(e => e.Member == member);

    public void Clear()
    {
        _list.Clear();
        _context = 0;
    }

    /// <summary>The member whose origin lights <paramref name="member"/>: its context's first; -1 when it is in none.</summary>
    public long LightingOriginOf(long member)
    {
        uint current = 0;
        long head = -1;
        foreach (var (context, m) in _list)
        {
            if (context != current)
            {
                current = context;
                head = m;
            }

            if (m == member)
                return head;
        }

        return -1;
    }
}
