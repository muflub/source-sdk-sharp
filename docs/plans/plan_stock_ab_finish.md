# Plan: finish the stock A/B queue (props, breakables, glass, animating, constraints)

Status: written 2026-10-28 at main 4115dd0c1 (lane `stock-ab`, worktree
`../ss-stock-ab`). Not started: see Decisions.

## Goal

Close every divergence still open in the stock A/B queue
(`$S/stock-ab-queue.md`, scratchpad), so that props, breakables, glass,
animating entities, constraint systems and momentary buttons behave as stock
HL2MP's do. "As stock" means the same outcome in a live A/B against the
stock build (`tools/stock-ab`), and for each rule a fact that a mutant turns
red.

## Ground rules (from CLAUDE.md and the lane's practice)

- Read the stock C++ in `../source-sdk-2013-valve` first, and port it.
  Never write to the clone.
- Every change gets its facts and mutants. Mutants run from a cp snapshot
  (`$S/mut-ml.py` template); a mutant that is equivalent is named as such in
  the commit message.
- One reviewable unit per commit, with `make test` before each commit.
  New ported files carry `tools/valve-header.txt` and a row in
  `docs/valve-header-new.tsv`.
- New keys, inputs and outputs: run `$S/inv-merge.py` (var inventory) and
  `$S/close-loop.sh` (contract baseline). Both are in the scratchpad.
- Land at milestones: `make check`, then rebase onto main, carrying the same
  commit count, then `git merge --ff-only`. Never push.

## Phase 1: glass shards and the sound of a pane breaking

Stock: `CBreakableSurface::ShatterPane`, `DropPane` and `CreateShards`, and
the blast arm of `TraceAttack` (`func_breakablesurf.cpp` 1057-1187, 520-600).
Each pane that goes sends `te->ShatterSurface`: small shards for a shattered
pane, none of the force for a dropped one. A blast sends one large shatter
while under 10% of the panes are broken, and otherwise one per standing
vertical strip. Each shattered pane also plays `DamageSound`.

Ours: `src/server/managed/Entities/Surfaces/FuncBreakableSurf.cs` only
breaks panes. Nothing on the server calls
`TempEnts.ShatterSurface` (`src/server/managed/TempEnts.cs:703`). The client
draws the grid from the pane bits (`ClientBreakableSurf.cs`) and plays
`ClientTeShatterSurface` (`src/client/managed/TempEnts/ClientTeShatterSurface.cs`).

1. **Measure first.** Shoot the ss_sandbox pane in both games with
   `$S/glassab.py` and compare screenshots and client temp-entity counts
   (`cs_cl_te_report`). If our client already makes shards from the pane bits,
   find where. Stock's client makes none from the bits, so those would be
   doubled once the temp entity is sent, and must go in the same commit.
2. Port `CreateShards` (shard size small or large, force, position) as a
   method that sends the temp entity. Call it from the bullet arm of
   `TraceAttack` once per pane taken out, from the settle in `OnThink` with no
   force (DropPane), from `OnTouch`, and from `Shatter`. Call it from the
   blast arm as stock's large shatter or strips.
3. Port `DamageSound` for each shattered pane. CBreakable's material sound
   already exists in `BreakableBrush` (`DamageSound`); make it reachable.
4. Facts: the temp entities sent per kind of break (count, sizes, force),
   and one sound per shattered pane. Live: the A/B screenshots side by side.

## Phase 2: the crossbow bolt into a pane

Stock: `CCrossbowBolt::BoltTouch` (`shared/hl2mp/weapon_crossbow.cpp` ~200-235)
calls `DispatchTraceAttack` on what it hits, and keeps flying through
`COLLISION_GROUP_BREAKABLE_GLASS`. Ours, in
`src/server/managed/Entities/Projectiles/Bolt.cs:208`, calls `Damage.ApplyAt`,
which our pane ignores for a bullet.

1. Send the hit to `ITraceAttacked` (`src/server/managed/TraceAttacks.cs`)
   when the struck entity answers one, as `Arsenal.Round` does. Otherwise use
   ApplyAt.
2. Facts: a bolt into a whole pane breaks it and flies on, and a bolt into a
   crate is unchanged. A mutant drops the dispatch.

## Phase 3: generic chunks (`breakable_model` / `breakable_count`)

