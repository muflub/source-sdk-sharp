# The allocation checker's link hook (plan_server_novalve.md §3.6).
#
# ss_alloc_check_wraps is an INTERFACE target: linking it puts -Wl,--wrap= on
# every allocation and release entry point, so each reference the linked code
# makes to malloc/free/operator new/delete resolves to the __wrap_ function in
# src/server/native/alloc_check.cpp, which must be linked alongside.
#
# libstdc++ stays a shared library. Its out-of-line template code
# (std::string, container growth) allocates AND frees inside libstdc++.so, where
# no reference is wrapped, so those blocks are invisible rather than foreign.
# A static libstdc++ would bring the runtime's own mutable state into server.so,
# which the globals ledger (rightly) counts as state this port added.
#
# SS_ALLOC_CHECK decides whether server.so carries it: on by default in debug,
# never in release.

set(SS_ALLOC_CHECK_SYMBOLS
    malloc calloc realloc free posix_memalign aligned_alloc memalign strdup strndup
    _Znwm _Znam _ZnwmRKSt9nothrow_t _ZnamRKSt9nothrow_t
    _ZnwmSt11align_val_t _ZnamSt11align_val_t
    _ZnwmSt11align_val_tRKSt9nothrow_t _ZnamSt11align_val_tRKSt9nothrow_t
    _ZdlPv _ZdaPv _ZdlPvm _ZdaPvm
    _ZdlPvSt11align_val_t _ZdaPvSt11align_val_t
    _ZdlPvmSt11align_val_t _ZdaPvmSt11align_val_t
    _ZdlPvRKSt9nothrow_t _ZdaPvRKSt9nothrow_t)

if(NOT TARGET ss_alloc_check_wraps)
  add_library(ss_alloc_check_wraps INTERFACE)
  set(_wraps "")
  foreach(s IN LISTS SS_ALLOC_CHECK_SYMBOLS)
    list(APPEND _wraps "-Wl,--wrap=${s}")
  endforeach()
  target_link_options(ss_alloc_check_wraps INTERFACE ${_wraps})
endif()

# Linux only: -Wl,--wrap is GNU ld's (and lld's), and the symbols below are
# glibc's and the Itanium ABI's; link.exe and Apple's ld have no such hook.
if(CMAKE_BUILD_TYPE STREQUAL "Debug" AND SS_PLATFORM STREQUAL linux)
  set(_ss_alloc_check_default ON)
else()
  set(_ss_alloc_check_default OFF)
endif()
option(SS_ALLOC_CHECK "Link the allocation checker into server.so" ${_ss_alloc_check_default})
