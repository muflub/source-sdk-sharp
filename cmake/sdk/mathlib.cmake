# mathlib.a, built from Valve's source in the fork.
#
# Generated once from VPC's ninja graph in Phase 6b and hand-owned since.
# Valve ships no linux64 prebuilt of it (see cmake/sdk/CMakeLists.txt).

add_library(mathlib STATIC)
ss_sdk_compile(mathlib PROJECTDIR mathlib)

# Only the members some link pulls in (libss_particles, client.so, server.so
# and the native tests that link mathlib); the other 13 of Valve's 22, and
# 3dnow.cpp, were compiled in every build tree and never linked.
# sdk_lib_members_test holds the list to that, as tier1.cmake says.
set(mathlib_sources
    mathlib/color_conversion.cpp
    mathlib/lightdesc.cpp
    mathlib/mathlib_base.cpp
    mathlib/powsse.cpp
    mathlib/randsse.cpp
    mathlib/sse.cpp
    mathlib/sseconst.cpp
    mathlib/ssenoise.cpp
    mathlib/vmatrix.cpp
)
list(TRANSFORM mathlib_sources PREPEND "${SS_STOCK}/")
target_sources(mathlib PRIVATE ${mathlib_sources})

target_compile_definitions(mathlib PRIVATE
    MATHLIB_LIB
)

ss_stock_includes(mathlib_includes common public public/mathlib public/tier0 public/tier1)
ss_stock_target(mathlib)
target_include_directories(mathlib PRIVATE ${mathlib_includes})
