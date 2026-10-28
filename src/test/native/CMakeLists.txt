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
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_physics.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_recipient_filter.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_trace_filter.cpp)

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
# The client library's leaf system (fwd_leaf_system.cpp, C7 cluster 12 L2) is
# client code built against game/client's headers, here as in the library:
# vendor/'s, on -I ahead of the fork's -isystem roots, so the tests that
# compile the client library in read the headers it is built from.
ss_vendor_includes(_ss_fwd_client_vendor client game/client game/shared common public public/tier0 public/tier1)
list(PREPEND _ss_fwd_client_vendor ${PROJECT_SOURCE_DIR}/src/client/native)
set(_ss_fwd_client_vendor_wno -Wno-unknown-pragmas -Wno-dangling-else -Wno-inconsistent-missing-override
    -Wno-nontrivial-memcall -Wno-null-dereference -Wno-overloaded-virtual -Wno-reorder-ctor
    -Wno-tautological-overlap-compare -Wno-unused-private-field)
set(_ss_fwd_leaf_system ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_leaf_system.cpp)
set_source_files_properties(${_ss_fwd_leaf_system} PROPERTIES COMPILE_DEFINITIONS CLIENT_DLL)
# And its shadow manager table (fwd_shadows.cpp, C7 cluster 12 S2), the same.
set(_ss_fwd_shadows ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_shadows.cpp)
set_source_files_properties(${_ss_fwd_shadows} PROPERTIES COMPILE_DEFINITIONS CLIENT_DLL)
ss_cpptest_engine_headers(fwd_steam_test
    DEFINES SS_FWD_CLIENT_BUILD SS_FWD_SERVES_CLIENT ${_ss_fwd_client_sdl}
    OPTIONS ${_ss_fwd_client_vendor_wno}  # Valve headers on -I, not -isystem
    INCLUDES ${_ss_fwd_client_vendor} sourcesharp/game/shared sourcesharp/host ${PROJECT_SOURCE_DIR}/src/fwd/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/fwd_steam_test.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_client.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_steam.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_physics.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_recipient_filter.cpp
            ${_ss_fwd_leaf_system}
            ${_ss_fwd_shadows})

# C7 cluster 12 L2: the client leaf system's forwarder class, driven as the
# engine and client.so's C++ drive it over a recording managed table, each
# answer's operations carried out in order, and the batches the managed
# registry asks native, against fakes whose recorders sit at the compiler's
# slots. The client's library is compiled in, as fwd_steam_test's is.
ss_cpptest_engine_headers(fwd_leaf_system_test
    DEFINES SS_FWD_CLIENT_BUILD SS_FWD_SERVES_CLIENT CLIENT_DLL ${_ss_fwd_client_sdl}
    OPTIONS ${_ss_fwd_client_vendor_wno}  # Valve headers on -I, not -isystem
    INCLUDES ${_ss_fwd_client_vendor} sourcesharp/game/shared sourcesharp/host
             ${PROJECT_SOURCE_DIR}/src/fwd/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/fwd_leaf_system_test.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_client.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_steam.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_physics.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_recipient_filter.cpp
            ${_ss_fwd_leaf_system}
            ${_ss_fwd_shadows})

# C7 cluster 12 S2: the client shadow manager's table (SSFwd_Shadows_Run),
# each record kind carried out over fakes whose recorders sit at the
# compiler's slots, and the run's bounds as fwd_shadows.h documents them. The
# client's library is compiled in, as fwd_leaf_system_test's is.
ss_cpptest_engine_headers(fwd_shadows_test
    DEFINES SS_FWD_CLIENT_BUILD SS_FWD_SERVES_CLIENT CLIENT_DLL ${_ss_fwd_client_sdl}
    OPTIONS ${_ss_fwd_client_vendor_wno}  # Valve headers on -I, not -isystem
    INCLUDES ${_ss_fwd_client_vendor} sourcesharp/game/shared sourcesharp/host
             ${PROJECT_SOURCE_DIR}/src/fwd/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/fwd_shadows_test.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_client.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_steam.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_physics.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_recipient_filter.cpp
            ${_ss_fwd_leaf_system}
            ${_ss_fwd_shadows})

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
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_physics.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_recipient_filter.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_trace_filter.cpp)

