extern alias client;

using System.Collections.Immutable;
using System.Text;
using Xunit;

using BotProfileStore = client::SourceSharp.Ai.Client.Profiles.BotProfileStore;
using IFwdBaseFileSystem = client::SourceSharp.Abi.IFwdBaseFileSystem;
using IModInfo = client::SourceSharp.Abi.IModInfo;
using IProfileDisk = client::SourceSharp.Ai.Client.Profiles.IProfileDisk;
using ModProfileDisk = client::SourceSharp.Ai.Client.Profiles.ModProfileDisk;

namespace SourceSharp.Ai.Tests.Setup;

/// <summary>The client's profile store (plan_hl2mp_ai §3.5): names, presets, the files under cfg/ai/bots.</summary>
public class BotProfileStoreTests
{
    internal sealed class MemoryDisk : IProfileDisk
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<string> Names()
            => [.. Files.Keys.Where(k => k.StartsWith(BotProfileStore.Directory + "/", StringComparison.Ordinal) && k.EndsWith(".json", StringComparison.Ordinal))
                             .Select(k => k[(BotProfileStore.Directory.Length + 1)..^5])];

        public byte[]? Read(string path) => Files.TryGetValue(path, out byte[]? b) ? b : null;

        public string? Write(string path, byte[] bytes)
        {
            Files[path] = bytes;
            return null;
        }
    }

    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    /// <summary>A profile over the eleven: ten levels given are the ten skills, Teamwork 0.</summary>
    private static SkillProfile Profile(string name, params int[] levels)
        => new(name, StandardSkills.Eleven, levels.Length == 10 ? [.. levels, 0] : [.. levels]);

    [Fact]
    public void ASavedProfileLoadsWithItsRevisions()
    {
        var disk = new MemoryDisk();
        var store = new BotProfileStore(disk);
        SkillProfile deadeye = Profile("Deadeye", 8, 7, 6, 6, 6, 6, 1, 1, 2, 2);
        Assert.True(store.Save(deadeye, [new ProfileRevision(1, T0, "first", deadeye.Levels)]).Accepted);
        Assert.True(store.Load("Deadeye", out BotProfileDocument? doc).Accepted);
        Assert.Equal((45, 1, "first"), (doc!.Profile.Spent, doc.Revisions.Length, doc.Revisions[0].Note));
    }

    [Fact]
    public void AProfileIsWrittenUnderCfgAiBots()
    {
        var disk = new MemoryDisk();
        new BotProfileStore(disk).Save(Profile("Deadeye", 1, 0, 0, 0, 0, 0, 0, 0, 0, 0), []);
        Assert.Equal(["cfg/ai/bots/Deadeye.json"], disk.Files.Keys);
    }

    [Fact]
    public void ANameWithASlashIsNeverAPath()
    {
        var disk = new MemoryDisk();
        var store = new BotProfileStore(disk);
        Assert.Equal("'../x' has '.': a profile's name is letters, digits, _ and -",
                     store.Save(Profile("../x", 1, 0, 0, 0, 0, 0, 0, 0, 0, 0), []).Refusal);
        Assert.Empty(disk.Files);
    }

    [Fact]
    public void LoadRefusesANameBeforeTouchingTheDisk()
        => Assert.Equal("'a/b' has '/': a profile's name is letters, digits, _ and -",
                        new BotProfileStore(new MemoryDisk()).Load("a/b", out _).Refusal);

    [Fact]
    public void APresetLoadsWithoutAFile()
    {
        Assert.True(new BotProfileStore(new MemoryDisk()).Load("expert", out BotProfileDocument? doc).Accepted);
        // Expert spends the whole pool: the Teamwork it is given is what is left, none.
        Assert.Equal((string.Join(",", SkillPresets.ExpertLevels) + ",0", 0), (string.Join(",", doc!.Profile.Levels), doc.Revisions.Length));
    }

    /// <summary>A preset is given the Teamwork it plays at (B.7): Medium's even share, 35 over ten skills, is 3.</summary>
    [Fact]
    public void APresetIsGivenTheTeamworkItPlaysAt()
    {
        Assert.True(new BotProfileStore(new MemoryDisk()).Load("normal", out BotProfileDocument? doc).Accepted);
        Assert.Equal(("teamwork", 3, 38), (doc!.Profile.Skills[10].Key, doc.Profile.Levels[10], doc.Profile.Spent));
    }

    /// <summary>A profile saved before teams has no Teamwork: it reads with its even share, and its revisions too.</summary>
    [Fact]
    public void AProfileSavedBeforeTeamsReadsWithItsEvenShareOfTeamwork()
    {
        var disk = new MemoryDisk();
        var old = new SkillProfile("Deadeye", StandardSkills.Ten, [8, 7, 6, 6, 6, 6, 1, 1, 2, 2]);
        disk.Files[BotProfileStore.PathOf("Deadeye")] = ProfileFile.Write(new BotProfileDocument(old, 60, [new ProfileRevision(1, T0, "first", old.Levels)]));
        Assert.True(new BotProfileStore(disk).Load("Deadeye", out BotProfileDocument? doc).Accepted);
        Assert.Equal((4, 49, 4), (doc!.Profile.Levels[10], doc.Profile.Spent, doc.Revisions[0].Levels[10]));
    }

    /// <summary>The control: a profile with Teamwork keeps its own, and a full one before teams is given none it cannot pay for.</summary>
    [Fact]
    public void ASavedTeamworkIsKeptAndAFullPoolGivesNone()
    {
        var disk = new MemoryDisk();
        var store = new BotProfileStore(disk);
        Assert.True(store.Save(Profile("Glued", 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 9), []).Accepted);
        var full = new SkillProfile("Full", StandardSkills.Ten, [.. SkillPresets.ExpertLevels]);
        disk.Files[BotProfileStore.PathOf("Full")] = ProfileFile.Write(new BotProfileDocument(full, 60, []));
        Assert.True(store.Load("Glued", out BotProfileDocument? glued).Accepted);
        Assert.True(store.Load("Full", out BotProfileDocument? read).Accepted);
        Assert.Equal((9, 0), (glued!.Profile.Levels[10], read!.Profile.Levels[10]));
    }

    [Fact]
    public void APresetsNameCannotBeSavedOver()
    {
        var disk = new MemoryDisk();
        Assert.Equal("'Expert' is a built-in profile; save it under another name",
                     new BotProfileStore(disk).Save(Profile("Expert", 1, 0, 0, 0, 0, 0, 0, 0, 0, 0), []).Refusal);
        Assert.Empty(disk.Files);
    }

    [Fact]
    public void AnOverspentProfileIsNeverWritten()
    {
        var disk = new MemoryDisk();
        Assert.Equal("profile 'Greedy' spends 70 of a 60-point pool",
                     new BotProfileStore(disk).Save(Profile("Greedy", 10, 10, 10, 10, 10, 10, 10, 0, 0, 0), []).Refusal);
        Assert.Empty(disk.Files);
    }

    [Fact]
    public void AMissingProfileIsRefusedNamingItsPath()
        => Assert.Equal("no profile 'Nobody': expected cfg/ai/bots/Nobody.json",
                        new BotProfileStore(new MemoryDisk()).Load("Nobody", out _).Refusal);

    [Fact]
    public void TheSavedNamesLeaveOutFilesThatAreNotProfiles()
    {
        var disk = new MemoryDisk();
        disk.Files["cfg/ai/bots/zed.json"] = [];
        disk.Files["cfg/ai/bots/Alpha.json"] = [];
        disk.Files["cfg/ai/bots/two words.json"] = [];
        disk.Files["cfg/ai/bots/easy.json"] = [];
        Assert.Equal(["Alpha", "zed"], new BotProfileStore(disk).SavedNames());
    }

    [Fact]
    public void AHandEditedFileIsJudgedOnLoad()
    {
        var disk = new MemoryDisk();
        disk.Files["cfg/ai/bots/Hacked.json"] = Encoding.UTF8.GetBytes(
            """{ "format": 1, "name": "Hacked", "pool": 60, "skills": { "accuracy": 11, "alertness": 0, "aggression": 0, "cover": 0, "tenacity": 0, "cunning": 0, "discipline": 0, "agility": 0, "nerve": 0, "headhunter": 0 }, "revisions": [] }""");
        Assert.Equal("cfg/ai/bots/Hacked.json: accuracy is 11, outside 0..10", new BotProfileStore(disk).Load("Hacked", out _).Refusal);
    }

    private sealed class Mod(string dir) : IModInfo
    {
        public string Id => "test";

        public string GameDir => dir;

        public string ManagedDir => dir;

        public IReadOnlyList<string> AssembliesFor(SourceSharp.Abi.Side side) => [];
    }

    private sealed class EngineFiles : IFwdBaseFileSystem
    {
        public List<string> Calls { get; } = new();

        public Dictionary<string, byte[]> Written { get; } = new();

        private string? _open;

        public byte[]? ReadAll(string fileName, string pathId) => null;

        public uint FileSize(string fileName, string pathId) => 0;

        public bool FileExists(string fileName, string? pathId) => false;

        public nint Open(string fileName, string options, string pathId)
        {
            Calls.Add($"open {fileName} {options} {pathId}");
            _open = fileName;
            return 7;
        }

        public int Write(ReadOnlySpan<byte> bytes, nint handle)
        {
            Written[_open!] = bytes.ToArray();
            return bytes.Length;
        }

        public void Close(nint handle) => Calls.Add($"close {handle}");
    }

    [Fact]
    public void TheModDiskWritesThroughTheEngineOnTheModPathAfterMakingTheDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ai-ui-disk-" + Guid.NewGuid().ToString("N"));
        try
        {
            var engine = new EngineFiles();
            var disk = new ModProfileDisk(engine, new Mod(dir));
            Assert.Null(disk.Write("cfg/ai/bots/Deadeye.json", [1, 2, 3]));
            Assert.Equal((true, "open cfg/ai/bots/Deadeye.json wb MOD; close 7", "1,2,3"),
                         (Directory.Exists(Path.Combine(dir, "cfg", "ai", "bots")), string.Join("; ", engine.Calls),
                          string.Join(",", engine.Written["cfg/ai/bots/Deadeye.json"])));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheModDiskListsTheProfileFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ai-ui-disk-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "cfg", "ai", "bots"));
            File.WriteAllText(Path.Combine(dir, "cfg", "ai", "bots", "Deadeye.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "cfg", "ai", "bots", "notes.txt"), "");
            Assert.Equal(["Deadeye"], new ModProfileDisk(new EngineFiles(), new Mod(dir)).Names());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheModDiskHasNoDirectoryUntilAProfileIsSaved()
        => Assert.Empty(new ModProfileDisk(new EngineFiles(), new Mod(Path.Combine(Path.GetTempPath(), "ai-ui-none-" + Guid.NewGuid().ToString("N")))).Names());
}
