using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Soundscapes;
using SourceSharp.Shared.Soundscapes;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client soundscape system as native reaches it (sound cluster 17, lane
/// c2-sims): the client-system shell's three calls through
/// IManagedSoundscapesClient, each counted, each on the driver; and the
/// restore arm stock keeps for a save restore.
/// </summary>
public class ClientSoundscapeWiringTests
{
    private sealed class Driver : IClientSoundscapesDriver
    {
        public readonly List<string> Calls = new();

        public void LevelInit(string mapName) => Calls.Add("init " + mapName);

        public void Update(float frameTime) => Calls.Add("update " + frameTime.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public void LevelShutdown() => Calls.Add("shutdown");
    }

    [Fact]
    public unsafe void EachShellCallIsCountedAndLandsOnTheDriver()
    {
        var crossings = new Crossings();
        var driver = new Driver();
        IManagedSoundscapesClient binding = new SoundscapesClientBinding(crossings, driver);

        fixed (byte* map = Encoding.UTF8.GetBytes("dm_lockdown\0"))
            binding.ClientSoundscapesLevelInit(map);
        binding.ClientSoundscapesUpdate(0.25f);
        binding.ClientSoundscapesUpdate(0.5f);
        binding.ClientSoundscapesLevelShutdown();

        Assert.True(string.Join(",", driver.Calls) == "init dm_lockdown,update 0.25,update 0.5,shutdown", string.Join(",", driver.Calls));
        Assert.True(crossings.Read(CrossingOp.ClientSoundscapesLevelInit) == 1
                    && crossings.Read(CrossingOp.ClientSoundscapesUpdate) == 2
                    && crossings.Read(CrossingOp.ClientSoundscapesLevelShutdown) == 1,
                    "each slot counts its own crossing");
    }

    private sealed class Audio : ISoundscapeAudio, ISoundscapeSounds
    {
        public readonly List<string> Loops = new();

        public void EmitAmbient(string wave, float volume, int pitch, int flags) => Loops.Add(wave);
        public void EmitWorld(string wave, float volume, int soundLevel, int pitch, int flags, Vector origin) => Loops.Add(wave);
        public void StopWorld(string wave) { }
        public void SetRoomType(int roomType) { }
        public void SetPlayerDsp(int dspType) { }
        public void SetConVar(string name, string value) { }
        public void RevertConVar(string name) { }

        public void MainView(out Vector origin, out Vector forward, out Vector right)
        {
            origin = default;
            forward = new Vector(1, 0, 0);
            right = new Vector(0, -1, 0);
        }

        public bool MakingResLists => false;
        public void TouchSoundFile(string wave) { }
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 1f;
    }

    private const string Script = """
        "restore.test"
        {
            "playlooping" { "volume" "1" "wave" "kept.wav" }
            "playlooping" { "volume" "1" "wave" "suppressed.wav" "suppress_on_restore" "1" }
        }
        """;

    private static (ClientSoundscapes, Audio) Build()
    {
        var engine = TestServices.Fake();
        engine.Returns("Console.FindConVarValue", "0");
        var audio = new Audio();
        var system = new ClientSoundscapes(audio, audio, new Clock(), engine, engine);
        system.LevelInit(SoundscapeLibrary.Load(path => path == SoundscapeLibrary.ManifestPath
            ? "\"manifest\" { \"file\" \"scripts/restore.txt\" }"
            : path == "scripts/restore.txt" ? Script : null));
        return (system, audio);
    }

    [Fact]
    public void ASoundMarkedSuppressOnRestoreIsNotStartedOnTheRestoreFrame()
    {
        var (system, audio) = Build();

        system.OnRestore();
        system.Force("restore.test", 36);

        Assert.True(string.Join(",", audio.Loops) == "kept.wav", "on the restore frame: " + string.Join(",", audio.Loops));
    }

    [Fact]
    public void OffTheRestoreFrameBothSoundsStart()
    {
        var (system, audio) = Build();

        system.OnRestore();
        system.Update(0.1f, 3f);   // the restore frame passes
        system.Force("restore.test", 36);

        Assert.True(string.Join(",", audio.Loops) == "kept.wav,suppressed.wav", "a frame later: " + string.Join(",", audio.Loops));
    }
}