# plan_server_native_v2 T4a: the IRecipientFilter libss_fwd_server builds in a
# managed block's reserved words, driven through its Valve interface as
# IEngineSound::EmitSound drives it. The server's library is compiled in.
ss_cpptest_engine_headers(fwd_recipient_filter_test
    DEFINES SS_FWD_SERVER_BUILD SS_FWD_SERVES_SERVER
    INCLUDES sourcesharp/game/shared sourcesharp/host ${PROJECT_SOURCE_DIR}/src/fwd/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/fwd_recipient_filter_test.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_server.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_physics.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_recipient_filter.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_trace_filter.cpp)

# plan_server_native_v2 T4f: the ITraceFilter libss_fwd_server builds in a
# managed block's reserved words, driven through its Valve interface as
# IEngineTrace::TraceRay drives it, and the handle a trace's entity answers.
# The server's library is compiled in.
ss_cpptest_engine_headers(fwd_trace_filter_test
    DEFINES SS_FWD_SERVER_BUILD SS_FWD_SERVES_SERVER
    INCLUDES sourcesharp/game/shared sourcesharp/host ${PROJECT_SOURCE_DIR}/src/fwd/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/fwd_trace_filter_test.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_server.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_physics.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_recipient_filter.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_trace_filter.cpp)

ss_cpptest_engine_headers(fwd_shim_test
    DEFINES SS_FWD_SERVER_BUILD SS_FWD_SERVES_SERVER
    INCLUDES sourcesharp/game/shared sourcesharp/host ${PROJECT_SOURCE_DIR}/src/fwd/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/fwd_shim_test.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_server.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_physics.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_recipient_filter.cpp
            ${PROJECT_SOURCE_DIR}/src/fwd/native/fwd_trace_filter.cpp)

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

# The server's temp entity tables as stored from a managed plan
# (plan_server_native_v2 T4b): the block read and refused when malformed, and
# each row stored into a SendProp with the real dt_send.cpp proxies.
ss_cpptest_mathlib(tempent_store_test
    INCLUDES sourcesharp/host sourcesharp/game/server sourcesharp/game/shared
    SOURCES sourcesharp/test/tempent_store_test.cpp
            sourcesharp/game/server/shell_tempents_store.cpp
    LIBS ss_cpptest_dtobjs)

# CServerTempEnts (shell_tempents.cpp): the tables and ServerClasses built from managed
# plans, a reload's answer, and the window the root closes.
ss_cpptest_mathlib(tempent_registry_test
    INCLUDES sourcesharp/host sourcesharp/game/server sourcesharp/game/shared
    SOURCES sourcesharp/test/tempent_registry_test.cpp
            sourcesharp/game/server/shell_tempents.cpp
            sourcesharp/game/server/shell_tempents_store.cpp
    LIBS ss_cpptest_dtobjs)

# The client's half: a managed plan's block read with the client's rows, and one
# row stored into one RecvProp, linked with the recv builders and nothing else.
ss_cpptest_engine_headers(tempent_recv_store_test
    DEFINES CLIENT_DLL
    INCLUDES public public/tier0 public/tier1 sourcesharp/host sourcesharp/game/client sourcesharp/game/shared
             ${PROJECT_SOURCE_DIR}/src/engineabi/native/include
    SOURCES sourcesharp/test/tempent_recv_store_test.cpp
            sourcesharp/game/client/shell_tempents_store_client.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/engineabi_recv.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/engineabi_recv_names.cpp)

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

# fx_muzzle_recording_test is RETIRED with fx.cpp (lane c5-particles). It held
# FX_MuzzleEffectAttached's tool-recording block to an IClientEntity resolve with
# its early return before every use; the block left with the file, and the C#
# attached flash (FxEffects.MuzzleEffectAttached) carries no recording tail, so
# there is no resolve left to come undone.

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

# bone_cache_key_test, procedural_bone_rule_test and anim_layer_rule_test LEFT
# with their headers (plan physics_bones_native_zero 1.2, client half): the
# rules are SourceSharp.Shared.Bones' BoneCacheKey, ProceduralBoneRule and
# AnimLayerRule, held by BonesCacheKeyTests, BonesProceduralRuleTests and
# BonesAnimLayerRuleTests, and no native code asks them any more.

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

