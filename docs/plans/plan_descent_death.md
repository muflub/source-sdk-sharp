# plan_descent_death: dying in TF2Descent (host D-H16)

Status: APPROVED by the user 2026-10-03 (with the store plan attached as Appendix A). Base: main cf771601. Host: 89b5dff, SDK 2.4.0.

## 1. What the user ruled (2026-10-03)

1. On death: a "You have died" screen, then the character reloads into town.
2. The way back: a teleport to the corpse, or going down level by level as usual.
3. The dead character's body stays on the ground.
4. Follow-ups (same day):
   - The server records the death. The client stays on the death screen until
     `RecordDeath` succeeds; after a 60 s timeout the server disconnects the player.
   - The teleport back is **a TF2 teleporter the player placed beforehand**.
   - Bodies: everyone sees them, and only the owner can loot one.

## 2. The host side (already live, D-H16)

- `Items.RecordDeath(lease, position)` moves everything carried onto the corpse and records
  where the character died. The service then sends that one character to town the normal
  way: `PrepareHop` to their level, our `LevelHop` checkpoints and releases, `Ready`,
  `Retry`. The rest of the party stays.
- The level stays up for a 15 min corpse grace after it empties.
- `Travel.ReturnToCorpse(lease)` takes the character alone back to the level they died on:
  - the same instance while it runs;
  - after it was reaped, a fresh instance of the same level, whose corpse items are in Lost & Found.
  - With no recorded death it is refused with `no_corpse_level`.
- `Items.LootCorpse(lease)` moves the corpse's items back to the character (as many as fit).
- All three exist in SDK 2.4.0; `IHostLink` has no wrappers for them yet.

## 3. What the game has today (surveyed at cf771601)

| Need | Today | Gap |
|---|---|---|
| Mod hears of a death | `ServerModule.OnPlayerKilled` (ServerModule.cs:2125) makes the body (`Corpses.Make`), then `Players.OnKilled` | No hook a mod can implement |
| Keep a dead player dead | `Players.Think` (Players.cs:217-260) respawns after `RespawnDelay` on attack/jump/reload. `IGameRules.CanRespawn` exists but is never consulted | No per-player veto |
| Body stays on the ground | `ss_ragdoll` (SsRagdoll.cs) is removed after `Lifetime = 15 s`. `Corpses.Limit = 12` trims the oldest | No way to keep one |
| Death screen | Server `IScreenEffects.Fade` (stay-out) plus the mod's HUD notice channel | None: buildable today |
| Interact with the body | `IPlayerButtons.Held(Use)` in a frame hook plus distance (as DevCommandsLive does) | None |

## 4. Design

### Core (src/server/managed): three generic gaps, each a hook a mod implements

- **C1 `IPlayerDeathHook.OnPlayerDied(int entIndex, EntityHandle attacker, Vector origin, EntityHandle body)`**
  - Dispatched through `IHooks<IPlayerDeathHook>` from `OnPlayerKilled`, after `Corpses.Make`, so the hook gets the body.
  - `origin` is read before anything moves the player.
- **C2 `IRespawnGate.MayRespawn(int entIndex)`**
  - `Players.Think` asks every gate before `Respawn`; any `false` keeps the player dead (observer `DeathView`).
  - With no gates, behaviour is unchanged.
- **C3 `Corpses.Keep(EntityHandle body)`**
  - The body is exempt from the 15 s lifetime and the 12-body trim, and lasts until the level ends.
  - The client already ragdolls an `ss_ragdoll` locally. A player who joins later, including one returning to it, sees it fall from its death pose.

### Mod (src/tf2_descent)

- **M1 `IHostLink`:** `RecordDeath(steamId, position)`, `LootCorpse(steamId)`, and `TravelDirection.Corpse`, which makes `Travel` call `ReturnToCorpse`.
- **M2 `DeathService`** (server; `IPlayerDeathHook`, `IRespawnGate`), hosted mode only. Unhosted play keeps the stock respawn.
  1. On death, mark the player dead (the gate refuses respawn) and keep the body (`Corpses.Keep`), remembering whose it is.
  2. Show the death screen: a stay-out fade to dark red, plus a large centred "You have died" HUD line, with "Returning to town…" under it.
  3. Call `RecordDeath(steamId, origin)` at once, so a level that dies in the next seconds cannot lose the death or the items moved to the corpse. The host's `PrepareHop` → `Retry` then takes the client to town. The party sees "<name> has died".
     - **The screen shows for 3 s** (user, 2026-10-03). `LevelHop` holds `HopReady` for a dead player until 3 s after the death. The death is already recorded, so only the hop waits.
  4. If `RecordDeath` fails (the host is unreachable or refuses), the server retries with back-off and the death screen stays up. After 60 s with no success the server disconnects the player ("Could not reach the hosting service"). The host still holds the character, as for any drop.
