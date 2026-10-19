using System.Text;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed SoundEmitterSystem against stock's CSoundEmitterSystem server
/// half (game/shared/SoundEmitterSystem.cpp): its Init and Shutdown, the
/// level's overrides and preload walk, the -makereslists precache log, the
/// script and direct precaches and their prefetch twins, and the shell's
/// script-or-path rule (plan_server_native_v2 T4a, the precache path). The
/// engine is a recorder; each fact names the stock rule it holds.
/// </summary>
public sealed class SoundEmitterSystemTests
{
    //-------------------------------------------------------------------------
    // The fake engine: a script table, the engine's precache table, a
    // filesystem and the precache window, each call written to Calls.
    //-------------------------------------------------------------------------

    private sealed class Script
    {
        public int Index;
        public string[] Waves = [];
        public bool Preload;
    }

    private sealed class FakeEngine : ISoundPrecacheEngine
    {
        public readonly Dictionary<string, Script> Scripts = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Precached = new(StringComparer.Ordinal);
        public readonly HashSet<string> Files = new(StringComparer.Ordinal);
        public readonly List<string> Calls = [];
        public readonly Dictionary<nint, StringBuilder> Written = [];
        public bool ModInitAnswer = true;
        public bool Allowed = true;
        public bool OpenFails;
        private nint _nextHandle = 100;

        public Script Add(string name, bool preload, params string[] waves)
        {
            var s = new Script { Index = Scripts.Count, Waves = waves, Preload = preload };
            Scripts[name] = s;
            return s;
        }

        private Script? ByIndex(int index) => Scripts.Values.FirstOrDefault(s => s.Index == index);

        public bool ModInit() { Calls.Add("ModInit"); return ModInitAnswer; }
        public void ModShutdown() => Calls.Add("ModShutdown");
        public void AddSoundOverrides(string scriptFile) => Calls.Add("AddSoundOverrides " + scriptFile);
        public void ClearSoundOverrides() => Calls.Add("ClearSoundOverrides");
        public void Flush() => Calls.Add("Flush");
        public int First() => Scripts.Count == 0 ? -1 : 0;
        public int Next(int index) => index + 1 < Scripts.Count ? index + 1 : -1;
        public int InvalidIndex() => -1;
        public int Lookups;

        public int SoundIndex(string name)
        {
            Lookups++;
            return Scripts.TryGetValue(name, out Script? s) ? s.Index : -1;
        }
        public bool IsValidIndex(int index) => ByIndex(index) is not null;
        public string? SoundName(int index) => Scripts.FirstOrDefault(p => p.Value.Index == index).Key;
        public bool ShouldPreload(int index) => ByIndex(index)?.Preload == true;
        public IReadOnlyList<string>? Waves(int index) => ByIndex(index)?.Waves;

        public bool PrecacheSound(string sample, bool preload)
        {
            Calls.Add($"PrecacheSound {sample} {preload}");
            Precached.Add(sample);
            return true;
        }

        public bool IsSoundPrecached(string sample) => Precached.Contains(sample);
        public void PrefetchSound(string sample) => Calls.Add("PrefetchSound " + sample);
        public bool PrecacheAllowed() => Allowed;

        public bool FileExists(string path, string pathId)
        {
            Calls.Add($"FileExists {path} {pathId}");
            return Files.Contains(path);
        }

        public void CreateDirHierarchy(string path, string pathId) => Calls.Add($"CreateDirHierarchy {path} {pathId}");

        public nint Open(string path, string options, string pathId)
        {
            Calls.Add($"Open {path} {options} {pathId}");
            if (OpenFails)
                return 0;
            nint handle = _nextHandle++;
            Written[handle] = new StringBuilder();
            return handle;
        }

        public int Write(ReadOnlySpan<byte> bytes, nint handle)
        {
            Calls.Add("Write");
            Written[handle].Append(Encoding.UTF8.GetString(bytes));
            return bytes.Length;
        }

        public void Close(nint handle) => Calls.Add($"Close {handle}");

        public IEnumerable<string> Precaches => Calls.Where(c => c.StartsWith("PrecacheSound ", StringComparison.Ordinal));
    }

    private readonly FakeEngine _engine = new();
    private readonly List<string> _warnings = [];
    private readonly List<string> _devMsgs = [];
    private string _map = "dm_lockdown";

