# The native tests: one gtest executable per ss_cpptest*() call in
# src/test/native/CMakeLists.txt, each registered with ctest, plus the
# must-not-compile checks.
#
# Ported in Phase 6b from tools/build/buildcpptests, whose five bash helpers
# map one-to-one onto the five functions below (build_and_run -> ss_cpptest,
# build_and_run_mathlib -> ss_cpptest_mathlib, build_and_run_tier1 ->
# ss_cpptest_tier1, build_and_run_engine_headers -> ss_cpptest_engine_headers,
# build_must_not_compile -> ss_must_not_compile). The flags, the defines, the
# include paths (-isystem where the helper said -isystem), the link lines and
# the runtime checks are that script's; every difference is named where it is
# made. tools/build/buildcpptests is now the thin driver that configures,
# builds and runs ctest for the farm's configuration.
#
# Every path in a call is relative to the farm's src/ (SS_SRC), exactly as the
# script wrote it, because that is the tree the tests read at run time too.
#
# WHY ONE CTEST TEST PER BINARY AND NOT gtest_discover_tests. Discovery would
# register each of the ~4,200 gtests as its own ctest test and run each in its
# own process under --gtest_filter. Three things the suite depends on would
# change with it: (1) tools/run-test-legs counts ONE gtest summary line per
# binary and checks the per-binary roster, so the floor would read 4,200
# "binaries"; (2) a test binary runs its global environments (mathlib_test_env.h
# calls MathLib_Init once per binary) and suite fixtures once, and several
# binaries (globals_client_ledger_test: 11 s) pay a large one-time setup that
# would be paid per test; (3) tests inside one binary run in one process, in
# order, as they always have. So each binary is one add_test(), which is also
# what buildcpptests did: build it, run it once.

include_guard(GLOBAL)
include(FetchContent)

# bin/<config>/cpptest, as before (the farm's ../out/cpptest is the same place).
# Which test tree this build directory is. `native` (the debug/release presets)
# builds every test but the oracles; `oracle` (the oracle-* presets, their own
# build directory farm/<config>/build-oracle) builds only the oracles, into
# their own output directory, so `make test` can run the two legs at once
# without two ninja runs sharing a build directory or an output file.
set(SS_TEST_TREE "native" CACHE STRING "native: every test but the oracles; oracle: only the oracles")
set_property(CACHE SS_TEST_TREE PROPERTY STRINGS native oracle)
if(SS_TEST_TREE STREQUAL "oracle")
  set(SS_CPPTEST_DIR "${SS_BIN}/cpptest-oracle")
else()
  set(SS_CPPTEST_DIR "${SS_BIN}/cpptest")
endif()
set(SS_CPPTEST_LIBDIR "${SS_SRC}/lib/public/${SS_PLATFORM_BINDIR}")

# --- googletest -------------------------------------------------------------------
#
# Pinned upstream googletest replaces the 2013 copy Valve bundles at
# thirdparty/protobuf-2.6.1/gtest. The tests compile as C++17, as the game does
# (plan_server_novalve.md; C++11 until 2026-09-29), which 1.12.1 supports.
# Offline: -DFETCHCONTENT_SOURCE_DIR_GOOGLETEST=<an unpacked release-1.12.1>
# (FetchContent reads that variable itself and skips the download).
FetchContent_Declare(googletest
  URL https://github.com/google/googletest/archive/refs/tags/release-1.12.1.tar.gz
  URL_HASH SHA256=81964fe578e9bd7c94dfdb09c8e4d6e6759e19967e397dbea48d1c10e45d0df2)
block()
  set(BUILD_GMOCK OFF CACHE BOOL "" FORCE)
  set(INSTALL_GTEST OFF CACHE BOOL "" FORCE)
  # Built the way the tests are: C++17, -g -O1 (the old harness compiled
  # gtest-all.cc into every binary with the test's own CXXFLAGS).
  set(CMAKE_CXX_STANDARD 17)
  set(CMAKE_CXX_STANDARD_REQUIRED ON)
  set(CMAKE_CXX_EXTENSIONS OFF)
  set(CMAKE_CXX_FLAGS "-g -O1")
  # googletest 1.12.1 asks for cmake_minimum_required(3.5), which CMake 4
  # warns is deprecated. The policies it gets are 3.10's either way.
  set(CMAKE_POLICY_VERSION_MINIMUM 3.10)
  FetchContent_MakeAvailable(googletest)
