# client.so, the client DLL.
#
# Generated once from VPC's ninja graph (client_csharp.vpc) in Phase 6b and
# hand-owned since.

add_library(client_sourcesharp SHARED)
set_target_properties(client_sourcesharp PROPERTIES OUTPUT_NAME client PREFIX "" NO_SONAME ON)
# NO_WRAP, as server.so (plan_client_novalve C9): the --wrap= set routes file
# calls through libtier0's path-case fixups, which client.so no longer links.
# The only wrapped calls left were our own (ss_command_line, fatal_report,
# alloc_check, tier0_thread_port, ss_platform.h's page probe), on /proc paths
# and paths from the environment, which server.so makes unwrapped too.
ss_sdk_target(client_sourcesharp PROJECTDIR game/client RPATH_ORIGIN NO_WRAP)

# 246 translation units, sorted: the Valve files that still come from the fork
# first, then this repo's, each group alphabetically (link order is this order;
# the live gate runs on it). This repo's files are named where they live
# (${CMAKE_CURRENT_SOURCE_DIR}/..., ${PROJECT_SOURCE_DIR}/src/...); only the
# Valve files that still come from the fork are named through the farm
# (${SS_SRC}/..., sdk-links/). tools/cmakegraph.py and its twins map a local
# path back to its farm spelling through tools/module-map.tsv.
set(client_sourcesharp_sources
    ${SS_SRC}/game/client/c_pixel_visibility.cpp
    ${SS_SRC}/game/client/clientleafsystem.cpp
    ${SS_SRC}/game/client/clientshadowmgr.cpp
    ${SS_SRC}/game/client/glow_overlay.cpp
    ${SS_SRC}/game/client/rendertexture.cpp
    ${SS_SRC}/game/client/ScreenSpaceEffects.cpp
    ${SS_SRC}/game/client/vgui_game_viewport.cpp
    ${SS_SRC}/game/client/warp_overlay.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/client_util_library.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/managed_runtime_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_beam_render.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_client_orphans.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_clientdll.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_clientdll_appsystems.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_clientdll_gameclientexports.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_clientdll_voicehelper.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_cliententitylist.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_clientsystems.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_decals_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_detail_props.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_dust_render.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_effect_dispatch_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_effect_ledger.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_effects_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_effects_list_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_effects_sim.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_entitystatics.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_explosion_overlay.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_flashlight_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_hud.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_hud_framework.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_impact_effects.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/md5_pseudorandom.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_input.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_managed_particles.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_map_material_reload.cpp
    # The material proxy trampoline (lane c5-proxies): one IMaterialProxy class
    # registered under every proxy name; each Init and bind asks MaterialProxyHost.
    # It replaced 22 fork TUs (*proxy.cpp, mathproxy.cpp, functionproxy.cpp, ...)
    # and shell_materialproxies.cpp (docs/client_evictions.md). It also holds
    # CMatStubHandler, which cl_mat_stub.cpp held (mat_stub is a managed convar).
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_material_proxy_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_new_particles_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_particle_mgr_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_player_orphans.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_precipitation_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_physics_client.cpp
    # IShimConVarsClient (C8): the convar watch and managed's decided values.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_convars_client.cpp
    # The server's tier connects, compiled for the client too (plan_client_novalve C9).
    ${PROJECT_SOURCE_DIR}/src/shared/native/tier_connect.cpp
    # tier2's members, ours (C9): the index generators and the names over them.
    ${CMAKE_CURRENT_SOURCE_DIR}/mesh_index_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tier2_names_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/material_reference_client.cpp
    # particles.a's names, forwarded to libss_particles.so (plan_particles_so.md;
    # what particles.a imports from tier2 is stubbed inside that library).
    ${CMAKE_CURRENT_SOURCE_DIR}/particles_lib_client.cpp
    # tier0 and vstdlib, the server's ports (plan_client_novalve C9, user
    # decision 2026-10-12): the spew/assert entry points and the thread and
    # platform calls both DLLs make, and client.so's own random stream.
    ${PROJECT_SOURCE_DIR}/src/shared/native/tier0_spew_port.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/tier0_thread_port.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/vstdlib_random_port.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tier0_spew_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tier0_thread_client.cpp
    # The client links neither library (C9): its own vprof profile, never
    # enabled (user ruling 2026-10-18), and its bindings to the tier0 and
    # vstdlib the engine loaded.
    ${CMAKE_CURRENT_SOURCE_DIR}/tier0_vprof_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tier_bindings_client.cpp
    # tier1, the server's ports (plan_client_novalve C9's tier1 step), and the
    # members of the same tier1 objects only the client calls.
    ${PROJECT_SOURCE_DIR}/src/shared/native/convar_port.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/tier1_port.cpp
    # mathlib, the server's port (C9): libmathlib.a is off the link line.
    ${PROJECT_SOURCE_DIR}/src/shared/native/mathlib_port.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/strtools_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/keyvalues_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/tier1_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_factory_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/bitbuf_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/convar_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_bounded_cvars_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_prediction.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_recipientfilter_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_recvtable.cpp
    # The recv tables' builders and their Valve names (plan_client_novalve C8, engineabi/recv.h).
    ${CMAKE_CURRENT_SOURCE_DIR}/engineabi_recv.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/engineabi_recv_names.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/generic_payload_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_renderable_facts.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_rope_render.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_soundscape_audio_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_centerprint_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_loadingdisc_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_debugoverlay_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_viewport_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_vgui_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_voicestatus_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_messagechars_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_rawpanel_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_sprite_leaf.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_sprite_render.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_standalone_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_surface_render.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_te_backends_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_te_forwards_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_te_pool_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_legacy_tempents_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_tempents_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_tools_off_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_usermessages_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_view_facts.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_view_targets.cpp
    # C7 (§3.3): View_Render's body -- the per-frame path question and the
    # render plan's executor -- and the recorder that measures both paths.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_view_executor_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_viewrender_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_view_scene_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_render_trace_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_world_nettable_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_worldquery_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_worldrender.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_worldtext_render.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_soundenvelope_client.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/world_adapter_client.cpp
    # The allocation checker (plan_server_novalve.md §3.6), taken for the client
    # in plan_client_novalve C1 the way the server took it: debug only, the TU
    # compiles to nothing unless SS_ALLOC_CHECK (below) puts the define and the
    # wraps on this target. Named at its server path, as src/test/native does.
    ${PROJECT_SOURCE_DIR}/src/server/native/alloc_check.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/collisionproperty.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/collisionutils.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/dt_utlvector_common.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/dt_element_names.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/entity_shared_statics.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/gamestringpool.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/generic_payload.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/movevars_shared.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/particle_manifest.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/randoverride.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_acquire_reasons.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/fatal_report.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_diagnostics.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/ss_command_line.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/engine_heap.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/loaded_symbol.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_gameevents.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_gamerules.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_internalfactory.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_movement.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_movement_audit.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_physsaverestore_absent.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_savegame.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_shared.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_vtable_interfaces.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/sound_emitter.cpp
    # docs/todo.md's console capture: the IConsoleDisplayFunc that forwards the
    # engine's prints to the load's IManagedConsoleOutputClient; installed/removed
    # by sourcesharp_gamesystem.cpp, its only includer.
    ${PROJECT_SOURCE_DIR}/src/shared/native/console_display_forward.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/sourcesharp_gamesystem.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/standalone_player_sync.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/steamid.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/takedamageinfo_library.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/trace_filter_library.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/util_shared_library.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/world_net_sync.cpp
)
target_sources(client_sourcesharp PRIVATE ${client_sourcesharp_sources})

