//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.Render.PixelVisibility;

/// <summary>
/// tier1's CUtlMultiList&lt;T, unsigned short&gt; as pixel visibility uses it:
/// one pool of elements and any number of doubly-linked lists over it. The
/// index arithmetic is the header's, because callers keep indices (a set's
/// node is half of a caller's handle): Alloc takes the internal free list's
/// head or grows by one, LinkToHead puts an element at a list's head,
/// RemoveAll destructs from the highest index down, and Purge forgets
/// everything, lists included, so indices start at 0 again.
/// </summary>
internal sealed class UtlMultiList<T>(Func<T> construct, Action<T>? destruct = null) where T : class
{
    public const ushort InvalidIndex = 0xFFFF;

    private struct Element
    {
        public T? Value;
        public ushort Next;
        public ushort Previous;
    }

    private struct ListHead
    {
        public ushort Head;
        public ushort Tail;
        public int Count;
    }

    private readonly List<Element> _memory = [];
    private readonly List<ListHead> _lists = [];
    private ushort _firstFree = InvalidIndex;
    private int _totalElements;
    private int _maxElementIndex;

    public T this[ushort i] => _memory[i].Value!;

    public ushort CreateList()
    {
        _lists.Add(new ListHead { Head = InvalidIndex, Tail = InvalidIndex, Count = 0 });
        return (ushort)( _lists.Count - 1 );
    }

    public int TotalCount => _totalElements;

    /// <summary>One past the highest index ever handed out (m_MaxElementIndex): what to walk to find every element.</summary>
    public int TotalSlots => _maxElementIndex;

    public int Count(ushort list) => _lists[list].Count;

    public ushort Head(ushort list) => _lists[list].Head;

    public ushort Next(ushort i) => _memory[i].Next;

    /// <summary>An allocated element, linked or not (a free one is not valid).</summary>
    public bool IsValidIndex(ushort i)
        => i < _maxElementIndex && ( _memory[i].Previous != i || _memory[i].Next == i );

    private bool IsInList(ushort i) => i < _maxElementIndex && _memory[i].Previous != i;

    public ushort Alloc()
    {
        ushort elem;
        if (_firstFree == InvalidIndex)
        {
            elem = (ushort)_maxElementIndex;
            ++_maxElementIndex;
            if (_memory.Count < _maxElementIndex)
                _memory.Add(default);
        }
        else
        {
            elem = _firstFree;
            _firstFree = _memory[_firstFree].Next;
        }

        Element e = _memory[elem];
        e.Next = elem;
        e.Previous = elem;
        e.Value = construct();
        _memory[elem] = e;
        ++_totalElements;
        return elem;
    }

    public void Unlink(ushort list, ushort elem)
    {
        if (!IsInList(elem))
            return;
        Element old = _memory[elem];
        ListHead head = _lists[list];

        if (old.Previous != InvalidIndex)
            SetNext(old.Previous, old.Next);
        else
            head.Head = old.Next;

        if (old.Next != InvalidIndex)
            SetPrevious(old.Next, old.Previous);
        else
            head.Tail = old.Previous;

        old.Previous = elem;
        old.Next = elem;
        _memory[elem] = old;
        --head.Count;
        _lists[list] = head;
    }

    /// <summary>LinkAfter( list, InvalidIndex, elem ): unlinked first, then made the head.</summary>
    public void LinkToHead(ushort list, ushort elem)
    {
        Unlink(list, elem);
        ListHead head = _lists[list];
        Element e = _memory[elem];
        e.Previous = InvalidIndex;
        e.Next = head.Head;
        _memory[elem] = e;
        head.Head = elem;
        if (e.Next == InvalidIndex)
            head.Tail = elem;
        else
            SetPrevious(e.Next, elem);
        ++head.Count;
        _lists[list] = head;
    }

    /// <summary>RemoveAll then the memory and the lists: everything gone, indices from 0 again.</summary>
    public void Purge()
    {
        RemoveAll();
        _lists.Clear();
        _memory.Clear();
        _firstFree = InvalidIndex;
        _totalElements = 0;
        _maxElementIndex = 0;
    }

    private void RemoveAll()
    {
        if (_maxElementIndex == 0)
            return;
        ushort prev = InvalidIndex;
        for (int i = _maxElementIndex; --i >= 0;)
        {
            ushort u = (ushort)i;
            Element e = _memory[u];
            if (IsValidIndex(u) && e.Value is { } value)
                destruct?.Invoke(value);
            e.Next = prev;
            e.Previous = u;
            _memory[u] = e;
            prev = u;
        }
        _firstFree = 0;
        for (int l = 0; l < _lists.Count; l++)
            _lists[l] = new ListHead { Head = InvalidIndex, Tail = InvalidIndex, Count = 0 };
        _totalElements = 0;
    }

    private void SetNext(ushort i, ushort next)
    {
        Element e = _memory[i];
        e.Next = next;
        _memory[i] = e;
    }

    private void SetPrevious(ushort i, ushort previous)
    {
        Element e = _memory[i];
        e.Previous = previous;
        _memory[i] = e;
    }
}
