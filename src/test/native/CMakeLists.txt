# The native tests: every gtest binary and must-not-compile check `make test`'s
# native leg runs. THIS FILE IS THE LEG'S MANIFEST: tools/leg-floor counts the
# ss_cpptest*() and ss_must_not_compile() calls below (comments stripped) to
# declare how many binaries and checks the leg must report, and tools/build/
# buildcpptests configures, builds and runs them through ctest.
#
#   tools/build/buildcpptests               (the farm's src/buildcpptests; make cpp-test)
#   tools/build/buildcpptests NAME...        build and run only those
#   ctest --preset debug -R '^NAME$'         the same, once built
#
# The five helpers are in cmake/SourceSharpTests.cmake. Every path below is
# relative to the farm's src/ (farm/<config>/src), the tree the tests were
# written against and the working directory they run in. Binaries go to
# bin/<config>/cpptest.
#
# Converted from tools/build/buildcpptests in Phase 6b: one call per helper
# invocation, in the script's order, with the script's comments kept above the
# call they explained (they still say "build_and_run" where the call is now
# ss_cpptest). Append new tests at the end.
#
# Tests build at -O0. OPTIMIZED builds one at -O1, as the game is built: for a
# test whose subject is what the compiler does to native code (vtable slots,
# pointers to member functions), not for one that checks logic.

include(${PROJECT_SOURCE_DIR}/cmake/SourceSharpTests.cmake)

# file-open recorder, which had no reader; MemberOf is a header-inline template,
# so the header alone is the whole subject.
ss_cpptest(vtable_hook_test OPTIMIZED
    SOURCES sourcesharp/test/vtable_hook_test.cpp)

# plan_client_novalve C7: the render call recorder overwrites one engine vtable
# slot in place for a frame (vtable_slot_patch.h, header-only).
ss_cpptest(vtable_slot_patch_test OPTIMIZED
    SOURCES sourcesharp/test/vtable_slot_patch_test.cpp)

# C7 item 7: the draw batch asks a renderable for its handle only when it is an
# entity (standalone_renderable_handle.h, header-only).
ss_cpptest(standalone_renderable_handle_test
    SOURCES sourcesharp/test/standalone_renderable_handle_test.cpp)

# C7 step 2: view_scene.cpp's FrustumTransform, kept for ScreenTransform
# (view_frustum_transform.h, header-only).
ss_cpptest(view_frustum_transform_test
    SOURCES sourcesharp/test/view_frustum_transform_test.cpp)

# C7 step 2: the view TUs that left client.so (read from the farm).
ss_cpptest(client_c7_evicted_test
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/client_c7_evicted_test.cpp)

# plan_engineapi_vtables D3: native takes a managed-built object only at the
# compiler's slot count for the class it calls (managed_accept.h).
ss_cpptest_engine_headers(managed_accept_test
    INCLUDES sourcesharp/game/shared sourcesharp/host
    SOURCES sourcesharp/test/managed_accept_test.cpp)

# The data structs managed reads and writes in place, held to their managed
# views' offsets, and the fact that no acquire hands managed a vtable count.
# Engine headers, because the SDK's own declarations decide the offsets; it
# links nothing.
ss_cpptest_engine_headers(vtable_index_agreement_test OPTIMIZED
    INCLUDES sourcesharp/host sourcesharp/game/shared
    SOURCES sourcesharp/test/vtable_index_agreement_test.cpp)

# plan_engine_forwarders.md: each slot of the forwarder tables reaches the
# method it names, with its arguments, against a fake engine object whose
# recorders sit at the compiler's own slots. The server's library
# (fwd_server.cpp) is compiled in rather than linked, so the binary runs on
# Windows with no DLL search path to set up; SS_FWD_SERVER_BUILD makes its
# export a definition here, and SS_FWD_SERVES_SERVER is what server.so's link
# of it defines, for the internal factory's serve helper. OPTIMIZED: its
# subject is what the compiler does with the dispatch.
ss_cpptest_engine_headers(fwd_engine_test OPTIMIZED
    DEFINES SS_FWD_SERVER_BUILD SS_FWD_SERVES_SERVER
    INCLUDES sourcesharp/game/shared sourcesharp/host ${PROJECT_SOURCE_DIR}/src/fwd/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/fwd_engine_test.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_server.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_physics.cpp)

# plan_engine_forwarders.md section 6: the client library's Steam callback,
# built in memory the test hands it and driven through CCallbackBase's
# virtuals as Steam drives it, reaches the one managed-built method with the
# payload, answers its size and reads what Steam writes into it. The client's
# library (fwd_client.cpp, fwd_steam.cpp) is compiled in, as fwd_engine_test
# compiles the server's, with what client.so's link of it defines.
# fwd_client.cpp calls IVEngineClient's virtuals, whose numbering depends on
# USE_SDL: the test compiles it with the define the library has (the
# platform's, SS_COMMON_DEFS), or fwd_client.cpp's guard refuses it.
set(_ss_fwd_client_sdl)
if(NOT SS_PLATFORM STREQUAL windows)
  set(_ss_fwd_client_sdl USE_SDL)
endif()
ss_cpptest_engine_headers(fwd_steam_test
    DEFINES SS_FWD_CLIENT_BUILD SS_FWD_SERVES_CLIENT ${_ss_fwd_client_sdl}
    INCLUDES sourcesharp/game/shared sourcesharp/host ${PROJECT_SOURCE_DIR}/src/fwd/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/fwd_steam_test.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_client.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_steam.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_physics.cpp)

# plan_forwarders_everywhere.md phase 1: the shim layer's own interfaces
# through the same forwarder tables, each slot holding what reached a
# recording IShimFrame. The server's library is compiled in, as above.
# plan_forwarders_everywhere.md phase 5: the vphysics callbacks libss_fwd
# builds in a managed block's reserved words, driven through their Valve
# interfaces as vphysics drives them. The server's library is compiled in.
ss_cpptest_engine_headers(fwd_physics_test
    DEFINES SS_FWD_SERVER_BUILD SS_FWD_SERVES_SERVER
    INCLUDES sourcesharp/game/shared sourcesharp/host ${PROJECT_SOURCE_DIR}/src/fwd/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/fwd_physics_test.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_server.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_physics.cpp)

ss_cpptest_engine_headers(fwd_shim_test
    DEFINES SS_FWD_SERVER_BUILD SS_FWD_SERVES_SERVER
    INCLUDES sourcesharp/game/shared sourcesharp/host ${PROJECT_SOURCE_DIR}/src/fwd/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/fwd_shim_test.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_server.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_physics.cpp)

# The CLOSED SET of acquisition failure reasons, walked rather than sampled --
# plan_native_api.md Part 3b and Part 5c. The contract says four literals, all
# ASCII, none of them a format string, and that is a claim about every member:
# a test that checked one would pass on the day a fifth arrived carrying "%s".
#
# It compiles the REAL table (shell_acquire_reasons.cpp), which is why that
# switch lives in its own translation unit -- shell_vtable_interfaces.cpp
# includes cbase.h and nothing that does can be built into a standalone gtest.
#
# tier1 rather than a stub, and for the golden-table reason the helper's own
# comment gives: V_strncpy is NOT strncpy (it always terminates, and it takes an
# int), and the truncation contract is exactly what these arms drive.
ss_cpptest_tier1(acquire_reason_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 common ${PROJECT_SOURCE_DIR}/src/host/native
             sourcesharp/game/shared
    SOURCES sourcesharp/test/acquire_reason_test.cpp
            sourcesharp/game/shared/shell_acquire_reasons.cpp)

# What happened to one of the eight movement ConVars after this mod set it --
# plan_shim.md 4a item 16's instrument, and docs/defect-convar-order.md's last
# open line. It compiles the REAL decision (shell_movement_audit.cpp), which is
# why that decision lives in a translation unit of its own: shell_movement.cpp
# includes cbase.h and nothing that does can be built into a standalone gtest.
ss_cpptest(movement_audit_test
    INCLUDES sourcesharp/game/shared
    SOURCES sourcesharp/test/movement_audit_test.cpp
            sourcesharp/game/shared/shell_movement_audit.cpp)

# Classname matching for the standalone entity hierarchy. Header-only and
# deliberately free of engine headers, so it needs nothing but itself.
#
# It covered output-name resolution too until lane 3c-entityshell: StandaloneOutputIndex
# had 0 callers anywhere and StandaloneNameEquals lost its last one when
# cs_sv_setlocal went managed, so both functions and the eight facts over them
# went in that commit. What is left is the one rule with a caller, and it is the
# rule that has to AGREE with something -- NamesMatch in baseentity.cpp -- which
# is why it is tested rather than read.
ss_cpptest(standalone_names_test
    SOURCES sourcesharp/test/standalone_names_test.cpp)

# THE CLIENT CLASS-NAME ORDERING IS GONE WITH ITS ONLY CALLER -- plan_shim.md
# §4a item 9, lane 3f. `client_class_names.h` sorted the client's class list the
# way a CUtlDict does so that `cl_dumpentityfactories` could be diffed against
# the server's dump; that ConCommand left shell_diagnostics.cpp and nothing
# replaced the call. 0 live translation units on either link line included the
# header and 0 named SourceSharp_ClientClassNameCompare
# (tools/shim-header-census includers / references).
#
# The CLAIM this binary made is not lost, and it is worth reading if the dump
# ever comes back: sorting was never the point, DIFFABILITY was. A case-
# insensitive compare with a case-SENSITIVE tiebreak, because two classes
# differing only in case would otherwise compare equal and qsort may order equal
# elements differently between runs -- stable within a run and unstable across
# them is exactly the property a diff must not have.

# THE SEQUENCE-BY-ACTIVITY RULE MOVED TO C# -- plan_shim.md Phase 2d, and this
# binary went with sequence_by_activity.h, which had no includer left.
#
# The CLAIM it made is not lost and did not want to be: the index the rule
# answers is PREDICTED state, so it must be a pure, deterministic function of
# the model -- FIRST match, never stock's weighted random pick. It is
# ModelDescriptor.LookupSequenceByActivity now, one implementation both sides
# run, and ModelDescriptorTests carries the crowbar case this binary was built
# around: three sequences share ACT_VM_HITCENTER and the first one wins.
#
# That is the point of the move rather than a side effect of it. The header
# existed so a test binary could hold the rule still WITHOUT an engine, and two
# native bodies still had to be kept in step by review; one managed body is the
# same on both sides by construction.

# An ABI entry point in shell_scripted.cpp whose ONLY arm is the one
# SourceSharpEntity_t::Animating() answers is a SILENT NO-OP for every entity on
# the parallel hierarchy -- which is most of this mod's. animating_projection_test
# pins that Animating() returns NULL there; nothing pinned that a caller must
# therefore have a second arm, and SourceSharp_EntitySetSequence shipped without
# one, so ScriptedEntity.PlaySequence looked a sequence up and then set nothing.
# A SWEEP rather than a fact about that one function: four of the five
# .Animating() sites already fall through to the standalone studio header, and a
# fact naming only the fifth would never see the sixth.
ss_cpptest(animating_parallel_arm_test
    SOURCES sourcesharp/test/animating_parallel_arm_test.cpp)

# Lane f-srvshim's two REFUSALS, which are the kind a later tidy-up undoes.
# (1) SourceSharp_EntitiesInSphere keeps its StandaloneEntities() walk even
# though anyentity.h's CAnyEntitySphereQuery would replace it with one partition
# query -- because CStandaloneEntity::UpdatePartition destroys the partition
# handle of anything neither solid nor a trigger, so an enumerator cannot see a
# non-solid standalone and an env_fire could not find the next one to spread to.
# The REASON is pinned beside the consequence: pinning only "the loop is still
# there" goes green on the day the reason changes, which is the day the loop
# becomes safe to delete.
# (2) In SourceSharp_LevelClearEntityList the world release is OUTSIDE the
# SS_CBASEENTITY_ALIAS guard and gEntList.Clear() is inside it -- two lines
# apart, which is exactly the shape a mechanical "guard the whole body" edit
# gets wrong. world_release_test.cpp is what that cost: the SECOND `map` command
# faulted inside the engine, and one-map-per-gate hid it for weeks.
ss_cpptest(srvshim_rulings_test
    SOURCES sourcesharp/test/srvshim_rulings_test.cpp)

# The generic table's payload props (plan_nettable_generic.md Part 14 Step 1).
# A payload prop is a byte offset and nothing else, so an offset that is wrong
# networks the wrong four bytes without any error anywhere -- which is the whole
# reason the arithmetic lives in a header with no engine in it.
ss_cpptest(generic_payload_test
    SOURCES sourcesharp/test/generic_payload_test.cpp)

# THE ONE UNMET PART 15 BULLET (payload_recv_test.cpp): "Recv proxy symmetry:
# send-then-recv over a random blob is the identity."
#
# It was unmet structurally, not by oversight. generic_payload_test.cpp above is
# deliberately engine-header-free -- "Nothing here links against dt_send.cpp",
# in its own header -- and a proxy cannot be called without the translation unit
# that defines it. So this is a second binary, and the only one here that links
# the datatable sources.
#
# THE THREE DATATABLE TUs are an object library with the engine-header flags.
# buildcpptests compiled them with -w (gcc 10 reported a signed/unsigned
# compare and two unused file statics in them); clang 22 reports nothing with
# -Wall bar one, so they are compiled with every other warning live.
# The recv side is client.so's own since C8: engineabi/recv.h's builders and
# the Valve names over them, which replaced public/dt_recv.cpp.
add_library(ss_cpptest_dtobjs OBJECT
    ${SS_SRC}/sourcesharp/public/dt_send.cpp
    ${PROJECT_SOURCE_DIR}/src/client/native/engineabi_recv.cpp
    ${PROJECT_SOURCE_DIR}/src/client/native/engineabi_recv_names.cpp
    ${SS_SRC}/sourcesharp/public/dt_utlvector_common.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/dt_element_names.cpp)
ss_test_compile_flags(ss_cpptest_dtobjs)
ss_test_engine_flags(ss_cpptest_dtobjs)
# sourcesharp/game/shared for ss_containers.h, the set dt_utlvector_common.cpp
# records its debug-build names in.
target_include_directories(ss_cpptest_dtobjs PRIVATE ${SS_SRC} ${SS_SRC}/sourcesharp/game/shared
    ${PROJECT_SOURCE_DIR}/src/engineabi/native/include)
# The one: dt_utlvector_common.cpp's two file statics (g_STDict, g_RTDict),
# used only under _DEBUG.
# Built as the client's arm: dt_recv.cpp's RecvPropArray3 names AllocateStringHelper,
# client-only since the server gc cull; the server arm is a subset of it.
# The flag is gcc's and clang's; cl stops at it (D8021), and needs nothing in
# its place, since /W3 does not warn on an unused file static.
set_source_files_properties(${SS_SRC}/sourcesharp/public/dt_utlvector_common.cpp
    PROPERTIES COMPILE_OPTIONS "$<$<NOT:$<CXX_COMPILER_ID:MSVC>>:-Wno-unused-variable>"
               COMPILE_DEFINITIONS CLIENT_DLL)

ss_cpptest_mathlib(payload_recv_test
    SOURCES sourcesharp/test/payload_recv_test.cpp
    LIBS ss_cpptest_dtobjs)

# The network array element names, "000" to "2047": the wire contract the send
# and receive tables match on, each written the first time it is asked for.
ss_cpptest_mathlib(dt_element_names_test
    SOURCES sourcesharp/test/dt_element_names_test.cpp
    LIBS ss_cpptest_dtobjs)

# The temp entity description (plan_tmp.md Part 9, gate 2). Widths, blob offsets
# and the worst-case encoded size against the engine's 192-byte event cap, plus
# the registry's two refusals that cannot be seen from inside a running game -- a duplicate class
# name (the client begins joining and stops, with nothing in any log) and a late
# registration (the engine walks the class list once, and a class built after it
# simply is not there). Header-only and engine-free, same as the payload above.
# -I for the ABI header, which is where the description structs are declared --
# tempent_schema.h includes it the same way entity_slots.h does, so the shape
# crossing the boundary has one definition rather than two that agree at first.
ss_cpptest(tempent_schema_test
    INCLUDES sourcesharp/host
    SOURCES sourcesharp/test/tempent_schema_test.cpp)

# The temp entity PROP PLAN (plan_tmp.md Part 9, gate 2, the client half). The
# expansion rule -- the leading "baseclass" prop, one field becoming
# m_vecOrigin[0..2], SENDINFO_VECTORELEM's negated SEND offset beside the
# positive RECEIVE one, and the array's template-then-DPT_Array pair -- is walked
# by BOTH shell_tempents.cpp and shell_tempents_client.cpp, so it lives in
# tempent_schema.h where neither owns it. generic_nettable.h already records
# what a rule with two implementations does; the symptom here is a client that
# begins joining and stops with nothing in any log. Header-only and engine-free,
# same -I for the ABI header as its neighbour above.
ss_cpptest(tempent_plan_test
    INCLUDES sourcesharp/host
    SOURCES sourcesharp/test/tempent_plan_test.cpp)

# The temp entity BACKENDS' arguments (plan_tmp.md Part 4, C-Phase 2, gate 2).
# Fifteen client backends landed in one pass and every one is the same shape:
# unpack a flat argument list, call one drawing primitive. The call needs the
# client DLL and cannot be tested here; the UNPACKING is where the mistake
# lives, and it is where the arguments are five floats in a row
# (BeamCreatePoints), an order that deliberately differs from its siblings'
# (BeamCreateFollow: colour before brightness), or a pointer that may be null
# beside one that may not. Engine-free, same -I for the ABI header as its two
# neighbours above -- shell_te_backend_args.h includes nothing else.
ss_cpptest(te_backend_args_test
    INCLUDES sourcesharp/host
    SOURCES sourcesharp/test/te_backend_args_test.cpp)

# beam_endpoint_test and beam_reaper_test left with the C2 beam port: the
# endpoint's route and the reap's verdict are C#'s (BeamEndpoint, and
# BeamSystem.KillDeadBeams), and their facts moved to ClientBeamSystemTests and
# BeamDrawArmTests. The two wiring facts that read native files are in
# client_beam_evicted_test.

# NO HTML PANEL IS BUILT AT JOIN (no_html_panel_test.cpp). Stock's CTextWindow
# built a vgui::HTML, which connects the game to Steam's web helper; when the
# helper dies steamclient.so asserts and Steam's crashhandler writes
# /tmp/dumps/assert_*.dmp. The client compiles no stock viewport panel and links
# no vgui_controls, read off the build graph.
ss_cpptest(no_html_panel_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/no_html_panel_test.cpp)


# fx_muzzle_recording_test is RETIRED with fx.cpp (lane c5-particles). It held
# FX_MuzzleEffectAttached's tool-recording block to an IClientEntity resolve with
# its early return before every use; the block left with the file, and the C#
# attached flash (FxEffects.MuzzleEffectAttached) carries no recording tail, so
# there is no resolve left to come undone.

# The netentity shell is DELETED, and a deletion needs a guard as much as a
# refusal does. Three ways to undo it, none of which fails to compile: the class
# comes back (abstract under the armed alias, no diagnostic of its own), the
# retired SS_SHELL_NETENTITY value gets reused (a wire number changing meaning
# silently), or the four ABI slots reaped at version 240 come back. They were
# removed after row 2 of .claude/entity-port/DEFERRED_LIVE_GATES.md armed all
# four on both sides with a probe, wrapped the live FramePreThink slot in the
# same probe as a CONTROL, and ran two passes of tools/live-gate: the control
# printed in three game processes and none of the four ever fired.
#
# It also replaces netentity_table_pairing_test, which went with its subject:
# that diffed two hand-copied prop lists which no longer exist, and its own
# header said it could not prove the two tables PAIR in any case.
ss_cpptest(netentity_deleted_test
    SOURCES sourcesharp/test/netentity_deleted_test.cpp)

# THE CLIENT PROP SHELL ANSWERS COLLISIONS (lane client-impact-props). The
# impact clip asks the client shell four ICollideable questions and all four
# were hardcoded inert (SOLID_NONE / FSOLID_NOT_SOLID / return false x2), so a
# bullet passed through every managed prop and its custom effect never fired.
# The scan pins the client's GetSolid/GetSolidFlags to its server twin's words,
# TestCollision to stock's C_BaseAnimating gate, TestHitboxes to the BoneLibrary
# trace, and the ten-row props tier that carries solidity across. Scan-only:
# building a C_StandaloneEntity drives the engine allocator, which the
# engine-free harness cannot pay (Main's ruling for this lane).
ss_cpptest(client_prop_collision_test
    SOURCES sourcesharp/test/client_prop_collision_test.cpp)

# playerresource_pairing_test left with its subject (lane c6-input): it paired
# the field names shell_playerresource.cpp's native reader asked C# for, and
# that reader (g_PR) left client.so with nothing reading it.

# The entity control block (plan_nettable_generic.md Part 9, Step 5). C# holds a
# pointer into an entity's blob for the life of that entity and revalidates it
# against a serial in this block, so every way the bookkeeping can be wrong is a
# dangling pointer rather than an error anywhere: a slot closed twice, a slot
# left open past its entity, a serial that repeats. Header-only for that reason,
# and -I for the ABI header the slot layout is declared in -- the layout is
# shared with C#, so it is declared once and included rather than restated.
ss_cpptest(entity_slots_test
    INCLUDES sourcesharp/host
    SOURCES sourcesharp/test/entity_slots_test.cpp)

# When a bullet carries on through glass. Header-only, and here because the
# inline version of this rule was a remote-triggerable segfault: it guarded on
# one expression and dereferenced another, and shooting the skylight on
# dm_lockdown killed the server every time. The rule now takes a CLASSNAME, so
# "nothing could name what was hit" is a value with an answer rather than a
# pointer nobody checked.
ss_cpptest(bullet_glass_test
    SOURCES sourcesharp/test/bullet_glass_test.cpp)

# One spelling of "is this solid" in the shooting path's trigger sweep
# (shot_path_solidity_test.cpp). shell_ai_orphans.cpp's CTriggerTraceEnum asked
# CBaseEntity::IsSolid() for an SDK entity and ::IsSolid( collideable ) for
# everything else; it now asks the second for both, which took two Phase 13
# step-6 armed sites out of that file. The substitution is only equal because of
# a four-link chain of definitions in files this lane does not own -- IsSolid ->
# CollisionProp, CollisionProp -> &m_Collision, GetCollideable -> &m_Collision,
# and the property's own IsSolid/GetSolid/GetSolidFlags over the same two
# members. Break a link and the two answers differ SILENTLY: both still compile,
# both still return bool, and a shot that stops on a trigger it should have
# passed through reads as a gameplay bug. Nothing in the build checks a chain of
# definitions, so this does.
ss_cpptest(shot_path_solidity_test
    SOURCES sourcesharp/test/shot_path_solidity_test.cpp)

# bone_cache_key_test, procedural_bone_rule_test and anim_layer_rule_test LEFT
# with their headers (plan physics_bones_native_zero 1.2, client half): the
# rules are SourceSharp.Shared.Bones' BoneCacheKey, ProceduralBoneRule and
# AnimLayerRule, held by BonesCacheKeyTests, BonesProceduralRuleTests and
# BonesAnimLayerRuleTests, and no native code asks them any more.

# ANIMATION POLICY, and what native code may still do (plan_entity.md step 6d).
# A source-shape test with no engine in it, and both of its subjects failed
# SILENTLY in the running game.
#
# A duration read through a file-static array of zeroes: zero is the START of a
# pose parameter's declared range and not its middle, so every duration on a
# blended sequence was read at one extreme of the blend and every cycle computed
# from it ran at the wrong speed. Five copies of that, and the fifth was in
# nobody's list -- which is why this walks the whole shim for the identifier
# rather than checking the files already known to have carried it.
#
# And the hitbox set, hard-coded to 0 in two places: 0 is every set every model
# in this mod declares, so the wrong answer and the right one are the same
# number today and stop being it the day a model carries two.
#
# COMMENTS ARE STRIPPED FIRST. Every file under test is full of prose about the
# bug being asserted gone, and a needle that matched the prose would pass on a
# tree that still had it -- which has happened here before, to a cast census.
ss_cpptest(animation_policy_test
    SOURCES sourcesharp/test/animation_policy_test.cpp)

# THE ACTIVITY/EVENT NAME REGISTRY IS GONE -- plan_shim.md Phase 2d, and this
# binary went with anim_name_registry.h, which was its whole subject.
#
# The registry existed so that game/shared/animation.cpp could look an activity
# or event NAME up and write the resulting ORDINAL into mdl memory, for a
# native reader to compare against an enum. Nothing compares ordinals now:
# precache_model.cpp reads event names (anim_event_names.h, checked against
# SourceSharp.Abi/EventCatalog.cs in both directions) and the sequence lookups
# are one managed rule over a ModelDescriptor, which carries activity names.
#
# WHAT THE RULES IT PINNED BECAME. Each was about a failure that is SILENT in
# the running game, and each is now a failure that cannot happen rather than
# one that is watched for:
#
#   * "a shared registration is refused on a taken INDEX as well as a taken
#     name" -- there are no indices to take.
#   * "a private index is allocated above everything registered so far" --
#     there is nothing to register; an unrecognised event name simply is not
#     one of the five names native code acts on.
#   * "a name too long for a row is refused rather than truncated, because
#     truncation would silently merge two private activities that agree for 79
#     characters" -- names are not copied into fixed rows any more. The
#     comparison is against a string literal, and
#     AnimEventNames.EqualityIsNotAPrefixMatch pins that a longer name does not
#     match a shorter one, which is the same hazard at the one place it is
#     still reachable.

# Save/restore is out of both builds (plan_shared.md Phase 2). Four things could
# undo that and this asserts all four: the $File lines coming back, a guard
# quietly removed with the code it guards, a refusal in shell_gamedll.cpp
# softening, and -- the one that almost happened -- somebody working the plan's
# own cut list and evicting saverestore_gamedll.cpp, which is not save machinery
# at all but ParseKeyvalue/ExtractKeyvalue, the datamap keyvalue reader every
# spawning entity goes through. Two of its cases assert a file is STILL BUILT,
# which is the unusual direction for an eviction test and is the point.
ss_cpptest(saverestore_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/saverestore_evicted_test.cpp)

# Valve's telemetry uploader is out of both builds (plan_shared.md Phase 4). The
# decision the plan is least sure of, so the test is shaped around the flip: the
# same four call sites are rewired whether the phase ends in a deletion or in a
# managed fan-in, and the server one is asserted BY NAME because that is where a
# managed GameStatsEvents would reattach. Also pins that the client system row
# that left was the LAST one -- a row removed from the middle renumbers a walk
# order client_systems_table_test.cpp pins elsewhere.
ss_cpptest(gamestats_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/gamestats_evicted_test.cpp)

# The client's team entity is out of the build (plan_entity.md Phase 5's exit
# clause). An EVICTION rather than an un-derive, and that is a measurement: no
# compiled server class is named CTeam, so no C_Team has ever been constructed
# and nothing C_BaseEntity did FOR one was ever done. The facts with teeth are
# the five sites that kept the answer they were already giving -- above all
# C_BaseEntity::InLocalTeam, which has returned true for every entity in the
# game for the life of this build because it compared two NULLs, and which
# nobody had written down.
ss_cpptest(c_team_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/c_team_evicted_test.cpp)

# Step 7h: cbase.h is a FORCED PRELUDE in the client project, not an include.
# client_csharp.vpc sets $Create/UsePCHThroughFile "cbase.h" and the generator
# puts `-include .../cbase.h` on every compile line the project does not exempt,
# so deleting the include from a .cpp changes nothing on its own. This test is
# what keeps the two halves of that step from drifting apart: a TU that dropped
# the include must be exempt in the vpc, and an exempt TU must not still ask for
# it. It also pins the SPELLING of the rows -- sixteen collectors in this
# directory read only the first path of a `\`-continued $File group, and 7h's
# 102 rows written that way took c_team_evicted_test's view of the script from
# 289 rows to 175 and failed it.
ss_cpptest(client_pch_prelude_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/client_pch_prelude_test.cpp)

# Step 8e-classes: the Phase 5 exit's "the class-list diff is empty" clause, and
# the InLocalTeam defect c-team wrote down. FOUR client classes had no server
# counterpart and they did NOT have one answer between them: CBoneFollower and
# CRagdollPropAttached were dead (their server halves are not in the build, one
# of them deleted on purpose by lane A5) and their classes are deleted;
# CGameRulesProxy and CRopeKeyframe lose only their REGISTRATION, the rope
# because it is a genuinely needed client-only class that draws every rope in
# the game (14 constructions over a live gate, all 14 from the client-only
# path). The diff is RECOMPUTED from both vpcs rather than listed, and the scan
# respects preprocessor guards -- a guard-blind reader invents a fifth orphan
# out of c_te_effect_dispatch.cpp's `#ifndef SOURCESHARP` arm.
ss_cpptest(orphan_clientclass_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/orphan_clientclass_test.cpp)

# Step p7-exit: the stock client TUs that STAY compiled ask the slot table.
#
# §6 names the client files that legitimately stay native when the port is
# finished -- leaf system, shadow manager, view render, client mode, input, HUD
# canvas, leaf renderers -- so for those "leave the build" was never the answer
# and "stop naming cl_entitylist" always was. Step 5a did it for the shim (34
# sites, shell_cliententitylist.h's banner); this leg is the same move for ten
# stock TUs, and it is a WIDENING rather than a rename because the resolvers
# answer for managed entities that were never in a CClientEntityList.
#
# The waiver set is RECOMPUTED from client_csharp.vpc's own $File rows with
# comments and dead preprocessor arms stripped, so a stock TU reaching for the
# list again reddens this without anybody editing a list. The fact that stops
# the cheap wrong move -- deleting stock's call instead of routing around it --
# is TheStockArmSurvivesEveryMigration, and TheGuardPassIsWhatSeparatesTheTwoArms
# is its control.
ss_cpptest(client_entity_resolve_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/client_entity_resolve_test.cpp)

# The engine's OWN save headers, encoded. The codec was save_field_codec.h and is
# the second half of game/shared/shell_savegame.h since plan_shim.md §5 3e. The
# engine hands the game its save header and its light styles as
# typedescription_t arrays and expects them back on load, so a game DLL that
# saves needs a datamap field codec whatever it does with its entities --
# CSave::WriteFields did it, and that left both builds in Phase 2.
#
# It is THREE field types and that is measured rather than chosen
# (docs/managed-saverestore.md 4a). The facts with teeth are the refusals: a
# codec that SKIPPED an unrecognised field would write a block one field short
# and every later field would be read from the wrong offset -- a map name out of
# the middle of a light style, restored without complaint. Swept across the
# whole enum, because the case that matters is a type nobody has thought of.
ss_cpptest(save_field_codec_test
    SOURCES sourcesharp/test/save_field_codec_test.cpp)

# The FRAME around each of those blocks (sourcesharp/game/server/shell_savegame.h).
# The codec answers "how wide is one field"; this answers the question
# above it -- how one SaveWriteFields call is laid out so the matching
# SaveReadFields finds it, and what happens when the two do not line up.
#
# The engine drives the READ: it decides how many SaveReadFields calls to make,
# under which names and with which datamaps, from counts it recovered out of a
# FILE. So every disagreement between the read it asks for and the write that
# happened is a chance to fill the engine's own structs out of the middle of the
# previous block. Twelve bytes per block make all four kinds loud, and the facts
# here are one per kind plus a truncation sweep -- every prefix of a valid block
# refused, with the destination asserted byte-for-byte unchanged, because a
# half-filled struct is worse than a refused one.
ss_cpptest(shell_savegame_test
    SOURCES sourcesharp/test/shell_savegame_test.cpp)

# The safe crossing from a handle-table entry to a typed weapon pointer
# (plan_baseentity.md Phase 10, using Phase 13 step 1's mechanism early). The
# hazard is a CHandle<CBaseCombatWeapon> slot holding a parallel-hierarchy
# entity: the old (T*) cast answers with a pointer whose vtable belongs to a
# different class, and nothing anywhere reports it. The rule is two virtual
# calls in a header with no engine in it, tested against mock hierarchies where
# the mis-cast is a visible number.
#
# The same file now carries Phase 13 step 1 proper: the crossing to IGameEntity,
# which is the general case and the harder one. That interface is not in the
# IHandleEntity chain at all, so an entity's boundary view is at a DIFFERENT
# ADDRESS -- on CStandaloneEntity it is the fourth base -- and the mock is
# shaped to match, because on a CBaseEntity the two addresses coincide and the
# comparison proves nothing.
ss_cpptest(handle_cross_test
    SOURCES sourcesharp/test/handle_cross_test.cpp)

# gameentity_handle_test WAS HERE, and it went with its subject in
# plan_entity.md step 1d. It covered SourceSharp_ScanForGameEntity -- the
# reverse walk of the entity list that answered "an IGameEntity * in hand,
# which handle names it?" -- and 3.3 leaves the shim no list to walk. The
# refusal that replaces the scan is covered by entity_reverse_route_test.cpp
# below, which reads the source rather than mocking a walk that no longer
# exists. sourcesharp/game/shared/gameentity_handle.cpp carries the whole
# argument for the deletion.

# Which of the two hierarchies a projectile's impact is allowed to NAME.
# The shell narrowed it through TraceEntity(), which answers NULL for anything
# outside the SDK's tree BY DESIGN -- util_shared.h says that NULL "reads as hit
# the world" -- and then handed the answer to C# as a HANDLE, where a handle
# never cared which hierarchy its owner lived on. So a managed rocket striking a
# managed crate crossed as the same invalid handle as a rocket striking nothing,
# silently.
#
# Two halves, because the fault has two. The disagreement is exercised on mocks
# of both hierarchies against handle_cross.h's REAL partition function, not a
# copy of it. The ban is a source scan in sdk_player_cast_test.cpp's idiom,
# because what has to stay true is a shape at three call sites and no value a
# gtest can compute would notice somebody spelling the narrowing back.
ss_cpptest(impact_subject_test
    SOURCES sourcesharp/test/impact_subject_test.cpp)

# A BULLET LEAVES A MARK, and both ends of saying so. impact_subject_test above
# is about what an impact may NAME; this is about whether one happens at all --
# the server dispatching it, something in this build claiming the dispatch name
# (behaviour ledger row C7), and the callback resolving its entity by the one
# spelling this client can answer. The live half is tools/fx-impact-gate.
ss_cpptest(impact_dispatch_test
    SOURCES sourcesharp/test/impact_dispatch_test.cpp)

# bone_merge_key_test LEFT with bone_merge_key.h (plan physics_bones_native_zero
# 1.2, client half): the rule is BoneMergeKey (BonesMergeKeyTests) and the
# store-only-when-readable rule ClientBoneDriver's (BonesClientMergeTests).

# What each [ScriptedEntity] kind installs with. Header-only, and here because
# the switch this replaced ended in a NULL dereference that only unreachability
# was protecting -- every case returned, and the InstallFactory( pClass, ... )
# after them read a pointer nothing ever assigned. gcc does not warn about
# either half of that. A total function returning a value cannot have a tail, and
# these tests assert the totality the switch only happened to have.
ss_cpptest(scripted_kinds_test
    SOURCES sourcesharp/test/scripted_kinds_test.cpp)

