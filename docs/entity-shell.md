# The entity shell, and what is left native in it

`plan_shim.md` §5 3b. Measured in lane 3c-entityshell, based on
the integration branch at `eaf6ae7d3`, with the instrument named at each claim.

Nothing here is a summary of the headers. It is the set of findings that had to
be **measured** rather than read, with the command that measured each, so that
the next lane can re-run them instead of believing this file.

---

## 1. The shell's vtable, settled: 59 virtual functions, 99 table entries

`plan_entity.md:146` says the shell has **39** engine slots. Lane S1 counted
**59** `virtual` declarations in `shell_standalone.h` and could not reconcile
the two. Both numbers are right about different things, and a vtable dump of
the built object says so **without a running game**:

```
tools/vtable-dump game/mod_sharp/bin/linux64/server.so CStandaloneEntity
```

It reads the `vtable for CStandaloneEntity` object straight out of `server.so`'s
`.data.rel.ro` — the entries are `R_X86_64_RELATIVE` relocations whose addends
are the function addresses — and finds each sub-vtable by the
`typeinfo for CStandaloneEntity` pointer that begins it, rather than by guessing
which words are relocated.

```
vtable for CStandaloneEntity  base=0x1e4d28  bytes=856  words=107
  sub-vtable 0 (words 2..):  60 slots   primary: the IServerEntity chain, IGameEntity, and the class's own
  sub-vtable 1 (words 64..): 11 slots   IServerNetworkable   (9 functions + the destructor pair)
  sub-vtable 2 (words 77..): 20 slots   ICollideable
  sub-vtable 3 (words 99..):  8 slots   IGameEntity          (6 functions + the destructor pair)
  TOTAL 99 virtual slots across 4 sub-vtables
```

Sub-vtables 1–3 hold **only** `non-virtual thunk to CStandaloneEntity::...`
entries, every one naming a function already in sub-vtable 0: the distinct-name
count over the whole table is 59, and over sub-vtable 0 alone it is also 59. So
the thunk tables add no functions, only entry points.

**The number to quote, and which question it answers:**

| question | answer |
|---|---|
| distinct virtual functions the shell defines | **59** |
| entries in the primary vtable | **60** — the destructor takes two |
| entries across all four sub-vtables | **99** |
| slots the four ENGINE interfaces declare | **39** — IHandleEntity 2 + IServerUnknown 3 + IServerEntity 3 + IServerNetworkable 9 + ICollideable 20, plus the destructor pair |

`plan_entity.md`'s 39 is the last row: what the engine's own interfaces require.
S1's 59 is the first: what the class actually declares. Neither was wrong; they
were never the same count. The 20 that are neither are the shell's own —
`Spawn`, `Touch`, `VPhysicsUpdate`, `GetGameEntity` and the rest of the
IGameEntity and relay surface — and they are what a slot census has to exclude
before "the engine calls 39 things" means anything.

---

## 2. Twenty ABI slots are wired, called by live managed rules, and do nothing

This is the finding that matters most in 3b, and it is the shape lane 2e-abi hit
once already (`ScriptedEntity.PlaySequence` routed through a nulled slot and
returned false on every map, silently, past two live-gate passes).

Lane S2 recorded `shell_entityapi.cpp` as "29 slot bodies are STUBS ... 23 of
them have no non-wrapper managed caller, 6 have callers that get nothing back".
**Re-measured at `eaf6ae7d3`, that split is inverted.**

```
./buildcpptests entity_shell_inert_slots_test     # the derived set, and the ledger
```

**The census IS the test**, which is why no throwaway script is cited here.
`sourcesharp/test/entity_shell_inert_slots_test.cpp` derives the set from
`shell_entityapi.cpp` and fails in both directions against the recorded list: a
slot that stops being a stub, and a slot that becomes one. Calling a body a stub
means that, with comments stripped, nothing in it reaches outside its own
arguments — no `->`, no `::`, no call. 28 bodies qualified; 27 since
`PlayerSetSlowMovement` was wired (2026-10-05, plan_physics_managed A27). (`GetPlayerTeam` is the
29th in S2's count; it is degenerate — a constant 0 for any player, −1 otherwise
— but it is not a stub.)

