# TF2 port — Stage 3: client (`src/tf2/client`, `src/tf2/ui`)

Status: DRAFT, uncommitted. Part of `docs/plans/plan_tf2_port.md` (§0 D1, §2,
§6). Requires Stage 0.9 (mod UI assemblies reach the game) and the Stage 2
carriers (`tf_player_data`, `tf_objective_resource`, `team_round_timer`,
building/flag netstates). Stock paths under `../source-sdk-2013/src/game/`.

Correction to the first draft: the HUD is not "E". `client/tf/tf_hud_*.cpp`
is **73 files, 46,701 lines**; `client/tf/vgui/` another 102 cpp / 64k. We
port **layout and behaviour** into the C# toolkit (D1), one panel per
phase-row, and we ship a milestone subset. Read `docs/ui/authoring.md` and
the `ui-screen` skill before any panel; the exemplars are `Hud`, `Scoreboard`,
`TeamMenu` (`src/client/ui/screens/`) and Descent's `DescentHud`/`DepthPicker`
(`src/tf2_descent/ui/screens/`).

Lanes: 3.1 (HUD) and 3.2 (menus) share the UI project — one lane, or two with
file ownership by screen. 3.3 (mirrors) must land before 3.1's data adapters
read anything. 3.4–3.6 are independent of each other.

---

## Phase 3.0 — Project wiring

**Make**
- `src/tf2/ui/TF2.Client.Ui.csproj`: copy `src/tf2_descent/ui/TF2.Descent.Client.Ui.csproj`
  (references: the UI analyzers, `SourceSharp.Client.Interface`,
  `SourceSharp.Client.Ui.Toolkit` — nothing else; `UiAssemblyRulesTests`
  enforces it). Add it to `SourceSharp.slnx`, the mod-gated analyzer list,
  `tools/ui-preview-assemblies.txt` (`TF2.Client.Ui=game/mod_tf2`), and
  `game/mods.yaml` `tf2.assemblies.client_ui: [TF2.Client.Ui]` (Stage 0.9).
  `tf2_descent` lists `[TF2.Client.Ui, TF2.Descent.Client.Ui]` so Descent
  keeps the TF2 HUD underneath its own.
- `src/tf2/ui/TfUiSources.cs`: the data interfaces TF2 screens read (one per
  concern, the `UiSources.cs` pattern): `ITfLocalPlayer` (class, max health,
  conditions, cloak/charge meters, disguise), `ITfObjectives` (control
  points, cart progress, flags), `ITfRoundTimer`, `ITfTeams` (scores, counts),
  `ITfKillFeed` (if the core `IKillFeedSource` lacks custom-kill icons),
  `ITfBuildings` (owner's buildings), `ITfTargetId`, `ITfClassMenuActions`,
  `ITfLoadout`.
- `src/tf2/client/Ui/*.cs`: the adapters, `[SideService(Side.Client, As=[typeof(ITf…)])]`,
  reading the Stage 3.3 mirrors; refreshed once per frame like
  `LocalPlayerUi.Refresh` (`src/client/managed/Ui/LocalPlayerUi.cs:21`).
- `game/mod_tf2/resource/tf2_sharp_english.txt` (UTF-16) for our own tokens;
  stock strings come from `resource/tf_english.txt` in the VPK through the
  Stage 0.9 chain (`mods.yaml tf2.localisation: [resource/tf_english.txt]`).

**Facts**: the assembly-rules fact passes for the new project; every
`ITf…Source` is read by at least one panel (`UiSourceUseTests`); a token
present only in `tf_english.txt` (e.g. `#TF_Scout`) resolves in the preview
chain (fact over `UiLanguageFile.Chain`).

**Gate**: `make ui-preview PNG=1` renders an empty TF2 HUD sample;
`make ui-lint` 0 findings; `make managed-test FILTER='Ui'` green.

## Phase 3.1 — HUD panels (milestone subset)

Each row: one `UiPanel` file in `src/tf2/ui/parts/`, composed into
`src/tf2/ui/screens/TfHud.cs : UiScreen` (`Layer => UiLayerKind.Hud`,
`Order` after the core `Hud` so it draws over it; the core `Hud`'s vitals/
ammo panels are hidden when a mod HUD declares the `hud.vitals`/`hud.ammo`
slots — add those slot names to the core `Hud` in Stage 0.9 if missing).
Port the stock panel's **layout numbers** (from `resource/ui/Hud*.res` in
the VPK — extract with `tools/vpk-extract --list 'resource/ui/hud*'`) and
**behaviour** (the `.cpp`); never the vgui class.