endblock()
FetchContent_GetProperties(googletest)  # googletest_SOURCE_DIR, out here
if(NOT EXISTS "${googletest_SOURCE_DIR}/googletest/include/gtest/gtest.h")
  message(FATAL_ERROR "googletest is not at '${googletest_SOURCE_DIR}'")
endif()
set(SS_GTEST_INCLUDE "${googletest_SOURCE_DIR}/googletest/include")
# -I, not -isystem, as buildcpptests had it (-I$GTEST/include -I$GTEST):
# googletest marks its include directories SYSTEM, which would also silence
# warnings in gtest's templates that OUR assertions instantiate (a signed /
# unsigned EXPECT_EQ is reported inside gtest.h). Those are ours to see.
foreach(t gtest gtest_main)
  set_property(TARGET ${t} PROPERTY INTERFACE_SYSTEM_INCLUDE_DIRECTORIES "")
endforeach()

# --- the flags ------------------------------------------------------------------------
#
# buildcpptests' CXXFLAGS:
#   -std=c++17 -g -O1 -pthread -Wall -Wno-unused-parameter
#   -Werror=format -Werror=delete-non-virtual-dtor -Werror
# -O1 rather than -O0: the vtable tests are about what the compiler does with
# pointers to member functions, and an unoptimised build is not the build the
# game ships. The two -Werror promotions are the game build's (undefined
# behaviour, not style).
# -Werror for the reason cmake/SourceSharp.cmake gives: a warning ninja printed
# once and then kept the object for is a warning nobody sees again.
set(SS_CPPTEST_WARN -Wall -Wno-unused-parameter -Werror=format
  -Werror=delete-non-virtual-dtor -Werror)
set(SS_CPPTEST_CXXFLAGS -g -O1 -pthread ${SS_CPPTEST_WARN})
if(SS_IS_MSVC)
  # The Windows column: the game build's MSVC warning set (/W3 /WX and the
  # VPC /wd list, cmake/SourceSharp.cmake), /O1 for the reason -O1 is above,
  # and /EHsc: the game is built without exceptions, but a test is not
  # shipped, and gtest and the standard library want them.
  set(SS_CPPTEST_WARN ${SS_WARN_MSVC})
  set(SS_CPPTEST_CXXFLAGS /Zi /O1 /EHsc ${SS_CPPTEST_WARN})
endif()

# ss_test_compile_flags(<target>): the compile and link line every test shares.
function(ss_test_compile_flags tgt)
  set_target_properties(${tgt} PROPERTIES
    CXX_STANDARD 17 CXX_STANDARD_REQUIRED ON CXX_EXTENSIONS OFF
    RUNTIME_OUTPUT_DIRECTORY "${SS_CPPTEST_DIR}")
  target_compile_options(${tgt} PRIVATE ${SS_CPPTEST_CXXFLAGS})
  if(NOT SS_IS_MSVC)
    target_link_options(${tgt} PRIVATE -pthread)
  else()
    # The POSIX spellings the tests use for files, directories and the
    # environment, on the CRT (cmake/compat/windows-test): <dirent.h> and
    # <unistd.h> stand-ins first on the include path, the rest forced in.
    target_include_directories(${tgt} BEFORE PRIVATE ${PROJECT_SOURCE_DIR}/cmake/compat/windows-test)
    target_compile_options(${tgt} PRIVATE
      "/FI${PROJECT_SOURCE_DIR}/cmake/compat/windows-test/ss_posix_test_compat.h")
  endif()
  if(SS_IS_CLANG AND SS_LINKER STREQUAL "lld")
    target_link_options(${tgt} PRIVATE --ld-path=${SS_LD_PATH})
  endif()
endfunction()