# The server's load-game player gate (sourcesharp/game/server/shell_savegame.h),
# what is left of the native save code: on a savegame load the engine calls
# ClientActive alone, and ShellSave_LoadGameNeedsPlayer says whether the player
# has to be built there. The save field codec is managed (SaveBlockCodec).
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

# standalone_basevelocity_test went with standalone_basevelocity.h
# (plan_server_native_v2 T4g): a player's base velocity is the managed record's,
# and the push rule's facts are ServerPlayerMovementRulesTests'.

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
# two comparisons over slot numbers; header-only, no engine.
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

# view_effect_reach_test went with view_effect_reach.h (plan_server_native_v2
# T4g): the punch walk is managed (ServerPlayerMovement.ViewPunch) and its reach
# rules are ServerPlayerMovementRulesTests'.

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
    INCLUDES sourcesharp/game/shared sourcesharp/game/server
    SOURCES sourcesharp/test/overlay_library_test.cpp
            sourcesharp/game/shared/overlay_library.cpp)

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
    SOURCES sourcesharp/test/map_entity_block_test.cpp
            game/shared/mapentities_shared.cpp)
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

# break_piece_offset_test moved to C# with its subject (BreakPiecePlacementTests).

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

# plan_server_native_v2 T4c: the save structs the managed save codec
# (SaveGameSession.cs) reads and writes by layout, one case per offset of
# SaveRestoreLayout; SaveGameSessionTests pins the same numbers in C#.
ss_cpptest_engine_headers(save_restore_layout_test
    DEFINES GAME_DLL
    INCLUDES public public/tier0 public/tier1
             ${PROJECT_SOURCE_DIR}/src/engineabi/native/include
    SOURCES sourcesharp/test/save_restore_layout_test.cpp)

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

# The handle ban is a rule on the shipped headers, so it reads vendor/.
ss_vendor_includes(_ss_handle_ban_vendor client game/shared common public public/tier0 public/tier1)

# clang: -Wno-unused-variable for Valve's public/tier1/utlblockmemory.h:350
# (an unused local in a template gcc 10 never instantiated), on a plain -I here.
ss_cpptest(standalone_handle_ban_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub ${_ss_handle_ban_vendor}
             sourcesharp/game/server
    SOURCES sourcesharp/test/standalone_handle_ban_test.cpp)

ss_must_not_compile(standalone_handle_ban
    EXPECT "CHandle<CStandaloneEntity>.*incomplete type|incomplete type 'CHandle<CStandaloneEntity>'"
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
            STANDALONE_HANDLE_BAN_NOCOMPILE=1
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub ${_ss_handle_ban_vendor}
             sourcesharp/game/server
    SOURCES sourcesharp/test/standalone_handle_ban_test.cpp)

# clang: -Wno-unused-variable for Valve's public/tier1/utlblockmemory.h:350
# (an unused local in a template gcc 10 never instantiated), on a plain -I here.
ss_cpptest(standalone_handle_ban_absent
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
            STANDALONE_HANDLE_BAN_NOCOMPILE=1 STANDALONE_HANDLE_BAN_ABSENT=1
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    INCLUDES ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub ${_ss_handle_ban_vendor}
             sourcesharp/game/server
    SOURCES sourcesharp/test/standalone_handle_ban_test.cpp)

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

# THE SHADOW MANAGER'S PENDING QUEUE (C7 cluster 12 S2): shadow_pending_queue.h,
# what the entity shells tell the managed shadow manager between its calls.
# No SDK header, so the shipped queue is the thing under test.
ss_cpptest(shadow_pending_queue_test
    INCLUDES sourcesharp/game/client
    SOURCES sourcesharp/test/shadow_pending_queue_test.cpp)

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

# exit_batches_test STOOD HERE and went with tools/p4-exit-batches, which
# answered which stock TUs were mutually locked -- a question about a removal
# set that no longer exists (ROUTE_TO_DONE §5). component_closure_test and
# tools/4f-component, which this used to point at as its model, STAY.

# plan_entity.md step 8e-physics's ask ledger and its gtest left with native's
# collision handler (plan_physics_bones_native_zero 2.1): the answers are
# managed's PhysicsSolver, counted by clause there.

