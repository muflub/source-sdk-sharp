using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;
using SourceSharp.Tests.Ui;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>A UiHost over fakes: a recording surface, a recording engine, no standalones and no engine client.</summary>
internal static class UiHostKit
{
    private static readonly SourceSharp.Client.Ui.Screens.FakeRadialMenu NoRadialMenu = new("") { Showing = false };

    internal sealed class NoArsenal : ILocalArsenal
    {
        public bool TryGetActive(out string name, out int clip, out int reserve)
        {
            (name, clip, reserve) = ("", -1, -1);
            return false;
        }
    }

    internal sealed class NoStandalones : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live() => [];

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    public static UiHost Build(UiFidelityTests.RecordingSurface surface, UiScreen[] hudScreens, UiScreen[]? menuScreens = null,
                               UiMenus? menus = null, RecordingEngine? engine = null, ILog? log = null)
    {
        var fonts = new UiFontLibrary();
        var backend = new VguiBackend(surface, fonts, new UiFontFiles([]));
        engine ??= Fake().Returns("Hud.LocalPlayerHealth", 80).Returns("Clock.Now", 3f).Returns("Hud.LocalPlayerIndex", 1)
                         .Returns("Console.FindConVarValue", "test server")
                         // The scoreboard maxClients load (docs/todo.md): unbound in the kit,
                         // so the header draws the array ceiling, the figure it drew before.
                         .Returns("Environment.Globals", default(Globals));
        var localiser = new GameUiLocaliser(null, Fake().ReturnsEachCall("Hud.Localise", () => ""));
        var hud = new Hud(new RecordingLog(), engine, engine, new DamageMessage(), new HudMsgMessage(),
                          new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());
        var player = new LocalPlayerUi(engine, new NoArsenal(), hud);
        var scores = new ScoreboardUi(new NoStandalones(), engine, new DefaultForwarders.NoEngineClient(), new EnginePlayerNames(new DefaultForwarders.NoEngineClient(), new IdentityNameFilter()), engine, engine);
        menus ??= new UiMenus(scores, new TeamMenuUi(scores, engine, new DefaultForwarders.NoEngineClient(), new RecordingLog()), new MotdUi(), new ConsoleUi(new DefaultForwarders.NoEngineClient()));
        var windows = new UiWindows(new MemoryWindowStore());
        var theme = SourceSharpTheme.Create();
        return new UiHost(log ?? new RecordingLog(), engine,
                          new UiLayer(hudScreens, new UiContext(theme, backend, localiser, []), backend),
                          new UiLayer(menuScreens ?? [], new UiContext(theme, backend, localiser, []), backend, windows),
                          backend, surface, player, hud.Messages, hud.Build, null, menus)
        {
            Windows = windows,
        };
    }

