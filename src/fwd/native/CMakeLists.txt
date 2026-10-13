# libss_fwd_engine.so / ss_fwd_engine.dll: the engine's interfaces, forwarded
# (plan_engine_forwarders.md).
#
# One generated function per engine method managed calls (fwd_engine.g.inc,
# from tools/engine-forwarders), each a direct call through the engine object's
# own vtable, so the slot numbering is this compiler's. It needs the SDK
# headers to compile and links no SDK library: the forwarders are handed the
# object they call through, and the tables are constant data. It exports only
# SSFwdEngine_Table (version_script.txt; DLL_EXPORT on Windows).
#
# server.so and client.so link it like any library and serve its tables
# through their internal factory (SourceSharp_InternalFactory): on Linux
# DT_NEEDED names its SONAME, libss_fwd_engine.so, found beside them through
# RUNPATH $ORIGIN; on Windows the import library. Managed reaches a table the
# way it reaches every native interface, through the shim root's
# AcquireInterface, and never loads the library itself.

add_library(ss_fwd_engine SHARED)
set_target_properties(ss_fwd_engine PROPERTIES OUTPUT_NAME ss_fwd_engine)
# NO_WRAP: the allocator wraps are for code that allocates through the
# engine's heap, and nothing here allocates. NO_RUNTIME: nothing here calls a
# runtime either (fwd_engine.cpp compares names itself), so the library needs
# libc alone on Linux and nothing on Windows, where the static CRT had been
# 104 KB of a 105 KB DLL. fwd_engine_load_test loads the built file and holds
# both: every slot set with no CRT to run an initialiser, and no import.
ss_sdk_target(ss_fwd_engine PROJECTDIR fwd RPATH_ORIGIN NO_WRAP NO_RUNTIME
    VERSION_SCRIPT ${CMAKE_CURRENT_SOURCE_DIR}/version_script.txt)
# ss_sdk_target drops the `lib` prefix, as Valve's game libraries have none.
# This one is ours and keeps it outside Windows, as libss_particles.so does.
if(NOT SS_PLATFORM STREQUAL windows)
  set_target_properties(ss_fwd_engine PROPERTIES PREFIX "lib")
endif()
set_property(TARGET ss_fwd_engine APPEND PROPERTY LINK_DEPENDS
    ${CMAKE_CURRENT_SOURCE_DIR}/version_script.txt)

target_sources(ss_fwd_engine PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/fwd_engine.cpp)

target_include_directories(ss_fwd_engine PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${SS_SRC}/common
    ${SS_SRC}/public
    ${SS_SRC}/public/tier0
    ${SS_SRC}/public/tier1
)

target_compile_definitions(ss_fwd_engine PRIVATE
    DLLNAME=ss_fwd_engine
    SS_FWD_ENGINE_BUILD
    SOURCESHARP
    NO_MALLOC_OVERRIDE
)

# The consumers: the header (ss_fwd.h) and the import side of SS_FWD_ENGINE_API.
target_include_directories(ss_fwd_engine INTERFACE ${CMAKE_CURRENT_SOURCE_DIR})
