# The forwarder libraries (plan_engine_forwarders.md), one per side (ruled
# 2026-10-18):
#
#   libss_fwd_server.so / ss_fwd_server.dll   what server.so's managed code calls
#   libss_fwd_client.so / ss_fwd_client.dll   what client.so's managed code calls,
#                                             with the Steam callback (fwd_steam.cpp)
#
# One generated function per method a side's managed code calls
# (fwd_<side>.g.inc, from tools/engine-forwarders), each a direct call through
# the object's own vtable, so the slot numbering is this compiler's. The tables
# both sides call are generated into both. A library needs the SDK headers to
# compile and links no SDK library: the forwarders are handed the object they
# call through, and the tables are constant data. Each exports only
# SSFwd<Side>_Table (version_script_<side>.txt; DLL_EXPORT on Windows).
#
# Each game DLL links its own side's library like any library and serves its
# tables through its internal factory (SourceSharp_InternalFactory,
# SSFwd_Serve): on Linux DT_NEEDED names the SONAME, found beside it through
# RUNPATH $ORIGIN; on Windows the import library. Managed reaches a table the
# way it reaches every native interface, through the shim root's
# AcquireInterface, and never loads a library itself.

# ss_fwd_library(<side>): the library ss_fwd_<side> from fwd_<side>.cpp,
# defining SS_FWD_<SIDE>_BUILD for ss_fwd.h's import/export macro, and for its
# consumers SS_FWD_SERVES_<SIDE>, so the DLL that links it serves its tables.
function(ss_fwd_library side)
  set(tgt ss_fwd_${side})
  string(TOUPPER ${side} SIDE)
  set(script ${CMAKE_CURRENT_SOURCE_DIR}/version_script_${side}.txt)

  add_library(${tgt} SHARED)
  set_target_properties(${tgt} PROPERTIES OUTPUT_NAME ${tgt})
  # NO_WRAP: the allocator wraps are for code that allocates through the
  # engine's heap, and nothing here allocates. NO_RUNTIME: nothing here calls
  # a runtime either (ss_fwd.h compares names itself, and ss_fwd_sdk.h
  # turns the headers' asserts off), so the library needs no other library on
  # either platform: not libc on Linux, no CRT on Windows, where the static
  # CRT had been 104 KB of a 105 KB DLL. fwd_libraries_load_test loads each
  # built file: every slot set with nothing to run an initialiser, and on
  # Windows no import; fwd_libraries_isolation_test reads each .so's NEEDED.
  ss_sdk_target(${tgt} PROJECTDIR fwd RPATH_ORIGIN NO_WRAP NO_RUNTIME
      VERSION_SCRIPT ${script})
  # ss_sdk_target drops the `lib` prefix, as Valve's game libraries have none.
  # These are ours and keep it outside Windows, as libss_particles.so does.
  if(NOT SS_PLATFORM STREQUAL windows)
    set_target_properties(${tgt} PROPERTIES PREFIX "lib")
  endif()
  set_property(TARGET ${tgt} APPEND PROPERTY LINK_DEPENDS ${script})

  # fwd_physics.cpp: the vphysics callbacks both sides hand vphysics
  # (plan_forwarders_everywhere.md phase 5), classes with virtuals deriving the
  # Valve interfaces, built without RTTI below for fwd_steam.cpp's reason.
  target_sources(${tgt} PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/fwd_${side}.cpp
                                ${CMAKE_CURRENT_SOURCE_DIR}/fwd_physics.cpp)

  # ${SS_SRC} itself for our own interfaces (plan_forwarders_everywhere.md),
  # which the generated file includes by their farm path,
  # "sourcesharp/game/<side>/ishim_*.h"; sourcesharp/host for the
  # sourcesharp.h the shim area headers include for their data structs.
  target_include_directories(${tgt} PRIVATE
      ${CMAKE_CURRENT_SOURCE_DIR}
      ${SS_SRC}
      ${SS_SRC}/sourcesharp/host
      ${SS_SRC}/common
      ${SS_SRC}/public
      ${SS_SRC}/public/tier0
      ${SS_SRC}/public/tier1
  )

  target_compile_definitions(${tgt} PRIVATE
      DLLNAME=${tgt}
      SS_FWD_${SIDE}_BUILD
      SOURCESHARP
      NO_MALLOC_OVERRIDE
  )
  # NO_MALLOC_OVERRIDE on Windows is a combination Valve never built:
  # memalloc.h defines its debug-info macros twice for it (C4005, an error
  # under /WX) and memdbgon.h names a memalign the CRT lacks. The compat header
  # server.dll and client.dll force in for the same reason settles both. Its
  # allocator inlines are never called by a forwarder, so none is emitted and
  # the library still imports nothing.
  if(SS_PLATFORM STREQUAL windows)
    target_compile_options(${tgt} PRIVATE
        "/FI${PROJECT_SOURCE_DIR}/cmake/compat/windows/ss_memalloc_windows.h")
  endif()

  # The consumers: the header (ss_fwd.h), the import side of its macros, and
  # the serve helper's choice of library.
  target_include_directories(${tgt} INTERFACE ${CMAKE_CURRENT_SOURCE_DIR})
  target_compile_definitions(${tgt} INTERFACE SS_FWD_SERVES_${SIDE})
endfunction()

ss_fwd_library(server)
ss_fwd_library(client)

# The client's library also builds the Steam callback (fwd_steam.cpp), a
# class with virtuals (SSSteamCallback, deriving CCallbackBase). With RTTI its
# vtable would point at a type_info, whose own vtable is the C++ runtime's,
# which the library does not link (NO_RUNTIME). Nothing casts a callback or
# asks its type -- Steam calls its three virtuals and no more -- so that file
# is built without RTTI; the forwarders keep the tree's default. Its target
# interface, IManagedSteamCallback, derives IManagedObject (imanaged_core.h).
target_sources(ss_fwd_client PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/fwd_steam.cpp)
foreach(no_rtti fwd_steam.cpp fwd_physics.cpp)
  if(SS_IS_MSVC)
    set_source_files_properties(${CMAKE_CURRENT_SOURCE_DIR}/${no_rtti} PROPERTIES COMPILE_OPTIONS /GR-)
  else()
    set_source_files_properties(${CMAKE_CURRENT_SOURCE_DIR}/${no_rtti} PROPERTIES COMPILE_OPTIONS -fno-rtti)
  endif()
endforeach()
# imanaged_core.h, which the managed sides' headers include: both libraries.
target_include_directories(ss_fwd_client PRIVATE ${SS_SRC}/sourcesharp/game/shared)
target_include_directories(ss_fwd_server PRIVATE ${SS_SRC}/sourcesharp/game/shared)