# The client replay bracket's NULL player -- plan_entity.md step 6f. The client's
# prediction is managed and CGameMovement::player is NULL for the whole of a
# client command, so every `player->` a serviced trace can reach is a segfault on
# this side. Four sites, guarded; this reads the guards, because what can go
# wrong is one being deleted and the symptom is a crash on the first user command
# of every session. The first live run of 6f died on exactly that.
ss_cpptest(client_replay_bracket_test
    SOURCES sourcesharp/test/client_replay_bracket_test.cpp)

# The shim's IGameMovement, and the fallback that was DELETED rather than moved
# -- plan_entity.md step 8f-igamemovement, an owner ruling. Two halves:
# CShellGameMovement publishes the interface the engine resolves by name, so it
# no longer needs the 6,040-line stock class to exist; and a managed side that
# has not started stops being a silent reversion to stock movement and becomes an
# Error() that does not return. Both are guarded here by reading the code,
# because both failure modes are invisible to a game that plays correctly -- one
# is a build running the WRONG movement while reporting nothing, and the other
# only shows up when somebody removes two vpc rows. The message text is called
# for real; it is engine-free for exactly that reason.
ss_cpptest(shell_gamemovement_test
    INCLUDES sourcesharp/game/shared
    SOURCES sourcesharp/test/shell_gamemovement_test.cpp)

# gamemovement.cpp leaving the build -- plan_entity.md step 8f-remove, the last
# part of the same owner ruling. The change is two `$File` rows, and every way it
# can be wrong is invisible to a game that plays correctly: a ONE-SIDED removal
# leaves one side linking a whole second CGameMovement and the two sides then
# move differently under prediction; a `$File` row that stops resolving breaks
# the build in somebody else's worktree; the deleted loan machinery left behind
# reads as a live fallback while being NULL forever; and a retired gate arm left
# armed is a check that fails forever or one everybody ignores.
#
# It also carries the two facts written from this step's own worst find:
# tools/8f-probe-movetypes had been pointed at a file whose anchors had moved
# into the shim, so it patched nothing and the only live evidence for the three
# move-type arms had silently stopped existing. Nothing in the tree knew.
ss_cpptest(gamemovement_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/gamemovement_evicted_test.cpp)

# The ground entity, as the one word the player's blob carries for it. The
# handle is predicted state -- FL_ONGROUND only MIRRORS it, and the movement
# loop reads the entity, not the bit -- so the encode is on the misprediction
# path: a serial masked to the wrong width makes the two sides write different
# words for the same entity, and the meter reports a divergence that is really
# an encoding. Header-only and engine-free for that reason.
ss_cpptest(ground_handle_wire_test
    SOURCES sourcesharp/test/ground_handle_wire_test.cpp)

# WHAT THE MOVEMENT CROSSING WRITES BACK for that same word -- plan_entity.md
# step 2g. The managed loop carries "is there ground" as a bool and native
# carries WHICH entity; joining them was written as a pair of EDGE tests, so it
# never wrote when the ground changed from one entity to ANOTHER with no frame
# of air between. Nothing could reach that until 2g gave managed movers their
# riders, because falling onto a floor clears the ground on the way and a
# carried rider does not fall. Measured live: a player carried 96 units up a
# shaft reported the crate they left at the bottom, for the whole ride.
# Header-only and engine-free, so the rule is checked here rather than by
# riding a lift.
ss_cpptest(movement_ground_writeback_test
    SOURCES sourcesharp/test/movement_ground_writeback_test.cpp)

# Rider or blocker WAS HERE (standalone_riders_test.cpp) and is deleted --
# plan_entity.md step 2g. The rule it pinned was standalone_riders.h's
# StandaloneRider_IsRider, which the native BlockerAt consulted. Both the rule
# and its caller are managed now (SourceSharp.Abi/MoverMath.cs's IsRider and
# Blocks; SourceSharp.Server/EntityMotion.cs's BlockerAt), so the facts are xUnit
# facts in EntityMotionPushTests -- including the two the C++ could not reach,
# because the managed rule also has to make the overlap decision the engine's
# box walk used to make for it.

# The base-velocity push rule. The third arm of the same story: the crossing
# that hands a rider a belt's motion resolved a CBaseEntity and returned in
# silence for the standalone player, so Riders.Ride moved nobody and said
# nothing. Fixing it means two arms, and two arms drift -- an overwrite where
# the other sums, a missing ground release. The shared rule is engine-free in a
# header so both arms provably use the same one.
ss_cpptest(standalone_basevelocity_test
    SOURCES sourcesharp/test/standalone_basevelocity_test.cpp)

# Which movetype a standalone entity reports across the ABI. The other half of
# the same lift: the rider rule above kept a passenger from BLOCKING the brush,
# and this one is what lets the brush find the passenger at all. A standalone
# player has no physics object, so the old single write site never fired and
# every player crossed as MOVETYPE_NONE -- discarded by Riders.Carry's filter
# one line before the geometry test. Silent in every direction that matters,
# and invisible on a DESCENDING lift, which is carried by gravity instead.
# Header-only; -I for the ABI header the movetype numbers are declared in.
ss_cpptest(standalone_movetype_test
    INCLUDES sourcesharp/host
    SOURCES sourcesharp/test/standalone_movetype_test.cpp)

# Whether a standalone entity tells vphysics that what it last answered about
# the entity's collision rules may now be wrong. ivp asks
# IPhysicsCollisionSolver::ShouldCollide ONCE per pair and caches it until
# RecheckCollisionFilter; stock's CBaseEntity::CollisionRulesChanged exists for
# exactly that and says so, and the parallel hierarchy's setters had no
# equivalent. Silent in every direction: the rule table reads correct and one
# prop falls through the floor for the rest of its life. Header-only, no
# includes -- the stub is the whole of the object interface this rule uses.
ss_cpptest(standalone_collision_rules_test
    SOURCES sourcesharp/test/standalone_collision_rules_test.cpp)

# Physics stock parity H's penetrate-event ledger and G's FindMaxContact were
# penetration_ledger.h / find_max_contact.h with a gtest each; both are managed
# deciders since plan_physics_bones_native_zero 2.1 (PhysicsPenetrationLedger,
# PhysicsMaxContact), and their facts carry every case the two gtests had.

# Physics stock parity B5's object_stress.h / object_stress_test are gone:
# stock's CalculateObjectStress is managed's PhysicsObjectStress, called by
# PlayerShadowStress since plan_physics_bones_native_zero 2.3, and
# PhysicsObjectStressTests carries all fourteen of the gtest's cases.

# plan_map_load P1: the spawn's targeted material reload, engine-free.
ss_cpptest(map_material_reload_test
    SOURCES sourcesharp/test/map_material_reload_test.cpp)

# AND THE WALK WAS NEVER INSTANTIATED -- plan_shim.md §5 3f, lane 3f.
# `collision_rules_recheck.h` is gone. Two live TUs INCLUDED it and neither ever
# called it: physics_shared.cpp writes the loop out by hand at
# PhysRecheckEntityCollisionRules because a shell holds ONE IPhysicsObject and
# has no list to enumerate, and collisionproperty.cpp only carried the include.
# 0 live TUs named PhysRecheckCollisionRulesOf on either side
# (tools/shim-header-census references).
#
# THE EDGE IT ARGUED FOR IS STILL REAL and is now stated where the code is: a
# ragdoll is many physics objects behind one entity, so rechecking only
# VPhysicsGetObject() would leave the other limbs holding an answer IVP will not
# revisit and one limb falls through the floor the rest of it is lying on. The
# shells do not have limbs; the site that grows one grows the walk back with it.

# THE SERVER RAGDOLL LRU'S UNANSWERABLE QUESTION IS MANAGED -- plan_shim.md §5
# 3f, lane 3f. `ragdoll_retire_rule.h` is gone: ragdoll_shared.cpp included it
# and never called it (0 live TUs named SourceSharpRagdollMayBeRetired on either
# side), because the server arm it was extracted from is an empty #else -- and
# the list it walks has no feeder the port can reach at all.
#
# The rule RUNS, in C#: SourceSharp.Server RagdollRetirementPolicy.Retire, over
# Corpses.cs's own list, which is the LRU that actually holds this mod's
# corpses. tools/c8-clientsystems-gate measures that one for the same reason.
#
# The DIRECTION is the part worth keeping and it is unchanged: stock asked the
# local player whether a ragdoll was behind him, there is no CBasePlayer in this
# build to ask, and "I do not know" falls to KEEP. Falling to REMOVE retires
# every ragdoll in the PVS on its first pass and looks exactly like the cap
# doing its job.

# The top of a move-parent chain. GetRootMoveParent() is an out-of-line member of
# the class the client flip removes, and three files that STAY called it -- the
# trace filter's sibling exclusion, collisionproperty.cpp and
# game/client/physics.cpp. Its BODY never needed the class (GetMoveParent is
# inline on both sides), so the walk came to a header.
#
# The two edges are what matter: an unparented entity must be ITS OWN root, or
# every unparented pair looks like siblings and the filter refuses everything;
# and a CYCLE must terminate, because stock's unbounded loop hangs inside a
# physics callback with no output at all. Header-only, engine-free.
ss_cpptest(move_root_test
    SOURCES sourcesharp/test/move_root_test.cpp)

# The per-entity SIDE TABLE, now keyed on a `const void *` instead of on a
# `const CBaseEntity *`. The system that owns these tables had to move out of
# physics_main_shared.cpp (which leaves in the client flip) into
# physics_shared.cpp (which stays), and stock's own comment on the key type
# licensed the retype -- "it could just as easily be and int or void *".
#
# The retype is invisible to a compiler in the direction that matters: every
# call site passed a pointer before and passes one now, so nothing fails to
# build if the table starts confusing keys, and the symptom is one entity
# reading another's touch links. tier1 rather than a stub because CUtlHash is
# as much the subject as the key type is.
# clang: -Wno-unused-variable for Valve's public/tier1/utlblockmemory.h:350
# (an unused local in a template gcc 10 never instantiated), on a plain -I here.
ss_cpptest_tier1(data_object_key_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    INCLUDES public public/tier0 public/tier1 common game/shared
    SOURCES sourcesharp/test/data_object_key_test.cpp)

# Whether setting a standalone entity's model may also set its collision size.
# The third piece of the same lift. A brush must adopt its model's bounds -- it
# is a slab somewhere in the map and has no other source of size -- and a player
# must not, because its movement hull is what every question about standing is
# answered against. Spawn installed the hull and the managed OnSpawn's first
# line replaced it with the citizen mesh's box, putting the reported feet six
# units below the real ones. Nothing errors when this is wrong; the player is
# merely measured wrong by every caller. Header-only, engine-free.
ss_cpptest(standalone_model_bounds_test
    SOURCES sourcesharp/test/standalone_model_bounds_test.cpp)

# The network-var chain's owner (plan_baseentity.md Phase 13 step 2). The holder
# in networkvar.h used to be a CBaseEntity * and a dereference; the typedef turns
# that name into IGameEntity, which has no NetworkStateChanged() and must not
# grow one, so the owner is captured at the assignment instead. What can go
# wrong is SILENCE -- a chain that forwards to nobody compiles, runs, and leaves
# the value on the server unsent -- so the rule lives in a header with no engine
# in it and is checked here.
ss_cpptest(chain_state_notify_test
    SOURCES sourcesharp/test/chain_state_notify_test.cpp)

# The edict's network serial (standalone_netserial.h). The engine reuses the
# previous PackedEntity for an edict slot while the serial it recorded still
# matches, and THE GAME is what writes that serial -- stock does it in
# CBaseEntity::SetRefEHandle, and the parallel hierarchy's override did not.
# Two console commands on dm_lockdown aborted the server on the engine's own
# mismatch guard; a same-tier reuse would have corrupted silently instead. The
# rule is one masked store in a header with no engine in it, and what is pinned
# here is the property the engine depends on: consecutive occupants of one
# edict publish different serials.
ss_cpptest(standalone_netserial_test
    SOURCES sourcesharp/test/standalone_netserial_test.cpp)

# WorldSpaceCenter over an ICollideable (plan_baseentity.md Phase 13
# precondition (e), client half). Call sites that held a C_BaseEntity * for
# WorldSpaceCenter() now hold the interface, and the value they get must be the
# value CCollisionProperty::WorldSpaceCenter computed — a center that drifts
# puts every retyped beam, decal and screen-space lookup a little off with no
# error anywhere. The arithmetic lives in a header with no engine in it and is
# pinned here arm by arm.
ss_cpptest(collideable_center_test
    SOURCES sourcesharp/test/collideable_center_test.cpp)

# Once per id (said_once_per_id.h). The client's unpublished-type message kept
# a single last-id latch, and two unpublished entities in the PVS alternate
# ids every frame -- each arrival overwrote the latch with the other id and
# both warned once per UPDATE, thousands of lines a session, burying real
# messages. The rule is a set in a header with no engine in it; what is pinned
# is exactly the alternation pattern that defeated the latch.
ss_cpptest(said_once_per_id_test
    SOURCES sourcesharp/test/said_once_per_id_test.cpp)

# A refused physics-bridge path (physics_bridge_refusal.h). The two physics.cpp
# files are §6 KEEPS that still call into the hierarchy that leaves; the calls
# with a native answer were rewritten to use it and the rest are refusals. §7
# says a refusal is "a reason, never a silent no-op", and what this pins is the
# pair of jobs the type does in one call: the print is ONCE and the COUNT keeps
# rising. An implementation that collapses them passes a first-two-calls test
# and loses the number a live run is asked for to prove the refusal is vacuous.
ss_cpptest(physics_bridge_refusal_test
    SOURCES sourcesharp/test/physics_bridge_refusal_test.cpp)

# Which slot a client-side recipient filter holds (local_recipient.h). Every add
# on a C_RecipientFilter went through C_BasePlayer::GetLocalPlayer(), which is
# NULL for a standalone viewer, so every client filter was EMPTY -- and both
# client temp-entity dispatchers suppress an effect outright when the filter is
# empty, so every predicted muzzle flash, tracer, impact and decal was thrown
# away before its effect function ran. The server's copy arrives a round trip
# later, which is why it looked like latency rather than a bug. The rule is
# three comparisons over slot numbers; header-only, no engine.
ss_cpptest(local_recipient_test
    SOURCES sourcesharp/test/local_recipient_test.cpp)

# The collision-group vocabulary (collision_group_names.h). C# names three of the
# engine's thirty groups and only the NUMBER crosses, so the translation is two
# switches -- one each way -- and a pair that disagrees writes a group as one
# thing and reads it back as another with nothing to say so. Both directions
# were needed the moment a projectile had to ask whether the trigger it just
# flew into was a dropped weapon, which is stock's own exemption in
# CMissile::MissileTouch. It is ONE table read two ways so the disagreement
# cannot be written down; what is pinned is the lookup rules, over the test's own
# table rather than the engine's -- header-only, no engine, so the binary exists.
ss_cpptest(collision_group_names_test
    SOURCES sourcesharp/test/collision_group_names_test.cpp)

# THE HEAD-GIB THROW WENT WITH ITS SUBJECT -- plan_shim.md §5 3f, lane 3f.
# `gib_head_throw.h` is gone. game/server/gib.cpp is not in this tree at all any
# more (gib_evicted_test.cpp is the record of why), so the rule it was extracted
# from has no caller: 0 live TUs on either link line included the header and 0
# named GibHeadThrow_Choose.
#
# THE HOLE IT FOUND IS NOT FIXED ANYWHERE, and that is deliberately said out
# loud rather than left implied by a deletion. Stock writes two branches and has
# three outcomes: the unwritten one is "somebody is watching, the 5% roll came
# up, and the subject did not resolve", where nothing is assigned and the head
# keeps its spawn velocity. It is unreachable in stock and was the only outcome
# this mod ever reached. If the head gib is ever ported to managed, that third
# case is the one to write first, and it falls to the random tumble.

# The C-cast shape itself, banned mechanically rather than by memory
# (sdk_player_cast_test.cpp). Fourteen sites cast an edict's entity to
# CBasePlayer* and thirteen NULL-checked it, which is safe only for as long as
# CStandaloneEntity::GetBaseEntity() returns NULL -- Phase 13 step 5 makes it
# return `this`, and on that day every one of those NULL checks starts PASSING
# and the call lands on the wrong vtable. The inventory of fourteen came from
# reading 501 files by hand and nothing in the build noticed a fifteenth. This
# test reads game/server and game/shared, drops what the preprocessor drops, and
# fails on any live instance. It checks its own reach first: a scanner that
# reads nothing passes every ban it is given.
ss_cpptest(sdk_player_cast_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/sdk_player_cast_test.cpp)

# The identity every GetAbsOrigin retype rests on (plan_baseentity.md Phase 13
# precondition (e)). Call sites that held a CBaseEntity * now ask
# GetCollideable()->GetCollisionOrigin() instead, and that is sound only because
# CCollisionProperty::GetCollisionOrigin returns m_pOuter->GetAbsOrigin()
# UNCONDITIONALLY -- one line, in a file nobody editing a call site would open.
# A condition added there makes every retyped site read a different vector for
# some entities and the same one for the rest. The companion claim is pinned
# too: GetCollisionAngles is NOT that identity, which is the standing reason
# GetAbsAngles must never be swept the same way.
ss_cpptest(collision_origin_identity_test
    SOURCES sourcesharp/test/collision_origin_identity_test.cpp)

# The precache statics really did leave the class (precache_model.h). Two static
# members of CBaseEntity that never touched an entity -- PrecacheModel and
# PrecacheModelComponents -- and the modelsounds.cache apparatus behind them are
# free functions in sourcesharp/game/server/precache_model.cpp now, with one-line forwarders
# left on the class for the SDK's 734 unqualified call sites.
#
# What the move bought was three files leaving the baseentity.o consumer set,
# and what bought THAT is a property no compiler checks: precache_model.cpp asks
# baseentity.o for nothing. Losing it is silent -- a green build, a probe count
# that quietly went backwards. So the scanner pins the two CBaseEntity:: names
# the file is allowed to reach and pins the forwarders as forwarders.
ss_cpptest(precache_model_move_test
    SOURCES sourcesharp/test/precache_model_move_test.cpp)

# One recipient's worth of RecipientFilter_RemoveIfNotCloseCaptioning, which was
# a static member of CBaseEntity that never touched an entity and is now the
# recipient filter's own (plan_baseentity.md Phase 13 precondition (e)). The
# rule has one case that is not what it looks like: atoi("") and atoi("0") are
# both zero and mean opposite things -- "not reported yet" keeps the recipient,
# "captions off" removes them. The function is also a step 5 gate: it removes
# nobody today, so step 5 is the first day the rule runs at all.
ss_cpptest(closecaption_recipient_test
    SOURCES sourcesharp/test/closecaption_recipient_test.cpp)

# The one name-matching rule (entity_name_match.h). It used to exist twice --
# stock's NamesMatch and entitylist.cpp's GameEntityNameMatches, the second
# carrying a comment saying it was a deliberate duplicate "kept identical on
# purpose", with nothing in the build to notice the day they drifted. It is also
# what FClassnameIs now asks instead of CBaseEntity::ClassMatches, which frees
# every TU that asks an entity what it is from a reference into baseentity.o --
# and that substitution is sound only if the const char* form answers exactly
# what the string_t form did, NULL_STRING included. The empty/NULL cases here
# are that identity, not edge-case pedantry.
ss_cpptest(entity_name_match_test
    SOURCES sourcesharp/test/entity_name_match_test.cpp)

# Collecting handles from a size-then-fill ABI slot (shellless_find.h) --
# plan_entity.md step 8e-entfire. `ent_fire` could not reach any entity with no
# edict, which is every point entity ent_create makes, because every native find
# walks CEntInfo and a shell-less entity has no C++ object to have an entry. The
# fix asks managed code; this is the collect that sits in front of the ask.
#
# Off the game because THE PATH IT EXISTS FOR CANNOT BE REACHED FROM A MAP: the
# grow needs more shell-less entities sharing one targetname than dm_lockdown
# has in total. An untested grow is a silent truncation -- the first 64 take the
# input and the rest do not -- which is the same shape of defect the step
# closes. Header-only, no engine and no tier1.
ss_cpptest(shellless_find_test
    SOURCES sourcesharp/test/shellless_find_test.cpp)

# The blast blocking rule (blast_blocking.h). Who is standing in the way of an
# explosion, and how much of it they keep. It was written over CBaseEntity
# pointers inside CGameRules::RadiusDamage and so was skipped entirely for every
# entity on the parallel hierarchy -- a managed crate shielded nothing while an
# SDK crate of the same mass shielded a quarter of its weight. Restating it over
# handles is what makes the two agree, and a handle is exactly what this binary
# can supply without an engine: two addresses are two entities, which is all the
# rule ever knew about them.
ss_cpptest(blast_blocking_test
    SOURCES sourcesharp/test/blast_blocking_test.cpp)

# The THIRD arm of the partition (world_subject.h). "Which hierarchy holds this
# entity" has two answers and the owner's instruction puts worldspawn on a third
# -- so every resolver built on those two (TraceEntity, AnyEntityOf,
# MovementGroundFromTrace) answers NULL for the world the day CWorld un-derives,
# and NULL is what all of them spell "nothing there". The rule is engine-free
# and takes both pointers, so the four cases run here; the ORDERING -- the world
# asked BEFORE the two hierarchy arms, which is what makes the arm live today
# rather than dead until T3 -- is scanned, because no engine-free binary can
# execute it.
ss_cpptest(world_subject_test
    SOURCES sourcesharp/test/world_subject_test.cpp)

# The trace filter's shape clause (standard_filter_shape.h). "Is this the sort
# of thing this trace stops on at all" -- world-like brush, or an entity only a
# mask that asked for entities gets. Stock states it over a CBaseEntity, so
# every entity on the parallel hierarchy skipped it and was waved through as
# though it were a static prop; the blast's own "is the world also in the way"
# retrace therefore stopped on the victim and zeroed its damage. One rule read
# by both arms, and the rule is what is pinned here -- the three facts it is
# made of are resolved from interfaces no engine-free binary has.
ss_cpptest(standard_filter_shape_test
    SOURCES sourcesharp/test/standard_filter_shape_test.cpp)

# Which of an entity's two velocity homes is the authority WAS HERE
# (velocity_home_test.cpp over shell_velocity_home.h) and is deleted with its
# subject (plan_physics_bones_perf C2, 2026-10-12): managed's
# EntityPhysicsMotion decides it for the ABI (EntityPhysicsMotionTests) and
# managed's PhysicsFrame holds it on the entity for the movement's ground read
# (PhysicsFrameTests), which StandaloneEntity_Velocity answers without asking.

# How fast a managed MOVER is going WAS HERE (mover_velocity_test.cpp) and is
# deleted -- plan_entity.md step 2g. shell_mover_velocity.h's arithmetic is
# MoverMath.Velocity now and its ownership question does not exist any more:
# StandaloneMover_OwnsVelocity asked whose velocity a stop may clear only while
# two owners wrote m_vecVelocity, and both owners are one managed class. What it
# pinned -- the committed step rather than the intended one, the zero a stopped
# mover must report -- is pinned by EntityMotionPushTests and, bit for bit, by
# the replay of the stock recording in MoverFixtureTests.

# Whether a model name is the MAP's own submodel or a file (shell_model_name.h).
# The two need opposite treatment -- "*23" came in with the BSP and is looked
# up, a path has to be precached -- and both hierarchies had their own copy of
# the comparison, the SDK arm's written second by reading the first. A copy that
# drifts fails silently in the worst way: both arms answer -1 for a name they
# cannot resolve, so a brush sent to precache and a model sent to the lookup
# report the identical "no model" and neither says which arm it took.
# Header-only, no engine.
ss_cpptest(model_name_test
    SOURCES sourcesharp/test/model_name_test.cpp)

# Who a view punch reaches, and how hard (view_effect_reach.h; the shake rules
# went with their last native caller, lane HC 2026-10-04 -- ScreenEffectsTests
# holds them). The three loops in util.cpp resolved each player slot
# as a CBasePlayer, got NULL, and `continue`d -- so the falloff, the on-ground
# gate and the transmit gate were never WRONG, they were never RUN. Every
# explosion in the game has been silently shaking nobody. Fixing the subject
# makes all of it live at once, which is the argument for pinning it before the
# switch is flipped rather than after. Header-only and engine-free: none of
# these rules ever needed an entity, only a distance and two flags.
ss_cpptest(view_effect_reach_test
    SOURCES sourcesharp/test/view_effect_reach_test.cpp)

# player_name_match_test went with player_name_match.h and
# shell_playerslots.cpp (plan_server_native_min P1): the slot-walk family is
# deleted; managed answers the lookups (PlayerNames / PlayerPing.Sample).

# The string helpers our native code calls in place of tier1's strtools.cpp
# (ss_string.h), one fact per behaviour. The oracle that runs them beside the
# pristine strtools.cpp is ss_string_oracle_test (src/test/oracle/native).
ss_cpptest(ss_string_test
    SOURCES sourcesharp/test/ss_string_test.cpp)

# ss_cpp20.h: C++20's std::erase / std::erase_if for this C++17 tree. The SAME
# facts built twice: as C++17 they test the shim's own code, as C++20 the names
# are std's, so a fact passing under one and failing under the other is the
# shim disagreeing with the standard. The second target is the one C++20 build
# in the tree, on purpose.
ss_cpptest(ss_cpp20_test
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES sourcesharp/test/ss_cpp20_test.cpp)
# -Wno-character-conversion for GOOGLETEST's header, not ours: under C++20
# gtest-printers.h:477 converts char8_t to char32_t, which clang flags.
ss_cpptest(ss_cpp20_std_test
    OPTIONS -Wno-character-conversion
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES sourcesharp/test/ss_cpp20_test.cpp)
set_target_properties(ss_cpp20_std_test PROPERTIES CXX_STANDARD 20)

# shadow_cast_policy_test IS GONE, and its subject with it (plan_entity.md step
# 6b). What kind of shadow a standalone entity casts is policy -- stock spells
# the same decision EF_NOSHADOW and a `disableshadows` keyvalue -- so the rule
# moved to ClientRenderPolicy.Shadow in C# and its sixteen gtests are sixteen
# xUnit facts in ClientRenderPolicyTests. The reason the rule EXISTS has not
# changed and is worth keeping written down: Phase 5's screenshot gate found
# managed props laying no contact shadow at all -- 0.60% of pixels against a
# 0.0000% floor, every one of them at the base of the model -- because of a
# client shadow handle nobody ever created, and once the handle exists the rule
# is what decides what it is FOR. It still has to answer what
# C_BaseEntity::ShadowCastType and C_BaseAnimating::ShadowCastType answer for
# the same model, or the two props differ again for a new reason that nothing
# but another photograph would report.

# The stack behind a failed assert (assert_stack.h). An assert names a leaf and
# not a bug: `imesh.h (1665) : IsFinite(x) && IsFinite(y) && IsFinite(z)` is
# CVertexBuilder::Position3f, and TWENTY-ONE translation units in this client
# compile a call to it -- so the report as shipped cannot tell a tracer from a
# particle from a shadow quad, which is the entire question. -assertstack prints
# the caller, and three pieces of arithmetic decide whether the line that
# answers it is any good: a key that must separate call SITES but not stack
# DEPTHS, a dedup where "already seen" and "table full" are different answers
# that both return false, and an offset that WRAPS into a plausible, wrong,
# addr2line-resolvable number if the address is below the module base.
# Header-only and engine-free.
ss_cpptest(assert_stack_test
    SOURCES sourcesharp/test/assert_stack_test.cpp)

# What a failed managed init says, and whether Init() runs at all
# (managed_init_failure.h). A user hit seven `Assertion Failed: 0` in
# usermessages.cpp followed by a segfault inside engine.so with no game-DLL
# frame, and the actual cause -- "the .NET runtime did not load" -- was one
# Warning four thousand lines earlier that a default launch does not print. Two
# things here decide whether that ever happens again: the message, which has to
# name the stage, the code and above all the REMEDY (two of the causes are
# gitignored directories absent from every fresh worktree), and the gate, which
# used to be a bool set on ENTRY to Init() -- so a failed attempt marked the
# system initialised and the client's second call silently did nothing. Needs
# the host's result codes, hence -Isourcesharp/host: a private copy of the enum
# would drift and print a confident, wrong cause.
ss_cpptest(managed_init_failure_test
    INCLUDES sourcesharp/host
    SOURCES sourcesharp/test/managed_init_failure_test.cpp)

# How long a puff of a jet lives (jet_lifetime.h). This one is not hypothetical:
# a map-placed env_smokestack with no JetLength key made
# m_InvLifetime = m_Speed / 0 = +Inf, C_SmokeStack::RenderParticles FLerped the
# particle's SIZE with it, and the size went to CVertexBuilder::Position3f --
# five thousand `IsFinite(x) && IsFinite(y) && IsFinite(z)` assertions in ninety
# seconds, from three entities spawned with default keys. env_steam's m_Lifetime
# is the same expression the other way up and asserted on IsFinite(b) in
# vector.h from the same cause.
#
# The guard that was there was `if (x != 0)` at ONE of the two divide sites, and
# Start() -- the other one -- runs first. The half that matters most for a test:
# a zero NUMERATOR passes `!= 0` and yields a finite ZERO, which both renderers
# then divide BY. So the contract is "finite and strictly POSITIVE", not "not a
# division by zero", and it is checked on the output as well as the inputs
# because 1e38/1e-38 overflows and 1e-38/1e38 underflows from finite arguments.
# Header-only and engine-free: two floats.
ss_cpptest(jet_lifetime_test
    SOURCES sourcesharp/test/jet_lifetime_test.cpp)

# Which control points of a managed particle system are kept at its owner
# (particle_follow_mask.h). The same family as jet_lifetime.h -- a value nobody
# supplied read as a legitimate one -- but the unsupplied value here is a
# POSITION, and the default for a control point nothing wrote is (0,0,0), the
# centre of the map.
#
# Found by reading the .pcf rather than by looking at a flame. burning_character
# has "Position on Model Random" naming control_point_number 1, and four of its
# five children name point 1 as well; that operator asks the particle query for
# points on the MODEL bound to that point, nothing can be bound because
# SetControlPointEntity takes a CBaseEntity, and the query's `if ( !bSucesss )`
# fallback answers the control point's POSITION for every particle. So an unfed
# point 1 is a flame at the map origin. The eight env_fire_* systems reference
# point 0 only -- checked in fire_01.pcf, not assumed -- which is why the
# default is bit 0 alone and no existing effect changes.
#
# The two things the tests pin: bit 0 is forced on, because a mask that cleared
# it would strand the effect where it was created (the same bug inverted, and
# harder to see); and the walk is bounded by the MASK's 32 bits rather than by
# MAX_PARTICLE_CONTROL_POINTS' 64, because `1 << 32` is undefined and in
# practice aliases point 32 onto point 0.
#
# Header-only and engine-free: arithmetic over an int.
ss_cpptest(particle_follow_mask_test
    SOURCES sourcesharp/test/particle_follow_mask_test.cpp)

# Whether a penetrating pair's OWNER may solve it (self_penetration_rule.h).
# CCollisionEvent::ShouldSolvePenetration decided with `if ( pGameData0 ==
# pGameData1 )` -- "two parts of one entity" -- and then reinterpret_cast that
# pointer to a C_BaseEntity and made a virtual call on it. Right for every
# object stock ever put in the world and wrong the moment something has NO
# owner, because TWO NULLS COMPARE EQUAL. A standalone ragdoll's objects carry
# no game data and cannot, and RagdollActivate flags every one of them as part
# of a ragdoll, so the first one to overlap itself was a SIGSEGV out of
# PhysicsSimulate. What is pinned is the case a test written from the fixed
# code would not think to write -- a pair belonging to nothing -- plus the
# exhaustive walk, because the claim is that exactly ONE of the eight inputs
# answers true and an `||` for an `&&` keeps every corner case green.
# Header-only and engine-free: three booleans in, one out.
ss_cpptest(self_penetration_rule_test
    SOURCES sourcesharp/test/self_penetration_rule_test.cpp)

# WHERE A THIRD-PERSON CAMERA SITS IS MANAGED, and its 18 gtests moved with it.
#
# thirdperson_camera.h held the orbit and its pitch clamp, the sign convention
# that back is negative forward, how much of a wanted displacement survives a
# hull sweep, and the 30-unit floor under which a pulled-in camera goes back to
# the eye. plan_entity.md §3.10 named it as policy to move and the policy-player
# lane moved it: SourceSharp.Abi/ThirdPersonCamera.cs, reached through the
# ThirdPersonCamera slot, with the 18 gtests ported one behaviour per [Fact] in
# SourceSharp.Tests/ThirdPersonCameraTests.cs.
#
# The reason those tests exist at all is unchanged and worth keeping here: a
# camera in front of the eye, a camera scaled by zero, and a camera that never
# moved all produce the SAME PICTURE -- a frame with no player in it -- so a
# screenshot cannot say which of the three happened.

# The view's own subject, and the de-entitying of the six TUs that used to ask a
# player entity for it (plan_entity.md step 7c). Two halves.
#
# The RULES half runs view_subject.h -- the underwater DSP's water level, the
# effects mask, and whether the engine's view entity should displace the eye.
# Each has an edge whose wrong answer is invisible in a running game: `>` for
# `>=` never switches the DSP on for anybody, `==` for `&` reads a two-bit mask
# wrong and a one-bit mask right, and a view entity of index 0 is the WORLD, so
# accepting it renders the frame from the map origin -- a black screen that
# reads as a renderer fault.
#
# The SOURCE-SCAN half is the step's gate written down: those six files stay in
# client_csharp.vpc and name no entity type, using the census's own regex, one
# assertion per file so a red says WHICH. It also holds the two checks that stop
# the step from being a compile gate -- that the fog and the 3D skybox, which no
# managed player carries, report their absence to the not-shipped ledger instead
# of returning a silent NULL, and that the three readers of "can the camera see
# the viewer's body" all ask ONE function.
ss_cpptest(view_subject_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/view_subject_test.cpp)

# The render path's own subject, and the de-entitying of the five TUs of step 7d
# (leaf system, shadow manager, detail objects, pixel visibility, glow outline).
# Two halves now; there were three.
#
# The SOURCE-SCAN half is the step's gate clause written down: zero
# ClientEntityList() and zero cl_entitylist in those five files, one assertion
# per file so a red says WHICH, comment-stripped because the new include block
# in clientshadowmgr.cpp spells the name in prose to explain why it went.
#
# The second half is what stops the scan being a compile gate. A grep goes to
# zero just as happily when the behaviour is deleted, so: the TWELVE resolves
# must still be there BY EXACT COUNT (a floor would pass for one resolve and
# eleven returns), the lost-renderable counter must have two readers and must
# still be gated on the handle being valid, the FOV gap must still be recorded
# where a restorer will find it, and PrintShadowReport must snapshot the resolve
# stats BEFORE its own resolves -- otherwise it reports its own traffic and a
# gate asserting "asks > 0" passes on a client that asked nothing.
#
# THE RULES HALF IS GONE, with renderable_subject.h, by lane 4c-render. It ran
# two rules without an engine: which arm answered a handle->renderable resolve,
# and the FOV distance adjust factor. The classification is now three counter
# increments at the resolve itself -- the header existed to make those three
# lines runnable and nothing else -- and the FOV factor is deleted with its one
# caller, which passed it (1.0f, 1.0f). Nine facts went with them. The edge the
# four FOV facts protected (a ratio the wrong way up fades props OUT as a scope
# zooms IN, which reads as an LOD fault in the models) is docs/absence-client.md
# row 6, and TheFovGapIsRecordedWhereARestorerWillFindIt holds it there.
ss_cpptest(renderable_subject_test
    SOURCES sourcesharp/test/renderable_subject_test.cpp)
