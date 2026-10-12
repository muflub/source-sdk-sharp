using System.Runtime.CompilerServices;

namespace SourceSharp.Abi;

/// <summary>
/// A fixed-length run of networked values inside a state object.
///
/// <para>
/// Declared with <see cref="NetArrayAttribute"/> on a partial property, and the
/// length is part of the declaration rather than of the data: it is the wire
/// format, so it cannot be decided at runtime any more than the field list can.
/// </para>
///
/// <para>
/// It is NOT an array of props — native never learns that these belong together.
/// The generator lays the elements out as that many consecutive fields, named
/// <c>Field.0</c> upward, and both sides walk the same list in the same order
/// for the same reason every other field pairs. An array is a convenience for
/// the author and nothing at all to the wire.
/// </para>
///
/// <para>
/// Assigning an element writes through to the entity's blob exactly as a plain
/// property does. The array object itself is never replaced, which is why the
/// property is get-only: replacing it would silently detach the elements from
/// the entity they write into.
/// </para>
/// </summary>
public sealed class NetArray<T> where T : unmanaged
{
    private readonly NetState _owner;
    private readonly string _field;
    private readonly int _offset;
    private readonly int _length;

    public NetArray(NetState owner, string field, int offset, int length)
    {
        _owner  = owner;
        _field  = field;
        _offset = offset;
        _length = length;
    }

    public int Length => _length;

    /// <summary>How many bytes one element occupies in the blob.</summary>
    public static int ElementSize => Unsafe.SizeOf<T>();

    /// <summary>
    /// An element, read from and written to the state's storage directly.
    ///
    /// <para>
    /// There is no managed copy of the run any more (Part 9). It held one, and
    /// the copy and the blob were meant to agree with nothing comparing them —
    /// which for the pane vector meant the server could believe a pane was gone
    /// while the bytes on the wire said otherwise.
    /// </para>
    ///
    /// <para>
    /// An out-of-range index is a mistake in code and says so, the same way an
    /// array does: the run's length is the wire format, so an index past it is
    /// not a value to clamp.
    /// </para>
    /// </summary>
    public T this[int index]
    {
        get
        {
            Check(index);
            return _owner.Blob(_field).Read<T>(_offset + index * ElementSize);
        }
        set
        {
            Check(index);
            _owner.Blob(_field).Write(_offset + index * ElementSize, value);
        }
    }

    /// <summary>
    /// Sets every element, which is the usual way one of these starts.
    ///
    /// One view for the whole run rather than one per element: the check that
    /// produces it is the same check every time, and a run of sixteen words is
    /// exactly where paying for it sixteen times starts to show.
    /// </summary>
    public void Fill(T value)
    {
        var view = _owner.Blob(_field);

        for (int i = 0; i < _length; i++)
            view.Write(_offset + i * ElementSize, value);
    }

    private void Check(int index)
    {
        if ((uint)index >= (uint)_length)
        {
            throw new IndexOutOfRangeException(
                $"{_field}[{index}] is outside the {_length} element(s) declared for it; " +
                $"the length of a networked run is its wire format and cannot be exceeded");
        }
    }
}

/// <summary>
/// Marks a partial property as a fixed-length run of networked values.
///
/// The length is the wire format. Changing it renumbers every offset after it,
/// on both sides at once — which is the same rule every other field follows, and
/// the reason it is stated here rather than sized at runtime.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class NetArrayAttribute : Attribute
{
    public NetArrayAttribute(int length)
    {
        Length = length;
    }

    /// <summary>How many elements. Part of the declaration, not of the data.</summary>
    public int Length { get; }

    /// <summary>
    /// Bits per ELEMENT on the wire, or zero for the element type's own width.
    /// Narrowing here narrows every element and none of the storage.
    /// </summary>
    public int Bits { get; init; }

    /// <summary>
    /// The wire name to build the per-element names from, or null for the
    /// property's. Elements are that name, a dot, and the index.
    /// </summary>
    public string? Name { get; init; }
}
