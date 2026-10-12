# TF2 port — Stage 2: server core (`src/tf2/server`, with shared state in `src/tf2/shared`)

Status: DRAFT, uncommitted. Part of `docs/plans/plan_tf2_port.md` (§0 rulings
D6, D7, D9; §2; §6). Requires Stages 0 and 1. Stock paths are under
`../source-sdk-2013/src/game/`; line numbers measured 2026-10-11 at the fork's
pinned sha — re-grep before reading. Sizes in parentheses are line counts.

Two corrections to the first draft that shape this stage:
1. The team-play round machinery is **not** in managed form and **not** in
   `sdk-links`: `shared/teamplayroundbased_gamerules.cpp` (3,781),
   `shared/teamplay_round_timer.cpp` (1,543), `server/team_control_point.cpp`
   (1,096), `team_control_point_master.cpp` (1,341), `team_control_point_round.cpp`
   (414), `server/trigger_area_capture.cpp` (1,247), `server/team_train_watcher.cpp`
   (1,572), `server/team_objectiveresource.cpp` (573) — 13,571 lines to port,
   and Payload needs the control-point stack for its checkpoints. They are
   phases 2.1 and 2.4 here, not "extend existing GameRules".
2. Flag logic is `shared/tf/entity_capture_flag.cpp` (3,064), not `tf_mapitems.cpp`.

What already exists in managed and is reused, not ported: `func_tracktrain`
(`src/server/managed/Entities/Brushes/FuncTrackTrain.cs`), `path_track`
(`Entities/Markers/PathTrack.cs`), `trigger_hurt`, `trigger_multiple`,
`trigger_push`, `logic_relay`, `game_text`, `env_explosion`, `prop_dynamic`,
`func_brush`, `player_manager`/`PlayerResource` (`Entities/PlayerResource.cs`),
`FlyingProjectile`, `Grenade`, `Explosions`, `PickupItem`.

Lanes: 2.1+2.2 (rules), 2.3 (player), 2.4 (map entities) can start in
parallel after Stage 1; 2.5/2.6 need 2.1–2.4; 2.7–2.12 need 2.3 and are
independent of each other (file ownership by directory).

---

## Phase 2.1 — Team-round rules (`IRoundFlow` owner) + round timer + teams

**Stock**: `shared/teamplayroundbased_gamerules.h:47-78` `gamerules_roundstate_t`
(`GR_STATE_INIT, PREGAME, STARTGAME, PREROUND, RND_RUNNING, TEAM_WIN, RESTART, STALEMATE, GAME_OVER, BONUS, BETWEEN_RNDS`);
`teamplayroundbased_gamerules.cpp`: `Think` L687, `State_Enter/Think_*`
(`PREROUND` L1503/L1610, `RND_RUNNING` L1768, `TEAM_WIN` L1851/L1892,
`STALEMATE` L2008/L2169), `CheckWaitingForPlayers` L921, `CheckRestartRound`
L1030, `CheckTimeLimit` L1166, `CheckReadyRestart` L1695, `SetWinningTeam`
L2329, `SetStalemate` L2517, `RespawnPlayers` L2614, `RoundRespawn` L2801,
`CleanUpMap` L2891, `BalanceTeams` L3062, `ResetScores` L3235,
`WouldChangeUnbalanceTeams` L3589, `AreTeamsUnbalanced` L3653,
`GetNextRespawnWave` L561; `shared/teamplay_round_timer.cpp` (1,543,
`team_round_timer` entity: setup time, timer add/pause, announcer ticks);
`server/tf/tf_team.cpp` (482), `tf_autobalance.cpp` (618); TF arms in
`tf_gamerules.cpp`: `SetupOnRoundStart` L4838, `InternalHandleTeamWin`
L15884, `Think` L8038 (skim for the milestone), `GetTeamAssignmentOverride`
L21355, `CanChangeClass`/`CanChangeTeam` (grep).

**Make** (`src/tf2/server/Rules/`)
- `TeamRoundFlow : IRoundFlow` (`[SideService(Side.Server)]`, the single
  owner): the state machine above as an explicit enum + `Enter/Think` pairs;
  waiting-for-players; setup time; round timer expiry → stalemate or win per
  mode; respawn waves (`IRespawnPolicy` owner: `TfRespawnPolicy`, wave time
  per team `mp_respawnwavetime` + mode scale); win → `TEAM_WIN` → bonus time
  → `RESTART` → `ResetRound` (walks `IRoundResettable` entities, Stage 0.7).
  Mode-specific win checks are asked of the mode proxy (2.2), never
  hard-coded here.
