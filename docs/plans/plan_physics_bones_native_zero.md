# Physics and bones: managed, native removed

Reviewed and committed by the user's request, 2026-10-11. Owner: session
source-sdk-sharp-0e. Base: main 375193a4c (2026-10-11).

**STATUS 2026-10-12: DONE at e0f8bfc42.** No physics or bone *logic* is
left in C++ except the four small decisions listed under "Phase 4 census".
What remains is engine-forced plumbing, each piece with its reason below.
The performance pass (user ruling: physics/bones crossings may raise the
live-gate budget; a perf pass at the end) is next.

Goal (user, 2026-10-11): "get all this across the line and ensure all
physics and bone is managed with native removed". *All this* = the
2026-10-10 review's fixes (docs/reviews/2026-10-10-recent-work.md). *Native
removed* = no physics or bone **logic** in C++. What the engine forces stays
as a thin boundary (CLAUDE.md: the engine calls a C++ vtable, reads a struct
by layout, or holds a pointer). Each piece that stays is listed below with
its reason, and only a measured reason keeps one.

## Inventory at 375193a4c (read-only survey, re-measure per phase)

Bones. The managed port is complete (src/shared/managed/Bones/, 13 files,
12,376 lines, 326 facts; bone_cpp_removal.md phase A1-A5 landed; inventory
closed at 21d0c2ae1, 138/138 rows). It is not wired in. Still native:
bone_setup.cpp 6212, bone_library.cpp/.h 651/743, shell_bone_leaf_library
234 + bone_leaf_library.h 181, ragdoll_shared.cpp 792 (client only),
ragdoll_library.cpp/.h 635/781, the SetupBones/EnsureBones arm of
shell_standalone_client.cpp, SourceSharp_StandaloneBoneMatrices in
shell_standalone_hierarchy.cpp. Fork links: bone_accessor.cpp,
studio_shared.cpp.

Physics server. The callback tables have partly moved: IPhysicsObjectEvent
is managed-built (B2.1), and 9 of the 10 IPhysicsCollisionEvent slots are
(B2.2). Still native: physics.cpp 2192 (physics-move-scope.md: SLOT 82,
HOOK 257, BOUNDARY 23, SLOT-HELP 343, LIVE-API 57, INSTRUMENT 246),
physics_fx 317, shell_constraints 1006, shell_motioncontrollers 979,
shell_physicsobjects 249, physics_shared.cpp 1363, and the twinned
deciders object_stress.h / penetration_ledger.h / find_max_contact.h /
physworld_match_rule.h (managed twins exist but do not decide yet). The
player shadow arm of shell_standalone_player.cpp is not split yet.

Physics client: fork game/client/physics.cpp 1487 and ragdoll.cpp 592
(client-novalve clusters 9 and 7, step C6, not started).

## Phase 0: land the review fixes -- DONE 2026-10-11

The omp agent landed its four lanes on main itself (main c588d1c61): every
commit of review-bones, review-pcore and review-physics, plus 7 of the 10
review-2026-10-11 commits, is on main. The other three were rightly
dropped: two console notes were folded into 2de0aeadb, and the Geiger cost
note became moot when main went back to stock's rotating check client.

Leftovers:
- Victim skip: native-zero dcae1517d.
- g_Forwarder / g_pInstalledCvar: already in the globals ledgers.
- object_stress `* 0.5` double: no code. Stock computes `(sum-objMass)` in
  float, `* 0.5` is exact, and rounding a float+float sum through double
  equals rounding it straight to float (double has more than 2*24+2 bits).
  No fact could go red.

## Phase 1: bones wired to managed, bone_setup.cpp evicted

Follows bone_cpp_removal.md B/C and its 2026-10-08 rulings (cache dropped,
PrefetchSequence managed with a shim, pose debugger goes to debug_api).
plan_bones_phase_bc.md's ledger items fold in here.

