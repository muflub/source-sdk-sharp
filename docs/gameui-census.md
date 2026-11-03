# GameUI census (plan_gameui_managed.md G0)

Measured 2026-11-01 at `e706cf611` on the Source SDK Base 2013 Multiplayer
install (appid 243750, Linux), debug build, with `tools/probes/g0-census`.
Every session: the menu, a map (`dm_lockdown` for the test mod, `ctf_2fort`
for tf2), disconnect, quit. Re-take with:

```
tools/probes/g0-census OUT trace
G0_MAP=dm_lockdown tools/probes/g0-census OUT forward
G0_MAP=dm_lockdown tools/probes/g0-census OUT inert
G0_MAP=dm_lockdown tools/probes/g0-census OUT loaded
G0_MAP=ctf_2fort   tools/probes/g0-census OUT forward tf2
```

## 1. The load-order checkpoint: passed (Linux)

The `dlopen`/`dlsym` tracer (`tools/probes/dl-trace`, `LD_PRELOAD` for one
session) logged, on one launch of the test mod:

| t (s, monotonic) | event | by |
|---|---|---|
| 10036.642962 | `dlopen` client.so (the mod's `bin/linux64`) | filesystem_stdio.so |
| 10036.642973 | `dlsym(client.so, "CreateInterface")` | engine.so |
| 10038.144359 | `dlopen` GameUI.so (the install's `bin/linux64`) | filesystem_stdio.so |
| 10038.144383 | `dlsym(GameUI.so, "CreateInterface")` | **engine.so** |

The engine resolves the stock module's factory **1.50 s after** client.so is
loaded and its factory resolved, through `engine.so`'s own `dlsym` import
(app systems' factories are resolved by `launcher.so`, not engine.so).
`engine.so` imports `dlsym` through a PLT slot (`R_X86_64_JUMP_SLOT` at
0x9fabe0) outside its `GNU_RELRO` range (0x8daa58 to 0x9fa000), with no
`BIND_NOW`: the slot is writable without `mprotect`. Windows (`GetProcAddress`
in `engine.dll`) is measured in CI with G4.

## 2. A GameUI that does nothing: the game survives

| run | stock module | launch | map | disconnect | quit |
|---|---|---|---|---|---|
| forward | loaded and called (the stub forwards) | ok | up | ok | exit 0 |
| inert | never loaded; the stub answers every call with nothing | ok | up | ok | exit 0 |
| loaded | loaded (its global constructors ran), never called | ok | up | ok | exit 0 |
| tf2 forward | loaded and called | ok | `ctf_2fort` up | ok | exit 0 |

With the stock module loaded but never called (the factory hook's end state)
the command and convar lists are **identical** to the inert run's: its global
constructors register nothing. The screen at the menu is black: nobody paints
the background or a menu.

## 3. The engine's calls

The `IGameUI`/`IGameConsole` sequence is C0's (plan_client_novalve.md §8,
"C0 probe results" item 3), unchanged at this tip, and the same in all four
runs except:

- `ServerBrowser.so`'s factory lookups through GameUI's factory
  (`RunGameEngine005` answered; `VProcessUtils001`, `NetworkSystemVersion001`,
  `VP4002`, `VMDLLIB001`, `VDmeMakeFileUtils001`, `VTEX_003` NULL): only when
  the stock module runs, since it loads `ServerBrowser.so`.
- one `OnGameUIHidden` fewer at map load when the stock module does not run
  (forward: two, inert: one). The second is the stock module hiding itself;
  managed must not depend on it.
- tf2 makes no call the test mod does not: the sets of distinct calls are
  equal.

client.so also asks the GameUI factory for `GameUI011` itself (today's
`SetMainMenuOverride` hand, deleted in G4).

## 4. What the stock modules register

From `tools/debug-api commands|convars native` at the menu, forward against
inert (`declaredBy` is the dev API's attribution: the GameUI module's show as
`client.dll`, `ServerBrowser.so`'s as `engine`).

GameUI.so, 15 commands and 2 convars:

| name | kind | keep? |
|---|---|---|
| `gamemenucommand` | command | **yes**: managed routes its names to screens (the TF2 menu sends it) |
| `gameui_show_dialog`, `gameui_hide_dialog` | command | yes: by screen name |
| `condump` | command | yes: the managed console writes `condumpNNN.txt` |
| `refresh_options_dialog`, `refresh_gameui_dialogs_modeset` | command | no: managed screens re-lay out on a mode change themselves |
| `vgui_message_dialog_modal` | convar (archive) | yes: `MessageDialog` reads it |
| `bench_showstatsdialog` (cheat), `commentary_testfirstrun`, `loadcommentary`, `commentary_firstrun` (convar), `sv_bonus_map_challenge_update`, `sv_bonus_map_complete`, `sv_bonus_map_unlock` (cheats), `mm_add_item`, `mm_add_player`, `vmodule_resetparents` | | no: the single-player surface and stock debug, ruled inert |

ServerBrowser.so (loaded only by GameUI), 1 command and 6 convars:
`sb_destroy_for_resize`; `sb_filter_incompatible_versions`,
`sb_firstopentime`, `sb_mod_suggested_maxplayers`, `sb_numtimesopened`,
`sb_quick_list_bit_field`, `sb_showblacklists`. The managed browser (G3)
registers the ones its filters read.

`gameui_activate`, `gameui_hide`, `gameui_allowescape` and
`gameui_preventescape` are in both runs: the engine's. The core menu's
Resume (`gameui_hide`) keeps working.

## 5. Localisation and art

- `resource/gameui_<language>.txt` (the module names
  `Resource/gameui_%language%.txt`): a loose file in `hl2/resource/` of both
  installs, on the GAME paths through the hl2 mount. Nothing else loads it;
  the managed localiser must (G1), or every `#GameUI_*` token (disconnect
  reasons, options labels) shows raw.
- The menu background: the material `console/<name>`, `<name>` from
  `IVEngineClient::GetMainMenuBackgroundName`, plus `_widescreen` when the
  screen's aspect ratio is at least 1.6, drawn full screen and opaque while no
  level is up. `GetMainMenuBackgroundName` is not on the managed ABI yet: G1
  adds the row.
- The loading icon: `console/startup_loading`.
