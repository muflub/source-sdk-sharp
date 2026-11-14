# plan_tf2_rpg.md — a Diablo-shaped action RPG in the Team Fortress universe, written in managed C# on the SourceSharp host

Measured at `85a7449d1` (`integrate-p13`, the merge that landed the managed entity
port). Working title **Descent** (placeholder: mod dir `game/mod_descent`, assembly
prefix `Descent.*`; rename is one sed). Untracked until reviewed.

The ask, in execution order:

1. A **town** hub map: four stores (weapons, armour, healing, magic), helper NPCs, a
   persistent stash, and a church whose stairs lead down.
2. **Fifteen dungeon levels** below the church, each assembled from a library of base
   rooms re-arranged per seed; mini-bosses on 5 and 10, the final boss on 15; enemies
   harder with depth.
3. A **hero**: health, mana, inventory, per-level attribute points, a skill tree that
   unlocks powers by level, spells cast from mana.
4. **Enemies**: the Mann vs. Machine robots, varied across many attributes; every kill
   gives XP and a chance of an item or Australium (gold).
5. **Items** with rolled stats; the more powerful the item, the higher its chance of
   carrying an unusual effect.
6. **Parties** share one set of levels; everyone else gets their own unique levels.
7. **Death** is not permanent, but the body drops everything carried and must be
   retrieved.
8. A **service** that persists player state, generates maps, and runs dedicated
   servers on demand, one per level instance.

> **Map compilation is now planned in `plan_maptools.md`** (2026-09-20): vbsp, vvis and
> vrad ported to managed C#, multithreaded, with a compile cache (its §10a) and — built for
> exactly this plan's room library — **room objects and a level linker** (its §10b): each
> room is compiled once, a level is *linked*. That plan supersedes R3b and §8 6e here and
> changes 0d, 6a, 6c and 6d; each place is marked below. Until its phases land, 6d's
> `WineMapCompiler` remains the working path and nothing in this plan waits on it.
> That plan's test-map catalogue (its §2a) includes a **room track, R0–R4**, built from a small
> socket kit and re-arranged per seed: it is the working prototype of 6a–6d here, and its R4
> adversarial rooms are the fixtures 6a's linter rules are written against.

---

## 0. What was decided, what was defaulted, what was measured

### 0.1 Rulings (asked and answered 2026-09-20)

| # | question | ruling |
|---|---|---|
| R1 | hero classes | the nine TF2 classes are nine hero classes, each with its own skill tree; shipped in waves |
| R2 | camera | first-person TF2; the Diablo is in loot, stats, skills and structure |
| R3 | level generation | the service stitches a VMF from room prefabs per seed and **compiles a BSP** (vbsp / vvis -fast / vrad -fast); clients download it |
| R3a | load time | levels are **pre-generated into a pool**; on-demand compile is the fallback, never the normal path (§7c) |
| R3b | map compilers | Wine-run Windows tools are the bootstrap path; ~~the target is native Linux builds usable as shared objects (§8, 6e)~~ **superseded 2026-09-20: the target is the managed C# compilers of `plan_maptools.md`**, in-process, behind the same `IMapCompiler` |
| R4 | party loot | Australium is shared (split among nearby members); item drops are instanced per player |
| R5 | corpse | a corpse lives only while its level server is up; when the server is reaped its contents move to a **Lost & Found** NPC in town, reclaimable for a fee |
| R6 | progression | the church offers any level already reached; layouts re-roll on every entry |
| R7 | town | shared hub servers (many players), more hubs spun up on demand; dungeon instances are private per party |
| R8 | party / PvP | party max 4; secure player-to-player trading in town; optional **hardcore** (permadeath) characters; no PvP |
| R9 | code base | **managed-first on SourceSharp**: classes, weapons, robot AI and all RPG systems are C#, re-implemented with the TF C++ as the reference; only TF *assets* are reused |
| R10 | service stack | ASP.NET Core + SQLite + level servers as local child processes, behind interfaces so DB and orchestrator can be swapped later |
| R11 | identity | SteamID trust, no ticket validation. Game servers authenticate to the service with a shared secret; clients never mutate state through the service. Spoofable on the open internet — accepted for v1 |
| R13 | carrying capacity | backpack and stash both **start small and grow through skill upgrades** — capacity is bought with skill points, in competition with combat skills (§4, 2d) |
| R20 | AI temperament | every robot brain has a set of **AI skills with levels** — aggression, accuracy, cover, soloness and more — rolled at random so robots fight in different styles. Robots of the same size get the same number of points to spread across them, and **the bigger the robot, the more points it has** (amended: the budget was first ruled equal for all) (§4 2f, Appendix B.11) |
| R19 | room size | **rooms are big enough to rocket jump in** — at least 60 % of each tileset's combat rooms; up to 40 % may be low-ceilinged, which is wanted variety, not a defect (§8 6a) |
| R18 | robot AI | a **new AI system that does not run on the main thread**. It takes its inputs from game events (sound, sight, gameplay events), keeps its own memory of them, and outputs move commands that the main thread actions on the next frame (§7 5c, Appendix B) |
| R17 | skills vs weapons | a skill is **never tied to a particular weapon the player finds**. Every skill is a **special action that costs mana** and works whatever is equipped, or an **upgrade**. An upgrade helps all weapons, or **one class of weapon** — rocket launchers, melee, shotguns … — but never a named item; or it is a **utility upgrade** — inventory size and stash size (R13), health regeneration over time or from damage dealt, and the like (§4 2e, Appendix A) |
| R16 | robot size | robots **vary in model scale** as one of their rolled attributes, from runts to giants — and size is a stat, not a skin (§4 2f, §7 5a–5d) |
| R15 | managed only | `Descent.Server` and `Descent.Client` are **managed code and nothing else** — no C++, no P/Invoke, no `unsafe` native calls of their own. Where the game needs something the managed host cannot do yet, the answer is to **port what is required** into the SourceSharp managed layer, game-agnostic, not to write Descent-flavoured C++ (§2 0b, §16) |
| R14 | item economy | **durability, identify, gambling and crafting are all in v1** (§4, 2h–2k) |
| R12 | item model | base types are TF2 weapons with rolled affixes; armour is the cosmetic slots (hat / body / feet / trinket) giving defence and resistances; magic is a mana-cast spell system themed on the Halloween spells and class abilities |

### 0.2 Defaults I chose because nobody ruled — overrule any of them by editing this table

