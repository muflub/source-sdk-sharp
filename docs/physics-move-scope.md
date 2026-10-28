# `physics.cpp`'s policy move — the honest scope

`plan_shim.md` §4a item 1. **Lane `5a-physresidue`, measured at `5034fb3e6`.
This is a SCOPE and nothing was executed.**

The item was written as "`physics.cpp`'s ~1,011-line policy move". That figure
did not survive measurement, the plan says so, and a previous lane refused both
to execute it and to guess a replacement number. This document is the list that
refusal was waiting for: **function by function, what moves, what cannot, and
why.**

The short answer is that **no whole function moves**, and that is a finding
rather than an evasion. The movable content is not function-shaped. Section 4
says what it is instead.

---

## 1. The file, measured

```
$ wc -l src/server/native/physics.cpp        (farm: src/sourcesharp/game/server/physics.cpp)
2994
$ ./5a-codelines.sh src/server/native/physics.cpp
raw=2994  code=1523  comment=1131  blank=340
```

So: **229 raw / 161 code / 26 comment / 42 blank.** Prose was **60.2% of
the non-blank lines** (283 of 470, when this was first written), not the 49% the plan quotes; the plan's
figure is comment-over-code (283/187 = 151%) or a different stripper. Either
way the point it was making stands and gets stronger: **less than half of this
file is code.** (At the server gc cull, before the debug-file move below, it
was 3,663 raw / 1,813 code / 1,460 comment / 390 blank, 44.6%.)

> **Re-measured at the close of `plan_shim.md` (lane 7a-close), and this
> is the whole of the change.** §8's ratchet 2 put one `SS_REASON` marker line
> into every shim translation unit, including this one, so the file went from
> 4,291 raw to 4,292 and from 1,631 comment lines to 1,632. **`code` did not
> move**: it is 2,206 before and after.
>
> The scope was re-derived rather than adjusted, because a test that can be
> made green by editing a number is not a test. `tools/physics-scope/fnmap`
> was re-run over the new file and its 118 rows are **identical in function and
> in body line count**; every start and end line moved by exactly +1 and
> nothing else changed:
>
> ```
> $ paste <(cut -f1 tools/physics-scope/fnmap.tsv) <(cut -f1 new.tsv) \
>     | awk '{d[$2-$1]++} END {for (k in d) print k, d[k]}'
> 1 118
> $ diff <(cut -f3,4 tools/physics-scope/fnmap.tsv) <(cut -f3,4 new.tsv)
> (no output)
> ```
>
> `tools/physics-scope/fnmap.tsv` is updated to the re-run, so the committed
> input matches the file it describes. No ruling changed and no total below
> changed.

> **Re-measured when the file came into this repository (2026-09-27), and no
> total moved.** The fork's src/sourcesharp/ is kept local by user decision, so
> physics.cpp now lives at src/server/native/physics.cpp (the farm path is
> unchanged) and carries the attribution header (tools/valve-header apply):
> the 9-line Valve banner became the 6-line header plus its two content lines
> (SS_REASON, Purpose), so 4,310 -> 4,309 raw and 1,640 -> 1,639 comment;
> code and blank did not move. Re-derived the same way:
> `tools/physics-scope/fnmap` over the new file gives the same 118 functions,
> every start and end line moved by exactly -1, and the body counts are
> identical; `tools/physics-scope/classify` still exits 0 with identical
> totals (2,136). `tools/physics-scope/fnmap.tsv` is the re-run.

> **Re-measured again by lane `g-vdisp`, and this one DID move a total.**
> `SourceSharpReportObjectsOutOfBounds` stopped asking vphysics'
> `CollideGetAABB` for a virtual-mesh (displacement) collide, which segfaults
> inside vphysics: it now collects the displacement terrain's collides
> first and skips them. That added 18 lines to the file -- 9 code, 8 comment,
> 1 blank -- all inside that one function, so 4,292 -> 4,310 raw, 2,206 ->
> 2,215 code, 1,632 -> 1,640 comment, 454 -> 455 blank.
>
> Re-derived the same way: `tools/physics-scope/fnmap` over the new file gives
> the same 118 functions; the ONLY body-count change is that function's 64 ->
> 73, 22 rows before it are unmoved and the 96 after it moved by exactly +18.
> `tools/physics-scope/classify` over the re-run still exits 0 with no ruling
> changed (the function was already `INSTRUMENT`), and the only totals that
> moved are `INSTRUMENT` 482 -> 491 and the grand total 2,127 -> 2,136:
>
> ```
> $ paste <(cut -f1 tools/physics-scope/fnmap.tsv) <(cut -f1 new.tsv) \
>     | awk '{d[$2-$1]++} END {for (k in d) print k, d[k]}'
> 18 96
> 0 22
> $ diff <(cut -f3,4 tools/physics-scope/fnmap.tsv) <(cut -f3,4 new.tsv)
> 22c22
> < 64	static void SourceSharpReportObjectsOutOfBounds()
> ---
> > 73	static void SourceSharpReportObjectsOutOfBounds()
> ```
>
> `fnmap.tsv` is updated to the re-run. The raw/code/comment/blank split was
> taken with a counter that reproduces 4,292 / 2,206 / 1,632 / 454 exactly on
> the file before the change.

> **Re-measured when the octree audit was removed (2026-09-28, user ruling),
> and this one moved totals.** The audit read vphysics' OV-node hash through
> three private IVP field offsets; it went with its command
> `ss_physworld_ovtree`, its pointer-sanity helper and its two load-time
> readings in `CPhysicsHook`. Its public-API half -- the walk for objects at a
> non-finite or out-of-map position -- stays, now reached by a command of its
> own, `ss_physworld_bounds`. 4,309 -> 4,172 raw, 2,215 -> 2,150 code,
> 1,639 -> 1,578 comment, 455 -> 444 blank (the same counter reproduces the
> old four exactly).
>
> `tools/physics-scope/fnmap` over the new file gives 116 functions, not 118:
> `SourceSharpLooksLikeHeapPointer` (4) and `SourceSharp_PhysWorldOvTreeReport`
> (63) are gone, `ss_physworld_ovtree` (4) became `ss_physworld_bounds` (9),
> and `CPhysicsHook::LevelInitPreEntity` / `LevelInitPostEntity` each lost the
> one line that took a reading (44 -> 43, 7 -> 6). Every other body count is
> identical. `tools/physics-scope/classify` exits 0 after the three rulings
> were updated to match; `INSTRUMENT` 13 / 491 -> 11 / 429, `HOOK` 192 -> 190,
> total 118 / 2,136 -> 116 / 2,072. `fnmap.tsv` is the re-run.

> **Re-measured when the server's commands and ConVars were declared in C#
> (plan_server_novalve.md P2.3c).** Every `CON_COMMAND( name, "help" )` header
> became `SS_NATIVE_COMMAND( name )` -- the help text and flags moved to
> `SourceSharp.Server.NativeCommands` -- and `air_density`'s static
> `ConCommand` became a one-line body of its own. The three ConVars
> (`phys_speeds`, `phys_timescale`, `sourcesharp_physworld_report`) are C#'s,
> read here through `CManagedConVarRef`s, and `TimescaleChanged` became
> `PhysTimescale()`, which reads the scale and resets the simulation clock on
> the first frame that sees a new value. 4,172 -> 4,188 raw, 2,150 -> 2,152
> code, 1,578 -> 1,588 comment, 444 -> 448 blank; the counter was rebuilt and
> reproduces the old four exactly. Body counts: `PhysTimescale()` 12 against
> `TimescaleChanged`'s 7, `ss_physworld_object` 49 -> 47 and
> `ss_physworld_penetration` 9 -> 8 (their multi-line headers), `air_density`
> 1 (new). Three ruling keys were re-spelled to the new heads and one added.
> `INSTRUMENT` 11 / 429 -> 11 / 431, `STOCK-DEBUG` 13 / 252 -> 14 / 253,
> total 116 / 2,072 -> 117 / 2,075. No verdict changed: an instrument is still
> reached by name, now by a name C# registers.

> **Re-measured when the surface property manifest moved to C#
> (plan_server_novalve.md P2.4).** `CPhysicsHook::Init` no longer calls
> `PhysParseSurfaceData` (SourceSharp.Server.SurfacePropertyManifest reads the
> manifest and hands VPhysics each file); one code line became four comment
> lines. 4,188 -> 4,191 raw, 2,152 -> 2,151 code, 1,588 -> 1,592 comment;
> `CPhysicsHook::Init` 16 -> 15, so `HOOK` 190 -> 189 and the total
> 117 / 2,075 -> 117 / 2,074. No ruling changed.

> **Re-measured when tier1's string calls were rewritten onto ss_string.h and
> the C library (2026-09-29, server.so off strtools.cpp).** Each `V_snprintf`,
> `Q_strncpy`, `Q_strncat` and `nexttoken` call became its `ss::` helper on the
> same line, and one file-scope `#include "ss_string.h"` was added. 4,191 ->
> 4,192 raw, 2,151 -> 2,152 code; comment and blank unchanged. `fnmap` over the
> new file gives the same 117 rows with the same heads and body counts, every
> start line one later; `classify` still totals 117 / 2,074 with no row
> unmatched or ambiguous. No ruling changed.

