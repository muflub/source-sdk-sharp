using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_map_load P1: a listen server's spawn reloads only the materials the two
/// map paks can have changed, in place of the engine's ReloadMaterials(NULL)
/// (0.56-0.94 s a load at 96d57d1).
/// </summary>
public class MapMaterialReloadTests
{
    /// <summary>A VBSP whose PAKFILE lump (40) is a zip of the given entries.</summary>
    private static byte[] Bsp(params string[] entries)
    {
        var pak = new MemoryStream();
        using (var zip = new ZipArchive(pak, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var e in entries)
                using (var w = new StreamWriter(zip.CreateEntry(e).Open()))
                    w.Write("\"LightmappedGeneric\" {}");
        byte[] pakBytes = pak.ToArray();

        int header = 8 + 64 * 16 + 4;
        var bsp = new byte[header + pakBytes.Length];
        Encoding.ASCII.GetBytes("VBSP").CopyTo(bsp, 0);
        BitConverter.GetBytes(20).CopyTo(bsp, 4);
        BitConverter.GetBytes(header).CopyTo(bsp, 8 + 40 * 16);
        BitConverter.GetBytes(pakBytes.Length).CopyTo(bsp, 8 + 40 * 16 + 4);
        pakBytes.CopyTo(bsp, header);
        return bsp;
    }

    private static readonly MapMaterialReload Policy = new();

    [Fact]
    public void A_pak_lists_its_materials_but_not_its_own_map_directory()
    {
        var index = Policy.Index("dm_a", Bsp("materials/tile/floor.vmt", "materials/maps/dm_a/tile/floor_1_2_3.vmt", "maps/dm_a.nav"))!;
        Assert.Equal(["tile/floor"], index.Materials);
        Assert.False(index.OverridesTextures);
    }

    [Fact]
    public void Remounting_the_same_pak_reloads_nothing()
    {
        byte[] bsp = Bsp("materials/tile/floor.vmt");
        var plan = Policy.Decide(Policy.Index("dm_a", bsp), Policy.Index("dm_a", bsp), previousExpected: true);
        Assert.False(plan.Full);
        Assert.Empty(plan.Materials);
    }

    [Fact]
    public void A_new_map_reloads_both_paks_materials()
    {
        var plan = Policy.Decide(Policy.Index("dm_b", Bsp("materials/metal/grate.vmt", "materials/maps/dm_b/x.vmt")),
                                 Policy.Index("dm_a", Bsp("materials/tile/floor.vmt", "materials/maps/dm_a/y.vmt")),
                                 previousExpected: true);
        Assert.False(plan.Full);
        Assert.Equal(["metal/grate", "tile/floor"], plan.Materials);
    }

    [Fact]
    public void The_same_map_name_with_a_different_pak_reloads_its_names()
    {
        var plan = Policy.Decide(Policy.Index("dm_a", Bsp("materials/tile/floor.vmt", "materials/tile/wall.vmt")),
                                 Policy.Index("dm_a", Bsp("materials/tile/floor.vmt")), previousExpected: true);
        Assert.False(plan.Full);
        Assert.Equal(["tile/floor", "tile/wall"], plan.Materials);
    }

    [Fact]
    public void A_texture_override_outside_the_maps_directory_is_the_full_reload()
    {
        var plan = Policy.Decide(Policy.Index("dm_b", Bsp("materials/tile/floor.vtf")), null, previousExpected: false);
        Assert.True(plan.Full);
    }

    [Fact]
    public void A_map_that_cannot_be_read_is_the_full_reload()
    {
        Assert.True(Policy.Decide(null, null, previousExpected: false).Full);
        Assert.True(Policy.Decide(Policy.Index("dm_b", Bsp()), null, previousExpected: true).Full);
        Assert.Null(Policy.Index("x", Encoding.ASCII.GetBytes("not a bsp at all, just bytes")));
    }

    [Fact]
    public void The_first_spawn_with_an_empty_pak_reloads_nothing()
    {
        var plan = Policy.Decide(Policy.Index("dm_a", Bsp()), null, previousExpected: false);
        Assert.False(plan.Full);
        Assert.Empty(plan.Materials);
    }

    [Fact]
    public void The_client_service_reads_the_files_and_falls_back_when_one_is_missing()
    {
        string dir = Directory.CreateTempSubdirectory("mmr-").FullName;
        try
        {
            string a = Path.Combine(dir, "dm_a.bsp"), b = Path.Combine(dir, "dm_b.bsp");
            File.WriteAllBytes(a, Bsp("materials/tile/floor.vmt"));
            File.WriteAllBytes(b, Bsp("materials/metal/grate.vmt"));
            var service = new ClientMapMaterials(new QuietLog());
            Assert.Equal("metal/grate\ntile/floor", service.ReloadSet(b, a));
            Assert.Equal("", service.ReloadSet(a, a));
            Assert.Null(service.ReloadSet(b, Path.Combine(dir, "deleted.bsp")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private sealed class QuietLog : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    private sealed class Host(string? answer) : IClientRuntimeHost
    {
        public (string Next, string Previous)? Asked;
        public void Frame(float frameTime) { }
        public void OnStage(int stage) { }
        public void LevelInit(string mapName) { }
        public void LevelShutdown() { }
        public void InvalidateMdlCache() { }
        public bool ParticlesNeedFullReload(int previousMapParticleFiles) => true;
        public string? MapMaterialReloadSet(string nextBsp, string previousBsp) { Asked = (nextBsp, previousBsp); return answer; }
        public void MapMaterialSearchPath(string? path, string? pathId, bool addToHead) { }
        public void MapMaterialMapAccess(bool open) { }
        public string? MapMaterialReloadPlan(string? subString) => null;
        public int WorldDataChanged(ReadOnlySpan<byte> blob, int updateType, Span<float> plan) => 0;
        public void LevelInitPreEntity() { }
        public void PostInit(int adapterMaxDxLevel) { }
    }

    private static int Call(Host host, string next, string? previous, int size, out string written)
        => Call(new Crossings(), host, next, previous, size, out written);

    private static unsafe int Call(Crossings crossings, Host host, string next, string? previous, int size, out string written)
    {
        var log = new RecordingLog();
        var models = new ModelDescriptors(_ => -1, _ => string.Empty, log);
        var binding = new ClientRuntimeBinding(crossings, log, host, new StandaloneMirror(), models,
                                               new BreakModels(_ => -1, _ => -1, _ => null, models, log));
        byte[] n = Encoding.UTF8.GetBytes(next + "\0");
        byte[] p = previous is null ? [] : Encoding.UTF8.GetBytes(previous + "\0");
        var outBuf = new byte[size];
        int r;
        fixed (byte* pn = n) fixed (byte* pp = p) fixed (byte* po = outBuf)
            r = binding.MapMaterialReload(pn, previous is null ? null : pp, po, size);
        written = r > 0 ? Encoding.UTF8.GetString(outBuf, 0, r) : string.Empty;
        return r;
    }

    [Fact]
    public void The_binding_writes_the_names_and_counts_the_crossing()
    {
        var host = new Host("tile/floor\nmetal/grate");
        var crossings = new Crossings();
        long before = crossings.Read(CrossingOp.ClientMapMaterialReload);
        Assert.Equal(22, Call(crossings, host, "/g/maps/b.bsp", "/g/maps/a.bsp", 64, out string names));
        Assert.Equal("tile/floor\nmetal/grate", names);
        Assert.Equal(("/g/maps/b.bsp", "/g/maps/a.bsp"), host.Asked);
        Assert.Equal(before + 1, crossings.Read(CrossingOp.ClientMapMaterialReload));
    }

    [Fact]
    public void The_binding_answers_the_full_reload_for_no_answer_and_for_one_that_does_not_fit()
    {
        Assert.Equal(-1, Call(new Host(null), "/g/maps/b.bsp", null, 64, out _));
        Assert.Equal(-1, Call(new Host("tile/floor\nmetal/grate"), "/g/maps/b.bsp", null, 8, out _));
        Assert.Equal(0, Call(new Host(""), "/g/maps/b.bsp", null, 8, out _));
    }

}