- `TfTeams : IJoinTeamPolicy` (Stage 0.5): `jointeam auto/red/blue/spectate`,
  balance rule (`WouldChangeUnbalanceTeams`), `mp_teams_unbalance_limit`,
  autobalance (`tf_autobalance.cpp`: who moves and when — port the pick
  rule and the warning message; the `tf_autobalance_*` convars from 1.3).
  `tf_team` netvars (score, player count, role) go on a `TfTeamResource`
  carrier entity (2.4's resources) — never a static.
- `TeamRoundTimer` `[ScriptedEntity("team_round_timer")]` with a
  `[StandaloneNetState]` (`TeamRoundTimerNetState`: end time, paused, setup
  length, state) so the HUD timer (Stage 3) reads it. Inputs/outputs per
  the stock datadesc (`Pause/Resume/SetTime/AddTime/Restart/Enable/Disable`,
  outputs `On5MinRemain`…`OnFinished`, `OnSetupFinished`).
- Client prints and announcer: use the stock `TextMsg`/`TFTeamWin`-style
  user messages that already exist, or TF2-keyed ones (`TF2:<id>`, Stage 0
  rules), listed in `src/tf2/shared/TfMessages.cs`.

**Facts** (`src/tf2/tests/Rules/`)
- One fact per transition: `PREGAME → PREROUND` when enough players,
  `PREROUND → RND_RUNNING` after `mp_enableroundwaittime`/setup, win →
  `TEAM_WIN` with the stock bonus time, `RESTART → PREROUND`, timer expiry →
  `STALEMATE` when `mp_stalemate_enable`, else the mode's answer. Drive with a
  fake clock; assert the state **and** the side effect (respawn wave fired,
  `IRoundResettable.RoundReset` called on a recorded entity).
- Respawn wave: two players dying 1 s apart respawn on the same wave edge.
- Teams: auto-join picks the smaller team; a join that would unbalance past
  the limit is refused with the stock reason; autobalance moves exactly one
  player and announces it.
- `team_round_timer`: `AddTime` then `Pause` freezes the remaining time;
  `OnFinished` fires once.

**Gate**: `make tf2-test FILTER='TeamRound|TfTeams|RoundTimer'`; live
`make headless RUN_MOD_ID=tf2 MAP=ctf_2fort`: two headless clients join
(auto-balance puts them on opposite teams — assert via `/entity` `m_iTeamNum`),
`tf2_dev_win 2` forces a win, console shows `TEAM_WIN → RESTART → PREROUND`.
valve-header rows for every ported file.

## Phase 2.2 — TF2 modes registered into the core's `GameModes` (U1, D6)

The proxy, the switch-with-warmup, `NullMode`, `game_mode_switch` and
`sv_mode` are **core** (Stage 0.7 part A). TF2 adds modes and a selector.

**Make** (`src/tf2/server/Modes/`)
- `TfModeBase`: the TF2 concerns shared by every TF2 mode, composed over
  `TeamRoundFlow` (2.1) — team-round `IRoundFlow`, `TfRespawnPolicy`,
  `TfSpawnSelector` (2.4), objective-resource population — with an
  **`ITfModeHook` chain** (D10: `OnRoundStart`, `OnObjectiveEvent`,
  `ModifyWinCheck`, `ModifyRespawnWave`) so Descent can alter a TF2 mode
  without subclassing it.
- `CtfMode` (2.5) and `PayloadMode` (2.6) as `[SideService(Side.Server, As=[typeof(IGameMode)])]`
  with ids `"ctf"` / `"pl"`.
- `TfGameModeSelector : IGameModeSelector`: CTF if any `item_teamflag`, PL if
  any `team_train_watcher`, else `null` (stock `CTFGameRules::Activate`
  L4217 infers from `tf_logic_*` entities — cite and follow the detection
  order where those entities exist). Registered as a selector chain member;
  Descent may register one that runs earlier (assembly order).

**Facts**: with both TF2 modes registered, `GameModes.Active.Id` is `"ctf"` on
a fixture entity list containing a flag, `"pl"` with a watcher, `"null"` with
neither; an `ITfModeHook` fake sees `OnRoundStart` once per round and its
`ModifyWinCheck` veto is honoured (stimulus non-zero); structure fact: no
file outside `Modes/` names a concrete mode type (reflection over
`TF2.Server` ctor/field types).

**Gate**: `make tf2-test FILTER=Mode`; live: on `ctf_2fort`, `sv_mode null`
→ round restarts into deathmatch after the warmup, `sv_mode ctf` back.

## Phase 2.3 — The TF player: components in the core composite + a networked carrier entity (D9, U3, D10)

The core player entity `SsPlayer` (`src/server/managed/Entities/Players/SsPlayer.cs:33`)
is the engine's player and stays core; the core's `PlayerComposites`
(Stage 0.7 part B) owns the per-player component set. TF2 contributes one
`IPlayerComponentSource` (`TfPlayerComponents`) whose components are listed
below; each component exposes an interface (`ITfClassComponent`,
`ITfConditions`, `ITfHealth`, …) that other components — and Descent's own
components in the same composite — find through `PlayerComposites.Get<T>`.
TF2 state is networked by a per-player **carrier entity** (`tf_player_data`,
`ScriptedEntityKind.Effect`, owner = player), the way stock networks
`tf_player_resource`/`tf_objective_resource` beside the player. The client
mirrors it (Stage 3.3). Descent's extra per-player fields go on Descent's own
carrier, not on `tf_player_data`.

**Stock**: `server/tf/tf_player.cpp` (23,131; 412 methods) and
`shared/tf/tf_player_shared.cpp` (14,813; 397 methods). Do **not** read them
top to bottom. Per component, read the named methods only:

| Component | Stock methods (file:line, size) | Milestone scope |
|---|---|---|
| `ClassComponent` | `CTFPlayer::SetDesiredPlayerClass`, `HandleCommand_JoinClass`, `ChangeClass` arms in `tf_player.cpp` (grep); `GetClassEyeHeight` → `g_TFClassViewVectors` (`tf_gamerules.cpp:1326`); model from class data (1.2) | class choice, model, hull/eye height, max health, `TfClassTable` |
| `LoadoutComponent` | `CTFPlayer::GiveDefaultItems`, `ManageRegularWeapons`, `ManageBuilderWeapons` (grep `tf_player.cpp`); `ItemsMatch`, `GetLoadoutItem` | give TF weapons by class via `Arsenal.GiveWeapon` with TF types (1.9) from `LocalInventory` (1.6); wearables (2.13) |
| `ConditionsComponent` | `tf_player_shared.cpp` `ConditionThink` L3108 (197), `ConditionGameRulesThink` L2334 (759), `OnConditionAdded` L1571, `OnConditionRemoved` L1892 | tick `TfConditions` (1.7), apply milestone arms, write the five masks to the carrier |
| `HealthComponent` | `CTFPlayerShared::Heal/StopHealing/HealTimerThink`, overheal decay (`GetMaxBuffedHealth`, `tf_player_shared.cpp` grep `m_flHealthBuffTime`), `CTFPlayer::TakeHealth` | max health, overheal cap 150 %, decay rate (cite constants) |
| `CombatComponent` | `FireBullet` L10295 (353 — spread, damage per bullet, penetration), `ModifyDamageInfo` L10204, `RecordDamageEvent` L9574; `tf_gamerules.cpp ApplyOnDamageModifyRules` L5914 (1,190 — ramp-up/fall-off, crit/mini-crit, invuln), `ApplyOnDamageAliveModifyRules` L7104 (506), `FPlayerCanTakeDamage` L9808, `PlayerKilled` L11315, `GetKillingWeaponName` L12108, `DeathNotice` L12814 | `IDamageRule` owner (Stage 0.7): see 2.7 |
| `SpyComponent` | `Disguise` L8157, `DetermineDisguiseWeapon` L8394, `DetermineDisguiseWearables` L8577, `OnAddStealthed` L6952, `OnRemoveStealthed` L7048, `InvisibilityThink` L7977, `UpdateCloakMeter` L14165 | 2.12 |
| `TauntComponent` | `IsAllowedToTaunt` L676 (244), `OnRemoveTaunting` L4036 | post-milestone: `later` row |
| `BuildingsComponent` | `CanBuild` L11322, `TryToPickupBuilding` L12536, `CanPickupBuilding` L12440 | 2.10 |
| `Spawn/Death` | `CTFPlayerShared::Spawn` L977, `CTFPlayer::Spawn`, `Event_Killed`, ragdoll/gib (grep) | spawn per class, death → `IPlayerDeathHook` already core |

**Make** (`src/tf2/server/Player/`)
- `TfPlayerComponents : IPlayerComponentSource` (`[SideService]`): creates the
  components below for each player; no `TfPlayer` god object and no TF2-owned
  lifecycle — `PlayerComposites` attaches, ticks and detaches. A thin
  `TfPlayers` facade (`ITfClassComponent Class(EntityHandle)` etc.) is allowed
  for call-site brevity only if it holds no state.
- `src/tf2/shared/Player/TfPlayerNetState.cs`: `[StandaloneNetState("tf_player_data")]`
  — owner entity index, class, five condition masks (`[Predicted]`),
  cloak meter, charge meter, disguise team/class, max health, `AirDashCount`
  (`[Predicted]`), medigun heal target, building counts. The movement rules
  (1.8) read class/conditions/air-dash **only** from here on both sides.
- `TfPlayerDataEntity` `[ScriptedEntity("tf_player_data", ScriptedEntityKind.Effect)]`
  with `[NetState] TfPlayerNetState`, created by the `CarrierComponent` when the core composite attaches it on connect,
  transmitted to everyone (the client needs other players' class/conditions
  for HUD and effects).
- `joinclass`/`changeclass` via Stage 0.6 `IClientCommandHook`
  (`TfClientCommands`), respawn-room rule for instant class change
  (`func_respawnroom` touch, 2.4), else change on next spawn (stock
  `CTFPlayer::HandleCommand_JoinClass`).
- Components as listed, each its own file, constructor-injected, no gameplay
  `if (class == …)` in the shell (structure fact: grep-audit `TfPlayer.cs`
  for `TfClass.`).

**Facts** (one file per component): class change sets hull/eye height
(`ViewOffsetZ` = `g_TFClassViewVectors[class].z`, value cited) and max
health from the class table; loadout gives exactly the class's base items in
slot order (RecordingEngine `GiveWeapon` calls); conditions tick writes the
masks to the carrier and expiry clears them; overheal decays to max health
at the stock rate (two ticks, before ≠ after); the component source yields one
carrier per connect and removes it on disconnect (count differential);
`Tick` order is deterministic (recorded order equals the declared order).

**Gate**: `make tf2-test FILTER='TfPlayer'`; live on `ctf_2fort`: `joinclass heavy`
→ `/entity` shows `tf_player_data` class 6, max health 300, speed ~230;
`joinclass scout` from the respawn room → instant; from mid-map → on
respawn. A datatable change is verified only by a full join: a second
headless client's mirror (Stage 3.3 stub, or `tools/debug-api` nettable
view) shows the first player's class.

