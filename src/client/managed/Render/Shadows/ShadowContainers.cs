//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.Render.Shadows;

/// <summary>
/// tier1's <c>CUtlLinkedList</c> (the single-list kind) as the shadow manager
/// and its allocator use it: which index an add takes, and the order a walk
/// from the head visits. A freed index is the next one given out (newest freed
/// first), else the one after the last ever given; RemoveAll frees every
/// element in list order ahead of the old free list; a purge starts again at
/// 0. The walk follows the links, so it is the order elements were added at
/// the tail, not index order.
/// </summary>
internal sealed class UtlLinkedListModel
{
    public const int Invalid = 0xFFFF;

    private int[] _next = new int[64];
    private int[] _prev = new int[64];
    private bool[] _live = new bool[64];
    private int _firstFree = Invalid;
    private int _lastAlloc = -1;
    private int _head = Invalid;
    private int _tail = Invalid;

    public int Count { get; private set; }

    public int Head => _head;

    /// <summary>The highest index ever handed out (the storage a save carries).</summary>
    public int LastAlloc => _lastAlloc;

    public bool IsValid(int i) => i >= 0 && i <= _lastAlloc && _live[i];

    public int Next(int i) => _next[i];

    /// <summary><c>AddToTail()</c>: the index the new element takes, linked at the tail.</summary>
    public int AddToTail()
    {
        int elem;
        if (_firstFree == Invalid)
        {
            elem = ++_lastAlloc;
            if (elem >= _live.Length)
            {
                Array.Resize(ref _live, _live.Length * 2);
                Array.Resize(ref _next, _next.Length * 2);
                Array.Resize(ref _prev, _prev.Length * 2);
            }
        }
        else
        {
            elem = _firstFree;
            _firstFree = _next[elem];
        }
        _live[elem] = true;
        _prev[elem] = _tail;
        _next[elem] = Invalid;
        if (_tail != Invalid)
            _next[_tail] = elem;
        else
            _head = elem;
        _tail = elem;
        ++Count;
        return elem;
    }

    /// <summary><c>Remove( elem )</c>: unlinked, and the next index given out.</summary>
    public void Remove(int elem)
    {
        int p = _prev[elem], n = _next[elem];
        if (p != Invalid)
            _next[p] = n;
        else
            _head = n;
        if (n != Invalid)
            _prev[n] = p;
        else
            _tail = p;
        _live[elem] = false;
        _next[elem] = _firstFree;
        _firstFree = elem;
        --Count;
    }

    /// <summary><c>RemoveAll()</c>: the list, head to tail, becomes the front of the free list.</summary>
    public void RemoveAll()
    {
        if (_lastAlloc < 0)
            return;
        int i = _head;
        while (i != Invalid)
        {
            int next = _next[i];
            _live[i] = false;
            _next[i] = next == Invalid ? _firstFree : next;
            i = next;
        }
        if (_head != Invalid)
            _firstFree = _head;
        _head = Invalid;
        _tail = Invalid;
        Count = 0;
    }

    /// <summary><c>Purge()</c>: everything gone, and the next add takes index 0.</summary>
    public void Purge()
    {
        Array.Clear(_live);
        _firstFree = Invalid;
        _lastAlloc = -1;
        _head = Invalid;
        _tail = Invalid;
        Count = 0;
    }

    public void Save(ReloadWriter writer)
    {
        writer.Write(_lastAlloc);
        writer.Write(_firstFree);
        writer.Write(_head);
        writer.Write(_tail);
        writer.Write(Count);
        for (int i = 0; i <= _lastAlloc; i++)
        {
            writer.Write(_live[i]);
            writer.Write(_next[i]);
            writer.Write(_prev[i]);
        }
    }

    public void Restore(ReloadReader reader)
    {
        _lastAlloc = reader.ReadInt();
        _firstFree = reader.ReadInt();
        _head = reader.ReadInt();
        _tail = reader.ReadInt();
        Count = reader.ReadInt();
        int size = Math.Max(64, _lastAlloc + 1);
        _live = new bool[size];
        _next = new int[size];
        _prev = new int[size];
        for (int i = 0; i <= _lastAlloc; i++)
        {
            _live[i] = reader.ReadBool();
            _next[i] = reader.ReadInt();
            _prev[i] = reader.ReadInt();
        }
    }
}

/// <summary>
/// The shadow manager's dirty set (<c>CUtlRBTree&lt; ClientShadowHandle_t &gt;</c>
/// under a less-than): a multiset walked in ascending order. Insert does not
/// refuse a handle already there (stock's Insert does not), Find answers
/// whether one is, RemoveOne takes one away.
/// </summary>
internal sealed class DirtyShadowSet
{
    private readonly List<int> _sorted = [];

    public int Count => _sorted.Count;

    public int this[int i] => _sorted[i];

    public bool Contains(int handle) => _sorted.BinarySearch(handle) >= 0;

    /// <summary>Inserted after any equal ones, so a walk visits each copy.</summary>
    public void Insert(int handle)
    {
        int at = _sorted.BinarySearch(handle);
        if (at < 0)
            at = ~at;
        while (at < _sorted.Count && _sorted[at] == handle)
            at++;
        _sorted.Insert(at, handle);
    }

    public bool RemoveOne(int handle)
    {
        int at = _sorted.BinarySearch(handle);
        if (at < 0)
            return false;
        _sorted.RemoveAt(at);
        return true;
    }

    public int Occurrences(int handle)
    {
        int n = 0;
        foreach (int h in _sorted)
            if (h == handle)
                n++;
        return n;
    }

    public void CopyTo(List<int> into)
    {
        into.Clear();
        into.AddRange(_sorted);
    }

    public void Clear() => _sorted.Clear();

    public void Save(ReloadWriter writer)
    {
        writer.Write(_sorted.Count);
        foreach (int h in _sorted)
            writer.Write(h);
    }

    public void Restore(ReloadReader reader)
    {
        _sorted.Clear();
        int n = reader.ReadInt();
        for (int i = 0; i < n; i++)
            _sorted.Add(reader.ReadInt());
    }
}
