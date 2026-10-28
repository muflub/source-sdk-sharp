set(LOCAL ${CMAKE_CURRENT_SOURCE_DIR})

add_library(client_sourcesharp SHARED)
set_target_properties(client_sourcesharp PROPERTIES OUTPUT_NAME client PREFIX "" NO_SONAME ON)
ss_sdk_target(client_sourcesharp PROJECTDIR game/client RPATH_ORIGIN NO_WRAP
    VERSION_SCRIPT ${SS_VENDOR}/client/devtools/version_script.linux.txt)

set(client_sourcesharp_sources
    ${LOCAL}/bitbuf_client.cpp
    ${LOCAL}/client_util_library.cpp
    ${LOCAL}/convar_client.cpp
    ${LOCAL}/engineabi_recv.cpp
    ${LOCAL}/engineabi_recv_names.cpp
    ${LOCAL}/generic_payload_client.cpp
    ${LOCAL}/keyvalues_client.cpp
    ${LOCAL}/managed_runtime_client.cpp
    ${LOCAL}/material_reference_client.cpp
    ${LOCAL}/mathlib_port_client.cpp
    ${LOCAL}/mesh_index_client.cpp
    ${LOCAL}/particles_lib_client.cpp
    ${LOCAL}/randoverride.cpp
    ${LOCAL}/shell_beam_render.cpp
    ${LOCAL}/shell_bounded_cvars_client.cpp
    ${LOCAL}/shell_centerprint_client.cpp
    ${LOCAL}/shell_client.cpp
    ${LOCAL}/shell_client_orphans.cpp
    ${LOCAL}/shell_clientdll.cpp
    ${LOCAL}/shell_clientdll_appsystems.cpp
    ${LOCAL}/shell_clientdll_gameclientexports.cpp
    ${LOCAL}/shell_clientdll_voicehelper.cpp
    ${LOCAL}/shell_cliententitylist.cpp
    ${LOCAL}/shell_clientsystems.cpp
    ${LOCAL}/shell_convars_client.cpp
    ${LOCAL}/shell_debugoverlay_client.cpp
    ${LOCAL}/shell_decals_client.cpp
    ${LOCAL}/shell_detail_props.cpp
    ${LOCAL}/shell_dust_render.cpp
    ${LOCAL}/shell_effect_dispatch_client.cpp
    ${LOCAL}/shell_effect_ledger.cpp
    ${LOCAL}/shell_effects_client.cpp
    ${LOCAL}/shell_effects_list_client.cpp
    ${LOCAL}/shell_effects_sim.cpp
    ${LOCAL}/shell_entitystatics.cpp
    ${LOCAL}/shell_factory_client.cpp
    ${LOCAL}/shell_glow_client.cpp
    ${LOCAL}/shell_hud.cpp
    ${LOCAL}/shell_hud_framework.cpp
    ${LOCAL}/shell_impact_effects.cpp
    ${LOCAL}/shell_input.cpp
    ${LOCAL}/shell_leaf_system_client.cpp
    ${LOCAL}/shell_legacy_tempents_client.cpp
    ${LOCAL}/shell_loadingdisc_client.cpp
    ${LOCAL}/shell_managed_clientsystems_client.cpp
    ${LOCAL}/shell_managed_particles.cpp
    ${LOCAL}/shell_map_material_reload.cpp
    ${LOCAL}/shell_material_proxy_client.cpp
    ${LOCAL}/shell_messagechars_client.cpp
    ${LOCAL}/shell_new_particles_client.cpp
    ${LOCAL}/shell_particle_mgr_client.cpp
    ${LOCAL}/shell_physics_client.cpp
    ${LOCAL}/shell_pixelvis_client.cpp
    ${LOCAL}/shell_player_orphans.cpp
    ${LOCAL}/shell_precipitation_client.cpp
    ${LOCAL}/shell_prediction.cpp
    ${LOCAL}/shell_rawpanel_client.cpp
    ${LOCAL}/shell_recipientfilter_client.cpp
    ${LOCAL}/shell_recvtable.cpp
    ${LOCAL}/shell_render_targets_client.cpp
    ${LOCAL}/shell_render_trace_client.cpp
    ${LOCAL}/shell_rope_render.cpp
    ${LOCAL}/shell_shadows_client.cpp
    ${LOCAL}/shell_soundemitter_client.cpp
    ${LOCAL}/shell_soundenvelope_client.cpp
    ${LOCAL}/shell_soundscape_audio_client.cpp
    ${LOCAL}/shell_sprite_leaf.cpp
    ${LOCAL}/shell_sprite_render.cpp
    ${LOCAL}/shell_standalone_client.cpp
    ${LOCAL}/shell_surface_render.cpp
    ${LOCAL}/shell_te_backends_client.cpp
    ${LOCAL}/shell_te_forwards_client.cpp
    ${LOCAL}/shell_te_pool_client.cpp
    ${LOCAL}/shell_tempents_client.cpp
    ${LOCAL}/shell_tempents_store_client.cpp
    ${LOCAL}/shell_usermessages_client.cpp
    ${LOCAL}/shell_vgui_client.cpp
    ${LOCAL}/shell_view_executor_client.cpp
    ${LOCAL}/shell_view_facts.cpp
    ${LOCAL}/shell_view_scene_client.cpp
    ${LOCAL}/shell_view_targets.cpp
    ${LOCAL}/shell_viewport_client.cpp
    ${LOCAL}/shell_viewrender_client.cpp
    ${LOCAL}/shell_voicestatus_client.cpp
    ${LOCAL}/shell_world_nettable_client.cpp
    ${LOCAL}/shell_worldquery_client.cpp
    ${LOCAL}/shell_worldrender.cpp
    ${LOCAL}/shell_worldtext_render.cpp
    ${LOCAL}/strtools_client.cpp
    ${LOCAL}/tier0_spew_client.cpp
    ${LOCAL}/tier0_thread_client.cpp
    ${LOCAL}/tier0_thread_port.cpp
    ${LOCAL}/tier0_vprof_client.cpp
    ${LOCAL}/tier1_client.cpp
    ${LOCAL}/tier2_names_client.cpp
    ${LOCAL}/tier_bindings_client.cpp
    ${LOCAL}/vstdlib_random_port.cpp
    ${LOCAL}/world_adapter_client.cpp
    ${SHARED}/alloc_check.cpp
    ${SHARED}/collisionproperty.cpp
    ${SHARED}/bound_callback.cpp
    ${SHARED}/console_display_forward.cpp
    ${SHARED}/convar_port.cpp
    ${SHARED}/dt_element_names.cpp
    ${SHARED}/dt_utlvector_common.cpp
    ${SHARED}/engine_heap.cpp
    ${SHARED}/entity_shared_statics.cpp
    ${SHARED}/fatal_report.cpp
    ${SHARED}/gamestringpool.cpp
    ${SHARED}/generic_payload.cpp
    ${SHARED}/loaded_symbol.cpp
    ${SHARED}/mathlib_port.cpp
    ${SHARED}/movevars_shared.cpp
    ${SHARED}/particle_manifest.cpp
    ${SHARED}/shell_acquire_reasons.cpp
    ${SHARED}/shell_diagnostics.cpp
    ${SHARED}/diagnostics_hooks.cpp
    ${SHARED}/shell_log.cpp
    ${SHARED}/shell_gameevents.cpp
    ${SHARED}/shell_gamerules.cpp
    ${SHARED}/shell_internalfactory.cpp
    ${SHARED}/shell_movement.cpp
    ${SHARED}/shell_movement_audit.cpp
    ${SHARED}/shell_physsaverestore_absent.cpp
    ${SHARED}/shell_shared.cpp
    ${SHARED}/shell_vtable_interfaces.cpp
    ${SHARED}/sourcesharp_gamesystem.cpp
    ${SHARED}/ss_command_line.cpp
    ${SHARED}/standalone_player_sync.cpp
    ${SHARED}/steamid.cpp
    ${SHARED}/tier0_spew_port.cpp
    ${SHARED}/tier1_port.cpp
    ${SHARED}/tier_connect.cpp
    ${SHARED}/trace_filter_library.cpp
    ${SHARED}/util_shared_library.cpp
    ${SHARED}/world_net_sync.cpp
)
target_sources(client_sourcesharp PRIVATE ${client_sourcesharp_sources})

