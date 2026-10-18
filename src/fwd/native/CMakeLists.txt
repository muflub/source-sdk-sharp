# The forwarder libraries (plan_engine_forwarders.md), one per family:
#
#   libss_fwd_engine.so   / ss_fwd_engine.dll     the engine's interfaces
#   libss_fwd_vphysics.so / ss_fwd_vphysics.dll   vphysics' interfaces and objects
#   libss_fwd_steam.so    / ss_fwd_steam.dll      a Steam callback, built natively
#                                                 (fwd_steam.cpp, by hand)
#
# One generated function per method managed calls (fwd_<family>.g.inc, from
# tools/engine-forwarders), each a direct call through the object's own
# vtable, so the slot numbering is this compiler's. A library needs the SDK
# headers to compile and links no SDK library: the forwarders are handed the
# object they call through, and the tables are constant data. Each exports
# only SSFwd<Family>_Table (version_script_<family>.txt; DLL_EXPORT on
# Windows).
#
# server.so and client.so link them like any library and serve their tables
# through their internal factory (SourceSharp_InternalFactory, SSFwd_Serve):
# on Linux DT_NEEDED names each SONAME, found beside them through RUNPATH
# $ORIGIN; on Windows the import libraries. Managed reaches a table the way it
# reaches every native interface, through the shim root's AcquireInterface,
# and never loads a library itself.

# ss_fwd_library(<family> <Export>): the library ss_fwd_<family> from
# fwd_<family>.cpp, exporting <Export>, defining SS_FWD_<FAMILY>_BUILD for
# ss_fwd.h's import/export macro.
function(ss_fwd_library family)
  set(tgt ss_fwd_${family})
  string(TOUPPER ${family} FAMILY)
  set(script ${CMAKE_CURRENT_SOURCE_DIR}/version_script_${family}.txt)

  add_library(${tgt} SHARED)
  set_target_properties(${tgt} PROPERTIES OUTPUT_NAME ${tgt})
  # NO_WRAP: the allocator wraps are for code that allocates through the
  # engine's heap, and nothing here allocates. NO_RUNTIME: nothing here calls
  # a runtime either (fwd_<family>.cpp compares names itself, and ss_fwd_sdk.h
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

  target_sources(${tgt} PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/fwd_${family}.cpp)

  target_include_directories(${tgt} PRIVATE
      ${CMAKE_CURRENT_SOURCE_DIR}
      ${SS_SRC}/common
      ${SS_SRC}/public
      ${SS_SRC}/public/tier0
      ${SS_SRC}/public/tier1
  )

  target_compile_definitions(${tgt} PRIVATE
      DLLNAME=${tgt}
      SS_FWD_${FAMILY}_BUILD
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

  # The consumers: the header (ss_fwd.h) and the import side of its macros.
  target_include_directories(${tgt} INTERFACE ${CMAKE_CURRENT_SOURCE_DIR})
endfunction()

ss_fwd_library(engine)
ss_fwd_library(vphysics)
ss_fwd_library(steam)

# The Steam library defines a class with virtuals (SSSteamCallback, deriving
# CCallbackBase). With RTTI its vtable would point at a type_info, whose own
# vtable is the C++ runtime's, which the library does not link (NO_RUNTIME).
# Nothing casts a callback or asks its type -- Steam calls its three virtuals
# and no more -- so it is built without RTTI. Its target interface,
# IManagedSteamCallback, derives IManagedObject (imanaged_core.h).
if(SS_IS_MSVC)
  target_compile_options(ss_fwd_steam PRIVATE /GR-)
else()
  target_compile_options(ss_fwd_steam PRIVATE -fno-rtti)
endif()
target_include_directories(ss_fwd_steam PRIVATE ${SS_SRC}/sourcesharp/game/shared)
# A game DLL that links the library serves its table (ss_fwd.h's SSFwd_Serve):
# the client, which registers the Steam callbacks.
target_compile_definitions(ss_fwd_steam INTERFACE SS_FWD_SERVES_STEAM)
