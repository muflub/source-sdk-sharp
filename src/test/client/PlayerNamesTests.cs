using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Steam;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Player names through ONE source, filtered as stock's g_PR->GetPlayerName
/// (UTIL_GetFilteredPlayerName) filtered every displayed name (plan_client_novalve
/// C6, Steam to managed; lane c6-input): the service, the Steam filter with
/// stock's fallback, and each screen that shows a name.
/// </summary>
public class PlayerNamesTests
{
    private sealed class Engine(Dictionary<int, ClientPlayerInfo> infos) : IFwdEngineClient
    {
        public bool TryGetPlayerInfo(int entIndex, out ClientPlayerInfo info) => infos.TryGetValue(entIndex, out info);
        public void SubmitConsoleLine(string line) { }
        public void SpectatorCommand(SpectatorKeyCommand command) { }
        public void SpectatorServerCommand(string line) { }
        public bool IsInGame() => true;
        public void VoiceServerCommand(string line, bool reliable) { }
        public string? LevelName() => null;
        public string? GameDirectory() => null;
        public uint ProtocolVersion() => 0;
        public void JoinTeam(int team) { }
        public void ConnectFromSteamLaunch(string connectLine) { }
        public void Reconnect() { }
        public void RequestManagedReload() { }
        public void ConsolePrintAt(int position, string text) { }
        public void ConsolePrintAt(int position, float timeToLive, float red, float green, float blue, bool fixedWidthFont, string text) { }
        public bool TryLightForPoint(in Vector position, bool clamp, out Vector light) { light = default; return false; }
        public bool TryLightForPointFast(in Vector position, bool clamp, out Vector light) { light = default; return false; }
    }

    private sealed class Shout : IPlayerNameFilter
    {
        public string Filter(string name) => "<" + name.ToUpperInvariant() + ">";
    }

    private static Engine OneBob() => new(new() { [2] = new ClientPlayerInfo("bob", 7, true, "STEAM_1:0:2") });

    // ---- the service ----

    [Fact]
    public void TheServiceFiltersTheEnginesNameAndKeepsTheRest()
    {
        var names = new EnginePlayerNames(OneBob, new Shout());
        Assert.True(names.TryGet(2, out var info));
        Assert.Equal(new ClientPlayerInfo("<BOB>", 7, true, "STEAM_1:0:2"), info);
    }

    [Fact]
    public void AnUnknownSlotIsNotFound()
    {
        var names = new EnginePlayerNames(OneBob, new Shout());
        Assert.False(names.TryGet(3, out _));
        Assert.False(new EnginePlayerNames(() => null, new Shout()).TryGet(2, out _));
    }

    // ---- the Steam filter (UTIL_GetFilteredPlayerName) ----

    private sealed class Steam(string? answer) : ISteamClientApi
    {
        public readonly List<(int Context, ulong Id, string Text, int Max)> Asked = new();
        public bool Available => true;
        public bool InitSafe() => true;
        public void SetTryCatchCallbacks(bool tryCatch) { }
        public bool HasUserAndUtils => answer is not null;
        public bool InitFilterText() => true;
        public bool LoggedOn => true;
        public ulong SteamId => 0;
        public int Universe => 0;
        public uint AppId => 0;
        public void RegisterCallback(nint callback, int callbackId) { }
        public void UnregisterCallback(nint callback) { }
        public string? LaunchCommandLine(out int bytes) { bytes = 0; return null; }
        public string? FilterText(int context, ulong id, string text, int max)
        {
            Asked.Add((context, id, text, max));
            return answer;
        }
    }

    [Fact]
    public void SteamFiltersANameInTheNameContextWithNoSourceSteamId()
    {
        var steam = new Steam("b*b");
        Assert.Equal("b*b", new SteamPlayerNameFilter(steam).Filter("bob"));
        // k_ETextFilteringContextName (3), an invalid CSteamID (0), MAX_PLAYER_NAME_LENGTH (32).
        Assert.Equal(new[] { (3, 0UL, "bob", 32) }, steam.Asked);
    }