## Phase 2.4 — Map entities batch 1: spawns, rooms, kits, control points, resources

**Stock** (`server/tf/` unless noted): `entity_tfstart.cpp` (178,
`info_player_teamspawn`: team, `controlpoint`, `round_*` keyvalues, enabled
state), `func_respawnroom.cpp` (541, + `func_respawnroomvisualizer`),
`entity_healthkit.cpp` (308: small/medium/full = 20.5 %/50 %/100 % of max,
extinguishes burning, respawn 10 s — cite `TF_HEALTHKIT_*`), `entity_ammopack.cpp`
(140: % of max ammo per type + metal), `func_regenerate.cpp` (216, resupply
locker), `entity_forcerespawn.cpp` (143), `func_no_build.cpp` (234),
`tf_triggers.cpp` (1,012 — port `trigger_add_tf_player_condition`,
`trigger_remove_tf_player_condition`, `trigger_ignite`; list the rest as
`later`), `tf_pushentity.cpp` (481, `trigger_push` TF arm — compare with the
managed `TriggerPush` and port only the difference);
`server/team_control_point.cpp` (1,096: ownership, `OnCapTeam1/2`, locked,
`point_index`), `team_control_point_master.cpp` (1,341: `caplayout`,
win on all points, round selection), `team_control_point_round.cpp` (414),
`server/trigger_area_capture.cpp` (1,247: `CaptureThink` L304 (361 — cap
rate per player count, blocking, reversal/decay), `StartCapture` L743,
`EndCapture` L852, `BreakCapture` L926, `AreaTouch` L255),
`server/team_objectiveresource.cpp` (573) + `server/tf/tf_objective_resource.cpp`
(359) — the networked objective table the HUD reads; `tf_player_resource`
(stock `CTFPlayerResource`, grep `server/tf`) — class/score/damage per player
beside the core `PlayerResource`.