# ss_test_engine_flags(<target>): what build_and_run_mathlib and
# build_and_run_engine_headers add for a test whose subject is a Valve library or
# header:
#   -m64 -DPOSIX -D_POSIX -DLINUX -D_LINUX -DGNUC -DNDEBUG -DPLATFORM_64BITS
#   -isystem public -isystem public/tier0 -isystem public/tier1 -isystem common
# -isystem, not -I: these four roots are Valve's, and their -Wall noise
# (platform.h's dangling else in DECLARE_ALIGNED_BYTE_ARRAY, the MSVC pragmas)
# is Valve's to own. Every warning stays live on the test's own sources.
function(ss_test_engine_flags tgt)
  if(SS_IS_MSVC)
    target_compile_definitions(${tgt} PRIVATE ${SS_CPPTEST_WINDOWS_DEFS} NDEBUG PLATFORM_64BITS)
  else()
    target_compile_options(${tgt} PRIVATE -m64)
    target_compile_definitions(${tgt} PRIVATE
      POSIX _POSIX LINUX _LINUX GNUC NDEBUG PLATFORM_64BITS)
  endif()
  target_include_directories(${tgt} SYSTEM PRIVATE
    ${SS_SRC}/public ${SS_SRC}/public/tier0 ${SS_SRC}/public/tier1 ${SS_SRC}/common)
endfunction()

set(SS_CPPTEST_KEYWORDS DEFINES OPTIONS INCLUDES SOURCES LIBS)

# --- the precompiled gtest header ------------------------------------------------------
#
# Every test TU parses gtest/gtest.h, and with it most of the standard library:
# about 0.8 s of each TU's 2.8 s at -O1 (clang 22, 40 TUs of the largest flag
# set, 2026-10-13). One precompiled gtest.h per flag set takes that back. Only
# gtest.h: adding the standard headers the tests use saved nothing more, and
# cmake_graph.h made the load slower than the parse it saved.
#
# A precompiled header is only accepted by a compile with the flags it was built
# with (clang refuses a different -O level or a different -D set), so tests
# share one when they share every flag: the same compile definitions, options,
# include directories and linked libraries, in the same directory. The first
# test of each set builds it and the rest REUSE_FROM that one: 30 headers, read
# by 332 of the 354 test TUs (2026-10-13). A set with a single TU gets none,
# since building the header costs more (1.1 s) than its one parse saves.
#
# The sets are only known once every test is defined and nothing more is added
# to one, so the headers are attached at the end of each directory that defines
# tests (cmake_language DEFER). gtest.h then comes before a test's own first
# line, which no test depends on: none defines a macro for gtest or the
# standard library first. test_pch_test holds the sets to their flags.
#
# Not on MSVC yet: its /Yu needs the forced /FI of the POSIX compat header and
# the PDB the header was built with to agree across REUSE_FROM, which is its
# own change. -DSS_CPPTEST_PCH=OFF builds every test the old way.
option(SS_CPPTEST_PCH "precompile gtest/gtest.h once per test flag set" ON)

# Notes <name> for the pass at the end of its directory.
function(_ss_cpptest_pch_record name)
  if(NOT SS_CPPTEST_PCH OR SS_IS_MSVC)
    return()
  endif()
  set_property(DIRECTORY APPEND PROPERTY SS_CPPTEST_PCH_TARGETS ${name})
  get_property(deferred DIRECTORY PROPERTY SS_CPPTEST_PCH_DEFERRED)
  if(NOT deferred)
    set_property(DIRECTORY PROPERTY SS_CPPTEST_PCH_DEFERRED ON)
    cmake_language(DEFER CALL _ss_cpptest_pch_attach)
  endif()
endfunction()

