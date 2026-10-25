using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A skin the server picks reaches the client (2026-10-19): ServerSkins writes
/// the entity's engine block at <see cref="NetworkedSkin.Offset"/> and marks
/// exactly that offset, so the m_nSkin prop is in the next delta. Before this
/// the server's EntitySetSkin body was empty and the choice reached no client.
/// </summary>
public sealed unsafe class ServerSkinsTests
{
    private static byte* s_Block;
    private static int s_Marks, s_MarkedOffset;
    private static uint s_MarkedHandle;

    [UnmanagedCallersOnly]
    private static void* GetBlock(void* self, uint handle, int* size)
    {
        *size = EngineBlock.SizeBytes;
        return s_Block;
    }

    [UnmanagedCallersOnly]
    private static void Mark(void* self, uint handle, int offset)
    {
        s_Marks++;
        s_MarkedHandle = handle;
        s_MarkedOffset = offset;
    }

    private static (ServerSkins Skins, FakeShimRoot Root) Build()
    {
        var root = new FakeShimRoot();
        root.Live("SourceSharpNetworkServer001", nameof(ShimNetworkServerFwd.EntityGetEngineBlock),
                  (nint)(delegate* unmanaged<void*, uint, int*, void*>)&GetBlock);
        root.Live("SourceSharpNetworkServer001", nameof(ShimNetworkServerFwd.EntityMarkBlockChanged),
                  (nint)(delegate* unmanaged<void*, uint, int, void>)&Mark);
        var side = TestSides.Create(Side.Server);
        side.Attachment.Initialize();
        Assert.True(side.Attachment.AttachShims(root.Bind(), out string why), why);
        s_Marks = 0;
        s_MarkedOffset = -1;
        return (ServerSkins.For(new ServerModuleEngine(side)), root);
    }

    [Fact]
    public void ASkinIsWrittenIntoTheBlockAndItsOffsetMarked()
    {
        s_Block = (byte*)NativeMemory.AllocZeroed((nuint)EngineBlock.SizeBytes);
        var (skins, root) = Build();
        try
        {
            var can = new EntityHandle(0x00020007u);

            skins.SetSkin(can, 4);

            Assert.Equal(4, *(int*)(s_Block + NetworkedSkin.Offset));
            Assert.Equal((1, can.Value, NetworkedSkin.Offset), (s_Marks, s_MarkedHandle, s_MarkedOffset));
        }
        finally
        {
            root.Dispose();
            NativeMemory.Free(s_Block);
        }
    }

    [Fact]
    public void TheSameSkinAgainMarksNothing()
    {
        s_Block = (byte*)NativeMemory.AllocZeroed((nuint)EngineBlock.SizeBytes);
        var (skins, root) = Build();
        try
        {
            var can = new EntityHandle(0x00020007u);
            skins.SetSkin(can, 2);
            skins.SetSkin(can, 2);

            Assert.Equal(1, s_Marks);
        }
        finally
        {
            root.Dispose();
            NativeMemory.Free(s_Block);
        }
    }
}
