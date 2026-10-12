# Physics stock-behaviour audit: what still runs for managed entities

Read-only audit, 2026-10-01. Repo `../ss-novalve` at `d179270`
(`git log --oneline -1`; working tree carried five unrelated `M` files).
Stock = `git -C ../source-sdk-2013 show 22288b919617be6c8ca3cefd7cca979cbb39a88c:<path>`;
stock line numbers below are of those blobs. "ours" paths are relative to the
ss-novalve checkout. No build, no game run: every number here is a line
number or a grep count from this session.

Ruling applied: a stock behaviour that stopped running because its
`PhysicsOwner_t::pBase` arm is dead is PORTED for managed entities, never
deleted.

## The two facts everything else hangs on

1. **`pBase` is NULL for every physics object this build makes.**
   `src/shared/native/physicsowner.h:487-505`: a tagged (handle) game-data word
   returns before `owner.pBase` is assigned; the world stores NULL
   (`physics.cpp:730-734`); nothing on the server calls `SetGameData` with a raw
   pointer (`grep -rn SetGameData src/server/native src/shared/native` → only
   three `SetGameData( NULL )` sites). So every `PhysicsOwnerOf(..).pBase` arm
   in `src/server/native/physics.cpp` is dead by construction (34 mentions,
   `grep -c pBase`).
2. **`PhysicsOwner_t::GetMoveType()` is a constant `MOVETYPE_NONE`**
   (`physicsowner.h:273-276`). It feeds `SourceSharp_FillCollisionSide`
   (`physics_shell.h:1893`) and the collision record's `nMoveType0/1`
   (`physics_shell.h:2116-2126`). The managed gate
   `PhysicsCollisionRules.Responds` (`src/server/managed/PhysicsCollisionRules.cs:210`)
   returns false unless one side is `MoveType.VPhysics` (= 6,
   `src/abi/WorldQuery.cs:204`). **Therefore no managed collision has ever
   passed `Responds`, and the shake/dust that `PhysicsCollisionResponse`
   emits (`PhysicsCollisionResponse.cs:146-177`) have never fired.** The xUnit
   facts that prove the rules feed `MoveType0 = VPhysics` by hand
   (`src/test/managed/PhysicsCollisionTests.cs:181`); the live-gate log has no
   `cs_sv_physics_events` readout (`grep -n "physics events\|responded" tools/.live-gate.log` → 0 lines),
   so "responded > 0" has never been measured. The engine block already has a
   `MoveType` field (`src/shared/native/engine_block_generated.h:113-114`,
   zeroed once at `shell_standalone_base.cpp:741` and never written), and
   `shell_worldquery.cpp:252-290` already answers MOVETYPE_VPHYSICS for a
   standalone entity with a physics object, so the fix is one function.

Circular evidence, flagged as asked:

- `physics.cpp:1405-1418` ("19,302 interpenetrating pairs reached
  ShouldSolvePenetration and 0 got past its second line") measures
  `PhysicsOwnerOf(pObj0).pBase && PhysicsOwnerOf(pObj1).pBase`
  (`physics.cpp:1460`), which is false for every object by fact 1. It proves
  the guard is dead, not that penetration handling is unneeded. The refusal
  text itself says the premise ("something now gives an object a real
  .pBase") would change the answer — it is the ruling that changes it.
- `physics.cpp:1234-1237` (ShouldFreezeObject): "the stock code decides that
  an entity with no pBase is not crushed, and this shard keeps that decision".
  Stock makes no such decision; `pEntity` is a bare cast that is never NULL
  for a stock prop (stock `physics.cpp:665-683`).
- `physics.cpp:2215-2229` (contact touch): "after the flip no surviving path
  gives an SDK entity an object", counted by a refusal that trips only on a
  non-NULL `pBase` — same shape.
- `docs/physics-move-scope.md:466-474`: "every arm behind a
  `PhysicsOwnerOf( ... ).pBase` resolve is unreachable ... That is a DELETION
  of unreachable stock arms, not a port". Unreachable for the wrong reason.

## Summary table

| # | Case | Stock | Status | Port shape | Test |
|---|------|-------|--------|------------|------|
| A1 | ShouldCollide core table (self, mutually-static, wheel, shadow pair, solid types, not-solid/trigger, group matrix, contents, pair hash) | physics.cpp:454-626 | WORKS | none (rule table, `physics_shell.h:1074-1198`) | existing physics_bridge_test |
| A2 | ShouldCollide: don't collide with your owner | :514-519 | MISSING (absent) | block read of `OwnerHandle` → pair fact `bOwnerPair`; mid-step, no crossing | gtest Evaluate(bOwnerPair)=0; live: prop with owner=player passes through player shadow |
| A3 | ShouldCollide: move-parent/sibling rules | :521-541, :557-573 | MISSING (absent), low | parent handle on block → pair fact; mid-step | gtest |
| A4 | ShouldCollide: AI mover (MOVETYPE_PUSH) rules | :554-579 | N/A now (no managed mover owns a VPhysics shadow; `InitPhysics` callers: FuncPushable, FuncPhysbox, SimplePhysics*, PhysConvert, TriggerVPhysicsMotion) — table arm exists but fed MOVETYPE_NONE | fix C2 (real move type); becomes live when movers get shadows | gtest exists |
| A5 | ForceVPhysicsCollide (func_clip_vphysics) | :511; bmodels.cpp:1456 | WORKS by accident (`FuncClipVphysics.cs:40-50` relies on the `GetSolidFlags()==0` neutral) | if A6 ports real solid flags, add a block bit `ForceVPhysicsCollide` read by `ForcesCollisionWith` | FuncClipVphysicsTests + gtest |
| A6 | ShouldCollide sees real SOLID type / FSOLID flags / player MASK_PLAYERSOLID | :543-617; player.cpp:3451 | PARTIAL (neutrals `physicsowner.h:332-349`; `SetNotSolid` exists in ABI `EngineAreas.Entities.cs:398` but the solver never hears it) | `PhysicsOwner_t::GetSolid/GetSolidFlags` read `pServer->GetCollideable()`; player answers MASK_PLAYERSOLID; mid-step block read | gtest FillCollisionSide of a not-solid managed object; live: not-solid managed prop falls through props |
| B1 | VPhysicsUpdate transform copy | baseentity_shared.cpp:1131-1176 | WORKS | — | existing |
| B2 | PhysicsTouchTriggers after the solver moves it | baseentity_shared.cpp:1183 | WORKS (`shell_standalone_base.cpp:1170,1698-1722`) | — | existing |
| B3 | Interactive debris → debris on sleep | baseentity_shared.cpp:1176-1180 | MISSING (absent) | managed, on ring `Sleep`: group InteractiveDebris → `SetCollisionGroup(Debris)`; post-step | xUnit: Sleep event changes group; break: skip the call |
| B4 | prop_physics: `m_bAwake` net, OnAwakened (START_ASLEEP), OnOutOfWorld, thrown flag clear | props.cpp:2912-2935 | MISSING (absent) (`PropPhysics.cs` has no OnAwakened/OnOutOfWorld) | managed on ring `Wake`/`Sleep` (already recorded `physics.cpp:1282,1326`); post-step | xUnit: Wake event fires OnAwakened once; live: start-asleep crate shot → output |
| B5 | Player stress (crush) damage from VPhysicsUpdate | player.cpp:8260-8268; basecombatcharacter.cpp:3043-3098; physics_impact_damage.cpp:503-700 | MISSING (absent) (no `CalculateObjectStress` in tree; player shadow isn't `m_pPhysics`, `shell_standalone_base.cpp:2527-2532`) | native carries out `CalculateObjectStress` (friction snapshot, needs IsPlayer/owner/moveable — all on PhysicsOwner_t) → record a `Stress` ring kind per active shadow per step (receivedStress, hasNonStatic, hasLargeObject) → managed SsPlayer decides 200 dmg rule; post-step | gtest stress over a fake snapshot; xUnit stress rule; live: 500 kg crate on player → crush damage |
| B6 | VPhysicsUpdatePusher (blocked pusher reconciliation) | baseentity.cpp:2900-3022 | N/A (no managed mover has a shadow) | with A4 | — |
| B7 | Player VPhysicsShadowUpdate: solver pushes player, penetration list, frozen recovery, stuck check | player.cpp:8286-8482 | MISSING (absent); shadow list refused `physics.cpp:493-509,1766-1779`; divergence stated `shell_standalone_player.cpp:1100-1115` | extend `ShadowProbe` state with GetShadowPosition/GetShadowVelocity/WasFrozen/PENETRATING; `PlayerShadow.Step` decides SetAbsOrigin/velocity impulse (managed trace exists); outbound from the managed command loop, no inbound crossing | xUnit on the plan (dist/vel thresholds, frozen arm); live: crate pushed into standing player moves them |
| C1 | PostCollision record + drain | — | WORKS (`physics.cpp:1871-1875,2079`) | — | existing |
| C2 | Collision record move type | baseentity.cpp:3124-3126 | MISSING (dead neutral): see fact 2 | `PhysicsOwner_t::GetMoveType` = VPHYSICS when `pGame` has an object, player's own `m_MoveType`; no ABI | gtest: FillCollisionSide of a managed prop reads 6; xUnit unchanged; live: `cs_sv_physics_events` responded > 0 (currently 0 by construction) |
| C3 | Impact sound | physics.cpp:2577-2592; baseentity.cpp:3136-3143; vphysics_sound batch | PARTIAL (rules computed, `SoundsNotPlayed` counted; native batch player exists `physics.cpp:609-650`, never fed) | one slot `PhysicsImpactSound(entity, channel, surf, surfHit, volume, speed)` → `AddImpactSound` (`ss_physics_sound.h:63`); batch emits from origin so no edict needed; post-step outbound | xUnit: fake world receives sound; gtest: queued entry plays; break: drop the slot call |
| C4 | Screen shake | physics.cpp:2606-2623 | blocked by C2 | — | existing xUnit; live after C2 |
| C5 | Dust | physics.cpp:2644-2686 | blocked by C2 | — | same |
| C6 | Impact damage to props/breakables/surfaces/physbox (CalculateDefaultPhysicsDamage → PhysCallbackDamage) | props.cpp:3026-3058; func_break.cpp:769-820; func_breakablesurf.cpp:1233-1293; physobj.cpp:774-797; physics_impact_damage.cpp:249-485 | MISSING (absent): dispatch dead `physics.cpp:1887-1889`; `PhysCallbackDamage` deleted and pinned absent by `src/test/native/physics_move_scope_test.cpp:509-527`; `UpdateDamageEvents` drains an always-empty list (no `AddDamageEvent` caller) | extend the collision record with pre/post linear and pre angular velocity per side + inertia + "is shadow" flag; managed ports `CalculatePhysicsImpactDamage` (pure) and applies via existing `OnDamaged`; post-step | xUnit: table port against stock numbers; live: crate thrown at glass breaks it |
| C7 | Restore inflictor velocity when the victim dies/goes non-solid | physics.cpp:2156-2254 | MISSING (dead stub `iEntBits=0`, `physics.cpp:2372-2381`) | managed after C6: if OnDamaged killed/removed → `SetVelocity` (ABI `EngineAreas.Entities.cs:198`) with the blended pre-velocity; post-step | xUnit blend math (massRatio remap) |
| C8 | forcetoenablemotion / ComputeEnablingImpulse, PREVENT_PLAYER_TOUCH_ENABLE | props.cpp:2947-3013; physobj.cpp:778-797 | MISSING (absent) | managed on PostCollision: speed×otherMass ≥ threshold → `EnableMotion` + `ApplyForceOffset` (needs other side's pre-velocity: C6 record) | xUnit; live: motion-disabled crate knocked loose |
| C9 | First-collision-after-launch, physics attacker, impale, NPC interaction, thrown-by-player | props.cpp:458-647, 3015-3023, 3060-3083 | N/A (physcannon/NPC, HL2) | — | — |
| C10 | VPhysicsShadowCollision: player hurt by moving props | basecombatcharacter.cpp:3109-3200; player table physics_impact_damage.cpp:19-48 | MISSING (absent) (`physics.cpp:1895-1924`) | same record as C6 plus `nFlags` bit "side is CALLBACK_SHADOW_COLLISION"; managed SsPlayer applies player table ×`m_impactEnergyScale`; post-step | xUnit player table; live: crate dropped on player hurts |
| C11 | PreCollision player-held velocity clamp | physics.cpp:1823-1857 | WORKS (`physics.cpp:1817-1847`) but no holder sets PLAYER_HELD in this mod | — | — |
| C12 | Shadow-vs-shadow: isCollision=false | physics.cpp:1902-1905 | PARTIAL (record is made before the arm; after C2 both sides are non-VPHYSICS so `Responds` rejects) | none | xUnit exists (NeitherSideSimulated…) |
| D1 | Friction effect (dust/sparks at contact) | physics.cpp:1966; physics_fx | WORKS (`physics.cpp:1999`) | — | — |
| D2 | Friction scrape sound loop + 0.5 s MP throttle + 0.1 s shutdown | baseentity.cpp:3161-3164; physics.cpp:1938-1967, 2083-2096, 2688-2737 | MISSING (dead arm; server `PhysFrictionSound` is `#ifdef CLIENT_DLL`, `physics_shared.cpp:1288`) | ring `Friction` already recorded (`physics.cpp:1994-1997`); managed policy (energy≥75, 'X', volume curve, throttles) pure; one generic slot for a per-entity looping sound patch (create/volume/pitch/destroy over `ss::SoundController`) | xUnit policy; gtest patch lifecycle; live: crate sliding scrapes |
| E1 | Contact StartTouch/EndTouch between physics objects → entity Touch | physics.cpp:2427-2503, 2099-2116; physics_main_shared.cpp:1060-1079 | MISSING (dead arm + refusal `physics.cpp:2238-2258,2273-2290`); ring records, managed counts only | zero ABI: buffer `(obj0,obj1)` in `m_touchEvents`, after the step call `owner0.StartTouch(owner1)` and `owner1.StartTouch(owner0)` (`physicsowner.h:604-616` → `g_SolverHeld`, `shell_standalone_base.cpp:1571-1592` → EntityDirectory diff); EndTouch contact count keys on `Key()`; post-step | gtest dispatch via fake owners; xUnit directory raises Start for a solver pair; live: thrown crate picks up an item / presses func_weight_button |
| E2 | Player Touch(prop) → touchedPhysObject | player.cpp:4762-4776 | WORKS (`shell_standalone_player.cpp:285-297`; `PlayerShadow.cs:361`) | — | existing |
| E3 | Physics trigger enter/leave | physics.cpp:2506-2548 | WORKS (`physics.cpp:2732-2810` → `PhysicsOwner_t::StartTouch`) | — | existing; `GetTriggerEvent` pBase record N/A (managed attaches by handle, `TriggerVPhysicsMotion.cs:153,181`) |
| F1 | Wake/Sleep ring events | physics.cpp:737-753 | WORKS (recorded), consumed by nothing | — | — |
| F2 | VPHYSICSWATCHER: phys_constraint wakes its other object | physconstraint.cpp:795-843, 1097-1148 | MISSING (dead arm `physics.cpp:1316-1338`; `PhysConstraint.cs` has no wake handling) | managed PhysConstraint consumes `Wake` for its two handles → `Physics.Wake(other)`; post-step | xUnit; live: wake one of two constrained crates → both awake |
| G | ShouldFreezeObject: debris crushed/removed when frozen under a heavier mover | physics.cpp:657-716, 628-655 | MISSING (dead arm; `physics.cpp:1213-1240` returns true) | native carries out `FindMaxContact` (deleted; restore) inside the callback and queues a DMG_CRUSH event through the existing `AddDamageEvent`/`UpdateDamageEvents` → `StandaloneEntity_TakeDamage`; managed `OnDamaged` decides (gib removal = SsGib); return value unchanged; entity solver N/A | gtest: fake snapshot queues crush; live: heavy crate on gib pile |
| H | ShouldSolvePenetration: penetrate list, FVPHYSICS_PENETRATING, 3 s disable → sleep, error boxes | physics.cpp:967-1057, 837-947, 800-820, 1920-1936 | MISSING (dead arm `physics.cpp:1424-1473`; evidence circular, see above). Readers of PENETRATING: `PhysicsCollisionRules.cs:271`, stress (B5), shadow update (B7) | native carries out a handle-keyed penetrate ledger (needs only IsPlayer, group, IsStatic, GetShadowController, Key — all on PhysicsOwner_t), `PhysSetGameFlags`, restore `PhysForceEntityToSleep` from `physics_debug.cpp:863`; the one decision (3 s, players/shadows exempt) as rule-table constants; return false on TRYDISABLE is mid-step but native-decidable; NPC/entity solver N/A; optional `Penetrating` ring kind for managed | gtest ledger state machine; live: two overlapping crates sleep after 3 s with flag set then cleared |
| I1 | Fluid splash, drag, damping, 0.5 s throttle | physics.cpp:1060-1128 | WORKS (`physics.cpp:1527-1664`, handle-keyed throttle 2132-2151) | — | existing |
| I2 | EFL_TOUCHING_FLUID / water-touch events / IsFloating (3× impact energy) / water type for slime splash | baseentity.cpp:4274-4300, 6863; physics_impact_damage.cpp:373-379 | MISSING (refused `physics.cpp:1491-1509`); ring FluidStart/End recorded, counted only | managed per-entity in-fluid set from the two ring kinds; feeds C6's energy scale and `GetWaterType`; post-step | xUnit: FluidStart then FluidEnd toggles; break: drop the End arm |
| J | Damage queue plumbing (AddDamageEvent/UpdateDamageEvents) | physics.cpp:2156-2331 | PARTIAL (delivers to `StandaloneEntity_TakeDamage` `physics.cpp:2380`, no producer) | producer restored by G/H; managed-side damage (C6/C10) does not need it | — |
| K | PhysCallbackImpulse/SetVelocity post-sim queue | physics.cpp:2743-2753 | N/A as mechanism (managed applies forces after the drain; `g_PostSimulationQueue` still exists `physics.cpp:148,2040`) | — | — |
| L | Break sound | props.cpp:1673; func_breakablesurf.cpp:612; physics.cpp:2594-2600 | MISSING (absent): `PhysBreakSound` deleted, `m_breakSounds` never filled, `PropPhysics.Break` fires OnBreak only (`PropPhysics.cs:416`) | slot `PhysicsBreakSound(origin, surfaceProps)` → `AddBreakSound` (`ss_physics_sound.h:109`) → `SourceSharp_PlayBreakSounds` (`physics.cpp:652-675`); or widen `MoveSurface` with the sound names and `EmitSoundAtPosition` | xUnit fake; live: break a crate, hear it |
| M | ShouldFreezeContacts warning | physics.cpp:718-733 | WORKS | — | — |
| N | AddDamageInflictor multi-object chain | physics.cpp:2368-2412 | N/A (one object per managed entity) | — | — |
| O | Server ragdoll VPhysicsUpdate/Collision; vehicles; warp effect | physics_prop_ragdoll.cpp; vehicle_base.cpp; HL2_EPISODIC | N/A (`SsRagdoll.cs` has no physics object; no vehicles) | — | — |

Counts: WORKS 11 (A1, B1, B2, C1, C11, D1, E2, E3, F1, I1, M) — note F1 is
"recorded, nothing consumes"; PARTIAL 5 (A6, C3, C12, J, plus C4/C5 blocked by
C2); MISSING (dead arm) 7 (C2, D2, E1, F2, G, H, I2); MISSING (absent) 10 (A2,
A3, B3, B4, B5, B7, C6, C7, C8, C10, L); N/A 8 (A4, B6, C9, K, N, O and the
two trigger sub-cases). A5 works for the wrong reason.

## Per-case details

### A. ShouldCollide

**A1 — WORKS.** Stock `physics.cpp:454-626`. Ours `physics.cpp:1116-1211`
fills two `SourceSharpCollisionSide_t` from `PhysicsOwner_t` and the object
(`physics_shell.h:1880-1901`) and evaluates the table (`:1074-1198`), stock's
order kept. Owners are resolved by `PhysicsOwnerOf`, which answers for managed
entities. Mid-step synchronous, no crossing — correct shape.

**A2 — MISSING (absent).** Stock `:514-519`: "don't collide with your owner"
when both have edicts. Nothing in `Evaluate` or `SourceSharpCollisionPairFacts_t`
(`physics_shell.h:936-970`) carries an owner relation. The block has
`OwnerHandle` (`engine_block_generated.h:111`) and `CStandaloneEntity::SetOwnerHandle`
writes it (`shell_standalone_base.cpp:2591-2597`). Port: in
`CCollisionEvent::ShouldCollide_2`, compare each owner's `OwnerHandle` block
field with the other's `GetRefEHandle()` (both reachable from `pServer`) and
set a new `facts.bOwnerPair`; `Evaluate` refuses with a new reason code. It is
a block read, so it stays mid-step with no crossing. Test: gtest on `Evaluate`
with `bOwnerPair=1` → 0 and reason `SS_REFUSE_OWNER`; break by not setting the
fact. Live: a managed projectile spawned with owner = player must not collide
with that player's shadow hull.

**A3 — MISSING (absent), low.** Stock `:521-541` (siblings under one root never
collide; pair hash on parents) and `:557-573` (child with a non-VPHYSICS root
is an AI mover). `shell_standalone_hierarchy.cpp` exists, so the question is
answerable from the block; needs a `bSameRoot` pair fact. Same shape as A2.

**A4 — N/A today.** Stock `:554-579`. The table has the arm
(`physics_shell.h:1127-1145`, `nAiMoverMoveTypes = 1<<7`), but `nMoveType` is
MOVETYPE_NONE for every managed side (fact 2). No managed mover currently owns
a VPhysics shadow object (grep of `InitPhysics|ConvertToPhysics|SetPhysicsStatic`
under `Entities/Brushes|Triggers|Physics` → FuncPushable, SimplePhysicsProp,
SimplePhysicsBrush, PhysConvert, TriggerVPhysicsMotion, FuncPhysbox only), so
there is no subject. When C2 gives movers a real move type and movers get
shadows, this arm goes live with no further work.

**A5 — works for the wrong reason.** `FuncClipVphysics.cs:40-50` says so
explicitly: it depends on `GetSolidFlags()` answering 0. If A6 is ported this
brush needs stock's `ForceVPhysicsCollide` back: a block bit read by
`PhysicsOwner_t::ForcesCollisionWith` (`physicsowner.h:366-369`, currently
`return false`). Do A5 in the same commit as A6.

**A6 — PARTIAL.** `physicsowner.h:332-349` answers SOLID_VPHYSICS / 0 /
MASK_SOLID for everyone. Consequences: `SetNotSolid` (ABI
`EngineAreas.Entities.cs:398`) is invisible to the solver (stock `:593-602`
would refuse the pair); FSOLID_TRIGGER on the entity is invisible (object's
`IsTrigger` still works, `bTrigger`); the player's shadow should use
MASK_PLAYERSOLID (stock `player.cpp:3451-3460`). Port: `GetSolid`/`GetSolidFlags`
read `pServer->GetCollideable()->GetSolid()/GetSolidFlags()` (the block,
`engine_block_generated.h:93,96`); `PhysicsSolidMaskForEntity` answers
MASK_PLAYERSOLID when `IsPlayer()`. Mid-step block reads. Test: gtest
`FillCollisionSide` of a managed object whose block says FSOLID_NOT_SOLID
yields `nSolidFlags & 4`; break by reverting the neutral.

### B. VPhysicsUpdate and shadow update

**B1/B2 — WORKS.** `physics.cpp:1747-1760` calls `owner.VPhysicsUpdate` for
every active object; `CStandaloneEntity::UpdatePhysics`
(`shell_standalone_base.cpp:2535-2572`) copies the transform;
`SetAbsTransform` ends in `TouchTriggers` (`:1170`), which is stock's
`PhysicsTouchTriggers` (`:1698-1722`).

**B3 — MISSING (absent).** Stock `baseentity_shared.cpp:1176-1180`. Managed:
on a `Sleep` ring event for an entity whose group is InteractiveDebris, call
`Physics.SetCollisionGroup(h, Debris)`. Post-step, no ABI.

**B4 — MISSING (absent).** Stock `props.cpp:2912-2935`. `PropPhysics.cs` has no
`OnAwakened`/`OnOutOfWorld` output (grep). Managed consumes `Wake`/`Sleep`
(already recorded at `physics.cpp:1282,1326`, counted at
`PhysicsCollisionResponse.cs:115`). `m_bAwake` is a networked bool the client
reads; whether the client port needs it is a separate question. OutOfWorld
needs a world-bounds query (stock `IsInWorld`: ±MAX_COORD and velocity).

**B5 — MISSING (absent).** Stock `player.cpp:8260-8268` → `ApplyStressDamage`
(`basecombatcharacter.cpp:3068-3098`) → `CalculateObjectStress`
(`physics_impact_damage.cpp:503-700`): walks the shadow's friction snapshot,
sums contact forces by owner, divides by gravity, 200 damage when
`receivedStress*scale/mass > phys_stressbodyweights` and not PENETRATING.
Nothing in our tree computes it (`grep -rl CalculateObjectStress src` →
only a comment in `FuncWeightButton.cs`). The player's shadow hulls are not
`m_pPhysics`, so `CStandaloneEntity::VPhysicsUpdate` ignores them
(`shell_standalone_base.cpp:2527-2532`). Port: native carries out the snapshot
walk (it needs IsMoveable, mass, owner key, IsPlayer, PLAYER_HELD flag, the
controller's last impulse — all native facts) and records a `Stress` ring kind
(new kind 13; bump `SS_PHYSEVENT_KIND_COUNT`, `PhysicsEvent.KindCount` and
`PhysicsEventLayoutTests`) with receivedStress/exertedStress/flags; managed
SsPlayer applies the 200-damage rule with `m_impactEnergyScale*0.125`
(`player.cpp:8265`). Post-step. Depends on H for the PENETRATING exemption.

**B6 — N/A** with A4.

**B7 — MISSING (absent).** Stock `player.cpp:8286-8482`: the solver's shadow
position/velocity are read back, the player is moved/impulsed when the error
exceeds `VPHYS_MAX_DISTSQR`/`VELSQR`, with the frozen-controller recovery and
the stuck fix. `shell_standalone_player.cpp:1100-1115` records the absence
("a player is not pushed by props and is not crushed by them"). The managed
`PlayerShadow.Step` already owns the forward state machine
(`src/abi/PlayerShadow.cs`) via `ShadowProbe`/`ShadowApply`
(`shell_standalone_player.cpp:1596-1770`). Port: add to
`SourceSharpPlayerShadowState` the controller's `GetShadowPosition` result,
`GetShadowVelocity`, `WasFrozen`, and whether the active hull is PENETRATING
(from H); `PlayerShadow.Step` computes the reverse arm (pure, testable) and
the plan grows `bMoveTo`/`bImpulse`; `ShadowApply` carries them out. The two
`UTIL_TraceEntity` checks are managed traces (WorldQuery). This runs from the
managed command loop after `PhysFrame`, so it is outbound only. Note stock's
ordering: shadow update runs in `PhysFrame` after `Simulate`, once per physics
frame, not per command.

### C. Collision response

**C2 — the first fix.** `physicsowner.h:273-276`. Replace with: `pGame` with
a physics object → MOVETYPE_VPHYSICS (the rule `shell_worldquery.cpp:274-290`
already applies); player → `MvGetMoveType()`; else the block's `MoveType`.
No ABI. After this, C4/C5 fire and A4's arm becomes real. Test: gtest that
`SourceSharp_FillCollisionSide` for a standalone entity with an object reads
`nMoveType == 6`, and a live `cs_sv_physics_events` reading with
`responded > 0` (today it is 0 by construction).

**C3 — PARTIAL.** `PhysicsCollisionResponse.cs:24-43` specifies the slot and
declines it over the edict question. The native batch player
(`physics.cpp:609-650`) emits from entity 0 at `queued.origin`, so no edict is
required: the slot takes the entity handle only to fill `pGameData` for
`AddImpactSound`'s dedupe (`ss_physics_sound.h:63`). Generic slot, post-step,
outbound from the drain.

**C6 — MISSING (absent), the largest port.** The dispatch is dead at
`physics.cpp:1887-1889`; every subclass `VPhysicsCollision` that computed
damage is gone with the class; `PhysCallbackDamage` is deleted and
`physics_move_scope_test.cpp:509-527` asserts it stays deleted (that test and
`docs/physics-move-scope.md:279` must be updated by the port). The decision
(table, energy scale, `SF_*_DONT_TAKE_PHYSICS_DAMAGE`, glass mass hack,
`damage > 10` for surfaces, pushables ignore players) is managed policy. The
arithmetic (`CalculatePhysicsImpactDamage`) needs per side: pre/post linear
velocity, pre angular velocity, inertia, invMass, IsMoveable, PLAYER_HELD /
NO_IMPACT_DMG / CONSTRAINT_STATIC / DMG_SLICE / HEAVY_OBJECT / MULTIOBJECT
game flags (already in record), IsStatic (in record), IsFloating (I2). Port:
extend `SourceSharpPhysicsEvent_s` (append after `bStatic1`: `preVel0/1`,
`postVel0/1`, `preAngVel0/1`, `inertia0/1`, 24 floats = 96 bytes; update
`event_is_100_bytes`, `PhysicsEvent.SizeInBytes` and
`PhysicsEventLayoutTests`); `PreCollision` already captures `preVelocity`
(`physics.cpp:1848`), `PostCollision` captures post (`:1894`). Managed:
`PhysicsImpactDamage.Calculate(in ev, index, table, energyScale, allowStatic,
heldObjects)` as a pure class; `PropPhysics`/`FuncBreakable`/`FuncBreakableSurf`
/`FuncPhysbox` call it from a new `ScriptedEntity.OnPhysicsCollision(in ev,
index)` hook raised by `PhysicsCollisionResponse.Collision` for each side with
a managed handle, and apply via their `OnDamaged`. Post-step; the ring is
already after `UpdateDamageEvents`.

**C7** needs C6 and the existing `SetVelocity` slot. **C8** needs C6's
pre-velocity of the other side plus `ComputeEnablingImpulse`'s
`CalculateForceOffset` — do it natively as a generic slot
`ApplyImpulseAtPoint(entity, worldVelocity, worldPoint)` or reuse
`ApplyForceOffset`.

**C10 — MISSING (absent).** Stock `basecombatcharacter.cpp:3109-3200` with the
player table (`physics_impact_damage.cpp:19-48`) and
`phys_impactforcescale`. The record must say which side is a
`CALLBACK_SHADOW_COLLISION` object (`physics.cpp:1895-1898` computes it and
throws it away): one bit each in `nFlags`. Managed SsPlayer handles the hook
from C6 when its side is the shadow and the other is MOVETYPE_VPHYSICS and
moveable and not its ground entity.

### D. Friction

**D2 — MISSING (dead arm).** `physics.cpp:1937-1963` resolves `.pBase`; the
server `PhysFrictionSound` body is compiled out (`physics_shared.cpp:1288`).
The ring event carries point, velocity, energy and both surfaces
(`physics.cpp:1994-1997`), which is the whole input. Managed policy:
`energy >= 75`, neither material 'X', `volume = (energy*ENERGY_VOLUME_SCALE)^2`,
rough/smooth choice by `roughnessFactor < roughThreshold` (needs the surface's
`sounds.scrapeRough/Smooth` and `audio.*` — widen `MoveSurface` or add a
`SurfaceSounds` slot), 0.5 s effect throttle per entity, 0.1 s silence →
stop. One generic slot for a looping entity sound patch
(`SoundCreate/ChangeVolume/ChangePitch/SoundDestroy` on `ss::SoundController`,
which `physics.cpp:2022` still uses). Post-step.

### E. Touch

**E1 — MISSING (dead arm), zero-ABI port.** Stock buffers contact touches
during the step (`m_bBufferTouchEvents`, `physics.cpp:1709/1770`) and
dispatches `PhysicsMarkEntitiesAsTouchingEventDriven` in `FrameUpdate`. Ours
records to the ring and then dies at `physics.cpp:2629-2630` / `:2681-2682`;
`DispatchStartTouch`/`EndTouch` are refusals (`:2238-2258`, `:2273-2290`).
But `PhysicsOwner_t::StartTouch/EndTouch` already exist for the trigger path
(`physicsowner.h:604-616`) and feed `g_SolverHeld`
(`shell_standalone_base.cpp:1571-1592`), which the overlap report hands to
`EntityDirectory` whose diff raises Start/Stay/End on both sides
(`EntityDirectory.cs:495-512`). Port: `touchevent_t` stores the two
`IPhysicsObject*` (as `triggerevent_t` does) instead of `IGameEntity*`;
`UpdateTouchEvents` rebuilds the owners and calls
`owner0.StartTouch(owner1); owner1.StartTouch(owner0)` (stock marks both
sides, `physics_main_shared.cpp:1060-1079`); `CountPhysicsObjectEntityContacts`
compares `Key()`. Delete the two refusals. Test: gtest with two fake owners
sees both `StartTouch` calls; xUnit: `EntityDirectory` raises `TouchEdge.Start`
for a solver-held pair with no AABB report; live: a thrown managed crate
collects an item / trips `func_weight_button`. Break: drop the second
`StartTouch` call → one side silent.

### F. Wake/Sleep

**F2 — MISSING (dead arm).** The only stock watchers are the two constraint
classes (`physconstraint.cpp:795-843`, `1097-1148`): when one attached object
wakes, wake the other so the constraint solves. `PhysConstraint.cs` /
`PhysicsConstraintEntity.cs` have no wake handling (grep). Managed consumes
`Wake` events; no ABI.

### G. ShouldFreezeObject

Stock `physics.cpp:657-716`: when vphysics freezes an object for its
per-object collision budget, and it is debris (groups DEBRIS,
INTERACTIVE_DEBRIS, DEBRIS_TRIGGER, `:786-798`) resting under a moveable
heavier contact (`FindMaxContact`, `:628-655`, threshold 10× mass), crush it
(`m_takedamage > DAMAGE_EVENTS_ONLY`) or remove a gib or create an entity
solver. Ours `physics.cpp:1213-1240` returns true for everyone. Port: restore
`FindMaxContact` (deleted; `docs/physics-move-scope.md:505`), ask
`owner.GetCollisionGroup()`, and queue `AddDamageEvent(owner.pGame, info)`
with DMG_CRUSH and `force.Length()*0.1`; `UpdateDamageEvents` already calls
`StandaloneEntity_TakeDamage` → managed `OnDamaged` decides (a gib class
removes itself; others take damage or return 0). This is the J queue's first
producer. The callback's return value is unchanged (stock returns true in all
arms). Entity solver (`EntityPhysics_CreateSolver`) is NPC code: N/A.
Test: gtest with a fake `IPhysicsFrictionSnapshot` giving a 20× heavier
moveable contact → one queued crush event naming the debris entity; break the
10× threshold and see red.

### H. ShouldSolvePenetration

Stock `physics.cpp:967-1057` with `FindOrAddPenetrateEvent` (`:922-947`),
`UpdatePenetrateEvents` (`:837-920`), `UpdateEntityPenetrationFlag`
(`:800-820`), `PhysForceEntityToSleep` (`:1920-1936`). Behaviour for this mod:
every penetrating pair is ledgered and both objects get
`FVPHYSICS_PENETRATING`; after 3 s stuck, unless either is a player or
shadow-controlled, both are put to sleep for 1 s (`COLLSTATE_TRYDISABLE` →
`DISABLED` → `ENABLED`); developer error boxes; flag cleared 1 s after the
last report. Debris-stuck-in-non-static (`TRYENTITYSOLVER`) and NPC solver
are N/A. Readers of the flag that are already in the tree: the managed shake
rule (`PhysicsCollisionRules.cs:271`), B5 and B7. Port: native ledger keyed by
`CBaseHandle` pairs (`PhysicsOwner_t` gives `ToHandleEntity()`, `IsPlayer()`,
`GetCollisionGroup()`, `pObject->GetShadowController()`), `PhysSetGameFlags`
(exists in `ss_physics_shared.h`), `PhysForceEntityToSleep` moved back from
`physics_debug.cpp:863`. The 3 s / 1 s constants go in the rule table so
managed states them. The mid-step return (`false` once on TRYDISABLE) is
native-decidable from the ledger. Optionally record a `Penetrating` ring kind
so managed can see it. Test: gtest on the ledger (ENABLED → TRYDISABLE at
3 s; player pair never disables; cleared after 1 s quiet; flag set on add and
cleared on remove); live: two crates spawned overlapping go to sleep by 3 s
and `ss_physworld_penetration` past-guard count leaves zero.

### I. Fluid

**I2 — MISSING (refused).** Stock `physics.cpp:1070-1071,1126-1127` →
`OnEntityEvent` (`baseentity.cpp:4274-4300`) counts water/slime touches;
`IsFloating` (`:6863`) gates the 3× energy scale in impact damage
(`physics_impact_damage.cpp:373-379`); `GetWaterType` picks the slime splash
(`physicsowner.h:293-296` neutral 0). Managed keeps a per-entity in-fluid set
from `FluidStart`/`FluidEnd` (both recorded, `physics.cpp:1618,1662`; the
start event carries density). Native splash could read the water type back if
managed writes it into the block's water fields. Post-step.

### L. Break sound

`PropPhysics.Break` (`PropPhysics.cs:408-416`) fires `OnBreak` and spawns
gibs; no sound (grep `BreakSound` under Props/Brushes → nothing). Stock
`props.cpp:1673` → `PhysBreakSound` → `AddBreakSound(origin, materialIndex)`.
Our `SourceSharp_PlayBreakSounds` (`physics.cpp:652-675`) still exists and is
never fed. One generic slot `PhysicsBreakSound(origin, surfaceProps)`;
managed knows the surfaceprop (it has `SurfaceProp` on breakable surfaces,
`FuncBreakableSurf.cs:242`; props get it from the model's solid — expose via
`GetMass`-style slot or the `MoveSurface` index already in the collision
record).

## Proposed order of work

Smallest new surface first; each step one commit with its test.

1. **C2: real move type on `PhysicsOwner_t`** (no ABI). Unblocks C4/C5/A4 and
   makes `cs_sv_physics_events responded` a real number. Add the gtest on
   `FillCollisionSide`. Then measure live and record the number with the tip.
2. **E1: contact touch dispatch through `PhysicsOwner_t::StartTouch`** (no
   ABI; delete two refusals). Post-step.
3. **F2, B3, B4, I2: managed consumers of the ring kinds already recorded**
   (`Wake`, `Sleep`, `FluidStart/End`) — no ABI; each a managed class change
   with an xUnit fact. Remove the "carried, nothing consumes it" markers in
   `PhysicsEventsCommand.cs:55-57` as each kind gains a consumer.
4. **A2 + A6 + A5 together: block-read pair facts** (owner, real solid
   flags, player solid mask, clip-brush force bit). Mid-step, no crossing;
   gtests on `Evaluate`/`FillCollisionSide`. Add A3 if hierarchy sees use.
5. **H: penetration ledger, native carry-out** (restores
   `PhysForceEntityToSleep`; constants in the rule table). Mid-step return,
   no crossing. Prerequisite for B5 and B7 and for the shake rule's flag.
6. **G: ShouldFreezeObject crush** (restores `FindMaxContact`; first
   producer for the J queue). Update `physics_move_scope_test.cpp` and
   `docs/physics-move-scope.md` in the same commit.
7. **C3 + L: two sound slots** (`PhysicsImpactSound`, `PhysicsBreakSound`),
   append-only in `SourceSharpEngineApi` and `Abi`; the batches already exist.
8. **C6 (+C7, C8, C10): collision record extension + managed impact
   damage.** Append the velocity/inertia tail and the shadow bits; port
   `CalculatePhysicsImpactDamage` and the three tables as pure managed code
   with oracle facts against stock numbers; raise `OnPhysicsCollision` per
   side. Post-step. Largest change; everything above makes its inputs true
   (move type, floating, PENETRATING).
9. **D2: friction sound loop** — one generic looping-sound slot and a
   `SurfaceSounds` read; managed policy pure.
10. **B5 + B7: player stress and reverse shadow update** — extend the
    shadow probe state (B7) and add a `Stress` ring kind (B5); managed
    `PlayerShadow`/`SsPlayer` decide. Post-step/outbound.

What is mid-step (must stay native, no managed crossing): A1-A6 (ShouldCollide
inputs), H's return value, G's snapshot walk. Everything else drains after the
step through the existing single crossing, or is carried out from the managed
command loop.

Ratchets and tests that will trip and must be updated deliberately:
`src/test/native/physics_move_scope_test.cpp` (pins the deleted helpers absent,
`:480-527`), `docs/physics-move-scope.md` totals, `PhysicsEventLayoutTests` /
`event_is_100_bytes` (record growth), `SS_PHYSEVENT_KIND_COUNT` /
`PhysicsEvent.KindCount` (new kinds), the ABI parity tests (new slots), and
`FuncClipVphysicsTests` (A6).