# plan_entity.md wave 2b, lane c7-leaf: the leaf/shadow/detail/think cluster.
# Two halves.
#
# THE RELOCATION HALF IS NOW A DELETION HALF, by lane 4c-render: the relocated
# bodies were measured at the far end and all four were constants on this link
# line, so seams and stock call sites went together. The facts assert the
# successor property instead -- each caller still does the work that does not
# need an entity, and neither end names one. Its original argument, kept because
# it is why the successor is written the way it is:
#
# The RELOCATION half is the anti-deletion argument for moving the bone-merge
# render bounds out of clientleafsystem.cpp. A grep for C_BaseEntity in that
# file goes to zero just as happily when Tracker 37433's special case is DELETED
# as when it is ported, and the deletion renders normally -- a bone-merged prop
# keeps its stale own-origin AABB and is frustum-culled at the edge of the
# screen for a frame at a time, with no assert and no crash. So the scan is
# paired with facts that both callers still consult the seam, that they act on
# its answer, that the two arms still take DIFFERENT parents (folding them would
# be a behaviour change hidden in a relocation), and that the bloat arithmetic
# survived the move.
#
# The PCH half is the lane's scoping finding for the flip, pinned by its cause
# rather than by a number: `-include cbase.h` plus
# cbase.h -> baseentity_shared.h -> c_baseentity.h -> particle_property.h ->
# particles_new.h plus CNewParticleEffect's inline virtuals is what puts a
# 21-symbol edge to particles_new.o on all 70 PCH'd stock client TUs. That edge
# alone holds them inside the 85-TU cycle, so no behaviour lane forbidden to
# touch a .vpc can move the SCC. tools/c7-leaf-cycle takes the reading; these
# facts stop the finding rotting into a paragraph nothing checks.
ss_cpptest(leaf_cycle_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/leaf_cycle_test.cpp)

# Lane c7-proxies' source scans that outlived the material proxies (lane
# c5-proxies moved them to C#: MaterialProxyFactsTests, MaterialProxyOracleTests):
# the alias-guard scanner, and that recvproxy.cpp and dt_utlvector_recv.cpp stay out (C8).
ss_cpptest(materialproxy_subject_test
    SOURCES sourcesharp/test/materialproxy_subject_test.cpp)
# plan_entity.md step 7e's subjects, in the source. The client sound cluster
# moved under the shim and was de-entitied, and three of the things that changed
# are invisible to the compiler: CurrentView* vs MainView* (an assert that only
# fires when somebody forces a soundscape), the C_BasePlayer asks (a shim TU's
# text is not counted the way a stock TU's is), and Soundscape_Update -- the
# landing site a soundscape push has no caller for, kept on purpose so that
# wiring the transport is a caller rather than a rewrite. Plus the two vpc rows,
# asserted on BOTH sides, and the gate instrument's own accessor.
ss_cpptest(soundscape_particle_7e_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/soundscape_particle_7e_test.cpp)

# What the trail family lost when it stopped being entities (trail_lifetime.h).
# Two guarantees C_BaseEntity had been giving the six trail classes silently,
# both re-provided by hand and neither one loud when it breaks. The zeroing
# allocator: C_SporeTrail's constructor sets six of its seven fields and the
# seventh is m_bEmit, whose documented "nothing can make one emit" is true only
# because the block arrives zeroed, and C_ParticleTrail's sets none at all and
# then reads m_pParticleMgr in its destructor. And the destruction latch: two
# callers destroy a trail with no ordering between them -- Release() from the
# shell and NotifyRemove() from CParticleMgr's level-shutdown sweep -- plus the
# subclass destructor's own RemoveEffect, which comes back round as a third.
# The heap under the allocator test is filled with 0xCD so that a missing memset
# fails on the first assertion rather than reading zeroes from a fresh page.
ss_cpptest(trail_lifetime_test
    SOURCES sourcesharp/test/trail_lifetime_test.cpp)

# The registry the de-entified effects hang on (shell_effect_registry.h). The
# `_render` shells used to hold CHandles, which read as null of their own accord
# once the engine destroyed the entity behind them. The effects are not entities
# any more, so the ROW is removed by the EFFECT from its destructor and the shell
# must not remove it again -- an inversion whose every failure mode is silent: a
# Release-then-Remove eats a neighbour's row and leaks that effect, a forwards
# walk skips the row after each removal, and `while (Count()) list[0]->Release()`
# hangs the client on a level change the day a factory in a different file
# forgets SetGoneCallback. Header-only and engine-free; the container is a
# template parameter so the game passes a CUtlVector and this passes a
# std::vector, and the fake effect removes its own row exactly as the real one
# does.
ss_cpptest(shell_effect_registry_test
    SOURCES sourcesharp/test/shell_effect_registry_test.cpp)

# The leaf-system link a de-entified RENDERABLE keeps (shell_leaf_link.h). The
# other half of the same loss: an effect that draws through a particle binding
# needs nothing, but C_PointWorldText builds its own mesh and was in the client
# leaf system only because C_BaseEntity put it there, took it out, and reported
# every SetAbsOrigin through MarkRenderHandleDirty. Doing that by hand fails
# three silent ways -- a second add STRANDS the first row, because AddRenderable
# overwrites the handle it wrote through the renderable and nothing can reach the
# old one; a remove on something never added hands RemoveRenderable an INVALID
# handle that survives only because IsValidIndex happens to reject it; and a move
# that is not reported leaves the object drawing from the leaves it was FIRST
# added to, which looks right until the camera is somewhere that should see it.
# Header-only and engine-free; the leaf system and the renderable are both
# template parameters, and the fakes RECORD so a test can assert what did NOT
# happen.
ss_cpptest(shell_leaf_link_test
    SOURCES sourcesharp/test/shell_leaf_link_test.cpp)

# Where a player's tick base is put (tickbase_rule.h): stock's
# AdjustPlayerTimeBase, which the port had re-basing on idle frames.
ss_cpptest(tickbase_rule_test
    SOURCES sourcesharp/test/tickbase_rule_test.cpp)

# THE TWO "NPCs AND THE PLAYER" FILTERS ARE GONE, AND THE RULE WITH THEM --
# plan_shim.md §5 3f, lane 3f. `npc_filter_rule.h` had exactly one includer,
# trace_filter_library.cpp, and within it exactly two callers:
# CTraceFilterOnlyNPCsAndPlayer and CTraceFilterNoNPCsOrPlayer. Both classes had
# no caller anywhere on either link line and were deleted, so the header went in
# the same commit.
#
# THE GAME RULE IS THE PART WORTH KEEPING and it is recorded here rather than in
# git history alone, because it is not obvious and it is the sort of thing a
# reimplementation gets wrong: the pair are opposites and the second is NOT the
# negation of the first. "Everything BUT people" refuses a CLASS_PLAYER_ALLY
# that is not an NPC, carrying stock's "CS hostages are CLASS_PLAYER_ALLY but
# not IsNPC()". A trace filter cannot cross to managed code -- it is asked per
# candidate per trace from inside the partition walk -- so if these filters are
# ever needed again the rule goes back in a header in push_blocker_rule.h's
# shape, over booleans, with no entity type in its interface, rather than back
# as a bare integer comparison inside a ShouldHitEntity body where nothing can
# ask it a question.

# The mechanics Phase 13 step 4's alias needs, all of it inert until the switch
# is armed (alias_mechanics_test.cpp). An alias is a declaration of the NAME, so
# every `class CBaseEntity;` in the include graph -- 73 across 68 files -- and
# every `friend class CBaseEntity;` has to be behind #ifndef SS_CBASEENTITY_ALIAS
# before step 4 can land. Nothing about that is visible at runtime and nothing
# about it fails today; it fails all at once on the day somebody arms the switch,
# in files whose authors never heard of it. So this reads source, like
# sdk_player_cast_test.cpp, and pins the include ORDER as well -- that ordering
# is the reason the alias lives in public/ rather than in baseentity.h.
ss_cpptest(alias_mechanics_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/alias_mechanics_test.cpp)

# sourcesharp/game/server/entity_bases.h, compiled BOTH WAYS (entity_bases_alias_test.cpp).
# That header is in the server's precompiled prelude -- all 197 TUs -- so the
# three flip sites it carried were, armed, a hard error in 180 of 196 server
# objects, 86 of which had nothing else wrong (docs/typedef-dryrun/). It now
# carries an #ifndef SS_CBASEENTITY_ALIAS / #else pair at each, and a
# preprocessor branch nobody compiles is a branch nobody knows is broken. So the
# header is included twice, from two TUs in one binary: once with a stand-in for
# the SDK's CBaseEntity and once with SS_CBASEENTITY_ALIAS defined and
# CBaseEntity a real alias for the real public/igameentity.h. The armed TU is
# the load-bearing one -- it does not compile at all if the armed arms are
# wrong. mathlib, and only because igameentity.h includes mathlib/vector.h.
# TWO binaries, not one, and the reason is the -D below: SS_CBASEENTITY_ALIAS
# has to be a COMMAND-LINE define -- alias_mechanics_test.cpp's
# TheSwitchIsNotArmedYet refuses to let a `#define SS_CBASEENTITY_ALIAS` exist
# anywhere under game/, public/ or sourcesharp/, which is exactly the guard that
# keeps step 4 from landing by accident -- and a -D cannot be applied to one TU
# of a single g++ invocation. It is scoped to this one test binary; nothing the
# game links sees it, and `survey_arm_alias.py . check` still reports disarmed.
ss_cpptest_mathlib(entity_bases_alias_test
    SOURCES sourcesharp/test/entity_bases_alias_test.cpp)

ss_cpptest_mathlib(entity_bases_alias_armed_test
    DEFINES SS_CBASEENTITY_ALIAS
    SOURCES sourcesharp/test/entity_bases_alias_armed.cpp)

# Lane H's arms — the other 58 headers Phase 13 step 4 has to get through
# (docs/typedef-dryrun/flip_shards.txt, 346 sites). entity_bases.h could be
# compiled both ways because it is small and standalone; player.h, Sprite.h and
# the rest drag in the whole SDK prelude, and a stand-in for that would drift
# from the real thing silently. So this one reads source, like
# alias_mechanics_test.cpp and sdk_player_cast_test.cpp, and pins the textual
# invariants a survey cannot: that every guard still has two arms carrying code
# (the merge-splice failure), that each armed arm actually stops naming the
# member the flip deletes, and that the ten hand-folded ObjectCaps bitmasks are
# arithmetically right. The compile-side evidence is src/survey_run armed.
ss_cpptest(header_alias_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/header_alias_test.cpp)

# Lane T shard T6's arms — the .cpp half of the same flip, fifteen files and
# 214 armed sites (docs/typedef-dryrun/route/s5_shards.json). Reads source for
# the same reason header_alias_test does, plus one this shard found the hard
# way: survey_patch_ninja.py patches the RELEASE graph, so EVERY armed
# measurement in this port carries -DNDEBUG and cannot see the body of an
# Assert() or anything inside `#ifdef _DEBUG`. Six sites in this shard were in
# exactly that blind spot and were found by re-running the survey's own command
# line with the two defines swapped. Nothing else here does that, so the
# textual check is the only standing evidence those six stay fixed.
ss_cpptest(cpp_alias_t6_test
    SOURCES sourcesharp/test/cpp_alias_t6_test.cpp)

# Lane shards-b's twenty client files: nineteen release TUs and twenty debug
# ones off the step-6 gate. Half of its edits carry no guard at all -- a player
# ask rerouted to IMovementPlayer compiles and answers identically unarmed --
# so nothing but a source read would notice one going back.
ss_cpptest(cpp_alias_shards_b_test
    SOURCES sourcesharp/test/cpp_alias_shards_b_test.cpp)

# The survey's own stubs, both sides, read as source. Each TYPEDEF_SURVEY
# `#else` arm used to run to the last line of its file, so arming the survey
# elided the whole header -- taking the free funnels, the classname predicate,
# the entity macros and the CHandle specialisations with it, all of which
# SURVIVE the flip. That artefact was 39 client sites of 1,978 and 9 server
# sites of 2,691. The arm now closes AT THE CLASS, and this pins that boundary
# from both directions: nothing that is an out-of-line member of the entity
# class may survive arming (one that does fails every armed TU in the header
# and the survey then measures nothing), and nothing free may be elided by it.
ss_cpptest(survey_stub_elision_test
    SOURCES sourcesharp/test/survey_stub_elision_test.cpp)

# Lane 1's arms — the .cpp half of the same flip (flip_shards.txt, lanes 1..6).
# Same evidence shape and the same reason: these .cpp files drag in the whole
# SDK prelude, so compiling them twice is not available. Adds one rule the
# header lane did not need — a .cpp guard can wrap a whole function body and
# swallow its only `return`, which armed would fall off the end of a non-void
# function, so every whole-body arm is required to sit in a void function.
ss_cpptest(cpp_alias_lane1_test
    SOURCES sourcesharp/test/cpp_alias_lane1_test.cpp)

# Route step 5's .cpp half, lane T shard T4 — fourteen files, 197 armed sites,
# all now at zero. Lane 1's shape and its three rules, plus three that shard T4
# needed and lane 1 did not:
#
#   a guard inside a VALUE-RETURNING function. Lane 1 required whole-body guards
#   to sit in void functions; nine of T4's do not, so the rule that replaces it
#   is "if the unarmed arm returns a value, the armed side must provide one too",
#   checked over every guard in the shard rather than over a list;
#
#   a guard that closes on a bare `else`, so that armed the block after the
#   #endif runs unconditionally — which is how particle_parse.cpp keeps CLEARING
#   the effect's entity field and how FindEntityProcedural keeps its "player 1"
#   fallback for "!pvsplayer";
#
#   an UNCONDITIONAL retype with no guard at all. Three of T4's edits stop naming
#   the entity outright, and the old spelling compiles clean either side of the
#   flip, so nothing in the build notices one rotting back. Pinned in both
#   directions.
ss_cpptest(cpp_alias_shard_t4_test
    SOURCES sourcesharp/test/cpp_alias_shard_t4_test.cpp)

# Shard 2's .cpp edits — the ten server files after lane 1's ten, by armed site
# count (docs/typedef-dryrun/shard2_classification.md). Where lane 1 landed
# ARMS, this shard landed almost none, and that is the finding: 846 of its 1,018
# armed sites are EVICTION, so an armed arm would be a pure stub. Twenty-eight
# were not eviction, and each of those is an UNCONDITIONAL edit rather than a
# guard, because the answer that survives the flip is also the right answer
# today: twenty-five static-member calls lifted to the free functions that
# already carried their bodies, and three sites where EntityFromEntityHandle's
# NULL had stopped being unreachable and become a live dereference. Nothing in
# the build notices any of the three rotting back.
ss_cpptest(cpp_alias_shard2_test
    SOURCES sourcesharp/test/cpp_alias_shard2_test.cpp)

# Shard T3's .cpp arms — route step 5's lane T, twelve files, 176 armed sites.
# Three things in it have no compiler behind them. (1) Two of those files lose
# 93 sites to ONE arm each, and the arm is only right because CCollisionProperty
# and CParticleProperty are CBaseEntity's own sub-objects; what must not drift is
# what is OUTSIDE it — UpdateDirtySpatialPartitionEntities is called by name from
# particlemgr.cpp, so guarding it turns step 6's compile error into a LINK error.
# (2) Every guard must be spelled `#ifndef SS_CBASEENTITY_ALIAS` on its own line,
# because that is the only spelling survey_arm_alias.py's `defeat` rewrites — a
# compound condition guards identically and silently stops the negative control
# being one. (3) Two arms COPY an answer that lives in another file: the server
# IsWorldEntity's armed arm is its client twin's line, and fx_impact clips
# against the IHandleEntity the enumerator was handed.
ss_cpptest(cpp_alias_shard_t3_test
    SOURCES sourcesharp/test/cpp_alias_shard_t3_test.cpp)
# Route step 5's lane-T shard T5 -- fourteen .cpp files, 194 armed sites
# (docs/typedef-dryrun/route/s5_shards.json). Six UNCONDITIONAL retypes with no
# guard on them at all, which is why they need a scanner: the old spelling
# compiles, the suite stays green, and the site is a Phase 13 site again. Plus
# the arms, and one rule lane 1 could not take -- several of this shard's sites
# are in functions that return a value, so where a guard swallows a `return` the
# ARMED text of the function has to still contain one.
ss_cpptest(cpp_alias_t5_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/cpp_alias_t5_test.cpp)
# Wave 3's eight sole-blocked TUs -- step 6's close-out list names eight
# translation units "blocked by RETYPE alone", and seven of them go green here
# (survey_run, alias armed: 68 -> 64 server, 82 -> 79 client, same seven names
# release and debug). Three things in this shard have no compiler behind them on
# EITHER arm. (1) toucher.cpp initialises Toucher_t's pBase and pGame, which
# armed are THE SAME TYPE, so the right initialiser and the wrong one both
# compile and only one delivers touches -- pinned by position. (2) The server
# solver's factory must stay OUTSIDE the arm that removes its class: five TUs
# call it through physics_npc_solver.h, so inside the arm they all compile and
# the link fails, which the step-6 rehearsal is structurally blind to. (3)
# viewpostprocess.cpp's seven asks became one call whose arm lives in another
# file, an unconditional retype whose old spelling compiles perfectly today.
# Plus the refusal: damagemodifier.cpp must NOT grow an arm.
ss_cpptest(cpp_alias_retype_w1_test
    SOURCES sourcesharp/test/cpp_alias_retype_w1_test.cpp)
# Lane T shard T1's .cpp arms. Unlike shard 2 -- whose 846 eviction sites made an
# armed arm "a pure stub" -- this shard is retype and CALLER-root work, so most
# of it IS `#ifndef SS_CBASEENTITY_ALIAS` arms: 195 of its 208 measured sites,
# across 12 files. NOTHING IN THE ORDINARY BUILD COMPILES AN ARMED ARM, so the
# only standing evidence for them is textual, and the four rot shapes it is
# aimed at are named in the file: a merge splice eating an `#endif`, an armed arm
# "simplified" back to the member the flip deletes, the move-type INVERSION
# (reading MOVETYPE_NONE as "not simulated" flips the answer for the whole
# parallel hierarchy), and a NULL test rewritten into a world test.
ss_cpptest(cpp_alias_t1_test
    SOURCES sourcesharp/test/cpp_alias_t1_test.cpp)
ss_cpptest(damage_attacker_test
    SOURCES sourcesharp/test/damage_attacker_test.cpp)

# One datadesc slot, two different casts (entity_funcslot.h). DEFINE_FUNCTION_RAW
# used to be a single C-style cast doing both the class conversion and the
# signature pun, and a C-style cast does not say which cast it picked:
# static_cast while classNameTypedef derives from CBaseEntity, reinterpret_cast
# the moment it stops -- which is Phase 13 step 4, for 47 sites that look
# identical either side of it. The split is only worth anything if the class half
# is genuinely loud, and THAT cannot be asserted from inside a test, so the four
# ill-formed shapes are compiled separately and required to fail, each at the
# probe rather than somewhere else. Header-only and engine-free.
ss_cpptest(entity_funcslot_test OPTIMIZED
    SOURCES sourcesharp/test/entity_funcslot_test.cpp)

ss_must_not_compile(entity_funcslot_unrelated
    EXPECT "entity_funcslot\\.h:[0-9]+:[0-9]+: error:"
    DEFINES ENTITY_FUNCSLOT_NOCOMPILE=1
    SOURCES sourcesharp/test/entity_funcslot_test.cpp)
ss_must_not_compile(entity_funcslot_private
    EXPECT "entity_funcslot\\.h:[0-9]+:[0-9]+: error:"
    DEFINES ENTITY_FUNCSLOT_NOCOMPILE=2
    SOURCES sourcesharp/test/entity_funcslot_test.cpp)
ss_must_not_compile(entity_funcslot_ambiguous
    EXPECT "entity_funcslot\\.h:[0-9]+:[0-9]+: error:"
    DEFINES ENTITY_FUNCSLOT_NOCOMPILE=3
    SOURCES sourcesharp/test/entity_funcslot_test.cpp)
ss_must_not_compile(entity_funcslot_virtual
    EXPECT "entity_funcslot\\.h:[0-9]+:[0-9]+: error:"
    DEFINES ENTITY_FUNCSLOT_NOCOMPILE=4
    SOURCES sourcesharp/test/entity_funcslot_test.cpp)

# The shortcut a de-entified volume takes for its world bounding box
# (world_aligned_bounds.h). C_FuncSmokeVolume lost its collision property with
# its entity, so CollisionProp()->WorldSpaceAABB became two vector adds -- which
# is exactly what CCollisionProperty does on ONE of its two branches, and the
# branch is chosen by whether the owner is rotated. Two vector adds are either
# right or silently TOO SMALL, and too small is an effect culled out of leaves it
# occupies: smoke that vanishes as the camera moves, with nothing in any log.
# So the shortcut is checked against mathlib's own TransformAABB rather than
# against a restatement of itself, and the precondition is tested as well as the
# rule -- the rotated case measures how much too small it gets, so the header's
# "the day something rotates, this line has to grow a TransformAABB" is a
# measured claim. Links mathlib, for the same reason the next one does.
ss_cpptest_mathlib(world_aligned_bounds_test
    SOURCES sourcesharp/test/world_aligned_bounds_test.cpp)

# The area-portal list, driven for real (areaportal_list_test.cpp).
#
# plan_entity.md step 4g took the portal off the SDK hierarchy, and with it went
# the entity handle -- whose serial is what stops a recycled slot answering a
# stale handle. The token is an id that is never reused, and this drives it,
# INCLUDING the case it exists for: remove a portal, create another, and check
# the first entity's token does not open the second entity's hole. That is a
# map-cycle bug, and every live gate this project has loads ONE map.
#
# mathlib because a window's fade reads CalcDistanceToAABB. It compiles the
# production header and defines g_AreaPortals and g_AreaPortalsById itself, so
# the bodies under test are the ones the server links.
ss_cpptest_mathlib(areaportal_list_test
    SOURCES sourcesharp/test/areaportal_list_test.cpp)

# Where a templated entity ends up (template_transform_cases.txt). The other test
# here that links mathlib, and it has to: point_template's placement arithmetic
# moved to C#, and the golden table the managed test reads is only worth
# anything if something still pins it to the VMatrix it was taken from. A table
# checked only by the side that reads it is a fixture agreeing with itself --
# see docs/writing-tests.md and the header of the test file.
ss_cpptest_mathlib(template_transform_test
    SOURCES sourcesharp/test/template_transform_test.cpp)

# bone_library_test LEFT with bone_library.h (plan physics_bones_native_zero
# 1.2, client half): layer 1's arithmetic is BoneLibraryMath
# (BonesLibraryMathTests), layers 2-3 BoneLibrary / BoneLibraryProcedural
# (BonesLibraryTests, BonesLibraryProceduralTests).

# The debug overlay library, and the clip it put back (plan_entity.md step 4c).
#
# THE SUBJECT IS THE CLIP. Six entries of game/shared/debugoverlay_shared.cpp had
# become `#ifdef SOURCESHARP return;` because each clipped against a local player
# held as a pointer to a class this build no longer has -- six silent no-ops,
# which is the shape §7 forbids. The library takes the two vectors the clip
# actually needs and the frame step fills them in from a handle; these facts pin
# that the clip is stock's and not an approximation of it.
#
# The segment rule is NOT the point rule applied twice, and the two spellings
# agree everywhere except on the segments r_visualizetraces exists to draw --
# near-and-behind at one end, far-and-ahead at the other. Two facts separate
# them, and a recording IVDebugOverlay says what actually reached the engine so
# that "it drew" is a recorded fact rather than the absence of a crash.
#
# Plus the same file fact bone_library_test carries, for the same reason and
# with the same tested stripper: neither library file may name an entity type,
# because step 7's client half calls this code.
#
# Compiles the library itself. Links mathlib: DotProduct, LengthSqr and the
# forward-vector-to-yaw conversion are the subject.
ss_cpptest_mathlib(overlay_library_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    INCLUDES sourcesharp/game/shared
    SOURCES sourcesharp/test/overlay_library_test.cpp
            sourcesharp/game/shared/overlay_library.cpp)

# plan_entity.md step 4e: the shared trace-filter family and the ConVar the
# trace inlines read moved out of game/shared/util_shared.cpp into the shim.
# TEXT ONLY -- the subject is which file holds which definition and which vpcs
# compile it, so there is nothing here to link.
ss_cpptest(trace_filter_library_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/trace_filter_library_test.cpp)

# plan_entity.md step 7f: the six symbols game/client/cdll_util.cpp still owed
# the shim moved into sourcesharp/game/client/client_util_library.cpp, and the
# vision-flag family moved WHOLE because its two counters are file statics.
# TEXT ONLY, for the same reason as the trace filter library above.
ss_cpptest(client_util_library_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/client_util_library_test.cpp)

# plan_entity.md step 7f: six entity-free families left game/shared/util_shared.cpp
# for the shim. The file is on BOTH link lines, so the vpc rows are asserted on
# both -- a one-sided row gives one side two definitions and the other none.
ss_cpptest(util_shared_library_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/util_shared_library_test.cpp)

# plan_entity.md Phase 4 exit, lane p4-util: game/shared/takedamageinfo.cpp is
# one of SCC 11's 31 TUs, and every definition it held moved WHOLE into
# sourcesharp/game/shared/takedamageinfo_library.cpp. The unit is the whole TU
# because 7f measured the family move and it CONVERTED. TEXT ONLY, for the same
# reason as the two libraries above.
ss_cpptest(takedamageinfo_library_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/takedamageinfo_library_test.cpp)

# plan_entity.md Phase 4 exit, lane p4-util: seven families of entity-free
# mechanism left game/server/util.cpp for
# sourcesharp/game/server/server_util_library.cpp. util.cpp is SERVER-ONLY, so
# the client vpc is asserted to have NO row -- the opposite direction from the
# shared libraries above. TEXT ONLY.
ss_cpptest(server_util_library_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/server_util_library_test.cpp)

# plan_entity.md Phase 4 exit, lane p4-util: the moves that shrink SCC 11's
# ATOMIC batch rather than the census. Nothing here was ever a shim->stock
# cross-edge, so tools/entity-census reads identically before and after; what
# moved is what one cycle member still owed another. TEXT ONLY.
ss_cpptest(cycle_debt_move_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/cycle_debt_move_test.cpp)

# plan_entity.md Phase 4 exit, lane p4-util: the blood/tracer/decal emitters out
# of game/shared/util_shared.cpp. 4e measured this family and declined it; what
# changed is recorded in the file's banner. UTIL_BloodDrips stayed and the
# reason is only visible from the client. TEXT ONLY.
ss_cpptest(blood_family_move_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/blood_family_move_test.cpp)

# What the map-entity-block parser answers (map_entity_block_cases.txt). The
# other half of point_template's port: the tokeniser the BSP's entity lump is
# read with, and the key/value cursor over one entity's block of it, moved to
# SourceSharp.Shared/MapEntityBlock.cs.
#
# This compiles the REAL game/shared/mapentities_shared.cpp -- against a one-TU
# cbase.h shim (test/entitylist_stub's arrangement) -- so the table the managed test reads is pinned
# to the code it was taken from rather than agreeing with itself.
#
# The two -Wno- flags are for VALVE'S HEADERS: tier0/platform.h's MSVC
# `#pragma warning` lines and its braceless DECLARE_ALIGNED_BYTE_ARRAY `if`.
# Same two as the other legs that pull the tier0 prelude in.
ss_cpptest_tier1(map_entity_block_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/map_entity_block_stub public public/tier0 public/tier1
             game/shared common
    SOURCES sourcesharp/test/map_entity_block_test.cpp)
# The ICollideable ADAPTER over that arithmetic (game/shared/collideable_center.h).
# collideable_center_test above pins the maths with no engine in it; this pins
# which of an ICollideable's answers the maths gets, which needs real Vectors, a
# real QAngle and a real matrix3x4_t -- so mathlib, and a second binary rather
# than more cases in the first. Its load-bearing case is the one that says the
# OBB centre and the midpoint of WorldSpaceSurroundingBounds are DIFFERENT
# POINTS on a rotated entity, which is what makes AnyEntity_t::BodyTarget's swap
# from one to the other something a test can fail on at all.
ss_cpptest_mathlib(collideable_center_adapter_test
    SOURCES sourcesharp/test/collideable_center_adapter_test.cpp)

# The guard over all six of the above. Every one of them links mathlib, and
# NOT calling MathLib_Init() is a green test with an assertion behind it: tier0's
# Assert prints and returns, gtest is never told, and both sides of every
# comparison come out of the same uninitialised tables and agree.
# collideable_center_adapter_test sat in that state -- four green tests over six
# printed assertions -- until this landed. Reads THIS FILE, so a new
# build_and_run_mathlib line is covered the moment it is written; the runtime
# half is the grep in build_and_run_mathlib itself. No mathlib here: it is a
# source scanner, and linking it would make it its own subject.
ss_cpptest(mathlib_init_guard_test
    SOURCES sourcesharp/test/mathlib_init_guard_test.cpp)

# The server's engine-facing core, retyped (plan_baseentity.md Phase 13).
# Several of those retypes are correct only because of a fact in a DIFFERENT
# file, and when such a fact stops being true the retyped code still compiles
# and still runs -- it just quietly does something else. Pinned here: the splash
# hook nothing overrides (so deleting the branch that called it is free), the
# fluid throttle's key being a HANDLE rather than an entity (so every entity
# outside the SDK does not share the single key NULL), and IsInPrediction's
# server arm being vacuous because player_command.cpp is out of the project and
# nothing else writes the prediction player.
ss_cpptest(engine_core_retype_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/engine_core_retype_test.cpp)

# The seventeen sound statics really did leave the class (gamesound.h) -- the
# same move precache_model_move_test guards, and guarded the same way. Of the
# twenty-six CBaseEntity members game/shared/SoundEmitterSystem.cpp defined out
# of line, SEVENTEEN are declared `static` and so take no `this`; their bodies
# are the free functions GameSound_*. The class-scope names spent one phase as
# one-line forwarders quarantined in game/shared/baseentity_sound_forwarders.cpp;
# route step 1 retired that file and moved all 78 call sites onto GameSound_*.
# The other NINE take a `this` and stayed put.
#
# Three ways that rots with no diagnostic anywhere: logic grows in a forwarder
# and the SDK's unqualified call sites silently stop agreeing with the port's
# qualified ones; an eighteenth static definition gets written back into
# SoundEmitterSystem.cpp because its neighbours are there; or one of the nine
# non-statics gets tidied into the quarantine file, which would compile and
# would also destroy the only reason that file exists. So the scanner pins the
# split at 17/9, pins every forwarder as a forward and nothing more, and pins
# the free functions as declared in the header and defined in the .cpp.
#
# A source scanner that reads nothing passes every claim, so this one is itself
# checked: the fixtures assert the three files were really opened, and that the
# extractor tells a DEFINITION from a CALL -- which matters here, because the
# file under test is full of both (three CBaseEntity::Instance calls and six
# "CBaseEntity::EmitSound" VPROF string literals).
ss_cpptest(soundemitter_lift_test
    SOURCES sourcesharp/test/soundemitter_lift_test.cpp)

# The sound system's five remaining reaches into an entity, and why each one
# went (laneR_sound_dead_paths_test.cpp). game/shared/SoundEmitterSystem.cpp is
# a removal-set file that STAYS -- fourteen server objects and nineteen client
# objects reach into it for GameSound_*, shell_sound.o among them, and it is the
# only path a sound takes in this port. So its NON-member code has to survive
# Phase 13's alias, and five places in it reached an entity. Each was measured
# and each turned out to be a path that cannot run in this build; each is now
# behind `#ifndef SOURCESHARP` with the stock text intact.
#
# Every one of those five rests on a fact about ANOTHER file, and every one of
# them is green in both directions if that fact stops being true:
#
#   * no file on either link line overrides ModifyEmitSoundParams, and
#     gamerules.h's TranslateEffectForVisionFilter still returns its argument --
#     so GameSound_Emit's entity lookup buys a no-op;
#   * player.cpp / c_baseplayer.cpp / baseplayer_shared.cpp are on neither link
#     line, so a CBaseEntity's IsPlayer() cannot answer true and `fromplayer`
#     was already false on every path;
#   * UTIL_SDKPlayerFromEdict still answers NULL by construction, so the
#     close-caption attenuation loop never ran its body;
#   * C_BasePlayer::GetLocalPlayer() still answers NULL, so playgamesound has
#     only ever printed its else-arm.
#
# The sixth reach was RETYPED rather than guarded: the server's actor-gender
# lookup asks the edict for its own IServerEntity now, which is not merely
# alias-proof but fixes a live defect -- CBaseEntity::Instance answers NULL for
# an entity outside the SDK hierarchy, and every entity this port owns is one.
#
# Nearly every claim here is "X appears ONLY inside a guard", the purest shape
# of a check that cannot fail, so the guard detector is run over hand-written
# fixtures that put the same token on both arms -- of `#ifndef`, of
# `#if !defined()`, of an `#ifdef ... #else`, nested, and of an unrelated
# conditional that must stay transparent -- and asserted to answer differently.
ss_cpptest(laneR_sound_dead_paths_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/laneR_sound_dead_paths_test.cpp)

# NOTHING SETS A PREDICTION SUPPRESS HOST (suppress_host_dead_test.cpp), which
# is what plan_entity.md step 4b's three deletions rest on.
#
# IPredictionSystem's suppress host is "the client who already predicted this
# locally, so do not send it to him again". Three blocks read it -- two in the
# recipient filter, one in the temp-entity system -- and the third was §3.16
# FAMILY 5: a base-entity pointer downcast to a player so it could be culled out
# of a filter. All three are deleted, on the argument that the host is NULL on
# every path in this build.
#
# THAT ARGUMENT IS ABOUT OTHER FILES and is invisible from the files that
# changed, which is exactly the kind of claim that rots without a guard. The
# whole inventory of writers is three: physics_main.cpp's is inside
# `#ifndef SOURCESHARP`, player.cpp is in neither vpc, and takedamageinfo.cpp
# writes back the value it read. A fourth, or any of those becoming live, fails
# a fact here by name.
#
# Every claim is "X does not appear in the part of this file the build
# compiles", the purest shape of a check that cannot fail, so the guard detector
# is run over fixtures putting the same token on both arms -- of `#ifndef`, of
# `#if !defined()`, of an unrelated conditional that must stay transparent, and
# in a comment.
ss_cpptest(suppress_host_dead_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/suppress_host_dead_test.cpp)