option(SS_PSO_PROBES "debug client.so: compile the particles-so spike probes" OFF)
if(SS_CONFIG STREQUAL "debug" AND SS_PSO_PROBES)
  target_sources(client_sourcesharp PRIVATE ${PROJECT_SOURCE_DIR}/tools/probes/client/pso_probes_debug.cpp)
endif()
option(SS_DT_PROBES "debug client.so: compile the C7 detail probe copy" OFF)
if(SS_CONFIG STREQUAL "debug" AND SS_DT_PROBES)
  include(${PROJECT_SOURCE_DIR}/tools/probes/client/probe_copies.cmake)
  ss_client_probe_copy_ours(src/client/native/shell_detail_props.cpp)
endif()
option(SS_CLIENT_PROBES "debug client.so: compile the C0 probes (tools/probes/client)" OFF)
if(SS_CONFIG STREQUAL "debug" AND SS_CLIENT_PROBES)
  target_sources(client_sourcesharp PRIVATE
      ${PROJECT_SOURCE_DIR}/tools/probes/client/c2sims_probes_debug.cpp
      ${PROJECT_SOURCE_DIR}/tools/probes/client/c5particles_probes_debug.cpp
      ${PROJECT_SOURCE_DIR}/tools/probes/client/fx_probes_debug.cpp
      ${PROJECT_SOURCE_DIR}/tools/probes/client/shell_probes_debug.cpp)
  include(${PROJECT_SOURCE_DIR}/tools/probes/client/probe_copies.cmake)