# DEBUG-ONLY PROBES, OPT-IN: tools/probes/client/shell_probes_debug.cpp holds
# the C0 probes of plan_client_novalve.md §6 item 7 (answers in its §8). It is
# measurement tooling, not shim, so it lives under tools/ (no shim walk, ledger
# or ratchet over src/client/native counts it). A release client.so never
# compiles it, and a debug one only when configured with
#   cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=ON
# (tools/probes/c0-session says how to run the probes and switch back). Added
# after every other source so its static initialiser runs after every
# registration it wraps. Not named <target>_debug_sources on purpose:
# tools/cmakegraph.py and its twins count that list into every debug build,
# and this one is opt-in.
# plan_particles_so.md's spike probes (tools/probes/client/pso_probes_debug.cpp):
# the draw-call and hop counters of the spike's A/B (plan_particles_so.md §7). Opt-in and
# debug-only like the C0 probes, and on their own switch so they build without
# the probe copies.
option(SS_PSO_PROBES "debug client.so: compile the particles-so spike probes" OFF)
if(SS_CONFIG STREQUAL "debug" AND SS_PSO_PROBES)
  target_sources(client_sourcesharp PRIVATE ${PROJECT_SOURCE_DIR}/tools/probes/client/pso_probes_debug.cpp)
endif()
# C7 detail's A/B probe: a probe copy of shell_detail_props.cpp logging every
# detail vertex and model-prop draw (tools/probes/client/detail_probe.h,
# cs_cl_probe_detail). Its base half, detailobjectsystem.cpp.probe.diff,
# applies to the fork's file at 82d2df06c, the eviction's parent.
option(SS_DT_PROBES "debug client.so: compile the C7 detail probe copy" OFF)
if(SS_CONFIG STREQUAL "debug" AND SS_DT_PROBES)
  include(${PROJECT_SOURCE_DIR}/tools/probes/client/probe_copies.cmake)
  ss_client_probe_copy_ours(src/client/native/shell_detail_props.cpp)
