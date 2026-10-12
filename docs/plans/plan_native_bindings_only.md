# Plan: native code is the engine binding and nothing else

Status: DRAFT for review (2026-10-01). Not committed until the user has read it.
Measured at main 307edcb, debug build unless stated.

## 1. The goal, and what "binding" means

The user's goal (2026-10-01): reduce the native code to just the engine bindings.

A line of C++ is a BINDING when it does one of the following, and nothing else:

| kind | what it is | examples today |
|---|---|---|
| B1 vtable thunk | an object the engine or VPhysics calls through, whose methods forward to managed | the IServerGameDLL/IClientRenderable shells, CCollisionEvent |
| B2 fixed layout | memory the engine reads at fixed offsets | the engine block, entity slots, CPlayerState, SendTables |
| B3 registration | handing the engine something at load or static init | EXPOSE_INTERFACE, factories, user-message registration |
| B4 outbound call | calling an engine interface on managed's behalf, through a generic bound interface | IShim* areas |
| B5 host | starting CoreCLR and dispatching into it | src/host |

Everything else is LOGIC, and it moves to C#: anything that decides, computes, parses, iterates or keeps game state.
That is CLAUDE.md's existing rule ("native code may carry out a decision; it must never make one") applied to the
whole tree rather than one port at a time.

## 2. Where the tree stands (307edcb)

### Our native code (the shim)
200 files, 122,159 lines by `SS_REASON` letter (docs/shim-reasons.md), plus about 49.5K lines of headers with no
letter. Total about 171K lines.

| letter | means | lines | in binding terms |
|---|---|--:|---|
| a | vtable the engine/VPhysics calls | 46,457 | B1, but most of these files also hold logic |
| b | interface resolved by name | 15,770 | B1/B3 |
| c | static-init registration | 6,613 | B3 (and the debug-only diagnostics, which leave release) |
| d | layout read at fixed offsets | 11,002 | B2 |
| e | synchronous mid-step answer | 8,638 | B1 thunk + LOGIC to move |
| f | bit-level wire codec | 5,541 | B2-adjacent; decide per codec (§6) |
| g | leaf math over mdl/bsp data | 20,123 | LOGIC |
| h | CoreCLR host and dispatch | 4,805 | B5 |
| x | no letter fits | 3,210 | LOGIC until proven otherwise |

The biggest single files:
- shell_standalone_client.cpp: 7,004 lines (a)
- bone_setup.cpp: 6,213 (g)
- shell_effects_sim.cpp: 3,447 (a)
- collisionutils.cpp: 3,363 (g)
- shell_standalone_base.cpp: 2,940 (a)
- physics.cpp: 2,875 (e)
- shell_worldquery.cpp: 2,317 (e)
- shell_impact_effects.cpp: 2,218 (g)
- shell_trail_sim.cpp: 2,064 (a)
- shell_te_backends_client.cpp: 2,114 (a)

### Valve code still compiled
- server.so: none. 132 objects in debug, 110 in release, all ours (plan_server_novalve P5, closed).
- client.so: 157 stock fork .cpp files (127 game/client, 14 game/shared, 16 public), about 87.6K lines, beside
  101 shim objects (docs/entity-census.txt §1, sdk-links/client.txt).

So the work is in three parts:
1. logic inside our server shim;
2. logic inside our client shim;
3. the client's stock Valve code.

## 3. Phases

Every phase follows the house rules:
- one reviewable commit per unit;
- make check, and the live gate when behaviour can change;
- ledgers and ratchets in the same commit;
- each move lands with the tests that pin it;
- "move first, measure after": a crossing budget that blows is a boundary-design task (batching,
  [SuppressGCTransition]), not a reason to stay native.

### N0: classify every function
A function-level ledger like tools/physics-scope (fnmap.tsv plus rulings.tsv), for the whole shim:

- every function's kind (B1 to B5, or LOGIC) and its line count;
- a ratchet on LOGIC lines, which may only fall;
- the gate that keeps it honest: a binding-classified function that grows a branch on game state is flagged.

The exit is a number for "logic left native", with a per-file list. This replaces guessing which file to do next.

### N1: server logic out
Server first, because it has no Valve code left and no rendering budget. In size order:

