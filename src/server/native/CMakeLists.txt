# server.so, the game DLL.
#
# Generated once from VPC's ninja graph (server_csharp.vpc) in Phase 6b and
# hand-owned since.

add_library(server_sourcesharp SHARED)
set_target_properties(server_sourcesharp PROPERTIES OUTPUT_NAME server PREFIX "" NO_SONAME ON)
ss_sdk_target(server_sourcesharp PROJECTDIR game/server RPATH_ORIGIN NO_WRAP
    VERSION_SCRIPT ${PROJECT_SOURCE_DIR}/cmake/version_script_server.txt)

# 107 translation units, sorted: the Valve files that still come from the fork
# first, then this repo's, each group alphabetically (link order is this order;
# the live gate runs on it). This repo's files are named where they live
# (${CMAKE_CURRENT_SOURCE_DIR}/..., ${PROJECT_SOURCE_DIR}/src/...); only the
# Valve files that still come from the fork are named through the farm
# (${SS_SRC}/..., sdk-links/). tools/cmakegraph.py and its twins map a local
# path back to its farm spelling through tools/module-map.tsv.
set(server_sourcesharp_sources
    ${CMAKE_CURRENT_SOURCE_DIR}/alloc_check.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/studio_mdlcache.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/tier0_spew_port.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/tier0_thread_port.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/tier1_port.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_factory.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/convar_port.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/tier_connect.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/baseentity_statics.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/managed_runtime_server.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/physics.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/plugin_check.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/precache_model.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/sendproxy.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/server_util_library.cpp
    # ServerNetworkProperty.cpp: GONE (P5 lane E) -- server.so never constructed a
    # CServerNetworkProperty (only CBaseEntity owned one); shell_gameents.cpp's
    # transmit walk was its last caller, through casts of other objects' networkables.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_absent.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_areaportal.cpp
    # shell_breakpieces.cpp: GONE -- break pieces are C#'s (BreakModels); its two
    # slots are stubs in shell_engineservices.cpp.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_clientsteps.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_commandclient.cpp
    # shell_constraints.cpp: GONE (plan_physics_bones_native_zero 2.4) -- the
    # constraint and spring tables are managed's (PhysicsConstraints, calling
    # vphysics through its vtables) and IPhysicsConstraintEvent is managed-built.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_debugoverlays.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_decals.cpp
    # The dev API's exit record: who asked the process to end (signal sender,
    # SDL quit / window close, a fatal stop). Records facts only.
    ${CMAKE_CURRENT_SOURCE_DIR}/exit_watch.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_dispatch.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_engineboundary.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_engineservices.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_entityapi.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_entityslots.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_frame.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_gameclients.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_gamedll.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_gamedll_convars.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_gameents.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_gametags.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_globals.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_level.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_mapparse.cpp
    # shell_motioncontrollers.cpp: GONE (plan_physics_bones_native_zero 2.4) --
    # the motion controllers are managed's (PhysicsMotionControllers) and
    # vphysics asks managed's own IMotionEvent table for the force.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_nettable.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_physicsobjects.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_playerinfo.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_playerlocaldata.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_playerslots.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_recipientfilter.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/ss_recipient_filter.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_restoreentities.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_scripted.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_sentences.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_server.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_serversystems.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_sharedappsystems.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_sound.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_soundent.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_soundenvelope.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_standalone.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_standalone_base.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_standalone_class.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_standalone_hierarchy.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_standalone_player.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_stringtables.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_te_forwards.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_tempents.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_transmitsteps.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_usermessage_send.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_world_nettable.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_worldquery.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/standalone_relays.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/toucher.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/world_adapter.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/world_subject_dll.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/collisionproperty.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/collisionutils.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/dt_send.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/dt_utlvector_common.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/engine_heap.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/loaded_symbol.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/ss_command_line.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/entity_shared_statics.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/gamestringpool.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/generic_payload.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/model_descriptor.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/movevars_shared.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/overlay_library.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_acquire_reasons.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/fatal_report.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_diagnostics.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_gameevents.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_gamerules.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_internalfactory.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_movement.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_physsaverestore_absent.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_savegame.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_shared.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_vtable_interfaces.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/sound_emitter.cpp
    # docs/todo.md's console capture: the IConsoleDisplayFunc that forwards the
    # engine's prints to the load's IManagedConsoleOutput; installed/removed by
    # sourcesharp_gamesystem.cpp, its only includer.
    ${PROJECT_SOURCE_DIR}/src/shared/native/console_display_forward.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/sourcesharp_gamesystem.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/standalone_player_sync.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/studio.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/takedamageinfo_library.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/trace_filter_library.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/util_shared_library.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/world_net_sync.cpp
)
target_sources(server_sourcesharp PRIVATE ${server_sourcesharp_sources})