- **M3 Way back by a placed TF2 teleporter.** The user ruled on 2026-10-03; the host shape was agreed with host-db the same day as SDK 2.5.0.
  - **Placing it:** any class, with a bound key (a `tf2_descent_teleporter` command, bound by default). One teleporter per character; placing again moves it. The game calls `Travel.PlaceAnchor(lease, position, yaw)`.
  - **The entity:** a mod `ScriptedEntity` showing TF2's teleporter model (`models/buildables/teleporter.mdl`, from the mounted TF2 content). It is not TF2's engineer building: there is no PDA, metal, sapping or upgrades.
  - **Using it:** any party member can. Town shows **one exit pad per character** in the party (user, 2026-10-03), leading to that character's teleporter. A character with no teleporter placed has no pad (user, 2026-10-03: "multiple player teleport exits should be arranged nicely").
    - **Layout:** the pads stand on a teleporter plaza in town (mapgen `TF2DescentTown` reserves it), spaced evenly on an arc facing the spawn. Each pad is labelled with its owner's name and the depth. The arc's spacing and radius are worked out from the pad count, so 1 to 8 pads all fit without overlap.
    - **Which pads a player sees:** a town player sees the pads of their own party's teleporters. Pads appear and leave as party members place or move teleporters. Several parties can share the hub, so pads are per party.
    - Stepping on a pad calls `Travel.ReturnToAnchor(lease, anchorOwner)`. The host sends the caller alone to that teleporter's level: the running instance, or a fresh instance of the same level hash.
    - With no teleporters there are no pads. A refused call (`no_anchor`) shows "That teleporter is gone".
  - **Arrival:** the joining level reads a one-shot arrival `{position, yaw}` from the lease it already takes at join, and spawns the player there instead of at a map spawn.
  - **A fresh instance of the same level** (same seed) re-creates its teleporters at map start from the anchors the host lists on `MapReadyResponse`.
  - **Death** keeps the teleporter. `ClearAnchor` exists for a removed teleporter.
  - **Without a teleporter:** walk down level by level. The host's claims already lead to the level the player died on.
  - **`ReturnToCorpse`** is unused: the host deprecates it in 2.5.0.
- **M4 Looting:** a frame hook sees the body's owner within 96 units holding +use. It calls `LootCorpse`, shows "Recovered N item(s)" and removes the body. Others' bodies show "<name>'s body" and can't be looted.
  - The game has no inventory yet, so recovered items are only reported.
  - On a fresh instance (the level was reaped) there is no body. Arriving shows "Your body was lost; its items are in Lost & Found".

### M5 Portal view on each town exit pad (user, 2026-10-03)

"The teleport in town should show a portal image of the place it goes to, with it being a 3d image that changes perspective as the player moves around the teleporter."

- **What is possible:** a snapshot of the destination, not a live view. A client renders only the level it is on, and the destination runs on another server.
- **The look:** the pad's portal face uses Source's `WindowImposter` shader (src/materialsystem/stdshaders/windowimposter_dx90.cpp). It samples a cubemap along the view direction, so the image has depth and its perspective changes as the player walks around the pad.
- **Capture:** when a teleporter is placed, the placing client renders a small cubemap (6 × 128² faces) at the teleporter's position. This needs a little native code in the client shell, a render-to-target and pixel read-back, exposed as a bound interface (the minimum the engine needs).
- **Transport:**
  1. The client sends the faces to its level server in chunks.
  2. The server stores them with the host (the store, Appendix A, at Character scope; about 30-50 KB as JPEG, within the 64 KB value limit).
  3. The host serves them on the fastdl.
  4. A town client downloads them like a prefetched map, writes a `.vtf` and `.vmt` under `download/materials`, and the pad uses that material.
- **Before the pad's image arrives,** or with none stored, the pad shows a plain swirl.
- **Spike first (P6a):** prove that a runtime-written cubemap VTF plus a `WindowImposter` material renders on a pad in town, then build the capture.

## 5. Phases (commit per step; tests first, shown red)

| Phase | What | Gate |
|---|---|---|
| P1 | C1-C3 with facts: the hook gets the body and pre-move origin; a refusing gate holds a player dead past the delay with buttons held; no gate means stock; a kept body outlives 15 s and the trim | `make check` |
| P2 | M1 and M2 with a recording fake link: death leads to keep, screen, RecordDeath at once, HopReady held until 3 s after the death, respawn refused; unhosted is stock; a failing RecordDeath is retried and, after 60 s, the player is disconnected | `make check` |
| P3 | M3: the teleporter entity and placing it, the town exit pads with the arc layout (a fact per pad count, 1-8, with no overlap), the host calls (after the host ships them) | `make check`, `make town` |
| P4 | M4: own body looted once, others' refused, the fresh-instance notice | `make check` |
| P6 | M5: (a) spike, a runtime cubemap through `WindowImposter` on a pad; (b) client capture, the native render-to-target bound interface, and facts on the face layout; (c) transport through the store and fastdl; (d) live, where the pad shows the teleporter's view and the perspective shifts as the player moves | `make check`, live |
| P5 | Live on k3s with the host session: die on depth 1 (`kill`), place a teleporter, die, see the screen, land in town, take the exit pad, arrive at the teleporter, walk to the body, loot it. Also: the party member stays; a second death works; after a reap the body is lost | gate 2x, k3s run, numbers |

