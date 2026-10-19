using System;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's skin write (2026-10-19): the engine block's Client.Skin, which
/// the m_nSkin prop sends, written only when it changes so the mark that
/// follows names a real change.
/// </summary>
public class NetworkedSkinTests
{
    [Fact]
    public void TheOffsetIsTheClientTailsSkin()
        => Assert.Equal((int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Client))
                        + (int)Marshal.OffsetOf<EngineBlockClientTail>(nameof(EngineBlockClientTail.Skin)),
                        NetworkedSkin.Offset);

    [Fact]
    public void ASkinIsWrittenWhereTheClientReadsIt()
    {
        var block = new byte[EngineBlock.SizeBytes];

        Assert.True(NetworkedSkin.Write(block, 3));

        Assert.Equal(3, MemoryMarshal.Read<int>(block.AsSpan(NetworkedSkin.Offset, sizeof(int))));
    }

    [Fact]
    public void TheSameSkinAgainIsNoChange()
    {
        var block = new byte[EngineBlock.SizeBytes];
        NetworkedSkin.Write(block, 2);

        Assert.False(NetworkedSkin.Write(block, 2));
    }

    [Fact]
    public void AShortBlockIsNotWritten()
        => Assert.False(NetworkedSkin.Write(new byte[NetworkedSkin.Offset], 1));
}