**Make** (`src/tf2/server/Entities/`, `src/tf2/shared/Resources/`)
- `InfoPlayerTeamSpawn` + `TfSpawnSelector : ISpawnSelector` (Stage 0.5):
  team match, enabled, round/control-point gating via `IModeSpawns`.
- `FuncRespawnRoom` (team-gated instant class change, no-build, projectile
  removal per stock), `FuncRespawnRoomVisualizer` (brush shown to the other
  team — needs a per-team visibility decision: record what the engine can
  do; if the managed brush entity cannot hide per client, divergence row).
- `HealthKit`/`AmmoPack` over `PickupItem` (`src/server/managed/Entities/Items/PickupItem.cs:17`)
  with TF rules (heal through `HealthComponent`, extinguish burning,
  metal); `FuncRegenerate` (full resupply, 3 s per player cooldown — cite).
- `EntityForceRespawn`, `FuncNoBuild`, the three triggers.
- Control points: `TeamControlPoint`, `TeamControlPointMaster`,
  `TeamControlPointRound`, `TriggerAreaCapture` (cap math ported exactly:
  `CaptureThink` rate formula with `mp_capstyle`, blocked/reversal/decay),
  all `IRoundResettable`.
- Resources: `TfObjectiveResourceEntity` `[ScriptedEntity("tf_objective_resource", Effect)]`
  + `[StandaloneNetState] TfObjectiveResourceNetState` (per control point:
  owner, cappers, progress, locked, icons; train progress for PL; flag state
  for CTF — one struct, modes fill their part through `IModeObjectives`);
  `TfTeamResourceEntity` (score, role, player count per team);
  `TfPlayerResource` fields (class, score, damage, healing) — extend the
  core `PlayerResourceNetState` only if a generic field is missing; else
  carry on `tf_player_data`.

**Facts**: spawn selector returns a same-team enabled spawn and never an
enemy one (both arms); respawn-room touch enables instant class change,
leaving disables; health kit heals 20.5 %/50 %/100 % of class max and
extinguishes (two facts), respawns after the stock delay (think scheduled);
ammo pack grants the stock percentages and metal; area capture with one
player reaches 100 % at `cap time`, two players faster per the stock
formula (quote), enemy presence blocks, leaving decays; master declares a
win when all points are owned; `RoundReset` returns points to the map's
default owner; objective resource fields change when a point is capped
(before ≠ after).

**Gate**: `make tf2-test FILTER='Spawn|RespawnRoom|HealthKit|AmmoPack|ControlPoint|AreaCapture|ObjectiveResource'`;
census gate: `make headless RUN_MOD_ID=tf2 MAP=cp_dustbowl` (and `pl_badwater`,
`ctf_2fort`): the entity census (`tools/debug-api` entity list) shows every
`team_control_point`, `trigger_capture_area`, `info_player_teamspawn`,
`func_respawnroom`, `item_healthkit_*`, `item_ammopack_*` instantiated as
**managed** classes and zero `[SourceSharp] unknown classname` lines for
them. Record the counts per map.

## Phase 2.5 — CTF (D4 milestone mode)

