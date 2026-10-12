using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_novalve.md P2.1: the particle config reader and the manifest
/// rules that replaced server.so's CParticleSystemMgr. The reader is held to
/// stock on the real content by tools/novalve-snapshot's differential (245
/// definitions agree on dm_lockdown, 159 with the fixture, 3 of them
/// always-precache); these facts pin the format and the rules on synthetic
/// files that need no game install.
/// </summary>
public class ParticleManifestTests
{
    // ---- a binary-2 DMX writer, just enough for .pcf files ----------------

    private sealed class Dmx
    {
        private readonly List<string> _table = new();
        private readonly List<(string Type, string Name)> _elements = new();
        private readonly List<List<(string Name, byte Type, Action<BinaryWriter> Value)>> _attrs = new();

        public int Element(string type, string name)
        {
            Index(type);
            _elements.Add((type, name));
            _attrs.Add(new());
            return _elements.Count - 1;
        }

        public void Attr(int element, string name, byte type, Action<BinaryWriter> value)
        {
            Index(name);
            _attrs[element].Add((name, type, value));
        }

        private short Index(string s)
        {
            int i = _table.IndexOf(s);
            if (i < 0) { _table.Add(s); i = _table.Count - 1; }
            return (short)i;
        }

        public byte[] Bytes(string header = "<!-- dmx encoding binary 2 format pcf 1 -->\n")
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            CStr(w, header);
            w.Write((short)_table.Count);
            foreach (string s in _table) CStr(w, s);
            w.Write(_elements.Count);
            foreach (var (type, name) in _elements)
            {
                w.Write(Index(type));
                CStr(w, name);
                w.Write(new byte[16]);
            }
            foreach (var list in _attrs)
            {
                w.Write(list.Count);
                foreach (var (name, type, value) in list)
                {
                    w.Write(Index(name));
                    w.Write(type);
                    value(w);
                }
            }
            return ms.ToArray();
        }

