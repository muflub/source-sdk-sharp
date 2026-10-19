using System;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// The skin a standalone entity draws, as the server writes it: the engine
/// block's <c>Client.Skin</c>, which the <c>m_nSkin</c> prop sends
/// (standalone_nettable.h) and the client's <c>GetSkin</c> answers.
/// </summary>
public static class NetworkedSkin
{
    /// <summary>Where the skin is inside the block.</summary>
    public static int Offset { get; } = EngineBlockMarks.OffsetInBlockOfClientTail(nameof(EngineBlockClientTail.Skin));

    /// <summary>
    /// Writes <paramref name="skin"/> into <paramref name="block"/>. False, and
    /// nothing written, for a block too short to hold it or a skin that did not
    /// change, so the caller marks only a real change.
    /// </summary>
    public static bool Write(Span<byte> block, int skin)
    {
        if (block.Length < Offset + sizeof(int))
            return false;

        Span<byte> at = block.Slice(Offset, sizeof(int));
        if (MemoryMarshal.Read<int>(at) == skin)
            return false;

        MemoryMarshal.Write(at, in skin);
        return true;
    }
}
