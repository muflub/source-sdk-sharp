using System.Buffers.Binary;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Server.Voice;

using Xunit;

namespace TF2.Tests.Voice;

/// <summary>
/// <see cref="TFSceneImage"/>: the entry lookup is the engine's case-, slash-
/// and extension-insensitive normalize binary-searched by CRC, a scene's
/// sounds come off its summary's string ids, and a missing or malformed image
/// reads as empty rather than throwing.
/// </summary>
public sealed class TFSceneImageTests
{
    [Fact]
    public void TheEntryKeyIsTheCanonicalCrcOverTheNormalizedName()
    {
        // the CRC-32 check value, the catalog's own
        Assert.Equal(0xCBF43926u, TFSceneImage.Crc32.Of("123456789"));

        // one scene, any spelling: lower case, backslashes, the extension forced
        uint keyed = TFSceneImage.Crc32.NormalizedSceneName("scenes/Player/Medic/low/611.vcd");
        Assert.Equal(keyed, TFSceneImage.Crc32.NormalizedSceneName("SCENES\\player\\MEDIC\\low\\611"));
        Assert.Equal(keyed, TFSceneImage.Crc32.NormalizedSceneName("scenes/player/medic/low/611.vcd"));
    }

    [Fact]
    public void ASceneIsFoundThroughAnySpellingAndCarriesItsSoundsAndDuration()
    {
        byte[] image = Image(
            ("scenes/Player/Medic/low/611.vcd", 1730, ["vo/medic/medic.ogg"]),
            ("scenes/Player/Scout/low/601.vcd", 640, ["vo/scout/thanks.ogg"]));
        var scenes = new TFSceneImage(Files(("scenes/scenes.image", image)));

        Assert.Equal(2, scenes.SceneCount);
        Assert.Equal(["vo/medic/medic.ogg"], scenes.SceneSounds("scenes\\Player\\MEDIC\\Low\\611"));
        Assert.Equal(["vo/scout/thanks.ogg"], scenes.SceneSounds("scenes/player/scout/low/601.vcd"));
        Assert.Equal(1.73f, scenes.SceneDuration("SCENES/Player/Medic/Low/611.vcd"), 3);
        Assert.Equal(0.64f, scenes.SceneDuration("scenes\\player\\scout\\low\\601.vcd"), 3);
    }

    [Fact]
    public void ASummaryListsItsSoundsInStoredOrder()
    {
        byte[] image = Image(("scenes/TwoSpeaker.vcd", 4000, ["vo/first.wav", "vo/second.wav", "vo/first.wav"]));
        var scenes = new TFSceneImage(Files(("scenes/scenes.image", image)));

        Assert.Equal(["vo/first.wav", "vo/second.wav", "vo/first.wav"], scenes.SceneSounds("scenes/TwoSpeaker"));
    }

    [Fact]
    public void AnAbsentSceneReadsAsEmpty()
    {
        byte[] image = Image(("scenes/Player/Medic/low/611.vcd", 999, ["vo/medic/medic.ogg"]));
        var scenes = new TFSceneImage(Files(("scenes/scenes.image", image)));

        Assert.Empty(scenes.SceneSounds("scenes/Player/Medic/low/999.vcd"));
        Assert.Equal(0f, scenes.SceneDuration("scenes/Player/Medic/low/999.vcd"));
    }

    [Fact]
    public void AMissingImageAndAMalformedOneReadAsEmptyRatherThanThrowing()
    {
        var absent = new TFSceneImage(Files());
        Assert.Equal(0, absent.SceneCount);
        Assert.Empty(absent.SceneSounds("scenes/any.vcd"));
        Assert.Equal(0f, absent.SceneDuration("scenes/any.vcd"));

        // a truncated body, a wrong id, a wrong version and an entry table
        // past the body each refuse the load
        Assert.Equal(0, NewImage(new byte[] { 0x46 }).SceneCount);
        Assert.Equal(0, NewImage(HeaderedImage(999, 2)).SceneCount);
        Assert.Equal(0, NewImage(HeaderedImage(TFSceneImage.ImageId, 7)).SceneCount);
        Assert.Equal(0, NewImage(HeaderedImage(TFSceneImage.ImageId, 2, entries: 1 << 20)).SceneCount);
    }

    [Fact]
    public void TheFirstEntryIsReachable()
    {
        // the search's lower bound is entry 1: a lone scene is probed at
        // middle-1 == 0, the quirk the port keeps
        byte[] image = Image(("scenes/Player/Medic/low/611.vcd", 250, ["vo/medic/alone.wav"]));
        var scenes = new TFSceneImage(Files(("scenes/scenes.image", image)));

        Assert.Equal(["vo/medic/alone.wav"], scenes.SceneSounds("scenes/Player/Medic/low/611.vcd"));
    }