Survey 2 (bone-TU audit, 375193a4c): no production C# outside Bones/
references SourceSharp.Shared.Bones. What keeps bone_setup.cpp linked is
bone_library.cpp (IBoneSetup, CIKContext, BuildMatrices, CalcProceduralBone,
TraceToStudio), shell_standalone_client.cpp (CIKContext at 1788-1887,
pose-param queries), ragdoll_shared.cpp (BoneToBone, Derivatives,
BoneIndexByName) and model_descriptor.cpp (default pose params, Duration).
Native bone decisions with **no managed twin yet** (they port in 1.0):
bone_library AccumulatePose / BuildBones / BuildAttachments /
TraceHitboxes / PoseToWorldProcedural / BuildMergeMap (~60% of its 345 code
lines); bone_library.h inlines (Cycle, ClampSequence, ComposeAttachment,
ApplyMergedBones, ~50 lines); procedural_bone_rule.h predicates;
bone_cache_key.h / bone_merge_key.h validity; client EnsureBones 1615-1988
and MergeBonesFrom 2062-2184 (~300 lines); server hierarchy EnsureBones
193-295 and cycle decisions (~150 lines). Debug-only probe/replay code
(client ~1,300 lines, shell_standalone_hierarchy_debug.cpp 763) is allowed
native tooling (debug builds only); it moves to debug_api or is deleted
with what it probes. RegisterBoneSweep has no production caller: delete it.
CJiggleBones (tier3) is a Valve library call: ported with the procedural
pass, or kept as a ruled outbound call if it holds engine state.

