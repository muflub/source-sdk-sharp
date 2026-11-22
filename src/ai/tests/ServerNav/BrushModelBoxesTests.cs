using System.Buffers.Binary;
using SourceSharp.Ai.Server.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// <see cref="BrushModelBoxes"/>: a brush model's volume as its brushes' boxes. The lumps are
/// built by hand: model 0's head node splits x at 0, each side a leaf holding one brush, the brushes
/// boxes of six axis-aligned sides, x -200 to -100 and 100 to 200, y and z 0 to 64; model 1 is a
/// lone leaf holding the second brush.
/// </summary>
public sealed class BrushModelBoxesTests
{
    private static readonly (float Nx, float Ny, float Nz, float Dist)[] PlaneTable =
    [
        (1, 0, 0, 0), // 0: the node's split
        (1, 0, 0, -100), (-1, 0, 0, 200), // 1, 2: brush 0's x faces
        (1, 0, 0, 200), (-1, 0, 0, -100), // 3, 4: brush 1's x faces
        (0, 1, 0, 64), (0, -1, 0, 0), (0, 0, 1, 64), (0, 0, -1, 0), // 5-8: y and z faces of both
    ];

    private static byte[] Bytes(int size, Action<Span<byte>> fill)
    {
        var b = new byte[size];
        fill(b);
        return b;
    }

    /// <summary>The lumps, leaves of <paramref name="leafVersion"/>'s size; brush 1's sides from <paramref name="secondSides"/>.</summary>
    private static BspTreeLumps Lumps(int leafVersion = 1, ushort[]? secondSides = null)
    {
        ushort[] sides0 = [1, 2, 5, 6, 7, 8];
        ushort[] sides1 = secondSides ?? [3, 4, 5, 6, 7, 8];
        byte[] planes = Bytes(PlaneTable.Length * 20, s =>
        {
            for (int i = 0; i < PlaneTable.Length; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(s[(i * 20)..], PlaneTable[i].Nx);
                BinaryPrimitives.WriteSingleLittleEndian(s[(i * 20 + 4)..], PlaneTable[i].Ny);
                BinaryPrimitives.WriteSingleLittleEndian(s[(i * 20 + 8)..], PlaneTable[i].Nz);
                BinaryPrimitives.WriteSingleLittleEndian(s[(i * 20 + 12)..], PlaneTable[i].Dist);
            }
        });
        byte[] nodes = Bytes(32, s =>
        {
            BinaryPrimitives.WriteInt32LittleEndian(s, 0);
            BinaryPrimitives.WriteInt32LittleEndian(s[4..], -1 - 1); // front: leaf 1 (x >= 0)
            BinaryPrimitives.WriteInt32LittleEndian(s[8..], -1 - 0); // back: leaf 0
        });
        int leafBytes = leafVersion == 0 ? 56 : 32;
        byte[] leaves = Bytes(2 * leafBytes, s =>
        {
            for (int l = 0; l < 2; l++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(s[(l * leafBytes)..], 1);
                BinaryPrimitives.WriteUInt16LittleEndian(s[(l * leafBytes + 24)..], (ushort)l);
                BinaryPrimitives.WriteUInt16LittleEndian(s[(l * leafBytes + 26)..], 1);
            }
        });
        byte[] models = Bytes(2 * 48, s =>
        {
            BinaryPrimitives.WriteInt32LittleEndian(s[36..], 0);
            BinaryPrimitives.WriteInt32LittleEndian(s[(48 + 36)..], -1 - 1);
        });
        byte[] leafBrushes = Bytes(4, s => BinaryPrimitives.WriteUInt16LittleEndian(s[2..], 1));
        byte[] brushes = Bytes(24, s =>
        {
            BinaryPrimitives.WriteInt32LittleEndian(s, 0);
            BinaryPrimitives.WriteInt32LittleEndian(s[4..], sides0.Length);
            BinaryPrimitives.WriteInt32LittleEndian(s[12..], sides0.Length);
            BinaryPrimitives.WriteInt32LittleEndian(s[16..], sides1.Length);
        });
        byte[] sides = Bytes((sides0.Length + sides1.Length) * 8, s =>
        {
            ushort[] all = [.. sides0, .. sides1];
            for (int i = 0; i < all.Length; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(s[(i * 8)..], all[i]);
        });
        return new BspTreeLumps(planes, nodes, leaves, leafVersion, models, leafBrushes, brushes, sides);
    }

    private static readonly HurtVolume Left = new(new AiVector(-200, 0, 0), new AiVector(-100, 64, 64));
    private static readonly HurtVolume Right = new(new AiVector(100, 0, 0), new AiVector(200, 64, 64));

    [Fact]
    public void AModelOfTwoBrushesIsTheirTwoBoxesNotTheBoxAroundThem()
    {
        Assert.Equal([Left, Right], BrushModelBoxes.Of(Lumps(), 0, default));
    }

    [Fact]
    public void TheBoxesAreMovedByTheEntitysOrigin()
    {
        var origin = new AiVector(1000, 2000, 3000);
        Assert.Equal(new HurtVolume(Right.Min + origin, Right.Max + origin), Assert.Single(BrushModelBoxes.Of(Lumps(), 1, origin)));
    }

    [Fact]
    public void LeavesOfVersionZeroHoldTheirBrushesInTheSamePlace()
    {
        Assert.Equal([Left, Right], BrushModelBoxes.Of(Lumps(leafVersion: 0), 0, default));
    }

    [Fact]
    public void ABrushWithoutASideOnEveryFaceOfABoxHasNoBox()
    {
        Assert.Null(BrushModelBoxes.Box(Lumps(secondSides: [3, 5, 6, 7, 8]), 1));
    }

    [Fact]
    public void AModelTheLumpsDoNotHoldHasNoBoxes()
    {
        Assert.Empty(BrushModelBoxes.Of(Lumps(), 2, default));
    }
}