        public static void CStr(BinaryWriter w, string s)
        {
            w.Write(Encoding.UTF8.GetBytes(s));
            w.Write((byte)0);
        }
    }

    private static byte[] Pcf(params string[] names) => PcfWith(names, extraRootAttribute: false);

    private static byte[] PcfWith(string[] names, bool extraRootAttribute)
    {
        var d = new Dmx();
        int root = d.Element("DmElement", "untitled");
        var defs = new List<int>();
        foreach (string n in names)
            defs.Add(d.Element("DmeParticleSystemDefinition", n));
        if (extraRootAttribute)
        {
            // An attribute of every variable-length shape BEFORE the array, so
            // the skip is what finds it.
            d.Attr(root, "note", 5, w => Dmx.CStr(w, "some text"));
            d.Attr(root, "blob", 6, w => { w.Write(3); w.Write(new byte[] { 1, 2, 3 }); });
            d.Attr(root, "vecs", 24, w => { w.Write(2); w.Write(new byte[24]); });
        }
        d.Attr(root, "particleSystemDefinitions", 15, w =>
        {
            w.Write(defs.Count);
            foreach (int i in defs) w.Write(i);
        });
        return d.Bytes();
    }

    // ---- PcfNames ----------------------------------------------------------

    [Fact]
    public void TheRootsDefinitionsAreReadInArrayOrder()
    {
        Assert.Equal(new[] { "fire_small", "smoke", "sparks" },
                     PcfNames.Read(Pcf("fire_small", "smoke", "sparks")));
    }

    [Fact]
    public void AttributesBeforeTheArrayAreSkippedByTheirShape()
    {
        Assert.Equal(new[] { "a", "b" },
                     PcfNames.Read(PcfWith(new[] { "a", "b" }, extraRootAttribute: true)));
    }

    [Fact]
    public void AnElementTheRootDoesNotListIsNotADefinition()
    {
        var d = new Dmx();
        int root = d.Element("DmElement", "untitled");
        int listed = d.Element("DmeParticleSystemDefinition", "listed");
        d.Element("DmeParticleSystemDefinition", "orphan");
        d.Attr(root, "particleSystemDefinitions", 15, w => { w.Write(1); w.Write(listed); });
        Assert.Equal(new[] { "listed" }, PcfNames.Read(d.Bytes()));
    }

    [Fact]
    public void AnotherEncodingIsRefusedNotGuessed()
    {
        var d = new Dmx();
        d.Element("DmElement", "untitled");
        var e = Assert.Throws<FormatException>(
            () => PcfNames.Read(d.Bytes("<!-- dmx encoding binary 5 format pcf 2 -->\n")));
        Assert.Contains("encoding 5", e.Message);
    }

    [Fact]
    public void ATruncatedFileThrowsRatherThanAnsweringPartOfIt()
    {
        // The file ends with the three definitions' own (empty) attribute
        // blocks, 4 bytes each, which the reader never needs; cutting 2 bytes
        // further lands inside the root's definitions array, which it does.
        byte[] whole = Pcf("a", "b", "c");
        Assert.Equal(new[] { "a", "b", "c" }, PcfNames.Read(whole.AsSpan(0, whole.Length - 12)));
        Assert.Throws<FormatException>(() => PcfNames.Read(whole.AsSpan(0, whole.Length - 14)));
    }

    [Fact]
    public void ATextFileIsRefused()
    {
        Assert.Throws<FormatException>(
            () => PcfNames.Read(Encoding.UTF8.GetBytes("<!-- dmx encoding keyvalues2 1 format pcf 1 -->\n\0")));
    }

    [Fact]
    public void ARootWithoutTheArrayIsRefused()
    {
        var d = new Dmx();
        int root = d.Element("DmElement", "untitled");
        d.Attr(root, "note", 5, w => Dmx.CStr(w, "x"));
        Assert.Throws<FormatException>(() => PcfNames.Read(d.Bytes()));
    }

    // ---- ParticleManifest --------------------------------------------------

    /// <summary>Files by (path, path id).</summary>
    private sealed class Files : ILevelFiles
    {
        public readonly Dictionary<(string, string), byte[]> Map = new();
        public readonly List<string> Reads = new();

        public void Put(string path, string pathId, string text) => Map[(path, pathId)] = Encoding.UTF8.GetBytes(text);
        public void Put(string path, string pathId, byte[] bytes) => Map[(path, pathId)] = bytes;

        public byte[]? ReadFileOnPath(string path, string pathId)
        {
            Reads.Add(pathId + ":" + path);
            return Map.TryGetValue((path, pathId), out byte[]? b) ? b : null;
        }

    }

    private static Files Game()
    {
        var f = new Files();
        f.Put("particles/a.pcf", "GAME", Pcf("a1", "a2"));
        f.Put("particles/b.pcf", "GAME", Pcf("b1"));
        f.Put("particles/c.pcf", "GAME", Pcf("c1", "a1"));
        return f;
    }

    [Fact]
    public void TheGameManifestPrecachesOnlyItsBangFiles()
    {
        Files f = Game();
        f.Put(ParticleManifest.GameManifest, "GAME",
            "particles_manifest\n{\n\"file\" \"particles/a.pcf\"\n\"file\" \"!particles/b.pcf\"\n}\n");
        var m = new ParticleManifest(new RecordingLog(), f);
        m.ParseGame();
        Assert.Equal(new[] { "a1", "a2", "b1" }, m.Definitions);
        Assert.Equal(new[] { "b1" }, m.AlwaysPrecache());
    }

    [Fact]
    public void ALaterFileRedefiningASystemTakesItsFlag()
    {
        Files f = Game();
        f.Put(ParticleManifest.GameManifest, "GAME",
            "particles_manifest\n{\n\"file\" \"!particles/a.pcf\"\n\"file\" \"particles/c.pcf\"\n}\n");
        var m = new ParticleManifest(new RecordingLog(), f);
        m.ParseGame();
        Assert.Equal(new[] { "a2" }, m.AlwaysPrecache());
        Assert.Equal(new[] { "a1", "a2", "c1" }, m.Definitions);
    }

    [Fact]
    public void EveryFileAMapManifestNamesIsPrecached()
    {
        Files f = Game();
        f.Put("maps/dm_x_particles.txt", "GAME", "p\n{\n\"file\" \"particles/a.pcf\"\n}\n");
        var m = new ParticleManifest(new RecordingLog(), f);
        m.ParseMap("dm_x");
        Assert.Equal(new[] { "a1", "a2" }, m.AlwaysPrecache());
    }

    [Fact]
    public void TheMapManifestIsLookedForInTheBspFirst()
    {
        Files f = Game();
        f.Put("particles.txt", "BSP", "p\n{\n\"file\" \"particles/b.pcf\"\n}\n");
        f.Put("maps/dm_x_particles.txt", "GAME", "p\n{\n\"file\" \"particles/a.pcf\"\n}\n");
        var m = new ParticleManifest(new RecordingLog(), f);
        m.ParseMap("dm_x");
        Assert.Equal(new[] { "b1" }, m.AlwaysPrecache());
        Assert.Equal("BSP:particles.txt", f.Reads[0]);
    }

    [Fact]
    public void AMapManifestFileOutsideParticlesIsRefused()
    {
        Files f = Game();
        f.Put("materials/x.pcf", "GAME", Pcf("x1"));
        f.Put("maps/dm_x_particles.txt", "GAME",
            "p\n{\n\"file\" \"materials/x.pcf\"\n\"file\" \"!particles/b.pcf\"\n}\n");
        var m = new ParticleManifest(new RecordingLog(), f);
        m.ParseMap("dm_x");
        Assert.Equal(new[] { "b1" }, m.AlwaysPrecache());
    }

    [Fact]
    public void AMapWithNoManifestReadsNoParticleFile()
    {
        Files f = Game();
        var m = new ParticleManifest(new RecordingLog(), f);
        m.ParseMap("dm_x");
        Assert.Empty(m.Definitions);
        Assert.DoesNotContain(f.Reads, r => r.EndsWith(".pcf", StringComparison.Ordinal));
        Assert.Equal(3, f.Reads.Count);
    }

    [Fact]
    public void AnUnreadableConfigFileContributesNothingAndTheRestStillLoad()
    {
        Files f = Game();
        f.Put("particles/bad.pcf", "GAME", Encoding.UTF8.GetBytes("not a dmx\0"));
        f.Put(ParticleManifest.GameManifest, "GAME",
            "particles_manifest\n{\n\"file\" \"!particles/bad.pcf\"\n\"file\" \"!particles/b.pcf\"\n}\n");
        var m = new ParticleManifest(new RecordingLog(), f);
        m.ParseGame();
        Assert.Equal(new[] { "b1" }, m.AlwaysPrecache());
    }

    [Fact]
    public void ClearForgetsEveryDefinition()
    {
        Files f = Game();
        f.Put(ParticleManifest.GameManifest, "GAME", "particles_manifest\n{\n\"file\" \"!particles/b.pcf\"\n}\n");
        var m = new ParticleManifest(new RecordingLog(), f);
        m.ParseGame();
        Assert.NotEmpty(m.AlwaysPrecache());
        m.Clear();
        Assert.Empty(m.Definitions);
        Assert.Empty(m.AlwaysPrecache());
    }
}
