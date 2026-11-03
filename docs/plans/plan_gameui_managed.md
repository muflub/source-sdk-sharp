# Plan: GameUI in C# — replace GameUI.so wholesale

Status: APPROVED 2026-11-01 (user: "approved, start"). G0 first.

Split out of `plan_client_novalve.md` §3.2a and phase C4 at the user's ask
(2026-11-01: "split it to its own plan and just replace game ui wholesale by
forwarding it all to managed and remaking the uis there"). It **replaces
C4's strangler**: there is no forwarding to the stock module at any point.
Our module answers every `IGameUI` and `IGameConsole` call, and every screen
the stock module drew is a managed screen.

## 0. Goal and shape

- The engine loads the stock `GameUI.so` as always, but **its factory is
  ours**: a one-shot hook client.so installs on `engine.so`'s symbol-lookup
  import (`dlsym`; Windows `GetProcAddress`) hands the engine our
  `CreateInterface` when it resolves the stock module's (D2, ruled). Ours
  answers `GameUI011` and `GameConsole004`, nothing else. The stock module
  stays mapped but is **never called**: its `Initialize` never runs, so it
  registers no command, loads no localisation and never loads
  `ServerBrowser.so`. No engine directory view, no extra binary.
- Every method of both interfaces lands in managed code through the repo's
  existing "Valve code calls, managed answers" pattern: a libss_fwd class
  derived from the Valve interface, each virtual calling one row of a
  managed-built table (`fwd_steam.cpp` is the model). Native code carries
  calls; it decides nothing.
- Every surface the stock module drew becomes a managed screen on the menu
  layer: the menu background, the loading screen, the message and failure
  dialogs, confirm quit, the console, options, create server, the server
  browser and the player list. The main and pause menus are already
  managed.
- **No stock fallback, ever** (user ruling 2026-11-01, "delete stock
  fallback"): no switch and no escape hatch. The managed screens land on
  main first, each reachable through `cs_ui_open`, the previews and its
  facts while the stock module still runs the game. The module goes in
  last, in the same commit series that deletes the stock path, so no build
  ships a menu with dead buttons and no build can fall back.

## 1. Ground truth at `c325d7f88` (every path below was read this session)

### 1.1 What the engine does with GameUI (measured, C0)

`plan_client_novalve.md` §8, "C0 probe results" item 3 (measured 2026-10-05; the C0 probe, `tools/probes/gameui-stub`,
session `tools/probes/sessions/gameui-full.steps`) settled the mechanics:

- **Load path:** the engine loads `GameUI.so` from its own `bin/linux64`,
  not from the mod's game bin. A stub in `bin/<config>/bin/linux64` never
  loaded. An engine view, with every install entry linked and
  `bin/linux64/GameUI.so` replaced, run through the launcher's `-engine
  <dir>`, loaded the view's module. **The mechanism is a client engine
  view.**
- **Order:** `VClient017` resolves first, but `IGameUI::Initialize`, `Start`,
  `IGameConsole::Initialize`/`SetParent` and `OnGameUIActivated` all arrive
  **before** `IBaseClientDLL::Init`, which is where the CLR comes up.
  `Connect`, `PostInit` and per-frame `RunFrame` arrive after it.
- **One session's sequence** (menu, `map`, play, `disconnect`, `quit`):
  `IsConsoleVisible`; `Initialize`, `Start`; console `Initialize`,
  `SetParent`; `OnGameUIActivated`; [client Init]; `Connect`, `PostInit`,
  `RunFrame`…; on `map`: `OnDisconnectFromServer(0)`,
  `OnLevelLoadingStarted(0)`, `UpdateProgressBar` ×249 with
  `#LoadingProgress_*` texts, `OnConnectToServer2`,
  `OnLevelLoadingFinished(0,"","")`, `OnGameUIHidden`; on `disconnect`:
  `OnDisconnectFromServer(0)`, `OnGameUIActivated`; on `quit`: the same,
  then `Shutdown`.
- **Never called in that session:** `IsMainMenuVisible`,
  `SetShowProgressText`, `SetProgressOnStart`, `OnConfirmQuit` (console
  `quit` skips it), `ShowMessageDialog`, `SendMainMenuCommand`, every
  bonus-map, Xbox and session method, and `IGameConsole::Activate`/`Hide`.
- **Loading paint:** the engine paints vgui from inside its load loop
  after `UpdateProgressBar` **returns true**: 285 paints after 250 true
  returns, against 44 when it returns false.

### 1.2 The interfaces

- `IGameUI` (`GameUI011`): 38 virtuals,
  `vendor/client/common/GameUI/IGameUI.h` (vendored at c325d7f88, Xbox stub
  include replaced by a forward declaration).
- `IGameConsole` (`GameConsole004`): 6 virtuals (`Activate`, `Initialize`,
  `Hide`, `Clear`, `IsConsoleVisible`, `SetParent`),
  `../source-sdk-2013-valve/src/common/GameUI/IGameConsole.h`. Not vendored
  yet; G1 vendors it (for `GameUiHost`'s facts).

### 1.3 What the stock module brings besides the interfaces

From `strings` over the 243750 install's `bin/linux64/GameUI.so`. Which
module *registers* each name is G0's census, because strings show only that
the name appears:

- console commands and convars it names: `gamemenucommand` (with its own
  usage line), `gameui_show_dialog`, `gameui_hide_dialog`,
  `gameui_activate`, `gameui_hide`, `gameui_allowescape`,
  `gameui_preventescape`, the crosshair `cl_crosshair*` family,
  `cl_playermodel`, `cl_logofile`, `cl_downloadfilter`, `cl_himodels`,
  `cl_hud_minmode` and more. The TF2 menu (`TFMainMenuUi`) and the core
  menu (`MainMenuUi.Resume`, `gameui_hide`) send some of these today.
- localisation: `Resource/gameui_%language%.txt` and 296 `#GameUI_*`
  tokens (disconnect reasons, options labels).
- `Resource/GameMenu.res` (TF2's in-game menu entries, which
  `TFMainMenu` now draws itself), the dialogs' `.res` files, `maps/*commentary.txt`, `condump%03d.txt`.
- the menu background it paints when no level is up, and the darkening fill
  behind its dialogs. The material it binds is G0's measurement.

### 1.4 What already exists in managed code

| surface | screen | state |
|---|---|---|
| main / pause menu | `src/client/ui/screens/MainMenu.cs`; TF2's `src/tf2/ui/screens/TFMainMenu.cs` (claims `MainMenuOverride`) | done; driven by `UiHost.ShowPauseMenu`/`HidePauseMenu` (`src/client/ui/host/UiRuntime.cs`) |
| console | `src/client/ui/screens/ConsoleScreen.cs` over `ConsoleLines` | done; not yet wired to `IGameConsole` |
| loading | `src/client/ui/screens/LoadingDisc.cs` | a disc only: no progress, no status, no tips |
| options | `MouseOptions.cs`, `KeyBindings.cs` (forms over `UiForm`) | two of six tabs |
| server browser | `src/client/ui/screens/ServerBrowser.cs` | screen done; its source `ServerBrowserUi` (`src/client/managed/Ui/MenuSources.cs`) answers no servers |
| create server | none; `plan_hl2mp_ai.md` §3.5 plans `CreateServer` (map, max players, bots) | planned there; this plan shares it |
| viewport held while GameUI is up | `UiLayer.Hold`, `UiHost.HoldViewport` | done (c325d7f88) |

Native today (c325d7f88): `src/client/native/shell_hud.cpp` hands a plain
menu panel to the **stock** GameUI as its main-menu override
(`IGameUI::SetMainMenuOverride`), and `src/client/native/gameui_edge.h`
watches `IEngineVGui::IsGameUIVisible`, because the stock module forwards
its activation to the client only for `-game tf`. Both go when our module
is in (G4).

Launcher: `Launcher.EngineView` (`src/launcher/Launcher.cs:508`) links the
**top-level** entries of an install, for the dedicated server on Linux
only. This plan does not use it (D2: the client.so hook), so the launcher
does not change.

Library loading: `src/shared/native/loaded_symbol.cpp` (`ss::FindLoadedSymbol`)
already finds a loaded module by file name on both platforms
(`dl_iterate_phdr` / `GetModuleHandleA`); the hook finds `engine.so` the
same way, and `dladdr` tells whether a resolved address lies in the stock
`GameUI.so`.

`ServerBrowser.so` (`ServerBrowser005`) is the stock browser window and
its join dialogs: the tabs (internet, custom, favourites, history,
spectate, LAN, friends, blacklist), add-by-IP, the game-info dialog with
auto-retry, the server password prompt and the warning and VAC-refusal
dialogs (its `servers/*.res`). Of the shipped binaries only `GameUI.so`
names it; `engine.so` does not. Mods cannot override engine modules:
the engine resolves them from its own `bin` (C0: a stub in the mod's bin
never loaded); a mod's `bin` serves only `client` and `server`.

Steam: managed code already calls `libsteam_api`'s flat C API through the
engine's loaded copy (`src/client/interop/SteamUserTickets.cs`), which is
the precedent for the server browser's `ISteamMatchmakingServers`.

## 2. Architecture

### 2.1 The factory and the objects

- Our factory, `SSGameUiFactory` (libss_fwd_client, `src/fwd/native`),
  has `CreateInterface`'s signature and answers `GameUI011` and
  `GameConsole004` with two objects built in `libss_fwd_client`, NULL for
  anything else. No new target and no new export: the hook hands the
  engine its address.
- `SSGameUI : IGameUI` and `SSGameConsole : IGameConsole` (libss_fwd_client,
  no RTTI, no allocation, as `fwd_steam.cpp`). Each virtual calls
  `IManagedGameUi` / `IManagedGameConsole`: one row per method in
  `docs/managed-interfaces.tsv`, generated by `tools/managed-areas`.
  Pointers the engine hands over (factories, `vgui::Panel *`, `VPANEL`)
  cross as opaque handles; strings cross as UTF-8 plus a length.
- **Early calls.** The calls that arrive before the CLR (§1.1) are recorded
  by the native objects in arrival order (a fixed array of call kinds plus
  their arguments: the factory pointer, the parent `VPANEL`) and replayed
  into managed when the client's managed root attaches.
  `IsConsoleVisible` answers false until then. A gtest holds the replay
  order; the `/client/{slot}/gameui` route lists every call received and
  when it was replayed.

### 2.2 Managed side

- `GameUiHost` (client core): implements `IManagedGameUi` and
  `IManagedGameConsole`, registered in `ClientModule`'s container. It
  routes each method:
  - `OnGameUIActivated`/`OnGameUIHidden` → `UiHost.ShowPauseMenu` /
    `HidePauseMenu` (directly; the edge watch goes).
  - loading calls → the loading screen.
  - disconnect and failure calls → the message dialog.
  - console methods → `ConsoleScreen` open, close and clear.
  - `RunFrame` → nothing per frame unless a screen needs it.
- **The GameUI root panel:** `SetParent`'s `VPANEL`, or the engine's
  `PANEL_GAMEUIDLL`, parents the menu layer's GameUI-time panel (today's
  override panel, now ours to place). No stock dialog exists, so no popup
  ordering problem remains: every dialog is a managed window in the same
  layer.
- `SetMainMenuOverride` and `SendMainMenuCommand` answer inertly. Only a
  stock TF2 client called them, and ours draws its menu in managed.
- **Commands:** managed registers what G0's census finds the stock module
  registering that something here uses: at least `gamemenucommand <name>`
  (routing stock's names — `OpenServerBrowser`,
  `OpenCreateMultiplayerGameDialog`, `OpenOptionsDialog`,
  `OpenMutePlayerDialog`, `OpenPlayerListDialog`, `ResumeGame`,
  `Disconnect`, `Quit`, `QuitNoConfirm` — to managed screens) and
  `gameui_show_dialog`/`gameui_hide_dialog`. The convars it owned
  (crosshair, player model, …) are registered by managed only where a
  screen or a cfg reads them.
- **Localisation:** the localiser loads `resource/gameui_<language>.txt`,
  as the stock module did, so engine disconnect reasons and options labels
  resolve.

### 2.3 Loading

`OnLevelLoadingStarted(show)`, `UpdateProgressBar(progress, text)`,
`SetShowProgressText`, `SetProgressOnStart`, `OnLevelLoadingFinished` drive
a managed loading screen. `UpdateProgressBar` returns true, so the engine
paints inside its load loop (§1.1).

The screen must be safe to paint from inside the load: no UI reload and no
allocation-heavy work there. `SetLoadingBackgroundDialog(VPANEL)` is a
client panel in stock TF2; here a mod's loading screen claims the loading
slot instead (TF2: the map image and tips), and the core's draws otherwise.

### 2.4 The rest of the surfaces

| stock surface | managed screen | needs from the engine |
|---|---|---|
| menu background (no level) | `MenuBackground`, lowest menu-layer screen, drawn while no level is up | the material G0 measures |
| message / failure dialogs (`OnDisconnectFromServer(eSteamLoginFailure)`, `OnLevelLoadingFinished(bError, reason, extended)`, `ShowMessageDialog`) | `MessageDialog` | the reason strings (`#GameUI_*`) |
| confirm quit (`OnConfirmQuit`, the menu's Quit) | `ConfirmQuit` (TF2's menu keeps its own) | `quit` |
| console (`GameConsole004`) | `ConsoleScreen` | already wired to the console's text |
| options: keyboard, mouse | `KeyBindings`, `MouseOptions` | done |
| options: audio, multiplayer | forms over `UiForm` and convars | none |
| options: video | form | two new forwarder rows for the video mode list (`IMaterialSystem::GetModeCount`/`GetModeInfo`), then `mat_setvideomode` |
| options: voice | form (enable, volume, boost) | the mic test needs voice-tweak rows (D4) |
| create server | `CreateServer`, shared with `plan_hl2mp_ai.md` §3.5 | a map list (file find rows) |
| player list / mute | `PlayerList` | the client's voice-ban calls (today `CGameClientExports`, `shell_clientdll_gameclientexports.cpp`) as rows |
| server browser | `ServerBrowser` (exists) | `ISteamMatchmakingServers` over the flat API; with no Steam (`-hushsteam`) it shows its empty state |
| new game, load/save, bonus maps, achievements, commentary, credits, Xbox and session methods | none | ruled inert: one row in `docs/absence-client.md`, "GameUI single-player surface" |

## 3. Phases

Each phase is a series of commits, every commit building and passing its
touched tests. Live checks use the debug API and TGA screenshots (`jpeg`
can miss vgui layering).

**G0: census (no behaviour change).** Re-take C0's sequence at the current
main with `tools/probes/gameui-stub` for both the test mod and tf2. Add:
- the commands and convars the stock module registers (the console list
  with the stub in place against a list with a stub that registers
  nothing);
- the localisation files it loads;
- the background material it binds;
- tf2's extra calls (TF2's client paths);
- when the engine resolves the stock module's `CreateInterface` relative
  to client.so's load and to the first call client.so receives
  (`CreateInterface("VClient017")`), and through which import of which
  module (expected: `engine.so`'s `dlsym` on Linux, its `GetProcAddress` on
  Windows, measured in CI); and what the stock module does at load alone
  (its global constructors), with its factory never called. **Checkpoint:**
  if the factory is resolved before client.so can install the hook, the
  plan stops for the user's ruling.

Output: `docs/gameui-census.md`, which every later phase cites.

**G1: the screens every session meets, on main, under the stock module.**
Menu background, loading screen with progress and status, message
dialog, confirm quit. Each is a menu screen with samples, goldens and
facts, opened by `cs_ui_open` for a live look; nothing drives them from
the engine yet. Vendor `IGameConsole.h`. Write `GameUiHost` against
in-process fakes of the two interfaces, so its routing (activation, hide,
loading, disconnect and failure, console) is held by facts before
anything native calls it.

**G2: options.** Audio, video (new rows), voice, multiplayer beside the two
done tabs, one options window with tabs.

**G3: create server, player list, server browser.** `CreateServer` lands
once, with the AI plan's bot rows if that plan has reached them.
`PlayerList` over the voice-ban rows. The browser's source over
`ISteamMatchmakingServers` (internet, LAN, favourites, history), refresh
and join. `gamemenucommand`'s routing table is complete at the end of G3:
every name G0's census found has a screen or a ruling.

**G4: the switch-over, one series.**
- The engine gets our factory through the client.so hook (D2): at the
  first call client.so receives, it patches `engine.so`'s import of the
  symbol lookup (`dlsym` in its GOT on Linux, `GetProcAddress` in its
  import table on Windows; the page made writable for the store and
  restored after). The replacement passes every lookup through untouched
  except one: `CreateInterface` resolving to an address inside the stock
  `GameUI.so` (`dladdr`), for which it answers `SSGameUiFactory`, and
  restores the original import, so the patch lives for one lookup. Native
  carries out the swap; it matches by address, not by a decision. A gtest
  patches a test library's import with the same helper and checks the
  swap, the pass-through and the restore.
- `SSGameUI`/`SSGameConsole`, the factory, the managed rows, the early-call
  replay; `GameUiHost` bound to them; managed registers `gamemenucommand`
  and the other commands G0 found.
- Deleted in the same series: the stock-module path (`gameui_edge.h`'s
  visibility watch and its client call, the `SetMainMenuOverride` hand in
  `shell_hud.cpp`, and their facts), and nothing that could load the stock
  `GameUI.so` remains.
- `docs/absence-client.md` gains the single-player surface's ruling rows.
- Facts: the hook helper (gtest: swap, pass-through, restore); the replay order (gtest); the C0 sequence replayed
  against our module's route log, equal. Live steps: `menu-main`,
  `menu-pause`, `loading-ui`, `console-ui`, `disconnect-dialog` (a refused
  connect), the options window, create server, the browser; `/proc/<pid>/maps`
  holds no stock `GameUI.so` and no `ServerBrowser.so`.

## 4. Measurements and gates

| what | instrument | holds |
|---|---|---|
| every `IGameUI`/`IGameConsole` call | `/client/{slot}/gameui` (last 64 calls, method, arguments, replayed or live, handled or ruled inert) | G4 onward |
| a call no screen answers and no ruling names | the same route's count of unruled calls | zero from G4 |
| no new export | K1 (`nm -D` plus the `CreateInterface` registry): client.so's and libss_fwd_client's sets unchanged; the factory is reached only through the hook | G4 |
| stock never called | the `/gameui` route shows every `GameUI011`/`GameConsole004` call landing in managed from the first; `/proc/<pid>/maps` holds no `ServerBrowser.so`; the stock module's commands (G0's census) are absent unless managed registered them | G4 live, every live step after |
| loading paint | paints counted inside the load while `UpdateProgressBar` answers true | G4 |