endif()
option(SS_CLIENT_PROBES "debug client.so: compile the C0 probes (tools/probes/client)" OFF)
if(SS_CONFIG STREQUAL "debug" AND SS_CLIENT_PROBES)
  target_sources(client_sourcesharp PRIVATE ${PROJECT_SOURCE_DIR}/tools/probes/client/shell_probes_debug.cpp)
  # lane c2-sims: the stock-oracle recorders (decals, the sims, sound)
  target_sources(client_sourcesharp PRIVATE ${PROJECT_SOURCE_DIR}/tools/probes/client/c2sims_probes_debug.cpp)
  # lane c2-fx's oracle probes (the effect dispatch table, the fx decisions)
  target_sources(client_sourcesharp PRIVATE ${PROJECT_SOURCE_DIR}/tools/probes/client/fx_probes_debug.cpp)
  # lane c5-particles's oracle probes (the particle query particles.a calls)
  target_sources(client_sourcesharp PRIVATE ${PROJECT_SOURCE_DIR}/tools/probes/client/c5particles_probes_debug.cpp)
  # lane c6-input's oracle probe (the stock input code, the rumble mixer):
  # tools/probes/client/c6input_probes_debug.cpp with the probe copies of
  # in_main/in_camera/in_steamcontroller/c_rumble.cpp, recorded at a9d8f9fd
  # (f9d7f6eb). Every TU it hooks has left the build, so it applies there.
  include(${PROJECT_SOURCE_DIR}/tools/probes/client/probe_copies.cmake)
endif()

# The allocation checker, as server.so carries it (cmake/AllocCheck.cmake): on
# by default in debug, never in release. Its report lines carry their own tag so
# a listen process's two reports (one log) stay apart; tools/novalve-snapshot
# --client checks this one against docs/alloc-live-allow-client.tsv.
if(SS_ALLOC_CHECK)
  target_compile_definitions(client_sourcesharp PRIVATE SOURCESHARP_ALLOC_CHECK
      "SOURCESHARP_ALLOC_CHECK_TAG=\"[SourceSharp/AllocCheck/client]\"")
  target_link_libraries(client_sourcesharp PRIVATE ss_alloc_check_wraps)
endif()

target_compile_definitions(client_sourcesharp PRIVATE
    DLLNAME=client
    NO_STRING_T
    CLIENT_DLL
    VECTOR
    VERSION_SAFE_STEAM_API_INTERFACES
    _snprintf=use_Q_snprintf_instead
    INCLUDED_STEAM2_USERID_STRUCTS
    USE_WEBM_FOR_REPLAY
    SOURCESHARP
    SS_CBASEENTITY_ALIAS
    # plan_client_novalve C9 (g_pMemAlloc), as the server's P5 step 5:
    # tier0/memalloc.h's arm for a module with no tier0 allocator. Its
    # MemAlloc_* hooks and MEM_ALLOC_CREDIT_* become no-ops and its
    # allocation macros malloc, so our objects stop naming g_pMemAlloc.
    # client.so has used glibc's allocator since memoverride.cpp left (C1);
    # the allocation checker measured no mismatch and no foreign free before
    # and after this switch (live, see the commit).
    NO_MALLOC_OVERRIDE
)
# On Windows memalloc.h's arm for such a module is empty (Valve never built
# Windows without tier0's allocator): cmake/compat/windows supplies the
# MemAlloc_* set POSIX gets, on the CRT, forced in ahead of every source.
if(SS_PLATFORM STREQUAL windows)
  target_compile_options(client_sourcesharp PRIVATE
    "/FI${PROJECT_SOURCE_DIR}/cmake/compat/windows/ss_memalloc_windows.h")
