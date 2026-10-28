# Cross-builds the Windows column from Linux: clang-cl and lld-link over the
# MSVC CRT and Windows SDK that xwin lays out (xwin --accept-license splat
# --output ~/.xwin; XWIN_DIR overrides the place). A check that the Windows
# arms compile and link; MSVC on CI stays the shipped build.
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR AMD64)

if(DEFINED ENV{XWIN_DIR})
  set(XWIN_DIR "$ENV{XWIN_DIR}")
else()
  set(XWIN_DIR "$ENV{HOME}/.xwin")
endif()
if(NOT EXISTS "${XWIN_DIR}/crt/include/vcruntime.h")
  message(FATAL_ERROR "no MSVC CRT at ${XWIN_DIR}: run `xwin --accept-license --arch x86_64 splat --output ${XWIN_DIR}`")
endif()

find_program(CMAKE_C_COMPILER NAMES clang-cl-22 clang-cl REQUIRED)
find_program(CMAKE_CXX_COMPILER NAMES clang-cl-22 clang-cl REQUIRED)
find_program(CMAKE_LINKER NAMES lld-link-22 lld-link REQUIRED)
find_program(CMAKE_AR NAMES llvm-lib-22 llvm-lib REQUIRED)
find_program(CMAKE_RC_COMPILER NAMES llvm-rc-22 llvm-rc)
find_program(CMAKE_MT NAMES llvm-mt-22 llvm-mt)

set(ss_xwin_includes
    /imsvc "${XWIN_DIR}/crt/include"
    /imsvc "${XWIN_DIR}/sdk/include/ucrt"
    /imsvc "${XWIN_DIR}/sdk/include/um"
    /imsvc "${XWIN_DIR}/sdk/include/shared")
string(JOIN " " ss_xwin_flags --target=x86_64-pc-windows-msvc -fuse-ld=lld-link ${ss_xwin_includes})
set(CMAKE_C_FLAGS_INIT "${ss_xwin_flags}")
set(CMAKE_CXX_FLAGS_INIT "${ss_xwin_flags}")

string(JOIN " " ss_xwin_link
    "/libpath:${XWIN_DIR}/crt/lib/x86_64"
    "/libpath:${XWIN_DIR}/sdk/lib/um/x86_64"
    "/libpath:${XWIN_DIR}/sdk/lib/ucrt/x86_64")
set(CMAKE_EXE_LINKER_FLAGS_INIT "${ss_xwin_link}")
set(CMAKE_SHARED_LINKER_FLAGS_INIT "${ss_xwin_link}")
set(CMAKE_MODULE_LINKER_FLAGS_INIT "${ss_xwin_link}")

set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
