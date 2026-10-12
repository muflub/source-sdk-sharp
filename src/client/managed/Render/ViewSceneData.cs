using System.Runtime.InteropServices;

using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render;

/// <summary>
/// One world view's scene as managed holds it (C7 step 2): the header, the
/// leaves and the entries of a <c>SourceSharpViewScene_t</c> buffer, copied out
/// of native memory or a recorded <c>scenebytes</c> line.
/// </summary>
public sealed class ViewSceneData
{
    public ViewSceneData(ViewScene header, ViewSceneLeaf[] leaves, ViewSceneEntry[] entries)
    {
        Header = header;
        Leaves = leaves;
        Entries = entries;
    }

    public ViewScene Header { get; }

    public ViewSceneLeaf[] Leaves { get; }

    public ViewSceneEntry[] Entries { get; }

    /// <summary>The buffer's three parts, or null when its size is not what its header says.</summary>
    public static ViewSceneData? FromBytes(ReadOnlySpan<byte> bytes)
    {
        int headerSize = Marshal.SizeOf<ViewScene>();
        if (bytes.Length < headerSize)
            return null;
        ViewScene header = MemoryMarshal.Read<ViewScene>(bytes);
        int leafSize = Marshal.SizeOf<ViewSceneLeaf>(), entrySize = Marshal.SizeOf<ViewSceneEntry>();
        if (header.LeafCount < 0 || header.EntryCount < 0 ||
            bytes.Length != headerSize + header.LeafCount * leafSize + header.EntryCount * entrySize)
            return null;
        var leaves = MemoryMarshal.Cast<byte, ViewSceneLeaf>(bytes.Slice(headerSize, header.LeafCount * leafSize)).ToArray();
        var entries = MemoryMarshal.Cast<byte, ViewSceneEntry>(bytes.Slice(headerSize + header.LeafCount * leafSize)).ToArray();
        return new ViewSceneData(header, leaves, entries);
    }
}