**Stock**: `shared/tf/entity_capture_flag.cpp` (3,064): `Spawn` L592,
`Reset` L831, `ResetMessage` L909, `FlagTouch` L1027 (122), `PickUp` L1149
(344), `Capture` L1493 (299), `Drop` L1792 (346), `Think` L2274 (134,
`TF_FLAG_THINK_TIME 0.25`), `AddPointValue` L2819; header constants
`entity_capture_flag.h:21-30` (model `models/flag/briefcase.mdl`, trail,
icon); convar `tf_flag_return_time_credit_factor` (:57); `server/tf/func_capture_zone.cpp`
(751); CTF win in `tf_gamerules.cpp` (`tf_flag_caps_per_round`, grep
`CheckCapsPerRound`/`FlagCaptured`); `func_flag_alert.cpp` (94),
`func_respawnflag.cpp` (85).

**Make**: `src/tf2/server/Entities/Ctf/ItemTeamFlag.cs` (`item_teamflag`;
states `Home/Carried/Dropped` per stock `ETFFlagStatus`, return timer with
credit factor, drop on death/`dropitem`, no pickup by a disguised/cloaked
spy per stock, neutral flag type later), `FuncCaptureZone.cs`,
`FuncFlagAlert.cs`, `FuncRespawnFlag.cs`, `Modes/CtfMode.cs` (`IGameMode`:
score on capture, win at caps-per-round, overtime while a flag is carried,
objective-resource flag fields, team-spawn filter). Flag carrier state on
`tf_player_data` (for HUD).

**Facts**: pick up from home → carried, carrier set; carrier death → dropped
with the stock return time; dropped untouched → home after the timer
(credit factor applied for carry time — fact with factor 1 vs 0); capture
by the carrier's team zone → score +1, flag home, `OnCapture` output fires;
own flag not home blocks capture only if stock says so (`CTF` type rule —
cite or drop the claim); caps-per-round reached → `TEAM_WIN` through the
proxy; `RoundReset` → both flags home.

**Gate**: `make tf2-test FILTER='Flag|CaptureZone|CtfMode'`; live
scripted on `ctf_2fort`: `make headless RUN_MOD_ID=tf2 MAP=ctf_2fort`, one
client teleported (`setpos`) to the enemy intel, touches it, teleports to
the own capture zone → console shows the capture event and team score 1
(the Stage 4 gate script automates this).

## Phase 2.6 — Payload (D4; D7 stock cart mechanics, no shortcut)