# standalone_solidity_order_test (lane physics-defects, solidity before the
# physics object) WENT WITH ITS SUBJECT (2026-10-12): CStandaloneEntity::
# InitPhysics is managed's EntityPhysicsCreation, whose order
# EntityPhysicsCreationTests holds in both arms of
# sourcesharp_solidity_before_physics.

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

# WHAT STOCK'S variant_t ANSWERS ABOUT A MAP'S TEXT -- plan_entity.md, the
# variant_t::Convert subsystem. No include path: it needs libc and gtest only,
# which is the point -- it is asserting what the platform does, so anything the
# engine could interpose would spoil the answer. It reads
# sourcesharp/test/variant_convert_cases.tsv, the same table the managed
# VariantAgreementTests reads.
ss_cpptest(variant_convert_test
    LINUX_ONLY "pins glibc's atoi on out-of-range input (the stock Linux behaviour), and the Windows CRT clamps differently, as stock Windows does"
    SOURCES sourcesharp/test/variant_convert_test.cpp)

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
# standalone_bone_matrices_test and standalone_bone_subject_test LEFT with
# their subject (plan physics_bones_native_zero 1.2, client half): the
# StandaloneBoneMatrices / StandaloneIsPoseable / StandaloneStudioName slots
# retired once both sides' skeletons were managed. What they pinned is held by
# ServerSkeletonsTests and ClientSkeletonsTests (BoneMatrices, IsPoseable,
# StudioName) and ClientBoneRecorderTests (the recorder's subject).
# ---------------------------------------------------------------------------

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
            ${PROJECT_SOURCE_DIR}/src/shared/native/alloc_check.cpp
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    DEFINES SOURCESHARP_ALLOC_CHECK
    LIBS ss_alloc_check_wraps)

# The unload report runs after the DSO's static destructors, at dlclose as at
# exit: alloc_unload_probe.so is built like server.so (alloc_check.cpp and its
# wraps) and holds a static std::unordered_map plus one deliberate leak.
# (Linux only, as the checker is: the probe library and the test that loads it.)
if(SS_PLATFORM STREQUAL linux)
add_library(ss_alloc_unload_probe SHARED
    ${PROJECT_SOURCE_DIR}/src/test/native/alloc_unload_probe/alloc_unload_probe.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/alloc_check.cpp)
ss_test_compile_flags(ss_alloc_unload_probe)
target_compile_definitions(ss_alloc_unload_probe PRIVATE SOURCESHARP_ALLOC_CHECK)
target_include_directories(ss_alloc_unload_probe PRIVATE ${PROJECT_SOURCE_DIR}/src/shared/native)
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
            ${PROJECT_SOURCE_DIR}/src/shared/native/alloc_check.cpp
    LIBS ss_alloc_check_wraps)

ss_cpptest(tier1_port_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/tier1_port_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/tier1_port.cpp)

# the DLLs' mathlib members (mathlib_port.cpp, both DLLs', and
# mathlib_port_client.cpp, client.so's alone), linked WITHOUT libmathlib.a:
# it builds only while the port is self-contained, and runs on Windows, where
# no mathlib binary can. mathlib_port_stock_test holds it to stock bit for bit.
ss_cpptest(mathlib_port_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/mathlib_port_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/mathlib_port.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/mathlib_port_client.cpp)

# The port's ss_mathlib:: inlines against the stock libmathlib.a members of the
# same names, side by side in one binary, every output float compared by its
# bits.
ss_cpptest_mathlib(mathlib_port_stock_test
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES sourcesharp/test/mathlib_port_stock_test.cpp)

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
ss_vendor_includes(_ss_convar_vendor server public public/tier0 public/tier1)
ss_cpptest(convar_port_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC GAME_DLL
    # utlblockmemory.h:350's unused variable, silenced by name as server.so does.
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    # convar.h from vendor/, as the DLLs build it: it befriends CCVarRegistry.
    INCLUDES ${_ss_convar_vendor} ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/convar_port_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/convar_port.cpp
            # AutoCompleteSuggest appends through the engine's heap.
            ${PROJECT_SOURCE_DIR}/src/shared/native/engine_heap.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/loaded_symbol.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/tier0_spew_port.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/fatal_report.cpp
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

# tier0_spew_port.cpp: the tier0 functions both DLLs call through dbg.h, and
# tier0_spew_client.cpp's, which only client.so calls (plan_server_novalve.md
# P4, plan_server_native_v2 T5). Links NO tier0, so every
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
            ${PROJECT_SOURCE_DIR}/src/client/native/tier0_spew_client.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/fatal_report.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/loaded_symbol.cpp
    LIBS dl)

