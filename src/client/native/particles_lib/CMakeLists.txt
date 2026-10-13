# libss_particles.so: Valve's particles.a and dmxloader.a in their own shared
# object (plan_particles_so.md; SPIKE, section 5).
#
# It links the two prebuilt archives, the tier1 and mathlib members they import
# and its own copy of the tier2 stand-ins, and exports only CreateInterface
# (version_script.txt), which hands out ISSParticleLib. Every copy inside is
# local, so the library's tier1 and client.so's tier1 ports never meet.
# client.so dlopens it from beside itself (particles_lib_client.cpp).

# Not on macOS: it links particles.a and dmxloader.a, prebuilt archives Valve
# ships for Linux and Windows only (cmake/sdk/CMakeLists.txt), and their
# sources are not in the SDK, so there is nothing to build it from there.
if(SS_PLATFORM STREQUAL osx)
  message(STATUS "libss_particles: not built on macOS (no particles.a / dmxloader.a to link)")
  return()
endif()

add_library(ss_particles SHARED)
set_target_properties(ss_particles PROPERTIES OUTPUT_NAME ss_particles NO_SONAME ON)
ss_sdk_target(ss_particles PROJECTDIR particles RPATH_ORIGIN
    VERSION_SCRIPT ${CMAKE_CURRENT_SOURCE_DIR}/version_script.txt)
set_property(TARGET ss_particles APPEND PROPERTY LINK_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/version_script.txt)

target_sources(ss_particles PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/particlelib.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/particles_tier2_stubs.cpp
    # CMaterialReference, which particles.a imports from tier2: client.so's port,
    # compiled again here against the library's own `materials`.
    ${PROJECT_SOURCE_DIR}/src/client/native/material_reference_client.cpp
    # Generate*IndexBuffer (tier2/meshutils.h), likewise client.so's port.
    ${PROJECT_SOURCE_DIR}/src/client/native/tier2_names_client.cpp
    ${PROJECT_SOURCE_DIR}/src/client/native/mesh_index_client.cpp
)

target_include_directories(ss_particles PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/include
    ${PROJECT_SOURCE_DIR}/src/client/native
    ${SS_SRC}/common
    ${SS_SRC}/public
    ${SS_SRC}/public/tier0
    ${SS_SRC}/public/tier1
)

target_compile_definitions(ss_particles PRIVATE
    DLLNAME=ss_particles
    NO_STRING_T
    VECTOR
    SOURCESHARP
    # As client.so: no tier0 allocator override in our own objects. particles.a
    # and dmxloader.a were compiled without it and call g_pMemAlloc themselves.
    NO_MALLOC_OVERRIDE
)
# The C allocator on Windows too, as client.dll (src/client/native).
if(SS_PLATFORM STREQUAL windows)
  target_compile_options(ss_particles PRIVATE
    "/FI${PROJECT_SOURCE_DIR}/cmake/compat/windows/ss_memalloc_windows.h")
endif()

if(SS_ALLOC_CHECK)
  target_sources(ss_particles PRIVATE ${PROJECT_SOURCE_DIR}/src/server/native/alloc_check.cpp)
  target_compile_definitions(ss_particles PRIVATE SOURCESHARP_ALLOC_CHECK
      "SOURCESHARP_ALLOC_CHECK_TAG=\"[SourceSharp/AllocCheck/particles]\"")
  target_link_libraries(ss_particles PRIVATE ss_alloc_check_wraps)
endif()

if(SS_PLATFORM STREQUAL linux)
  target_link_libraries(ss_particles PRIVATE rt dl)
endif()
ss_link_group(ss_particles sdk::particles sdk::dmxloader tier1 mathlib sdk::tier0 sdk::vstdlib)
