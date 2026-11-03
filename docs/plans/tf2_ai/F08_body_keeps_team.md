# F08: a body keeps the team and class it died on (TF2)

Base: `dae055998`. Worktree `../ss-tf2ai-F08`, branch
`tf2ai-F08`. Size M, two commits. Fit: Qwen. Depends on nothing.

## Goal

A corpse never changes colour or model when its owner changes team (ruled
2026-11-02). Stock networks the body's own team and class on the ragdoll;
this build must too, and TF2's material proxies must read the body's own
values, never the owner's current team.

## Stock, read this session (`tf_player.cpp`)

- `:15625`: the ragdoll's `m_iTeam = GetTeamNumber()` at creation.
- `:15870-15880`: when the player is disguised (`bDisguised`) the ragdoll's
  `m_iTeam` is the disguise team and `m_iClass` the disguise class; otherwise
  the real team and class.
- Client `c_tf_player.cpp:533` receives `m_iTeam`; `:712`, `:936`, `:1600`
  read it for the body's effects and colour.

## Code today

- The core ragdoll carrier already has `Team` (`src/shared/managed/
  RagdollNetState.cs:178`, a byte) and `PlayerIndex` (`:82`); `Corpses.Make`
  (`src/server/managed/Corpses.cs:181`) builds the body from the player's
  model at death (`:199`).
- TF2's proxies (`src/tf2/client/Proxies/TFMaterialProxies.cs:141`) read the
  team of a *player index* from the player resource (`resource.State.Team[player]`),
  and `TFProxyPlayer(State, Team, IsLocal)` (`:210`) is built that way; the
  team colour arms are at `:249` and `:262`. A body whose material proxy is
  resolved through its owner's index would turn the owner's new colour.
- `src/tf2/server/Player/TFBurnRagdoll.cs:26` attaches TF2's burn state to a
  ragdoll on `OnPlayerDied`: the place TF2 already touches the body at death.

## Build

1. **Server** (commit 1): in the TF2 death path that already reaches the
   ragdoll (`TFBurnRagdoll.OnPlayerDied`'s neighbour; a new
   `TFRagdollTeam.cs` in `src/tf2/server/Player/`, `IPlayerDeathHook` in the
   TF2 player band `999_3xx`, Valve header + row "ports the ragdoll's team
   and class at creation (tf_player.cpp)"), write the body's `Team` from the
   disguise when `ITFPlayerData.Networked(index)` says disguised
   (`TFPlayerNetState.DisguiseTeam`, `IsStealthed`/the disguise condition
   through `TFPlayerData.cs:62`), else the real team; add `Class` to a TF2
   ragdoll companion state if `RagdollNetState` has no class field (it has
   none: check `:39-180`), as a small `[StandaloneNetState]` keyed by the
   ragdoll's index, under the 2048-byte tier (`TFNetStateTierTests` guards
   it) and in `TFMapCleanupFilter.PortPreserved` if made at level init (it
   is not: made at death). Facts in `src/tf2/tests/Player/TFRagdollTeamTests.cs`
   (new): `ABodyCarriesTheTeamItDiedOn`, `ADisguisedSpysBodyWearsTheDisguiseTeam`,
   `AnOwnersLaterTeamChangeLeavesTheBodyAlone` (change the owner's team through
   `ITeamRoundRoster.ChangeTeam` after death; the body's `Team` is unchanged).
2. **Client** (commit 2): the proxies read a ragdoll's own `Team`/`Class`
   when the entity is a ragdoll (the `RagdollNetState` is on it), never
   `resource.State.Team[owner]`. Fact in `src/tf2/tests/Client/` beside the
   proxy facts (grep `TFMaterialProxies` there): `AProxyOnABodyReadsTheBodysTeam`:
   with the owner's resource row on the other team, the proxy answers the
   body's. If the client test rig cannot make a ragdoll entity, say so in
   the hand-back and keep the server facts: do not fake it.
3. Live check (command-readable, no screenshots): `lane-boot.sh tf2
   ctf_2fort <port> 'cmd jointeam red' 'cmd joinclass scout' 'wait 3'
   'kill' 'wait 1' 'cmd jointeam blue' 'wait 2'` then the dev API's
   `/entity` listing: the ragdoll entity's `Team` field reads 2 while the
   player's row reads 3. Paste both lines.

## Red first

`AnOwnersLaterTeamChangeLeavesTheBodyAlone` with the proxy still reading the
owner (or, server-side, with `Team` written from the roster at proxy time):
`Failed: 1`, then `Failed: 0`.

## Commands

`make managed-test FILTER='FullyQualifiedName~TFRagdollTeam'` → `Failed: 0`;
`make managed-test FILTER='FullyQualifiedName~TFNetStateTier'` → `Failed: 0`;
`make test`; `tools/valve-header check` 0.
