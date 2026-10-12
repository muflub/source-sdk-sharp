# Plan: every stock physics case works for managed entities

Status: DRAFT for review (2026-10-01). Not committed until the user has read it.
Base: server-novalve at d179270 + 56c4e5e. The case-by-case audit is in
`docs/plans/physics_stock_audit.md` (stock blob 22288b9; case ids A1..O used below).

## Why
User, 2026-10-01: "go back to stock for the port and make sure every case is hooked up properly with managed
entities". Ruling of the same day: stock behaviour behind a dead CBaseEntity arm is ported, never deleted.

The audit found 11 cases WORK, 5 PARTIAL, 7 MISSING behind a dead `pBase` arm, 11 MISSING outright, and 8 N/A
(HL2 NPC, physcannon, vehicles, ragdoll solver).

Two facts carry the rest:
1. `PhysicsOwner_t::pBase` is NULL for every server physics object. The "measured 0" notes in physics.cpp
   (penetration 19,302 asks, freeze, touch) count that arm itself, so they are circular evidence.
2. `PhysicsOwner_t::GetMoveType()` is the constant MOVETYPE_NONE, and the collision record copies it, so
   `PhysicsCollisionRules.Responds` has refused every live collision: the managed shake and dust response
   has never fired in a game. Its xUnit facts pass only because they set MoveType.VPhysics by hand.

## Rules for every step
- TEST FIRST (user ruling 2026-10-01): for each case, write the tests that pin stock's logic and events BEFORE the
  port. These are xUnit facts with stock's numbers, gtests for native carry-out, and a live-gate or bisect check
  that the event reaches its consumer. Run them on the unported tree and keep the red output; then port until they
  are green. They land in one commit with the red evidence quoted, so make check stays green at every commit.
- One case (or one named group) per commit. Each commit lands with a test that is red before it: an xUnit fact
  for a managed decision, a gtest for native carry-out, and a live-gate or bisect reading for a game effect.
- Managed decides; native carries out (CLAUDE.md). Where stock decides inside a solver callback (penetration H,
  freeze G, ShouldCollide inputs A2/A6), native reads the facts and asks managed through an
  [UnmanagedCallersOnly] + [SuppressGCTransition] entry. Each such entry gets a measured per-call cost on
  dm_lockdown. The exception is ShouldCollide's pair facts (A2/A5/A6): those are block reads into the existing
  rule table, which is already managed-authored data.
- Event-driven cases drain after the step through the existing ring (one crossing per frame).
- ABI: new slots are appended only (SourceSharpEngineApi + Abi in the same order), with record-layout and parity
  tests updated in the same commit.
- A native dead arm (`.pBase` path, refusal object) leaves only in the commit whose managed port makes the case
  work, never before.
- Test pins that hold deletions absent (`physics_move_scope_test.cpp:480-527`, `docs/physics-move-scope.md`)
  are re-ruled in the commit that restores the helper, with a dated note.

## Steps
1. **C2: real move type.** VPHYSICS for a managed entity that owns the object, and the player's own move type
   for the shadow. No ABI. Proof:
   - a gtest on FillCollisionSide;
   - live: `cs_sv_physics_events` responded > 0 on dm_lockdown, quoted with the tip (0 before);
   - C4 (shake) and C5 (dust) go live with it.
2. **E1: contact touch.** Buffer the pair, then after the step call `PhysicsOwner_t::StartTouch`/`EndTouch` both
   ways, with stock's contact count on EndTouch. No ABI. Deletes the two refusals. Live proof: a thrown crate
   presses a func_button / touches a trigger-less item.
2b. **E1b: contact touch with the world.**
   - Stock marks a prop resting on the floor as touching worldspawn too.
   - Toucher_t names an entity on one of the two arms, and the world is on neither, so E1 dispatches
     entity-entity pairs only.
   - Needs a world toucher and a check of what managed entities do with a world touch (stock props mostly
     ignore it).
   - Test first: a crate on the floor lists the world (index 0) in its touch links.
