//========= SourceSharp — the allocation checker =============================//
// plan_server_novalve.md §3.6. A debug-build link hook (-Wl,--wrap= on the
// malloc and operator new/delete families, cmake/AllocCheck.cmake) that keeps
// a side table of every block server.so allocates and checks each release:
//
//   mismatch      new/free, malloc/delete, new[]/delete, aligned/unaligned,
//                 or a sized delete whose size is not the allocated one
//   foreign free  server.so frees a block it did not allocate: engine or tier0
//                 memory, i.e. ownership crossed the engine/mod boundary
//   live          blocks still in the table when the report is taken
//
// SOURCESHARP_ALLOC_CHECK=report|abort|off picks what a mismatch or a foreign
// free does (default report); SOURCESHARP_ALLOC_CHECK_LOG names a file the
// report is appended to as well as stderr.
//
// The wraps add nothing to a block, so a block the engine frees still frees:
// the checker observes and never changes behaviour.
//=============================================================================//

#pragma once

#include <stddef.h>
#include <stdint.h>

struct SourceSharpAllocCheckCounts
{
	uint64_t allocations;
	uint64_t releases;
	uint64_t mismatches;
	uint64_t foreignFrees;
	uint64_t live;
};

extern "C" SourceSharpAllocCheckCounts SourceSharp_AllocCheckCounts();

// Write the summary (counts, foreign-free sites, the largest live sites) to
// stderr and the log file, tagged with `when`.
extern "C" void SourceSharp_AllocCheckReport( const char *when );

// Tests only: forget every record and zero the counters.
extern "C" void SourceSharp_AllocCheckResetForTest();
