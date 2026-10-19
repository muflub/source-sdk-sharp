# What remains C++, and why — re-derived at the close

`plan.md` §1a: native code exists only to bind the engine. `plan_shim.md` §1
gives the eight reasons that count, lettered (a) to (h). Until the close of the
shim project that ruling lived only in `plan_shim_rulings.tsv` — a 439-row
table measured at `85a7449d1` by read-only lanes, describing paths that have
since moved, split, merged or gone — and in `plan_entity.md` §6, a prose table
that named clusters rather than files and carried no letters at all. Nothing
re-derived either against the tree, and a reader opening a `.cpp` could not see
why the file was allowed to exist.

**The letter is now in the file it is about**, as line 2:

```
// SS_REASON a  a vtable the engine calls through
```

and `sourcesharp/test/shim_reason_letters_test.cpp` requires exactly one, at
column zero, with that letter's own text and no other, in every `.cpp` under
`src/sourcesharp/` outside `test/` and `managed/`.

**This document is derived from those markers, not from the rulings table.**
Every count below was produced by walking the tree.

## The letters

| | reason | example in the tree |
|---|---|---|
| a | a vtable the engine or VPhysics calls through | `CStandaloneEntity`, `CCollisionEvent`, `IPartitionEnumerator` |
| b | an interface resolved by name at load | `ServerGameDLL012`, `ServerGameClients004`, `PlayerInfoManager002` |
| c | static-init registration | `EXPOSE_INTERFACE`, `DECLARE_HUDELEMENT`, `CON_COMMAND`, `IGameSystem` |
| d | a layout the engine reads at fixed offsets | the engine block, `SourceSharpEntitySlot_t`, `CPlayerState` |
| e | a synchronous mid-step answer | `ShouldCollide`, `ShouldSolvePenetration`, `IMotionEvent::Simulate` |
| f | a bit-level wire codec | SendTable proxies, the user-message writes, the client's bitbuf members |
| g | leaf math over mdl/bsp data consumed by renderer or physics | `bone_setup.cpp`, `collisionutils.cpp`, the sprite/rope mesh builders |
| h | the CoreCLR host and bootstrap dispatch | `host/sourcesharp.cpp` |
| **x** | **no letter fits** — native only until a named native caller goes | the nine below |

"It was convenient", "it runs every frame" and "it would be slow in C#" are not
on the list. (g) is the weakest letter and the plan says so wherever it is the
only reason.

## Where the letters came from, file by file

This matters, because stamping a letter is making a claim:

| provenance | files | what it means |
|---|--:|---|
| the file's own header prose | 11 | a lane that relocated the file already wrote `REASON (n)` into it. The marker was taken from there, and `AMarkerAgreesWithTheProseItWasDerivedFrom` requires the two to keep agreeing. |
| `plan_shim_rulings.tsv` | 131 | the recorded per-file judgment of the analysis wave, transcribed into the file it is about. Moving it out of a table nobody re-derives and into the file a reader opens is the whole of ratchet 2. |
| ruled here, at the close | 26 | the rulings table left these blank or `-`: files created, split or relocated after `85a7449d1`, plus the ones its own lanes marked "no letter fits". |

**The agreement check's eleven are not quite these eleven, and the difference
is worth knowing.** It finds every file whose header states `REASON (n)` in the
form it matches, and that set excludes `physics_shared.cpp` — which writes
`REASONS (a), (b-adjacent), (e) and (g)`, four reasons, so there is no single
prose letter for a marker to agree with — and includes `public/studio.cpp`,
whose marker came from the rulings table and whose own prose independently says
the same letter. Eleven either way; all eleven agree.

Where a ruling row listed several letters, the marker carries the **primary**
one — the letter the majority of that file's rows led with. The secondary
reasons are in the rulings table and in the files' own header blocks; the
marker is the answer to "why is this native at all", not a full account.

## The table, re-derived

| | reason | srv | shd | cli | pub | com | host | files | lines |
|---|---|--:|--:|--:|--:|--:|--:|--:|--:|
| a | a vtable the engine or VPhysics calls through | 21 | 5 | 31 |  |  |  | 60 | 35,357 |
| b | an interface resolved by name at load | 12 | 3 | 16 |  |  |  | 41 | 18,090 |
| c | static-init registration | 17 | 6 | 3 |  |  |  | 27 | 4,077 |
| d | a layout the engine reads at fixed offsets | 5 | 6 | 5 | 1 |  |  | 20 | 9,383 |
| e | a synchronous mid-step answer | 5 | 3 | 2 |  |  |  | 9 | 4,552 |
| f | a bit-level wire codec | 5 | 5 | 2 | 1 |  |  | 13 | 3,715 |
| g | leaf math over mdl/bsp data consumed by renderer or physics | 4 | 1 | 7 | 1 |  |  | 15 | 9,418 |
| h | the CoreCLR host and bootstrap dispatch | 1 | 4 | 2 |  |  | 1 | 8 | 5,112 |
| x | NO LETTER FITS | 7 | 2 | 1 |  | 2 |  | 18 | 4,559 |
|  | **total** | **77** | **35** | **69** | **3** | **2** | **1** | **211** | **94,263** |