**Stock**: `server/team_train_watcher.cpp` (1,572): `WatcherActivate` L826
(217 — finds the train, `path_track` graph, control points as checkpoints),
`WatcherThink` L1084 (342 — progress, cappers, speed levels, recede after
`tf_escort_recede_time` 30 s / overtime 5 s, alarm), `HandleTrainMovement`
L520, `InternalSetNumTrainCappers` L644, `InternalSetSpeedForwardModifier`
L627, `ProjectPointOntoPath` L1464, `TimerMayExpire` L1442,
`FireGameEvent` L416; keyvalues :110-127 (`train_can_recede`,
`train_recede_time`, `linked_pathtrack_*`, `linked_cp_*`, `hud_min_speed_level_*`);
`trigger_area_capture` in cart-push mode (`m_bCaptureBlocked`, "cap zone on
the cart"; grep `IsCapturingTrain`); cart dispenser `server/tf/tf_obj_dispenser.cpp`
(`CObjectCartDispenser`, grep) — milestone: heal/ammo radius only, no
upgrade.

**Make**: `src/tf2/server/Entities/Payload/TeamTrainWatcher.cs` (over the
managed `FuncTrackTrain`/`PathTrack`: speed levels by capper count, recede,
checkpoints = control points captured when the cart passes, overtime,
objective-resource progress fields), `Modes/PayloadMode.cs` (`IGameMode`:
final checkpoint → attackers win, round timer expiry (with overtime rule)
→ defenders win, stage advance for multi-stage maps via
`team_control_point_round`), `CartDispenser.cs` (minimal). Check first that
the managed `FuncTrackTrain` exposes what the watcher needs (set speed,
current `path_track`, position along path); if not, add the generic members
to the **core** entity with facts (not a TF2 copy).

**Facts**: one capper → speed level 1, three → level 3, zero for 30 s →
recede at the stock speed; a defender in the zone blocks; passing a
checkpoint captures its control point, adds the stock time bonus, and
stops recede past it; final checkpoint → win through the proxy; objective
resource progress tracks the projected position (before ≠ after a push);
`RoundReset` returns the cart to the first `path_track`.

**Gate**: `make tf2-test FILTER='TrainWatcher|Payload'`; live scripted on
`pl_badwater`: a client teleported onto the cart pushes it to checkpoint 1
(`tools/debug-api` reads the watcher's progress netstate), waits 30 s away
→ recede observed. Both numbers logged.

## Phase 2.7 — Damage layer (`IDamageRule` owner): crits, ramp-up, afterburn, headshots, fall damage, kill feed

**Stock**: `tf_gamerules.cpp ApplyOnDamageModifyRules` L5914 (1,190) —
read in this order: crit/mini-crit multipliers, distance ramp-up/fall-off
(`tf_damage_range`-style constants: 512 u, ×1.5/×0.5 for bullets, cite the
exact `RemapValClamped` lines), `TF_COND_INVULNERABLE`, backstab ×6 (knife
sets `DMG_CRITICAL` + custom), headshot crit (`TF_DMG_CUSTOM_HEADSHOT` from
the sniper rifle via hitgroup 1, Stage 0.8), self-damage rules
(rocket/sticky jumping, `tf_player_shared ModifyDamageInfo` L10204),
`ApplyOnDamageAliveModifyRules` L7104 (afterburn application, damage
events), `FPlayerCanTakeDamage` L9808 (team damage, spawn protection),
`PlayerKilled` L11315, `GetKillingWeaponName` L12108 (582 — kill-feed weapon
name resolution), `DeathNotice` L12814 (431 — `player_death` event fields:
`weapon`, `customkill`, `damagebits`, `assister`); afterburn `Burn` L6622
(150) + `ConditionGameRulesThink`'s burning arm (tick interval and damage —
cite `TF_BURNING_FREQUENCY`/`TF_BURNING_FLAME_LIFE` from `tf_shareddefs.h`);
fall damage `CTFGameRules::FlPlayerFallDamage` (grep) — TF2 uses a
randomised fall damage with a class sound; stock `tf_gamemovement`
`CheckFalling`.

TF2 has **no** body-part damage multipliers; headshot is a crit, not a
table. Drop the first draft's "hitgroup multiplier table".

**Make**: `src/tf2/server/Combat/TfDamageRules.cs` (`IDamageRule` owner:
one pass in stock order over a `DamageContext`), `CritRules.cs` (bucket +
roll result from the weapon behaviour carried in the context's custom
field), `Afterburn.cs` (on `ConditionsComponent`: ignite with duration,
tick damage, extinguish by water/health kit/medigun; duration refresh rule),
`FallDamage.cs`, `TfDeathNotice.cs` (fills the stock `player_death` game
event the client kill feed already subscribes to — `src/client/managed/Ui/KillFeed.cs`
+ `KillIcons`: TF2 weapon names map to icons in Stage 3.1).

**Facts**: bullet at 0 u → ×1.5, at 512+ u → ×0.5 (quote the ramp
constants), explosive ramp differs per stock; crit ×3 ignores ramp
(stock: crits remove fall-off — cite); mini-crit ×1.35 keeps ramp-up only;
invulnerable → 0; team damage off → 0 unless self; headshot on hitgroup 1
with a sniper rifle at ≥ the stock charge → crit, body → normal; afterburn
ticks the stock damage at the stock interval and expires at the stock life;
water extinguishes; fall damage from the stock height formula is within the
stock random band (fact fixes the seed); `player_death` event carries
`weapon="scattergun"` for a scattergun kill (recorded event).

**Gate**: `make tf2-test FILTER='TfDamage|Crit|Afterburn|FallDamage|DeathNotice'`;
live on `ctf_2fort`: a soldier rocket at a heavy at 100 u and at 800 u —
log both damages; a sniper headshot kills a 125-hp scout, body shot does
not; kill feed (HL2 panel) shows the weapon.

## Phase 2.8 — Projectiles (milestone: rocket, pipe, sticky, syringe; later: flare, arrow, energy ball)

**Stock**: `server/tf/tf_projectile_rocket.cpp` (181) + `shared/tf/tf_weaponbase_rocket.cpp`
(667: straight flight, speed 1100, explode on touch, radius damage with
`tf_rocket` constants); `shared/tf/tf_weaponbase_grenadeproj.cpp` (798:
vphysics grenade base, fuse, team collision) + `tf_weapon_grenade_pipebomb.cpp`
(1,434: pipe = explode on contact with enemy/after fuse 2.3 s + roll;
sticky = stick to world on touch, arm time 0.7 s, detonate on command,
max 8 per player, oldest removed — cite constants) + `tf_weapon_pipebomblauncher.cpp`
(649: charge → launch speed, det); syringe projectile (grep
`CTFProjectile_Syringe` under `server/tf`/`shared/tf`: nail base, gravity
arc, 10 dmg); later: `tf_projectile_flare.cpp` (631), `tf_projectile_arrow.cpp`
(1,494), `tf_projectile_energy_ball.cpp` (391).

**Make** (`src/tf2/server/Projectiles/`): `TfRocket : FlyingProjectile`
(`src/server/managed/Entities/Projectiles/FlyingProjectile.cs:20`; `Impact`
→ `Explosions.Fire` with TF radius/damage and self-damage rules),
`TfPipeGrenade`/`TfStickybomb` over the `Grenade` pattern
(`Grenade.cs:21`, `ScriptedEntityKind.Prop`, `IEngineEntityPhysics`
`EngineAreas.Entities.cs:341` — `SetBounce`, `SetGravity`, `EnableMotion`
false to stick), `TfSyringe : FlyingProjectile`; `StickyController` in
`BuildingsComponent`'s sibling `ProjectilesComponent` (count, oldest-removal,
detonate). The weapon behaviours (1.9) call `IWeaponArms.Launch…` with the
TF projectile class name and velocity; the server arm creates these.
Team ownership and "projectile passes through teammates" via the collision
group (`SetCollisionGroup`).