# At the end of the directory, when nothing more is added to a test: the tests
# keyed by every property that reaches their compile line (what a test's own
# CMakeLists adds after its ss_cpptest*() call included, and the libraries it
# links, whose usage requirements do), then one header per set of two or more
# TUs, built by the set's first test and reused by the others.
function(_ss_cpptest_pch_attach)
  get_property(targets DIRECTORY PROPERTY SS_CPPTEST_PCH_TARGETS)
  set(keys "")
  foreach(name IN LISTS targets)
    set(key "")
    foreach(prop COMPILE_DEFINITIONS COMPILE_OPTIONS INCLUDE_DIRECTORIES LINK_LIBRARIES)
      get_target_property(v ${name} ${prop})
      string(APPEND key "${prop}=${v}|")
    endforeach()
    string(MD5 hash "${key}")
    if(NOT hash IN_LIST keys)
      list(APPEND keys ${hash})
      set(names_${hash} "")
      set(tus_${hash} 0)
    endif()
    list(APPEND names_${hash} ${name})
    get_target_property(sources ${name} SOURCES)
    list(FILTER sources INCLUDE REGEX "\\.(cpp|cc|cxx)$")
    list(LENGTH sources n)
    math(EXPR tus_${hash} "${tus_${hash}} + ${n}")
  endforeach()
  foreach(hash IN LISTS keys)
    if(tus_${hash} LESS 2)
      continue()
    endif()
    set(names ${names_${hash}})
    list(POP_FRONT names donor)
    target_precompile_headers(${donor} PRIVATE <gtest/gtest.h>)
    foreach(name IN LISTS names)
      target_precompile_headers(${name} REUSE_FROM ${donor})
    endforeach()
  endforeach()
endfunction()

# THE TESTS NAME LINUX'S DEFINES, and on Windows they are translated here,
# once, rather than in each of ~300 calls: the POSIX platform names a test
# passes for Valve's headers (POSIX, LINUX, GNUC, COMPILER_GCC and their
# spellings) become the Windows set, so platform.h takes its Windows arm with
# the compiler that is really compiling it. Every other define a test passes
# is kept as it is.
set(SS_CPPTEST_WINDOWS_DEFS WIN32 _WIN32 WIN64 _WIN64 COMPILER_MSVC COMPILER_MSVC64)
set(SS_CPPTEST_POSIX_DEF_RE "^(POSIX|_POSIX|LINUX|_LINUX|LINUX64|GNUC|COMPILER_GCC|_LINUXSTEAMRT64|LINUXSTEAMRT64)(=.*)?$")
function(_ss_cpptest_platform_defines var)
  if(NOT SS_IS_MSVC)
    return()
  endif()
  set(out "")
  set(had_posix OFF)
  foreach(d IN LISTS ${var})
    if(d MATCHES "${SS_CPPTEST_POSIX_DEF_RE}")
      set(had_posix ON)
    elseif(d STREQUAL "_DEBUG")
      # A test defines _DEBUG for Valve's asserts (dbgflag.h's DBGFLAG_*).
      # Under MSVC _DEBUG also selects the debug C++ runtime, which the
      # release gtest library cannot link with; RELEASEASSERTS gives the same
      # DBGFLAG_* set without it.
      list(APPEND out RELEASEASSERTS)
    else()
      list(APPEND out "${d}")
    endif()
  endforeach()
  if(had_posix)
    list(APPEND out ${SS_CPPTEST_WINDOWS_DEFS})
  endif()
  set(${var} "${out}" PARENT_SCOPE)
endfunction()

# LINUX_ONLY <reason>: a test whose subject exists only on Linux (an ELF
# scan with nm or readelf, /proc, the Linux build's graph, a .so it dlopens)
# is not built on another platform, and the reason is listed at configure
# time (ss_cpptest_report_linux_only) rather than the test failing there for
# a reason that is not about the code.
function(_ss_cpptest_linux_only name reason)
  set_property(GLOBAL APPEND PROPERTY SS_CPPTEST_LINUX_ONLY "${name}: ${reason}")
endfunction()

function(ss_cpptest_report_linux_only)
  get_property(rows GLOBAL PROPERTY SS_CPPTEST_LINUX_ONLY)
  list(LENGTH rows n)
  if(n GREATER 0)
    message(STATUS "native tests not built on ${SS_PLATFORM} (LINUX_ONLY): ${n}")
    foreach(r IN LISTS rows)
      message(STATUS "  ${r}")
    endforeach()
  endif()
endfunction()