1. **physics.cpp's decisions.** Collision-event bookkeeping, friction sounds, damage-from-impact and the
   penetration rules move to C#. This includes every stock path behind `PhysicsOwner_t::pBase`, which is NULL
   for every server entity, so the stock behaviour never runs for managed entities. It is PORTED, never deleted
   (user ruling 2026-10-01). The list:
   - wake and sleep watchers;
   - ShouldSolvePenetration;
   - ShouldFreezeObject (debris crush);
   - PostCollision → VPhysicsCollision: impact damage, impact sounds, shadow collision;
   - Friction: scrape sounds and dust;
   - AddDamageInflictor: velocity restore on a killing impact;
   - physics Start/EndTouch between entities, with the contact count.

   Today the managed PhysicsCollisionResponse does shake and dust only; touch, wake and sleep are counted, not
   acted on. Each native arm leaves only after its managed port lands with tests. The VPhysics callback classes (CCollisionEvent, the solver interfaces) stay as B1
   thunks. Mid-step answers such as ShouldCollide become a thunk calling managed through an
   [UnmanagedCallersOnly] entry with [SuppressGCTransition]; per-call cost is measured on dm_lockdown.
2. **shell_worldquery.cpp:** trace and query composition moves to C#; the engine calls stay as B4.
3. **shell_standalone_base/player/hierarchy:** entity behaviour already lives mostly in C#, so what remains is
   per-op bookkeeping. Move it and keep the vtable shells.
4. **shell_motioncontrollers, shell_constraints, shell_scripted, world_adapter:** the same treatment.

### N2: shared leaf math to C#
TEST FIRST (user ruling 2026-10-01). Before any bone code moves:
- write facts that pin stock's results (bone matrices, IK, attachments, hitboxes) against the oracle;
- write the events the engine must see (SetupBones answers, hitbox traces);
- show them red against an empty managed implementation;
- port until they are green.
Bone setup (bone_setup.cpp, studio.cpp, bone_library), collisionutils and the studio model readers are pure
maths over mdl/bsp data. They move to a managed library (System.Numerics / Vector128), shared by both sides.

The engine-facing entries stay as thin B1 thunks: IClientRenderable::SetupBones, the server's hitbox and
bone-merge asks.

The risk is per-frame cost on the client, one SetupBones per visible animated entity per frame. The boundary is
designed for batching from the start: one crossing per frame that sets up every dirty skeleton, not one per
entity. Measured against today's native cost before the old code is deleted.

### N3: client shim logic to C#
The effect simulators and renderers (shell_effects_sim, shell_trail_sim, shell_dust/smoke/precipitation/
sprite/steamjet render and sim, shell_impact_effects, shell_te_backends_client, shell_soundscape_client) are
logic around a small number of engine draw and particle calls.

The simulation moves to C#. Rendering keeps a B4 surface: mesh and material calls, batched per frame.
shell_standalone_client.cpp (7K) gets the N1.3 treatment.

### N4: the client's 157 stock Valve files
This is a client-novalve plan, the analogue of plan_server_novalve. Per file, as the server's census did:
- the ones only stock code reaches go;
- the ones the engine needs get a B1/B2 shim;
- the rest port to C#.

A separate plan document when N1 to N3 are under way. Its first step is the same census the server's P0 took.

### N5: measure and close
Re-measure §2's numbers and the release binary sizes; then the before/after table. The LOGIC ratchet from N0
should read zero, or list each named, ruled exception.

## 4. What stays native for good
The B1 to B5 kinds, and:
- the f-letter codecs the engine itself calls (SendProxy/RecvProxy functions; usercmd's bf_read/bf_write layout);
- the debug-only diagnostics (*_debug.cpp), which are tooling, not shipped code.

Everything else on this list is expected to reach zero.

## 5. Risks
- **Per-frame crossing cost** (N2 bones, N3 effects). Mitigated by batching designed in, measured before
  deleting native code; CLAUDE.md's rule is that a blown budget is a boundary-design task.
- **Listen server.** Client and server share one CoreCLR. Managed state per side stays separated by DI, as today.
- **Hot reload (cs_sv_reload).** Managed code that now owns state the engine reads must survive a reload:
  plan_managed_vtable_reload's rules apply to every new managed-built interface.
- **No exceptions.** -fno-exceptions (user ruling) is easier as native shrinks; do it early (it is independent).

## 6. Decisions for the user
1. **Codecs (letter f).** Do the wire codecs count as binding? (Proposed: yes for what the engine calls, e.g.
   proxies; encode/decode logic C# calls on its own behalf moves.)
2. **Order.** Proposed: N0 → N1 → N2 → N3, with N4's census running alongside N3. Alternatively start with N2
   (bones), the biggest single logic block.
3. **Performance policy.** For a per-frame path, may a move land at a measured cost increase, to be optimised
   after (CLAUDE.md's "move first, measure after")? Or must it be cost-neutral before the native code is deleted?