The line counts were re-taken when SourceSharp moved to its own repository: the
7-line Valve attribution header added to each file accounts for the whole rise
(124,588 → 125,276), and no file changed otherwise.
Re-taken 2026-09-29 when server.so's tier1 string calls were rewritten onto
ss_string.h and the C library: the rise is the `#include` lines that brought
the helpers in; every call changed on its own line (125,061 → 125,100).
Re-taken 2026-10-02 at plan_side_isolation P2: the user-message registry and
dispatch moved into C#. shell_usermessage_registry.cpp (b) and
shell_usermessages.cpp (c) left both builds; the client's forwarders are
shell_usermessages_client.cpp (b); shell_usermessage_send.cpp (a) holds the
Valve senders' forwarders and the send slot, and shell_gamedll.cpp (a)
forwards GetUserMessageInfo (122,379 → 121,630).
Re-taken 2026-10-04 at the user-message payload model: shell_vieweffects.cpp
(f) left both builds -- its shake and fade senders had no caller once C#'s
ScreenEffects sent both payloads -- and its view punch walk moved into
shell_scripted.cpp (a) (121,643 → 121,323). sound_emitter.cpp (b) forwards
the close caption's facts to C# instead of writing the message
(121,323 → 121,341), and shell_usermessage_send.cpp (a) loses the native
senders' UserMessageBegin, MessageEnd, MessageWriteRawBits and their sentinel
(121,341 → 121,265). shell_usermessages_client.cpp (b) gains the bodies of
the seven client slots the ported stock hooks carry their effects out through
(121,265 → 121,421).
Re-taken 2026-10-04 at the debug-api lane: exit_watch.cpp (a) is new -- the
dev API's exit recorders, a signal handler and SDL's event watch, called where
managed code cannot run -- with shell_standalone_player.cpp's player-shadow
counter copy (a) and fatal_report.cpp's weak call to the fatal witness (h)
(121,827 → 122,216).
Re-taken 2026-10-05 at webhelper-assert: shell_client.cpp (b) defers the info
panel to its first lookup, so building its vgui::HTML no longer connects every
game to Steam's web helper (122,216 → 122,236).
Re-taken 2026-10-05 at launch-args: ss_command_line.cpp (x) reads the
launcher's SOURCESHARP_ARGS after argv, where the launcher moves SourceSharp's
own flags off the engine's 512-byte command line (122,236 → 122,257).
Re-taken 2026-10-09 at the merged todo batch (tempent-pool retirement,
PlayerBoxInPvs, client prop collisions): (a) 38,228 -> 38,145 is
shell_te_pool_client.cpp's four dead exports (-155) against
shell_standalone_client.cpp's collideable twin (+72); (d) 10,985 -> 11,027
is shell_entityapi.cpp's PlayerBoxInPvs body (+42); (f) 5,260 -> 5,269 is
shell_standalone.cpp's received solidity fields (+9). File counts unchanged
in every row.
Re-taken 2026-10-11 at plan_physics_bones_native_zero 2.3's diagnostics move:
the SS-PHYSWORLD report, physics_budget, PhysForceEntityToSleep and
air_density are managed's. (e) 6,143 -> 5,706 is physics.cpp (-437); (c)
5,527 -> 5,319 is physics_debug.cpp's report command, physics_budget and the
dead flood list (-208). File counts unchanged (110,782 in all). Then the
native rule table, whose last reader was that report: (a) 39,916 -> 39,872 is
shell_gamerules.cpp's push into it (-44), (d) 11,031 -> 11,009 is
physics_shell.cpp's layout asserts on it (-22); 110,716 in all.
Then the sound batches' emit: (a) 39,872 -> 39,904 is shell_sound.cpp's
EmitWorldSoundScript, the carry-out of a queued sound managed's
PhysicsSoundBatch decides (+32); 110,748 in all.
Then the native sound batches: (e) 5,706 -> 5,572 is physics.cpp's queues,
their play and the two queueing slots, managed's PhysicsSoundBatch since
2.3 (-134); 110,614 in all.
Then the server's served physics names (2.5): (b) 17,698 -> 17,710 is
shell_vtable_interfaces.cpp serving VPhysicsCollision007, which managed's
player shadow acquires for BBoxToCollide (+12); 110,626 in all.
Then the player shadow's switch (2.5): (a) 39,904 -> 40,028 is
shell_standalone_player.cpp's facts, adopt, release and commit slots (+124;
the old InitVCollision / probe / apply bodies leave next), (c) 5,319 ->
5,305 is shell_standalone_player_debug.cpp's cs_sv_playershadow, managed now
(-14), (d) 11,009 -> 11,007 is shell_entityapi.cpp's teleport of the
shadow, managed's EngineSide.Teleported now (-2); 110,734 in all.
Then the shadow's native leftovers (2.5): (a) 40,028 -> 39,350 is
shell_standalone_player.cpp's InitVCollision, SetVCollisionState,
ShadowProbe, ShadowApply, TeleportShadow, GroundPhysicsMass and their slot
bodies and counters, PlayerShadowBodies' since 23bd46067 (-678); 110,056 in
all.
Then the physics frame (2.3): (e) 5,572 -> 5,425 is physics.cpp's PhysFrame, PhysTimescale and the profile state, managed's PhysicsFrame now, against the owners' update it carries out (-147); (a) 39,350 -> 39,295 is shell_serversystems.cpp's event-ring sink and its bind (-47) and shell_scripted.cpp's ring drain (-8); 109,854 in all.
Then the player's stress walk (2.3): (a) 39,295 -> 39,176 is shell_standalone_player.cpp's AwakeShadow, AwakeShadows, ShadowStress and RecordShadowStress, PhysicsFrame's and PlayerShadowStress' now (-119); (e) 5,425 -> 5,418 is physics.cpp's PhysicsRecordShadowStress (-7); 109,728 in all.
Then the event ring (2.3): nothing recorded into it once the frame was managed's. (d) loses physics_shell.cpp, the ring's and the id table's layout asserts (one server file, -141); (c) 5,305 -> 5,254 is physics_debug.cpp's cs_sv_physics_shell (-51); (e) 5,418 -> 5,408 is physics.cpp's ring clears (-10); (a) 39,176 -> 39,175 is shell_scripted.cpp's include (-1); 205 files, 109,525 lines in all.
Then the NaN ledger (2.5): (c) 5,254 -> 5,246 is physics_debug.cpp's [SS-PHYSWORLD-NAN] line, managed's ss_physworld_nan now (-8); (a) 39,175 -> 39,171 is shell_standalone_player.cpp's two dead includes (-4); 109,513 in all.
Then 2.6's server ABI commit, re-measured on main 466d9e12c: (b) 17,905 -> 17,921 is shell_vtable_interfaces.cpp serving StaticPropMgrServer002 to managed's world build (+16); 98,538 in the table.
Then physics_shared.cpp's server half (2.6, re-measured on 466d9e12c): (e) 5,543 -> 5,547 is physics.cpp, which asks managed for the world and the surface sounds and now defines the five vphysics pointers (+4); (a) 35,889 -> 35,893 is shell_standalone_base.cpp asking managed for a model's solid (+1) and shell_serversystems.cpp's tombstone for the CDataObjectAccessSystem row (+3); 98,546 in the table. physics_shared.cpp itself is still the client's.
Then the client flip (2.6): shell_physics_shared_client.cpp arrives in (a) (client, +154 lines, one file), the remnant fork game/client/physics.cpp links; physics_shared.cpp is still on disk until the next commit. (a) 59 -> 60 files, 35,893 -> 36,047; 206 files, 98,700 in the table.
Then physics_shared.cpp left the repository (2.6, re-measured on 466d9e12c): (a) 36,047 -> 34,684 and 60 -> 59 files (shared, -1,363); shd 6 -> 5; 205 files, 97,337 in the table.

