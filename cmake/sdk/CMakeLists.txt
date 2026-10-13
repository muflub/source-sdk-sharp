# The Valve libraries.
#
# Prebuilt: what Valve ships in src/lib/public/linux64 (and src/lib/public/x64
# for Windows) and the fork tracks, linked into the farm per file
# (sdk-links/libs.txt and friends). Valve ships NO macOS prebuilts (the SDK's
# README names Windows and Linux), so on macOS none of the sdk:: targets
# exist: ss_link_group leaves them out, the game libraries link with
# -undefined dynamic_lookup (cmake/SourceSharp.cmake), and the one target that
# needs a prebuilt archive at link time, libss_particles (particles.a and
# dmxloader.a, whose sources Valve does not publish), is not built there
# (src/client/native/particles_lib/CMakeLists.txt).
#
# Built from source (tier1.cmake, mathlib.cmake, raytrace.cmake): Valve's tree
# has no linux64 prebuilt of these three (vgui_controls and matsys_controls were
# built here too until the client stopped linking them, plan_client_novalve C3) -- upstream 22288b919 tracks none, and the .a files an
# old checkout holds are gitignored output of its own VPC build. tier1 is
# compiled from Valve's own interface.cpp again since Phase 6b, when the fork
# dropped the Interface_SetMissHook edit.

if(SS_PLATFORM STREQUAL linux)
  set(SS_SDK_LIBDIR "${SS_SRC}/lib/public/linux64")

  foreach(lib bitmap dmxloader particles tier2 tier3 vtf)
    add_library(sdk::${lib} STATIC IMPORTED GLOBAL)
    set_target_properties(sdk::${lib} PROPERTIES
      IMPORTED_LOCATION "${SS_SDK_LIBDIR}/${lib}.a")
  endforeach()

  # tier0 and vstdlib carry no SONAME, so they are linked by -l name (as VPC did)
  # and recorded in DT_NEEDED as libtier0.so / libvstdlib.so, not by path.
  foreach(lib tier0 vstdlib steam_api)
    add_library(sdk::${lib} SHARED IMPORTED GLOBAL)
    set_target_properties(sdk::${lib} PROPERTIES
      IMPORTED_LOCATION "${SS_SDK_LIBDIR}/lib${lib}.so")
  endforeach()
  set_target_properties(sdk::tier0 sdk::vstdlib PROPERTIES IMPORTED_NO_SONAME ON)
elseif(SS_PLATFORM STREQUAL windows)
  # Every Windows prebuilt is a .lib: the static archives, and the import
  # libraries of tier0.dll, vstdlib.dll and steam_api64.dll. link.exe takes
  # each by path, so all of them are UNKNOWN imports; nothing here records a
  # DLL name, the import library does.
  set(SS_SDK_LIBDIR "${SS_SRC}/lib/public/x64")

  foreach(lib bitmap dmxloader particles tier2 tier3 vtf tier0 vstdlib)
    add_library(sdk::${lib} UNKNOWN IMPORTED GLOBAL)
    set_target_properties(sdk::${lib} PROPERTIES
      IMPORTED_LOCATION "${SS_SDK_LIBDIR}/${lib}.lib")
  endforeach()
  add_library(sdk::steam_api UNKNOWN IMPORTED GLOBAL)
  set_target_properties(sdk::steam_api PROPERTIES
    IMPORTED_LOCATION "${SS_SDK_LIBDIR}/steam_api64.lib")
  # zlib, which the client links as the system's -lz on Linux and macOS: Valve
  # ships it for Windows.
  add_library(sdk::z UNKNOWN IMPORTED GLOBAL)
  set_target_properties(sdk::z PROPERTIES
    IMPORTED_LOCATION "${SS_SDK_LIBDIR}/libz.lib")
endif()

include(tier1.cmake)
include(mathlib.cmake)
include(raytrace.cmake)