# DEBUG-ONLY SOURCES -- the diagnostic console commands (user ruling 2026-10-01:
# "move debug commands to debug files and only include them for compile in
# cmake for debug builds"). Each is <host>_debug.cpp beside the file whose state
# it reads. A release server.so compiles none of them, and SourceSharp.Server's
# NativeCommands declares their rows under #if DEBUG. tools/cmakegraph.py and
# its twins read this block and count it only for a debug build directory.
set(server_sourcesharp_debug_sources
    ${CMAKE_CURRENT_SOURCE_DIR}/physics_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_areaportal_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_debugoverlays_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_engineboundary_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_entityslots_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_gamedll_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_gameents_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_nettable_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_playerinfo_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_scripted_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_standalone_base_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_standalone_class_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_standalone_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_standalone_player_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_te_forwards_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_tempents_debug.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/world_adapter_debug.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/gamestringpool_debug.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/generic_payload_debug.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_diagnostics_debug.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_movement_debug.cpp
)
if(SS_CONFIG STREQUAL "debug")
  target_sources(server_sourcesharp PRIVATE ${server_sourcesharp_debug_sources})
  list(APPEND server_sourcesharp_sources ${server_sourcesharp_debug_sources})
endif()

# The allocation checker (plan_server_novalve.md §3.6, cmake/AllocCheck.cmake):
# debug only. alloc_check.cpp is always in the list above (the list is what
# tools/cmakegraph.py reads) and compiles to nothing unless SS_ALLOC_CHECK puts
# the define and the wraps on this target. It holds no static object with a
# constructor, so its place in the list moves no static-init order.
if(SS_ALLOC_CHECK)
  target_compile_definitions(server_sourcesharp PRIVATE SOURCESHARP_ALLOC_CHECK)
  target_link_libraries(server_sourcesharp PRIVATE ss_alloc_check_wraps)
endif()

target_compile_definitions(server_sourcesharp PRIVATE
    DLLNAME=server
    GAME_DLL
    VECTOR
    VERSION_SAFE_STEAM_API_INTERFACES
    _snprintf=use_Q_snprintf_instead
    INCLUDED_STEAM2_USERID_STRUCTS
    SWDS
    SOURCESHARP
    SOURCESHARP_SHELL_GAMEDLL
    SS_CBASEENTITY_ALIAS
    # plan_server_novalve.md P2.3b: stock util_shared.h's UTIL_Trace* inlines
    # read `extern ConVar r_visualizetraces`. This turns that extern into a
    # function declaration and each read into a call that answers the
    # ENGINE's r_visualizetraces (engine.so declares it), so server.so needs
    # no ConVar object of that name. trace_filter_library.cpp defines it.
    "r_visualizetraces=(*SourceSharp_EngineVisualizeTraces())"
    # plan_server_novalve.md P5 step 5: tier0/memalloc.h's arm for a module
    # with no tier0 allocator. Its MemAlloc_* tracking hooks and
    # MEM_ALLOC_CREDIT_* become no-ops, so CUtlMemory's inline growth, which
    # called our g_pMemAlloc's empty Register/Push hooks and nothing else of
    # it, no longer names g_pMemAlloc. CUtlMemory allocates with malloc
    # either way.
    NO_MALLOC_OVERRIDE
)

