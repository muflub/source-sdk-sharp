# SourceSharp's native build: server.so, client.so, libmanaged_host.so and the
# native tests (src/test/native, SS_BUILD_TESTS).
#
#   cmake --preset debug && cmake --build --preset debug     (make debug does this)
#
# Sources come from the farm, farm/<config>/src (tools/setup), and the output
# goes to bin/<config>/bin/linux64. See cmake/SourceSharp.cmake for the flags
# and CMakePresets.json for the four configurations: debug and release build
# locally with clang; ci-debug and ci-release run the same project inside Valve's
# sniper SDK container (tools/build/ci-build).
#
# There is no launcher here: it is the C# NativeAOT source# (src/launcher).

cmake_minimum_required(VERSION 3.25)
project(SourceSharp LANGUAGES CXX)

set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

include(cmake/SourceSharp.cmake)
include(cmake/AllocCheck.cmake)

add_subdirectory(cmake/sdk)
add_subdirectory(src/host/native)
add_subdirectory(src/server/native)
add_subdirectory(src/client/native/particles_lib)
add_subdirectory(src/fwd/native)
add_subdirectory(src/client/native)

# The native gtests and must-not-compile checks (cmake/SourceSharpTests.cmake):
# ON in the local debug/release presets, OFF in ci-* (CI builds the game
# libraries; the tests run locally with the host clang). ON fetches the pinned
# googletest at configure time (FETCHCONTENT_SOURCE_DIR_GOOGLETEST for offline).
option(SS_BUILD_TESTS "build the native tests and register them with ctest" ON)
if(SS_BUILD_TESTS)
  enable_testing()
  add_subdirectory(src/test/native)
endif()