endif()

target_include_directories(client_sourcesharp PRIVATE
    ${PROJECT_SOURCE_DIR}/src/client/native
    ${PROJECT_SOURCE_DIR}/src/host/native
    ${PROJECT_SOURCE_DIR}/src/shared/native
    # plan_client_novalve.md 3.1: the engineabi module, #included as
    # "engineabi/<what it declares>.h".
    ${PROJECT_SOURCE_DIR}/src/engineabi/native/include
    ${SS_SRC}/common
    ${SS_SRC}/game/client
    ${SS_SRC}/game/client/game_controls
    ${SS_SRC}/game/shared
    ${SS_SRC}/public
    ${SS_SRC}/public/tier0
    ${SS_SRC}/public/tier1
    ${SS_SRC}/thirdparty/sixensesdk/include
    ${SS_SRC}/vgui2/controls
    ${SS_SRC}/vgui2/include
)

# The managed host, linked by target (ss_link_host).
ss_link_host(client_sourcesharp)

# The tier0 ports client.dll defines itself read tier0's headers with
# STATIC_TIER0 on MSVC: the declarations are plain there, so the definitions
# are not exported (MSVC exports a definition of a dllimport-declared function)
# and client.dll exports CreateInterface alone (game_dll_exports_test). Every
# other file's import binds to them through SS_BIND_IMPORT. A compile
# definition, so it is in force before the forced memalloc header reads tier0.
if(SS_IS_MSVC)
  set_property(SOURCE
      ${PROJECT_SOURCE_DIR}/src/shared/native/tier0_spew_port.cpp
      ${PROJECT_SOURCE_DIR}/src/shared/native/tier0_thread_port.cpp
      ${CMAKE_CURRENT_SOURCE_DIR}/tier0_spew_client.cpp
      ${CMAKE_CURRENT_SOURCE_DIR}/tier0_thread_client.cpp
      ${CMAKE_CURRENT_SOURCE_DIR}/tier0_vprof_client.cpp
      ${CMAKE_CURRENT_SOURCE_DIR}/tier_bindings_client.cpp
    TARGET_DIRECTORY client_sourcesharp
    APPEND PROPERTY COMPILE_DEFINITIONS STATIC_TIER0)
endif()

# VPC linked the Valve libraries inside -Wl,--start-group ... --end-group: they
# refer to each other in both directions, and GNU ld (the CI link) resolves an
# archive only against what came before it.
#
# particles.a and dmxloader.a are libss_particles.so's (plan_particles_so.md,
# particles_lib/): client.so links it, and particles_lib_client.cpp defines
# the names client.so's objects take from particles.a as forwards to it.
target_include_directories(client_sourcesharp PRIVATE ${PROJECT_SOURCE_DIR}/src/client/native/particles_lib/include)
# Linked like any library: SSParticleLib_Interface resolves when client.so loads
# (DT_NEEDED on libss_particles.so's SONAME, beside client.so by its RUNPATH
# $ORIGIN; ss_particles.lib on Windows). No libss_particles on macOS
# (particles_lib/CMakeLists.txt): the client links there with dynamic lookup,
# and the call fails at run time, as it would with the library missing.
if(TARGET ss_particles)
  target_link_libraries(client_sourcesharp PRIVATE ss_particles)
endif()
# plan_engine_forwarders.md: the client's forwarder library (libss_fwd_client,
# with the Steam callback), whose tables the internal factory serves as the
# server's does, linked the same way.
target_link_libraries(client_sourcesharp PRIVATE ss_fwd_client)
# dl is glibc's: dlopen/dlsym/dladdr/dlclose are libdl's on the Steam
# Runtime's glibc (2.31; libc's from 2.34). Nothing in client.so calls librt
# or zlib, so neither is linked on Linux; zlib is Valve's libz.lib on Windows
# (cmake/sdk).
if(SS_PLATFORM STREQUAL linux)
  target_link_libraries(client_sourcesharp PRIVATE dl)
elseif(SS_PLATFORM STREQUAL windows)
  target_link_libraries(client_sourcesharp PRIVATE sdk::z)
else()
  target_link_libraries(client_sourcesharp PRIVATE z)
endif()
# plan_client_novalve C9: the client links no Valve tier library, as the
# server links none: tier0_vprof_client.cpp and tier_bindings_client.cpp take
# tier0's and vstdlib's place (on Windows: CI-verified only), and nothing left
# in client.so takes a member of libtier1.a.
