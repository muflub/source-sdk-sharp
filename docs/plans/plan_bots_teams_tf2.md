# Teams, votes, on-demand nav, and class-based TF2 bots: the plan

Status: reviewed and committed; the open questions accepted as recommended (user,
2026-11-02: "commit plan but dont start"). A plan only: nothing in it is implemented,
and work starts when the user says so.

The user's request, word for word (2026-11-02): "Plan out what a game mode
for team death match looks like for hl2dm and what changes ai needs to
support it and then extend that to tf2 mod with class based ai with its
many game modes". Added the same day: "also include voting and admin menu
options to change game modes and game mode config", and "also add on how
server would gen nav3d on map change to a map without a nav3d file or
broken nav3d file (can ship map tool assemblies with the server)". Two
rulings the same day: "bots dont vote and map votes should be inscope".

## 0. Premises, and how to read this

Everything below was read at these commits, this session:

| tree | commit | note |
|---|---|---|
| this repo, `main` | `6dc0e4029` (2026-11-02) | the HL2MP AI, the TF2 mod, the vote and admin code are all on main |
| `../ss-hl2mp-ai-tactics` | branch `hl2mp-ai-tactics` at `6dc0e4029`, **uncommitted**: 50 files, +1,712/−144 (`git diff --stat`), new `src/ai/core/Nav/ChokeMap.cs`, `src/ai/core/Record/AiTactics.cs`, `src/ai/tests/Tactics/` | prefire, camp spots at pickups and chokes, style relative to a bot's own mean. Treated as landing soon; nothing here depends on its exact shape |
| map-tools `../source-sdk-map-tools` | `38446d1` = `maptools.lock` | read only |
| the Valve clone `../source-sdk-2013-valve` | `22288b91` = `valve-sdk.lock` | read only; identifiers cited, no code quoted |
| `~/git/sharp-fresh/docs/plans/plan_tf2_rpg.md` | Appendix B (`:988-1189`), uncommitted in that repo | restated in `docs/plans/tf2/stage5_bots.md` §5.0 |
| the TF2 lanes' worktrees (`ss-tf2`, `ss-tf2-gate2c`, `ss-tf2-pyrofx`, `ss-tf2-spyslots`, `ss-tf2-weaponselect`) | all at `d7dbf6b25`, 0 commits ahead of main (a subagent's `git worktree list` + `git log`) | in flight: scoreboard, minigun, blast-jump, reload-anim, voice-menu, weapon-select polish; **no commit adds a mode or a bot** |

Five parts, in the order the user asked for them plus the two additions,
then phases, risks, decisions. Parts D and E (votes/admin/config, and
on-demand nav) are placed after C in the text but **before** C in the
phases, because TF2 bots on community maps need both.

- **Part A** HL2DM team deathmatch as a game mode.
- **Part B** What the AI needs for teams (HL2DM).
- **Part C** TF2: class-based bots and its game modes.
- **Part D** The game-mode config model, votes and the admin menu.
- **Part E** The server builds a `.nav3d` on map change.
- **§6** Phases, gates. **§7** Risks and effort. **§8** Decisions: rulings
  (dated) and open questions with recommendations. **§9** What could not
  be verified.