> **Re-measured when VProf was deleted (2026-09-30, plan_server_novalve.md P4:
> server.so off tier0).** The `tier0/vprof.h` include and the `VPROF_BUDGET`
> line at the top of `CPhysicsHook::FrameUpdatePostEntityThink` went, with the
> blank line after it. 4,192 -> 4,189 raw, 2,152 -> 2,150 code, 448 -> 447
> blank; comment unchanged. `fnmap` gives the same 117 rows and heads;
> `FrameUpdatePostEntityThink` 12 -> 11, so `HOOK` 189 -> 188 and the total
> 117 / 2,074 -> 117 / 2,073, no row unmatched or ambiguous. No ruling changed.

> **Re-measured when the server's recipient filters became ss:: types (2026-09-30,
> plan_server_novalve.md P5).** vphysics_sound.h's inline PlayImpactSounds /
> PlayBreakSounds build game/server's CPASAttenuationFilter, which server.so no
> longer defines, so `CPhysicsHook::PreClientUpdate` plays its queues through two
> file-local functions, `SourceSharp_PlayImpactSounds` (35) and
> `SourceSharp_PlayBreakSounds` (21), the same walks on `ss::CPASAttenuationFilter`.
> Both ruled HOOK. 4,189 -> 4,264 raw, 2,150 -> 2,206 code, 1,592 -> 1,603
> comment, 447 -> 455 blank. `fnmap` gives 119 rows (the 117 with the same heads
> and bodies, plus the two); `classify` totals 119 / 2,129, HOOK 9 / 188 -> 11 /
> 244, no row unmatched or ambiguous. No other ruling changed.

