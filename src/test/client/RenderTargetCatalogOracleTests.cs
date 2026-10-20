using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed render-target catalog (<see cref="RenderTargetCatalog"/>)
/// against stock's rendertexture.cpp, run offline from the pristine file by
/// src/test/oracle/native/rendertexture_oracle_test.cpp
/// (<c>Fixtures/rendertexture-stock.tsv</c>, README beside it).
///
/// <para>
/// Each getter's row is read off the recording: the name, group, complaint and
/// creation flags of its first FindTexture, and where in the release
/// function's order its reference was dropped (or that it was kept). The
/// catalog must declare exactly those, slot by slot, in rendertexture.h's
/// order, which is the order the native store's SS_RT_* slots are numbered in.
/// </para>
/// </summary>
public class RenderTargetCatalogOracleTests
{
    private sealed record StockRow(string Getter, int Arg, string Name, string Group, bool Complain, int Flags, int ReleaseOrder);

    private static List<StockRow> Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "rendertexture-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock render textures are missing: " + path);
        var lines = File.ReadAllLines(path).Where(l => l.Length > 0).Select(l => l.Split('\t')).ToList();

        var rows = new List<StockRow>();
        var slotOfTexture = new Dictionary<string, int>(StringComparer.Ordinal);
        var releaseOrder = new Dictionary<int, int>();
        string phase = "";
        int slot = -1;
        foreach (string[] f in lines)
        {
            switch (f[0])
            {
                case "phase":
                    phase = f[1];
                    break;
                case "G" when phase == "first":
                    rows.Add(new StockRow(f[1], f[2] == "-" ? -1 : int.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture), "", "", false, 0, 0));
                    slot = rows.Count - 1;
                    break;
                case "findtexture" when phase == "first":
                    rows[slot] = rows[slot] with { Name = f[1], Group = f[2], Complain = f[3] == "1", Flags = int.Parse(f[4], System.Globalization.CultureInfo.InvariantCulture) };
                    break;
                case "holds" when phase == "first":
                    slotOfTexture[f[1]] = slot;
                    break;
                case "dec" when phase == "release":
                    releaseOrder[slotOfTexture[f[1]]] = releaseOrder.Count + 1;
                    break;
            }
        }
        for (int i = 0; i < rows.Count; i++)
            rows[i] = rows[i] with { ReleaseOrder = releaseOrder.TryGetValue(i, out int o) ? o : 0 };

        // The stimulus reached the recording: every getter found a texture, and some were released and some kept.
        Assert.True(rows.Count == 17 && rows.All(r => r.Name.Length > 0), $"the oracle parsed ({rows.Count} getters)");
        Assert.True(releaseOrder.Count > 0 && releaseOrder.Count < rows.Count, $"the release dropped {releaseOrder.Count} of {rows.Count}");
        return rows;
    }

    private sealed class Recorder : IRenderTargetTable
    {
        public readonly List<(int Slot, string Name, string Group, bool Complain, int Flags, int ReleaseOrder)> Rows = [];

        public bool Declare(int slot, string name, string group, bool complain, int creationFlags, int releaseOrder)
        {
            Rows.Add((slot, name, group, complain, creationFlags, releaseOrder));
            return true;
        }
    }

    [Fact]
    public void EachSlotIsTheGetterStockCalledInTheHeadersOrder()
    {
        var stock = Stock();
        var catalog = new RenderTargetCatalog();
        Assert.Equal(stock.Count, catalog.Rows.Count);
        Assert.True(stock.Count == (int)RenderTargetSlot.Count, $"stock calls {stock.Count} getters, the slots number {(int)RenderTargetSlot.Count}");
        for (int i = 0; i < stock.Count; i++)
            Assert.Equal((RenderTargetSlot)i, catalog.Rows[i].Slot);
    }

    [Fact]
    public void EachSlotFindsTheNameStockFound()
    {
        var stock = Stock();
        var catalog = new RenderTargetCatalog();
        for (int i = 0; i < stock.Count; i++)
            Assert.True(stock[i].Name == catalog.Rows[i].Name, $"{stock[i].Getter}({stock[i].Arg}): stock {stock[i].Name}, managed {catalog.Rows[i].Name}");
    }

    [Fact]
    public void EachSlotAsksInStocksGroupWithStocksComplaintAndFlags()
    {
        var stock = Stock();
        var catalog = new RenderTargetCatalog();
        for (int i = 0; i < stock.Count; i++)
        {
            Assert.Equal(stock[i].Group, catalog.Rows[i].Group);
            Assert.Equal(stock[i].Complain, catalog.Rows[i].Complain);
            Assert.Equal(stock[i].Flags, catalog.Rows[i].CreationFlags);
        }
    }

    [Fact]
    public void TheReleaseDropsStocksSetInStocksOrder()
    {
        var stock = Stock();
        var catalog = new RenderTargetCatalog();
        for (int i = 0; i < stock.Count; i++)
            Assert.True(stock[i].ReleaseOrder == catalog.Rows[i].ReleaseOrder,
                        $"{stock[i].Getter}({stock[i].Arg}): stock releases it {stock[i].ReleaseOrder} (0 = kept), managed {catalog.Rows[i].ReleaseOrder}");
    }

    [Fact]
    public void TheFrameBufferAndTeenyNamesFollowStocksNumbering()
    {
        Assert.Equal("_rt_FullFrameFB", RenderTargetCatalog.FrameBufferName(0));
        Assert.Equal("_rt_FullFrameFB2", RenderTargetCatalog.FrameBufferName(2));
        Assert.Equal("_rt_TeenyFB1", RenderTargetCatalog.TeenyName(1));
    }

    [Fact]
    public void DeclaringHandsEveryRowToTheTableInSlotOrder()
    {
        var catalog = new RenderTargetCatalog();
        var table = new Recorder();
        Assert.Equal(17, catalog.DeclareTo(table));
        Assert.Equal(17, table.Rows.Count);
        for (int i = 0; i < table.Rows.Count; i++)
        {
            RenderTargetRow r = catalog.Rows[i];
            Assert.Equal(((int)r.Slot, r.Name, r.Group, r.Complain, r.CreationFlags, r.ReleaseOrder), table.Rows[i]);
        }
    }
}