# Farm-relative paths (".", "public/tier0", "sourcesharp/test/x.cpp") made
# absolute under SS_SRC, in place.
function(_ss_cpptest_paths var)
  set(out "")
  foreach(p IN LISTS ${var})
    if(p STREQUAL ".")
      list(APPEND out "${SS_SRC}")
    elseif(IS_ABSOLUTE "${p}")
      list(APPEND out "${p}")
    else()
      list(APPEND out "${SS_SRC}/${p}")
    endif()
  endforeach()
  set(${var} "${out}" PARENT_SCOPE)
endfunction()

# The environment every test runs with. SOURCESHARP_SDK_TREE is the farm, which
# is what the managed RepoLayout reads too; the other three name the checkout,
# the CMake build (compile_commands.json and build.ninja live there) and the
# configuration, for tests whose subject is the build.
set(SS_CPPTEST_ENV
  "SOURCESHARP_SDK_TREE=${SS_FARM}"
  "SOURCESHARP_REPO=${PROJECT_SOURCE_DIR}"
  "SOURCESHARP_BUILD_DIR=${PROJECT_BINARY_DIR}"
  "SOURCESHARP_CONFIG=${SS_CONFIG}")

add_custom_target(ss_cpptests)
# The oracle tests (src/test/oracle/native) build and run apart from the native
# leg: they read the pristine Valve SDK, and make test runs them as the oracle
# leg (tools/build/buildcpptests --oracle). A directory that sets
# SS_CPPTEST_GROUP to `oracle` before its ss_cpptest*() calls puts its binaries
# here and labels them `oracle` instead of `gtest`.
add_custom_target(ss_oracle_tests)

