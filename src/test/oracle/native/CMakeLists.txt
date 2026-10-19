# THE NATIVE ORACLES: a port checked against the Valve code it replaced, with
# that code compiled from the PRISTINE upstream clone at valve-sdk.lock -- not
# the fork and not the farm, which holds only what the game DLLs compile.
#
# Where the clone is: $SOURCESHARP_VALVE_SDK (the Makefile exports it), else
# the path `make setup` recorded in valve-sdk.lock.local. With no clone the
# oracles are registered as tests that FAIL, naming the fix: a missing
# reference is a failure, never a skip, and it must not stop the rest of the
# build from configuring.
#
# The subject (the upstream .cpp and its game/ headers) comes from the clone;
# the library headers (public/, public/tier0, public/tier1, common/) come from
# the farm, because the test links the farm's tier1/tier0/vstdlib and the
# headers must describe the libraries actually linked.
#
# These binaries carry the `oracle` label and the ss_oracle_tests target; the
# native leg excludes them and `make test`'s oracle leg runs them
# (tools/build/buildcpptests --oracle). tools/leg-floor oracle counts the
# ss_cpptest*() calls below.

set(SS_CPPTEST_GROUP oracle)

set(SS_VALVE_SDK "$ENV{SOURCESHARP_VALVE_SDK}")
if(NOT SS_VALVE_SDK AND EXISTS "${PROJECT_SOURCE_DIR}/valve-sdk.lock.local")
  file(STRINGS "${PROJECT_SOURCE_DIR}/valve-sdk.lock.local" SS_VALVE_SDK LIMIT_COUNT 1)
endif()
set(SS_VALVE_SRC "${SS_VALVE_SDK}/src")
# Every oracle test is told where the clone is (valve_sdk.h reads it).
list(APPEND SS_CPPTEST_ENV "SOURCESHARP_VALVE_SDK=${SS_VALVE_SDK}")

# ss_oracle_missing(<name>): the oracle cannot be built; register a test of that
# name that fails and says why.
function(ss_oracle_missing name why)
  add_test(NAME ${name}
    COMMAND sh -c "echo '!! ${name}: ${why}. Run make setup.'; exit 1")
  set_tests_properties(${name} PROPERTIES LABELS oracle)
  message(WARNING "oracle ${name}: ${why}; its test will FAIL. Run make setup.")
endfunction()

# THE WEAPON SCRIPT ORACLE, for the managed weapon script database --
# plan_entity.md §6's "17 that LEAVE" row for weapons_resource.cpp /
# weapon_parse.cpp / weapon_parse_default.cpp.
#
# It compiles upstream's game/shared/weapon_parse.cpp (FileWeaponInfo_t::Parse)
# and weapon_parse_default.cpp (the CreateWeaponInfo() it is reached through)
# and runs them over the mod's scripts, pinning the golden table the managed
# suite reads (sourcesharp/test/weapon_script_cases.txt) to the code it was
# taken from rather than agreeing with itself.
#
# tier1 because the subject reads its input through the REAL KeyValues, and a
# hand-written KV stand-in would be the very thing the golden guards against:
# GetInt and GetString CONVERT from a type KeyValues assigned at load time.
#
# weapon_parse_stub/ holds three one-TU header shims (cbase.h, util_shared.h,
# cdll_util.h); see those files. The clone's game/shared comes AFTER them, so
# ammodef.h, weapon_parse.h and shareddefs.h are upstream's.
#
# The -Wno- flags are for VALVE'S CODE, not this port's: this compiles a stock
# game/shared translation unit with -Wall. weapon_parse.cpp's
# `for ( int i=0; i < ARRAYSIZE( g_ItemFlags ); i++ )` is a signed/unsigned
# comparison Valve wrote; the subject is the code as it actually is, so it is
# suppressed here rather than edited there. -Wno-unused-variable for
# public/tier1/utlblockmemory.h (an unused local in a template);
# -Wno-inconsistent-missing-override -Wno-overloaded-virtual for
# public/filesystem_passthru.h, the same two the game build names in
# cmake/SourceSharp.cmake.
set(weapon_parse "${SS_VALVE_SRC}/game/shared/weapon_parse.cpp")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${weapon_parse}")
  ss_oracle_missing(weapon_script_oracle_test
    "no pristine Valve SDK (${SS_VALVE_SDK}): need ${weapon_parse}")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(weapon_script_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-sign-compare -Wno-unused-variable
            -Wno-inconsistent-missing-override -Wno-overloaded-virtual
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/weapon_parse_stub public public/tier0 public/tier1
             ${SS_VALVE_SRC}/game/shared common
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/weapon_script_oracle_test.cpp
            ${weapon_parse}
            ${SS_VALVE_SRC}/game/shared/weapon_parse_default.cpp)
endif()

# THE ORACLES THAT READ, rather than compile, upstream's text: each parses one
# upstream file out of the pristine clone (valve_sdk.h, $SOURCESHARP_VALVE_SDK)
# and compares what it declares with the port's value. They were checks in the
# native leg that read the file out of the farm, which no longer carries it
# because neither DLL compiles it.
#
# The native damage masks (shell_gamerules.cpp) against CMultiplayRules's
# Damage_Get* sets in game/shared/multiplay_gamerules.cpp.
ss_cpptest(gamerules_damage_oracle_test
    INCLUDES ${PROJECT_SOURCE_DIR}/src/server/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/gamerules_damage_oracle_test.cpp
            # P5 lane R: ss_shareddefs.h's restated constants (the GR_* codes
            # among them) against upstream's shareddefs.h / util_shared.h /
            # gamerules.h.
            ${CMAKE_CURRENT_SOURCE_DIR}/ss_shareddefs_oracle_test.cpp)

# The save codec's two time sentinels (shell_savegame.h) against upstream's
# ZERO_TIME (game/shared/saverestore.cpp) and INVALID_TIME (public/datamap.h).
ss_cpptest(save_field_codec_oracle_test
    INCLUDES ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/save_field_codec_oracle_test.cpp)

# The five client animation-event ids folded into precache_model.cpp against
# upstream's game/shared/ModelSoundsCache.cpp.
ss_cpptest(server_wave2_oracle_test
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/server_wave2_oracle_test.cpp)