    private SoundEmitterSystem System(bool makeResLists = false)
        => new(_engine, makeResLists, () => _map, _warnings.Add, _devMsgs.Add);

    //-------------------------------------------------------------------------
    // Init / Shutdown
    //-------------------------------------------------------------------------

    [Fact]
    public void InitIsTheTablesModInit()
    {
        _engine.ModInitAnswer = false;

        Assert.False(System().Init());
        Assert.Equal(["ModInit"], _engine.Calls);
    }

    [Fact]
    public void InitAnswersTrueWhenModInitDoes()
    {
        Assert.True(System().Init());
    }

    [Fact]
    public void ShutdownClosesTheLogThenShutsTheTableDown()
    {
        _engine.Add("Doors.Move1", false, "doors/move1.wav");
        var system = System(makeResLists: true);
        system.LevelInitPreEntity();
        _engine.Calls.Clear();

        system.Shutdown();

        Assert.Equal(["Close 100", "ModShutdown"], _engine.Calls);
    }

    //-------------------------------------------------------------------------
    // LevelInitPreEntity: the map's overrides and the preload walk
    //-------------------------------------------------------------------------

    [Fact]
    public void ALevelsOwnSoundScriptIsAddedAsOverrides()
    {
        _engine.Files.Add("maps/dm_lockdown_level_sounds.txt");

        System().LevelInitPreEntity();

        Assert.Contains("FileExists maps/dm_lockdown_level_sounds.txt GAME", _engine.Calls);
        Assert.Contains("AddSoundOverrides maps/dm_lockdown_level_sounds.txt", _engine.Calls);
    }

    [Fact]
    public void NoLevelScriptNoOverrides()
    {
        System().LevelInitPreEntity();

        Assert.Contains("FileExists maps/dm_lockdown_level_sounds.txt GAME", _engine.Calls);
        Assert.DoesNotContain(_engine.Calls, c => c.StartsWith("AddSoundOverrides", StringComparison.Ordinal));
    }

    [Fact]
    public void TheMapNameIsLoweredItsSlashesFixedAndItsExtensionStripped()
    {
        _map = "Sub\\DM_Test.v2";

        System().LevelInitPreEntity();

        Assert.Contains("FileExists maps/sub/dm_test_level_sounds.txt GAME", _engine.Calls);
    }

    [Fact]
    public void EveryEntryThatAsksToPreloadHasItsWavesPrecached()
    {
        _engine.Add("A.Preloaded", true, "a/one.wav", "a/two.wav");
        _engine.Add("B.NotPreloaded", false, "b/one.wav");
        _engine.Add("C.Preloaded", true, "c/one.wav");

        System().LevelInitPreEntity();

        Assert.Equal(["PrecacheSound a/one.wav True", "PrecacheSound a/two.wav True", "PrecacheSound c/one.wav True"],
                     _engine.Precaches);
    }

    [Fact]
    public void ThePreloadIsPermittedSoItWarnsNoDirectPrecache()
    {
        _engine.Add("A.Preloaded", true, "a/one.wav");

        System().LevelInitPreEntity();

        Assert.Empty(_warnings);
    }

    [Fact]
    public void ThePreloadRunsAfterTheOverridesAreAdded()
    {
        _engine.Files.Add("maps/dm_lockdown_level_sounds.txt");
        _engine.Add("A.Preloaded", true, "a/one.wav");

        System().LevelInitPreEntity();

        Assert.True(_engine.Calls.IndexOf("AddSoundOverrides maps/dm_lockdown_level_sounds.txt")
                    < _engine.Calls.IndexOf("PrecacheSound a/one.wav True"));
    }

    [Fact]
    public void AnEntryWithNoWavesSaysSoOnTheDeveloperConsole()
    {
        _engine.Add("Empty.Entry", true);

        System().LevelInitPreEntity();

        Assert.Equal(["CSoundEmitterSystem:  sounds.txt entry 'Empty.Entry' has no waves listed under 'wave' or 'rndwave' key!!!\n"],
                     _devMsgs);
        Assert.Empty(_engine.Precaches);
    }

    //-------------------------------------------------------------------------
    // LevelShutdownPostEntity and Flush
    //-------------------------------------------------------------------------

    [Fact]
    public void LevelShutdownClearsTheOverrides()
    {
        System().LevelShutdownPostEntity();

        Assert.Equal(["ClearSoundOverrides"], _engine.Calls);
    }