Re-taken 2026-10-12 when the native build gained its Windows and macOS columns: the platform calls our shims made (dladdr, backtrace, mprotect, /proc, sigaction, dlopen) are spelled once in ss_platform.h, a header, and the files that made them now branch per platform; no file left or arrived (99,421 → 99,753, and the ratchet's shim_lines row rose with the header, 142,747 → 143,348, which binds the three-platform spelling and nothing else).

Then the entity physics init's ABI row (2026-10-12, re-measured by the walk on 0b8787ad5): (e) 5,547 -> 5,569 is shell_physicsobjects.cpp's SourceSharp_EntitySwapPhysicsObject (+22), the shell holding the object managed's EntityPhysicsCreation makes (stock's VPhysicsSwapObject); 97,359 in the table. CStandaloneEntity::InitPhysics, which it replaces, leaves in a later commit.

Then native's EntityInitPhysics and EntitySetPhysicsStatic left the ABI (2026-10-12, re-measured by the walk on 0b8787ad5): (e) 5,569 -> 5,516 is shell_physicsobjects.cpp's SourceSharp_EntityInitPhysics (-53); (a) 34,684 -> 34,663 is shell_scripted.cpp's SourceSharp_EntitySetPhysicsStatic (-21); 97,285 in the table.

Then CStandaloneEntity::InitPhysics left (2026-10-12, re-measured by the walk on 0b8787ad5): (a) 34,663 -> 34,439 is shell_standalone_base.cpp (InitPhysics, its spawn arm and the solidity-order convar ref) and shell_dispatch.cpp's OnManagedPhysicsCreated relay (-224); 97,061 in the table.

Then EntityConvertToPhysics left the ABI (2026-10-12, re-measured by the walk on 0b8787ad5): (e) 5,516 -> 5,468 is shell_physicsobjects.cpp's always-0 stub and its section (-48); phys_convert is managed's PhysicsConversion; 97,013 in the table.

Then PhysicsCollideExtent and PhysicsInertia left the ABI (2026-10-12, re-measured by the walk on 0b8787ad5): (a) 34,439 -> 34,402 is shell_scripted.cpp's two Vector proxies (-37); a Vector by-value return is spelled, and managed calls both through the vtables; 96,976 in the table.

Then shell_physicsobjects.cpp's header said what the file still holds (2026-10-12, on 0b8787ad5): (e) 5,468 -> 5,465 (-3); 96,973 in the table. Its letter stays (e): what is left is the hold the engine reads and the owner lookups vphysics' callbacks ask mid-step.

Then the last thin vphysics forwards left the ABI (2026-10-12, re-measured by the walk on main 1ee3376e1): (a) -110 is shell_scripted.cpp's mass, force-offset, torque, motion, wake, sleep and game-flag forwards; (d) -46 is shell_entityapi.cpp's trigger and velocity-impulse forwards; (e) -17 is shell_worldquery.cpp's ApplyForceCenter; managed's EntityPhysicsMotion makes each call through PhysicsObjectVtbl; 97,915 in the table.

Then the velocity home left native (2026-10-12, re-measured by the walk on main 1ee3376e1): (a) -69 is shell_scripted.cpp's two angular-velocity bodies keeping only the shell's member; (d) -78 is shell_entityapi.cpp's two velocity bodies likewise and EntityTeleport's solver half; managed's EntityPhysicsMotion decides the home and moves a teleported object; 97,768 in the table.

Then the pooled beam renderable's handle reset (2026-10-12, measured by the walk on main 1ee3376e1): (g) 9,702 -> 9,704 is shell_beam_render.cpp (+2), a bug fix the phys-convert gate step found; 97,770 in the table.

Then the frame's skeleton batch (2026-10-12, plan_physics_bones_perf A2, measured by the walk on main 90f3b4296): (a) 34,393 -> 34,453 is shell_standalone_client.cpp (+60), SetupBones answering from the table managed posed at the frame's first ask (IManagedBonesClient::BoneBatch) and FrameBoneBatch asking for it; 25 of the client's 52 crossings a frame were SetupBones asks; 97,830 in the table.

Then the port's small todos (plan_physics_bones_perf lane C, 2026-10-12, re-measured by the walk on its branch rebased onto main 20450cce0): (a) 34,453 -> 34,401 is shell_standalone_player.cpp (-30: NoteTouchedPhysics and m_oldOrigin out, the touch list recorded instead; the decisions are PlayerShadowBodies'), standalone_relays.cpp (-18: the ground velocity's solver-or-member pick out, a held read in) and shell_standalone_base.cpp (-4: the asleep-twice skip out, the held velocity's two writes in); (e) 5,448 -> 5,458 is physics.cpp (+9: the owner batch holds the velocity managed hands over) and shell_worldquery.cpp (+1: the movetype word managed writes); 97,788 in the table. shell_velocity_home.h left the tree and carried no marker.