The managed half of the reading — which of the 28 have a caller — was taken by
resolving each slot's facade name out of `EngineBinding.cs` and grepping for
both names outside `SourceSharp.Abi`. It is not a committed instrument because
it answered one question once; the SET it produced is what is recorded below,
and re-taking it is a grep for the facade name in the table's right-hand
column.

**Why S2 saw 23 unreached and this lane sees 3.** Managed code does not call
`Engine.<Slot>`. It calls the **facade** method on `IEnginePlayers`,
`IEngineEntities` or `IEnginePlayerMovement` in `EngineBinding.cs`, which has a
*different name* — `GetArmor`, `SetFriction`, `ForceButtons`, `DetectInSkybox`.
A grep for the slot name sees the wrapper layer and stops there. The reading
below resolves each slot's facade name out of `EngineBinding.cs` — the
`... => Engine.<Slot>` lines — and then greps for **both** names outside
`SourceSharp.Abi`.

### (a) three slots with no managed caller at all

`GetPlayerWeaponAmmo`, `SetPlayerWeaponClip`, `TakePlayerAmmo`. Reached only by
`ArsenalPlayers`' own pass-through, which nothing calls. These are marked
retired; see `docs/abi-retirements.md`.

### (b) five slots the `ArsenalPlayers` decorator answers

`GiveItem`, `GiveAmmo`, `DropActiveWeapon`, `RemoveAllItems`, `GetActiveWeapon`.
The native stub is only the non-hierarchy fallback, and every player is on the
hierarchy, so it is unreachable in practice. Correct as they stand.

### (c) nineteen slots a live managed rule calls and receives nothing from

Each row is a game rule that silently does not happen:

| slot | the managed rule that calls it | what does not happen |
|---|---|---|
| `EntityGetArmor` / `EntitySetArmor` | `ServerModule.cs:1693`, `ItemBattery.cs`, `ItemSuitCharger.cs` | armour absorbs no damage; batteries and chargers give none |
| `EntitySetFriction` | `FuncFriction.cs`, `SsGib.cs` | `func_friction` does nothing; gibs slide |
| `EntitySetTakeDamageMode` | `TriggerCamera.cs:312,430` | the player stays damageable during a camera take |
| `EntitySetForceServerRagdoll` | `TriggerServerRagdoll.cs:83` | the trigger is inert |
| `EntityDetectInSkybox` | `EnvSmokeStack.cs:129` | a skybox smokestack is never detected as one |
| `AddPlayerScore` | `GameScore.cs:62` | `game_score` scores nothing |
| `PlayerGetButtons` | `TriggerCamera.cs:307,593` | the camera never sees a button |
| `PlayerSetFrozen` | `TriggerCamera.cs:344,425` | the player is not frozen for the take |
| `PlayerForceButtons` | `TriggerPlayerMovement.cs:116` | a forced duck does not duck |
| `PlayerSetAllowAutoMovement` | `TriggerPlayerMovement.cs:119` | auto-movement is never suppressed |
| `PlayerSetButtonsDisabled` | `PlayerSpeedMod.cs:154` | `player_speedmod` disables no buttons |
| `PlayerSetLaggedMovement` | `PlayerSpeedMod.cs:117` | `player_speedmod` changes no speed |
| `PlayerSetMovementConstraint` | `PointPlayerMoveConstraint.cs:95` | the constraint constrains nothing |
| `PlayerHolsterActiveWeapon` | `PlayerSpeedMod.cs:126` | the weapon stays out |
| `PlayerDeployLastWeapon` | `PlayerSpeedMod.cs:140` | it never comes back |
| `PlayerGetActiveWeaponEntity` | `TriggerCamera.cs:383`, `WeaponInventory.cs:38` | no weapon is hidden; the inventory reads empty |
| `PlayerGetWeaponEntity` | `WeaponInventory.cs:42` | every inventory slot reads empty |
| `PlayerGetViewModelEntity` | `Commands.cs:74` | there is no viewmodel to act on |
| `GetPlayerActiveWeapon` | `PlayerSpeedMod.cs:138` | decorated for hierarchy players; listed for completeness |