# THE INCLUDE SETS -- plan_server_novalve.md 3.4 (amended in P1: per source,
# not an object library, so this target keeps its one source list, which
# tools/cmakegraph.py and its twins read).
#
# The target carries only this repo's directories. Every other directory is
# per source, in one of two sets:
#
#   server_novalve_includes  the R5 set: the allowlisted fork dirs only
#                            (docs/server-fork-allowlist.txt's closure). A TU
#                            here that reaches a game/ header fails to compile.
#   server_legacy_includes   the old target-wide list, in its old order, for
#                            every TU not yet moved. Removing a TU from the
#                            legacy list IS the move.
#
# Ninja puts source-level directories BEFORE the target's, so ours now come
# last. Measured when this landed: every one of the 109 objects' resolved
# dependency sets (`ninja -t deps`, readlink -f; 43,960 lines) is identical to
# the old target-wide order's, so no header changed which file it names.
target_include_directories(server_sourcesharp PRIVATE
    ${PROJECT_SOURCE_DIR}/src/host/native
    ${PROJECT_SOURCE_DIR}/src/server/native
    ${PROJECT_SOURCE_DIR}/src/shared/native
    # plan_server_novalve.md 3.1: the engineabi module, #included as
    # "engineabi/<what it declares>.h".
    ${PROJECT_SOURCE_DIR}/src/engineabi/native/include
)

set(server_novalve_includes
    ${SS_SRC}/public
    ${SS_SRC}/public/tier0
    ${SS_SRC}/public/tier1
)
# src/test/native's R5 canary compiles against exactly this set.
set_property(GLOBAL PROPERTY SS_SERVER_NOVALVE_INCLUDES "${server_novalve_includes}")

set(server_legacy_includes
    ${SS_SRC}/common
    ${SS_SRC}/game/server
    ${SS_SRC}/game/server/NextBot
    ${SS_SRC}/game/shared
    ${SS_SRC}/game/shared/econ
    ${SS_SRC}/public
    ${SS_SRC}/public/tier0
    ${SS_SRC}/public/tier1
    ${SS_SRC}/utils/common
)

