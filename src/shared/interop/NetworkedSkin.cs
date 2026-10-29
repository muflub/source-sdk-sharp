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
    public static bool Write(Span<byte> block, int skin) => WriteTailInt(block, Offset, skin);

    /// <summary>One int of the client tail, written only when it changed.</summary>
    internal static bool WriteTailInt(Span<byte> block, int offset, int value)
    {
        if (block.Length < offset + sizeof(int))
            return false;

        Span<byte> at = block.Slice(offset, sizeof(int));
        if (MemoryMarshal.Read<int>(at) == value)
            return false;

        MemoryMarshal.Write(at, in value);
        return true;
    }
}

/// <summary>
/// The bodygroup combination a standalone entity draws, as the server writes
/// it: the engine block's <c>Client.Body</c>, which the <c>m_nBody</c> prop
/// sends (standalone_nettable.h) and the client's <c>GetBody</c> answers.
/// </summary>
public static class NetworkedBody
{
    /// <summary>Where the body is inside the block.</summary>
    public static int Offset { get; } = EngineBlockMarks.OffsetInBlockOfClientTail(nameof(EngineBlockClientTail.Body));

    /// <summary>Writes <paramref name="body"/>: false, and nothing written, when it did not change.</summary>
    public static bool Write(Span<byte> block, int body) => NetworkedSkin.WriteTailInt(block, Offset, body);
}