    [NotOnCiFact("needs a Team Fortress 2 install for its content, which a runner has not")]
    public void TheShippedImageCarriesTheMedicMedicScene()
    {
        string content = RepoLayout.Repo("bin", "debug", "content");
        Assert.True(Directory.Exists(content),
            $"no {content}: make setup links bin/<config>/content to a Team Fortress 2 install (TF2)");

        var scenes = new TFSceneImage(new TfvPks(Path.Combine(content, "tf")));

        Assert.True(scenes.SceneCount > 1000,
            $"the shipped image parsed {scenes.SceneCount} scenes");

        // the response PlayerMedicMedic's scene, per the shipped scripts/talker/medic.txt:
        // one speak event, the Medic.Medic01 soundscript, two and a half seconds
        float duration = scenes.SceneDuration("scenes/Player/Medic/low/611.vcd");
        var sounds = scenes.SceneSounds("scenes/Player/Medic/low/611.vcd");
        Assert.True(duration is > 2f and < 3f,
            $"the medic scene read {duration}s with sounds [{string.Join(", ", sounds)}]");
        Assert.True(sounds.Count > 0 && sounds[0].StartsWith("Medic.", StringComparison.Ordinal),
            $"the medic scene's sounds read [{string.Join(", ", sounds)}]");
    }

    // -----------------------------------------------------------------

    private static TFSceneImage NewImage(byte[] body)
        => new(Files(("scenes/scenes.image", body)));

    /// <summary>
    /// An image holding the given scenes, laid out as the format has it:
    /// header, string table, pool, entries sorted by key, summaries.
    /// </summary>
    internal static byte[] Image(params (string Scene, int Msecs, string[] Sounds)[] scenes)
        => Image(TFSceneImage.ImageId, TFSceneImage.ImageVersion, scenes);

    private static byte[] HeaderedImage(int id, int version, (string Scene, int Msecs, string[] Sounds)[]? scenes = null, int entries = -1)
        => Image(id, version, scenes ?? [("scenes/Placeholder.vcd", 100, ["vo/placeholder.wav"])], entries);

    private static byte[] Image(int id, int version, (string Scene, int Msecs, string[] Sounds)[] scenes, int entries = -1)
    {
        var pool = new List<string>();
        int IdOf(string s)
        {
            int at = pool.IndexOf(s);
            if (at < 0)
            {
                pool.Add(s);
                at = pool.Count - 1;
            }

            return at;
        }

        var rows = scenes
            .Select(s => (Crc: TFSceneImage.Crc32.NormalizedSceneName(s.Scene), s.Msecs, Sounds: s.Sounds.Select(IdOf).ToArray()))
            .OrderBy(r => r.Crc)
            .ToList();

        int count = entries < 0 ? rows.Count : entries;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // layout: header, string table, entries at the declared offset, pool, summaries
        int entryOffset = 20 + 4 * pool.Count;
        int poolAt = entryOffset + 16 * count;

        writer.Write(id);
        writer.Write(version);
        writer.Write(count);
        writer.Write(pool.Count);
        writer.Write(entryOffset);

        for (int i = 0; i < pool.Count; i++)
        {
            writer.Write(0u);       // patched below, once the pool's place is known
        }

        // the offsets are absolute, so the pool's layout is computed before it is written
        var bodies = new byte[pool.Count][];
        int at = poolAt;
        for (int i = 0; i < pool.Count; i++)
        {
            bodies[i] = Encoding.ASCII.GetBytes(pool[i] + "\0");
            at += bodies[i].Length;
        }

        var summaries = new int[rows.Count];
        int summaryAt = at;
        for (int i = 0; i < rows.Count; i++)
        {
            summaries[i] = summaryAt;
            summaryAt += 8 + 2 * rows[i].Sounds.Length;
        }

        for (int i = 0; i < count && i < rows.Count; i++)
        {
            writer.Write(rows[i].Crc);
            writer.Write(0);        // data offset; the summary reader never takes it
            writer.Write(4);
            writer.Write(summaries[i]);
        }

        for (int i = 0; i < pool.Count; i++)
        {
            writer.Write(bodies[i]);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            writer.Write(rows[i].Msecs);
            writer.Write(rows[i].Sounds.Length);
            foreach (int sound in rows[i].Sounds)
                writer.Write((short)sound);
        }

        // patch the string offsets now that the stream is final
        byte[] image = stream.ToArray();
        at = poolAt;
        for (int i = 0; i < pool.Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(20 + 4 * i, 4), at);
            at += bodies[i].Length;
        }