1.0 Port the twin-less decisions above to managed (facts first from
    the native code at base, then the C# version), next to the Bones/ clusters.
1.1 B1: managed-built interface `IManagedBones` (rows in
    docs/managed-interfaces.tsv, chain= in tools/vtable-layout,
    AbiParityTests). It answers BuildBones / AccumulatePose /
    CalcBoneDerivatives / CalcBoneToBoneTransform /
    GetDefaultSurfaceProps for a batch.
1.2 B2: one crossing per frame. The driver collects dirty skeletons, and
    managed writes the engine bone arrays in place. Re-point bone_library,
    ragdoll_shared, physics_shared and the server hierarchy shell at it.
1.3 B3 cost gate (**checkpoint**): per-frame SetupBones cost, managed vs
    native, same build, dm_lockdown, numbers with instrument and commit. A
    blown budget is a boundary-design task, not a retreat.
1.4 C1: evict bone_setup.cpp from the three CMake list positions (server
    :120, novalve row :319, client :293). Retire BoneCacheStore alongside
    it. GC_SECTIONS=0 build + tools/nogc-relink.
1.5 C2: shrink bone_library.cpp and shell_bone_leaf_library to the layout
    writes. C3: confirm studio.cpp holds no bone logic (expected: none).
1.6 Oracle parity (plan_bones_phase_bc C2) for each PORTED symbol over the
    dm_lockdown model corpus.

## Phase 2: server physics logic to managed

Survey 2 (physics-TU audit, 375193a4c):
- **Twins:** in all four pairs the native copy is the live one, and no
  production C# references the managed twin.
  - object_stress.h is called at shell_standalone_player.cpp:1868.
  - penetration_ledger.h is called at physics.cpp:1512/1546/1789/2125.
  - find_max_contact.h is called at physics.cpp:1300.
  - physworld_match_rule.h is called at physics_debug.cpp:320.
- **Dead code:** physics_fx.cpp's PhysicsSplash (108-317) has no caller.
  PhysicsSplashPlan.cs is the live version, so delete the native one as
  step 2.0.
- **physics_shell.cpp** is layout static_asserts only, built by tests
  only. It stays as a parity test.
- **Native vphysics callback implementations still to move:**
  - CCollisionEvent's solver half: ShouldCollide_2 1154-1267,
    ShouldFreeze* 1275-1345, ShouldSolvePenetration 1472-1527, and the
    PostSimulationFrame/flush/UpdatePenetrateEvents 1720-1838.
  - CPhysicsGameTrace (physics_shared.cpp:953).
  - IVPhysicsKeyHandler: CSkipKeys (physics.cpp:1561),
    CSolidSetDefaults, and the two ragdoll handlers
    (ragdoll_shared.cpp:87/128).
  - CPhysConstraintEvents (shell_constraints.cpp:938).
  - CSourceSharpMotionEvent (shell_motioncontrollers.cpp:85).
- **physics.cpp's other logic:**
  - PhysFrame 1571-1708;
  - impact and break sound playback 604-705;
  - damage events and inflictor restore 1891-2122;
  - the physworld report 791-1100, which is diagnostics and goes to
    debug_api or debug-only.
- **Rough logic-line estimates per TU:**
  - shell_constraints: ~450 (creation, motors, springs);
  - shell_motioncontrollers: ~400 (force, motor, keep-upright, medium);
  - shell_physicsobjects: ~60;
  - physics_shared: ~450 (data objects, solid parsing, collision
    enable/disable, ComputeSlideDirection, friction; friction already
    has managed versions);
  - the player vphysics arm of shell_standalone_player.cpp 1090-2075:
    ~350 (ShadowProbe/Apply/Stress, Init/DestroyVCollision);
  - shell_propdata_system (client): ~120.
- **Client:**
  - fork physics.cpp: ~700 lines of logic (CCollisionEvent, CPhysicsSystem,
    PhysicsSimulate, touch, friction, splash, fluid);
  - fork ragdoll.cpp: CRagdoll;
  - ragdoll_library: ~300;
  - ragdoll_shared: ~450.

2.0 Delete dead native PhysicsSplash/BestAxisMatchingNormal (physics_fx);
    the managed twins become the live deciders and the four native headers
    retire with their gtests (after the managed facts carry their cases).
2.1 The four IPhysicsCollisionSolver slots and PostSimulationFrame become
    managed-built Valve-layout tables, the B2.1/B2.2 pattern: one interface
    per step, installed at pre-entity init, native deleted after a live
    check.
2.2 The other callback interfaces become managed-built Valve-layout tables,
    one per commit: IPhysicsGameTrace, IVPhysicsKeyHandler (CSkipKeys,
    CSolidSetDefaults), IPhysicsConstraintEvent, IMotionEvent. The
    penetration ledger moves with the solver slots in 2.1. Resume-doc item
    H is answered by the 2026-10-11 goal ("native removed"): the give-up
    becomes a managed answer asked mid-step.
2.3 physics.cpp SLOT-HELP (343) and physics_fx to managed. CPhysicsHook
    stays as a game-system registration that forwards to managed.
    Arms behind the always-NULL pBase are PORTED, not deleted (user ruling;
    physics-move-scope.md §4 says otherwise and is corrected in the same
    commit).
2.4 shell_constraints / shell_motioncontrollers / shell_physicsobjects:
    their bookkeeping (lists, break recording, decomposition) moves to
    managed. Native keeps only outbound IPhysicsEnvironment calls, done as
    direct vtable calls from managed where a layout row exists.
2.5 Player shadow: split shell_standalone_player.cpp's shadow arm, and move
    its decisions to PlayerShadow/PlayerShadowRecovery. IPhysicsPlayerController
    stays a held pointer.
2.6 physics_shared.cpp: collision-pair and solid defaults to managed;
    whatever the engine reads by layout stays.

## Phase 3: client ragdolls (client physics callbacks belong to ea)

Ownership (source-sdk-sharp-ea, 2026-10-11):
- **Fork client physics.cpp (cluster 9):** ea's input lane owns it
  (client-r-input, ../ss-client-input), which is porting the callbacks
  test-first. Not this plan's work. Tell ea before moving anything in
  src/shared/managed/Physics: the lane relocated two files there.
- **Ragdolls (cluster 7):** this plan owns ragdoll.cpp, ragdoll_library
  and ragdoll_shared. Coordinate with the input lane's ragdoll
  owner-tagging (game data on client ragdolls).
- **shell_standalone_client.cpp:** shared. ea's C7 lane (render batching)
  edits it and lands first. Message ea before touching the
  EnsureBones/MergeBonesFrom arm (Phase 1.0/1.2) so the two are serialised.

Work:
- ragdoll_t's IPhysicsObject pointers are held as handles.
- CRagdoll, RagdollCreate, ApplyAnimationAsVelocity, SolveSeparation and
  ragdoll_library's Create/StepPose/ApplyForce move to managed.
- The two ragdoll IVPhysicsKeyHandlers become managed-built tables.
- fork ragdoll.cpp and ragdoll_shared leave the client link
  (sdk-links/client.txt lines 175-176 and 270 shrink).

## Phase 4: gates

- Per commit: build + touched tests; zero warnings; the fork is clean after
  each run.
- Per phase: `make check`; one manual `make live-gate` (dm_lockdown + the
  second map load) at the phase tip, since SetupBones and physics callbacks are
  what single-load gates miss; then the daily gate's next report.
- End: `make sdk-census`; sdk-links lines deleted for every fully-ported
  file; a census table of what native stays and why (thunk / layout /
  pointer / registration / debug-only), and zero logic rows.

## Checkpoints (stop and report)

- 1.3 cost gate numbers.
- Any fork edit (e.g. plan_bones_phase_bc's bone_setup.h trim): route to the user.
- Before editing shell_standalone_client.cpp: serialise with ea's C7 lane.

## Phase 4 census (2026-10-12, main e0f8bfc42)

Landed, in order (each `make check` 13/13 at the merge):
- review fixes (Phase 0), physics_fx deleted (03b261d81);
- bones 1.0 / 1.0b / 1.1-1.3 server / bone_setup split / client bones / C1
  (a1b10eb15 .. fafe611f1), ragdolls (de3c1f33a), descriptor timing
  (ec57ebe8c): bone_setup.cpp, bone_library.cpp, ragdoll_shared.cpp,
  studio_sequence_timing.cpp and the bone headers deleted; fork ragdoll.cpp
  off client.so;
- physics 2.1 solver (4202f899b), 2.2/2.4 constraints and controllers
  (bc21e4d46), 2.3/2.5 frame and player shadow (8be875b6d), 2.6
  physics_shared.cpp deleted (4602cf071), InitPhysics + phys_convert port +
  Vector-by-value generator (775ae7f52), the last vphysics forwards and the
  phys-convert gate step (e0f8bfc42).

Native physics and bone code still in the tree, by reason:

| Reason | Pieces |
|---|---|
| Registration (engine-driven game system) | physics.cpp CPhysicsHook / PhysicsGameSystem (538 lines incl. the held globals), forwarding level and frame events to managed |
| Held pointer | CStandaloneEntity m_pPhysics (SwapPhysics, DestroyPhysics, EntityPhysicsObject); the player's shadow and controller pointers and DestroyVCollision; physenv / g_PhysWorldObject |
| vphysics callback / owner decode | shell_physicsobjects.cpp (130) owner and toucher lookups vphysics asks mid-step; physicsowner.h / physics_gamedata.h / physicslink.h; SourceSharp_PhysicsUpdateOwners |
| Layout | physics_shell.h (asserts), ss_physics.h, ss_physics_shared.h flag bits, ss_physsaverestore.h + shell_physsaverestore_absent.cpp, standalone_bone_trace.h, studio.cpp (the mdl layout the engine reads; no bone math) |
| Debug-only | physics_debug.cpp, physworld_match_rule.h |
| Registration and held pointers (client; 2026-10-18) | shell_physics_client.cpp: kClientSystems[]'s CPhysicsSystem row and PhysicsDLLInit / PhysicsReset / PhysicsSimulate, each a forward to IManagedPhysicsClient; physprops and physcollision, read by name by five shells |
| Carry-outs (client; IShimPhysicsClient) | shell_physics_client.cpp: a surfaceprop's impact audio, the impact emit, the splash's collide extent, point contents, lighting and three fx_water effects |

Client leftovers closed 2026-10-18 (lane client physics+bones): fork
game/client/physics.cpp left client.so (its decisions are ClientPhysicsSystem's);
ragdoll_library.cpp/.h, shell_physics_shared_client.cpp, the bone leaf library
(bone_leaf_library.h, shell_bone_leaf_library.cpp) and game_entity_relays.h's
client half were deleted; docs/client_evictions.md has each row.

Still deciding something natively: nothing. The four follow-ups listed at
e0f8bfc42 were closed by lane C2 (plan_physics_bones_perf), 2026-10-12, per
docs/todo.md's entry and re-checked on main 234984fe3: the owner update
writes every active object as stock's PhysFrame (no asleep skip);
shell_velocity_home.h is deleted (the ground velocity is a held value
managed hands over); the movetype is the block word managed writes; the
touched-moveable check is PlayerShadowBodies.TouchedMoveablePhysics. As
they stood at e0f8bfc42:
1. CStandaloneEntity::UpdatePhysics skips an asleep object's pose copy (inside the batched owner update): movable with a batched design.
2. StandaloneEntity_Velocity (shell_velocity_home.h) picks solver or member for the native movement fill, per trace: movable with a batched design.
3. StandaloneMoveType_Report calls any entity holding an object VPHYSICS, static included (a stock divergence): the movement lane's.
4. The player move-trace's "touched a moveable physics object" check (shell_standalone_player.cpp): the movement lane's.

Measured costs for the perf pass:
- bones 2-2.5x native per skeleton in release (Male_01 merge rebuilds every frame; no per-frame batching yet);
- ragdolls about 4 vs 2 us per ragdoll-frame (managed also settles and sleeps);
- motion Simulate about 2x;
- solver about 0.2 us per ask;
- GetVelocity about 190 ns vs 75 ns per call;
- the live-gate budget rows raised under the ruling.