# The seven physics statics really did leave the class (physicslink.h) -- the
# same move soundemitter_lift_test guards, found in the same way and guarded the
# same way. Of the FIFTY CBaseEntity members game/shared/physics_main_shared.cpp
# defined out of line, SEVEN take no `this`: six are declared `static` in
# game/server/baseentity.h (1186-1195) and game/client/c_baseentity.h (947-956)
# -- the link machinery, which is handed both of its entities as arguments --
# and the seventh, PhysicsClipVelocity, is pure arithmetic over its four
# arguments. Their bodies are the free functions PhysicsLink_* /
# Physics_ClipVelocity, defined UNMOVED in physics_main_shared.cpp because they
# reach that translation unit's file-static link pools; the class-scope names
# are one-line forwarders quarantined in
# game/shared/baseentity_physics_forwarders.cpp for Phase 13 step 3 to delete in
# one revertible commit. The other FORTY-THREE take a `this` and stayed put.
#
#   physics_main_shared.o             before 50/50   after 43/43
#   baseentity_physics_forwarders.o                  after  7/7
#
# 43 + 7 = 50 on both sides: the symbol set handed to the link is unchanged.
#
# Same three silent rots as the sound lift (logic grows in a forwarder; an
# eighth definition gets written back into the shared file; one of the
# forty-three gets tidied into the quarantine file), plus a fourth specific to
# this one: somebody drops the `static` from a declaration in baseentity.h and
# the PREMISE of the lift stops being true with everything still compiling. All
# four are pinned.
#
# A source scanner that reads nothing passes every claim, so this one is itself
# checked: the fixtures assert the five files were really opened, that the
# extractor tells a DEFINITION from a CALL and ignores prose, and that the
# static / free-definition / forwarder-body detectors each answer FALSE on a
# hand-written negative -- without which the claims above cannot fail.
ss_cpptest(physicslink_lift_test
    SOURCES sourcesharp/test/physicslink_lift_test.cpp)

# baseentity_shared.cpp's share of the same step-3 job, and the one that carries
# the failure nobody was tracking.
#
# Of its 85 server / 86 client out-of-line CBaseEntity definitions, FIVE take no
# `this`: four declared `static` (IsPrecacheAllowed, SetAllowPrecache,
# IsSimulatingOnAlternateTicks, SetPredictionRandomSeed) and
# ComputeTracerStartPosition, which is neither static nor virtual and reads no
# member. They are now EntityPrecache_* / EntitySim_* / EntityPrediction_* /
# EntityTracer_* in game/shared/entity_shared_statics.h, with the class-scope
# names quarantined in game/shared/baseentity_shared_forwarders.cpp. THREE more
# were deleted outright, each proven dead by nm over every object on both link
# lines plus a tree-wide grep, each in its own revertible commit:
# ApplyLocalVelocityImpulse, GetLastThinkTick( const char * ),
# GetNextThink( int ) const. 85 -> 80 -> 77 server, 86 -> 81 -> 78 client, with
# 5/5 in the quarantine file.
#
# THE STATIC DATA MEMBER is the half no consumer count can see.
# `bool CBaseEntity::m_bAllowPrecache = false;` was this file's one (the tree
# has SIXTEEN, not the four plan_baseentity.md listed). A static data member of
# an alias stops the file PARSING rather than failing to link, so it is a worse
# failure than everything step 3 tracks and it is invisible to every
# symbol-counting instrument. The storage moved to EntityPrecache_AllowedRef();
# this test carries a static-data-member DETECTOR, which no earlier lift test
# had, and proves the detector positive on a fixture before asserting the file
# is clean -- an empty-list assertion over a detector that finds nothing is the
# purest check that cannot fail.
#
# Same rots as the two lifts before it, plus: a static data member written back
# into either file, a deleted member typed back, and one of the five acquiring
# `virtual` (which would make the lift wrong, since a virtual name IS a vtable
# slot -- eight members of this file take no `this` and were left alone for
# exactly that reason). All pinned, and every detector is proven able to answer
# both ways on hand-written fixtures and, for `virtual`, on the real headers.
ss_cpptest(baseentity_shared_lift_test
    SOURCES sourcesharp/test/baseentity_shared_lift_test.cpp)

# The other nine of the SIXTEEN static data members, and the two files that held
# fourteen of them. plan_baseentity.md Phase 13 note C counted them; nothing in
# the tree could SEE them, because every earlier lift test extracts a function
# definition and a data member is invisible to that shape by construction.
#
# Nine were hand-written in game/server/baseentity.cpp and
# game/client/c_baseentity.cpp: three dead (deleted) and six live (moved to
# entity_prediction_seed.h and the new entity_abs_query_state.h). The remaining
# five are emitted by the network-table and datamap macros and no source scanner
# can reach them; they are named, not asserted.
#
# The detector here takes the SCOPE as an argument, which the shared-file one
# does not need to. c_baseentity.cpp is not a shared file and writes
# `C_BaseEntity::` literally -- and `CBaseEntity::` is a SUFFIX of it, so a
# hardcoded short scope silently reports client members under the server name.
# That is the same fault the plan's printed measurement recipe had, and it fails
# toward "there is nothing here".
ss_cpptest(baseentity_statics_test
    SOURCES sourcesharp/test/baseentity_statics_test.cpp)

# Twelve of game/client/c_baseentity.cpp's 269 out-of-line member definitions
# left the class scope: the six frame-loop statics c_baseentity_statics.h had
# listed as BLOCKED, the five allocation operators, and the IsFloating stub.
# 269 -> 257 in the release object.
#
# Three of the four friend declarations that license the private access break
# the build if they go. The FOURTH does not:
# EntityInterp_CheckParanoidMeasurement's whole body is behind
# INTERPOLATEDVAR_PARANOID_MEASUREMENT, commented out at interpolatedvar.h:27,
# so deleting its friend is green in every configuration this tree builds and
# breaks the first time anyone turns the define on. This test is the only thing
# that would notice.
#
# The allocation operators are the other reason: "finishing" that lift by making
# the operators themselves free functions is not a compile error, it is a
# REPLACEMENT of the global operator new, and every allocation in client.so
# would start going through an allocator that memsets.
ss_cpptest(c_baseentity_lift_test
    SOURCES sourcesharp/test/c_baseentity_lift_test.cpp)

# The server half of the same job. Eight of game/server/baseentity.cpp's 257
# out-of-line member definitions left: release 257 -> 250, debug 259 -> 251.
# Only TWO are lifts -- ShouldToggle and CreatePredictedEntityByName, into
# baseentity_statics.h. The other six had nothing to lift (empty bodies, or
# bodies that are empty in every configuration this tree builds) and moved into
# the class body, which takes the symbol off and adds no free function.
#
# Two of those six are guarded rather than moved, and that is what most needs
# a test: MyNPCPointer and ScriptGetModelKeyValues still HAVE out-of-line
# definitions, costing no symbol only because #ifndef SOURCESHARP excludes them.
# Delete that guard and both symbols come back with the build still green.
#
# ScriptGetModelKeyValues is also the file's live pointer-to-member trap. Its
# body is one `return NULL` and it still cannot be lifted, because
# DEFINE_SCRIPTFUNC_NAMED takes its ADDRESS -- the SUB_DoNothing shape, missed
# by a first sweep here that looked for DEFINE_THINKFUNC and friends and not
# for the script macro family.
ss_cpptest(baseentity_lift_test
    SOURCES sourcesharp/test/baseentity_lift_test.cpp)

# The NULL-subject bug class, ground edition -- an INVENTORY, not yet a ban.
# `SetGroundEntity( TraceEntity( trace ) )` records no ground for an SDK entity
# that settles onto a managed floor, because TraceEntity answers NULL for the
# parallel hierarchy. Two instances are live, at physics_main.cpp:1102 and
# :1886, and they cannot be fixed until m_hGroundEntity can hold either
# hierarchy -- so a ban test would go red on real code, which is how ban tests
# get commented out. This pins the inventory instead: these two, in that one
# file, none anywhere else. A THIRD cannot appear silently, and when the
# widening lands the test goes red for the right reason and says so.
#
# It also records that the bug is WORSE than game/server/util.cpp:340-378 says.
# CanStandOn( NULL ) returns TRUE (baseentity.h:1122), so site 1102 does not
# skip its block -- it enters it, decides the entity can stand there, and then
# clears FL_ONGROUND. That severity claim rests on one line of a header, so the
# line itself is pinned.
ss_cpptest(ground_trace_subject_test
    SOURCES sourcesharp/test/ground_trace_subject_test.cpp)

# The step-3 removal set's TAIL -- physics_main.cpp, physics_main_client.cpp,
# shell_ai_orphans.cpp and subs.cpp -- pinned per file at 18/15/1/2, plus the
# two quarantine files the lifted names moved into.
#
# It carries two categories plan_baseentity.md does not name, both of which are
# functions that take NO `this` and still cannot be lifted:
#
#   NO `this` BUT VIRTUAL -- PerformCustomPhysics's whole body is Assert(0) and
#   it is virtual on BOTH sides, so a lift would leave every override without a
#   base implementation. Pinned against both real headers, and the virtual
#   detector is proven to answer FALSE for a known non-virtual in the same
#   header so the EXPECT_TRUEs cannot pass for free.
#
#   NO `this` BUT TAKEN AS A POINTER-TO-MEMBER -- subs.cpp's SUB_DoNothing has
#   an EMPTY body, which is the textbook lift shape; what blocks it is that it
#   is a think function reached as `SetThink( &CDerived::SUB_DoNothing )` and
#   registered by DEFINE_FUNCTION. The reason is in the CALLERS, not the body,
#   so both are pinned.
#
# Also pins the scope trap that makes the plan's printed measurement recipe fail
# toward "the set is empty": the client's definitions are written
# `C_BaseEntity::` literally, so a scanner looking only for `CBaseEntity::`
# finds nothing there and reports the client side clean. Every extractor takes
# the scope as a parameter and is run over both spellings.
#
# The static-DATA-member detector is here too -- the failure `nm ... $2=="T"`
# is structurally blind to, because T is text symbols only -- and is proven to
# answer TRUE on a hand-written positive, without which its four uses are
# empty-list assertions that cannot fail.
ss_cpptest(removal_set_tail_test
    SOURCES sourcesharp/test/removal_set_tail_test.cpp)

# The MIDDLE of the same removal set: the three game/shared/ files, 129
# out-of-line CBaseEntity definitions, every one of which lands in BOTH .so.
#
# The counts are the small part. What this pins is the SIX members that are
# `virtual` on one side and plain on the other -- ShouldDrawUnderwaterBullet-
# Bubbles, HandleShotImpactingWater, DispatchTraceAttack, IsCurrentlyTouching,
# SUB_Remove (client-virtual) and ResolveFlyCollisionCustom (server-virtual).
# Read on one header each of those looks liftable; lifting it COMPILES GREEN IN
# BOTH CONFIGURATIONS and leaves every override on the other side with no base
# implementation. No build catches that, so a scanner has to.
#
# Two more disagreements are not about `virtual` at all and no `virtual` scan
# would find them: IsToolRecording exists on the client only (which is why one
# text yields 77 symbols on one link line and 78 on the other), and
# m_bIsPlayerSimulated is PUBLIC on the server and PRIVATE on the client --
# the reason SetPlayerSimulated and UnsetPlayerSimulated cannot be lifted even
# though the compiler says their bodies need no `this`.
#
# The access-region detector and the DEFINE_SCRIPTFUNC first-argument detector
# are both new here, and both are proven to answer every way they can on
# hand-written fixtures -- three of the six disagreement assertions are "is NOT
# virtual on this side", which a detector that finds nothing satisfies for free.
ss_cpptest(removal_set_middle_test
    SOURCES sourcesharp/test/removal_set_middle_test.cpp)

# GC_SECTIONS=0 -- the diagnostic Makefile:30 recommends -- failed the SERVER
# link, with 8 undefined references to 3 symbols props.cpp defined before
# server_csharp.vpc:1223 evicted it, and that props_shared.cpp still names.
#
# The normal build hides it: nothing reachable calls the two functions holding
# those references, so --gc-sections drops the CALLERS before the linker needs
# the callees. `make`, `make test` and src/evict_probe are all structurally
# blind to it -- evict_probe relinks with the same flags the normal build uses.
# Breaking a diagnostic is specially bad, because it is only ever reached for
# while something else is already wrong.
#
# A gtest cannot run a link, so this pins the three legs the repair stands on
# instead, and says so: props_shared.cpp still names all three, props.cpp is
# still evicted (so the stubs are not duplicates), and shell_props_absent.cpp
# supplies all three, server-side only, with parameter types that AGREE with
# the externs. That last is the tripwire -- a mangled name is built from
# parameter types, so a drifted stub still compiles and still links under
# --gc-sections, and fails again only under GC_SECTIONS=0.
ss_cpptest(gc_sections_link_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/gc_sections_link_test.cpp)

# What a PERMANENT decal is stored on. SourceSharp_PaintDecal narrowed the
# trace's IHandleEntity through TraceEntity(), which answers NULL outside the
# SDK's tree -- and then turned that NULL into entity index 0, which is the
# WORLD. So a decal aimed at a managed brush was stored against the world at
# world coordinates: the wrong subject and, because index 0's frame is the
# identity, the wrong frame too. Half of this pins the arithmetic with real
# mathlib -- ICollideable::CollisionToWorldTransform is not
# CBaseEntity::EntityToWorldTransform, and the two agree exactly at zero
# rotation, which is why no axis-aligned map could have found it -- and half
# bans the narrowing at the call site the way impact_subject_test does.
ss_cpptest_mathlib(permanent_decal_subject_test
    SOURCES sourcesharp/test/permanent_decal_subject_test.cpp)

# The two DispatchParticleEffect overloads that outlive the entity component,
# and the §6 ruling that sent them to the wrong place. §6's "17 that LEAVE"
# table answers particle_parse.cpp with "SourceSharp_ParticleSystemStart / Stop
# / FollowPoints, already in the shim" and c8-flip carried that forward as
# "RE-POINT, cheapest family". It is not a re-point: that entry point takes a
# MANAGED ENTITY HANDLE and attaches one followed system to it, and two of the
# three §6-STAYS emitters that owe these overloads pass no entity at all. So
# both bodies moved verbatim into particle_manifest.cpp instead, and this pins
# the move (defined in the staying object, gone from the leaving one -- a
# duplicate is as fatal as a miss), the three call sites that must still make
# them (a compile gate would have taken blood, slime and tracers instead), the
# SS_CBASEENTITY_ALIAS guard closing on the `else` whose arm CLEARS the entity
# field, and the two measurements that refute the re-point.
ss_cpptest(particle_dispatch_rehome_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/particle_dispatch_rehome_test.cpp)

# The clientside physics-prop simulation is `-$File` out of client_csharp.vpc,
# and the .cpp stays on disk exactly as props.cpp's does -- so NO compiler sees
# that decision and every fact under it is defensible only by a source scan.
# Four of them, each revocable by an edit that produces no diagnostic: the
# exclusion itself; props.cpp staying excluded, since its :1775/:1784 are the
# only two te->PhysicsProp senders and reviving them reopens the temp-entity
# door; CTempEnts::PhysicsProp building no prop; and cdll_client_int.cpp not
# calling RecreateAll. The fifth is the fragile one -- props_shared.cpp holds
# four calls to a BreakModelCreateSingle that no longer exists, and the client
# links only because --gc-sections drops PropBreakableCreateAll once its one
# client caller is gone. A second caller makes that reference real.
ss_cpptest(clientside_prop_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/clientside_prop_evicted_test.cpp)

# game/server/effects.cpp is out of the server build (effects_evicted_test.cpp).
# Sixteen entity classes whose every LINK_ENTITY_TO_CLASS was already guarded
# away; evict_probe named ONE symbol the rest of the server still wanted,
# UTIL_BloodSpray, and its only live caller was our own SourceSharp_BloodEffect
# shim -- the inverted shape. The link agrees today and says nothing about why,
# so the facts are scanned: the exclusion; both lifted tenants defined once in
# shell_entity_orphans.cpp; the six precached sounds by NAME (a count of six is
# satisfied by six copies of one); bloodspray answered by a managed command; and
# the fixture-against-native check that game/client/fx_blood.cpp still reads
# exactly the three FX_BLOODSPRAY_ bits BloodSprayCommand.Everything sends in
# place of stock's 0xFF.
#
# The load-bearing one is the PRECACHE_REGISTER_FN: nothing NAMES EffectsPrecache,
# so losing it costs no diagnostic anywhere -- it costs four game sounds, each
# silent on its first play.
ss_cpptest(effects_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/effects_evicted_test.cpp)

# docs/physics-move-scope.md, the honest scope for plan_shim.md 4a item 1
# (physics_move_scope_test.cpp). The document rules function by function on what
# can leave game/server/physics.cpp; NOTHING WAS EXECUTED from it, and the
# finding it records is that no whole function moves. A scope is worth the
# measurement it was taken on, and item 1's previous figure -- "~1,011 lines" --
# was quoted from phase to phase long after the file had changed underneath it.
#
# So every number in the document that source can answer is re-derived here: the
# sixteen vphysics slots by name out of physics_collisionevent.h, the ring's
# eleven recorded kinds out of physics.cpp and shell_constraints.cpp (and
# PRE_COLLISION's absence, which is what makes "eleven of twelve" a measurement),
# the six instruments and five stock debug commands apart, the 585 managed lines
# by wc, and the totals table summing to its own total. The link-line readings
# are not re-run here -- they need nm over 108 objects -- and the document says
# so instead of implying they are checked.
ss_cpptest(physics_move_scope_test
    LINUX_ONLY "forks a child process and reads its wait status"
    SOURCES sourcesharp/test/physics_move_scope_test.cpp)

# Route step 4: our own shim TUs stopped deriving from the entity
# (shim_underive_test.cpp). Three effect classes in shell_effects_sim.cpp, the
# rope anchor, and the gamerules proxy entity, which nothing ever spawned.
# The link agrees about the cost -- client 48 consumers / 4727 refs -> 46 /
# 3812, server 49 / 2181 -> 48 / 2020 -- and is silent about everything that
# matters: whether the think those entities carried still happens, whether the
# allocator still zeroes the fields three constructors leave alone, and whether
# the three volume tests that used to trace against a brush model still answer.
# Each of those fails with no diagnostic anywhere.
ss_cpptest(shim_underive_test
    SOURCES sourcesharp/test/shim_underive_test.cpp)

# The rules class derives from IGameRules and nothing else, and the file its
# last base came from is out of the server build. A source test because what it
# checks is a shape -- which base a class names and what a vpc lists -- and a
# re-added base compiles perfectly.
ss_cpptest(gamerules_interface_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/gamerules_interface_test.cpp)

# The player's nine hulls are declared in SourceSharp.Shared and published into
# the rules object's CViewVectors. shell_gamerules.cpp keeps the same nine as
# the value a read BEFORE managed registration answers with, so the two tables
# are a pair that must agree and nothing observable happens when they do not --
# the game simply plays with one hull for part of a level load and another
# afterwards. A source test because that is an EDIT, and an edit compiles.
ss_cpptest(gamerules_view_vectors_test
    SOURCES sourcesharp/test/gamerules_view_vectors_test.cpp)

# The entry-point prototypes are GENERATED (tools/entrypoint-decls) from the ABI
# header's signatures and from the fill block's per-side wiring. This re-derives
# both in C++ and compares, which catches the two staleness shapes that are not
# compile errors: a declaration left for a slot the fill no longer wires, and a
# declaration on the wrong side of the CLIENT_DLL guard.
ss_cpptest(entrypoint_decls_test
    SOURCES sourcesharp/test/entrypoint_decls_test.cpp)

# What shell_diagnostics.h PROMISES, and what must not stop being promised. Four
# entry points had no caller and went; three things in the cluster are reached
# BY NAME -- -assertlog (31 tools), cs_{sv,cl}_crossings (8 tools, the live gate
# and the DebugApi) and cs_cl_notshipped (7a/7c) -- and a ConCommand that stops
# being registered is not a link error, it is a tool whose console says "Unknown
# command".
ss_cpptest(shell_diagnostics_surface_test
    SOURCES sourcesharp/test/shell_diagnostics_surface_test.cpp)

# The collision matrix says exactly what CGameRules::ShouldCollide said. Keeps
# stock's predicate verbatim as the oracle and checks all four hundred pairs --
# the domain is finite, so this is a proof rather than a sample.
ss_cpptest(collision_rules_test
    SOURCES sourcesharp/test/collision_rules_test.cpp)

# THE VERDICTS ON THE SHIM TUs THAT DERIVED FROM THE ENTITY
# (shim_underive_verdict_test.cpp). All three refusals this file was written to
# carry have now been TAKEN, which is what its own standard asked for -- "a red
# here means RE-TAKE THE VERDICT, not regression". shell_netentity went first,
# by route (c). world_adapter went at plan_cworld_trace.md T3: edict 0, DT_WORLD
# and the NULL-subject contract, three grounds dissolved and one measured wrong.
# shell_areaportal went at plan_entity.md step 4g: its ground was a FENCE around
# func_areaportalbase.h, and the fence left with the files -- none of
# func_areaportalbase.cpp, func_areaportal.cpp or func_areaportalwindow.cpp is
# compiled, so the header had two readers left and both were shim files. The
# list is retyped in the shim's own areaportal_list.h to a record that is not an
# entity. What the file asserts now is THE SHAPE THAT REPLACED each refusal, so
# a later lane cannot quietly put one back. Same standard gib_evicted_test.cpp
# was written to.
ss_cpptest(shim_underive_verdict_test
    SOURCES sourcesharp/test/shim_underive_verdict_test.cpp)

# CStandaloneEntity has ONE layout, for everybody (standalone_layout_test.cpp).
# Its members are defined out of line in THREE .cpp files -- base, hierarchy and
# shell_standalone -- from a header none of them is the swap partner of, and
# step 5's manifest handed the first two to different agents. Nothing in the
# build can see that: an ODR violation between two TUs of one .so needs no
# diagnostic, the blob is reached through NON-VIRTUAL DataBlob()/BlobBytes() so
# there is no dispatch to disagree, and operator new's Assert is debug-only and
# about the subclass size rather than about two TUs disagreeing.
#
# Two facts, both textual because a compiler structurally cannot check them:
# no preprocessor conditional inside the class body, and no shell off the side
# of the chain the blob offset's MAX is taken over.
ss_cpptest(standalone_layout_test
    SOURCES sourcesharp/test/standalone_layout_test.cpp)

# game/server/gib.cpp is OUT of the server build and off disk
# (gib_evicted_test.cpp -- this WAS gib_verdict_test.cpp, and the rename is the
# finding). It had the one-consumer inverted shape effects.cpp had --
# shell_worldquery.o alone, for SourceSharp_ShootGib, acting for a MANAGED
# env_shooter -- and the eviction had been evaluated and REFUSED, on three named
# blockers: no collision callback for a solver-simulated entity, six networked
# fields and none of them render state, and no way to make a ragdoll from a
# model.
#
# All three were fixed, each by a different lane and each for its own reasons,
# and each fix was recorded in that file beside the blocker it closed. So the
# arithmetic the refusal rested on inverted -- everything the port would have
# had to ADD was already there -- and plan_entity.md step 4a took the eviction.
# A gib is ss_gib now; the behaviour is asserted in C# (GibTests), and what is
# asserted here is what only a source scan can see.
#
# The three blocker tests are KEPT, re-headlined: their subjects are load-bearing
# now rather than absent, so a red one means the port has lost something it
# stands on. One refusal genuinely survives -- OnScriptedPhysicsCollision is
# still dead and still blocks the RAGDOLL half of the cluster.
ss_cpptest(gib_evicted_test
    SOURCES sourcesharp/test/gib_evicted_test.cpp)

# plan_entity.md step 4a's cluster, file by file (effects_cluster_test.cpp).
# FOUR of its seven stock TUs have left the server build and THREE have not, and
# each of the three is held by one or two stock TUs that belong to a different
# step. "Not ready yet" and "quietly forgotten" look identical in a status note,
# so the holders are asserted in PAIRS: a holder leaving is a RED naming the file
# it unblocks. It also pins the two retired slots as refusals that SAY SO, which
# is the one thing every other test in the tree cannot tell from a silent zero.
ss_cpptest(effects_cluster_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/effects_cluster_test.cpp)

# func_clip_vphysics is C#'s, and here is what holds it up
# (clip_vphysics_port_test.cpp). REPLACES bmodels_verdict_test.cpp, which
# recorded the REFUSAL to move this classname and whose own message said what to
# do when it was overturned.
#
# The refusal rested on ForceVPhysicsCollide being SDK-only: stock's clip brush
# is SOLID_VPHYSICS with FSOLID_NOT_SOLID on top, and it overrides that virtual
# to undo the not-solid rejection in CCollisionEvent::ShouldCollide. Answering
# it from C# meant a managed call per collision PAIR, which is more C++ in a
# worse place than the eviction removes.
#
# The port does not answer it. PhysicsOwner_t::GetSolidFlags already answers 0
# for an entity outside the SDK -- deliberately, with the reason written beside
# it -- so the not-solid rejection never applies to a managed brush and one that
# is passable to TRACES is still solid to the SOLVER. That is the whole of a
# clip brush, and the port cost ZERO lines of added C++.
#
# Which makes three facts in three files nobody looks at load-bearing, so all
# three are asserted here. The first is the one that would be a plausible
# improvement and a silent regression: teach PhysicsOwner_t::GetSolidFlags to
# read ICollideable and dm_lockdown's three clip brushes stop clipping.
ss_cpptest(clip_vphysics_port_test
    SOURCES sourcesharp/test/clip_vphysics_port_test.cpp)


# game/server/soundent.cpp holds a LIST, not an entity
# (soundent_gamesystem_test.cpp). CSoundEnt was a CPointEntity and is now a
# CAutoGameSystemPerFrame: being an entity bought it a Think slot and a row in
# the entity list, every one of its five referrers asks a STATIC of it, and
# `soundent` is a classname NO map spawns -- the only demand for it was our own
# world_adapter.cpp calling InitSoundEnt, which called
# CBaseEntity::Create( "soundent" ). The inverted shape, closed.
#
# CAISound went with it: ai_sound is C#'s (Entities/Env/AiSound.cs) and the C++
# registration had been inside `#ifndef SOURCESHARP` for batches.
#
# Asserted rather than written down, because the parts that can go stale are
# facts about OTHER people's files: the map, the six static spellings five
# objects hold undefined references to, and the managed class that owns the
# classname C++ stopped serving.
ss_cpptest(soundent_gamesystem_test
    SOURCES sourcesharp/test/soundent_gamesystem_test.cpp)

# game/server/sound.cpp keeps SENTENCEG_* and has lost its ENTITY
# (ambient_generic_evicted_test.cpp). CAmbientGeneric was ~1200 lines whose
# registration had been inside `#ifndef SOURCESHARP` for batches; C# serves
# ambient_generic and dm_lockdown places TWENTY-ONE of them. It is deleted
# rather than left compiled because Phase 13 makes CBaseEntity a typedef for
# IGameEntity -- six pure virtuals and no members -- so "leave it compiled"
# stopped being one of the options.
#
# UTIL_RestartAmbientSounds went with it: its body walked gEntList for the
# classname and could not outlive the type. The DECLARATION stays in util.h on
# purpose, so that anyone putting player.cpp (its only caller, excluded below)
# back gets a link error naming the symbol rather than a silent no-op. That
# exclusion is asserted here, because it is the whole safety argument and it
# lives in a file this lane does not own.
ss_cpptest(ambient_generic_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/ambient_generic_evicted_test.cpp)

# game/client/c_func_dust.cpp holds TWO unrelated things and one of them left the
# build (dust_verdict_test.cpp). C_Func_Dust is one of the nine compiled
# ClientClasses that pair on a ServerClass name nothing in this build sends --
# CFunc_Dust -- with no classname on either side reaching it, so its only route
# into existence was `new C_Func_Dust` in our own shell_dust_render.cpp: the
# INVERTED shape. The motes now live on a CParticleEffect the shell owns, which
# is what CDustEffect always was.
#
# FX_Dust, TE_Dust and C_TEDust are in the same file, are the dust PUFF rather
# than the volume, and STAY -- c_te.o and EffectsClient.o want them. A guard
# whose #endif slides is how one takes the other with it, so the test's region
# scanner is required to answer INSIDE for the volume and OUTSIDE for the puff,
# and is itself checked against a fixture first.
ss_cpptest(dust_verdict_test
    SOURCES sourcesharp/test/dust_verdict_test.cpp)

# THE OTHER HALF OF THE DUST STORY (nodraw_visibility_test.cpp). 7a's report
# ends with two func_dust brushes that exist on the client, have no model, and
# never get one -- because CStandaloneEntity::SetVisible expressed "invisible"
# by zeroing the model index. Step 8e-render moved that to EF_NODRAW in the
# block's Effects word, which the wire now carries. Every link in that chain is
# a place the state silently stops reaching the client, so each is asserted
# apart; the first two are a compiled check over the real shared field table,
# because an entry inserted in the middle of the array without the enum moving
# with it would mis-address every prop after it on BOTH sides.
ss_cpptest(nodraw_visibility_test
    SOURCES sourcesharp/test/nodraw_visibility_test.cpp)

# THE LAST ENTRY OF ContractBaseline.OpenDefects (teleport_discontinuity_test.cpp).
# "An entity that teleports is drawn sliding to where it went": stock networks a
# counter a teleport bumps and the client throws its interpolation history away
# when the value moves; this hierarchy bumped nothing and reset a ring on one
# condition -- a handle rebind, which is a create. IT IS TWO HALVES AND THEY MUST
# LAND TOGETHER, and neither is a compile error, a crash or a red managed fact
# without the other: the reader's unit facts bump the counter themselves. So the
# WIRE between them -- block word, SendProp, RecvProp, change mark -- is asserted
# link by link, with the shared field table compiled the way nodraw's is.
ss_cpptest(teleport_discontinuity_test
    SOURCES sourcesharp/test/teleport_discontinuity_test.cpp)

# THE OTHER TWO-THIRDS OF STEP 8e-render (fog_wire_test.cpp). World fog did not
# render on this client and had not since the flip: fogcontroller.cpp is
# entirely `#ifndef SOURCESHARP`, so server.so declared no DT_FogController and
# there was NO SENDER. Every link of the channel that replaces it is asserted
# apart -- including the two CALLS that have no other trigger, which is the
# shape this tree has twice found in the state "driver, dispatcher, tests, and
# no production caller".
ss_cpptest(fog_wire_test
    SOURCES sourcesharp/test/fog_wire_test.cpp)

# THE SAME SIX LINKS, ONE WIRE OVER (skybox_wire_test.cpp), for the defect the
# fog's row was filed beside and which took TWO faults to close: phase4-exit's
# reachability half (UpdateAllClientData walked UTIL_PlayerByIndex, which cannot
# answer, so SourceSharp_FillSkyboxParams was never reached) and this lane's
# wire half (CStandalonePlayer networks PlayerNetState, not CPlayerLocalData, so
# what the server filled went nowhere). Measured live before the fix: 12,270
# frames with a 3D skybox VISIBLE from the eye and NULL answered on every one.
# The sixth link here is the RENDERER'S OWN DRAW COUNT -- 8e-render's dust
# defect passed every counter it had while drawing nothing.
ss_cpptest(skybox_wire_test
    SOURCES sourcesharp/test/skybox_wire_test.cpp)

# Four unpaired CLIENT classes -- C_PhysBox, C_PhysicsProp, C_BreakableProp and
# C_BaseParticleEntity -- and the facts their eviction from client_csharp.vpc
# rests on (client_orphan_evictions_test.cpp). The link already agrees: all four
# objects come out of the client with evict_probe exit 0 in BOTH configurations,
# so nothing derives from them and nothing calls a member. What the link cannot
# see is the JOIN -- put props.cpp, physobj.cpp or baseparticleentity.cpp back in
# the SERVER build and the server sends a class the client can no longer decode,
# with no diagnostic anywhere -- so that is scanned from both ends: the three
# server exclusions stay, and the whole tree is walked to prove no file that is
# still compiled re-declares one of the four datatable names.
#
# The load-bearing pair is baseparticleentity.cpp's REGISTER_EFFECT: it writes
# into CParticleMgr::m_effectFactories, whose only reader is CreateEffect, which
# nothing calls. Give it a caller and the eviction quietly costs a lookup that
# now answers NULL.
ss_cpptest(client_orphan_evictions_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/client_orphan_evictions_test.cpp)

# plan_entity.md step 4f. Two deletions of the same shape, pinned together
# (entity_base_husks_test.cpp): three SDK entity classes whose
# LINK_ENTITY_TO_CLASS was already `#ifndef SOURCESHARP`, so nothing could
# create one -- and 140 link-glue stubs whose callers left the build with the
# files earlier steps deleted.
#
# The load-bearing pair here is client.cpp's `sv_allow_point_servercommand`
# registration: CPointServerCommand was its only native reader and is gone, but
# PointServerCommand.cs asks for the convar BY NAME. Delete the registration
# with the class and the most dangerous entity in the SDK becomes ungated with
# nothing failing. The other one is EffectsPrecache, which `tools/4a-owes`
# names nowhere by design -- it is on CPrecacheRegister's list, not reached by
# symbol -- so an owes-driven deletion would take six live game sounds with it.
ss_cpptest(entity_base_husks_test
    SOURCES sourcesharp/test/entity_base_husks_test.cpp)

# The hermetic half of tools/4f-component (component_closure_test.cpp), the same
# way entity_gates_test is entity-gates'. The tool's real mode reads a
# 166-object link and cannot be shown to be right from its own output, so the
# closure algebra runs over a fixture -- and this is what makes `make check` run
# that fixture at all. It also pins the two false greens a measuring tool dies
# of: a self-test that ran ZERO checks and still exits 0, and a seed the side
# does not compile closing to the empty set, which reads exactly like "this step
# is independent of everything else".
ss_cpptest(component_closure_test
    LINUX_ONLY "forks a child process and reads its wait status"
    SOURCES sourcesharp/test/component_closure_test.cpp)

# The FLEX PAIR leaves the CLIENT build -- c_baseflex.cpp and
# c_baseanimatingoverlay.cpp (client_flex_evicted_test.cpp). 240 symbol-needs and
# two above-cliff consumers of c_baseentity.o, both vtable-bound.
#
# The liveness argument is written against NETWORK NAMES, not DT_ table names,
# because on this tree the DT_ diff is wrong in both directions: DT_WORLD vs
# DT_World is a case mismatch on a class live on both sides, and "DT_BaseFlex"
# occurs as a plain string in 48 server objects whose sources do not register it.
# The engine pairs a ClientClass to a server class by network name, so that is
# what is scanned -- and CBaseFlex and CBaseAnimatingOverlay are registered by
# game/server/baseflex.cpp and game/server/BaseAnimatingOverlay.cpp, both of
# which are already out of the server build.
#
# The link agrees in both configurations (evict_probe exit 0), so nothing here
# re-checks symbols. What it does check is what the linker cannot see: the JOIN
# from both ends, the client CLASSMAP path (neither file ever had a
# LINK_ENTITY_TO_CLASS, so CreateEntityByName could never reach either class),
# and the six convars that leave with the class that was their only subject.
ss_cpptest(client_flex_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/client_flex_evicted_test.cpp)