Stock: `PropBreakableCreateAll`'s second branch (`shared/props_shared.cpp`
1133-1218). It runs when the model's break list is empty and the prop data
names a chunk group with a count. Each chunk is
`GetRandomChunkModel(group, maxSize)`, with health 1, fade 5-10 s, the
default burst, `COLLISION_GROUP_DEBRIS` and the breakable skin. It is placed
at a random point on the plane of the prop's two largest OBB axes and pushed
from the centre, with its box aligned to the prop's. `m_iMaxBreakableSize`
is computed in `ParsePropFromKV` (`props_shared.cpp:383-389`) as the area of
the two largest OBB axes over 32 x 32.

The chunk count already counts as pieces, from 442472a96 (`BreakableProp`'s
`StartingHealth`). `PropDataValues` carries `BreakableModel`,
`BreakableCount` and `BreakableSkin` (`src/server/interop/ServerInterfaces.cs`).

1. **Server** (single player, or a model marked `multiplayer_break server`
   or `both`): `PropBreak.CreateAll` (`src/server/managed/Entities/Props/PropBreak.cs`)
   gets the generic branch. `BreakableProp` hands it the group, count, skin and
   max size, the max size computed from `Entities.GetBounds` at spawn. Chunks
   come from `IServerPropData.RandomChunkModel` (`ServerInterfaces.cs:582`).
   Port AlignBoxes with its stock comment as the reference.
2. **Shared prop data:** the client needs `prop_data` and propdata.txt's
   `BreakableModels`. Move the parse half of `src/server/managed/PropData.cs`
   (ModelPropValues, base chain, chunk groups) into `src/shared/managed`,
   with the server keeping its file loading through `IEngineFiles`. The
   client reads model text through `EngineModelInfo`
   (`src/shared/interop/EngineModelInfo.cs`) and files through its own file
   area. The analyzers SS0001 and the interface-assembly rule apply.
3. **Client** (the multiplayer default): `ClientPhysicsProps.Break`
   (`src/client/managed/TempEnts/ClientPhysicsProps.cs:530`) gets the same
   generic branch when the model's list is empty. Stock's client runs it from
   `C_PhysPropClientside`'s own prop data.
4. Facts on both sides: a model without gibs whose data names WoodChunks with
   count 4 makes 4 chunks from that group, each on the plane, with fade 5-10
   and the skin; a count of 0 makes none; a model with gibs ignores the
   group. Live: break a Wooden.* prop with no gibs (find one with
   `$S/healthab.py`'s rows plus the model's text) and count the chunks in
   both games.

## Phase 4: CBreakableProp leftovers

Stock: `props.cpp` (`CBreakableProp::Spawn`, `OnTakeDamage`,
`BreakablePropTouch`, `BreakThink`), with spawnflags in `props_shared.h:21-22`.

1. **`m_createTick`:** a blow on the tick the prop was made only reaches the
   base, as DAMAGE_EVENTS_ONLY. The tick comes from
   `IEngineEnvironment.Globals.TickCount`. This stops one explosion from
   breaking the pieces of the prop it just broke.
2. **`SF_PHYSPROP_TOUCH` (0x10):** something running into the prop at a speed
   whose 1% reaches its health breaks it with a crush, and takes a quarter of
   that as a slash.
3. **`SF_PHYSPROP_PRESSURE` (0x20) and `PressureDelay`:** a player standing on
   it starts `BreakThink` after the delay. The ground read for a player is
   `SsPlayer.GroundIndex` (`src/server/managed/Entities/Players/SsPlayer.cs:240`).
   Players only (ruled 2026-10-28). Something else resting on the prop does not
   count, which is a recorded divergence: there is no managed ground read for
   non-players, and no dm_lockdown or ss_sandbox entity sets the flag.
4. **`PerformanceMode`** on props: `PM_NO_GIBS` makes no server pieces unless
   `breakable_disable_gib_limit` is set (`BreakableConVars.cs`), and
   `PM_FULL_GIBS` ignores the gib limit, as `CBreakableProp::Break` passes it
   to `PropBreakableCreateAll`.
5. Facts and mutants for each.

## Phase 5: CBreakable leftovers (func_breakable, func_pushable)

Stock: `server/func_break.cpp`.

1. **`spawnobject`:** the `pSpawnObjects` table (lines 48 and 80) and Die's
   spawn of the chosen item where the breakable stood.
2. **`SF_BREAK_PRESSURE` and `PressureDelay`** (line 150, think at 616), with
   players only, as Phase 4.3.
3. **Physics impact damage:** `CBreakable::VPhysicsCollision`, with
   physdamagescale, `SF_BREAK_PHYSICS_BREAK_IMMEDIATELY` and
   DONT_TAKE_PHYSICS_DAMAGE. Read the stock function before writing.
