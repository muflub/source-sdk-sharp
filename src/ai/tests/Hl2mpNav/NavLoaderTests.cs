using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// Loading a level's navigation (plan_hl2mp_ai §6.3): the files through the
/// engine's file seam (a dictionary here), a refusal naming the map and the
/// file for each way it can be missing or wrong, never a fallback.
/// </summary>
public sealed class NavLoaderTests
{
    private static readonly byte[] Bsp = TinyBsp.Make(TinyBsp.Stock);

    private static byte[] NavFor(byte[] bsp) => NavFixtures.Course(Nav3dBspBinding.Of(bsp)).Write();

    private static NavLoadResult Load(Dictionary<string, byte[]> files) => NavLoader.Load("dm_test", p => files.GetValueOrDefault(p), null);

    [Fact]
    public void ANavBuiltFromThisBspLoads()
    {
        NavLoadResult r = Load(new() { ["maps/dm_test.bsp"] = Bsp, ["maps/dm_test.nav3d"] = NavFor(Bsp) });
        Assert.Null(r.Refusal);
        Assert.True(r.Nav!.Nav.Graph.CellCount > 1000);
        Assert.Single(r.Nav.Entities.Spawns);
    }

    [Fact]
    public void AMissingNavIsRefusedNamingTheMapAndTheFile()
        => Assert.Equal("no navigation for dm_test: expected maps/dm_test.nav3d",
                        Load(new() { ["maps/dm_test.bsp"] = Bsp }).Refusal);

    [Fact]
    public void AMissingBspIsRefused()
        => Assert.Equal("no maps/dm_test.bsp to bind maps/dm_test.nav3d to",
                        Load(new() { ["maps/dm_test.nav3d"] = NavFor(Bsp) }).Refusal);

    [Fact]
    public void BytesThatAreNotANavAreRefused()
    {
        string? why = Load(new() { ["maps/dm_test.bsp"] = Bsp, ["maps/dm_test.nav3d"] = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24] }).Refusal;
        Assert.StartsWith("maps/dm_test.nav3d for dm_test cannot be read: ", why, StringComparison.Ordinal);
    }

    [Fact]
    public void ANavBuiltFromAnotherBspIsRefusedByName()
    {
        byte[] other = TinyBsp.Make(TinyBsp.Stock, planes: 9);
        string? why = Load(new() { ["maps/dm_test.bsp"] = Bsp, ["maps/dm_test.nav3d"] = NavFor(other) }).Refusal;
        Assert.NotNull(why);
        Assert.StartsWith("maps/dm_test.nav3d was built from another map: it names map checksum ", why, StringComparison.Ordinal);
        Assert.Contains($"not maps/dm_test.bsp (checksum {Nav3dBspBinding.Of(Bsp).MapChecksum})", why, StringComparison.Ordinal);
    }

    [Fact]
    public void ANavNamingNoMapIsRefusedForAStockBsp()
    {
        string? why = Load(new() { ["maps/dm_test.bsp"] = Bsp, ["maps/dm_test.nav3d"] = NavFixtures.Course().Write() }).Refusal;
        Assert.Equal($"maps/dm_test.nav3d was built from another map: it names no map, not maps/dm_test.bsp (checksum {Nav3dBspBinding.Of(Bsp).MapChecksum})", why);
    }

    [Fact]
    public void ALinkedLevelBindsByItsLevelId()
    {
        var id = Guid.Parse("0f1e2d3c-4b5a-8978-8685-f4e3d2c1b0a9");
        byte[] bsp = TinyBsp.Make($"{{\n\"classname\" \"worldspawn\"\n\"ss_level_id\" \"{id}\"\n}}\n");
        byte[] nav = NavFixtures.Course(null, id).Write();
        Assert.Null(Load(new() { ["maps/dm_test.bsp"] = bsp, ["maps/dm_test.nav3d"] = nav }).Refusal);

        byte[] other = NavFixtures.Course(null, Guid.NewGuid()).Write();
        Assert.StartsWith("maps/dm_test.nav3d was built for another level: it names ",
                          Load(new() { ["maps/dm_test.bsp"] = bsp, ["maps/dm_test.nav3d"] = other }).Refusal, StringComparison.Ordinal);
    }
}