3. **Ring consumers, no ABI:**
   - F2: phys_constraint wakes its partner;
   - B3: interactive debris becomes debris on sleep;
   - B4: prop_physics OnAwakened / OnOutOfWorld / m_bAwake;
   - I2: per-entity in-fluid set, IsFloating, water type.

   One commit each. PhysicsEventsCommand's "nothing consumes" marks go as each kind gains a consumer.

   Progress and findings (2026-10-01):
   - B4a is done: start asleep (OnPhysicsCreated, a new generic EntityOp) and OnAwakened once. The ring reaches
     entities through PhysicsEventRouter.
   - B4b is done: OnOutOfWorld on every VPhysicsUpdate. Native records an Update ring event per active managed
     object in PhysFrame's loop, delivered at the next drain (one frame late), and routed to OnPhysicsUpdate.
   - B3 is deferred. The managed CollisionGroup has no InteractiveDebris (it reads back as None), and nothing in
     this build enters it; stock's HL2MP users are server ragdolls with physics and the physcannon.
   - F2 is N/A. The constraint watchers are under HINGE_NOTIFY, which is HL2_EPISODIC, off in HL2MP.
   - I2 moves to step 8: its only HL2MP consumer is impact damage's floating energy scale.
   - B4c, m_bAwake networking and the thrown-by-player flag, is left. Check first whether the client reads it.
   - "Hangs until woken" needs a map-placed start-asleep prop (ent_create teleports, which wakes it). It goes in
     the sandbox map's coverage pass.
   - Found: ScriptedEntityRegistry.PhysicsCollision has no caller, so OnPhysicsCollision overrides
     (PhysMagnet's attach-on-impact) never run. It belongs to step 8.
4. **A2 + A5 + A6: ShouldCollide pair facts.** Owner pair, real solid type and flags, the player's
   MASK_PLAYERSOLID, and the func_clip_vphysics force bit, all in one commit (A6 alone breaks
   FuncClipVphysics). A3 (move-parent) is added if the hierarchy uses it.
   Step 4 follow-up: a live check that a crate stops on a dm_lockdown func_clip_vphysics while a player walks
   through. It needs the brush's footprint (/map/geometry or a bounds field on /entities). The filtername key
   (a managed filter asked mid-step) remains a stated divergence; no shipping map uses it.
5. **H: penetration.**
   - Native: the handle-keyed penetrate ledger, the FVPHYSICS_PENETRATING game flag, and PhysForceEntityToSleep
     restored.
   - Managed decides: the 3 s give-up, and the player/shadow exemptions.
   - Prerequisite for B5, B7 and the shake rule's PENETRATING test.
6. **G: freeze crush.** Native restores FindMaxContact and queues DMG_CRUSH through the existing damage queue
   (J); managed decides whether the frozen debris is crushed or removed.
   Progress (2026-10-02):
   - H is done. penetration_ledger.h holds stock's 3 s / 1 s ladder as an engine-free state machine; native
     evaluates it, as the collision rule table is evaluated. Follow-up for review: make the give-up a managed
     mid-step answer if the native-decides line should hold here too.
   - TRYENTITYSOLVER (CPhysicsEntitySolver for stuck debris) is deferred.
   - G is done. ShouldFreezeObject crushes frozen debris (find_max_contact.h, DMG_CRUSH through the physics
     damage queue); PropPhysics removes an undamageable prop gib. A non-gib's entity solver is deferred, with
     TRYENTITYSOLVER.
   - G has no live check yet: it needs a pile that makes vphysics freeze an object.
7. **C3 + L: impact and break sounds.** Two appended slots feeding the batches that already exist in
   physics.cpp / ss_physics_sound.h.
8. **C6 + C7 + C8 + C10: impact damage.**
   - The collision record grows: pre/post linear and pre angular velocity per side, inertia, and a shadow bit.
   - Managed ports CalculatePhysicsImpactDamage and the three stock tables, with oracle facts against stock
     numbers.
   - It covers prop, breakable, surface and physbox damage; the inflictor velocity restore on a kill;
     forcetoenablemotion; and the player hurt by moving props.
9. **D2: friction scrape sound.** One generic looping-sound slot; the managed policy is pure.
10. **B5 + B7: the player and the solver.**
    - **Design note (2026-10-02, before building B7).** Stock calls
      `VPhysicsShadowUpdate` once per PHYSICS frame, from `PhysFrame`'s walk of
      `g_pShadowEntities` after `Simulate`, when the player's origin is still the
      target the shadow was given. Ours refuses that list, and the command loop's
      `PlayerShadow.Advance` (lane defect-shadow) approximates the reconcile per
      COMMAND, after that command's movement, against last frame's shadow -- which
      is why it needs the "leaning" filter and compares the commanded rather than
      the achieved velocity. B7 therefore:
      1. native `PhysFrame` records a `ShadowUpdate` ring kind for each player
         whose active hull is awake (the shadow list's walk, as a fact);
      2. the shadow probe gains the facts stock reads: GetShadowPosition's
         "updated", GetShadowVelocity, WasFrozen, the hull's PENETRATING flag,
         the ground's physics mass and the player's;
      3. a managed `PlayerShadowRecovery` service (DI, per-player old origin and
         frozen latch) makes stock's whole decision -- touched, frozen recovery,
         the dist/vel error arm with its impulse and move, the in-error stuck
         check -- with the traces as managed hull traces;
      4. Advance is deleted in its own commit once the live push step shows the
         jam gone (it is the replacement's subject), with numbers.
    - **Measured (2026-10-02), and Advance stays.** Push step x10, walk < 100
      units = jammed: Advance only (6d7cd89) 4/10; B7 + Advance 2/10; B7 alone
      (sourcesharp_shadow_advance 0) 8/10 -- the rate lane defect-shadow
      measured with neither (66/89). B7 runs (480 shadow updates, 108 moves, 96
      impulses in one push round), so the reverse update is not what jams: the
      forward aim is. PlayerShadow.Plan's softened aim (LeaningAimWeight 0.5,
      half the shortfall ahead of a blocked player) is not stock's
      UpdateVPhysicsPosition, and the lane traced the deadlock to it. Deleting
      Advance would take the jam from 4/10 to 8/10. NEXT: port
      UpdateVPhysicsPosition's aim to stock and re-measure with Advance off;
      delete Advance only when that is at or under 4/10.
    - **Correction (2026-10-02, afternoon).** The two notes below blame the
      forward aim and a lagging teleport. Both were wrong: PlayerShadow.Plan's
      softened aim IS stock's PostThinkVPhysics arithmetic (player.cpp:4793-4796),
      short only of stock's `!pPhysGround` test, and stock's setpos (client.cpp:1258)
      only sets the origin too. Still open: why our reconcile fires (112 moves in the
      trigger step, no prop) where stock's would not -- suspects are the contact bits
      (stock's m_touchedPhysObject: a moveable VPHYSICS touch, IsInContact, a
      penetrating mover) and the stuck check's trace. Diagnose on the B7 branch.
    - **B7 parked on branch b7-shadow-update-wip (1c6a5ce), not merged.** Green
      in isolation (make check, its own facts and live check), but the full gate
      fails trigger's OnEndTouch and prop-hurts-player in both passes: with our
      forward model the shadow trails a setpos and leads/lags a walk, so the
      reconcile fires constantly (112 moves in the trigger step) and drags the
      player back. ORDER CHANGE: the forward aim (UpdateVPhysicsPosition, and a
      teleport moving the shadow) is ported first, then B7 is re-gated, then
      Advance is retired on numbers. This touches lane defect-shadow's measured
      PlayerShadow.Plan, so it waits for the owner's review.
    - **DONE (2026-10-03).** B7 landed as 2182466e. Its gate failures were
      stock's own reconcile reacting to plain `setpos` leaving the shadow
      behind; 0b76b768 made the gate teleport as stock's `setpos_exact` does
      (shadow carried, controller re-aimed). Advance is retired: push x10 with
      B7 alone 0/10 jammed, B7 + Advance 2/10, Advance alone (before B7) 4/10.
      Resume notes: `physics_parity_resume.md`.
    - Stress crush: a `Stress` ring kind from a native snapshot walk.
      **B5 built (2026-10-02).** object_stress.h ports CalculateObjectStress as
      engine-free arithmetic (14 gtests, red against a stub); the player's
      awake hull reads its friction snapshot after the step and records
      SS_PHYSEVENT_STRESS = 14 (B7's ShadowUpdate renumbers to 15); managed
      SsPlayer.OnVPhysicsStress applies stock's rule (1/8 scale, five body
      weights, large non-static pusher, not penetrating) and queues 200
      DMG_CRUSH. Divergences: attacker None (no world attacker on the ABI), no
      damage force (the ABI's direction is a blast direction), friendly is
      player-held only (no relationships or nav-ignore in HL2MP). Live step
      `stress`: a 30 kg crate resting on the head is felt as non-static stress
      and does not crush; red with the native record removed.
      **FINDING (resolved below): a crate heavy enough to crush never touches the shadow.**
      At massScale 100 (about 3,000 kg) and 300, set a unit above the head, it
      falls through the player to the floor in under 0.5 s with no contact in
      the shadow's friction snapshot; a 30 kg crate is reported every frame.
      The push-mass limit (350) only clamps the controller's push speed in
      vphysics, so it is not the cause. Suspect the per-command shadow
      teleport (PlayerShadow.Plan) handing the solver a penetration it resolves
      by letting the crate through. Needs its own diagnosis before a live crush
      can be staged.
      **Diagnosed (2026-10-02): the threshold is the push-mass limit, so it is
      stock's.** Bisected by mass on the set-down crate: 300 and 330 kg rest on
      the head (felt as stress), 390, 900, 3,000 and 9,000 kg fall through. The
      idle shadow sleeps (stock's CPlayerController::Update also stops
      simulating it when nothing changes) and the heavy crate never wakes it.
      Stock sets the same 350 kg (player.cpp SetPushMassLimit), and the only
      reader of it in stock's game code and in vphysics is the
      controller's push clamp, inside the same vphysics.so. Not a port
      divergence, and no snap or teleport is involved (snaps unchanged across
      the drop). Consequence: a resting crush needs at least 3,400 kg of
      received stress, which no single object over 350 kg can put on a player
      this way. It takes something pinning a heavy object onto them (a lift, a
      door, a stack). The live step stays "felt, not crushed".
    - Reverse shadow update: the solver pushes the player; the shadow probe state is extended and
      PlayerShadow decides.

## Out of scope (N/A, from the audit)
A4/B6 (AI movers with shadows: none yet), C9 (physcannon/NPC), K (impulse queue mechanism), N (multi-object
inflictors), O (ragdoll solver, vehicles). Each is listed in the audit with its reason.

## Gates
make check per commit. Live gate for every step from 1 on (each changes runtime behaviour), plus each step's own
live reading. The push step's known jam (debug-regressions lane) is quoted as a rate, not waved through.