# The ENTITY BEAM leaves the CLIENT build -- game/shared/beam_shared.cpp
# (client_beam_evicted_test.cpp). 180 symbol-needs and one above-cliff consumer.
#
# Not the beams anybody sees: every beam this game draws is a Beam_t made by the
# live temp entities (CTEBeamPoints and friends) through CreateBeamPoints, and
# that path is untouched. C_Beam is the env_beam/env_laser MAP entity.
#
# THE LOAD-BEARING TEST IS THE CLASSMAP one, because unlike the flex pair this
# file carried LIVE client registrations -- LINK_ENTITY_TO_CLASS( beam, CBeam )
# and ( info_target, CInfoTarget ) -- so both names leave the CLIENT classmap
# with it. A factory reached by a string leaves no symbol, so no linker can say
# whether anything wanted them; the tree is scanned instead. The only askers for
# "beam" are inside the evicted file itself.
#
# And the file asserts the Beam_t overload SURVIVES, in as many words: a change
# that deleted both overloads would satisfy every absence check in the file while
# silently stopping every beam in the game from rendering.
ss_cpptest(client_beam_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/client_beam_evicted_test.cpp)

# CBeam and CBaseViewModel leave the SERVER build
# (server_beam_viewmodel_evicted_test.cpp). Unlike the client four above, these
# two are not orphans: each has a LIVE LINK_ENTITY_TO_CLASS -- `beam` and
# `viewmodel` -- so they are constructible in principle and simply never
# constructed. That is a claim about STRINGS, and no linker makes it: a factory
# reached by name from a .bsp or from managed code leaves no symbol in the link.
#
# So the maps are scanned as well as the sources, and the sharpest fact in the
# file is about a classname that IS in both maps: beam_shared.cpp:62 registers
# info_target, which dm_lockdown and ss_sandbox both declare. The eviction is
# safe only because that registration sits inside `#ifndef SOURCESHARP` and
# managed code answers the name -- un-guard it and this quietly takes a factory
# two maps need. bmodels.cpp was REFUSED on exactly that evidence, until
# func_clip_vphysics moved to C# and the evidence changed sides; see
# clip_vphysics_port_test.cpp above.
ss_cpptest(server_beam_viewmodel_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/server_beam_viewmodel_evicted_test.cpp)

# game/client/c_rope.cpp is STILL in the client build, on purpose
# (rope_verdict_test.cpp). lane_u/ledger.md cluster 7 verdicts C_RopeKeyframe
# DELETE on strong evidence -- N-DEAD, no client classname, dm_lockdown's nine
# ropes served by Ropes.cs and ClientRope.cs -- and the deletion is REFUSED here
# for a reason the (S) column understates: two of the four referrers are the
# render path, not incidental link demand. server_csharp.vpc's own comment
# beside the rope.cpp exclusion says "the drawing is the stock C_RopeKeyframe,
# built client-side by shell_rope_render.cpp -- c_rope.cpp therefore STAYS in
# the client build as the render backend". So the managed rope is drawn by this
# entity, and deleting it means rewriting shell_rope_render.cpp to draw directly
# -- U2's move for sprites and dust, except that this shim belongs to shard T3.
#
# Two blockers, each a fact about a file somebody may change, so each is
# asserted rather than written down: shell_rope_render.cpp names C_RopeKeyframe,
# and c_baseanimating.cpp's model-cables path calls CreateFromKeyValues on a
# class this server really can send. A red is RE-TAKE THE VERDICT, not a
# regression -- same standard as gib_evicted_test.cpp.
ss_cpptest(rope_verdict_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/rope_verdict_test.cpp)

# C_LocalTempEntity is DE-ENTIFIED, and the ledger had REFUSED that
# (lane_u/ledger.md §5). The refusal rested on two premises, both measurably
# false: that a tempent is in the client entity list -- all three
# AddNonNetworkableEntity calls were dead in this build -- and that only
# C_StandaloneEntity implements the IClientUnknown + IClientRenderable floor,
# when CShellRenderable is 214 header-only lines and already carries the sprite
# trail. Each premise is asserted here, along with the six answers
# CDefaultClientRenderable gets WRONG rather than not at all: a missing
# ComputeFxBlend is a tempent that never fades, a missing GetColorModulation is
# every blood sprite drawn white, a missing IgnoresZBuffer is a muzzle flash
# that is never drawn -- and none of the three produces a diagnostic.
#
# Also pins the blob-offset measurement that unblocked all of this: the
# constant, its assert, and the fact that C_StandaloneEntity has exactly one
# constructor and it joins the NETWORKABLE entity list. A red is RE-TAKE THE
# VERDICT, not a regression.
ss_cpptest(tempent_verdict_test
    SOURCES sourcesharp/test/tempent_verdict_test.cpp)

# The pool's SEAM (plan_tmp.md Part 5, lane C4). The pool's behaviour is tested
# in C# where it now lives; what is here are the three ways a seam is wrong that
# no managed test can see -- a life field half-wired, a stock #ifndef arm
# deleted, and a refusal quietly becoming a second implementation.
ss_cpptest(tempent_pool_test
    SOURCES sourcesharp/test/tempent_pool_test.cpp)

# The pool's SPAWN seam (C-Phase 5), which is the other direction of the same
# boundary. The seven managed spawn helpers are tested in C#; what is here are
# the ways this seam is wrong that no managed test can see -- most of all a
# field that CROSSES and is never applied, which from the managed side is
# indistinguishable from one that is, because the assertion over there reads the
# record back rather than the tempent.
ss_cpptest(tempent_spawn_seam_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/tempent_spawn_seam_test.cpp)

# The HL2 commentary model viewer and the two model panels under it are out of
# the client build (commentary_viewer_evicted_test.cpp). A VGUI panel was one of
# c_baseentity.o's vtable-bound consumers because basemodelpanel.h:24 is
# `class CModelPanelModel : public C_BaseFlex` -- a panel that previews a model
# does it by making an entity.
#
# The link had exactly ONE reference left, baseviewport.cpp's `new` inside
# CreatePanelByName, and it is now inside a `#ifndef SOURCESHARP`. GUARDING A
# CALLER IS NORMALLY THE WRONG WAY OUT -- server_csharp.vpc says so, and lifted
# three functions rather than guard them -- so the two legs that make it right
# here are asserted rather than argued: the commentary system is `-$File` out of
# BOTH builds, and CreateDefaultPanels never asks for this panel, so the only way
# to reach it was a console command for authoring content this build cannot play.
#
# The one that costs nothing to break is the vpc exclusion: the guard alone makes
# the build link, so un-excluding the three files silently reinstates a
# C_BaseFlex vtable and 120 symbol-needs for a panel nothing can open.
ss_cpptest(commentary_viewer_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/commentary_viewer_evicted_test.cpp)

# Where the four table chains END, once CBaseEntity is IGameEntity
# (base_table_chain_end_test.cpp). Five static DATA members of CBaseEntity block
# Phase 13 step 4 -- m_DataMap both sides, m_PredMap and m_pClassRecvTable
# client, m_pClassSendTable server -- and they are a COMPILE error rather than a
# link one, in every derived class's own TU, because each table macro names the
# member of the BASE: 37 references across 29 distinct release objects at
# 674f3e0e, not the three defining files the plan expected.
#
# The fix is one non-template overload per chain, taking IGameEntity *. It is
# INERT in an unarmed tree -- nothing has IGameEntity for a BaseClass yet -- so
# no normal build exercises it, and an armed survey only shows the errors gone,
# never that the DERIVED case still resolves the other way. Both halves are
# asserted here, against the real datamap.h / dt_send.h / dt_recv.h rather than
# a replica: a replica in which the template always won would pass every
# NULL assertion a correct one passes.
#
# mathlib, and only because igameentity.h includes mathlib/vector.h -- the same
# reason entity_bases_alias_test needs it.
ss_cpptest_mathlib(base_table_chain_end_test
    SOURCES sourcesharp/test/base_table_chain_end_test.cpp)

# break_piece_offset_test moved to C# with its subject (BreakPiecePlacementTests).

# Behaviour ledger row C1's SEAM (ragdoll_blast_seam_test.cpp). The blast push
# on ragdolls is C# and eleven [Fact]s hold its behaviour; what is here are the
# four ways this seam can be wrong that no managed test can see, because each is
# on the far side of a slot a managed test replaces with a double:
#
#   a trace mask named on ONE arm only -- sourcesharp.h says twice what happens
#   then, "read there as the default (brushes only) and answered a plausible
#   wrong number in silence", and nothing guarded it until now; the impulse
#   landing on the wrong physics object, where element zero is what
#   VPhysicsGetObject answers with and a loop over the limbs is a body that
#   flies apart; the creation tick stamped above BecomeRagdoll's seven early
#   returns, which makes the push skip an entity that never became a ragdoll;
#   and CRagdollExplosionEnumerator moving inside the SOURCESHARP guard, which
#   would leave the managed [Fact] that reads stock's constants off disk still
#   green and no longer a comparison.
ss_cpptest(ragdoll_blast_seam_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/ragdoll_blast_seam_test.cpp)

# plan_server_all.md Wave 0 -- the free ones (server_wave0_evicted_test.cpp).
# The wave moves no logic, so this file is the only thing that holds it. Four
# facts, and each has a way of being quietly undone:
#
#   The four SourceSharp-authored files (entity_bases, baseentity_statics,
#   precache_model, toucher -- .cpp and .h) moved out of game/server/ into
#   sourcesharp/game/server/. BOTH directories are on this project's
#   $AdditionalIncludeDirectories, so a copy left behind at the old path would
#   compile and win silently; asserted absent, not just present-at-the-new-path.
#
#   The two $File lines naming translation units no configuration can produce
#   -- replay_gamestats_shared.cpp [$BUILD_REPLAY], haptic_utils.cpp [$WINDOWS].
#
#   The three probe verdicts -- h_export.cpp, dt_utlvector_send.cpp,
#   interpolatortypes.cpp -- gone from the server file list, with h_export's
#   emptiness on Linux re-read from the file rather than trusted.
#
#   ...and all three still ON DISK. server_base.vpc names all three,
#   client_base.vpc names h_export.cpp, and the client compiled
#   interpolatortypes.cpp until plan_client_novalve C1 evicted it for the same
#   reason (docs/client-novalve-rulings.tsv).
ss_cpptest(server_wave0_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/server_wave0_evicted_test.cpp)

# plan_server_all.md Wave 1 -- the shadowed five (server_wave1_shadowed_test.cpp).
# FOUR of them left, by four different exits, and the exits are the point: a
# later lane reading only the meter cannot tell a deletion from a fold.
#
#   weapon_parse.cpp        deleted from the list; it already had zero
#                           consumers. CreateWeaponInfo went out of
#                           shell_shared.cpp with it -- weapon_parse.cpp:103
#                           was its only caller, and that premise is asserted
#                           against weapon_parse.cpp itself.
#   env_wind_shared.cpp     deleted after one dead call went. The safety
#                           argument lives in somebody else's file -- effects.cpp
#                           is the only server-side constructor of a
#                           CEnvWindShared and it is out of the build -- so that
#                           is asserted here too.
#   soundscape_system.cpp   relocated and shrunk to shell_soundscape.cpp. The
#                           four traversal steps that decide a soundscape's
#                           NUMBER are pinned individually, because the number
#                           is shared with the client's own parse of the same
#                           files and a disagreement is silent.
#   func_areaportalbase.cpp folded whole into shell_areaportal.cpp -- and then
#                           UNFOLDED at plan_entity.md step 4g, when the list
#                           was retyped to a record that is not an entity. The
#                           fact is now that the SDK class has no instances and
#                           no compiled reader, which is the same eviction
#                           stated against the shape that replaced it.
#
# The FIFTH, TemplateEntities.cpp, is asserted STILL PRESENT. Wave 1's premise
# for it -- that PointTemplate.cs owns the behaviour -- is only half right:
# managed owns the spawning, and the store plus its entity-IO name fixup is
# live C++ with three live consumers. "Wave done" plus a moved meter is exactly
# how a file stops being anybody's problem, so it is written down as a test.
ss_cpptest(server_wave1_shadowed_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/server_wave1_shadowed_test.cpp)

# plan_server_all.md Wave 2 -- two lifts and a fold (server_wave2_lifts_test.cpp).
#
#   haptic_msgs.cpp        six usermessages->Register calls lifted into
#                          shell_shared.cpp under `#ifdef GAME_DLL`, AND SINCE
#                          UNLIFTED -- plan_gamedll.md Phase 5a step 2 deleted
#                          them, and the six are the first six [UserMessage]
#                          declarations in C# now. What is checked here is that
#                          the residue is gone from both sides and that nothing
#                          calls RegisterHapticMessages any more.
#   ModelSoundsCache.cpp   folded into precache_model.cpp. The five CL_EVENT_*
#                          ids are checked in BOTH files: they are animation
#                          event numbers baked into the models, stock's own
#                          comment says they must match cl_animevent.h, and
#                          nothing checks that at build time.
#   precache_register.cpp  folded into world_adapter.cpp. Asserted to have ONE
#                          list head: two would mean the registrant pushes onto
#                          one and the walk reads the other, which is a silent
#                          no-op and six unprecached sounds, not a link error.
#
# And interval.cpp is asserted STILL PRESENT, with its reason. It probes free in
# both configurations and `make debug GC_SECTIONS=0` names four references to it
# from AI_Criteria.cpp -- evict_probe links with the collector on, so a verdict
# of "free" means free under the collector. The next lane should not have to
# rediscover that.
ss_cpptest(server_wave2_lifts_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/server_wave2_lifts_test.cpp)

# plan_side_isolation P2 -- the user-message forwarders
# (usermessage_forwarders_test.cpp).
#
# The registry, ids, send, dispatch and handlers are C#'s, with xUnit facts
# over them (UserMessageRoutingTests). What native code keeps is one forward
# per entry point the engine and Valve's code reach -- GetUserMessageInfo, the
# Valve senders' name lookup and DispatchUserMessage -- and the send half that
# carries C#'s answer out. HookMessage went with its last compiled caller
# (lane HC, 2026-10-04); NoNativeHookRouteRemains reads client_sourcesharp's
# list through cmake_graph.h. The haptics-
# order test and its oracle went with the native list: ids are carried by the
# SsUserMessages table, and demos are out of scope. usermessage_info_test and
# usermessage_ordering_guard_test's behaviours that still have a native
# subject are carried here.
ss_cpptest(usermessage_forwarders_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/usermessage_forwarders_test.cpp)

# plan_gamedll_after.md Phase A4 subject 3 (commandclient_move_test.cpp).
#
# The console client's slot used to be a file-scope int in gameinterface.cpp
# that SetCommandClient assigned and UTIL_GetCommandClientIndex read, 3,200
# lines apart in one translation unit. A4 split them across two files, so what
# used to be a scope guarantee is now a link. The way that breaks is silent --
# the getter answers 1 forever and every console ent_fire looks ignored -- and
# util.h:641-653 records the hour this project already spent on that exact
# symptom. These facts pin the write, the read, the +1 between them, and that
# there is exactly one definition of each.
ss_cpptest(commandclient_move_test
    SOURCES sourcesharp/test/commandclient_move_test.cpp)

# plan_gamedll_after.md Phase A4 subject 4 (debugoverlays_move_test.cpp).
#
# DrawAllDebugOverlays left gameinterface.cpp for its own file. Three things
# about that move the linker cannot check: that frame step 12 still CALLS it
# (it reaches it through a forward declaration in shell_framesteps.cpp, so
# deleting the call compiles clean and draws nothing forever), that it is still
# NOT behind _DEBUG (stock's own comment beside the call is "allow this in
# release for now"), and that both SS_CBASEENTITY_ALIAS guards inside it came
# across -- each wraps a C-cast to CBaseEntity of something this build may put
# a non-CBaseEntity in.
ss_cpptest(debugoverlays_move_test
    SOURCES sourcesharp/test/debugoverlays_move_test.cpp)

# plan_gamedll_after.md Phase A9 (gameinterface_split_test.cpp).
#
# The four census interfaces gameinterface.cpp still implemented -- GameEnts,
# SharedAppSystems, GameClients (x3 version strings) and GameTags -- move to
# files of their own and the file is deleted. Three things there that a link
# cannot see: an EXPOSE_ that goes MISSING is found by the engine at load and
# not by ld; an EXPOSE_ that is DUPLICATED links fine and is resolved by
# static-init order; and the file-scope state each class carried had to become
# class fields, where a field that is declared and never assigned compiles.
ss_cpptest(gameinterface_split_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/gameinterface_split_test.cpp)

# plan_server_all.md, the nine zero-consumer files (server_wave3_interfaces_test.cpp).
#
# docs/server_eviction_graph.md lists nine stock objects nm says nobody asks
# for. Section 8 warns that a zero external surface is a probe candidate and not
# a free deletion, and it was right nine times out of nine: TWO were evicted and
# SEVEN are traps, of four different kinds. This file is the note back to
# whoever runs the probe next and gets the same nine.
#
#   playerinfomanager.cpp  deleted. Publishes three interfaces through
#                          CreateInterface (invisible to nm), and the strings
#                          appear only in serverplugin_empty.dll -- a WINDOWS
#                          sample plugin.
#   studio_shared.cpp      deleted. Unreferenced with --gc-sections OFF, which
#                          is the only way that claim means anything.
#   plugin_check.cpp       KEPT. Same macro, opposite answer:
#                          PluginHelpersCheck001 IS in engine.so.
#   randoverride.cpp       the dangerous one: it INTERPOSES libc's
#                          rand()/srand(). Remove it and the link succeeds --
#                          libc answers -- and its callers change random number
#                          generator in silence. Its server callers are gone, so
#                          it left server.so for src/client/native (2026-10-12);
#                          the test now holds "no server object imports rand".
#   networkvar.cpp         KEPT. Zero readers in release, TWENTY-SIX in debug.
#                          Section 8's trap 4, and why evict_probe takes a MODE.
#   interval.cpp           KEPT; see server_wave2_lifts_test.cpp.
#   effect_dispatch_data.cpp KEPT; DT_EffectData must leave both sides at once.
#   stdafx.cpp             KEPT; it creates the PCH.
#   memoverride.cpp        not this project's line to remove.
ss_cpptest(server_wave3_interfaces_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/server_wave3_interfaces_test.cpp)

# plan_server_all.md Wave 8, the tiny globals (server_globals_merge_test.cpp).
# Five files -- globals.cpp, init_factory.cpp, variant_t.cpp, server_class.cpp,
# networkvar.cpp -- became sourcesharp/game/server/shell_globals.cpp. Section 9
# names exactly these five and says one file can hold them.
#
# A RELOCATION AND NOT AN EVICTION, which is why it was possible at all:
# docs/server_eviction_graph.md computes that eighty-one of the remaining stock
# files are one entangled knot in which no member's $File line can go until
# essentially all of them do, and four of these five are IN it. Nothing stopped
# naming these symbols; the definitions moved and the link graph is unchanged.
#
# So this test asks a different question from the rest of the series. Not "is it
# out of the list" but "are all eight definitions still here, EXACTLY ONCE".
# Exactly-once is the half that matters: a merge that copies rather than moves
# gives two g_pServerClassHead, and the second is a second list head --
# ServerClasses register into one and the engine walks the other, with no link
# error to say so.
#
# Plus which files stayed on disk (server_class.cpp and networkvar.cpp are under
# public/ and the CLIENT compiles both, asserted against client_base.vpc itself)
# and which did not (the three server-only ones are deleted, on the same Phase 13
# reasoning bmodels.cpp went out on).
ss_cpptest(server_globals_merge_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/server_globals_merge_test.cpp)

# The damage masks, ported out of CMultiplayRules (gamerules_damage_test.cpp).
# Five bitmasks that are POLICY -- acid keeps hurting you, a fall applies no
# physics force, a crowbar can gib a corpse -- and were literal ORs inside stock
# method bodies. DamageRules.cs owns them now and pushes them once at startup.
#
# DamageRulesTests checks the managed half against multiplay_gamerules.cpp. This
# checks the two things that are native:
#
#   the DEFAULTS in shell_gamerules.cpp are stock's, mask by mask, compared
#   against that same .cpp -- because the table is read from damage paths that
#   can run before the managed runtime is up on a listen server still loading,
#   and a zeroed table means no damage is time-based and nothing gibs;
#
#   all TEN overrides exist. Miss one and it falls through to CMultiplayRules,
#   which still compiles and still answers, so the port would be half-done with
#   four masks managed and one silently stock and nothing to say which.
#
# Plus the improvement over stock is asserted rather than just made: each
# predicate must call its own getter and must NOT name DMG_* flags itself.
# Stock answered every one of these questions twice, in two independent
# spellings, with nothing checking they agreed.
ss_cpptest(gamerules_damage_test
    SOURCES sourcesharp/test/gamerules_damage_test.cpp)

# THE LAYOUT OF CGlobalVars (plan_gamedll.md Part 0a #1), which the managed
# `Globals` reader mirrors field for field in SourceSharp.Abi/Globals.cs.
#
# This is the file that COMPILES globalvars_layout.h, and that is most of its
# job. The header's table is a set of static_asserts against the real
# offsetof(CGlobalVars, ...) -- so a field inserted into public/edict.h or
# public/globalvars_base.h fails this build with the field's name in the error
# -- and a static_assert in a header with no consumer is not a check at all.
# Until the shell picks the header up in Part 2, this is the only consumer.
#
# The failure it exists for is silent on both sides of the boundary: a managed
# mirror one field short reads its neighbour's bytes and returns a plausible
# curtime. Nothing crashes and nothing is logged; the game just runs at the
# wrong speed. GlobalVarsLayoutParityTests is the managed half, and it reads
# this same table as text so that no number in the comparison comes from C#.
ss_cpptest_engine_headers(globalvars_layout_test
    SOURCES sourcesharp/test/globalvars_layout_test.cpp)

# The server's runtime adapter, RUN rather than read
# (managed_runtime_server_test.cpp). plan_gamedll.md Phase 2 and Part 3b.
#
# CManagedRuntimeServer is the whole of the new mechanism between the shell game
# DLL and C#: one forward per IManagedRuntimeServer method into the managed
# table. Two properties, and both are properties of the BODIES rather than of
# the shape:
#
#   every method reaches its own slot and reaches it exactly once -- the four
#   map-load arms differ by one identifier, take the same argument and return
#   the same type, so a Background forwarding to NewGame would boot, load, and
#   run the wrong arm of stock's LevelInit forever with nothing to say so;
#
#   and it holds NO COPY of the table. `m_Api = *SourceSharp_GetManagedApi()` in
#   a constructor compiles, links, passes everything else in this tree, and
#   breaks on the first cs_sv_reload: the pointer is stable because it is a
#   file-static's address, but the CONTENTS are memset and refilled, so the copy
#   holds function pointers into an unloaded assembly. The only proof is to move
#   the table underneath a live adapter and watch the call land in the new
#   place, which is what the generation-1/generation-2 tables here do -- twice
#   in a row, which is the reload gate the phase asks for reduced to what is
#   testable with no engine.
#
# It compiles the SHIPPED header, which is why that class is in a header with no
# engine include in it. Plus the source checks on the runtime's slots (every one
# SS_RETIRED since plan_engineapi_vtables E1, and no required-slot list left to
# name them since E17b), and Part 10b's two DLLShutdown defects,
# which no other test can see because the shell's EXPOSE_ is commented out until
# Phase 4 and nothing calls DLLShutdown at all.
#
# -I for the ABI header and for sourcesharp_gamesystem.h, which is where
# IManagedRuntimeServer and MapLoadInfo are declared.
ss_cpptest(managed_runtime_server_test
    INCLUDES ${PROJECT_SOURCE_DIR}/src/host/native sourcesharp/game/shared
    SOURCES sourcesharp/test/managed_runtime_server_test.cpp)

# THE SWITCH -- plan_gamedll.md Phase 4 (shell_gamedll_registration_test.cpp).
#
# The engine now finds CShellGameDll when it asks for "ServerGameDLL012". What
# makes that safe rather than catastrophic is three arrangements, and not one of
# them is a compile error, a link error, or a number `strings` can produce:
#
#   the guard is on the REGISTRATION and not on CServerGameDLL's class body --
#   Phase 4's own text asks for both that and a CServerGameDLL * delegate, and
#   a pointer needs a complete type with a definition to link, so the two
#   instructions are mutually exclusive and only one of them is the gate;
#
#   every engine-facing method hands its call back to stock while -shellgamedll
#   is absent, which is the whole of T4's A/B and the thing Phases 5-7 break one
#   method at a time if nothing enumerates the class;
#
#   and the shell ASSIGNS g_pSharedChangeInfo. Stock assigns it inside
#   CServerGameDLL::DLLInit; its reader is CBaseEdict::StateChanged, an inline
#   that dereferences it unconditionally on every networked variable write. A
#   shell that owns DLLInit and does not assign it is a null dereference at the
#   first state change -- invisible to every offline gate, which is why the
#   arrangement is pinned here instead.
#
# Source-read rather than run, and that is forced: shell_gamedll.cpp includes
# gameinterface.h and cbase.h's whole world, so its bodies cannot be linked into
# a gtest binary. The binary half of the first property is
# tools/gamedll-interface-census, which reads the built server's symbol table.
ss_cpptest(shell_gamedll_registration_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/shell_gamedll_registration_test.cpp)

# THE REFUSALS AND THE COUNTER -- plan_gamedll.md Part 7 and Phase 8
# (shell_gamedll_refusals_test.cpp).
#
# Sixteen methods on CShellGameDll do not get a managed side. Part 7's rule is
# plan_server.md Part 5's: a stub that is never called and a stub that is called
# constantly look identical from outside -- so every one of them warns and
# counts, and Phase 8's gate is the counter reading ZERO after a map has been
# played rather than the game failing to crash.
#
# Two halves needing two instruments, which is why this binary does both:
#
#   the COUNTER's bodies, run. CShellGameDllRefusals lives in an engine-free
#   header for managed_runtime_server.h's reason -- that a call increments, that
#   the same method twice is one row and not two, and that two counters share
#   nothing (which is the only way, from inside a gtest, to tell a field from
#   the file-scope static this project's rules forbid) are all properties of a
#   body, and a property of a body can only be checked by running it;
#
#   and the ARRANGEMENT in shell_gamedll.cpp, source-read, because that file can
#   never be linked here. Above all that IsRestoring does NOT warn: it returns
#   false, which is correct, but GameFrame reads it as its first gate so the
#   engine asks it every frame, and a warning there is a console nobody can read
#   and a game nobody can play. That failure mode is invisible to every
#   assertion about return values, and "someone tidied the odd one out to match
#   its fifteen neighbours" is exactly the edit that looks like an improvement.
ss_cpptest(shell_gamedll_refusals_test
    INCLUDES sourcesharp/game/server
    SOURCES sourcesharp/test/shell_gamedll_refusals_test.cpp)

# THE ONE REFUSAL THAT STILL HAS TO WRITE SOMETHING -- docs/plan-audit.md D16
# (shell_savecomment_test.cpp).
#
# Fourteen of the fifteen refused save-cluster methods return void or a value
# the shell chooses, so refusing by doing nothing is a complete answer.
# GetSaveComment is handed a buffer to FILL -- 80 chars on the
# engine's stack -- and a refusal that returned without touching it left the
# engine calling uninitialised stack the save's title.
#
# It is not hypothetical. D16 measured the save cluster REACHABLE: the engine's
# gate is sv.IsMultiplayer(), this mod's own GetPlayerLimits sets minplayers to
# 1, and a server launched -maxplayers 1 answers `save` by calling
# GetSaveComment and SaveInit. Both were counted live.
#
# Same split as the refusals binary above: the FORMATTING is a property of a
# body, so shell_savecomment.h is compiled and run here; the ARRANGEMENT in
# shell_gamedll.cpp -- that it fills AND still counts, in that order -- is
# source-read, because that file can never be linked into a gtest. The subject
# of every fact is a COLUMN, because the save UI parses this string by position
# and a formatter that lost its width would read perfectly and parse wrong.
ss_cpptest(shell_savecomment_test
    INCLUDES sourcesharp/game/server
    SOURCES sourcesharp/test/shell_savecomment_test.cpp)

# GATE 5's RECORDERS -- plan_server.md Gate 5, docs/plan-audit.md item C2
# (shell_steporder_test.cpp).
#
# The order the 39 native steps run in is the whole of what C# contributes to a
# map load and a frame (plan_gamedll.md Part 4: "a step is never a decision"),
# and until this landed there was nowhere it was written down. A dropped step, a
# duplicated step or two swapped all produce a server that boots, loads, plays
# and answers 473 / 61 -- `make live-gate` cannot see any of the three.
#
# Two halves needing two instruments, which is why this binary does both, and it
# is shell_gamedll_refusals_test.cpp's split for the same reason:
#
#   the RECORDERS' bodies, run. CShellStepOrder and CShellTransmitSet live in an
#   engine-free header so that a ring that overflows SAYING SO, a budget of n
#   recording exactly n, and a one-shot capture being one-shot are checked by
#   running them rather than by reading them.
#
#   and the ARRANGEMENT in the four .cpp files that record, source-read, because
#   all four include cbase.h's world and can never be linked here. The half that
#   decays is the call-site count: a step added without its Record() line is a
#   sequence short by one in a position the expected file has no row for, and
#   the gate then reports a dropped step that never happened. So the site count
#   is checked against the DEFINITION count rather than against a number written
#   down in the test.
ss_cpptest(shell_steporder_test
    INCLUDES sourcesharp/game/server
    SOURCES sourcesharp/test/shell_steporder_test.cpp)

# CHECKTRANSMIT'S CROSSING -- plan_server.md Phase 8b, docs/plan-audit.md C4
# (shell_transmitsteps_test.cpp).
#
# ALL SOURCE READS, and the file says why at length: the two steps need the
# entire server (engine, edict_t, GetContainingEntity, CBasePlayer::m_Local,
# CStandalonePlayer) and can never be linked here. The BEHAVIOUR is held by
# ServerEntitiesTests (8 xUnit facts, mutation-verified 5/5 by
# sourcesharp/test/server_entities_mutate.sh) and by tools/gate5's recorded
# transmit set; what is here is the ARRANGEMENT, which is a property of text
# that compiles and runs identically whether or not it holds.
#
# The four that matter: the `#ifdef SOURCESHARP` disjunction actually LEFT
# shell_gameents.cpp (if it comes back it comes back as a plausible local fix,
# and the C# body goes on being called and being right and being ignored); the
# WALK stayed native, which the plan and the audit both state as a limit rather
# than a task; each step reads one hierarchy so that neither can choose; and the
# unguarded static_cast<CBasePlayer*> is stock's and must not be "fixed".
ss_cpptest(shell_transmitsteps_test
    SOURCES sourcesharp/test/shell_transmitsteps_test.cpp)

# CLIENTEARPOSITION'S CROSSING -- plan_server.md Phase 7, docs/plan-audit.md C5
# (shell_clientsteps_test.cpp). The same shape as the file above it and for the
# same reasons; the BEHAVIOUR is ServerClientsTests' 7 xUnit facts.
#
# Two things here are NOT in the transmit file's list, because they are what is
# different about this crossing:
#
#   THE ORDER IS REVERSED. ClientEarPosition asked the STANDALONE hierarchy
#   first; CheckTransmit asked the SDK one first. Both are reproduced rather
#   than reconciled, and here it is load-bearing: the SDK arm's miss path is the
#   Assert(0) that Phase 10 recorded as FIRING once the managed weapon machine
#   began emitting sounds on standalone players. ServerClientsTests drives BOTH
#   crossings in one fact so that making them agree cannot pass.
#
#   THE SS_CBASEENTITY_ALIAS ARM MOVED WITH THE READ. The guard exists because
#   CBasePlayer::EarPosition is defined in the struck baseplayer_shared.cpp, so
#   it belongs beside the call -- and a guard left behind in
#   shell_gameclients.cpp would be guarding a call that is not there.
ss_cpptest(shell_clientsteps_test
    SOURCES sourcesharp/test/shell_clientsteps_test.cpp)

# plan_gamedll.md Part 8, Phase 6 -- the two strings that must land at index 0
# (stringtable_index0_test.cpp).
#
# GetMaterialIndex and GetParticleSystemIndex BOTH answer 0 for a name they
# cannot find, so index 0 of each table is what every failed lookup gets. Put
# anything else there first and a failed lookup silently resolves to a real
# material or a real particle system instead of the error one -- everywhere,
# with a DevMsg nobody reads and no other symptom at all.
#
# Not a needle. The shipped body of SourceSharp_CreateNetworkStringTables is
# parsed into the SEQUENCE of operations it performs and that sequence is
# replayed against a model of the engine's numbering, so the assertion is on the
# replayed INDEX. Any earlier add to either table fails it, including one nobody
# thought to write a needle for, and so does a table created after its first add
# -- which is the other way to get index 0 wrong and does not look like a
# reordering.
#
# WIDENED AT PHASE A8, and the widening is what keeps it covering the whole
# thing. plan_gamedll_after.md Phase A8 gives a MOD an AddString, called from
# the managed CreateNetworkStringTables slot -- inside the same engine entry
# point the two precaches are in -- so "a managed add before the precaches"
# became a reachable edit. The replay's subject is now the SHELL's method with
# the native one inlined and the managed call-out modelled as an add to every
# shared table, rather than a second test beside it: a parallel check would have
# left this replay covering half the sequence with nobody noticing which half.
ss_cpptest(stringtable_index0_test
    SOURCES sourcesharp/test/stringtable_index0_test.cpp)

# plan_gamedll.md Part 3a / Part 9a, Phase 7 -- the named list
# (server_systems_table_test.cpp).
#
# THE PHASE'S REAL RISK IS NOT A CRASH. It is that a participant ends up driven
# TWICE per frame, or driven by NOBODY -- and the second is silent by
# construction: a CAutoGameSystem nothing walks does not warn, does not fail to
# link, and does not run. These facts are about the TABLE, which decides both.
#
# Five properties: the rows are unique; every init stage walks them forward and
# every shutdown stage backward, from the SAME array; GameStringSystem is row
# zero so the backwards walk puts it last; the auto-registered
# CSourceSharpSystem is NOT a row (it is the delegate path's driver, and a row
# for it as well is the double drive); and the managed side's four server event
# points have exactly ONE BODY each, called by both drivers.
ss_cpptest(server_systems_table_test
    SOURCES sourcesharp/test/server_systems_table_test.cpp)

ss_cpptest(server_systems_underive_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/server_systems_underive_test.cpp)

# plan_cworld_trace.md T4 -- the CLIENT's half of the same job
# (client_systems_table_test.cpp): kClientSystems[] is unique and ordered;
# every init stage walks it forward and every shutdown stage walks the SAME
# array backward; the base registers nothing and holds nothing and refuses a
# server include; igamesystem.cpp is off BOTH link lines, which is what makes a
# reverted system an undefined reference rather than a silent no-op; every
# driver call site is a table walk rather than a deletion; and the map name --
# which had three live readers in stock's s_pMapName -- is asked for rather
# than held.
ss_cpptest(client_systems_table_test
    SOURCES sourcesharp/test/client_systems_table_test.cpp)

# plan_entity.md §6, the CLIENT FLIP -- lane c8-clientsystems
# (client_flip_ruling_test.cpp): every one of kClientSystems[]'s 38 live rows
# carries a `// RULING |` line saying which translation unit DEFINES its system
# and whether that unit survives the flip. These facts hold the ruling to the
# tree rather than to a filename: the ruling and the table must cover each other
# exactly, every ruling's file must really define the symbol it names, and no
# STAYS may name a file plan_entity.md §6 puts on its seventeen-file LEAVE list
# (nor a LEAVES name one it does not). The one row this lane PORTED,
# C_BaseAnimatingGameSystem, is asserted on its CAPABILITY -- the bone thread
# pool's queue is still emptied and still reported at level shutdown -- and on
# its measured position in the walk order, not on the fact that it compiles.
ss_cpptest(client_flip_ruling_test
    SOURCES sourcesharp/test/client_flip_ruling_test.cpp)