# The TUs already on the R5 set. SS_ENGINEABI is what engineabi/switch/
# headers key on for a shared TU that still builds against the fork in client.so.
set(server_novalve_sources
    ${CMAKE_CURRENT_SOURCE_DIR}/alloc_check.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/studio_mdlcache.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/tier0_spew_port.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/tier0_thread_port.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/tier1_port.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_factory.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/convar_port.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/tier_connect.cpp
    # No SDK header at all: POSIX, dlfcn and the entry-point prototypes.
    ${CMAKE_CURRENT_SOURCE_DIR}/exit_watch.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/engine_heap.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/loaded_symbol.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/ss_command_line.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/fatal_report.cpp
    # P5 wave 1 (2026-09-30): the ten legacy TUs that already compiled on the
    # R5 set, found by a -fsyntax-only trial of every legacy TU with the
    # legacy directories removed. Shared: client.so keeps building them on
    # its own list.
    ${PROJECT_SOURCE_DIR}/src/shared/native/collisionutils.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/dt_send.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/dt_utlvector_common.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/overlay_library.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_acquire_reasons.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/studio.cpp
    # P5 wave 2: cbase.h included and never used -- the include removed, every
    # function the TU defines compared identical before and after (objdump).
    ${CMAKE_CURRENT_SOURCE_DIR}/plugin_check.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_sharedappsystems.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_internalfactory.cpp
    # P5 wave 3: its dead SOURCESHARP_LINK_ENTITY machinery (the one reason it
    # reached util.h) deleted; cbase.h kept for the client half only.
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_diagnostics.cpp
    # P5 wave 4: toucher.h no longer pulls baseentity.h/util_shared.h (Toucher_t
    # needs only the handle types), and toucher.cpp's cbase.h was unused.
    ${CMAKE_CURRENT_SOURCE_DIR}/toucher.cpp
    # P5 wave 5: the server's recipient filters' list logic (ss_recipient_filter.h).
    ${CMAKE_CURRENT_SOURCE_DIR}/ss_recipient_filter.cpp
    # P5 wave 6: cbase.h out (they used only gpGlobals / mdlcache / CCommand /
    # gameeventmanager from it, now declared or included directly); every
    # function they define compared identical before and after (objdump).
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_serversystems.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/generic_payload.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_gameevents.cpp
    # P5 wave 7: shell_standalone.h reaches only public/ and our own headers
    # (wave 7a); these three then needed VPlane + engine, CCommand, and
    # IPhysicsObject in place of cbase.h.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_areaportal.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_nettable.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/standalone_relays.cpp
    # P5 wave 7c: imovementplayer.h left the overlay for src/shared/native/, and
    # standalone_player_sync.h includes basehandle.h; cbase.h was then unused.
    ${PROJECT_SOURCE_DIR}/src/shared/native/world_net_sync.cpp
    # P5 wave 9: the save block's records come from engineabi/saverestore.h
    # (K2-checked against upstream) instead of public/saverestoretypes.h, which
    # does not compile under GAME_DLL without cbase.h.
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_savegame.cpp
    # P5 lane R: g_pDeveloper and InitializeCvars need only icvar.h (cvar,
    # ConVar); game.h and cbase.h were unused.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_gamedll_convars.cpp
    # P5 lane R: g_pGameRules reached through igamerules.h, which now declares
    # it, in place of cbase.h and gamerules.h.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_gametags.cpp
    # P5 lane R: the send half needs IVEngineServer and engine; cbase.h and
    # enginecallback.h out. plan_side_isolation P2: the ss:: list it read is gone.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_usermessage_send.cpp
    # P5 lane R: the tables it creates, their capacities and the rules' table
    # creator declared in the TU; five game/ headers and cbase.h out.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_stringtables.cpp
    # P5 lane R: the server half's accessors from ss_gamestringpool.h; cbase.h,
    # igamesystem.h and gamestringpool.h stay for the client half only.
    ${PROJECT_SOURCE_DIR}/src/shared/native/gamestringpool.cpp
    # P5 lane R: the server half counts slots of public/ interfaces and reads
    # ss::factorylist_t; cbase.h, con_nprint.h and sharedInterface.h are the
    # client half's.
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_vtable_interfaces.cpp
    # P5 lane R: cbase.h and gameinterface.h were unused.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_gamedll.cpp
    # P5 lane R: the server half needs gpGlobals, engine and GetCurrentGravity,
    # declared; cbase.h, igamesystem.h and movevars_shared.h are the client's.
    # docs/todo.md's console capture; see the comment at server_sourcesharp_sources.
    ${PROJECT_SOURCE_DIR}/src/shared/native/console_display_forward.cpp
    ${PROJECT_SOURCE_DIR}/src/shared/native/sourcesharp_gamesystem.cpp
    # P5 lane R: the engine-interface globals and DLLInit's acquisition on
    # public/ headers; `random` defined by its link name, random_valve.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_engineboundary.cpp
    # P5 lane R: with ss::CUserCmd, shell_standalone_player.h is R5-clean; the
    # frame needs gpGlobals and eiface.h, the server shell ss_shareddefs.h's
    # kMaxPlayers, and neither cbase.h nor gameinterface.h.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_frame.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_server.cpp
    # P5 lane R: the level steps on ss_mapentities.h, ss_soundent.h, ss_world.h
    # and entity_shared_statics.h, with the engine globals declared.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_level.cpp
    # P5 lane R: the rules on igamerules.h / ss_viewvectors.h /
    # ss_takedamageinfo.h / ss_shareddefs.h; the server's network table spelled
    # as the send table it is; cbase.h and five game/ headers are the client's.
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_gamerules.cpp
    # P5 lane E: sendproxy resolves its handle through SourceSharp_EntityFromHandle
    # (what CBaseHandle::Get's body is on the server) and needs dt_send.h,
    # string_t.h, basetypes.h; shell_engineservices needed only an extern
    # filesystem in place of cbase.h, baseanimating.h, entitylist.h and
    # TemplateEntities.h.
    ${CMAKE_CURRENT_SOURCE_DIR}/sendproxy.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_engineservices.cpp
    # P5 lane E 3: CWorld's header is ss_world.h (public/ headers only), and
    # public/gametrace.h is trace_t; cbase.h was then unused here, and
    # shell_world_nettable needed dt_send.h + server_class.h in its place.
    ${CMAKE_CURRENT_SOURCE_DIR}/world_subject_dll.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_entityslots.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_world_nettable.cpp
    # P5 lane E 6: with entity_shared_statics.h out of the overlay, what it used
    # in place of cbase.h was basehandle.h, igameentity.h and tier0/dbg.h.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_restoreentities.cpp
    # P5 lane E 9: with CServerNetworkProperty gone (lane E 4) and ss_util.h
    # (lane E 7), cbase.h, entityapi.h and entitylist.h give way to ss_util.h
    # and two extern declarations.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_gameents.cpp
    # P5 lane E 10: shared; its server half needed only basehandle.h,
    # igameentity.h, ispatialpartition.h, tier0/tslist.h, shell_serversystem.h
    # and an extern partition, in a !CLIENT_DLL arm. client.so keeps its list.
    ${PROJECT_SOURCE_DIR}/src/shared/native/collisionproperty.cpp
    # P5 lane E 13: with the dead factories gone (lane E 12), dt_send.h, edict.h,
    # irecipientfilter.h and an extern enginesound in place of cbase.h,
    # baseanimating.h, entitylist.h and sendproxy.h.
    ${CMAKE_CURRENT_SOURCE_DIR}/baseentity_statics.cpp
    # P5 lane E 16: with ss::CUserCmd (shell_standalone_player.h) and the dead
    # SDK player lookups gone (lane E 15), util.h gives way to ss_util.h,
    # ss_usermessages.h (lane R) and the engine / gpGlobals externs.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_clientsteps.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_playerslots.cpp
    # P5 lane E 18: with ss::CUserCmd and ss_entityfactory.h (lane E 17),
    # cbase.h and entitylist.h give way to ss_util.h.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_standalone_class.cpp
    # P5 lane E 20: with ss::MapEntity_ParseEntity (lane E 19) and
    # standalone_nettable.h free of shareddefs.h, cbase.h, entitylist.h and
    # mapentities.h were unused.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_standalone.cpp
    # P5 lane E 21: shared; with ss::CUserCmd and igamerules.h (round 2), its
    # server half needed only edict.h and an extern gpGlobals in place of
    # cbase.h and baseentity_shared.h. client.so keeps its list.
    ${PROJECT_SOURCE_DIR}/src/shared/native/entity_shared_statics.cpp
    # P5 lane E 22: util.h and IEffects.h give way to ss_util.h, ss_effects.h
    # (lane SP) and ss_shareddefs.h (lane R), plus an extern gpGlobals.
    ${CMAKE_CURRENT_SOURCE_DIR}/server_util_library.cpp
    # P5 lane E 26: with ss::variant_t (lane E 8) and ss::factorylist_t (lane R),
    # cbase.h was unused.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_globals.cpp
    # P5 lane E 29: with lane R's ss::CTakeDamageInfo, cbase.h, entitylist.h and
    # entityfilter.h were unused.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_dispatch.cpp
    # P5 lane E 30: with ss::CTakeDamageInfo (lane R) and ss_physics_shared.h
    # (lane SP), ss_util.h, engine/ivmodelinfo.h and four declarations replace
    # cbase.h, physics.h, physics_shared.h, movevars_shared.h, entitylist.h,
    # mapentities.h and anyentity.h.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_standalone_base.cpp
    # P5 lane E 32: with ss::CTakeDamageInfo, entity_name_match.h out of the
    # overlay and the ss_util.h spellings, cbase.h, TemplateEntities.h and
    # mapentities.h give way to ss_util.h, ss_shareddefs.h and vphysics_interface.h.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_entityapi.cpp
    # P5 lane SP: the server half needs only managed_convar_ref.h and its own
    # GetCurrentGravity declaration.
    ${PROJECT_SOURCE_DIR}/src/shared/native/movevars_shared.cpp
    # P5 lane SP: modelinfo and mdlcache declared; the new-event-system bit
    # from anim_event_names.h instead of eventlist.h.
    ${PROJECT_SOURCE_DIR}/src/shared/native/model_descriptor.cpp
    # P5 lane SP: physicslink.h left the overlay for src/shared/native/; cbase.h
    # was then unused.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_physicsobjects.cpp
    # P5 lane SP: its IDecalEmitterSystem and `decalsystem` left with their last
    # caller; it answers ss_decals.h alone.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_decals.cpp
    # P5 lane SP: ss_util.h and ss_physics_shared.h in place of cbase.h,
    # entitylist.h, baseanimating.h and physics_shared.h; gpGlobals, modelinfo
    # and mdlcache declared.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_standalone_hierarchy.cpp
    # P5 lane SP: gamesound.h left the overlay for src/shared/native/; cbase.h
    # out, UTIL_EmitAmbientSound (sound_emitter.cpp) declared.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_sound.cpp
    # P5 lane SP: the Valve controller gone (aa9909a) and ss::EmitSound_t in
    # (0ae5154); ss_gamestringpool.h in place of cbase.h / gamestringpool.h.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_soundenvelope.cpp
    # P5 lane SP: engine and modelinfo declared in place of cbase.h; seven
    # unused animation/sound includes gone.
    ${CMAKE_CURRENT_SOURCE_DIR}/precache_model.cpp
    # P5 lane SP: ss_util.h (engine, ss::ENTINDEX) in place of cbase.h; the
    # sentence emitter and SENTENCEG_Lookup declared.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_sentences.cpp
    # P5 lane SP: CSoundEnt's forwarders gone with their last caller; the TU
    # answers ss_soundent.h alone.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_soundent.cpp
    # P5 lane SP: ss_util.h in place of cbase.h (ss::UTIL_TraceLine);
    # ndebugoverlay.h was unused; entity_debug_globals.h left the overlay.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_debugoverlays.cpp
    # P5 (integrator): the server half needs nothing from cbase.h once
    # ss_playerlocaldata.h and ss_usercmd.h exist; the client keeps it.
    ${PROJECT_SOURCE_DIR}/src/shared/native/standalone_player_sync.cpp
    # P5 (integrator): lane R's shell_shared; with ss::CAmmoDef, ss_gamemovement.h and
    # the prediction list gone, the server half needs convar.h and dbg.h only.
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_shared.cpp
    # P5 (integrator): lane R's takedamageinfo_library; the random stream and the
    # DMG_ bits it named from sharedInterface.h / shareddefs.h come from
    # random_valve and ss_shareddefs.h.
    ${PROJECT_SOURCE_DIR}/src/shared/native/takedamageinfo_library.cpp
    # P5 (integrator): lane R's util_shared_library; the server half takes ss_util.h,
    # ss_shareddefs.h and random_valve in place of cbase.h / util_shared.h / sharedInterface.h.
    ${PROJECT_SOURCE_DIR}/src/shared/native/util_shared_library.cpp
    # P5 (integrator): the CPlayerLocalData group. With ss_playerlocaldata.h and
    # ss_usercmd.h in place, these needed only the engine and gpGlobals externs.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_commandclient.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_playerinfo.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_transmitsteps.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_playerlocaldata.cpp
    # P5 (integrator): physics.cpp, on ss_physics.h (game/server/physics.h's records as ss:: types).
    ${CMAKE_CURRENT_SOURCE_DIR}/physics.cpp
    # P5 (integrator, engineapi's TU by a4's hand-over): engine and gpGlobals externs.
    ${CMAKE_CURRENT_SOURCE_DIR}/managed_runtime_server.cpp
    # P5 (integrator, engineapi's TU by a4's hand-over): ss::CPostFrameNavigationHook,
    # ss::SetPlayerSurface and ss_shareddefs.h's constants.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_absent.cpp
    # P5 (integrator, engineapi's TU): declarations of server.so's own functions and globals.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_gameclients.cpp
    # P5 (integrator, engineapi's TU): ss_util.h / ss_shareddefs.h / ss_physics_shared.h and declarations.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_standalone_player.cpp
    # P5 (integrator, engineapi's TU): the server arm's stock macros aliased to ss:: spellings.
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_movement.cpp
    # P5 (integrator, lane E's TU): the server arm on tier1/convar.h, ivmodelinfo.h, ss_entityhandle.h.
    ${PROJECT_SOURCE_DIR}/src/shared/native/trace_filter_library.cpp
    # P5 lane SP: ss_util.h (engine, ss::INDEXENT) in place of cbase.h; the
    # dead prediction-system instance gone (833ee78).
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_recipientfilter.cpp
    # P5 lane SP: ss::TePrediction in (0bfbce4); engine and CCommand from
    # public/ headers in place of cbase.h.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_tempents.cpp
    # P5 lane SP: the Valve IEffects gone (6d67c68), ss::CEffectData in
    # (dd8b64a); gpGlobals and the EffectDispatch table declared.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_te_forwards.cpp
    # P5 lane SP: ss_util.h and ss_physics_shared.h in place of cbase.h,
    # physics.h and four unused animation/prop headers.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_scripted.cpp
    # P5 lane SP: its server half is ss::PhysSaveRestore_ForgetAllModels, an
    # empty function on no headers; the Valve manager stays client-only.
    ${PROJECT_SOURCE_DIR}/src/shared/native/shell_physsaverestore_absent.cpp
    # P5 lane SP: ss_util.h (with its new ss::UTIL_TraceHull), ss_shareddefs.h
    # and ss_physics_shared.h in place of cbase.h and six fork headers; our own
    # util helpers declared.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_worldquery.cpp
    # P5 lane SP: the server arm on public/ headers and ours (ss_util.h,
    # ss_usermessages.h); engine globals and two of our helpers declared. The
    # client arm is unchanged. (closecaption_recipient.h's flag bits went with
    # the native caption writer, lane HC 2026-10-04.)
    ${PROJECT_SOURCE_DIR}/src/shared/native/sound_emitter.cpp
    # P5 lane E 36: ss::CPrecacheRegister (ss_precache_register.h), ss_util.h's
    # spellings, ss_gamestringpool.h, ss::CEffectData (lane SP) and the headers
    # out of the overlay (anyentity.h, movement_ground.h) replace cbase.h,
    # precache_register.h, client.h, physics.h, globals.h, particle_parse.h,
    # igamesystem.h and the AI/activity/event headers; six names declared.
    ${CMAKE_CURRENT_SOURCE_DIR}/world_adapter.cpp
    # P5, the last legacy TU: the native map-text parser is gone (the world and
    # the templates arrive parsed), so cbase.h, mapentities.h and
    # mapentities_shared.h give way to ss_util.h, ss_entityhandle.h and
    # handle_cross.h.
    ${CMAKE_CURRENT_SOURCE_DIR}/shell_mapparse.cpp
    # The debug-only diagnostics are on the R5 set from the start.
    ${server_sourcesharp_debug_sources}
)
foreach(src IN LISTS server_sourcesharp_sources)
  if(src IN_LIST server_novalve_sources)
    set_property(SOURCE ${src} TARGET_DIRECTORY server_sourcesharp
      APPEND PROPERTY INCLUDE_DIRECTORIES ${server_novalve_includes})
    set_property(SOURCE ${src} TARGET_DIRECTORY server_sourcesharp
      APPEND PROPERTY COMPILE_DEFINITIONS SS_ENGINEABI)
  else()
    set_property(SOURCE ${src} TARGET_DIRECTORY server_sourcesharp
      APPEND PROPERTY INCLUDE_DIRECTORIES ${server_legacy_includes})
  endif()