# shell_log.cpp: the server's CShellLog and the free logging calls that forward to it,
# against vendor/'s convar.h as server.so builds it.
ss_vendor_includes(_ss_log_vendor server public public/tier0 public/tier1)
ss_cpptest(shell_log_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC GAME_DLL
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    INCLUDES ${_ss_log_vendor} ${PROJECT_SOURCE_DIR}/src/shared/native ${PROJECT_SOURCE_DIR}/src/server/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/shell_log_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/shell_log.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/convar_port.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/engine_heap.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/loaded_symbol.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/tier0_spew_port.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/fatal_report.cpp
    LIBS dl)

# diagnostics_hooks.cpp: the spew hook a side installs through a thunk bound to it, over
# the same port, and what its destruction leaves installed.
ss_cpptest(diagnostics_hooks_test
    DEFINES POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC DEBUG _DEBUG
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/diagnostics_hooks_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/diagnostics_hooks.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/bound_callback.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/tier0_spew_port.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/fatal_report.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/loaded_symbol.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/ss_command_line.cpp
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
            ${PROJECT_SOURCE_DIR}/src/client/native/tier0_thread_port.cpp)

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
# (CThreadFastMutex, DebuggerBreak), defined in client.so (server.so's objects
# import none since plan_server_native_v2 T5). Links NO tier0.
ss_cpptest(tier0_thread_port_test
    LINUX_ONLY "drives the POSIX thread port through pthreads"
    DEFINES POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES public public/tier0 public/tier1 ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/tier0_thread_port_test.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/tier0_thread_port.cpp)

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
# shell_usermessage_send.cpp: CServerUserMessages over the generated engine fake
# (tools/native-fakes), on the server's vendor headers as the fake is.
ss_vendor_includes(_ss_server_vendor server public public/tier0 public/tier1)
ss_cpptest(shell_usermessage_send_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC GAME_DLL
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-nontrivial-memcall -Wno-unused-variable
            -Wno-unused-private-field  # vendor's byte-run fields, as on the server target
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native/sdk/public ${_ss_server_vendor}
             ${PROJECT_SOURCE_DIR}/src/server/native ${PROJECT_SOURCE_DIR}/src/shared/native sourcesharp/host
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/shell_usermessage_send_test.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/shell_usermessage_send.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/ss_recipient_filter.cpp)
# shell_debugoverlays.cpp: CServerDebugOverlays over the overlay library and the
# generated IVDebugOverlay fake (tools/native-fakes), on the server's vendor headers.
ss_cpptest_mathlib(shell_debugoverlays_test
    DEFINES GAME_DLL SS_CBASEENTITY_ALIAS SS_STOCK_PUBLIC_HEADERS
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-nontrivial-memcall -Wno-unused-variable
            -Wno-unused-private-field  # vendor's byte-run fields, as on the server target
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native/sdk/public ${_ss_server_vendor}
             ${PROJECT_SOURCE_DIR}/src/server/native ${PROJECT_SOURCE_DIR}/src/shared/native sourcesharp/host
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/shell_debugoverlays_test.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/shell_debugoverlays.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/overlay_library.cpp)
# shell_gameclients.cpp: CServerGameClients and its root-held forwarder over generated
# fakes (tools/native-fakes); a client with no player throughout. Links tier1 for bf_read.
ss_cpptest_mathlib(shell_gameclients_test
    DEFINES GAME_DLL SS_CBASEENTITY_ALIAS SS_STOCK_PUBLIC_HEADERS
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-nontrivial-memcall -Wno-unused-variable
            -Wno-unused-private-field  # vendor's byte-run fields, as on the server target
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native/sdk/public ${_ss_server_vendor}
             ${PROJECT_SOURCE_DIR}/src/server/native ${PROJECT_SOURCE_DIR}/src/shared/native sourcesharp/host
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/shell_gameclients_test.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/shell_gameclients.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/precache_model.cpp)
# shell_gameents.cpp: CServerGameEnts' transmit walk and its root-held forwarder over
# generated fakes (tools/native-fakes).
ss_cpptest_mathlib(shell_gameents_test
    DEFINES GAME_DLL SS_CBASEENTITY_ALIAS SS_STOCK_PUBLIC_HEADERS
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-nontrivial-memcall -Wno-unused-variable
            -Wno-unused-private-field  # vendor's byte-run fields, as on the server target
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native/sdk/public ${_ss_server_vendor}
             ${PROJECT_SOURCE_DIR}/src/server/native ${PROJECT_SOURCE_DIR}/src/shared/native sourcesharp/host
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/shell_gameents_test.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/shell_gameents.cpp)
# shell_sharedappsystems.cpp: the app systems the engine loads for the game.
ss_cpptest(shell_sharedappsystems_test
    DEFINES GAME_DLL SS_CBASEENTITY_ALIAS SS_STOCK_PUBLIC_HEADERS POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
            _DLL_EXT=.so
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-nontrivial-memcall -Wno-unused-variable
            -Wno-unused-private-field  # vendor's byte-run fields, as on the server target
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native/sdk/public ${_ss_server_vendor}
             ${PROJECT_SOURCE_DIR}/src/server/native ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/shell_sharedappsystems_test.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/shell_sharedappsystems.cpp)
