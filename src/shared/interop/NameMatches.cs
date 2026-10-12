using System;
using System.Buffers;
using System.Collections.Generic;

namespace SourceSharp.Abi;

/// <summary>
/// Every entity answering to a name, for <c>foreach</c>, allocating nothing.
///
/// <para>
/// <b>THE CONTRACT.</b> The matches are a SNAPSHOT taken when the lookup was
/// made, in name-lookup order (shelled entities in creation order, then
/// shell-less ones by entry). Walking it is safe while the loop body changes
/// the entity set, which is the usual caller's whole point (firing an input at
/// every match can kill or spawn entities):
/// </para>
/// <list type="bullet">
/// <item>an entity removed before the loop reaches it is SKIPPED;</item>
/// <item>an entity spawned during the loop is NOT visited, even if it matches;</item>
/// <item>a match renamed during the loop is still visited (it matched when asked).</item>
/// </list>
/// <para>
/// That is what stock's "call again with the previous handle" chaining gave, and
/// what a lazy walk over the live name index could not: it would throw on the
/// index changing, or skip.
/// </para>
///
/// <para>
/// <b>ALLOCATION.</b> The snapshot lives in an <see cref="ArrayPool{T}"/> buffer,
/// and the enumerator is this ref struct itself, so a <c>foreach</c> allocates
/// nothing. <c>foreach</c> disposes it, which returns the buffer; the buffer
/// moves to the enumerator on <see cref="GetEnumerator"/>, so a <c>using</c>
/// around the same value returns it at most once.
/// </para>
/// </summary>
public ref struct NameMatches
{
    private EntityHandle[]? _rented;
    private readonly int _count;
    private readonly Func<EntityHandle, bool>? _alive;
    private int _next;
    private EntityHandle _current;

    /// <param name="rented">A buffer rented from <see cref="ArrayPool{T}.Shared"/>; ownership passes here.</param>
    /// <param name="count">How many of its entries are the matches.</param>
    /// <param name="alive">Whether a match still exists when the walk reaches it.</param>
    internal NameMatches(EntityHandle[]? rented, int count, Func<EntityHandle, bool>? alive)
    {
        _rented = rented;
        _count = rented is null ? 0 : count;
        _alive = alive;
        _next = 0;
        _current = EntityHandle.None;
    }

    /// <summary>No matches.</summary>
    public static NameMatches None => default;

    /// <summary>A snapshot of a list the caller already holds (a recording fake's answer).</summary>
    public static NameMatches Copy(IReadOnlyList<EntityHandle>? handles)
    {
        if (handles is null || handles.Count == 0)
            return default;

        EntityHandle[] rented = ArrayPool<EntityHandle>.Shared.Rent(handles.Count);
        for (int i = 0; i < handles.Count; i++)
            rented[i] = handles[i];

        return new NameMatches(rented, handles.Count, null);
    }

    /// <summary>How many entities matched when the lookup was made.</summary>
    public readonly int Count => _count;

    /// <summary>The walk. The buffer moves with it, so only the walk returns it.</summary>
    public NameMatches GetEnumerator()
    {
        NameMatches walk = this;
        _rented = null;
        return walk;
    }

    /// <summary>The match the walk is on.</summary>
    public readonly EntityHandle Current => _current;

    /// <summary>The next match that still exists.</summary>
    public bool MoveNext()
    {
        while (_rented is not null && _next < _count)
        {
            EntityHandle handle = _rented[_next++];

            if (_alive is null || _alive(handle))
            {
                _current = handle;
                return true;
            }
        }

        _current = EntityHandle.None;
        return false;
    }

    /// <summary>Returns the buffer. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_rented is null)
            return;

        ArrayPool<EntityHandle>.Shared.Return(_rented);
        _rented = null;
    }
}