| Panel | Stock | Behaviour to port |
|---|---|---|
| Health + overheal | `tf_hud_playerstatus.cpp` (health cross, overheal pulse, low-health blink), `HudPlayerHealth.res` | overheal cap 150 %, low-health threshold and blink rate (cite) |
| Ammo | `tf_hud_ammostatus.cpp`, `HudAmmoWeapons.res` | clip / reserve / no-clip layouts; metal for engineer |
| Class icon + stats | `tf_hud_playerstatus.cpp` class image | class image per team |
| Round timer / setup | `tf_hud_objectivestatus.cpp` + `tf_hud_timer`-era code (`hud_timer`?) — grep `CTFHudTimeStatus` | reads `team_round_timer` netstate; setup label; overtime; waiting-for-players |
| Objective: CTF | `tf_hud_flagstatus.cpp` | flag home/carried/dropped per team, return timer |
| Objective: Payload | `tf_hud_escort.cpp` | cart progress bar, checkpoints, capper count, recede alarm |
| Objective: CP (needed by PL maps) | `tf_hud_controlpointicons.cpp`, `hud_controlpointicons` | point icons, owner colour, cap progress |
| Kill feed | core `KillFeedPanel` (`src/client/ui/screens/KillFeed.cs:21`) + `tf_hud_deathnotice.cpp` | TF weapon → icon map (`KillIcons`), crit/headshot/backstab glyphs |
| Damage indicator / damage numbers | `tf_hud_damageindicator.cpp`, `hud_damageaccount_panel` | directional flash; floating damage numbers (`hud_combattext`) |
| Target ID | `tf_hud_target_id.cpp` | name/health/class of the aimed teammate, disguised spies show the disguise |
| Crosshair | core `CrosshairPanel` | per-weapon crosshair file from the weapon script (`crosshair` key) |
| Medic charge / uber | `tf_hud_mediccharge.cpp` | charge %, heal target name, "ready" pulse |
| Cloak meter / disguise status | `tf_hud_spy_disguise`/`tf_hud_cloak` (grep) | cloak %, disguise class/team panel |
| Building status | `tf_hud_building_status.cpp` | owner's buildings: health, level, upgrade progress, sapped |
| Scoreboard | `tf_clientscoreboard.cpp` (2,205) — simplified first pass over the core `Scoreboard` + `TeamTable` | per-team columns (class, score, ping), round scores; the core `IScoreboardSource` already has teams |
| Spectator | core spectator panels | class/team of the observed player via `ITfLocalPlayer` |

**Make**: one commit per row; each panel gets a `[UiSample]`/`[UiGallery]`
state set (`src/tf2/ui/screens/TfHud.Samples.cs`, fakes in
`src/tf2/ui/Samples/TfFakeSources.cs`) covering full/low/overheal,
clip/no-clip, setup/active/overtime, flag states, cart at 0/50/100 %.

**Facts**: per panel a behaviour fact (binds to the source, updates on
change — before ≠ after) and golden rows in `UiGoldenTests.Exemplars`
(`src/test/client/Ui/UiGoldenTests.cs`: `{ "tfhud", "<slug>" }` at 1920×1080
and 2560×1080 + the 1280×720 PNG; write with
`UI_GOLDEN_WRITE=1 make managed-test FILTER='FullyQualifiedName~UiGoldenTests'`
and review the diff). `UiAllocationTests` (no allocation in `Describe`).

**Gate** per panel: `make ui-preview PNG=1` reviewed at every size,
`make ui-lint` 0, goldens committed, `make managed-test FILTER='Ui'`; live:
screenshot through `tools/debug-api` (jpeg) of the state the panel shows.

## Phase 3.2 — Team menu, class menu, loadout screen, first-join flow

**Stock**: `client/tf/vgui/tf_classmenu.cpp` (580), `tf_teammenu.cpp`,
`class_loadout_panel.cpp`, `character_info_panel.cpp` (743) — offline
variant (D2: no store/trading). Flow: join → MOTD → team menu → class menu
→ spawn; `changeclass`/`changeteam` reopen them.

**Make** (`src/tf2/ui/screens/`): `TfTeamMenu` (red/blu/auto/spectate with
counts; replaces the HL2DM `TeamMenuUi` when TF2 is loaded — the core
`UiHost.TakePanel("team")` (`src/client/ui/host/UiRuntime.cs:22,236`) must
route to the mod's screen when one declares `Panel => "team"`; add that to
Stage 0.9 if absent), `TfClassMenu` (nine classes with portraits from the
VPK — `ui.Texture` exists: `src/client/ui/toolkit` leaf, commit `cf1d561d`),
`TfLoadoutScreen` (per class, slots → items from `ItemSchema` + the local
inventory via a `TF2:loadout` message pair). Actions go through
`ITfClassMenuActions` → client console commands `jointeam`, `joinclass`,
`tf2_equip` (Stage 0.6 hooks on the server).

