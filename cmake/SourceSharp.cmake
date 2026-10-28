# The flags every native target shares, and how its output is deployed.
#
# Translated in Phase 6b from the fork's vpc_scripts/source_posix_ninja.vpc
# (the ninja `cxx` and `link_shared` rules VPC generated from it). The GNU
# column is that file's list unchanged: it is what CI's gcc, inside the
# sniper container, compiles with. The Clang column is the same intent spelled
# for clang 22, which is what a local build uses; every difference is named
# where it is made.
#
# THREE PLATFORMS (2026-10-12). Linux is where the game runs and what every
# test leg measures; the Windows (MSVC) and macOS (Apple clang, x86_64) columns
# are the same intent spelled for those toolchains, from Valve's own
# source_win32_base.vpc / source_dll_win32_*.vpc and source_posix_base.vpc
# [$OSXALL], so the same libraries build on every platform the engine has. The
# Linux column is untouched by the other two: every platform-specific line is
# under an SS_PLATFORM test, and what Linux compiles with is what it compiled
# with before. Valve ships prebuilt libraries for linux64 and x64 (Windows)
# and none for macOS, so a macOS build links with -undefined dynamic_lookup
# and leaves out the one target that needs a prebuilt archive
# (cmake/sdk/CMakeLists.txt says which).

include_guard(GLOBAL)

# --- platform --------------------------------------------------------------------

# linux, windows or osx: which column below applies, and which bin/<config>
# subdirectory the engine loads game libraries from (Valve's layout: linux64,
# x64, and osx64 for a 64-bit macOS build).
if(WIN32)
  set(SS_PLATFORM windows)
  set(SS_PLATFORM_BINDIR x64)
elseif(APPLE)
  set(SS_PLATFORM osx)
  set(SS_PLATFORM_BINDIR osx64)
else()
  set(SS_PLATFORM linux)
  set(SS_PLATFORM_BINDIR linux64)
endif()

# --- options -------------------------------------------------------------------

# The linker drops every function and variable nothing reachable refers to.
# OFF is a diagnostic: the first thing to try when a symbol goes missing without
# a link error (`make debug GC_SECTIONS=0`). It changes the compile line too, so
# crossing it rebuilds everything.
option(SS_GC_SECTIONS "link with --gc-sections (and compile with -ffunction/data-sections)" ON)

# --coverage on every translation unit and link, for reach evidence. Also
# defines SS_COVERAGE=1 so the C++ can see it is a coverage build (see
# shell_diagnostics.cpp). Forces nothing about the build type.
option(SS_COVERAGE "instrument every native target with --coverage" OFF)

# A release build splits each library's DWARF into <name>.dbg as it deploys
# (cmake/deploy.cmake). OFF keeps it in the binary (buildsourcesharp --no-strip).
# Linux only: MSVC writes a .pdb beside the DLL already, and macOS debug info
# lives in the object files (a .dSYM is dsymutil's job, not this build's).
option(SS_SPLIT_DEBUG "release: split debug info into <name>.dbg on deploy" ON)

# The linker, for a clang build: lld, found on PATH. Distributions install it
# versioned (ld.lld-22) with no unversioned ld.lld, hence --ld-path rather than
# -fuse-ld=lld. SS_LINKER=bfd is the recorded fallback: GNU ld, through clang's
# default. Linux only: Apple's clang links with Apple's ld, MSVC with link.exe.
set(SS_LINKER "lld" CACHE STRING "lld or bfd (Linux clang builds only)")
set_property(CACHE SS_LINKER PROPERTY STRINGS lld bfd)
if(SS_PLATFORM STREQUAL linux)
  find_program(SS_LD_PATH NAMES ld.lld-22 ld.lld
               DOC "the lld binary clang links with when SS_LINKER=lld")
endif()

# --- where things are ------------------------------------------------------------

string(TOLOWER "${CMAKE_BUILD_TYPE}" SS_CONFIG)
if(NOT SS_CONFIG MATCHES "^(debug|release)$")
  message(FATAL_ERROR "CMAKE_BUILD_TYPE must be Debug or Release (use a preset: cmake --preset debug)")
endif()

# Sources are compiled FROM THE FARM (tools/setup): farm/<config>/src looks like
# the old single-repo checkout, with every Valve file linked in per file from the
# fork (sdk-links/*.txt) and every file of ours linked from src/<module>. So
# sdk-links/ stays the one record of the Valve files this build depends on.
set(SS_FARM "${PROJECT_SOURCE_DIR}/farm/${SS_CONFIG}")
set(SS_SRC "${SS_FARM}/src")
# vendor/ holds the Valve files the modules build from (vendor/README.md):
# shared/ both sides, server/ and client/ one side each.
set(SS_VENDOR "${PROJECT_SOURCE_DIR}/vendor")
set(SHARED "${PROJECT_SOURCE_DIR}/src/shared/native")

# ss_vendor_includes(<out-var> <side> <dir>...): each Valve include dir, the
# side's copy ahead of the shared one, in the order given.
function(ss_vendor_includes out side)
  set(dirs)
  foreach(sub IN LISTS ARGN)
    list(APPEND dirs ${SS_VENDOR}/${side}/${sub} ${SS_VENDOR}/shared/${sub})
  endforeach()
  set(${out} ${dirs} PARENT_SCOPE)