    [Fact]
    public void WithNoSteamUtilsTheNameIsShownAsItCame()
    {
        Assert.Equal("bob", new SteamPlayerNameFilter(new Steam(null)).Filter("bob"));
    }

    // ---- each screen that shows a name ----

    private sealed class Standalones : IClientStandalones
    {
        public readonly List<Client.Standalone.ClientStandaloneEntity> All = new();
        public IReadOnlyList<Client.Standalone.ClientStandaloneEntity> Live() => All;
        public Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    private static Standalones BobConnected()
    {
        var resource = new Client.Standalone.ClientPlayerResource(new RecordingEngine());
        resource.State.BindLocal();
        resource.State.Flags[2] = PlayerResourceNetState.FlagConnected | PlayerResourceNetState.FlagAlive;
        resource.State.UserId[2] = 7;
        resource.State.Team[2] = 3;
        resource.State.Health[2] = 80;
        var s = new Standalones();
        s.All.Add(resource);
        return s;
    }

    private static IPlayerNames Names() => new EnginePlayerNames(OneBob, new Shout());

    [Fact]
    public void TheKillFeedShowsTheFilteredName()
    {
        var players = new KillFeedPlayers(BobConnected(), Fake().Returns("Hud.LocalPlayerIndex", 1), Names());
        Assert.True(players.TryFind(7, out string name, out _, out _));
        Assert.Equal("<BOB>", name);
    }

    [Fact]
    public void TheChatShowsTheFilteredName()
    {
        Assert.True(new ChatPlayers(BobConnected(), Names()).TryFind(2, out string name, out _));
        Assert.Equal("<BOB>", name);
    }

    [Fact]
    public void TheVoiceSpeakersShowTheFilteredName()
    {
        Assert.True(new VoicePlayers(BobConnected(), Names()).TryFind(2, out string name, out _, out _));
        Assert.Equal("<BOB>", name);
    }

    [Fact]
    public void TheSpectatorShowsTheFilteredName()
    {
        Assert.True(new SpectatedPlayers(BobConnected(), Names()).TryFind(2, out string name, out _, out _));
        Assert.Equal("<BOB>", name);
    }

    [Fact]
    public void TheScoreboardShowsTheFilteredNameAndTheBotFlag()
    {
        var engine = Fake().Returns("Hud.LocalPlayerIndex", 1).Returns("Environment.Globals", default(Globals))
                          .Returns("Console.FindConVarValue", (string?)null);
        var scores = new ScoreboardUi(BobConnected(), engine, () => null, Names(), engine, engine);
        scores.Refresh();
        Assert.Equal(1, scores.PlayerCount);
        Assert.Equal("<BOB>", scores.Player(0).Name);
        Assert.True(scores.Player(0).Bot);
    }
}

/// <summary>
/// No screen reads a player's name from the engine past the one source: the
/// engine's player info is asked only by <c>EnginePlayerNames</c> (the
/// filtered name) and <c>VoiceEngine</c> (the guid voice bans key on, which is
/// no displayed name). A new direct read would show an unfiltered name.
/// </summary>
public class PlayerNameSourceCensusTests
{
    // Without the extension: these are managed sources, which the suite's
    // file-name census (resolved against the farm) cannot see; the positive
    // arm below is what keeps this fact from going vacuous.
    private static readonly string[] Allowed =
    [
        "src/client/managed/Ui/PlayerNames",     // the one source
        "src/client/managed/Voice/VoiceEngine",  // the guid, not a name
        "src/client/interop/EngineClient",       // the engine read itself
        "src/client/interface/EngineClient",     // the seam that declares that read
    ];

    [Fact]
    public void OnlyTheNameSourceAndTheVoiceGuidReadTheEnginesPlayerInfo()
    {
        string root = RepoLayout.Repo("src", "client");
        var readers = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("TryGetPlayerInfo(", StringComparison.Ordinal))
            .Select(f => "src/client/" + Path.ChangeExtension(Path.GetRelativePath(root, f), null).Replace('\\', '/'))
            .Order()
            .ToList();

        // The positive arm: the scan read the tree and found the source itself.
        Assert.Contains("src/client/managed/Ui/PlayerNames", readers);
        Assert.Equal(Allowed.Order(), readers);
    }
}
