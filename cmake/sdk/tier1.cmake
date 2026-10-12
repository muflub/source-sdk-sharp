# tier1.a, built from Valve's source in the fork.
#
# Generated once from VPC's ninja graph in Phase 6b and hand-owned since.
# Valve ships no linux64 prebuilt of it (see cmake/sdk/CMakeLists.txt).

add_library(tier1 STATIC)
ss_sdk_compile(tier1 PROJECTDIR tier1)

set(tier1_sources
    tier1/bitbuf.cpp
    tier1/byteswap.cpp
    tier1/characterset.cpp
    tier1/checksum_crc.cpp
    tier1/checksum_sha1.cpp
    tier1/commandbuffer.cpp
    tier1/convar.cpp
    tier1/datamanager.cpp
    tier1/diff.cpp
    tier1/exprevaluator.cpp
    tier1/generichash.cpp
    tier1/ilocalize.cpp
    tier1/interface.cpp
    tier1/KeyValues.cpp
    tier1/keyvaluesjson.cpp
    tier1/kvpacker.cpp
    tier1/lzmaDecoder.cpp
    tier1/mempool.cpp
    tier1/memstack.cpp
    tier1/NetAdr.cpp
    tier1/newbitbuf.cpp
    tier1/rangecheckedvar.cpp
    tier1/reliabletimer.cpp
    tier1/snappy-sinksource.cpp
    tier1/snappy-stubs-internal.cpp
    tier1/snappy.cpp
    tier1/sparsematrix.cpp
    tier1/splitstring.cpp
    tier1/stringpool.cpp
    tier1/strtools.cpp
    tier1/strtools_unicode.cpp
    tier1/tier1.cpp
    tier1/tokenreader.cpp
    tier1/uniqueid.cpp
    tier1/utlbinaryblock.cpp
    tier1/utlbuffer.cpp
    tier1/utlbufferutil.cpp
    tier1/utlstring.cpp
    tier1/utlsymbol.cpp
    utils/lzma/C/LzmaDec.c
)
# The platform members, as tier1.vpc conditions them: processor_detect.cpp
# [$WINDOWS] or processor_detect_linux.cpp [$POSIX] (CPUID through the
# compiler's intrinsics or inline asm); pathmatch.cpp [$LINUXALL], the
# case-insensitive path fixups a case-sensitive file system needs; qsort_s.cpp
# [$LINUXALL], the CRT already having one on Windows and macOS taking its own.
if(SS_PLATFORM STREQUAL windows)
  list(APPEND tier1_sources tier1/processor_detect.cpp)
elseif(SS_PLATFORM STREQUAL osx)
  list(APPEND tier1_sources tier1/processor_detect_linux.cpp)
else()
  list(APPEND tier1_sources tier1/pathmatch.cpp tier1/processor_detect_linux.cpp tier1/qsort_s.cpp)
endif()
list(TRANSFORM tier1_sources PREPEND "${SS_SRC}/")
target_sources(tier1 PRIVATE ${tier1_sources})

target_compile_definitions(tier1 PRIVATE
    TIER1_STATIC_LIB
)

target_include_directories(tier1 PRIVATE
    ${SS_SRC}/common
    ${SS_SRC}/public
    ${SS_SRC}/public/tier0
    ${SS_SRC}/public/tier1
)
# gcc -O2 only (the CI release build), and the VPC release build printed the
# same three: byteswap.h:212 -Wmaybe-uninitialized (twice, utlbufferutil.cpp)
# and utlstring.h:399 -Wstringop-truncation (ilocalize.cpp; with that one off,
# gcc reports the same strncpy as -Wstringop-overflow instead). Valve's code; the
# warnings depend on what the optimiser inlined, not on anything we changed.
if(NOT SS_IS_CLANG)
  target_compile_options(tier1 PRIVATE -Wno-maybe-uninitialized -Wno-stringop-truncation
    -Wno-stringop-overflow)
endif()

# VPC compiled this C file with its C++ rule (g++), so it is C++ here too --
# and the project enables CXX only, which would otherwise drop it silently.
set_source_files_properties("${SS_SRC}/utils/lzma/C/LzmaDec.c"
  TARGET_DIRECTORY tier1 PROPERTIES LANGUAGE CXX)