endforeach()

# libmanaged_host.so has no SONAME, so it is named with -L/-l (as VPC did): by
# path, DT_NEEDED would record the build directory.
add_dependencies(server_sourcesharp sourcesharp)
target_link_options(server_sourcesharp PRIVATE -L$<TARGET_FILE_DIR:sourcesharp>)

# IDENTICAL CODE FOLDING, release only (2026-10-01). lld merges functions whose
# code is byte-identical; `safe` never merges one whose address is taken (clang
# emits .llvm_addrsig), so registration tables and vtables keep distinct
# pointers. Measured on release server.so: .text 659,853 -> 635,173 (-3.7%);
# `all` folds only 328 bytes more. Debug keeps one symbol per function for the
# nm/globals instruments. GNU ld (SS_LINKER=bfd, the CI image) has no --icf.
if(SS_CONFIG STREQUAL "release" AND SS_IS_CLANG AND SS_LINKER STREQUAL "lld")
  target_link_options(server_sourcesharp PRIVATE -Wl,--icf=safe)
endif()

# NO RTTI IN RELEASE server.so (2026-10-01). Nothing in release asks for a type
# at run time: the one real dynamic_cast (shell_serversystems.cpp's per-frame
# walk) asks IsPerFrame() instead. The three left are inside Asserts
# (SourceSharp_ShellOf x2, SourceSharp_PlayerOf), so debug keeps RTTI and with it
# their exact check that every IGameEntity is a shell. IGameEntity has no
# identity virtual to ask instead. The cost: a new dynamic_cast or typeid
# outside an Assert compiles in debug and fails only in `make release`.
if(SS_CONFIG STREQUAL "release")
  target_compile_options(server_sourcesharp PRIVATE -fno-rtti)
