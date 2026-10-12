# The Valve libraries.
#
# Prebuilt: what Valve ships in src/lib/public/linux64 and the fork tracks,
# linked into the farm per file (sdk-links/libs.txt and friends).
#
# Built from source (tier1.cmake, mathlib.cmake, raytrace.cmake): Valve's tree
# has no linux64 prebuilt of these three (vgui_controls and matsys_controls were
# built here too until the client stopped linking them, plan_client_novalve C3) -- upstream 22288b919 tracks none, and the .a files an
# old checkout holds are gitignored output of its own VPC build. tier1 is
# compiled from Valve's own interface.cpp again since Phase 6b, when the fork
# dropped the Interface_SetMissHook edit.

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

include(tier1.cmake)
include(mathlib.cmake)
include(raytrace.cmake)
