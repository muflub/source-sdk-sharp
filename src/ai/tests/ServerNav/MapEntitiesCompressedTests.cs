using System.Buffers.Binary;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// A shipped map (TF2's) stores its lumps LZMA-compressed: its header's uncompressed size is
/// set. The MapFormats this binding runs cannot decode them, so the entities are read as none
/// and say why, rather than parsed out of compressed bytes; the navigation still loads.
/// </summary>
public class MapEntitiesCompressedTests
{
    private static byte[] Compressed(int lump)
    {
        byte[] bsp = TinyBsp.Make(TinyBsp.Stock);
        BinaryPrimitives.WriteInt32LittleEndian(bsp.AsSpan(8 + lump * 16 + 12), 4096);
        return bsp;
    }

    [Fact]
    public void APlainMapsEntitiesAreRead()
    {
        MapEntities e = MapEntities.Read(TinyBsp.Make(TinyBsp.Stock));
        Assert.Equal((2, (string?)null), (e.All.Count, e.Unread));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    public void ACompressedEntityOrModelLumpIsReadAsNoEntitiesAndSaysSo(int lump)
    {
        MapEntities e = MapEntities.Read(Compressed(lump));
        Assert.Empty(e.All);
        Assert.Contains($"bsp lump {lump} is LZMA-compressed", e.Unread, StringComparison.Ordinal);
    }

    [Fact]
    public void AMapWithCompressedEntitiesStillLoadsItsNavigation()
    {
        byte[] bsp = Compressed(0);
        byte[] nav = NavFixtures.Course(Nav3dBspBinding.Of(bsp)).Write();
        NavLoadResult r = NavLoader.Load("course", p => p.EndsWith(".bsp", StringComparison.Ordinal) ? bsp : nav, null);
        Assert.Null(r.Refusal);
        Assert.NotNull(r.Nav!.Entities.Unread);
    }
}