# shell_level.cpp: CServerLevel over generated fakes (tools/native-fakes).
ss_cpptest_mathlib(shell_level_test
    DEFINES GAME_DLL SS_CBASEENTITY_ALIAS SS_STOCK_PUBLIC_HEADERS
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-nontrivial-memcall -Wno-unused-variable
            -Wno-unused-private-field -Wno-inconsistent-missing-override  # vendor headers, as on the server target
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native/sdk/public ${_ss_server_vendor}
             ${PROJECT_SOURCE_DIR}/src/server/native ${PROJECT_SOURCE_DIR}/src/shared/native sourcesharp/host
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/shell_level_test.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/shell_level.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/precache_model.cpp)
# world_queries.cpp: CServerWorldQueries' sky camera and invisible-range count.
ss_cpptest_mathlib(world_queries_test
    DEFINES GAME_DLL SS_CBASEENTITY_ALIAS SS_STOCK_PUBLIC_HEADERS
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-nontrivial-memcall -Wno-unused-variable
            -Wno-unused-private-field -Wno-inconsistent-missing-override -Wno-reorder-ctor  # vendor headers, as on the server target
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native/sdk/public ${_ss_server_vendor}
             ${PROJECT_SOURCE_DIR}/src/server/native ${PROJECT_SOURCE_DIR}/src/shared/native sourcesharp/host
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/world_queries_test.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/world_queries.cpp)
# server_entity_resolver.cpp: handles that name nothing, and the invisible-range count.
ss_cpptest_mathlib(server_entity_resolver_test
    DEFINES GAME_DLL SS_CBASEENTITY_ALIAS SS_STOCK_PUBLIC_HEADERS
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-nontrivial-memcall -Wno-unused-variable
            -Wno-unused-private-field -Wno-inconsistent-missing-override -Wno-reorder-ctor  # vendor headers, as on the server target
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native/sdk/public ${_ss_server_vendor}
             ${PROJECT_SOURCE_DIR}/src/server/native ${PROJECT_SOURCE_DIR}/src/shared/native sourcesharp/host
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/server_entity_resolver_test.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/server_entity_resolver.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/precache_model.cpp)
# precache_model.cpp: CServerModelPrecache over generated fakes (tools/native-fakes).
ss_cpptest_mathlib(precache_model_test
    DEFINES GAME_DLL SS_CBASEENTITY_ALIAS SS_STOCK_PUBLIC_HEADERS
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-nontrivial-memcall -Wno-unused-variable
            -Wno-unused-private-field -Wno-inconsistent-missing-override  # vendor headers, as on the server target
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native/sdk/public ${_ss_server_vendor}
             ${PROJECT_SOURCE_DIR}/src/server/native ${PROJECT_SOURCE_DIR}/src/shared/native sourcesharp/host
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/precache_model_test.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/precache_model.cpp)
# shell_gameevents.cpp: CGameEvents over generated fakes (tools/native-fakes), as the server builds it.
ss_cpptest_mathlib(game_events_test
    DEFINES GAME_DLL SS_CBASEENTITY_ALIAS SS_STOCK_PUBLIC_HEADERS
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-nontrivial-memcall -Wno-unused-variable
            -Wno-unused-private-field -Wno-inconsistent-missing-override  # vendor headers, as on the server target
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native/sdk/public ${_ss_server_vendor}
             ${PROJECT_SOURCE_DIR}/src/server/native ${PROJECT_SOURCE_DIR}/src/shared/native sourcesharp/host
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/game_events_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/shell_gameevents.cpp)
# shell_commandclient.cpp: who may run an admin command, over the generated engine fake.
ss_cpptest_mathlib(command_client_test
    DEFINES GAME_DLL SS_CBASEENTITY_ALIAS SS_STOCK_PUBLIC_HEADERS
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-nontrivial-memcall -Wno-unused-variable
            -Wno-unused-private-field -Wno-inconsistent-missing-override  # vendor headers, as on the server target
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native/sdk/public ${_ss_server_vendor}
             ${PROJECT_SOURCE_DIR}/src/server/native ${PROJECT_SOURCE_DIR}/src/shared/native sourcesharp/host
             ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/command_client_test.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/shell_commandclient.cpp
            ${PROJECT_SOURCE_DIR}/src/server/native/precache_model.cpp)
