# The flags every native target shares, and how its output is deployed.
#
# Translated in Phase 6b from the fork's vpc_scripts/source_posix_ninja.vpc
# (the ninja `cxx` and `link_shared` rules VPC generated from it). The GNU
# column is that file's list unchanged: it is what CI's gcc, inside the
# sniper container, compiles with. The Clang column is the same intent spelled
# for clang 22, which is what a local build uses; every difference is named
# where it is made.

include_guard(GLOBAL)

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
option(SS_SPLIT_DEBUG "release: split debug info into <name>.dbg on deploy" ON)

# The linker, for a clang build: lld, found on PATH. Distributions install it
# versioned (ld.lld-22) with no unversioned ld.lld, hence --ld-path rather than
# -fuse-ld=lld. SS_LINKER=bfd is the recorded fallback: GNU ld, through clang's
# default.
set(SS_LINKER "lld" CACHE STRING "lld or bfd (clang builds only)")
set_property(CACHE SS_LINKER PROPERTY STRINGS lld bfd)
find_program(SS_LD_PATH NAMES ld.lld-22 ld.lld
             DOC "the lld binary clang links with when SS_LINKER=lld")

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
# bin/<config> IS the game directory; the shared native binaries go to bin/linux64.
set(SS_BIN "${PROJECT_SOURCE_DIR}/bin/${SS_CONFIG}")
# CI presets deploy elsewhere (bin/ci-<config>/linux64), so `make ci-build` never
# replaces the binaries of the local game directory.
set(SS_DEPLOY_DIR "${SS_BIN}/bin/linux64" CACHE PATH "where the built .so files are deployed")

if(NOT EXISTS "${SS_SRC}/public/tier0/platform.h")
  message(FATAL_ERROR "no farm at ${SS_FARM}: run `make setup ROOT=../source-sdk-2013`")
endif()

# CMake's per-build-type defaults (-O3 -DNDEBUG, -g) are replaced by the lists
# below, which are the VPC build's.
set(CMAKE_CXX_FLAGS_DEBUG "")
set(CMAKE_CXX_FLAGS_RELEASE "")
set(CMAKE_C_FLAGS_DEBUG "")
set(CMAKE_C_FLAGS_RELEASE "")

set(SS_IS_CLANG OFF)
if(CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
  set(SS_IS_CLANG ON)
elseif(NOT CMAKE_CXX_COMPILER_ID STREQUAL "GNU")
  message(FATAL_ERROR "unsupported compiler ${CMAKE_CXX_COMPILER_ID}: clang (local) or gcc (CI)")
endif()
if(SS_IS_CLANG AND SS_LINKER STREQUAL "lld" AND NOT SS_LD_PATH)
  message(FATAL_ERROR "no lld on PATH (ld.lld-22 or ld.lld): install lld-22, "
                      "or configure with -DSS_LINKER=bfd")
endif()

# --- compile ---------------------------------------------------------------------

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
endif()
set(SS_DEFS_debug DEBUG _DEBUG)
set(SS_DEFS_release NDEBUG)

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
# Not carried over: -Dsprintf=... -Dstrncpy=... -DPROTECTED_THINGS_ENABLE, which
# the VPC rule defined per project and then undid on the same line with
# -Usprintf -Ustrncpy -UPROTECTED_THINGS_ENABLE. The compiler never saw them.

# The GNU wrappers VPC's link rule installs (every file-system entry point goes
# through tier0's path-case fixups).
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
    LIBRARY_OUTPUT_DIRECTORY "${PROJECT_BINARY_DIR}/out")
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
  target_compile_definitions(${tgt} PRIVATE
    ${SS_COMMON_DEFS} ${SS_DEFS_${SS_CONFIG}}
    PROJECTDIR=${SS_SRC}/${A_PROJECTDIR}
    $<$<BOOL:${SS_COVERAGE}>:SS_COVERAGE=1>)
  # CMake defines <target>_EXPORTS in every shared library's TUs; VPC did not,
  # and nothing reads it.
  set_target_properties(${tgt} PROPERTIES DEFINE_SYMBOL "")
endfunction()

# ss_sdk_target(<target> PROJECTDIR <farm-relative dir> [RPATH_ORIGIN] [NO_WRAP])
#
# ss_sdk_compile plus the VPC build's shared-library link line and the deploy
# into the game directory. NO_WRAP leaves out tier0's file-system wrappers, for
# a target that links no tier0 (the host, and server.so since
# plan_server_novalve.md P4): with them, every opendir it calls is an import
# from tier0.
function(ss_sdk_target tgt)
  cmake_parse_arguments(A "RPATH_ORIGIN;NO_WRAP" "PROJECTDIR;VERSION_SCRIPT" "" ${ARGN})
  # VERSION_SCRIPT: the export list. Valve's devtools script (global: *) unless
  # the target names its own -- server.so's exports CreateInterface only.
  if(NOT A_VERSION_SCRIPT)
    set(A_VERSION_SCRIPT ${SS_SRC}/devtools/version_script.linux.txt)
  endif()
  ss_sdk_compile(${tgt} PROJECTDIR ${A_PROJECTDIR})

  set(wraps)
  if(NOT A_NO_WRAP)
    list(TRANSFORM SS_WRAPPED PREPEND "-Wl,--wrap=" OUTPUT_VARIABLE wraps)
  endif()
  target_link_options(${tgt} PRIVATE
    ${SS_OPT_${SS_CONFIG}}
    -Wl,--build-id -Wl,-z,noexecstack
    $<$<BOOL:${SS_GC_SECTIONS}>:-Wl,--gc-sections>
    -Wl,--no-undefined ${wraps} -static-libgcc
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
  target_link_libraries(${tgt} PRIVATE m dl pthread uuid)
  set_property(TARGET ${tgt} APPEND PROPERTY LINK_DEPENDS
    ${SS_SRC}/devtools/version_script.linux.txt)

  # RUNPATH $ORIGIN and nothing else: the DLLs find libmanaged_host.so,
  # libtier0.so and the rest beside themselves in bin/linux64.
  if(A_RPATH_ORIGIN)
    set_target_properties(${tgt} PROPERTIES
      BUILD_WITH_INSTALL_RPATH ON INSTALL_RPATH "\$ORIGIN")
  else()
    set_target_properties(${tgt} PROPERTIES SKIP_BUILD_RPATH ON)
  endif()

  # Deploy into bin/<config>/bin/linux64 by copy-then-rename, never in place: a
  # running game has the old file mapped, and `make run`'s snapshot hard-links
  # it. A release build splits the debug info into <name>.dbg on the way.
  add_custom_command(TARGET ${tgt} POST_BUILD
    COMMAND ${CMAKE_COMMAND}
      -DIN=$<TARGET_FILE:${tgt}>
      -DDEST_DIR=${SS_DEPLOY_DIR}
      -DSPLIT=$<AND:$<STREQUAL:${SS_CONFIG},release>,$<BOOL:${SS_SPLIT_DEBUG}>>
      -DOBJCOPY=${CMAKE_OBJCOPY}
      -P ${PROJECT_SOURCE_DIR}/cmake/deploy.cmake
    VERBATIM)
endfunction()