        return image;
    }

    /// <summary>The seam over in-memory bytes.</summary>
    internal static IEngineFiles Files(params (string Path, byte[] Body)[] files)
    {
        var map = files.ToDictionary(f => f.Path, f => f.Body, StringComparer.OrdinalIgnoreCase);
        return new MemoryFiles(map);
    }

    internal sealed class MemoryFiles(Dictionary<string, byte[]> map) : IEngineFiles
    {
        public int GameFileSize(string path) => map.TryGetValue(path, out byte[]? body) ? body!.Length : -1;

        public string? ReadGameFile(string path)
            => map.TryGetValue(path, out byte[]? body) ? Encoding.UTF8.GetString(body!) : null;

        public byte[]? ReadGameFileBytes(string path) => map.GetValueOrDefault(path);

        public byte[]? ReadFileOnPath(string path, string pathId) => ReadGameFileBytes(path);
    }

    /// <summary>
    /// The game-file seam over the tree's mounted Team Fortress 2 content,
    /// extracting VPK entries on demand (the reader the response rules' facts
    /// carry): every <c>*_dir.vpk</c> indexed once, bodies read from the
    /// numbered archives or a directory entry's own preload bytes.
    /// </summary>
    private sealed class TfvPks : IEngineFiles
    {
        private sealed class Entry(string root, ushort archive, uint offset, uint length, byte[]? preload)
        {
            public string Root = root;
            public ushort Archive = archive;
            public uint Offset = offset;
            public uint Length = length;
            public byte[]? Preload = preload;
        }

        private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _bodies = new(StringComparer.OrdinalIgnoreCase);

        public TfvPks(string tfDir)
        {
            foreach (string dirVpk in Directory.GetFiles(tfDir, "*_dir.vpk"))
                Index(dirVpk);
        }

        private void Index(string dirVpk)
        {
            using BinaryReader r = new(File.OpenRead(dirVpk));
            r.ReadUInt32();                         // signature
            uint version = r.ReadUInt32();
            r.ReadUInt32();                         // tree size
            if (version == 2)
                r.ReadBytes(16);

            string Text()
            {
                var bytes = new List<byte>();
                byte c;
                while ((c = r.ReadByte()) != 0)
                    bytes.Add(c);
                return Encoding.Latin1.GetString([.. bytes]);
            }

            string root = dirVpk.EndsWith("_dir.vpk", StringComparison.Ordinal) ? dirVpk[..^8] : dirVpk;

            for (string ext; (ext = Text()).Length > 0;)
            for (string dir; (dir = Text()).Length > 0;)
            for (string name; (name = Text()).Length > 0;)
            {
                r.ReadUInt32();                     // crc
                ushort preload = r.ReadUInt16();
                ushort archive = r.ReadUInt16();
                uint offset = r.ReadUInt32();
                uint length = r.ReadUInt32();
                r.ReadUInt16();                     // terminator
                byte[]? inlineBytes = archive == 0xFFFF ? r.ReadBytes(preload) : null;
                if (archive != 0xFFFF && preload > 0)
                    r.ReadBytes(preload);

                _entries[$"{dir}/{name}.{ext}"] = new Entry(root, archive, offset, length, inlineBytes);
            }
        }

        public byte[]? ReadGameFileBytes(string path)
        {
            if (_bodies.TryGetValue(path, out byte[]? cached))
                return cached;

            if (!_entries.TryGetValue(path, out Entry? entry))
                return null;

            byte[] body;
            if (entry.Archive == 0xFFFF)
            {
                body = entry.Preload!;
            }
            else
            {
                using BinaryReader archive = new(File.OpenRead($"{entry.Root}_{entry.Archive:D3}.vpk"));
                archive.BaseStream.Seek(entry.Offset, SeekOrigin.Begin);
                body = archive.ReadBytes((int)entry.Length);
            }

            _bodies[path] = body;
            return body;
        }

        public int GameFileSize(string path) => ReadGameFileBytes(path)?.Length ?? -1;

        public string? ReadGameFile(string path)
            => ReadGameFileBytes(path) is byte[] b ? Encoding.Latin1.GetString(b) : null;

        public byte[]? ReadFileOnPath(string path, string pathId)
            => string.Equals(pathId, "GAME", StringComparison.Ordinal) ? ReadGameFileBytes(path) : null;
    }
}
