set(LOCAL ${CMAKE_CURRENT_SOURCE_DIR})

add_library(server_sourcesharp SHARED)
set_target_properties(server_sourcesharp PROPERTIES OUTPUT_NAME server PREFIX "" NO_SONAME ON)
ss_sdk_target(server_sourcesharp PROJECTDIR game/server RPATH_ORIGIN NO_WRAP
    VERSION_SCRIPT ${PROJECT_SOURCE_DIR}/cmake/version_script_server.txt)

set(server_sourcesharp_sources
    ${LOCAL}/exit_watch.cpp
    ${LOCAL}/managed_runtime_server.cpp
    ${LOCAL}/physics.cpp
    ${LOCAL}/precache_model.cpp
    ${LOCAL}/server_util_library.cpp
    ${LOCAL}/shell_absent.cpp
    ${LOCAL}/shell_clientsteps.cpp
    ${LOCAL}/shell_commandclient.cpp
    ${LOCAL}/shell_debugoverlays.cpp
    ${LOCAL}/shell_dispatch.cpp
    ${LOCAL}/shell_engineboundary.cpp
    ${LOCAL}/shell_engineservices.cpp
    ${LOCAL}/shell_entityapi.cpp
    ${LOCAL}/shell_entityslots.cpp
    ${LOCAL}/shell_factory.cpp
    ${LOCAL}/shell_frame.cpp
    ${LOCAL}/shell_gameclients.cpp
    ${LOCAL}/shell_gamedll.cpp
    ${LOCAL}/shell_gameents.cpp
    ${LOCAL}/shell_gametags.cpp
    ${LOCAL}/shell_globals.cpp
    ${LOCAL}/shell_level.cpp
    ${LOCAL}/shell_mapparse.cpp
    ${LOCAL}/shell_nettable.cpp
    ${LOCAL}/shell_physicsobjects.cpp
    ${LOCAL}/shell_scripted.cpp
    ${LOCAL}/shell_sentences.cpp
    ${LOCAL}/shell_server.cpp
    ${LOCAL}/shell_sharedappsystems.cpp
    ${LOCAL}/shell_soundent.cpp
    ${LOCAL}/shell_standalone.cpp
    ${LOCAL}/shell_standalone_base.cpp
    ${LOCAL}/shell_standalone_class.cpp
    ${LOCAL}/shell_standalone_hierarchy.cpp
    ${LOCAL}/shell_standalone_player.cpp
    ${LOCAL}/shell_stringtables.cpp
    ${LOCAL}/shell_tempents.cpp
    ${LOCAL}/shell_tempents_store.cpp
    ${LOCAL}/shell_transmitsteps.cpp
    ${LOCAL}/shell_usermessage_send.cpp
    ${LOCAL}/shell_world_nettable.cpp
    ${LOCAL}/shell_worldquery.cpp
    ${LOCAL}/ss_recipient_filter.cpp
    ${LOCAL}/standalone_relays.cpp
    ${LOCAL}/world_adapter.cpp
    ${LOCAL}/world_subject_dll.cpp
    ${SHARED}/alloc_check.cpp
    ${SHARED}/console_display_forward.cpp
    ${SHARED}/convar_port.cpp
    ${SHARED}/dt_send.cpp
    ${SHARED}/engine_heap.cpp
    ${SHARED}/entity_shared_statics.cpp
    ${SHARED}/fatal_report.cpp
    ${SHARED}/gamestringpool.cpp
    ${SHARED}/generic_payload.cpp
    ${SHARED}/loaded_symbol.cpp
    ${SHARED}/mathlib_port.cpp
    ${SHARED}/movevars_shared.cpp
    ${SHARED}/overlay_library.cpp
    ${SHARED}/shell_acquire_reasons.cpp
    ${SHARED}/shell_diagnostics.cpp
    ${SHARED}/shell_gameevents.cpp
    ${SHARED}/shell_gamerules.cpp
    ${SHARED}/shell_internalfactory.cpp
    ${SHARED}/shell_movement.cpp
    ${SHARED}/shell_physsaverestore_absent.cpp
    ${SHARED}/shell_shared.cpp
    ${SHARED}/shell_vtable_interfaces.cpp
    ${SHARED}/sourcesharp_gamesystem.cpp
    ${SHARED}/ss_command_line.cpp
    ${SHARED}/standalone_player_sync.cpp
    ${SHARED}/takedamageinfo_library.cpp
    ${SHARED}/tier0_spew_port.cpp
    ${SHARED}/tier1_port.cpp
    ${SHARED}/tier_connect.cpp
    ${SHARED}/util_shared_library.cpp
    ${SHARED}/world_net_sync.cpp
)
target_sources(server_sourcesharp PRIVATE ${server_sourcesharp_sources})

