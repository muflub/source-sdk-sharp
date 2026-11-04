using System.Runtime.InteropServices;

using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Render;

/// <summary>
/// One world view's scene as managed holds it (C7 step 2): the header, the
/// leaves and the entries of a <c>SourceSharpViewScene_t</c> buffer, copied out
/// of native memory or a recorded <c>scenebytes</c> line.
/// </summary>
public sealed class ViewSceneData
{
    private ViewSceneLeaf[] _leaves;
    private ViewSceneEntry[] _entries;

    public ViewSceneData(ViewScene header, ViewSceneLeaf[] leaves, ViewSceneEntry[] entries)
    {
        Header = header;
        _leaves = leaves;
        _entries = entries;
        LeafCount = leaves.Length;
        EntryCount = entries.Length;
    }

    /// <summary>An empty one, for <see cref="TryFill"/>.</summary>
    public ViewSceneData()
        : this(default, [], [])
    {
    }

    public ViewScene Header { get; private set; }

    private int LeafCount { get; set; }

    private int EntryCount { get; set; }

    public ReadOnlySpan<ViewSceneLeaf> Leaves => _leaves.AsSpan(0, LeafCount);

    public ReadOnlySpan<ViewSceneEntry> Entries => _entries.AsSpan(0, EntryCount);

    /// <summary>The buffer's three parts in a new scene, or null when its size is not what its header says.</summary>
    public static ViewSceneData? FromBytes(ReadOnlySpan<byte> bytes)
    {
        var scene = new ViewSceneData();
        return scene.TryFill(bytes) ? scene : null;
    }

    /// <summary>
    /// The buffer's three parts in this scene, over what it held, keeping its
    /// arrays: false, and the scene empty, when its size is not what its header
    /// says.
    /// </summary>
    public bool TryFill(ReadOnlySpan<byte> bytes)
    {
        Header = default;
        LeafCount = 0;
        EntryCount = 0;
        int headerSize = Marshal.SizeOf<ViewScene>();
        if (bytes.Length < headerSize)
            return false;
        ViewScene header = MemoryMarshal.Read<ViewScene>(bytes);
        int leafSize = Marshal.SizeOf<ViewSceneLeaf>(), entrySize = Marshal.SizeOf<ViewSceneEntry>();
        if (header.LeafCount < 0 || header.EntryCount < 0 ||
            bytes.Length != headerSize + header.LeafCount * leafSize + header.EntryCount * entrySize)
            return false;
        MemoryMarshal.Cast<byte, ViewSceneLeaf>(bytes.Slice(headerSize, header.LeafCount * leafSize))
            .CopyTo(Scratch.Sized(ref _leaves, header.LeafCount));
        MemoryMarshal.Cast<byte, ViewSceneEntry>(bytes.Slice(headerSize + header.LeafCount * leafSize))
            .CopyTo(Scratch.Sized(ref _entries, header.EntryCount));
        Header = header;
        LeafCount = header.LeafCount;
        EntryCount = header.EntryCount;
        return true;
    }
}