4. **Game events:** `break_breakable` (entindex, userid, material; func_break.cpp:994)
   and `break_prop` (entindex, userid; `CBreakableProp::Break`). Both are
   declared in hl2's `resource/gameevents.res` (lines 173-184). Fire them
   through `IEngineGameEvents` (`EventCreate`, `EventSetInt`, `EventFire`), as
   `src/server/managed/DeathNotice.cs` does. A user id is only for a player
   breaker.
5. **`SetName( NULL_STRING )`** before Die's outputs (line 1107).
6. Facts and mutants for each. Live: break a dm_lockdown window in both
   games and compare the events from both logs.

## Phase 6: CBaseAnimating per-class leftovers

Stock: `server/baseanimating.cpp` datadesc (190-217). Ours:
`src/server/managed/Entities/AnimatingEntity.cs`.

1. **`fademindist` / `fademaxdist` / `fadescale`** keys and inputs. Today a
   managed prop's fade reaches the client nowhere. Network them in class state
   (ruled 2026-10-28: only some classes need them): a `[NetState]` on
   `AnimatingEntity` that every animating class inherits, with the layout and
   parity tests. Measured 2026-10-28: 50 of the 296 managed classnames are
   animating. On dm_lockdown, 187 of 605 entities set fade keys (prop_dynamic,
   prop_physics, prop_physics_respawnable). On ss_sandbox one does, an
   env_detail_controller, which is not animating.
2. **`LightingOrigin`** key and **`SetLightingOrigin`** input, a networked
   lighting origin handle in the same class state. No entity on dm_lockdown or
   ss_sandbox sets it, so the live check is a sandbox station.
3. **`SetCycle`, `SetPlaybackRate`** and the sequence keys, through the
   animation state each class already networks (`PropDynamicNetState`, for
   one).
4. **`BecomeRagdoll`:** check what the managed ragdoll path offers (the client
   ragdoll ports) before choosing.
5. Facts and mutants; the contract baseline closes the rows.

## Phase 7: constraint systems and constraint sounds

Stock: `server/physconstraint.cpp` (Break 315, ConstraintBroken 402, the
sound thinking near 808) and `CPhysConstraintSystem`. Ours:
`src/server/managed/Entities/Physics/PhysConstraintSystem.cs` is an empty
class, by a recorded choice: "grouping needs the group to exist before any
member is created".

1. Create the group through the existing forwarder
   (`IPhysicsEnvironment::CreateConstraintGroup`, `src/abi/SharedForwarders.g.cs:387`).
   Order member creation after it, and activate the group when all members
   exist. ss_sandbox's phys_constraint uses a system.
2. The `ConstraintBroken` input and the hinge and slide sounds
   (`m_soundInfo`): reversal and slide sound keys.
3. Facts: members join the group before they are created, and the group is
   activated once. Live: the ss_sandbox constraint station in both games.

## Phase 8: momentary_rot_button's CBaseButton inputs

Stock: `server/buttons.cpp` 63-67 (Lock, Unlock, Press, PressIn, PressOut)
and 953-966. Ours: `src/server/managed/Entities/Brushes/MomentaryRotButton.cs`.
Port Press, PressIn and PressOut, with OnIn and OnOut as stock's momentary
button answers them. Read `CMomentaryRotButton`'s overrides before writing.

## Phase 9: verification and landing

1. `$S/healthab.py` on dm_lockdown and ss_sandbox: spawn health the same as
   stock for every row.
2. The glass, chair, crate and barrel A/Bs (`$S/glassab.py`, `dynab.py`,
   `crate.py`, `barrelab.py`) re-run, with no new difference.
3. The census (`tools/stock-ab/stock-ab run --config debug --out DIR --only overview`):
   the classname and contract gaps should only shrink.
4. `make check`; land as x23 by rebase and ff; update the milestone memory and
   the queue.

## Decisions

- 2026-10-28, user: "Write a plan and commit to finish but dont execute".
  The plan is committed on this instruction, against CLAUDE.md's
  "uncommitted until reviewed" default. Nothing in it is started.
- 2026-10-28, user: "How many classes need the origin? If all engine block,
  if some class network state". 50 of 296 managed classnames are animating,
  so fade and lighting origin go in class network state on `AnimatingEntity`
  (Phase 6.1-6.2).
- 2026-10-28, user: "Players only". Breaking on pressure (Phases 4.3 and 5.2)
  counts only a player standing on the breakable, through
  `SsPlayer.GroundIndex`. No generic ground read is added for other entities,
  and the gap is recorded as a divergence.