## 5. Risks

- **Calls before the CLR.** Settled by C0. The replay must also survive a
  client that fails Init: replay nothing and let `Shutdown` arrive.
- **Painting inside the load loop.** A managed exception there is a hang
  or a crash mid-load; the loading screen draws from data it already
  holds.
- **Things the stock module did implicitly** (commands, convars,
  localisation, the background, `gameui_*` escape handling). G0's census
  turns each into a row, and anything missed shows up as an unruled call
  or a missing command.
- **Patching another module's import.** The hook writes one entry of
  `engine.so`'s import table: full RELRO makes that page read-only (made
  writable for the store, then restored), and a different lookup path in a
  future engine build would bypass it. G0 measures the path; a bypass fails
  loudly at the first call (the stock module answering, `ServerBrowser.so`
  mapped, the `/gameui` route empty).
- **The stock module stays mapped.** Its global constructors run at load
  (G0 records what they touch); nothing calls into it after. A few MB of
  memory.
- **No Steam.** Under `-hushsteam` the browser has nothing; LAN discovery
  also goes through Steam's matchmaking API.

## 6. Decisions to rule

- **D2: how the engine gets ours.** RULED 2026-11-01 (§7): the factory
  hook in client.so (the stock module loads, its factory is swapped). Not
  taken: a hook on the library load, the client engine view, an
  `LD_PRELOAD` redirect. "Load ours first" alone does not work: the engine opens GameUI
  by full path, and a different file already loaded does not satisfy it
  (C0: a stub in the mod's bin never loaded).
- **D4: options parity.** RULED 2026-11-01 (§7). Was recommended: the voice tab's mic test and the
  multiplayer tab's spray import wait (ruled divergences) until someone
  needs them; every other control is ported.
- **D5: the client-novalve plan.** RULED 2026-11-01 (§7); done: on approval, mark
  `plan_client_novalve.md` C4 and §3.2a "moved to plan_gameui_managed.md";
  this plan supersedes the strangler.

## 7. Decisions (rulings, dated)

- 2026-11-01: split from the client-novalve plan; wholesale replacement,
  every call to managed, no forwarding to the stock module (user).
- 2026-11-01: no stock fallback (user: "delete stock fallback"). No per-mod
  switch and no escape hatch; the screens land first under the stock
  module and the module lands with the stock path's deletion (G4). Retires
  the old D1 (the switch) and D3 (the escape hatch).
- 2026-11-01: D4 yes (user): every options control is ported except the
  voice tab's mic test and the multiplayer tab's spray import, which wait
  as ruled divergences until someone needs them.
- 2026-11-01: D5 yes (user): `plan_client_novalve.md` §3.2a and C4 are
  marked moved here; this plan supersedes the strangler.
- 2026-11-01: D2, the hook in client.so (user: "do the hook in client.so"):
  a one-shot redirect of the filesystem module's library-load import, no
  engine view; G0's load-order measurement is a checkpoint for it.
- 2026-11-01: D2 is the **factory** hook (user: "factory hook"), replacing
  the same day's load hook: `engine.so`'s symbol lookup is swapped for the
  stock module's `CreateInterface`; the stock module is mapped but never
  called; no extra binary.
- 2026-11-01: plan approved; start (user: "approved, start").

## 8. Outcome (executed 2026-11-01)

All four phases landed (G0 census; G1 screens and host; G2 options; G3
create server, player list, browser, the game menu's table; G4 the
switch-over), plus the user's mid-plan addition, a taskbar for every mod
(open windows, click-snap to a screen half or quarter, settings and clock on
the right). `make test` passed all legs at the end of each phase.

Where the build differs from the plan:
- `SSGameUI`/`SSGameConsole` and the factory live in client.so
  (`shell_gameui.cpp`), not libss_fwd_client: the engine takes them through
  the factory before the CLR exists (the forwarder library's objects are
  built by managed), and the swap needs libc, which the forwarder library
  links none of.
- The swap goes in at the engine's first call into the client,
  `CClientDLLSharedAppSystems::Count` (`CModAppSystemGroup::Create`, before
  `Host_Init` loads the GameUI module).
- The menu root follows the engine's GameUI panel while the GameUI is up
  (the engine hides the client root then), polled once a frame; the
  override panel is gone.
- The stock module's commands are registered by managed: `gamemenucommand`,
  `gameui_show_dialog`/`gameui_hide_dialog` (by screen name), `condump`.

Verified live on Linux (tools/probes/g0-census plain, the test mod and TF2):
`/proc/<pid>/maps` holds GameUI.so and no ServerBrowser.so; the menu and
background at launch; options, create server, the browser (also through
`gamemenucommand OpenServerBrowser`); a map load and the menu after
disconnect. Not done: the Windows arm (the `GetProcAddress` swap is
written; CI checks it at push).

Added after (2026-11-01, main eeb6102d3):
- `/client/{slot}/gameui` (§4): every call counted by method, the last 64
  kept with arguments and outcome (handled, inert by the host's ruling,
  unhosted), and `unruled`. `RunFrame` and `IsConsoleVisible` come every
  frame, so they are counted, not kept, and a run of progress updates is one
  entry. Live (dm_lockdown, the test mod, `tools/probes/g0-census`, which
  saves it as gameui.json): 3758 calls, unruled 0, the early calls replayed
  first.
- The loading paint (§4): the route's `lastLoad`. With `progress_enable`
  before `map` (what the create-server page does): the dialog asked for,
  247 updates, 24 answered yes, 72 menu frames drawn inside the load. A bare
  `map` asks for no dialog.
- The disconnect dialog live: `G0_CMD="connect 127.0.0.1:27999"
  G0_CMD_WAIT=40` (no screenshot lands during the retries): "Disconnected",
  "Connection failed after 4 retries.", Close, over the menu.
- The taskbar shows exactly while the main or pause menu is up, never in
  play; TF2's footer row is on it; a title bar dragged to a screen edge
  snaps.