Then libmathlib.a left server.so's link (2026-10-13, measured by the walk on main aeff4a2): (g) arrives with sourcesharp/game/server/mathlib_port.cpp (server, +79 lines, one file), the mathlib members server.so calls, forwarding to mathlib_port.h's inlines; 219 files, 99,819 in the table. Then (b) 18,658 -> 18,657 is shell_gamedll.cpp's MathLib_Init call, gone with the archive (-1); 99,818 in the table.

Then the engine forwarders (plan_engine_forwarders.md phase 1, 2026-10-13, measured by the walk on main 25e9c07): (b) arrives with sourcesharp/fwd/fwd_engine.cpp, libss_fwd_engine's one translation unit, which serves the generated IVModelInfo forwarders by name (+38 lines, one file); 18,657 -> 18,695; 220 files, 99,856 in the table. Then the game DLLs serve its tables: (b) 18,695 -> 18,704 is shell_internalfactory.cpp asking the library (+9); 99,865 in the table. The library is its own location, fwd, outside the srv/shd/cli columns. Then the library links no runtime: (b) 18,704 -> 18,710 is fwd_engine.cpp comparing table names itself in place of libc's strcmp (+6), and defining _fltused for its CRT-free Windows build (+7); 99,878 in the table. Then every interface managed calls (plan_engine_forwarders.md phases 2-4): (b) 18,717 -> 18,722 is sourcesharp/fwd/fwd_vphysics.cpp, libss_fwd_vphysics's one translation unit (+1 file), with both libraries' lookup moved into ss_fwd.h, a header the table does not count; 221 files, 99,883 in the table. Then the Steam callback (plan_engine_forwarders.md phase 5): (b) 18,722 -> 18,853 is sourcesharp/fwd/fwd_steam.cpp, libss_fwd_steam's one translation unit (+131 lines, one file), which builds the CCallbackBase Steam registers so the compiler orders its two Run overloads, and forwards both to the managed-built IManagedSteamCallback; 222 files, 100,014 in the table. Then one forwarder library per side (ruled 2026-10-18): (b) 18,853 -> 18,857 is sourcesharp/fwd/fwd_server.cpp and fwd_client.cpp in place of fwd_engine.cpp and fwd_vphysics.cpp (+4 and +5, one translation unit for one), and fwd_steam.cpp a source of the client's library, its functions declared in fwd_steam.h, a header the table does not count (-5); 222 files, 100,018 in the table. Then the vphysics callbacks (plan_forwarders_everywhere.md phase 5): (b) 18,857 -> 19,028 is sourcesharp/fwd/fwd_physics.cpp, a source of both libraries (+195 lines, one file), which builds each Valve callback interface vphysics is handed in a managed block and forwards its virtuals to a managed-built table, with the libraries' pure-virtual handler moved into it from fwd_steam.cpp (-24); 223 files, 100,189 in the table. Then no acquire counts a vtable (plan_forwarders_everywhere.md phase 6): managed calls every area and engine interface through a forwarder table carrying its own count, so the slot counts the internal factory handed back go. (b) 19,028 -> 18,857 is shell_vtable_interfaces.cpp's twelve per-interface counts becoming one served-set check (-177), shell_internalfactory.cpp's and particles_lib_client.cpp's acquires (-10, -3), and the IVEngineClient build-flag guard moving into fwd_client.cpp (+19); the generated areas' acquires lost theirs too, in .inc files the walk does not count; (a) 35,001 -> 34,962 is shell_frame.cpp and shell_level.cpp's acquires and their count comments; (h) 4,841 -> 4,839 is sourcesharp_gamesystem.cpp's two vtable_hook.h includes; 223 files, 99,977 in the table. Then the client's render-policy model counts left native (studio.cpp's eviction, 2026-10-18, measured by the walk on 385914c8c): (a) 34,889 -> 34,839 is shell_standalone_client.cpp's PublishModelFacts, which no longer opens a CStudioHdr for the four counts ClientRenderPolicy now reads from the managed header (-50); 99,630 in the table. Then the model descriptor left native (2026-10-18, measured by the walk): (f) 4,670 -> 4,465 is sourcesharp/game/shared/model_descriptor.cpp, deleted with IShimModels' ModelDescriptor slot (-205 lines, one file); the managed StudioModelDescriptor reads the descriptor from the side's StudioHdr; 219 files, 99,425 in the table. Then studio.cpp left both DLLs and the repository (2026-10-18, measured by the walk): (d) 11,924 -> 10,404 is sourcesharp/public/studio.cpp (-1,520 lines, one file, the pub column), whose last consumers read the managed StudioHdr; 218 files, 97,905 in the table. Its prose letter went with it, so the agreement check reads six files (it read seven). Then studio_mdlcache.cpp left too (2026-10-18, measured by the walk): (g) is sourcesharp/game/shared/studio_mdlcache.cpp (-56 lines, one file), the model-cache questions studio.h declares, whose only caller was studio.cpp; 217 files, 97,849 in the table.

Then physics_debug.cpp left (2026-10-18, measured by the walk on the lane rebased onto main f707d5b8c): its six debug commands are managed's PhysicsDebugCommands.cs. (c) loses the file (server, -566 lines, 29 -> 28 files, 5,345 -> 4,779); 223 files, 100,162 lines in the table.

Then detailobjectsystem.cpp left client.so (C7 cluster 10, 2026-10-18, measured by the walk on the lane rebased onto main a7ee694f9): (a) arrives with sourcesharp/game/client/shell_detail_props.cpp (client, +497 lines, one file), client.so's IDetailObjectSystem, the model props' IClientRenderable and the leaf enumerator, carrying out SourceSharp.Client.Detail's answers; (g) 9,755 -> 9,748 is shell_client_orphans.cpp's GetDetailController, whose only importer was detailobjectsystem.cpp (-7); 218 files, 98,339 in the table.

Then the server's last two native skeleton slots left the ABI (2026-10-18, lane claude/tail-skeleton-standalone, measured by the walk on the lane rebased onto main f4b31392d): (a) 34,846 -> 34,798 is shell_scripted.cpp's SourceSharp_EntitySetSequence and its comment block (-48); (g) 9,446 -> 9,415 is shell_standalone_hierarchy.cpp's SourceSharp_EntityLookupAttachmentAny and StandaloneBones_Unreached (-31). ServerSkeletons answers both; no file left or arrived; 209 files, 93,286 lines in the table.

Then CI on main (2026-10-19, measured by the walk on 546910d): client.dll's MSVC link without tier0.lib. (d) 9,357 -> 9,383 is tier_bindings_client.cpp emitting tier0's inline CThreadMutex and CThreadSpinRWLock members, which fork objects import where MSVC did not inline them (+18), and binding g_pVCR, through which the emitted Lock enters its critical section, to tier0.dll's (+8); (x) 4,540 -> 4,559 is tier0_vprof_client.cpp emitting CVProfile's inline EnterScope and ExitScope the same way (+16), and defining its profile in an extern "C" block, which gcc 10 requires (+3); 209 files, 93,331 in the table.

#### (a) a vtable the engine or VPhysics calls through — 55 files, 49,282 lines

`sourcesharp/game/client/shell_beam_endpoint_client.cpp`  
`sourcesharp/game/client/shell_clientdll_voicehelper.cpp`  
`sourcesharp/game/client/shell_detail_props.cpp`  
`sourcesharp/game/client/shell_dust_render.cpp`  
`sourcesharp/game/client/shell_effects_sim.cpp`  
`sourcesharp/game/client/shell_explosion_overlay.cpp`  
`sourcesharp/game/client/shell_hud.cpp`  
`sourcesharp/game/client/shell_hud_framework.cpp`  
`sourcesharp/game/client/shell_recipientfilter_client.cpp`  
`sourcesharp/game/client/shell_renderable_facts.cpp`  
`sourcesharp/game/client/shell_rope_render.cpp`  
`sourcesharp/game/client/shell_smoke_render.cpp`  
`sourcesharp/game/client/shell_smokestack_render.cpp`  
`sourcesharp/game/client/shell_smokestack_sim.cpp`  
`sourcesharp/game/client/shell_smokevolume_sim.cpp`  
`sourcesharp/game/client/shell_standalone_client.cpp`  
`sourcesharp/game/client/shell_steamjet_render.cpp`  
`sourcesharp/game/client/shell_steamjet_sim.cpp`  
`sourcesharp/game/client/shell_surface_render.cpp`  
`sourcesharp/game/client/shell_te_backends_client.cpp`  
`sourcesharp/game/client/shell_te_forwards_client.cpp`  
`sourcesharp/game/client/shell_te_particle_render.cpp`  
`sourcesharp/game/client/shell_te_pool_client.cpp`  
`sourcesharp/game/client/shell_trail_sim.cpp`  
`sourcesharp/game/client/shell_worldtext_sim.cpp`  
`sourcesharp/game/server/entity_bases.cpp`  
`sourcesharp/game/server/exit_watch.cpp`  
`sourcesharp/game/server/shell_areaportal.cpp`  
`sourcesharp/game/server/shell_clientsteps.cpp`  
`sourcesharp/game/server/shell_constraints.cpp`  
`sourcesharp/game/server/shell_decals.cpp`  
`sourcesharp/game/server/shell_dispatch.cpp`  
`sourcesharp/game/server/shell_frame.cpp`  
`sourcesharp/game/server/shell_level.cpp`  
`sourcesharp/game/server/shell_recipientfilter.cpp`  
`sourcesharp/game/server/shell_scripted.cpp`  
`sourcesharp/game/server/shell_serversystems.cpp`  
`sourcesharp/game/server/shell_sound.cpp`  
`sourcesharp/game/server/shell_standalone_base.cpp`  
`sourcesharp/game/server/shell_standalone_class.cpp`  
`sourcesharp/game/server/shell_standalone_player.cpp`  
`sourcesharp/game/server/shell_te_forwards.cpp`  
`sourcesharp/game/server/shell_tempents.cpp`  
`sourcesharp/game/server/shell_usermessage_send.cpp`  
`sourcesharp/game/server/shell_world_nettable.cpp`  
`sourcesharp/game/server/standalone_relays.cpp`  
`sourcesharp/game/server/world_adapter.cpp`  
`sourcesharp/game/shared/collisionproperty.cpp`  
`sourcesharp/game/shared/console_display_forward.cpp`  
`sourcesharp/game/shared/physics_shared.cpp`  
`sourcesharp/game/shared/shell_gameevents.cpp`  
`sourcesharp/game/shared/shell_gamerules.cpp`  
`sourcesharp/game/shared/shell_physsaverestore_absent.cpp`  

#### (b) an interface resolved by name at load — 30 files, 16,003 lines

`sourcesharp/fwd/fwd_server.cpp`  
`sourcesharp/fwd/fwd_client.cpp`  
`sourcesharp/fwd/fwd_steam.cpp`  
`sourcesharp/fwd/fwd_physics.cpp`  
`sourcesharp/game/client/shell_factory_client.cpp`  
`sourcesharp/game/client/shell_client.cpp`  
`sourcesharp/game/client/shell_clientdll.cpp`  
`sourcesharp/game/client/shell_clientdll_appsystems.cpp`  
`sourcesharp/game/client/shell_clientdll_gameclientexports.cpp`  
`sourcesharp/game/client/shell_cliententitylist.cpp`  
`sourcesharp/game/client/shell_clientsystems.cpp`  
`sourcesharp/game/client/shell_entitystatics.cpp`  
`sourcesharp/game/client/shell_input.cpp`  
`sourcesharp/game/client/shell_prediction.cpp`  
`sourcesharp/game/client/shell_stringtables_client.cpp`  
`sourcesharp/game/client/shell_tempents_client.cpp`  
`sourcesharp/game/client/shell_usermessages_client.cpp`  
`sourcesharp/game/server/plugin_check.cpp`  
`sourcesharp/game/server/shell_commandclient.cpp`  
`sourcesharp/game/server/shell_engineboundary.cpp`  
`sourcesharp/game/server/shell_engineservices.cpp`  
`sourcesharp/game/server/shell_gameclients.cpp`  
`sourcesharp/game/server/shell_gamedll.cpp`  
`sourcesharp/game/server/shell_gameents.cpp`  
`sourcesharp/game/server/shell_gametags.cpp`  
`sourcesharp/game/server/shell_playerinfo.cpp`  
`sourcesharp/game/server/shell_playerslots.cpp`  
`sourcesharp/game/server/shell_sentences.cpp`  
`sourcesharp/game/server/shell_server.cpp`  
`sourcesharp/game/server/shell_sharedappsystems.cpp`  
`sourcesharp/game/server/shell_stringtables.cpp`  
`sourcesharp/game/shared/shell_internalfactory.cpp`  
`sourcesharp/game/shared/shell_vtable_interfaces.cpp`  
`sourcesharp/game/shared/sound_emitter.cpp`  

#### (c) static-init registration — 7 files, 2,790 lines

`sourcesharp/game/client/shell_effect_ledger.cpp`  
`sourcesharp/game/client/shell_materialproxies.cpp`  
`sourcesharp/game/server/shell_debugoverlays.cpp`  
`sourcesharp/game/server/shell_mapparse.cpp`  
`sourcesharp/game/shared/movevars_shared.cpp`  
`sourcesharp/game/shared/shell_shared.cpp`  

#### (d) a layout the engine reads at fixed offsets — 16 files, 11,183 lines

`sourcesharp/game/client/keyvalues_client.cpp`  
`sourcesharp/game/client/shell_player_orphans.cpp`  
`sourcesharp/game/client/shell_view_facts.cpp`  
`sourcesharp/game/client/shell_worldrender.cpp`  
`sourcesharp/game/client/world_adapter_client.cpp`  
`sourcesharp/game/server/server_util_library.cpp`  
`sourcesharp/game/server/shell_entityapi.cpp`  
`sourcesharp/game/server/shell_entityslots.cpp`  
`sourcesharp/game/server/shell_globals.cpp`  
`sourcesharp/game/shared/gamestringpool.cpp`  
`sourcesharp/game/shared/shell_movement.cpp`  
`sourcesharp/game/shared/standalone_player_sync.cpp`  
`sourcesharp/game/shared/world_net_sync.cpp`  
`sourcesharp/public/dt_send.cpp`  

#### (e) a synchronous mid-step answer — 10 files, 10,407 lines

`sourcesharp/game/client/shell_worldquery_client.cpp`  
`sourcesharp/game/server/physics.cpp`  
`sourcesharp/game/server/shell_motioncontrollers.cpp`  
`sourcesharp/game/server/shell_physicsobjects.cpp`  
`sourcesharp/game/server/shell_soundenvelope.cpp`  
`sourcesharp/game/server/shell_worldquery.cpp`  
`sourcesharp/game/server/toucher.cpp`  
`sourcesharp/game/server/world_subject_dll.cpp`  
`sourcesharp/game/shared/takedamageinfo_library.cpp`  
`sourcesharp/game/shared/trace_filter_library.cpp`  

#### (f) a bit-level wire codec — 16 files, 6,123 lines

`sourcesharp/game/client/generic_payload_client.cpp`  
`sourcesharp/game/client/shell_recvtable.cpp`  
`sourcesharp/game/client/shell_world_nettable_client.cpp`  
`sourcesharp/game/server/sendproxy.cpp`  
`sourcesharp/game/server/shell_nettable.cpp`  
`sourcesharp/game/server/shell_playerlocaldata.cpp`  
`sourcesharp/game/server/shell_soundscape.cpp`  
`sourcesharp/game/server/shell_standalone.cpp`  
`sourcesharp/game/server/shell_transmitsteps.cpp`  
`sourcesharp/game/shared/entity_shared_statics.cpp`  
`sourcesharp/game/shared/generic_payload.cpp`  
`sourcesharp/game/shared/shell_savegame.cpp`  
`sourcesharp/game/shared/util_shared_library.cpp`  
`sourcesharp/public/dt_utlvector_common.cpp`  

#### (g) leaf math over mdl/bsp data consumed by renderer or physics — 21 files, 21,639 lines

`sourcesharp/game/client/shell_client_orphans.cpp`  
`sourcesharp/game/client/shell_embers_render.cpp`  
`sourcesharp/game/client/shell_impact_effects.cpp`  
`sourcesharp/game/client/shell_sprite_leaf.cpp`  
`sourcesharp/game/client/shell_sprite_render.cpp`  
`sourcesharp/game/client/shell_trail_render.cpp`  
`sourcesharp/game/client/shell_worldtext_render.cpp`  
`sourcesharp/game/server/baseentity_statics.cpp`  
`sourcesharp/game/shared/mathlib_port.cpp`  
`sourcesharp/game/server/precache_model.cpp`  
`sourcesharp/game/server/shell_standalone_hierarchy.cpp`  
`sourcesharp/game/shared/bone_library.cpp`  
`sourcesharp/game/shared/overlay_library.cpp`  
`sourcesharp/game/shared/particle_manifest.cpp`  
`sourcesharp/game/shared/ragdoll_shared.cpp`  
`sourcesharp/game/shared/studio_sequence_timing.cpp`  
`sourcesharp/public/bone_setup.cpp`  
`sourcesharp/public/collisionutils.cpp`  

#### (h) the CoreCLR host and bootstrap dispatch — 7 files, 5,583 lines

`sourcesharp/game/client/client_util_library.cpp`  
`sourcesharp/game/client/managed_runtime_client.cpp`  
`sourcesharp/game/server/managed_runtime_server.cpp`  
`sourcesharp/game/shared/shell_acquire_reasons.cpp`  
`sourcesharp/game/shared/shell_diagnostics.cpp`  
`sourcesharp/game/shared/sourcesharp_gamesystem.cpp`  
`sourcesharp/host/sourcesharp.cpp`  


## (x) The fourteen files for which no letter fits

These are **not native by right**. Each is native because some other native
caller has not been ported yet, and each one's exit condition is a named file.
`shim_reason_none` in `docs/shim-ratchet.tsv` caps the count at 14, and
`TheFilesWithNoLetterAreCappedAndEachHasAnExitCondition` checks this list
against the tree **in both directions** — a marker with no row here reddens,
and a row here whose file no longer carries an `x` reddens too, so neither half
can rot alone.

Forcing a letter onto these would have hidden the single most useful number the
close produces. **This is the number the port exists to drive to zero.**

| file | lines | what keeps it native | exit condition |
|---|--:|---|---|
| `sourcesharp/game/client/randoverride.cpp` | 30 | nothing of its own. `plan_shim_rulings.tsv` ruled it KEEP with the reason written out as "- (no letter fits)", riding on the leaf-renderer row rather than earning one | the leaf-renderer cluster it rides on |
| `sourcesharp/common/steamid.cpp` | 741 | nothing of its own — `plan_shim_rulings.tsv` ruled it **DELETE** and the deletion was never executed | its own deletion, with a 0-consumer proof |
| `sourcesharp/game/server/alloc_check.cpp` | 637 | nothing the engine asks for: it is the `__wrap_` half of the debug build's allocation link hook (plan_server_novalve.md §3.6), proving no block is freed across the engine/mod boundary. It compiles to nothing outside `SS_ALLOC_CHECK` | the release build carries no checker, so none; it leaves only if the no-cross-free rule gets another instrument |
| `sourcesharp/game/server/shell_absent.cpp` | 342 | eleven definitions that an object on the server link line still imports. The file states its own exit condition as zero and does not reach it | the last importer of each of the eleven |
| `sourcesharp/game/shared/tier0_spew_port.cpp` | 356 | tier0's spew and assert entry points (Msg, Warning, DevMsg, Error, the five functions every Assert expands to, the spew slot), defined in server.so so it links no libtier0 (plan_server_novalve.md P4). Native code calls them through `public/tier0/dbg.h` | P5's header move: no server object calling a `dbg.h` function |
| `sourcesharp/game/shared/tier0_thread_port.cpp` | 202 | the tier0 thread and platform calls fork-header inlines make: `ThreadGetCurrentId` and `CThreadFastMutex::Lock( id, sleep )` (studio.h's `CStudioHdr` holds two fast mutexes) and `Plat_IsInDebugSession` (`DebuggerBreak`, in `_DEBUG` asserts and tslist.h), defined in server.so so it links no libtier0 for them. Our own code uses std for these (collisionproperty's partition lock, ss_clock.h) | P5's header move: when no server object includes threadtools.h or platform.h's `DebuggerBreak` |
| `sourcesharp/game/client/vstdlib_random_port.cpp` | 144 | vstdlib's `RandomSeed`/`RandomInt`/`RandomFloat`/`RandomFloatExp` over one server-owned stream of the same generator (the P0 random probe: callers need the algorithm, not vstdlib's shared instance), so server.so links no libvstdlib. Callers: `randoverride.cpp`'s `rand()`, `bone_setup`'s attachment pick, `SharedRandom*` | its callers moving managed: `SharedRandom*` with usercmd prediction, the attachment pick with bone setup, `rand()` with randoverride.cpp's deletion |
| `sourcesharp/game/shared/ss_command_line.cpp` | 157 | this process's command line from `/proc/self/cmdline` and the launcher's `SOURCESHARP_ARGS`, in place of tier0's `CommandLine()`, with LocalCommandLine.cs's matching: native still decides `-assertlog`/`-assertstack` (shell_diagnostics), `-makereslists` (sound_emitter), and serves `SourceSharp_CommandLineHasParam`/`GetParamValue` to managed | managed answering its command-line questions from `ProcessCommandLine` (the two EngineApi slots retired) and the two native decisions moving with it |
| `sourcesharp/game/server/shell_decals.cpp` | 162 | the three decal-name lookups (ss_decals.h), each a forward to SourceSharp.Server's DecalEmitter. It was (a) while `decalsystem` was an IDecalEmitterSystem the tree called through; that pointer left with its last caller (P5 lane SP), so the honest letter is none | `world_adapter.cpp`'s `CWorld::DecalTrace` asking managed code itself |
| `sourcesharp/game/server/shell_soundent.cpp` | 209 | five entry points native code calls (ss_soundent.h: Init, Shutdown, InsertSound, the client-sound pair), each a forward to SourceSharp.Server's SoundEnt. CSoundEnt's statics left with their last caller (P5 lane SP) | `world_adapter.cpp`, `shell_level.cpp`, `shell_gameclients.cpp` and `shell_absent.cpp` calling managed directly |
| `sourcesharp/game/shared/shell_movement_audit.cpp` | 108 | arithmetic with no engine in it — a float comparison and a switch. Its one non-test caller is `shell_movement.cpp:201` | that one call site, which can ask managed code instead |
| `sourcesharp/game/client/shell_viewrender_client.cpp` | 783 | `view`, the client's IViewRender (C7 step 3: view.cpp, viewrender.cpp and viewdebug.cpp left): CShellViewRender carries out managed's view decisions (SetUpViews, Render) and the plan's views, holds the view state the fork's TUs read through MainView*/CurrentView*/SetupCurrentView/R_CullSphere, and the convars and commands native code links or that act on native state | the fork's readers of the view state and of mat_wireframe, r_DrawDetailProps and cl_drawshadowtexture (particlemgr.cpp, clientshadowmgr.cpp, detailobjectsystem.cpp, glow_overlay.cpp, c_pixel_visibility.cpp, the shells) leaving or reading by name, and the engine-facing carry-out moving behind generic ABI calls |
| `sourcesharp/game/client/strtools_client.cpp` | 206 | strtools.h's names that client.so's objects, particles.a, dmxloader.a and the tier1 members kept for those archives still call, over ss_string.h (user ruling 2026-10-12: client-only what the server does not need) | each name's last caller leaving; for the archives' share, particles.a and dmxloader.a leaving client.so |
| `sourcesharp/game/client/tier0_thread_client.cpp` | 56 | tier0's `ThreadInMainThread`, which only client objects call (shell_clientdll's load pump, shell_entitystatics' abs-query guards; clientleafsystem and sequence_Transitioner while on the link); beside the shared tier0_thread_port.cpp because the server does not need it (user ruling 2026-10-12). And `Plat_FloatTime` over ss_clock.h, for particlemgr.cpp's one difference of two readings, so client.so links no libtier0 (C9, 2026-10-18) | those call sites leaving or asking another way; for `Plat_FloatTime`, particlemgr.cpp leaving |
| `sourcesharp/game/client/tier0_vprof_client.cpp` | 93 | client.so's own `g_VProfCurrentProfile`, never enabled, and the three `CVProfNode` members the VPROF macros name (user ruling 2026-10-18: drop vprof from client.so rather than bind to the engine's profile), so client.so links no libtier0 (C9) | clientleafsystem.cpp leaving client.so: its VPROF_BUDGET scopes are the only importers (lld without -ltier0, 2026-10-18) |
| `sourcesharp/game/client/tier0_spew_client.cpp` | 27 | tier0's `DevWarning( level )`, which only client objects call (bone_setup, shell_clientdll, the temp-entity shells); beside the shared tier0_spew_port.cpp because the server does not need it (user ruling 2026-10-12) | those call sites asking managed logging, or their TUs leaving |
| `sourcesharp/game/client/tier1_client.cpp` | 174 | tier1's small members client.so still calls, so each object leaves its link: checksum_crc.cpp's CRC32 functions over ss_crc32.h (sentence.cpp, shell_input.cpp, SharedRandom's seed), rangecheckedvar.cpp's g_bDoRangeChecks (sequence_Transitioner.cpp) and mempool.cpp's CUtlMemoryPool (clientleafsystem.cpp, viewrender.cpp, the temp-entity pools, the view executor) | each name's last native caller leaving |
| `sourcesharp/game/client/particles_lib/particles_tier2_stubs.cpp` | 73 | what particles.a imports from tier2.a (CBeamSegDraw's Start/NextSeg/End and vtable, RenderWireframeBox/Sphere), defined as counted no-ops so tier2.a left client.so (user ruling 2026-10-12); moved into libss_particles.so with particles.a (plan_particles_so.md), its counts read back through ISSParticleLib::Counters. Native because particles.a calls them in-process | real tier2 ports in the library, or particles.a's rope and debug-wireframe draws through managed |


## Link libraries, re-measured at the close

`plan_shim.md` §8 asked for the `$Lib` rows re-measured against the closed
tree. **This is a dated measurement, not a ratchet** — the two rows that were
decided (`choreoobjects` removed, `dmxloader` kept) are ratcheted in
`sourcesharp/test/vpc_link_libraries_test.cpp`; the rest move every phase and
pinning them here would make this a document the commit that changes the tree
has to edit.

Measured 2026-09-20 in lane 7a-close by `.lane7a/libs.py`: enumerate the
objects the **ninja graph's link edge** names (never `_vpc_/ninja/debug/objs/`,
which keeps ghosts of evicted translation units), take their undefined symbols
with `nm -u`, and intersect with each `.a`'s `nm --defined-only` set, joining
on the **mangled** name.

| library | server | client |
|---|--:|--:|
| `tier1` | 125 | 191 |
| `mathlib` | 55 | 105 |
| `particles` | 11 | 41 |
| `tier3` | 8 | 16 |
| `tier2` | 3 | 30 |
| `vgui_controls` | — | 660 |
| `bitmap` | — | 4 |
| `vtf` | — | 2 |
| `matsys_controls` | — | 1 |
| `dmxloader` | 0 | 0 |

(108 objects / 1,245 undefined symbols on the server link line; 253 / 2,566 on
the client.)

**Direct demand of zero is a candidate, not a verdict.** `dmxloader` is the
recorded counter-example: zero direct importers on both sides, and dropping the
row fails the link, because the members `particles.a` pulls in name
`CDmxElement::` and `CDmxAttribute::`. The only proof that a row can go is
dropping it and linking.

Against §8's predictions, re-measured rather than carried forward:

| §8 said | measured at the close |
|---|---|
| `particles.a` leaves the server once the manifest read is managed | **not yet** — 11 server objects still import from it directly |
| `tier2.a` leaves the server once `shell_filesystem` and `precache_model` read the engine's `filesystem` | **3 left**, the closest row to going |
| `tier3.a` on the server costs `mdlcache` plus four `studiohdr_t` helpers and a jiggle-bone stub | **8 imports**, consistent with that account |
| `mathlib.a` stays with the leaf math | **55 / 105**, unchanged from the figure §8 carried — the instrument agreeing with a number taken independently months earlier is the control for everything else in this table |
| the client keeps `particles`, `tier2`, `tier3` until its stock rows are planned | 41 / 30 / 16, and 145 stock `$File` rows remain (ratcheted) |

`appframework`, `choreoobjects`, `gcsdk_sdk`, `raytrace`, `shaderlib` and
`vpklib` have zero direct demand on both sides **and no `$Lib` row on either
script** — they are in `lib/public/linux64/` because the tree builds them, not
because this mod links them.