P1 landed on main at bc10ca92. P2 landed on this branch at d56f80d2
(2026-10-08): the DeathServiceTests facts are the gate sentences above,
run by the tf2_descent leg, plus two HostLinkTests facts for the
ReadyAfterHold seam.

## 6. Decisions

- **Q1:** the server retries `RecordDeath` for 60 s, then disconnects the player.
- **Q2:** the way back is a player-placed teleporter.
- **Q3:** bodies are seen by all and looted only by their owner.
- **Q4:** any class places a teleporter, with a bound key.
- **Q5:** any party member can use a teleporter. A fresh copy of the same level re-creates it. Death keeps it.
- **Town exits:** one pad per character (their teleporter), any party member may use it, arranged on an arc on a town plaza. The caller travels alone.
- **Q6:** the death screen shows for 3 s. The death is recorded at once; the hop waits.

**Dependencies:** P3 needs host SDK 2.5.0 (`PlaceAnchor` / `ClearAnchor` / `ReturnToAnchor(lease, anchorOwner)`, the party's anchors listed at the hub, the anchors on `MapReadyResponse`, the arrival on `LeaseResponse`). P1, P2 and P4 do not.

## Appendix A: the host key/value store (attached by the user, 2026-10-03)

Status: draft for review, 2026-10-03. Base: main 8c87d0aa. Host side: requested of host-db,
proposed to ride in SDK 2.5.0 with the teleporter anchors (plan_descent_death §4 M3).

### 1. What the user ruled (2026-10-03)

"A key/value store api bound to instance and session so the server can store and retrieve
data across restarts."

- **Scopes:** all three of these.
  - **Level:** keyed by the level hash, so a fresh instance of the same level (after a reap or crash) reads the same data.
  - **Instance:** keyed by the running instance; it survives a restart of that instance's game server, and a fresh instance starts empty.
  - **Character:** follows the character across hops, levels and reconnects.
- **Sizes:** small values, for game state rather than bulk data: bytes, up to about 64 KB each, a few hundred keys per scope. The host sets the exact limits.

### 2. Host side (host-db; shape to be agreed)

- Per scope: `Get`, `Set`, `Delete`, `List(prefix)`, with an optional expected version on `Set` (compare-and-set). Two servers can hold a character's data around a hop.
- **Who may call:**
  - Character scope: whoever holds the character's lease.
  - Level and Instance scopes: the level server, for its own instance and level hash.
- **Lifetimes:**
  - Instance data goes when the instance is reaped.
  - Level data goes when the level's last claim goes.
  - Character data lasts as long as the character.
- Over-limit writes are refused with a reason, never truncated.

### 3. Game side

- **G1:** `IHostLink` wraps the SDK calls. Like every host call they run on the SDK thread and complete on the game thread.
- **G2:** `IHostStore`, a tf2_descent server service that mods inject:
  - `Task<byte[]?> Get(StoreScope, string key)` (with a character scope by steam id)
  - `Task<bool> Set(StoreScope, string key, ReadOnlyMemory<byte> value, long? expectedVersion)`
  - `Delete` and `List`
  - Typed helpers over UTF-8 and JSON for small records.
  - **Unhosted:** an in-memory store with the same contract, so local play and tests behave alike. It does not persist (an unhosted server has no host to outlive it).
- **G3 Facts:** the round trip on a recording fake link; scope keys go to the right SDK scope; the size limit refuses before sending; compare-and-set conflicts surface as `false`; the unhosted store matches the hosted contract.
- **G4 Live (k3s):**
  1. Write Level, Instance and Character keys.
  2. Kill the level pod's game server: the Instance data survives, if the pod restarts in place.
  3. Reap the instance and re-enter the same level: the Level data is present and the Instance data is gone.
  4. Hop to another level: the Character data follows.

### 4. Open

- **Instance restarts:** the host plan says a level pod is never restarted (Q12) and the hub pod is. "Across restarts" for the Instance scope therefore means the hub, plus any restart policy the host adds. To confirm with host-db.
- **The first user:** the teleporters themselves live in the host's anchor rows, not this store. What else will use it is open.