**Facts** (RecordingEngine + the `ProjectileTouchTests` pattern
`src/test/server/ProjectileTouchTests.cs`): rocket explodes on first world
touch with the stock radius; pipe explodes on enemy touch before the fuse,
on the fuse otherwise; sticky sticks (motion disabled) and does not explode
on touch, arms after 0.7 s, detonates on command, ninth sticky removes the
oldest; syringe follows the gravity arc (two positions) and hits for 10;
owner never hit by their own pipe before it bounces (stock rule, cite).

**Gate**: `make tf2-test FILTER='TfRocket|Pipe|Sticky|Syringe'`; live:
give each, fire into a wall/at a bot, `tools/debug-api` shows the
explosion/hit; sticky `+attack2` detonates.

## Phase 2.9 — Medigun and healing

**Stock**: `shared/tf/tf_weapon_medigun.cpp` (3,178; 69 methods): heal
target acquisition (`FindNewTarget`, range, LOS, `AllowedToHealTarget`),
`HealTargetThink`, heal rate (base 24 hp/s scaled ×1–3 by the target's time
since last damage, 10–15 s — cite `tf_medigun_*` and `HEAL_RATE` constants),
overheal to 150 % (`GetMaxBuffedHealth`), uber build (`UberchargeChargeRate`
faster on hurt targets, slower on overhealed), release → `TF_COND_INVULNERABLE`
8 s + `_WEARINGOFF`, beam state networked; Quick-Fix/Vaccinator/Kritzkrieg
arms → `later` rows (Kritzkrieg is cheap once conditions exist).

**Make**: `src/tf2/shared/Weapons/MedigunBehaviour.cs` (predicted
part: charge meter read, heal-target index on the weapon netstate for
the HUD) + `src/tf2/server/Weapons/MedigunServer.cs` (target selection,
heal application through `HealthComponent`, uber release through
`ConditionsComponent`); medigun state fields on `tf_player_data`.

**Facts**: heal rate ×1 right after damage, ×3 after the stock quiet
time (two inputs); overheal stops at 150 % of max; uber accrues per the
stock rate and halves on an overhealed target (cite); release gives both
medic and target invulnerability for 8 s then wearing-off; LOS break
beyond the grace time drops the target; cannot heal an enemy or a
disguised enemy spy (both arms).

**Gate**: `make tf2-test FILTER=Medigun`; live: medic heals a hurt heavy
to 450, uber at 100 % → both ignore a rocket.

## Phase 2.10 — Buildings (engineer)

**Stock**: `server/tf/tf_obj.cpp` (3,823; 111 methods — read `StartBuilding`,
`FinishedBuilding`, `Upgrade*`, `OnTakeDamage`, `Killed`, `SetBuilder`,
`CheckBuildPoint`/placement validity, sapper hooks), `tf_obj_sentrygun.cpp`
(2,398; 54: `FindTarget`, `Attack`, `Fire`, `FireRocket`, rotation per
level, ammo/rockets, `SentryThink`), `tf_obj_dispenser.cpp` (1,188: heal
radius/rate per level, metal regen, ammo), `tf_obj_teleporter.cpp` (1,540:
entrance/exit pairing, charge/recharge per level, player teleport,
`TF_COND_TELEPORTED`), `tf_obj_sapper.cpp` (786), `tf_weapon_builder.cpp`
(1,285: ghost placement, metal cost, `CanBuild`), PDA `tf_weapon_pda.cpp`
(479), `tf_weapon_wrench.cpp` (593: hit → repair/upgrade/metal), player
`CanBuild` L11322, `TryToPickupBuilding` L12536 (carrying, later), build
costs/limits in `tf_shareddefs` (`g_ObjectInfos`, grep `GetObjectInfo`).

**Make** (`src/tf2/server/Buildings/`): `BaseObject` (`[ScriptedEntity(..., Prop)]`
or `Brush`-less: use the kind whose physics matches — the sentry is a solid
animated prop; health, team, builder, level, build/upgrade progress,
sapped state, `[StandaloneNetState] ObjectNetState`), `SentryGun`
(target selection: nearest enemy in range/LOS, not disguised-as-friendly or
cloaked; aim/fire per level; rockets at level 3), `Dispenser`, `Teleporter`
(entrance/exit, charge), `Sapper` (attach, drain, destroy), `Builder`
behaviour (ghost → place validity via a managed hull trace; metal cost),
PDA behaviours (build menu / destroy menu via user messages),
`WrenchBehaviour` (melee + build interaction). `BuildingsComponent` tracks
limits (one of each; stock `GetNumObjects`).

**Facts** (per building ≥ 3): sentry acquires the nearest valid target
within range and ignores a disguised spy of its team (both arms), fires at
the level's rate, costs the stock metal to upgrade; dispenser heals in
radius at the level rate and regenerates the owner's metal; teleporter
teleports a teammate after charge and recharges per level; sapper disables
then destroys after the stock time, wrench hits remove it; placement in
solid or in an enemy respawn room is refused; a second sentry is refused.

**Gate**: `make tf2-test FILTER='Sentry|Dispenser|Teleporter|Sapper|Builder'`;
live: build each by console, a bot (or second client) walks into sentry
range and is shot; teleport observed (`getpos` jump).

## Phase 2.11 — Flamethrower, flames, airblast