**None of these is an ABI retirement.** A slot in bucket (c) has a caller that
wants an answer, and retiring it would record a live requirement as finished.
They are a defect ledger, and `sourcesharp/test/entity_shell_inert_slots_test.cpp`
pins the set so that a new stub, or a new caller of one, is loud rather than
silent.

Each row's fix is a port, not a rewiring: the state these slots read lived on
`CBasePlayer`, which left the build in 13c. Armour and the button modifiers want
managed player state; the weapon four want the Arsenal to answer them the way it
already answers `GiveItem` and `GiveAmmo`, which is a decorator method each.

---

## 3. Rulings this lane's own measurement overturned

### `cs_standalone_spawn` is not dead — `tools/live-gate` drives it

Lane S1 ruled the four dev `CON_COMMAND`s in `shell_standalone.cpp:1150-1311`
deletable, "managed twins `ent_create`, `cs_sv_parent`, `cs_sv_hierarchy`". A
console command is reached BY NAME and no link-line census sees it:

```
tools/reached-by-name        # tools/ docs/ cfg/ managed/ gtest, per name
cs_standalone_spawn      8      0      0      0      0
```

The eight are `tools/live-gate`, `tools/6c-gate-probe`, `tools/4d-record-bones`,
`tools/4d-replay-bones`, `tools/6c-record-bones`, `tools/6c-replay-bones`,
`tools/6d-record-layers` and `tools/6d-replay-layers` — the integrator's own
gate among them. Deleting it breaks the gate that would have caught the
deletion. It stays until those eight are re-pointed, which is a separate change
with its own evidence.

The other three of that group are clear: `cs_standalone_parent` (1 managed
mention, no driver), `cs_standalone_mapspawn` and `cs_sv_viewmodel_report`
(0 anywhere).

### `collision_group_names.h` stays native

S1 ruled it MOVE, to a `CollisionGroup` enum on `ScriptedEntity.cs` carrying
"the engine's `COLLISION_GROUP_*` numbers so the crossing carries the engine
value". The header's own block already answers that and S1 did not address the
answer: `Collision_Group_t` is a thirty-entry **positional** enumeration in
`public/const.h`, most of whose members this mod has no name for. Mirroring its
numbers into C# means an engine-side insertion silently renumbers the managed
enum, with nothing to say so — and the point of the five-row translation is that
SourceSharp's names are *not* positional. Keeping it costs five rows and two
inline lookups; being wrong about it costs a collision group written as one
thing and read back as another.

### `shell_shot_path.h`'s dangling declarations stay (§4a item 8)

The three `EntityShot_*` functions are declared and not defined, and
`game/server/anyentity.h:359` still has the inline that calls the first. That is
deliberate: `removal_set_tail_test.cpp:1064` records it, and the next
translation unit that calls one gets a link error naming the symbol, which is
the loud failure a standing declaration is for.

It cannot be removed yet because `plan_shim.md` §10's open question — *do
bullets have to hit triggers?* — is unsettled, and if the answer is yes the
three come back **with** callers. Removing the tripwire before the question is
answered destroys the thing that would make the answer cheap.

The 0-caller fact: `game/shared/baseentity_shared.cpp`, the only caller, has no
`build ... : cxx` edge in `src/_vpc_/ninja/sdk_csharp_debug.ninja`.

### §4a item 10 is already closed

`g_nSourceSharpStateChangedCalls` does not exist in the tree:
`grep -rn g_nSourceSharpStateChangedCalls src` returns two **comment** lines
(`shell_standalone_base.cpp:569`, `cpp_alias_shard_t4_test.cpp:1379`) and no
declaration, definition or write. An earlier lane removed it and left the
argument in place at the site, which is the right residue. Nothing to do, and
nothing for lane 3a to collide with.

---

## 4. Three headers dead in the live graph, and why only one of them left