endfunction()

# The pristine upstream clone at valve-sdk.lock (`make setup` provisions it;
# $SOURCESHARP_VALVE_SDK overrides): libss_particles and the tier1 and mathlib
# it links build from it, never from vendor/ or the fork (user 2026-10-25), so
# vendor/ holds only what client.so and server.so read.
set(SS_STOCK_SDK "$ENV{SOURCESHARP_VALVE_SDK}")
if(NOT SS_STOCK_SDK AND EXISTS "${PROJECT_SOURCE_DIR}/valve-sdk.lock.local")
  file(STRINGS "${PROJECT_SOURCE_DIR}/valve-sdk.lock.local" SS_STOCK_SDK LIMIT_COUNT 1)
endif()
if(NOT EXISTS "${SS_STOCK_SDK}/src/public/tier0/platform.h")
  message(FATAL_ERROR "no pristine Valve SDK clone at '${SS_STOCK_SDK}': run make setup "
                      "(or set SOURCESHARP_VALVE_SDK); libss_particles builds from it")
endif()
set(SS_STOCK "${SS_STOCK_SDK}/src")

# ss_stock_includes(<out-var> <dir>...): the clone's include dirs, behind the
# clang wrapper for the one stock header clang rejects (cmake/compat/clang).
function(ss_stock_includes out)
  set(dirs)
  # clang and clang-cl (which SS_IS_CLANG does not name: it is MSVC's front end).
  if(CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
    list(APPEND dirs ${PROJECT_SOURCE_DIR}/cmake/compat/clang)
  endif()
  foreach(sub IN LISTS ARGN)
    list(APPEND dirs ${SS_STOCK}/${sub})
  endforeach()
  set(${out} ${dirs} PARENT_SCOPE)
endfunction()

# ss_stock_target(<target>): what a target built from the clone needs beyond
# its include dirs: under clang-cl, the stock headers clang-cl rejects and MSVC
# accepts, settled first (cmake/compat/clang-cl/ss_stock_clang_cl.h).
function(ss_stock_target tgt)
  if(SS_IS_MSVC AND CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
    target_compile_options(${tgt} PRIVATE "/FI${PROJECT_SOURCE_DIR}/cmake/compat/clang-cl/ss_stock_clang_cl.h")
  endif()
endfunction()

# bin/<config> IS the game directory; the shared native binaries go to
# bin/<platform dir> (linux64, x64, osx64).
set(SS_BIN "${PROJECT_SOURCE_DIR}/bin/${SS_CONFIG}")
# CI presets deploy elsewhere (bin/ci-<config>/linux64), so `make ci-build` never
# replaces the binaries of the local game directory.
set(SS_DEPLOY_DIR "${SS_BIN}/bin/${SS_PLATFORM_BINDIR}" CACHE PATH "where the built libraries are deployed")

if(NOT EXISTS "${SS_SRC}/public/tier0/platform.h")
  message(FATAL_ERROR "no farm at ${SS_FARM}: run `make setup ROOT=../source-sdk-2013`")
endif()

# CMake's per-build-type defaults (-O3 -DNDEBUG, -g; /MDd /Zi /Ob0 /Od /RTC1 and
# /MD /O2 /Ob2 /DNDEBUG on MSVC) are replaced by the lists below, which are the
# VPC build's.
set(CMAKE_CXX_FLAGS_DEBUG "")
set(CMAKE_CXX_FLAGS_RELEASE "")
set(CMAKE_C_FLAGS_DEBUG "")
set(CMAKE_C_FLAGS_RELEASE "")

set(SS_IS_CLANG OFF)
set(SS_IS_MSVC OFF)
if(SS_PLATFORM STREQUAL windows)
  # cl, or clang-cl, which takes cl's flags and links with lld-link: with the
  # MSVC CRT and Windows SDK laid out by xwin it cross-builds this column
  # from Linux. MSVC stays what the shipped build and CI use.
  if(NOT (CMAKE_CXX_COMPILER_ID STREQUAL "MSVC" OR CMAKE_CXX_COMPILER_FRONTEND_VARIANT STREQUAL "MSVC"))
    message(FATAL_ERROR "unsupported compiler ${CMAKE_CXX_COMPILER_ID} on Windows: MSVC (Valve's README names VS 2022's v143), or clang-cl")
  endif()
  set(SS_IS_MSVC ON)
elseif(SS_PLATFORM STREQUAL osx)
  if(NOT CMAKE_CXX_COMPILER_ID MATCHES "^(Apple)?Clang$")
    message(FATAL_ERROR "unsupported compiler ${CMAKE_CXX_COMPILER_ID} on macOS: Apple clang")
  endif()
  set(SS_IS_CLANG ON)
  # The engine is x86-64 only (mathlib's SSE paths, x86 inline assembly), so an
  # Apple Silicon machine builds the x86_64 slice. The preset says so; this is
  # the check that it did.
  if(NOT CMAKE_OSX_ARCHITECTURES STREQUAL "x86_64")
    message(FATAL_ERROR "macOS builds are x86_64 only: configure with -DCMAKE_OSX_ARCHITECTURES=x86_64 (the osx-* presets do)")
  endif()
else()
  if(CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
    set(SS_IS_CLANG ON)
  elseif(NOT CMAKE_CXX_COMPILER_ID STREQUAL "GNU")
    message(FATAL_ERROR "unsupported compiler ${CMAKE_CXX_COMPILER_ID}: clang (local) or gcc (CI)")
  endif()
  if(SS_IS_CLANG AND SS_LINKER STREQUAL "lld" AND NOT SS_LD_PATH)
    message(FATAL_ERROR "no lld on PATH (ld.lld-22 or ld.lld): install lld-22, "
                        "or configure with -DSS_LINKER=bfd")
  endif()
endif()

if(SS_IS_MSVC)
  # Valve's game libraries link the static CRT (/MT, /MTd in debug:
  # source_dll_win32_*.vpc $RuntimeLibrary). CMake's default is the DLL CRT.
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  # NO C++ EXCEPTIONS, as the GNU column's -fno-exceptions (and VPC's
  # $EnableC++Exceptions "No"): CMake puts /EHsc on every MSVC compile line by
  # default, and it is taken off here, once, for every target.
  string(REPLACE "/EHsc" "" CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS}")
endif()

# --- compile ---------------------------------------------------------------------

if(SS_IS_MSVC)
  # source_dll_win32_debug.vpc / _release.vpc: /Od vs /O2 /Ob2 (Maximize Speed,
  # Any Suitable inlining), /Zi program database both, /Zo (richer debug info
  # for optimised code, release), /GF string pooling, /Oy- (frame pointers
  # kept: FRAME_POINTER_OMISSION_DISABLED, as the GNU -fno-omit-frame-pointer).
  set(SS_OPT_debug /Od /Zi /Oy- /GF)
  set(SS_OPT_release /O2 /Ob2 /Zi /Zo /GF /Oy-)
else()
  set(SS_OPT_debug -O0 -ggdb3)
  set(SS_OPT_release -O2 -g -fno-strict-aliasing -ffast-math -fno-omit-frame-pointer -ftree-vectorize)
  # DWARF 4, the version the VPC build's gcc 10 wrote by default. clang 22
  # defaults to DWARF 5, where GNU `nm -l` finds no line for a DATA symbol, and
  # tools/globals-census reads exactly that to categorise each static.
  list(APPEND SS_OPT_debug -gdwarf-4)
  list(APPEND SS_OPT_release -gdwarf-4)
  if(NOT SS_IS_CLANG)
    # gcc-only optimisation switches; clang has no spelling for either (its loop
    # unswitching is on at -O2, and predictive commoning has no equivalent).
    list(APPEND SS_OPT_release -fpredictive-commoning -funswitch-loops)
    # No speculative devirtualization (gcc -O2 has it on; clang has no such
    # pass). gcc guesses the dynamic type of a virtual call from the classes
    # a header defines inline, and compiles that class's body behind a
    # vtable check. Our libraries leave out Valve code on purpose, and the
    # guesses reach it: random_valve->RandomFloat() guessed as vstdlib's
    # CDefaultUniformRandomStream calls RandomFloat, which server.so does not
    # link; our view's GetFrustum() guessed as CViewRender's calls
    # CBase3dView::GetFrustum, which client.so does not compile. Both are
    # undefined symbols at link, and the guess is wrong at run time anyway.
    list(APPEND SS_OPT_release -fno-devirtualize-speculatively)
  endif()
endif()
set(SS_DEFS_debug DEBUG _DEBUG)
set(SS_DEFS_release NDEBUG)
if(SS_IS_MSVC)
  # source_dll_win32_debug.vpc's one extra: the checked-iterator warnings off.
  list(APPEND SS_DEFS_debug _SCL_SECURE_NO_WARNINGS)
endif()

# The warning set. -Wswitch followed by -Wno-switch in the VPC list nets to
# -Wno-switch, written once here.
set(SS_WARN_GNU
  -Wall -Wno-multichar -Wno-write-strings -Wno-unused-variable -Wno-unused-function
  -Winvalid-pch -Wno-sign-compare -Wno-nonnull-compare -Wno-ignored-attributes
  -Wno-misleading-indentation -Wno-unused-but-set-variable -Wno-unused-local-typedefs
  -Wno-switch -Wno-parentheses -Wno-unknown-pragmas -Wno-unused-parameter
  -Wno-unused-value -Wno-comment -Wno-float-equal -Werror=return-type -Wformat
  -Wformat-security -Werror=format
  -Wno-overloaded-virtual -Wno-placement-new -Wno-c++20-compat -Wno-class-memaccess
  -Wno-conversion-null -Wno-invalid-offsetof -Wno-reorder -Werror=delete-non-virtual-dtor)
# gcc 10 (CI's sniper container) only, and not in the VPC list: its
# -Wmaybe-uninitialized follows inlining, and CUtlVectorFixedGrowable's switch
# from inline to heap storage (utlmemory.h's memcpy of the whole fixed
# buffer) reads elements nothing has written yet, by design. It fires in
# every TU that grows one past its fixed count (shell_material_proxy_client,
# shell_managed_particles), always in Valve's header. clang's
# -Wuninitialized, which is on, still checks our own code.
list(APPEND SS_WARN_GNU -Wno-maybe-uninitialized)
# The same intent for clang. Dropped, because clang has no such warning:
# -Wno-nonnull-compare, -Wno-placement-new, -Wno-class-memaccess,
# -Wno-conversion-null. Added lines carry their
# reason; every one is about Valve code, and none hides a warning in ours that
# gcc would have reported.
set(SS_WARN_CLANG
  -Wall -Wno-multichar -Wno-write-strings -Wno-unused-variable -Wno-unused-function
  -Winvalid-pch -Wno-sign-compare -Wno-ignored-attributes
  -Wno-misleading-indentation -Wno-unused-but-set-variable -Wno-unused-local-typedefs
  -Wno-switch -Wno-parentheses -Wno-unknown-pragmas -Wno-unused-parameter
  -Wno-unused-value -Wno-comment -Wno-float-equal -Werror=return-type -Wformat
  -Wformat-security -Werror=format
  -Wno-overloaded-virtual -Wno-c++20-compat
  -Wno-invalid-offsetof -Wno-reorder -Werror=delete-non-virtual-dtor
  # Clang-only diagnostics that fire in VALVE code (the fork's files and the
  # headers every TU includes), measured over a full debug build in Phase 6b.
  # Each names a warning gcc does not have or does not enable at -Wall; the
  # sites are Valve's to own, and our own sites were fixed instead.
  #   426 sites: Valve headers mark some overrides `override` and not others
  #   (c_baseplayer.h, ...). Also vgui's DECLARE_CLASS_SIMPLE macro, whose
  #   GetMessageMap() has no `override`, expanded in our three vgui panels.
  -Wno-inconsistent-missing-override
  #   38 sites: memset/memcpy over classes with constructors
  #   (interpolatedvar.h, bitvec.h, ...).
  -Wno-nontrivial-memcall
  #   6 sites: deliberate null-reference idioms (iclientrenderable.h:266,
  #   clientmode_shared.cpp, c_basetempentity.h).
  -Wno-null-dereference
  #   4 sites: `if ( this )` checks (jobthread.h:495, studio.cpp:858, viewdebug.cpp).
  -Wno-undefined-bool-conversion
  #   4 sites: unused private fields (bone_setup.h:434, camomaterialproxy.cpp, ...).
  -Wno-unused-private-field
  #   2 sites: `T x();` declarations in shell_clientdll.cpp (relocated Valve code).
  -Wno-vexing-parse
  #   1 site each: bone_setup.cpp:5105 float[] vs float[N]; ButtonCode.h:313;
  #   vgui_debugoverlaypanel.cpp:120 array != NULL; shell_soundscape_client.cpp:1092
  #   float literal to bool (both relocated Valve code).
  -Wno-array-parameter -Wno-tautological-overlap-compare
  -Wno-tautological-pointer-compare -Wno-literal-conversion
  #   2 sites: memoverride.cpp:433/465 replace operator delete without the
  #   implicit noexcept of the standard declaration.
  -Wno-implicit-exception-spec-mismatch
  #   tier1 only: KeyValues.cpp:2526 compares a char with a value out of its
  #   range. (checksum_md5.cpp's `register`, the one C++17 removed, left the
  #   build with that file: cmake/sdk/tier1.cmake.)
  -Wno-tautological-constant-out-of-range-compare)
if(SS_PLATFORM STREQUAL osx)
  # Apple's clang is a release or two behind LLVM's, so a -Wno- above it does
  # not know yet must not itself be the warning (-Werror) that stops the build.
  list(APPEND SS_WARN_CLANG -Wno-unknown-warning-option)
  # Apple's SDK marks sprintf, strcpy and friends deprecated
  # (__deprecated_msg in its headers; nothing else's libc does), and Valve's
  # code calls them throughout (checksum_sha1.cpp first). Valve's sites, so
  # the warning is off here rather than the code edited.
  list(APPEND SS_WARN_CLANG -Wno-deprecated-declarations)
endif()
# MSVC: source_win32_base.vpc's set. /W3, not VPC's /W4: VPC turns warnings
# into errors on 32-bit Windows only ([!$WIN64]) and lets the 64-bit build's
# /W4 noise through; here every warning is an error (/WX, the GNU column's
# -Werror), at the level Valve's code is clean at. Each /wd is VPC's, with
# its reason: 4316 (object allocated on the heap may not be aligned), 5033
# ('register' storage class), 5054/5055 (operators between enumerations),
# 4577 (noexcept with no exception handling mode), 4091 ('keyword' ignored
# on left of 'type'), 4355 ('this' used in base member initializer list),
# 4101 (unreferenced local variable).
set(SS_WARN_MSVC
  /W3 /WX /wd4316 /wd5033 /wd5054 /wd5055 /wd4577 /wd4091 /wd4355 /wd4101
  # The GNU column's -Wno-unused-*, -Wno-switch, -Wno-parentheses and
  # -Wno-unknown-pragmas, as MSVC numbers them: 4100 (unreferenced formal
  # parameter), 4189 (local initialised but not referenced), 4505 (unreferenced
  # local function), 4061/4062 (enumerator not handled in switch), 4554
  # (operator precedence), 4068 (unknown pragma), 4244/4267/4305 (the narrowing
  # conversions Valve's code is full of), 4996 (deprecated CRT names).
  /wd4100 /wd4189 /wd4505 /wd4061 /wd4062 /wd4554 /wd4068 /wd4244 /wd4267 /wd4305 /wd4996)

# The project-wide define set every VPC target had, per platform.
if(SS_PLATFORM STREQUAL windows)
  # source_win32_base.vpc [$WIN64] + source_dll_win32_base.vpc + the
  # per-configuration lists in source_dll_win32_{debug,release}.vpc, and
  # source_video_base.vpc's [$WIN32] video set. No GNUC/POSIX, no GL or SDL:
  # the Windows client renders through DirectX, in the engine.
  set(SS_COMMON_DEFS
    VPROF_LEVEL=1
    VPC SOURCESDK SOURCE_HAS_FREETYPE
    COMPILER_MSVC COMPILER_MSVC64 PLATFORM_64BITS WIN64 _WIN64 WIN32 _WIN32 _WINDOWS _USRDLL
    _CRT_SECURE_NO_DEPRECATE _CRT_NONSTDC_NO_DEPRECATE
    _ALLOW_RUNTIME_LIBRARY_MISMATCH _ALLOW_ITERATOR_DEBUG_LEVEL_MISMATCH _ALLOW_MSC_VER_MISMATCH
    _DLL_EXT=.dll _EXTERNAL_DLL_EXT=.dll
    BINK_VIDEO AVI_VIDEO WMV_VIDEO DEV_BUILD FRAME_POINTER_OMISSION_DISABLED
    VPCGAME=csharp VPCGAMECAPS=CSHARP)
elseif(SS_PLATFORM STREQUAL osx)
  # source_posix_base.vpc [$OSXALL] + [$POSIX] + source_video_base.vpc's
  # [$OSXALL] and [$GL]/[$SDL] sets: the macOS client was GL through
  # togl, with SDL for its window and input.
  set(SS_COMMON_DEFS
    VPROF_LEVEL=1 GNUC NO_HOOK_MALLOC _FILE_OFFSET_BITS=64
    VPC SOURCESDK SOURCE_HAS_FREETYPE COMPILER_GCC _DLL_EXT=.dylib
    _OSX OSX _DARWIN_UNLIMITED_SELECT FD_SETSIZE=10240 OVERRIDE_V_DEFINES
    PLATFORM_64BITS POSIX=1 _POSIX=1 GL_GLEXT_PROTOTYPES
    DX_TO_GL_ABSTRACTION USE_SDL QUICKTIME_VIDEO FORCE_QUICKTIME
    DEV_BUILD FRAME_POINTER_OMISSION_DISABLED
    _DLL_PREFIX=lib _EXTERNAL_DLL_EXT=.dylib VPCGAME=csharp VPCGAMECAPS=CSHARP)
else()
  set(SS_COMMON_DEFS
    VPROF_LEVEL=1 GNUC NO_HOOK_MALLOC _FILE_OFFSET_BITS=64
    # The project-wide set every VPC target had. Where VPC defined a name twice
    # (-D_LINUX then -D_LINUX=1), the later spelling is the one the compiler kept
    # and the one written here.
    VPC SOURCESDK SOURCE_HAS_FREETYPE COMPILER_GCC _DLL_EXT=.so _LINUX=1 LINUX=1
    PLATFORM_64BITS POSIX=1 _POSIX=1 BINK_VIDEO GL_GLEXT_PROTOTYPES
    DX_TO_GL_ABSTRACTION USE_SDL DEV_BUILD FRAME_POINTER_OMISSION_DISABLED
    _DLL_PREFIX=lib _EXTERNAL_DLL_EXT=.so _LINUXSTEAMRT64=1 LINUX64=1
    LINUXSTEAMRT64=1 SYSTEM_TRIPLE=x86_64-linux-gnu VPCGAME=csharp VPCGAMECAPS=CSHARP)
endif()
# Not carried over: -Dsprintf=... -Dstrncpy=... -DPROTECTED_THINGS_ENABLE, which
# the VPC rule defined per project and then undid on the same line with
# -Usprintf -Ustrncpy -UPROTECTED_THINGS_ENABLE. The compiler never saw them.

# The GNU wrappers VPC's link rule installs (every file-system entry point goes
# through tier0's path-case fixups). Linux only: they are glibc's names, and
# the case fixups are for a case-sensitive file system.
set(SS_WRAPPED fopen freopen open creat access __xstat stat stat64 lstat fopen64
  open64 opendir chmod chown lchown symlink link __lxstat64 mknod utimes unlink
  rename utime __xstat64 mount mkfifo mkdir rmdir scandir realpath __lxstat)

# ss_sdk_compile(<target> PROJECTDIR <farm-relative dir>)
#
# The VPC build's compile line, for any native target (the static Valve
# libraries use only this half). PROJECTDIR is the directory the .vpc lived in;
# it is a -D the Valve sources may read, and it names the farm path.
#
# C++17, NOT THE VPC LINE'S C++11 (2026-09-29, plan_server_novalve.md): every
# native target and every test. Measured before the switch: the server's 107
# and the client's 253 translation units compile with zero errors and zero
# warnings as C++17 under these same flags.
function(ss_sdk_compile tgt)
  cmake_parse_arguments(A "" "PROJECTDIR" "" ${ARGN})
  set_target_properties(${tgt} PROPERTIES
    CXX_STANDARD 17 CXX_STANDARD_REQUIRED ON CXX_EXTENSIONS OFF
    CXX_VISIBILITY_PRESET hidden
    POSITION_INDEPENDENT_CODE ON
    ARCHIVE_OUTPUT_DIRECTORY "${PROJECT_BINARY_DIR}/lib"
    LIBRARY_OUTPUT_DIRECTORY "${PROJECT_BINARY_DIR}/out"
    RUNTIME_OUTPUT_DIRECTORY "${PROJECT_BINARY_DIR}/out")
  if(SS_IS_MSVC)
    # /fp:fast, /Zc:forScope, /Zm200 (source_dll_win32_*.vpc); /Gw, /Zc:inline,
    # /Zc:threadSafeInit-, /Wv:18, /Zc:__cplusplus (source_win32_base.vpc, each
    # with its reason there); /GS, the buffer security check a non-retail
    # build keeps; /bigobj for the translation units whose section count the
    # default format cannot hold (the client's are large).
    target_compile_options(${tgt} PRIVATE
      ${SS_OPT_${SS_CONFIG}} /fp:fast /Zc:forScope /Zm200 /Gw /Zc:inline
      /Zc:threadSafeInit- /Wv:18 /Zc:__cplusplus /GS /bigobj
      ${SS_WARN_MSVC})
    # clang-cl (the windows-cross presets) checks that the Windows arms
    # compile and link; its warnings are clang's, not cl's, so they are off
    # there and MSVC on CI holds the zero-warning rule.
    if(CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
      target_compile_options(${tgt} PRIVATE -w)
    endif()
  else()
    if(SS_IS_CLANG)
      set(warn ${SS_WARN_CLANG})
    else()
      set(warn ${SS_WARN_GNU})
    endif()
    # -Werror: the tree holds zero warnings, and ninja prints a warning only on
    # the build that compiles the file -- the object stays and the warning is
    # never seen again. As an error there is no object, so every build after
    # stops at that file until it is fixed. A Valve site gets a named -Wno- on
    # its target with a comment, never an edit (CLAUDE.md).
    # NO C++ EXCEPTIONS (user ruling 2026-10-01: "c++ should not be using
    # exceptions"). Nothing of ours throws or catches; with no landing pads
    # nothing references _Unwind_Resume, so -static-libgcc no longer copies the
    # unwinder in. backtrace() is glibc's and loads libgcc_s itself.
    target_compile_options(${tgt} PRIVATE
      ${SS_OPT_${SS_CONFIG}} -fno-exceptions -fno-delete-null-pointer-checks -march=core2 -pipe
      ${warn} -Werror
      $<$<BOOL:${SS_GC_SECTIONS}>:-ffunction-sections -fdata-sections>
      $<$<BOOL:${SS_COVERAGE}>:--coverage>)
  endif()
  target_compile_definitions(${tgt} PRIVATE
    ${SS_COMMON_DEFS} ${SS_DEFS_${SS_CONFIG}}
    PROJECTDIR=${SS_SRC}/${A_PROJECTDIR}
    $<$<BOOL:${SS_COVERAGE}>:SS_COVERAGE=1>)
  if(SS_PLATFORM STREQUAL osx)
    # platform.h includes <malloc.h> on every platform; macOS has no such
    # header (its allocator API is <malloc/malloc.h>), so one that forwards
    # to it sits first on the include path there. Under cmake/, not the shim
    # tree: it is build plumbing, and the shim's line ratchet walks the tree.
    target_include_directories(${tgt} BEFORE PRIVATE
      ${PROJECT_SOURCE_DIR}/cmake/compat/osx)
  endif()
  # CMake defines <target>_EXPORTS in every shared library's TUs; VPC did not,
  # and nothing reads it.
  set_target_properties(${tgt} PROPERTIES DEFINE_SYMBOL "")
endfunction()

# ss_sdk_target(<target> PROJECTDIR <farm-relative dir> [RPATH_ORIGIN] [NO_WRAP]
#               [NO_RUNTIME])
#
# ss_sdk_compile plus the VPC build's shared-library link line and the deploy
# into the game directory. NO_WRAP leaves out tier0's file-system wrappers, for
# a target that links no tier0 (the host, and server.so since
# plan_server_novalve.md P4): with them, every opendir it calls is an import
# from tier0.
#
# NO_RUNTIME, for a library that calls nothing (libss_fwd_server): no C
# runtime, no C++ runtime and none of the system libraries the VPC line
# names, so it needs no other library at all. On Linux, -nostdlib: no libc,
# no libgcc and no crt start files, whose only work is running global
# constructors and destructors (with libc's __cxa_finalize) for a library
# that has none; -fno-stack-protector, as some distributions' gcc turns it on
# and its check is libc's. On Windows, /NODEFAULTLIB, /NOENTRY (no DllMain)
# and /GS- (the security cookie is the CRT's). On both, nothing runs at load
# but the loader's relocations, so the library may hold no dynamic
# initialiser, and --no-undefined (on Windows, any unresolved symbol) still
# fails the link on anything it would call. The static CRT had been most of
# such a DLL: 104 KB of 105 for the forwarders.
function(ss_sdk_target tgt)
  cmake_parse_arguments(A "RPATH_ORIGIN;NO_WRAP;NO_RUNTIME" "PROJECTDIR;VERSION_SCRIPT" "" ${ARGN})
  # VERSION_SCRIPT: the export list. Valve's devtools script (global: *) unless
  # the target names its own -- server.so's exports CreateInterface only.
  if(NOT A_VERSION_SCRIPT)
    set(A_VERSION_SCRIPT ${SS_STOCK}/devtools/version_script.linux.txt)
  endif()
  ss_sdk_compile(${tgt} PROJECTDIR ${A_PROJECTDIR})

  # Valve's game libraries carry no `lib` prefix on any platform (server.dll,
  # client.dylib); a target that wants one says so after this (the host).
  set_target_properties(${tgt} PROPERTIES PREFIX "")

  if(SS_IS_MSVC)
    # source_dll_win32_base.vpc's link line: the Windows system libraries every
    # Valve DLL names, /ignore:4221 (an object with no public symbols), and
    # /DEBUG:FULL (the .pdb the .vpc asks for, "optimized for sharing and
    # publishing"). What is exported is what the sources mark DLL_EXPORT
    # (CreateInterface, the host's SOURCESHARP_EXPORT API): there is no
    # version script to say otherwise, and nothing else is marked.
    # /PDBALTPATH:%_PDB%: the DLL names its .pdb by file name alone, not the
    # build machine's absolute path; a debugger finds it on its symbol path.
    target_link_options(${tgt} PRIVATE /DEBUG:FULL /ignore:4221 /PDBALTPATH:%_PDB%
      $<$<BOOL:${SS_GC_SECTIONS}>:/OPT:REF>)
    if(A_NO_RUNTIME)
      # SS_NO_CRT: the sources define the one symbol MSVC's code generation
      # expects of the CRT (_fltused); a test that compiles the same source
      # into an executable with a CRT leaves it to the CRT.
      target_compile_options(${tgt} PRIVATE /GS-)
      target_compile_definitions(${tgt} PRIVATE SS_NO_CRT)
      target_link_options(${tgt} PRIVATE /NODEFAULTLIB /NOENTRY)
    else()
      # rpcrt4: UuidCreate and friends, which Valve's code called where Linux
      # linked libuuid (no Linux game DLL imports it any more; the Windows set
      # is CI-verified).
      target_link_libraries(${tgt} PRIVATE shell32 user32 advapi32 gdi32 comdlg32 ole32 rpcrt4)
    endif()
  elseif(SS_PLATFORM STREQUAL osx)
    # Apple's ld: -dead_strip is --gc-sections. No version script (the
    # symbols' own visibility decides: hidden by default, DLL_EXPORT for
    # CreateInterface), and -undefined dynamic_lookup because Valve ships no
    # macOS tier0/vstdlib to link against; what the engine provides at load
    # time resolves then. @loader_path is $ORIGIN.
    target_link_options(${tgt} PRIVATE
      ${SS_OPT_${SS_CONFIG}}
      $<$<BOOL:${SS_GC_SECTIONS}>:-Wl,-dead_strip>
      -Wl,-undefined,dynamic_lookup
      $<$<BOOL:${SS_COVERAGE}>:--coverage>)
    if(A_RPATH_ORIGIN)
      set_target_properties(${tgt} PROPERTIES
        BUILD_WITH_INSTALL_RPATH ON INSTALL_RPATH "@loader_path")
    else()
      set_target_properties(${tgt} PROPERTIES SKIP_BUILD_RPATH ON)
    endif()
  else()
    set(wraps)
    if(NOT A_NO_WRAP)
      list(TRANSFORM SS_WRAPPED PREPEND "-Wl,--wrap=" OUTPUT_VARIABLE wraps)
    endif()
    target_link_options(${tgt} PRIVATE
      ${SS_OPT_${SS_CONFIG}}
      -Wl,--build-id -Wl,-z,noexecstack
      $<$<BOOL:${SS_GC_SECTIONS}>:-Wl,--gc-sections>
      -Wl,--no-undefined ${wraps} $<$<NOT:$<BOOL:${A_NO_RUNTIME}>>:-static-libgcc>
      -Wl,--version-script=${A_VERSION_SCRIPT}
      $<$<BOOL:${SS_COVERAGE}>:--coverage>)
    if(SS_IS_CLANG AND SS_LINKER STREQUAL "lld")
      # --undefined-version: the version script names __dynamic_cast and friends
      # in `local:` whether or not the library defines them. GNU ld ignores such
      # a name; lld 16+ refuses the link unless told the script is shared.
      # version_script_lld.txt: see its header -- the extern "C++" patterns
      # above miss names LLVM cannot demangle.
      target_link_options(${tgt} PRIVATE --ld-path=${SS_LD_PATH} -Wl,--undefined-version
        -Wl,--version-script=${PROJECT_SOURCE_DIR}/cmake/version_script_lld.txt)
      set_property(TARGET ${tgt} APPEND PROPERTY LINK_DEPENDS
        ${PROJECT_SOURCE_DIR}/cmake/version_script_lld.txt)
    endif()
    if(A_NO_RUNTIME)
      # Not under --coverage, whose counters need libgcov and libc: a
      # coverage build links the defaults. -static-libgcc is left off above:
      # with no libgcc there is no form of it to choose, and clang warns
      # that it is unused.
      target_compile_options(${tgt} PRIVATE -fno-stack-protector)
      target_link_options(${tgt} PRIVATE $<$<NOT:$<BOOL:${SS_COVERAGE}>>:-nostdlib>)
    else()
      # m, dl and pthread: libm's maths, and libdl's and libpthread's calls on the
      # Steam Runtime's glibc (2.31). No game DLL calls libuuid.
      target_link_libraries(${tgt} PRIVATE m dl pthread)
    endif()
    set_property(TARGET ${tgt} APPEND PROPERTY LINK_DEPENDS ${A_VERSION_SCRIPT})

    # RUNPATH $ORIGIN and nothing else: the DLLs find libmanaged_host.so,
    # libtier0.so and the rest beside themselves in bin/linux64.
    if(A_RPATH_ORIGIN)
      set_target_properties(${tgt} PROPERTIES
        BUILD_WITH_INSTALL_RPATH ON INSTALL_RPATH "\$ORIGIN")
    else()
      set_target_properties(${tgt} PROPERTIES SKIP_BUILD_RPATH ON)
    endif()
  endif()

  # Deploy into bin/<config>/bin/<platform> by copy-then-rename, never in place:
  # a running game has the old file mapped, and `make run`'s snapshot hard-links
  # it. A Linux release build splits the debug info into <name>.dbg on the way;
  # MSVC's .pdb is deployed beside the DLL the same way.
  if(SS_PLATFORM STREQUAL linux)
    set(split $<AND:$<STREQUAL:${SS_CONFIG},release>,$<BOOL:${SS_SPLIT_DEBUG}>>)
  else()
    set(split 0)
  endif()
  add_custom_command(TARGET ${tgt} POST_BUILD
    COMMAND ${CMAKE_COMMAND}
      -DIN=$<TARGET_FILE:${tgt}>
      -DDEST_DIR=${SS_DEPLOY_DIR}
      -DSPLIT=${split}
      -DOBJCOPY=${CMAKE_OBJCOPY}
      -P ${PROJECT_SOURCE_DIR}/cmake/deploy.cmake
    VERBATIM)
  if(SS_IS_MSVC)
    add_custom_command(TARGET ${tgt} POST_BUILD
      COMMAND ${CMAKE_COMMAND}
        -DIN=$<TARGET_PDB_FILE:${tgt}>
        -DDEST_DIR=${SS_DEPLOY_DIR}
        -DSPLIT=0
        -P ${PROJECT_SOURCE_DIR}/cmake/deploy.cmake
      VERBATIM)
  endif()
endfunction()

# ss_link_group(<target> <lib>...)
#
# VPC linked the Valve libraries inside -Wl,--start-group ... --end-group: they
# refer to each other in both directions, and GNU ld (the CI link) resolves an
# archive only against what came before it. On Linux that is CMake's
# LINK_GROUP:RESCAN; link.exe and Apple's ld search every archive until nothing
# changes, so there the list is linked as it is. A name that is not a target
# on this platform (a Valve prebuilt macOS has none of; cmake/sdk) is left
# out, and said so once at configure time.
function(ss_link_group tgt)
  set(libs)
  foreach(lib ${ARGN})
    if(TARGET ${lib} OR NOT lib MATCHES "^sdk::")
      list(APPEND libs ${lib})
    else()
      message(STATUS "${tgt}: no ${lib} on ${SS_PLATFORM}; left out of the link")
    endif()
  endforeach()
  if(SS_PLATFORM STREQUAL linux)
    list(JOIN libs "," joined)
    target_link_libraries(${tgt} PRIVATE "$<LINK_GROUP:RESCAN,${joined}>")
  else()
    target_link_libraries(${tgt} PRIVATE ${libs})
  endif()
endfunction()

# ss_link_host(<target>)
#
# The managed host, which both game libraries link, by target on every
# platform: DT_NEEDED records libmanaged_host.so's SONAME (it has one, its file
# name, for this), link.exe takes the import library, and Apple's ld the
# install name (@rpath/libmanaged_host.dylib). All of them sit in the same bin
# directory in the game, where the DLLs' RUNPATH $ORIGIN (or the launcher's
# PATH on Windows) finds them.
function(ss_link_host tgt)
  target_link_libraries(${tgt} PRIVATE sourcesharp)
endfunction()