> **Re-measured after the server gc cull (2026-10-01, user ruling: what the
> linker drops is removed), and this is the deletion section 5 was waiting
> for.** server.so's `-Wl,--print-gc-sections` link discarded 34 of physics.cpp's
> functions -- every `DEAD` (23), `DEAD-VIA` (6) and `STALE-KEEP` (4) row, and
> one `LIVE-API` row, the named `PhysFrictionSound` overload, whose one importer
> (physics_shared.cpp's numeric overload) is itself unreached -- and they are
> deleted. 4,264 -> 3,663 raw, 2,206 -> 1,813 code, 1,603 -> 1,460 comment,
> 455 -> 390 blank (a counter that reproduces 4,264 / 2,206 / 1,603 / 455 on
> the file before). `fnmap` gives 83 rows; `classify` totals 83 / 1,736 with no
> row unmatched and no ruling ambiguous. The 36 rulings that matched nothing --
> the 34 deleted functions and two `PhysCallbackDamage` keys that had stopped
> matching when its parameter became `ss::CTakeDamageInfo` -- left
> rulings.tsv. No surviving ruling changed.

> **Re-measured when the diagnostic console commands moved to a debug-only
> file (2026-10-01, user ruling: "move debug commands to debug files and only
> include them for compile in cmake for debug builds").** Ten commands left
> physics.cpp for `src/server/native/physics_debug.cpp`, which CMake compiles
> only for a debug build (`server_sourcesharp_debug_sources`): the six
> `INSTRUMENT` commands (`ss_physworld_report`, `_recheck`, `_bounds`,
> `_object`, `_penetration`, `cs_sv_physics_shell`) and the four stock
> `STOCK-DEBUG` ones (`physics_highlight_active`, `physics_report_active`,
> `physics_select`, `physics_budget`), with every helper only they used --
> `SourceSharpReportObjectsOutOfBounds`, `IterateActivePhysicsEntities`,
> `CallbackHighlight`, `CallbackReport`, `CConstraintFloodEntry`,
> `CConstraintFloodList`, `MarkVPhysicsDebug`, `PhysicsCommand` and
> `PhysForceEntityToSleep`. 19 rows left `fnmap.tsv`, their rulings left
> `rulings.tsv` (kept there as `#` comments), and none was re-ruled on the way
> out. `air_density` stays, by the same ruling.
>
> What stayed had to be reachable from the new file without becoming a global:
> `SourceSharp_PhysWorldReport` and `PhysWorldReportEmit` stay here, because
> `CPhysicsHook::LevelInitPostEntity` still runs the report when
> `sourcesharp_physworld_report` is on, and lost their `static` so that
> `physics_internal.h` can declare them (the key `static void
> SourceSharp_PhysWorldReport` was re-spelled to the new head). Two accessors
> were added and ruled: `PhysicsPenetrationCounts` (6, `INSTRUMENT`) reads
> `ShouldSolvePenetration`'s three counters for `ss_physworld_penetration`, and
> `PhysicsCollisionEvents_BufferTouchEvents` (4, `STOCK-DEBUG`) is
> `physics_budget`'s one reach into `g_Collisions`. The state stays here.
>
> 3,663 -> 2,875 raw, 1,813 -> 1,390 code, 1,460 -> 1,173 comment, 390 -> 312
> blank (the counter reproduces 3,663 / 1,813 / 1,460 / 390 on the file
> before). `fnmap` gives 66 rows -- the 83, less the 19 that moved, plus the two
> accessors -- with every surviving head's body count unchanged; `classify`
> totals 66 / 1,315 with no row unmatched, no ruling ambiguous and none unused.
> `INSTRUMENT` 11 / 431 -> 5 / 244, `STOCK-DEBUG` 14 / 253 -> 3 / 19; every
> other row is unchanged. **Nothing moved to managed code**: this is a
> relocation inside native, and the new file is not ruled here.

> **Re-measured at physics stock parity E1 (2026-10-01): contact touch is
> ported.** The two printed refusals in `DispatchStartTouch`/`DispatchEndTouch`
> became five-line dispatches through `PhysicsOwner_t` (both entities, both
> ways), the touch queue stores entity handles instead of entity pointers, and
> `StartTouch`/`EndTouch`/`CountPhysicsObjectEntityContacts` resolve owners on
> the arm managed entities are on instead of the dead `.pBase` one.
> 2,875 -> 2,773 raw, 1,390 -> 1,352 code, 1,173 -> 1,114 comment, 312 -> 307
> blank (the counter reproduces 2,875 / 1,390 / 1,173 / 312 on the file
> before). `fnmap` gives the same 66 functions; six bodies changed
> (DispatchStartTouch 26 -> 5, DispatchEndTouch 18 -> 5, UpdateTouchEvents
> 36 -> 40, CountPhysicsObjectEntityContacts 15 -> 14, EndTouch 30 -> 23,
> AddTouchEvent unchanged at 12 with a new signature). `classify` exits 0 with no
> ruling changed: `SLOT-HELP` 433 -> 402, `SLOT` 336 -> 329, total 1,315 ->
> 1,277. Code fell by 38 and the bodies by 38, so all of it was inside
> functions.

> **Re-measured at physics stock parity B4b (2026-10-02).** PhysFrame's
> active-object loop records an Update ring event for each managed entity's
> own object (stock's per-tick VPhysicsUpdate reaching managed code). 2,773 ->
> 2,783 raw, 1,352 -> 1,357 code, 1,114 -> 1,118 comment, 307 -> 308 blank.
> `fnmap`: same 66 functions, one body changed (PhysFrame 54 -> 59).
> `classify` exits 0, no ruling changed: `HOOK` 244 -> 249, total 1,277 ->
> 1,282. Section 3.3's heading said 189 against the table's 244 before this;
> it now carries the measured 249.

> **Re-measured at physics stock parity step 4 (2026-10-02).**
> ShouldCollide_2 fills the owner and hierarchy pair facts (A2/A3), and the
> world report zeroes them. 2,783 -> 2,807 raw, 1,357 -> 1,375 code, 1,118 ->
> 1,122 comment, 308 -> 310 blank. `fnmap`: same 66 functions,
> ShouldCollide_2 40 -> 56, SourceSharp_PhysWorldReport 211 -> 213.
> `classify` exits 0, no ruling changed: `SLOT-HELP` 402 -> 418, `INSTRUMENT`
> 244 -> 246, total 1,282 -> 1,300. Code +18 and bodies +18. Section 3.2's
> heading said 431 against the table's 244 before this; it now carries the
> measured 246.

> **Re-measured at physics stock parity H (2026-10-02): the penetrate ledger
> is back.** ShouldSolvePenetration asks penetration_ledger.h instead of
> refusing behind `.pBase` (37 -> 28). Five functions were added and ruled
> SLOT-HELP: PenetrationKey 6, PenetrationObject 9, SetPenetratingFlag 10,
> UpdatePenetrateEvents 22, and PhysForceEntityToSleep 19 (back from
> physics_debug.cpp; it was STOCK-DEBUG there). FrameUpdate and LevelShutdown
> grew one line each. 2,807 -> 2,917 raw, 1,375 -> 1,435 code, 1,122 -> 1,158
> comment, 310 -> 324 blank. `classify` exits 0: 66 -> 71 functions, SLOT
> 329 -> 320, SLOT-HELP 24/418 -> 29/486, total 1,300 -> 1,359. Code +60
> against bodies +59: the one file-scope line is the world's ledger key.

> **Re-measured at physics stock parity G (2026-10-02): frozen debris is
> crushed.** ShouldFreezeObject 5 -> 23, plus IsDebrisGroup (6, SLOT-HELP).
> The contact search is the template in find_max_contact.h, not a function
> here. 2,917 -> 2,936 raw, 1,435 -> 1,460 code, 1,158 -> 1,149 comment, 324 ->
> 327 blank. `classify` exits 0: 71 -> 72 functions, SLOT 320 -> 338,
> SLOT-HELP 29/486 -> 30/492, total 1,359 -> 1,383.

> **Re-measured at physics stock parity C3/L (2026-10-02): the sound slots.**
> SourceSharp_EntityPhysicsImpactSound (10) and SourceSharp_EntityPhysicsBreakSound
> (12) feed the batches CPhysicsHook::PreClientUpdate already played, ruled
> LIVE-API (IShimPhysics slots). PenetrationObject was renamed
> ManagedPhysicsObjectOf now that three callers share it (same 9 lines, ruling
> re-keyed). 2,936 -> 2,970 raw, 1,460 -> 1,483 code, 1,149 -> 1,154 comment,
> 327 -> 333 blank. `classify` exits 0: 72 -> 74 functions, LIVE-API 3/16 ->
> 5/38, total 1,383 -> 1,405. Code +23 against bodies +22: the one file-scope
> line is ManagedPhysicsObjectOf's forward declaration.

> **Re-measured at physics stock parity C6 (8a, 2026-10-02): the motion tail.**
> PostCollision hands the recorder the velocities PreCollision captured
> (37 -> 47). 2,970 -> 2,982 raw, 1,483 -> 1,493 code, 1,154 -> 1,156 comment.
> `classify` exits 0: SLOT 338 -> 348, total 1,405 -> 1,415; code +10 = bodies
> +10.

> **Re-measured at physics stock parity C6 (8e, 2026-10-02): the solver's
> verdicts.** PostCollision hands the recorder isCollision and
> isShadowCollision (47 -> 49). 2,982 -> 2,984 raw, 1,493 -> 1,495 code,
> comment unchanged. `classify` exits 0: SLOT 348 -> 350, total 1,415 ->
> 1,417; code +2 = bodies +2.

> **Re-measured at physics stock parity I2 (8g, 2026-10-02): the fluid
> records carry the object's material.** FluidEndTouch's record call grew a
> line (17 -> 18). 2,984 -> 2,985 raw, 1,495 -> 1,496 code, comment
> unchanged. `classify` exits 0: SLOT 350 -> 351, total 1,417 -> 1,418; code
> +1 = bodies +1.

> **Re-measured at physics stock parity B5 (2026-10-02): the player's stress
> crush.** PhysicsRecordShadowStress (4) writes the STRESS ring event for
> the player file, and PhysFrame calls the player walk (59 -> 60). 2,985 ->
> 2,994 raw, 1,496 -> 1,501 code, 1,156 -> 1,158 comment. `classify` exits 0:
> 74 -> 75 functions, LIVE-API 5/38 -> 6/42, HOOK 249 -> 250, total 1,418 ->
> 1,423; code +5 = bodies +5.

> **Re-measured at physics stock parity B7 (2026-10-02): VPhysicsShadowUpdate.**
> PhysFrame walks the players' awake hulls and records SHADOW_UPDATE (60 ->
> 69), and the penetrate ledger answers what an entity penetrates
> (CCollisionEvent::PenetratingObjects 11, PhysicsPenetratingObjects 4).
> 2,994 -> 3,029 raw, 1,501 -> 1,525 code, 1,158 -> 1,167 comment. `classify`
> exits 0: 75 -> 77 functions, LIVE-API 6/42 -> 8/57, HOOK 250 -> 259, total
> 1,423 -> 1,447; code +24 = bodies +24.

> **Re-measured at plan_physics_managed A9 (2026-10-04): the entity solver.**
> ShouldSolvePenetration passes the ledger the debris fact (28 -> 30) and
> UpdatePenetrateEvents records SS_PHYSEVENT_ENTITY_SOLVER for each pair the
> ledger hands to the managed solver (22 -> 37). 3,029 -> 3,059 raw, 1,525 ->
> 1,542 code, 1,167 -> 1,176 comment, 337 -> 341 blank (the counter is not in
> the tree; a stripper that reproduces 3,029/1,525/1,167/337 on the previous
> file gave these). `classify` exits 0: 77 functions, SLOT 351 -> 353,
> SLOT-HELP 492 -> 507, total 1,447 -> 1,464; code +17 = bodies +17. All
> `CCollisionEvent::` members 764 -> 781 (fnmap over the 39 definitions);
> §2's other derived figures (402, 843) were not re-derived.

> **Re-measured at plan_physics_managed B2.1 (2026-10-05): IPhysicsObjectEvent
> left native.** vphysics' wake and sleep call managed's own table
> (PhysicsObjectEventBinding, installed by PhysicsCallbacks at pre-entity
> level init); CCollisionEvent no longer implements IPhysicsObjectEvent, and
> ObjectWake (9) and ObjectSleep (9) are deleted with their rulings. 3,059 ->
> 2,994 raw, 1,542 -> 1,523 code, 1,176 -> 1,131 comment, 341 -> 340 blank
> (the scratch counter reproduces 3,059/1,542/1,176/341 on the previous file).
> `classify` exits 0: 77 -> 75 functions, SLOT 16/353 -> 14/335, total
> 1,464 -> 1,446. `CCollisionEvent::` members 39/781 -> 37/763. WAKE and SLEEP
> are now produced by managed, not by the ring (§4).

> **Re-measured at plan_physics_managed B2.2 (2026-10-05): nine of
> IPhysicsCollisionEvent's ten slots left native.** vphysics calls managed's
> own IPhysicsCollisionEvent table (PhysicsCollisionEventBinding), which answers
> PreCollision, PostCollision, Friction, Start/EndTouch, Fluid start/end and
> the trigger edges and forwards only PostSimulationFrame here. The nine bodies
> are empty stubs in the header now, and twenty-one functions are deleted with
> their rulings: the nine, the friction table (FindFriction, ShutdownFriction,
> UpdateFrictionSounds), the fluid throttle (UpdateFluidEvents,
> DeltaTimeSinceLastFluid, PhysicsRefuseWaterTouchFlag), the buffered touch
> queue (AddTouchEvent, UpdateTouchEvents, DispatchStart/EndTouch,
> CountPhysicsObjectEntityContacts) and PhysicsCollisionEvents_BufferTouchEvents.
> 2,994 -> 2,192 raw, 1,523 -> 1,095 code, 1,131 -> 828 comment, 340 -> 269
> blank (the scratch counter reproduces 2,994/1,523/1,131/340 on the previous
> file). `classify` exits 0: 75 -> 54 functions, SLOT 14/335 -> 5/82,
> SLOT-HELP 30/507 -> 19/343, HOOK 259 -> 257, STOCK-DEBUG 3/19 -> 2/15, total
> 1,446 -> 1,023. `CCollisionEvent::` members 37/763 -> 19/378.

> **Re-measured at plan_physics_bones_native_zero 2.1 (2026-10-11):
> CCollisionEvent is deleted.** vphysics' IPhysicsCollisionSolver is managed's
> own Valve-layout table (PhysicsCollisionSolverBinding over PhysicsSolver:
> ShouldCollide, ShouldSolvePenetration and its ledger, ShouldFreezeObject and
> the crush, ShouldFreezeContacts, AdditionalCollisionChecksThisTick), and
> PostSimulationFrame is managed's too, calling the event ring's drain out
> (IShimPhysics.PhysicsDrainEvents). The environment is created with no native
> solver and no native collision handler; `ss_physics_collisionevent.h` is
> deleted. Twenty-seven functions go with their rulings: the five slots, the
> wheel rule, the handler's constructor, the debris predicate, the penetration
> key / flag / counters / partner walk, the frame and post-step drains
> (UpdateRemoveObjects, FlushQueuedOperations, FrameUpdate,
> UpdatePenetrateEvents), the damage pipeline (UpdateDamageEvents, both
> RestoreDamageInflictorState, AddDamageEvent, Find/AddDamageInflictor), the
> collision-check budget's counter, PhysIsInCallback and LevelShutdown.
> ManagedPhysicsObjectOf is re-ruled BOUNDARY (only the sound slots read it)
> and PhysForceEntityToSleep STOCK-DEBUG (physics_budget is its one caller
> again). 2,192 -> 1,353 raw, 1,095 -> 651 code, 828 -> 534 comment, 269 ->
> 168 blank (the scratch counter reproduces 2,192/1,095/828/269 on the
> previous file). `classify` exits 0: 54 -> 27 functions, SLOT 5/82 -> 0/0,
> SLOT-HELP 19/343 -> 0/0, LIVE-API 8/57 -> 5/36, INSTRUMENT 5/246 -> 3/236,
> HOOK 257 -> 255, STOCK-DEBUG 2/15 -> 3/34, BOUNDARY 4/23 -> 5/30, total
> 1,023 -> 591. `CCollisionEvent::` members 19/378 -> 0/0.

> **Re-measured at plan_physics_bones_native_zero 2.1's ask-ledger delete
> (2026-10-11).** The per-object ask ledger (lane 8e-physics) recorded native
> ShouldCollide's answers; with that function gone nothing fed it, so the
> class, its gtest and every report field that read it are deleted (the
> FELL line's asked/refused columns, the WATCH and REASONS lines, the
> summary's asks/askRows), and the report evaluates the rule table without
> the pair hash (the disabled pairs are managed's EntityPairCollisions).
> Managed's PhysicsSolver counts the asks and refusals by clause
> (cs_sv_physics_solver). 1,353 -> 1,279 raw, 651 -> 592 code, 534 -> 523
> comment, 168 -> 164 blank. `classify` exits 0: 27 functions, INSTRUMENT
> 3/236 -> 3/178, HOOK 255 -> 254, total 591 -> 532.

> **Re-measured at plan_physics_bones_native_zero 2.4's constraint-table
> delete (2026-10-11).** vphysics' IPhysicsConstraintEvent is managed's own
> table (PhysicsConstraintEventBinding, installed by PhysicsCallbacks), so the
> environment is created with NULL for it and the `extern
> IPhysicsConstraintEvent *g_pConstraintEvents;` declaration goes with
> shell_constraints.cpp. 1,279 -> 1,278 raw, 592 -> 591 code; comment and
> blank unmoved. `fnmap` gives the same 27 functions with identical body
> counts (every row after line 135 moved by exactly -1); `classify` exits 0
> with identical totals (532).

> **Re-measured at plan_physics_bones_native_zero 2.2's CSkipKeys delete
> (2026-10-11).** The IVPhysicsKeyHandler that skipped every key had no user
> left: nothing in the tree handed it to a solid parse (the plan's survey line
> 1561 named it as a callback still to move; it was a declaration only). Its
> BOUNDARY ruling goes with it. 1,278 -> 1,269 raw, 591 -> 585 code, 164 ->
> 161 blank. `fnmap` gives 26 functions; `classify` exits 0: BOUNDARY 5/30 ->
> 4/24, total 532 -> 526.

> **Re-measured at plan_physics_bones_native_zero 2.3's diagnostics move
> (2026-10-11).** The SS-PHYSWORLD report and its sink (INSTRUMENT, 2/166),
> PhysForceEntityToSleep and CC_AirDensity with its command (STOCK-DEBUG,
> 3/34) and the two entity-object helpers only the sleep used (BOUNDARY,
> 2/12) are managed's or gone: PhysWorldReport (the report, its per-pid file
> and the automatic run behind `sourcesharp_physworld_report`),
> PhysicsBudget over PhysicsSolver.ForceEntityToSleep, and
> AirDensityCommand. The report's rule evaluation is managed's
> PhysicsSolver.AgainstTheWorld. LevelInitPostEntity lost its automatic run
> (HOOK 254 -> 252). 1,269 -> 832 raw, 585 -> 369 code, 523 -> 345 comment,
> 161 -> 118 blank (codelines re-derived: the stripper reproduces 1,269 /
> 585 / 523 / 161 at bc21e4d46). `fnmap` gives 19 functions; `classify`
> exits 0 with the seven retired rulings removed: INSTRUMENT 3/178 -> 1/12,
> STOCK-DEBUG 3/34 -> 0/0, BOUNDARY 4/24 -> 2/12, total 526 -> 312.

> **Re-measured at plan_physics_bones_native_zero 2.3's sound batches
> (2026-10-11).** The queued impact and break sounds are managed's
> PhysicsSoundBatch, played at OnPreClientUpdate through vphysics' surface
> table and the EmitWorldSoundScript slot. Deleted: SourceSharp_PlayImpactSounds
> and SourceSharp_PlayBreakSounds (HOOK 2/56), CPhysicsHook::PreClientUpdate
> (HOOK 1/10), the two queueing slots (LIVE-API 2/22), ManagedPhysicsObjectOf
> (BOUNDARY 1/7), the two queues and the timer on CPhysicsHook (the class body,
> Init and LevelShutdownPostEntity, HOOK -7), and ss_physics_sound.h. 832 ->
> 698 raw, 369 -> 265 code, 345 -> 332 comment, 118 -> 101 blank. `fnmap`
> gives 13 functions; `classify` exits 0 with six rulings retired: HOOK
> 11/252 -> 8/179, LIVE-API 5/36 -> 3/14, BOUNDARY 2/12 -> 1/5, total 312 ->
> 210.

> **Re-measured at plan_physics_bones_native_zero 2.3's physics frame
> (2026-10-11).** PhysFrame's decisions are managed's PhysicsFrame:
> whether and how long to step, phys_timescale's clock reset, phys_speeds'
> profile line, and what the step tells the game (UPDATE per owner, each
> player's STRESS and SHADOW_UPDATE, posted to the router with no ring).
> CPhysicsHook::FrameUpdatePostEntityThink forwards to IManagedPhysicsFrame.
> Deleted: PhysFrame (HOOK 1), PhysTimescale (INSTRUMENT 1/12), the two
> ConVar refs, the running average and m_isFinalTick (no reader). Came:
> SourceSharp_PhysicsUpdateOwners (LIVE-API 1/19), the owners' transform
> update carried out for the managed frame. 698 -> 551 raw, 265 -> 193 code,
> 332 -> 280 comment, 101 -> 78 blank. `fnmap` gives 12 functions; `classify`
> exits 0 with two rulings retired and one added: INSTRUMENT 1/12 -> 0/0,
> HOOK 8/179 -> 7/104, LIVE-API 3/14 -> 4/33, total 210 -> 142.

Then the player's stress walk: PhysicsRecordShadowStress (LIVE-API 1/4), the
STRESS ring write PhysFrame's player walk called, left with that walk
(PlayerShadowStress), and physics_internal.h, its only declaration, with it.
551 -> 544 raw, 193 -> 188 code, 280 -> 279 comment, 78 -> 77 blank;
`fnmap` 11 functions; LIVE-API 4/33 -> 3/29, total 142 -> 138.

Then the event ring (2.3): nothing recorded into it once the frame was
managed's, so the ring, its sink, cs_sv_physics_shell and the physics id
table went, and CPhysicsHook stopped clearing the ring at level init and
shutdown (HOOK 7/104 -> 7/101). 544 -> 534 raw, 188 -> 185 code, 279 -> 273
comment, 77 -> 76 blank; `fnmap` 11 functions; total 138 -> 135.

Then physics_shared.cpp's server half (2.6, 2026-10-12): the world's physics
is managed's (ServerPhysicsWorld, asked through IManagedPhysicsFrame's
CreateWorld), so `PhysCreateWorld` (BOUNDARY 1/5) went; LevelInitPreEntity
asks managed for the world and the surface sounds' precache and no longer
makes `g_EntityCollisionHash` (39 -> 41), LevelShutdownPostEntity no longer
destroys it (15 -> 13); the five vphysics pointers stock's physics_shared.cpp
defined are file-scope globals here now. 534 -> 538 raw, 185 -> 183 code,
273 -> 282 comment, 76 -> 73 blank (counted with the same stripper, which
gives 534/185/273/76 at ab3134b2a); `fnmap` 10 functions; HOOK 7/101 (the two
moves cancel), BOUNDARY 1/5 -> 0/0, total 135 -> 130.

Then an entity's physics init (InitPhysics to managed, 2026-10-12), outside
physics.cpp: CStandaloneEntity::InitPhysics (shell_standalone_base.cpp), the
spawn arm that asked for a Prop kind's object, SourceSharp_EntityInitPhysics /
EntitySetPhysicsStatic / EntityConvertToPhysics (shell_physicsobjects.cpp,
shell_scripted.cpp) and the two Vector proxies are managed's
EntityPhysicsCreation, PhysicsConversion (stock's phys_convert, ported) and
direct vtable calls. physics.cpp itself is unchanged: `tools/physics-scope`
at 0b8787ad5 still reads HOOK 7/101, LIVE-API 3/29, 10 functions, 130.
The PhysModelCreate* tombstone in the appendix below now has managed's
EntityPhysicsCreation as "what builds a physics object from an entity's
vcollide"; standalone_solidity_order_test.cpp, quoted there, is deleted.

Then the ground's velocity (plan_physics_bones_perf C2, 2026-10-12):
`SourceSharp_PhysicsUpdateOwners` takes the velocity managed's PhysicsFrame
gives each active object and holds it on the object's own entity
(StandaloneEntity_HoldMoveVelocity), which the movement's ground read answers
from; the pick between the solver and the member left native. LIVE-API
3/29 -> 3/34; 538 -> 547 raw, 183 -> 188 code, 282 -> 286 comment, 73 blank
(the same stripper reproduces 538/183/282/73 at 532c1a078); `fnmap` 10
functions, total 130 -> 135.

The dead `gamesound.h` include (2026-10-19, after the client sound emitter
left client.so and nothing on either side imports a GameSound_* function):
the include and its three-line comment go, 547 -> 542 raw, 188 -> 187 code,
286 -> 283 comment, 73 -> 72 blank. `fnmap` re-run: the same 10 functions at
the same code sizes, 5 lines earlier; `classify` totals unchanged.

kServerSystems[]'s order went managed (plan_server_native_v2 T4e, 2026-10-19):
`CPhysicsHook::FrameUpdatePostEntityThink` only forwarded to managed's
PhysicsFrame, and `ServerSystems.FramePostThink` now runs the step at the
row's position itself, so the override, its declaration and its comment go and
the class derives `CServerSystem` (the per-frame base went with the walks).
542 -> 534 raw, 187 -> 181 code, 283 -> 281 comment, 72 blank (a scratch
counter reads the previous file as 542/186/284/72, the same deltas). `fnmap`
re-run and `fnmap.tsv` rewritten: 10 -> 9 functions, 135 -> 129 body lines
(the class row 19 -> 18, the 5-line forward gone); `rulings.tsv` loses the
forward's HOOK row, so `classify` reads HOOK 7/101 -> 6/95, TOTAL 9/129.

The SDK shadow-entity list's always-NULL `g_pShadowEntities` and its printed
refusal went (plan_server_native_v2 T6, 2026-10-19): nothing filled the list,
so the refusal reported a state that could not change. 534 -> 474 raw,
181 -> 162 code, 281 -> 242 comment, 72 -> 70 blank (the scratch counter reads
534/180/282/72 -> 474/161/243/70, the same deltas). `fnmap` re-run and
`fnmap.tsv` rewritten: still 9 functions, 129 -> 112 body lines
(`LevelInitPreEntity` 41 -> 25, `LevelShutdownPostEntity` 13 -> 12, every
definition 6 lines earlier); `classify` reads HOOK 6/95 -> 6/78, TOTAL 9/112.

The native comments were cut to what the code needs (2026-10-26, comment-only:
the code with comments stripped is identical). 474 -> 229 raw, 161 code
unchanged, 243 -> 26 comment, 70 -> 42 blank. `fnmap` re-run and `fnmap.tsv`
rewritten: the same 9 functions and body lines, every definition earlier;
`classify` reads the same.

Of the 162 code lines, **112 are inside a function body** across **9
top-level definitions**. The other 50 are includes, file-scope globals and
forward declarations.

Every number below is machine-derived, not counted by hand:

```
$ python3 5a-fnmap.py   src/sourcesharp/game/server/physics.cpp > 5a-physics-fnmap.tsv
$ python3 5a-callers.py 5a-physics-fnmap.tsv src/sourcesharp/game/server/physics.cpp
$ python3 5a-physics-classify.py 5a-physics-fnmap.tsv 5a-physics-rulings.tsv
```

The classifier **fails** (exit 1) if any function has no ruling, if a ruling key
matches more than one function, or if a ruling matches none. It exits 0 here, so
the ten totals below account for all 9 functions and all 112 lines with
nothing dropped and nothing double-counted. That guard is the reason to trust
the arithmetic rather than the author.

### The instrument that had to be built second, and the ruling it overturned

The first pass ruled each function **from its name and its neighbourhood**, and
that pass was wrong in eleven places. The one that makes the point:
`PhysicsImpactSound` was ruled "the impact-sound batcher's entry;
`PhysCollisionSound`'s only caller" — **the call runs the other way.**
`PhysCollisionSound` computes the volume curve and then calls
`PhysicsImpactSound`, and since `PhysCollisionSound` has no caller at all, the
"helper of a live slot" reading had it exactly backwards: both are dead.

So `5a-callers.py` was written to answer, for every function, *which function's
body contains each call site of it*. Its output moved eleven functions between
verdicts — `PhysForceEntityToSleep` from "the drain path" to
`CON_COMMAND( physics_budget )`, the three shadow accessors out of `BOUNDARY`
entirely, six functions into `DEAD-VIA`. **A scope ruled by name is a scope with
errors in it, and this one had them.**

Two limits of that instrument, stated because they bound what the table can
claim:

- It matches the **bare name**, so a short one over-reports. `CPhysicsHook::Init`
  appears to be called from three `CCollisionEvent` methods; those are
  `m_triggerEvents[index].Init( ... )`. Every edge involving a common name was
  read at its site before being used.
- It does not strip **string literals**, and that is what produced the shadow
  accessors' apparent caller: `CPhysicsHook::LevelInitPreEntity` names
  `PhysAddShadow` and `PhysHasShadow` inside a refusal message, not in a call.

---

## 2. The ten verdicts

| verdict | fns | lines | can it move? |
|---|---:|---:|---|
| `INSTRUMENT` | 0 | 0 | none: `PhysTimescale` went with PhysFrame to managed's PhysicsFrame (2.3); the SS-PHYSWORLD report and its sink are managed's (PhysWorldReport, plan_physics_bones_native_zero 2.3; their other commands are in `physics_debug.cpp` since 2026-10-01; was 11 / 431; 5 / 246 until 2.1; 3 / 236 until the ask ledger left the report; 3 / 178 until 2.3; 1 / 12 until 2.3's physics frame) |
| `SLOT-HELP` | 0 | 0 | deleted with CCollisionEvent (plan_physics_bones_native_zero 2.1; was 30 / 507 until B2.2, 19 / 343 until 2.1) |
| `SLOT` | 0 | 0 | deleted: every vphysics slot is a managed table (2.1; was 14 / 335 until B2.2, 5 / 82 until 2.1) |
| `DEAD` | 0 | 0 | deleted (server gc cull, 2026-10-01; was 25 / 284) |
| `STOCK-DEBUG` | 0 | 0 | re-registered on the managed side (2.3): `air_density` (AirDensityCommand) and `physics_budget` with its sleep (PhysicsBudget, PhysicsSolver.ForceEntityToSleep); the rest went to `physics_debug.cpp` (2026-10-01; was 14 / 253; 3 / 19 until B2.2 deleted `physics_budget`'s touch buffering; 2 / 15 until 2.1 left PhysForceEntityToSleep its one caller; 3 / 34 until 2.3) |
| `HOOK` | 6 | 78 | no — `CPhysicsHook`, a native game system whose stages managed's ServerSystems orders (6 / 95 until plan_server_native_v2 T6 took the shadow list's refusal out of LevelInitPreEntity and its NULL store out of LevelShutdownPostEntity; 7 / 101 until plan_server_native_v2 T4e took the frame forward out: ServerSystems runs PhysicsFrame at the row's position; 254 until 2.3 took the report's automatic run out of LevelInitPostEntity; 11 / 252 until 2.3 moved the sound batches and PreClientUpdate to managed's PhysicsSoundBatch; 8 / 179 until 2.3 moved PhysFrame to managed's PhysicsFrame; 7 / 104 until the ring's clears left with the ring) |
| `LIVE-API` | 2 | 28 | no — a shim object on the link line imports it (3 / 34 until PhysicsWorldOwner was re-ruled BOUNDARY, 2026-10-18; 3 / 29 until the owner batch held managed's velocity, plan_physics_bones_perf C2; was 4 / 50; 3 / 16 until C3/L's two sound slots; 5 / 38 until B5's stress write; 6 / 42 until B7's penetration answer; 8 / 57 until 2.1 deleted the penetration readouts and PhysIsInCallback; 5 / 36 until 2.3 took the two sound slots out of the ABI; 3 / 14 until 2.3's PhysicsUpdateOwners slot; 4 / 33 until the stress walk's ring write left with PhysFrame) |
| `DEAD-VIA` | 0 | 0 | deleted with the dead functions above (was 6 / 41) |
| `STALE-KEEP` | 0 | 0 | deleted, its reason having expired (was 4 / 34) |
| `BOUNDARY` | 1 | 6 | `PhysicsWorldOwner`, the compare against the world's object managed hands native (re-ruled from LIVE-API 2026-10-18: no other server object imports it since physics_debug.o left); the world build is managed's (2.6; was 1 / 5, `PhysCreateWorld`); ManagedPhysicsObjectOf re-ruled here at 2.1 and deleted with the sound slots at 2.3; CSkipKeys deleted at 2.2; EntityPhysicsObject and its list deleted with their caller at 2.3 |
| **total** | **9** | **112** | |

`SLOT` + `SLOT-HELP` = **0 lines** since plan_physics_bones_native_zero 2.1
deleted CCollisionEvent: **0 are vtable slots** vphysics dispatches into this
file (425 until 2.1, 843 until B2.2). That is what the plan's "**701** are
`CCollisionEvent::` vtable slots vphysics calls" was reaching for; the two
corrections below are kept as the record of what the class was:

- **All `CCollisionEvent::` members in this file are 378 lines across 19
  definitions, not 701** (763 / 37 until B2.2), and only **82 are vtable slots vphysics
  actually dispatches** (5 methods: `IPhysicsCollisionSolver`'s four and
  `PostSimulationFrame`; `IPhysicsObjectEvent` is managed's since B2.1 and the
  other nine `IPhysicsCollisionEvent` slots since B2.2; per
  `game/server/physics_collisionevent.h:62`). The remaining **296 are
  private helpers and drains this side calls** (re-derived at physics stock
  parity E1 from `fnmap` + `classify`, members by verdict). The 28 dead lines
  this sentence used to name (`AddRemoveObject`, `GetTriggerEvent`,
  `GetInflictorVelocity`) went in the server gc cull; the sentence's 756 / 40
  was not re-derived then, and read 728 / 37 by the same method just before E1.
  The distinction matters because a helper's *content* can move where a slot's
  *existence* cannot.
- `SLOT-HELP` (343) is not the same set as "the `CCollisionEvent::` half": it
  also covers the free helpers only a slot calls — `WheelCollidesWith`
  (`PhysicsRefuseWaterTouchFlag` and `CountPhysicsObjectEntityContacts` went
  with their slots at B2.2) — and
  excludes the three dead members above.

---

## 3. Why each "cannot" is a cannot

### 3.1 `SLOT` (0; 82 until 2.1) — the caller is inside `vphysics.so`

> **Since plan_physics_bones_native_zero 2.1 the trampoline is managed's.**
> The environment is created with no native solver and no native collision
> handler, and PhysicsCallbacks installs managed's Valve-layout tables at
> pre-entity init; the paragraph below is the record of CCollisionEvent.

`CPhysicsHook::LevelInitPreEntity` hands `&g_Collisions` to
`physenv->SetCollisionSolver`, `SetCollisionEventHandler` and
`SetObjectEventHandler`. From that moment vphysics dispatches these 16 methods
through the object's vtable, from inside the solver, with no C# reach and no
seam to hold. **The slot is the trampoline.** Moving it is not a thing that can
be done; emptying it is, and that is §4.

### 3.2 `INSTRUMENT` (246) — reached by name, invisible to any census

> **244 since 2026-10-01, and the commands are debug-only.** The six commands
> below, and `SourceSharpReportObjectsOutOfBounds`, moved to
> `physics_debug.cpp`, which only a debug build compiles; so every driver in
> this table needs a debug build. What is left here is the report and its sink
> (still run at level init when `sourcesharp_physworld_report` is on), the
> collision-check budget counter, the penetration counters' accessor and
> `PhysTimescale`. The table is the driver census it was taken as.

> **Managed since 2026-10-18.** ss_physworld_recheck, _bounds and _object
> are SourceSharp.Server's PhysWorldProbes (PhysicsDebugCommands.cs, debug
> builds), with native's lines and the same per-pid file; physics_debug.cpp
> is deleted.

The port's own physics diagnostics. A `ConCommand` registers itself from a
static constructor, so **no link-line census can see a driver**; these have to
be swept for by name. Re-measured here rather than quoted (`./5a-namedrive.sh`):

| command | sites | files |
|---|---:|---:|
| `ss_physworld_report` (managed since 2.3: PhysWorldReportCommand, same name, same per-pid file) | 13 | 4 |
| `ss_physworld_object` | 11 | 4 |
| `cs_sv_physics_shell` (retired at 2.3 with the event ring it reported on) | 6 | 2 |
| `ss_physworld_penetration` (retired at 2.1; the gate reads managed's solver counters) | 4 | 1 |
| `ss_physworld_recheck` | 2 | 1 |
| `ss_physworld_bounds` | 3 | 1 |

**39 sites across 9 files** (8 under `tools/`, 1 managed:
`SourceSharp.Server/PhysicsEventsCommand.cs`; `docs/` and `cfg/` name none). The
plan's "8 tools, 30 sites" undercounts both — re-measure before using either
number.

`ss_physworld_object` additionally has a gtest-pinned match rule
(`physworld_match_rule.h`), written after that probe was found reporting 415 of
427 objects while claiming the whole environment. None of this can move until
every driving site is re-pointed **in the same commit**, and moving a probe is
not what a policy move is for.

### 3.3 `HOOK` (249) — the engine drives it

`CPhysicsHook` is an `IGameSystemPerFrame` with a row in `kServerSystems[]`.
Its nine slots are the engine's level and frame callbacks, and
`LevelInitPreEntity` is where `physenv` is created and the three event
interfaces are bound. `PhysFrame` is counted here rather than as policy: its
only caller is `FrameUpdatePostEntityThink`, and its body is the per-tick
`physenv->Simulate` plus the active-object walk.

### 3.4 `LIVE-API` (50) — measured, and it is only four symbols

> **Three since the server gc cull (2026-10-01):** the named `PhysFrictionSound`
> overload went; its importer, physics_shared.cpp's numeric overload, is itself
> unreached. The measurement below is the four-symbol state it was taken on.

This is the single most useful measurement in this document, and two
independent instruments agree on it exactly.

```
$ tools/shim-census --mtime-policy off who-imports \
      src/sourcesharp/game/server/physics.cpp
# objects  server  108 on the graph  108 on disk  0 ghosts
# objects  client  253 on the graph  253 on disk  0 ghosts
# COUNT    server  2 object files import it
```

and, by a different route entirely — `nm` over the link line with an explicit
symbol-type filter, which is what `./5a-physics-exports.sh` does:

```
physics.o defines (T/W/D/B/R/V):      937
other server objects leave undefined: 1222
== intersection ==
  PhysIsInCallback()
  PhysFrictionSound(IGameEntity*, IPhysicsObject*, char const*, short&, float)
  PhysicsGameSystem()
  PhysicsWorldOwner(IPhysicsObject const*)
```

**`physics.o` defines 937 symbols and exactly FOUR are imported by anything else
on the server link line.** Nothing on the client link line imports any of them —
`physics.cpp` is not a client translation unit.

### 3.5 `BOUNDARY` (23) — a `this` no managed side can hold

> **One since 2026-10-18: `PhysicsWorldOwner` (6 lines).** It was
> LIVE-API while physics_debug.o imported it; `nm -C` over server.so's 119
> link-line objects (farm/debug, at 8605b6ea1) finds it defined in
> physics.cpp.o and undefined in no other, so its one caller is
> physicsowner.h's inline PhysicsOwnerOf inside SourceSharp_PhysicsUpdateOwners.
> It is the pointer compare this section names: managed hands native the
> world's object, and this answers whether an object is it.

Thin definitions whose whole content is a raw `IPhysicsObject *`:
`EntityPhysicsObject`, `EntityPhysicsObjectList` and `PhysCreateWorld`.
These are the boundary itself, and a managed twin would need the pointer that
cannot cross. (All three are gone: the two accessors with their caller at
plan_physics_bones_native_zero 2.3, and `PhysCreateWorld` at 2.6, when the
pointer did cross -- managed calls vphysics through its own tables and hands
native the world's object, which physicsowner.h compares against.) (`CSkipKeys`, an `IVPhysicsKeyHandler` that skipped every key,
was ruled here until plan_physics_bones_native_zero 2.2 found it had no user
and deleted it.)

### 3.6 `STALE-KEEP` (34) — kept, with a reason that has expired

> **Deleted in the server gc cull (2026-10-01):** all four were dropped by
> server.so's link, which settles the expired reasons below.

`PhysAddShadow`, `PhysRemoveShadow`, `PhysHasShadow` and the
`PhysicsRefuseShadowAccessor` the three share. The refusal object states why
they are kept, at `physics.cpp:2786`:

> keeping them is not politeness. `physics.h:96-98` declares all three and
> `baseentity.o` and `baseentity_shared.o` both call `PhysRemoveShadow` from
> their own translation units; guarding the DEFINITIONS away would compile every
> TU cleanly and fail the link with an undefined symbol, on a build no
> compile-only survey can see.

**Neither object is on either link line any more**, and the four-symbol reading
in §3.4 covers these: all three have zero importers. `5a-callers.py` finds no
call site for any of them in this file either — its apparent one is the refusal
message quoting their own names.

They are listed apart from `DEAD` on purpose. The argument for keeping them was
*correct when it was written* and is a good argument; what changed is the fact
underneath it. **Re-checking a stated reason is cheaper than re-deriving the
ruling, and nobody had.** Deleting them is a decision for whoever owns
`physics.h`'s declarations, not a sweep.

---

## 4. What actually moves

**Nothing, as a function.** Not one of the 77 is a self-contained policy body
with a managed home and no native caller. That is why the "~1,011 lines" never
resolved into a diff: it was a line count looking for a function list that does
not exist.

The movable content is **decision content inside slots that stay**, and the
mechanism for moving it already exists and is already carrying most of it:

- **The event ring.** Native records; managed decides on the drain.
  `SourceSharpPhysicsEvent_t` and `SS_PHYSEVENT_*` (`physics_shell.h:85`) define
  sixteen kinds (twelve until physics stock parity B4b added `UPDATE`,
  thirteen until B5 added `STRESS`, fourteen until B7 added `SHADOW_UPDATE`,
  fifteen until plan_physics_managed A9 added `ENTITY_SOLVER`).
  **Five are recorded natively**: four through
  `SourceSharp_RecordPhysicsEvent` in this file (`UPDATE` from PhysFrame's
  active-object loop, `STRESS`
  for a player's awake hull through `PhysicsRecordShadowStress`,
  `SHADOW_UPDATE` from PhysFrame's walk of the players' awake hulls, and
  `ENTITY_SOLVER` from UpdatePenetrateEvents' pairs for the entity solver) and
  `CONSTRAINT_BROKEN` through `SourceSharp_RecordConstraintBroken` at
  `shell_constraints.cpp:964`. **`PRE_COLLISION` is the one that is not, and
  that is a documented decision, not a gap.** `WAKE` and `SLEEP` left the ring at
  plan_physics_managed B2.1: managed's own IPhysicsObjectEvent table posts them
  to the router (2026-10-05). `START_TOUCH`, `END_TOUCH`, `FRICTION`,
  `FLUID_START`, `FLUID_END`, `ENTER_TRIGGER`, `LEAVE_TRIGGER` and
  `POST_COLLISION` left it at B2.2: managed's own IPhysicsCollisionEvent table
  holds the touch and trigger pairs and posts the rest (2026-10-05).
- **The managed destinations already hold 655 lines**:
  `SourceSharp.Server/PhysicsCollisionResponse.cs` (312) and
  `PhysicsCollisionRules.cs` (343); 640 (297 + 343) until the response
  answered managed's own PostCollision (plan_physics_managed B2.2); 636 (293 + 343) until the response's
  collision world was handed the side's own surface table (plan_abi_hotreload
  Phase 1); 618 (293 + 325) until the rules named the
  FVPHYSICS_* flags the impact damage reads (C6); 599 (274 + 325) until the response queued
  the impact sound it used to count as dropped (physics stock parity C3); 585
  (267 + 318) when first measured,
  before the repo move added the 7-line attribution header to each. `wc -l` on both. The "~1,011" was never
  reduced by these.

So the remaining move is **per slot, and it is subtraction rather than
relocation**: for each of the 5 slots left (14 until B2.2), the lines between the recorder and the
end of the body that are stock's SDK arm. **Those lines are mostly not live
either.** `PhysicsOwner_t::pBase` is NULL for every physics object this build
makes (`plan_shim.md` §4a item 15, measured over 519 objects), so every arm
behind a `PhysicsOwnerOf( ... ).pBase` resolve is unreachable. The file already
marks **10 such sites** with the word `ARMED`, the bluntest being
`PostCollision` at `physics.cpp:2963`:

> ARMED THIS FUNCTION HAS ALREADY RETURNED before reaching any of the
> dispatches below.

**That is a DELETION of unreachable stock arms, not a port** — and, exactly as
§4a item 15 concluded for penetration granularity, porting it would ADD
behaviour rather than move it, which is the opposite of what the phase is for.

### The number, and why this document does not give one

An honest line count for "what moves" requires attributing every line of 5 slot
bodies to *recorder*, *armed-dead stock arm*, or *live decision*. That is a
per-line reading of 336 lines plus the 433 of helpers they call, and it cannot be
done by any instrument this lane has. **A scope with a number it cannot defend is
worse than no scope**, so: no number.

The experiment that produces one is a line-level attribution pass over those 16
bodies, one slot per commit, each landing with the managed twin and the ring
kind it consumes. It is bounded by the 843 `SLOT`+`SLOT-HELP` lines and is
certainly far smaller, because most of the 843 is recorder and drain.

---

## 5. Three categories that are not this item's, and should not be folded in

### 5.1 `DEAD` — 284 lines, 25 functions

> **Deleted in the server gc cull (2026-10-01):** server.so's
> `--print-gc-sections` link dropped every one, and the cull removed them.
> What follows is the record of why they were dead.

Zero importers on both link lines (§3.4's four-symbol reading covers every
export of this file) **and** no call site anywhere in the file, counted over
comment-stripped source with a bare-name match so a function used as a POINTER
is not missed (`5a-refcount.py`, cross-checked by `5a-callers.py`).

`PhysIsFinalTick`, `FindMaxContact`, `PhysShouldCollide`, `PhysSolidOverride`,
`PhysSetMassCenterOverride`, `PhysGetMassCenterOverrideIndex`,
`PhysGetMassCenterOverride`, `PhysGetEntityMass`, `PhysEnableFloating`,
`PhysOnCleanupDeleteList`, `PhysGetDamageInflictorVelocityStartOfFrame`,
`PhysGetTriggerEvent`, `PhysCollisionSound`, `PhysBreakSound`,
`PhysCleanupFrictionSounds`, `PhysCallbackImpulse`, `PhysCallbackSetVelocity`,
both `PhysCallbackDamage` overloads, `PhysCallbackRemove`,
`PhysSetEntityGameFlags`, `PhysFindOrAddVehicleScript`, `PhysFlushVehicleScripts`,
`FindPhysicsObjectByName`, `DebugDrawContactPoints`.

**This is a deletion lane's work, not a policy move**, and folding it in would
let a deletion be reported as a port. Two cautions for whoever takes it:

- The reading is a **floor**. A virtual call emits no undefined symbol. None of
  these are virtual, so it is sound here — but re-establish that, do not inherit
  it.
- `PhysCollisionSound` is the volume curve — the 0.05 s repeat filter, the
  70 in/s floor and the quadratic ramp — and `PhysicsCollisionRules.Sound` in
  managed code already computes all three. What managed code does NOT have is
  `PhysicsImpactSound`'s batch into `physicssound::AddImpactSound`, which
  `PhysicsCollisionResponse.cs` names as the forwarding target of a slot it
  wants. **So this pair is a decision, not a sweep**: deleting it deletes a
  specified future dependency, and the reason that slot was not added is written
  down there (a managed entity may have no edict, and `PhysicsImpactSound`'s
  first act is `ENTINDEX( ...GetEdict() )`).

### 5.2 `DEAD-VIA` — 41 lines, 6 functions

> **Deleted in the server gc cull (2026-10-01),** with section 5.1's dead
> functions, as this section said they had to be.

Reachable only from a function in §5.1. `PhysicsImpactSound`,
`PostSimulation_ImpulseEvent`, `PostSimulation_SetVelocityEvent`,
`CCollisionEvent::AddRemoveObject`, `CCollisionEvent::GetTriggerEvent`,
`CCollisionEvent::GetInflictorVelocity`.

Held apart from `DEAD` because **the claim is weaker and the order matters**:
each has a call site, so a deletion pass that took it first would break a build
that still compiles. They go after their caller, or with it.

This is also the group the by-name pass got wrong (§1), so treat the verdict as
derived from `5a-callers.py` and re-derive it rather than inheriting it.

### 5.3 `STOCK-DEBUG` — 253 lines, 14 functions

> **The named decision, taken (plan_physics_bones_native_zero 2.3):
> re-registered on the managed side.** `air_density` is AirDensityCommand
> (every build, cheat-flagged as before); `physics_budget` is
> PhysicsBudgetCommand (debug builds), timing the step through vphysics'
> vtable and putting objects to sleep through PhysicsSolver.ForceEntityToSleep,
> which is stock's PhysForceEntityToSleep. The native bodies and
> `PhysForceEntityToSleep` are deleted. `physics_highlight_active`,
> `physics_report_active` and `physics_select` are still native in
> `physics_debug.cpp`.

> **All five managed (2026-10-18).** `physics_highlight_active`,
> `physics_report_active` and `physics_select` are re-registered on the
> managed side too (PhysicsDebugCommands.cs, debug builds), and
> `physics_debug.cpp` is deleted. `physics_select` resolves its subject again
> (the player's aim, then a name, then a classname) and toggles
> CALLBACK_MARKED_FOR_TEST, where native printed a refusal;
> `physics_highlight_active` still marks nothing, since no entity of this build
> has `m_debugOverlays`.

> **Moved to `physics_debug.cpp` (2026-10-01),** a debug-only file, by user
> ruling -- the four commands and every helper below, `PhysForceEntityToSleep`
> included. That is a location, not the named decision this section asks for:
> they are still stock's commands, still native, and still undriven. What is
> left in physics.cpp is `air_density` (with `CC_AirDensity`) and
> `PhysicsCollisionEvents_BufferTouchEvents`, 3 / 19.

`physics_highlight_active`, `physics_report_active`, `physics_select`,
`physics_budget`, `air_density` and their helpers — including
`PhysForceEntityToSleep`, whose only call site in the tree is inside
`physics_budget` at `physics.cpp:2616`. **Nothing under `tools/`, `docs/`, the
managed tree or `cfg/` names any of the five commands** (§3.2's table: 0 sites
each). They are stock's own debug commands, reachable only by a human typing at
the console.

The file already states the port's answer for this family, at
`physics.cpp:2325`: re-register them on the managed side rather than widening
the C++ ones — and records that this was already done once, for
`physics_debug_entity`, in `shell_standalone_player.cpp`.

So the ruling is **"re-register or delete, by a named decision"**, not "moves"
and not "cannot move". It is listed separately because rolling it into a policy
move would put 253 lines of undecided work behind a heading that says the
decision has been made.

---

## 6. Corrections to `plan_shim.md` §4a item 1

| the plan says | measured here |
|---|---|
| 4,292 raw | **4,291 (`wc -l`) when this was written.** It was 4,292 after §8's reason-letter marker added one comment line at the close, and that agreement was a coincidence; it was 4,310 after lane `g-vdisp`'s crash fix was 4,309 after the attribution header, was 4,172 after the octree audit was removed, was 4,188 after the commands were declared in C#, was 4,191 after the surface property manifest moved to C#, was 4,192 after the string calls moved onto ss_string.h, was 4,189 after VProf was deleted, was 4,264 after the queued physics sounds moved onto ss:: filters, was 3,663 after the server gc cull deleted the 34 functions server.so's link dropped, was 2,875 after the diagnostic commands moved to physics_debug.cpp, was 2,773 after contact touch was ported (physics stock parity E1), was 2,783 after PhysFrame recorded the per-frame update (B4b), was 2,807 after ShouldCollide read the owner and hierarchy facts (step 4), was 2,917 after the penetrate ledger came back (H), was 2,936 after ShouldFreezeObject crushed frozen debris (G), was 2,970 after the impact and break sound slots (C3/L), was 2,982 after PostCollision recorded the motion tail (C6), was 2,984 after it recorded the solver's collision verdicts (C6, 8e), was 2,985 after the fluid records carried the object's material (I2), was 2,994 after PhysFrame recorded the players' stress (B5), was 3,029 after it walked their shadows (B7), was 3,059 after the penetrate ledger handed stuck debris to the entity solver (plan_physics_managed A9), was 2,994 after IPhysicsObjectEvent moved to managed (B2.1), and is 2,192 since nine of IPhysicsCollisionEvent's slots moved to managed and their bodies were deleted (B2.2; section 1). The plan's figure was never derived the same way — see section 1's notes |
| 2,202 code lines (49% prose) | 1,542 code; prose is 43.3% of non-blank lines |
| 1,921 code lines in bodies | 1,023, across 54 functions (1,446 / 75 until B2.2) |
| 701 are `CCollisionEvent::` vtable slots | 82 are vtable slots; 378 are all `CCollisionEvent::` members; 425 are slots + the helpers only a slot calls (335 / 763 / 843 until B2.2) |
| 443 are diagnostics (8 tools, 30 sites) | 431 are the port's instruments (9 files, 39 sites) and a FURTHER 253 are stock debug commands nothing drives; since 2026-10-01 the commands among them live in physics_debug.cpp and 244 + 19 remain here |
| managed destinations already hold 599 lines | confirmed, `wc -l` on both files; 618 since C3, 636 since C6, 640 since the side-owned surface table, 655 since B2.2's PostCollision |

None of these changes the plan's conclusion. They make it stronger: the
unmovable share is larger than stated, the driven-by-name share is larger than
stated, and 325 lines of what is left are dead rather than portable.

---

## 7. Reproducing this

**The scripts are NOT committed**, and that is worth stating rather than
leaving to be discovered: they lived in lane `5a-physresidue`'s worktree
(`5a-fnmap.py`, `5a-callers.py`, `5a-refcount.py`, `5a-physics-classify.py`,
`5a-physics-rulings.tsv`, `5a-codelines.sh`, `5a-linkline.sh`,
`5a-namedrive.sh`, `5a-physics-exports.sh`) and go when it does.

So treat every total here as **a reading to re-derive, not a figure to quote** —
which is the discipline whose absence produced the "~1,011 lines" this document
replaces. Each is small and each is described where it is used:

- **the function map** — enumerate column-0 definitions and count the
  comment-stripped lines in each body;
- **the caller map** — for each definition, which definition's line range
  contains each bare-name reference to it, outside its own body;
- **the classifier** — join the two against a per-function ruling table and
  **fail** if any function is unruled, any key matches twice, or any ruling
  matches nothing;
- **the link-line enumerator** — take the objects from the `link_shared` edge of
  the ninja graph, never from `_vpc_/ninja/*/objs/`;
- **the export check** — `nm --defined-only` on `physics.o` filtered to
  `[TWDBRV]`, intersected with `nm --undefined-only` over every other object on
  that link line.

`physics_move_scope_test.cpp` is the part that IS committed, and it re-derives
from the tree everything a gtest can: the fourteen slot names out of
`physics_collisionevent.h`, the ring's recorded kinds out of `physics.cpp` and
`shell_constraints.cpp`, the managed destinations' line counts, and the totals
table summing to its own stated total. What it cannot re-run is the part that
needs `nm` over 108 objects, which is §3.4 and is why §3.4 shows two
instruments rather than one.

The census preconditions are the standing three and were met before every
reading above: `ninja -t cleandead`, then `ninja -n` requiring
`ninja: no work to do`, then `tools/shim-census --mtime-policy off`. Both graphs
were generated and both were built.
## Appendix — §4a items 2 and 3, re-measured, so nobody re-measures them again

The same lane was given both, on the grounds that each was a "0 consumers,
measured" claim still "present in N files". **Both were already done, and in both
cases the N was a grep over PROSE.** Recorded here so the next reader does not
repeat the measurement.

### Item 2 — the `PhysModelCreate*` family: nothing to delete

The item says "0 consumers on both sides, still present in 5 files". All five
hits are comments:

| file | what is actually there |
|---|---|
| `sourcesharp/game/shared/physics_shared.cpp:99` | a note about how the deleted `PhysModelCreateCustom` assigned `objectparams_t::pName` |
| `sourcesharp/game/server/physics.cpp:2125,2161` | two sentences using the name to explain when a solve is running |
| `sourcesharp/game/server/shell_standalone_base.cpp:1280,1285` | stock's code quoted, commented out |
| `sourcesharp/test/standalone_solidity_order_test.cpp:12` | stock's own comment, quoted as the thing the test pins |
| `sourcesharp/test/cpp_alias_t1_test.cpp:288` | a note saying this table's anchor was RE-POINTED off the family when it went |

The tombstone of record is this item: THE `PhysModelCreate*` FAMILY IS GONE
(plan_shim.md §4a item 2, lane 3a). The fork's `src/game/shared/physics_shared.h:158`
carries the same words as a comment, which the comment-free patched upstream
(`make sdk-upstream`) does not, so `physics_move_scope_test.cpp` reads them
here. With comments stripped, that header contains **zero** occurrences of any of the nine
names, and **two** of `PhysModelParseSolid` — the parse half that was never part
of the family and deliberately stays. That second number is the control: it is
what makes "zero" a reading of the right file rather than of an empty string.

**Nothing was deleted for this item and nothing needed to be.** What was missing
was a test: the family's absence was asserted nowhere, so a re-added declaration
would have been caught by nothing. `physics_move_scope_test.cpp` now asserts it,
with `PhysModelParseSolid`'s presence as the anti-vacuous half.

### Item 3 — the ragdoll LRU and low-violence manager: done, minus one deliberate keep

The LRU and the manager are gone (lane 3a). `ragdoll_shared.cpp:729`,
`shell_serversystems.cpp:359` and `shell_clientsystems.cpp:417` each carry a
tombstone naming what went and why; `anim_cluster_ledger_test` and
`client_flip_ruling_test` already assert the absence of the class, its two
accessors and its two system-table rows. Six of the item's six files are prose.

**The one piece of live code left is `ishim_level.h`'s `SetLowViolence` slot and
its empty override in `shell_level.cpp:655`, and this lane REFUSES to retire
it.** The refusal is not caution; the tree already argues it in three places:

- `ServerGameDll.cs:495` — "THE CALL STAYS ANYWAY, and for a stronger reason
  than before: its POSITION is the fact." Stock sets low violence at
  `gameinterface.cpp:1049` and precaches point templates at `:1053`; **this port
  ran that pair inverted** from the day the method was written, under a `[Fact]`
  that pinned the inversion, and `tools/gate5` found it on its first real run —
  no census, no live gate and no managed test could see it, "because the test
  that would have was written from the same wrong belief as the code".
- `ishim_level.h:65` makes the general ruling for this interface, for
  `PrecachePointTemplates`: "A retired slot is not called, so retiring this one
  would delete the only marker either ordering can be observed through. ONE
  no-op crossing per LEVEL is the price of that."
- Two live xUnit facts pin the order —
  `AFreshMapLoadRunsStocksNineStepsInStocksOrder` and
  `LowViolenceIsSetBeforeThePointTemplatesArePrecached`.

Retiring the slot would delete one of the two endpoints those facts are written
between. **It costs one no-op crossing per level and buys the only place the
repair can be observed. It stays.** This needed no ABI slot in either direction:
`SetLowViolence` is an `IShimLevel` vtable slot, not an `Abi.cs` field.