`notify_gate.h`, `push_blocker_rule.h` and `shell_parent_frame.h` reached **no
translation unit on either link line**. `tools/shim-census who-imports` calls
that question vacuous for a header — a header defines no symbols of its own — so
the proof is an include closure taken UPWARD from the header to the objects the
graph compiles:

```
tools/header-consumers src/sourcesharp/game/server/notify_gate.h ...
== notify_gate.h          server 0 TU(s)   client 0 TU(s)
== push_blocker_rule.h    server 0 TU(s)   client 0 TU(s)
== shell_parent_frame.h   server 0 TU(s)   client 0 TU(s)
```

None of the three is named in any `.vpc`, and `tools/reached-by-name` found no
`tools/`, `docs/`, `cfg/` or managed reference to any of them.

**Only `shell_parent_frame.h` was removed**, because it is the only one of the
three whose sole includer was its own gtest.

Its rule has a managed home that is tested: `ParentFrameRule` in
`SourceSharp.Abi/Hierarchy.cs`, pinned by `HierarchyTests` against the same
`sourcesharp/test/parent_frame_cases.txt` the native test read. The native rule
is genuinely gone and not merely unreferenced —
`grep -rn SourceSharp_ParentFrameKind src` returns **two comment lines and no
code** — so deleting the header does not leave a live native arm without its
agreement partner. The cases file stays; only the native reader goes. The prose
in `Hierarchy.cs` and `HierarchyTests.cs` that described the pair as two
declarations held equal was corrected in the same commit: the fixture is now the
only statement of the rule that is not the code, which is a stronger claim on it,
not a weaker one.

**`notify_gate.h` and `push_blocker_rule.h` stay, and the blocker is not
evidence.** Each is `#include`d by a stock file that is out of the build:
`game/server/entitylist.cpp:15` and `game/server/pushentity.h:18`. Deleting the
header without editing those leaves a dangling include in a file still in the
tree; editing them is a stock-file change, and their text is read verbatim with
precise assertions by eight gtests (`entity_visibility_test`,
`cpp_alias_shard_t4_test`, `entity_list_sweep_test`, `sdk_player_cast_test`,
`world_release_test`, `header_alias_test`, `entity_base_husks_test`,
`server_systems_underive_test`) — `cpp_alias_shard_t4_test.cpp:534,846,915,964`
alone pins four separate regions of `entitylist.cpp` by content.

So these two go out **with** the stock files that include them, not before, and
the S1 DELETE ruling is right about the evidence and wrong about the unit of
work. `game/public/igameentity.h:185,234` also cite both headers by name as the
place two rules are written down, which is a third edit the same change owns.

## 5. `shellless_find.h` is dead, and its deletion is wider than one header

The shell-less `ent_fire` path is unreachable, and this lane leaves it in place
rather than half-removing it.

`standalone_relays.cpp` is the only translation unit on the graph that includes
`shellless_find.h`, and it is itself live — 13 objects import its
`StandaloneEntity_*` symbols. But **none of those 13 imports any `ShellLess`
symbol**, and the two entry points that would drive the path from outside,
`ShellLessEntity_AnythingAnswersTo` (`game/server/baseentity.cpp:5891`) and
`ShellLessEntity_LiveCount` (`game/server/entitylist.cpp:2389`), are in files
with no `build` edge in the graph.