# _ss_cpptest(<kind> <name> [DEFINES ...] [OPTIONS ...] [INCLUDES ...]
#             SOURCES ... [LIBS ...])
function(_ss_cpptest kind name)
  cmake_parse_arguments(A "" "LINUX_ONLY" "${SS_CPPTEST_KEYWORDS}" ${ARGN})
  if(A_UNPARSED_ARGUMENTS OR NOT A_SOURCES)
    message(FATAL_ERROR "ss_cpptest ${name}: SOURCES required; unparsed: ${A_UNPARSED_ARGUMENTS}")
  endif()
  if(TARGET ${name})
    message(FATAL_ERROR "ss_cpptest ${name}: named twice")
  endif()
  if(A_LINUX_ONLY AND NOT SS_PLATFORM STREQUAL linux)
    _ss_cpptest_linux_only(${name} "${A_LINUX_ONLY}")
    return()
  endif()
  if(kind STREQUAL "MATHLIB" AND SS_IS_MSVC)
    # MathLib_Init asks tier0 for GetCPUInformation, and the only 64-bit
    # Windows tier0.dll the SDK ships (devtools/bin, built for its tools)
    # does not export it: the loader would refuse the binary. On Linux the
    # SDK's libtier0.so has it. The game's own tier0.dll would.
    _ss_cpptest_linux_only(${name} "links mathlib, whose MathLib_Init needs tier0's GetCPUInformation, which the SDK's only 64-bit Windows tier0.dll (devtools/bin) does not export")
    return()
  endif()
  _ss_cpptest_paths(A_SOURCES)
  _ss_cpptest_paths(A_INCLUDES)
  _ss_cpptest_platform_defines(A_DEFINES)
  if(SS_IS_MSVC)
    # The OPTIONS the tests pass are gcc's and clang's warning switches
    # (-Wno-unknown-pragmas, -Wno-dangling-else, ...), which cl refuses; MSVC
    # has the game build's /wd list (SS_CPPTEST_WARN). Its own /options stay.
    list(FILTER A_OPTIONS INCLUDE REGEX "^/")
    # And the POSIX system libraries some tests name (dl, rt, pthread, ...):
    # the CRT and the Windows SDK hold all of it.
    list(FILTER A_LIBS EXCLUDE REGEX "^(dl|rt|pthread|m|uuid)$")
  endif()

  add_executable(${name} ${A_SOURCES})
  ss_test_compile_flags(${name})
  if(kind STREQUAL "MATHLIB" OR kind STREQUAL "ENGINE_HEADERS")
    ss_test_engine_flags(${name})
  endif()
  if(kind STREQUAL "ENGINE_HEADERS")
    # build_and_run_engine_headers' -I. (after the -isystem roots, before the
    # test's own -I list).
    target_include_directories(${name} PRIVATE ${SS_SRC})
  endif()
  target_compile_definitions(${name} PRIVATE ${A_DEFINES})
  target_compile_options(${name} PRIVATE ${A_OPTIONS})
  target_include_directories(${name} PRIVATE ${A_INCLUDES})
  target_link_libraries(${name} PRIVATE GTest::gtest GTest::gtest_main ${A_LIBS})
  _ss_cpptest_pch_record(${name})

  set(env ${SS_CPPTEST_ENV})
  if(kind STREQUAL "MATHLIB")
    # THE ONE PLACE mathlib IS LINKED (mathlib_init_guard_test counts it).
    target_link_libraries(${name} PRIVATE mathlib tier1 sdk::tier0 sdk::vstdlib)
    list(APPEND env "LD_LIBRARY_PATH=${SS_CPPTEST_LIBDIR}")
  elseif(kind STREQUAL "TIER1")
    # A real game .cpp whose only library dependency is tier1's string
    # functions: Q_strncpy is not strncpy, so the real tier1 rather than a stub.
    target_link_libraries(${name} PRIVATE tier1 sdk::tier0 sdk::vstdlib)
    list(APPEND env "LD_LIBRARY_PATH=${SS_CPPTEST_LIBDIR}")
  endif()
  if(SS_CPPTEST_GROUP STREQUAL "oracle")
    add_dependencies(ss_oracle_tests ${name})
    set(label oracle)
  else()
    add_dependencies(ss_cpptests ${name})
    set(label gtest)
  endif()

  add_test(NAME ${name} COMMAND ${name})
  set_tests_properties(${name} PROPERTIES
    WORKING_DIRECTORY "${SS_SRC}" ENVIRONMENT "${env}" LABELS ${label})
  if(SS_IS_MSVC)
    # tier0.dll and vstdlib.dll for the tests that link their import
    # libraries: the 64-bit pair the SDK ships beside its build tools
    # (sdk-links/windows.txt), found first on PATH as Linux finds
    # libtier0.so through LD_LIBRARY_PATH.
    set_property(TEST ${name} APPEND PROPERTY ENVIRONMENT_MODIFICATION
      "PATH=path_list_prepend:${SS_SRC}/devtools/bin")
  endif()
  if(kind STREQUAL "MATHLIB")
    # THE ASSERTION THAT IS ONLY A PRINTED LINE. mathlib_base.cpp guards ~30
    # entry points with Assert( s_bMathlibInitialized ), and in these binaries
    # tier0's Assert PRINTS and returns -- gtest never hears about it -- so a
    # binary that forgot MathLib_Init() computes both sides of every comparison
    # from uninitialised tables, agrees with itself and reports PASSED.
    # collideable_center_adapter_test sat in exactly that state. gtest cannot
    # see this, so the runner does: the text anywhere in the output fails the
    # test whatever its exit status. See sourcesharp/test/mathlib_test_env.h.
    set_tests_properties(${name} PROPERTIES
      FAIL_REGULAR_EXPRESSION "s_bMathlibInitialized")
  endif()
endfunction()

function(ss_cpptest name)
  _ss_cpptest(PLAIN ${name} ${ARGN})
endfunction()
function(ss_cpptest_mathlib name)
  _ss_cpptest(MATHLIB ${name} ${ARGN})
endfunction()
function(ss_cpptest_tier1 name)
  _ss_cpptest(TIER1 ${name} ${ARGN})
endfunction()
function(ss_cpptest_engine_headers name)
  _ss_cpptest(ENGINE_HEADERS ${name} ${ARGN})
endfunction()

