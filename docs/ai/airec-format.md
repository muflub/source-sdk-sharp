# The `.airec` recording format (version 1)

What `AiRecorder` writes and `AiReplayer` reads (`src/ai/core/Record/`,
plan_hl2mp_ai §2.6). The code is the definition; this page is its map.

## Framing

```
file    = magic "SSAIREC\0" (8 bytes), u16 version (= 1), record*
record  = u8 type, u32 payload length, payload
```

Little-endian throughout. A reader refuses another magic, another version,
a record type version 1 does not have, a payload over 16 MiB, a record cut
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

An actor is `i32 slot, i32 serial`; a vector three f32; angles three f32
(pitch, yaw, roll).

## Replay

Records are fed in file order to a fresh `AiSession`: BotAdded, BotRemoved,
Event and Snapshot drive it, Intent records are collected as the expected
stream, UserCmd records are counted. The replay passes when the intents the
fresh brains produce encode to the same bytes as the recorded ones.
