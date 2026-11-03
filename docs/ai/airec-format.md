# The `.airec` recording format (version 3)

What `AiRecorder` writes and `AiReplayer` reads (`src/ai/core/Record/`,
plan_hl2mp_ai §2.6). The code is the definition; this page is its map.

## Framing

```
file    = magic "SSAIREC\0" (8 bytes), u16 version (= 3; 2 and 1 are read too), record*
record  = u8 type, u32 payload length, payload
```

Little-endian throughout. A reader refuses another magic,
a version other than 1, 2 or 3, a record type the format does not have, a payload over 16 MiB, a record cut
short, and a payload with bytes its type does not read. The first record
is the Header, and there is one.

## Records

| type | name | payload |
|---|---|---|
| 1 | Header | u64 match seed, u16+utf8 map, u64 nav graph hash, u64 skill parameters hash, i32 slots, i32 sighted capacity |
| 2 | BotAdded | actor, u64 seed, u16+bytes skill levels |
| 3 | BotRemoved | actor |
| 4 | Event | `AiEvent`: u8 kind, actor bot, i32 tick, actor other, vector position, vector velocity, f32 amount, i32 code, i32 code2 |
| 5 | Snapshot | i32 tick, u16 views, then per view in slot order: actor, `SelfState`, u16 sighted, `ActorFacts`* |
| 6 | Intent | `IntentCommand`, every field (the replay compares these bytes) |
| 7 | UserCmd | `AiUserCmd`: one player's usercmd as the server ran it, bots' and humans' |
| 8 | World | (version 2) what the brains know: u16+utf8 map, f32 tick seconds, u64 nav graph hash, i32 items then each item (i32 index, u16+utf8 class, u8 kind, vector, f32 respawn seconds, i32 weapon kind), i32 spawns then vectors, i32 weapons then each `WeaponFacts` (i32 kind, u16+utf8 name, i32 clip, f32 cadence, f32 reload seconds, bool reload audible, bool shell by shell, f32 projectile speed), i32+utf8 the skill curves' JSON. Written by `ai_record start` right after the Header, before the bots |
| 9 | Frame | (version 3) `AiActorFrame`: actor, i32 tick, vector origin, vector eye, vector velocity. Every living actor, humans and bots, each tick while recording (§3.4.4) |
| 10 | Observed | (version 3) an `AiEvent` (as Event) about an actor no brain plays: a person's `SightGained`/`SightLost` from the bots' vision pass, and every actor's `ShotFired` (i32 code the weapon, f32 amount the rounds) and `HitDealt` (other the victim, f32 amount the damage, code the weapon it last fired, code2 the range in units). Never fed to a brain |

## Anonymity (version 3)

`ai_record` writes every actor through an `ActorAnonymiser`: a person's
actor (a human's serial, marked by `0x40000000`) is written as its slot
with a serial drawn from the OS's random generator for that recording, the
same in every record (events, the KillFeed's killer codes, snapshots'
sighted facts, intents' look targets, usercmds, frames); bots are written as
they are. No record type holds a name, a Steam id or chat.
`ai_record human` (or `ai_record start` with no path) writes
`<mod>/ai-recordings/human/<utc>.airec`, outside git.

An actor is `i32 slot, i32 serial`; a vector three f32; angles three f32
(pitch, yaw, roll).

## Replay

Records are fed in file order to a fresh `AiSession` (`AiReplayer.ReplayWithRecordedWorld`
for a recording with a World record: the brains are built over its map
knowledge and curves and the graph whose hash it names, with the game's
deciders; the transport may be inline or a pool of any size, the replay
waits rather than let a lane drop a step): BotAdded, BotRemoved,
Event and Snapshot drive it, Intent records are collected as the expected
stream, UserCmd records are counted, Frame and Observed records are skipped
(they are `ai-replay metrics`' input). The replay passes when the intents the
fresh brains produce encode to the same bytes as the recorded ones.