# The same lane's RULINGS, which are claims about what is NOT in a translation
# unit and so cannot be driven.
#
# Thirteen symbols were put to lane f-bone and nine moved. The other four have a
# ruling each, and every one of them contradicts a prior that was written down:
# §6's carve-out calls UnlockStudioHdr a static (it is a private instance
# method), the lane brief calls InvalidateBoneCaches and UpdateClientSideAnimations
# non-statics (both are declared static), and c8-flip.md rules GetHitboxSet onto
# the leaf-library row on the grounds that it takes a CStudioHdr (it is
# `return m_nHitboxSet;`). A ruling recorded only in a handover decays; these
# hold each one to the tree.
#
# The family-2 arm is the load-bearing one: it walks the PREPROCESSOR STACK of
# the three §6 STAY files and requires every hitbox ask to be inside
# `#ifndef SS_CBASEENTITY_ALIAS`, with none outside. That is the entire reason
# those two symbols are not re-homed -- armed, there is no ask -- and the scanner
# itself is driven against a sample with guarded and unguarded needles in it, so
# an arm that answered "inside" for everything fails.
ss_cpptest(bone_leaf_rehome_ruling_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/bone_leaf_rehome_ruling_test.cpp)

# plan_gamedll.md Phase 9 -- the sixteen TODOs, and that none lost its verdict
# (gamedll_ledger_test.cpp).
#
# THE PHASE'S ONE STATED FAILURE MODE IS A DELETION THAT LOOKS RIGHT. All
# sixteen `// TODO:` comments are gone from the tree, which is the outcome the
# plan wanted and is also, letter for letter, what losing the question looks
# like in a diff. The only thing separating the two is that a verdict was
# written in each one's place, and nothing in the build has ever checked that.
#
# Three subjects. THE TABLE: sixteen rows, each with a verdict cell that reads
# as a verdict and a commit cell naming a commit a bisect can reach -- nine of
# those cells were blank for most of this plan's life. THE SITES: every
# surviving TODO in shell_gamedll.cpp and sourcesharp_gamesystem.h sits in a
# comment block that answers it, so a bare TODO reappearing fails here rather
# than being noticed. THE METERS: the refusal counter's reading after a played
# map, and both seed populations -- readings, which a summary quietly turns back
# into claims.
ss_cpptest(gamedll_ledger_test
    SOURCES sourcesharp/test/gamedll_ledger_test.cpp)

# plan_gamedll_after.md Phase A2 -- Part 11a2's three gaps, and the C++ halves
# that stayed native (steam_mdlcache_test.cpp).
#
# InvalidateMdlCache, GameServerSteamAPIActivated and GameServerSteamAPIShutdown
# were empty bodies with a NOT PORTED verdict and no TODO. All three are ported
# now: each forwards to managed and managed calls back one native step. Twelve
# managed facts cover the C# half; these cover the four properties underneath
# that neither a reader nor a compiler can check.
#
# THE ORDER INSIDE THE RE-ACQUIRE -- Clear() then Init(), because activation
# repeats on every reconnect and an Init over a filled context leaves whichever
# pointers the new one failed to replace. ONE BODY FOR THE CLEAR -- the level
# step forwards, because two ->Clear() sites is TODO #5's refused shape. THE
# WALK IS STOCK'S -- dynamic_cast to CBaseAnimating, load-bearing because
# gEntList holds CBaseEntity * and a static_cast is wrong for every brush
# entity. AND NO STATE -- A1's statics-audit catches a file-scope `static` and
# says in its own closing note that it does not catch a non-static global, which
# is the shape a cached "have we activated" flag would take.
ss_cpptest(steam_mdlcache_test
    SOURCES sourcesharp/test/steam_mdlcache_test.cpp)

# lane diag-visibility -- A MANAGED ENTITY EXISTS AND A TOOL CANNOT SEE IT
# (entity_visibility_test.cpp).
#
# Two diagnostics that walked the SDK hierarchy and therefore reported a world
# that was mostly absent. `report_entities` printed NOT ONE LINE on dm_lockdown
# while `./debug-api ents "*"` on the same game read 471; the client's
# `InvalidateMdlCache` walked C_BaseEntity and reached ZERO of this mod's
# entities, silently, from a body that looked exactly like a working one.
#
# The managed halves have facts of their own (ClientMdlCacheFlushTests, fifteen).
# These are the C++ shape, and the one that earns the file is
# TheClientBindingIsNotInsideTheServerOnlyBlock: the first version of the client
# binding landed inside sourcesharp_gamesystem.cpp's 350-line
# `#ifndef CLIENT_DLL`, and the build was green, the crossing fired, C# printed
# its line, and the count in it was 0. No compiler and no grep sees that -- only
# the preprocessor NESTING does, so this file evaluates it, and six facts about
# the evaluator itself keep it from being a check that cannot fail.
ss_cpptest(entity_visibility_test
    SOURCES sourcesharp/test/entity_visibility_test.cpp)

# plan_gamedll_after.md Phase A5 -- the surviving SDK ServerClass registrations
# (stock_serverclass_census_test.cpp).
#
# The endgame meter's second number, given a floor. It read 7 and reads 5. The
# seven were a dependency TREE rather than a list: BEGIN_SEND_TABLE reaches
# BaseClass::m_pClassSendTable and only IMPLEMENT_SERVERCLASS_INTERNAL defines
# it, so a registration with a networked descendant cannot leave, and the meter
# only ever comes down from the leaves inward. A5 took the two leaves that
# existed, CRagdollPropAttached and CTeam.
#
# THE PREMISE IS PINNED, NOT ONLY THE EDIT. An eviction is correct only while
# nothing produces the props it removed -- and that is a property of OTHER
# files, which can change without anybody touching the two that were edited. So
# NothingProducesAnAttachedRagdoll and NothingEverFillsATeam scan the whole
# compiled set for a caller of the zero-caller functions the argument rests on.
# Those are the facts that go red at the moment the eviction stops being true,
# which is otherwise announced nowhere.
#
# The compiled set is DERIVED from server_csharp.vpc's $File lines rather than
# listed, and comments are stripped before every scan -- both edited files now
# spell the exact strings this test asserts are absent, in the paragraphs
# explaining why they are absent.
ss_cpptest(stock_serverclass_census_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/stock_serverclass_census_test.cpp)

# THE MANAGED HOST IS ONE OBJECT PER PROCESS, and this is the gate on it.
#
# sourcesharp/host/sourcesharp.cpp owns the CLR refcount. It used to be a static
# lib linked into client.so AND server.so, so the process had TWO copies of the
# state whose comment said "Single runtime per process" -- and Acquire's
# "runtime already running" branch therefore never executed in any build. The
# fake-engine harness could not see it: it acquires twice inside ONE binary and
# passes either way.
#
# This reads the three linked objects and asserts libmanaged_host.so DEFINES the
# host while both game DLLs IMPORT it, so the moment anyone compiles the host
# back into a module the leg goes red -- which is far better than trying to
# reproduce a shutdown fault in a live listen server. It needs the binaries;
# `make check` builds before it tests.
ss_cpptest(managed_host_single_instance_test
    LINUX_ONLY "reads libmanaged_host.so's ELF headers"
    SOURCES sourcesharp/test/managed_host_single_instance_test.cpp)

# plan_gamedll_after.md Phase A3 -- the two "C" systems that became managed
# (c_systems_managed_test.cpp).
#
# CPropData and CDecalEmitterSystem left kServerSystems[], and the failure that
# creates is SILENT TWICE OVER. A row that leaves the table and gains no managed
# participant does not warn, does not fail to link and does not run -- the decal
# table would simply be empty, and an empty decal table answers -1 to every
# lookup exactly as a working one answers for a decal that is genuinely absent.
# And decals.cpp leaving server_csharp.vpc is T1's hazard in its exact shape:
# the client compiles the same file, and the reason the removal is safe is a
# property of a file the server no longer builds, so no server-side gate sees it.
#
# Six subjects, all source-scanned because every one of them lives in a
# translation unit that includes cbase.h's whole world: the rows are gone AND
# say they moved rather than were deleted; the pre-entity half has exactly one
# caller and one body, inside the guard the other four are in; decals.cpp is the
# client's and keeps its stock base; the shim answers all three of stock's
# interface methods from managed code, guards a null table on each, and holds no
# file-scope state; CPropData's two halves have one owner each with the blocker
# named at the site; and the game-file read refuses rather than truncating.
ss_cpptest(c_systems_managed_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/c_systems_managed_test.cpp)
# plan_gamedll_after.md Part B2 -- CWorld is a shim (world_shim_test.cpp).
#
# The pattern-setting case for every remaining stock server class, so what this
# pins is THE SHAPE rather than the diff. worldspawn carries no
# DECLARE_SERVERCLASS, no IMPLEMENT_SERVERCLASS_ST and no datadesc; its
# ServerClass, its ClientClass and every prop on both are built from ONE C#
# declaration (SourceSharp.Shared.WorldNetState), and native reaches the values
# by asking C# where each field is.
#
# EVERY FACT HERE IS ONE NO COMPILER CAN SEE. The base table has to be prop ZERO
# on both sides, because that is where stock's BEGIN_SEND_TABLE put it and a
# table that builds and links with it somewhere else is off by one for its whole
# length. The ten field names have to be the same ten on two sides of a language
# boundary, because a misspelling resolves to -1 and -1 reads as a zero. The
# world singleton has to be claimed in the CONSTRUCTOR, because the map's keys
# arrive before Spawn does and the blob they are stored in is found by identity
# against that pointer -- which is the one real bug this port had, found by a
# live run and not by reading.
#
# The parity list is a THIRD copy of the ten names, deliberately: a checker that
# derived its list from either side could only ever agree with that side.
ss_cpptest(world_shim_test
    SOURCES sourcesharp/test/world_shim_test.cpp)
# THE 284 ASSERTIONS A cs_sv_reload FIRED (standalone_reload_idempotent_test.cpp).
#
# The managed load context goes away and comes back; the native entity factory
# dictionary does not. So C# re-declared all 284 of its classes into a
# dictionary that already had them, and
# CEntityFactoryDictionary::InstallFactory opens with
# Assert( FindFactory( pClassName ) == NULL ). Measured at exactly 284, against
# 284 in `./debug-api classes`, and carried by four plans without being fixed.
#
# There is no uninstall in IEntityFactoryDictionary to fix it the other way, so
# registration is idempotent by classname instead. What this pins is the shape;
# the behaviour is pinned live, where tools/live-gate now runs a cs_sv_reload
# and fails on any assertion after it.
ss_cpptest(standalone_reload_idempotent_test
    SOURCES sourcesharp/test/standalone_reload_idempotent_test.cpp)
# THE PLAYER WHO LEFT AND WAS NEVER ANNOUNCED (player_disconnect_removal_test.cpp).
#
# plan_entity.md step 2c moved the removal announcement out of
# ~CStandaloneEntity and into EntityDirectory's free pass, which is right for
# every removal MANAGED starts and reaches none of the one removal NATIVE
# starts: CServerGameClients::ClientDisconnect does `delete pStandalone`.
#
# Two live runs measured the cost. After a second client joined and left, every
# hot reload said "480 of 481 entit(ies) restored; 1 FAILED to rebuild, first
# 'ss_player'" -- the departed player's managed instance still in the registry,
# carried into the reload's records, and unrebuildable because the entity it
# names is gone. Also LivenessMirror answering "alive" about a freed entity,
# and SsPlayer.OnRemoved never running.
#
# What this pins is the shape, and above all the ORDER: the announcement
# carries the handle and a span over the blob, and the delete frees both. The
# behaviour is pinned live, where tools/live-gate's hot-reload step now asserts
# reload.failed.entities == 0 and names the class that failed.
ss_cpptest(player_disconnect_removal_test
    SOURCES sourcesharp/test/player_disconnect_removal_test.cpp)
# THE ENGINE'S POINTER INTO THIS DLL (gameevent_listener_lifetime_test.cpp).
#
# shell_gameevents.cpp handed the engine's event manager a file-scope
# listener with AddListener and called RemoveListener nowhere. The engine keeps
# that list for the whole process -- it is set up once at engine start-up --
# so the subscription correctly outlives every map change, and
# would have outlived the DLL its vtable lives in.
#
# The identical shape is already handled three lines away:
# SourceSharp_UnloadManagedSide opens with SourceSharp_RemoveFileSystemHook.
# What this pins is the pair, and the ORDER -- an unhook below the
# g_bSourceSharpLoaded early-out skips the case that most needs it.
ss_cpptest(gameevent_listener_lifetime_test
    SOURCES sourcesharp/test/gameevent_listener_lifetime_test.cpp)
# THE SHELL SECTION 13 ASKED FOR IS GONE, AND SO IS ITS TEST
# (toolframework_absent_test.cpp, deleted in Phase 1b).
#
# plan_server_all.md section 13 ruled toolframework_server.cpp should be
# "replaced with an absent-shell", and shell_toolframework_absent.cpp was that
# shell. Its own test carried the exit condition in as many words --
# TheOneConsumerIsStillThere said that if shell_gameclients.cpp's
# `g_pToolFrameworkServer->PreSetupVisibility()` ever went away, "delete both
# together". Phase 1b deleted that call and both files in one commit.
#
# The no-op was identical rather than merely harmless, which is why removing the
# call is not a behaviour change: every stage of the stock class opens with
# `if ( !m_pTools ) return;`, m_pTools is set only in a CToolFrameworkServer::Init
# that Phase 7a left out of kServerSystems[], so it was NULL in every process
# this build ever ran.
# WHO FREES THE WORLD (world_release_test.cpp) -- the regression pin for a crash
# that shipped on the integration branch and that no gate could see.
#
# ffd3e097 un-derived CWorld from CBaseEntity and left `Release() {}` behind a
# comment saying the entity purge would free it anyway. That was true of the
# route CWorld had just stopped taking: CGlobalEntityList::Clear() sends SDK
# entities to UTIL_Remove, which deletes, and everything else to Release(),
# which for this one class did nothing. So the world outlived its level,
# g_WorldEntity stayed set, the next map's worldspawn was REFUSED, and the first
# trace of that load -- SourceSharp_PaintDecal's, at level init -- faulted
# inside the engine. dm_lockdown painted 81 decals on the first load and died on
# the 82nd.
#
# IT NEEDS TWO MAP LOADS TO SEE, which is why nothing saw it: every live gate in
# this tree loaded one map. The runtime half of this guard is the level-reload
# step in the live gate; what is pinned HERE is the contract underneath it --
# which arm of Clear()'s fork the world falls down, that the arm destroys, and
# that ~CWorld undoes each registration AttachToEdictZero made, all of which
# were ~CBaseEntity's until that commit.
ss_cpptest(world_release_test
    SOURCES sourcesharp/test/world_release_test.cpp)
# plan_gamedll_after.md Phase A3, SECOND pair -- CSoundEnt (soundent_managed_test.cpp).
#
# The same silent failure in a different shape. CSoundEnt's row overrode
# FrameUpdatePreEntityThink and nothing else, so a row that leaves the table and
# gains no managed participant leaves the sound list NEVER SWEPT -- and a list
# that is never swept does not empty, it keeps answering with entries that
# should have expired. Nothing prints, nothing crashes, and the AI hears gunfire
# from a minute ago.
#
# The second subject has no analogue in the first pair: the pool used to be
# reachable as MEMORY. SoundPointerForIndex handed a CSound * to two callers,
# and a port that left either of them holding one would be handing a raw pointer
# into a managed array -- which compiles, links, and reads whatever is at that
# address.
#
# BOTH SYSTEMS OF THE PAIR ARE HERE, because their two failure shapes only make
# sense beside each other.
#
# CSoundEnt (14 checks): the row is gone and says it MOVED and where to;
# soundent.cpp is out of server_csharp.vpc and the shim is in; the header still
# declares the statics the shim defines; the shim defines all five, guards a null
# table on each of the four that ask, holds no file-scope state, and crosses
# owner AND target as handles; neither remaining native caller holds a CSound *;
# and the ABI's four new slots are declared once and REQUIRED while the two
# engine slots are gone from both sides of the boundary.
#
# CSoundControllerImp (11 checks): the same row questions, plus the ones its
# SHARED file forces. soundenvelope.cpp compiles into client.so too, so the
# checks are that the SERVER stopped compiling it and the CLIENT did not, and
# that client_shared_parity.sh names it -- because that gate DERIVES its list
# from the two vpcs, so the file dropped out of its scope at the exact commit
# its client half needed watching. Then: the shim overrides every pure virtual
# the header declares (counted FROM the header, not written down), guards every
# forward against a null managed table with no do-nothing table behind it, holds
# three statics of which exactly one is a variable and that one is
# function-local, crosses a patch as an int handle in all seventeen slots, binds
# all four engine slots, and defines GetSoundSaveRestoreOps rather than leaving
# the header's promise dangling.
#
# And the fourth frame point, which had NO PRODUCTION CALLER until this phase --
# the second stage in this tree found in that state.
ss_cpptest(soundent_managed_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/soundent_managed_test.cpp)

# plan_gamedll_after.md Phase A3, the FIFTH and last "C" system --
# CSourceSharpStandaloneSystem (standalone_system_test.cpp).
#
# The odd one out, and the checks are shaped by that. The other four were stock
# SDK systems being pulled across a boundary; this one is SourceSharp's own, and
# Part 9a's note -- "already ours" -- reads as an argument that it moves easily.
# Measured, it is the native FLOOR the managed hierarchy stands on, so it was
# taken apart by STAGE rather than ported as a system.
#
# Init() is DELETED, and its deadness is an ORDER spread over four files in two
# languages: DLLInit loads the managed runtime before it walks the system table,
# ServerModule.Initialize publishes the hierarchy unconditionally, PublishAll
# calls NetHierarchyDeclare, and that is what registers the tiers. No one file
# can state it, which is precisely why a second registration call survived --
# it compiled, linked, ran, and could only ever return at its own gate.
#
# The other two stages MOVED, and the failure that creates is silent in the
# worst way this hierarchy has: the standalone entities are not on gEntList, so
# a drive that stops running does not empty the world -- it leaves every
# standalone entity frozen and still transmitting, and a teardown that stops
# running leaves the previous map's entities standing in the next one, holding
# edicts and answering to their names.
#
# And the event point the teardown needed had NO PRODUCTION CALLER: the third
# stage found in that state in this plan, after OnLevelInitPreEntity (first
# pair) and OnPreClientUpdate (second pair).
ss_cpptest(standalone_system_test
    SOURCES sourcesharp/test/standalone_system_test.cpp)

# plan_entity.md Phase 0a. The census tool is a shell script, so this leg does
# not compile it -- it RUNS it, and drives it into the states where it must
# refuse. No engine headers, no defines: everything it needs is popen and the
# tree it is standing in.
ss_cpptest(entity_census_test
    LINUX_ONLY "runs the tree's bash and Python instruments (tools/) and reads their exit status"
    SOURCES sourcesharp/test/entity_census_test.cpp)

# plan_shim.md §6, lane 4a. tools/shim-orphan-reach is a python script, so this
# leg does not compile it -- it RUNS it over the real link line and requires
# neither linker-satisfaction file to define anything nobody imports. It also
# drives the tool into its REFUSAL, because a tool that cannot ask the question
# must not answer zero: an unbuilt tree otherwise reads as "delete this file".
ss_cpptest(orphan_reach_test
    LINUX_ONLY "forks a child process and reads its wait status"
    SOURCES sourcesharp/test/orphan_reach_test.cpp)

# plan_entity.md Phase 5, the 5b split. tools/dead-conditionals is a python
# script, so this leg does not compile it -- it RUNS it, over the real tree and
# over scratch fixtures that drive it into every state it must report. The
# fixtures are the point: "the tree is clean" on its own is a check that cannot
# fail. No engine headers: popen, stat and the tree it is standing in.
ss_cpptest(dead_conditionals_test
    LINUX_ONLY "runs the tree's bash and Python instruments (tools/) and reads their exit status"
    SOURCES sourcesharp/test/dead_conditionals_test.cpp)

# plan_entity.md 0c. The size-then-fill loop behind cs_sv_crossings, with a fake
# slot per failure mode. Engine-free by construction: crossings_fetch.h takes
# nothing but <stdlib.h>, which is what lets it be compiled here at all --
# shell_diagnostics.cpp, its other consumer, includes cbase.h and cannot be.
ss_cpptest(crossings_fetch_test
    INCLUDES sourcesharp/game/shared
    SOURCES sourcesharp/test/crossings_fetch_test.cpp)

# The engine-boundary probe's counters -- plan_entity.md Phase 0 step 0e.
#
# ENGINE-FREE, which is why it is an ordinary build_and_run: the probe header
# takes nothing from Valve, deliberately, so the thing docs/engine-boundary.md
# rests on can be checked without a running game. -ldl is for dladdr, which is
# how a recorded return address becomes "module+0xOFFSET" -- the form the
# probe prints.
ss_cpptest(boundary_probe_test
    INCLUDES sourcesharp/game/shared
    SOURCES sourcesharp/test/boundary_probe_test.cpp
    LIBS dl)

# plan_entity.md Phase 0b. The gates tool is a shell script too, so like the
# census leg this one RUNS it rather than compiling it: against today's binaries
# (where all three gates must FAIL, because the definition of done is the end
# state), against a fixture whose every count is zero (where they must PASS --
# otherwise the pass path is never executed on any tree that exists), and
# against a fixture with two cast sites, one allowlisted and one not.
ss_cpptest(entity_gates_test
    LINUX_ONLY "runs the tree's bash and Python instruments (tools/) and reads their exit status"
    SOURCES sourcesharp/test/entity_gates_test.cpp)

# relink_exit_contract_test STOOD HERE and went with its two subjects.
# `tools/client-flip-relink` and `src/p4-exit-relink` answered "does the project
# still LINK with this set of objects removed", and the removal set is gone:
# ROUTE_TO_DONE §5 names both for deletion "with their gtests ... not left as
# green legs that measure nothing". The finding they carried -- a `set -e`
# inherited from a sourced helper killing a script before it writes its verdict
# line, the 45th check-that-cannot-fail on this port -- is recorded in the
# close's handover, because the lesson outlives the tool.

# plan_entity.md Phase 1 step 1c -- THE SLOT TABLE (§3.3).
#
# Engine-free, like every other leg here, and it compiles against the same four
# Valve headers: sourcesharp/test/entitylist_stub supplies the one-TU cbase.h,
# and -Ipublic gets basehandle.h and const.h. The two -Wno- flags are for
# Valve's headers -- tier0/platform.h
# carries MSVC #pragma warning lines and a braceless if in
# DECLARE_ALIGNED_BYTE_ARRAY -- and the leg still runs at zero warnings of its
# own.
ss_cpptest(slot_table_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
    SOURCES sourcesharp/test/slot_table_test.cpp)

# THE TABLE CANNOT ITERATE, and that is a property of the BUILD.
#
# plan_entity.md's definition of done, clause 2: no native entity list survives
# the port. A table that can be walked is one. Nothing gtest can assert says
# that -- every assertion about a member that does not exist passes whether or
# not the member is absent -- so the five spellings anybody would actually reach
# for are compiled as five ill-formed programs and each is required to be
# REFUSED, at its own error rather than at a typo.
#
# Adding any of the five to CSourceSharpSlotTable turns the matching leg red.
# That is the whole mechanism: mutation-verified by adding ActiveCount/ActiveAt
# to the header and watching probe 2 report "COMPILED".
ss_must_not_compile(slot_table_no_rangefor
    EXPECT "'begin' was not declared in this scope|no viable 'begin' function available"
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
            SLOT_TABLE_NOCOMPILE=1
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
    SOURCES sourcesharp/test/slot_table_test.cpp)

ss_must_not_compile(slot_table_no_activelist
    EXPECT "no member named 'ActiveCount'"
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
            SLOT_TABLE_NOCOMPILE=2
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
    SOURCES sourcesharp/test/slot_table_test.cpp)

ss_must_not_compile(slot_table_no_storage
    EXPECT "no member named 'Slots'"
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
            SLOT_TABLE_NOCOMPILE=3
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
    SOURCES sourcesharp/test/slot_table_test.cpp)

ss_must_not_compile(slot_table_no_cursor
    EXPECT "no member named 'FirstHandle'"
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
            SLOT_TABLE_NOCOMPILE=4
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
    SOURCES sourcesharp/test/slot_table_test.cpp)

ss_must_not_compile(slot_table_no_foreach
    EXPECT "no member named 'ForEach'"
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
            SLOT_TABLE_NOCOMPILE=5
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
    SOURCES sourcesharp/test/slot_table_test.cpp)

# THE ENGINE BLOCK, FIELD BY FIELD (plan_entity.md §3.2, Phase 1a).
#
# The block is declared twice -- SourceSharp.Abi/EngineBlock.cs and
# sourcesharp/game/shared/engine_block.h -- because a shell getter has to be one
# load at a constant offset and managed code has to write the value that load
# reads. §7 names that as a drift risk and asks for three checks. This is the
# native one: one EXPECT_EQ per field against offsetof, plus the two questions
# only this side can answer -- whether the room the C# block reserves for
# PVSInfo_t and CPlayerState is still big enough for the engine's real structs.
#
# mathlib, because the block holds Vector, QAngle and matrix3x4_t: the whole
# point of using the engine's own types here is that 1b's getters can return a
# reference straight out of the block.
#
# GAME_DLL, because CPlayerState is a DIFFERENT SIZE on the two sides --
# PlayerState.h puts netname, fixangle, anglechange, hltv, replay, frags and
# deaths behind `#if !defined( CLIENT_DLL )`. The server's is the larger and the
# one the reservation has to hold, so the server's is what is measured.
ss_cpptest_mathlib(engine_block_layout_test
    DEFINES GAME_DLL
    INCLUDES sourcesharp/game/shared
    SOURCES sourcesharp/test/engine_block_layout_test.cpp)

# plan_entity.md step 1d -- the shim entity-list sweep, and the two resolvers it
# added. The census is the step's instrument and this is the step's REASON: a
# count says how many entity-list calls are left, never which, so a call added
# back into a file that already has one moves the total by one and reads exactly
# like a site legitimately still there.
#
# Source-read and it is forced: all four subjects include cbase.h's whole world
# and none of them can ever be linked into a gtest. Appended at the END, as the
# lane brief directs, so a merge with a concurrent lane keeps both sides.
ss_cpptest(entity_list_sweep_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/entity_list_sweep_test.cpp)

# plan_entity.md Phase 1 step 1h -- PHYSICS GAME DATA IS THE HANDLE (§3.3).
#
# Engine-free for the same reason slot_table_test is: physics_gamedata.h is
# arithmetic over a CBaseHandle and the slot table is an array. Same four Valve
# headers through sourcesharp/test/entitylist_stub, and the same two -Wno- flags
# for tier0/platform.h's MSVC #pragma warning lines and braceless if.
#
# Run from src/, so the source-scanning facts at the bottom of the file find the
# tree through SrcRoot()'s "." candidate -- the same way entity_gates_test and
# cpp_alias_lane1_test find theirs.
# THE CLIENT ARM OF physicsowner.h DECODES A MANAGED OWNER (plan_client_novalve
# C6, cluster 9; lane c6-input). Built with CLIENT_DLL so the client arm is the
# one compiled; physicsowner_client_stub stands in for c_baseentity.h, which
# that arm includes and names nothing in.
ss_cpptest(physicsowner_client_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC CLIENT_DLL
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-nontrivial-memcall  # vphysics_interface.h:1047 (Valve)
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/physicsowner_client_stub ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 public/mathlib common
    SOURCES sourcesharp/test/physicsowner_client_test.cpp)

ss_cpptest(physics_gamedata_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
    SOURCES sourcesharp/test/physics_gamedata_test.cpp)

# physics_wake_sleep_test WAS HERE (lane phys-wakesleep: Wake and Sleep are
# simulation state, NOT motion enable). It scanned the two shell entry points;
# they left with the last thin vphysics forwards (2026-10-12), and the fact is
# managed's now: EntityPhysicsMotionTests.WakeAndSleepAreOneCallEach and
# DisablingMotionLeavesItAsleepOrAwake.

# plan_entity.md step 1e -- TYPED HANDLES OUT OF THE SHIM, and the half of that
# gate a text census cannot take.
#
# The census says the typed handles are gone TODAY. These three legs say they
# cannot come back: sourcesharp/game/server/standalone_handle_ban.h declares an
# explicit specialisation of CHandle over the shell and never defines it, so an
# object of that type is an incomplete type and the program is refused.
#
# THREE LEGS AND NONE OF THEM IS SUFFICIENT ALONE.
#
#   ..._test    the facts around the ban: that it is a DECLARATION and not a
#               defined (and therefore instantiable) specialisation, that
#               shell_standalone.h reaches it, that a pointer to the banned type
#               is still nameable, and that the two legs below both still exist.
#   ...ban      the ill-formed program. Must NOT compile, at "incomplete type".
#   ..._absent  THE CONTROL: the SAME source, the same NOCOMPILE define, and one
#               fewer #include. Must compile and run. Without it the refusal
#               above is evidence of nothing -- a typo refuses just as loudly --
#               and the control is the leg that would be dropped silently,
#               because deleting it breaks nothing that prints.
#
# Engine-free through sourcesharp/test/entitylist_stub's one-TU cbase.h, the
# same route slot_table_test takes, and it is the REAL game/shared/ehandle.h
# that is banned rather than a stand-in for it. The two -Wno- flags are that
# leg's, for its reason (tier0/platform.h's MSVC pragmas and a braceless if).
#
# Appended at the END, as the lane brief directs, so a merge with a concurrent
# lane keeps both sides.
# (SS_1E_BAN_FLAGS, which the three checks below shared, is written out in each.)

# clang: -Wno-unused-variable for Valve's public/tier1/utlblockmemory.h:350
# (an unused local in a template gcc 10 never instantiated), on a plain -I here.
ss_cpptest(standalone_handle_ban_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
             game/shared sourcesharp/game/server
    SOURCES sourcesharp/test/standalone_handle_ban_test.cpp)

ss_must_not_compile(standalone_handle_ban
    EXPECT "CHandle<CStandaloneEntity>.*incomplete type|incomplete type 'CHandle<CStandaloneEntity>'"
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
            STANDALONE_HANDLE_BAN_NOCOMPILE=1
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
             game/shared sourcesharp/game/server
    SOURCES sourcesharp/test/standalone_handle_ban_test.cpp)

# clang: -Wno-unused-variable for Valve's public/tier1/utlblockmemory.h:350
# (an unused local in a template gcc 10 never instantiated), on a plain -I here.
ss_cpptest(standalone_handle_ban_absent
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
            STANDALONE_HANDLE_BAN_NOCOMPILE=1 STANDALONE_HANDLE_BAN_ABSENT=1
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
             game/shared sourcesharp/game/server
    SOURCES sourcesharp/test/standalone_handle_ban_test.cpp)

# plan_entity.md step 1e -- THE SWEEP'S LEDGER.
#
# Source-read and it is forced: every subject includes cbase.h's whole world.
# It walks the two shim directories off the FILESYSTEM rather than off a list of
# file names, so the fact cannot be made to pass by shortening a ledger, and it
# carries its own matcher tests -- every fact in it is EXPECT_EQ against zero,
# and a matcher that answered zero for everything would make all of them pass on
# a tree nobody had swept.
ss_cpptest(typed_handle_sweep_test
    SOURCES sourcesharp/test/typed_handle_sweep_test.cpp)

# plan_entity.md Phase 3 step 3a -- THE PHYSICS SHELL (§3.8).
#
# Engine-free for the reason physics_gamedata_test is: everything above
# physics_shell_test (the event ring, its drain and the event record's layout)
# is gone with the ring (plan_physics_bones_native_zero 2.3): PhysicsFrame posts
# the step's events to managed's router with no queue and no crossing.

# plan_entity.md Phase 5a -- THE CLIENT ARM OF THE SLOT TABLE (§3.3).
#
# -DCLIENT_DLL, which is the point rather than a detail: slot_table.h splits its
# declarations per side, and the must-not-compile leg below asks for a bind arm
# that exists on the SERVER. Compiled without CLIENT_DLL the refusal would be
# the server block being hidden rather than the client surface not having it,
# and the leg would pass for the wrong reason.
#
# Otherwise the same engine-free route slot_table_test takes:
# sourcesharp/test/entitylist_stub is the one-TU cbase.h, -Ipublic gets
# basehandle.h and const.h, and the two -Wno- flags are that leg's, for its
# reason (tier0/platform.h's MSVC pragmas and a braceless if).
#
# Appended at the END, as the lane brief directs, so a merge with a concurrent
# lane keeps both sides.
# (SS_5A_FLAGS, which the three checks below shared, is written out in each.)

ss_cpptest(client_slot_table_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC CLIENT_DLL
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
             sourcesharp/game/shared
    SOURCES sourcesharp/test/client_slot_table_test.cpp)

# THE CLIENT CANNOT ISSUE ITS OWN SERIALS, and that is a property of the BUILD.
#
# §3.3: the server assigns a networked entity's handle and the client is told
# what it is. A client-side Bind() that chose a serial would produce handles the
# other machine never assigned, and a handle travels -- an EHANDLE prop puts one
# on the wire. So the arm is not declared, and this is what says so: adding a
# SourceSharp_ClientSlotBind to slot_table.h turns this leg red.
#
# The CONTROL is the leg above: the same translation unit, the same defines, the
# probe switched off, sixteen facts compiled and run. So a refusal here
# cannot be a missing include or a typo refusing everything.
ss_must_not_compile(client_slot_table_no_own_serial
    EXPECT "'SourceSharp_ClientSlotBind' was not declared in this scope|use of undeclared identifier 'SourceSharp_ClientSlotBind'"
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC CLIENT_DLL
            CLIENT_SLOT_TABLE_NOCOMPILE=1
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
             sourcesharp/game/shared
    SOURCES sourcesharp/test/client_slot_table_test.cpp)

# The IBaseClientDLL shell's ARRANGEMENT, and the document its boundary capture
# is recorded in.
#
# This was clientdll_probe_test.cpp and linked -ldl for dladdr, reached through
# the probe's CBoundaryProbe::DescribeAddress. Lane 5b deleted the probe, so
# there is no header to include and no dladdr to link: every fact here reads
# shell_clientdll.cpp, client_csharp.vpc, public/cdll_int.h and
# docs/clientdll-boundary.md off the filesystem. That is the half no unit test
# can reach -- shell_clientdll.cpp includes cbase.h's whole world and can never
# be linked into a gtest.
ss_cpptest(shell_clientdll_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/shell_clientdll_test.cpp)

# THE CLIENT'S REFUSAL COUNTER -- plan_entity.md 5b, and the client side of
# shell_gamedll_refusals_test above.
#
# It is a second binary rather than more facts in that one because the two
# counters are two classes in two headers, one per side, and neither side's
# include path can see the other's. The client's lives in
# sourcesharp/game/client/ and the server's in sourcesharp/game/server/; the
# duplication is FLAGGED for the integrator (.claude/entity-port/requests/5b.md)
# rather than resolved by a lane that does not own the server's header.
#
# ENGINE-FREE, which is why it is an ordinary build_and_run: the counter takes
# nothing from Valve, deliberately, so the mechanism the gate is read out of can
# be checked without a running game. The warning half is DECLARED by the header
# and defined by whoever links it -- tier0's Warning() in shell_clientdll.cpp, a
# recorder here -- which is the seam that lets the SHIPPED class be the thing
# under test rather than a transcription of it.
ss_cpptest(shell_clientdll_refusals_test
    INCLUDES sourcesharp/game/client
    SOURCES sourcesharp/test/shell_clientdll_refusals_test.cpp)