# bound_callback.cpp: C callbacks bound to an object through a generated x86-64 thunk.
ss_cpptest(bound_callback_test
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/bound_callback_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/bound_callback.cpp)
# ss_command_line.h: this process's own argv from /proc/self/cmdline, in place of
# tier0's CommandLine() (P4). Links no tier0. Built as the client's arm: the
# Value half is client-only since the server gc cull (server.so never reads a value).
ss_cpptest(ss_command_line_test
    LINUX_ONLY "compares with glibc's program_invocation_name"
    DEFINES CLIENT_DLL
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/ss_command_line_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/ss_command_line.cpp)

# plan_server_novalve.md P5 lane SP -- ss_effect_dispatch.h's two wire numbers
# are their fork headers' own values, and ss::CEffectData's defaults are
# stock's (ss_effect_dispatch_test.cpp).
ss_cpptest_engine_headers(ss_effect_dispatch_test
    SOURCES sourcesharp/test/ss_effect_dispatch_test.cpp)

# THE CENSUS TIER (cmake/SourceSharpTests.cmake ss_census): the tests that
# drive the census tools over the real build, 2-15 s each (ctest at
# 79d3b8af: globals_client_ledger 14.8, orphan_reach 8.3, entity_gates 6.9,
# globals_ledger 6.2, component_closure 5.6, entity_census 2.7; every other
# binary under 1.3). make test-full runs them; make test does not.
ss_census(globals_client_ledger_test)

# tier1 and mathlib compile only the members some link pulls in
# (cmake/sdk/tier1.cmake, mathlib.cmake): every consumer of the two archives is
# relinked with --trace, and every member must be pulled in by one of them.
# Census tier, because it relinks about thirty binaries.
ss_cpptest(sdk_lib_members_test
    LINUX_ONLY "relinks the gcc/clang build's consumers with ld's --trace and reads build.ninja"
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/sdk_lib_members_test.cpp)
ss_census(sdk_lib_members_test)
ss_census(globals_ledger_test)

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

# server_state.h's heap tiers (plan_server_native_v2 T6, plan_globals.md §2):
# the level reset is structural only if a Begin after an End is a new object.
# Stock public headers (the farm's), so IGameEntity is their CBaseEntity
# (igameentity_fwd.h).
ss_cpptest(server_state_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC SS_STOCK_PUBLIC_HEADERS GAME_DLL
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-nontrivial-memcall -Wno-unused-variable
    INCLUDES ${PROJECT_SOURCE_DIR}/src/server/native ${PROJECT_SOURCE_DIR}/src/shared/native sourcesharp/host
             ${PROJECT_SOURCE_DIR}/src/test/native/entitylist_stub public public/tier0 public/tier1 common
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/server_state_test.cpp)

# What this platform does not build (LINUX_ONLY, and must-not-compile under
# MSVC), listed at configure time with each reason.
ss_cpptest_report_linux_only()