Removing it means editing `standalone_relays.{h,cpp}` and `shell_dispatch.cpp`
(this lane's), `game/server/baseentity.cpp` and `game/server/entitylist.cpp`
(neither compiled, neither this lane's), and re-pointing
`entity_visibility_test.cpp`, which asserts both that
`ShellLessEntity_LiveCount` is defined and that `entitylist.cpp` calls it. That
is a set-cover across three owners, and it is recorded here rather than
attempted piecemeal.

---

## 6. The five headers' prose, and why it did not move to this file

`plan_shim.md` §5 3b says `shell_standalone.h` (1,506), `shell_resolve.h` (277),
`standalone_relays.h` (258), `toucher.h` (163) and `entity_factory_table.h`
(218) "carry ≈2,300 lines for ≈150 declarations" and that the arguments should
move here.

Re-measured, the ratio is real but smaller, and the move is not free:

```
comment lines 1,082 + 202 + 191 + 114 + 120 = 1,709
non-comment, non-blank  278 +  47 +  41 +  35 +  72 =   473
```

So it is 1,709 lines of prose over 473 of code, not 2,300 over 150.

**Twenty-seven gtests read these headers, and several read their TEXT rather
than including them** — `animation_policy_test.cpp:625,730`,
`collision_origin_identity_test.cpp:161`, `entity_op_test.cpp:95,652`,
`gib_evicted_test.cpp:267`, `hierarchy_managed_test.cpp:474`,
`impact_subject_test.cpp:310`, `player_disconnect_removal_test.cpp:61`,
`shot_path_solidity_test.cpp:312`, `standalone_handle_ban_test.cpp:153`,
`standalone_layout_test.cpp:102,270,308`, `close_rulings_test.cpp:379`,
`permanent_decal_subject_test.cpp:300`. Some strip comments first and some do
not, and which is which has to be settled per test before a line of prose moves.

That is the same shape as the `.vpc` prose rule — a comment in one of those
files is an input to a test, not decoration — so the move is a step of its own
with a per-test audit in front of it, not a mechanical relocation. It is not
done here, and this section is the reason.

---

## 7. The player's console commands, and the two crossings that unblocked them

`plan_shim.md` §5 3b's first bullet: `ClientCommand`, its `CON_COMMAND`s and
`Route` — "≈460 lines" — become `[ConCommand]` attributes, with "one new
crossing (a view-angle snap with `fixangle`)".

Re-measured, it was **260 lines** of `shell_standalone_player.cpp` (2,831 →
2,571, then 2,576 with the replacement prose) and **two** new crossings, not one.

### Why two

`setang` was the one the plan named: `CPlayerState::fixangle` is engine memory
the engine reads back through `IServerGameClients::GetPlayerState`, and it is
the half that makes the client *adopt* `v_angle` instead of continuing to
predict from its own. A `v_angle` write without it is overwritten by the next
usercmd's stale viewangles before anyone sees the change — the snap works for
one frame and undoes itself.

`noclip` needed a second one, and the plan missed it. A move type can already be
changed from managed code — `MoveProcessMovement` takes a `SourceSharpMoveState`
whose `nMoveType` `shell_movement.cpp:1321` writes back after the call — but
that is a movement-frame path, and a console command happens *between* frames.
A request parked for the next movement frame does nothing at all for a player
standing still, which is exactly when `noclip` is typed.

```
PlayerSnapEyeAngles( uint32_t hPlayer, const float *pPitchYawRoll )
PlayerSetMoveType  ( uint32_t hPlayer, int32_t nMoveType ) -> the PREVIOUS move type
```

Both appended at the tail of `SourceSharpEngineApi` on both sides, server-only
in the fill block, bodies in `shell_entityapi.cpp` beside the other player
slots. `SOURCESHARP_ABI_VERSION` is not bumped; the integrator bumps once.

`PlayerSetMoveType` answers the *previous* move type so that a toggle is one
crossing rather than a read and a write with a frame between them — on the very
state that decides what a frame of movement does.

### What moved, and what did not

Fifteen registrations and a ~120-line verb table went to
`SourceSharp.Server/PlayerCheatCommands.cs`: `kill`, `explode`, `ss_kill`,
`hurtme`, `ss_hurtme`, `setpos`, `setpos_exact`, `ss_setpos`, `setang`,
`setang_exact`, `noclip`, `ss_noclip`, `notarget`, `god`, `buddha`.

Three things stayed native, each for a stated reason:

| kept | why |
|---|---|
| `ss_ground` | prints native movement state with no managed mirror — `MvGetFlags`, `MvGetBaseVelocity`, `MvLocal().m_flFallVelocity`, the ground entity's hierarchy — and the value of the instrument is that it reads them directly rather than being told what managed code believes. `tools/` names it. |
| `use` + `StandalonePlayer_RouteVerbatim` | `use` IS `ss_use`, one selection rule under two spellings, answered in `ServerModule.OnClientCommand`. A managed `[ConCommand]` would be a second implementation of a rule that already has one. |
| `StandalonePlayer_CommandClient` | who typed it. Two callers: `ss_ground`, and `SourceSharp_GetCommandClientHandle`, which is how every managed `[ConCommand]` gets the same fact. |

`StandalonePlayer_ClientCommand` keeps its signature — `shell_gameclients.cpp`
is another lane's file and calls it — and its body is now the managed first
refusal and nothing else. It is reached only by words that are *not* registered
`ConCommand`s, because a registered name is dispatched by the engine and never
arrives there; so `ss_use`, `ss_slot`, `invnext`, `invprev` and `lastinv` still
come through it, and everything the verb table used to handle now takes the
other door.

### Two behaviour changes, deliberate, each with a fact

**A coordinate or an amount that is not a number is refused.** `atof` answers 0
for anything unparseable, so `hurtme x` applied no damage and said nothing, and
`setpos 100 x 300` moved the player to y=0 and reported success. A typo is the
likeliest thing to go wrong at this console and it was the one thing that
produced no complaint.

**`notarget` and `noclip` tell a failed resolve apart from a state.** No player
is ever in `MoveType.None`, so that answer from the crossing means "this handle
is not a hierarchy player" — reporting `noclip ON` over a handle nothing
happened to is the shape of silence the whole port removes.

### The guard tests that caught the move, and what each needed

Four managed facts went red on this change, and all four were right to:

- `DevCommandsTests.EveryDevelopmentCommandIsActuallyRegistered` — the test's
  container had no `IEngineDamage` or `IEnginePlayerMovement`, so eleven
  commands vanished from the registry **in silence**, which is exactly the
  failure that fact exists to catch. Added by name, as `ent_fire`'s and
  `drawline`'s dependencies were before them.
- `DevCommandsTests.NothingIsAnsweredThatWasNotOnTheList` — a fourth catalogue,
  `ArrivedFromThePlayerShell`. `ArrivedWithAnEvictedFile` is about commands that
  moved when a *stock* file left the build and `WrittenByThisPort` about
  commands stock never had; these moved when a *shim* file's policy left C++,
  which is neither. Verdict A's count of 43 is a fact about stock and is not
  touched.
- `DevCommandsTests.EveryCheatCommandIsRefusedWithCheatsOff` — `kill`,
  `explode` and `ss_kill` are not cheat-gated, because stock's were not. The
  exceptions are **named**, not derived from the `RequiresCheats` property the
  commands set: reading that would make the test agree with whatever the
  commands happen to say, so a command that silently turned its gate off would
  pass.
- `MoveTypePredictionParityTests.TheSameScanDoesFindTheMoveTypesThisBuildDoesSet`
  — its subject was the native noclip line
  `MvSetMoveType( bNoclip ? MOVETYPE_NOCLIP : MOVETYPE_WALK )`, the last line in
  the compiled set carrying **both** halves of its sibling's predicate. With
  `noclip` managed the move type is an argument, so the conjunction went to 0.
  Lowering the floor to 0 would have been the check that cannot fail; instead
  each half is now proved on its own, which is strictly stronger — a parser
  blind to either half would have given the sibling a clean bill for the wrong
  reason, and requiring one line with both only ever tested them together.

---

## 9. The twelve rule headers, ruled one at a time

`plan_shim.md` §5 3b's second bullet lists twelve headers, "each a decision with
a managed home named in the table and an existing crossing to ride". The task
brief puts it more strongly: each "claims to be arithmetic decided without an
engine — which is exactly what C# gives for free".

**Five of the twelve do not claim that, and the rulings table already says so.**
Re-measured at `eaf6ae7d3`, with the single native consumer of each named:

| header | lines | native consumer | verdict | why |
|---|---|---|---|---|
| `standalone_names.h` | 120 → 90 | `shell_standalone_base.cpp` | **DONE** | Two of its three functions had no caller left and went with the code and the eight assertions that pinned them (lane 3c, commit 3/N). The survivor exists to AGREE with `NamesMatch` in `baseentity.cpp`, which is why it is tested rather than read. |
| `collision_group_names.h` | 116 | `shell_entityapi.cpp` | **KEEP** — overturns S1 | See §3. `Collision_Group_t` is a thirty-entry POSITIONAL enumeration in `public/const.h`; mirroring its numbers into C# lets an engine-side insertion silently renumber the managed enum. |
| `standalone_collision_rules.h` | 83 | `shell_standalone_base.cpp` | **KEEP** (S1 agrees) | `IPhysicsObject::RecheckCollisionFilter` must be called natively when solidity or collision group changes — vphysics caches `ShouldCollide` per pair. |
| `standalone_handle_ban.h` | 79 | *(none — it is a compile-time ban)* | **KEEP** (S1 agrees) | Declares `CHandle<CStandaloneEntity>` incomplete so a typed handle over the shell cannot be written. No runtime code to move. |
| `standalone_netserial.h` | 87 | `shell_standalone_base.cpp`, `shell_entityslots.cpp` | **KEEP** (S1 agrees) | `edict_t::m_NetworkSerialNumber` is engine memory `SV_PackEntity` keys its `ChangeFrameList` by. |
| `shell_velocity_home.h` | 0 | — | **DELETED** 2026-10-12 (plan_physics_bones_perf C2) | Its last caller, StandaloneEntity_Velocity, reads the velocity managed holds on the entity (PhysicsFrame hands the object's over; EntityPhysicsMotion the member's); the pick is managed's. |
| `shell_model_name.h` | 48 | `shell_standalone_base.cpp`, `shell_physicsobjects.cpp` | **KEEP** (S1 agrees) | Picks which ENGINE call `SetModelName` makes. The rule is one line; the thing it selects between is not managed-reachable. |
| `scripted_kinds.h` | 252 | `shell_scripted.cpp` | **MOVE, not done here** | The strongest remaining candidate and genuinely engine-free: seven kinds to six booleans-and-integers, a pure lookup. **Its cost is an ABI change, not an append**: `RegisterScriptedEntity` takes a kind ID, and moving the table means the six values crossing instead — either a signature change to a live slot (which other lanes are editing) or a new `RegisterScriptedEntityEx` beside it. That is a serialised decision, not a lane's. |
| `standalone_movetype.h` | 66 | `shell_worldquery.cpp` | **MOVE, blocked** | Its only native caller is `shell_worldquery.cpp:270` (`EntityGetState`), which is not this lane's file. The move is a one-line precedence rule plus a state-record field; it belongs with whoever owns that file. |
| `standalone_basevelocity.h` | 75 | `shell_worldquery.cpp` | **MOVE, blocked** | Same file, same reason (`:649`, `EntityAddBaseVelocity`). |
| `break_piece_offset.h` | 77 | `shell_breakpieces.cpp` | **MOVE, blocked** | S3's ruling and S3's file. |
| `standalone_model_bounds.h` | 55 | `shell_standalone_base.cpp` | **MOVE, available** | The one unblocked MOVE left: its caller is this lane's, and the ride is an `adoptBounds` argument on the existing managed `SetModel` crossing. Not taken here for want of lane time, and recorded rather than silently dropped. |

**The shape of the finding.** Seven of the twelve are not arithmetic at all —
they pick an engine call, an address, or a compile-time property — and the
rulings table said so for six of them before this lane started. Of the five that
are arithmetic, one is done, one is blocked on an ABI decision that is not one
lane's to take, two are blocked on another lane's file, and one is available and
left.

So the brief's framing — twelve headers that are "exactly what C# gives for
free" — holds for five, and the reason the other seven are native is written in
each of them. That is worth recording precisely, because "a header with no
engine include must be portable" is the inference that would move
`standalone_netserial.h`, whose whole subject is a field in `edict_t`.