**Facts**: class menu opens on first spawn and on `changeclass` (recorded
`UiLayer.Open`), choosing sends `joinclass <name>` (recorded command),
loadout change persists across reconnect (server fact in 1.6 + a client fact
that the screen shows the server's reply); goldens for each screen at both
sizes.

**Gate**: `make ui-lint` 0; live: join `ctf_2fort` → team menu → class menu
→ spawn as the chosen class (`/entity` class matches); `changeclass` while
in the respawn room swaps instantly.

## Phase 3.3 — Client mirrors

**Stock**: `client/tf/c_tf_player.cpp` (model/disguise/invis rendering,
condition effects), `c_tf_projectile_*`, `c_baseobject.cpp` family,
`c_tf_objective_resource.cpp`, `c_team_train_watcher.cpp`.

**Make** (`src/tf2/client/Entities/`): `[ClientStandaloneEntity("tf_player_data")]`
`ClientTfPlayerData` (`[NetState] TfPlayerNetState`; on change: publish to
`ITfLocalPlayer`/`ITfTargetId` adapters, drive condition visuals in 3.6,
disguise → model/name swap on the owning player, cloak → render alpha via
`IEngineEntityRender`), `ClientTfObjectiveResource`, `ClientTeamRoundTimer`,
`ClientTfTeamResource`, `ClientItemTeamFlag` (carried → attach to carrier),
`ClientBaseObject` family (model per level, build progress animation),
`ClientTfWearable` (bone-merge to owner — `ClientBoneMerge` exists),
projectile mirrors (`ClientTfRocket` etc.: trails via 3.6). Pattern:
`src/client/managed/Standalone/ClientStandaloneEntity.cs:36` + `ClientTestBarrel.cs`.

**Facts**: each mirror decodes the fields its adapter reads (write a blob
with the generator's layout, assert the adapter's value — before ≠ after);
disguise change on a non-local player calls `SetModel` with the disguise
class model (RecordingEngine); cloak alpha follows the meter.

**Gate**: `make tf2-test FILTER='ClientTf'`; live full join: write a class
change on the server, read it on the client (`tools/debug-api` nettable
diff) — the only valid proof of a datatable change.

## Phase 3.4 — Prediction of TF weapons, conditions and movement

Most of this is free if Stages 0–2 held the rule "shared behaviours decide
from predicted state": `ClientArsenal` already runs `WeaponLogic` with the
same `IWeaponBehaviour`s (`src/client/managed/ClientArsenal.cs:30`,
`ClientPlayerPrediction.cs:361,491`), and `TfMovementRules` reads
`tf_player_data`'s predicted fields. This phase **proves** it and fills the
client arms.

**Make**: `ClientWeaponArms` (`ClientArsenal.cs:426-433`, empty today) gets
the TF2 client-side effects for predicted fire (muzzle flash, shell eject,
sound) via the `IWeaponArms` arms; crit **decision stays server-authoritative**
for damage, but the roll is predicted for the sound/trail (stock does the
same with the shared seed — cite `CalcIsAttackCriticalHelper`'s
`IsFirstTimePredicted` arms). Predicted projectile stand-ins: out of scope
until the core's `PredictableId` work (`docs/plans/plan_client_novalve.md:960-972`,
memory `predicted-projectiles-followup`) lands — record as a `later` row.

**Facts** (the `ClientPlayerPredictionTests` `Bench` pattern,
`src/test/client/ClientPlayerPredictionTests.cs:149`): a scattergun shot
consumes one clip on the client and the acknowledged server state agrees
(zero prediction error in the ring); heavy spin-up (`TF_COND_AIMING`) lowers
predicted max speed without a correction when the server state arrives;
scout air dash predicted and acknowledged; a source-parity fact in the
`MoveTypePredictionParityTests` style that both bindings feed the same
`tf_player_data` fields into `TfMoveInputs`.

**Gate**: live with `net_fakelag 100`: run/strafe/jump/fire as scout and
heavy — `tools/debug-api` prediction report shows no error growth
(quote the counts before and after a 60 s session, same build).

## Phase 3.5 — TF2 animation layer over the core `MultiplayerAnimState` (U5: full)

**Requires** Stage 0.10 (the generic `MultiplayerAnimState` with its
`IAnimStateLayer` chain, shared, both sides). No Tier-A stop-gap: the user
ruled full animation.