# tier1's convar.cpp, pristine, run through the SAME facts as the port
# (src/test/native/convar_port_test.cpp against src/shared/native/convar_port.cpp).
# Stock passing them is what says the facts describe stock's behaviour, so the
# port passing them says the port behaves as stock does.
set(convar_stock "${SS_VALVE_SRC}/tier1/convar.cpp")
if(NOT EXISTS "${convar_stock}")
  ss_oracle_missing(convar_stock_oracle_test "no ${convar_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(convar_stock_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC GAME_DLL
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable -Wno-sign-compare
            -Wno-register -Wno-deprecated-register
            # ButtonCode.h:313's IsMouseCode, reached through cdll_int.h.
            -Wno-tautological-overlap-compare
    INCLUDES public public/tier0 public/tier1 common
    SOURCES ${PROJECT_SOURCE_DIR}/src/test/native/convar_port_test.cpp
            ${convar_stock})
endif()

# THE STRING HELPER ORACLE. src/shared/native/ss_string.h replaces tier1's
# strtools.cpp at our server call sites (so server.so stops extracting it);
# this links the pristine strtools.cpp and runs every helper beside the
# function it replaces, over edge cases and seeded random inputs. The object
# comes first on the link line, so its V_ definitions are the ones the test
# calls and libtier1.a's copy of the member is never extracted.
#
# -Wno- flags for VALVE'S CODE, not the test's: the MSVC pragmas in
# strtools.cpp and the tier1 headers, utlblockmemory.h's unused local,
# platform.h's dangling else in DECLARE_ALIGNED_BYTE_ARRAY and fmtstr.h's
# set-but-unused `result`.
set(strtools_stock "${SS_VALVE_SRC}/tier1/strtools.cpp")
set(strtools_unicode_stock "${SS_VALVE_SRC}/tier1/strtools_unicode.cpp")
if(NOT EXISTS "${strtools_stock}" OR NOT EXISTS "${strtools_unicode_stock}")
  ss_oracle_missing(ss_string_oracle_test "no ${strtools_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(ss_string_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-unused-variable -Wno-dangling-else
            -Wno-unused-but-set-variable
    INCLUDES public public/tier0 public/tier1 common ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/ss_string_oracle_test.cpp
            ${strtools_stock}
            ${strtools_unicode_stock})
endif()

# ss::EngineUtlVector<T> (src/shared/native/engine_utlvector.h), the layout view
# server.so reads engine-built vectors through, against the PRISTINE CUtlVector:
# the clone's public headers come first on the include path, after one stand-in
# (utlvector_stub/tier1/utlblockmemory.h, which says why).
set(utlvector_stock "${SS_VALVE_SRC}/public/tier1/utlvector.h")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${utlvector_stock}")
  ss_oracle_missing(engine_utlvector_layout_oracle_test "no ${utlvector_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(engine_utlvector_layout_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/utlvector_stub
             ${SS_VALVE_SRC}/public ${SS_VALVE_SRC}/public/tier0 ${SS_VALVE_SRC}/public/tier1
             ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/engine_utlvector_layout_oracle_test.cpp)
endif()