endif()

# UNUSED FUNCTIONS AND VARIABLES ARE ERRORS IN server.so (2026-10-01). The
# project-wide -Wno-unused-* exist for Valve noise; every server TU is ours, so
# they are turned back on here, after ss_sdk_compile's. A static the gc cull
# fenced out, or a helper whose last caller moved to C#, now fails the build
# instead of waiting to be found. The one Valve site is utlblockmemory.h:350 (an
# unused local in a template every CUtlVector user instantiates, 85 TUs);
# --system-header-prefix treats that one header as a system header rather than
# editing it. gcc has no such flag, so the CI build takes the warnings alone.
target_compile_options(server_sourcesharp PRIVATE -Wunused-function -Wunused-variable
  $<$<BOOL:${SS_IS_CLANG}>:--system-header-prefix=tier1/utlblockmemory.h>)
target_link_libraries(server_sourcesharp PRIVATE managed_host)
set_property(TARGET server_sourcesharp APPEND PROPERTY LINK_DEPENDS $<TARGET_FILE:sourcesharp>)

# VPC linked the Valve libraries inside -Wl,--start-group ... --end-group: they
# refer to each other in both directions, and GNU ld (the CI link) resolves an
# archive only against what came before it.
target_link_libraries(server_sourcesharp PRIVATE
    dl
    "$<LINK_GROUP:RESCAN,mathlib>"
)

# physics.cpp (relocated Valve code) destroys vehicle_gear_t and
# vehicle_crashsound_t -- classes with virtual functions and no virtual
# destructor -- through tier0's Destruct<T>() (platform.h:1602) when a
# CUtlVector of them is cleared. clang files that explicit destructor call under
# -Wdelete-non-abstract-non-virtual-dtor, which our -Werror=delete-non-virtual-dtor
# promotes; gcc's -Wdelete-non-virtual-dtor covers only `delete`, so it never
# warned. Off for this one Valve TU under clang only: every other TU, ours
# included, keeps the error.
if(SS_IS_CLANG)
  set_source_files_properties("${CMAKE_CURRENT_SOURCE_DIR}/physics.cpp"
    TARGET_DIRECTORY server_sourcesharp
    PROPERTIES COMPILE_OPTIONS "-Wno-delete-non-abstract-non-virtual-dtor")
endif()