**Stock**: `shared/tf/tf_playeranimstate.cpp` (1,551) — `CTFPlayerAnimState`:
class-specific sequence selection by weapon slot (`stand_<SLOT>`,
`run_<SLOT>`, `crouch_<SLOT>`, `crouchwalk_<SLOT>`, `jump_start/float/land`,
`swim_<SLOT>`; `GetWeaponSlotSuffix`-style helpers — cite), gesture
overrides per class (`ATTACK_STAND_<SLOT>`, `RELOAD_*`, `FLINCH_*`,
`gesture_melee_*`), taunt layer (`DoAnimationEvent(PLAYERANIMEVENT_TAUNT)`,
`m_bTaunting`), disguise/stealth hooks, the `TF_DLL` arms in
`multiplayer_animstate.cpp` (7 sites); viewmodel sequences per weapon script
(`viewmodel`, `anim_prefix`); `tf_player_shared.cpp OnEmitFootstepSound`
L12143 for step sounds per class.

**Make** (`src/tf2/shared/Anim/`, both sides): `TfAnimLayer : IAnimStateLayer`
registered into the core chain — sequence names per class × slot from the
model's real sequence list (`LookupSequence` arm; a fact reads the names from
fixture `.mdl` headers via `StudioHdr`), gesture overrides, taunt layer,
cloak/disguise animation hooks (disguised spy animates as the disguise
class: read `tf_player_data`); itself extensible by an **`ITfAnimHook`**
chain (D10: Descent's custom taunts/emotes). Viewmodel: `TfViewModelAnims`
mapping `WeaponAnim` → the weapon script's sequences.

**Facts**: for two classes and three slots the main sequence equals the
stock name (table fact against fixture model headers); fire → `ATTACK_STAND_<SLOT>`
gesture once; reload gesture interrupted by fire per stock; taunt sets the
taunt layer and blocks movement sequences until done; disguised spy picks
the disguise class's sequences; `ITfAnimHook` fake overrides a gesture.

**Gate**: live: run/strafe (8-way blend visible)/crouch/jump/swim/reload/
taunt as three classes — screenshots (jpeg via `tools/debug-api`) of each;
no T-pose (a T-pose is a wrong sequence name — fail the gate, never waive).
Prediction report flat (the local player's animation is predicted).

## Phase 3.6 — Effects and temp entities

**Stock**: `server/tf/te_tfblood.cpp`, client particle names in
`c_tf_player.cpp`/`c_tf_weapon_*` (`critgun_weaponmodel_*`, `crit_text`,
`rockettrail`, `stickybombtrail_*`, `flamethrower` muzzle/`flamethrower_*`
particle systems, `teleported_*`, `dispenser_heal_*`, `medicgun_beam_*`,
`player_intel_papertrail`, building sparks/destruction); `tf_fx_shared.cpp`
(tracers); `clientmode_tf.cpp` (screen effects: invuln overlay).

**Make** (`src/tf2/client/Effects/`): `IClientEffectHandler`s as
`[SideService(Side.Client, As=[typeof(IClientEffectHandler)])]` (Stage 0.9
discovery) for the TF2 named effects; particle systems dispatched through
`IFxRender.DispatchParticleEffect` (`src/client/interop/FxRender.cs:96`)
with the stock system names (they live in TF2's `particles/*.pcf`, mounted);
`te_tfblood` as a `[ClientTempEnt("CTETFBlood")]`; crit trail and sticky
trail on the projectile mirrors; medigun beam (`IManagedParticleSource` or a
beam arm — check what the core offers for beams first); invuln/uber
material overlay via the C5 material proxies (landed `375193a4c`).

**Facts**: each effect kind registered once and dispatched for its name
(`ClientEffectDispatch.cs:27` managed-first order); a crit shot event spawns
the crit particle on the right entity; the blood tempent decodes its stock
fields; lifetime of trails bounded (no steady-state growth over 1,000 frames).

**Gate**: live: crit rocket trail, burning player flames, sentry build
sparks, uber overlay — one screenshot each.

---

## Stage 3 exit

A human joins `ctf_2fort` and `pl_badwater` under `-mod tf2`: team and class
menus, TF2 HUD for every milestone panel, correct ammo/health/overheal/
timer/objective displays, kill feed with TF icons, animations without
T-poses (full `MultiplayerAnimState` + TF2 layer), effects for crits/flames/uber/builds; prediction report
flat under 100 ms fake lag. `make ui-lint` 0, goldens committed for every
TF2 screen, `make test` green. Record the screenshot set and prediction
counts in `docs/plans/tf2/stage3_results.md`.