Rules that bind every part (from `docs/plans/plan_hl2mp_ai.md` §12.1,
carried in §8.1): the bot does only what a player can do and sees only what
a player can see; its one output is a usercmd; memory in memory only; off
the main thread, more than one worker allowed, never the game thread; 100 %
managed plus generated forwarder rows; `.nav3d` from map-tools, no stock
nav mesh and no NextBot port (vocabulary only); human motion; no human
recordings committed; the core assembly (`SourceSharp.Ai`) names nothing of
HL2MP (plan §1.3's gate) and must stay mod-neutral for TF2.

The repo disagrees with the older plans in places; each is called out where
it matters (marked **repo differs**).

---

## Part A: HL2DM team deathmatch as a game mode

### A.1 What it is for a player

Two teams, Combine and Rebels. You pick a team or are auto-assigned to the
smaller one; you wear your team's models; you cannot hurt teammates unless
the server allows it; the team's frags are the score; the round ends at the
frag limit (a team's score) or the time limit; the scoreboard shows two
columns with the team totals; `say_team` reaches your team. That is stock
HL2DM with `mp_teamplay 1` (`CHL2MPRules::IsTeamplay`,
`hl2mp_gamerules.h:151`; game description "Team Deathmatch",
`hl2mp_gamerules.cpp:836-842`).

### A.2 Ground truth at `6dc0e4029`: what exists, what is stubbed, what is missing

| piece | stock (clone) | this repo | state |
|---|---|---|---|
| the knob | `mp_teamplay` (`game.cpp:33`), latched once into `m_bTeamPlayEnabled` in the rules' constructor (`hl2mp_gamerules.cpp:195`) | `GameDllConVars.cs:33` declares it, default 0. Its only reader is `PlayerModels.cs:89` (`Teamplay`) | DECLARED, unread by rules |
| teams | `team_manager` entities Unassigned/Spectator/Combine/Rebels always created (`hl2mp_gamerules.cpp:175-193`; `TEAM_COMBINE` 2, `TEAM_REBELS` 3, `hl2mp_gamerules.h:31-32`) | `ITeams` store (`src/server/managed/Teams.cs:37`: `Team`, `Players`, `Count`, `Join`, `SelectSpawn`); names 2 = Combine, 3 = Rebels in `HostSay.cs:155` and `PlayerModels.cs:54,57`; `TeamNames` rule chain (`src/shared/managed/TeamNames.cs:18,31`). No team entity | REAL store, no entity, no score |
| join | `HandleCommand_JoinTeam` (`hl2mp_player.cpp:1011-1052`), `ChangeTeam` (`:958-1009`): DM forces team 0 (`:971-975`), a switch from an assigned team suicides (`:1005-1007`) | `jointeam N` at `ServerModule.cs:2998` to `ITeams.Join`; team 0 refused with stock's warning; `IJoinTeamPolicy` single owner (`Teams.cs:86`), zero owners in core, TF2's `TFTeams` is the only one (`src/tf2/server/Rules/TFTeams.cs:33`); no suicide on switch (`docs/divergences.md:15`); no forced team 0 (`:16`) | REAL seam; two deliberate divergences |
| auto-assign | `PickDefaultSpawnTeam` (`hl2mp_player.cpp:280-328`): smaller team, random on a tie | none; `jointeam 0` is refused; the team menu's AUTO ASSIGN sends 0 and the live gate **pins the refusal** (`tools/live-gate:8412-8419`, needle "invalid team index") | MISSING; a gate pin to update |
| team spawns | `info_player_combine` / `info_player_rebel` are `CPointEntity`s (`hl2mp_player.cpp:46-47`); `EntSelectSpawnPoint` (`:1390-1487`) picks them under teamplay and **falls back to `info_player_deathmatch`** when the map has none (`:1410-1414`) | `SpawnPoints.Choose` asks `ITeams.SelectSpawn` (mode's `IGameMode.Spawns`, then the `ISpawnSelector` chain, `Teams.cs:111`), then walks `info_player_deathmatch` → `info_player_start` (`EntityNames.cs:136`); the two team classes exist only in `src/server/managed/Modes/HL2MPMapCleanupFilter.cs:53-54`'s keep list; `KillAround` (`SpawnPoints.cs:196`) is team-blind; `ISpawnFragRule` (`:41`) has one owner, TF2's `TFSpawnNoFrag.cs:19` | MISSING |
| **dm_lockdown** | | `game/mod_test/maps/dm_lockdown.bsp`: `grep -ac info_player_combine` = 0, `info_player_rebel` = 0, 9 `info_player_deathmatch` + 1 `info_player_start` (a subagent's `strings`/`grep -ac`, byte-identical to the Steam copy). **dm_lockdown has no team spawns**; stock plays TDM on it through the deathmatch fallback | checked |
| models | `g_ppszRandomCombineModels` / `g_ppszRandomCitizenModels` (`hl2mp_player.cpp:107,126`), `SetPlayerTeamModel` (`:417`), `cl_playermodel` implies a team under teamplay (`hl2mp_gamerules.cpp:799-806`) | `PlayerModels.Choose` (`PlayerModels.cs:172`): `Teamplay ? TeamModel : FreeModel`; team 2 wears Combine, 3 Citizens; a `cl_playermodel` change under teamplay joins the matching team (`:149`); re-dress on `ITeamChangeWatcher` (`:43`). Bots: `BotAppearance.cs:54` picks one of 19 models at join, no team | REAL (humans); bots team-blind |
| friendly fire | `mp_friendlyfire` (`gamevars_shared.cpp:44`, default 0, replicated); `CTeamplayRules::FPlayerCanTakeDamage` (`teamplay_gamerules.cpp:355-367`) | `GameRules.cs:393` `Teammates` (team ≥ 2 and equal) is used at `:366`; but the convar read is **`cs_sv_friendly_fire`** (`ConVars.cs:77`, default **"1"**) through `FriendlyFireConVar` (`GameRules.cs:119,220`). `mp_friendlyfire` (`GameVars.cs:34`) has no core reader; TF2 reads it (`src/tf2/server/Combat/TFTeamDamageRule.cs:21`) | WRONG KNOB, wrong default for HL2DM |
| team score | attacker's team +1, suicide −1 (`hl2mp_player.cpp:1327-1334`, `CTeam::AddScore` `team.cpp:281`); frag limit is a team's score under teamplay (`hl2mp_gamerules.cpp:323-331`) | `IScoring.TeamScore` (`src/server/managed/Modes/GameModeContracts.cs:148-154`) → `PlayerResource.UpdateTeamScores` (`PlayerResource.cs:200`) → `PlayerResourceNetState.TeamScore` (`:143`). The core's `NullMode` registers no `IScoring`, so the wire carries 0. Frag limit ends on the best single player (`GameRules.cs` `ThinkStock`, `_scoreboard.Best()`) | plumbing REAL, no writer; limit is DM-only |
| scoreboard | `hl2mpclientscoreboard.cpp:33-36,339-349` team sections; team score in the header (`:355,386`) | `src/client/ui/screens/Scoreboard.cs:8` draws up to two `TeamTable`s (`toolkit/Widgets/TeamTable.cs:17`) when any player is on 2 or 3 (`MenuSources.cs:127`); **the team score is the client's sum of member frags**, not `PlayerResource.TeamScore` | REAL layout, wrong source |
| team menu | the panel on first spawn | `src/client/ui/screens/TeamMenu.cs:17` (two cards, Spectate, Auto assign); `UiRuntime.cs:34` maps "team" → "teammenu". Nothing in core sends `VGUIMenu team`: only the cheat `cs_sv_showpanel` (`ShowPanelCommand.cs:12`). No core `changeteam` key action (TF2 has one) | REAL screen, never opened |
| team chat | `CTeamplayRules::PlayerCanHearChat` (`teamplay_gamerules.cpp:393`) | `SayCommands.cs:16` routes `say_team` to `HostSay.Say`; `HostSay.CanHear` (`HostSay.cs:128`) asks `Teammates`, which is `=> false` (`:145`). `IChatAudience` (`:49`) can only veto. Pins: `tools/live-gate:4976` ("has no teammates"), `Hl2mpBotChatTests.ATeamLineIsNotHeardWithoutTeammates` | STUB |
| HUD | `CTeamPlayHud` (`hl2mp_hud_team.cpp:25`) under teamplay; death notices coloured by team (`hud_deathnotice.cpp:143,176-177`); team colours Combine blue / Rebels red (`c_playerresource.cpp:77-81`) | no team HUD line; kill feed and chat not team-coloured for HL2MP (not checked beyond the scoreboard tones `TeamA`/`TeamB`) | MISSING (small) |
| autobalance, team limits | **none in stock HL2DM**: `mp_autoteambalance` is defined in `teamplayroundbased_gamerules.cpp:249` (compiled by `server_hl2mp.vpc:57`, logic round-based and unreachable from `CHL2MPRules`); `mp_limitteams` has zero hits in `game/` | TF2 only (`src/tf2/server/Rules/TFAutobalance.cs:19`, `TeamBalance.cs:33`) | not a parity requirement |
| a mode | `CHL2MPRules` branches on `IsTeamplay` | `IGameMode` registry (`GameModeContracts.cs:21`: `Id`, `Rounds`, `Respawn`, `Spawns`, `Scoring`, `Setup`, `Cleanup`); `GameModes.SwitchTo(id)` (`GameModes.cs:306`: keeps the players, flushes the round, runs `Setup`, warmup); `sv_mode` (`Modes/SvModeCommand.cs:7`), `game_mode_switch` entity (`Modes/GameModeSwitch.cs:18`), `IGameModeSelector` chain at level init (`GameModeContracts.cs:190`, asked at `GameModes.cs:169`); the admin menu lists `Registered` and switches (`Admins.cs:211-215`) | REAL registry; no TDM mode |

Maps: only `dm_lockdown` is installed here (`find` under `game/`,
`bin/debug/`, and the Steam `hl2mp` dir found no other `dm_*.bsp`; the VPKs'
strings name no bsp). `cfg/mapcycle_default.txt` in Steam's hl2mp lists seven
maps; `Intermission.ReadCycle` (`Intermission.cs:164`) has no existence
filter, so the intermission and the admin menu's map list can name six maps
that are not installed (what the engine then does is unverified).

### A.3 What gets built

A `tdm` game mode in the core, chosen by stock's knob and switchable
through the registry.

1. **`TeamDeathmatchMode : IGameMode`, id `tdm`** (`src/server/managed/Modes/TeamDeathmatchMode.cs`, new). `Rounds`: the core's deathmatch round flow with the frag-limit test replaced by a team score test (stock `hl2mp_gamerules.cpp:323-331`). `Scoring`: an `IScoring` that ports `Event_Killed`'s team arithmetic (attacker's team +1, suicide −1; `hl2mp_player.cpp:1327-1334`) and answers `TeamScore(2|3)`. The stock gotcha is ported as stock, with a divergence-free note in the test: a teamkill gives the team +1 while the killer's frags drop by 1 (`CTeamplayRules::IPointsForKill`, `teamplay_gamerules.cpp:415-423`). Nothing reads the deathmatch `Best()`.
2. **Team spawns.** `Spawns`: an `ISpawnSelector` that picks `info_player_combine` / `info_player_rebel` by team with stock's deathmatch fallback (`hl2mp_player.cpp:1410-1414`), the frag-on-spawn rule team-aware (teammates are not killed; stock `:1451`). The two class names get `EntityNames` constants and `MapSpawner` must create them (whether it does today is unverified; they are plain point entities in stock).
3. **Join policy and auto-assign.** The single-owner `IJoinTeamPolicy` cannot take a core owner without colliding with TF2's (`SideServices.cs:46-49`: a mod may not replace). So `IGameMode` gains `IJoinTeamPolicy? JoinPolicy => null`, asked by `Teams.Join` **before** the registered owner, the same shape as `Spawns`. TDM's policy: team 0 means auto (stock `PickDefaultSpawnTeam`), a switch from an assigned team kills the player (stock, closing `divergences.md:15` for this mode), and refuses a join that would leave the teams more than `mp_teams_unbalance_limit` apart (stock TF2's name, `teamplayroundbased_gamerules.cpp:219`; HL2DM has none, so default 0 = off, a config setting of Part D). No forced moves: stock HL2DM has no autobalance, and bots keep the teams even instead (Part B.6).
4. **Mode selection.** A core `IGameModeSelector` (`TeamplayModeSelector`, order after any mod's) answers `tdm` when `mp_teamplay` is 1 at level init, else null (the core's rules). That is stock's latch (`m_bTeamPlayEnabled` is read once per level). Mid-level, `sv_mode tdm` / the admin menu / a vote switch through `GameModes.SwitchTo`, which already keeps the players and restarts the round; TDM's `Setup` (`IModeSetup`) auto-assigns everyone on team 0 and re-dresses them (`PlayerModels` already watches team changes). Switching back to the core's rules sends everyone to team 0 (stock DM forces it).
5. **Friendly fire.** The core reads **`mp_friendlyfire`** (stock's knob and default 0) wherever it reads `cs_sv_friendly_fire` today (`GameRules.cs:119,220`); `cs_sv_friendly_fire` is retired in the same commit (its only use is this read; grep before deleting: `tools/live-gate` and `tools/` for the string). Default is an open question (§8.2 Q1).
6. **Team chat.** `HostSay.Teammates` becomes an instance rule over `ITeams` gated on the active mode's team-play (a small `ITeamplay` answer the mode owns: `IGameMode` gains `bool Teamplay => false`, TDM answers true, TF2's modes answer true). `say_team` then reaches the team; the two pins (`tools/live-gate:4976`, the chat fact) stay true with teamplay off and get a teamplay arm.
7. **Team menu and key.** Under a teamplay mode the server sends `VGUIMenu team` on first spawn (as TF2 does with its own panels); a core `changeteam` key action opens it; the `ui-teammenu` gate step gains a teamplay arm where AUTO ASSIGN joins the smaller team, and keeps its DM arm (the refusal) as is.
8. **Score on the wire and HUD.** `MenuSources.ScoreboardUi` reads `PlayerResource.TeamScore` (the sum of member frags is wrong once a teamkill or suicide has happened); the two tables are titled Combine/Rebels with the stock colours; a one-line team HUD element (`CTeamPlayHud`'s "your team" line) and team-coloured kill feed and chat names under teamplay. One `ui-screen` loop each (`docs/ui/authoring.md`).
9. **Replicate `mp_teamplay`** or, better, the active mode id: the client infers teams today from the roster. The mode id is already asked by the admin menu; Part D's config model networks it (D.3), and the scoreboard reads that.
10. **Create-server.** The core's Game tab is `cfg/settings_default.scr` rows (`CreateServer.cs:12-13`, `IServerSettingsSource` `UiSources.cs:1016`); the test mod ships none (`game/mod_test/cfg` does not exist). Part D's config model replaces that script for our mods; until then a `game/mod_test/cfg/settings_default.scr` with `mp_teamplay`, `mp_friendlyfire`, `mp_timelimit`, `mp_fraglimit` is the two-line stopgap.

Not built: `team_manager` entities (nothing in managed needs the entity; the
wire carries the score through `PlayerResource`), `mp_autoteambalance`'s forced
moves (not stock HL2DM; Part D offers the join limit).

### A.4 Maps to test with

| map | why | team spawns |
|---|---|---|
| `dm_lockdown` | the only installed stock map; the fallback arm | none (fallback to deathmatch spawns, as stock) |
| `ss_ai_tdm` (new, §B.8) | two bases, one mid pickup, a choke | `info_player_combine` and `info_player_rebel` placed by the room library, so the team-spawn arm is exercised |
| any installed Steam `dm_*` | not available here (A.2); the gate must not list a map it cannot load |

### A.5 Facts and gates for Part A

Facts (server test project): a kill writes the attacker's team score and a
suicide takes one; a teamkill is stock's +1/−1 pair; the frag limit ends the
round on a team's score and not on a player's; `tdm` spawns by team and falls
back when a map has no team spawn; `jointeam 0` under `tdm` lands on the
smaller team and on a random one at a tie (seeded); switching from an
assigned team kills; `say_team` reaches teammates only and nobody under the
core's rules; `mp_friendlyfire 0` blocks teammate damage and never self
damage; no-owner facts for every new default member (`IGameMode.JoinPolicy`,
`Teamplay`) prove the core's rules are untouched. Mutation arms through
`tools/ai-mutations`' shape: flip the score sign, drop the fallback, drop the
team gate in `CanHear`.

Live (`tools/live-gate`, new steps, each with a budget row): `tdm-round`:
`sv_mode tdm` on dm_lockdown with four bots (Part B), two teams of two, a
frag limit of 10, the round ends on a team score read from
`/entity`... (the resource's `TeamScore`), the scoreboard shows two columns
whose totals equal the resource's; then `changelevel ss_ai_tdm` and a second
round on team spawns (**the second map load**, CLAUDE.md); `tdm-chat`:
`say_team` from a bot reaches its teammates' consoles and not the other
team's; `ui-teammenu`'s teamplay arm.

---

### A.6 A player's score survives a rejoin (HL2DM and TF2)

Ruled 2026-11-02: "a players score should be remembered if they rejoin for
hl2dm and tf2 (last 128 players)". The stock files read here keep nothing
for a player who left, so this is ours (unverified beyond those files:
`gameinterface.cpp`, `client.cpp`, `tf_gamerules.cpp`, `tf_gamestats.cpp`).

- **What:** a core service, `RejoinScores`, in the mod-neutral server. When a
  human disconnects (`IClientLifecycle.OnClientDisconnected`) it stores their
  score, and on their next connect in the same match it writes it back before
  their first scoreboard row is sent: frags and deaths for HL2DM, and through a
  small seam the mod's own totals for TF2 (`TFPlayerStats`: score and the stats
  the scoreboard shows). A rejoin gets the score, not their team, class or
  position: those go through the join rules, because a team may have filled.
- **How many:** the last 128 players to leave, oldest dropped first, one entry
  per person (leaving twice replaces). In memory only; a server restart forgets.
- **Who:** keyed by SteamID64. A client without one (`sv_lan 1 -hushsteam` local
  games, where it may read 0) is keyed by its network ID string or, failing that,
  its name, and a keyed-by-name restore says so on the console. Bots are never
  stored: the roster already owns them.
- **When it clears:** a new match. Choice made here, since scores reset then
  anyway: a map change, a mode change and a match restart (`mp_restartgame`,
  TF2's round-reset that zeroes the board) clear the table, so a score is
  never brought into a game that started after it. A TF2 round inside one
  match keeps it.
- **Team score:** a player's frags return, but their team's total doesn't gain
  them twice. HL2DM's TDM team score (A.3) is its own counter: restore doesn't
  add to it.
- **Facts:** leave and rejoin restores the numbers; the 129th departure drops the
  oldest; leaving twice keeps the latest; a new map clears; a bot isn't stored;
  a restore doesn't change a team total. Each red once, with an arm. TF2 has its
  own fact for its totals. **Gate:** a `rejoin-score` step, a client gets a kill,
  disconnects and reconnects, and reads its row; both mods, with a second map
  load showing it clear.
- **Where it lands:** first thing in P1, on its own commit; it depends on nothing
  else here and can land early if the user wants it.

### A.7 Teammates walk through each other

Ruled 2026-11-02: "team members can walk through each other". This is a choice
against stock HL2DM, whose `CHL2MPRules::ShouldCollide`
(`hl2mp_gamerules.cpp:901`) has no team test, so teammates block there; it goes in
`docs/divergences.md`. TF2's stock has it already (`tf_avoidteammates`, default 1,
`tf_gamemovement.cpp:44`); the convars exist here (`TFSharedConVars.cs:22`), but
a grep finds nothing that reads them yet, so TF2 needs this built too.

- **What changes:** the player-movement collision test skips a player on the same
  team. It runs on both sides, in the same shared code, or prediction misjudges
  and the client snaps. A mod-neutral seam: the active mode answers "do these two
  block" (`IGameMode`), so deathmatch stays solid, TDM and TF2 pass through.
- **Only movement.** Bullets, blast and physics don't change. A teammate still
  stops a bullet as stock does, with friendly fire deciding the damage (Q1), so the
  bots' "no firing through a teammate" gate (B.5) stands.
- **Standing inside one another:** a soft push-apart when overlapped, as TF2's
  `tf_avoidteammates_pushaway`, so a spawn or doorway doesn't leave two players
  stacked. Also: spawn checks and spawn frag (`SpawnPoints`) keep treating a
  teammate as taken, and a spawn frag doesn't kill a teammate: pick another spot.
- **A setting:** "teammates collide" joins the mode's settings (D.2), default off,
  effect at once.
- **What it means for bots:** `GiveWay` and head-on handling apply to enemies only;
  a teammate is neither an obstacle in `NavObstacleField` nor a cause of stuck
  detection, and Stick or Follow may overlap in a corridor. The push-apart must not
  read as being stuck.
- **Facts,** each red once with an arm, on both sides: two teammates pass; two
  enemies still block; a deathmatch pair still block; predicted and server positions
  agree through a pass; bullets still stop on a teammate. Gate: `tdm-bots` walks a
  client through a teammate bot and into an enemy one.

### A.8 A body keeps the team it died on

Ruled 2026-11-02: "make sure the body of the player that swapped team keeps the
team color they used to be on". A swap kills the player (A.3) and then moves them,
so the body must not change colour or model when its owner's team does.

- **The rule:** a corpse holds its own copy of team and model from the moment of
  death, and nothing on the client reads the owner's current team to draw it.
- **HL2DM:** the server already copies the model name at death (`Corpses.cs:199`),
  so the Combine or Rebel model should hold, but `PlayerModels.TeamChanged`
  (`PlayerModels.cs:160`) re-dresses the player immediately after, and stock's client
  ragdoll takes its model from the player it's linked to
  (`CreateHL2MPRagdoll`, `c_hl2mp_player.cpp`). Check that our client's
  `ClientSsRagdoll` can't pick up the new look, including a late-joining client
  and one who first sees the body after the swap.
- **TF2:** stock networks the body's own team on it (`m_iTeam`, set at creation,
  `tf_player.cpp:15625` and `:15872`, where a disguised Spy's body wears the disguise
  team, `:15877` otherwise), and the client reads that (`GetRagdollTeam`). Check that
  our mod does the same: its material proxies read team colour from the player
  (`TFMaterialProxies.cs:141`, `:249`, `:262`), and any path where a corpse, its gibs
  or attached items reach those would turn the body the new colour. A hazard, not yet
  a confirmed bug; check it first, because TF2 can swap teams today.
- **Facts,** each red once with an arm, in both mods: a body made before a swap keeps
  its model and team colour after it; a client that first sees the body after the swap
  sees the old colour; a Spy's body wears the disguise team as stock; the player's new
  look is on the player only. A live screenshot pair, opened: a body beside its
  owner's new team colours. Gate: part of `tdm-bots`' swap arm.

## Part B: what the AI needs for teams

### B.1 Where "everyone is an enemy" lives today

The core carries no notion of a team at all (`grep -rn -i team src/ai/core`
finds nothing). The HL2MP binding's two team references are chat's
`CanHear(..., chat.TeamOnly)` (`Hl2mpBotChat.cs:340`) and `say(..., teamOnly:
false)` (`:391`). So every assumption is implicit:

| layer | the assumption | where |
|---|---|---|
| perception | `ActorFacts(Id, Origin, Velocity, WeaponSeen, Activity, VisibleFraction)` has no team; `SelfState` has none (`src/ai/core/Snapshot/PerceivedView.cs:22,52`) | the view |
| memory | every `KnownActor` is an opponent: confidence, ammo estimate, habits, grudge (`src/ai/core/Memory/Blackboard.cs:24`, `BotMemory.cs`) | the blackboard |
| deciding | `ChooseTarget` walks every known actor (`Hl2mpDecider.cs:535-575`); close range fires first (`CloseRange`, `:39`); Headhunter ranks every scoreboard row (`:548-550`) | the trees |
| applying | fire whenever the target is in reach; nothing between the muzzle and the target is checked except sight | `BotCommandApplier` |
| roster | bots join no team; `BotAppearance` draws any of 19 models (`BotAppearance.cs:17-54`); yielding benches the newest bot (`BotRoster.cs:349` `Reconcile`) | `Hl2mpBots`, `Hl2mpRoster` |
| chat | a bot hears what `CanHear` allows (team-aware already); it always speaks on `say` | `Hl2mpBotChat` |
| HUD events | `ScoreboardRow` carries frags and deaths, not a team (`AiEvent.cs:40`) | the senses |

### B.2 Perception: team is something a client sees

A client sees a player's team: the model (HL2DM), the colour and the
scoreboard column. So the view carries it honestly:

- `SelfState` gains `Team` (own HUD). `ActorFacts` gains `Team`: **the team a
  client at the bot's eyes would render** (in HL2DM the model's team; in TF2
  the disguise's team, C.4). The core's `ActorFactsShapeTests` member list
  grows by one, and the negative fact stays: no health, clip or reserve.
- `ScoreboardRow` events carry the team; `MatchState` carries the team scores
  (both on the HUD).
- New event `TeammateHurtMe` is not needed: `Damaged.attacker` plus the
  attacker's known team says it.
- Friend-or-foe is a pure function in core: `Friends(a, b) = a.Team >= 2 &&
  a.Team == b.Team`. Nothing else in core names a team number; the binding
  says which numbers are teams (HL2MP 2/3, TF2 2/3 by coincidence).

What does not change: sight, hearing, the trace budget. A teammate in view is
a sighted actor like any other; a sighted teammate's position is honest
knowledge (B.4 uses it).

### B.3 Memory: an enemy's memory, a teammate's whereabouts

`KnownActor` gains `Team` (as last seen) and the blackboard splits its use:

| store | enemies | teammates |
|---|---|---|
| confidence, last seen/heard | yes | yes (where my team is) |
| ammo estimate, habits, camp spots, engagement range | yes | no (never built; the arrays stay the same size, the update is skipped) |
| grudge (`GrudgeAt`) | yes | **never**: a teammate's hit under friendly fire is a `friendly_fire` moment (chat may say "watch it"), not a grudge |
| health estimate | yes | a teammate's HUD health is not visible either; the same perceived estimate applies |
| scoreboard rank | the enemy rows only (Headhunter, B.5) | own team's rows for "who is carrying" |

A player who changes team mid-round is re-classified from what every client
is told, the `player_team` event and the scoreboard row, never from the
server's store directly: B.9 has the rule for each part of a bot's memory.

### B.4 The team layer: player-equivalent sharing, off the main thread, deterministic

What a player on a team can share with teammates, and how fast:

| channel | a player's version | the bot's version | latency | what crosses |
|---|---|---|---|---|
| seeing a teammate | you see where they are and what they face | `ActorFacts` of a sighted teammate | none (the view) | position, velocity, weapon |
| the scoreboard | team score, who is alive (dead rows) | `ScoreboardRow`, `MatchState` | the HUD's update | score, alive |
| the kill feed | who killed whom with what | `KillFeed` (exists) | none | a teammate's death and its place is **not** on the feed: the bot knows only that they died |
| voice/text callout | "two in the lower hall", "sniper on the ledge" | a `Callout` event the speaking bot emits and every teammate's lane receives | `calloutDelayMs` 400–1,200 by Alertness, plus a 1-in-3 chance per sighting of saying nothing at all (people do not call everything) | enemy kind, a **place** (the nav graph's region name or the nearest pickup, never a cell id or an exact position), a count, at confidence 0.5; at most one per bot per 3 s |

No shared mind: a callout is an `AiEvent` like a `SoundHeard`, produced by a
brain step as part of its intent record (a `Say` slot beside `move/look/
action`), read by the main thread with the intent, and **re-published** as an
event to the teammates' lanes at `tick + delay`. That keeps §2.7's rule: a
brain step reads only its snapshot and its own event range; the callouts a
bot receives at tick N were emitted at or before N − delay and merged in
**slot order** by the publisher, so the stream is the same at any thread
count (the 1/2/4-thread replay fact covers it unchanged; a recording holds the
callouts as events). The plan's §2.7 line "HL2MP has no team board; TF2's will
be a per-step delta merged in slot order between steps" is this, built for
HL2DM first (**repo differs** from the plan's order: TDM needs it before TF2).

Humans hear a subset: a callout whose confidence is high goes out as
`say_team` text too, through the chat lane's normal filters, at most one line
per bot per 20 s (chat's existing cadence rules), so a human on the team gets
"Enemy at the lower hall" and can act on it. A human's own `say_team` is
parsed for the same vocabulary by the chat lane (B.5's orders).

### B.5 Deciding with teammates: targets, fire discipline, roles

**Targets.** `ChooseTarget` skips friends. Headhunter ranks enemy rows only.
Close-range-first stays, enemies only.

**Not shooting a teammate.** The applier's fire gate gets one more check, as
a player's eyes would: if the **sighted** actor nearest the crosshair ray
(within the weapon's angular half-width + 3°, `AimCone`'s number) is a friend
and nearer than the target, hold fire this tick (`HeldFireFriend` counter, a
jank part). Under `mp_friendlyfire 1` the gate also applies to a friend
within the splash radius of an explosive (the bot knows its own weapon). No
trace, no server truth: only sighted actors. A friend hit by splash anyway is
a `friendly_fire` moment and counted.

**Roles** are behaviour-tree branches that exist only when the mode has
teams (the HL2MP tree gets a `Team` subtree; the DM tree is unchanged, held by
the existing scenario recordings replaying byte-identical):

| role branch | when | what it does | reads |
|---|---|---|---|
| Stick | a teammate known within 1,024 u and no fight | keep within `leashUnits` (Teamwork, B.7) of the nearest teammate; walk, do not shadow | teammate positions |
| Cover | a teammate is engaged (their `SeenFire` or a callout) | take a cell that sees the teammate's enemy's believed position from another angle (`NavSight`, as Ambush does), fire on sight | the team's callouts, own memory |
| Cover a pickup | the camp subtree (tactics branch) with a teammate | one holds, the other roams; the holder's spot is the one a teammate is not watching | the camp planner |
| Crossfire | approaching a known enemy with a teammate on the same path | take the alternative route (`routeAltWeight`, Cunning) so the two arrive from two doors; a callout "going around" | `ChokeMap` / routes |
| Trade | a teammate died in sight to a sighted enemy | push that enemy now while they are likely reloading or hurt (the ammo window logic exists) | kill feed + sight |
| Chase with a partner | hunting with a teammate near | the nearer one leads, the other covers the exits (the follower's flow field from the enemy's predicted cell) | the position predictor |
| Regroup | own death count on the team rising and no teammate in sight; or a **no-progress call** (below) | go to the team's believed centroid (a named region), or the called rally spot | callouts, sightings |
| Push | the rally has enough (below) or its timer ran out | go for the objective together: the same corner list, the leader's pace | callouts |

**Regroup and push on no progress** (ruled 2026-11-02 for TF2, C.6.1; the
generic half is here so HL2DM's TDM gets the simple version). "No progress"
in TDM is losing on the scoreboard's team score with no team kill in the
last 90 s. A bot that reads it (its own HUD) and has Teamwork ≥ 4 makes a
**rally call** through the callout channel (B.4, with its delay): a place
it chooses from what it knows (its team's believed centroid, else a choke
it holds, else a spawn-side spot out of the enemy's last-seen sight). Bots
that hear it and are not fighting (close-range firing comes first) or about
to take an objective walk there; a low-Teamwork bot comes late (its
`followSeconds` scaled) or not at all (Teamwork ≤ 2 ignores the call). When
three have gathered within 384 u, or `rallyTimeoutS` (25 s) runs out, the
caller calls **push** and they go together. Cooldown: no new rally within
`rallyCooldownS` (120 s) of the last push, per team; the caller is the
lowest slot that read no progress, so replay holds at any thread count;
the rally spot and the exact call tick carry a draw from the team's seed
(`hash(matchSeed, team)`) of ±10 s so it does not look scripted.
| Follow / help a human | a human teammate said "follow me" / "help" / "go" / "hold" in `say_team` (a small vocabulary, 2 matches per word list), or Teamwork ≥ 7 and a human is near | follow at 200–400 u for `followSeconds`; "help" is Cover on that human; "go" releases | chat lane |

Commitment numbers (§5.5's `BtCommit`) for each branch are set at Phase 2 of
§6 with the measurement that tunes them; the plan does not invent them here.

### B.6 The roster: team assignment, keeping teams even, yielding the right slot

- `ai_bot_add <name> [skills=…] [team=auto|2|3]` and the roster spec gain
  `team=`; default `auto` = the smaller team by the mode's own rule
  (the bot sends `jointeam 0`, as a player would: the policy decides).
- Even teams: `Hl2mpRoster.Reconcile` keeps `|count(2) − count(3)| ≤ 1`
  counting humans, by assigning the next bot to the smaller team and, when a
  human switches or joins and the teams go uneven by 2, **re-teaming the
  newest bot** on the larger team (through `jointeam`, so it dies and
  respawns as a player would). Humans are never moved: a bot moves. That is
  the autobalance HL2DM gets (A.3 item 3).
- Yielding: when a human joins and the room is gone, bench the newest bot on
  **the team the human landed on** (the human's auto-assign then keeps the
  count); when a human leaves, the oldest benched bot returns to the smaller
  team. The `RefusedOnLevel` / benched machinery is unchanged.
- Create-server (`src/ai/hl2mp/ui/BotsPage.cs`): under a team mode the page
  shows "per team" counts (N Combine, M Rebels, or "even"), written as
  `team=` in the roster lines `CreateServerModel.cs:146` already emits.
- `BotAppearance` under a team mode is the team's set: the bot sets
  `cl_playermodel` to a Combine or Citizen model (the server re-dresses on a
  team change anyway, `PlayerModels.TeamChanged`).

### B.7 Skills: one more row, read by style

`SkillProfile` is a named vector, not a fixed ten (plan §10). For team modes
add **Teamwork** (0 = lone wolf, 10 = glued; Appendix B's "Soloness"
inverted so that a higher level reads as more of a virtue, like the other
nine). It drives `leashUnits` (1,500 → 300), `calloutChance`, `followSeconds`,
`coverWeight` for teammates, and the Crossfire/Trade utilities. The pool stays
60 with cap 10 (eleven rows, 110 possible, so a full pool leaves 50 on the
table). Under the core's deathmatch rules it drives nothing, and the spread's
favourite draw excludes it, so a preset bot made for DM does not waste points
on it; a named custom profile keeps what the user spent. Style (the name's
"best trait" and the tactics branch's "relative to a bot's own mean") reads
Teamwork like any other row: a bot whose Teamwork sits furthest from its own
mean is "Sticks Close" or "Lone Wolf" in the name pools. Open question §8.2 Q4
(whether the eleventh row is right, or a mode-only profile).

### B.8 Testing Part B

**Map.** `ss_ai_tdm` (room library, committed `.vmf` + `.yaml` under
`src/ai/tests/` like the six `ss_ai_*`): two bases with team spawns, a mid
room with a pickup, two routes between (one choke each), so Crossfire has two
doors. Built per tree by `make ai-maps` (`ssmap room` + `ssmap link
-require-nav`, `Makefile:682-695`).

**Scenario facts** (`tools/ai-scenario tdm`, recordings replayed by a
`SsAiTdmScenarioTests` beside the five existing scenario fact classes): two
bots a side, scripted enemy drives, each with a negative arm:

| fact | number it must hit | negative arm |
|---|---|---|
| a bot never fires with a sighted teammate nearer on the crosshair ray | 0 of N such ticks over the recording (N > 100 by construction: the teammate is driven across the line of fire) | remove the gate: N > 0 |
| callouts arrive at teammates and not enemies, delayed | every `Callout` event's receive tick − emit tick within 400–1,200 ms; 0 reach the other team | delay 0, or enemies receive |
| teamwork is monotonic | mean distance to the nearest teammate at Teamwork 2 > 6 > 10 (the inline simulation of §8.2, like the ten monotonic facts) | flat curve |
| Trade pushes | after a scripted teammate death in sight, the surviving bot's branch is Trade within one decide | drop the branch: never |
| re-team keeps teams even | a scripted human join on team 2 with 2v2 bots re-teams one bot within `RecountTicks`; the human is never moved | drop Reconcile's arm |
| yield from the right team | a human joining with no room benches a bot from the team the human landed on | bench the newest regardless |
| determinism with callouts | the recording replays byte-identical at 1, 2 and 4 threads (the existing `tools/ai-replay verify`) | reorder the merge |

**Jank plus a team measure.** `tools/ai-replay jank` gains a team block,
reported per actor and not in the headline until measured: team-kills per
alive minute (with `mp_friendlyfire 1`), ticks fire was held for a friend,
share of alive time within 512 u of a teammate, callouts per minute, share of
engagements begun with a teammate within 512 u. The standard run `make
ai-jank MODE=tdm` on `dm_lockdown` (four bots, 2v2, the scripted client on
a team). Budget after three runs: the existing `AI_JANK_BUDGET` 60 holds for
the headline; team-kills under 0.2 per bot-minute at the Medium preset with
friendly fire on, 0 with it off (that one cannot fail; it is reported, not
gated).

**Live gate steps** (`tools/live-gate`): `tdm-bots`: 2v2 on dm_lockdown for
two minutes, team scores both move, no handle fault, callouts seen in the
recording; then `changelevel ss_ai_tdm`, bots respawn on team spawns and the
teams stay even after the gate's human switches sides (the **second map
load**).

---

### B.9 A player changes team mid-game

Ruled 2026-11-02: "ai needs to handle a player swapping team". Friend or foe is
never remembered as a fact about a person: `Friends()` reads the team on the
current row each decide, so a swap takes effect at the next snapshot. What a
player would see: the scoreboard row and the model change, and the swapper dies
(the suicide on switch, A.3), so a bot has an honest cue. It learns the new team
from the `player_team` event every client gets (`Teams.cs:346`), not from the server's truth earlier.

| | foe becomes friend | friend becomes foe |
|---|---|---|
| **Target, aim, prefire** | dropped at once, in the same decide; a bot in a burst stops firing (the friend-on-crosshair gate, B.5, also holds) | a target only once seen, through normal recognition |
| **Beliefs, last seen, ammo estimate** | cleared: nothing to track | starts empty, except what it just knew as a teammate's position, kept as a plain last-seen: a person remembers where a former teammate was |
| **Grudge and habits** | grudge dormant while friendly, resuming at its faded value on a swap back; habits kept, since they describe the person | same |
| **Callouts** | callouts about them in flight are dropped on arrival; they stop receiving ours and start sending | stop being listened to at the swap; their last callouts aren't retracted, since a person wouldn't forget them |
| **Roles** | a Cover, Follow or Crossfire pairing with them re-picks at the next decide | any pairing with them ends at once |
| **Headhunter and scoreboard** | a leader on your team isn't a target | a leader on the other team is, at the next rank read |

Also: **going to or from spectator** is the same event (a spectator isn't an
opponent; ruled 2026-11-02). **A bot itself swapped** (roster balancing, TF2's
autobalance, a vote's scramble): it behaves as any player, its own beliefs reset
for the new friend list, its role and goal end, and it respawns where the mode says;
its name, skills and grudges stay. **TF2's disguise** isn't a swap: a disguised Spy's
team reads as disguised from what's rendered (C.4), and only a real team event
counts. **A swap during a fight or a team-kill window** must not count the swapper's
earlier hits as friendly fire or a grudge. **Determinism:** the swap is an event in
the ring, so replay holds at 1, 2 and 4 threads.

Facts, each red once with an arm: a bot firing at a swapper stops in the same decide
and fires at nothing friendly; a swapped foe is engaged only after recognition; no
grudge grows while friendly and it returns on a swap back; a Follow pair re-picks;
a callout from a swapped player is ignored; Headhunter changes target; a bot swapped
by the roster keeps its skills and name. Gate: `tdm-bots` gains a swap arm, a client
on team A swaps to B and back while two bots watch.

## Part C: TF2 with class-based AI and its game modes

### C.1 Ground truth of the TF2 mod at `6dc0e4029`

Read by a subagent from `src/tf2/` (736 `.cs` files) and `docs/divergences.md`;
spot-checked by grep this session (every path below exists with the
identifier named).

**Assemblies.** `TF2.Server` (`src/tf2/server`), `TF2.Client`, `TF2.Client.Interface` (`src/tf2/interface/TFUiSources.cs`), `TF2.Client.Ui` (`src/tf2/ui`), `TF2.Tests`; `src/tf2/shared` compiles into both sides (`TF2.Server.csproj:4`). `src/tf2_descent` (`TF2.Descent.*`) loads on top of TF2 (`game/mods.yaml:82-89`, `TF2.Descent.Server.csproj:28`) and is out of the TF2 plan's scope (`plan_tf2_port.md:51`).

**Registration.** No module class: listing in `game/mods.yaml` is the registration (`:77-80` for tf2); `[SideService]` scan plus `ISideServiceRegistrar.AddHooks` (`TFModesRegistrar.cs:9`). The HL2MP AI rides the same way (`mods.yaml:53-58`). **There is no `IBotMod` or AI binding interface**: the AI's seams are the core's `IBotPlayers` (`src/server/managed/BotPlayers.cs:15`, generic), `IPlayerCommandRunner`, and the AI core's `IDecider` (`src/ai/core/Brain/Decider.cs:39`) set per session through `AiSession.Deciders` (`AiSession.cs:133`). Nothing in `src/tf2` references `src/ai`.

**Classes.** All nine exist: `TFClass` (`TFSharedDefs.cs:30`), per-class data from `scripts/playerclasses/` at runtime (`TFClassTable.cs:25`, `TFClassData.cs:62` `MaxSpeed`), `joinclass` (`TFJoinClassCommand.cs:20`, with instant respawn in own spawn room, class limits), class applied at spawn (`TFPlayerSpawns.cs:22`), speeds (`TFMaxSpeed.cs:85`), one hull for every class (`TFViewVectors.cs:50-52`: 48 × 48 × 82, duck 62).

**Weapons and abilities** (exists / partial / missing):

| class | ability | state | where |
|---|---|---|---|
| Medic | heal beam, overheal | exists, live gate `heal` | `server/Medigun/TFMedigunWeapon.cs:66`, `TFPlayerHealing.cs:82` |
| Medic | ÜberCharge | exists in code, no gate | `TFMedigunWeapon.cs:512` `player_invulnerable` |
| Engineer | build/destroy, metal, levels, repair, carry | exists | `Buildings/TFBuildCommands.cs:45`, `TFObjectBuilder.cs:96`, `TFBaseObject.cs:76`, `TFSentryGun.cs:48`, `TFDispenser.cs:47`, `TFTeleporter.cs:87` |
| Engineer | wrangler | not audited (unverified) | |
| Spy | disguise, cloak, backstab, sapper | exists (backstab lands at the swing, a divergence row) | `Player/TFSpy.cs:24`, `TFSpyCommands.cs:26,94`, `shared/Weapons/TFKnifeBehaviour.cs:17`, `Buildings/TFSapper.cs:52`, `TFSapperCommands.cs:21` |
| Sniper | charge, headshot | exists | `TFSniperRifleBehaviour.cs:14`, `Combat/TFDamageLayer.cs:44` |
| Pyro | flames, airblast | exists | `TFShellBehaviours.cs:152`, `Projectiles/TFFlameEffects.cs:26` |
| Demoman | stickies, detonate | exists | `TFShellBehaviours.cs:24`, `TFProjectileEffects.cs:23` |
| Soldier | rocket jump, self-damage | exists | `Player/TFBlastJump.cs:51` |
| Scout | double jump | exists | `Movement/TFJump.cs:15` |
| Heavy | spin-up | exists (0.75 s) | `TFMinigunBehaviour.cs:14` |
| all | unlock weapons, sword/shield, charge | deferred (`docs/plans/tf2/deferred.tsv:15-22`) | |

**Objectives and modes.** Entities: `team_control_point` (+`_master`, `_round`), `trigger_capture_area`, `item_teamflag`, `func_capturezone`, `team_train_watcher`, `func_tracktrain` (core), `mapobj_cart_dispenser`, `tf_logic_koth`, `func_respawnroom`, `func_respawnroomvisualizer` (**solid to nobody, drawn for everyone**: `FuncRespawnRoomVisualizer.cs:24`, `divergences.md:74`), `func_regenerate`, `team_round_timer` with setup (`TeamRoundTimer.cs:686`), respawn waves (`TFSharedConVars.cs:102`, `TFRespawnPolicy.cs:14`), `tf_gamerules`. Missing: `tf_logic_arena`, `tf_logic_mannpower`, `tf_logic_medieval`, `tf_logic_player_destruction`, MvM. The selector (`TFGameModeSelector.cs:66-81`) maps flag → `ctf`, train watcher → `pl`, CP master → `koth` or `cp`, `sd_` → null, else the core's rules (**repo differs** from `divergences.md:40`, which predates CP/KOTH).

| mode | state |
|---|---|
| CTF | runs; live gate step `ctf` on ctf_2fort (`tools/tf2-gate:79`) |
| Payload | runs; live gate `payload` on pl_badwater (`:26`) |
| 5CP | mode and unit tests; no live run |
| Attack/Defend | partial: team roles not ported, no default side (`CPMode.cs:21-27`) |
| KOTH | mode exists (`KothMode.cs:31`), unit tests only |
| Payload Race | partial: `tf_logic_multiple_escort` is a HUD flag (`PayloadMode.cs:33`) |
| Arena, MvM, PD, RD, Mannpower, Medieval, Passtime | missing, deferred. **MvM is out** of this plan |

Content: `bin/debug/content/tf/maps/` holds the stock set (cp_dustbowl,
koth_viaduct, plr_hightower, arena_lumberyard, mvm_decoy among 200+). `game/
mod_tf2/maplist.txt` lists ctf_2fort, koth_badlands, cp_badlands. TF2 is not
in the daily gate (`tools/daily-gate` has no tf2 step).

**Teams.** `TFTeam` Red 2 / Blue 3 (`TFSharedDefs.cs:20`); `jointeam` with
panels (`TFJoinTeamCommand.cs:51`), `TFTeams : IJoinTeamPolicy` (unbalance
refusal), `TFAutobalance` (`mp_autoteambalance 2` machine; its bot exclusion
`CanBeAutobalanced` is not ported: `TFAutobalance.cs:32-35`), `TeamBalance.cs:33`,
`info_player_teamspawn` (`InfoPlayerTeamSpawn.cs:30`), `TFSpawnSelector`
(`src/tf2/server/Entities/TFSpawnSelector.cs`), respawn rooms with a box point
test (`divergences.md:75`), `filter_activator_tfteam` doors over the core's
`func_door` (`EntityNames.cs:98`). Door behaviour on real maps is unverified
(no gate step covers it).

**Scoreboard.** Class per player for own-team rows only (`TFScoreboard.cs:391`
`ShowsClass`), the stock gate; a spectator sees all.

**Rows a bot plan must reopen** (`deferred.tsv:3,11,40,71`): sentry
`FindTarget`'s bots pass, capture rules' bot arms, `trigger_bot_tag`, the
medigun's bot arms; and `TFJoinTeamCommand.cs:84`'s "no bots to spare".

### C.2 Layers and assembly layout

The core stays mod-neutral; the HL2MP binding today holds a great deal that
is generic server-binding code. Lift it once, before TF2:

| assembly | holds | references |
|---|---|---|
| `SourceSharp.Ai` (core, exists) | as today, plus the team primitives (B.2–B.4), `ObjectiveKnowledge` (C.6), an `Ability` one-shot in `ActionIntent` (C.3) | BCL only (the no-Abi fact stays) |
| **`SourceSharp.Ai.Server`** (new, `src/ai/server/`) | lifted from `SourceSharp.Ai.Hl2mp`: `BotCommandApplier`, `BrainDriver`, `GiveWay`, `NavFollower`, the `Nav/` loader, derivation, cache, validator, obstacle field, `Senses/` (`VisionPass`, `SnapshotBuilder`, `SoundTap`, `CombatTap`, `CommandTap`, `ShotTap`, the dev-API routes), `Commands/BotRoster`, `BotCommands` (the generic `ai_*` verbs), the recorder, the chat worker's server half | `SourceSharp.Ai`, `SourceSharp.Abi`, `SourceSharp.Server`, `Debug.Api.Server.Interfaces`, `SourceSharp.MapFormats` (+ `SourceSharp.MapTools` after Part E) |
| `SourceSharp.Ai.Hl2mp` (exists, shrinks) | `Trees/Hl2mpDecider`, `Hl2mpWeaponRanges`, `Knowledge/Hl2mpKnowledge` (weapon table from HL2MP's catalog), `BotAppearance`, `BotNaming` (if the pools stay HL2MP's), `Chat/Hl2mpBotChat`'s HL2MP vocabulary, `Hl2mpAiService`, the HL2MP taps that name HL2MP weapons/sounds | `SourceSharp.Ai.Server` |
| **`SourceSharp.Ai.TF2`** (new, `src/ai/tf2/server/`), `.Client`, `.Ui` | the TF2 decider (per-class subtrees over one tree), `TFKnowledge` (class table, weapon table from `TFWeaponTypes`, building facts), the objective feed, the TF2 taps (medigun, buildings, flags, points, carts, cloak/disguise as rendered), class selection, TF2 appearance (class model by team is the game's), the TF2 bots page | `SourceSharp.Ai.Server`, `TF2.Server`, `TF2.Client.Interface` |

`mods.yaml` lists `SourceSharp.Ai.Server` before each binding (a mod restates
the whole list). The name `TF2` in `SourceSharp.Ai.TF2` follows the user's
spelling rule (TF uppercase). The lift is a move, not a rewrite: the facts in
`src/ai/tests` move with their subjects, and the HL2MP gate steps must pass
unchanged after it (the recordings replay byte-identical).

What `SourceSharp.Ai` assumes today that is HL2MP-shaped and must be
generalised in the lift: `ActionKind` (Fire/AltFire/Reload/SelectWeapon/Use,
`IntentCommand.cs:42`) has no ability one-shot; `ItemKind` (`MapKnowledge.cs:54`)
is HL2MP's pickups (TF2 adds health/ammo packs of three sizes, which fit as
kinds); `WeaponFacts` (`:12`) has no projectile arc or charge; `SelfState` has
no class, conditions or metal; `ScoreboardRow` has no team or class;
`MoveKind` has no double jump (a `Jump` while airborne suffices if the applier
lets it) and no blast jump.

### C.3 Per-class brains: real abilities through usercmds only

A bot's one output is a usercmd (plan §12.1). TF2's abilities are all
reachable that way, plus the client commands a player's keys send
(`joinclass`, `build`, `destroy`, `disguise`, `lastdisguise`, `cloak`,
`sap`, `voicemenu`, `taunt`; all exist on the server as player commands:
`TFJoinClassCommand.cs`, `TFBuildCommands.cs:45`, `TFSpyCommands.cs:26-94`,
`TFSapperCommands.cs:21`). The core gains `ActionKind.Ability(id)`: a one-shot
whose `id` indexes **the binding's own table** (`AbilityTable`, data the TF2
binding publishes with `MapKnowledge`), so the core never names a TF2 verb.
The applier runs the row: a client command string (`build 2 0`), a button
mask (`IN_ATTACK2`), or a sequence (aim down 70°, `IN_JUMP` + `IN_DUCK`, then
`IN_ATTACK` at the ground: a rocket jump). Sequences are the applier's, on the
main thread, from the bot's own eye angles.

| class | what the brain does | abilities as usercmds | HUD facts it reads (own) | enemy facts it perceives |
|---|---|---|---|---|
| Scout | flank, cap (×2 capture rate is the game's), harass, pick stragglers | double jump = `IN_JUMP` pressed again airborne (`TFJump`); scattergun close, pistol mid | — | speed of the target (lead) |
| Soldier | push with the team, deny chokes, rocket-jump to high ground | `Ability(RocketJump)` sequence; splash aim at feet (an `AimPoint.Feet`) | own health for a jump's cost | |
| Pyro | guard the medic/engineer nest, flank, reflect | airblast = `IN_ATTACK2` timed on a seen projectile (`SightGained` of a projectile actor: projectiles become sighted actors in the TF2 view, as a client renders them) | | a Spy test: flames reveal (`TFSpy` cloak conditions are the game's) |
| Demoman | sticky traps at chokes (the tactics branch's `ChokeMap` gives the spots), area denial, detonate on a sighted enemy over the trap | `IN_ATTACK` × n then `IN_ATTACK2` | own sticky count (HUD) | |
| Heavy | hold, spin up early at corners (`Ability(SpinUp)` = hold `IN_ATTACK2`), pocket with a medic | | spin state (own) | |
| Engineer | build loop: dispenser by the team's path, sentry at a `SENTRY_SPOT`-like place the sight table rates (sees the point, hidden from the long line), teleporter entrance at spawn, exit forward; upgrade to 3 (hit with the wrench, `IN_ATTACK` on the building), repair, carry forward | `build N` / `destroy N` client commands; placement by aiming the blueprint (the game's `TFBuilderPlacement`) and `IN_ATTACK` | metal, building states (own HUD, `TFHudAccount`) | sapped: the building's model/sound (a client sees it) |
| Medic | heal the hurt teammate in sight (prefer the Heavy/Soldier being shot at), build über on a pocket, pop (`IN_ATTACK2`) when the pocket's health falls or on a push call, retreat behind | hold `IN_ATTACK` on the target; the beam's target is the game's | über % (own HUD) | a teammate's health is **not** on the HUD except through the medigun's target panel: the brain reads the target's health only while healing them (a client sees that) |
| Sniper | hold a long line (`NavSight` long lines, as camp spots), scope (`IN_ATTACK2`), charge, headshot the still target (`AimPoint.Head`, accuracy by skill), relocate when shot | | charge % (own HUD) | |
| Spy | disguise (`disguise <class> <team>`) as a class the enemy fields (seen or kill-feed), cloak (`cloak`) to cross, approach from behind (the position predictor's facing), backstab (`IN_ATTACK` with the knife behind), sap (`sap` then `IN_ATTACK` on the building), escape | | cloak meter (own) | |

**Honesty about the Spy, both ways.** The disguised or cloaked Spy is what a
client renders: `ActorFacts.Team` is the disguise's team, `WeaponSeen` the
disguise's weapon, `VisibleFraction` falls with cloak (a Spy over 75 %
invisible is not in the view, as the renderer draws almost nothing: stock's
`IsIgnored` number used as the vocabulary, `tf_bot_vision.cpp:258-302`). A bot
suspects a "teammate" only from cues a player gets: bumping into one that is
solid (a disguised enemy collides; `Touch`'s vocabulary, `tf_bot.cpp:1445`),
one that hurts the bot (`Damaged` by a friend-looking actor), one seen
placing a sapper or decloaking (an `ActorActivity` flag the client renders),
one a teammate called out ("Spy!"), and one walking the wrong way out of the
enemy's spawn (the objective knowledge says where their spawn is). Suspicion
is a per-actor counter in the blackboard with a half-life; above a threshold
the actor is an enemy for targeting and a callout goes out. No reading of
the server's real team. A fact: with the Spy's disguise on and no cue, a bot
never targets it (the negative arm forces a server-truth read and the fact
goes red).

**A Spy dressed as you.** Ruled 2026-11-02: "bots should identify spys dressed as
them selves". A disguise borrows a real player's name (`DisguiseTargetIndex`,
`TFPlayerNetState.cs:123`, networked, chosen by `FindDisguiseTarget`,
`TFSpy.cs:353`; every client sees it in the target ID, `tf_hud_target_id.cpp:248`).
A player who sees themselves walking towards them knows at once. So a bot that sees a
"teammate" whose borrowed name is its own treats them as an enemy Spy, at once and
at full confidence, whatever its suspicion skill, after the normal recognition time:
the name is read off the same rendered facts as the disguise. It also calls that out
to the team (B.4), with the delay any callout has, as "Spy dressed as me".

The same reasoning gives the other cues a person uses, in order of strength, all
from what that bot honestly knows: the name is its own (certain); the name belongs to
a player it can see elsewhere at the same moment (certain); the name belongs to someone
the scoreboard or kill feed says is dead (strong); and the class doesn't match that
player's current class as it last saw them (weak, Alertness scales it). A fact for each,
each red once with an arm, plus a negative: the same Spy disguised as someone else, with
no other cue, isn't identified. A live check: a scripted Spy takes on the bot's own name
and is shot on sight; the same Spy in another name isn't. **For this to be possible, the
borrowed name must reach `ActorFacts` as a client's own read does** (C.2's field list),
and it's the only part of the disguise that names a person: the real team and class stay
hidden.

### C.3.2 Class tactics (ruled 2026-11-02)

The ruling, by class, with what the bot does, what it reads (only what it
knows), what the mod must have first (prework, Part F) and the fact or
scenario that shows it. Everything here is bot brain (P6–P8) except the
column "needs first".

| class | what it does | reads | needs first (prework) | shown by |
|---|---|---|---|---|
| **Scout: double jump and be hard to hit** | with an enemy holding sight of it (its own `SightGained` on them plus their facing from the view), it strafes aperiodically, double-jumps across their line (a second `IN_JUMP` airborne: `TFJump.cs:15`, `AirDash` in `TFPlayerNetState`) and breaks line at corners; scaled by Agility (`strafeWeight`, `dodgeWeight`, `jumpUse`), inside §3.4's motion rules: no spin, no constant snapping, a flick only when the view target changes | own `AirDash` count (HUD), the enemy's facing (`ActorFacts` velocity and the view's eye direction, C.2's field list) | F12 (`AirDash` on the actor facts) | scenario `ss_ai_tf_evade`: a scripted Soldier bot of fixed skill fires at a Scout running a fixed route; the evading Scout is hit less than a same-skill Scout running straight, by the aim meter's hit rate (`AimMeter`), at every Agility level from 4 up; flat mutant (no evasion) red; `tools/ai-replay jank`'s yaw-reversal and strafe parts stay inside the human envelope |
| **Soldier and Demoman: blast jumps for faster movement** | on a route longer than `blastRouteUnits` (1,200 u) with no enemy sighted and health above `blastHealthFloor` (Soldier 90, Demoman 80, from `TFBlastJump`'s cost: a rocket jump costs about 40–60 health; the numbers are the game's, read once from the damage layer in a fact), it performs `Ability(RocketJump)` / `Ability(StickyJump)` toward the next corner that is reachable by air: the graph gets a **blast-jump edge class** (`JumpReach.Blast`, derived at load from the voxel data like the crouch-jump class, a ledge up to 600 u away and 400 u up with a clear arc, Soldier and Demoman only); the follower takes such an edge only when the brain says so, never in a fight unless escaping (Survive), never as a flag carrier (the game's rule drops the flag? no: a carrier may jump; the rule is tactical: the carrier does not blast-jump away from its escort), never with the clip empty. "Nade jump" for a Demoman is a **sticky jump** (one sticky at the feet, `IN_ATTACK` then `IN_ATTACK2` detonate with `IN_JUMP`+`IN_DUCK`), what the launcher can do (`TFShellBehaviours.cs:24`); a pipe (grenade launcher) jump is not built: the grenade's fuse and arc make it a trick, not a route | own health, clip, the route's length (the follower), the edge class | F12 (nothing else: `TFBlastJump.cs:51` is the game's state), the TF nav spike (F02) for the edge derivation's input | fact: a route of 2,000 u on `ss_ai_tf_jump` takes a Soldier bot under 70 % of a walking bot's time with health ≥ 150, and never under 90 health; a Demoman the same with a sticky; the carrier never jumps off its escort; mutant (edge class disabled) red. Effort: the edge derivation is a week of P5/P6 work (the arc test over voxels), the applier sequence two days; the `TFBlastJump` cost table is read, not copied |
| **Spy: avoid common paths; jump on rocks while cloaked** | prefers routes through low-traffic cells: traffic is the bot's own **route prior** over the graph (`OpponentHabits`: visit counts it has seen) plus the `ChokeMap`'s betweenness as a prior for "common" (a map's main routes are known before playing, §5.0); path cost adds `trafficWeight × traffic`; while cloaked it moves to the nearest **perch**: a cell the graph marks as a climbable prop or ledge off the walking routes (the nav's `JUMP` links to a cell with no neighbours on the main route, scored at load), and waits there for the cloak to refill when an enemy is near its route; it decloaks only out of sight | the `ChokeMap`, its route prior, its own cloak meter (HUD), the perch table (map knowledge) | F02 (nav on TF maps), F12 (`CloakMeter`) | fact: over a scenario with scripted enemies walking the main route, a Spy bot's path shares under 30 % of its cells with the route's top-traffic set, a plain bot over 70 %; a cloaked Spy near an enemy is on a perch cell at the moment the enemy passes in 8 of 10 seeds; mutants red |
| **Sniper: camp known sight lines** | at load, long sight lines over the graph: `NavSight` (`Range` 1,024 u, `NavSight.cs:32`) extended with a `LongLine` pass (2,048 u, from candidate cells the `ChokeMap` ranks and the objective feed's points and routes); a spot scores by what it sees (chokes, objectives, routes), how little sees it, and distance from own spawn; the Sniper walks to the best unclaimed spot (a callout claims it, B.4), scopes, and moves after a kill or when shot (as before) | the sight table, `ChokeMap`, the objective feed (F13/F14) | F13/F14 (objective positions), F02 | fact: the top three spots on `ss_ai_tf_cp` each see the point or a choke and are seen by under 20 % of the cells that see the point; a Sniper bot is at one of the top five spots for over 60 % of its alive time; after a kill it changes spot within 15 s in 8 of 10 seeds |
| **Engineer: build at chokes, set up teleporters** | sentry on a cell that sees a choke (`ChokeMap` top set) between the enemy and the objective and is not on the long line (the Sniper table), dispenser behind it out of the line, teleporter entrance within 512 u of own spawn's exit, exit within 768 u of the sentry; relocates when the objective feed says the front moved (a point taken, the cart past a checkpoint): pick up (the game's carry) and rebuild forward | `ChokeMap`, the sight tables, the objective feed, own metal and building states (HUD) | F13/F14 (front position), F12 | fact: on `ss_ai_tf_cp` the sentry's cell sees the choke and the dispenser's does not see the long line; after a scripted point capture the Engineer rebuilds within 1,024 u of the new front within 90 s; mutant (no relocation) red |
| **Pyro: spy-check with the flamethrower; light arrows** | puffs (a 0.2 s `IN_ATTACK`) at teammates passing within 128 u at a rate `spyCheckRate` by Alertness, more at cells the `ChokeMap` marks as entries from the enemy side and more while a Spy is suspected (C.3's counter); a burning "teammate" is the honest reveal (the game's cloak/disguise rules under fire); **lights arrows**: stands within 96 u of a teammate holding a Huntsman and puffs when they draw (`ActorActivity` shows the draw), which sets the arrow alight as stock does (`tf_weapon_flamethrower.cpp:3142` `SetArrowAlight` on a teammate's bow caught in the flame; `tf_weapon_compound_bow.cpp:125` passes it to the arrow; `tf_projectile_arrow.cpp:486` burns on hit) | sighted teammates, their weapon (`WeaponSeen` = bow), the suspicion counter | **F20: the mod has no arrow lighting** (`grep -rn Alight src/tf2` is empty; the bow exists as a behaviour-free `tf_weapon_compound_bow` row) | fact: a disguised scripted Spy walking past a Pyro bot is burning within 3 s in 9 of 10 seeds at Alertness 6, 5 of 10 at Alertness 2; a Huntsman teammate's arrow leaves lit after the Pyro's puff (`m_bArrowAlight`'s port read on the projectile); flat mutant red |
| **Spy checks by walking** | at random (`walkCheckChance` by Alertness, once per 20–40 s, ×3 while a Spy is suspected), a bot steers through a teammate it is passing anyway (no detour over 64 u): under A.7 a teammate passes through, an enemy in disguise **collides** (the collide rule reads the real team: F04/F05's fact "a disguised enemy still blocks") so a blocked walk is the honest cue: suspicion up, callout. Excluded from jank: the window is tagged in the recording (`WalkCheck` intent flag), and the jank tool skips wall-bump and stuck inside a tagged window of at most 0.5 s; A.7's push-apart applies only to true teammates, so it cannot mask a collision | sighted teammates' positions, own velocity (a stop under 20 u/s against a "teammate" for 0.3 s) | F04, F05 (the collide rule and its real-team fact) | fact: a disguised scripted Spy standing in a corridor is suspected by a passing bot within one walk-check in 8 of 10 seeds; a real teammate never raises suspicion; the jank score's wall-bump part does not rise with `walkCheckChance` (mutant: untagged windows make it rise) |

Which are bot brain, not prework: every row's behaviour (P6/P7); the blast
edge class and the perch and long-line tables (P5/P6, the nav layer). The
prework is the mod's arrow lighting (F20), the collide rule and its
disguise fact (F04/F05), the actor facts (F12) and the objective feed
(F13/F14).

### C.3.1 Sub-classes: a loadout and a way of playing

Ruled 2026-11-02: "tf2 ai should have sub classes for each class that picks a
different load out and changes how it acts". A class is what a bot can do; a
sub-class is which of those it leans on, as players do. The names below are the
community's, not Valve's, from general knowledge of the game checked where possible
against the wiki's community strategy pages (sources at the end of this section);
community pages are old in places, so a name or a fit may not match current balance.

**What a sub-class is:** data in `tf2_subclasses.json`, one row per sub-class: the
class, the loadout (a preference list per slot, stock weapon last as the fallback),
spread weights over the skills, and overrides on the class's tree and the mode's
goals. It is not a new tree per sub-class, and it adds no points to the pool (C.5).

**Choosing one:** the bot's seed draws at spawn, weighted by its favourite skill
(C.5), what the team lacks and the map and mode (C.4, C.6). Holds until it changes
class or the map changes; `ai_bot_reroll_on_map` redraws it. A custom bot may pin one
in the editor. **Choosing a weapon goes through the player's path:** the
`TFLoadoutProvider` and `ItemIsAllowed`, so a mode's restriction binds a bot too.
Bots have no SteamID file (`InventoryStore` is per SteamID64), so their items come
from an in-memory source behind the same provider seam. **What others perceive:** a
bot reads an enemy's sub-class only from the weapon it sees or the kill feed (C.4),
never a label: "that Demo has a sword".

**The name and tag:** the phrase pools gain sub-class phrases ("Tommy Pocket (Hard)",
"Pia Airblast (Easy)"); the tag stays the difficulty.

**Item status at `6dc0e4029`:** the economy reads `items_game` and hooks attributes
(`src/tf2/shared/Econ/AttributeHooks.cs`), but many unlocks' behaviours are not there.
The status column is a grep over `src/tf2` for the item's attribute or weapon class, so
"not found" means **not found by that name**, to be audited in P7, and "stock" needs
nothing new. A row whose items don't work falls back to its stock row; none blocks a
class shipping.

| Class | Sub-class | Loadout | How it acts | Items |
|---|---|---|---|---|
| Scout | Flanker | Scattergun, Pistol, Bat | goes round, picks stragglers and Medics, caps at ×2 | stock |
| Scout | Bonk runner | Bonk! Atomic Punch | drinks to run past sentries and chokes, caps behind | not found |
| Scout | Harasser | Sandman, Force-a-Nature | stuns from range, jumps high, never stands | not found |
| Soldier | Pocket | Rocket Launcher, Shotgun, Shovel | stays with a Medic and pushes with them | stock |
| Soldier | Roamer | Rocket Launcher, Gunboats, Escape Plan | rocket-jumps ahead, kills the backline, retreats on low health | jump stock; items unaudited |
| Soldier | Banner | Buff Banner or Battalion's Backup | builds rage in the fight, pops it with the team | not found |
| Soldier | Direct Hit | Direct Hit | holds range and picks targets, airshots, kills sentries | weapon class found |
| Soldier | Trolldier | Market Gardener, Gunboats | jumps at people and hits them with the shovel | crit-airborne found |
| Pyro | Ambusher | Backburner | waits round corners, comes from behind | not found |
| Pyro | Airblast guard | Flamethrower, Shotgun, Homewrecker | stays by sentries and teammates: reflects, extinguishes, checks for Spies | stock; Homewrecker unaudited |
| Pyro | Combo | Degreaser, Flare Gun, Axtinguisher | lights, switches, finishes up close | Flare Gun found; others not found |
| Demoman | Hybrid | Grenade Launcher, Stickybomb Launcher, Bottle | pipes in the fight, stickies on chokes | stock |
| Demoman | Trapper | Scottish Resistance | sets traps at the objective and detonates on sight | not found |
| Demoman | Demoknight | Chargin' Targe, Eyelander | no launchers: charges from the flank, picks Snipers and Spies; a poor Medic pocket per the wiki | shield found; sword class found, Eyelander's heads not found |
| Heavy | Pocketed | Minigun, Shotgun, Fists | front of the push with a Medic, holds the spin | stock |
| Heavy | Anchor | Natascha | slows what it hits, holds a chokepoint, protects the Medic | not found |
| Heavy | Ambusher | Tomislav | spins quietly at corners, moves between fights | spin-up attribute found |
| Heavy | Siege | Brass Beast | digs in on the objective, rarely moves | aim-move attribute found |
| Engineer | Nest | Stock tools, Wrangler | sentry, dispenser, teleporter on a defended spot; repairs under fire; wrangles | stock; Wrangler not found |
| Engineer | Gunslinger | Gunslinger, Frontier Justice | quick mini-sentries at the front, moves up, fights with a gun | not found |
| Engineer | Road builder | Stock tools, Eureka Effect | forward teleporter first, gets the team to the fight | stock; Eureka unaudited |
| Medic | Über | Medi Gun, Ubersaw | pockets, builds and calls Über, pushes with it | stock; Ubersaw unaudited |
| Medic | Kritz | Kritzkrieg | saves crits for a grouped enemy, with a Soldier or Demo | not found |
| Medic | Quick-Fix | Quick-Fix | follows a jumper's jumps (per the wiki), keeps moving | not found |
| Medic | Vaccinator | Vaccinator | changes resistance to what's shooting; hard | item name found once |
| Sniper | Marksman | Sniper Rifle, SMG, Kukri | holds a long sightline, moves after a kill | stock |
| Sniper | Huntsman | Huntsman, Jarate | fights at medium range and in corridors, keeps moving | bow found; Jarate name found |
| Sniper | Guarded | Sniper Rifle, Razorback | holds a spot Spies come to, checks behind less | item name found once |
| Spy | Backstabber | Revolver, Knife, Invis Watch | disguises, gets behind, stabs the Medic or Sniper first | stock |
| Spy | Dead Ringer | Dead Ringer | fights openly, feigns on being hit, gets away | feign found |
| Spy | Infiltrator | Cloak and Dagger | stays cloaked behind lines, waits for the moment | cloak-rate attribute found |
| Spy | Ambassador | Ambassador | shoots heads at range when spotted | item name found |
| Spy | Saboteur | Sapper first | saps nests and teleporters with the team's push | stock |

**Sized honestly:** the stock rows ship with P7. Each unlock that isn't there is a port of
that item, not bot work: order them by what the sub-class needs and do them in lanes
alongside P7 (Direct Hit, Flare Gun and the bow first, since they exist). P7's 6–8
weeks covers the stock rows and existing items; the full table is more, and depends on
how many unlocks the TF2 work has when P7 starts.

**Any item the class supports (ruled 2026-11-02).** A bot is not limited to the
table's items or to anything unlocked: every item in the schema its class can
equip is allowed, no drops, no ownership. The in-memory item source behind
`TFLoadoutProvider` (`src/tf2/server/Econ/TFLoadoutProvider.cs:46`) answers,
for a bot, every class-equippable item whose behaviour **works in the mod**;
`ItemIsAllowed` (`:198`, stock `tf_player.cpp:4416-4485`) still binds, so a
mode's restriction holds for bots too. What that changes:

- The sub-class table is the set of curated, named ways of playing; a row's
  loadout is a preference list that may name any item. It is not an allow-list.
- A slot a sub-class does not name is a **seeded draw** from the items that
  work for that class and slot, uniform, the same at every difficulty (the
  "nicer weapons by difficulty" reading was dropped, §8.1). A pinned custom
  bot keeps its choice; `ai_bot_reroll_on_map` redraws.
- "Works in the mod" is **data the loader reads**, not a grep: the audit
  stage (F17/F18) writes `game/mod_tf2/scripts/items/items_working.tsv`
  (defindex, name, item_class, slot, classes, attribute classes, status
  `works|partial|missing`, evidence), the bot source draws only `works`
  rows, and a fact holds that no `partial` or `missing` item is ever drawn.
  A port stage flips a row to `works` in the same commit as the behaviour.
- **How the AI understands an arbitrary item:** `WeaponKnowledge` (the
  binding's table, §5.0) is built from the schema plus the class scripts for
  any item, never a hand list: `item_class` gives the weapon kind (its
  behaviour row in `TFWeaponTypes`), the script gives clip, cadence, reload
  and projectile, the attributes adjust them (`mult_clipsize`,
  `mult_postfiredelay`, `mult_projectile_speed`, …) through the same
  `AttributeHooks` the weapon runs on. Families that need their own handling
  are flagged in the table's `family` column: passive items
  (`tf_wearable_razorback`, Gunboats and other `provide_on_active`
  wearables), shields (`tf_wearable_demoshield`), banners
  (`tf_weapon_buff_item`: rage), meters (`item_meter_charge_rate`), drinks
  and lunchboxes, the PDA and builder rows; a flagged family a brain does not
  understand is not drawn for that bot until its subtree reads it.
- **The counts** (this session, a prefab-resolving parse of
  `bin/debug/content/tf/scripts/items/items_game.txt` written to the
  scratchpad, no file committed): 498 class-equippable weapon-slot items
  (primary 203, secondary 156, melee 123, pda2 8, building 8; by class Scout
  75, Pyro 67, Heavy 67, Engineer 67, Soldier 62, Demoman 61, Spy 60,
  Sniper 59, Medic 40), which are **179 distinct behaviours** once
  warpaint and strange variants are folded (same `item_class`, same
  behaviour attributes), across 70 `item_class` families and 279 behaviour
  attribute classes, of which **212 are named nowhere in `src/tf2`**; 48
  behaviours have every attribute named in the mod. So the audit covers 179
  behaviours, not C.3.1's 33 rows; the port stages stay ordered by what the
  sub-classes need (about 22 behaviours, Part F's F21–F32), and the rest,
  about 110 behaviours over 200 attribute classes, is a long tail: at one
  attribute family per lane session (2–6 classes each), **40–60 lane
  sessions**, three to six months of six lanes at the current finish rate.

### C.3.3 Looks by difficulty (ruled 2026-11-02: "nicer" means looks)

A player should read the danger: Easy bots look plain, Expert bots
decorated. Seed-drawn with the loadout, kept for the map unless re-rolled, a
pinned custom bot keeps its look. Cosmetics never change a hitbox or how a
bot plays (a fact: the hull and the weapon facts are identical with and
without the cosmetics).

| tier | Easy | Medium | Hard | Expert |
|---|---|---|---|---|
| hats (`head`, `misc` slots the class can wear: 190 head, 96 misc in the schema) | none | one hat | hat + misc | hat + misc |
| weapon quality names (the item's quality in its displayed name: Strange, Unusual prefixes, `TFItemStrings.cs:143`) | Unique | Unique | one Strange | Strange + an Australium-looking primary where the schema has one |
| unusual effects | — | — | — | one, if the renderer can show it |

What the mod can show today: the schema's qualities parse
(`ItemSchema.cs:99`), wearables exist on the server (`TFWearables.cs:58`,
`TFWearableNetState`) but **nothing in `src/tf2/client` or `src/tf2/ui`
draws a wearable** (a grep for `Wearable` there is empty; the client
bone-merge render is a deferred row, `deferred.tsv` area `wearables`), and
no particle attachment exists for unusuals. So the looks need F37 (wearable
rendering, not for local models) before a hat is visible, and F38 (quality
names in the scoreboard and target ID) for the names. Until then the draw
exists as data and a fact, and shows nothing. Facts: over 500 seeds the
share of decorated bots is monotonic Easy < Medium < Hard < Expert (flat
mutant red); the same seed gives the same items; only `works` items and
wearables the class can wear are drawn.

**Tests:** a fact per row on a spawn fixture: this sub-class holds this loadout and the
stock fallback when the item isn't allowed. Monotonic style facts per sub-class (a Pocket
stays within N u of its Medic; a Roamer's distance from its team is several times that; a
Trapper's kills sit near its stickies), and flat mutants red. A scenario per class that
shows two sub-classes acting differently, using the jank tool's "how each actor plays"
block. One live game per class with its sub-classes, opened screenshots.

Sources for the names: the [Official TF2 Wiki community strategy pages](https://wiki.teamfortress.com/wiki/Community_Soldier_strategy) ([Demoman](https://wiki.teamfortress.com/wiki/Community_Demoman_strategy), [Engineer](https://wiki.teamfortress.com/wiki/Community_Engineer_strategy), [Medic](https://wiki.teamfortress.com/wiki/Community_Medic_strategy)), [Steam community discussions](https://steamcommunity.com/app/440/discussions/0/4349988380668998730), and for Spy's watches [the TF2 wiki on Cloak](https://wiki.tf2.com/wiki/Cloak) and [PCGamesN](https://pcgamesn.com/tf2/relearning-play-spy). "Pocket", "Roamer", "Trolldier" and "Battle Medic" were not confirmed by those searches; they are the community's words from general knowledge.

### C.4 Class selection, composition, and learning the enemy's classes

- A bot picks a class with `joinclass` when it has none, and weighs a change
  on each death under the shared cooldown (below). That replaces stock's
  always-or-never `tf_bot_keep_class_after_death` as our rule. A bot doesn't
  also change in its spawn room mid-life (stock's
  `tf_bot_reevaluate_class_in_spawnroom`), since that would go round the clock.
- Composition is a **team roster target** the binding keeps per team: the
  stock vocabulary's offense/defense/comp presets (`tf_bot.cpp:1026-1060`:
  twelve entries each, our own tables, not copied) by the mode's role for
  that team (attacking in A/D or Payload, defending, symmetric), as weights;
  the next bot to spawn picks the class furthest below its weight, with a
  medic once the team has four and an engineer on defence. Humans count in
  the composition (a bot sees its own team's classes on the scoreboard, the
  stock gate `ShowsClass`).
- Enemy classes are learnt as a player learns them: sightings
  (`WeaponSeen`/model → class), the kill feed's weapon icon, callouts. A
  sentry seen (a building actor in the view) raises Demoman/Spy/Soldier
  weights for the next picks (stock's counter in vocabulary: `tf_bot.cpp:1196-1240`);
  three Snipers seen raises Scout/Spy. The reaction is a weight change
  with a cooldown, never a whole-team re-roll.
- The scoreboard tag "(Medium)" stays; the class icon is the game's.

**Changing class on respawn.** Ruled 2026-11-02: "bots should evaluate changing
class on respawn but have a global cool down for all bots to not thrash". A dead bot
weighs a change once per death, while it waits to respawn, as a player does at the
class menu: what the team lacks now (composition targets above), what the enemy
fields (sightings and kill feed), what killed it, and the mode's state. The current
class gets a stay bonus, so a change has to be clearly better.

- **The global cooldown:** one server-wide clock, shared by every bot on both teams.
  When any bot changes class, no bot may change until it runs out: `ai_bot_class_cooldown`,
  default 30 s. Bots that wanted to and couldn't simply respawn as they are, and weigh it
  again next death. It's global as ruled, not per team; if one team hogging it shows up in
  the facts, per-team is a later setting.
- **Also per bot:** a bot won't change again for 120 s (`ai_bot_class_stay`), so the same
  bot doesn't take every window. Setup time, and a first pick on joining, don't count and
  don't start the clock.
- **Who gets the window:** when several want a change in one tick, the biggest team need
  wins, ties to the lowest slot, on the main thread, so replay holds at any thread count.
- **Only through the player's path:** the `joinclass` command (`TFJoinClassCommand.cs`),
  taking effect at respawn with the same rules as a person, including class limits. A new
  class redraws the sub-class (C.3.1) by seed; the name's phrase stays until the map changes,
  so a scoreboard name doesn't flicker.
- **Levels:** a bot keeps its skills and reads the new class's curves (C.5). A pinned custom
  bot never changes.
- **Facts,** each red once with an arm: two bots dying together make at most one change
  inside the window; the clock is shared across teams; a bot inside its own stay doesn't
  change; a clearly better need wins the window; with no need nobody changes; a change takes
  effect at the next spawn, and not before. A scenario with a team short of a Medic, measured
  over 5 minutes: changes per minute stay under the clock's limit, and the team gets a Medic.

### C.5 Skills per class

The 60-point pool, cap 10, eleven rows (Teamwork from B.7). A class does not
add points (Appendix B.11's rule: "templates bias, they do not add"): a
class template supplies **favourite weights and caps** for the spread
(Sniper leans Accuracy/Cover/low Teamwork; Scout Aggression/Agility; Medic is
capped at Teamwork ≥ 6, i.e. a floor; Heavy Nerve; Spy Cunning) and
per-class **curves** (`tf2_skills.json`, one file, class sections): Accuracy
maps to a Sniper's flick and a Heavy's tracking differently. A bot that
changes class keeps its levels and reads the new class's curves. The
monotonic facts run per class on the inline simulation. Open question Q7 on
whether per-class pools should differ at all (recommendation: no).

### C.6 Game modes: how each runs here, what a bot must understand, the objective seam

Map objectives are public knowledge (the HUD shows every point, flag and
cart): the mod publishes `ObjectiveKnowledge` (core type) once per map and
updates its dynamic part on the main thread each tick into the snapshot's
HUD block:

```
Objective { id, kind: Capture | Carry | Deliver | Push | Block | Hold | Defend,
            positions[] (a point, a flag home, a cart path's cells), ownerTeam,
            state: Locked | Open | Capturing(team, progress) | Carried(actor?) | Moving(dir) | Won,
            mayAct: team mask, timeLeft, setup: bool }
```

Built from `TeamControlPoint`, `TeamControlPointMaster/Round`,
`TriggerAreaCapture`, `ItemTeamFlag`, `FuncCaptureZone`, `TeamTrainWatcher` +
the `func_tracktrain`'s path, `KothLogic`, `TeamRoundTimer` (setup,
`setup_length`). A bot reads it as data; the core knows kinds, not TF2.

| mode | here today | a bot must understand | first map |
|---|---|---|---|
| CTF | runs, gated | fetch (the enemy flag's home, then the carrier), deliver (own capture zone), escort the carrier, defend own flag (intel room), the flag's return timer (HUD) | ctf_2fort (the gate's map; nav per Part E) |
| 5CP | mode, unit tests | which point is open for each team (`Locked`/`Open`), capture (stand in the area; Scout ×2), defend the last, block a capture by standing on it, setup (gates closed, the nav's setup-gate cells blocked until the timer) | cp_badlands (in `maplist.txt`) |
| Attack/Defend | partial: team roles missing | the attacking team captures in order, the defending team holds; the round timer; which side is which comes from the mod's team role, which the TF2 mod must port first (C.1) | cp_dustbowl (stage5's live gate map), after the role port |
| Payload | runs, gated | push (stand by the cart, `mayAct` by team), block (the defender stands on it), the cart's checkpoints and its roll-back, the cart dispenser | pl_badwater |
| Payload Race | partial | both teams push; block the other cart; two paths | plr_hightower, after the race rules land |
| KOTH | mode, unit tests | the one point, its unlock timer, hold while the team timer runs, retake | koth_viaduct |
| Arena | missing | out until `tf_logic_arena` exists; the brain would be TDM's with one capture point unlocking | arena_lumberyard, later |
| MvM, PD, RD, Mannpower, Medieval, Passtime, SD | missing | out of this plan | |

Mode-specific brain branches hang off the shared tree's `Objective` subtree
with the stock vocabulary as names only (`CaptureObjective`,
`DefendObjective`, `FetchFlag`, `DeliverFlag`, `EscortCarrier`, `PushCart`,
`BlockCart`, `GuardCart`, `SeekAndDestroy` fallback); the per-class subtrees
(C.3) sit beside them and the tactical layer (retreat, get health/ammo,
destroy a sentry, use a teleporter) above, matching stock's three-layer stack
in shape (`CTFBotMainAction` → `TacticalMonitor` → `ScenarioMonitor`,
`tf_bot_behavior.cpp:58-61`) without its code.

### C.6.1 Regroup and push as a team (ruled 2026-11-02)

"Team bots in TF2 should also every now and then regroup if not making
progress and push as a team." The generic machinery is B.5's Regroup and
Push roles over the callout channel; TF2 gives it a real progress source
and the class rules.

- **No progress, per mode, from the objective feed** (F13/F14 publish a
  `Progress` field per objective and a `LastProgressTick` per team): CTF, no
  flag pickup or capture by the team in 120 s; CP and A/D, no capture
  progress on the team's open point in 90 s while the enemy holds it; KOTH,
  the enemy's timer running and the point not contested for 60 s; Payload,
  the cart not moved by the attackers in 60 s, or for the defenders, pushed
  past a checkpoint; any mode, 4 or more team deaths in the window with no
  gain. A team that is winning (ahead on the feed's score, or the cart past
  half) never regroups.
- **The rally.** The caller (B.5's rule) picks a spot near the front: a
  choke its team holds (`ChokeMap` cell between the team's last-seen
  positions and the objective), else a respawn-side cell out of the enemy's
  last-seen sight (the sight table). A bot in a fight finishes it; one on an
  objective it is about to take (capture progress > 50 %) stays; Spies and
  Snipers never regroup (their roles); humans are never ordered, a human
  within 384 u of the rally counts as gathered, and bots join a human's push
  (a human moving toward the objective with two bots in sight starts the
  push early).
- **The push.** When `rallyNeeded` (half the team's bots, at least 2) are
  within 384 u, or 25 s pass, or a Medic bot at the rally has Über ≥ 100 %
  (its own HUD; it calls "charged", a callout), they go: the Medic pops at
  the first enemy sighted past the choke; Soldier and Demoman time a blast
  jump (C.3.2) to the leader's first corner; Heavies spin up at the corner.
- **Every now and then.** `rallyCooldownS` 120 s per team after a push; the
  call tick carries a ±10 s draw from the team's seed; the rally spot draw
  picks among the top three candidates by seed, so two games differ.
- **Skill.** Teamwork scales it: the call needs Teamwork ≥ 4, arrival delay
  is `(10 − Teamwork) × 1 s`, Teamwork ≤ 2 ignores calls. Nerve raises the
  death count a bot tolerates before reading "no progress".

Facts and measures (each red once with an arm): a scenario on
`ss_ai_tf_cp` with a scripted stalled front (an enemy sentry holding the
point) shows a rally call within 90 s of the stall, and more bots arriving
within a shorter span than without it: the arrival spread (first to last
bot within 384 u of the point) is under half of the control run's; a team
making progress (scripted captures) never calls; the cooldown holds (no
second call inside 120 s, mutant red); a Spy and a Sniper never arrive at
the rally; replay holds at 1, 2 and 4 threads; flat Teamwork mutant red.
Prework: the feed's `Progress`/`LastProgressTick` fields (F13/F14).

### C.7 The mod's special cases

- **Setup time.** Bots pick class, build (Engineer), heal to full über
  (Medic), place stickies (Demoman) and wait at the gates; the nav's cells
  past a closed setup gate are blocked until `setup` clears (the
  obstacle field gets a per-team "blocked until" mask from the objective
  feed: stock's `BLUE_SETUP_GATE` vocabulary, our own data).
- **Respawn waves.** `TFRespawnPolicy` decides; a dead bot's brain reads its
  own `Respawnable` and spends the wait choosing a class.
- **Spawn rooms.** Enemies in a respawn room are not targets (stock's
  `PointInRespawnRoom` in vocabulary, `tf_bot.cpp:4631`); a bot in its own
  spawn room may change class at once (the game's rule). Team-only doors and
  the visualizer are a **TF2 mod gap first**: the visualizer is solid to
  nobody (`FuncRespawnRoomVisualizer.cs:24`), so enemies walk into spawns
  today; the mod must make it solid per team (a per-team collision answer
  through the seams, `divergences.md:74` says what is missing) before the
  nav can mark those cells; then `NavObstacleField` gets a per-team solidity
  (a visualizer is an obstacle for the other team only, a `filter_activator_tfteam`
  door likewise).
- **Class movement on the nav.** One hull for all classes, so one clearance
  graph (the HL2MP derivation's stand/crouch boxes with TF2's 82/62); speeds
  differ (the follower's arrival estimates read `MaxSpeed`); the Scout's
  double jump is a second **jump reach** class on the same graph (links the
  file splits by reach, as crouch-jump is today); blast jumps are a
  **third reach class, `JumpReach.Blast`** (ruled 2026-11-02, C.3.2: this
  overturns the earlier "not nav links in v1"), derived at load for the
  Soldier and Demoman only, taken by the follower only on the brain's
  say-so and under its health, clip and fight rules; Engineer teleporters are **dynamic edges** the team's bots learn
  when they see the pair (own team's buildings are visible on the map and
  the HUD's building panel for the owner only; a teammate learns it by
  sight).
- **Descent.** `src/tf2_descent` is a hosted RPG/co-op layer over TF2
  (`HostLink`, `TravelService`, `LevelHop`, `DeathService`). Appendix B was
  written for its robots (packs, temperament by size, R20). This plan's
  TF2 binding is the same core and server layer; Descent's robots would be a
  third binding (`SourceSharp.Ai.Descent`) over `SourceSharp.Ai.TF2`'s class
  brains with Appendix B.11's size budget instead of the 60 pool. It fits;
  it is not in this plan (the TF2 port plan rules Descent out of scope,
  `plan_tf2_port.md:51`).

### C.8 Testing Part C

Facts per class (inline simulation over a synthetic view): each ability's
one-shot is applied exactly once and as the right usercmd/command (a fake
runner records it); the Spy honesty fact (C.3); the Medic heals the sighted
hurt teammate and pops under the threshold; the Engineer's build loop reaches
level 3 in a scripted empty room within N seconds of metal (N from the game's
numbers); the composition picks a Medic at four. Per mode: the objective feed
diffs against the entities (count and positions, like `MapKnowledge`'s fact);
a scripted CTF round ends in a capture by bots alone; a 5CP point flips;
a cart reaches the first checkpoint. Scenario maps: `ss_ai_tf_ctf`,
`ss_ai_tf_cp` (room library with the TF2 entities placed; the room library
is map-tools' and takes a mod entity contract, `SourceSharp.RoomContracts`).

Live (`tools/tf2-gate`, new steps): `bots-ctf`: 6v6 bots on ctf_2fort for
three minutes, both teams score at least one capture, no handle fault,
the `/proc/<pid>/task` count returns to baseline after `changelevel
pl_badwater` and `bots-payload` pushes the cart past checkpoint 1 (**the
second map load**). Main-thread budget measured first (stage5's rule): the
HL2MP numbers are `ai.sense.us` 185.9 µs and `ai.publish.us` 90.2 µs with 4
bots on 4 workers (plan §12.3); 12 bots must be measured before a budget is
written.

---

## Part D: the game-mode config model, votes and the admin menu

### D.1 What exists

**Votes** (kick only): `VoteController` (`src/server/managed/Entities/Rules/VoteController.cs:102`; `VoteIssue(Name, Details, PassCommand, TeamRestricted, Options)` at `:15`, already general), `ServerVotes` (`Votes.cs:35`, verbs `ss_callvote`/`ss_vote`, `Call` accepts only `kick <slot>`), `IsImmune` (`:108`, listen host and admins), the stock convars (`:177-201`: `sv_allow_votes`, `sv_vote_timer_duration` 15, `sv_vote_creation_timer` 150, `sv_vote_quorum_ratio` 0.6, `sv_vote_allow_spectators` 0, `sv_vote_holder_may_vote_no` 0, **`sv_vote_bots_allowed` 0**: bots are already outside the electorate, `CountVoters` `:511`). Wire `VoteState` (`src/shared/managed/VoteNet.cs:36`: `Phase, Caller, Target, Yes, No, Voters, SecondsLeft`) is **kick-shaped**: no issue kind, no map, no options. Client: `ClientVotes` (`callvote` `:50`, radial Ballot/Call pages), `VotePanel.cs:37` hard-codes the kick title. No live gate step. Stale comment `VoteController.cs:75`. Stock HL2MP has **no** vote system (only TF creates the controllers, `tf_gamerules.cpp:12087-12089`); the TF issue list is `tf_voteissues.h:35-254` (`CKickIssue`, `CChangeLevelIssue`, `CNextLevelIssue`, `CExtendLevelIssue`, `CScrambleTeams`, `CRestartGameIssue`, `CTeamAutoBalanceIssue`, `CClassLimitsIssue`, `CPauseGameIssue`, …) with `sv_vote_issue_*_allowed` convars; the end-of-map multi-choice vote exists in stock (`ManageServerSideVoteCreation`, `tf_gamerules.cpp:14755`, off unless `sv_vote_issue_nextlevel_choicesmode 1`).

**Admin** (commit `29728fb8b`): `ServerAdmins` (`Admins.cs:92` `IsAdmin` = listen host slot 1, never on a dedicated server, or a key proved through `AdminGate` (`AdminGate.cs:22`: nonce, 3 failures → 60 s lockout) against `cfg/admins.yaml` (`src/mods/AdminsFile.cs:8`, ECDSA P-256, `AdminNet.cs:24`)); `IAdminMenu`/`AdminMenuBuilder` (`Admins.cs:181-215`: Change map from the mapcycle → `changelevel`, Game mode from `GameModes.Registered` → `SwitchTo`, Kick by user id, Restart → `mp_restartgame 1`, then every `IAdminMenuProvider`'s items; the AI adds "add bots easy…expert / remove all" at `BotCommands.cs:225`); `Run` re-checks `IsAdmin`, rebuilds the menu for that slot and runs only an id found in the fresh build: **the client never sends a command**. Client `ClientAdmin` (`adminmenu`, `ss_admin_keygen` `:254`), radial (`RadialMenus.cs:36` `MaxItems` 9), protocol in `docs/admin_menu.md`. No rcon anywhere in managed code. `ServerAdmin` (`ServerAdminCommands.cs:47`) is the separate `UTIL_IsCommandIssuedByServerAdmin` rule.

**Create server**: `CreateServer.cs:21` (Server tab: map from `EngineMapList` `OptionsSources.cs:86`, hostname, maxplayers, password; Game tab: `cfg/settings_default.scr` rows); mod pages discovered from `client_ui` assemblies (`UiModScreens.cs:12`), `CreateServerPage` seam (`CreateServerPage.cs:15-32`: `Describe`, `StartRefusalToken`, `BeforeMap(choices, lines)`); the start is one console text (`OptionsSources.cs:134-152`: `… <pages' lines>; progress_enable; map M`). The bots page is the model (`BotsPage.cs:18`, `CreateServerModel.cs:146`).

**Map cycle**: `Intermission.NextMap` (`Intermission.cs:137`: `nextlevel`, else the cycle), `ReadCycle` (`:164`, no existence filter), `mapcyclefile` (`GameDllConVars.cs:30`). No `nextmap` command.

### D.2 The config model (mod-neutral, published by the mod)

```
GameModeCatalog { modes: ModeSpec[] }
ModeSpec { id ("null"→ shown as "Deathmatch", "tdm", "ctf", "cp", "pl", "koth", …),
           title, settings: SettingSpec[], supports: MapRequirement }
SettingSpec { key (a convar: mp_friendlyfire, mp_timelimit, mp_fraglimit,
              mp_teams_unbalance_limit, ai_bot_count, ai_bot_difficulty, …),
              kind: Bool | Int | Float | Choice(names), min, max, default,
              applies: Now | NextRound | NextMap, votable: bool, adminOnly: bool }
MapRequirement { anyOf: [entity class names the map must contain], or None }
```

- **Where it lives.** A core `IGameModeCatalog` built from the registered
  `IGameMode`s plus each mode's `IModeSettings? Settings => null` (a new
  default member, like `Spawns`): the core publishes `null`/Deathmatch and
  `tdm`; TF2's modes publish theirs (`mp_respawnwavetime`, `mp_autoteambalance`,
  `tf_…`). The AI binding contributes its rows through the same seam
  (`IModeSettingsProvider`, chained), so `ai_bot_count`, `ai_bot_difficulty`,
  `ai_bot_reroll_on_map` are settings of every mode.
- **Storage.** The convars themselves are the store (archived where stock
  archives them; replicated ones reach the client already). The mode id is
  `sv_mode`'s current value. Nothing new is written to disk by the server
  except what a convar's archive flag already does; the client's
  create-server choices persist in its own settings file as today.
- **When a setting takes effect** is the `applies` field, and the catalog is
  the one place that says it: friendly fire, limits and bot count `Now`;
  autobalance and unbalance limit `NextRound` (applied in the mode's `Setup`);
  the mode itself `Now` through `SwitchTo` (which restarts the round and keeps
  the players; A.3 item 4), **except** a mode whose `MapRequirement` the
  current map fails, which is refused with the reason ("ctf_2fort has no
  control points"); the map `NextMap` (a `changelevel`).
- **Which modes a map supports** is `MapRequirement` against the map's
  entity lump (the same read `TFGameModeSelector` does through `map.Contains`;
  the HL2MP side reads the lump through `MapEntities.Read` already). The
  catalog answers `Supports(map, modeId)` for the current map at once and
  for another map through the lump of that bsp (a file read through the
  engine's GAME paths, cached per map). That is what stops a vote for CTF
  on a map with no flags: the vote is refused at call time with the reason.
- **The client's copy.** The catalog is networked once per level and on
  change as a string-table-like blob (the admin menu already streams text
  in 200-byte pieces, `AdminMenuText.PieceBytes`): the create-server page,
  the vote call page and the admin menu all draw from the same
  `ICatalogSource`; the create-server page draws it from the **client's own
  assemblies** before a server exists (the catalog is pure data a mod's
  client assembly can build the same way).
- **How a change reaches the AI.** `GameModes.SwitchTo` already restarts the
  round; the AI binding's `IGameLifecycle`/round hooks: on a mode switch
  every bot is **re-added as a player would rejoin**: `jointeam 0` under the
  new mode's policy (TDM: a team; DM: team 0), the brain restarted with the
  mode's tree (the decider is per session, `AiSession.Deciders`, so the
  binding hands a new `IDecider` per bot keyed by `GameModes.ActiveId`), the
  objective knowledge rebuilt (C.6), the roster's team targets recomputed
  (B.6). Memory survives (same map), as a player's would.

### D.3 Votes

Rules (stock's where it has them, named per convar; the new ones named):

| rule | value | where |
|---|---|---|
| who may call | any connected human not in cooldown; spectators only if `sv_vote_allow_spectators`; a bot never calls (ruled) | `CanCallVote`, exists |
| electorate | **humans only** (ruled): `sv_vote_bots_allowed` stays 0 and the bots' arm is removed from the count, so the convar is retired (a vote nobody can cast cannot be allowed) | `CountVoters` |
| quorum, majority | `sv_vote_quorum_ratio` 0.6 of the electorate must cast; yes > no passes (yes/no); multi-choice: most votes, ties by first option (stock's `ProcessResults` shape) | exists |
| one human and five bots | the electorate is 1; the caller's own yes is quorum and majority, so the vote passes at once. Recommendation: **fine on a listen server** (it is their server; the admin menu does the same); a dedicated server operator gets `sv_vote_min_voters` (new, default 1) to demand more, since one stranger changing the mode on an empty public server is the case to guard | §8.2 Q2 |
| cooldowns | `sv_vote_creation_timer` 150 s per caller; a failed issue of a kind cannot be re-called for `sv_vote_failure_timer` (new, stock's name, 300 s) | `:189`; new |
| timer | `sv_vote_timer_duration` 15 s, early finish when everyone cast | exists |
| what a passing vote runs | the issue's `PassCommand` through the same validated server path the admin menu uses (never a raw console line from the client) | `VoteIssue.PassCommand` |
| bots | neither call nor cast nor count; they **react** to the outcome like any player (D.2's switch) | ruled |

Issues (each an `IVoteIssue` the core or a mod registers, chained; the
catalog says which settings are `votable`):

| issue | details | effect | stock name |
|---|---|---|---|
| kick | slot | exists | `CKickIssue` |
| change mode | mode id from the catalog, must be supported by the current map | `GameModes.SwitchTo` | (none in stock) |
| change setting | key + value within the catalog's range, `votable` | the convar write, with `applies` honoured | (`CClassLimitsIssue`, `CTeamAutoBalanceIssue` are this shape) |
| next map | a candidate map | sets `nextlevel` (`Intermission` reads it); multi-choice when called with no map (the end-of-map vote, below) | `CNextLevelIssue` |
| change level now | a candidate map | `changelevel` after `sv_vote_command_delay` | `CChangeLevelIssue` |
| extend map | — | `mp_timelimit` += 20 (stock's number) | `CExtendLevelIssue` |
| restart round | — | `mp_restartgame 1` | `CRestartGameIssue` |
| scramble teams | — | the team-mode's `Setup` with a seeded re-assignment of everyone | `CScrambleTeams` |

**Map votes** (ruled in scope). Candidates = `mapcyclefile`'s list ∩ the
maps installed on the GAME paths (the server-side twin of `EngineMapList`;
today the cycle can name uninstalled maps, A.2), each annotated with the
modes it supports (the catalog's lump read) and whether a `.nav3d` exists or
can be built (Part E): a map with no nav is **allowed** and marked "nav will
be built on load" (the vote text says so); a map Part E has refused this
session (too tall, no world) is listed without bots in the text. The
end-of-map vote: in the intermission, when `sv_vote_issue_nextlevel_choicesmode`
is 1 (new convar, stock's name; recommendation: default **1** here, stock's
0, because a listen server with bots has no other way to pick the next map
without the console), a server-called multi-choice `next map` vote of up to
5 candidates (stock's lowest-playtime order replaced by cycle order plus
"extend" when allowed) runs for the intermission's length; its winner sets
`nextlevel`. `nextmap` console command prints `Intermission.NextMap()`.

**Wire.** `VoteState` grows: `Issue` (byte, the kind), `Details` (a string
piece: the map or setting), `Options[≤5]` (strings), and the client's
`VotePanel` draws the kind's title and the options (yes/no or 1–5). A
version bump is not a thing (the ABI is tables); the net state is a
`partial struct` and its parity test moves with it.

**Web dev UI.** Yes, a read-only "Match" card on the existing AI/World panel
(`src/webui/src/ai/AiWorldPanel.tsx`): the active mode, the catalog's current
values, the vote in progress with counts, the nav build state (E.4). One
route `/match` beside `/ai/*` (`AiBotRoutes.cs:11`'s shape). No writes from
the web UI (the admin menu is the write path, in-game, signed).

### D.4 The admin menu

Who: as today (`IsAdmin`): the listen-server host, or a listed key. On a
dedicated server with no keys listed nobody is an admin; every `Run` by a
non-admin is refused and logged with the slot and the id (`Admins.cs:149`'s
re-check already does; the log line gets the player's name and user id so a
gate can grep it). Recommendation for Q3 (what admin means on a listen
server): the host is the admin, full stop; a second listen-server player is
an admin only with a listed key, as today.

What the menu offers (all through the existing `AdminNode` builder, each id
re-validated on `Run`):

| folder | items | permission |
|---|---|---|
| Map | the candidates of D.3 (installed, with mode support and nav state in the label) → `changelevel`; "Next map: …" → sets `nextlevel` | admin |
| Mode | the catalog's modes supported by this map → `SwitchTo`; unsupported ones shown disabled with the reason | admin |
| Settings | one sub-folder per `Now`/`NextRound` setting: a radial of its choices (bool: on/off; int: a few steps: the catalog's `steps` hint; `MaxItems` is 9) | admin |
| Players | kick (exists), move to team (team modes), make spectator | admin |
| Bots | add N (preset), remove all (exist, the AI's provider), per-team add, difficulty, re-roll now, "bots follow orders" toggle (B.5) | admin, provided by the AI binding |
| Votes | call any issue as the server (no quorum: an admin's call is an action, logged as "admin <name> changed …") ; cancel the running vote | admin |
| Restart | exists | admin |

A screen only needs what the radial has today (`RadialMenus.cs:33`); nothing
new in the toolkit. The one new client piece is `VotePanel`'s general shape.

### D.5 Facts and gates for Part D

Facts: the catalog lists the core's two modes with their settings and their
`applies`; `Supports(dm_lockdown, ctf)` is false and `(ctf_2fort, ctf)` true
from the lump; a vote on an unsupported mode is refused at call with the
reason; the electorate counts no bot (a rig with 1 human + 5 bots: `Voters`
= 1); `sv_vote_min_voters 2` refuses the one-human call; a passing setting
vote writes the convar and a `NextRound` one waits for `Setup`; the
end-of-map vote's winner is `NextMap()`; an admin `Run` by a non-admin slot
is refused and logged; the catalog's wire round-trips. Mutation arms: pass a
vote with `Yes == No`, count bots, skip the map-support check, run an admin
id without the re-check.

Live gate steps (`tools/live-gate`): `vote-mode`: a human (the gate's
client) calls `change mode tdm` on dm_lockdown with 3 bots: `Voters` 1, the
vote passes at once, `[SourceSharp/Modes] switched to 'tdm'` is logged, the
bots re-team (team counts 2/2 counting the human), the scoreboard shows two
columns; `vote-fail`: with `sv_vote_min_voters 2` the same call is refused
with the reason on the client's panel; `admin-refused`: a `Run` from the
gate's second client (not an admin, dedicated-less listen: slot 2) is refused
and the log line names the slot; `vote-map`: `next map ss_ai_tdm` passes,
the round ends on a frag limit, the intermission changes to `ss_ai_tdm`
(**the second map load**) and bots play there.

---

## Part E: the server builds a `.nav3d` on map change

### E.1 Today

A `.nav3d` is per-tree build output of `make ai-nav` (`Makefile:665`,
`tools/ai-nav-build:84` runs `ssmap nav3d <bsp> -out <nav> -game <mod>` from
the map-tools checkout pinned by `maptools.lock`, with a `.stamp` beside the
file). The server reads `maps/<map>.nav3d` through the engine's GAME paths
(`NavLoader.cs:27`, `Hl2mpNav.cs:99`, `IEngineFiles.ReadGameFileBytes`
`EngineAreas.Files.cs:51`), binds it to the bsp (`MatchesBsp`: CRC-32 map
checksum **and** SHA-256 of the whole bsp, map-tools `Nav3dBspBinding.cs`;
plan §6.3 says checksum only: **repo differs**), derives the graph and caches
it under `<GameDir>/maps/ai-nav/…navgraph` (`NavGraphCache.cs:29`). Missing,
unreadable, wrong version (`Nav3dReader.cs:288`), wrong bsp: a **refusal**
string (`NavLoader.cs:36-63`), logged as `[SourceSharp/Ai] <refusal>`, and
`Hl2mpBots.Add` refuses ("No navigation, no bot", `Hl2mpBots.cs:102`); roster
bots stay benched with `RefusedOnLevel` (`BotRoster.cs:108`). `Hl2mpNav.Load`
runs **synchronously** at `OnLevelInitPostEntity` (`Hl2mpNav.cs:75`). Two
written rules say the game never builds one: plan §6.4 `:1397-1399` ("`make`
is what produces it, `source#` never builds one"), `NavLoader.cs:20` ("never
a fallback"), and map-tools' `tools/pack-mapformats.sh:12` ("the game must
get the formats and nothing of the compile tools"). **This part overturns
all three** on the user's ruling (2026-11-02, §8.1).

What "broken" must cover, and what the loader can tell:

| case | detectable today | by |
|---|---|---|
| missing | yes | `GameFileSize` |
| truncated, bad section, bad codec stream | yes, as `InvalidDataException` → "cannot be read" | `Nav3dReader.Open` validates the whole file |
| wrong format version | yes (reads 3 and 2 only) | `Nav3dReader.cs:288` |
| bound to other bytes (`MatchesBsp` false) | yes | `BSPB` section |
| built by an older tool from the same bsp | **no**: the file stores no tool version (`docs/nav3d-format.md`: header has `levelId`, `packId`, 24 reserved bytes); only `ai-nav-build`'s `.stamp` knows, and the loader never reads it | gap |
| built against different mounted content (props) | **no** (the output depends on it; a missing model is a warning and exit 0) | gap |
| a stale `.navgraph` cache | keyed by bsp + nav hashes + versions (`NavGraphCache.cs`), so no | — |

### E.2 Shipping map-tools' code: what, how, in-process or child

**Which assemblies.** The nav path is `SourceSharp.MapTools`
(`LevelNavFromBsp.BuildAsync` `LevelNavFromBsp.cs:152`, `NavModelBounds.Hull/
Pieces` over bytes `:239,135`, `NavGeometry`, `GameContentMounter`) over
`SourceSharp.MapFormats` (already shipped, 1.0.2) and `SourceSharp.RoomContracts`
(a MapTools reference; 35,840 B). Sizes from `bin/Release` there: MapTools
3,870,208 B; MapFormats 376,320 B (already in `bin/managed`); YamlDotNet
300,032 B (already shipped, same 18.1.0 pin). **CPU only**: zero Vulkan/Silk
hits on the nav path (`grep` this session: 0); `SourceSharp.MapTools.Gpu` is
vrad's and never referenced by MapTools. No native library on the nav path
(`NavModelBounds` uses `Phys.Managed`). Licence MIT (`LICENSE:1`,
`PackageLicenseExpression` in the formats csproj). Packaging: a second nupkg,
`SourceSharp.MapTools` (+ RoomContracts), from map-tools (`IsPackable` flipped
in `SourceSharp.MapTools.csproj:6`; `pack-mapformats.sh:12`'s rule amended
there, a map-tools change the user allowed), restored from the same local
feed with the same pinned-version fact (`MapFormatsPackageTests`'
`TheAssemblyIsThePinnedPackageVersion` gets a twin), copied by
`CopyLocalLockFileAssemblies` (`SourceSharp.Ai.Hl2mp.csproj:12`) into
`bin/managed`. Load context: the AI assembly's (`GameLoadContext`, collectible,
`GameLoadContext.cs:11`), so the build must be cancelled and joined before
unload, as `ChatWorker.Stop`'s 2,000 ms join is (`ChatWorker.cs:208`,
`ChatUnloadTests.cs:32`), and a fact holds the unload with a build in flight.

**In-process versus a child process.** The facts:

| | in-process (a worker thread in the AI's pool) | child process (`ssmap nav3d`) |
|---|---|---|
| reading the bsp | bytes through `IEngineFiles` (VPK and pak visible) | needs a disk path: a bsp inside a VPK has none |
| static-prop content (`.phy`/`.mdl`) | the same seam, from the mounted game (`-game` is the engine's own mount, so it is right by construction for TF2 and a dedicated server) | `GameContentMounter` mounts by `gameinfo.txt`; the test mod's `|appid_243750|` needs Steam under `$HOME` (`Program.cs:408`): **fails on a Steam-less box** |
| a crash | a managed exception (no native code on the path), caught, refused | isolates a segfault that cannot happen here |
| memory of a big map | the game's heap; no cap possible | its own; killable |
| threads | `MaxDegree` set by us, below-normal priority | its own; `-threads` |
| runtime | none needed | the bundled `bin/dotnet` has **no `dotnet` muxer**; needs an apphost + `DOTNET_ROOT` |
| precedent | `ChatWorker` (a background thread with join) | none in the server |

**Recommendation: in-process**, on the worker pool, with the inputs read on
the main thread first and the pure build off it; a child process is kept as
a hardening option for huge maps (Q9), not built now. The deciding fact is
content: the engine's own mount is the only correct answer to "which `.phy`
does this server have", and the library takes bytes (`NavModelBounds.Pieces
(ReadOnlySpan<byte>)`), so nothing of `ssmap`'s mounting is needed.

### E.3 How it runs

1. `OnLevelInitPostEntity`: `NavLoader` tries, in order, `maps/<map>.nav3d`
   (a shipped or hand-built file), then `<GameDir>/maps/ai-nav/<map>-<bsp
   sha16>.nav3d` (the cache). A readable, bound file wins. Otherwise, if
   `ai_nav_build` is 1 (default 1; 0 keeps today's refusal), the level's
   refusal becomes `navigation for <map>: building` and a **build job** is
   posted.
2. The main thread reads the inputs once (a frame or two, time-sliced if
   needed): the bsp bytes (already read for binding), the static-prop lump's
   model names, and each model's `.mdl` + `.phy` bytes through `IEngineFiles`
   (dm_lockdown: 86 props, plan §12.4; a TF map: hundreds; a read is a file
   seam call each: measure). Missing models are counted and reported (a
   warning line names them; the build still runs, as `ssmap` does).
3. The worker runs `LevelNavFromBsp.BuildAsync` with `MaxDegree =
   ai_nav_build_threads` (default `max(1, ProcessorCount / 2)`), the pool's
   threads at `BelowNormal` priority, a `CancellationToken` the level's
   shutdown cancels, a wall-clock cap `ai_nav_build_timeout` (default 600 s),
   and a leaf cap (the format's own limits: 255 voxels tall, world required;
   a `ai_nav_build_max_leaves` of 4,000,000, about 25 × dm_lockdown's
   156,159). Progress: the library's column progress (if exposed; else per
   phase) to a counter the console prints every 10 % (`[SourceSharp/Ai]
   navigation for <map>: building 40 % (12 s)`) and `/ai/nav` serves.
4. On success: `Nav3dWriter` to a temp file beside the target (GUID name)
   and an atomic rename (the `PhysicalFileSystem.ReplaceAsync` shape,
   `PhysicalFileSystem.cs:461`), plus a `.stamp` (bsp sha, MapTools version,
   MapFormats version, the content's model list hash, seconds, thread count).
   Then the existing derive path (already a pool job per plan §2.7) and, at
   the next frame on the main thread, the level's nav is set and the
   roster's `Reconcile` brings the benched bots in (today `Reconcile` does
   not re-ask after a refusal: **unverified**; it must, keyed by the nav's
   `Version`).
5. On failure: the refusal becomes `no navigation for <map>: build failed:
   <reason>` (the library's own messages: "the map is N units tall…", "no
   world model"), the server stays up, bots stay benched, one retry only if
   the failure was an I/O error or a timeout under load (never for a
   format refusal), never a loop (`RefusedOnLevel`'s shape, per map per
   session).
6. Cancel: a `changelevel` during a build cancels the token and joins (≤ 2 s;
   the library checks the token between columns, `LevelNavFromBsp.cs:150`);
   no partial file exists (temp + rename). A hot reload does the same before
   unload.

Timing premise (measured in map-tools at the lock: `docs/nav3d-format.md:1042-1046`):
dm_lockdown 4.5 s at 32 threads, 30.1 s at 1 thread, under a box load near
47; byte-identical sha `84740bf8…` at both counts. In-process with half the
cores and below-normal priority, expect 8–15 s on this box for dm_lockdown;
ctf_2fort unmeasured (§9). A human's join never waits: the level is up, the
bots are what waits.

Determinism: the library's design statement (`LevelNavFromBsp.cs:122-127`)
and its fact (`SourceSharp.Tests/MapTools/Nav/LevelNavFromBspTests.cs`
`TheSameMapGivesTheSameBytesEveryTime`, 1 vs 4 threads, 4 concurrent). To be
re-measured **inside the game process** (floating-point state on engine
threads is the unknown): a fact builds dm_lockdown in a test process at 1
and 4 threads and compares to the stamp's sha; the live gate compares the
server's built file to `make ai-nav`'s.

### E.4 Output, cache, operators, check

- Output dir: `<GameDir>/maps/ai-nav/` (the navgraph cache's directory,
  writable by construction: `mod+mod_write`, `ModsConfig.cs:60`'s override
  so managed writes where the engine reads). Never beside a shipped map
  (`maps/` may be a VPK or read-only), never into the Valve clone, never
  into a `-instance` dir's linked `maps/` (instances share the tree's maps
  dir, `Hl2mpNav.cs:69-72`): the cache dir is per game dir and a second
  instance writing the same key writes the same bytes through its own temp
  file (the fixed `.tmp` name in `NavGraphCache.cs:54` is a collision today
  and gets the GUID treatment in the same commit).
- A shipped `maps/<map>.nav3d` that binds is used as is and never
  overwritten; one that does not bind is ignored and a cache build replaces
  it in the cache dir.
- Reuse: a second load of the same map (same bsp sha) finds the cache file
  and skips the build (gate step).
- Convars: `ai_nav_build` (0/1), `ai_nav_build_threads`, `ai_nav_build_timeout`,
  `ai_nav_build_max_leaves`, `ai_nav_build_check` (0/1: run the floor check
  after a build; default 0 in release, 1 under the gate). Commands:
  `ai_nav_build <map>|all` pre-builds (all = the map cycle's installed
  maps, one at a time, lowest priority) and `ai_nav_status`.
- The check: `ssmap nav3d -check`'s work is `NavFloorCheck` over every
  standable place (dm_lockdown: 35,054 places; cost **not measured**
  anywhere: it prints elapsed, `Nav3dCommand.cs:229`); run in-process on
  the worker after the build when `ai_nav_build_check 1`, result to the log
  and `/ai/nav`; a failing check (missing floors over 0.5 %) marks the file
  suspect in the stamp and the log, and does not refuse it (the validator
  already spot-checks against the live world, plan §6.3).

### E.5 Security

The input is a bsp the engine has already loaded and parsed, and the server
operator chose the map; a bsp from anywhere still goes through our parser
off the main thread. Limits that hold: every lump offset and length is
checked against the file length (`BspData` does, versions 19–21 only), the
leaf/height/world refusals, the timeout, the thread cap. A managed
out-of-memory is an exception the job catches and reports as a refusal (the
process survives an OOM in a worker only if the allocation failed cleanly:
**the one case a child process would isolate better**, Q9). No file path from
the bsp is used to open anything except through the engine's own search
paths (model names → `IEngineFiles`), so no traversal.

### E.6 Vote and admin interaction

The candidate list (D.3) annotates each map with its nav state: `shipped`,
`cached`, `will build (~N s)`, `refused: <reason>`. A vote for a map with no
nav passes and the build runs on load with the console and web UI showing
progress; the vote text says "bots join when navigation is built". The
"which modes a map supports" read (D.2) and the nav state read share the
per-map bsp read (one cached lump read per candidate).

### E.7 Facts and gates for Part E

Facts: a missing file posts a build and the refusal says `building`; a
truncated file, a flipped byte in a section, a wrong version byte and a bsp
mismatch each lead to a build (five arms); `ai_nav_build 0` keeps the
refusal; a cancelled build leaves no file and the level shuts down within 2
s; a build that throws leaves the server's lifecycle intact (the driver's
`threw` line is **not** seen: the job catches); the retry happens once for
an I/O failure and never for a height refusal; the cache key changes with
the bsp sha; the stamp names the tool versions; determinism at 1 and 4
threads in the test process; the unload fact with a build in flight.
Mutation arms: drop the atomic rename (a crash mid-write leaves a half file
the loader then refuses: the fact plants one), count a bot before the nav is
set, build on the main thread (a fact asserts the worker's thread id).

Live gate (`tools/live-gate`, with the machine's load stated in the report
as `tools/ai-scenario` does): `nav-build`: `changelevel ss_ai_routes` with
its `.nav3d` **removed** (the step moves it aside, restores in `finally`):
the console shows `building … 100 %`, the file appears in the cache dir,
bots join and walk (the `ss-ai-nav` walk assertion), elapsed under 60 s at
the stated load; `nav-corrupt`: the file truncated to half and, a second
pass, one byte flipped in a section: both rebuilt; `nav-fail`: a planted
file whose bsp is a 1 × 1 brush with no world (a fixture bsp) is refused
with the reason and the server answers the dev API afterwards; `nav-cache`:
a second `changelevel` to the same map reuses the cache (no `building` line,
`[SourceSharp/Ai] navigation for … (cached)`): **the second map load**.

---

## Part F: prework stages for local models (ruled 2026-11-02)

Everything a TF2 bot depends on that is not its own brain is split into
stages a Qwen or GLM lane can implement unattended, each in its own file
under `docs/plans/tf2_ai/` with its whole brief inside (base check, worktree,
owned files, stock lines read this session, fact names, the commands and the
`Failed: 0` condition, one fact shown red first, and a fit rating). The
README there holds the order, the dependency graph and what runs in
parallel. In short:

| group | stages | for local models? |
|---|---|---|
| truth and small arms | F01 stale rows; F03 bot arms (autobalance, humans-must-join, medigun netchan); F09 round facts; F12 actor facts; F38 quality names | yes (Qwen; GLM for F01/F03/F09) |
| movement and collision | F04 the per-entity collide rule (core seam) | **no** (stronger model) |
| | F05 `tf_avoidteammates` + push-away over it; F07 the visualizer solid and sent per team (over F04/F06) | yes (Qwen) |
| transmit | F06 the per-client transmit rule (core seam) | **no** |
| bodies | F08 a body keeps its team and class | yes (Qwen) |
| modes | F10 team roles (A/D); F11 PLR race rules; F13 objective feed CTF+PL; F14 objective feed CP+KOTH; F15 KOTH gate; F16 5CP gate | yes (Qwen; F13/F14 GLM-able) |
| nav | F02 the TF map nav spike | **no** (needs map-tools' `dotnet run`, which a lane cannot run) |
| items | F17 the schema audit tool; F18a–d the audit fill by class pair; F19 the bots' item source; F20 arrow lighting; F21–F32 item families the sub-classes need; the long tail (F33+, generated from the audit) | yes (Qwen) |
| looks | F37 wearable rendering on the client | **no** (rendering, visual proof) |
| core seams from Part A/D | F40 `IGameMode.Teamplay`/`JoinPolicy` defaults with no-owner facts | yes (Qwen) |
| not prework | Arena (`tf_logic_arena`: 16 rules arms + 23 player arms + the logic entity, not cheap: stays out); the bots' cosmetic draw, the blast edge class, perches and long lines (P5–P7) | — |

## 6. Phases: commit-sized steps and gates, in dependency order

Each phase ends with `make test` green (the `ai` leg included), one
reviewable commit per step, `make check` before a merge to main, and
`test-full` before any ledger or link-line change (memory rule). Lanes by
file ownership; the ABI tail (new forwarder rows: none expected; the net
state structs) serialised. Every live gate below loads two maps.

| phase | ships | depends on | size (honest) |
|---|---|---|---|
| **P0 measure** (no code but counters and the map) | `ss_ai_tdm` map + yaml; the TF2 nav spike: `ssmap nav3d` on ctf_2fort, cp_dustbowl, koth_viaduct with `-game` the tf content (does it build? LZMA lumps, prop lump version, height); `ai.sense.us` with 12 bots on dm_lockdown; the bsp-read cost of a TF map's props | nothing | 2–3 days |
| **P1 HL2DM TDM** (Part A) | steps: (1) `mp_friendlyfire` read + retire `cs_sv_friendly_fire`; (2) `IGameMode.JoinPolicy`/`Teamplay` defaults + no-owner facts; (3) `TeamDeathmatchMode` with `IScoring` and the team frag limit; (4) team spawns + fallback + team-aware frag rule; (5) auto-assign and suicide-on-switch under `tdm`; (6) `TeamplayModeSelector`; (7) `HostSay.Teammates` real; (8) team menu on first spawn + `changeteam` key + the gate's teamplay arm; (9) scoreboard reads `TeamScore`, team HUD line, coloured feed/chat; (10) the stopgap `settings_default.scr`; (11) gate steps `tdm-round`, `tdm-chat` | P0's map | 2 weeks |
| **P2 AI teams** (Part B) | (1) `Team` on `SelfState`/`ActorFacts`/rows, `Friends()` in core, shape facts; (2) blackboard split (no grudge/ammo on friends); (3) target filter + fire gate for friends + jank parts; (4) the `Callout` say slot, the delayed slot-order republish, the replay fact at 1/2/4 threads; (5) roster `team=`, even teams, yield from the right team, team models; (6) Teamwork row + curves + name pools; (7) the Team subtree: Stick, Cover, Trade, Regroup; (8) Crossfire and Chase (needs the tactics branch's `ChokeMap`); (9) human orders in `say_team` + the create-server per-team page; (10) `ai-scenario tdm`, the scenario facts, `make ai-jank MODE=tdm`, gate `tdm-bots` | P1; the tactics branch landed (for 8) | 3–4 weeks |
| **P3 config, votes, admin** (Part D) | (1) `IModeSettings` + `GameModeCatalog` + `Supports` from the lump + facts; (2) the catalog on the wire + `ICatalogSource` on the client; (3) `VoteState` general + `VotePanel` general + `IVoteIssue` chain; (4) issues: mode, setting, next map, change level, extend, restart, scramble; `sv_vote_min_voters`, `sv_vote_failure_timer`, bots out of the count (retire `sv_vote_bots_allowed`); (5) map candidates (installed ∩ cycle, mode support, nav state), the end-of-map vote, `nextmap`; (6) the admin menu's folders (Mode, Settings, Players, Bots, Votes) + refusal log line; (7) create-server reads the catalog (replaces the stopgap script); (8) the AI's reaction to a switch (re-add, re-team, new decider); (9) the web UI Match card; (10) gate steps `vote-mode`, `vote-fail`, `admin-refused`, `vote-map` | P1 (the mode); P2 step 5 (re-team) | 2–3 weeks; can run as a lane beside P2 after P1 |
| **P4 nav on demand** (Part E) | (1) map-tools: `SourceSharp.MapTools` packable + the pack script's rule amended (that repo, user-allowed); the game pins it; (2) the loader's search order + cache dir + GUID temp names; (3) the build job: inputs on the main thread, build on the pool, progress, cancel, timeout, limits; (4) the stamp, the refusal strings, one retry; (5) `Reconcile` re-asks on nav version; (6) convars and commands; (7) the check on the worker; (8) `/ai/nav` building state + web UI; (9) the determinism fact in-process; (10) the four gate steps | nothing (a lane beside P2/P3); **before P5** | 1–2 weeks |
| **P5 the lift** (C.2) | `SourceSharp.Ai.Server` carved out of `SourceSharp.Ai.Hl2mp`, one move commit per directory, the facts moved, HL2MP recordings replay byte-identical, the HL2MP gate steps unchanged; `ActionKind.Ability`, `ObjectiveKnowledge`, `SelfState` class/conditions fields with HL2MP answering defaults | P2 (so teams are in what is lifted); **F12, F13, F14** (the mod's facts the lifted types are shaped to carry) | 1 week |
| **P6 TF2 first slice: Soldier, CTF, ctf_2fort** | (1) `SourceSharp.Ai.TF2` skeleton, `mods.yaml`, the no-HL2MP-name fact for the new assembly; (2) TF2 nav: the loader over the TF hull, double-jump reach class, per-team obstacle solidity (**needs the mod's visualizer fix first**: a TF2 lane item); (3) the TF2 taps + view (team as rendered, projectiles as actors, buildings as actors); (4) `TFKnowledge` (classes, weapons, packs); (5) the objective feed for CTF; (6) one tree: Soldier with Fetch/Deliver/Escort/Defend + the shared tactical layer; (7) class pick `joinclass soldier`, roster per team; (8) the TF2 bots page; (9) reopen `deferred.tsv:3,11,40,71` with facts; (10) `tools/tf2-gate` `bots-ctf` + `bots-payload` after a `changelevel` (12 bots, budget measured in P0) | P4, P5; **F02, F03, F04, F05, F07, F12, F13, F17–F19, F40** | 4–6 weeks |
| **P7 the other eight classes** | one lane per two classes, each a subtree + its ability rows + facts: Scout/Heavy, Medic/Engineer, Demoman/Pyro, Sniper/Spy (the Spy honesty fact gates the lane); composition targets and enemy-class learning; per-class curves; the class tactics of C.3.2 and the looks draw of C.3.3 | P6; **F08, F20, F21–F32** (the items each sub-class needs), **F37/F38** for the looks to show | 6–8 weeks |
| **P8 the other modes** | Payload (push/block/guard), KOTH, 5CP, then A/D after the mod ports team roles, PLR after race rules; `ss_ai_tf_cp` map; gate steps per mode; the regroup-and-push of C.6.1; Arena when `tf_logic_arena` exists | P6, P7 (Engineer/Medic for Payload); **F10, F11, F14, F15, F16** | 4–6 weeks |
| **P9 tune and hand off** | the TF2 jank run, the team measures, three-pass budgets, the daily gate's tf2 leg, the Descent note | P8 | 1–2 weeks |

Order of landing: P0 → P1 → {P2, P3, P4 in parallel lanes} → P5 → P6 → P7 →
P8 → P9. P3's steps 1–7 need only P1; its step 8 waits for P2 step 5.

## 7. Risks, with sizes

| risk | why it is real | what limits it | size |
|---|---|---|---|
| **Part C is huge** | nine classes × abilities × five modes × the mod's own gaps (visualizer solidity, team roles, race rules, Arena). Stock's bot tree is 134 files / 20,439 lines of behaviours (stage5 §5.1) and ours must be written from the vocabulary, not ported | the slice order (one class, one mode, end to end) and per-lane facts; "no code needed" is an acceptable result for a mode whose rules are not ported | 4–6 months of lane work |
| TF maps may not build a `.nav3d` | no TF map has been through `ssmap nav3d` (map-tools has no TF test); LZMA lumps, prop lump versions and the 255-voxel height are each unverified; 2fort is 1,500+ units tall but under the cap, dustbowl's three stages are wide | P0's spike before anything else in C; a larger voxel is the format's own escape hatch | blocking for C if it fails; a map-tools lane then |
| the human motion model (Phase 5b of the AI plan) is not done | the spin-bot detector and the distribution gates do not exist (`plan_hl2mp_ai.md:3305-3313`); a Sniper's flick and a Scout's tracking are exactly what 5b tunes | Q6: whether C waits; the aim curves exist and are calibrated for HL2MP | the risk is bots that look wrong in TF2, not blocked work |
| the callout layer breaks determinism | a republish keyed by tick + delay is new in the publisher | the 1/2/4-thread replay fact already exists and covers it unchanged | small if the fact is written first |
| `IJoinTeamPolicy` single owner | a core owner collides with TF2's | the default member on `IGameMode` (A.3 item 3); a no-owner fact | small |
| the in-process build's memory | no cap on a worker; a huge community map | leaf cap, timeout, the child-process fallback (Q9) | medium |
| the lift (P5) churns 26,173 lines of AI code | moves across three assemblies with the analyzers' gates (`Directory.Build.props:72,74`) | move commits per directory, recordings replay byte-identical, the HL2MP gate unchanged | medium, one careful week |
| gate pins to update | `ui-teammenu`'s refusal needle, `live-gate:4976`'s "no teammates", the chat fact; the TF2 gate's `classes` step is red already (`heavy_speed=unmeasurable`) | named in the steps that change them | small |
| TF2 lanes land polish while P6 lands bots | shared files (`TFJoinTeamCommand`, `TFAutobalance`, the deferred rows) | file ownership; the bot rows reopened in a bot lane only | small |
| only one HL2MP map is installed | TDM on dm_lockdown is the fallback arm only; team spawns are tested only on our map | `ss_ai_tdm`; any Steam `dm_*` the user installs is a bonus | small |

## 8. Decisions

### 8.1 User rulings carried in (dated)

From `docs/plans/plan_hl2mp_ai.md` §12.1 (2026-11-01 unless dated), binding
on every part here:

- The bot only does what a player can do and sees what a player can see;
  intent commands as usercmds; memory in memory only, never on disk.
- AI work off the main thread; more than one worker allowed; never the game
  thread; determinism by decide-tick-keyed inputs and slot-order merge.
- 100 % managed plus generated forwarder rows (the three accepted rows);
  no hand-written native code.
- `.nav3d` from map-tools is the nav mesh; no stock nav mesh, no NextBot
  port (vocabulary only); map-tools may be updated to build it from a bsp.
- Human motion: no spin bots, no always-quick snapping; skills map to flick
  accuracy and tracking lag; no human recordings committed.
- Push/hold/retreat on perceived ammo; Headhunter replaces Soloness in
  HL2MP's ten; TF2's binding restores the team trait beside them.
- The 60-point pool, cap 10; presets vary by spread; names from trait pools;
  the scoreboard tag; the roster survives map change and yields slots;
  `ai_bot_reroll_on_map`; close-range firing first; prefire and camping at
  pickups and chokes (2026-11-02); style reads a bot's own favourite skill
  at any difficulty (2026-11-02); grudges and habits; chat from a local
  model, off by default; hot reload restarts bots in their slots.
- The live gate's bots step is listen-server only.
- Bots know the map before playing; everything dynamic is perceived.

This plan's own rulings (2026-11-02, through the coordinator):

- **Scope added:** "also include voting and admin menu options to change
  game modes and game mode config" (Part D).
- **Scope added:** "also add on how server would gen nav3d on map change to
  a map without a nav3d file or broken nav3d file (can ship map tool
  assemblies with the server)" (Part E; it overturns plan §6.4's "`source#`
  never builds one", `NavLoader.cs:20`'s "never a fallback" and map-tools'
  "nothing of the compile tools").
- **"bots dont vote"**: bots neither call nor cast nor count; the electorate
  is the humans (D.3).
- **"map votes should be inscope"**: next-map, change-level, the end-of-map
  vote, the candidate list with nav state (D.3).
- **"accept defaults for open question"**: Q1 to Q13 in §8.2 as recommended.
- **"ai needs to handle a player swapping team"**: B.9.
- **"team members can walk through each other"**: A.7.
- **"make sure the body of the player that swapped team keeps the team color they used to be on"**: A.8.
- **"tf2 ai should have sub classes for each class that picks a different load out and changes how it acts"**: C.3.1.
- **"bots should identify spys dressed as them selves"**: C.3, after the honesty rule.
- **"bots should evaluate changing class on respawn but have a global cool down for all bots to not thrash"**: C.4.
- **"bots should ignore spectators as well"**: being done now on the tactics lane; B.9 treats going to or from spectator as a team change.
- **"a players score should be remembered if they rejoin for hl2dm and tf2
  (last 128 players)"**: A.6.
- **"prework to add ai tf2 should be broken out as stages that local models
  can impl"**: Part F and `docs/plans/tf2_ai/` (one file per stage, a README
  with the order and the dependency graph; each rated for local-model fit).
- **"scouts should double jump and try and be hard to hit around enemies,
  solders and demos should rocket/nade jump to get faster movement, spys
  should avoid common paths and jump on rocks why cloaked to avoid being
  seen, snipers should camp known site lines, engineers should build at
  choke points and setup teleporters, pryos should spy check with flame
  thrower and light sniper arrows on fire, randomly bots should try and walk
  through team mates for spy check"**: C.3.2 (class tactics) and the
  sub-class rows; it overturns C.7's "no blast-jump links in v1".
- **"bots can use any item their class supports"**: C.3.1's "Any item"
  rule: no ownership, no drops; the in-memory item source answers every
  schema item the class can equip whose behaviour works in the mod;
  `ItemIsAllowed` still binds.
- **"harder the bot, the nicer random items it has"**, then (the same day)
  **"nicer" means looks only**: part 1 (weapon tiers by difficulty) is
  dropped: the seeded weapon draw is the same at every difficulty and the
  audit table carries only "works in the mod". Part 2 stays: quality and
  cosmetics by difficulty (C.3.3), Easy plain, Expert decorated, seed-drawn,
  never changing a hitbox or how a bot plays.
- **"team bots in tf2 should also every now and then regroup if not making
  progress and push as a team"**: C.6.1, with the generic half in B.5
  (Regroup and Push as team roles) and its progress source in the objective
  feed (F13/F14 prework).

### 8.2 The questions asked, all answered as recommended

Ruled 2026-11-02: "accept defaults for open question". Each recommendation
below is now the plan.

| # | question | recommendation |
|---|---|---|
| Q1 | **Friendly-fire default** for HL2DM TDM: stock's `mp_friendlyfire 0`, or this build's `cs_sv_friendly_fire 1`? | **0, stock's**: TDM is new here, and the build's 1 was the deathmatch knob's accident, not a choice. The catalog makes it a one-click setting either way |
| Q2 | **One human and five bots: does a vote pass at once?** | **Yes on a listen server** (the host could do it from the admin menu anyway). Add `sv_vote_min_voters` (default 1) so a dedicated operator can demand 2+ |
| Q3 | **What admin means on a listen server**: the host only, or any player with a listed key too? | **Both, as today**: the host is always an admin; others only by key. No rcon |
| Q4 | **Does the pool need a team skill?** An eleventh row (Teamwork), or a profile that exists only in team modes? | **The eleventh row**, always present, inert in DM and excluded from DM's favourite draw: one profile format, one editor, one name pool; TF2 needs it anyway (plan §10 already says eleven) |
| Q5 | **Do bots follow human orders?** (`follow me`, `help`, `go`, `hold` in team chat) | **Yes, a four-word vocabulary**, Teamwork-weighted, with an admin toggle and `ai_bot_orders 0/1`; it is what makes playing with bots feel like a team, and it is player-equivalent (a human teammate reading chat) |
| Q6 | **Does Part C wait on Phase 5b** (the human motion model and its gates)? | **No for P5–P6, yes for P7's Sniper/Scout lanes**: a Soldier on CTF is testable without 5b; a Sniper's flick is 5b's subject. Schedule 5b as a lane in parallel with P3/P4 |
| Q7 | **Per-class skill pools?** Different totals or caps per class | **No**: one pool, per-class curves and spread weights (C.5), so a Hard bot is Hard as any class and the presets' sums stay the four numbers |
| Q8 | **Which TF2 modes first?** | **CTF on ctf_2fort** (gated, simplest objective), then **Payload on pl_badwater** (gated, the second map load), then KOTH, 5CP; A/D and PLR after the mod ports team roles and race rules; Arena only if `tf_logic_arena` lands |
| Q9 | **Nav build in-process or in a child process?** | **In-process** (E.2: content through the engine's own mount, bytes in, no runtime muxer to ship, a managed exception is a refusal); revisit as a child only if memory on a real community map proves it |
| Q10 | **Ship the on-demand build on by default or opt in?** | **On by default** (`ai_nav_build 1`): a server whose map has no nav otherwise has no bots, and the build never blocks a human. The client needs none of it (it ships no MapTools) |
| Q11 | **Autobalance in HL2DM TDM**: none (stock), the join limit only, or forced moves? | **Join limit off by default plus bots keeping the teams even** (B.6); forced moves of humans are not stock HL2DM and the bots make them unnecessary |
| Q12 | **End-of-map vote default**: stock's `choicesmode 0` or on? | **On (1)** for our mods: a listen server with bots otherwise has no in-game way to pick the next map |
| Q13 | **Deathmatch's mode id**: keep the core's `"null"` and show it as "Deathmatch", or register a named `dm` mode? | **Keep `null`, show "Deathmatch"**: registering a second core mode would duplicate the no-owner rules every mode fact depends on |

## 9. What could not be verified this session (and is marked as such above)

- Whether `MapSpawner` creates `info_player_combine`/`info_player_rebel`
  entities today (they are in the cleanup keep list only; moot on
  dm_lockdown).
- Whether the Steam `dm_*` maps beyond dm_lockdown carry team spawns (not
  installed here).
- `Hl2mpRoster.Reconcile`'s behaviour when a refused add is later possible
  (read to `:372` by a subagent): E.3 step 4 assumes it must be made to
  re-ask.
- Thread safety of the engine file seam off the main thread (E.3 reads on
  the main thread for that reason); whether a freshly written file is seen
  at once through GAME paths; whether the GAME path sees models inside a
  bsp's pak lump.
- `ssmap nav3d` on any TF map (P0's spike): LZMA lumps, static-prop lump
  version, `.phy` parsing, the height cap.
- Determinism of the build inside the game process (a fact in P4).
- The cost of the floor check (`-check`); the bsp-read cost of a TF map's
  props; `ai.sense.us` with 12 bots.
- Door behaviour on real TF maps (no gate step).
- The Engineer's wrangler, bow, flare gun and throwables in the TF2 mod (not
  audited).
- Whether `SelectWeapon` covers TF2's slots and whether `MapTools` passes the
  mod analyzers (SS0010–13) unchanged.
- `docs/divergences.md:40` is stale against the selector; `deferred.tsv:3,11,71`
  and `TFAutobalance.cs:32-35` say "no bots" while `IBotPlayers` exists:
  both are facts about the docs, not unverified claims, listed here so a
  reader does not trust the rows.

Every path, type and API named above was grepped or read this session (the
checks are in this session's transcript: a `check()` loop over 120 core,
TF2 and map-tools citations, each matched by identifier, plus the reads in
§0's table). Line numbers are those at the commits in §0.
