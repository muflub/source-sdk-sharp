# libmanaged_host.so, the .NET host both DLLs link.
#
# Generated once from VPC's ninja graph (sourcesharp.vpc) in Phase 6b and
# hand-owned since.

add_library(sourcesharp SHARED)
set_target_properties(sourcesharp PROPERTIES OUTPUT_NAME managed_host NO_SONAME ON)
ss_sdk_target(sourcesharp PROJECTDIR sourcesharp NO_WRAP)
# libmanaged_host.so / libmanaged_host.dylib / managed_host.dll: the lib prefix
# the game libraries do without (ss_sdk_target), on the platforms that use one.
if(NOT SS_PLATFORM STREQUAL windows)
  set_target_properties(sourcesharp PROPERTIES PREFIX "lib")
endif()

# 1 translation units, sorted: the Valve files that still come from the fork
# first, then this repo's, each group alphabetically (link order is this order;
# the live gate runs on it). This repo's files are named where they live
# (${CMAKE_CURRENT_SOURCE_DIR}/..., ${PROJECT_SOURCE_DIR}/src/...); only the
# Valve files that still come from the fork are named through the farm
# (${SS_SRC}/..., sdk-links/). tools/cmakegraph.py and its twins map a local
# path back to its farm spelling through tools/module-map.tsv.
set(sourcesharp_sources
    ${CMAKE_CURRENT_SOURCE_DIR}/sourcesharp.cpp
)
target_sources(sourcesharp PRIVATE ${sourcesharp_sources})

target_compile_definitions(sourcesharp PRIVATE
    DLLNAME=libmanaged_host
    # sourcesharp.h: this target EXPORTS the API; every other includer of the
    # header imports it (SOURCESHARP_EXPORT is __declspec(dllexport) here and
    # dllimport there; one visibility attribute on the GNU platforms).
    SOURCESHARP_HOST_BUILD
)

# The host includes nothing of Valve's: only the system headers and its own
# include/ (hostfxr, coreclr_delegates, nethost).
target_include_directories(sourcesharp PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

# No Valve libraries: the host is CoreCLR hosting and nothing else, so it links
# only the C runtime (and, being NO_WRAP, imports nothing from tier0).