    /// <summary>
    /// The side's services a whole UI composes over (ClientUi.Build), over
    /// fakes: <paramref name="dir"/> is the mod's game and managed directory,
    /// and <paramref name="modData"/> answers IModInfo's other keys (the mod's
    /// UI assemblies).
    /// </summary>
    public static ServiceContainer RuntimeServices(RecordingEngine engine, IUiSurface surface, string dir,
                                                   Func<string, object?>? modData = null,
                                                   SourceSharp.Client.Ui.Screens.FakeRadialMenu? radial = null)
    {
        radial ??= NoRadialMenu;
        var services = new ServiceContainer()
            .AddInstance<IEngineHud>(engine).AddInstance<IEngineConsole>(engine).AddInstance<IGameClock>(engine)
            .AddInstance<IModInfo>(new ModInfo(k => k == ModInfo.Keys.ManagedDir || k == ModInfo.Keys.GameDir ? dir : modData?.Invoke(k)))
            .AddInstance<IUiSurface>(surface)
            .AddInstance<IUiKeyActions>(new SourceSharp.Client.Ui.Screens.Samples.FakeKeyActions())
            .AddInstance<IRadialMenuSource>(radial).AddInstance<IRadialMenuActions>(radial)
            .AddInstance<IVoteSource>(new SourceSharp.Client.Ui.Screens.FakeVote { Showing = false })
            .AddInstance<IAdminKeySource>(new SourceSharp.Client.Ui.Screens.Samples.FakeAdminKey(false))
            .AddInstance<IClientStandalones>(new NoStandalones())
            .AddInstance<IPlayerNames>(new EnginePlayerNames(new DefaultForwarders.NoEngineClient(), new IdentityNameFilter()))
            .AddInstance<IEngineEnvironment>(engine)
            .AddInstance<IKillFeedSource>(new NoKills())
            .AddInstance<IDamageDealtSource>(new SourceSharp.Client.Ui.Screens.Samples.FakeDamageDealt())
            .AddInstance<IMinimapSource>(new SourceSharp.Client.Ui.Screens.Samples.FakeMinimap { Outline = [] })
            .AddInstance<ICentrePrintSource>(new CentrePrint(engine))
            .AddInstance<IChatSource>(new ChatFeed(engine, new RecordingLog(), engine, new NoChatPlayers()))
            .AddInstance<IChatInputSource>(new ChatInputUi(new DefaultForwarders.NoEngineClient()))
            .AddInstance<ILoadingDiscSource>(new LoadingDisc(engine))
            .AddInstance<IMenuBackgroundName>(new DefaultForwarders.NoEngineClient())
            // The options window's engine (plan_gameui_managed.md G2): defaults that answer nothing.
            .AddInstance<IVoiceTweakControls>(new DefaultForwarders.NoEngineClient())
            .AddInstance<IFwdGameUIFuncs>(new DefaultForwarders.NoGameUIFuncs())
            .AddInstance<IFwdMaterialSystem>(new DefaultForwarders.NoMaterialSystem())
            .AddInstance<IFwdFileFinder>(new DefaultForwarders.NoFileFinder())
            .AddInstance<IFwdBaseFileSystem>(new DefaultForwarders.NoFiles())
            // The server browser's Steam and socket: no Steam, no datagrams.
            .AddInstance<ISteamServerBrowser>(new NoSteamBrowser())
            .AddInstance<IDatagramTransport>(new NoDatagrams())
            .AddInstance<IVoiceBlockEvents>(new NoMutes())
            .AddInstance<IDebugOverlaySource>(new NoOverlay())
            .AddInstance<IVoiceSource>(new VoiceSpeakers(new NoVoicePlayers()));
        var observer = new ObserverState(new NoObserverFields(), new NoSpectated(), () => null);
        return services.AddInstance<IObserverSource>(observer).AddInstance<ISpectatorTargetSource>(observer);
    }

    private sealed class NoMutes : IVoiceBlockEvents
    {
        public bool IsBlocked(int client) => false;

        public void ToggleBlocked(int client) { }
    }

    /// <summary>A -hushsteam launch's Steam: unavailable, every list empty.</summary>
    internal sealed class NoSteamBrowser : ISteamServerBrowser
    {
        public bool Available => false;
        public bool RequestInternet(string gameDir) => false;
        public bool Refreshing => false;
        public int ServerCount => 0;
        public bool Responded(int i) => false;
        public bool TryGetServer(int i, out SteamServerItem item)
        {
            item = default;
            return false;
        }
        public void ReleaseRequest() { }
        public int FavouriteCount => 0;
        public bool TryGetFavourite(int i, out SteamFavourite favourite)
        {
            favourite = default;
            return false;
        }
        public void AddFavourite(uint ip, ushort connectionPort, ushort queryPort, uint flags) { }
        public void RemoveFavourite(uint ip, ushort connectionPort, ushort queryPort, uint flags) { }
    }

    private sealed class NoDatagrams : IDatagramTransport
    {
        public void Send(ServerAddress to, ReadOnlySpan<byte> data) { }
        public void Broadcast(ushort port, ReadOnlySpan<byte> data) { }
        public int Receive(Span<byte> buffer, out ServerAddress from)
        {
            from = default;
            return -1;
        }
    }

    private sealed class NoChatPlayers : IChatPlayers
    {
        public bool TryFind(int client, out string name, out int team) => ((name, team) = ("", 0)) == default;
    }

    private sealed class NoSpectated : ISpectatedPlayers
    {
        public bool TryFind(int client, out string name, out int team, out int health)
        {
            (name, team, health) = ("", 0, 0);
            return false;
        }
    }

    private sealed class NoVoicePlayers : IVoicePlayers
    {
        public bool TryFind(int client, out string name, out int team, out bool alive)
        {
            (name, team, alive) = ("", 0, true);
            return false;
        }
    }

    private sealed class NoOverlay : IDebugOverlaySource
    {
        public int Count => 0;

        public DebugOverlayLine Line(int i) => throw new ArgumentOutOfRangeException(nameof(i));
    }

    private sealed class NoKills : IKillFeedSource
    {
        public int LineCount => 0;

        public KillFeedLine Line(int i) => throw new ArgumentOutOfRangeException(nameof(i));
    }
}