# The save block's token table (engineabi/saverestore.h) against upstream's
# CSaveRestoreSegment, each in its own TU (saverestore_symbol/), every token's
# slot compared: the engine shares the table, so placement is save format.
set(saverestore_stock "${SS_VALVE_SRC}/public/saverestoretypes.h")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${saverestore_stock}")
  ss_oracle_missing(saverestore_symbol_oracle_test "no ${saverestore_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
# -Wno-nontrivial-memcall is for VALVE'S CODE: CGameSaveRestoreInfo's constructor
# memsets a saverestorelevelinfo_t holding Vectors (ours zeroes it through void *).
ss_cpptest_tier1(saverestore_symbol_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable -Wno-nontrivial-memcall
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/utlvector_stub
             ${SS_VALVE_SRC}/public ${SS_VALVE_SRC}/public/tier0 ${SS_VALVE_SRC}/public/tier1
             ${PROJECT_SOURCE_DIR}/src/engineabi/native/include
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/saverestore_symbol_oracle_test.cpp
            ${CMAKE_CURRENT_SOURCE_DIR}/saverestore_symbol/upstream.cpp
            ${CMAKE_CURRENT_SOURCE_DIR}/saverestore_symbol/ours.cpp)
endif()

# engine_heap.h's lookup and EngineFilledUtlVector against the PRISTINE
# memalloc.h (slots) and THE GAME'S OWN libtier0.so (a real CUtlVector grown
# through it, read through the view, freed through the lookup). The game's copy
# is found in Steam's library folders here; the test fails, naming the fix,
# when none is. It links no tier0: it must first see a process without one.
set(SS_ENGINE_TIER0 "$ENV{SOURCESHARP_ENGINE_TIER0}")
if(NOT SS_ENGINE_TIER0)
  foreach(_vdf "$ENV{HOME}/.steam/steam/steamapps/libraryfolders.vdf"
               "$ENV{HOME}/.steam/debian-installation/steamapps/libraryfolders.vdf"
               "$ENV{HOME}/.local/share/Steam/steamapps/libraryfolders.vdf")
    if(SS_ENGINE_TIER0 OR NOT EXISTS "${_vdf}")
      continue()
    endif()
    file(STRINGS "${_vdf}" _paths REGEX "\"path\"")
    foreach(_line IN LISTS _paths)
      string(REGEX REPLACE ".*\"path\"[ \t]+\"([^\"]+)\".*" "\\1" _lib "${_line}")
      set(_cand "${_lib}/steamapps/common/Source SDK Base 2013 Multiplayer/bin/linux64/libtier0.so")
      if(NOT SS_ENGINE_TIER0 AND EXISTS "${_cand}")
        set(SS_ENGINE_TIER0 "${_cand}")
      endif()
    endforeach()
  endforeach()
endif()
set(memalloc_stock "${SS_VALVE_SRC}/public/tier0/memalloc.h")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${memalloc_stock}")
  ss_oracle_missing(engine_heap_oracle_test "no ${memalloc_stock}: is the pristine clone at valve-sdk.lock?")
else()
set(_ss_env_before ${SS_CPPTEST_ENV})
list(APPEND SS_CPPTEST_ENV "SOURCESHARP_ENGINE_TIER0=${SS_ENGINE_TIER0}")
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest(engine_heap_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/utlvector_stub
             ${SS_VALVE_SRC}/public ${SS_VALVE_SRC}/public/tier0 ${SS_VALVE_SRC}/public/tier1
             ${PROJECT_SOURCE_DIR}/src/shared/native ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/engine_heap_oracle_test.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/engine_heap.cpp
            ${PROJECT_SOURCE_DIR}/src/shared/native/loaded_symbol.cpp
    LIBS dl)
set(SS_CPPTEST_ENV ${_ss_env_before})
endif()

# vstdlib_random_port.cpp (server.so's own random stream, P4) against THE
# GAME'S OWN libvstdlib.so, draw for draw. It sits beside the libtier0.so found
# above and needs it on the library path (it has no RPATH). Links no vstdlib.
get_filename_component(_ss_engine_bin "${SS_ENGINE_TIER0}" DIRECTORY)
set(_ss_env_before ${SS_CPPTEST_ENV})
if(SS_ENGINE_TIER0)
  list(APPEND SS_CPPTEST_ENV "SOURCESHARP_ENGINE_VSTDLIB=${_ss_engine_bin}/libvstdlib.so"
                             "LD_LIBRARY_PATH=${_ss_engine_bin}")
endif()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest(vstdlib_random_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    # src/shared/native for ss_platform.h (SS_BIND_IMPORT; system headers only).
    INCLUDES ${SS_VALVE_SRC}/public ${SS_VALVE_SRC}/public/tier0 ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/vstdlib_random_oracle_test.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/vstdlib_random_port.cpp
    LIBS dl)
set(SS_CPPTEST_ENV ${_ss_env_before})

# ss_containers.h (src/shared/native), the standard-library stand-ins for the
# tier1 container semantics server.so's own code relied on, against the
# PRISTINE CUtlDict / CaselessStringLessThan / V_stricmp: the clone's
# public headers first, after the utlblockmemory.h stand-in (utlvector_stub/,
# which says why). Links the farm's tier1 for V_stricmp and tier0 for the
# dictionary's key copies.
set(utldict_stock "${SS_VALVE_SRC}/public/tier1/utldict.h")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${utldict_stock}")
  ss_oracle_missing(ss_containers_oracle_test "no ${utldict_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(ss_containers_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/utlvector_stub
             ${SS_VALVE_SRC}/public ${SS_VALVE_SRC}/public/tier0 ${SS_VALVE_SRC}/public/tier1
             ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/ss_containers_oracle_test.cpp)
endif()

# vcollide_t's layout, which SourceSharp.Abi.VCollideLayout reads in place to
# get a model's collision keyvalue text (break pieces), against the PRISTINE
# public/vcollide.h. VCollideLayoutTests pins the managed struct to the same
# numbers.
set(vcollide_stock "${SS_VALVE_SRC}/public/vcollide.h")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${vcollide_stock}")
  ss_oracle_missing(engine_vcollide_layout_oracle_test "no ${vcollide_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest(engine_vcollide_layout_oracle_test
    INCLUDES ${SS_VALVE_SRC}/public
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/engine_vcollide_layout_oracle_test.cpp)
endif()

# THE PRECIPITATION ORACLE (lane c2-sims): stock's client weather --
# CClient_Precipitation's rain, snow and ash and CSnowFallManager's snowfall --
# run offline from the PRISTINE game/client/c_effects.cpp over a scripted world
# (precipitation_stub/precip_harness.h says what is stock and what is
# stand-in), seeded, and held to the golden
# src/test/client/Fixtures/precipitation-stock.tsv, which the managed port's
# PrecipitationOracleTests replays. In the game every one of those emission
# paths is dead (they ask C_BasePlayer::GetLocalPlayer(), NULL in this mod), so
# a live recording could only record nothing.
#
# mathlib because the subject is vector math (AngleVectors, VectorNormalize,
# RandomVector) and tier1/vstdlib for its ConVars and the random streams.
# Upstream's public/collisionutils.cpp is compiled beside it for
# IsBoxIntersectingSphere and IntersectRayWithBox. The -Wno- flags are for
# VALVE'S CODE: c_effects.cpp's misleading indentation, its unused
# `vecCenter = ( a, b ) * 0.5` comma (under _DEBUG only, but parsed), the
# unused vOldPos-style locals and the MSVC pragmas.
set(c_effects_stock "${SS_VALVE_SRC}/game/client/c_effects.cpp")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${c_effects_stock}")
  ss_oracle_missing(precipitation_oracle_test "no ${c_effects_stock}: is the pristine clone at valve-sdk.lock?")
else()
# The subject is COPIED, byte for byte, into its own directory before it is
# included: its quoted #includes would otherwise find game/client's real
# headers beside it ahead of the stand-ins.
configure_file("${c_effects_stock}" "${CMAKE_CURRENT_BINARY_DIR}/precipitation_subject/c_effects.cpp" COPYONLY)
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_mathlib(precipitation_oracle_test
    DEFINES COMPILER_GCC CLIENT_DLL
    OPTIONS -Wno-unknown-pragmas -Wno-misleading-indentation -Wno-unused-variable -Wno-unused-value
            -Wno-unused-but-set-variable -Wno-dangling-else -Wno-sign-compare -Wno-comma
            -Wno-unused-function -Wno-unused-private-field -Wno-inconsistent-missing-override
            -Wno-overloaded-virtual -Wno-keyword-macro -Wno-c++11-narrowing
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/precipitation_stub
             ${CMAKE_CURRENT_BINARY_DIR}/precipitation_subject
             ${SS_VALVE_SRC}/game/client ${SS_VALVE_SRC}/game/shared
             sourcesharp/test
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/precipitation_oracle_test.cpp
            ${SS_VALVE_SRC}/public/collisionutils.cpp)
endif()

# THE VOICE BAN ORACLE (lane ui, C3 step 4, plan cluster 15): stock's
# CVoiceBanMgr from the PRISTINE game/shared/voice_banmgr.cpp, over an
# in-memory voice_ban.dt (voiceban_stub/), held to the golden
# src/test/client/Fixtures/voiceban-stock.tsv that the managed VoiceBanList
# replays: the file format stays compatible both ways. -Wno-dangling-else is for
# VALVE'S tier0/platform.h, which memdbgon.h brings in.
set(voice_banmgr_stock "${SS_VALVE_SRC}/game/shared/voice_banmgr.cpp")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${voice_banmgr_stock}")
  ss_oracle_missing(voiceban_oracle_test "no ${voice_banmgr_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest(voiceban_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/voiceban_stub public public/tier0 ${SS_VALVE_SRC}/game/shared
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/voiceban_oracle_test.cpp)
endif()

# The client's recv-table builders (engineabi/recv.h, plan_client_novalve C8)
# against upstream's public/dt_recv.cpp, linked into one binary: upstream
# supplies the Valve names, ours the engineabi::recv namespace, and every
# builder's prop and every proxy's output is compared. -Wno- flags are for
# VALVE'S CODE: platform.h's pragmas and dangling else; utlvector_stub/ stands
# in for utlblockmemory.h, which upstream's headers include and clang refuses.
set(dt_recv_stock "${SS_VALVE_SRC}/public/dt_recv.cpp")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${dt_recv_stock}")
  ss_oracle_missing(recv_builders_oracle_test "no ${dt_recv_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(recv_builders_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC CLIENT_DLL
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/utlvector_stub
             ${SS_VALVE_SRC}/public ${SS_VALVE_SRC}/public/tier0 ${SS_VALVE_SRC}/public/tier1
             ${PROJECT_SOURCE_DIR}/src/engineabi/native/include
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/recv_builders_oracle_test.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/engineabi_recv.cpp
            ${dt_recv_stock})
endif()

# public/tier2/meshutils.h's five generators, ours (src/client/native/
# mesh_index_client.cpp, in ss::meshutils) against tier2.a's own meshutils.o
# (plan_client_novalve C9). The SDK ships no source for them: the archive
# object is the reference.
set(tier2_stock "${SS_VALVE_SRC}/lib/public/linux64/tier2.a")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${tier2_stock}")
  ss_oracle_missing(meshutils_oracle_test "no ${tier2_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(meshutils_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else
    INCLUDES ${SS_VALVE_SRC}/public ${SS_VALVE_SRC}/public/tier0
             ${PROJECT_SOURCE_DIR}/src/client/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/meshutils_oracle_test.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/mesh_index_client.cpp
    LIBS ${tier2_stock})
endif()

# CMaterialReference and CTextureReference, ours (src/client/native/
# material_reference_client.cpp) against upstream's public/materialsystem/
# MaterialSystemUtil.cpp, compiled beside them with its classes renamed
# (materialreference_stock.cpp), over a recording material-system double
# (plan_client_novalve C9).
# -Wno-reorder-ctor and -Wno-unused-private-field are for VALVE'S HEADERS
# (bitmap/imageformat.h's constructor, the material headers' unused fields).
set(msu_stock "${SS_VALVE_SRC}/public/materialsystem/MaterialSystemUtil.cpp")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${msu_stock}")
  ss_oracle_missing(materialreference_oracle_test "no ${msu_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(materialreference_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
            "SS_STOCK_MATERIALSYSTEMUTIL_CPP=\"${msu_stock}\""
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-private-field -Wno-reorder-ctor
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/utlvector_stub
             ${SS_VALVE_SRC}/public ${SS_VALVE_SRC}/public/tier0 ${SS_VALVE_SRC}/public/tier1
             ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/materialreference_oracle_test.cpp
            ${CMAKE_CURRENT_SOURCE_DIR}/materialreference_stock.cpp
            ${PROJECT_SOURCE_DIR}/src/client/native/material_reference_client.cpp)
endif()

# tier2's CBeamSegDraw, from tier2.a's own beamsegdraw.o (the SDK ships no
# source for it), over a recording render context and mesh: stock is held to
# the golden src/test/client/Fixtures/beamsegdraw-stock.tsv that the managed
# BeamSegDraw replays, and src/client/native/beam_strip_vertex.h, the
# executors' writer, to the buffers stock wrote (plan_client_novalve C9).
# -Wno-reorder-ctor, -Wno-unused-private-field and -Wno-overloaded-virtual are
# for VALVE'S HEADERS (the two above; materialsystem/imesh.h's IMesh hides
# its bases' ValidateData, ModifyBegin and ModifyEnd).
if(NOT SS_VALVE_SDK OR NOT EXISTS "${tier2_stock}")
  ss_oracle_missing(beamsegdraw_oracle_test "no ${tier2_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(beamsegdraw_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-private-field -Wno-reorder-ctor
            -Wno-overloaded-virtual
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/utlvector_stub
             ${SS_VALVE_SRC}/public ${SS_VALVE_SRC}/public/tier0 ${SS_VALVE_SRC}/public/tier1
             ${PROJECT_SOURCE_DIR}/src/shared/native ${PROJECT_SOURCE_DIR}/src/client/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/beamsegdraw_oracle_test.cpp
    LIBS ${tier2_stock})
endif()

# Pristine tier1 bitbuf.cpp over src/test/native/bitbuf_scenario.h, held to
# the golden src/test/client/Fixtures/bitbuf-stock.tsv that bitbuf_client_test
# replays against client.so's bitbuf members (plan_client_novalve C9). The
# -Wno- flags are for VALVE'S CODE, as ss_string_oracle_test's, plus
# bitvec.h's memset of a non-trivial class (-Wno-nontrivial-memcall).
set(bitbuf_stock "${SS_VALVE_SRC}/tier1/bitbuf.cpp")
if(NOT EXISTS "${bitbuf_stock}")
  ss_oracle_missing(bitbuf_stock_oracle_test "no ${bitbuf_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(bitbuf_stock_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-unused-variable -Wno-dangling-else
            -Wno-unused-but-set-variable -Wno-nontrivial-memcall
    INCLUDES public public/tier0 public/tier1 common ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/bitbuf_stock_oracle_test.cpp
            ${bitbuf_stock})
endif()

# Pristine game/shared/usercmd.cpp's WriteUsercmd (with pristine bitbuf.cpp)
# over seeded command pairs, held to the golden
# src/test/client/Fixtures/usercmd-encode-stock.tsv that the managed encoder
# (UserCmdCodec) replays (plan_client_novalve C9). usercmd_stub/ stands in
# for the game's cbase.h and imovehelper.h; the subject and its header are
# COPIED, byte for byte, into their own directory first, so their quoted
# includes find the stand-ins rather than game/shared's. The -Wno- flags are
# for VALVE'S CODE.
set(usercmd_stock "${SS_VALVE_SRC}/game/shared/usercmd.cpp")
if(NOT EXISTS "${usercmd_stock}" OR NOT EXISTS "${bitbuf_stock}")
  ss_oracle_missing(usercmd_encode_oracle_test "no ${usercmd_stock}: is the pristine clone at valve-sdk.lock?")
else()
configure_file("${usercmd_stock}" "${CMAKE_CURRENT_BINARY_DIR}/usercmd_subject/usercmd.cpp" COPYONLY)
configure_file("${SS_VALVE_SRC}/game/shared/usercmd.h" "${CMAKE_CURRENT_BINARY_DIR}/usercmd_subject/usercmd.h" COPYONLY)
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(usercmd_encode_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-unused-variable -Wno-dangling-else
            -Wno-unused-but-set-variable -Wno-nontrivial-memcall
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/usercmd_stub
             ${CMAKE_CURRENT_BINARY_DIR}/usercmd_subject
             public public/tier0 public/tier1 common
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/usercmd_encode_oracle_test.cpp
            ${CMAKE_CURRENT_BINARY_DIR}/usercmd_subject/usercmd.cpp
            ${bitbuf_stock})
endif()

# THE SEQUENCE TRANSITIONER ORACLE: pristine game/shared/sequence_Transitioner.cpp
# (CSequenceTransitioner) on the client's pristine C_AnimationLayer
# (game/client/animationlayer.h) and pristine public/studio.cpp's CStudioHdr,
# over a scripted run of sequence changes, held to the golden
# src/test/oracle/managed/Fixtures/sequence-transitioner-stock.tsv that the
# managed SequenceTransitioner replays (SequenceTransitionerOracleTests, the
# oracle-managed leg). seqtransition_stub/ stands in for the game's cbase.h;
# the subject and its header are COPIED, byte for byte, into their own
# directory first, so the quoted "cbase.h" finds the stand-in. mathlib because
# studio.cpp is linked whole. The -Wno- flags are for VALVE'S CODE.
set(seqtrans_stock "${SS_VALVE_SRC}/game/shared/sequence_Transitioner.cpp")
set(seqtrans_studio "${SS_VALVE_SRC}/public/studio.cpp")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${seqtrans_stock}" OR NOT EXISTS "${seqtrans_studio}")
  ss_oracle_missing(sequence_transitioner_oracle_test "no ${seqtrans_stock}: is the pristine clone at valve-sdk.lock?")
else()
configure_file("${seqtrans_stock}" "${CMAKE_CURRENT_BINARY_DIR}/seqtrans_subject/sequence_Transitioner.cpp" COPYONLY)
configure_file("${SS_VALVE_SRC}/game/shared/sequence_Transitioner.h" "${CMAKE_CURRENT_BINARY_DIR}/seqtrans_subject/sequence_Transitioner.h" COPYONLY)
configure_file("${SS_VALVE_SRC}/game/client/animationlayer.h" "${CMAKE_CURRENT_BINARY_DIR}/seqtrans_subject/animationlayer.h" COPYONLY)
configure_file("${SS_VALVE_SRC}/game/client/lerp_functions.h" "${CMAKE_CURRENT_BINARY_DIR}/seqtrans_subject/lerp_functions.h" COPYONLY)
# studio.cpp beside its own pristine studio.h, which the harness then reads too.
configure_file("${seqtrans_studio}" "${CMAKE_CURRENT_BINARY_DIR}/seqtrans_subject/studio.cpp" COPYONLY)
configure_file("${SS_VALVE_SRC}/public/studio.h" "${CMAKE_CURRENT_BINARY_DIR}/seqtrans_subject/studio.h" COPYONLY)
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_mathlib(sequence_transitioner_oracle_test
    DEFINES COMPILER_GCC CLIENT_DLL
    OPTIONS -Wno-unknown-pragmas -Wno-unused-variable -Wno-dangling-else
            -Wno-unused-but-set-variable -Wno-nontrivial-memcall -Wno-reorder-ctor
            -Wno-writable-strings -Wno-undefined-bool-conversion
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/seqtransition_stub
             ${CMAKE_CURRENT_BINARY_DIR}/seqtrans_subject
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/sequence_transitioner_oracle_test.cpp
            ${CMAKE_CURRENT_BINARY_DIR}/seqtrans_subject/sequence_Transitioner.cpp
            ${CMAKE_CURRENT_BINARY_DIR}/seqtrans_subject/studio.cpp)
endif()

# Pristine tier1/KeyValues.cpp's LoadFromBuffer over the corpus
# src/test/client/Fixtures/keyvalues-load-cases.tsv, held to the golden
# keyvalues-load-stock.tsv that the managed loader (KeyValuesLoad) replays
# (plan_client_novalve C9, KeyValues K4). The pristine file's definitions are
# the ones linked: libtier1.a's KeyValues.o defines nothing this one does not,
# so the archive member is never extracted. The -Wno- flags are for VALVE'S CODE
# (the out-of-range one is the int-against-LONG_MAX overflow test, always false
# on LP64, which is why a long overflow loads as a truncated int).
set(keyvalues_stock "${SS_VALVE_SRC}/tier1/KeyValues.cpp")
if(NOT EXISTS "${keyvalues_stock}")
  ss_oracle_missing(keyvalues_load_oracle_test "no ${keyvalues_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(keyvalues_load_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-unused-variable -Wno-dangling-else
            -Wno-unused-but-set-variable -Wno-nontrivial-memcall
            -Wno-inconsistent-missing-override -Wno-overloaded-virtual -Wno-reorder-ctor
            -Wno-tautological-constant-out-of-range-compare -Wno-undefined-bool-conversion
    INCLUDES public public/tier0 public/tier1 common
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/keyvalues_load_oracle_test.cpp
            ${keyvalues_stock})
endif()

# Pristine tier1/KeyValues.cpp over src/test/native/keyvalues_scenario.h,
# recorded as the golden src/test/client/Fixtures/keyvalues-members-stock.tsv
# that keyvalues_client_test replays against client.so's own KeyValues
# members (plan_client_novalve C9 K5). The -Wno- flags are for VALVE'S CODE,
# as keyvalues_load_oracle_test's.
if(NOT EXISTS "${keyvalues_stock}")
  ss_oracle_missing(keyvalues_members_oracle_test "no ${keyvalues_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(keyvalues_members_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-unused-variable -Wno-dangling-else
            -Wno-unused-but-set-variable -Wno-nontrivial-memcall
            -Wno-inconsistent-missing-override -Wno-overloaded-virtual -Wno-reorder-ctor
            -Wno-tautological-constant-out-of-range-compare -Wno-undefined-bool-conversion
    INCLUDES public public/tier0 public/tier1 common ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/keyvalues_members_oracle_test.cpp
            ${keyvalues_stock})
endif()

# ss_crc32.h (src/shared/native) against pristine tier1/checksum_crc.cpp,
# compiled into the test so its CRC32_* are the ones called and libtier1.a's
# member is never extracted (plan_client_novalve C9, the small tier1 pieces).
set(checksum_crc_stock "${SS_VALVE_SRC}/tier1/checksum_crc.cpp")
if(NOT EXISTS "${checksum_crc_stock}")
  ss_oracle_missing(ss_crc32_oracle_test "no ${checksum_crc_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(ss_crc32_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-unused-variable -Wno-dangling-else -Wno-implicit-fallthrough
    INCLUDES public public/tier0 public/tier1 common ${PROJECT_SOURCE_DIR}/src/shared/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/ss_crc32_oracle_test.cpp
            ${checksum_crc_stock})
endif()

# Pristine tier1/mempool.cpp over src/test/native/mempool_scenario.h, recorded
# as the golden src/test/client/Fixtures/mempool-stock.tsv that
# mempool_client_test replays against client.so's CUtlMemoryPool members
# (tier1_client.cpp, plan_client_novalve C9).
set(mempool_stock "${SS_VALVE_SRC}/tier1/mempool.cpp")
if(NOT EXISTS "${mempool_stock}")
  ss_oracle_missing(mempool_stock_oracle_test "no ${mempool_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(mempool_stock_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-unused-variable -Wno-dangling-else -Wno-sign-compare
    INCLUDES public public/tier0 public/tier1 common ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/mempool_stock_oracle_test.cpp
            ${mempool_stock})
endif()

# Pristine bitmap.a (TGAWriter) and vtf.a (CVTFTexture), the prebuilt archives
# the SDK ships (no source), over seeded pixel buffers, recorded as the golden
# src/test/client/Fixtures/image-encode-stock.tsv that the managed screenshot
# encoders replay byte for byte (plan_client_novalve C9: bitmap.a and vtf.a
# off client.so).
set(bitmap_stock "${SS_VALVE_SRC}/lib/public/linux64/bitmap.a")
set(vtf_stock "${SS_VALVE_SRC}/lib/public/linux64/vtf.a")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${bitmap_stock}" OR NOT EXISTS "${vtf_stock}")
  ss_oracle_missing(image_encode_oracle_test "no ${bitmap_stock} or ${vtf_stock}: is the pristine clone at valve-sdk.lock?")
else()
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_mathlib(image_encode_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable -Wno-reorder-ctor
    INCLUDES public public/tier0 public/tier1
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/image_encode_oracle_test.cpp
    LIBS ${vtf_stock} ${bitmap_stock})
endif()

# THE LIP SYNC ORACLE: stock's viseme pass, the arm public/sentence.cpp lost
# when C_BaseFlex left the client (docs/client-novalve-rulings.tsv: sentence.cpp
# is ruled `port`). Pristine sentence.cpp is linked whole; c_baseflex.cpp's
# viseme functions are CUT from the pristine file between the markers below
# (verbatim, into lipsync_subject/) and compiled inside the harness's stand-in
# C_BaseFlex, since the whole file needs the whole client. A marker that is
# not found fails the test rather than the configure. The golden
# src/test/client/Fixtures/lipsync-stock.tsv is replayed by the managed
# LipSyncOracleTests. mathlib for Catmull_Rom_Spline (GetIntensity), tier1
# for CUtlBuffer and the ConVars. The -Wno- flags are for VALVE'S CODE.
set(sentence_stock "${SS_VALVE_SRC}/public/sentence.cpp")
set(baseflex_stock "${SS_VALVE_SRC}/game/client/c_baseflex.cpp")
set(lipsync_cut_ok FALSE)
if(SS_VALVE_SDK AND EXISTS "${sentence_stock}" AND EXISTS "${baseflex_stock}")
  set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${baseflex_stock}")
  file(READ "${baseflex_stock}" _bf)
  set(lipsync_cut "")
  set(lipsync_cut_ok TRUE)
  # Each pair: the first text of a region, and the text that ends it (not kept).
  foreach(_pair
      "ConVar g_CV_PhonemeDelay(|ConVar g_CV_FlexRules("
      "void C_BaseFlex::SetupMappings( char const *pchFileRoot )|// Purpose: initialize fast lookups when model changes"
      "#define STRONG_CROSSFADE_START|// Purpose: fill keyvalues message with flex state"
      "int C_BaseFlex::g_numflexcontrollers;|char const *C_BaseFlex::GetGlobalFlexControllerName")
    string(FIND "${_pair}" "|" _bar)
    string(SUBSTRING "${_pair}" 0 ${_bar} _from)
    math(EXPR _bar "${_bar} + 1")
    string(SUBSTRING "${_pair}" ${_bar} -1 _to)
    string(FIND "${_bf}" "${_from}" _a)
    if(_a EQUAL -1)
      set(lipsync_cut_ok FALSE)
      set(lipsync_missing "${_from}")
      break()
    endif()
    string(SUBSTRING "${_bf}" ${_a} -1 _tail)
    string(FIND "${_tail}" "${_to}" _b)
    if(_b EQUAL -1)
      set(lipsync_cut_ok FALSE)
      set(lipsync_missing "${_to}")
      break()
    endif()
    string(SUBSTRING "${_tail}" 0 ${_b} _region)
    string(APPEND lipsync_cut "// ---- pristine c_baseflex.cpp from `${_from}`\n${_region}\n")
  endforeach()
endif()
if(NOT SS_VALVE_SDK OR NOT EXISTS "${sentence_stock}" OR NOT EXISTS "${baseflex_stock}")
  ss_oracle_missing(lipsync_oracle_test "no ${sentence_stock} or ${baseflex_stock}: is the pristine clone at valve-sdk.lock?")
elseif(NOT lipsync_cut_ok)
  ss_oracle_missing(lipsync_oracle_test "pristine c_baseflex.cpp no longer holds the marker `${lipsync_missing}`")
else()
file(WRITE "${CMAKE_CURRENT_BINARY_DIR}/lipsync_subject/c_baseflex_visemes.inl" "${lipsync_cut}")
# The harness reads the subject's own pristine sentence.h (the farm no longer
# links it: sentence.cpp has left client.so).
configure_file("${SS_VALVE_SRC}/public/sentence.h" "${CMAKE_CURRENT_BINARY_DIR}/lipsync_subject/sentence.h" COPYONLY)
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_mathlib(lipsync_oracle_test
    DEFINES NDEBUG POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC CLIENT_DLL
    OPTIONS -Wno-unknown-pragmas -Wno-dangling-else -Wno-unused-variable -Wno-sign-compare
            -Wno-unused-but-set-variable -Wno-nontrivial-memcall -Wno-unused-function
    INCLUDES public public/tier0 public/tier1 common ${PROJECT_SOURCE_DIR}/src/test/native
             ${CMAKE_CURRENT_BINARY_DIR}/lipsync_subject
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/lipsync_oracle_test.cpp
            ${sentence_stock})
endif()

# THE DETAIL-PROP ORACLE (C7 cluster 10): stock's client detail props -- the
# detail lump's parse, the per-view fades, the back-to-front sprite sorts and
# every sprite quad -- run offline from the PRISTINE
# game/client/detailobjectsystem.cpp over seeded lumps and a scripted world
# (detailprops_stub/detail_harness.h says what is stock and what is
# stand-in), held to the golden src/test/client/Fixtures/detailprops-stock.tsv,
# which the managed port's DetailPropsOracleTests replays. mathlib for the
# angle and SIMD math, tier1 for CUtlBuffer, CUtlMap and the ConVars. The
# -Wno- flags are for VALVE'S CODE: its unused locals (vecSway, texul in
# GetSpriteMiddleBottomPosition), the MSVC pragmas, the hidden GetAttachment
# overload and the null references its handle stubs return.
set(detailprops_stock "${SS_VALVE_SRC}/game/client/detailobjectsystem.cpp")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${detailprops_stock}")
  ss_oracle_missing(detailprops_oracle_test "no ${detailprops_stock}: is the pristine clone at valve-sdk.lock?")
else()
# The subject is COPIED, byte for byte, into its own directory before it is
# included: its quoted #includes would otherwise find game/client's real
# headers beside it ahead of the stand-ins.
configure_file("${detailprops_stock}" "${CMAKE_CURRENT_BINARY_DIR}/detailprops_subject/detailobjectsystem.cpp" COPYONLY)
# The subject's own pristine gamebspfile.h, tier1/heapsort.h and
# tier0/valve_minmax_off.h (the farm no longer links them: their one includer
# on client.so was the subject).
configure_file("${SS_VALVE_SRC}/public/gamebspfile.h" "${CMAKE_CURRENT_BINARY_DIR}/detailprops_subject/gamebspfile.h" COPYONLY)
configure_file("${SS_VALVE_SRC}/public/tier1/heapsort.h" "${CMAKE_CURRENT_BINARY_DIR}/detailprops_subject/tier1/heapsort.h" COPYONLY)
configure_file("${SS_VALVE_SRC}/public/tier0/valve_minmax_off.h" "${CMAKE_CURRENT_BINARY_DIR}/detailprops_subject/tier0/valve_minmax_off.h" COPYONLY)
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_mathlib(detailprops_oracle_test
    DEFINES COMPILER_GCC CLIENT_DLL
    OPTIONS -Wno-unknown-pragmas -Wno-unused-variable -Wno-unused-but-set-variable -Wno-sign-compare
            -Wno-overloaded-virtual -Wno-unused-function -Wno-unused-private-field
            -Wno-inconsistent-missing-override -Wno-nontrivial-memcall -Wno-null-dereference
            -Wno-reorder-ctor
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/detailprops_stub
             ${CMAKE_CURRENT_BINARY_DIR}/detailprops_subject
             public public/tier0 public/tier1 sourcesharp/test ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/detailprops_oracle_test.cpp)
endif()

# THE FLASHLIGHT ORACLE (C7 cluster 12): stock's client flashlight --
# CFlashlightEffect's projected texture (the swing, the hull traces and their
# filter, the pull-back, the basis, the state the shadow manager is handed),
# the old dynamic-light flashlight and the r_newflashlight callback -- run
# offline from the PRISTINE game/client/flashlighteffect.cpp over a scripted
# world (flashlight_stub/flashlight_harness.h says what is stock and what is
# stand-in), held to the golden src/test/client/Fixtures/flashlight-stock.tsv,
# which the managed port's FlashlightOracleTests replays. mathlib for the
# basis and VectorNormalize, tier1 for the ConVars and their callback. The
# -Wno- flags are for VALVE'S CODE: the MSVC pragmas, its unused statics
# under the tool framework, and the member it leaves uninitialised.
set(flashlight_stock "${SS_VALVE_SRC}/game/client/flashlighteffect.cpp")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${flashlight_stock}")
  ss_oracle_missing(flashlight_oracle_test "no ${flashlight_stock}: is the pristine clone at valve-sdk.lock?")
else()
# The subject and its own header are COPIED, byte for byte, into their own
# directory before they are included: their quoted #includes would otherwise
# find game/client's real headers beside them ahead of the stand-ins.
configure_file("${flashlight_stock}" "${CMAKE_CURRENT_BINARY_DIR}/flashlight_subject/flashlighteffect.cpp" COPYONLY)
configure_file("${SS_VALVE_SRC}/game/client/flashlighteffect.h" "${CMAKE_CURRENT_BINARY_DIR}/flashlight_subject/flashlighteffect.h" COPYONLY)
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_mathlib(flashlight_oracle_test
    DEFINES COMPILER_GCC CLIENT_DLL
    OPTIONS -Wno-unknown-pragmas -Wno-unused-variable -Wno-unused-but-set-variable -Wno-unused-function
            -Wno-unused-private-field
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/flashlight_stub
             ${CMAKE_CURRENT_BINARY_DIR}/flashlight_subject
             public public/tier0 public/tier1 sourcesharp/test ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/flashlight_oracle_test.cpp)
endif()

# THE LEAF SYSTEM ORACLE (C7 cluster 12, docs/plans/c7_leaf_system.md L0):
# stock's client leaf system -- the registry, BuildRenderablesList's arms,
# ComputeTranslucentRenderLeaf and SortEntities, CollateViewModelRenderables'
# split and the shadows projected into leaves -- run offline from the
# PRISTINE game/client/clientleafsystem.cpp over a scripted world
# (leafsystem_stub/leafsystem_harness.h says what is stock and what is
# stand-in), held to the golden src/test/client/Fixtures/leafsystem-stock.tsv,
# which the managed LeafRegistry's facts replay. It also compiles its own
# reading of the registry (leafsystem_stub/leafsystem_gather.h) over the
# pristine class, and holds that
# header's layout mirror to it. mathlib for the bounds, tier0 for CTSList and
# the thread check, tier1 for the containers and the ConVars; the pristine
# tier1/mempool.cpp for the list's fixed-size allocator, which client.so
# carries as its own port (tier1_client.cpp) and the tier1 library does not;
# libatomic for CTSList's 16-byte compare-exchange. _DEBUG compiles the
# subject's cl_drawleaf filter, which the debug client has
# (NO_MALLOC_OVERRIDE, as the client builds). The -Wno- flags are for VALVE'S
# CODE: the MSVC pragmas, its threaded arms' unused helpers, mempool.cpp's
# dangling else and sign compares, and the pristine iclientrenderable.h's
# GetRefEHandle, which binds a null reference.
set(leafsystem_stock "${SS_VALVE_SRC}/game/client/clientleafsystem.cpp")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${leafsystem_stock}")
  ss_oracle_missing(leafsystem_oracle_test "no ${leafsystem_stock}: is the pristine clone at valve-sdk.lock?")
else()
# The subject, its header and the game-system header it derives from are
# COPIED, byte for byte, into their own directory before they are included:
# their quoted #includes would otherwise find game/client's real headers
# beside them ahead of the stand-ins.
configure_file("${leafsystem_stock}" "${CMAKE_CURRENT_BINARY_DIR}/leafsystem_subject/clientleafsystem.cpp" COPYONLY)
configure_file("${SS_VALVE_SRC}/game/client/clientleafsystem.h" "${CMAKE_CURRENT_BINARY_DIR}/leafsystem_subject/clientleafsystem.h" COPYONLY)
configure_file("${SS_VALVE_SRC}/game/shared/igamesystem.h" "${CMAKE_CURRENT_BINARY_DIR}/leafsystem_subject/igamesystem.h" COPYONLY)
# And the renderable interfaces, pristine: the farm's answer GetBaseEntity()
# with SourceSharp's IGameEntity, and the subject asks it for a C_BaseEntity.
configure_file("${SS_VALVE_SRC}/public/iclientunknown.h" "${CMAKE_CURRENT_BINARY_DIR}/leafsystem_subject/iclientunknown.h" COPYONLY)
configure_file("${SS_VALVE_SRC}/public/iclientrenderable.h" "${CMAKE_CURRENT_BINARY_DIR}/leafsystem_subject/iclientrenderable.h" COPYONLY)
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_mathlib(leafsystem_oracle_test
    DEFINES COMPILER_GCC CLIENT_DLL _DEBUG NO_MALLOC_OVERRIDE
    OPTIONS -Wno-unknown-pragmas -Wno-unused-variable -Wno-unused-but-set-variable -Wno-unused-function
            -Wno-unused-private-field -Wno-null-dereference -Wno-dangling-else -Wno-sign-compare
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/leafsystem_stub
             ${CMAKE_CURRENT_BINARY_DIR}/leafsystem_subject
             public public/tier0 public/tier1 sourcesharp/test ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/leafsystem_oracle_test.cpp
            ${SS_VALVE_SRC}/tier1/mempool.cpp
    LIBS atomic)
endif()

# THE CLIENT SOUND EMITTER ORACLE (the client port of sound_emitter.cpp's emit
# path): stock's CSoundEmitterSystem as the client compiles it -- the emit,
# stop, ambient and close-caption paths, GetParametersForSound,
# PrecacheScriptSound and the system's stages -- run offline from the PRISTINE
# game/shared/SoundEmitterSystem.cpp over a scripted world
# (soundemitter_stub/soundemitter_harness.h says what is stock and what is
# stand-in), held to the golden
# src/test/client/Fixtures/soundemitter-client-stock.tsv, which the managed
# ClientSoundEmitter's ClientSoundEmitterOracleTests replay. tier1 for the
# ConVars, the symbol table and the string functions. The -Wno- flags are for
# VALVE'S CODE: the MSVC pragmas, platform.h's dangling else, and the
# subject's server-only statics and unused locals under CLIENT_DLL.
set(soundemitter_stock "${SS_VALVE_SRC}/game/shared/SoundEmitterSystem.cpp")
if(NOT SS_VALVE_SDK OR NOT EXISTS "${soundemitter_stock}")
  ss_oracle_missing(soundemitter_client_oracle_test "no ${soundemitter_stock}: is the pristine clone at valve-sdk.lock?")
else()
# The subject and the two game/shared headers it was written against are
# COPIED, byte for byte, into their own directory before they are included:
# their quoted #includes would otherwise find game/shared's real headers
# beside them ahead of the stand-ins.
configure_file("${soundemitter_stock}" "${CMAKE_CURRENT_BINARY_DIR}/soundemitter_subject/SoundEmitterSystem.cpp" COPYONLY)
configure_file("${SS_VALVE_SRC}/game/shared/shareddefs.h" "${CMAKE_CURRENT_BINARY_DIR}/soundemitter_subject/shareddefs.h" COPYONLY)
configure_file("${SS_VALVE_SRC}/game/shared/igamesystem.h" "${CMAKE_CURRENT_BINARY_DIR}/soundemitter_subject/igamesystem.h" COPYONLY)
# (At the start of the line: tools/leg-floor counts ss_cpptest*() calls there.)
ss_cpptest_tier1(soundemitter_client_oracle_test
    DEFINES POSIX LINUX _LINUX GNUC PLATFORM_64BITS COMPILER_GCC CLIENT_DLL NO_MALLOC_OVERRIDE
    OPTIONS -Wno-unknown-pragmas -Wno-unused-variable -Wno-unused-but-set-variable -Wno-unused-function
            -Wno-unused-private-field -Wno-dangling-else
    INCLUDES ${CMAKE_CURRENT_SOURCE_DIR}/soundemitter_stub
             ${CMAKE_CURRENT_BINARY_DIR}/soundemitter_subject
             public public/tier0 public/tier1 sourcesharp/test ${PROJECT_SOURCE_DIR}/src/test/native
    SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/soundemitter_client_oracle_test.cpp)
endif()
