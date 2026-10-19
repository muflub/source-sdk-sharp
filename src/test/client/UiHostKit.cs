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
                               UiMenus? menus = null, RecordingEngine? engine = null)
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
        var scores = new ScoreboardUi(new NoStandalones(), engine, () => null, new EnginePlayerNames(() => null, new IdentityNameFilter()), engine, engine);
        menus ??= new UiMenus(scores, new TeamMenuUi(scores, engine, () => null, new RecordingLog()), new MotdUi(), new ConsoleUi(() => null));
        var windows = new UiWindows(new MemoryWindowStore());
        var theme = SourceSharpTheme.Create();
        return new UiHost(new RecordingLog(), engine,
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
                                                   Func<string, object?>? modData = null)
    {
        var services = new ServiceContainer()
            .AddInstance<IEngineHud>(engine).AddInstance<IEngineConsole>(engine).AddInstance<IGameClock>(engine)
            .AddInstance<IModInfo>(new ModInfo(k => k == ModInfo.Keys.ManagedDir || k == ModInfo.Keys.GameDir ? dir : modData?.Invoke(k)))
            .AddInstance<IUiSurface>(surface)
            .AddInstance<IClientStandalones>(new NoStandalones())
            .AddInstance<IPlayerNames>(new EnginePlayerNames(() => null, new IdentityNameFilter()))
            .AddInstance<IEngineEnvironment>(engine)
            .AddInstance<IKillFeedSource>(new NoKills())
            .AddInstance<ICentrePrintSource>(new CentrePrint(engine))
            .AddInstance<IChatSource>(new ChatFeed(engine, new RecordingLog(), engine, new NoChatPlayers()))
            .AddInstance<IChatInputSource>(new ChatInputUi(() => null))
            .AddInstance<ILoadingDiscSource>(new LoadingDisc(engine))
            .AddInstance<IDebugOverlaySource>(new NoOverlay())
            .AddInstance<IVoiceSource>(new VoiceSpeakers(new NoVoicePlayers()));
        var observer = new ObserverState(new NoObserverFields(), new NoSpectated(), () => null);
        return services.AddInstance<IObserverSource>(observer).AddInstance<ISpectatorTargetSource>(observer);
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