**Stock**: `shared/tf/tf_weapon_flamethrower.cpp` (3,206; 73 methods —
`PrimaryAttack`/firing state machine, ammo per flame, `FireProjectile` →
flame, `SecondaryAttack` airblast cost/cooldown, `DeflectProjectiles`,
`DeflectPlayer`), `shared/tf/tf_flame.cpp` (1,216; 41: `CTFFlameEntity::Create`
velocity/spread, `FlameThink` life/speed decay, `OnCollide` ignite + damage
by distance/time, burn), `tf_shareddefs.h` flame/airblast constants
(`TF_FLAMETHROWER_*`, `tf_flamethrower_*` convars in the flamethrower file).

**Make**: `src/tf2/shared/Weapons/FlamethrowerBehaviour.cs` (predicted
ammo/state), `src/tf2/server/Projectiles/FlameEntity.cs` (pooled, kind
`Projectile`, no model — damage volume only; sets afterburn via 2.7,
direct damage falloff per stock), `Airblast.cs` (reflect: projectiles of
2.8 get `Reflected(team, newVelocity)` → owner/team change and mini-crit
flag per stock; push players with the stock impulse; extinguish teammates).
Visuals are the client's (Stage 3.6).

**Facts**: a flame ignites on first enemy collide and damages by the stock
distance curve (two distances); flames expire at the stock life; airblast
consumes the stock ammo and reflects a rocket (velocity reversed toward
the aim direction, owner changed, mini-crit flag set), pushes a player with
the stock impulse, extinguishes a burning teammate; cooldown prevents a
second airblast inside the stock delay.

**Gate**: `make tf2-test FILTER='Flame|Airblast'`; live: burn a target
(afterburn ticks logged), airblast a rocket back at a soldier (reflected
kill in the death notice with the stock `customkill`).

## Phase 2.12 — Spy: cloak, disguise, backstab, sapper use

**Stock**: `tf_player_shared.cpp` `Disguise` L8157 (118), `DetermineDisguiseWeapon`
L8394 (183), `DetermineDisguiseWearables` L8577, `OnAddStealthed` L6952,
`OnRemoveStealthed` L7048, `InvisibilityThink` L7977, `UpdateCloakMeter`
L14165 (93), `TF_COND_DISGUISING` delay, bumping/`m_flInvisChangeCompleteTime`;
`tf_weapon_invis.cpp` (372), `tf_weapon_knife.cpp` (743: `CanPerformBackstabAgainstTarget`
angle rule, `DMG_CRITICAL` ×6 → instant kill), `tf_weapon_pda.cpp` disguise
menu; client-side disguise rendering is Stage 3.3/3.5.

Milestone scope (a user decision, see `plan_tf2_port.md` §7 U4): full
disguise state (team, class, weapon, name, fake health) networked on
`tf_player_data`; sentries and medigun respect it (2.9/2.10 facts already
written both arms); client-side model/name swap in Stage 3.

**Make**: `src/tf2/server/Player/SpyComponent.cs`, `src/tf2/shared/Weapons/InvisWatchBehaviour.cs`,
`KnifeBehaviour.cs`, `DisguiseKitBehaviour.cs` (PDA menu via user messages
`TF2:disguise`), cloak meter `[Predicted]` on `tf_player_data`.

**Facts**: cloak drains and refills at the stock rates (two ticks); taking
damage while cloaked flickers (`stealth` partial — cite) ; disguise completes
after the stock delay and sets team/class/weapon; attacking removes the
disguise; backstab from behind within the stock angle → ×6 kill, from the
front → normal knife damage; a disguised spy is not auto-targeted by a
sentry (reuses 2.10's fact).

**Gate**: `make tf2-test FILTER='Spy|Invis|Knife|Disguise'`; live: cloak →
`/entity` shows the condition and meter; backstab a stationary heavy.

## Phase 2.13 — Wearables (cosmetics; small, after 2.3)

**Stock**: `shared/tf/tf_item_wearable.cpp` (842), `shared/econ/econ_wearable.cpp`;
attach to the player model via bone merge (`EF_BONEMERGE`); the client
draws them (Stage 3.3).

**Make**: `src/tf2/server/Player/WearablesComponent.cs` creating one
`tf_wearable` carrier entity per equipped wearable (model index, owner,
team skin; `[StandaloneNetState]`), removed on class change/death
(stock: wearables die with the player and respawn with them).

**Facts**: equip → one entity with the item's `model_player`; class change
→ removed and recreated for the new class's loadout; disconnect → none
left (count differential).

**Gate**: `make tf2-test FILTER=Wearable`.

---

## Stage 2 exit

`make headless RUN_MOD_ID=tf2 MAP=ctf_2fort` and `MAP=pl_badwater`: two
headless clients auto-balanced, all nine classes spawn with their loadouts,
move at class speeds, deal TF damage with crits/afterburn/headshots, heal,
build, cloak; CTF captures score and end the round; the Payload cart
pushes, recedes and wins/loses; `sv_mode` switches with a warmup;
`make test` all legs green (the `tf2` leg declares its floor and is far
larger than Stage 1's). The HUD is still the HL2 one. Record the census
counts for the three maps and the live measurements named in each phase's
gate in `docs/plans/tf2/stage2_results.md` (uncommitted until reviewed, like
the plan).