# THE DETAIL PROPS' WIRE (C7 cluster 10): detail_props_wire.h, the readers
# shell_detail_props.cpp carries the managed detail props' answers out with.
# No SDK header, so the shipped reader is the thing under test.
ss_cpptest(detail_props_wire_test
    INCLUDES sourcesharp/game/client
    SOURCES sourcesharp/test/detail_props_wire_test.cpp)

# THE CLIENT FLIP'S RULING TABLE AND ITS REFUSAL COUNTER -- CLIENT_FLIP lane 2
# (`c8-entitystatics`), plan_entity.md §7.
#
# The same shape and the same seam as the refusals test above: the counter takes
# nothing from Valve, so the mechanism a §7 declaration is read out of can be
# checked without a running game, and ShellEntityStatics_RefusalWarning is
# DECLARED by the header and defined by whoever links it -- tier0's Warning() in
# shell_entitystatics.cpp, a recorder here.
#
# ONE DIFFERENCE, AND IT IS THE POINT. The RULING TABLE lives in the header
# rather than in the .cpp, so this reads the rows the game ships instead of a
# transcription of them. A transcription is a copy that drifts, which is exactly
# how a declared refusal quietly stops matching the code it declares.
#
# It also source-reads shell_clientdll.cpp, the two translation units the flip
# deletes, and game/server/player.cpp -- the arrangement half, which cannot be
# linked because those files include cbase.h's whole world.
ss_cpptest(shell_entitystatics_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    INCLUDES sourcesharp/game/client
    SOURCES sourcesharp/test/shell_entitystatics_test.cpp)

# THE NOT-SHIPPED LEDGER -- plan_entity.md step 7g.
#
# The same shape one level up: the refusals counter above is per IBaseClientDLL
# METHOD, this one is per CAPABILITY and per INTERFACE NAME, and it is what 7g's
# gate ("each interface answered or refused BY NAME") is read out of. Engine-free
# for the same reason and with the same seam -- ShellNotShipped_Warning is
# declared by the header and defined by tier0's Msg path in shell_diagnostics.cpp
# or by a recorder here -- so the SHIPPED class and the SHIPPED interface list
# are what run, not a transcription.
ss_cpptest(shell_notshipped_test
    INCLUDES sourcesharp/game/shared
    SOURCES sourcesharp/test/shell_notshipped_test.cpp)

# THE ENTITY OP TABLE -- plan_entity.md §3.4, step 1f.
#
# The op number crosses the ABI as a bare int32_t AND is a bit position in the
# per-class override mask, so it is pinned as literals on BOTH sides and the two
# pin lists are cross-read: this binary parses SourceSharp.Abi/EntityOp.cs and
# compares op for op, because a pin on one side alone cannot see a renumbering
# on the other.
#
# It also reads shell_standalone_scripted.cpp and asserts that every relay tests
# its own override bit before crossing. That fact has NO RUNTIME SYMPTOM: a
# relay that dropped its guard would still be correct -- managed code would get
# an op it does nothing with -- so every functional test and the live gate would
# pass, and the only trace would be a crossing count that quietly went back up.
#
# ENGINE-FREE, which is why it is an ordinary build_and_run: entity_op.h takes
# nothing from Valve, and sourcesharp.h deliberately knows nothing about Source,
# so the layout of SourceSharpOpArgs can be checked with offsetof here rather
# than being taken on trust from the far side's marshaller.
ss_cpptest(entity_op_test
    INCLUDES sourcesharp/game/shared
    SOURCES sourcesharp/test/entity_op_test.cpp)

# THE ABI SLOTS THAT ANSWER BY DOING NOTHING (shell_entityapi.cpp).
#
# Lane 2e-abi found ScriptedEntity.PlaySequence -- every dynamic prop's sequence
# call -- still routed through a slot Phase 2 had nulled. It returned false for
# every prop on every map, silently; no test covered it and the live gate passed
# over it twice. A slot that does nothing is indistinguishable at every seam
# from a slot with nothing to do, which is why only an INVENTORY catches this.
#
# shell_entityapi.cpp holds 28 more of the same shape, and 20 of them have a
# live managed rule calling them -- armour, func_friction, trigger_camera,
# player_speedmod, game_score. docs/entity-shell.md section 2 names the rule and
# the consequence for each. They are not retirements: a caller that wants an
# answer is a requirement, not a finished job.
#
# So this DERIVES the stub set from the source -- a body is a stub when, with
# comments stripped, nothing in it reaches outside its own arguments: no `->`,
# no `::`, no call -- and fails in BOTH directions against the recorded list.
# The four checks in front of the inventory are what stop it being a check that
# cannot fail: the file has to be found, the definition finder has to find the
# whole file, and the classifier is shown a known-live body and a known-stub
# body and has to tell them apart. Without those, two empty sets compare equal.
#
# Header-free and engine-free: it reads shell_entityapi.cpp as TEXT and compiles
# none of it, so it needs no SDK prelude and cannot be made to pass by changing
# a header the file includes.
ss_cpptest(entity_shell_inert_slots_test
    SOURCES sourcesharp/test/entity_shell_inert_slots_test.cpp)

# THE CLIENT'S RUNTIME ADAPTER, RUN RATHER THAN READ -- the mirror of
# managed_runtime_server_test above, and the client half of the arrangement it
# pins.
#
# It compiles the SHIPPED header (sourcesharp/game/client/managed_runtime_client.h)
# against a managed table it rewrites underneath a live adapter, which is the
# only proof of the property no reading can establish: that the adapter holds no
# COPY of the table. A copy taken at construction compiles, links, passes every
# behavioural test, and breaks on the first cs_cl_reload with function pointers
# into an unloaded assembly.
#
# Plus the source half, which is this file's own: that CShellClientDLL loads the
# runtime by name BEFORE gHUD.Init() and before the client-systems walk,
# releases it by name in Shutdown in the server's stop/walk/release order, and
# that the game-system row does neither. Before this lane the client had none of
# it -- IManagedRuntimeClient had no methods and its LoadManagedRuntime() was
# declared and defined nowhere -- so there was nothing for a test to name.
#
# -I for the ABI header and for sourcesharp_gamesystem.h, which is where
# IClientRuntimeAdapter and IManagedRuntimeEnvironment are declared. Engine
# headers since plan_client_vtables CE1: the adapter forwards into the generated
# imanaged_runtime_client.h, which needs tier0/platform.h (as managed_accept_test).
ss_cpptest_engine_headers(managed_runtime_client_test
    INCLUDES ${PROJECT_SOURCE_DIR}/src/host/native sourcesharp/game/shared
    SOURCES sourcesharp/test/managed_runtime_client_test.cpp)

# THE CLIENT SHELL READS THE ENGINE BLOCK -- plan_entity.md 5c, and the client
# half of standalone_layout_test above.
#
# Sixteen members left C_StandaloneEntity for the block: the seven head values
# the engine's DECODER now writes (the recv props address the block directly)
# and the nine render-tail values this side computes. Nothing in a build can see
# a member and a block field holding the same value -- both compile, both are
# read, and the game looks right until they disagree -- so the checks are
# textual, exactly as the server's are.
#
# It also pins the ONE place the two sides deliberately differ: the server's
# block is the blob's prefix and this one is a member of the shell, because the
# blob's first 352 bytes on this side are the SERVER's block arriving as payload
# words, and its first four are the server's CBaseHandle. The floor in
# operator new is asserted to agree with that placement, so changing one alone
# is red rather than a 320-byte overrun.
ss_cpptest(standalone_client_layout_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/standalone_client_layout_test.cpp)

# CLIENT INTERPOLATION IS MANAGED CODE -- plan_entity.md 6a.
#
# CInterpolatedVar is 1591 lines of template that every standalone client entity
# carried two instantiations of. The history and the blend are managed rings now;
# what is left native is the LIST the pass walks and the after-effects of a pose
# changing (the transform rebuild and the leaf-system notify, both engine calls).
#
# Textual, for 1b's and 5c's reason: nothing in a build can see two
# implementations of one algorithm, both compiled and both reachable. A native
# Interpolate that quietly kept blending would fight the managed one on alternate
# frames and the game would look nearly right.
#
# Whether the managed curve IS the engine's curve is not this file's question --
# that is SourceSharp.Tests/InterpolationTests, replaying a recording of the C++
# this removes against it bit for bit.
ss_cpptest(client_interpolation_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/client_interpolation_test.cpp)

# Where a parented child ends up (parent_compose_cases.txt). plan_entity.md step
# 2h moved parenting's arithmetic to C#, and the golden table the managed test
# reads is only worth anything if something still pins it to the mathlib it was
# taken from. A table checked only by the side that reads it is a fixture
# agreeing with itself -- see docs/writing-tests.md and the header of the test
# file.
#
# NOT template_transform_test's table. That one records VMatrix; parenting never
# touched a VMatrix, it composed matrix3x4_t through AngleMatrix, MatrixInvert,
# ConcatTransforms and MatrixAngles -- so this table is taken from those calls.
ss_cpptest_mathlib(parent_compose_test
    SOURCES sourcesharp/test/parent_compose_test.cpp)

# plan_entity.md step 5d -- TYPED HANDLES OUT OF THE CLIENT SHIM, which is 1e's
# gate on the other side, and the half of it a text census cannot take.
#
# The census says the typed handles are gone from the compiled client shim TUs
# TODAY. These three legs say they cannot come back:
# sourcesharp/game/client/standalone_handle_ban_client.h declares an explicit
# specialisation of CHandle over the CLIENT shell and never defines it, so an
# object of that type is an incomplete type and the program is refused.
#
# WHY A SECOND BAN AND NOT A REUSE OF 1e's. The two shells are two classes --
# CStandaloneEntity and C_StandaloneEntity -- and an explicit specialisation has
# to name the type it specialises. A shared header would be a ban on nothing.
# standalone_handle_ban_client_test has a fact for exactly that mistake.
#
# AND THE CLIENT HAZARD IS THE WORSE OF THE TWO. Since 5a, CHandle<T>::Get()
# resolves through ClientEntityList(), which IS the slot table, and whose two
# primitives answer for BOTH a C_StandaloneEntity shell and a stock client
# entity out of the port seam. So a typed handle here does not fail to resolve:
# it resolves, to whichever kind the entry holds, and applies (T*) to the
# answer. beam_endpoint.h recorded the live client that killed (the route is
# BeamEndpoint.cs's now).
#
# THREE LEGS AND NONE OF THEM IS SUFFICIENT ALONE.
#
#   ..._test         the facts around the ban: that it is a DECLARATION and not
#                    a defined (and therefore instantiable) specialisation, that
#                    shell_standalone_client.h reaches it, that it is the
#                    CLIENT's own and not a merge of the server's, and that the
#                    two legs below both still exist.
#   ...ban_client    the ill-formed program. Must NOT compile, at "incomplete
#                    type".
#   ..._absent       THE CONTROL: the SAME source, the same NOCOMPILE define,
#                    and one fewer #include. Must compile and run. Without it
#                    the refusal above is evidence of nothing -- a typo refuses
#                    just as loudly -- and the control is the leg that would be
#                    dropped silently, because deleting it breaks nothing that
#                    prints.
#
# Engine-free through sourcesharp/test/entitylist_stub's one-TU cbase.h, the
# same route 1e's server twin takes, and it is the REAL game/shared/ehandle.h
# that is banned rather than a stand-in for it. The two -Wno- flags are that
# leg's, for its reason (tier0/platform.h's MSVC pragmas and a braceless if).
#
# Appended at the END, as the lane brief directs, so a merge with a concurrent
# lane keeps both sides.
# (SS_5D_BAN_FLAGS, which the three checks below shared, is written out in each.)

# clang: -Wno-unused-variable for Valve's public/tier1/utlblockmemory.h:350
# (an unused local in a template gcc 10 never instantiated), on a plain -I here.
ss_cpptest(standalone_handle_ban_client_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
             game/shared sourcesharp/game/client
    SOURCES sourcesharp/test/standalone_handle_ban_client_test.cpp)

ss_must_not_compile(standalone_handle_ban_client
    EXPECT "CHandle<C_StandaloneEntity>.*incomplete type|incomplete type 'CHandle<C_StandaloneEntity>'"
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
            STANDALONE_HANDLE_BAN_CLIENT_NOCOMPILE=1
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
             game/shared sourcesharp/game/client
    SOURCES sourcesharp/test/standalone_handle_ban_client_test.cpp)

# clang: -Wno-unused-variable for Valve's public/tier1/utlblockmemory.h:350
# (an unused local in a template gcc 10 never instantiated), on a plain -I here.
ss_cpptest(standalone_handle_ban_client_absent
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
            STANDALONE_HANDLE_BAN_CLIENT_NOCOMPILE=1
            STANDALONE_HANDLE_BAN_CLIENT_ABSENT=1
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
             game/shared sourcesharp/game/client
    SOURCES sourcesharp/test/standalone_handle_ban_client_test.cpp)

# plan_entity.md step 5d -- THE CLIENT SWEEP'S LEDGER.
#
# Source-read, and it is forced: every subject includes cbase.h's whole client
# world and none of them can be linked into a gtest. The same instrument 1e's
# typed_handle_sweep_test.cpp is, over sourcesharp/game/client rather than over
# the server and shared directories.
ss_cpptest(client_typed_handle_sweep_test
    SOURCES sourcesharp/test/client_typed_handle_sweep_test.cpp)

# That the native policy parenting used to hold is GONE rather than standing
# beside the managed one (plan_entity.md step 2h made live). Every fact in it is
# about code that does not exist, which is the one thing no behavioural test can
# see: a second composer would AGREE with the first -- it is a port of it -- and
# the game would look correct until the day the two disagreed about one link.
ss_cpptest(hierarchy_managed_test
    SOURCES sourcesharp/test/hierarchy_managed_test.cpp)

# physics_ids_test (step 3b's physics id table) is gone with the table
# (plan_physics_bones_native_zero 2.3): no native producer asked it for an id;
# managed's PhysicsId is the scheme its own joint table hands out.

# plan_entity.md step 3c -- THE PLAYER'S STATE IS IN THE BLOCK, and
# GetPlayerState may not answer NULL.
#
# Two halves. The refusal counter is RUN: CShellPlayerInfoRefusals is engine-free
# playerinfo_block_test went with shell_playerinfo.cpp (plan_server_native_min
# P1, Decision 1): the refusal seam it recorded is deleted with the interface
# no engine binary asks for.

# tf2 stage 0.5 -- THE PLAYER'S TEAM IS ONE NATIVE STORE (player_team_store_test.cpp).
# PlayerSetTeam writes CStandalonePlayer::m_iTeamNum; SourceSharp_GetPlayerTeam
# and IPlayerInfo::GetTeamIndex read it back. Both readers answered a flat 0
# before 0.5, and a revert compiles, links and passes every managed fact (they
# run over a double). Source-read with comments stripped, like
# playerinfo_block_test: the bodies hang off engine state no test binary links.
ss_cpptest(player_team_store_test
    SOURCES sourcesharp/test/player_team_store_test.cpp)

# plan_entity.md step 3g -- THE RULES CALLERS. Two facts families in one file:
# cast family 5 has left shell_gamerules.cpp (§3.16's own gate for this step),
# and 3a's level-init collision-rule bridge is deleted rather than moved onto
# the evaluation path. Source-read because both are SHAPES -- a re-added
# downcast compiles, and a re-added ask gives the right answer at a virtual call
# per pair per step.
ss_cpptest(rules_callers_test
    SOURCES sourcesharp/test/rules_callers_test.cpp)

# ragdoll_ids_test left with the native body table (plan_physics_bones_native_zero Phase 3):
# the client's bodies are SourceSharp.Client's ClientRagdolls, keyed by owner handle.


# plan_entity.md Phase 6 step 6e -- THE SHAPE OF THE CREATION, read as text.
# "A flat function over a bone snapshot returning ids" is three properties of
# SOURCE, and none of them can be asserted from inside a running program: a
# refactor that left the old entity-shaped path standing beside the new one
# passes every behavioural test there is.
#
# EVERY SCAN IN IT STRIPS COMMENTS FIRST, which 67 of this tree's 75
# source-reading test files do not, and the header it reads explains at length
# what it does not name.
#
# Appended at the END, as the lane brief directs.

ss_cpptest(anim_cluster_ledger_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/anim_cluster_ledger_test.cpp)

# The usercmd codec is C# on both sides (usercmd_codec_test.cpp): no DLL
# compiles a usercmd.cpp, and the client's IInput codec slots cross to managed
# (plan_server_novalve P2.2, plan_client_novalve C9).
ss_cpptest(usercmd_codec_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/usercmd_codec_test.cpp)

# plan_shim.md Phase 2d -- ANIMATION EVENTS BY NAME, NOT BY ORDINAL
# (anim_event_names_test.cpp). precache_model.cpp used to identify new-system
# events by a number the per-level event REGISTRY wrote into mdl memory,
# matching the enum only because the registry was filled in enum order. That
# is the whole reason the registry (shell_animlists.cpp + anim_name_registry.h)
# and the managed feed behind it had to exist.
#
# anim_event_names.h holds the five rows native code reads, and a copy of five
# rows of a table is a thing that DRIFTS -- so the agreement is asserted in
# both directions: the two type bits are read out of eventlist.h's text, and
# every name and client/server bit is checked against
# SourceSharp.Abi/EventCatalog.cs, which is where the table really lives.
#
# No engine, no tier1: the header includes nothing, in bone_cache_key.h's
# tradition, which is what lets this binary exist at all.
#
# Appended at the END, as the lane brief directs.
ss_cpptest(anim_event_names_test
    SOURCES sourcesharp/test/anim_event_names_test.cpp)

# model_descriptor_format_test.cpp went 2026-10-18 with both halves it
# compared: the descriptor is read by the managed StudioModelDescriptor, and
# neither model_descriptor.cpp's walk nor ModelDescriptor.Decode is left.
# plan_entity.md Phase 7 step 7b -- THREE STOCK TEMP-ENTITY TUs LEAVE THE CLIENT
# (tempent_evicted_test.cpp). c_basetempentity.cpp, c_te_particlesystem.cpp and
# c_te_explosion.cpp are out of client_csharp.vpc.
#
# Source-read, and it has to be: not one of the three claims under the eviction
# is visible to a linker. That nothing derives from C_BaseTempEntity any more is
# a class derivation, which emits no symbol; that CTEParticleRenderer MOVED
# rather than being rewritten is a comparison of two files; and that stock's
# blast arithmetic was not TRANSCRIBED into the shim is an absence in a file the
# build never reads. A build that compiles is what an eviction always produces.
#
# The comment stripper is load-bearing rather than tidy -- shell_clientdll.cpp
# keeps four comments naming the calls it lost -- and TheStripperIsLoadBearing
# asserts the raw text still carries what the stripped text may not, so a
# stripper that returned nothing fails instead of passing everything.
ss_cpptest(tempent_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/tempent_evicted_test.cpp)

# plan_entity.md Phase 7 step 7a -- FOUR STOCK EFFECT TUs LEAVE THE CLIENT
# (effect_tus_evicted_test.cpp). c_env_fog_controller.cpp, c_func_dust.cpp,
# c_sprite.cpp and c_impact_effects.cpp are out of client_csharp.vpc, and the
# leaf renderers they held are in shell_sprite_leaf.cpp,
# shell_impact_effects.cpp and shell_dust_render.cpp.
#
# Source-read for the same reason 7b's is, and for one worse one this step met
# in person: c_sprite.cpp was first rewritten with GetSpriteAxes and DrawSprite
# left in it AS WELL AS in the shim, and the build was green -- because the file
# is no longer compiled, so the linker never saw the second copy. The
# column-zero-brace check on each evicted file's compiled arm is what caught it.
#
# It also checks the GATE'S instrument: every counter shell_effect_ledger.h
# declares must be incremented somewhere and printed by cs_cl_effects_report,
# because a counter nobody bumps reads zero forever and tools/7a-effect-gate
# would read that as a measured absence.
ss_cpptest(effect_tus_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/effect_tus_evicted_test.cpp)

# plan_entity.md §3.8, lane p4-physics -- IS THE PHYSICS EVENT RING'S SINK
# ACTUALLY BOUND? (physics_event_sink_test.cpp)
#
# Step 3a's ring was drained into a NULL sink from the day it was built:
# SetSink's only caller in the tree was physics_shell_test.cpp, which binds a
# recording sink of its own before every push -- so every gtest over the ring
# was green for a mechanism that reached nobody. Measured live on dm_lockdown
# before the fix: high water 263 events, drain crossings 0, handed on 0.
#
# Source-read, and it is forced: the bind is one line in a shim that includes
# cbase.h's whole world, and the drain runs inside a physics frame. What can be
# checked is that a production binder exists, that it is exactly one, that it is
# the SHIM and not the stock file that is on its way out of the vpc, and that
# what it binds forwards to the ABI slot.
#
# Comment-stripped for 7b's reason twice over: the test's own explanation names
# SetSink and PhysicsEvents repeatedly, and so do several of the files it scans.
#
# Appended at the END, as the lane brief directs, so a merge with a concurrent
# lane keeps both sides.
ss_cpptest(physics_event_sink_test
    SOURCES sourcesharp/test/physics_event_sink_test.cpp)
# exit_batches_test STOOD HERE and went with tools/p4-exit-batches, which
# answered which stock TUs were mutually locked -- a question about a removal
# set that no longer exists (ROUTE_TO_DONE §5). component_closure_test and
# tools/4f-component, which this used to point at as its model, STAY.

# plan_entity.md Phase 4 EXIT. UpdateAllClientData walked UTIL_PlayerByIndex,
# which cannot answer in this build, so its BODY never ran: the 3d skybox never
# reached a client and m_skybox3d.area stayed 0 where stock says 255. Proved live
# A/B with tools/p4-exit-probe-skybox (skyarea 0 -> 255). TEXT ONLY: this file
# pins the four things an edit could quietly undo, plus the PREMISE.
ss_cpptest(client_data_walk_test
    SOURCES sourcesharp/test/client_data_walk_test.cpp)

# plan_entity.md Phase 4 EXIT. The first two stock TUs of the component region to
# leave server_csharp.vpc at all: movehelper_server.cpp (evicted, left on disk)
# and playerlocaldata.cpp (moved to the shim). TEXT ONLY -- the subject is which
# vpc compiles what, and the claim that made the eviction safe.
ss_cpptest(server_tus_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/server_tus_evicted_test.cpp)

# plan_entity.md Phase 4 EXIT. The exit's unit of work is the CYCLE in the stock
# link graph -- the set of stock TUs that must leave server_csharp.vpc in one
# commit because each of them needs a symbol another of them defines. Two
# relocations made that cycle smaller (31 TUs / 49,118 lines -> 27 / 47,191),
# and these two files pin them. TEXT ONLY: the subject is which file holds which
# definition and which vpcs compile it, so there is nothing here to link.
ss_cpptest(admin_command_move_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/admin_command_move_test.cpp)

ss_cpptest(shared_random_move_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/shared_random_move_test.cpp)

# (entity_base_cluster_test.cpp): SCC 11 cluster A -- the entity base itself,
# and the standing rule that every removal here names what the base was DOING.
# Eight silent losses in this port were all the same shape: something CBaseEntity
# did FOR an entity that is no longer under it. TEXT ONLY: the subject is which
# file holds which definition, which guard it sits behind, and which managed
# class answers the spelling it retired, so there is nothing here to link.
ss_cpptest(entity_base_cluster_test
    SOURCES sourcesharp/test/entity_base_cluster_test.cpp)
# plan_entity.md step 8e-physics's ask ledger and its gtest left with native's
# collision handler (plan_physics_bones_native_zero 2.1): the answers are
# managed's PhysicsSolver, counted by clause there.

# ---------------------------------------------------------------------------
# lane p4-exit: game/server/client.cpp leaves server_csharp.vpc. The enumeration
# of what its six self-registrations became, and the guard-aware scan that says
# `tools/4a-owes client` reading 0 was never evidence about a ConVar.
# ---------------------------------------------------------------------------
ss_cpptest(client_tu_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/client_tu_evicted_test.cpp)


# standalone_solidity_order_test (lane physics-defects, solidity before the
# physics object) WENT WITH ITS SUBJECT (2026-10-12): CStandaloneEntity::
# InitPhysics is managed's EntityPhysicsCreation, whose order
# EntityPhysicsCreationTests holds in both arms of
# sourcesharp_solidity_before_physics.

# ---------------------------------------------------------------------------
# lane p4-flip -- WHAT THE NATIVE MAP PARSE STILL MAKES.
#
# Pure source scan over server_csharp.vpc and the files it names; no engine, no
# link. Appended at the END, as the lane brief directs, so a merge with a
# concurrent lane keeps both sides.
# ---------------------------------------------------------------------------
ss_cpptest(map_residue_factories_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/map_residue_factories_test.cpp)

# ---------------------------------------------------------------------------
# lane worldspawn -- A GUARD THAT SWALLOWED ITS OWN LIVE ARM.
#
# One invariant over the whole shim: no `#ifndef SS_CBASEENTITY_ALIAS` region
# may contain the whole of a pStandalone arm and offer no `#else`. Four
# functions in shell_entityapi.cpp broke it at once, one of which made the
# player spawn dead on every armed map load. Pure source scan; no engine, no
# link. Appended at the END, as the lane brief directs.
# ---------------------------------------------------------------------------
ss_cpptest(armed_arm_swallowed_test
    SOURCES sourcesharp/test/armed_arm_swallowed_test.cpp)

# lane loose-ends -- WHICH HALF OF THE PLAYER-SHADOW FIX IS ON -- IS ANSWERED,
# AND shadow_switch_test IS GONE WITH ITS SUBJECT.
#
# Its seven facts were about the arm arithmetic of three convars
# (sourcesharp_shadow_stock_params and its two halves) that no longer exist:
# lane nan-explain explained the NaN, showed that
# the PRIMING half is the one that closes it and that the damping half cannot
# touch a CPlayerController member at all, and landed both unconditionally.
# The convars' own header named "whoever closes the NaN" as their removal
# owner.
#
# THE ONE DURABLE THING IN THOSE SEVEN did not go with them: stock's three
# damping values (0.1, 0.1, 0.05) are now pinned by
# ShadowPrimingPin.StocksThreeDampingDefaultsAreStillTheValuesWritten below,
# which reads the source rather than the switch.

# ---------------------------------------------------------------------------
# lane loose-ends -- THE TWO SECTION 6 RULINGS.
#
# c7-fx and c7-leaf each found a compiled client TU that plan_entity.md §6 did
# not name and correctly declined to rule on it. This lane ruled: both stay
# native and are de-entitied in place. A ruling is prose, so what is pinned is
# the three things it rests on -- §6 naming both files, EffectsClient.cpp still
# touching no entity, and toolframework_client.cpp's entity edges still inside
# the SS_CBASEENTITY_ALIAS region WHOLE -- until the file left client.so (SFM is
# dead, user ruling 2026-10-19), which is what is pinned for it now.
#
# Comments and string literals are stripped first: the headers this lane added
# to both files quote the very tokens these facts search for.
#
# Appended at the END, as the lane brief directs.
# ---------------------------------------------------------------------------
ss_cpptest(section6_rulings_test
    SOURCES sourcesharp/test/section6_rulings_test.cpp)

ss_cpptest(convar_notify_test
    SOURCES sourcesharp/test/convar_notify_test.cpp)

# WHAT STOCK'S variant_t ANSWERS ABOUT A MAP'S TEXT -- plan_entity.md, the
# variant_t::Convert subsystem. No include path: it needs libc and gtest only,
# which is the point -- it is asserting what the platform does, so anything the
# engine could interpose would spoil the answer. It reads
# sourcesharp/test/variant_convert_cases.tsv, the same table the managed
# VariantAgreementTests reads.
ss_cpptest(variant_convert_test
    LINUX_ONLY "pins glibc's atoi on out-of-range input (the stock Linux behaviour), and the Windows CRT clamps differently, as stock Windows does"
    SOURCES sourcesharp/test/variant_convert_test.cpp)

# THE FOUR RULINGS THE CLOSE MADE ON tools/entity-census-patterns.
#
# Three rows narrowed and one widened, and a narrowing is the most dangerous
# edit anyone can make to a gate: it lowers a number without touching the tree.
# Every one is driven over text BOTH ways here -- the thing the row still hunts
# IS matched, the thing the ruling excused is NOT -- so a regression to the bare
# pattern reddens one half and a re-widening reddens the other. Plus the two
# premises the deletions in the close rest on: CHandle<IGameEntity>'s Get AND
# Set are specialised and neither is a cast, and every GetBaseEntity() in the
# tree answers NULL.
#
# Engine-free: it reads the pattern file and four game headers as TEXT and uses
# std::regex. Appended at the end of the TEST LIST -- NOT at the end of the
# file, because the last thing in this file is the unmatched-name refusal and a
# row below it is a row `buildcpptests <name>` refuses to build. That is the
# trap the refusal's own banner names, sprung on the lane that added it.
ss_cpptest(close_rulings_test
    LINUX_ONLY "compiles a probe with the host compiler and reads its gcc/clang diagnostics"
    SOURCES sourcesharp/test/close_rulings_test.cpp)

# THE UNMATCHED-NAME REFUSAL, AND IT HAS TO BE THE LAST THING IN THIS FILE.
#
# It used to sit two thirds of the way down, and every lane brief in this port
# tells a lane to APPEND ITS TEST AT THE END so that two lanes merge with
# keep-both. Those two instructions fight: three tests -- an octree-audit
# test (since removed with its subject), standalone_solidity_order_test and
# shadow_switch_test (since deleted with its subject; see the note where its
# entry was) -- were appended below the guard, so a filtered
# `src/buildcpptests <one of them>` set MATCHED=0 at the guard and REFUSED a
# test this file really did define. Measured before the move: that command
# printed "no test here is named: ..." and built nothing.
#
# The unfiltered run was always fine, which is why it went unnoticed: the guard
# only fires when a name was given. So what it broke was exactly the filtered
# form two mutation harnesses' headers document as the way to rebuild their
# subject -- and a mutation harness whose rebuild refuses reads as a subject
# that would not compile.
#
# lane sub-factory -- THE PORT'S OWN ENTITY FACTORY TABLE.
#
# `EntityFactoryDictionary()` is not SDK legacy waiting to be deleted: every
# `[ScriptedEntity]` C# declares installs there, worldspawn installs there, and
# the player is FOUND there at connect. It is machinery the managed hierarchy
# runs on, borrowed from `src/game/server/util.cpp` -- a stock translation unit
# in the removal set. `src/sourcesharp/game/server/entity_factory_table.h` is
# the replacement's name half.
#
# BEHAVIOURAL, not a source scan, and that is why the table is a header at all.
# `shell_standalone_class.cpp` includes cbase.h, so nothing that holds the
# dictionary today can be linked into a gtest, and every existing test that
# reaches this subsystem reads the .cpp as a string. A scanner cannot see that
# Find is case-insensitive, that a reload's second install is a no-op rather
# than stock's silent duplicate key, or that CanonicalName stopped indexing an
# element vector at 0xFFFF for an unknown class.
#
# tier1, for CUtlDict and CUtlVector -- the table IS those two, and a stub would
# be testing the stub. -Isourcesharp/game/server for the header itself.
# clang: -Wno-unused-variable for Valve's public/tier1/utlblockmemory.h:350
# (an unused local in a template gcc 10 never instantiated), on a plain -I here.
ss_cpptest_tier1(entity_factory_table_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    INCLUDES public public/tier0 public/tier1 common sourcesharp/game/server
             sourcesharp/game/shared
    SOURCES sourcesharp/test/entity_factory_table_test.cpp)

# The weapon script oracle (weapon_script_oracle_test) compiles upstream's
# weapon_parse.cpp, so it lives with the other oracles in src/test/oracle/native
# and reads the pristine Valve SDK rather than the farm.

# THE CLIENT'S WORLD ARM, and the eleven port definitions that left the stock
# file (client_world_arm_test.cpp) -- plan_entity.md §3.3, lane `c8-listseam`.
#
# Two things in one leg because they are one lane's one change: the port's own
# code came out of game/client/cliententitylist.cpp, and the reason it COULD is
# that worldspawn stopped needing what is left of CClientEntityList to be found.
#
# The four rules under test are inlines in shell_cliententitylist.h that take
# CBaseHandles and nothing else -- world_subject.h's arrangement, and the reason
# it is that arrangement -- so this leg needs the tier0 prelude for basehandle.h
# and no engine at all. Three of the four have an IsValid() guard whose absence
# is invisible until a level is unloaded: CBaseHandle::GetEntryIndex() answers
# NUM_ENT_ENTRIES-1 for an INVALID handle, deliberately (basehandle.h:145), so
# an unguarded compare says "that is the world" about entry 8191.
#
# The source scans STRIP COMMENTS and the stripper is itself asserted, because
# the two files this lane moved code OUT of now carry a note naming what left --
# and COMMON.md's "prose counts as code to a raw grep" has already cost this
# port a wrong census.
#
# The two -Wno- flags are for VALVE'S HEADERS, the same two and for the same
# reason as the other tier0-prelude legs: tier0/platform.h's MSVC `#pragma
# warning` lines and its braceless DECLARE_ALIGNED_BYTE_ARRAY `if`.
ss_cpptest(client_world_arm_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 common sourcesharp/game/client
    SOURCES sourcesharp/test/client_world_arm_test.cpp)

# shadow_priming_pin_test (lane nan-explain) stood here: a text scan of
# CStandalonePlayer::InitVCollision's priming Update. The body is managed's
# since plan_physics_bones_native_zero 2.5 (PlayerShadowBodies), and every case
# is a managed fact over the calls it makes (PlayerShadowBodiesTests: the
# priming update between controller and state, its zero velocity, never
# conditional, its reason beside it; the damping defaults in
# The_hulls_are_made_with_stocks_parameters; the containment convars'
# absence in PlayerShadowTests).

# ---------------------------------------------------------------------------
# lane pvs-ragdoll -- THE CLIENT PVS-NOTIFY RULING.
#
# `f-list` refused the two PVS-notify walks with an argument that quoted
# C_ClientRagdoll's `GetPVSNotifyInterface() { return this; }` as evidence the
# list was empty. This lane MEASURED the path on a live dm_lockdown instead:
# AddPVSNotifier is entered 371 times a level and adds 0 rows, and the game
# constructs 0 C_ClientRagdoll. The one the probe built worked perfectly, so
# the machinery is live and unoccupied rather than absent.
#
# What is pinned here is the two properties that make guarding the walks sound
# -- exactly one non-NULL GetPVSNotifyInterface in the tree, and both its TU
# and AddPVSNotifier's TU inside tools/client-flip-closure's removal seed --
# plus the two that say this is NOT the shadow-leak crash class.
#
# A SOURCE SWEEP, and it reads HEADERS: the one non-NULL implementation is a
# header inline, which a vpc-driven `$File` walk cannot see at all.
#
# APPENDED AT THE END so a second lane's test merges with keep-both.
# ---------------------------------------------------------------------------
ss_cpptest(pvs_notify_ruling_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/pvs_notify_ruling_test.cpp)

