# mathlib.a, built from Valve's source in the fork.
#
# Generated once from VPC's ninja graph in Phase 6b and hand-owned since.
# Valve ships no linux64 prebuilt of it (see cmake/sdk/CMakeLists.txt).

add_library(mathlib STATIC)
ss_sdk_compile(mathlib PROJECTDIR mathlib)

set(mathlib_sources
    mathlib/3dnow.cpp
    mathlib/almostequal.cpp
    mathlib/anorms.cpp
    mathlib/bumpvects.cpp
    mathlib/color_conversion.cpp
    mathlib/halton.cpp
    mathlib/IceKey.cpp
    mathlib/imagequant.cpp
    mathlib/lightdesc.cpp
    mathlib/mathlib_base.cpp
    mathlib/polyhedron.cpp
    mathlib/powsse.cpp
    mathlib/quantize.cpp
    mathlib/randsse.cpp
    mathlib/simdvectormatrix.cpp
    mathlib/sparse_convolution_noise.cpp
    mathlib/spherical.cpp
    mathlib/sse.cpp
    mathlib/sseconst.cpp
    mathlib/ssenoise.cpp
    mathlib/vector.cpp
    mathlib/vmatrix.cpp
)
list(TRANSFORM mathlib_sources PREPEND "${SS_SRC}/")
target_sources(mathlib PRIVATE ${mathlib_sources})

target_compile_definitions(mathlib PRIVATE
    MATHLIB_LIB
)

target_include_directories(mathlib PRIVATE
    ${SS_SRC}/common
    ${SS_SRC}/public
    ${SS_SRC}/public/mathlib
    ${SS_SRC}/public/tier0
    ${SS_SRC}/public/tier1
)