| # | gap | default |
|---|---|---|
| D1 | attribute numbers (R22 sets the shape) | four attributes, 5 points per level: **Brawn** (Diablo's Strength), **Smarts** (Magic), **Aim** (Dexterity), **Grit** (Vitality). Per-class starting values and caps, and what each point buys, in Appendix C. Respec sold by Merasmus at a rising price |
| D2 | level cap / skill points | character level cap 30 in v1; 1 skill point per level; skill tiers unlock at 1 / 6 / 12 / 18 / 24 / 30; three tabs per class **plus one shared Mercenary tab** of class-neutral passives, which is where R13's capacity skills live |
| D3 | class order | wave 1 Soldier (vertical slice), wave 2 Heavy + Medic, then Pyro/Demoman/Sniper, then Scout/Engineer/Spy |
| D4 | inventory numbers (R13 sets the shape) | one-cell slots. Backpack starts at **12**, *Deep Pockets* adds 4 per rank, 7 ranks → 40. Stash starts at **20**, *Mann Co. Storage Plan* adds 16 per rank, 5 ranks → 100. Equipment slots (primary, secondary, melee, hat, body, feet, 2 trinkets, **amulet** — R29; TF medals are its models, and it is the slot whose affix pool can roll `+skills`) and a potion belt of 4 are fixed. The stash is per account and shared across its characters, so its capacity is the **highest** *Storage Plan* rank among that account's living characters; hardcore has its own stash and counts only hardcore characters |
| D5 | rarity ladder | TF quality names: Stock (0 affixes) → Vintage (1–2) → Strange (3–4) → Unusual (5–6 + an unusual effect) → Australium (hand-authored uniques) |
| D6 | what drops on death | everything: equipped, backpack, belt and carried Australium. Only the owner can loot their corpse. Corpse kept alive 15 min after the instance empties, then Lost & Found at a fee of 25 % of the contents' vendor value |
| D7 | hardcore death | character is marked fallen and unplayable; gear is lost; name goes on a memorial wall in town |
| D8 | party entry rule | the party may enter any level the **leader** has reached; members earn "reached" as they arrive |
| D9 | traversal | one-way descent. Stairs down → next level; Town Portal item → town and back once into the same live instance |
| D10 | XP sharing | each member within range of the kill receives full XP, scaled by hero-vs-monster level difference |
| D11 | party scaling | +60 % robot health and +25 % pack size per extra member; loot rolls are per player so are unaffected |
| D12 | difficulty tiers | one tier in v1; the schema carries a `difficulty` column from day one so Mann-Up / Expert replays are data, not migration |
| D13 | bosses | L5 Sir Nukesalot (giant demoman); L10 Major Crits with a Tank; L15 Captain Punch as Gray Mann's enforcer, multi-phase. All use models TF2 ships |
| D14 | tilesets | 1–5 church catacombs (Mann Manor / Helltower assets), 6–10 Mann Co. mines (coaltown / mannworks), 11–15 Gray Mann's robot works (bigrock / rottenburg interiors) |
| D15 | town NPCs | weapons: Saxton Hale; armour, **repair and the crafting workbench**: an Engineer; healing: a Medic; magic: Merasmus; **identify**: a Spy (he has a dossier on everything); **gambling**: a Scout hawking Mann Co. supply crates; stash: a resupply locker; quests/voice: the Administrator; Lost & Found: a Soldier. All models TF2 ships |
| D16 | potions | health = Sandvich / medkits; mana = Bonk cans; town portal = Eureka Effect charge; identify = a *Dossier* consumable; field repair = a *Repair Kit* (crafted only) |
| D17 | quests beyond a main line | **out of v1** |
| D21 | durability numbers (R14) | weapons lose 1 per N uses (N by weapon family — a minigun is not a shovel), armour loses 1 per M hits taken. At 0 the item is **broken**: stays equipped, grants nothing, and a broken weapon fires as its Stock base. Never destroyed by wear. Repair cost scales with ilvl, rarity and points missing. Death costs no extra durability — dropping everything is penalty enough. An *Indestructible* affix exists; Australium-rarity items have it implicitly |
| D22 | identify (R14) | Vintage and above drop **unidentified**: base type and rarity colour visible, affixes and unusual effect hidden. Unidentified items cannot be equipped; they can be stashed, traded, salvaged and sold (at the Stock price). The Spy identifies for a fee scaled by ilvl; a Dossier does it in the field |
| D23 | gambling (R14) | the Scout sells sealed crates by **base type** at a steep Australium price; ilvl is the buyer's level ± 2, rarity is rolled at opening on a published odds table weighted well below dungeon drops for top rarities, result arrives unidentified. In-game Australium only — no real-money path exists or is planned |
| R23 | automap | an **automap** that fills in as rooms are entered (§4 2m, Appendix D #1) |
| R24 | dungeon furniture | **shrines, fountains, containers, explosive barrels and traps** in the rooms (§4 2l, D #3) |
| R25 | robot resistances | robots have **resistances and immunities** per damage type (§4 2f, D #4) |
| R26 | party revive | a downed hero can be **revived by a party member** before the corpse-run rule applies (§4 2g, D #5) |
| R27 | tomes and scrolls | spell **tomes level up by reading more copies**; single-use **scrolls** (§4 2e, D #6) |
| R28 | cursed stats | item stats can be **negative as well as positive**, so one item can carry both (§4 2b, D #8) |
| R29 | amulet | an **amulet** equipment slot beside the two trinkets (D4, D #9) |
| R22 | attributes | characters **invest level-up points into attributes**, on a Diablo-style character sheet — base and current columns, points to distribute, and the derived numbers beside them (reference: the Diablo 1 sheet supplied) (§4 2a, Appendix C) |
| R21 | single presence | the gateway also guarantees **a player is only ever in one backend server at a time** (§9 7e) |
| D25 | one address for the whole game | a **gateway**: a UDP relay the client always connects to, which forwards each client's traffic to whichever instance the service has routed them to (§9, 7e). Instances bind loopback / private addresses and are never reachable directly. Adopted if Phase 0h passes; otherwise travel is 0f's `redirect` to per-instance addresses |
| D24 | crafting (R14) | TF2's own metal ladder: **salvage** an item → Scrap / Reclaimed / Refined Metal by rarity and ilvl (3 Scrap = 1 Reclaimed, 3 Reclaimed = 1 Refined, both ways). Recipes at the Engineer's bench: craft a random item of a chosen base type at your level; **re-roll one affix** of an item (the others locked, cost rising each time on that item); upgrade rarity one step (Stock → Vintage → Strange only — Unusual is never craftable, it must drop); combine three potions into the next size; Repair Kit. Metals stack to 100 per slot |
| D18 | UI tech | managed immediate-mode panels over the existing managed HUD surface, subject to the Phase 0 measurement (§2, 0e) |
| D19 | robots as entities | `[ScriptedEntity]` NPCs, **not** fake clients — a horde must not spend player slots |
| D20 | navigation | a managed nav graph stitched from per-room fragments by the layout generator — the generator already knows every walkable cell, so no native nav mesh and no runtime `nav_generate` |

### 0.3 What was measured (each re-verified by a second command this session)

| fact | value | command |
|---|---|---|
| TF / NextBot / nav rows in the build | **0 / 0 / 0**, debug and release | `grep -c 'game/server/tf/\|game/shared/tf/\|game/client/tf/'`, `'NextBot/'`, `'server/nav_'` over `src/_vpc_/ninja/sdk_csharp_*.ninja` |
| managed TF code | 0 files | `grep -rli 'CTFBot\|tf_player\|mann_vs_machine' src/sourcesharp/managed` |
| managed entity sites | 285 `[ScriptedEntity(` under `SourceSharp.Server`; families are Brushes … Weapons, **no NPC/AI family** | `grep -rn '^\s*\[ScriptedEntity('` ; `ls Entities` |
| ABI | `SOURCESHARP_ABI_VERSION 244` | `src/sourcesharp/host/sourcesharp.h:38` |
| HTTP client / Steam ticket code | 0 files | `grep -rl 'HttpClient\|GetAuthSessionTicket\|BeginAuthSession' src/sourcesharp` |
| per-client transmit control | only `SetTransmitAlways(entity, bool)` — no per-recipient filter | `EngineAreas.Entities.cs:543` |
| map compilers on Linux | not built (`[$WINDOWS]` in `projects.vgc`); `make map` runs Orange Box-era `vbsp.exe / vvis.exe / vrad.exe` under Wine, BSP v20 | `Makefile:301-314, 600-603` |
| compilers as libraries | `vvis` and `vrad` already expose `ILaunchableDLL` (`vvis.cpp:1268`, `vraddll.cpp:22-23`); `vbsp` is an executable only, no interface | `grep -rn 'EXPOSE_SINGLE_INTERFACE\|ILaunchableDLL' src/utils/{vbsp,vvis,vrad}` |
| maps from code | `SourceSharp.MapGen` exists: `Vmf.cs` emitter, `Sandbox.cs` grid layout, `BspEntities.cs` reader | `ls src/sourcesharp/managed/SourceSharp.MapGen` |
| content mounted | `mod_sharp` mounts appid 243750 HL2/HL2MP VPKs only; nothing from appid 440 | `game/mod_sharp/gameinfo.txt:59-74` |

**Build facts every phase depends on.**

**Everything TF is reference, not dependency.** `src/game/server/tf/bot/`,
`player_vs_environment/` (25 files: populators, spawners, tank, upgrades),
`src/game/shared/tf/`, `src/game/shared/econ/`, `halloween/` are all on disk and none
of it links. Per R9 and CLAUDE.md they are what we read when unsure what the logic
should do; nothing in this plan adds them to a `.vpc`.

**There is no per-game managed assembly today.** `SourceSharp.Server/Client/Shared`
are HL2DM-flavoured and discovery is reflection over them. How a second game's
assemblies get loaded is unmeasured and is Phase 0's first question.

**Instanced loot is the one known gap, and R15 says how gaps close.** R4 requires an
entity only one client can see, and the ABI has only `SetTransmitAlways`. The
`CheckTransmit` loop is native by an earlier plan's standing ruling
(`ServerEntities.cs:16-21` records it — it is the per-client, per-entity hot loop), so
the port is of the **decision**, not the loop: a managed, game-agnostic
`TransmitRecipients` field on the entity that the existing native loop reads as data.
Nothing in the shell learns what "loot" or "Descent" is. Every later gap follows the
same order of preference: (1) already possible in managed; (2) port the stock C++
logic into `SourceSharp.*` managed; (3) only if the engine itself must call or be
called — a generic appended ABI slot, with the policy still in C#. Memory
`abi-version-bumps-collide` applies — lanes append, the integrator bumps once.

**Orchestrating many servers on one box has bitten this repo before.** Memories
`multiple-game-instances` (own `TMPDIR` each), `live-gate-port-is-not-the-one-you-asked-for`
(the bind loop walks upward silently; read the bound port from `/proc`),
`devapi-socket-dir-is-machine-wide`, `steam-ticket-fails-with-many-instances` and
`pkill-f-kills-its-own-wrapper` are requirements on Phase 7, not anecdotes.

---

## 1. The shape of the thing

```
                    ┌──────────────────────── Descent.Service (ASP.NET Core, SQLite) ───────────────────────┐
                    │ Characters+lease │ Stash │ Parties │ Trades │ LostAndFound │ MapForge │ Orchestrator  │
                    └───────▲──────────────────────────────────────────────▲───────────────────┬────────────┘
        server secret, HTTPS│                                              │ fastdl (read-only) │ spawns / reaps
   ┌────────────────────────┴───────┐      redirect       ┌────────────────┴──────────┐        │
   │ hub server  (descent_town.bsp) │ ───────────────────▶ │ level server (dl_<seed>.bsp) │ ◀──────┘
   │ shops, stash, trade, church    │ ◀─────────────────── │ robots, loot, corpses, stairs│   one process per party per level
   └────────────────▲───────────────┘   portal / death     └────────────────▲──────────┘
                    └────────────────────── game client ─────────────────────┘
```

| project | depends on the engine? | holds |
|---|---|---|
| `Descent.Rules` | **no** — pure C# | attributes and derived stats, XP curve, item bases / affixes / rarity / unusual roll, loot tables, inventory + stash + trade models, skill definitions and effect maths, robot templates and champion affixes, death rules, the layout generator, wire DTOs. Shared by game **and** service, so both sides roll identically |
| `Descent.Server` | yes | hero player, weapons, the robot entity and the main-thread half of the AI (sensors, command applier), spawn director, pickups, corpses, vendors, service client, dev commands |
| `Descent.Client` | yes | HUD orbs, panels (inventory, character, skills, shop, stash, trade, party), prediction hooks |
| `Descent.Ai` | **no — and it may not**: no reference to `SourceSharp.Abi` at all | robot brains: perception memory, threat, decisions, path and steering, the command stream (Appendix B). Having no engine reference is how the threading rule is enforced — code that cannot see the engine cannot call it from the wrong thread |
| `Descent.MapForge` | no (shells out to the compilers today; calls `SourceSharp.MapTools` in-process once `plan_maptools.md` lands) | room library loader, VMF merge, compile driver, nav stitch, packager. Built on `SourceSharp.MapGen`'s `Vmf.cs` |
| `Descent.Service` | no | the API, persistence, pool manager, orchestrator |
| `Descent.Tests`, `Descent.Service.Tests` | no | xUnit `[Fact]`s, one behaviour per fact |

Two rules fall out of that table. **Anything that can live in `Descent.Rules` must** —
it is the only place logic is testable without a map load, and the service needs the
same code to validate what servers report. **The server is authoritative for every
roll**; the client renders.

Every system also ships **dev commands** (`descent_give`, `descent_setlevel`,
`descent_spawn_robot`, `descent_inv`, `descent_die` …) so each live gate is driven
headlessly through `devapi` (needs `sv_cheats 1` — memory `ent-fire-needs-cheats`).

---

## 2. Phase 0 — measure before building (no product code)

Each sub-step writes its finding into §0.3 and, where it overturns a default, into §0.2.

### 0a. How does a second game load?
Read `SourceSharp.Bootstrap` and the host loader: which assemblies are reflected for
`[ScriptedEntity]`, and what decides that. Rule on: separate `Descent.*` assemblies
selected by mod dir (preferred) vs. folders inside `SourceSharp.Server`.
- Gate: a written ruling naming the file and line that picks the assemblies.

### 0b. Can a managed NPC be a robot?
With a throwaway `[ScriptedEntity]` on `ss_sandbox`: set a TF robot model at scales
0.6, 1.0 and 1.75 (R16 — hull, hitboxes and attachments must follow)
(`models/bots/soldier/bot_soldier.mdl`), play a sequence, drive pose parameters
(`move_x`, `move_y`, `body_pitch`), move with collision, take hitbox-grouped damage,
attach a weapon model.
- Gate: a table of the five capabilities (each at all three scales), each ruled **works / port to managed (naming the stock C++ being ported and its managed home) / needs a generic ABI slot (naming it, and why a port cannot do it)**. "Write it in C++" is not an available ruling (R15).

### 0c. TF content mount
Mount appid 440 VPKs in a scratch `gameinfo.txt`; confirm robot, class, weapon and
cosmetic models, sounds and particles load on client **and** on a dedicated server
(which needs the TF2 DS depot, appid 232250).
- Gate: `devapi` reports the model precached with non-zero hitbox count on both sides; the missing-content list is empty or written down.

### 0d. Compile budget
Hand-merge eight sandbox rooms into one VMF and time `vbsp`, `vvis -fast`,
`vrad -fast` under Wine, plus BSP size raw and bz2.
- Gate: a timing table. It sets the pool depth in §7c; **it cannot veto R3** because of R3a.
- The table is taken again, on the same eight rooms, when `plan_maptools.md` reaches its Phase 7 (managed monolithic compile) and its Phase 9 (**link** of precompiled room objects, its §10b); the pool depth is re-derived from the link figure. No speed-up is assumed here — that plan quotes none until it is measured.

### 0e. UI surface
What can managed client code draw and read today (`SourceSharp.Client/Hud*.cs`):
rects, text, textures, mouse cursor capture, key focus?
- Gate: capability table; D18 confirmed or replaced.

### 0f. Server-to-server travel
Can a server send a client to another address (`redirect` / client `connect` via a
server-issued command), and does it survive the map download?
- Gate: one client hops between two local servers, driven by `devapi`, in the **main checkout** with the NVIDIA offload vars (memory `launch-game-on-the-nvidia-card`).

### 0g. Dedicated headless server
Does `mod_sharp` run as a dedicated server with no client, several at once?
- Gate: three concurrent servers, each with its own `TMPDIR` and devapi socket dir, bound ports read from `/proc`, all answering `devapi`.

### 0h. The gateway (D25)
A throwaway UDP relay in C#: one public port, one backend-facing socket per client
session. Measure: a client joins a backend through it; the server issues `retry`, the
relay's route is flipped meanwhile, and the client lands on a **second** backend at the
same address; added latency; whether six clients connecting at once through one relay
source IP trip `MAX_REUSE_PER_IP`, and whether giving each session its own `127.x.y.z`
source address clears it; what the server does about Steam auth for a loopback peer.
- Gate: the hop works end to end under `devapi`, with the four measurements written into §0.3. If it fails, 7d falls back to 0f's `redirect` and D25 is struck.

### 0i. A managed thread beside the engine (R18)
Nothing under `SourceSharp.Server` starts a thread today (`grep -rl 'new Thread\|Task.Run'`
finds only generated files), so this is unmeasured ground. With a throwaway worker
thread that never touches the engine: does it run freely while the main thread is
inside native code; does the host or any managed callback assume a single thread;
which GC mode is the host in, and how long does a gen-0 / gen-2 collection triggered
**by the worker** stop the main thread; does the thread join cleanly at level shutdown
across two map loads?
- Gate: answers in §0.3, with the GC pause numbers. They set Appendix B's allocation rule and frame budget; they cannot veto R18, only shape it.

### Phase 0 exit
§0.3 has a row per sub-step with its command. Commit: this file only (after review).

---

## 3. Phase 1 — skeleton

### 1a. Projects and mod dir
Add the eight projects to `SourceSharp.slnx`; `game/mod_descent/` with `gameinfo.txt`
(0c's mounts), output wired per 0a. `make descent` builds it; a `descent` leg joins
`tools/run-test-legs`.
- Gate: `dotnet build` zero warnings; a `descent_marker` `[ScriptedEntity]` spawns on a test map under `mod_descent`, seen through `devapi ents`.

### 1b. The data pipeline
Content is data, not code: `game/mod_descent/scripts/descent/*.json` for item bases,
affixes, skills, robot templates, loot tables, level table. `Descent.Rules` loads and
**validates** them (unknown stat, dangling reference, out-of-range roll → load error).
- Gate: a fact per validation rule, each mutation-verified red (memory `the-check-that-cannot-fail`).

### Phase 1 exit
`make check` green at the existing baselines plus the new leg; zero warnings.

---

## 4. Phase 2 — the rules core (pure C#, parallel lane A)

No engine, no service. This phase is where most of the game is decided and most of
the tests live. Every number below is a JSON default, not a constant.

### 2a. Hero sheet
Base stats per class (from the TF class table: health, speed), and the four invested
attributes of R22. **Appendix C is the specification**: each attribute has a *base*
(class start + points invested, capped per class) and a *now* (base + gear + buffs),
and every derived number reads *now*. Derived stats: `MaxLife = classLife +
lifePerGrit·Grit + lifePerLevel·level`, `MaxMana = classMana + manaPerSmarts·Smarts +
manaPerLevel·level` (multipliers per class, C.2), armour, damage range,
damage multipliers, crit chance, resistances (fire / blast / bullet / shock), and
sustain — health and mana regeneration **over time** and **from damage dealt**, each the
sum of base, skills (A.5) and item affixes under a hard cap. `XpToNext(L) = round(100 · L^2.2)`. Level-up grants 5 attribute + 1 skill point.
- Gate: facts for each derivation, level-up, respec, and the cap; Appendix C's gates; for sustain, that every source adds, that each total stops exactly at its cap however many sources are stacked, and that health-from-damage counts damage *dealt* (after the target's resistances), so overkill and immune targets heal nothing.

### 2b. Items
`ItemBase` (TF weapon or cosmetic, class restriction, slot, level requirement, implicit
stats) + `Rarity` (D5) + rolled `Affix`es (prefix / suffix pools gated by item level)
→ `ItemInstance { baseId, ilvl, rarity, affixes[], unusualEffect?, seed, identified, durability, maxDurability, rerollCount }`.
The rolled half of an instance is fully reproducible from its seed — the service
re-rolls it to audit a server; the mutable half (`identified`, `durability`,
`rerollCount`, and any crafted affix with the seed that produced it) is a logged
history the service can replay.

**Unusual chance grows with power**, as the ask requires:
`power = Σ(affixRoll / affixMax · affixWeight) + ilvl/maxIlvl`, normalised 0..1;
`P(unusual) = rarityBase · power²`. Unusual effects are gameplay procs that reuse the
TF unusual particle systems as their visual (Burning Flames → attacks ignite; Green
Energy → mana on hit; Sunbeams → healing aura …).

**Cursed stats (R28).** Beside the prefix and suffix pools sits a **cursed pool** of
negative stats — `−max life`, `−move speed`, `+mana costs`, `−a resistance`, `wears
twice as fast`, and *Loud* (robots hear you from twice as far, straight into B.3's
hearing). Any Vintage-or-better item may roll cursed stats **alongside** its positive
ones, so a single item reads "+60 % damage, +2 to Ordnance, −25 % max life". A cursed
stat is not free misery: each one **buys budget**, raising the roll ceiling of the
item's positive affixes beyond what an uncursed item of that level can reach — the
best-in-slot numbers live on cursed items, and whether the price is worth it depends
on the build. Chance per item rises with depth. Cursed stats are hidden until
identified like everything else (R14), which is what makes identifying a gamble
rather than a formality; once known they show in red. Nothing is sticky — a cursed
item unequips like any other. Re-roll (2k) may target a cursed stat, at triple cost.
- Gate: facts that an item can carry positive and cursed stats together; that each cursed stat raises the positive ceiling by its stated budget and an uncursed item can never reach that ceiling; that net power (positives minus curses) stays inside the band for its rarity and level, so cursed is a trade and never strictly better or strictly worse; that the `ItemView` of an unidentified item leaks no cursed field (2i's byte assertion, extended); that `P` is monotonic in power and in rarity, that a max-roll item beats a min-roll one by the stated ratio over 100k seeded rolls, and that re-rolling a seed is byte-identical.

### 2c. Loot
Kill → `LootTable(monsterLevel, template, championAffixes, magicFind)` → nothing /
Australium / potion / item. Rolled **once per eligible player** (R4); Australium rolled
once and split.
- Gate: distribution facts with a fixed seed and tolerance; party split conserves the total.

### 2d. Inventory, stash, shop, trade
**Capacity is derived, never stored** (R13): `BackpackSlots(sheet)` and
`StashSlots(account)` are functions of skill ranks, so the service recomputes them and
a server cannot report a bigger bag than the character has earned. **Capacity can
shrink** — a respec, a deleted character, a fallen hardcore character — and shrinking
never destroys an item: slots beyond capacity become **withdraw-only** until the
overflow is cleared. Picking up, buying, trade-receiving and corpse-looting all refuse
when full; a corpse holds everything regardless of capacity (D6), so recovery can take
more than one trip.
Slot model (D4), equip rules (class, level, attribute requirements), stacking, vendor
buy / sell / buy-back pricing, stash moves, two-phase trade (offer → both lock → both
confirm → atomic swap). All pure functions over an immutable state so the service can
replay them.
- Gate: a fact per rule; facts that capacity at rank 0 and at max rank equal D4's numbers, that each rank adds exactly its step, that stash capacity is the max over living characters, and that every shrink path leaves overflow withdraw-only with nothing lost; property tests that no sequence of operations — including rank-up and respec mid-sequence — duplicates or loses an item (count-in = count-out; memory `count-before-and-after-a-mechanical-edit`).

### 2e. Skills and spells
`SkillDef { class, tab, tier, prerequisites, maxRank, manaCost(rank), cooldown(rank), effect }`
where `effect` composes a small set of primitives: projectile, hitscan, area burst,
aura, buff / debuff (TF conditions), summon, dash, heal, passive stat.

**Skills and loot are separate axes (R17).** An *action* carries its own damage —
`base(rank) × (1 + 0.04·heroLevel) × (1 + tabAttribute/50)` — and fires the same with
a shovel in hand as with a rocket launcher. An *upgrade* is a passive whose scope is
either **all weapons** or **one weapon class** — every item base carries a `family`
(rocket launcher, shotgun, melee, minigun, flamethrower, medigun …), and an upgrade
may name a family, so "rocket launchers reload faster" is legal and "the Black Box
reloads faster" is not. A class-scoped upgrade does nothing while another class is in
hand, and its tooltip says so. Gear still matters to a caster, through affixes (`+% skill
damage`, `+mana`, `+1 to a skill`), not through the weapon's damage roll. Because TF2
weapon names are **item bases** under R12, no skill may share a name with one — a
skill called *Black Box* beside an item called Black Box is two things with one name.
The loader enforces all of it: a `SkillDef` that references an **item base**, whose id
collides with an item base id, that names a `family` no item base has, or an *action*
that names a family at all, is a load error.

Examples — Soldier: *Patriot Missile*, the three *Orders*, *Stomp*; Medic: *Über* (mana-cast),
*Crusader's Volley*, *Overheal Aura*; Merasmus sells cross-class spell tomes
(fireball, lightning ball, pumpkin MIRV, blast jump) that use the same primitives.

**Tomes level, scrolls are spent (R27).** A tome is a consumable: the first copy read
teaches the spell at rank 1, each further copy raises it one rank, to 5. Every rank
has a rising **Smarts requirement read from Now** (Appendix C.3 — drop below it and
the spell goes inactive, it is not forgotten). Tomes drop as loot with depth-gated
ranks, and Merasmus stocks only the low ones, so spell ranks are a **second
progression that is found, not bought with skill points** — skill points stay for the
class tree, and a respec never touches what was read. Spells are *actions* under R17:
mana, own damage, no weapon. A **scroll** casts one spell once, at a fixed rank, with
no Smarts requirement and no mana — the way a Heavy throws one fireball. Scrolls stack
to 10 per slot; Town Portal and the Dossier (D16) are scrolls in this sense. The
service logs tomes consumed, so `spellRank ≤ tomes read` is auditable like C.3's
point identity.
The shared Mercenary tab holds the **utility upgrades** any class may buy — inventory
size and stash size (R13), and sustain such as health regeneration over time or from
damage dealt; the tab is laid out in Appendix A.5.
The first fully designed tree is the Soldier's, **Appendix A**. Designing it showed the
primitive list above is four short; 2e also builds: **charge / channel** (hold to
build, release to fire), **toggle with drain**, **triggered passive** (on landing, on
melee kill, on hitting an airborne target), and **threat** (a taunt needs robots to
keep a threat table — a requirement on 5c). It also needs a hero `BlastJumping` state
(stock `TF_COND_BLASTJUMPING` is the reference) that several skills key on.
- Gate: facts for prerequisite gating, rank scaling, mana spend and refusal, cooldown, that a Mercenary-tab skill is purchasable by every class, and R27 — first tome teaches, each copy adds a rank, the sixth is refused and not consumed, the Smarts gate per rank reads Now, an unmet gate deactivates without forgetting, a respec leaves spell ranks alone, a scroll casts at its fixed rank for no mana and is consumed exactly once even if the cast is interrupted; R17's load errors (one fact each: item-base reference, name collision, unknown family, family on an action — each mutation-verified red); that every action deals identical damage with every weapon equipped and with none; that a class-scoped upgrade applies to every item base of its family and to none outside it; plus, per Appendix A skill, one fact for its rank-1 and max-rank numbers and one for each prerequisite and synergy edge — generated from the JSON so a skill added without facts fails the build.

### 2f. Robots on paper
`RobotTemplate` ported from the MvM spawner's vocabulary (`tf_populator_spawners.h:159`:
class, weapon, scale, health, skill, `Attributes` such as AlwaysCrit / HoldFireUntilFullReload,
item attributes) plus Diablo champion affixes that stack on top: *Extra Fast*,
*Über at low health*, *Sentry Buster death*, *Crit aura*, *Fire enchanted*,
*Medic-shielded*, *Teleporting*, *Multishot*, *Giant*, and the resistance affixes below. `MonsterLevel(depth)`, stat
curves, XP value, pack composition per depth, party scaling (D11).

**Scale is a rolled attribute (R16).** A template carries a scale *range* and each
robot rolls inside it from the pack seed: ordinary robots 0.85–1.15 so no two in a
pack stand the same height, *Runt* 0.6–0.75, *Giant* 1.5–1.9 (stock MvM giants are
1.75), bosses fixed per D13. Scale then **drives stats through one curve** rather than
being cosmetic: health ∝ scale², melee reach and knockback resistance ∝ scale, move
speed ∝ 1/√scale, XP and loot quality ∝ health. A runt is fast, fragile and hard to
hit; a giant is slow, enormous and worth killing. Footstep and voice pitch follow
scale, as MvM does for giants. Hitboxes scale with the model, so a runt is honestly a
smaller target.

**Robots resist (R25).** Damage has four types, the same four the hero sheet resists:
**fire**, **blast**, **bullet** (all kinetic damage — bullets and melee) and **shock**;
each weapon family and each action declares one. A `RobotTemplate` carries a resist
per type from −50 % (a weakness — Pyro-bots burn badly, nothing likes EMP less than a
giant) to +75 %. **Immunity is for champions only**, through the affixes *Fireproof*,
*Blast-plated*, *Bulletproof* and *Shock-hardened*: one immunity per champion, never
two. The point is Diablo's: a hero who deals a single damage type must sometimes
reach for something else — a second weapon family, a tome (R27), a party member. The
guard rail is the spawn director: no pack is more than half immune to any one type,
and nothing on the critical path is immune to every type a level-appropriate solo
hero can deal. Resistances show as icons on the robot's health bar, and a hit on an
immune robot says **IMMUNE** rather than a silent zero.

**Temperament is rolled too (R20)**, from the same pack seed, by the point-buy of
Appendix B.11: ten AI skills bought from a budget that **grows with the robot's
rolled scale** — `budget = round(40 × scale)`. So size is felt twice: a giant is not
only tougher by the scale curve above, it is also the better-drilled fighter, and a
runt is quick, fragile and a bit dim. Depth and champion affixes never touch the
budget; two robots the same size always have the same points, and what they spent
them on is what makes them different. XP and loot quality follow health × budget, so
the smarter giant pays for itself.
- Gate: facts that depth N+1 packs strictly dominate depth N on the threat metric, and per-affix stat facts; that rolled scale always lies in its template's range and is seed-stable; that each scale-driven stat is monotonic in scale with the stated exponent; that threat-per-XP stays within a band across the scale range, so neither runts nor giants are the thing to farm; B.11's roll facts; and for R25 — every weapon family and action declares exactly one damage type (load error otherwise), resist is applied once and clamped to −50…+75 off champions, only a champion affix yields 100 %, no robot is immune to two types, no generated pack breaks the half-immune rule over 10k seeds, and the economy model shows each Appendix A.6 build can clear every depth solo.

### 2g. Death rules
What drops (D6), corpse ownership, Lost & Found fee, hardcore (D7).

**Downed before dead (R26).** In a party, lethal damage with at least one other member
alive in the same instance puts the hero **down**, not dead: 20 s on the floor, unable
to act, dropping nothing. A party member who holds *use* within 96 u for 3 s
(interrupted by damage) revives them at 30 % life with 2 s of grace. Each further down
on the same level shortens the window by 5 s, to a floor of 5. If the window lapses, or
the last standing member goes down, everyone down **dies and R5 applies exactly as
written** — corpse, town, the run back. Solo there is no downed state at all.
Hardcore is the same rule with the same stakes: down is a chance, lapse is permanent.
Robots are told (`HeroDowned`, Appendix B.3): a downed hero's threat drops to zero so
the fight moves on, except that high-*Cunning* robots may hold near the body and wait
for the rescuer — the revive is meant to be a decision, not a free action.
- Gate: a fact per rule.

### 2h. Durability (R14, D21)
Wear per use / per hit, the broken state and what it still grants, repair pricing
(single item and repair-all), Repair Kit, *Indestructible*. Broken is a **derived-stat**
concern: 2a's sheet reads a broken item as contributing nothing, so there is one code
path, not a special case per stat.
- Gate: facts for wear rates per weapon family, the 1 → 0 transition, that a broken item contributes exactly zero to every derived stat, that wear never goes below 0 or removes the item, repair price monotonic in ilvl / rarity / damage, and *Indestructible* never wears.

### 2i. Identify (R14, D22)
The affixes are rolled **at drop** — the seed is fixed then, so identifying reveals,
it never re-rolls, and no amount of reconnecting or re-identifying changes the result.
`ItemView(instance, viewer)` is the only projection that ever leaves the server: for an
unidentified instance it carries base, rarity and ilvl and **nothing else**, so a
client cannot read hidden affixes out of memory or off the wire.
- Gate: facts that identify is idempotent and seed-stable, that equip is refused, that the view of an unidentified item serialises without any affix or unusual field (asserted on the serialised bytes, not on the object), and the fee curve.

### 2j. Gambling (R14, D23)
Crate price by base type and level, the odds table, the roll. Seeded like any drop,
so the service audits it the same way. The odds table is data and is shown in the UI.
- Gate: distribution facts over 100k seeded opens against the published table; expected Australium cost per Unusual from gambling is strictly above the expected cost from playing (so the crate is a sink, never the best farm) — computed by the 2c/2f economy model, not asserted by hand.

### 2k. Crafting (R14, D24)
Salvage yields, metal conversion both ways, each recipe as a pure function
`(inputs, seed) → outputs` with inputs consumed atomically. Re-roll keeps the other
affixes byte-identical and raises `rerollCount`; its cost curve is what stops it
being a slot machine for perfect items.
- Gate: a fact per recipe incl. refusal on wrong or insufficient inputs; metal conversion round-trips losslessly; re-roll leaves locked affixes untouched; no recipe can output Unusual or Australium rarity; salvage → craft → salvage loops strictly lose value (no perpetual-motion economy); the 2d count-in = count-out property extended over craft and salvage operations.

### 2l. Dungeon furniture (R24)
All placed from the level seed at markers the rooms declare (6a); what stands on a
marker is decided at load, so the same room is never furnished the same way twice.

- **Shrines** — Merasmus's altars. One use, a cryptic one-line message, and an effect from a seeded table: mostly good (a level's worth of +damage, a full refill, a revealed automap, +1 rank to a random spell for the level), sometimes a trade (+Brawn −Smarts until you leave), occasionally plain bad. The name hints; it never tells. Party-wide shrines say so.
- **Fountains** — a working Mann Co. **Dispenser**: heals and gives ammo, from a finite charge that does not refill.
- **Containers** — supply crates, lockers, sarcophagi in the catacombs. Loot rolls through 2c with a container table, **per player** like any drop (R4): everyone in the room when it opens gets their own roll. Some are locked (a key drops on the level), some are **trapped**.
- **Pumpkin bombs and barrels** — shootable, area-burst **blast** damage to heroes and robots alike, chain reactions allowed. Robots know where they are (B.3): a *Cunning* robot shoots the one you are standing beside, a low-*Discipline* one fights next to its own.
- **Traps** — on containers and doorways: dart (bullet), flame jet (fire), charge (blast), tesla coil (shock). Every trap has a visible or audible tell a careful player can learn; they hurt robots too, so they can be used.
- Gate: a fact per shrine effect including expiry on leaving the level; furnishing is seed-stable and respects per-room marker limits; container loot is rolled once per eligible player and never twice for the same player; a Dispenser's charge only falls; barrel damage and chain radius; each trap's type and tell; bad-shrine frequency stays under its cap over 100k rolls.

### 2m. Automap (R23)
A pure model: `Automap { roomsSeen, doorsSeen, markers }` over the layout generator's
room graph (6b). A room is revealed when a party member **enters** it and its doors
are shown as leading onward; the party shares one map. Markers: arrival, stairs (once
seen), portal, party members, your corpse, used / unused shrines, locked containers.
Two storeys draw as two layers with the hero's layer on top. **The server reveals, the
client only draws**: a room's outline is sent when it is revealed and not before, so
the map cannot be read out of client memory ahead of the player. A shrine can reveal a
whole level.
- Gate: facts that an unentered room is absent from the serialised state (asserted on the bytes), entering reveals exactly that room and its door stubs, party reveals merge and are idempotent, markers appear only once seen, the reveal-all effect reveals all, and the map of a level is a pure function of the layout and the visit order.

### Phase 2 exit
`Descent.Tests` green, zero warnings, no `SourceSharp.*` engine reference in `Descent.Rules.csproj` (asserted by a test that reads the csproj from **its own** worktree — memory `a-test-can-read-the-wrong-checkout`).

---

## 5. Phase 3 — the service, persistence half (parallel lane B)

### 3a. Storage
SQLite (WAL) behind `ICharacterStore`, `IStashStore`, `IInstanceStore`. Tables:
`accounts(steamid)`, `characters(id, account, class, name, hardcore, fallen, level, xp, attrs, skills, reached_depth, difficulty, sheet_json, version)`,
`items(id, owner_kind, owner_id, slot, instance_json)`, `stash`, `lost_and_found`,
`parties`, `instances`, `maps`, `audit_log`.

### 3b. The character lease
Exactly one server may hold a character at a time. `POST /characters/{id}/lease`
returns a **fencing token**; every write carries it; a stale token is refused. Leases
expire on missed heartbeats. This is what makes server-to-server travel and crashes
safe, and it is the anti-duplication spine of the whole game. The gateway's single-presence
rule (R21, 7e) stops the double-presence situation arising at the network; the lease is what
keeps the data right even if it somehow does. Neither replaces the other.
- Gate: facts for double-lease refusal, stale-token refusal after re-lease, expiry, and crash recovery (checkpoint survives, un-checkpointed loot does not duplicate).

### 3c. API (server-secret only)
`/characters` (list, create, delete), `/lease`, `/checkpoint` (sheet + inventory,
versioned, idempotent), `/stash/move`, `/trade/commit` (atomic, both leases held by the
same hub), `/lostandfound`, `/party`. Everything a server reports is replayed through
`Descent.Rules` and rejected if impossible. `/craft`, `/gamble/open`, `/identify` and
`/repair` are service-committed transactions like a trade: Australium, materials and
the resulting item change in one write, keyed by an idempotency token so a retry after
a timeout cannot pay twice or mint twice.
- Gate: `WebApplicationFactory` facts per endpoint incl. auth refusal; an audit fact that a forged item (seed does not reproduce) is rejected.

### Phase 3 exit
Service test project green; `dotnet run` serves a character end to end against a temp DB.

---

## 6. Phase 4 — the hero in the engine (lane C, after Phase 1)

Vertical slice class: **Soldier** (the managed tree already has projectiles).

### 4a. Class on a managed player
TF class model, hull, speed, health; first-person arms (`c_models`); the TF
multiplayer anim state re-implemented from `tf_playeranimstate.cpp` as far as the
slice needs (run, jump, primary fire, reload).
### 4b. Three weapons
Rocket launcher, shotgun, shovel — behaviour from the TF C++, damage routed through
the existing managed `DamageRules` with the hero sheet's multipliers and the target's
resistances applied.
### 4c. Health, mana, skills
Mana as a networked player field; skill activation (bound keys + belt), cooldowns,
the effect primitives of 2e bound to engine actions; potions. The slice ships six
Appendix A skills chosen to touch every kind of primitive once: *Patriot Missile*
(projectile), *Marksmanship* (passive stat), *Jump Training* + *Death From Above*
(triggered passive on `BlastJumping`), *Battle Orders* (aura), *EMP Charge* (charge).
### 4d. Sheet application
Equip changes recompute derived stats server-side; owner-only state (inventory, sheet)
goes by user message, not entity fields, and items cross the wire only as 2i's
`ItemView`. Durability wear is applied server-side on fire / on hit and sent as deltas.
- Gate (phase): live, headless via `devapi`: create Soldier → `descent_give` a rolled launcher → damage on a target dummy equals `Descent.Rules`' prediction within rounding; level-up grants points; casting with no mana is refused. **Two map loads** in the same process (memory `one-map-load-hides-a-whole-bug-class`).

---

## 7. Phase 5 — robots (lane D, after 0b)

### 5a. `descent_robot` entity
Model, animation, locomotion with step / gravity / collision, hitbox damage, gibs and
death, per 0b's rulings — each missing capability ported into `SourceSharp.*` managed
first, a generic ABI slot only where 0b proved the engine itself is the obstacle.
Model scale is already reachable from managed (`SetModelScale`,
`EngineBinding.cs:485`); what 0b must still prove is that the **collision hull and
hitboxes follow it**, because a giant with a man-sized hull walks through doors it
should not fit and a runt with a man-sized hitbox is a lie.
### 5b. Managed navigation (D20)
`NavGraph`: convex walkable cells + portals, A* with funnel smoothing, dynamic
obstacle avoidance between robots. Pure C#, tested in `Descent.Tests` on synthetic rooms.
**Clearance (R16):** every cell and portal carries clear width and height, computed
by 6a's room tooling; A* takes the robot's scaled hull as a parameter and ignores
what it cannot fit through. Avoidance radius scales too.
### 5c. Behaviour
The AI is its own system, off the main thread, designed in **Appendix B** (R18). 5c
builds it in the order B.9 gives. The behaviours themselves are still ported by
reading `bot/behavior/`: idle / patrol, investigate, chase, attack per weapon family
(melee, hitscan, projectile lead, flame, sticky trap), retreat, medic-follow-and-heal,
sniper hold, spy cloak-and-stab, Sentry Buster run — but as decisions over the
brain's memory that emit intents, never as code that touches an entity.
### 5d. Spawn director
Rooms sleep until a hero is within two rooms; packs come from 2f keyed by depth, seed
and party size. Cap ≈ 60 live robots (to be re-measured against the edict limit).
A robot is only spawned at a marker whose clearance fits its rolled scale **and** from
which, at that scale, it can reach the room's sockets — a giant sealed in a closet is
a bug, a giant that can never leave its arena is a design choice and must be tagged so.
### 5e. Kill → reward
XP (D10), loot (2c). Item drops are **instanced**: one pickup entity per eligible
player, visible to that player only — via the managed `TransmitRecipients` field of §0.3.
Australium is one shared pickup, split on collection.
- Gate (phase): Appendix B's gates (B.10), then the headless arena test — 20 robots of 6 templates path to and kill an idle hero; a scripted hero kills them and the XP + drops match a seeded `Descent.Rules` replay exactly; a second client **cannot see** the first client's drop (asserted from the second client's entity list, not from the server's intent).

---

## 8. Phase 6 — MapForge: levels from rooms (lane E, after 0d)

### 6a. Room format
A room is a VMF on a fixed grid (cell 1024³, rooms span 1×1 to 3×3 cells, up to 2
storeys; sized for blast jumping — R19, below) with **door sockets** on cell faces, plus metadata: tileset, tags
(`start`, `stairs`, `boss_arena`, `treasure`, `corridor`, `junction`, `shrine`),
weight, min / max depth, spawn markers (`descent_spawn_pack`, and the furniture markers of 2l:
`descent_mark_shrine`, `_container`, `_dispenser`, `_barrel`, `_trap`), a 2D **outline per storey** for the automap (2m, derived from the nav fragment, never drawn by hand),
and a **nav fragment** whose cells and portals carry clear width and height (R16: a giant must know which doors it fits). Rooms are sealed boxes so any legal assembly is leak-free by
construction. v1 target: 25 rooms per tileset.
**Rocket-jump room (R19).** At least **60 % of each tileset's combat rooms** —
combat meaning everything except `corridor` and `junction` — must contain a **jump
volume**; the other 40 % may be low and tight, and rooms declare which they are
(`jump` / `low`) so the layout generator can use it. A jump room has open air at least
`JumpClear` high over at least half its floor, with an unobstructed horizontal run of
at least `JumpRun`. Both constants are **measured, not guessed**: Phase 4 records the
apex and carry of a standard crouched rocket jump and of *Rocket Leap* from the
managed Soldier, and `JumpClear` = apex + scaled hull height + margin. Until then
rooms are authored to a placeholder of 512 u clear (half a cell; a two-storey room
gives 2,048) and 768 u run. Low rooms are where the ground game wins — cover-loving
and ambushing robots (B.11) are at their best there — so the mix is a pacing tool:
the generator (6b) never places more than two `low` combat rooms in a row on the
critical path, and boss arenas are always `jump` rooms. Robots
read the same volume: it is where giants (R16) fit and where *Stomp* lands. The
designer's trap to avoid is decoration — beams, chandeliers, pipes — quietly eating
the volume, which is why lint measures clear air from the brushes and props rather
than trusting the room's declared tag, and fails a tileset under the 60 % line.
- Gate: a room linter (`mapforge lint`): sockets on grid, sealed, nav fragment covers every spawn marker and connects every socket; R19's jump volume (below). One fact per lint rule.
- **Linker guarantees (`plan_maptools.md` §10b).** Compiling a room once and linking levels is sound only if every room keeps these, so they are lint rules with a refusing fixture each: every brush lies inside the room's own cells and nothing crosses a cell face except the fixed socket kit; the shell is sealed except at sockets; sockets come from the kit at kit positions; placement is whole-cell translation and 90° rotation only. "Rooms are sealed boxes" above stops being a convenience and becomes the contract the linker's shortcuts rest on.

### 6b. Layout generator (in `Descent.Rules`)
Deterministic from `(roomLibVersion, tileset, depth, seed)`: build the critical path
start → stairs (length grows with depth), add branches and loops, place a treasure
room off-path, force the `boss_arena` before the stairs on 5 / 10 / 15, cap unused
sockets with wall plugs.
- Gate: property tests over 10k seeds — never more than two `low` combat rooms in a row on the critical path, boss arena always `jump` (R19); connected, no overlapping cells, stairs reachable, every socket matched or capped, same input → identical output.

### 6c. VMF merge
Merge room solids and entities directly in C# (extend `Vmf.cs`: translate, remap ids
and targetnames per room). **Not** `func_instance` — the Wine-run compilers are Orange
Box-era and instance support there is unproven. (With the managed compilers of
`plan_maptools.md` that caution no longer binds — its VMF reader handles `func_instance` —
but direct merge stays: it is simpler, and the merged VMF is the **reference input** the
level linker is judged against.)
- Gate: byte-identical VMF for the same input; entity and solid counts equal the sum of the parts (counted, not eyeballed).

### 6d. Compile and package
Behind `IMapCompiler` (first implementation `WineMapCompiler`): `vbsp` → `vvis -fast`
→ `vrad -fast` via the existing `make map` machinery; fail on
`**** leaked ****` or a `.lin` file. Stitch the nav fragments — and the per-room cell-to-cell **visibility tables** the AI's vision pass prefilters with (Appendix B.3), joined through door portals — into `dl_<hash>.dnav`
(server-only sidecar). bz2 the BSP for fastdl. Content-address everything by
`hash(roomLibVersion, tileset, depth, seed)`.
`IMapCompiler` gets two further implementations from `plan_maptools.md`, in the order they
land: `ManagedMapCompiler` (its Phase 7 — the same three stages in one managed process, no
Wine) and `LinkedMapCompiler` (its Phase 9 — `ssmap room` once per library room, `ssmap link`
per level; cross-room visibility composed over the door graph, which is already tighter than
the `vvis -fast` specified here). The room objects live in the room library's cache database
and are keyed by room content, so `roomLibVersion` in the hash above keeps meaning what it
means. The leak check stays as a gate on the monolithic path and becomes a lint fact on the
linked one, where a legal assembly cannot leak.
- Gate: a generated level loads, a hero walks start → stairs by nav path under `devapi`, and a robot paths across three room boundaries.

### 6e. Native compilers as shared objects (R3b) — SUPERSEDED by `plan_maptools.md`
> **Superseded 2026-09-20.** This section existed because the C++ tools are global state that
> ends in `exit()`, which forced a `.so` per tool, one compile per worker process, and a new
> native `libvbsp.so` entry point. The managed compilers have no statics and fail with
> exceptions, so none of that is needed and no C++ is built or written: see
> `plan_maptools.md` (§1 design rules, §7 for the one native dependency it keeps —
> Valve's `vphysics.so`, which also means MapForge should still run compiles **outside the
> ASP.NET process** until that plan's spike 0b has shown the library is safe to host there).
> The text below is kept as the record of what was decided and why; it is not to be executed.

6d ships on Wine so nothing waits for this; 6e then replaces it behind the same
`IMapCompiler` interface (`WineMapCompiler` → `NativeMapCompiler`).

- **Build.** Lift the `[$WINDOWS]` guard in `projects.vgc:118,133,153` for `vbsp`,
  `vvis_dll`, `vrad_dll` into the container build; fix what does not port (expect
  threading, `vmpi`, and filesystem init — cut `vmpi` out rather than port it). This
  is existing C++ being compiled, not new C++ being written; the new native code is
  one thing: a `libvbsp.so` target giving `vbsp` the same `ILaunchableDLL` entry
  `vvis` and `vrad` already have.
- **Load.** MapForge binds the three `.so`s through `CreateInterface` →
  `ILaunchableDLL::main(argc, argv)`.
- **Isolation — the constraint that shapes this.** These tools are built on global
  state and end in `Error()` → `exit()`. They are **not** re-entrant and must never be
  loaded into the ASP.NET process: one bad room would take the service down, and a
  second compile in the same process reads the first one's globals. The shared objects
  are loaded by a small `mapforge-worker` process, **one compile per process**, with
  the pool manager running N workers. "Shared object" buys no-Wine, no-Windows-depot
  and a callable API; it does not buy in-process compilation.
- Gate: for 20 seeds, native and Wine outputs load and play identically (entity lump
  equal; brush, face and leaf counts equal; lightmaps within tolerance — BSP bytes are
  not expected to match); a deliberately leaking VMF fails the worker and leaves the
  service up; 0d's timing table re-taken native vs Wine on the same box under matched
  load (memory `matched-power-to-see-throttling`).

---

## 9. Phase 7 — the service, orchestration half, and travel (lane B continues)

### 7a. Orchestrator
`IInstanceHost` with a local-process implementation: spawn a dedicated server
(`+map`, `+descent_instance <id>`, secret in env), own `TMPDIR` and devapi socket dir
per instance, **read the bound port back from `/proc`**, health by heartbeat, reap by
PID only (never a `pkill` pattern). Hubs are instances too: a new hub starts when all
are ≥ 80 % full.
- Gate: facts against a fake process host; one live run of 1 hub + 3 level servers concurrently, each joinable.

### 7b. Instance lifecycle
`requested → map_ready → booting → live → draining → reaped`. A level instance
belongs to one party (a solo player is a party of one) — **this is what makes other
players' levels their own**. It drains when empty; if it holds a corpse it waits D6's
grace first; on reap, corpse contents go to `lost_and_found` (R5).

### 7c. The pre-generated pool (R3a)
MapForge runs as a background worker keeping **K ready maps per depth** (K from 0d's
timings and observed demand; start at 3), oldest first, each handed out **once** then
retired so no player meets the same layout twice. Two more warmers: when a party
enters depth N the pool for N+1 is topped up first, and when a party stands in the
church the depths it can select are topped up. On-demand compile exists only as the
fallback for an empty pool, with a visible "the church doors are stuck" wait. Retired
maps are deleted once no live instance references them.
- Gate: facts for hand-out-once, refill ordering, fallback, and GC; a live measure that stairs-to-spawn time with a warm pool is dominated by download + load, not compile.

### 7e. The gateway (D25) — read this before 7d; it changes how 7d hops
`Descent.Gateway`, a separate C# process (so a service restart does not drop every
player). It is a **relay, not a splice**: payload-agnostic, it never parses the
netchannel. Per client source `ip:port` it holds a session with its own backend-facing
socket, so each backend sees a stable peer and its challenge / sequence state stays valid.

**What it cannot do** is move a connected client between servers invisibly. A
netchannel is bound to one server's challenge, sequence numbers, string tables,
baselines and loaded map; level B is a different BSP. A hop is always a disconnect, a
new handshake and a map load. What the gateway buys is that the hop is
**`retry` to the same address** instead of `redirect` to a new one:

1. the service tells the gateway "session S → instance B" (route flips for *new* handshakes only; the live flow to A is untouched);
2. server A checkpoints, releases the lease, and issues `retry` to the client — server-executable in the engine (`FCVAR_SERVER_CAN_EXECUTE`);
3. the client's fresh `getchallenge` arrives at the gateway from the same source address and is forwarded to B.

Routing is keyed on the client's source address because the first packets of a
handshake are connectionless and carry no identity. The hub, which learns the SteamID,
reports "backend port P is SteamID X" so the service can address sessions by player. A
NAT rebind mid-hop lands the client on the default route — the hub — which re-routes
it; that is the recovery path, not an error.

**Engine limits that bite a relay**: every client
appears to come from the gateway's IP, so `sv_max_connects_sec` (2/s per IP, a convar —
raise it on instances) and `MAX_REUSE_PER_IP 5` (**hard-coded**: more than five
half-connected clients from one IP are refused) both apply. Single box: bind each
session's backend socket to its own `127.x.y.z`, so every client has a distinct IP.
Multi-host: a private subnet does the same, or the gateway admits at most five
handshakes per backend at a time. Bans and logs must use the real address, which only
the gateway knows — it reports it to the service.

**Single presence (R21).** Because instances are reachable only through the gateway,
it is the one place that can *guarantee* a player is in exactly one backend, rather
than hope two servers agree:

- **One session, one route.** A session forwards to exactly one backend. A route flip is a **hard cut**: the moment the first packet of the new handshake is forwarded to B, the flow to A is closed and A is told the player has gone — there is no window in which packets reach both.
- **One player, one session.** The gateway relays opaque packets and cannot read a SteamID, so identity arrives the way it already does: the backend reports "gateway port P is SteamID X". If X already has a **live** session (traffic within the last 10 s) the newcomer is refused and its session dropped — oldest wins, because under R11 a SteamID can be claimed by anyone and newest-wins would let a spoofer kick the real player. If the old session is **silent** (crashed client, NAT rebind, pulled cable) the newcomer takes over and the stale session is closed. Until a session's identity is confirmed it can reach only the hub, never a level instance.
- **The service holds the registry, the gateway enforces it.** `player → session → backend` lives in the service, so the rule survives a gateway restart and still holds if §17.1 ever puts a gateway on each host.
- **It feeds the lease.** When a session dies the gateway says so at once, and the service releases that character's lease (3b) without waiting out a heartbeat timeout — a crashed player gets back in sooner, not just more safely.

What else it gives: one public UDP port to open and defend; instances need no public
port at all (the `/proc` port read of 7a becomes internal bookkeeping); server
addresses never reach a client; A2S queries are answered once, by the gateway. What it
costs: every packet takes an extra hop (loopback on one box, a real hop across hosts),
and it is a single point of failure — so it holds no game state and a restart costs
players a `retry`, nothing more.
- Gate: facts against a fake backend for session create / expire, route flip affecting only new handshakes, real-address reporting, the five-at-a-time admission, and R21 — a flip closes the old flow before the new one carries a second packet (no packet is ever delivered to both backends, asserted on the fake backends' receive logs), a second live session for one SteamID is refused, a silent one is taken over, an unidentified session cannot be routed to a level instance, session death releases the lease, and the registry survives a gateway restart; live — 7d's gate run with every client connected only ever to the gateway's address, and eight clients joining a hub simultaneously with none refused; and a second client presenting an already-connected SteamID never appears in any backend's player list.

### 7d. Travel
Church stairs (hub) → party leader picks a reached depth (R6, D8) → service assigns
instance → servers checkpoint and release leases → clients hop (7e's `retry` through the gateway;
0f's `redirect` if D25 was struck) → new
server takes leases. Same path for stairs-down, Town Portal and death. fastdl serves
`dl_<hash>.bsp.bz2` read-only.
- Gate: live, two clients in a party descend 1 → 2 → town → back, inventories intact and the item count in the DB unchanged across every hop; a third solo client asking for depth 1 receives a **different** map hash.

---

## 10. Phase 8 — client UI (lane F, after 0e)

View-models in `Descent.Client` are pure and unit-tested; the draw layer is thin.
Panels: HUD (health and mana orbs, belt, XP bar, party frames, boss bar), inventory +
paper doll (unearned slots drawn locked, overflow slots drawn withdraw-only), character sheet with attribute spend, skill tree, vendor, stash, trade,
party, church depth picker, repair (single / all), identify, crate opening with the
odds table on screen, crafting bench (recipe list, inputs, preview of what is locked
vs re-rolled), item tooltips with rarity colours, durability, and compare; a HUD
warning when equipped gear is near broken. The **automap** (R23) as a full overlay and a
corner minimap, drawn only from what the server has revealed; the **spellbook** with
tome ranks and scroll counts (R27); resistance icons and **IMMUNE** on robot health
bars (R25); cursed stats in red (R28); the **downed** state — bleed-out timer, revive
progress, and party frames that show who is down and where (R26); the amulet slot on
the paper doll (R29).
- Gate: view-model facts; a live screenshot gate per panel through `devapi`.

---

## 11. Phase 9 — the town

`descent_town.vmf` (hand-authored, the one map that is not generated): square, four
shops, workbench, crate stall, the Spy's corner, stash locker, church with the stairs, memorial wall, Lost & Found. Vendors are
`[ScriptedEntity]` NPCs with stock rolled per hub per hour from 2b at the buyer's
level; healer restores and sells potions; Merasmus sells spell tomes and respecs; the
Engineer repairs and runs the crafting bench; the Spy identifies; the Scout sells
crates (D15).
Trading (2d) commits through `/trade/commit`. Character select / create (class, name,
hardcore flag) happens on joining a hub.
- Gate: live — buy, sell, buy-back, stash in, reconnect, stash out, trade between two clients; wear a weapon to broken in a level, confirm it fires as Stock, repair it; identify a drop and confirm the revealed affixes equal the seed's replay; open a crate; salvage, convert metal, craft, re-roll. DB item and Australium totals reconcile against the audit log after every step.

---

## 12. Phase 10 — death and the corpse run

On death: everything (D6) moves to a `descent_corpse` entity at the death position,
recorded to the service **immediately** (so a server crash cannot eat it), hero
respawns in town with nothing. The church offers "return to your corpse (depth N)"
while that instance lives. Owner-only loot; one use-key takes all that fits. Instance
reaped → Lost & Found at D6's fee. Hardcore → D7, and the corpse is lootable by nobody.
- Gate: live — in a two-client party, down one hero and revive them: nothing dropped, 30 % life, no corpse, no service death record; down them again and let it lapse: R5 runs as below; with both down, both die at once; solo, death is immediate with no downed state. Then — die, verify empty in town, return, recover, counts conserved; kill the level server by PID instead, verify Lost & Found holds exactly the corpse's contents; hardcore death marks `fallen` and refuses the lease.

---

## 13. Phase 11 — fifteen levels of content

Level table (JSON): depth → tileset, path length, pack budget, monster level, template
pool, champion chance, loot ilvl. Three tilesets × 25 rooms. Boss arenas and the three
bosses (D13) as scripted multi-phase encounters with their own loot tables; killing
the L15 boss completes the descent and unlocks D12's next tier in data.

**11a. Furniture in the world (R24).** The `[ScriptedEntity]`s for shrines, Dispensers,
containers, barrels and traps, driven by 2l, placed at 6a's markers; per-tileset
dressing; each emits the sound and hazard events B.3 expects.

**11b. Resistances in the level table (R25).** Template resist rows and per-depth
champion-affix weights, tuned so depth 1–5 teaches weaknesses, 6–10 introduces
resistance, and immunity first appears at 11.
- Gate: live — every furniture kind is used once through `devapi` and matches its 2l replay; a robot is killed by a barrel and by a trap; an immune champion shows IMMUNE to its type and dies to another; the automap after a full clear equals the layout. Then a bot-driven soak descends 1 → 15 with cheats, every level loads, every boss can be killed and drops; the 2f dominance fact holds over the shipped table.

## 14. Phase 12 — the other eight classes (content waves, D3)

Per class: model + anims, stock weapons, three skill tabs, class-restricted item
bases. One class per lane, by file ownership; the ABI tail is serialised (memory
`always-use-subagents`).
- Gate per class: the Phase 4 gate, re-run for that class.

## 15. Phase 13 — balance, soak, performance

Perf comes last by standing rule (memory `perf-pass-comes-after-the-port`): record
costs with numbers as they are met, fix them here. 4-player, 60-robot soak; service
soak of 20 concurrent instances; economy simulation in `Descent.Rules` (seeded runs
of 1 → 15) to tune drop rates and prices before humans do.

---

## 16. Lanes and standing rules

Lanes partition by project so they do not collide: **A** Rules, **B** Service,
**C** Hero, **D** Robots, **E** MapForge, **F** UI. A and B need no engine and start
immediately after Phase 1; C–F start when their Phase 0 measurement is in. First
playable = A + B(3) + C + D + E + 7d: one class, one tileset, real loot, persisted.

- All work in a dedicated `git worktree`, reset to the branch tip as step 0; stage explicit paths only; one commit per sub-step, each with its test.
- Managed tests are `[Fact]` methods, one behaviour each; every bug fix lands with a regression test proven red first; zero warnings before a phase is called done.
- **R15, enforced by tests, not by intent.** Facts in `Descent.Tests`, each reading the tree of **its own** worktree: no `DllImport` / `LibraryImport` / `extern` / `AllowUnsafeBlocks` in `Descent.Server`, `Descent.Client`, `Descent.Ai` or `Descent.Rules`; no file outside `src/sourcesharp/managed/` and `game/mod_descent/` contains the string `descent` in any case (the native shell stays game-agnostic); `Descent.*` reference the engine only through `SourceSharp.Abi`'s public managed surface. Each fact mutation-verified red by planting the thing it forbids.
- The whole expected native diff for the game is therefore: the data read for `TransmitRecipients`, any generic slot 0b proves unavoidable, and nothing in the tools: 6e's `libvbsp.so` entry point and Linux build fixes are superseded by the managed compilers of `plan_maptools.md`, which write no C++.
- Every number quoted into this file is backed by a command run in that session, and a second independent one.
- TF2 assets are mounted from the user's TF2 install, never copied into the repo.

## 17. Still open — would change what gets built, not yet blocking

1. **Hosting beyond one box** — R10 is single-host. `IInstanceHost` is the seam; nothing else should assume locality. With D25, multi-host also means deciding between one gateway in front of every host (extra network hop for all traffic) or a gateway per host with the client sent to the right one.
2. **R11's spoofing risk** once hubs are public — ticket validation is a contained change at the hub's join path plus one service check.
3. **Steam app id** — 243750 (SDK Base 2013 MP) carries us through development; shipping is a separate question.
4. **Difficulty tiers and endgame** after the L15 kill (D12).
5. **Art** — 75 rooms and a town are the largest non-code cost in the plan; no new models are assumed anywhere.
6. **What Diablo 1 has that this plan does not** — Appendix D lists it, ranked. Seven of its rows are now rulings R23–R29; quests and story (#2), elixirs (#7), named uniques (#10) and the smaller items remain undecided.

---

## Appendix A — the Soldier's skill tree

The template the other eight classes follow (§14). All numbers are JSON defaults for
2e, written as rank 1 → max rank.

### A.1 Rules of the tree

- **R17 governs everything here.** Each skill is either an **action** (costs mana, brings its own damage, works with anything or nothing equipped) or an **upgrade** (a passive scoped to all weapons, or to one weapon **class** — marked ◆ below). The Soldier *is* rockets, so his actions conjure ordnance from mana — his own stockpile, not the launcher he happens to be holding. No skill below is named after a TF2 weapon, because those names belong to items (R12).
- Action damage is `base(rank) × (1 + 0.04·heroLevel) × (1 + tabAttribute/50)`; the table gives `base`. Tab attributes: Ordnance → **Aim** (shock actions → **Smarts**), Airborne → **Brawn**, Command → **Smarts** for potency, with **Grit** the health the Airborne tab spends.
- **30 points to spend** (D2) against **138 ranks** on offer in the class tabs plus 47 in the Mercenary tab (R13, A.5). A finished Soldier has maxed perhaps five skills and dipped into a few more; every rank of *Deep Pockets* is a rank of something here not taken.
- Three tabs of ten skills: tiers 1–4 hold two skills each (one action, one upgrade, as a rule), tier 5 one, tier 6 a one-rank capstone. Tiers unlock at hero level 1 / 6 / 12 / 18 / 24 / 30. Max rank 5 except capstones.
- A skill needs its tier's level **and** its listed prerequisites at rank ≥ 1.
- **Synergies**, Diablo II style: some skills gain from ranks in another, so early points are not orphaned.
- Items can roll **+1 to a skill / tab / all Soldier skills**, `+% skill damage` and `−% mana cost`; bonus ranks may exceed max rank by up to 3 but never unlock a skill you have no real point in.
- Stock rocket jumping with a found launcher stays **inherent** — it is physics, it costs health not mana, and no skill gates it. The tree adds a mana-cast jump so an Airborne build does not depend on owning a launcher.

### A.2 Ordnance — explosives on demand

| tier | skill | kind | what it does (rank 1 → 5) | mana / cd | needs | behaviour reference |
|---|---|---|---|---|---|---|
| 1 | **Patriot Missile** | action, projectile | shoulder-fire a rocket: base 20 → 60, 110 u blast | 4 / — | — | `tf_projectile_rocket` |
| 1 | **Marksmanship** | upgrade | all weapons: +3 %/rank damage, −4 %/rank spread, +3 %/rank projectile speed | — | — | — |
| 2 | **Salvo** | action, channel | hold to ready 3 → 5 Patriot Missiles, release to fire them in 0.6 s with 3° spread; over-hold misfires on you | 3 per rocket / 6 s | Patriot Missile | Beggar's Bazooka load / misfire logic |
| 2 | **Field Dressing** | upgrade, triggered | any hit with any weapon or action heals 1 → 3 HP per enemy struck (max 3 per hit, 10 HP/s) | — | — | heal-on-hit attribute |
| 3 | **EMP Charge** | action, charge | 2 s charge: base 45 → 110 as **shock**; robots EMP-stunned 1 → 2 s (bosses: slowed) | 15 / 8 s | Patriot Missile | Cow Mangler charged shot |
| 3 | **Rocket Specialist** ◆ | upgrade, *rocket launchers* | +5 %/rank reload speed, +3 %/rank blast radius and rocket speed; +1 clip at ranks 2 and 4; at rank 5 a direct hit stuns 0.5 s | — | Marksmanship | MvM upgrade `rocket_specialist` |
| 4 | **Rain of Freedom** | action, projectile burst | only while `BlastJumping`: 6 → 10 mini-rockets, base 12 → 30 each, over 1.5 s | 18 / 12 s | Salvo | Air Strike airborne fire |
| 4 | **Killer Instinct** | upgrade | all weapons and actions: +2 %/rank crit chance, +5 %/rank crit damage | — | Marksmanship | crit path |
| 5 | **Fire Mission** | action, area burst | mark ground within sight; after 3 s, 5 shells land across 400 u, base 60 → 140 each | 30 / 25 s | Rain of Freedom | — (new) |
| 6 | **Screamin' Eagle** | capstone | 8 s: everything you fire or cast crits; weapons use no ammo and need no reload; actions cost half mana | 50 / 90 s | Fire Mission, Killer Instinct | Kritzkrieg condition |

Synergies: *Patriot Missile* +4 % damage per rank of *Marksmanship*; *Rain of Freedom* +1 rocket per 2 ranks of *Jump Training* (Airborne); *Fire Mission* +5 % damage per rank of *Salvo*.

### A.3 Airborne — leave the ground, come back down on someone

| tier | skill | kind | what it does (rank 1 → 5) | mana / cd | needs | behaviour reference |
|---|---|---|---|---|---|---|
| 1 | **Jump Training** | upgrade | −15 %/rank self-inflicted blast damage from any source (−75 % at 5); +3 %/rank jump force | — | — | Gunboats attribute |
| 1 | **Boot Camp** | upgrade | +2 %/rank move speed; all weapons deploy and holster +6 %/rank faster | — | — | deploy-time attribute |
| 2 | **Stomp** | upgrade, triggered | landing from a blast jump: no fall damage, and an area burst 120 → 240 u, base 30 → 90, with knock-up | — | Jump Training | Mantreads stomp |
| 2 | **Double Time** | action, buff | you and allies within 300 u: +20 → 40 % move speed for 4 s | 6 / 8 s | Boot Camp | speed-boost condition |
| 3 | **Death From Above** | upgrade, triggered | everything you deal while `BlastJumping` — any weapon, any action — +20 → 60 %; a kill while airborne refunds 10 mana. ◆ *melee*: a melee hit while `BlastJumping` always crits | — | Jump Training | Market Gardener airborne check |
| 3 | **Pain Tolerance** | upgrade | per 10 % health missing: +2 → 6 % damage with all weapons and actions, +1 → 3 % move speed | — | Boot Camp | Equalizer / Escape Plan curves |
| 4 | **Parachute** | toggle, drain | slow fall, full air control, +5 → 25 % damage with everything while floating; drains 2 → 1 mana/s | drain | Jump Training 3 | B.A.S.E. Jumper |
| 4 | **Trench Fighter** ◆ | upgrade, *melee* | +8 %/rank melee damage, +3 %/rank swing speed; a melee kill heals 2 → 6 % max health | — | Pain Tolerance | `tf_weapon_shovel`, heal-on-kill attribute |
| 5 | **Rocket Leap** | action, dash | an instant blast jump along your aim — no weapon, no ammo, no self-damage; blast at launch base 25 → 70 | 12 / 10 → 6 s | Stomp | — (new) |
| 6 | **Human Rocket** | capstone | launch yourself at a target point, invulnerable in flight; land for base 200 plus your *Stomp* | 40 / 45 s | Rocket Leap, Death From Above | — (new) |

Synergies: *Death From Above* +4 % per rank of *Stomp*, and its melee crit +10 % damage per rank of *Trench Fighter*; *Stomp* +8 % radius per rank of *Jump Training*; *Human Rocket* +5 % damage per rank of *Rocket Leap*.

### A.4 Command — orders, bellowing and staying alive

One **Order** at a time until the capstone. (TF2's banners are findable items under R12, so the skills are orders; the bugle and flag are only their visuals.)

| tier | skill | kind | what it does (rank 1 → 5) | mana / cd | needs | behaviour reference |
|---|---|---|---|---|---|---|
| 1 | **Battle Orders** | action, aura | 8 → 12 s, party within 450 u deals +15 → 35 % damage with everything | 20 / 30 s | — | `tf_weapon_buff_item` aura |
| 1 | **Basic Training** | upgrade | +3 %/rank max health, +2 %/rank all resistances | — | — | — |
| 2 | **Dig In** | action, aura | 8 → 12 s, party takes −15 → 35 % damage and cannot be crit | 20 / 30 s | Basic Training | defence-buff condition |
| 2 | **Maggots!** | action, threat + debuff | robots within 600 u target you for 4 s and deal −10 → 30 % damage | 8 / 12 s | Basic Training | — (needs 5c threat table) |
| 3 | **Forward March** | action, aura | 8 → 12 s, party heals for 10 → 30 % of damage dealt and moves +15 % faster; passive +1 → 3 HP/s always | 20 / 30 s | Battle Orders | regen-buff condition |
| 3 | **Art of War** | upgrade | +4 %/rank damage with all weapons and actions against champion, giant and boss robots | — | — | — |
| 4 | **Chain of Command** | upgrade | Orders: +10 %/rank duration, +8 %/rank radius, −4 %/rank cooldown | — | any two Orders | — |
| 4 | **Rally Cry** | action, heal | party: cleanse burn / bleed / slow, heal 10 → 30 % max health over 4 s | 22 / 40 s | Dig In | — |
| 5 | **Juggle Doctrine** | upgrade, triggered | any robot you knock airborne, by any means, is marked 3 → 5 s: the whole party mini-crits it | — | Art of War | airborne-target mini-crit |
| 6 | **Supreme Commander** | capstone | all three Orders at once at your ranks for 10 s; passively, issuing an Order no longer cancels the one in force | 60 / 120 s | Chain of Command, Forward March | — |

Synergies: each Order +3 % potency per rank in the other two; *Juggle Doctrine* mark +0.5 s per rank of *Marksmanship* (Ordnance).

### A.5 Mercenary — the tab every class shares

Utility upgrades only: no actions, no weapons, identical for all nine classes. Bought
with the same skill points as everything above, which is the whole point — carrying
more is paid for in fighting less. No tiers; instead each **rank** has a hero-level
floor, so capacity grows across the whole climb instead of being bought out at level 7.

| skill | ranks | what each rank gives | rank *r* needs hero level |
|---|---|---|---|
| **Deep Pockets** | 7 | **inventory size**: +4 backpack slots (12 → 40) | 4*r* − 3 (1, 5, 9 … 25) |
| **Mann Co. Storage Plan** | 5 | **stash size**: +16 stash slots (20 → 100); the account uses its highest rank among living characters (D4) | 5*r* − 4 (1, 6, 11, 16, 21) |
| **Second Wind** | 5 | **health regen over time**: +0.3 % max health per second, doubled after 5 s without taking damage | 3*r* |
| **Leech** | 5 | **health from damage**: heal for 1 % of all damage you deal, any weapon or action | 3*r* |
| **Clear Head** | 5 | **mana regen over time**: +8 % mana regeneration | 3*r* |
| **Siphon** | 5 | **mana from damage**: +0.5 % of damage dealt returns as mana | 3*r* |
| **Scavenger** | 5 | +6 % chance a kill drops an item, +4 % Australium found | 3*r* |
| **Haggler** | 5 | −4 % vendor, repair, identify and crate prices; +4 % sell prices | 3*r* |
| **Field Rations** | 5 | potions restore +8 % more and the belt's shared cooldown is 6 % shorter | 3*r* |

Sustain **stacks additively** with class skills and item affixes that do the same job
(the Soldier's *Field Dressing* and *Forward March*, a `+life on hit` affix), and the
totals are capped in 2a — health from damage at 15 % of damage dealt, regeneration at
3 % max health per second — so no pile of sources adds up to a hero who cannot die.

Capacity is derived from these ranks and nothing else (2d), so a respec that lowers a
rank shrinks the bag by 2d's rule: overflow goes withdraw-only, nothing is lost.

### A.6 The builds this is meant to allow

| build | points go to | attributes | plays like |
|---|---|---|---|
| **Artillery** | Ordnance actions to *Screamin' Eagle*, *EMP Charge* maxed, a rank of *Battle Orders* | Aim, Smarts | a caster; the gun is for when the mana runs out. Wants `+skill` and `+mana` gear |
| **Gunman** | upgrades over actions — *Marksmanship*, *Killer Instinct*, *Pain Tolerance*, *Basic Training*, then commits to a class with *Rocket Specialist* or *Trench Fighter* | Aim, Grit | the loot build: the next good drop of his class is better in his hands than anyone's |
| **Trolldier** | Airborne to *Human Rocket*, *Jump Training* + *Death From Above* maxed, *Rain of Freedom* | Brawn, Grit | never on the ground; spends health as a resource; highest skill ceiling |
| **Sergeant** | Command to *Supreme Commander*, *Field Dressing*, *Basic Training* | Grit, Smarts | the party's spine; weakest solo, strongest in four |

Any of them can give up ~8 points to the Mercenary tab and become the party's pack mule — the R13 trade made visible.

- Gate (design, checked by the 2f/2c economy model before any human plays it): at hero level 30 with median gear, the first three builds each clear a depth-15 pack within ±25 % of the others' time solo; *Sergeant* raises a four-hero party's clear speed by more than a fourth *Artillery* would; and swapping a build's weapon for a Stock one costs *Gunman* the most and *Artillery* the least — the proof that actions and loot really are separate axes. A build outside the band is a data bug, not a playstyle.

---

## Appendix B — the robot AI: off the main thread, fed by events, answering with commands

### B.1 The constraint that shapes it

The engine is single-threaded and so is everything that touches it: entities, traces,
sounds, the ABI. So the AI thread may touch **none** of it. That is not a convention
to remember, it is a build fact: `Descent.Ai` has no reference to `SourceSharp.Abi`
(§1), and a test fails if one appears. Everything the AI knows arrives as data the
main thread published; everything it wants happens because the main thread read a
command. The reference for *what* to perceive and react to is NextBot, which is
already event-shaped — `NextBot/NextBotEventResponderInterface.h` declares 53
`On…` events and `NextBotVisionInterface.h` the sight rules. We port its vocabulary,
not its threading.

### B.2 One frame

```
 main thread, frame N                                   AI thread
 ───────────────────────────────────────────           ─────────────────────────────────
 1 APPLY    drain commands (made from N-1)   ◀──────── command slots (latest per robot) + one-shot queue
            validate handle → locomotion / aim / fire
 2 SIMULATE engine tick
 3 SENSE    vision budget → sight events
            taps → sound / damage / gameplay events
 4 PUBLISH  snapshot N (latest-wins) ─────────────────▶ wake: take newest snapshot,
            events (queue, never dropped) ────────────▶ drain ALL events
            signal, and carry on — never wait          remember → decide → path → steer
                                                        write commands tagged "from N"
```

The main thread **never blocks on the AI**. If the AI is late, frame N+1 applies the
commands it already has. Steady state, a robot acts on frame N+1 on what was true at
frame N: one tick of latency, which is below a robot's designed reaction time anyway.

### B.3 Inputs — the AI perceives, it does not query

**Events** (main → AI, a single-producer single-consumer ring; **never dropped** — on
overflow the producer counts and the gate fails, it does not overwrite):

| family | events | produced by |
|---|---|---|
| sound | `Sound { pos, radius, kind: gunfire / explosion / footstep / voice / skill / pain / death, source, team }` | a tap where `Descent.Server` emits gameplay sounds — weapon fire, blasts, landings, sprinting, skills, *Maggots!* |
| sight | `SightGained { robot, target, pos }`, `SightLost { robot, target, lastPos, lastVel }` | the main-thread vision pass (below) |
| hurt | `Damaged { robot, attacker, amount, dir, kind }`, `AllyDied`, `AllyDamaged` | the damage pipeline |
| gameplay | `Taunted { robot, by, until }`, `OrderIssued`, `HeroDowned`, `HeroRevived`, `HeroDied`, `HazardArmed / HazardGone` (barrels and traps, so a brain knows what it can use and what to stand clear of), `HeroCastSkill`, `DoorOpened`, `RoomWoke / RoomSlept` | the systems that own them |
| self | `Spawned { robot, template, scale, temperament, seed, cell }`, `Removed`, `Arrived`, `Stuck`, `Blocked`, `Landed`, `Stunned / Recovered`, `KnockedAirborne`, `ClipEmpty`, `ReloadDone`, `CommandRejected { seq, why }` | the robot entity and the command applier — the feedback half of the loop |

**Snapshot** (main → AI, **latest wins**; an old one is worthless): per robot — position,
velocity, health, clip, grounded; per hero — position, velocity, state flags. It is
truth, and a brain is **not allowed all of it**: a hero's kinematics may be read only
by a robot that currently holds `SightGained` on that hero. Everything else a robot
believes comes out of its memory. That rule is what stops an off-thread AI with a
full world copy from being omniscient, and it is enforced in the type: brains receive
a `PerceivedView` filtered per robot, never the raw snapshot.

**Sight is the one sense that needs the engine**, because line of sight is a trace.
The main-thread vision pass keeps traces rare: a candidate pair must pass range, the
robot's view cone (eye height scales with R16), and a **cell-to-cell visibility table**
MapForge precomputes per room and stitches through door portals (6d, alongside the
nav fragments); only survivors cost a trace, from a fixed per-frame trace budget
served round-robin. With ≤ 4 heroes the candidate set is small; the budget is set
from 0i's numbers. Cloak, disguise and darkness are multipliers on range.

**Hearing needs no engine at all.** The AI thread attenuates a sound by **path
distance through the room graph**, not straight-line distance, so a blast two rooms
away is muffled and one through a solid wall is not heard as adjacent.

### B.4 Memory — what a brain keeps

Per robot, a fixed-size blackboard, no allocation after spawn:

- **known entities** — `{ handle, lastSeenPos, lastSeenVel, lastSeenTick, lastHeardPos, lastHeardTick, confidence }`. Confidence decays; a lost target is *predicted* along `lastVel` for a moment, then becomes a place to search, then is forgotten.
- **threat table** — per hero: damage taken from them, healing they did, proximity; decays. `Taunted` pins the target until its tick, which is all *Maggots!* (Appendix A) needs.
- **investigations** — unexplained sounds worth walking to, ranked by loudness and recency.
- **self** — current intent, its sequence number, last feedback (`Stuck`, `Arrived` …).

Per pack, a **shared blackboard**: when one robot gains sight it "calls out" — after a
short delay, pack-mates within earshot receive the contact at reduced confidence.
Alert spreads the way sound does, so a careful player can kill a sentry before the
room wakes. Medics and shielders read the pack board to choose whom to follow.

### B.5 Deciding, pathing, steering

- **Decide** (each brain at 10 Hz, staggered across snapshots; every threshold and weight below is read from the robot's **temperament**, B.11 — the tree is shared, the numbers in it are personal): a behaviour tree per template family whose leaves only *read memory* and *write an intent*. Bosses use the same runtime with phase-scripted trees.
- **Path**: hordes do not run 60 A* searches. Per hero cell and per **clearance class** (R16: runt / normal / giant) the AI thread keeps one Dijkstra **flow field** over the `NavGraph`, rebuilt only when that hero changes cell; a chasing robot just reads its cell's arrow. Individual A* with funnel smoothing is kept for the odd trip — investigate, flee, take a sniper post — time-sliced so no single think overruns.
- **Steer** (every snapshot): separation from neighbours scaled by hull, corner-cutting guarded by clearance, strafing and range-keeping per weapon family.

### B.6 Output — intents with a lifetime, not per-frame pushes

```
MoveCommand { robot, fromTick, seq, expiresTick,
              move   : Hold | Follow(corners[≤4], speed) | Strafe | Flee | Jump,
              face   : None | MoveDir | Point(p) | Entity(h),
              action : None | Fire | Melee | Ability(id) | Reload | Taunt   (+ oneShotSeq) }
```

A command is an **intent the main thread can keep executing on its own for several
frames**: up to four path corners ahead, and `face: Entity(h)` rather than an angle,
so aim is resolved on the main thread against the target's *current* position and is
never a frame stale. Two channels: a **latest-wins slot per robot** for move / face,
and a **sequenced queue** for one-shot actions, which must be neither lost nor
repeated (a re-applied slot must not fire a second rocket).

The applier trusts nothing. It checks the robot handle is still that robot — handles
carry a serial, and the invalid handle is an explicit sentinel, never `default`
(memories `default-handle-is-entry-zero`, `hskip-sentinel-is-invalid-ehandle`) — that
`seq` is newer, that the robot is not stunned or airborne, that the ability is off
cooldown; a refusal goes back as `CommandRejected`. Locomotion, collision, animation
and the weapon stay main-thread code in the robot entity (5a).

### B.7 When things go wrong

- **AI thread late or stalled**: commands carry `expiresTick` (~250 ms). Robots finish what they were told, then hold. They never chase a ghost and the server never hitches. Events keep queueing and are all processed on recovery.
- **Robot died between publish and apply**: the handle check drops the command; counted, not logged per instance.
- **GC**: a collection started by the AI thread stops the main thread too. So a brain tick allocates **nothing** in steady state — pooled structs and arrays sized at spawn — and a test holds it to zero with `GC.GetAllocatedBytesForCurrentThread()`.
- **Lifecycle**: the thread starts at level load with the immutable `NavGraph` and visibility table, and is **stopped and joined at level shutdown before either is released**. Teardown order is a named risk in this repo (memory `two-static-instruments-agreed-and-the-game-segfaulted`), which is why the gate loads two maps.
- **Many servers per box** (R10): one AI thread per server process, not a pool per process — twenty instances must not mean twenty pools.

### B.8 Determinism and debugging

A brain is a pure function of `(seed, snapshot stream, event stream)`; the thread is
only transport. `IAiTransport` has two implementations — **threaded**, and **inline**
(brain runs on the caller, `descent_ai_sync 1`) — so any bug can be A/B'd between
"the logic is wrong" and "the hand-off is wrong". A server can **record** its input
streams; a test **replays** a recording through a fresh brain and demands the
identical command stream. Every live AI bug becomes a fixture. `descent_ai_dump
<robot>` prints a brain's blackboard through `devapi`.

### B.9 Build order (this is 5c)

1. `Descent.Ai` skeleton, the three transports' data types, the inline transport, the no-engine-reference fact.
2. Temperament roll (in `Descent.Rules`) and the trait → parameter curves, so every later step is built reading parameters, never constants. Then memory and hearing, driven entirely by synthetic event streams in xUnit.
3. Threat, pack board, alert propagation.
4. Flow fields with clearance classes, steering.
5. Behaviour-tree runtime and the first three families (melee rusher, rocket soldier, medic).
6. Main-thread half in `Descent.Server`: sound and damage taps, vision pass, command applier.
7. Threaded transport, stall and teardown handling, recorder and replayer.
8. Remaining families, then boss trees (Phase 11).

### B.10 Gates

- **Pure (xUnit, no engine):** a fact per memory rule (decay, predict, search, forget), hearing by path distance vs a wall, threat and `Taunted`, alert propagation delay and range, flow field never routes a giant through a door it cannot fit, steering separation; `PerceivedView` cannot yield a hero the robot has no sight on (asserted by trying).
- **Structure:** `Descent.Ai.csproj` has no `SourceSharp.Abi` reference, mutation-verified by adding one; zero allocated bytes over 1,000 steady-state brain ticks.
- **Determinism:** a recorded session replays to a byte-identical command stream, inline and threaded.
- **Hand-off:** events in = events processed, across an induced 1 s AI stall; during the stall main-thread frame time is unchanged, commands expire and robots hold; a one-shot action is applied exactly once under slot re-application; a command for a robot killed that frame is dropped and counted.
- **Temperament:** B.11's gates.
- **Live:** 60 robots, 4 heroes — main-thread AI cost (sense + publish + apply) inside the budget 0i set, measured under matched load; two map loads, with the process's thread count (`/proc/<pid>/task`) back to baseline after each shutdown; the arena test of §7 passes identically with `descent_ai_sync` 0 and 1.

### B.11 Temperament — ten AI skills, a budget set by size (R20)

Every brain carries ten skills, each level **0–10**, bought from a budget of
**`round(40 × scale)`** points — size (R16) is the only thing that moves it:

| size | scale | points | out of a possible 100 |
|---|---|---|---|
| runt | 0.60 – 0.75 | 24 – 30 | one real strength and little else |
| ordinary | 0.85 – 1.15 | 34 – 46 | two or three strengths, clear weaknesses |
| giant | 1.50 – 1.90 | 60 – 76 | good at most things; one or two gaps to find |
| boss | fixed per D13 | by its scale | its few weaknesses are authored as the fight's puzzle, not rolled |

Nobody is good at everything — even a 76-point giant leaves 24 on the table — and
what a robot is bad at is as much a tell as what it is good at. Robots of equal size
are equals in points; only the spread differs.

| AI skill | at 0 | at 10 | what it drives in the brain |
|---|---|---|---|
| **Aggression** | hangs at maximum range, backs off when approached | closes to point-blank, chases through rooms | preferred engagement range, chase persistence, push-on-ally-death |
| **Accuracy** | sprays, never leads a moving target | tight cone, leads projectiles, picks the head | aim error cone, tracking lag, lead quality. It is *aim*, not damage — damage is 2f's business |
| **Cover** | stands in the open, reloads where it is | fights from corners, reloads out of sight, peeks | weight on cells the target cannot see (the B.3 visibility table doing double duty), reload-retreat, peek timing |
| **Soloness** | glued to its pack, focus-fires the pack's target, always calls out | roams and flanks alone, ignores pack calls, never calls out | leash distance to pack centroid, pack-board read / write weight, target choice. The pack's lowest-Soloness robot is who the others form on — leaders emerge, they are not assigned |
| **Alertness** | tunnel vision, deaf, slow to react | wide cone, hears a footstep next door, snaps to contact | view cone and range multipliers, hearing multiplier, delay from `SightGained` to first action |
| **Tenacity** | forgets you at the first corner | hunts your last known position for a minute | confidence decay rate, search duration, how many rooms it will follow |
| **Discipline** | fires the moment it sees anything, wastes abilities, easily baited | holds fire to effective range, bursts, saves abilities, kills the medic first | fire-control gates, ability timing, target priority, up to −30 % duration on `Taunted` |
| **Agility** | walks in straight lines | strafes, jumps, leaves a *Fire Mission* marker at once | strafe and dodge weights, reaction to `HeroCastSkill` and incoming projectiles |
| **Cunning** | always takes the shortest path | takes the other door, waits beside the doorway, predicts where you will reappear | alternate-route choice over the room graph, ambush holds, lost-target prediction |
| **Nerve** | breaks when hurt or when allies fall | never retreats, not even at 1 HP | morale: flee thresholds on own health, `AllyDied`, a giant's death nearby |

**The roll** lives in `Descent.Rules` beside scale (2f), a pure function of
`(packSeed, robotIndex, template)`, so a recording replays and the service can re-derive it:

- **Spiky on purpose.** Points are not spread evenly: the roll picks two or three *signature* skills and loads them, then scatters the rest. An even 4-4-4-4 robot is the one style that reads as no style.
- **Templates bias, they do not add.** A template supplies weights and per-skill caps, never points — only size does that: a Sniper leans Accuracy / Cover / Soloness, a Scout bot Aggression / Agility, a Medic is capped at Soloness 2, a Sentry Buster at Cover 0 and floored at Nerve 10. Points a cap refuses spill to the next-weighted skill, so a capped giant still spends its whole budget.
- **Packs are mixed.** The spawn director re-rolls a pack whose members are all the same style, so a room holds a rusher, a lurker and a marksman rather than five of one.

**Levels become behaviour through data.** Each skill maps through a JSON curve to the
concrete numbers the brain reads — `aimErrorDeg`, `reactionMs`, `retreatHealthPct`,
`leashUnits`, `searchSeconds` … — some fed by two skills (flanking wants Cunning *and*
Soloness). Tuning a style never touches code.

**Players can read it.** A robot's top two skills give it a style name — *Reckless,
Deadeye, Skulking, Lone, Vigilant, Relentless, Drilled, Twitchy, Devious, Fearless* —
shown on champions and bosses ("Deadeye Lone Sniper") and printed by
`descent_ai_dump`. `descent_spawn_robot <template> <skill=level …>` forces a
temperament for testing, refusing any that does not sum to the budget for the scale given.

- Gate (roll, pure): every roll sums to exactly `round(40 × scale)` — asserted at the ends and middle of every size range, and that budget is strictly monotonic in scale and never exceeds 100; two robots of equal scale always have equal budgets whatever their depth, template or affixes; cap spill-over loses no points; each roll respects 0–10 and the template's caps, and is seed-stable; over 100k rolls per size class ≥ 90 % have a skill at 7 or more, runts included (spiky) and each template's biased skills have a higher mean than the unbiased ones; a same-style pack is re-rolled.
- Gate (each skill does what it says): in the inline AI simulation with everything else held equal, each skill's observable is **monotonic in its level** — Accuracy ↑ mean aim error ↓; Aggression ↑ mean engagement distance ↓; Cover ↑ share of time in cells the target cannot see ↑; Soloness ↑ mean distance to pack centroid ↑; Alertness ↑ detection delay ↓; Tenacity ↑ search time ↑; Discipline ↑ shots fired beyond effective range ↓; Agility ↑ projectiles dodged ↑; Cunning ↑ share of approaches by a non-shortest route ↑; Nerve ↑ retreats ↓. One fact per skill, and each mutation-verified by flattening that skill's curve.
- Gate (no skill is the best buy): across 10k simulated fights **within one size class** and with stats held equal, robots grouped by signature skill land within a ±20 % band of threat to the hero. A skill outside the band is mis-priced and its curve is retuned, never the budget.
- Gate (size pays its way): across size classes threat rises with budget, and 2f's threat-per-XP band still holds once the budget term is in the XP formula — a 76-point giant must be worth more, not just be harder.

---

## Appendix C — the character sheet and attribute points (R22)

### C.1 The sheet

Laid out after the Diablo 1 sheet supplied as reference, field for field:

```
 ┌ name ─────────────┐ ┌ class ────────────┐
   Level   [ 18 ]       Experience [   41,902 ]
                        Next level [   57,640 ]
            BASE  NOW                  Australium [ 14,815 ]
   Brawn   [ 45 ][ 62 ] +
   Smarts  [ 10 ][ 10 ] +             Armour        [ 292 ]
   Aim     [ 85 ][111 ] +             Crit          [ 14% ]
   Grit    [ 40 ][ 56 ] +             Damage        [45-52]
   Points to distribute [ 5 ]
                                      Resist fire   [ 50% ]
   Life    [163 ][163 ]               Resist blast  [ 75% ]
   Mana    [108 ][ 39 ]               Resist bullet [ 20% ]
                                      Resist shock  [ 75% ]
```

- **Base** is what the character *is*: class starting value plus every point invested. **Now** is base plus gear, skills and buffs, and **everything in the game reads Now** — derived stats and item requirements alike.
- The **+** buttons appear only while *Points to distribute* is above zero. One click spends one point; shift-click spends five. Spending is immediate, server-side, and permanent short of a respec.
- Colour carries information, as in the original: Now in **blue** when raised above base, **red** when lowered (a curse, a broken item); Base in **gold** when it has reached the class cap; a resistance in gold at its cap; current Life or Mana in red when below maximum.
- Life and Mana show *maximum | current*. Damage is the equipped weapon's real range after every bonus, so the sheet answers "is this drop better?" without arithmetic.

**One deliberate departure.** Diablo's *To hit* is a dice roll against armour class, and
it has no place in a first-person game where the player does the aiming — a rocket
that visibly connects must connect. Its slot on the sheet is **Crit** chance, which is
what Aim buys instead; likewise **Armour** reduces the damage of a hit that lands
rather than making hits miss.

### C.2 What a point buys

| attribute | Diablo's | each point gives | also gates |
|---|---|---|---|
| **Brawn** | Strength | +1 % melee damage, +0.5 % blast-weapon damage, +0.5 % knockback resistance; Airborne skill damage (Appendix A) | heavy weapons and body armour |
| **Smarts** | Magic | `manaPerSmarts` max mana, +1 % action damage on Smarts-scaled actions, +0.5 % mana regeneration | spell tomes, energy weapons |
| **Aim** | Dexterity | +1 % bullet and projectile damage, +0.1 % crit chance, −0.3 % spread, +0.2 armour (Diablo's Dex / 5) | precision weapons |
| **Grit** | Vitality | `lifePerGrit` max life, +0.2 % to every resistance | — |

Armour reduces a landed hit by `armour / (armour + 50 + 10·attackerLevel)` — diminishing,
never total, and worth less against deeper robots. Resistances cap at **75 %**.

Classes start differently and **cap differently**, so a Heavy can never out-think a
Medic however he spends. Every class starts with 60 points' worth and has room for 250
more; a level-30 hero has 145 to invest (29 level-ups × 5), so nobody maxes
everything and most heroes cap one attribute at most.

| class | Brawn start / cap | Smarts | Aim | Grit | life per Grit | mana per Smarts |
|---|---|---|---|---|---|---|
| Scout | 15 / 60 | 10 / 60 | 25 / 110 | 10 / 80 | 2.0 | 1.5 |
| Soldier | 20 / 90 | 5 / 50 | 20 / 90 | 15 / 80 | 3.0 | 1.5 |
| Pyro | 20 / 85 | 10 / 75 | 10 / 60 | 20 / 90 | 3.0 | 2.0 |
| Demoman | 20 / 90 | 10 / 60 | 15 / 80 | 15 / 80 | 3.0 | 1.5 |
| Heavy | 25 / 110 | 5 / 40 | 10 / 60 | 20 / 100 | 4.0 | 1.0 |
| Engineer | 10 / 60 | 25 / 100 | 15 / 80 | 10 / 70 | 2.0 | 2.5 |
| Medic | 10 / 50 | 25 / 110 | 10 / 70 | 15 / 80 | 2.5 | 3.0 |
| Sniper | 10 / 55 | 10 / 70 | 30 / 120 | 10 / 65 | 2.0 | 2.0 |
| Spy | 10 / 60 | 20 / 90 | 20 / 100 | 10 / 60 | 2.0 | 2.5 |

### C.3 Rules

- **Points are earned by levelling and nothing else**: 5 per level-up, banked if unspent, shown as *Points to distribute*. `base ≤ cap` always; a point cannot be put into a capped attribute.
- **The server spends, the service audits.** For every character the service can check `Σ(base − start) + unspent = 5 × (level − 1)` and `base ≤ cap`; a checkpoint that fails is refused, like a forged item (3c).
- **Requirements read Now, and losing them never strips you.** Gear that grants Brawn can be what lets you wear other gear. If a requirement stops being met — the ring comes off, the item breaks (2h) — the dependent item goes **inactive** exactly as a broken one does: it stays equipped and contributes nothing until the requirement is met again. No cascade of items falling into a full backpack.
- **Respec** at Merasmus returns every invested point for a price that rises each time; base falls back to class start, and the inactive rule above handles whatever gear that orphans.
- **Hardcore** changes nothing here.

### C.4 Gates

- Pure: a fact per row of C.2 — each attribute's per-point effect, per class multiplier; start and cap for all nine classes (every start sums to 60, every class has 250 of headroom — asserted over the JSON, not by eye); spending refused at cap, at zero points, and for another player's character; shift-spend never overshoots a cap and returns the remainder; the audit identity holds after any sequence of level-ups, spends and respecs (property test); armour is monotonic, never reaches 100 %, and weakens with attacker level; resistances stop at 75 %; an unmet requirement makes an item inactive and meeting it again restores it, with no item moved.
- View-model (Phase 8): Base / Now colouring for raised, lowered and capped; the + buttons exist exactly when points do; Damage shows the equipped weapon's true range.
- Live: level a hero through `devapi`, spend points, reconnect through the service, and the sheet is identical; a forged checkpoint with one extra point is refused by the service.

---

## Appendix D — compared with Diablo 1: what is missing (undecided)

Measured against Diablo 1 feature by feature. **Ruled in on 2026-09-20: #1, #3, #4, #5,
#6, #8, #9 → R23–R29**, written into the phases above; their rows below are kept as
the record of why. Every other row is still only a recommendation.

### D.1 Where the plan already matches or goes further

Town with smith / witch / healer / identifier / gambler (Griswold, Adria, Pepin, Cain,
Wirt → Hale + Engineer, Merasmus, Medic, Spy, Scout); random dungeons from tile sets;
boss floors; four attributes on a Base / Now sheet with class caps; prefix / suffix
items, uniques, durability, identify, requirements; town portal; level shortcuts; four
players; XP scaled by level difference; monster families with variants and named
champions. **Beyond Diablo 1:** a stash (D1 had none), skill trees, crafting, secure
trading, per-party instancing, hardcore, server-authoritative persistence (D1's open
characters were trivially cheated), instanced loot, robot temperament and size.

### D.2 What is missing, most important first

| # | Diablo 1 has | the plan has | recommend |
|---|---|---|---|
| 1 | an **automap** — indispensable in a maze you have never seen | nothing | **RULED → R23.** **v1.** Nearly free: the layout generator already owns the room graph; reveal rooms as entered, mark stairs, party, corpse and portal |
| 2 | **quests**, a random subset per game (Butcher, Skeleton King, Poisoned Water …) with set-piece rooms, NPC dialogue and unique rewards — most of its replay value and all of its story | D17 puts them out of v1; no story frame at all (why is there a robot works under a church?) | **v1, small.** A story spine of 3 fixed beats on 5 / 10 / 15, plus a pool of ~8 side quests of which each descent rolls 3; quest rooms are tagged prefabs the generator must place |
| 3 | **dungeon furniture**: shrines with cryptic, sometimes harmful effects; fountains; chests, exploding barrels, sarcophagi, bookcases, weapon racks; trapped chests and doors | a `shrine` room tag and a treasure room; no objects, no traps, no destructibles | **RULED → R24.** **v1.** It is what a room is *for* between fights. Explosive barrels and traps also give robots and *Cunning* something to use |
| 4 | **monster resistances and immunities** per damage type — the reason a single-element build cannot clear the game | heroes have four resistances; robots have none | **RULED → R25.** **v1.** One field on `RobotTemplate` plus a champion affix (*Fireproof*, *Shock-hardened*). Immunity only on champions; ordinary robots resist at most |
| 5 | **resurrection by another player** in multiplayer | death always sends you to town | **RULED → R26.** **v1 for parties.** A downed window (say 20 s) in which a party member can revive; solo, or if it lapses, R5's corpse run applies unchanged. Hardcore: downed is still a chance, lapse is still death |
| 6 | spells learned from **books**, levelled by reading more copies, open to every class; single-use **scrolls**; **staves with charges**, recharged in town | Merasmus sells cross-class tomes (2e) but tomes have no levels; no scrolls, no charges | **RULED → R27.** **v1: tome levels and scrolls.** Re-reading a tome raises that spell's rank (Smarts requirement rising) — a second progression that drops as loot. Charges: skip |
| 7 | **elixirs** — rare permanent +1 to an attribute | nothing | **v1.** Trivial, and the best possible drop at the level cap. C.3's audit identity gains an `elixirs` term, still bounded by the class cap |
| 8 | **cursed items** — negative affixes, and the reason identifying is a gamble | every affix is a bonus, so identify (R14) only ever reveals good news | **RULED → R28.** As ruled: negative *stats* that sit on an item beside its positive ones, each buying extra positive budget |
| 9 | jewellery: two rings **and an amulet** | two trinkets | **RULED → R29.** cheap; add an amulet slot (TF medals are the models) |
| 10 | **named unique monsters** in fixed places with their own packs and lore | champions with style names, rolled | fold into #2: quest targets are the named ones |
| 11 | **darkness and light radius**: monsters wake by your light; +light gear | rooms wake by distance; vision by cone | **skip the stat, keep the mood** — tileset lighting only. *Alertness* (B.11) already does the mechanical job |
| 12 | Normal / Nightmare / Hell | D12: schema ready, one tier in v1 | as planned — after v1 |
| 13 | monster lore that fills in as you kill more of a type | nothing | later; a bestiary is pure UI over kill counts the service can already keep |
| 14 | grid inventory with item footprints | one-cell slots (D4) | keep D4 — R13's capacity skills are clearer in slots |
| 15 | friendly fire and hostility | R8: no PvP | keep R8 |
| 16 | gossip, lore books, music per tileset | the Administrator as a voice (D15) | with #2; sound is an art cost like rooms |

### D.3 Two things Diablo could not do that this setting can

- **Engineer-bots as nests.** Diablo has no spawners; MvM does. An Engineer robot that builds a teleporter feeding the room until it is destroyed is the TF-native version of a monster generator, and gives the *Soloness* / *Discipline* robots something to defend.
- **Verticality.** Every Diablo level is flat. R19's jump rooms mean height is a resource: snipers on gantries, loot on ledges, shortcuts only a blast jump reaches.