endif()

if(SS_ALLOC_CHECK)
  target_compile_definitions(client_sourcesharp PRIVATE SOURCESHARP_ALLOC_CHECK
      "SOURCESHARP_ALLOC_CHECK_TAG=\"[SourceSharp/AllocCheck/client]\"")
  target_link_libraries(client_sourcesharp PRIVATE ss_alloc_check_wraps)
endif()

target_compile_definitions(client_sourcesharp PRIVATE
    CLIENT_DLL
    DLLNAME=client
    INCLUDED_STEAM2_USERID_STRUCTS
    NO_MALLOC_OVERRIDE
    NO_STRING_T
    SOURCESHARP
    SS_CBASEENTITY_ALIAS
    SS_STOCK_PUBLIC_HEADERS
    USE_WEBM_FOR_REPLAY
    VECTOR
    VERSION_SAFE_STEAM_API_INTERFACES
    _snprintf=use_Q_snprintf_instead
)
if(SS_PLATFORM STREQUAL windows)
  target_compile_options(client_sourcesharp PRIVATE
    "/FI${PROJECT_SOURCE_DIR}/cmake/compat/windows/ss_memalloc_windows.h")
endif()

ss_vendor_includes(client_vendor_includes client common game/client game/client/game_controls game/shared public
    public/tier0 public/tier1 thirdparty/sixensesdk/include vgui2/controls vgui2/include)
target_include_directories(client_sourcesharp PRIVATE
    ${LOCAL}
    ${PROJECT_SOURCE_DIR}/src/host/native
    ${SHARED}
    ${PROJECT_SOURCE_DIR}/src/engineabi/native/include
    ${LOCAL}/sdk/public
    ${LOCAL}/sdk/game/client
    ${SHARED}/sdk/public
    ${SHARED}/sdk/game/shared
    ${client_vendor_includes}
    ${LOCAL}/particles_lib/include
)

ss_link_host(client_sourcesharp)
if(TARGET ss_particles)
  target_link_libraries(client_sourcesharp PRIVATE ss_particles)
endif()
target_link_libraries(client_sourcesharp PRIVATE ss_fwd_client)
if(SS_PLATFORM STREQUAL linux)
  target_link_libraries(client_sourcesharp PRIVATE dl)
elseif(SS_PLATFORM STREQUAL windows)
  target_link_libraries(client_sourcesharp PRIVATE sdk::z)
else()
  target_link_libraries(client_sourcesharp PRIVATE z)
endif()
if(SS_IS_MSVC)
  set_property(SOURCE
      ${LOCAL}/tier0_spew_client.cpp
      ${LOCAL}/tier0_thread_client.cpp
      ${LOCAL}/tier0_thread_port.cpp
      ${LOCAL}/tier0_vprof_client.cpp
      ${LOCAL}/tier_bindings_client.cpp
      ${SHARED}/tier0_spew_port.cpp
    TARGET_DIRECTORY client_sourcesharp
    APPEND PROPERTY COMPILE_DEFINITIONS STATIC_TIER0)
endif()