    [Fact]
    public void LevelShutdownClosesTheLogAfterClearingTheOverrides()
    {
        var system = System(makeResLists: true);
        system.LevelInitPreEntity();
        _engine.Calls.Clear();

        system.LevelShutdownPostEntity();

        Assert.Equal(["ClearSoundOverrides", "Close 100"], _engine.Calls);
    }

    [Fact]
    public void CloseLogClosesTheLogAndLeavesTheTable()
    {
        var system = System(makeResLists: true);
        system.LevelInitPreEntity();
        _engine.Calls.Clear();

        system.CloseLog();

        Assert.Equal(["Close 100"], _engine.Calls);
    }

    [Fact]
    public void FlushClosesTheLogThenFlushesTheTable()
    {
        var system = System(makeResLists: true);
        system.LevelInitPreEntity();
        _engine.Calls.Clear();

        system.Flush();

        Assert.Equal(["Close 100", "Flush"], _engine.Calls);
    }

    //-------------------------------------------------------------------------
    // PrecacheScriptSound
    //-------------------------------------------------------------------------

    [Fact]
    public void AScriptPrecachesEveryWaveAndAnswersItsIndex()
    {
        _engine.Add("First", false, "x.wav");
        _engine.Add("Doors.Move1", false, "doors/move1.wav", "doors/move2.wav");

        short handle = System().PrecacheScriptSound("Doors.Move1");

        Assert.Equal((short)1, handle);
        Assert.Equal(["PrecacheSound doors/move1.wav True", "PrecacheSound doors/move2.wav True"], _engine.Precaches);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void AWaveNotInTheTableIsPrecachedDirectlyWithoutAWarning()
    {
        short handle = System().PrecacheScriptSound("ambient/hum.WAV");

        Assert.Equal((short)-1, handle);
        Assert.Equal(["PrecacheSound ambient/hum.WAV True"], _engine.Precaches);
        Assert.Empty(_warnings);
        Assert.Empty(_devMsgs);
    }

    [Fact]
    public void AnMp3IsADirectPrecacheToo()
    {
        System().PrecacheScriptSound("music/song.mp3");

        Assert.Equal(["PrecacheSound music/song.mp3 True"], _engine.Precaches);
    }

    [Fact]
    public void Mp3IsMatchedCaseSensitivelyAsStocksStrstrDid()
    {
        System().PrecacheScriptSound("music/song.MP3");

        Assert.Empty(_engine.Precaches);
        Assert.Equal(["PrecacheScriptSound 'music/song.MP3' failed, no such sound script entry\n"], _devMsgs);
    }

    [Fact]
    public void AMissingScriptIsReportedOncePerName()
    {
        var system = System();

        short handle = system.PrecacheScriptSound("No.Such");
        system.PrecacheScriptSound("No.Such");
        system.PrecacheScriptSound("Other.Missing");

        Assert.Equal((short)-1, handle);
        Assert.Equal(["PrecacheScriptSound 'No.Such' failed, no such sound script entry\n",
                      "PrecacheScriptSound 'Other.Missing' failed, no such sound script entry\n"], _devMsgs);
    }

    [Fact]
    public void TheMissingSetIsCaseSensitive()
    {
        var system = System();

        system.PrecacheScriptSound("No.Such");
        system.PrecacheScriptSound("no.such");

        Assert.Equal(2, _devMsgs.Count);
    }

    [Fact]
    public void AnEmptyNameReportsNothing()
    {
        System().PrecacheScriptSound("");

        Assert.Empty(_devMsgs);
        Assert.Empty(_engine.Precaches);
    }

    //-------------------------------------------------------------------------
    // GameSound_Precache: the direct precache's two warnings
    //-------------------------------------------------------------------------

    [Fact]
    public void ADirectPrecacheWarnsThatItIsDirect()
    {
        bool answer = System().Precache("ambient/hum.wav");

        Assert.True(answer);
        Assert.Equal(["Direct precache of ambient/hum.wav\n"], _warnings);
        Assert.Equal(["PrecacheSound ambient/hum.wav True"], _engine.Precaches);
    }

    [Fact]
    public void APrecacheOutsideTheWindowOfAnUnprecachedSoundIsLate()
    {
        _engine.Allowed = false;

        System().Precache("ambient/hum.wav");

        Assert.Equal(["Direct precache of ambient/hum.wav\n", "Late precache of ambient/hum.wav\n"], _warnings);
        Assert.Equal(["PrecacheSound ambient/hum.wav True"], _engine.Precaches);
    }

    [Fact]
    public void APrecacheOutsideTheWindowOfAPrecachedSoundIsNotLate()
    {
        _engine.Allowed = false;
        _engine.Precached.Add("ambient/hum.wav");

        System().Precache("ambient/hum.wav");

        Assert.Equal(["Direct precache of ambient/hum.wav\n"], _warnings);
    }

    [Fact]
    public void AScriptPrecachedLateWarnsLateButNotDirect()
    {
        _engine.Allowed = false;
        _engine.Add("Doors.Move1", false, "doors/move1.wav");

        System().PrecacheScriptSound("Doors.Move1");

        Assert.Equal(["Late precache of doors/move1.wav\n"], _warnings);
    }

    [Fact]
    public void ThePermitEndsWithTheScriptsWaves()
    {
        _engine.Add("Doors.Move1", false, "doors/move1.wav");
        var system = System();

        system.PrecacheScriptSound("Doors.Move1");
        system.Precache("ambient/hum.wav");

        Assert.Equal(["Direct precache of ambient/hum.wav\n"], _warnings);
    }

    //-------------------------------------------------------------------------
    // The prefetch twins
    //-------------------------------------------------------------------------

    [Fact]
    public void PrefetchingAScriptPrefetchesEveryWave()
    {
        _engine.Add("Doors.Move1", false, "doors/move1.wav", "doors/move2.wav");

        System().PrefetchScriptSound("Doors.Move1");

        Assert.Equal(["PrefetchSound doors/move1.wav", "PrefetchSound doors/move2.wav"], _engine.Calls);
    }

    [Fact]
    public void PrefetchingAWaveNotInTheTablePrefetchesItAsNamed()
    {
        System().PrefetchScriptSound("ambient/hum.wav");

        Assert.Equal(["PrefetchSound ambient/hum.wav"], _engine.Calls);
    }

    [Fact]
    public void PrefetchingAMissingScriptDoesNothingAndSaysNothing()
    {
        System().PrefetchScriptSound("No.Such");

        Assert.Empty(_engine.Calls);
        Assert.Empty(_devMsgs);
    }

    [Fact]
    public void PrefetchingAScriptWithNoWavesSaysSo()
    {
        _engine.Add("Empty.Entry", false);

        System().PrefetchScriptSound("Empty.Entry");

        Assert.Equal(["CSoundEmitterSystem:  sounds.txt entry 'Empty.Entry' has no waves listed under 'wave' or 'rndwave' key!!!\n"],
                     _devMsgs);
    }

    [Fact]
    public void PrefetchIsTheEnginesPrefetch()
    {
        System().Prefetch("ambient/hum.wav");

        Assert.Equal(["PrefetchSound ambient/hum.wav"], _engine.Calls);
    }

    //-------------------------------------------------------------------------
    // The -makereslists precache log
    //-------------------------------------------------------------------------

    [Fact]
    public void WithoutMakeResListsNothingIsLogged()
    {
        _engine.Add("Doors.Move1", false, "doors/move1.wav");
        var system = System();

        system.LevelInitPreEntity();
        system.PrecacheScriptSound("Doors.Move1");

        Assert.DoesNotContain(_engine.Calls, c => c.StartsWith("Open", StringComparison.Ordinal)
                                                  || c.StartsWith("CreateDirHierarchy", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLevelsLogIsOpenedInTheReslistsDirectory()
    {
        System(makeResLists: true).LevelInitPreEntity();

        Assert.Equal(["CreateDirHierarchy reslists DEFAULT_WRITE_PATH", "Open reslists\\dm_lockdown.snd wt GAME"],
                     _engine.Calls.Take(2));
    }

    [Fact]
    public void EachScriptIsLoggedOnceQuoted()
    {
        _engine.Add("Doors.Move1", false, "doors/move1.wav");
        _engine.Add("Doors.Move2", false, "doors/move2.wav");
        var system = System(makeResLists: true);
        system.LevelInitPreEntity();

        system.PrecacheScriptSound("Doors.Move1");
        system.PrecacheScriptSound("Doors.Move1");
        system.PrecacheScriptSound("Doors.Move2");

        Assert.Equal("\"Doors.Move1\"\n\"Doors.Move2\"\n", _engine.Written[100].ToString());
    }

    [Fact]
    public void AMissingScriptOrADirectWaveIsNotLogged()
    {
        var system = System(makeResLists: true);
        system.LevelInitPreEntity();

        system.PrecacheScriptSound("No.Such");
        system.PrecacheScriptSound("ambient/hum.wav");

        Assert.Equal("", _engine.Written[100].ToString());
    }

    [Fact]
    public void ALogClosedAtLevelShutdownIsReopenedByTheNextPrecache()
    {
        _engine.Add("Doors.Move1", false, "doors/move1.wav");
        var system = System(makeResLists: true);
        system.LevelInitPreEntity();
        system.PrecacheScriptSound("Doors.Move1");
        system.LevelShutdownPostEntity();

        system.PrecacheScriptSound("Doors.Move1");

        Assert.Equal("\"Doors.Move1\"\n", _engine.Written[101].ToString());
    }

    [Fact]
    public void ALogThatWillNotOpenDisablesLoggingWithOneWarning()
    {
        _engine.OpenFails = true;
        _engine.Add("Doors.Move1", false, "doors/move1.wav");
        _engine.Add("Doors.Move2", false, "doors/move2.wav");
        var system = System(makeResLists: true);
        system.LevelInitPreEntity();

        system.PrecacheScriptSound("Doors.Move1");
        system.PrecacheScriptSound("Doors.Move2");

        Assert.Equal(["Disabling precache logging due to file i/o problem!!!\n"], _warnings);
        Assert.Equal(2, _engine.Calls.Count(c => c.StartsWith("Open", StringComparison.Ordinal)));
    }

    [Fact]
    public void ALevelInitWithTheLogStillOpenForgetsWhatItLogged()
    {
        // StartLog clears the set even when it keeps the open file: a second
        // level start with no shutdown between logs the script again.
        _engine.Add("Doors.Move1", false, "doors/move1.wav");
        var system = System(makeResLists: true);
        system.LevelInitPreEntity();
        system.PrecacheScriptSound("Doors.Move1");

        system.LevelInitPreEntity();
        system.PrecacheScriptSound("Doors.Move1");

        Assert.Equal("\"Doors.Move1\"\n\"Doors.Move1\"\n", _engine.Written[100].ToString());
    }

    [Fact]
    public void ALevelInitForgetsWhatTheLastLevelLogged()
    {
        _engine.Add("Doors.Move1", false, "doors/move1.wav");
        var system = System(makeResLists: true);
        system.LevelInitPreEntity();
        system.PrecacheScriptSound("Doors.Move1");
        system.LevelShutdownPostEntity();

        system.LevelInitPreEntity();
        system.PrecacheScriptSound("Doors.Move1");

        Assert.Equal("\"Doors.Move1\"\n", _engine.Written[101].ToString());
    }

    //-------------------------------------------------------------------------
    // SourceSharp_PrecacheSound's rule: a script or a path
    //-------------------------------------------------------------------------

    [Theory]
    [InlineData("Doors.Move1", true)]
    [InlineData("hum.wav", true)]
    [InlineData("ambient/hum.wav", false)]
    [InlineData("ambient\\hum.wav", false)]
    public void ANameWithNoSlashIsAScript(string name, bool script)
        => Assert.Equal(script, SoundEmitterSystem.IsScriptSound(name));

    [Fact]
    public void APathIsADirectPrecacheAndWarnsSo()
    {
        System().PrecacheSound("vehicles/digger_stopengine1.wav");

        Assert.Equal(["Direct precache of vehicles/digger_stopengine1.wav\n"], _warnings);
        Assert.Equal(["PrecacheSound vehicles/digger_stopengine1.wav True"], _engine.Precaches);
    }

    [Fact]
    public void AScriptNameIsAScriptPrecache()
    {
        _engine.Add("Doors.Move1", false, "doors/move1.wav");

        System().PrecacheSound("Doors.Move1");

        Assert.Equal(["PrecacheSound doors/move1.wav True"], _engine.Precaches);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void AnEmptyNameIsNotPrecached()
    {
        System().PrecacheSound("");

        Assert.Empty(_engine.Calls);
        Assert.Equal(0, _engine.Lookups);
    }
}
