using SourceSharp.Abi;
using SourceSharp.Client;
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
}