# ss_must_not_compile(<name> EXPECT <regex> [DEFINES ...] [OPTIONS ...]
#                     [INCLUDES ...] SOURCES ...)
#
# The other half of a check that is a COMPILE ERROR rather than an assertion. A
# test can only assert about programs that compile, so a loud probe (the class
# probe in entity_funcslot.h, the slot table's missing members) is unfalsifiable
# from inside gtest. This is a ctest test that runs the compiler with
# -fsyntax-only at TEST time (cmake/must_not_compile.cmake) and fails when the
# file COMPILES, or when it fails somewhere other than the error line EXPECT
# names -- a typo refuses just as loudly, and is not evidence.
#
# EXPECT is a regular expression matched against each `error:` line. The
# conversions give gcc's wording and clang's side by side where they differ.
function(ss_must_not_compile name)
  cmake_parse_arguments(A "" "EXPECT" "${SS_CPPTEST_KEYWORDS}" ${ARGN})
  if(A_UNPARSED_ARGUMENTS OR NOT A_SOURCES OR NOT A_EXPECT OR A_LIBS)
    message(FATAL_ERROR "ss_must_not_compile ${name}: EXPECT and SOURCES required, no LIBS")
  endif()
  if(TARGET ${name} OR TEST ${name})
    message(FATAL_ERROR "ss_must_not_compile ${name}: named twice")
  endif()
  if(SS_IS_MSVC)
    _ss_cpptest_linux_only(${name} "a must-not-compile check: it runs the compiler with gcc's and clang's flags and EXPECTs their error wording")
    return()
  endif()
  _ss_cpptest_paths(A_SOURCES)
  _ss_cpptest_paths(A_INCLUDES)
  list(TRANSFORM A_DEFINES PREPEND "-D")
  list(TRANSFORM A_INCLUDES PREPEND "-I")

  # buildcpptests' line: g++ $CXXFLAGS -I$GTEST/include -I$GTEST -fsyntax-only "$@"
  set(args -std=c++17 ${SS_CPPTEST_CXXFLAGS} -I${SS_GTEST_INCLUDE} -fsyntax-only
    ${A_DEFINES} ${A_OPTIONS} ${A_INCLUDES} ${A_SOURCES})
  set(rsp "")
  foreach(a IN LISTS args)
    string(REPLACE "\\" "\\\\" a "${a}")
    string(REPLACE "\"" "\\\"" a "${a}")
    string(APPEND rsp "\"${a}\"\n")
  endforeach()
  set(dir "${CMAKE_CURRENT_BINARY_DIR}/must_not_compile")
  file(CONFIGURE OUTPUT "${dir}/${name}.rsp" CONTENT "${rsp}")
  file(CONFIGURE OUTPUT "${dir}/${name}.cmake" CONTENT
"set(NAME [==[${name}]==])
set(CXX [==[${CMAKE_CXX_COMPILER}]==])
set(RSP [==[${dir}/${name}.rsp]==])
set(ERR [==[${SS_CPPTEST_DIR}/${name}.err]==])
set(EXPECT [==[${A_EXPECT}]==])
")
  add_test(NAME ${name}
    COMMAND ${CMAKE_COMMAND} -DCONFIG=${dir}/${name}.cmake
            -P ${PROJECT_SOURCE_DIR}/cmake/must_not_compile.cmake)
  set_tests_properties(${name} PROPERTIES
    WORKING_DIRECTORY "${SS_SRC}" ENVIRONMENT "${SS_CPPTEST_ENV}"
    LABELS must-not-compile)
endfunction()

# ss_census(<name>): moves an ss_cpptest*() binary into the `census` tier.
# The census tests drive the census tools (globals-census, entity-census,
# 4f-component, orphan reach) over the real build, 2-15 s each and about 70%
# of the native leg's CPU; they run in `make test-full` (buildcpptests
# --census), not in the routine `make test`. A label, not a second helper:
# tools/leg-floor counts every `ss_cpptest*` call as a binary, and reads these
# calls to split the native and census legs' rosters. Call it after the test
# is defined.
function(ss_census name)
  get_property(linux_only GLOBAL PROPERTY SS_CPPTEST_LINUX_ONLY)
  if(linux_only MATCHES "(^|;)${name}: ")
    return()  # not built on this platform (LINUX_ONLY)
  endif()
  if(NOT TEST ${name})
    message(FATAL_ERROR "ss_census ${name}: no such test (call it after its ss_cpptest*())")
  endif()
  set_property(TEST ${name} APPEND PROPERTY LABELS census)
endfunction()
