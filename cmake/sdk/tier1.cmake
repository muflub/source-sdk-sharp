# tier1.a, built from Valve's source in the fork.
#
# Generated once from VPC's ninja graph in Phase 6b and hand-owned since.
# Valve ships no linux64 prebuilt of it (see cmake/sdk/CMakeLists.txt).

add_library(tier1 STATIC)
ss_sdk_compile(tier1 PROJECTDIR tier1)

# Only the members some link pulls in: libss_particles (whose prebuilt
# particles.a and dmxloader.a reach KeyValues, convar, the string tools and the
# buffers), client.so and the native tests that link tier1. The other 26 of
# Valve's 43 were compiled in every build tree and never linked, so the list
# names what is used, and sdk_lib_members_test (census tier) relinks every
# consumer with --trace and fails on a member none of them pulls in. A member
# that a link needs and the list lacks fails that link.
set(tier1_sources
    tier1/bitbuf.cpp
    tier1/characterset.cpp
    tier1/checksum_crc.cpp
    tier1/convar.cpp
    tier1/generichash.cpp
    tier1/KeyValues.cpp
    tier1/memstack.cpp
    tier1/strtools.cpp
    tier1/strtools_unicode.cpp
    tier1/tier1.cpp
    tier1/uniqueid.cpp
    tier1/utlbinaryblock.cpp
    tier1/utlbuffer.cpp
    tier1/utlbufferutil.cpp
    tier1/utlstring.cpp
    tier1/utlsymbol.cpp
)
# qsort_s.cpp is [$LINUXALL] in tier1.vpc: the CRT already has one on Windows,
# and macOS takes its own. On Linux strtools reaches it.
if(NOT SS_PLATFORM STREQUAL windows AND NOT SS_PLATFORM STREQUAL osx)
  list(APPEND tier1_sources tier1/qsort_s.cpp)
endif()
list(TRANSFORM tier1_sources PREPEND "${SS_VENDOR}/client/")
target_sources(tier1 PRIVATE ${tier1_sources})

target_compile_definitions(tier1 PRIVATE
    TIER1_STATIC_LIB
)

ss_vendor_includes(tier1_includes client common public public/tier0 public/tier1)
target_include_directories(tier1 PRIVATE ${tier1_includes})
# gcc -O2 only (the CI release build), and the VPC release build printed the
# same three: byteswap.h:212 -Wmaybe-uninitialized (twice, utlbufferutil.cpp)
# and utlstring.h:399 -Wstringop-truncation (ilocalize.cpp, no longer built;
# with that one off, gcc reports the same strncpy as -Wstringop-overflow
# instead, and utlstring.h is still read by every member here). Valve's code; the
# warnings depend on what the optimiser inlined, not on anything we changed.
if(CMAKE_CXX_COMPILER_ID STREQUAL "GNU")
  target_compile_options(tier1 PRIVATE -Wno-maybe-uninitialized -Wno-stringop-truncation
    -Wno-stringop-overflow)
endif()
