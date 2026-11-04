using System.Runtime.InteropServices;

using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A view's scene is filled over the one the host keeps: each view sees its own
/// counts and entries, whatever a larger view before it left in the arrays.
/// </summary>
public class ViewSceneDataTests
{
    private static byte[] Bytes(int leaves, int entries, int firstEntIndex = 1)
    {
        int headerSize = Marshal.SizeOf<ViewScene>(), leafSize = Marshal.SizeOf<ViewSceneLeaf>(), entrySize = Marshal.SizeOf<ViewSceneEntry>();
        var bytes = new byte[headerSize + leaves * leafSize + entries * entrySize];
        var header = new ViewScene { LeafCount = leaves, EntryCount = entries, OpIndex = 7 };
        MemoryMarshal.Write(bytes, in header);
        for (int i = 0; i < entries; i++)
        {
            var entry = new ViewSceneEntry { EntIndex = firstEntIndex + i };
            MemoryMarshal.Write(bytes.AsSpan(headerSize + leaves * leafSize + i * entrySize), in entry);
        }
        return bytes;
    }

    [Fact]
    public void ASmallSceneAfterALargeOneHoldsOnlyItsOwn()
    {
        var scene = new ViewSceneData();
        Assert.True(scene.TryFill(Bytes(4, 40)));
        Assert.Equal(40, scene.Entries.Length);

        Assert.True(scene.TryFill(Bytes(1, 2, firstEntIndex: 100)));

        Assert.Equal(1, scene.Leaves.Length);
        Assert.Equal(2, scene.Entries.Length);
        Assert.Equal(100, scene.Entries[0].EntIndex);
        Assert.Equal(101, scene.Entries[1].EntIndex);
        Assert.Equal(2, scene.Header.EntryCount);
    }

    [Fact]
    public void ALargerSceneGrowsTheArrays()
    {
        var scene = new ViewSceneData();
        Assert.True(scene.TryFill(Bytes(1, 2)));
        Assert.True(scene.TryFill(Bytes(8, 80, firstEntIndex: 5)));
        Assert.Equal(80, scene.Entries.Length);
        Assert.Equal(84, scene.Entries[79].EntIndex);
    }

    [Fact]
    public void ABufferOfTheWrongSizeLeavesTheSceneEmpty()
    {
        var scene = new ViewSceneData();
        Assert.True(scene.TryFill(Bytes(2, 3)));

        byte[] tooLong = [.. Bytes(2, 3), 0];
        Assert.False(scene.TryFill(tooLong));

        Assert.Equal(0, scene.Entries.Length);
        Assert.Equal(0, scene.Leaves.Length);
        Assert.Equal(0, scene.Header.EntryCount);
        Assert.Null(ViewSceneData.FromBytes(tooLong));
    }

    [Fact]
    public void AWarmFillAllocatesNothing()
    {
        var scene = new ViewSceneData();
        byte[] bytes = Bytes(4, 40);
        for (int i = 0; i < 8; i++)
            scene.TryFill(bytes);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++)
            scene.TryFill(bytes);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