set(server_sourcesharp_debug_sources
    ${LOCAL}/shell_debugoverlays_debug.cpp
    ${LOCAL}/shell_engineboundary_debug.cpp
    ${LOCAL}/shell_gamedll_debug.cpp
    ${LOCAL}/shell_gameents_debug.cpp
    ${LOCAL}/shell_nettable_debug.cpp
    ${LOCAL}/shell_standalone_class_debug.cpp
    ${LOCAL}/shell_standalone_debug.cpp
    ${LOCAL}/shell_standalone_player_debug.cpp
    ${LOCAL}/shell_tempents_debug.cpp
    ${SHARED}/gamestringpool_debug.cpp
    ${SHARED}/generic_payload_debug.cpp
    ${SHARED}/shell_diagnostics_debug.cpp
    ${SHARED}/shell_movement_debug.cpp
)
if(SS_CONFIG STREQUAL "debug")
  target_sources(server_sourcesharp PRIVATE ${server_sourcesharp_debug_sources})
endif()

if(SS_ALLOC_CHECK)
  target_compile_definitions(server_sourcesharp PRIVATE SOURCESHARP_ALLOC_CHECK)
  target_link_libraries(server_sourcesharp PRIVATE ss_alloc_check_wraps)
endif()

target_compile_definitions(server_sourcesharp PRIVATE
    DLLNAME=server
    GAME_DLL
    INCLUDED_STEAM2_USERID_STRUCTS
    NO_MALLOC_OVERRIDE
    SOURCESHARP
    SOURCESHARP_SHELL_GAMEDLL
    SS_CBASEENTITY_ALIAS
    SS_ENGINEABI
    SS_STOCK_PUBLIC_HEADERS
    SWDS
    VECTOR
    VERSION_SAFE_STEAM_API_INTERFACES
    _snprintf=use_Q_snprintf_instead
)
if(SS_PLATFORM STREQUAL windows)
  target_compile_options(server_sourcesharp PRIVATE
    "/FI${PROJECT_SOURCE_DIR}/cmake/compat/windows/ss_memalloc_windows.h")
endif()

ss_vendor_includes(server_vendor_includes server public public/tier0 public/tier1)
set_property(GLOBAL PROPERTY SS_SERVER_NOVALVE_INCLUDES
    ${SHARED}/sdk/public ${server_vendor_includes})
target_include_directories(server_sourcesharp PRIVATE
    ${SHARED}/sdk/public
    ${server_vendor_includes}
    ${PROJECT_SOURCE_DIR}/src/host/native
    ${LOCAL}
    ${SHARED}
    ${PROJECT_SOURCE_DIR}/src/engineabi/native/include
)

ss_link_host(server_sourcesharp)
target_link_libraries(server_sourcesharp PRIVATE ss_fwd_server)
if(SS_PLATFORM STREQUAL linux)
  target_link_libraries(server_sourcesharp PRIVATE dl)
endif()

if(SS_CONFIG STREQUAL "release" AND SS_PLATFORM STREQUAL linux AND SS_IS_CLANG AND SS_LINKER STREQUAL "lld")
  target_link_options(server_sourcesharp PRIVATE -Wl,--icf=safe)
endif()
if(SS_CONFIG STREQUAL "release")
  target_compile_options(server_sourcesharp PRIVATE $<IF:$<BOOL:${SS_IS_MSVC}>,/GR-,-fno-rtti>)
endif()
if(NOT SS_IS_MSVC)
  target_compile_options(server_sourcesharp PRIVATE -Wunused-function -Wunused-variable)
endif()
if(SS_IS_MSVC)
  set_property(SOURCE ${SHARED}/tier0_spew_port.cpp TARGET_DIRECTORY server_sourcesharp
    APPEND PROPERTY COMPILE_DEFINITIONS STATIC_TIER0)
endif()
if(SS_IS_CLANG)
  set_source_files_properties(${LOCAL}/physics.cpp TARGET_DIRECTORY server_sourcesharp
    PROPERTIES COMPILE_OPTIONS "-Wno-delete-non-abstract-non-virtual-dtor")
endif()