# ---------------------------------------------------------------------------
# plan_shim.md 1d: game/server/game.cpp left the build and its seventeen ConVar
# declarations did not. Seven of the seventeen are reached only by NAME -- from
# managed code, from the engine, and from cfg/settings.scr -- which is why the
# link-line census that authorised the deletion reads 0 importers for all
# seventeen and is nonetheless not the whole answer. Also pins that exactly one
# compiled server file calls ConVar_Register: tier1 guards re-entry, so a
# second call makes the accessor of whichever ran second dead code, silently.
#
# APPENDED AT THE END so a second lane's test merges with keep-both.
# ---------------------------------------------------------------------------
ss_cpptest(gamedll_convars_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/gamedll_convars_test.cpp)

# ---------------------------------------------------------------------------
# DOES MANAGED RegisterConVar ACCEPT FCVAR_REPLICATED?
# (convar_replicated_flag_test.cpp, plan_shim.md Phase 2b, lane 2-physics)
#
# Three of ragdoll_shared.cpp's ConVars are FCVAR_REPLICATED and the plan moves
# them to managed declarations. The whole round trip is text -- the managed enum
# is a hand-written copy of the engine's bit layout -- and a replicated ConVar
# that registers unreplicated is invisible until two clients disagree.
#
# APPENDED AT THE END so a second lane's test merges with keep-both.
# ---------------------------------------------------------------------------
ss_cpptest(convar_replicated_flag_test
    SOURCES sourcesharp/test/convar_replicated_flag_test.cpp)

ss_cpptest(convar_dllflag_test
    SOURCES sourcesharp/test/convar_dllflag_test.cpp)

# ---------------------------------------------------------------------------
# plan_shim.md Phase 5: docs/convar-ledger.tsv is the whole native keep list
# for ConVar and ConCommand declarations under src/sourcesharp. This pins it
# both ways -- nothing declared natively that the ledger does not list, and no
# ledger row that outlived its declaration.
#
# APPENDED AT THE END so a second lane's test merges with keep-both.
# ---------------------------------------------------------------------------
ss_cpptest(convar_ledger_test
    SOURCES sourcesharp/test/convar_ledger_test.cpp)

# ---------------------------------------------------------------------------
# plan_shim.md Phase 6: the FCVAR word on a managed ConCommand. The slot took
# (name, help) and nothing else, so a C#-declared command was always unflagged
# -- thirteen ledger rows sat at `deferred-abi` for that one reason. This pins
# the word reaching the ConCommand constructor AND the DLL identifier bit the
# static construction list can no longer supply.
#
# APPENDED AT THE END so a second lane's test merges with keep-both.
# ---------------------------------------------------------------------------
ss_cpptest(concommand_flags_test
    SOURCES sourcesharp/test/concommand_flags_test.cpp)

# ---------------------------------------------------------------------------
# standalone_bone_matrices_test and standalone_bone_subject_test LEFT with
# their subject (plan physics_bones_native_zero 1.2, client half): the
# StandaloneBoneMatrices / StandaloneIsPoseable / StandaloneStudioName slots
# retired once both sides' skeletons were managed. What they pinned is held by
# ServerSkeletonsTests and ClientSkeletonsTests (BoneMatrices, IsPoseable,
# StudioName) and ClientBoneRecorderTests (the recorder's subject).
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# plan_shim.md Phase 6: what the ledger's deferred-abi rows are ACTUALLY
# waiting for. All thirteen carried the same reason -- the missing FCVAR word
# -- which ABI 250 lifted, and all thirteen are still blocked on something
# else. A reason that has been lifted is worse than none: it sends the next
# lane at a door that is already open.
#
# APPENDED AT THE END so a second lane's test merges with keep-both.
# ---------------------------------------------------------------------------
ss_cpptest(concommand_flags_ledger_test
    SOURCES sourcesharp/test/concommand_flags_ledger_test.cpp)

# ---------------------------------------------------------------------------
# plan_shim.md Phase 6, ratchet 1: server_csharp.vpc names no $File row under
# $SRCDIR\game\ or $SRCDIR\public\ -- Phase 2's exit condition, met and until
# now unwatched. The ceiling for the client's remaining stock rows is in
# docs/shim-ratchet.tsv and the same parser measures both, so the server's
# zero cannot pass vacuously.
#
# APPENDED AT THE END so a second lane's test merges with keep-both.
# ---------------------------------------------------------------------------
ss_cpptest(shim_vpc_stock_rows_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/shim_vpc_stock_rows_test.cpp)

# ---------------------------------------------------------------------------
# plan_shim.md Phase 6, ratchets 2 and 3: every .cpp under src/sourcesharp
# outside test/ and managed/ carries exactly one SS_REASON marker naming one
# of §1's eight letters -- or `x`, for the eight files no letter covers, each
# of which has an exit condition in docs/shim-reasons.md. The shim's file and
# line counts are ceilings in docs/shim-ratchet.tsv.
#
# APPENDED AT THE END so a second lane's test merges with keep-both.
# ---------------------------------------------------------------------------
ss_cpptest(shim_reason_letters_test
    SOURCES sourcesharp/test/shim_reason_letters_test.cpp)


# plan_globals.md step G0: the server link line holds no static state that
# docs/globals-ledger.tsv has not ruled, and the counts in
# docs/globals-ratchet.tsv are ceilings. State is a SECTION, not a keyword --
# readelf over the objects on the link edge -- so this is the check that
# tools/statics-audit's source scan could not make.
#
# APPENDED AT THE END so a second lane's test merges with keep-both.
# ---------------------------------------------------------------------------
ss_cpptest(globals_ledger_test
    LINUX_ONLY "runs the tree's bash and Python instruments (tools/) and reads their exit status"
    SOURCES sourcesharp/test/globals_ledger_test.cpp)

# The CLIENT's half of plan_globals.md step G0: docs/globals-client-ledger.tsv
# rules every piece of static state on the client link line, both ways, and
# docs/globals-client-ratchet.tsv caps the counts. Separate from the server's
# files, which other lanes own.
#
# APPENDED AT THE END so a second lane's test merges with keep-both.
# ---------------------------------------------------------------------------
ss_cpptest(globals_client_ledger_test
    LINUX_ONLY "runs the tree's bash and Python instruments (tools/) and reads their exit status"
    SOURCES sourcesharp/test/globals_client_ledger_test.cpp)

# =============================================================================
# AT THE END, so that the next appended test is above it by construction.

# ---------------------------------------------------------------------------
# Phase 6b: cmake_graph.h, the one C++ reader of what the CMake build compiles
# and links (compile_commands.json cross-checked against the CMakeLists lists,
# build.ninja's link edges), which every "is X compiled / linked" gtest here
# stands on. Real-build facts plus fixtures for every missing/empty/stale
# input, each of which must be an error rather than an answer.
# ---------------------------------------------------------------------------
ss_cpptest(cmake_graph_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/cmake_graph_test.cpp)

# The tests' precompiled gtest header (cmake/SourceSharpTests.cmake): every TU
# that reads one reads a header built with its own flags, token for token, and
# no header serves a single TU.
ss_cpptest(test_pch_test
    LINUX_ONLY "reads the compile database's clang lines, and MSVC builds the tests without a precompiled header"
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/test_pch_test.cpp)

# ---------------------------------------------------------------------------
# Phase 6b: the link-line facts that outlived VPC (they were in
# vpc_link_libraries_test, vpc_no_protobuf_test and vpc_platforms_test, beside
# script checks that went with VPC): choreoobjects gone; dmxloader kept because
# particles.a demands it; particles, steam_api and managed_host linked; no
# libprotobuf. Read off build.ninja's link edges through cmake_graph.h.
# ---------------------------------------------------------------------------
ss_cpptest(link_libraries_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES sourcesharp/test/link_libraries_test.cpp)

# The oracles: ports checked against upstream Valve code compiled from the
# pristine clone at valve-sdk.lock (label `oracle`, make test's oracle leg).
# Added from here so they share this directory's test settings.
# Only in the oracle tree (SS_TEST_TREE, the oracle-* presets): the oracles
# have their own build directory, so the native and oracle legs never share one.
if(SS_TEST_TREE STREQUAL "oracle")
  add_subdirectory(${PROJECT_SOURCE_DIR}/src/test/oracle/native
                   ${CMAKE_CURRENT_BINARY_DIR}/oracle)
endif()

# The allocation checker (plan_server_novalve.md §3.6), linked exactly as
# server.so links it in debug: alloc_check.cpp plus the wraps. Each finding is
# planted and seen, abort mode dies on both, and matching pairs (libstdc++'s own
# growth included) raise nothing.
ss_cpptest(alloc_check_test
    LINUX_ONLY "runs under the allocation checker, which wraps glibc's allocator (--wrap) and resolves symbols with dlsym"
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/alloc_check_test.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/alloc_check.cpp
    INCLUDES ${PROJECT_SOURCE_DIR}/src/server/native
    DEFINES SOURCESHARP_ALLOC_CHECK
    LIBS ss_alloc_check_wraps)

# The unload report runs after the DSO's static destructors, at dlclose as at
# exit: alloc_unload_probe.so is built like server.so (alloc_check.cpp and its
# wraps) and holds a static std::unordered_map plus one deliberate leak.
# (Linux only, as the checker is: the probe library and the test that loads it.)
if(SS_PLATFORM STREQUAL linux)
add_library(ss_alloc_unload_probe SHARED
    ${PROJECT_SOURCE_DIR}/src/test/native/alloc_unload_probe/alloc_unload_probe.cpp
    ${PROJECT_SOURCE_DIR}/src/server/native/alloc_check.cpp)
ss_test_compile_flags(ss_alloc_unload_probe)
target_compile_definitions(ss_alloc_unload_probe PRIVATE SOURCESHARP_ALLOC_CHECK)
target_include_directories(ss_alloc_unload_probe PRIVATE ${PROJECT_SOURCE_DIR}/src/server/native)
target_link_libraries(ss_alloc_unload_probe PRIVATE ss_alloc_check_wraps dl)
set_target_properties(ss_alloc_unload_probe PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY ${CMAKE_CURRENT_BINARY_DIR}/alloc_unload_probe)
endif()
ss_cpptest(alloc_check_unload_test
    LINUX_ONLY "dlopens a .so built with the allocation checker, which wraps glibc's allocator"
    DEFINES "SS_ALLOC_UNLOAD_PROBE=\"$<TARGET_FILE:ss_alloc_unload_probe>\""
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/alloc_check_unload_test.cpp
    LIBS dl)
if(TARGET alloc_check_unload_test)
  add_dependencies(alloc_check_unload_test ss_alloc_unload_probe)
endif()

# server.so's R5 include set (plan_server_novalve.md 2, 3.4): the allowlisted
# engine interface headers compile on it, and cbase.h is not reachable from it.
get_property(_ss_novalve_includes GLOBAL PROPERTY SS_SERVER_NOVALVE_INCLUDES)
ss_cpptest(novalve_include_set_test
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/novalve_include_set_test.cpp
    DEFINES POSIX _POSIX LINUX _LINUX GNUC PLATFORM_64BITS GAME_DLL
    # The allowlisted Valve headers warn as they do in server.so, which
    # silences the same four by name (SS_WARN_CLANG): platform.h's pragmas and
    # dangling else, bitvec.h's memset over a class with a constructor, and
    # utlblockmemory.h:350's unused variable.
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-nontrivial-memcall -Wno-unused-variable
    INCLUDES ${_ss_novalve_includes})
ss_must_not_compile(novalve_include_set_canary
    EXPECT "'cbase\\.h' file not found"
    DEFINES NOVALVE_CANARY=1
    INCLUDES ${_ss_novalve_includes}
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/novalve_include_set_test.cpp)

# ss_client_message.h: the client's tier1 writer against the server's own
# decoder (server.so links no tier1), bit for bit at every offset.
ss_cpptest_tier1(ss_client_message_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/ss_client_message_test.cpp)

# plan_server_novalve.md P2.2: server.so's user messages are C# payloads appended
# by SourceSharp_AppendBits; tier1 here is the stock bf_write writers they are
# held to, bit for bit, at every starting offset. (usermessage_write_test until
# the last native payload writer went, 2026-10-04.)
ss_cpptest_tier1(usermessage_append_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/usermessage_append_test.cpp)

# ---------------------------------------------------------------------------
# plan_server_novalve.md P2.3c: SS_NATIVE_COMMAND bodies, found by the name C#
# registers. Header-only; the test's own CCommand stands in for tier1's.
# ---------------------------------------------------------------------------
ss_cpptest(native_command_body_test
    INCLUDES sourcesharp/game/shared
    SOURCES sourcesharp/test/native_command_body_test.cpp)

# ---------------------------------------------------------------------------
# plan_server_novalve.md P3, tier1's small members: the server's own string
# pool under CGameStringPool, and tier1_port.cpp's interface registry and
# bf_read::SetOverflowFlag. The registry test links no tier1: the port is the
# only definition, as it is in server.so.
# ---------------------------------------------------------------------------
ss_cpptest_tier1(server_string_pool_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/server/native
             ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/server_string_pool_test.cpp)

# The same pool in a TU that includes tier0/memdbgon.h first, as
# gamestringpool.cpp does, under the allocation checker's wraps: whatever it
# releases, it allocated (the P4 prerequisite: no foreign frees).
ss_cpptest_tier1(server_string_pool_alloc_test
    LINUX_ONLY "runs under the allocation checker, which wraps glibc's allocator (--wrap) and resolves symbols with dlsym"
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC SOURCESHARP_ALLOC_CHECK
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/server/native
             ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/server_string_pool_alloc_test.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/alloc_check.cpp
    LIBS ss_alloc_check_wraps)

ss_cpptest(tier1_port_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/tier1_port_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/tier1_port.cpp)

# server.so's mathlib members (mathlib_port.cpp), linked WITHOUT libmathlib.a:
# it builds only while the port is self-contained, and runs on Windows, where
# no mathlib binary can. mathlib_port_stock_test holds it to stock bit for bit.
ss_cpptest(mathlib_port_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/mathlib_port_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/mathlib_port.cpp)

# The port's ss_mathlib:: inlines against the stock libmathlib.a members of the
# same names, side by side in one binary, every output float compared by its
# bits.
ss_cpptest_mathlib(mathlib_port_stock_test
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES sourcesharp/test/mathlib_port_stock_test.cpp)

# ---------------------------------------------------------------------------
# A managed declaration never aliases a convar the engine defines:
# convar_ownership.h's rule, which SourceSharp_EngineRegisterConVar applies.
# ---------------------------------------------------------------------------
ss_cpptest(convar_ownership_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/convar_ownership_test.cpp)

# ---------------------------------------------------------------------------
# The client's MD5_PseudoRandom answers from C# (IManagedDiagnosticsClient);
# tier1's checksum_md5.cpp is out of the build. Links tier0 for Error().
# ---------------------------------------------------------------------------
ss_cpptest_tier1(md5_pseudorandom_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC CLIENT_DLL
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/host/native
             ${PROJECT_SOURCE_DIR}/src/shared/native ${PROJECT_SOURCE_DIR}/src/client/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/md5_pseudorandom_test.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/md5_pseudorandom.cpp)

# ---------------------------------------------------------------------------
# server.so's interface factory: one CreateInterface over a fixed table
# (shell_factory.cpp), in place of tier1's registry and the EXPOSE_ macros.
# ---------------------------------------------------------------------------
ss_cpptest(server_factory_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/server/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/server_factory_test.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/shell_factory.cpp)

# ---------------------------------------------------------------------------
# tier1's convar.cpp, ported (convar_port.cpp): ConCommandBase, ConCommand,
# ConVar, ConVarRef and ConVar_Register, against a fake ICvar. Links tier1 for
# the string functions only; the port is the only convar.cpp definition here.
# ---------------------------------------------------------------------------
ss_cpptest_tier1(convar_port_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC GAME_DLL
    # utlblockmemory.h:350's unused variable, silenced by name as server.so does.
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/convar_port_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/convar_port.cpp
            # AutoCompleteSuggest appends through the engine's heap.
            ${PROJECT_SOURCE_DIR}/src/shared/native/engine_heap.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/loaded_symbol.cpp
    LIBS dl)

# ---------------------------------------------------------------------------
# The standard-library containers server.so's own code moved to so it stops
# importing tier0's g_pMemAlloc (plan_server_novalve.md P4), each driven through
# the header the game compiles: ss_containers.h's string pool and case-blind
# hash (the tier1-comparable parts are ss_containers_oracle_test's), the
# game-event subscription record
# (shell_gameevents.cpp) and the managed console's carrier registry
# (sourcesharp_gamesystem.cpp). Header-only, engine-free.
# ---------------------------------------------------------------------------
ss_cpptest(ss_containers_test
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/ss_containers_test.cpp)
ss_cpptest(game_event_subscriptions_test
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/game_event_subscriptions_test.cpp)
ss_cpptest(console_registry_test
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/console_registry_test.cpp)

# SourceSharp_BoneBatchEntry (standalone_bone_batch.h, plan_physics_bones_perf
# A2): the frame's posed skeletons answer only the asks they were posed for.
# Header-only, engine-free.
ss_cpptest(standalone_bone_batch_test
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/standalone_bone_batch_test.cpp)

# ss::EngineUtlVectorAppend (engine_utlvector.h): growing a vector the ENGINE
# owns with the engine's allocator, as ConCommand::AutoCompleteSuggest
# (convar_port.cpp) does to the engine's CUtlVector<CUtlString>. Against a
# recording heap; engine_utlvector_layout_oracle_test hands the result to the
# pristine template. Header-only, engine-free.
ss_cpptest(engine_utlvector_append_test
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/engine_utlvector_append_test.cpp)

# SourceSharp_FatalReport: a failed assert or Error() reports to stderr and to
# $SOURCESHARP_ASSERT_DIR/assert-<pid>.txt and exits 253 (-3). Death tests.
# The dev API's exit record, native half: the signal recorder notes the
# sender and runs the handler that was there; the SDL watch counts quit and
# window-close; the fatal witness writes the record file.
ss_cpptest(exit_watch_test
    LINUX_ONLY "forks a child process and reads its wait status"
    INCLUDES ${PROJECT_SOURCE_DIR}/src/server/native
             ${PROJECT_SOURCE_DIR}/src/shared/native
             ${PROJECT_SOURCE_DIR}/src/host/native
    SOURCES sourcesharp/test/exit_watch_test.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/exit_watch.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/fatal_report.cpp)

ss_cpptest(fatal_report_test
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES sourcesharp/test/fatal_report_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/fatal_report.cpp)

# tier0_spew_port.cpp: the tier0 functions server.so calls through dbg.h,
# defined in server.so (plan_server_novalve.md P4). Links NO tier0, so every
# Msg / Warning / Assert / Error here reaches the port.
# _DEBUG: the Assert macro only expands with DBGFLAG_ASSERT, which _DEBUG sets,
# as it does in server.so's debug build. -Wno-unused-variable for Valve's
# utlblockmemory.h (an unused local in a template), as elsewhere.
ss_cpptest(tier0_spew_port_test
    DEFINES POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC DEBUG _DEBUG
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES sourcesharp/test/tier0_spew_port_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/tier0_spew_port.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/fatal_report.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/loaded_symbol.cpp
    LIBS dl)

# The same port with a libtier0.so loaded: its default spew hands each line to
# that library's slot, which in the game is the engine's and writes console.log.
# fake_tier0/libtier0.so stands in for the game's (a slot and nothing else), so
# the facts can fill the slot; the binary still links no tier0.
# (Linux only: the stand-in is a libtier0.so the test dlopens.)
if(SS_PLATFORM STREQUAL linux)
add_library(ss_fake_tier0 SHARED ${PROJECT_SOURCE_DIR}/src/test/native/fake_tier0/fake_tier0.cpp)
ss_test_compile_flags(ss_fake_tier0)
set_target_properties(ss_fake_tier0 PROPERTIES
    OUTPUT_NAME tier0
    LIBRARY_OUTPUT_DIRECTORY ${CMAKE_CURRENT_BINARY_DIR}/fake_tier0
    CXX_VISIBILITY_PRESET hidden)
endif()
ss_cpptest(tier0_spew_forward_test
    LINUX_ONLY "finds libtier0.so's spew function with dlopen and dlsym"
    DEFINES POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC DEBUG _DEBUG
            "SS_FAKE_TIER0=\"$<TARGET_FILE:ss_fake_tier0>\""
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/tier0_spew_forward_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/tier0_spew_port.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/fatal_report.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/loaded_symbol.cpp
    LIBS dl)
if(TARGET tier0_spew_forward_test)
  add_dependencies(tier0_spew_forward_test ss_fake_tier0)
endif()

# client.so's tier0 and vstdlib bindings (plan_client_novalve C9): every name
# tier_bindings_client.cpp defines reaches the library the engine loaded. The
# fakes stand in for the game's libtier0.so and libvstdlib.so (their file
# names, each name the client binds, and a record of each call), linked so they
# are loaded before the bindings copy their two pointers at load.
if(SS_PLATFORM STREQUAL linux)
foreach(fake tier0 vstdlib)
  add_library(ss_fake_engine_${fake} SHARED
      ${PROJECT_SOURCE_DIR}/src/test/native/fake_engine_tier/fake_engine_${fake}.cpp)
  ss_test_compile_flags(ss_fake_engine_${fake})
  set_target_properties(ss_fake_engine_${fake} PROPERTIES
      OUTPUT_NAME ${fake}
      LIBRARY_OUTPUT_DIRECTORY ${CMAKE_CURRENT_BINARY_DIR}/fake_engine_tier
      CXX_VISIBILITY_PRESET hidden)
endforeach()
endif()
ss_cpptest(tier_bindings_client_test
    LINUX_ONLY "binds to the libtier0.so and libvstdlib.so loaded in the process"
    DEFINES POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/tier_bindings_client_test.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/tier_bindings_client.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/loaded_symbol.cpp
    LIBS ss_fake_engine_tier0 ss_fake_engine_vstdlib dl)

# client.so's own tier0 profile, never enabled (user ruling 2026-10-18), and
# its Plat_FloatTime (plan_client_novalve C9). Links no tier0: a VPROF macro
# that reached a node would stop the process.
ss_cpptest(tier0_client_test
    LINUX_ONLY "client.so's own tier0 profile and clock, which only Linux builds"
    DEFINES POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC VPROF_LEVEL=1
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-reorder-ctor
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/tier0_client_test.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/tier0_vprof_client.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/tier0_thread_client.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/tier0_thread_port.cpp)

# console_display_forward.cpp: the engine's console prints, forwarded to this
# load's managed console-output area (docs/todo.md's console capture item).
# Links no engine: CRecordingCvar records the Install/RemoveConsoleDisplayFunc
# pair, the fixture's sink answers ManagedConsoleOutput(). The server-arm
# accessor is defined here, in the test's own image, exactly as the spew
# forward test defines g_pCVar.
ss_cpptest(console_display_forward_test
    DEFINES POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC DEBUG _DEBUG
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 common sourcesharp/host
             ${PROJECT_SOURCE_DIR}/src/shared/native
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/console_display_forward_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/console_display_forward.cpp)

# console_line_buffer.h: the console lines a side's forwarder appends and its
# managed side drains (console-batch): order, numbering, the bound and its
# overflow count, threaded producers, a drain that never holds a print up, and
# the layout NativeConsoleBuffer.cs reads. Header-only, links nothing.
# ss_ci_skip.h: the CI skip (SOURCESHARP_CI=1) the native tests use for what
# cannot pass on CI's runners (src/test/common/NotOnCiFactAttribute.cs).
ss_cpptest(ss_ci_skip_test
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/ss_ci_skip_test.cpp)

ss_cpptest(console_line_buffer_test
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/console_line_buffer_test.cpp)

# tier0_thread_port.cpp: the thread and platform calls fork-header inlines make
# (CThreadFastMutex, DebuggerBreak), defined in server.so (P4). Links NO tier0.
ss_cpptest(tier0_thread_port_test
    LINUX_ONLY "drives the POSIX thread port through pthreads"
    DEFINES POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/tier0_thread_port_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/tier0_thread_port.cpp)

# ss_recipient_filter.cpp: the list logic of server.so's own recipient filters
# (plan_server_novalve.md P5). Links that file alone, on the R5 include set.
# -Wno-nontrivial-memcall for Valve's bitvec.h and -Wno-unused-variable for its
# utlblockmemory.h, as on the server target.
ss_cpptest(ss_recipient_filter_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-nontrivial-memcall -Wno-unused-variable
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/server/native
             ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/ss_recipient_filter_test.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/ss_recipient_filter.cpp)
# ss_command_line.h: this process's own argv from /proc/self/cmdline, in place of
# tier0's CommandLine() (P4). Links no tier0. Built as the client's arm: the
# Value half is client-only since the server gc cull (server.so never reads a value).
ss_cpptest(ss_command_line_test
    LINUX_ONLY "compares with glibc's program_invocation_name"
    DEFINES CLIENT_DLL
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/ss_command_line_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/ss_command_line.cpp)

# plan_server_novalve.md P5 lane SP -- ss_physics_shared.h's game-flag bits are
# physics_shared.h's own values, read out of the fork header's text
# (ss_physics_shared_test.cpp). Its PhysSetGameFlags NULL-guard fact left with
# the helper (2026-10-12).
ss_cpptest_engine_headers(ss_physics_shared_test
    SOURCES sourcesharp/test/ss_physics_shared_test.cpp)

# plan_server_novalve.md P5 lane SP -- ss_effect_dispatch.h's two wire numbers
# are their fork headers' own values, and ss::CEffectData's defaults are
# stock's (ss_effect_dispatch_test.cpp).
ss_cpptest_engine_headers(ss_effect_dispatch_test
    SOURCES sourcesharp/test/ss_effect_dispatch_test.cpp)

# plan_server_novalve.md P5 lane SP -- ss::TePrediction answers the suppress
# host it was given, and NULL when fresh (ss_te_prediction_test.cpp).
ss_cpptest(ss_te_prediction_test
    SOURCES sourcesharp/test/ss_te_prediction_test.cpp)

# THE CENSUS TIER (cmake/SourceSharpTests.cmake ss_census): the tests that
# drive the census tools over the real build, 2-15 s each (ctest at
# 79d3b8af: globals_client_ledger 14.8, orphan_reach 8.3, entity_gates 6.9,
# globals_ledger 6.2, component_closure 5.6, entity_census 2.7; every other
# binary under 1.3). make test-full runs them; make test does not.
ss_census(globals_client_ledger_test)
ss_census(orphan_reach_test)
ss_census(entity_gates_test)

# tier1 and mathlib compile only the members some link pulls in
# (cmake/sdk/tier1.cmake, mathlib.cmake): every consumer of the two archives is
# relinked with --trace, and every member must be pulled in by one of them.
# Census tier, because it relinks about thirty binaries.
ss_cpptest(sdk_lib_members_test
    LINUX_ONLY "relinks the gcc/clang build's consumers with ld's --trace and reads build.ninja"
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/sdk_lib_members_test.cpp)
ss_census(sdk_lib_members_test)
ss_census(globals_ledger_test)
ss_census(component_closure_test)
ss_census(entity_census_test)

# The Valve names client.so links for its recv tables (plan_client_novalve C8):
# engineabi_recv_names.cpp's members and the standard table's pointer identity,
# which the oracle (recv_builders_oracle_test) cannot link beside upstream's
# dt_recv.cpp. The engine-header kind: client_class.h reaches studio.h and the
# client interfaces, which need the engine target's -Wno- set.
ss_cpptest_engine_headers(client_recv_names_test
    DEFINES CLIENT_DLL
    INCLUDES public public/tier0 public/tier1
             ${PROJECT_SOURCE_DIR}/src/engineabi/native/include
    SOURCES sourcesharp/test/client_recv_names_test.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/engineabi_recv.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/engineabi_recv_names.cpp)

# plan_client_novalve C8 (lane c6-input): the util and registry TUs that left
# client.so with nothing to replace them, and their call sites, read from the farm.
ss_cpptest(client_c8_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/client_c8_evicted_test.cpp)

# plan_client_novalve C9: tier2.a is off client.so's link, and every tier2 name
# client.so defines is one of our objects' (particles.a's imports are stubbed,
# user ruling 2026-10-12).
ss_cpptest(client_no_tier2_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/client_no_tier2_test.cpp)

# client.so's CUtlMemoryPool (tier1_client.cpp, C9) over mempool_scenario.h,
# held to the golden pristine mempool.cpp wrote (mempool_stock_oracle_test).
ss_cpptest_tier1(mempool_client_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    INCLUDES public public/tier0 public/tier1 common ${PROJECT_SOURCE_DIR}/src/shared/native
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/mempool_client_test.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/tier1_client.cpp)

# The client's interface factory (shell_factory_client.cpp, C9): InterfaceReg's
# chain and CreateInterface's answers. Links no tier1, so they are ours.
ss_cpptest(shell_factory_client_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 game/client
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/shell_factory_client_test.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/shell_factory_client.cpp)

# SFM is dead (user ruling 2026-10-19): client.so's tools-off stand-ins
# (shell_tools_off_client.cpp) answer "tools are off" to the Valve TUs that still
# name the tool framework. Links no tier0 or tier1.
ss_cpptest(shell_tools_off_client_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC CLIENT_DLL
    # Valve headers itoolentity.h reaches: utlblockmemory.h's unused local,
    # imageformat.h's and studio.h's initialiser order, studio.h's memcpy of
    # classes, and iclientrenderable.h's `*((CBaseHandle*)0)`.
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable -Wno-reorder-ctor
            -Wno-nontrivial-memcall -Wno-null-dereference
    INCLUDES public public/tier0 public/tier1 public/mathlib game/client game/shared
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/shell_tools_off_client_test.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/shell_tools_off_client.cpp)

# SFM is dead (user ruling 2026-10-19): the tool framework's two TUs are off
# client.so's source list, the stand-ins are on it, and the built client.so
# names neither tool interface.
ss_cpptest(client_tools_evicted_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and its built client.so with strings"
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/client_tools_evicted_test.cpp)
if(TARGET client_tools_evicted_test)
  add_dependencies(client_tools_evicted_test client_sourcesharp)
endif()

# plan_client_novalve C9: tier1.a's KeyValues.o, ilocalize.o and
# strtools_unicode.o are not linked into client.so: each name they define that
# client.so has is one of our objects' (keyvalues_client.cpp).
ss_cpptest(client_tier1_members_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/client_tier1_members_test.cpp)

# plan_client_novalve C9: tier3.a is off client.so's link too, and every tier3
# name client.so defines is one of our objects' (tier_connect.cpp).
# plan_client_novalve C9: bitmap.a and vtf.a are off client.so's link, and no
# name either defines is in client.so.
ss_cpptest(client_no_bitmap_vtf_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/client_no_bitmap_vtf_test.cpp)

ss_cpptest(client_no_tier3_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/client_no_tier3_test.cpp)

# plan_particles_so.md 3.3: libss_particles.so exports only
# SSParticleLib_Interface, client.so needs it (and the managed host) by SONAME,
# no import of it binds into client.so, and the tier1 names both carry are
# local in each.
ss_cpptest(particles_lib_isolation_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/particles_lib_isolation_test.cpp)

# plan_engine_forwarders.md: each forwarder library (libss_fwd_server.so,
# libss_fwd_client.so) exports its table function alone and links nothing;
# each game DLL needs its own side's by SONAME, imports that function, and
# does not need the other side's. Read
# off the built files with nm and readelf.
ss_cpptest(fwd_libraries_isolation_test
    LINUX_ONLY "reads the Linux build's shared objects with nm and readelf"
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/fwd_libraries_isolation_test.cpp)

# plan_engine_forwarders.md: each built forwarder library, loaded. They link no
# runtime (NO_RUNTIME), so nothing runs at load but the loader's relocations:
# every slot of their tables must be set without an initialiser, and on Windows
# each DLL imports nothing. fwd_engine_test compiles the sources into an
# executable with a CRT and cannot see either.
ss_cpptest(fwd_libraries_load_test
    DEFINES "SS_FWD_SERVER_LIB=\"$<TARGET_FILE:ss_fwd_server>\""
            "SS_FWD_CLIENT_LIB=\"$<TARGET_FILE:ss_fwd_client>\""
    INCLUDES ${PROJECT_SOURCE_DIR}/src/fwd/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/fwd_libraries_load_test.cpp
    LIBS dl)
add_dependencies(fwd_libraries_load_test ss_fwd_server ss_fwd_client)

# The game DLLs export CreateInterface alone, read from the built files on both
# platforms (ELF's dynamic symbols, PE's export directory): MSVC exports a
# definition of a dllimport-declared function, and server.dll/client.dll had
# exported tier0's ports, vstdlib's random stream and Valve's DLL_EXPORT cvar.
ss_cpptest(game_dll_exports_test
    DEFINES "SS_SERVER_LIB=\"$<TARGET_FILE:server_sourcesharp>\""
            "SS_CLIENT_LIB=\"$<TARGET_FILE:client_sourcesharp>\""
            "SS_HOST_LIB=\"$<TARGET_FILE:sourcesharp>\""
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/game_dll_exports_test.cpp)
add_dependencies(game_dll_exports_test server_sourcesharp client_sourcesharp sourcesharp)

# What the server does not need, in a client-only file beside a shared port
# (user ruling 2026-10-12): per symbol, no server importer and no server
# definition, and the client file still defines it.
ss_cpptest(server_dead_split_test
    LINUX_ONLY "reads the Linux build's graph (cmake_graph.h: compile_commands.json and build.ninja of the gcc/clang build) and asserts on its command lines"
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/server_dead_split_test.cpp)

# client.so's bit-buffer members (bitbuf_client.cpp, and tier1_port.cpp's
# bf_read::SetOverflowFlag) against the golden pristine bitbuf.cpp recorded
# (bitbuf_stock_oracle_test; plan_client_novalve C9). Links no tier1.
ss_cpptest(bitbuf_client_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/bitbuf_client_test.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/bitbuf_client.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/tier1_port.cpp)

# client.so's KeyValues members (keyvalues_client.cpp, plan_client_novalve C9
# K5) over keyvalues_scenario.h, held to the golden pristine KeyValues.cpp
# wrote (keyvalues_members_oracle_test). tier1 so a member the scenario calls
# and the port lacks pulls the archive's KeyValues.o and fails on duplicates.
ss_cpptest_tier1(keyvalues_client_test
    LINUX_ONLY "loads tier0 from the SDK's lib/public/linux64, and for Windows the SDK ships tier0 only as an import library"
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    # utlblockmemory.h's unused local under NDEBUG, a Valve header's.
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    INCLUDES public public/tier0 public/tier1 common ${PROJECT_SOURCE_DIR}/src/shared/native
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/keyvalues_client_test.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/keyvalues_client.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/engine_heap.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/loaded_symbol.cpp)

# ss_bitblock.h, the native half of the length-prefixed bit buffer, over
# client.so's bitbuf members (plan_client_novalve C9). Links no tier1.
ss_cpptest(ss_bitblock_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/ss_bitblock_test.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/bitbuf_client.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/tier1_port.cpp)

# What this platform does not build (LINUX_ONLY, and must-not-compile under
# MSVC), listed at configure time with each reason.
ss_cpptest_report_linux_only()
