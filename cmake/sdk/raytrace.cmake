# raytrace.a, built from Valve's source in the fork.
#
# Generated once from VPC's ninja graph in Phase 6b and hand-owned since.
# Valve ships no linux64 prebuilt of it (see cmake/sdk/CMakeLists.txt).

add_library(raytrace STATIC)
ss_sdk_compile(raytrace PROJECTDIR raytrace)

set(raytrace_sources
    raytrace/raytrace.cpp
    raytrace/trace2.cpp
    raytrace/trace3.cpp
)
list(TRANSFORM raytrace_sources PREPEND "${SS_SRC}/")
target_sources(raytrace PRIVATE ${raytrace_sources})

target_include_directories(raytrace PRIVATE
    ${SS_SRC}/common
    ${SS_SRC}/public
    ${SS_SRC}/public/tier0
    ${SS_SRC}/public/tier1
    ${SS_SRC}/utils/common
)
